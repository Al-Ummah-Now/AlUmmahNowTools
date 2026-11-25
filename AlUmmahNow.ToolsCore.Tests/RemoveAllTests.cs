using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>Remove all does for every file what Remove does for one (issue 85).</summary>
public class RemoveAllTests
{
    private static QueueItem Item(string name, int at = 0) => QueueItem.For(
        new ScannedVideo(Path.Combine(Path.GetTempPath(), name), 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = name }, Array.Empty<ScanNote>()),
        "b1", "ch1", DateTimeOffset.UtcNow.AddSeconds(at));

    private static StepState S(QueueItem item, ImportStepKind kind) => item.Steps.Single(s => s.Kind == kind);

    private static async Task Until(ImportQueue queue, Func<bool> done) => await Temp.WaitUntil(() =>
    {
        queue.Pump();
        return done();
    });

    [Fact]
    public async Task Every_file_leaves_whatever_its_state_running_steps_stop_and_nothing_starts()
    {
        var path = Path.Combine(Temp.Folder(), "queue.json");
        var upload = new ScriptedStep(ImportStepKind.Upload, async (item, context, ct) =>
        {
            item.UploadUrl = "https://videos.example/tus/" + item.Id;
            item.UploadOffset = 4096;
            context.Save();
            await Task.Delay(Timeout.Infinite, ct);
            return StepResult.Done;
        });
        var hash = new ScriptedStep(ImportStepKind.Hash, (item, _, _) => item.Video.FileName switch
        {
            "failed.mp4" => throw new FileNotFoundException("gone"),
            "backing-off.mp4" => throw new ImportApiException(503, "internal_error", "down"),
            "later.mp4" => Task.FromResult(StepResult.Later),
            "done.mp4" => Task.FromResult(StepResult.SkipRest),
            _ => Task.FromResult(StepResult.Done),
        });
        var sink = new RecordingSink();
        using var queue = new ImportQueue(path, ImportQueueTests.All(upload, hash), sink, backoff: _ => TimeSpan.FromMinutes(5));
        queue.SetParallel(ImportStepKind.Upload, 2);
        var names = new[] { "up1.mp4", "up2.mp4", "waiting.mp4", "failed.mp4", "backing-off.mp4", "later.mp4", "done.mp4", "paused.mp4" };
        var items = names.Select(Item).ToList();
        queue.Add(items);
        queue.Pause(items[7].Id);
        await Until(queue, () => upload.Running == 2 && items[2].CurrentStep!.Kind == ImportStepKind.Upload);
        await Temp.WaitUntil(() => items[3].Phase == ItemPhase.Failed && items[4].Phase == ItemPhase.RetryWaiting && items[5].Phase == ItemPhase.ServerWorking && items[6].Phase == ItemPhase.Done);
        Assert.Equal(2, queue.Items.Count(i => i.UploadUnfinished));
        var runs = upload.Runs;

        Assert.Equal(8, queue.RemoveAll());
        Assert.Empty(queue.Items);
        Assert.False(queue.AllPaused);
        Assert.Equal(0, queue.Pump());
        await Temp.WaitUntil(() => upload.Running == 0);
        // Pressed twice
        Assert.Equal(0, queue.RemoveAll());
        Assert.Equal(0, queue.Pump());
        Assert.Equal(runs, upload.Runs);
        // A removed file's stop is no failure
        Assert.Single(sink.Failed);

        // And nothing comes back after a restart
        using var second = new ImportQueue(path, ImportQueueTests.All());
        Assert.Empty(second.Items);
        Assert.Equal(0, second.Pump());
    }

    [Fact]
    public async Task Files_added_afterwards_run_also_when_all_was_paused()
    {
        using var queue = new ImportQueue(Path.Combine(Temp.Folder(), "queue.json"), ImportQueueTests.All());
        queue.Add(new[] { Item("a.mp4"), Item("b.mp4", 1) });
        queue.PauseAll();
        queue.RemoveAll();
        queue.Add(new[] { Item("c.mp4") });
        await Until(queue, () => queue.Items.Single().Phase == ItemPhase.Done);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task What_was_made_here_and_not_sent_goes_and_the_persons_files_stay(bool all)
    {
        var dir = Temp.Folder();
        var video = Temp.Bytes(dir, "mine.mp4", 100);
        var renditions = Path.Combine(dir, "renditions", "x");
        Temp.Write(renditions, Path.Combine("360p", "index.m3u8"), "#EXTM3U");
        var transcript = Temp.Write(dir, "x.json", "{}");
        using var queue = new ImportQueue(Path.Combine(dir, "queue.json"), ImportQueueTests.All());
        var item = Item("mine.mp4");
        item.Video = item.Video with { Path = video };
        item.Renditions = new RenditionSet { Directory = renditions };
        item.Transcript = new TranscriptState { Path = transcript };
        queue.Add(new[] { item });

        if (all) queue.RemoveAll();
        else queue.Remove(item.Id);
        await Temp.WaitUntil(() => !Directory.Exists(renditions) && !File.Exists(transcript));
        Assert.True(File.Exists(video));
    }

    [Fact]
    public async Task A_running_step_stops_before_its_files_go()
    {
        var dir = Temp.Folder();
        var renditions = Path.Combine(dir, "renditions");
        Temp.Write(renditions, "index.m3u8", "#EXTM3U");
        var stopping = new TaskCompletionSource();
        var upload = new ScriptedStep(ImportStepKind.Upload, async (item, _, ct) =>
        {
            item.Renditions = new RenditionSet { Directory = renditions };
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { await stopping.Task; }
            return StepResult.Done;
        });
        using var queue = new ImportQueue(Path.Combine(dir, "queue.json"), ImportQueueTests.All(upload));
        queue.Add(new[] { Item("a.mp4") });
        await Until(queue, () => upload.Running == 1);
        queue.RemoveAll();
        Assert.Empty(queue.Items);
        await Task.Delay(50);
        Assert.True(Directory.Exists(renditions));
        stopping.SetResult();
        await Temp.WaitUntil(() => !Directory.Exists(renditions));
    }

    [Fact]
    public void Uploads_count_as_unfinished_from_their_start_until_they_are_done()
    {
        var item = Item("a.mp4");
        Assert.False(item.UploadUnfinished);
        S(item, ImportStepKind.Upload).Status = StepStatus.Running;
        Assert.True(item.UploadUnfinished);
        S(item, ImportStepKind.Upload).Status = StepStatus.Pending;
        item.UploadUrl = "https://videos.example/tus/1";
        item.Paused = true;
        Assert.True(item.UploadUnfinished);
        S(item, ImportStepKind.Upload).Status = StepStatus.Done;
        Assert.False(item.UploadUnfinished);
    }
}

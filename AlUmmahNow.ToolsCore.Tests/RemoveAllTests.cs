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
    public void Only_the_files_it_was_asked_about_leave()
    {
        using var queue = new ImportQueue(Path.Combine(Temp.Folder(), "queue.json"), ImportQueueTests.All());
        queue.Add(new[] { Item("a.mp4"), Item("b.mp4", 1) });
        var asked = queue.Items.Select(i => i.Id).ToList();
        // While the question is open: one is added, one leaves
        var late = Item("c.mp4", 2);
        queue.Add(new[] { late });
        queue.Remove(asked[0]);
        Assert.Equal(1, queue.RemoveAll(asked));
        Assert.Equal(late.Id, Assert.Single(queue.Items).Id);
        Assert.Equal(0, queue.RemoveAll(asked));
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
        var item = Item("mine.mp4");
        var work = Work(dir);
        var renditions = Path.Combine(work.Renditions, item.Id);
        Temp.Write(renditions, Path.Combine("360p", "index.m3u8"), "#EXTM3U");
        var transcript = Temp.Write(work.Transcripts, item.Id + ".json", "{}");
        using var queue = new ImportQueue(Path.Combine(dir, "queue.json"), ImportQueueTests.All(), work: work);
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
        var work = Work(dir);
        var added = Item("a.mp4");
        var renditions = Path.Combine(work.Renditions, added.Id);
        Temp.Write(renditions, "index.m3u8", "#EXTM3U");
        var stopping = new TaskCompletionSource();
        var upload = new ScriptedStep(ImportStepKind.Upload, async (item, _, ct) =>
        {
            item.Renditions = new RenditionSet { Directory = renditions };
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { await stopping.Task; }
            return StepResult.Done;
        });
        using var queue = new ImportQueue(Path.Combine(dir, "queue.json"), ImportQueueTests.All(upload), work: work);
        queue.Add(new[] { added });
        await Until(queue, () => upload.Running == 1);
        queue.RemoveAll();
        Assert.Empty(queue.Items);
        await Task.Delay(50);
        Assert.True(Directory.Exists(renditions));
        stopping.SetResult();
        await Temp.WaitUntil(() => !Directory.Exists(renditions));
    }

    /// <summary>A directory link: a junction on Windows (no rights needed), a symbolic link elsewhere.</summary>
    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { RedirectStandardOutput = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    private static WorkFolders Work(string dir) => new(Path.Combine(dir, "app", "renditions"), Path.Combine(dir, "app", "transcripts"));

    /// <summary>Paths a damaged or edited queue file may hold: nothing but the app's own work for that very file is deleted.</summary>
    [Theory]
    [InlineData("videos")]
    [InlineData("parent")]
    [InlineData("dots")]
    [InlineData("root")]
    [InlineData("app")]
    [InlineData("relative")]
    [InlineData("other-name")]
    [InlineData("inside")]
    [InlineData("another-files")]
    [InlineData("link")]
    [InlineData("linked-root")]
    [InlineData("linked-app")]
    [InlineData("id")]
    public async Task A_hostile_path_in_the_saved_queue_deletes_nothing(string attack)
    {
        var dir = Temp.Folder();
        var videos = Path.Combine(dir, "videos");
        var video = Temp.Bytes(Temp.Folder(videos), "mine.mp4", 100);
        var sidecar = Temp.Write(videos, "mine.ar.srt", "1");
        var work = Work(dir);
        var item = Item("mine.mp4");
        item.Video = item.Video with { Path = video };
        var name = item.Id;
        var bystander = Temp.Write(Path.Combine(work.Renditions, "0123456789abcdef0123456789abcdef"), "index.m3u8", "x");
        Temp.Write(work.Transcripts, "0123456789abcdef0123456789abcdef.json", "{}");
        var kept = new List<string> { video, sidecar, bystander };
        string folder, file;
        switch (attack)
        {
            case "videos": (folder, file) = (videos, video); break;
            case "parent": (folder, file) = (dir, sidecar); break;
            case "dots":
                (folder, file) = (Path.Combine(work.Renditions, "..", "..", "videos"), Path.Combine(work.Transcripts, "..", "..", "videos", "mine.mp4"));
                break;
            case "root": (folder, file) = (work.Renditions, work.Transcripts); break;
            case "app": (folder, file) = (Path.Combine(dir, "app"), Path.Combine(dir, "app")); break;
            case "relative": (folder, file) = (name, name + ".json"); break;
            case "other-name":
                folder = Path.GetDirectoryName(kept[^1])!;
                file = Temp.Write(work.Transcripts, "notes.json", "{}");
                kept.Add(file);
                break;
            case "inside":
                kept.Add(Temp.Write(Path.Combine(work.Renditions, name, "360p"), "index.m3u8", "x"));
                folder = Path.Combine(work.Renditions, name, "360p");
                file = kept[^1];
                break;
            case "another-files":
                folder = Path.GetDirectoryName(bystander)!;
                file = Path.Combine(work.Transcripts, "0123456789abcdef0123456789abcdef.json");
                kept.Add(file);
                break;
            case "link":
                // root/id is a link to the person's folder
                Link(Path.Combine(work.Renditions, name), videos);
                (folder, file) = (Path.Combine(work.Renditions, name), video);
                break;
            case "linked-root":
                // The renditions folder itself leads somewhere else
                Directory.Delete(work.Renditions, true);
                kept.Remove(bystander);
                kept.Add(Temp.Write(Path.Combine(videos, name), "mine2.mp4", "x"));
                Link(work.Renditions, videos);
                (folder, file) = (Path.Combine(work.Renditions, name), video);
                break;
            case "linked-app":
                Directory.Delete(Path.Combine(dir, "app"), true);
                kept.Remove(bystander);
                kept.Add(Temp.Write(Path.Combine(videos, "renditions", name), "mine3.mp4", "x"));
                kept.Add(Temp.Write(Path.Combine(videos, "transcripts"), name + ".json", "x"));
                Link(Path.Combine(dir, "app"), videos);
                (folder, file) = (Path.Combine(work.Renditions, name), Path.Combine(work.Transcripts, name + ".json"));
                break;
            default:
                // The id leads out of the folder
                item.Id = Path.Combine("..", "..", "videos");
                (folder, file) = (Path.Combine(work.Renditions, item.Id), Path.Combine(work.Transcripts, item.Id));
                break;
        }
        item.Renditions = new RenditionSet { Directory = folder };
        item.Transcript = new TranscriptState { Path = file };
        using (var queue = new ImportQueue(Path.Combine(dir, "queue.json"), ImportQueueTests.All(), work: work))
        {
            queue.Add(new[] { item });
            Assert.Equal(1, queue.RemoveAll());
            await queue.StopAsync();
        }
        Assert.All(kept, k => Assert.True(File.Exists(k), k));
        Assert.True(Directory.Exists(videos));

        // And the encode's own clean-up takes no such path either
        Assert.False(WorkFiles.DeleteFolder(folder, work.Renditions, item.Id));
        Assert.All(kept, k => Assert.True(File.Exists(k), k));
    }

    [Fact]
    public async Task A_link_inside_the_apps_work_goes_without_what_it_leads_to()
    {
        var dir = Temp.Folder();
        var videos = Path.Combine(dir, "videos");
        var video = Temp.Bytes(Temp.Folder(videos), "mine.mp4", 100);
        var work = Work(dir);
        var item = Item("mine.mp4");
        var renditions = Path.Combine(work.Renditions, item.Id);
        Temp.Write(renditions, "index.m3u8", "x");
        Link(Path.Combine(renditions, "360p"), videos);
        item.Renditions = new RenditionSet { Directory = renditions };
        using var queue = new ImportQueue(Path.Combine(dir, "queue.json"), ImportQueueTests.All(), work: work);
        queue.Add(new[] { item });
        queue.RemoveAll();
        await queue.StopAsync();
        Assert.False(Directory.Exists(renditions));
        Assert.True(File.Exists(video));
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

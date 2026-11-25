using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>The queue runs for the account that is signed in, and for nobody otherwise.</summary>
public class QueueSessionTests
{
    private static QueueItem Item(string name) => QueueItem.For(
        new ScannedVideo(Path.Combine(Path.GetTempPath(), name), 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = name }, Array.Empty<ScanNote>()),
        "b1", "ch1", DateTimeOffset.UtcNow);

    private static ScriptedStep Upload() => new(ImportStepKind.Upload, async (item, context, ct) =>
    {
        item.UploadUrl = "https://videos.example/tus/" + item.Id;
        item.UploadOffset = 4096;
        context.Save();
        await Task.Delay(Timeout.Infinite, ct);
        return StepResult.Done;
    });

    [Theory]
    [InlineData(SessionState.Staff, "a", QueueSession.Run)]
    [InlineData(SessionState.Staff, null, QueueSession.AsItIs)]
    [InlineData(SessionState.SignedOut, null, QueueSession.Stop)]
    [InlineData(SessionState.SigningIn, null, QueueSession.Stop)]
    [InlineData(SessionState.NotStaff, "a", QueueSession.Stop)]
    [InlineData(SessionState.Checking, "a", QueueSession.AsItIs)]
    [InlineData(SessionState.Unreachable, "a", QueueSession.AsItIs)]
    [InlineData(SessionState.Starting, null, QueueSession.AsItIs)]
    public void Who_is_signed_in_decides(SessionState state, string? account, QueueSession expected) =>
        Assert.Equal(expected, QueueSessions.For(state, account));

    [Fact]
    public async Task Signed_out_nothing_runs_and_the_same_account_goes_on_also_after_a_restart()
    {
        var path = Path.Combine(Temp.Folder(), "queue.json");
        var upload = Upload();
        string id;
        using (var queue = new ImportQueue(path, ImportQueueTests.All(upload)))
        {
            queue.Add([Item("a.mp4")]);
            Assert.False(queue.IsStarted);
            queue.StartFor("one");
            Assert.True(queue.IsStarted);
            await Temp.WaitUntil(() => upload.Running == 1);
            await queue.StopAsync();
            Assert.False(queue.IsStarted);
            Assert.Equal(0, upload.Running);
            var item = Assert.Single(queue.Items);
            Assert.False(item.Paused);
            Assert.Equal(4096, item.UploadOffset);
            id = item.Id;
            await Task.Delay(1500);
            Assert.Equal(1, upload.Runs);
            // Again, and once more while it runs
            queue.StartFor("one");
            queue.StartFor("one");
            await Temp.WaitUntil(() => upload.Runs == 2 && upload.Running == 1);
            await queue.StopAsync();
        }
        var later = Upload();
        using var second = new ImportQueue(path, ImportQueueTests.All(later));
        Assert.Equal(id, Assert.Single(second.Items).Id);
        second.StartFor("one");
        await Temp.WaitUntil(() => later.Running == 1);
        await second.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task For_another_account_it_is_paused_until_someone_says_so(bool restarted)
    {
        var path = Path.Combine(Temp.Folder(), "queue.json");
        var upload = Upload();
        var queue = new ImportQueue(path, ImportQueueTests.All(upload));
        try
        {
            queue.Add([Item("a.mp4")]);
            queue.StartFor("one");
            await Temp.WaitUntil(() => upload.Running == 1);
            await queue.StopAsync();
            if (restarted)
            {
                queue.Dispose();
                queue = new ImportQueue(path, ImportQueueTests.All(upload));
            }
            queue.StartFor("two");
            await Task.Delay(1500);
            Assert.Equal(1, upload.Runs);
            Assert.True(queue.AllPaused);
            Assert.True(Assert.Single(queue.Items).Paused);
            // And it is theirs from here on
            queue.ResumeAll();
            await Temp.WaitUntil(() => upload.Running == 1);
            await queue.StopAsync();
            queue.StartFor("two");
            await Temp.WaitUntil(() => upload.Running == 1);
            await queue.StopAsync();
        }
        finally
        {
            queue.Dispose();
        }
    }

    [Fact]
    public async Task A_queue_saved_before_this_goes_on_for_whoever_signs_in()
    {
        var path = Path.Combine(Temp.Folder(), "queue.json");
        using (var before = new ImportQueue(path, ImportQueueTests.All())) before.Add([Item("a.mp4")]);
        Assert.Contains("a.mp4", File.ReadAllText(path));
        var upload = Upload();
        using var queue = new ImportQueue(path, ImportQueueTests.All(upload));
        queue.StartFor("one");
        await Temp.WaitUntil(() => upload.Running == 1);
        await queue.StopAsync();
    }
}

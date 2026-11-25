using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>Stopping the queue never waits, under its lock, for something that needs the lock.</summary>
public class QueueStopTests
{
    private static QueueItem Item() => QueueItem.For(
        new ScannedVideo(Path.Combine(Path.GetTempPath(), "a.mp4"), 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = "a" }, Array.Empty<ScanNote>()),
        "b1", "ch1", DateTimeOffset.UtcNow);

    private static Task<T> Soon<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(30));

    /// <summary>A step whose cancellation writes to the log (the queue's lock) while the step itself is already ending.</summary>
    private static ScriptedStep EndsDuringItsCancellation() => new(ImportStepKind.Hash, async (_, context, ct) =>
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.UnsafeRegister(_ => { cancelled.SetResult(); Thread.Sleep(500); context.Log(LogLevel.Info, "x", "x"); }, null);
        await cancelled.Task;
        // Not waiting for the callback, as a step that just ends
        registration.Unregister();
        ct.ThrowIfCancellationRequested();
        return StepResult.Done;
    });

    [Theory]
    [InlineData("stop")]
    [InlineData("pause")]
    [InlineData("remove")]
    public async Task A_cancellation_that_needs_the_lock_while_its_step_ends_does_not_hang(string way)
    {
        var step = EndsDuringItsCancellation();
        var queue = new ImportQueue(Path.Combine(Temp.Folder(), "queue.json"), ImportQueueTests.All(step));
        queue.Add(new[] { Item() });
        queue.Start();
        await Temp.WaitUntil(() => step.Running == 1, 20_000);
        await Soon(Task.Run(async () =>
        {
            switch (way)
            {
                case "pause": queue.PauseAll(); break;
                case "remove": queue.RemoveAll(); break;
            }
            await queue.StopAsync();
            return true;
        }));
        Assert.Equal(0, step.Running);
        // Nothing holds the lock
        Assert.Equal(way == "remove" ? 0 : 1, (await Soon(Task.Run(() => queue.Items))).Count);
        queue.Dispose();
    }

    [Fact]
    public async Task A_stopped_queue_starts_again()
    {
        var step = new ScriptedStep(ImportStepKind.Hash, async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return StepResult.Done; });
        using var queue = new ImportQueue(Path.Combine(Temp.Folder(), "queue.json"), ImportQueueTests.All(step));
        queue.Add(new[] { Item() });
        for (var i = 0; i < 3; i++)
        {
            queue.Start();
            await Temp.WaitUntil(() => step.Running == 1, 20_000);
            await queue.StopAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, step.Running);
            Assert.Equal(0, queue.Items[0].Steps[0].Attempts);
        }
        Assert.Equal(3, step.Runs);
    }
}

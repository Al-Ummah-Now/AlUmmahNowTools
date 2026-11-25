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

    /// <summary>
    /// A step whose cancellation writes to the log (the queue's lock) while the step itself is already ending: its callback is
    /// not waited for, so the queue finishes the step with the callback still running.
    /// </summary>
    private static ScriptedStep EndsDuringItsCancellation(int milliseconds) => new(ImportStepKind.Hash, async (_, context, ct) =>
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Never disposed: that would wait for the callback
        ct.UnsafeRegister(_ => { cancelled.SetResult(); Thread.Sleep(milliseconds); context.Log(LogLevel.Info, "x", "x"); }, null);
        await cancelled.Task;
        ct.ThrowIfCancellationRequested();
        return StepResult.Done;
    });

    /// <summary>
    /// Many times, with many steps at once: with sources linked to the loop's it hung only when the loop's cancellation came to a
    /// step before the step's own did.
    /// </summary>
    [Theory]
    [InlineData("stop", 150)]
    [InlineData("pause", 5)]
    [InlineData("remove", 5)]
    public async Task A_cancellation_that_needs_the_lock_while_its_step_ends_does_not_hang(string way, int times)
    {
        const int files = 8;
        for (var n = 0; n < times; n++)
        {
            var step = EndsDuringItsCancellation(5);
            var queue = new ImportQueue(Path.Combine(Temp.Folder(), "queue.json"), ImportQueueTests.All(step));
            queue.SetParallel(ImportStepKind.Hash, files);
            queue.Add(Enumerable.Range(0, files).Select(_ => Item()));
            queue.Start();
            await Temp.WaitUntil(() => step.Running == files, 20_000);
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
            Assert.Equal(way == "remove" ? 0 : files, (await Soon(Task.Run(() => queue.Items))).Count);
            queue.Dispose();
        }
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

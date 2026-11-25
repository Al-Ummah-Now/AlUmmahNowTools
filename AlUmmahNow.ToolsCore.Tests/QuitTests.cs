using System.Diagnostics;
using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>Closing the one-window app quits it, asking first when imports run (Tools #78).</summary>
public class QuitGuardTests
{
    private static QueueItem Item(Action<QueueItem>? shape = null)
    {
        var item = QueueItem.For(new ScannedVideo("/x/a.mp4", 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata(), Array.Empty<ScanNote>()), "b1", "ch1", DateTimeOffset.UtcNow);
        shape?.Invoke(item);
        return item;
    }

    private static void Step(QueueItem item, StepStatus status, Action<StepState>? more = null)
    {
        var first = item.Steps[0];
        first.Status = status;
        more?.Invoke(first);
    }

    [Fact]
    public void Running_queued_retrying_and_server_working_files_count_as_still_running()
    {
        var running = Item(i => Step(i, StepStatus.Running));
        var queued = Item();
        var retrying = Item(i => Step(i, StepStatus.Pending, s => s.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(10)));
        var server = Item(i => Step(i, StepStatus.Pending, s => s.WaitingOnServer = true));
        Assert.Equal(ItemPhase.Running, running.Phase);
        Assert.Equal(ItemPhase.Waiting, queued.Phase);
        Assert.Equal(ItemPhase.RetryWaiting, retrying.Phase);
        Assert.Equal(ItemPhase.ServerWorking, server.Phase);
        Assert.Equal(4, QuitGuard.ActiveFiles(new[] { running, queued, retrying, server }));
    }

    [Fact]
    public void Paused_failed_and_finished_files_do_not_count()
    {
        var paused = Item(i => i.Paused = true);
        var failed = Item(i => Step(i, StepStatus.Failed));
        var done = Item(i => { foreach (var s in i.Steps) s.Status = StepStatus.Done; });
        Assert.Equal(ItemPhase.Paused, paused.Phase);
        Assert.Equal(ItemPhase.Failed, failed.Phase);
        Assert.Equal(ItemPhase.Done, done.Phase);
        Assert.Equal(0, QuitGuard.ActiveFiles(new[] { paused, failed, done }));
        Assert.Equal(0, QuitGuard.ActiveFiles(Array.Empty<QueueItem>()));
    }

    [Theory]
    [InlineData(0, CloseDecision.QuitNow)]
    [InlineData(1, CloseDecision.AskFirst)]
    [InlineData(250, CloseDecision.AskFirst)]
    public void The_question_is_asked_only_when_something_runs(int active, CloseDecision expected) =>
        Assert.Equal(expected, QuitGuard.OnCloseRequested(active));
}

public class QuitCoordinatorTests
{
    private sealed class Rig
    {
        public readonly List<string> Calls = new();
        public int Active;
        public Func<int, Task<bool>> Confirm = _ => Task.FromResult(true);
        public Func<CancellationToken, Task> Stop = _ => Task.CompletedTask;
        public readonly List<string> Logs = new();
        public int Questions;

        public QuitCoordinator Make(TimeSpan? budget = null) => new(
            () => Active,
            async n => { Questions++; Calls.Add("ask " + n); return await Confirm(n); },
            async ct => { Calls.Add("stop"); await Stop(ct); },
            () => Calls.Add("exit"),
            budget,
            Logs.Add,
            () => { Calls.Add("hand over"); HandOver(); });

        public Action HandOver = () => { };
    }

    [Fact]
    public async Task An_update_is_handed_over_after_the_work_has_stopped_and_before_the_exit()
    {
        var release = new TaskCompletionSource();
        var rig = new Rig { Stop = async _ => await release.Task };
        var quit = rig.Make();
        var first = quit.RequestQuitAsync();
        await Task.Delay(100);
        Assert.Equal(new[] { "stop" }, rig.Calls);
        release.SetResult();
        Assert.True(await first);
        Assert.Equal(new[] { "stop", "hand over", "exit" }, rig.Calls);
    }

    [Fact]
    public async Task A_window_that_goes_under_the_question_stops_the_work_hands_over_and_exits_once_whatever_is_answered()
    {
        foreach (var answered in new[] { false, true })
        {
            var rig = new Rig { Active = 1 };
            var answer = new TaskCompletionSource<bool>();
            rig.Confirm = _ => answer.Task;
            var quit = rig.Make();
            var asked = quit.RequestQuitAsync();
            Assert.True(quit.IsAsking);
            Assert.True(await quit.QuitNowAsync());
            answer.SetResult(answered);
            Assert.True(await asked);
            Assert.Equal(new[] { "ask 1", "stop", "hand over", "exit" }, rig.Calls);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_process_ending_by_itself_stops_the_work_first_and_calls_no_exit(bool handOver)
    {
        var release = new TaskCompletionSource();
        var rig = new Rig { Active = 2, Stop = async _ => await release.Task };
        var quit = rig.Make();
        var end = quit.EndAsync(handOver);
        Assert.False(end.Wait(100));
        release.SetResult();
        await end.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(handOver ? new[] { "stop", "hand over" } : new[] { "stop" }, rig.Calls);
        // Nothing of it again, whoever asks
        await quit.EndAsync(handOver: true);
        Assert.True(await quit.RequestQuitAsync());
        Assert.Equal(handOver ? 2 : 1, rig.Calls.Count);
    }

    [Fact]
    public async Task The_process_ending_during_a_quit_joins_it_and_its_hand_over()
    {
        var release = new TaskCompletionSource();
        using var over = new ManualResetEventSlim();
        var rig = new Rig { Stop = async _ => await release.Task };
        rig.HandOver = () => over.Wait(TimeSpan.FromSeconds(10));
        var quit = rig.Make();
        var first = quit.RequestQuitAsync();
        var end = quit.EndAsync(handOver: false);
        release.SetResult();
        Assert.False(end.Wait(200));
        over.Set();
        await end.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await first);
        Assert.Equal(new[] { "stop", "hand over", "exit" }, rig.Calls);
    }

    [Fact]
    public async Task A_session_that_ends_during_a_quit_hands_nothing_over()
    {
        var release = new TaskCompletionSource();
        var rig = new Rig { Stop = async _ => await release.Task };
        var quit = rig.Make();
        var first = quit.RequestQuitAsync();
        var end = quit.EndAsync(handOver: false, refuse: true);
        release.SetResult();
        await end.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await first);
        Assert.Equal(new[] { "stop", "exit" }, rig.Calls);
    }

    [Fact]
    public async Task Nothing_is_handed_over_when_the_work_did_not_end()
    {
        foreach (var stop in new Func<CancellationToken, Task>[] { _ => new TaskCompletionSource().Task, _ => throw new InvalidOperationException("something is still running") })
        {
            var rig = new Rig { Stop = stop };
            Assert.True(await rig.Make(TimeSpan.FromMilliseconds(150)).QuitNowAsync());
            Assert.Equal(new[] { "stop", "exit" }, rig.Calls);
        }
    }

    [Fact]
    public async Task A_hand_over_that_fails_does_not_keep_the_app()
    {
        var rig = new Rig { HandOver = () => throw new IOException("no updater") };
        Assert.True(await rig.Make().QuitNowAsync());
        Assert.Equal(new[] { "stop", "hand over", "exit" }, rig.Calls);
        Assert.Contains(rig.Logs, l => l.Contains("no updater", StringComparison.Ordinal));
    }

    [Fact]
    public void The_limits_of_the_parts_fit_in_the_total() =>
        // The scanner's word to it may block as long as its ending is waited for
        Assert.True(EndLimits.Queue + EndLimits.Reports + (2 * EndLimits.ScannerAsked) + EndLimits.ScannerKilled + EndLimits.Connection < EndLimits.Total);

    [Fact]
    public async Task With_nothing_running_it_stops_and_exits_without_asking()
    {
        var rig = new Rig();
        Assert.True(await rig.Make().RequestQuitAsync());
        Assert.Equal(new[] { "stop", "hand over", "exit" }, rig.Calls);
    }

    [Fact]
    public async Task With_imports_running_it_asks_then_stops_then_exits_in_that_order()
    {
        var rig = new Rig { Active = 3 };
        var quit = rig.Make();
        Assert.True(await quit.RequestQuitAsync());
        Assert.Equal(new[] { "ask 3", "stop", "hand over", "exit" }, rig.Calls);
        Assert.True(quit.IsQuitting);
    }

    [Fact]
    public async Task Keep_running_leaves_everything_as_it_is_and_the_next_close_asks_again()
    {
        var rig = new Rig { Active = 2, Confirm = _ => Task.FromResult(false) };
        var quit = rig.Make();
        Assert.False(await quit.RequestQuitAsync());
        Assert.Equal(new[] { "ask 2" }, rig.Calls);
        Assert.False(quit.IsQuitting);
        Assert.False(quit.IsAsking);

        rig.Confirm = _ => Task.FromResult(true);
        Assert.True(await quit.RequestQuitAsync());
        Assert.Equal(new[] { "ask 2", "ask 2", "stop", "hand over", "exit" }, rig.Calls);
    }

    [Fact]
    public async Task A_second_close_while_the_question_is_open_is_not_a_second_question()
    {
        var rig = new Rig { Active = 1 };
        var answer = new TaskCompletionSource<bool>();
        rig.Confirm = _ => answer.Task;
        var quit = rig.Make();
        var first = quit.RequestQuitAsync();
        Assert.True(quit.IsAsking);
        Assert.False(await quit.RequestQuitAsync());
        Assert.Equal(1, rig.Questions);
        answer.SetResult(true);
        Assert.True(await first);
        Assert.Equal(new[] { "ask 1", "stop", "hand over", "exit" }, rig.Calls);
    }

    [Fact]
    public async Task The_system_may_terminate_only_once_the_work_is_stopped_and_the_exit_is_called()
    {
        var release = new TaskCompletionSource();
        QuitCoordinator? quit = null;
        var exitingAtExit = false;
        quit = new QuitCoordinator(() => 0, _ => Task.FromResult(true), async ct => await release.Task, () => exitingAtExit = quit!.IsExiting);
        var first = quit.RequestQuitAsync();
        // Stopping: the quit is decided, the system's terminate (a second Cmd+Q) is still not allowed through
        Assert.True(quit.IsQuitting);
        Assert.False(quit.IsExiting);
        var again = quit.RequestQuitAsync(); // a second Cmd+Q: the same quit, never a second stop
        Assert.False(quit.IsExiting);
        release.SetResult();
        Assert.True(await first);
        Assert.True(await again);
        Assert.True(exitingAtExit);
        Assert.True(quit.IsExiting);
    }

    [Fact]
    public async Task The_stop_in_flight_can_be_joined_by_a_process_that_ends_meanwhile()
    {
        var release = new TaskCompletionSource();
        var quit = new QuitCoordinator(() => 0, _ => Task.FromResult(true), _ => release.Task, () => { });
        Assert.Null(quit.StopTask);
        var first = quit.RequestQuitAsync();
        await Temp.WaitUntil(() => quit.StopTask is not null);
        Assert.True(quit.IsQuitting);
        Assert.False(quit.StopTask!.IsCompleted);
        // Joining it waits for the stop that is running; it is not a second stop
        Assert.False(quit.StopTask!.Wait(TimeSpan.FromMilliseconds(100)));
        release.SetResult();
        Assert.True(quit.StopTask!.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(await first);
        Assert.True(quit.IsExiting);
    }

    [Fact]
    public async Task A_stop_that_blocks_its_thread_neither_blocks_the_caller_nor_hides_the_stop_from_a_joiner()
    {
        using var gate = new ManualResetEventSlim();
        var quit = new QuitCoordinator(() => 0, _ => Task.FromResult(true), _ => { gate.Wait(); return Task.CompletedTask; }, () => { });
        var watch = Stopwatch.StartNew();
        var first = quit.RequestQuitAsync(); // must come back at once although the stop blocks its thread
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        // The moment it is quitting, the stop is there to join (no gap in which a process exit would see "quitting" and nothing to wait for)
        Assert.True(quit.IsQuitting);
        Assert.NotNull(quit.StopTask);
        Assert.False(quit.StopTask!.Wait(TimeSpan.FromMilliseconds(100)));
        gate.Set();
        Assert.True(quit.StopTask!.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(await first);
    }

    [Fact]
    public async Task Asking_again_after_the_quit_started_stops_and_exits_once()
    {
        var rig = new Rig();
        var release = new TaskCompletionSource();
        rig.Stop = _ => release.Task;
        var quit = rig.Make();
        var first = quit.RequestQuitAsync();
        var again = quit.RequestQuitAsync();
        Assert.True(quit.IsQuitting);
        release.SetResult();
        Assert.True(await first);
        Assert.True(await again);
        Assert.Equal(new[] { "stop", "hand over", "exit" }, rig.Calls);
    }

    [Fact]
    public async Task A_question_that_cannot_be_shown_quits_because_the_queue_is_saved_either_way()
    {
        var rig = new Rig { Active = 1, Confirm = _ => throw new InvalidOperationException("no window") };
        Assert.True(await rig.Make().RequestQuitAsync());
        Assert.Equal(new[] { "ask 1", "stop", "hand over", "exit" }, rig.Calls);
        Assert.Contains(rig.Logs, l => l.Contains("could not be shown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stopping_that_fails_still_exits()
    {
        var rig = new Rig { Stop = _ => throw new IOException("disk") };
        Assert.True(await rig.Make().RequestQuitAsync());
        Assert.Equal(new[] { "stop", "exit" }, rig.Calls);
        Assert.Contains(rig.Logs, l => l.Contains("disk", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stopping_that_hangs_exits_when_the_budget_runs_out()
    {
        var rig = new Rig { Stop = _ => new TaskCompletionSource().Task };
        var watch = Stopwatch.StartNew();
        Assert.True(await rig.Make(TimeSpan.FromMilliseconds(150)).RequestQuitAsync());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "stop", "exit" }, rig.Calls);
        Assert.Contains(rig.Logs, l => l.Contains("took longer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task When_the_files_cannot_be_counted_it_asks_rather_than_stop_unasked()
    {
        var rig = new Rig();
        var quit = new QuitCoordinator(() => throw new InvalidOperationException("queue"), n => { rig.Calls.Add("ask " + n); return Task.FromResult(false); }, _ => Task.CompletedTask, () => rig.Calls.Add("exit"));
        Assert.False(await quit.RequestQuitAsync());
        Assert.Equal(new[] { "ask 1" }, rig.Calls);
    }
}

/// <summary>Quitting stops what runs, to the child processes, and saves the queue (Tools #78).</summary>
public class QuitStopsWorkTests
{
    private static string QueuePath() => Path.Combine(Temp.Folder(), "queue.json");

    private static QueueItem NewItem() =>
        QueueItem.For(new ScannedVideo("/x/a.mp4", 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata(), Array.Empty<ScanNote>()), "b1", "ch1", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Stopping_waits_until_the_running_step_has_stopped_and_saves_it_as_waiting()
    {
        var path = QueuePath();
        var started = new TaskCompletionSource();
        var cleaned = false;
        var hash = new ScriptedStep(ImportStepKind.Hash, async (_, _, ct) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally
            {
                // What a step does when it is cancelled: take its child process down. Slow on purpose.
                await Task.Delay(300);
                cleaned = true;
            }
            return StepResult.Done;
        });
        var queue = new ImportQueue(path, ImportQueueTests.All(hash));
        queue.Add(new[] { NewItem() });
        queue.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ItemPhase.Running, queue.Items[0].Phase);

        await queue.StopAsync();

        // Not "stopping soon": the step has finished stopping when StopAsync answers
        Assert.True(cleaned);
        var saved = new ImportQueue(path, ImportQueueTests.All());
        var restored = Assert.Single(saved.Items);
        Assert.Equal(StepStatus.Pending, restored.Steps.Single(s => s.Kind == ImportStepKind.Hash).Status);
        Assert.Equal(0, restored.Steps.Single(s => s.Kind == ImportStepKind.Hash).Attempts);
        Assert.Contains("Hash: stopped", saved.LogText(restored.Id), StringComparison.Ordinal);
        Assert.Equal(1, QuitGuard.ActiveFiles(saved.Items));
        saved.Dispose();
        queue.Dispose();
    }

    [Fact]
    public async Task A_step_that_ignores_stopping_does_not_hold_the_quit_forever_and_is_saved_to_run_again()
    {
        var path = QueuePath();
        var started = new TaskCompletionSource();
        var never = new TaskCompletionSource<StepResult>();
        var queue = new ImportQueue(path, ImportQueueTests.All(new ScriptedStep(ImportStepKind.Hash, (_, _, _) => { started.SetResult(); return never.Task; })));
        queue.Add(new[] { NewItem() });
        queue.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var watch = Stopwatch.StartNew();
        await queue.StopAsync(TimeSpan.FromMilliseconds(300));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));

        // Saved as running: the next start runs it again, as after a crash
        var restored = Assert.Single(new ImportQueue(path, ImportQueueTests.All()).Items);
        Assert.Equal(StepStatus.Pending, restored.Steps.Single(s => s.Kind == ImportStepKind.Hash).Status);
        never.SetResult(StepResult.Done);
    }

    [Fact]
    public async Task A_step_the_queue_starts_while_it_is_stopping_is_stopped_too_and_nothing_starts_after()
    {
        var path = QueuePath();
        var started = new TaskCompletionSource();
        var cancelled = 0;
        var hash = new ScriptedStep(ImportStepKind.Hash, async (_, _, ct) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); await Task.Delay(200); throw; }
            return StepResult.Done;
        });
        var queue = new ImportQueue(path, ImportQueueTests.All(hash));
        queue.Add(new[] { NewItem(), NewItem(), NewItem() });
        queue.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await queue.StopAsync();

        // Every step that ran was cancelled and has finished stopping; the loop starts none after the stop
        Assert.Equal(hash.Runs, cancelled);
        Assert.Equal(0, hash.Running);
        var runsAtStop = hash.Runs;
        Assert.Equal(0, queue.Pump());
        Assert.Equal(runsAtStop, hash.Runs);
        queue.Dispose();
    }

    /// <summary>An ffmpeg that never ends: cancelling the step takes the process down, and the answer comes after it is gone.</summary>
    [Fact]
    public async Task Cancelling_a_step_kills_its_ffmpeg_before_it_answers()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh")) return; // the Windows launch check covers children there
        var dir = Temp.Folder();
        var pidFile = Path.Combine(dir, "pid");
        var fake = Path.Combine(dir, "ffmpeg");
        File.WriteAllText(fake, $"#!/bin/sh\necho $$ > '{pidFile}'\nexec sleep 120\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var media = new FfmpegMediaTools(_ => Task.FromResult<(string, string)?>((fake, fake)));
        using var cts = new CancellationTokenSource();

        var run = media.ExtractAudioAsync("/x/a.mp4", Path.Combine(dir, "a.wav"), cts.Token);
        await Temp.WaitUntil(() => File.Exists(pidFile) && new FileInfo(pidFile).Length > 0);
        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        Assert.False(Process.GetProcessById(pid).HasExited);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await Temp.WaitUntil(() =>
        {
            try { return Process.GetProcessById(pid).HasExited; }
            catch (ArgumentException) { return true; }
        });
    }
}

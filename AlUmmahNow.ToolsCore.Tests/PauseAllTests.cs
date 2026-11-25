using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>Pause all, Resume all, and one file's Pause and Resume, over every step and every state a file can be in (issue 80).</summary>
public class PauseAllTests
{
    public enum State
    {
        Queued,
        Running,
        /// <summary>"Trying again in 10 s"</summary>
        BackingOff,
        /// <summary>The server is working; looked at again later (Check, Translate, the Quran index)</summary>
        ServerWorking,
        /// <summary>Looking at the server's work right now</summary>
        Checking,
        /// <summary>Gave up on a failure that trying again may fix</summary>
        FailedRetryable,
    }

    public static TheoryData<ImportStepKind, State> Matrix()
    {
        var data = new TheoryData<ImportStepKind, State>();
        foreach (var kind in Enum.GetValues<ImportStepKind>())
        {
            foreach (var state in Enum.GetValues<State>()) data.Add(kind, state);
        }
        return data;
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ScannedVideo Video(string name = "a.mp4") =>
        new(Path.Combine(Path.GetTempPath(), name), 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = name }, Array.Empty<ScanNote>());

    /// <summary>A queue whose one file is at <paramref name="kind"/> in <paramref name="state"/>; the step then does as told.</summary>
    private sealed class Rig : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(Temp.Folder(), "queue.json");
        public readonly Clock Clock = new();
        public readonly ScriptedStep Step;
        public readonly ImportQueue Queue;
        public readonly string Id;
        private readonly ImportStepKind _kind;
        /// <summary>What the step does when it runs</summary>
        public Func<IStepContext, CancellationToken, Task<StepResult>> Run = (_, _) => Task.FromResult(StepResult.Done);

        public Rig(ImportStepKind kind)
        {
            _kind = kind;
            Step = new ScriptedStep(kind, (_, context, ct) => Run(context, ct));
            Queue = Open(Step);
            var item = QueueItem.For(Video(), "b1", "ch1", Clock.Now);
            Id = item.Id;
            Queue.Add(new[] { item });
        }

        public ImportQueue Open(ScriptedStep step)
        {
            var queue = new ImportQueue(Path, ImportQueueTests.All(step), time: Clock, backoff: _ => TimeSpan.FromSeconds(10));
            queue.Settings.MaxAttempts = 3;
            return queue;
        }

        public QueueItem Item => Queue.Items.First(i => i.Id == Id);
        public StepState State => Item.Steps.Single(s => s.Kind == _kind);

        public async Task Until(Func<bool> done) => await Temp.WaitUntil(() =>
        {
            Queue.Pump();
            return done();
        });

        public async Task Reach(State state)
        {
            switch (state)
            {
                case PauseAllTests.State.Queued:
                    await Until(() => Item.CurrentStep!.Kind == _kind || Item.Steps.Where(s => s.Kind < _kind).All(s => s.Status == StepStatus.Done) || Step.Runs > 0, hold: true);
                    break;
                case PauseAllTests.State.Running:
                    Run = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return StepResult.Done; };
                    await Until(() => Step.Running == 1);
                    break;
                case PauseAllTests.State.BackingOff:
                    Run = (_, _) => throw new ImportApiException(503, "internal_error", "down");
                    await Until(() => Item.Phase == ItemPhase.RetryWaiting);
                    break;
                case PauseAllTests.State.ServerWorking:
                    Run = (context, _) => { context.Later(TimeSpan.FromSeconds(30)); return Task.FromResult(StepResult.Later); };
                    await Until(() => Item.Phase == ItemPhase.ServerWorking);
                    break;
                case PauseAllTests.State.Checking:
                    await Reach(PauseAllTests.State.ServerWorking);
                    Run = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return StepResult.Done; };
                    Clock.Now += TimeSpan.FromSeconds(31);
                    await Until(() => Step.Running == 1);
                    break;
                case PauseAllTests.State.FailedRetryable:
                    Run = (_, _) => throw new ImportApiException(503, "internal_error", "down");
                    await Until(() =>
                    {
                        Clock.Now += TimeSpan.FromSeconds(11);
                        return Item.Phase == ItemPhase.Failed;
                    });
                    break;
            }
        }

        /// <summary>Up to just before the step: the step's own slot is held by not pumping once the steps before it are done.</summary>
        private async Task Until(Func<bool> done, bool hold) => await Temp.WaitUntil(() =>
        {
            if (hold && Item.Steps.Where(s => s.Kind < _kind).All(s => s.Status == StepStatus.Done)) return true;
            Queue.Pump();
            return done();
        });

        public void Dispose() => Queue.Dispose();
    }

    private static ItemPhase PhaseOf(State state) => state switch
    {
        State.Queued => ItemPhase.Waiting,
        State.Running or State.Checking => ItemPhase.Running,
        State.BackingOff => ItemPhase.RetryWaiting,
        State.ServerWorking => ItemPhase.ServerWorking,
        _ => ItemPhase.Failed,
    };

    private static void AssertPaused(QueueItem item)
    {
        Assert.Equal(ItemPhase.Paused, item.Phase);
        Assert.Equal((false, true, false), (item.CanPause, item.CanResume, item.CanRetry));
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Pause_all_pauses_every_unfinished_file_and_resume_all_goes_on_from_its_step(ImportStepKind kind, State state)
    {
        using var rig = new Rig(kind);
        await rig.Reach(state);
        Assert.Equal(PhaseOf(state), rig.Item.Phase);
        var before = (rig.State.Attempts, rig.State.NextAttemptAt, rig.State.LastErrorCode);

        // Pressed twice quickly
        rig.Queue.PauseAll();
        AssertPaused(rig.Item);
        rig.Queue.PauseAll();
        AssertPaused(rig.Item);
        Assert.True(rig.Queue.AllPaused);
        Assert.Single(rig.Queue.LogLines(rig.Id), l => l.Code == "paused");

        // The running step stops; nothing starts, however long it stays paused
        await Temp.WaitUntil(() => rig.Step.Running == 0);
        await Temp.WaitUntil(() => rig.State.Status != StepStatus.Running);
        var runs = rig.Step.Runs;
        rig.Clock.Now += TimeSpan.FromHours(2);
        Assert.Equal(0, rig.Queue.Pump());
        Assert.Equal(runs, rig.Step.Runs);
        AssertPaused(rig.Item);
        Assert.Equal(kind, rig.Item.CurrentStep!.Kind);
        // A stop is not an attempt, and a countdown keeps its time
        if (state is State.Queued or State.Running or State.Checking) Assert.Equal(0, rig.State.Attempts);
        else Assert.Equal(before, (rig.State.Attempts, rig.State.NextAttemptAt, rig.State.LastErrorCode));

        // It goes on from the same step, and only from there
        rig.Run = (_, _) => Task.FromResult(StepResult.Done);
        rig.Queue.ResumeAll();
        rig.Queue.ResumeAll();
        Assert.False(rig.Queue.AllPaused);
        Assert.False(rig.Item.Paused);
        Assert.NotEqual(ItemPhase.Paused, rig.Item.Phase);
        Assert.Single(rig.Queue.LogLines(rig.Id), l => l.Code == "resumed");
        Assert.All(rig.Item.Steps.Where(s => s.Kind < kind), s => Assert.Equal(StepStatus.Done, s.Status));
        await rig.Until(() => rig.Item.Phase == ItemPhase.Done);
        Assert.Equal(runs + 1, rig.Step.Runs);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task One_files_pause_and_resume_do_the_same_and_leave_the_others_alone(ImportStepKind kind, State state)
    {
        using var rig = new Rig(kind);
        await rig.Reach(state);
        var other = QueueItem.For(Video("b.mp4"), "b1", "ch1", rig.Clock.Now);
        rig.Queue.Add(new[] { other });

        rig.Queue.Pause(rig.Id);
        rig.Queue.Pause(rig.Id);
        AssertPaused(rig.Item);
        Assert.False(rig.Queue.AllPaused);
        Assert.Equal(ItemPhase.Waiting, other.Phase);
        await Temp.WaitUntil(() => rig.Step.Running == 0 && rig.State.Status != StepStatus.Running);

        rig.Run = (_, _) => Task.FromResult(StepResult.Done);
        rig.Queue.Resume(rig.Id);
        rig.Queue.Resume(rig.Id);
        Assert.NotEqual(ItemPhase.Paused, rig.Item.Phase);
        rig.Clock.Now += TimeSpan.FromMinutes(1);
        await rig.Until(() => rig.Queue.Items.All(i => i.Phase == ItemPhase.Done));
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Pause_all_survives_a_restart_in_every_state(ImportStepKind kind, State state)
    {
        using var rig = new Rig(kind);
        await rig.Reach(state);
        rig.Queue.PauseAll();
        await Temp.WaitUntil(() => rig.Step.Running == 0 && rig.State.Status != StepStatus.Running);

        var step = new ScriptedStep(kind);
        using var second = rig.Open(step);
        var restored = Assert.Single(second.Items);
        AssertPaused(restored);
        Assert.True(second.AllPaused);
        rig.Clock.Now += TimeSpan.FromHours(1);
        Assert.Equal(0, second.Pump());
        // A file added now waits with the others
        var added = QueueItem.For(Video("b.mp4"), "b1", "ch1", rig.Clock.Now);
        second.Add(new[] { added });
        AssertPaused(added);
        Assert.Equal(0, second.Pump());

        second.ResumeAll();
        await Temp.WaitUntil(() =>
        {
            second.Pump();
            return second.Items.All(i => i.Phase == ItemPhase.Done);
        });
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Resuming_one_file_while_all_is_paused_runs_that_file_only(ImportStepKind kind, State state)
    {
        using var rig = new Rig(kind);
        await rig.Reach(state);
        var other = QueueItem.For(Video("b.mp4"), "b1", "ch1", rig.Clock.Now);
        rig.Queue.Add(new[] { other });
        rig.Queue.PauseAll();
        await Temp.WaitUntil(() => rig.Step.Running == 0 && rig.State.Status != StepStatus.Running);

        rig.Run = (_, _) => Task.FromResult(StepResult.Done);
        rig.Queue.Resume(rig.Id);
        // Still all paused for the rest, and for files added now
        Assert.True(rig.Queue.AllPaused);
        AssertPaused(other);
        var third = QueueItem.For(Video("c.mp4"), "b1", "ch1", rig.Clock.Now);
        rig.Queue.Add(new[] { third });
        AssertPaused(third);
        rig.Clock.Now += TimeSpan.FromMinutes(1);
        await rig.Until(() => rig.Item.Phase == ItemPhase.Done);
        Assert.All(new[] { other, third }, i => Assert.All(i.Steps, s => Assert.Equal(StepStatus.Pending, s.Status)));

        // The last paused file resumed on its own ends Pause all: files added then just run
        rig.Queue.Resume(other.Id);
        rig.Queue.Resume(third.Id);
        Assert.False(rig.Queue.AllPaused);
        var fourth = QueueItem.For(Video("d.mp4"), "b1", "ch1", rig.Clock.Now);
        rig.Queue.Add(new[] { fourth });
        Assert.Equal(ItemPhase.Waiting, fourth.Phase);
        // and pausing one of them later is that file's alone
        rig.Queue.Pause(fourth.Id);
        Assert.False(rig.Queue.AllPaused);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Pause_then_resume_before_the_step_has_stopped_runs_it_once_more_and_never_twice_at_once(ImportStepKind kind, State state)
    {
        using var rig = new Rig(kind);
        await rig.Reach(state);
        rig.Run = async (_, ct) => { await Task.Yield(); return StepResult.Done; };
        for (var i = 0; i < 5; i++)
        {
            rig.Queue.PauseAll();
            rig.Queue.ResumeAll();
        }
        rig.Clock.Now += TimeSpan.FromMinutes(1);
        await rig.Until(() => rig.Item.Phase == ItemPhase.Done);
        Assert.Equal(1, rig.Step.MostAtOnce);
    }

    [Fact]
    public async Task A_file_reads_paused_while_its_step_is_still_stopping_and_resume_waits_for_it()
    {
        using var rig = new Rig(ImportStepKind.Encode);
        var stopping = new TaskCompletionSource();
        rig.Run = async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { await stopping.Task; }
            return StepResult.Done;
        };
        await rig.Until(() => rig.Step.Running == 1);
        rig.Queue.PauseAll();
        Assert.Equal(StepStatus.Running, rig.State.Status);
        AssertPaused(rig.Item);
        rig.Run = (_, _) => Task.FromResult(StepResult.Done);
        rig.Queue.ResumeAll();
        Assert.Equal(0, rig.Queue.Pump());
        Assert.Equal(1, rig.Step.Runs);
        stopping.SetResult();
        await rig.Until(() => rig.Item.Phase == ItemPhase.Done);
        Assert.Equal(2, rig.Step.Runs);
    }

    [Fact]
    public async Task A_step_cut_short_by_pause_is_not_a_failure_whatever_it_stopped_with()
    {
        using var rig = new Rig(ImportStepKind.Upload);
        rig.Run = async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { }
            throw new IOException("The pipe has been ended");
        };
        await rig.Until(() => rig.Step.Running == 1);
        rig.Queue.PauseAll();
        await Temp.WaitUntil(() => rig.State.Status == StepStatus.Pending);
        AssertPaused(rig.Item);
        Assert.Null(rig.State.LastError);
        Assert.Equal(0, rig.State.Attempts);
        Assert.DoesNotContain(rig.Queue.LogLines(rig.Id), l => l.Code is "attempt_failed" or "failed");
    }

    [Fact]
    public async Task A_step_that_finishes_anyway_keeps_its_result_and_the_next_one_does_not_start()
    {
        using var rig = new Rig(ImportStepKind.Attach);
        var sent = new TaskCompletionSource();
        rig.Run = async (_, _) => { await sent.Task; return StepResult.Done; };
        await rig.Until(() => rig.Step.Running == 1);
        rig.Queue.PauseAll();
        sent.SetResult();
        await Temp.WaitUntil(() => rig.State.Status == StepStatus.Done);
        AssertPaused(rig.Item);
        Assert.Equal(ImportStepKind.Transcribe, rig.Item.CurrentStep!.Kind);
        Assert.Equal(0, rig.Queue.Pump());
    }

    [Fact]
    public async Task The_last_step_finishing_while_paused_leaves_the_file_done_and_nothing_paused()
    {
        using var rig = new Rig(ImportStepKind.Translate);
        var asked = new TaskCompletionSource();
        rig.Run = async (_, _) => { await asked.Task; return StepResult.Done; };
        await rig.Until(() => rig.Step.Running == 1);
        rig.Queue.PauseAll();
        asked.SetResult();
        await Temp.WaitUntil(() => rig.Item.Phase == ItemPhase.Done);
        Assert.False(rig.Item.Paused);
        Assert.False(rig.Queue.AllPaused);
    }

    [Fact]
    public async Task A_failure_retrying_cannot_fix_stays_failed_and_its_retry_works_while_all_is_paused()
    {
        using var rig = new Rig(ImportStepKind.Hash);
        rig.Run = (_, _) => throw new FileNotFoundException("gone");
        await rig.Until(() => rig.Item.Phase == ItemPhase.Failed);
        // Nothing to pause
        rig.Queue.PauseAll();
        Assert.False(rig.Queue.AllPaused);
        Assert.Equal((false, false, true), (rig.Item.CanPause, rig.Item.CanResume, rig.Item.CanRetry));

        var other = QueueItem.For(Video("b.mp4"), "b1", "ch1", rig.Clock.Now);
        rig.Queue.Add(new[] { other });
        rig.Queue.PauseAll();
        Assert.Equal(ItemPhase.Failed, rig.Item.Phase);
        AssertPaused(other);
        rig.Run = (_, _) => Task.FromResult(StepResult.Done);
        rig.Queue.ResumeAll();
        Assert.Equal(ItemPhase.Failed, rig.Item.Phase);
        rig.Queue.PauseAll();
        rig.Queue.Retry(rig.Id);
        await rig.Until(() => rig.Item.Phase == ItemPhase.Done);
        AssertPaused(other);
    }

    [Fact]
    public async Task Removing_the_last_paused_file_ends_pause_all()
    {
        using var rig = new Rig(ImportStepKind.Hash);
        rig.Queue.PauseAll();
        rig.Queue.Remove(rig.Id);
        Assert.False(rig.Queue.AllPaused);
        var next = QueueItem.For(Video("b.mp4"), "b1", "ch1", rig.Clock.Now);
        rig.Queue.Add(new[] { next });
        Assert.Equal(ItemPhase.Waiting, next.Phase);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task An_upload_goes_on_from_its_offset_after_resume_all()
    {
        using var rig = new Rig(ImportStepKind.Upload);
        rig.Run = async (_, ct) => { rig.Item.UploadOffset = 4096; await Task.Delay(Timeout.Infinite, ct); return StepResult.Done; };
        await rig.Until(() => rig.Step.Running == 1 && rig.Item.UploadOffset == 4096);
        rig.Queue.PauseAll();
        await Temp.WaitUntil(() => rig.Step.Running == 0 && rig.State.Status == StepStatus.Pending);
        using var second = rig.Open(new ScriptedStep(ImportStepKind.Upload));
        Assert.Equal(4096, second.Items[0].UploadOffset);
    }

    [Fact]
    public async Task A_real_child_process_ends_when_its_step_is_paused()
    {
        if (OperatingSystem.IsWindows()) return; // the same Kill(entireProcessTree) there; no stand-in for ffmpeg without a shell script
        var dir = Temp.Folder();
        var pidFile = Path.Combine(dir, "pid");
        var fake = Temp.Write(dir, "ffprobe", $"#!/bin/sh\nsleep 300 &\necho $! > '{pidFile}'\nwait\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var media = new FfmpegMediaTools(_ => Task.FromResult<(string, string)?>((fake, fake)));
        using var rig = new Rig(ImportStepKind.Encode);
        rig.Run = async (_, ct) => { await media.ProbeAsync("x", ct); return StepResult.Done; };
        await rig.Until(() => File.Exists(pidFile) && File.ReadAllText(pidFile).Trim().Length > 0);
        var pid = int.Parse(File.ReadAllText(pidFile).Trim());

        rig.Queue.PauseAll();
        await Temp.WaitUntil(() => rig.State.Status == StepStatus.Pending, 10_000);
        AssertPaused(rig.Item);
        await Temp.WaitUntil(() =>
        {
            try { using var p = System.Diagnostics.Process.GetProcessById(pid); return p.HasExited; }
            catch (ArgumentException) { return true; }
        });
    }

    [Fact]
    public void A_queue_saved_by_an_older_app_reads_its_failures_by_their_code()
    {
        Assert.True(new StepState { LastErrorCode = "videos_unreachable" }.CanTryAgain);
        Assert.False(new StepState { LastErrorCode = "file_missing" }.CanTryAgain);
        Assert.False(new StepState { LastErrorCode = "file_read", Retryable = false }.CanTryAgain);
    }
}

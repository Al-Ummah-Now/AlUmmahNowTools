namespace AlUmmahNow.ToolsCore.Instance;

public enum StartDecision
{
    /// <summary>This is the one copy on this queue folder</summary>
    Run,
    /// <summary>The running copy came forward: this one ends</summary>
    HandedOver,
    /// <summary>Another copy holds the queue and does not answer: say so, and run nothing</summary>
    AlreadyOpen,
    /// <summary>This is the one copy, but the updater is replacing the app: say so, and run nothing before it is done</summary>
    FinishingUpdate,
}

/// <summary>What a starting copy does (Tools #89): run, hand over to the copy that is open, or wait for one that is closing.</summary>
public static class SingleInstance
{
    /// <summary>A copy that is closing stops and saves its work first (about 20 s at most)</summary>
    public static readonly TimeSpan WhileQuitting = TimeSpan.FromSeconds(30);

    /// <summary>A copy that does not answer may be just starting, or gone a moment later</summary>
    public static readonly TimeSpan WhileSilent = TimeSpan.FromSeconds(12);

    /// <summary>After an update the updater starts the app and ends by itself: not worth a window</summary>
    public static readonly TimeSpan UpdaterEnding = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The whole start. The copy that has the queue listens for second ones before it waits for anything, so they find it
    /// answering; and it waits without a window only for a moment.
    /// </summary>
    /// <param name="listen">Starts answering second copies</param>
    /// <param name="waitForUpdater">Waits at most that long; false when the updater is still at work</param>
    public static async Task<StartDecision> StartAsync(Func<LockAnswer> take, Func<Task<FrontAnswer?>> ask, Action listen, Func<TimeSpan, bool> waitForUpdater, TimeProvider? time = null, Func<TimeSpan, Task>? delay = null)
    {
        var decision = await DecideAsync(take, ask, time, delay).ConfigureAwait(false);
        if (decision != StartDecision.Run) return decision;
        listen();
        return waitForUpdater(UpdaterEnding) ? StartDecision.Run : StartDecision.FinishingUpdate;
    }

    private static readonly TimeSpan Between = TimeSpan.FromMilliseconds(500);

    /// <param name="take">Tries the folder's lock</param>
    /// <param name="ask">Asks the running copy to come forward; null when it does not answer</param>
    public static async Task<StartDecision> DecideAsync(Func<LockAnswer> take, Func<Task<FrontAnswer?>> ask, TimeProvider? time = null, Func<TimeSpan, Task>? delay = null)
    {
        time ??= TimeProvider.System;
        delay ??= Task.Delay;
        var started = time.GetUtcNow();
        var quitting = false;
        while (true)
        {
            // Unavailable: a folder that cannot be locked cannot be told apart from a free one; the queue says if it cannot save
            if (take() != LockAnswer.Busy) return StartDecision.Run;
            var answer = await ask().ConfigureAwait(false);
            if (answer == FrontAnswer.Shown) return StartDecision.HandedOver;
            quitting |= answer == FrontAnswer.Quitting;
            if (time.GetUtcNow() - started >= (quitting ? WhileQuitting : WhileSilent)) return StartDecision.AlreadyOpen;
            await delay(Between).ConfigureAwait(false);
        }
    }
}

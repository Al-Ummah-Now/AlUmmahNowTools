namespace AlUmmahNow.ToolsCore.Import;

/// <summary>What the app does when its window is closed, Cmd+Q is pressed or Dock > Quit is chosen (Tools #78).</summary>
public enum CloseDecision
{
    /// <summary>Nothing is running: stop and quit.</summary>
    QuitNow,
    /// <summary>Files are still being imported: ask first (keep running, or quit and stop).</summary>
    AskFirst,
}

/// <summary>
/// The rules for closing the one-window app. The host (the Uno window, the macOS terminate hook) supplies the dialog,
/// the stop and the exit, so the rules are tested without a window.
/// </summary>
public static class QuitGuard
{
    /// <summary>
    /// Files the app is working on or about to work on: running, queued, waiting to try again, or waiting for the
    /// server's own work. Paused files, failed ones (waiting for Retry) and finished ones are not counted: nothing runs.
    /// </summary>
    public static int ActiveFiles(IEnumerable<QueueItem> items) =>
        items.Count(i => i.Phase is ItemPhase.Running or ItemPhase.Waiting or ItemPhase.RetryWaiting or ItemPhase.ServerWorking);

    public static CloseDecision OnCloseRequested(int activeFiles) => activeFiles > 0 ? CloseDecision.AskFirst : CloseDecision.QuitNow;
}

/// <summary>How long each part of the end may take. Together they fit in <see cref="Total"/>, which is all the end ever waits for the work.</summary>
public static class EndLimits
{
    /// <summary>Running steps stop, and the queue is saved</summary>
    public static readonly TimeSpan Queue = ImportQueue.StepStopGrace;
    /// <summary>The error reports waiting are sent</summary>
    public static readonly TimeSpan Reports = TimeSpan.FromSeconds(3);
    /// <summary>The folder scanner ends when it is told to, or else is killed</summary>
    public static readonly TimeSpan ScannerAsked = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ScannerKilled = TimeSpan.FromSeconds(2);
    /// <summary>The live connection to the scanner is closed</summary>
    public static readonly TimeSpan Connection = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Total = TimeSpan.FromSeconds(20);
    /// <summary>The staged update is checked again and the updater started, after the work has ended</summary>
    public static readonly TimeSpan HandOver = TimeSpan.FromSeconds(10);
}

/// <summary>
/// The one owner of the app's end, however it comes: the window's close button, Cmd+Q, the Quit menu item, Dock > Quit, a close
/// that cannot be cancelled, Restart now, the host returning, the session or the process ending. It asks first when files are
/// still being imported and there is someone to ask (Esc or Keep running leaves everything as it is). Then, always in this order
/// and once: the work is stopped (child processes; the queue saved, so the files carry on from where they were at the next
/// start); a staged update is handed to the updater, only if the work really ended and the end allows it; the exit.
/// Asking twice at once is one question; asking again after Keep running asks again.
/// </summary>
public sealed class QuitCoordinator
{
    private readonly Func<int> _activeFiles;
    private readonly Func<int, Task<bool>> _confirm;
    private readonly Func<CancellationToken, Task> _stop;
    private readonly Action _exit;
    private readonly Action? _handOver;
    private readonly TimeSpan _stopBudget;
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // What an end that comes meanwhile joins
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private State _state = State.Idle;
    private bool _exiting;
    private bool _callsExit;
    private bool _mayHandOver;

    private enum State
    {
        Idle,
        Asking,
        Quitting,
    }

    /// <param name="activeFiles">How many files are being imported now</param>
    /// <param name="confirm">Asks "Imports are still running" with that many files; true is Quit and stop, false is Keep running (Esc, closing the question)</param>
    /// <param name="stop">Stops the work and saves it (the queue, the FileScan service, the log)</param>
    /// <param name="exit">Ends the process through the host's own exit, once the work is saved</param>
    /// <param name="stopBudget">How long stopping may take before the app exits anyway; what was running starts again at the next start</param>
    /// <param name="handOver">Starts the updater, which waits for this process to end</param>
    public QuitCoordinator(Func<int> activeFiles, Func<int, Task<bool>> confirm, Func<CancellationToken, Task> stop, Action exit, TimeSpan? stopBudget = null, Action<string>? log = null, Action? handOver = null)
    {
        _activeFiles = activeFiles;
        _confirm = confirm;
        _stop = stop;
        _exit = exit;
        _handOver = handOver;
        _stopBudget = stopBudget ?? EndLimits.Total;
        _log = log;
    }

    /// <summary>True once the quit is decided: hosts let the window close and the terminate through.</summary>
    public bool IsQuitting
    {
        get { lock (_gate) return _state == State.Quitting; }
    }

    /// <summary>
    /// True from the moment the work is stopped and saved and the exit is called. Only then may the system's own terminate
    /// go through (macOS): one asked for sooner, by a second Cmd+Q during the cleanup, would cut the cleanup short.
    /// </summary>
    public bool IsExiting
    {
        get { lock (_gate) return _exiting; }
    }

    /// <summary>The end in flight, the work stopped and the update handed over (null before it began).</summary>
    public Task? StopTask
    {
        get { lock (_gate) return _state == State.Quitting ? _ended.Task : null; }
    }

    /// <summary>True while the "still running" question is open.</summary>
    public bool IsAsking
    {
        get { lock (_gate) return _state == State.Asking; }
    }

    /// <summary>
    /// Asks to quit. True when the app is quitting (the work was stopped and saved, and the exit was called); false when
    /// the user chose to keep running, or a question is already open.
    /// </summary>
    public Task<bool> RequestQuitAsync()
    {
        lock (_gate)
        {
            if (_state == State.Quitting) return _done.Task;
            if (_state == State.Asking) return Task.FromResult(false);
            _state = State.Asking;
        }
        return AskAsync();
    }

    /// <summary>Quits without asking: there is no one left to ask (the window is gone, under an open question too), or it was asked for knowingly.</summary>
    public Task<bool> QuitNowAsync()
    {
        Begin(exit: true, handOver: true);
        return _done.Task;
    }

    /// <summary>
    /// The process ends by itself: the same end without the exit. What already began is joined, not repeated. Completes when the
    /// work has ended and the update was handed over.
    /// </summary>
    /// <param name="handOver">A staged update may be handed over, when nothing decided that before</param>
    /// <param name="refuse">No update is handed over, whatever was decided before (the session ends: the updater would be cut off)</param>
    public Task EndAsync(bool handOver, bool refuse = false)
    {
        if (refuse)
        {
            lock (_gate)
            {
                handOver = false;
                _mayHandOver = false;
            }
        }
        Begin(exit: false, handOver);
        return _ended.Task;
    }

    private async Task<bool> AskAsync()
    {
        int active;
        try { active = _activeFiles(); }
        catch (Exception ex)
        {
            // Unknown: ask as if something runs, so nothing is stopped unasked
            _log?.Invoke("Could not count the running files: " + ex.Message);
            active = 1;
        }
        if (QuitGuard.OnCloseRequested(active) == CloseDecision.AskFirst)
        {
            bool confirmed;
            try { confirmed = await _confirm(active).ConfigureAwait(false); }
            catch (Exception ex)
            {
                // The question could not be shown. The queue is saved and resumes at the next start either way, so
                // quitting loses nothing, and a close that does nothing is the bug this class exists to fix
                _log?.Invoke("The quit question could not be shown; quitting: " + ex.Message);
                confirmed = true;
            }
            if (!confirmed)
            {
                lock (_gate)
                {
                    // Meanwhile the end may have begun, whatever the answer
                    if (_state == State.Asking)
                    {
                        _state = State.Idle;
                        return false;
                    }
                }
            }
        }
        Begin(exit: true, handOver: true);
        return await _done.Task.ConfigureAwait(false);
    }

    private void Begin(bool exit, bool handOver)
    {
        lock (_gate)
        {
            if (_state == State.Quitting) return;
            _state = State.Quitting;
            _callsExit = exit;
            _mayHandOver = handOver;
        }
        _ = EndAllAsync();
    }

    private async Task EndAllAsync()
    {
        var stopped = false;
        using (var budget = new CancellationTokenSource(_stopBudget))
        {
            try
            {
                // On its own thread: a stop that blocks (a lock, a child process) must not block the caller, which may be the UI
                // thread, or the process-exit handler that waits for it
                await Task.Run(() => _stop(budget.Token)).WaitAsync(budget.Token).ConfigureAwait(false);
                stopped = true;
            }
            catch (OperationCanceledException)
            {
                _log?.Invoke($"Stopping took longer than {_stopBudget.TotalSeconds:0} s; quitting. What was running starts again at the next start.");
            }
            catch (Exception ex)
            {
                _log?.Invoke("Error while stopping, quitting anyway: " + ex.Message);
            }
        }
        bool handOver;
        lock (_gate) handOver = stopped && _mayHandOver && _handOver is not null;
        if (handOver)
        {
            try
            {
                await Task.Run(_handOver!).WaitAsync(EndLimits.HandOver).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Staged again at the next start
                _log?.Invoke("The update was not handed over: " + ex.Message);
            }
        }
        _ended.TrySetResult();
        bool exit;
        lock (_gate)
        {
            _exiting = true;
            exit = _callsExit;
        }
        try
        {
            if (exit) _exit();
            _done.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _done.TrySetException(ex);
        }
    }
}

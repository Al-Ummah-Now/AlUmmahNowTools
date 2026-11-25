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

/// <summary>
/// One quit, however it was asked for: the window's close button, Cmd+Q, the Quit menu item or Dock > Quit. It asks
/// first when files are still being imported (Esc or Keep running leaves everything as it is), then stops the work
/// (child processes; the queue saved, so the files carry on from where they were at the next start) and only then exits.
/// Asking twice at once is one question; asking again after Keep running asks again.
/// </summary>
public sealed class QuitCoordinator
{
    private readonly Func<int> _activeFiles;
    private readonly Func<int, Task<bool>> _confirm;
    private readonly Func<CancellationToken, Task> _stop;
    private readonly Action _exit;
    private readonly TimeSpan _stopBudget;
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private State _state = State.Idle;
    private bool _exiting;
    private Task? _stopTask;
    private Task<bool>? _quit;

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
    public QuitCoordinator(Func<int> activeFiles, Func<int, Task<bool>> confirm, Func<CancellationToken, Task> stop, Action exit, TimeSpan? stopBudget = null, Action<string>? log = null)
    {
        _activeFiles = activeFiles;
        _confirm = confirm;
        _stop = stop;
        _exit = exit;
        _stopBudget = stopBudget ?? TimeSpan.FromSeconds(20);
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

    /// <summary>The stop in flight while quitting (null before and after), so a process that ends meanwhile can join it instead of starting a second.</summary>
    public Task? StopTask
    {
        get { lock (_gate) return _stopTask; }
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
            if (_state == State.Quitting) return _quit!;
            if (_state == State.Asking) return Task.FromResult(false);
            _state = State.Asking;
        }
        return RunAsync();
    }

    private async Task<bool> RunAsync()
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
                lock (_gate) _state = State.Idle;
                return false;
            }
        }

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // What a process that ends meanwhile joins: published in the same breath as IsQuitting, so nobody sees one without the other
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _state = State.Quitting;
            _quit = done.Task;
            _stopTask = stopped.Task;
        }
        try
        {
            using var budget = new CancellationTokenSource(_stopBudget);
            try
            {
                // On its own thread: a stop that blocks (a lock, a child process) must not block the caller, which may be the UI
                // thread, or the process-exit handler that waits for it
                await Task.Run(() => _stop(budget.Token)).WaitAsync(budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _log?.Invoke($"Stopping took longer than {_stopBudget.TotalSeconds:0} s; quitting. What was running starts again at the next start.");
            }
            catch (Exception ex)
            {
                _log?.Invoke("Error while stopping, quitting anyway: " + ex.Message);
            }
            finally
            {
                stopped.TrySetResult();
            }
            lock (_gate) _exiting = true;
            _exit();
            done.TrySetResult(true);
            return true;
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
            throw;
        }
    }
}

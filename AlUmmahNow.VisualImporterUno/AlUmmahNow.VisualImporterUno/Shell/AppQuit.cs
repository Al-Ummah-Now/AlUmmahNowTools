using System.Runtime.InteropServices;
using AlUmmahNow.ToolsCore.Import;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Closing the app (Tools #78). The importer is a one-window app: closing its window quits it, on Windows, macOS and
/// Linux. Uno does nothing when the last window goes (the app kept running with no window on macOS), and nothing stopped
/// the FileScan service or the host, so every way of leaving comes here:
/// <list type="bullet">
/// <item>the window's close button (<c>AppWindow.Closing</c>: cancelled, asked about, then the quit closes it);</item>
/// <item>a close the platform could not cancel (<c>Window.Closed</c>: nothing left to ask, so it quits);</item>
/// <item>Cmd+Q, the Quit menu item and Dock > Quit on macOS (the native "can exit" answer, taken over below).</item>
/// </list>
/// When files are being imported the question "Imports are still running" comes first (Esc is Keep running). Quitting
/// then stops the queue (ffmpeg and the others die with their steps, the queue is saved so the files carry on at the
/// next start), stops the FileScan service and the host, and asks the app to exit. See <see cref="QuitCoordinator"/>.
/// </summary>
internal sealed class AppQuit
{
    private readonly Window _window;
    private readonly QuitCoordinator _coordinator;
    private bool _windowClosed;
    private readonly System.Diagnostics.Stopwatch _since = new();

    private AppQuit(Window window, Func<FlowDirection> flow)
    {
        _window = window;
        _flow = flow;
        _coordinator = new QuitCoordinator(
            () => ImportWorkspace.ActiveFiles,
            ConfirmAsync,
            ct => StopAsync(ct, installUpdate: true),
            ExitApp,
            log: message => Log.Information("[Quit] {Message}", message));
    }

    private int _stopped;
    private readonly Func<FlowDirection> _flow;
    private static AppQuit? _current;

    public static QuitCoordinator? Coordinator => _current?._coordinator;

    /// <summary>Takes over the window's close and (on macOS) the application's terminate. Call once, when the window is made.</summary>
    public static void Attach(Window window, Func<FlowDirection> flow)
    {
        var quit = _current = new AppQuit(window, flow);
        window.AppWindow.Closing += (sender, e) =>
        {
            // The close button: keep the window until the question is answered; the quit closes it
            if (quit._coordinator.IsQuitting) return;
            e.Cancel = true;
            _ = quit.RequestAsync("window close");
        };
        window.Closed += (_, _) =>
        {
            quit._windowClosed = true;
            // A close that could not be cancelled, or Window.Close() from code: the window is gone, so there is no one to ask
            if (!quit._coordinator.IsQuitting) _ = quit.RequestAsync("window closed");
        };
        // Review frames of the question (TOOLS_DEMO=1 TOOLS_SCREENSHOT=out.png TOOLS_SCREENSHOT_QUIT=1)
        if (Environment.GetEnvironmentVariable("TOOLS_SCREENSHOT_QUIT") == "1") _ = quit.ShowForScreenshotAsync();
        // However the process is asked to end (SIGTERM, a log-out, the system's terminate): what runs stops and is saved first
        AppDomain.CurrentDomain.ProcessExit += (_, _) => quit.StopOnProcessExit();
        MacTerminateHook.Install(window.DispatcherQueue, () => quit._coordinator.IsExiting, () => _ = quit.RequestAsync("Cmd+Q / Dock Quit"), quit.StopOnProcessExit);
    }

    /// <summary>For the launch check (<c>TOOLS_SMOKE_CLOSE</c>) and anywhere else the app quits by itself.</summary>
    public static Task<bool> RequestQuitAsync(string why)
    {
        if (_current is { } quit) return quit.RequestAsync(why);
        // No window was made yet (the app is starting or failed to): nothing to stop, nobody to ask
        Application.Current.Exit();
        return Task.FromResult(true);
    }

    private async Task<bool> RequestAsync(string why)
    {
        try
        {
            if (!_since.IsRunning) _since.Restart();
            Log.Information("[Quit] Requested: {Why}", why);
            return await _coordinator.RequestQuitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Quit] The quit failed ({Why})", why);
            return false;
        }
    }

    private Task<bool> ConfirmAsync(int files)
    {
        // The window is gone: there is nothing to ask, and the queue is saved either way
        if (_windowClosed) return Task.FromResult(true);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_window.DispatcherQueue.TryEnqueue(async () =>
        {
            try { done.TrySetResult(await ShowQuestionAsync(files)); }
            catch (Exception ex) { done.TrySetException(ex); }
        })) done.TrySetResult(true);
        return done.Task;
    }

    /// <summary>"Imports are still running": Quit and stop, or Keep running (Esc, or closing the box, keeps running).</summary>
    private async Task<bool> ShowQuestionAsync(int files)
    {
        var xamlRoot = _window.Content?.XamlRoot ?? throw new InvalidOperationException("The window has no content to show the question in");
        // A question already open (remove a file, cancel a sync) gives way: a close must never be ignored
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
            if (popup.Child is ContentDialog open) open.Hide();
        var dialog = BuildQuestion(files, xamlRoot, _flow());
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowForScreenshotAsync()
    {
        await Task.Delay(1500).ConfigureAwait(false);
        _window.DispatcherQueue.TryEnqueue(async () =>
        {
            try { await ShowQuestionAsync(Math.Max(ImportWorkspace.ActiveFiles, 1)); }
            catch (Exception ex) { Log.Warning(ex, "[Quit] Could not show the question for the screenshot"); }
        });
    }

    internal static ContentDialog BuildQuestion(int files, XamlRoot xamlRoot, FlowDirection flow)
    {
        var panel = new StackPanel { Spacing = 6 };
        // Each line is short enough for one line: a popup's Arabic order only holds on one
        foreach (var line in new[] { Loc.Format("Quit_Files", files), Loc.Get("Quit_Resume") })
            panel.Children.Add(new TextBlock { Text = Loc.PopupLine(Loc.KeepLastWords(line)), TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Quit_Title"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Quit_Confirm"),
            CloseButtonText = Loc.Get("Quit_Keep"),
            DefaultButton = ContentDialogButton.Close,
            FlowDirection = flow,
            XamlRoot = xamlRoot,
        };
        DialogLook.Apply(dialog);
        return dialog;
    }

    /// <summary>
    /// Stops what the app started: the queue (running steps, so ffmpeg and the others, the queue saved), then the FileScan
    /// service, SignalR, the host and the log. Each step has its own limit inside; the coordinator's budget covers all.
    /// </summary>
    /// <param name="installUpdate">A quit: a staged update is handed to the updater, which waits for this process to end. Not when the
    /// session ends, where the updater would be cut off in the middle of replacing the app.</param>
    private static async Task StopAsync(CancellationToken ct, bool installUpdate)
    {
        var quit = _current;
        Log.Information("[Quit] Stopping the work ({Ms} ms after the request)", quit?._since.ElapsedMilliseconds);
        await ImportWorkspace.StopForQuitAsync().ConfigureAwait(false);
        // Here and not only on ProcessExit: a quit on macOS ends through the system, which never raises it
        if (installUpdate) AppUpdates.InstallOnQuit();
        await App.Current.ShutdownServicesAsync().ConfigureAwait(false);
        Log.Information("[Quit] Work stopped and saved ({Ms} ms after the request)", quit?._since.ElapsedMilliseconds);
    }

    /// <summary>
    /// The process is ending (a signal, a log-out, the system's terminate): what runs is stopped and saved first, a few seconds
    /// at most. A quit already stopping it is joined, not repeated and not ignored: the process must not end half way through it.
    /// Once the quit has stopped everything and called the exit there is nothing left to wait for.
    /// </summary>
    private void StopOnProcessExit()
    {
        // Once: the session's end on macOS comes here before the process does
        if (_coordinator.IsExiting || Interlocked.Exchange(ref _stopped, 1) != 0) return;
        try
        {
            var wait = TimeSpan.FromSeconds(8);
            if (_coordinator.IsQuitting)
            {
                Log.Information("[Quit] The process is ending while the quit is stopping the work: waiting for it");
                _coordinator.StopTask?.Wait(wait);
                return;
            }
            Log.Information("[Quit] The process is ending: stopping the work first");
            // Off this thread, which may be the interface's
            Task.Run(() => StopAsync(CancellationToken.None, installUpdate: false)).Wait(wait);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Quit] Stopping on process exit failed");
        }
    }

    private void ExitApp()
    {
        // The stop is done and saved. Exit goes through the host (macOS: NSApp terminate, answered below; Windows and Linux:
        // the host's loop ends and Main returns), on the UI thread
        void Exit()
        {
            Log.Information("[Quit] Exiting ({Ms} ms after the request)", _since.ElapsedMilliseconds);
            // Windows and Linux end when the last window is gone, and Windows' Exit alone closes nothing: close it, then exit
            if (!_windowClosed)
            {
                try { _window.Close(); Log.Information("[Quit] Window closed"); }
                catch (Exception ex) { Log.Warning(ex, "[Quit] Could not close the window"); }
            }
            try { Application.Current.Exit(); Log.Information("[Quit] Application.Exit returned"); }
            catch (Exception ex) { Log.Warning(ex, "[Quit] Application.Exit failed"); }
            // macOS: Exit asked the system to terminate, which ends the process. Windows and Linux: Uno's host does not end the
            // process by itself once the last window is gone (its event-loop threads are not background threads, and on Windows
            // Run does not return), and everything is stopped and saved by now: the process ends here, off the UI thread
            if (OperatingSystem.IsMacOS()) ExitBackstop.Arm();
            else ExitBackstop.EndSoon();
        }
        if (_window.DispatcherQueue.HasThreadAccess) Exit();
        else if (!_window.DispatcherQueue.TryEnqueue(Exit)) Exit();
    }
}

/// <summary>
/// After the quit has stopped and saved everything the process ends: on macOS the system's terminate does it (this is only the last
/// resort if it does not), on Windows and Linux the quit ends it itself (Uno's host does not). If something the app does not own
/// (a host's own thread) still holds it ten seconds later, it is logged and the process ends then. This runs only after
/// the work is saved, never in the middle of it.
/// </summary>
internal static class ExitBackstop
{
    /// <summary>The last resort: the process is still here <paramref name="after"/> the quit stopped and saved everything.</summary>
    public static void Arm(TimeSpan? after = null)
    {
        var wait = after ?? TimeSpan.FromSeconds(10);
        Start(wait, () => Log.Warning("[Quit] The process was still running {Seconds} s after the quit; ending it", wait.TotalSeconds));
    }

    /// <summary>The quit is done and saved: give the host a moment to close its window, then end the process, off the UI thread.</summary>
    public static void EndSoon() => Start(TimeSpan.FromMilliseconds(300), () => Log.Information("[Quit] Work stopped and saved; ending the process"));

    private static void Start(TimeSpan wait, Action log)
    {
        var thread = new Thread(() =>
        {
            Thread.Sleep(wait);
            try { log(); Log.CloseAndFlush(); }
            catch (Exception) { }
            Environment.Exit(0);
        })
        { IsBackground = true, Name = "quit-exit" };
        thread.Start();
    }
}

/// <summary>
/// macOS only. Uno answers the system's "may the app terminate?" with a fixed yes, so Cmd+Q, the Quit menu item and
/// Dock > Quit ended the process at once, without the question and without stopping anything. This answers it
/// ourselves: no, and the quit goes through <see cref="QuitCoordinator"/>, which exits (and then this answers yes).
/// Two kinds of terminate are answered yes at once, because they must never wait for a person: the one the coordinator
/// made itself, and the one the system makes when the session ends (log out, restart, shut down). The second one is
/// not asked about; the work is stopped and saved first, a few seconds at most.
/// </summary>
internal static class MacTerminateHook
{
    private delegate int CanExit();
    private static CanExit? _keep;

    [DllImport("libUnoNativeMac.dylib", EntryPoint = "uno_set_application_can_exit_callback", ExactSpelling = true)]
    private static extern void SetCanExitCallback(IntPtr callback);

    /// <param name="sessionEnding">Stops and saves the work, a few seconds at most: the system's exit does not raise ProcessExit</param>
    public static void Install(DispatcherQueue ui, Func<bool> exiting, Action request, Action sessionEnding)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            _keep = () =>
            {
                if (exiting()) return 1;
                if (SessionEnding())
                {
                    Log.Information("[Quit] The session is ending (log out, restart or shut down): no question, the work is saved first");
                    sessionEnding();
                    return 1;
                }
                ui.TryEnqueue(() => request());
                return 0;
            };
            SetCanExitCallback(Marshal.GetFunctionPointerForDelegate(_keep));
            Log.Information("[Quit] Cmd+Q and Dock > Quit go through the quit question");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warning(ex, "[Quit] Could not take over Cmd+Q; the system's quit applies");
        }
    }

    // The quit Apple event the system sends carries its reason ('why?'): log out, restart or shut down, with or without their dialog.
    // Cmd+Q and Dock > Quit carry none. Read from the event being handled (this runs inside it, on the main thread).
    private const uint KeyReason = 0x7768793F;     // 'why?' (kAEQuitReason)
    private static readonly uint[] SessionReasons = { 0x6C6F676F, 0x726C676F, 0x72657374, 0x73687574, 0x72727374, 0x7273646E }; // 'logo' 'rlgo' 'rest' 'shut' 'rrst' 'rsdn'

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, uint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern uint SendForUInt(IntPtr receiver, IntPtr selector);

    internal static bool SessionEnding()
    {
        try
        {
            var manager = Send(objc_getClass("NSAppleEventManager"), sel_registerName("sharedAppleEventManager"));
            if (manager == IntPtr.Zero) return false;
            var current = Send(manager, sel_registerName("currentAppleEvent"));
            if (current == IntPtr.Zero) return false;
            var reason = Send(current, sel_registerName("attributeDescriptorForKeyword:"), KeyReason);
            if (reason == IntPtr.Zero) return false;
            return Array.IndexOf(SessionReasons, SendForUInt(reason, sel_registerName("enumCodeValue"))) >= 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}

/// <summary>
/// The launch check's way of closing the app (docs/RELEASE.md): <c>TOOLS_SMOKE_CLOSE=close|quit|terminate</c> with
/// <c>TOOLS_SMOKE_CLOSE_DELAY_MS</c> (default 8000). The check then asserts the process ends within seconds and leaves no
/// child process behind.
/// <list type="bullet">
/// <item><c>close</c>: <c>Window.Close()</c>, as a close the platform cannot cancel (the <c>Closed</c> path);</item>
/// <item><c>quit</c>: the same request the close button, Cmd+Q and Dock > Quit make;</item>
/// <item><c>terminate</c>: the host's own exit, as the system asks it (macOS: <c>NSApp terminate:</c>, through the hook).</item>
/// </list>
/// </summary>
internal static class QuitSmoke
{
    public static async Task RunAsync(Window window, string mode)
    {
        var delay = int.TryParse(Environment.GetEnvironmentVariable("TOOLS_SMOKE_CLOSE_DELAY_MS"), out var ms) ? ms : 8000;
        // The check looks at the app's child processes once this file is there, and times the end from the "closing" file
        var ready = Environment.GetEnvironmentVariable("TOOLS_SMOKE_READY");
        Mark(ready, Environment.ProcessId.ToString());
        await Task.Delay(delay).ConfigureAwait(false);
        Log.Information("[Quit] Launch check: closing the app ({Mode})", mode);
        Mark(ready is null ? null : ready + ".closing", mode);
        var done = new TaskCompletionSource();
        window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                switch (mode)
                {
                    case "quit": _ = AppQuit.RequestQuitAsync("launch check"); break;
                    case "terminate": Application.Current.Exit(); break;
                    default: window.Close(); break;
                }
            }
            finally { done.TrySetResult(); }
        });
        await done.Task.ConfigureAwait(false);
    }

    private static void Mark(string? path, string text)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { File.WriteAllText(path, text); }
        catch (Exception ex) { Log.Warning(ex, "[Quit] Launch check: could not write {Path}", path); }
    }
}

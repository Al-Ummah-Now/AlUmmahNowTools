using System.Diagnostics;
using System.Runtime.InteropServices;
using AlUmmahNow.ToolsCore;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Instance;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// One copy of the app per queue folder (Tools #89). Decided in <c>Main</c>, before the window, the services and the updates:
/// a second copy brings the first one forward and ends, or says it is already open when the first one does not answer. A
/// demo run has no queue of its own on disk and runs beside anything; <c>TOOLS_DEMO_INSTANCE=folder</c> gives it one (the
/// launch check), <c>TOOLS_DEMO_INSTANCE=busy</c> shows the "already open" window for review frames.
/// </summary>
internal static class AppInstance
{
    private static QueueFolderLock? _lock;
    private static string? _folder;
    private static Window? _window;
    private static readonly CancellationTokenSource Ending = new();

    /// <summary>Another copy has the queue and does not answer: only the window that says so is shown.</summary>
    public static bool AlreadyOpen { get; private set; }

    /// <summary>This copy may run the queue and install updates.</summary>
    public static bool OwnsQueue { get; private set; }

    /// <summary>Other copies running from this install; an update waits for them.</summary>
    public static InstallGuard? Install { get; } = InstallGuard.ForThisProcess();

    /// <summary>False: the copy that is open came forward, and this one ends.</summary>
    public static bool Start()
    {
        _folder = Path.GetDirectoryName(ImportQueue.DefaultPath(ToolsEnvironment.For(ToolsEnvironmentKind.Production)));
        if (Environment.GetEnvironmentVariable("TOOLS_DEMO") == "1")
        {
            var demo = Environment.GetEnvironmentVariable("TOOLS_DEMO_INSTANCE");
            AlreadyOpen = demo == "busy";
            if (string.IsNullOrEmpty(demo) || AlreadyOpen) return true;
            _folder = demo;
        }
        var folder = _folder!;
        var decision = SingleInstance.DecideAsync(() => QueueFolderLock.TryTake(folder, out _lock), () => InstanceChannel.AskAsync(folder, AllowForeground)).GetAwaiter().GetResult();
        switch (decision)
        {
            case StartDecision.HandedOver:
                Console.Error.WriteLine("[Instance] Al Ummah Now Tools is already open; its window is in front.");
                return false;
            case StartDecision.AlreadyOpen:
                Console.Error.WriteLine("[Instance] Al Ummah Now Tools is already open and does not answer.");
                AlreadyOpen = true;
                return true;
        }
        OwnsQueue = true;
        // Started while the updater replaces the app: it ends what runs from the folder, so nothing is imported before it is done
        Install?.WaitForUpdater(TimeSpan.FromSeconds(90));
        _ = Task.Run(() => InstanceChannel.ListenAsync(folder, BringForwardAsync, ex => Log.Warning("[Instance] A second copy was not answered: {Why}", ex.Message), Ending.Token));
        return true;
    }

    /// <summary>The window a second copy brings forward.</summary>
    public static void Attach(Window window) => _window = window;

    private static async Task<FrontAnswer> BringForwardAsync()
    {
        if (AppQuit.Coordinator is { IsQuitting: true }) return FrontAnswer.Quitting;
        // Still starting: its window opens in front by itself
        if (_window is not { } window) return FrontAnswer.Shown;
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                WindowFront.Show(window);
                Log.Information("[Instance] Brought forward for a second copy");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Instance] The window could not be brought forward");
            }
            shown.TrySetResult();
        })) return FrontAnswer.Quitting;
        await shown.Task.ConfigureAwait(false);
        return FrontAnswer.Shown;
    }

    /// <summary>Starts the app again, for Try again in the "already open" window, and ends this one.</summary>
    public static void StartAgain()
    {
        if (Environment.ProcessPath is { } program)
        {
            var bundle = program.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
            var start = OperatingSystem.IsMacOS() && bundle > 0
                ? new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-n", program[..(bundle + 4)] } }
                : new ProcessStartInfo(program);
            start.UseShellExecute = false;
            try { Process.Start(start)?.Dispose(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Console.Error.WriteLine("[Instance] " + ex.Message); }
        }
        Application.Current.Exit();
        ExitBackstop.EndSoon();
    }

    private static void AllowForeground(int pid)
    {
        if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(pid);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int pid);
}

/// <summary>The app's window in front of the others, also when it is minimized or the app is hidden. On the UI thread.</summary>
internal static class WindowFront
{
    public static void Show(Window window)
    {
        try
        {
            if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        }
        catch (Exception ex) when (ex is NotSupportedException or NotImplementedException or InvalidOperationException)
        {
            Log.Information("[Instance] Restore: {Why}", ex.Message);
        }
        window.Activate();
        if (OperatingSystem.IsWindows()) Windows();
        else if (OperatingSystem.IsMacOS()) Mac();
    }

    private static void Windows()
    {
        using var me = Process.GetCurrentProcess();
        var handle = me.MainWindowHandle;
        if (handle == IntPtr.Zero) return;
        const int restore = 9;
        if (IsIconic(handle)) ShowWindow(handle, restore);
        SetForegroundWindow(handle);
    }

    private static void Mac()
    {
        var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        if (app == IntPtr.Zero) return;
        Send(app, sel_registerName("unhide:"), IntPtr.Zero);
        var windows = Send(app, sel_registerName("windows"));
        var count = (long)Send(windows, sel_registerName("count"));
        for (nint i = 0; i < count; i++)
        {
            var one = Send(windows, sel_registerName("objectAtIndex:"), i);
            if (SendBool(one, sel_registerName("isMiniaturized")) != 0) Send(one, sel_registerName("deminiaturize:"), IntPtr.Zero);
        }
        SendFlag(app, sel_registerName("activateIgnoringOtherApps:"), 1);
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte SendBool(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendFlag(IntPtr receiver, IntPtr selector, byte argument);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
}

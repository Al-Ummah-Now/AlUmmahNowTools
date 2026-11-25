#if UPDATE_TEST
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The update test's ways of closing the app (<c>TOOLS_UPDATE_TEST_CLOSE</c>), each as the platform makes it:
/// <list type="bullet">
/// <item><c>quit</c>: the request every way ends in;</item>
/// <item><c>close-button</c>: the window's close button (macOS the button's own click, Windows <c>WM_CLOSE</c>);</item>
/// <item><c>window-close</c>: <c>Window.Close()</c>, a close that cannot be cancelled;</item>
/// <item><c>cmd-q</c> (macOS): the Quit menu item's <c>terminate:</c>;</item>
/// <item><c>dock-quit</c> and <c>log-out</c> (macOS): the system's quit event, without a reason and with the log-out's;</item>
/// <item><c>restart</c>: the update bar's own Restart now button.</item>
/// </list>
/// Compiled only with -p:UpdateTest=true.
/// </summary>
internal static class UpdateTestClose
{
    public static void Run(string mode)
    {
        var window = App.Current.MainWindow ?? throw new InvalidOperationException("no window");
        window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                UpdateTestHandler.Log($"closing {mode}");
                switch (mode)
                {
                    case "quit": _ = AppQuit.RequestQuitAsync("update test"); break;
                    case "window-close": window.Close(); break;
                    case "close-button" when OperatingSystem.IsWindows():
                        if (!Process.GetCurrentProcess().CloseMainWindow()) throw new InvalidOperationException("no main window");
                        break;
                    case "close-button": Send(MacCloseButton(), "performClick:", IntPtr.Zero); break;
                    case "cmd-q": Send(MacApp(), "terminate:", IntPtr.Zero); break;
                    case "dock-quit": SendQuitEvent(reason: 0); break;
                    case "log-out": SendQuitEvent(reason: 0x6C6F676F); break; // 'logo'
                    case "restart": Press(window.Content!); break;
                    default: throw new ArgumentException(mode);
                }
            }
            catch (Exception ex)
            {
                UpdateTestHandler.Log($"could not close: {ex.Message}");
            }
        });
    }

    private static void Press(DependencyObject root)
    {
        var bar = Find<UpdateBar>(root, _ => true) ?? throw new InvalidOperationException("no update bar");
        var button = Find<Button>(bar, b => b.Visibility == Visibility.Visible && Equals(b.Content, Loc.Get("Update_RestartNow"))) ?? throw new InvalidOperationException("no Restart now button");
        if (!button.IsEnabled) throw new InvalidOperationException("Restart now is disabled");
        new ButtonAutomationPeer(button).Invoke();
    }

    private static T? Find<T>(DependencyObject root, Func<T, bool> matches) where T : class, DependencyObject
    {
        if (root is T found && matches(found)) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find(VisualTreeHelper.GetChild(root, i), matches) is { } child) return child;
        return null;
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendButton(IntPtr receiver, IntPtr selector, nuint button);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendUInt(IntPtr receiver, IntPtr selector, uint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendEvent(IntPtr receiver, IntPtr selector, uint eventClass, uint eventId, IntPtr target, short returnId, int transactionId);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendAttribute(IntPtr receiver, IntPtr selector, IntPtr descriptor, uint keyword);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendOut(IntPtr receiver, IntPtr selector, nuint options, double timeout, out IntPtr error);

    private static void Send(IntPtr receiver, string selector, IntPtr argument) => Send(receiver, sel_registerName(selector), argument);

    private static IntPtr MacApp() => Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));

    /// <summary>The close button of the app's window: the app has other windows (its menus'), which have none.</summary>
    private static IntPtr MacCloseButton()
    {
        var windows = Send(MacApp(), sel_registerName("windows"));
        var count = (long)Send(windows, sel_registerName("count"));
        var found = IntPtr.Zero;
        for (nint i = 0; i < count; i++)
        {
            var window = Send(windows, sel_registerName("objectAtIndex:"), i);
            var button = SendButton(window, sel_registerName("standardWindowButton:"), 0);
            var visible = (Send(window, sel_registerName("isVisible")) & 0xFF) != 0;
            UpdateTestHandler.Log($"window {Marshal.PtrToStringUTF8(Send(Send(Send(window, sel_registerName("class")), sel_registerName("description")), sel_registerName("UTF8String")))} visible={visible} button={button != IntPtr.Zero}");
            if (!visible || button == IntPtr.Zero) continue;
            if (found != IntPtr.Zero) throw new InvalidOperationException("the app has more than one window");
            found = button;
        }
        return found != IntPtr.Zero ? found : throw new InvalidOperationException("no close button");
    }

    /// <summary>The quit event ('aevt'/'quit') to this process, as the Dock and the log-out send it.</summary>
    private static void SendQuitEvent(uint reason)
    {
        var descriptor = objc_getClass("NSAppleEventDescriptor");
        var target = Send(descriptor, sel_registerName("currentProcessDescriptor"));
        var quit = SendEvent(descriptor, sel_registerName("appleEventWithEventClass:eventID:targetDescriptor:returnID:transactionID:"), 0x61657674, 0x71756974, target, -1, 0);
        if (reason != 0) SendAttribute(quit, sel_registerName("setAttributeDescriptor:forKeyword:"), SendUInt(descriptor, sel_registerName("descriptorWithEnumCode:"), reason), 0x7768793F); // 'why?'
        SendOut(quit, sel_registerName("sendEventWithOptions:timeout:error:"), 1 /* no reply */, 5, out var error);
        if (error != IntPtr.Zero) throw new InvalidOperationException("the quit event was not sent");
    }
}
#endif

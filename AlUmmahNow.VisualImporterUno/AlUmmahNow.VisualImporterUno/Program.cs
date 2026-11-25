#if !WINDOWS
using Microsoft.UI.Xaml;
using Uno.UI.Hosting;

namespace AlUmmahNow.VisualImporterUno;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack's install, update and uninstall hooks run first and may exit (wiki AUN-APP-UPDATES §5.2)
        // and never install a package by themselves: UpdateService checks its signature and hash again first
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        // One copy per queue folder (Tools #89): the copy that is open came forward, and this one ends
        if (!Shell.AppInstance.Start(args)) return;
        var host = UnoPlatformHostBuilder.Create()
            // One app for the three desktops (wiki TOOLS-ADMIN): AppKit on macOS, Win32 on Windows, X11 on Linux
            .UseMacOS()
            .UseWin32()
            .UseX11()
            .UseLinuxFrameBuffer()
            .App(() => new App())
            .Build();

        host.Run();
        // Run returns once the last window is gone and the quit has stopped and saved the work (Shell/AppQuit.cs). Uno's own
        // event-loop threads are not background threads, so on Windows and Linux they would keep the process alive with no window
        // (the launch check measured it): the process ends here, as Main returns
        Serilog.Log.Information("[Quit] The host stopped; ending the process");
        Environment.Exit(0);
    }
}
#endif

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
        var host = UnoPlatformHostBuilder.Create()
            // One app for the three desktops (wiki TOOLS-ADMIN): AppKit on macOS, Win32 on Windows, X11 on Linux
            .UseMacOS()
            .UseWin32()
            .UseX11()
            .UseLinuxFrameBuffer()
            .App(() => new App())
            .Build();

        host.Run();
    }
}
#endif

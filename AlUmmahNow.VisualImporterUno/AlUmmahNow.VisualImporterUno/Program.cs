#if !WINDOWS
using Microsoft.UI.Xaml;
using Uno.UI.Hosting;

namespace AlUmmahNow.VisualImporterUno;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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

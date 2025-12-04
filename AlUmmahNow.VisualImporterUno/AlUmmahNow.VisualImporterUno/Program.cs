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
            .UseX11()
            .UseLinuxFrameBuffer()
            .App(() => new App())
            .Build();

        host.Run();
    }
}
#endif

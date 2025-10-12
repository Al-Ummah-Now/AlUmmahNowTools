using Microsoft.UI.Xaml;

namespace AlUmmahNow.VisualImporterUno;

#if !WINDOWS
public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var app = new App();
        });
    }
}
#endif

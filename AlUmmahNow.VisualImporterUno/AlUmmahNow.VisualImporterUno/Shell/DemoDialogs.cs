using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>Review frames of a page's questions: <c>TOOLS_DEMO=1 TOOLS_DEMO_DIALOG=name</c> opens the one of that name.</summary>
internal static class DemoDialogs
{
    public static string? Name => Environment.GetEnvironmentVariable("TOOLS_DEMO") == "1" ? Environment.GetEnvironmentVariable("TOOLS_DEMO_DIALOG") : null;

    public static void OnLoaded(FrameworkElement page, string name, Func<Task> open)
    {
        if (Name != name) return;
        page.Loaded += async (_, _) =>
        {
            // A dialog needs the window's XamlRoot, which is ready a moment after the page loads
            await Task.Delay(1200);
            await open();
        };
    }
}

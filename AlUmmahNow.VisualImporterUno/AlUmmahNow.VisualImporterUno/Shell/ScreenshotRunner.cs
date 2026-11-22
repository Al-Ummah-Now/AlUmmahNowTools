#if !WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Screenshots for review (<c>TOOLS_SCREENSHOT=out.png</c>, with <c>TOOLS_DEMO=1</c>): sizes the
/// window (<c>TOOLS_SCREENSHOT_SIZE=1280x800</c>), waits for the screen to settle, renders the window's
/// content to a PNG and exits. No screen-recording permission needed. A frame of a page ends at an edge
/// (<see cref="EndAtEdge"/>): never through a line of text or a control (<c>TOOLS_SCREENSHOT_CROP=0</c> keeps the window).
/// </summary>
public static class ScreenshotRunner
{
    public static async Task RunAsync(Window window, string path)
    {
        var size = (Environment.GetEnvironmentVariable("TOOLS_SCREENSHOT_SIZE") ?? "1280x800").Split('x');
        var (w, h) = (int.Parse(size[0]), int.Parse(size[1]));
        // AppWindow sizes are physical pixels: scale the logical size by the display's
        await Task.Delay(500);
        var scale = window.Content?.XamlRoot?.RasterizationScale ?? 1.0;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = (int)(w * scale), Height = (int)(h * scale) });
        await Task.Delay(int.TryParse(Environment.GetEnvironmentVariable("TOOLS_SCREENSHOT_DELAY_MS"), out var ms) ? ms : 3000);
        // Dialogs and flyouts live in popups beside the content, under the window's root visual: render from the top
        UIElement root = (UIElement)window.Content!;
        while (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(root) is UIElement parent) root = parent;
        using var full = await RenderAsync(root);
        var popups = window.Content?.XamlRoot is { } xamlRoot ? Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Count : 0;
        var end = popups == 0 && Environment.GetEnvironmentVariable("TOOLS_SCREENSHOT_CROP") != "0" ? EndAtEdge((UIElement)window.Content!) : null;
        var cropTo = end is { } y ? Math.Min(full.Height, (int)Math.Ceiling(y * scale)) : full.Height;
        using var image = cropTo < full.Height ? full.Subset(new SKRectI(0, 0, full.Width, cropTo)) : SKImage.FromBitmap(SKBitmap.FromImage(full));
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using (var file = File.Create(path)) data.SaveTo(file);
        Serilog.Log.Information("[Shell] Screenshot written to {Path} ({W}x{H})", path, image.Width, image.Height);
        Environment.Exit(0);
    }

    /// <summary>
    /// Where a page's frame ends (logical pixels from the top), or null for the whole window: the lowest edge of a card, a
    /// row or a divider inside the page's scroll area that no text or control crosses, plus a 4px margin, so a frame never
    /// ends half way through a row (AUDIT-2 2026-11-22). Only when that keeps most of the window.
    /// </summary>
    internal static double? EndAtEdge(UIElement content)
    {
        var height = content.ActualSize.Y;
        if (height <= 0 || Largest<Microsoft.UI.Xaml.Controls.ScrollViewer>(content) is not { } page) return null;
        var leaves = new List<(double Top, double Bottom)>();
        var edges = new List<double>();
        Collect(page, leaves, edges);
        const double margin = 4;
        foreach (var edge in edges.Where((e) => e + margin <= height).OrderByDescending((e) => e))
        {
            if (edge < height * 0.5) break;
            var cut = edge + margin;
            if (leaves.Any((l) => l.Top < cut - 0.5 && l.Bottom > edge + 0.5)) continue;
            return cut;
        }
        return null;
    }

    private static void Collect(DependencyObject parent, List<(double, double)> leaves, List<double> edges)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i) is not FrameworkElement child || child.Visibility != Visibility.Visible || child.ActualHeight <= 0) continue;
            var bounds = child.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, child.ActualWidth, child.ActualHeight));
            if (child is Microsoft.UI.Xaml.Controls.TextBlock or Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or Microsoft.UI.Xaml.Controls.TextBox
                or Microsoft.UI.Xaml.Controls.ComboBox or Microsoft.UI.Xaml.Controls.ProgressRing or Microsoft.UI.Xaml.Controls.ProgressBar
                or Microsoft.UI.Xaml.Controls.IconElement or Microsoft.UI.Xaml.Controls.Image or Microsoft.UI.Xaml.Controls.ToggleSwitch)
            {
                leaves.Add((bounds.Top, bounds.Bottom));
                continue;
            }
            if (child is Microsoft.UI.Xaml.Controls.Border { } border && (border.Background is not null || border.BorderThickness.Bottom > 0)) edges.Add(bounds.Bottom);
            else if (child is Microsoft.UI.Xaml.Shapes.Rectangle) edges.Add(bounds.Bottom);
            Collect(child, leaves, edges);
        }
    }

    private static T? Largest<T>(DependencyObject parent) where T : FrameworkElement
    {
        T? best = null;
        void Walk(DependencyObject node)
        {
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
                if (child is T match && match.Visibility == Visibility.Visible && (best is null || match.ActualWidth * match.ActualHeight > best.ActualWidth * best.ActualHeight)) best = match;
                Walk(child);
            }
        }
        Walk(parent);
        return best;
    }

    private static async Task<SKImage> RenderAsync(UIElement element)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await bitmap.GetPixelsAsync());
        var info = new SKImageInfo(bitmap.PixelWidth, bitmap.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        return SKImage.FromPixelCopy(info, pixels);
    }
}
#endif

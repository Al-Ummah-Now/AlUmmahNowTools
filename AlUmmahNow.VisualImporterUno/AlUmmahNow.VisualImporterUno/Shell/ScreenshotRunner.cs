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
        // An open popup (suggestions, a dialog) stays whole: the frame ends at an edge below it
        var popupBottom = 0.0;
        if (window.Content?.XamlRoot is { } xamlRoot)
            foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
                if (popup.Child is FrameworkElement child && child.ActualHeight > 0)
                {
                    // A dialog's popup covers the whole window (its dimmed backdrop): what has to stay whole is the dialog's box
                    var box = Named(child, "BackgroundElement") ?? child;
                    popupBottom = Math.Max(popupBottom, box.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, box.ActualWidth, box.ActualHeight)).Bottom);
                }
        // The window never grows past the screen: a page taller than that (Arabic wraps longer) is scrolled on by the
        // missing height and the rows that come into view are added below, so it ends where its English frame does
        var shot = full;
        var shift = popupBottom > 0 ? 0 : await ScrollForMissingAsync(window, h);
        if (shift > 0)
        {
            using var below = await RenderAsync(root);
            var add = Math.Min(below.Height, (int)Math.Round(shift * scale));
            var info = new SKImageInfo(full.Width, full.Height + add, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.DrawImage(full, 0, 0);
            // Only the page's scroll area moved: the rows added below come from it, and everything beside it (the side
            // menu) continues with its own last row, so nothing that stays put is drawn twice
            var (left, right) = PageColumns((UIElement)window.Content!, scale, full.Width);
            surface.Canvas.DrawImage(full, new SKRectI(0, full.Height - 1, full.Width, full.Height), new SKRectI(0, full.Height, full.Width, full.Height + add));
            surface.Canvas.DrawImage(below, new SKRectI(left, below.Height - add, right, below.Height), new SKRectI(left, full.Height, right, full.Height + add));
            shot = surface.Snapshot();
        }
        var end = Environment.GetEnvironmentVariable("TOOLS_SCREENSHOT_CROP") != "0" ? EndAtEdge((UIElement)window.Content!, popupBottom) + shift : null;
        var cropTo = end is { } y ? Math.Min(shot.Height, (int)Math.Ceiling(y * scale)) : shot.Height;
        using var image = cropTo < shot.Height ? shot.Subset(new SKRectI(0, 0, shot.Width, cropTo)) : SKImage.FromBitmap(SKBitmap.FromImage(shot));
        if (!ReferenceEquals(shot, full)) shot.Dispose();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using (var file = File.Create(path)) data.SaveTo(file);
        Serilog.Log.Information("[Shell] Screenshot written to {Path} ({W}x{H})", path, image.Width, image.Height);
        Environment.Exit(0);
    }

    /// <summary>
    /// Where a page's frame ends (logical pixels from the top), or null for the whole window: the lowest edge of a card, a
    /// row or a divider inside the page's scroll area that no text or control crosses, plus a 4px margin, so a frame never
    /// ends half way through a row (AUDIT-2 2026-11-22). Only when that keeps most of the window, and never above
    /// <paramref name="below"/> (the bottom of an open popup).
    /// </summary>
    internal static double? EndAtEdge(UIElement content, double below = 0)
    {
        var height = content.ActualSize.Y;
        if (height <= 0 || Largest<Microsoft.UI.Xaml.Controls.ScrollViewer>(content) is not { } page) return null;
        var leaves = new List<(double Top, double Bottom)>();
        var edges = new List<double>();
        Collect(page, leaves, edges);
        // TOOLS_SCREENSHOT_EDGE=card: only the bottom of a whole card (a direct child of the page's panel), never a row inside one
        var cardsOnly = Environment.GetEnvironmentVariable("TOOLS_SCREENSHOT_EDGE") == "card";
        if (cardsOnly && page.Content is Microsoft.UI.Xaml.Controls.Panel panel)
        {
            edges = panel.Children.OfType<FrameworkElement>()
                .Where((c) => c.Visibility == Visibility.Visible && c.ActualHeight > 0)
                .Select((c) => c.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, c.ActualWidth, c.ActualHeight)).Bottom)
                .ToList();
        }
        const double margin = 4;
        foreach (var edge in edges.Where((e) => e + margin <= height).OrderByDescending((e) => e))
        {
            if (edge < height * (cardsOnly ? 0.05 : 0.5) || edge < below) break;
            var cut = edge + margin;
            if (leaves.Any((l) => l.Top < cut - 0.5 && l.Bottom > edge + 0.5)) continue;
            return cut;
        }
        return null;
    }

    private static FrameworkElement? Named(DependencyObject parent, string name)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } fe && n == name && fe.ActualHeight > 0) return fe;
            if (Named(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>The pixel columns of the page's scroll area (the whole width when there is none).</summary>
    private static (int Left, int Right) PageColumns(UIElement content, double scale, int width)
    {
        if (Largest<Microsoft.UI.Xaml.Controls.ScrollViewer>(content) is not { } page) return (0, width);
        var bounds = page.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, page.ActualWidth, page.ActualHeight));
        var left = Math.Clamp((int)Math.Floor(bounds.Left * scale), 0, width);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right * scale), left, width);
        return right > left ? (left, right) : (0, width);
    }

    /// <summary>
    /// When the screen kept the window shorter than the asked height and the page's scroll area runs to the window's
    /// bottom, scrolls it on by the missing height (or what is left to scroll); returns how far it moved.
    /// </summary>
    private static async Task<double> ScrollForMissingAsync(Window window, int askedHeight)
    {
        if (window.Content is not FrameworkElement content || Largest<Microsoft.UI.Xaml.Controls.ScrollViewer>(content) is not { } page) return 0;
        var missing = askedHeight - content.ActualHeight;
        var pageBottom = page.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, page.ActualHeight)).Y;
        var room = page.ScrollableHeight - page.VerticalOffset;
        if (missing < 1 || room < 1 || Math.Abs(pageBottom - content.ActualHeight) > 1) return 0;
        // Only when something on the page runs past the window: room left to scroll may be bare padding
        var leaves = new List<(double Top, double Bottom)>();
        var edges = new List<double>();
        Collect(page, leaves, edges);
        var lowest = leaves.Select((l) => l.Bottom).Concat(edges).DefaultIfEmpty(0).Max();
        if (lowest <= content.ActualHeight + 0.5) return 0;
        var from = page.VerticalOffset;
        page.ChangeView(null, from + Math.Min(missing, room), null, true);
        await Task.Delay(1000);
        return page.VerticalOffset - from;
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

#if !WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Screenshots for review (<c>TOOLS_SCREENSHOT=out.png</c>, with <c>TOOLS_DEMO=1</c>): sizes the
/// window (<c>TOOLS_SCREENSHOT_SIZE=1280x800</c>), waits for the screen to settle, renders the window's
/// content to a PNG and exits. No screen-recording permission needed.
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
        using var image = await RenderAsync(root);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using (var file = File.Create(path)) data.SaveTo(file);
        Serilog.Log.Information("[Shell] Screenshot written to {Path} ({W}x{H})", path, image.Width, image.Height);
        Environment.Exit(0);
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

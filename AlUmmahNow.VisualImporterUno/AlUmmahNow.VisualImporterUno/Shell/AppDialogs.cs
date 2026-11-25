using AlUmmahNow.ToolsCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>Every dialog of the app is shown here: one at a time, on the window's thread, and not over a window that is going.</summary>
internal static class AppDialogs
{
    private static readonly DialogGate Gate = new();

    /// <param name="build">Makes it, on the window's thread</param>
    /// <param name="closeAfter">Closes it by itself</param>
    /// <returns>Null: it was not shown (another one is open, or there is no window to show it in)</returns>
    public static async Task<ContentDialogResult?> ShowAsync(Func<XamlRoot, ContentDialog> build, DialogKind kind, XamlRoot? root = null, TimeSpan? closeAfter = null)
    {
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher = null;
        try { dispatcher = App.Current?.MainWindow?.DispatcherQueue; }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Dialog] There is no window to show it in"); }
        if (dispatcher is null) return null;
        if (dispatcher.HasThreadAccess) return await HereAsync().ConfigureAwait(true);
        var answer = new TaskCompletionSource<ContentDialogResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = dispatcher.TryEnqueue(async () => answer.TrySetResult(await HereAsync().ConfigureAwait(true)));
        return queued ? await answer.Task.ConfigureAwait(false) : null;

        async Task<ContentDialogResult?> HereAsync()
        {
            try
            {
                ContentDialog? dialog = null;
                var (shown, result) = await Gate.ShowAsync<ContentDialogResult?>(kind, async () =>
                {
                    if ((root ?? App.Current?.MainWindow?.Content?.XamlRoot) is not { } at) return null;
                    dialog = build(at);
                    if (closeAfter is { } time) _ = CloseAfterAsync(dialog, time);
                    return await dialog.ShowAsync();
                }, () => dialog?.Hide()).ConfigureAwait(true);
                return shown ? result : null;
            }
            catch (Exception ex)
            {
                // The window is closing: nobody can answer
                Serilog.Log.Warning(ex, "[Dialog] Could not be shown");
                return null;
            }
        }
    }

    private static async Task CloseAfterAsync(ContentDialog dialog, TimeSpan time)
    {
        await Task.Delay(time).ConfigureAwait(true);
        dialog.Hide();
    }

    /// <summary>The control a keyboard goes on from when the one it was on has gone (with what was removed).</summary>
    public static async void KeepFocus(Control next)
    {
        // The list follows what was removed a moment later
        await Task.Delay(300).ConfigureAwait(true);
        if (next.XamlRoot is not { } root) return;
        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is Control { IsLoaded: true, IsEnabled: true, ActualWidth: > 0 } now && now is not ScrollViewer && Shown(now)) return;
        next.Focus(FocusState.Programmatic);
    }

    private static bool Shown(DependencyObject? element)
    {
        for (; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
            if (element is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }
}

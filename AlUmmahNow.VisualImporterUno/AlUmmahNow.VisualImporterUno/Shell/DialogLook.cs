using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The app's dialog buttons: the default one filled, the others outlined, all the same height. Uno puts its own,
/// shorter accent style on the platform's default button over any style given, so the dialog has no platform default:
/// the filled style marks it, and Enter presses it (from a one-line box too) as it would the platform's.
/// </summary>
internal static class DialogLook
{
    public static void Apply(ContentDialog dialog)
    {
        var resources = Application.Current.Resources;
        var chosen = dialog.DefaultButton;
        Style For(ContentDialogButton button) => (Style)resources[chosen == button ? "ToolsDialogDefaultButton" : "ToolsDialogButton"];
        dialog.PrimaryButtonStyle = For(ContentDialogButton.Primary);
        dialog.SecondaryButtonStyle = For(ContentDialogButton.Secondary);
        dialog.CloseButtonStyle = For(ContentDialogButton.Close);
        if (chosen == ContentDialogButton.None) return;
        dialog.DefaultButton = ContentDialogButton.None;
        var name = chosen switch { ContentDialogButton.Primary => "PrimaryButton", ContentDialogButton.Secondary => "SecondaryButton", _ => "CloseButton" };
        dialog.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != VirtualKey.Enter || e.OriginalSource is TextBox { AcceptsReturn: true } or Button) return;
            if (FindChild(dialog, name) is Button button && button.IsEnabled && new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) is Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke)
            {
                invoke.Invoke();
                e.Handled = true;
            }
        }), true);
    }

    private static DependencyObject? FindChild(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name) return child;
            if (FindChild(child, name) is { } found) return found;
        }
        return null;
    }
}

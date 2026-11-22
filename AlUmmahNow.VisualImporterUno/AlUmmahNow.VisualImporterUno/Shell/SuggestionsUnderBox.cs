using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Keeps an AutoSuggestBox's list under the box at the box's width. In a right-to-left window Uno's Skia popup places the
/// list from the mirrored edge, so it opens beside the box and runs off the window; once it opens, its offset is
/// corrected by the distance between the list and the box.
/// </summary>
public static class SuggestionsUnderBox
{
    public static void Attach(AutoSuggestBox box)
    {
        box.Loaded += (_, _) =>
        {
            if (Find<Popup>(box) is not { } popup) return;
            popup.Opened += (_, _) => box.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Align(box, popup));
        };
    }

    private static void Align(AutoSuggestBox box, Popup popup)
    {
        if (popup.Child is not FrameworkElement list) return;
        if (list.Width != box.ActualWidth) list.Width = box.ActualWidth;
        // Its rows follow the window, as the chosen channel's card does
        if (list.FlowDirection != box.FlowDirection) list.FlowDirection = box.FlowDirection;
        // Right to left, the popup's origin is the box's right edge but the list is drawn from its left edge there
        var offset = box.FlowDirection == FlowDirection.RightToLeft ? -box.ActualWidth : 0;
        if (popup.HorizontalOffset != offset) popup.HorizontalOffset = offset;
    }

    private static T? Find<T>(DependencyObject parent) where T : class
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}

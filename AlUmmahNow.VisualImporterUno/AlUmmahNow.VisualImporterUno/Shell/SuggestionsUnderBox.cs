using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Keeps an AutoSuggestBox's list under the box at the box's width. In a right-to-left window Uno's Skia popup places the
/// list from the mirrored edge, so it opens beside the box and runs off the window; once it opens, its offset is
/// corrected by the distance between the list and the box. The list is then lined up with the box's outer edges and
/// drawn opaque to its edge (FlyoutPresenterBackground, the theme's flyout fill, and FanousDividerBrush), so nothing underneath shows beside it.
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
        // Opaque fill and edge: the default edge is see-through, so a border underneath showed along it
        var fill = Application.Current.Resources.TryGetValue("FlyoutPresenterBackground", out var f) ? f as Brush : null;
        var edge = Application.Current.Resources.TryGetValue("FanousDividerBrush", out var e) ? e as Brush : null;
        if (list is Border border)
        {
            if (fill is not null) border.Background = fill;
            if (edge is not null) border.BorderBrush = edge;
        }
        else if (list is Control control)
        {
            if (fill is not null) control.Background = fill;
            if (edge is not null) control.BorderBrush = edge;
        }
        // The template places the list from the text field inside the box, a pixel or two in from its border
        box.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => LineUp(box, popup, list, firstTry: true));
    }

    /// <summary>Moves the list by what is left between its left edge and the box's (measured on screen, both directions).</summary>
    private static void LineUp(AutoSuggestBox box, Popup popup, FrameworkElement list, bool firstTry)
    {
        if (!popup.IsOpen) return;
        var gap = LeftEdge(box) - LeftEdge(list);
        if (Math.Abs(gap) < 0.5) return;
        if (firstTry)
        {
            popup.HorizontalOffset += gap;
            box.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => LineUp(box, popup, list, firstTry: false));
        }
        else
        {
            // A mirrored popup moves the list the other way, so the first move doubled the gap: go back by it
            popup.HorizontalOffset -= gap;
        }
    }

    private static double LeftEdge(FrameworkElement element) =>
        element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)).Left;

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

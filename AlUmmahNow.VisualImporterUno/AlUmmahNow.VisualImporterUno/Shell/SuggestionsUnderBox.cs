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
/// corrected by the distance between the list and the box. Uno places an open list anew at every change of the text or the rows:
/// the corrected offset is put back at once. The list is then lined up with the box's outer edges and
/// drawn opaque to its edge (FlyoutPresenterBackground, the theme's flyout fill, and FanousDividerBrush), so nothing underneath shows beside it.
/// </summary>
public static class SuggestionsUnderBox
{
    public static void Attach(AutoSuggestBox box)
    {
        Popup? attached = null;
        long token = 0;
        var place = new Place();
        EventHandler<object> opened = (_, _) => { if (attached is { } popup) Later(box, popup, place); };
        void Detach()
        {
            if (attached is not { } popup) return;
            popup.Opened -= opened;
            popup.UnregisterPropertyChangedCallback(Popup.HorizontalOffsetProperty, token);
            attached = null;
        }
        // In a folded part of a page a box is loaded before it has its parts: they are there once it has a size or the focus
        void AttachNow()
        {
            if (attached is not null || Find<Popup>(box) is not { } popup) return;
            attached = popup;
            place.Offset = null;
            // Its rows follow the window, as the chosen channel's card does; set before any row is drawn (turned round later, Uno
            // now and then drew a left-to-right line of a row, the email, mirrored)
            if (popup.Child is FrameworkElement rows) rows.FlowDirection = box.FlowDirection;
            popup.Opened += opened;
            token = popup.RegisterPropertyChangedCallback(Popup.HorizontalOffsetProperty, (_, _) =>
            {
                if (place.Moving || !popup.IsOpen) return;
                if (place.Offset is { } offset && place.Width == box.ActualWidth) Move(popup, place, offset);
                Later(box, popup, place);
            });
        }
        box.Loaded += (_, _) =>
        {
            Detach();
            AttachNow();
        };
        box.GotFocus += (_, _) => AttachNow();
        box.Unloaded += (_, _) => Detach();
        // The list is placed where the box was when it opened
        box.SizeChanged += (_, e) =>
        {
            AttachNow();
            if (e.PreviousSize.Width > 0 && e.NewSize.Width != e.PreviousSize.Width) box.IsSuggestionListOpen = false;
        };
    }

    /// <summary>Where the list was last lined up with the box.</summary>
    private sealed class Place
    {
        public double? Offset;
        public double Width;
        public bool Moving;
        public bool Waiting;
    }

    private static void Move(Popup popup, Place place, double offset)
    {
        place.Moving = true;
        popup.HorizontalOffset = offset;
        place.Moving = false;
    }

    private static void Later(AutoSuggestBox box, Popup popup, Place place)
    {
        if (place.Waiting) return;
        place.Waiting = true;
        box.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            place.Waiting = false;
            Align(box, popup, place);
        });
    }

    private static void Align(AutoSuggestBox box, Popup popup, Place place)
    {
        if (!popup.IsOpen || popup.Child is not FrameworkElement list) return;
        if (list.Width != box.ActualWidth) list.Width = box.ActualWidth;
        if (list.FlowDirection != box.FlowDirection) list.FlowDirection = box.FlowDirection;
        // Right to left, the popup's origin is the box's right edge but the list is drawn from its left edge there
        var offset = box.FlowDirection == FlowDirection.RightToLeft ? -box.ActualWidth : 0;
        if (place.Offset is null || place.Width != box.ActualWidth) Move(popup, place, offset);
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
        box.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => LineUp(box, popup, place, list, firstTry: true));
    }

    /// <summary>Moves the list by what is left between its left edge and the box's (measured on screen, both directions).</summary>
    private static void LineUp(AutoSuggestBox box, Popup popup, Place place, FrameworkElement list, bool firstTry)
    {
        if (!popup.IsOpen) return;
        var gap = LeftEdge(box) - LeftEdge(list);
        if (Math.Abs(gap) >= 0.5)
        {
            if (firstTry)
            {
                Move(popup, place, popup.HorizontalOffset + gap);
                box.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => LineUp(box, popup, place, list, firstTry: false));
                return;
            }
            // A mirrored popup moves the list the other way, so the first move doubled the gap: go back by it
            Move(popup, place, popup.HorizontalOffset - gap);
        }
        (place.Offset, place.Width) = (popup.HorizontalOffset, box.ActualWidth);
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

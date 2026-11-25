using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// A row of buttons, each as wide as its words, that goes on in a next row where the width ends. Hidden ones take no place.
/// Right-to-left mirrors the order through the usual FlowDirection.
/// </summary>
public sealed class WrapRow : Panel
{
    public double Spacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Place(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize.Width, arrange: true);
        return finalSize;
    }

    private Size Place(double width, bool arrange)
    {
        double x = 0, y = 0, row = 0, widest = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            // Half a pixel of rounding never breaks a row that fits
            if (x > 0 && x + size.Width > width + 0.5)
            {
                x = 0;
                y += row + Spacing;
                row = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, size.Width, size.Height));
            widest = Math.Max(widest, x + size.Width);
            row = Math.Max(row, size.Height);
            x += size.Width + Spacing;
        }
        return new Size(widest, y + row);
    }
}

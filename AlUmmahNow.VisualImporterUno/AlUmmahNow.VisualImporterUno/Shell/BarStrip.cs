using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Lays a chart's days side by side in equal slots that fill <see cref="FillWidth"/>. When the days don't fit at
/// <see cref="MinSlotWidth"/> each, the strip grows past it and its ScrollViewer scrolls.
/// </summary>
/// <remarks>
/// A panel inside a horizontal ScrollViewer is measured with unlimited width, so the width to fill is handed in by the
/// page from the ScrollViewer's own size.
/// </remarks>
public sealed partial class BarStrip : Panel
{
    public static readonly DependencyProperty FillWidthProperty =
        DependencyProperty.Register(nameof(FillWidth), typeof(double), typeof(BarStrip), new PropertyMetadata(0d, (d, _) => ((BarStrip)d).InvalidateMeasure()));

    public static readonly DependencyProperty MinSlotWidthProperty =
        DependencyProperty.Register(nameof(MinSlotWidth), typeof(double), typeof(BarStrip), new PropertyMetadata(44d, (d, _) => ((BarStrip)d).InvalidateMeasure()));

    public double FillWidth { get => (double)GetValue(FillWidthProperty); set => SetValue(FillWidthProperty, value); }

    public double MinSlotWidth { get => (double)GetValue(MinSlotWidthProperty); set => SetValue(MinSlotWidthProperty, value); }

    private double SlotWidth => Children.Count == 0 ? 0 : Math.Max(MinSlotWidth, FillWidth / Children.Count);

    protected override Size MeasureOverride(Size availableSize)
    {
        var slot = SlotWidth;
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(slot, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(slot * Children.Count, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var slot = SlotWidth;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(i * slot, 0, slot, finalSize.Height));
        }
        return new Size(slot * Children.Count, finalSize.Height);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Lays its children out in as many equal columns as fit at <see cref="MinItemWidth"/>, up to <see cref="MaxColumns"/>,
/// and stacks them in one column on a narrow window. Rows take the height of their tallest tile, so cards line up.
/// Right-to-left mirrors the order through the usual FlowDirection.
/// </summary>
public sealed class TileGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty =
        DependencyProperty.Register(nameof(MinItemWidth), typeof(double), typeof(TileGrid), new PropertyMetadata(280d, OnLayoutChanged));

    public static readonly DependencyProperty MaxColumnsProperty =
        DependencyProperty.Register(nameof(MaxColumns), typeof(int), typeof(TileGrid), new PropertyMetadata(3, OnLayoutChanged));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(TileGrid), new PropertyMetadata(16d, OnLayoutChanged));

    /// <summary>A child that takes every column of its row.</summary>
    public static readonly DependencyProperty IsFullWidthProperty =
        DependencyProperty.RegisterAttached("IsFullWidth", typeof(bool), typeof(TileGrid), new PropertyMetadata(false, OnLayoutChanged));

    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    public static bool GetIsFullWidth(DependencyObject element) => (bool)element.GetValue(IsFullWidthProperty);
    public static void SetIsFullWidth(DependencyObject element, bool value) => element.SetValue(IsFullWidthProperty, value);

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TileGrid grid) grid.InvalidateMeasure();
        else if (d is FrameworkElement { Parent: TileGrid parent }) parent.InvalidateMeasure();
    }

    private int ColumnsFor(double width)
    {
        if (double.IsInfinity(width) || width <= 0) return 1;
        var columns = (int)((width + Spacing) / (MinItemWidth + Spacing));
        return Math.Clamp(columns, 1, Math.Max(1, MaxColumns));
    }

    private IEnumerable<List<UIElement>> Rows(int columns)
    {
        var row = new List<UIElement>();
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            if (GetIsFullWidth(child))
            {
                if (row.Count > 0) yield return row;
                yield return new List<UIElement> { child };
                row = new List<UIElement>();
                continue;
            }
            row.Add(child);
            if (row.Count == columns)
            {
                yield return row;
                row = new List<UIElement>();
            }
        }
        if (row.Count > 0) yield return row;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = ColumnsFor(availableSize.Width);
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth * columns : availableSize.Width;
        var cell = (width - Spacing * (columns - 1)) / columns;
        double height = 0;
        var first = true;
        foreach (var row in Rows(columns))
        {
            double rowHeight = 0;
            foreach (var child in row)
            {
                var childWidth = row.Count == 1 && GetIsFullWidth(child) ? width : cell;
                child.Measure(new Size(childWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }
            height += rowHeight + (first ? 0 : Spacing);
            first = false;
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnsFor(finalSize.Width);
        var cell = (finalSize.Width - Spacing * (columns - 1)) / columns;
        double y = 0;
        foreach (var row in Rows(columns))
        {
            var rowHeight = row.Max(c => c.DesiredSize.Height);
            double x = 0;
            foreach (var child in row)
            {
                var childWidth = row.Count == 1 && GetIsFullWidth(child) ? finalSize.Width : cell;
                child.Arrange(new Rect(x, y, childWidth, rowHeight));
                x += childWidth + Spacing;
            }
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}

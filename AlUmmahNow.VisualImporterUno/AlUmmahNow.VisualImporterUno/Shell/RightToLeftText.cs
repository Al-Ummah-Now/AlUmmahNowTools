using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// A title that may be Arabic, wrapping correctly in either interface language. In the Arabic window, and for text that
/// is not Arabic, it is one wrapping TextBlock. For an Arabic title in the English window, Uno's Skia text lays the line
/// out left to right in pieces split at punctuation, and storing the pieces in reverse (<see cref="Loc.RightToLeftLine"/>)
/// only holds on one line: once the title wraps, its halves land on the wrong lines. Here each word and each mark is its own
/// block, laid out from the right edge and wrapped onto the next line as an Arabic line is, so any width reads in order.
/// </summary>
public sealed partial class RightToLeftText : Panel
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(RightToLeftText), new PropertyMetadata("", (d, _) => ((RightToLeftText)d).Rebuild()));

    public static readonly DependencyProperty TextStyleProperty =
        DependencyProperty.Register(nameof(TextStyle), typeof(Style), typeof(RightToLeftText), new PropertyMetadata(null, (d, _) => ((RightToLeftText)d).Rebuild()));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(RightToLeftText), new PropertyMetadata(null, (d, _) => ((RightToLeftText)d).Rebuild()));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Style? TextStyle { get => (Style?)GetValue(TextStyleProperty); set => SetValue(TextStyleProperty, value); }
    public Brush? Foreground { get => (Brush?)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    // Words, and the marks between them (Arabic and Latin punctuation), as separate tokens
    private static readonly Regex Tokens = new(@"[^\s:;!?.,،؛()\[\]\-–—|/]+|[:;!?.,،؛()\[\]\-–—|/]+");
    private static readonly Regex Mark = new(@"^[:;!?.,،؛()\[\]\-–—|/]+$");

    private bool _flowing;
    private double _widest;

    private void Rebuild()
    {
        Children.Clear();
        var text = Text ?? "";
        _flowing = !Loc.IsArabic && LooksArabic(text);
#if !HAS_UNO
        _flowing = false;
#endif
        if (!_flowing)
        {
            // A title keeps its last two words together, except a Latin one in the Arabic window, where a no-break space
            // moves a trailing colon to the wrong side
            Children.Add(Block(LooksArabic(text) || !Loc.IsArabic ? Loc.KeepLastWords(text) : text, wrap: true));
            return;
        }
        foreach (Match m in Tokens.Matches(text))
        {
            var token = m.Value;
            var block = Block(IsArabicDigits(token) ? new string(token.Reverse().ToArray()) : token, wrap: false);
            block.Tag = Mark.IsMatch(token) ? "mark" : "word";
            Children.Add(block);
        }
    }

    private TextBlock Block(string text, bool wrap)
    {
        var block = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (TextStyle is not null) block.Style = TextStyle;
        if (Foreground is not null) block.Foreground = Foreground;
        return block;
    }

    private static bool LooksArabic(string text) => text.FirstOrDefault(char.IsLetter) is >= '؀' and <= 'ۿ';

    private static bool IsArabicDigits(string token) => token.Length > 1 && token.All(c => c is >= '٠' and <= '٩');

    private double Gap => (Children.FirstOrDefault() as TextBlock)?.FontSize * 0.28 ?? 4;

    private bool IsMark(int i) => (Children[i] as FrameworkElement)?.Tag as string == "mark";

    // A word is followed by a space, except before a mark, which sits against the word it follows (on its left)
    private double GapBefore(int i) => i == 0 || IsMark(i) ? 0 : Gap;

    private double Width(int from, int to)
    {
        double width = 0;
        for (var i = from; i < to; i++) width += (i > from ? GapBefore(i) : 0) + Children[i].DesiredSize.Width;
        return width;
    }

    /// <summary>
    /// Where each line starts, filling each line in turn. A last line of one word takes the word before it too (and the
    /// marks between), so a title never ends on a lone word.
    /// </summary>
    private List<int> LineStarts(double max)
    {
        var starts = new List<int> { 0 };
        for (var i = 1; i < Children.Count; i++)
        {
            if (Width(starts[^1], i + 1) > max && !IsMark(i)) starts.Add(i);
        }
        if (starts.Count > 1)
        {
            var last = starts[^1];
            var words = Enumerable.Range(last, Children.Count - last).Count(i => !IsMark(i));
            var before = starts[^2];
            // The word to move down: the last word of the line above, with any marks after it
            var move = last - 1;
            while (move > before && IsMark(move)) move--;
            if (words == 1 && move > before && Width(move, Children.Count) <= max) starts[^1] = move;
        }
        return starts;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(_flowing ? new Size(double.PositiveInfinity, double.PositiveInfinity) : availableSize);
        if (!_flowing) return Children.Count > 0 ? Children[0].DesiredSize : new Size();
        if (Children.Count == 0) return new Size();
        var max = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        var starts = LineStarts(max);
        double height = 0, widest = 0;
        for (var l = 0; l < starts.Count; l++)
        {
            var end = l + 1 < starts.Count ? starts[l + 1] : Children.Count;
            widest = Math.Max(widest, Width(starts[l], end));
            height += Enumerable.Range(starts[l], end - starts[l]).Max(i => Children[i].DesiredSize.Height);
        }
        _widest = widest;
        return new Size(double.IsInfinity(availableSize.Width) ? widest : Math.Min(widest, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!_flowing)
        {
            if (Children.Count > 0) Children[0].Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            return finalSize;
        }
        if (Children.Count == 0) return finalSize;
        // Lines fill from the right edge of the block, as Arabic text does; the block keeps the row's start, so a short
        // title sits where an English one would
        var right = Math.Min(_widest, finalSize.Width);
        var starts = LineStarts(finalSize.Width);
        double y = 0;
        for (var l = 0; l < starts.Count; l++)
        {
            var end = l + 1 < starts.Count ? starts[l + 1] : Children.Count;
            double x = right, lineHeight = 0;
            for (var i = starts[l]; i < end; i++)
            {
                var child = Children[i];
                x -= (i > starts[l] ? GapBefore(i) : 0) + child.DesiredSize.Width;
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
                lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
            }
            y += lineHeight;
        }
        return finalSize;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// A folder or file path that reads left to right on English and Arabic pages alike.
/// </summary>
/// <remarks>
/// Uno's Skia text takes a line's direction from the page, not from the TextBlock, so on an Arabic page the "/" that
/// starts "/Volumes/Media" is drawn after the path, and a direction mark doesn't hold it in place. The marks before the
/// first letter or digit therefore get their own TextBlock, placed first in a left-to-right grid.
/// </remarks>
public sealed partial class PathText : Grid
{
    public static readonly DependencyProperty PathProperty =
        DependencyProperty.Register(nameof(Path), typeof(string), typeof(PathText), new PropertyMetadata(null, (d, _) => ((PathText)d).Update()));

    private readonly TextBlock _lead = new();
    private readonly TextBlock _rest = new() { TextTrimming = TextTrimming.CharacterEllipsis };

    public PathText()
    {
        FlowDirection = FlowDirection.LeftToRight;
        HorizontalAlignment = HorizontalAlignment.Left;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (Application.Current.Resources.TryGetValue("ToolsMuted", out var style) && style is Style muted)
        {
            _lead.Style = muted;
            _rest.Style = muted;
        }
        SetColumn(_rest, 1);
        Children.Add(_lead);
        Children.Add(_rest);
    }

    /// <summary>A long path goes on under itself instead of ending in dots, and can be selected.</summary>
    public bool Wrap
    {
        get => _rest.TextWrapping != TextWrapping.NoWrap;
        set
        {
            _rest.TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            _rest.TextTrimming = value ? TextTrimming.None : TextTrimming.CharacterEllipsis;
            _rest.IsTextSelectionEnabled = value;
        }
    }

    /// <summary>The text's style, by its key; <c>ToolsMuted</c> unless given.</summary>
    public string TextStyle
    {
        get => _textStyle;
        set
        {
            _textStyle = value;
            if (!Application.Current.Resources.TryGetValue(value, out var style) || style is not Style given) return;
            // The style's own wrapping must not undo Wrap
            var wrap = Wrap;
            _lead.Style = given;
            _rest.Style = given;
            Wrap = wrap;
        }
    }

    private string _textStyle = "ToolsMuted";

    public string? Path
    {
        get => (string?)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    private void Update()
    {
        var path = Path ?? string.Empty;
        var start = 0;
        while (start < path.Length && !char.IsLetterOrDigit(path[start])) start++;
        _lead.Text = path[..start];
        _rest.Text = path[start..];
    }
}

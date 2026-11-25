using AlUmmahNow.ToolsCore.Import;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// A reusable English + Arabic input pair for dialogs and forms, matching admin-web's LocalizedFieldPair:
/// English (LTR) and العربية (RTL, Arabic font) side by side, each with a Translate button calling
/// admin-api <c>POST /api/translate/fields</c>, inline validation errors, editable result, and Undo when
/// replacing existing text.
/// </summary>
internal sealed class LocalizedFieldPair
{
    public delegate Task<FieldTranslateItem> TranslateDelegate(string from, string to, string text, string field, CancellationToken ct);

    /// <summary>The pair's width under which its two languages are stacked.</summary>
    private const double StackBelow = 420;

    /// <summary>The two fonts differ in line height: the same header and box heights keep the two columns level.</summary>
    private const double HeaderHeight = 20;
    private const double BoxHeight = 36;

    private readonly string _field;
    private readonly int _maxLength;
    private readonly TranslateDelegate _translate;
    private readonly TextBox _enBox;
    private readonly TextBox _arBox;
    private readonly TextBlock _enError;
    private readonly TextBlock _arError;
    private readonly Button _enTranslateBtn;
    private readonly Button _arTranslateBtn;
    private readonly TextBlock _statusText;
    private readonly StackPanel _statusDetailRow;
    private readonly TextBlock _statusDetailLabel;
    private readonly TextBlock _statusDetailValue;
    private readonly Button _undoBtn;

    private bool _busy;
    private (string Lang, string Previous, string Applied)? _undo;

    public LocalizedFieldPair(
        string label,
        string field,
        TranslateDelegate translate,
        bool required = true,
        bool multiline = false,
        int maxLength = 100)
    {
        _field = field;
        _maxLength = maxLength;
        _translate = translate;

        var res = Application.Current.Resources;
        var labelStyle = (Style)res["ToolsLabel"];
        var mutedStyle = (Style)res["ToolsMuted"];
        var errorBrush = (Brush)res["FanousErrorTextBrush"];
        var mutedBrush = (Brush)res["FanousTextMutedBrush"];
        var secondaryBtnStyle = (Style)res["ToolsSecondaryButton"];
        var arabicFont = ShellState.TextFontFor("ar");
        var uiFlow = Loc.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var uiFont = Loc.IsArabic ? arabicFont : null;

        _enBox = new TextBox
        {
            FlowDirection = FlowDirection.LeftToRight,
            TextAlignment = TextAlignment.Left,
            Language = "en",
            MaxLength = maxLength,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        _enBox.MinHeight = multiline ? 76 : BoxHeight;
        AutomationProperties.SetName(_enBox, Loc.Format("Localized_FieldIn", label, Loc.Get("Localized_LangEnglish")));

        _arBox = new TextBox
        {
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right,
            Language = "ar",
            MaxLength = maxLength,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        if (arabicFont is not null) _arBox.FontFamily = arabicFont;
        _arBox.MinHeight = multiline ? 76 : BoxHeight;
        AutomationProperties.SetName(_arBox, Loc.Format("Localized_FieldIn", label, Loc.Get("Localized_LangArabic")));

        _enError = new TextBlock
        {
            Style = mutedStyle,
            Foreground = errorBrush,
            FlowDirection = uiFlow,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        _arError = new TextBlock
        {
            Style = mutedStyle,
            Foreground = errorBrush,
            FlowDirection = uiFlow,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        if (uiFont is not null)
        {
            _enError.FontFamily = uiFont;
            _arError.FontFamily = uiFont;
        }

        _enTranslateBtn = MakeTranslateButton(secondaryBtnStyle, Loc.PopupLine(Loc.Get("Localized_TranslateToAr")), uiFont, HorizontalAlignment.Left);
        _arTranslateBtn = MakeTranslateButton(secondaryBtnStyle, Loc.PopupLine(Loc.Get("Localized_TranslateToEn")), uiFont, HorizontalAlignment.Right);
        _enTranslateBtn.Click += async (_, _) => await TranslateAsync("en", "ar");
        _arTranslateBtn.Click += async (_, _) => await TranslateAsync("ar", "en");

        _enBox.TextChanged += (_, _) =>
        {
            // Only clear Undo on user edits; Uno Skia can dispatch TextChanged asynchronously after programmatic sets
            if (_undo is { Lang: "en" } u && !string.Equals(_enBox.Text ?? "", u.Applied, StringComparison.Ordinal)) ClearUndo();
            UpdateButtons();
        };
        _arBox.TextChanged += (_, _) =>
        {
            if (_undo is { Lang: "ar" } u && !string.Equals(_arBox.Text ?? "", u.Applied, StringComparison.Ordinal)) ClearUndo();
            UpdateButtons();
        };

        _statusText = new TextBlock
        {
            Style = mutedStyle,
            Foreground = mutedBrush,
            FlowDirection = uiFlow,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        _statusDetailLabel = new TextBlock
        {
            Style = mutedStyle,
            Foreground = errorBrush,
            FlowDirection = uiFlow,
            TextWrapping = TextWrapping.NoWrap,
        };
        _statusDetailValue = new TextBlock
        {
            Style = mutedStyle,
            Foreground = errorBrush,
            FlowDirection = FlowDirection.LeftToRight,
            TextWrapping = TextWrapping.Wrap,
        };
        if (uiFont is not null)
        {
            _statusText.FontFamily = uiFont;
            _statusDetailLabel.FontFamily = uiFont;
        }
        _statusDetailRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            FlowDirection = uiFlow,
            Visibility = Visibility.Collapsed,
        };
        _statusDetailRow.Children.Add(_statusDetailLabel);
        _statusDetailRow.Children.Add(_statusDetailValue);

        var undoLabel = new TextBlock { Text = Loc.PopupLine(Loc.Get("Localized_Undo")), FontSize = 12 };
        if (uiFont is not null) undoLabel.FontFamily = uiFont;
        _undoBtn = new Button
        {
            Style = secondaryBtnStyle,
            MinHeight = 30,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 12,
            FlowDirection = uiFlow,
            VerticalAlignment = VerticalAlignment.Top,
            Content = undoLabel,
            Visibility = Visibility.Collapsed,
        };
        _undoBtn.Click += (_, _) => ApplyUndo();

        var suffix = required ? " *" : "";
        var enHeader = new TextBlock
        {
            Text = Loc.PopupLine(Loc.Get("Localized_English") + suffix),
            Style = mutedStyle,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Height = HeaderHeight,
        };
        var arName = new TextBlock { Text = Loc.PopupLine(Loc.Get("Localized_Arabic")), Style = mutedStyle };
        if (arabicFont is not null) arName.FontFamily = arabicFont;
        // The star is its own block, so Uno's text order can't move it: it follows the Arabic word, on its left
        var arHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 4,
            Height = HeaderHeight,
        };
        if (required) arHeader.Children.Add(new TextBlock { Text = "*", Style = mutedStyle });
        arHeader.Children.Add(arName);

        var enCol = new StackPanel { Spacing = 6 };
        enCol.Children.Add(enHeader);
        enCol.Children.Add(_enBox);
        enCol.Children.Add(_enError);
        enCol.Children.Add(_enTranslateBtn);

        var arCol = new StackPanel { Spacing = 6 };
        arCol.Children.Add(arHeader);
        arCol.Children.Add(_arBox);
        arCol.Children.Add(_arError);
        arCol.Children.Add(_arTranslateBtn);

        var grid = new Grid { ColumnSpacing = 12, FlowDirection = FlowDirection.LeftToRight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // English on the left (start in LTR), Arabic on the right (start in RTL)
        Grid.SetColumn(enCol, 0);
        Grid.SetColumn(arCol, 1);
        grid.Children.Add(enCol);
        grid.Children.Add(arCol);
        // Too narrow for two columns: Arabic goes below English
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.SizeChanged += (_, e) =>
        {
            var stacked = e.NewSize.Width < StackBelow;
            if (Grid.GetRow(arCol) == (stacked ? 1 : 0)) return;
            Grid.SetRow(arCol, stacked ? 1 : 0);
            Grid.SetColumn(arCol, stacked ? 0 : 1);
            Grid.SetColumnSpan(enCol, stacked ? 2 : 1);
            Grid.SetColumnSpan(arCol, stacked ? 2 : 1);
            grid.RowSpacing = stacked ? 10 : 0;
        };

        // Constrained Grid so long status/error text wraps within the dialog width instead of clipping
        var statusTextCol = new StackPanel { Spacing = 2 };
        statusTextCol.Children.Add(_statusText);
        statusTextCol.Children.Add(_statusDetailRow);
        var statusRow = new Grid { ColumnSpacing = 8, FlowDirection = uiFlow };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(statusTextCol, 0);
        Grid.SetColumn(_undoBtn, 1);
        statusRow.Children.Add(statusTextCol);
        statusRow.Children.Add(_undoBtn);

        var root = new StackPanel { Spacing = 6 };
        root.Children.Add(new TextBlock { Text = Loc.PopupLine(label), Style = labelStyle });
        root.Children.Add(grid);
        root.Children.Add(statusRow);
        Element = root;

        UpdateButtons();
    }

    public UIElement Element { get; }
    public string English => _enBox.Text ?? "";
    public string Arabic => _arBox.Text ?? "";

    public void SetValues(string? en, string? ar)
    {
        _enBox.Text = en ?? "";
        _arBox.Text = ar ?? "";
        UpdateButtons();
    }

    public void SetErrors(string? enError, string? arError)
    {
        SetErrorBlock(_enError, enError);
        SetErrorBlock(_arError, arError);
    }

    /// <summary>Screenshot/demo helper: renders the Translate busy state (disabled buttons + "Translating…").</summary>
    internal void SetDemoWorking()
    {
        _busy = true;
        ClearUndo();
        UpdateButtons();
        SetStatus(Loc.Get("Localized_Translating"));
    }

    /// <summary>Screenshot/demo helper: renders a status or error line (with optional separate detail label + LTR value).</summary>
    internal void SetDemoStatus(string message, bool isError = false, string? detailLabel = null, string? detailValue = null)
    {
        _busy = false;
        ClearUndo();
        UpdateButtons();
        SetStatus(message, isError, detailLabel, detailValue);
    }

    /// <summary>Screenshot/demo helper: renders a post-translation replacement status with the Undo button visible.</summary>
    internal void SetDemoUndo(string targetLang, string previousText, string statusMessage)
    {
        _busy = false;
        var applied = targetLang == "en" ? English : Arabic;
        _undo = (targetLang, previousText, applied);
        _undoBtn.Visibility = Visibility.Visible;
        UpdateButtons();
        SetStatus(statusMessage);
    }

    internal static string TruncateAtCodePoint(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var len = maxLength;
        if (len > 0 && char.IsHighSurrogate(text[len - 1])) len--;
        return text[..len];
    }

    private static void SetErrorBlock(TextBlock block, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            block.Text = "";
            block.Visibility = Visibility.Collapsed;
        }
        else
        {
            block.Text = message;
            block.Visibility = Visibility.Visible;
        }
    }

    private static Button MakeTranslateButton(Style secondaryStyle, string text, FontFamily? font, HorizontalAlignment alignment)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            FlowDirection = Loc.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        row.Children.Add(new FontIcon { Glyph = "\uE8C1", FontSize = 13 });
        var tb = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.NoWrap };
        if (font is not null) tb.FontFamily = font;
        row.Children.Add(tb);
        return new Button
        {
            Style = secondaryStyle,
            MinHeight = 32,
            Padding = new Thickness(10, 0, 10, 0),
            Content = row,
            HorizontalAlignment = alignment,
        };
    }

    private void UpdateButtons()
    {
        _enTranslateBtn.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(_enBox.Text);
        _arTranslateBtn.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(_arBox.Text);
    }

    private void SetStatus(string? message, bool isError = false, string? detailLabel = null, string? detailValue = null)
    {
        var res = Application.Current.Resources;
        var brush = (Brush)res[isError ? "FanousErrorTextBrush" : "FanousTextMutedBrush"];
        _statusText.Foreground = brush;
        _statusDetailLabel.Foreground = brush;
        _statusDetailValue.Foreground = brush;
        if (string.IsNullOrWhiteSpace(message))
        {
            _statusText.Text = "";
            _statusText.Visibility = Visibility.Collapsed;
        }
        else
        {
            _statusText.Text = message;
            _statusText.Visibility = Visibility.Visible;
        }
        if (string.IsNullOrWhiteSpace(detailValue))
        {
            _statusDetailLabel.Text = "";
            _statusDetailValue.Text = "";
            _statusDetailRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            _statusDetailLabel.Text = string.IsNullOrWhiteSpace(detailLabel) ? "" : detailLabel;
            _statusDetailValue.Text = detailValue;
            _statusDetailRow.Visibility = Visibility.Visible;
        }
    }

    private void ClearUndo()
    {
        _undo = null;
        _undoBtn.Visibility = Visibility.Collapsed;
    }

    private void ApplyUndo()
    {
        if (_undo is not { } u) return;
        ClearUndo();
        if (u.Lang == "en") _enBox.Text = u.Previous;
        else _arBox.Text = u.Previous;
        var langName = Loc.Get(u.Lang == "en" ? "Localized_LangEnglish" : "Localized_LangArabic");
        SetStatus(Loc.Format("Localized_Undone", langName));
    }

    private async Task TranslateAsync(string from, string to)
    {
        var source = (from == "en" ? _enBox.Text : _arBox.Text)?.Trim() ?? "";
        if (source.Length == 0)
        {
            var fromName = Loc.Get(from == "en" ? "Localized_LangEnglish" : "Localized_LangArabic");
            SetStatus(Loc.Format("Localized_SourceEmpty", fromName), isError: true);
            return;
        }

        _busy = true;
        ClearUndo();
        UpdateButtons();
        SetStatus(Loc.Get("Localized_Translating"));

        try
        {
            var item = await _translate(from, to, source, _field, CancellationToken.None);
            if (string.Equals(item.Refused, "quran", StringComparison.OrdinalIgnoreCase))
            {
                // Label (localized) and Latin Surah refs sit in separate TextBlocks so Uno Skia never scrambles bidi runs
                var refs = item.Quran is { Count: > 0 } ? string.Join(", ", item.Quran.Select(q => q.Label)) : null;
                SetStatus(Loc.Get("Localized_QuranRefused"), isError: true, detailLabel: Loc.Get("Localized_QuranRefs"), detailValue: refs);
                return;
            }
            var translated = (item.Text ?? "").Trim();
            if (translated.Length == 0)
            {
                SetStatus(Loc.Get("Localized_ErrorFailed"), isError: true);
                return;
            }
            var wasTruncated = translated.Length > _maxLength;
            if (wasTruncated) translated = TruncateAtCodePoint(translated, _maxLength);

            var targetBox = to == "en" ? _enBox : _arBox;
            var previous = targetBox.Text ?? "";
            var replaced = !string.IsNullOrWhiteSpace(previous) && !string.Equals(previous, translated, StringComparison.Ordinal);
            if (replaced)
            {
                _undo = (to, previous, translated);
                _undoBtn.Visibility = Visibility.Visible;
            }
            targetBox.Text = translated;
            var toName = Loc.Get(to == "en" ? "Localized_LangEnglish" : "Localized_LangArabic");
            var baseStatus = Loc.Format(replaced ? "Localized_Replaced" : "Localized_Filled", toName);
            SetStatus(wasTruncated ? Loc.Format("Localized_Truncated", baseStatus, Loc.Number(_maxLength)) : baseStatus);
        }
        catch (ImportApiException ex)
        {
            var msg = ex.Code switch
            {
                "daily_limit" => Loc.Get("Localized_ErrorLimit"),
                "translation_paused" or "feature_off" => Loc.Get("Localized_ErrorPaused"),
                "budget_spent" => Loc.Get("Localized_ErrorBudget"),
                "forbidden" => Loc.Get("Localized_ErrorForbidden"),
                "unavailable" or "unreachable" => Loc.Get("Localized_ErrorUnavailable"),
                _ => Loc.Get("Localized_ErrorFailed"),
            };
            SetStatus(msg, isError: true);
        }
        catch (Exception)
        {
            SetStatus(Loc.Get("Localized_ErrorFailed"), isError: true);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }
}

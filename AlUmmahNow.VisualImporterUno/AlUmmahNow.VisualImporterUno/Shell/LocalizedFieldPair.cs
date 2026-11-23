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
    private readonly Button _undoBtn;

    private bool _busy;
    private (string Lang, string Previous)? _undo;

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

        _enBox = new TextBox
        {
            FlowDirection = FlowDirection.LeftToRight,
            TextAlignment = TextAlignment.Left,
            Language = "en",
            MaxLength = maxLength,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        if (multiline) _enBox.MinHeight = 76;
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
        if (multiline) _arBox.MinHeight = 76;
        AutomationProperties.SetName(_arBox, Loc.Format("Localized_FieldIn", label, Loc.Get("Localized_LangArabic")));

        var uiFlow = Loc.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _enError = new TextBlock { Style = mutedStyle, Foreground = errorBrush, FlowDirection = uiFlow, Visibility = Visibility.Collapsed };
        _arError = new TextBlock { Style = mutedStyle, Foreground = errorBrush, FlowDirection = uiFlow, Visibility = Visibility.Collapsed };
        if (Loc.IsArabic && arabicFont is not null)
        {
            _enError.FontFamily = arabicFont;
            _arError.FontFamily = arabicFont;
        }

        var uiFont = Loc.IsArabic ? arabicFont : null;
        _enTranslateBtn = MakeTranslateButton(secondaryBtnStyle, Loc.PopupLine(Loc.Get("Localized_TranslateToAr")), uiFont, HorizontalAlignment.Left);
        _arTranslateBtn = MakeTranslateButton(secondaryBtnStyle, Loc.PopupLine(Loc.Get("Localized_TranslateToEn")), uiFont, HorizontalAlignment.Right);
        _enTranslateBtn.Click += async (_, _) => await TranslateAsync("en", "ar");
        _arTranslateBtn.Click += async (_, _) => await TranslateAsync("ar", "en");

        _enBox.TextChanged += (_, _) =>
        {
            if (_undo?.Lang == "en") ClearUndo();
            UpdateButtons();
        };
        _arBox.TextChanged += (_, _) =>
        {
            if (_undo?.Lang == "ar") ClearUndo();
            UpdateButtons();
        };

        _statusText = new TextBlock { Style = mutedStyle, Foreground = mutedBrush, Visibility = Visibility.Collapsed };
        _undoBtn = new Button
        {
            Style = secondaryBtnStyle,
            MinHeight = 30,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 12,
            Content = new TextBlock { Text = Loc.PopupLine(Loc.Get("Localized_Undo")), FontSize = 12 },
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
        };
        var arHeader = new TextBlock
        {
            Text = Loc.PopupLine(Loc.Get("Localized_Arabic") + suffix),
            Style = mutedStyle,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (arabicFont is not null) arHeader.FontFamily = arabicFont;

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

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        statusRow.Children.Add(_statusText);
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

    private static void SetErrorBlock(TextBlock block, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            block.Text = "";
            block.Visibility = Visibility.Collapsed;
        }
        else
        {
            block.Text = Loc.PopupLine(message);
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

    private void SetStatus(string? message, bool isError = false)
    {
        var res = Application.Current.Resources;
        _statusText.Foreground = (Brush)res[isError ? "FanousErrorTextBrush" : "FanousTextMutedBrush"];
        if (string.IsNullOrWhiteSpace(message))
        {
            _statusText.Text = "";
            _statusText.Visibility = Visibility.Collapsed;
        }
        else
        {
            _statusText.Text = Loc.PopupLine(message);
            _statusText.Visibility = Visibility.Visible;
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
        if (u.Lang == "en") _enBox.Text = u.Previous;
        else _arBox.Text = u.Previous;
        var langName = Loc.Get(u.Lang == "en" ? "Localized_LangEnglish" : "Localized_LangArabic");
        SetStatus(Loc.Format("Localized_Undone", langName));
        ClearUndo();
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
                var refs = item.Quran is { Count: > 0 }
                    ? " " + Loc.Format("Localized_QuranRefs", string.Join(Loc.IsArabic ? "، " : ", ", item.Quran.Select(q => q.Label)))
                    : "";
                SetStatus(Loc.Get("Localized_QuranRefused") + refs, isError: true);
                return;
            }
            var translated = (item.Text ?? "").Trim();
            if (translated.Length == 0)
            {
                SetStatus(Loc.Get("Localized_ErrorFailed"), isError: true);
                return;
            }
            if (translated.Length > _maxLength) translated = translated[.._maxLength];

            var targetBox = to == "en" ? _enBox : _arBox;
            var previous = targetBox.Text ?? "";
            var replaced = !string.IsNullOrWhiteSpace(previous) && !string.Equals(previous, translated, StringComparison.Ordinal);
            targetBox.Text = translated;
            if (replaced)
            {
                _undo = (to, previous);
                _undoBtn.Visibility = Visibility.Visible;
            }
            var toName = Loc.Get(to == "en" ? "Localized_LangEnglish" : "Localized_LangArabic");
            SetStatus(Loc.Format(replaced ? "Localized_Replaced" : "Localized_Filled", toName));
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

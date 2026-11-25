using AlUmmahNow.ToolsCore.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The update notice (wiki AUN-APP-UPDATES §12): one line under the header, the surface colour, never covering work.
/// Text, What's new, the action, and × (only while it can be closed; closing hides that version). Announced once to
/// screen readers. Right to left in Arabic, digits through <see cref="Loc"/>.
/// </summary>
public sealed partial class UpdateBar : UserControl
{
    private readonly UpdateService _service;
    private readonly Func<bool> _busy;
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _progress = new() { Width = 120, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly HyperlinkButton _whatsNew = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _action = new() { VerticalAlignment = VerticalAlignment.Center, MinHeight = 32, Padding = new Thickness(14, 0, 14, 0), CornerRadius = new CornerRadius(8) };
    private readonly Button _close = new() { Width = 32, Height = 32, Padding = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Content = new FontIcon { Glyph = "", FontSize = 12 } };
    private string? _announced;

    public UpdateBar(UpdateService service, Func<bool> busy)
    {
        _service = service;
        _busy = busy;
        _whatsNew.Content = Loc.Get("Update_WhatsNew");
        _action.Style = (Style)Application.Current.Resources["ToolsPrimaryButton"];
        AutomationProperties.SetName(_close, Loc.Get("Update_Close"));
        AutomationProperties.SetLiveSetting(_text, AutomationLiveSetting.Polite);
        _close.Click += (_, _) => _service.Dismiss();
        _whatsNew.Click += async (_, _) => await ShowNotesAsync();
        _action.Click += async (_, _) => await OnActionAsync();

        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 8, 8, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new FontIcon { Glyph = "", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["FanousPrimaryBrush"] };
        Grid.SetColumn(_text, 1);
        Grid.SetColumn(_progress, 2);
        Grid.SetColumn(_whatsNew, 3);
        Grid.SetColumn(_action, 4);
        Grid.SetColumn(_close, 5);
        row.Children.Add(icon);
        row.Children.Add(_text);
        row.Children.Add(_progress);
        row.Children.Add(_whatsNew);
        row.Children.Add(_action);
        row.Children.Add(_close);
        // Narrow: the words have the first line to themselves, the buttons stand under them
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 640;
            if (Grid.GetRow(_action) == (narrow ? 1 : 0)) return;
            row.RowSpacing = narrow ? 6 : 0;
            Grid.SetColumnSpan(_text, narrow ? 4 : 1);
            foreach (var part in new FrameworkElement[] { _progress, _whatsNew, _action }) Grid.SetRow(part, narrow ? 1 : 0);
        };
        // Brushes through {ThemeResource} so the bar follows Light and Dark (a code lookup would pin one theme)
        var border = Themed<Border>("<Border Background=\"{ThemeResource FanousSurfaceBrush}\" BorderBrush=\"{ThemeResource FanousOutlineBrush}\" BorderThickness=\"0,0,0,1\" />");
        border.Child = row;
        Content = border;
        _service.Changed += v => DispatcherQueue.TryEnqueue(() => Render(v));
        Render(_service.View);
    }

    /// <summary>An element whose brushes are {ThemeResource}s, so they follow the app's Light/Dark theme.</summary>
    internal static T Themed<T>(string xaml) where T : class =>
        (T)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml.Replace("<" + typeof(T).Name + " ", "<" + typeof(T).Name + " xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" ", StringComparison.Ordinal));

    /// <summary>The forced screen is its own view; everything else shows here.</summary>
    private void Render(UpdateView v)
    {
        if (v.Stage is UpdateStage.Hidden or UpdateStage.Forced)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        FlowDirection = Loc.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var version = Loc.Version(v.Version);
        (_text.Text, var action) = v.Stage switch
        {
            UpdateStage.Available => (Loc.Format("Update_Available", version), v.DownloadOnly ? Loc.Get("Update_Download") : Loc.Get("Update_Update")),
            UpdateStage.Downloading => (Loc.Format("Update_Downloading", Loc.Percent((v.Progress ?? 0) * 100)), null),
            UpdateStage.Ready => (Loc.Get(_busy() ? "Update_ReadyBusy" : _service.OtherCopyRunning ? "Update_ReadyOtherCopy" : "Update_Ready"), Loc.Get("Update_RestartNow")),
            UpdateStage.RequiredSoon => (Loc.Get("Update_RequiredSoon"), null),
            _ => (string.Empty, null),
        };
        _action.Content = action;
        _action.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        _action.IsEnabled = !(v.Stage == UpdateStage.Ready && _busy());
        _progress.Visibility = v.Stage == UpdateStage.Downloading ? Visibility.Visible : Visibility.Collapsed;
        _progress.Value = (v.Progress ?? 0) * 100;
        _whatsNew.Visibility = (Loc.IsArabic ? v.NotesAr : v.NotesEn) is { Length: > 0 } && v.Stage != UpdateStage.Downloading ? Visibility.Visible : Visibility.Collapsed;
        _close.Visibility = v.CanClose ? Visibility.Visible : Visibility.Collapsed;
        // Ready is said again when its words change (another copy is open)
        var key = v.Stage == UpdateStage.Ready ? $"{v.Stage}:{v.Version}:{_text.Text}" : $"{v.Stage}:{v.Version}";
        if (_announced != key)
        {
            _announced = key;
            FrameworkElementAutomationPeer.FromElement(_text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private async Task OnActionAsync()
    {
        var v = _service.View;
        if (v.Stage == UpdateStage.Ready)
        {
            // Refused: the bar says why (another copy is open, or work started meanwhile)
            if (!await _service.RestartNowAsync()) Render(_service.View);
            return;
        }
        var url = await _service.UpdateNowAsync();
        if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private async Task ShowNotesAsync()
    {
        var v = _service.View;
        ContentDialog Build(XamlRoot root)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = Loc.Format("Update_NotesTitle", Loc.Version(v.Version)),
                Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = (Loc.IsArabic ? v.NotesAr : v.NotesEn) ?? string.Empty, TextWrapping = TextWrapping.Wrap } },
                CloseButtonText = Loc.Get("Update_NotesClose"),
                FlowDirection = Loc.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            };
            DialogLook.Apply(dialog);
            return dialog;
        }
        await AppDialogs.ShowAsync(Build, AlUmmahNow.ToolsCore.DialogKind.Question, XamlRoot);
    }
}

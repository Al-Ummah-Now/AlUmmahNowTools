using AlUmmahNow.ToolsCore.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Below the minimum version, with an update to install (wiki AUN-APP-UPDATES §3.7, §12): the whole window, the app
/// icon, "This version is no longer supported", the admin's message, the running and required versions, Update (or
/// Download) and Quit. No close button.
/// </summary>
public sealed partial class ForcedUpdateView : UserControl
{
    private readonly UpdateService _service;
    private readonly TextBlock _body = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 480 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 480 };
    private readonly Button _update = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _quit = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ProgressBar _progress = new() { Visibility = Visibility.Collapsed };
    public ForcedUpdateView(UpdateService service)
    {
        _service = service;
        _update.Style = (Style)Application.Current.Resources["ToolsPrimaryButton"];
        _quit.Style = (Style)Application.Current.Resources["ToolsSecondaryButton"];
        _quit.Content = Loc.Get("Update_Quit");
        _quit.Click += (_, _) => Application.Current.Exit();
        _update.Click += async (_, _) =>
        {
            if (_service.View.Stage == UpdateStage.Ready)
            {
                await _service.RestartNowAsync();
                return;
            }
            var url = await _service.UpdateNowAsync();
            if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) await Windows.System.Launcher.LaunchUriAsync(uri);
        };
        var title = new TextBlock { Text = Loc.Get("Update_ForcedTitle"), Style = (Style)Application.Current.Resources["ToolsCardTitle"], TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        var icon = new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = new CornerRadius(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new ImageBrush { ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Images/staff_mark.png")), Stretch = Stretch.UniformToFill },
        };
        var buttons = new StackPanel { Spacing = 10, Width = 280, HorizontalAlignment = HorizontalAlignment.Center, Children = { _update, _quit } };
        var panel = new StackPanel { Spacing = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(24), Children = { icon, title, _body, _message, _progress, buttons } };
        // The page background through {ThemeResource}, so the screen follows Light and Dark
        var root = UpdateBar.Themed<Grid>("<Grid Background=\"{ThemeResource FanousBackgroundBrush}\" />");
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        _service.Changed += v => DispatcherQueue.TryEnqueue(() => Render(v));
        Visibility = Visibility.Collapsed;
        Render(_service.View);
    }

    private void Render(UpdateView v)
    {
        var forced = v.Stage == UpdateStage.Forced || (v.Required && v.Stage is UpdateStage.Downloading or UpdateStage.Ready);
        Visibility = forced ? Visibility.Visible : Visibility.Collapsed;
        if (!forced) return;
        FlowDirection = Loc.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _body.Text = Loc.Format("Update_ForcedBody", Loc.Version(_service.InstalledVersion), Loc.Version(v.Version));
        var message = Loc.IsArabic ? v.MessageAr : v.MessageEn;
        _message.Text = message ?? string.Empty;
        _message.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        _progress.Visibility = v.Stage == UpdateStage.Downloading ? Visibility.Visible : Visibility.Collapsed;
        _progress.Value = (v.Progress ?? 0) * 100;
        _update.Content = v.Stage == UpdateStage.Ready ? Loc.Get("Update_RestartNow") : v.DownloadOnly ? Loc.Get("Update_Download") : Loc.Get("Update_Update");
        _update.IsEnabled = v.Stage != UpdateStage.Downloading;
    }
}

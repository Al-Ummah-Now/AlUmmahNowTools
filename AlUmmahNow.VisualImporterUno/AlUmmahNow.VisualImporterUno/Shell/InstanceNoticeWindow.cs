using AlUmmahNow.ToolsCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// A copy that cannot run now (Tools #89): its first copy holds the queue and does not answer, or the updater is replacing the
/// app. It says so and runs nothing, neither the queue nor the services nor the updates. Try again starts the app anew; Close
/// ends it. After an update the app starts again by itself.
/// </summary>
internal sealed partial class InstanceNoticeWindow : Window
{
    public InstanceNoticeWindow(ToolsPreferences preferences, bool finishingUpdate)
    {
        Title = Loc.Get("Shell_WindowTitle");
        var title = new TextBlock { Text = Loc.Get(finishingUpdate ? "Instance_UpdateTitle" : "Instance_Title"), Style = (Style)Application.Current.Resources["ToolsCardTitle"], TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        var body = new TextBlock { Text = Loc.KeepLastWords(Loc.Get(finishingUpdate ? "Instance_UpdateBody" : "Instance_Body")), Style = (Style)Application.Current.Resources["ToolsBody"], TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        var again = new Button { Content = Loc.Get("Instance_TryAgain"), Style = (Style)Application.Current.Resources["ToolsPrimaryButton"], HorizontalAlignment = HorizontalAlignment.Stretch };
        var close = new Button { Content = Loc.Get("Instance_Close"), Style = (Style)Application.Current.Resources["ToolsSecondaryButton"], HorizontalAlignment = HorizontalAlignment.Stretch };
        again.Click += (_, _) => AppInstance.StartAgain();
        close.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            Application.Current.Exit();
            ExitBackstop.EndSoon();
        };
        var icon = new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = new CornerRadius(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new ImageBrush { ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Images/staff_mark.png")), Stretch = Stretch.UniformToFill },
        };
        var buttons = new StackPanel { Spacing = 10, Width = 280, HorizontalAlignment = HorizontalAlignment.Center };
        if (!finishingUpdate) buttons.Children.Add(again);
        buttons.Children.Add(close);
        var panel = new StackPanel { Spacing = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(24), Children = { icon, title, body, buttons } };
        var root = UpdateBar.Themed<Grid>("<Grid Background=\"{ThemeResource FanousBackgroundBrush}\" />");
        var page = new UserControl { Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        if (ShellState.TextFontFor(preferences.Language) is { } font) page.FontFamily = font;
        root.Children.Add(page);
        root.FlowDirection = preferences.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var dark = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128;
        root.RequestedTheme = preferences.EffectiveTheme(dark) == ToolsTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        Content = root;
        root.Loaded += (_, _) =>
        {
            // AppWindow sizes are physical pixels: 640 × 420 as the person sees them, whatever the display's scale
            var scale = root.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = (int)Math.Ceiling(640 * scale), Height = (int)Math.Ceiling(420 * scale) });
            (finishingUpdate ? close : again).Focus(FocusState.Programmatic);
        };
    }
}

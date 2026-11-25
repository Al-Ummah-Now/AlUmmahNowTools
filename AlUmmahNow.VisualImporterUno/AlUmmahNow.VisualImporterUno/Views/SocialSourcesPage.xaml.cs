using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;

namespace AlUmmahNow.VisualImporterUno.Views;

/// <summary>The Social sources page (wiki TOOLS-ADMIN TA-13): super admins list, add and run a channel's social sources.</summary>
public sealed partial class SocialSourcesPage : Page
{
    public SocialSourcesViewModel ViewModel { get; }

    public SocialSourcesPage()
    {
        ViewModel = App.GetService<SocialSourcesViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        if (ViewModel.SelectedChannel is { } channel) ChannelBox.Text = channel.Name;
        SuggestionsUnderBox.Attach(ChannelBox);
        ChannelBoxes.Attach(ChannelBox, ViewModel.ChannelSearch, channel => ViewModel.SelectedChannel = channel);
        // Typed values start at the start edge in Arabic (the Import page's rule, TA-13): Arabic text runs right to left, a
        // Latin value (an address, a Latin name) keeps its order but starts at the right edge. An empty field starts on the
        // right (Uno places its caret by its alignment). Two Latin lines under a channel's name end at the right edge too.
        foreach (var box in new[] { AddressBox, LabelBox, ByBox, NoteBox }) SetValueDirection(box, latinOnly: ReferenceEquals(box, AddressBox));
        if (Loc.IsArabic) ChannelSummaryText.TextAlignment = TextAlignment.Right;
        Loaded += async (_, _) =>
        {
            SetChannelValueDirection();
            KeepLastWords(PageContent);
            await ViewModel.RefreshAsync();
            await OpenDemoAsync();
        };
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Bindings.Update();
    }

    /// <summary>
    /// Demo frames (<c>TOOLS_DEMO_SOCIAL=job|items|remove|add|cancel|cancelled|takeout|uploads_off|uploads_off_takeout|uploads_off_job</c>):
    /// the failed source's job log open, the Telegram source's items open, the Remove dialog, the Add card at the top, the Cancel
    /// dialog on the Telegram source's running sync, that sync cancelled with its log, the YouTube source's Takeout match, or the
    /// uploads-off views (sources list, Takeout panel, failed sync-job drawer).
    /// </summary>
    private async System.Threading.Tasks.Task OpenDemoAsync()
    {
        if (!ImportWorkspace.Current.IsDemo) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => RefreshButton.Focus(FocusState.Programmatic));
        var open = Environment.GetEnvironmentVariable("TOOLS_DEMO_SOCIAL");
        SourceRow? Row(string id) => ViewModel.Sources.FirstOrDefault(r => r.Id == id);
        switch (open)
        {
            case "job" when Row(DemoSocial.FailedId) is { } failed:
                ViewModel.ToggleJob(failed);
                ScrollTo(SourcesCard);
                break;
            case "uploads_off" or "uploads_off_retried" when Row(DemoSocial.FailedId) is { } failed:
                ViewModel.ToggleJob(failed);
                ScrollTo(failed);
                break;
            case "uploads_off_job" when Row(DemoSocial.FailedId) is { } failed:
                ViewModel.ToggleJob(failed);
                ScrollToBottomOf(failed, "JobCard");
                break;
            case "items" or "uploads_off_items" when Row(DemoSocial.TelegramId) is { } telegram:
                await ViewModel.ToggleItemsAsync(telegram);
                ScrollTo(telegram);
                break;
            case "add":
                ScrollTo(AddCard);
                break;
            case "remove" when Row(DemoSocial.FailedId) is { } row:
                await System.Threading.Tasks.Task.Delay(1000);
                Remove_Click(new Border { Tag = row }, new RoutedEventArgs());
                break;
            case "cancel" when Row(DemoSocial.TelegramId) is { } running:
                ViewModel.ToggleJob(running);
                ScrollTo(SourcesCard);
                await System.Threading.Tasks.Task.Delay(1000);
                CancelJob_Click(new Border { Tag = running }, new RoutedEventArgs());
                break;
            case "cancelled" when Row(DemoSocial.TelegramId) is { } stopped:
                ViewModel.ToggleJob(stopped);
                ScrollTo(SourcesCard);
                break;
            case "uploads_off_takeout" when Row(DemoSocial.YouTubeId) is { } yt:
                await ViewModel.ToggleTakeoutAsync(yt);
                await ViewModel.ListTakeoutFolderAsync(yt, "/Takeout");
                ScrollTo(yt, "TakeoutCard");
                break;
            case "takeout" when Row(DemoSocial.YouTubeId) is { } youtube:
                await ViewModel.ToggleTakeoutAsync(youtube);
                await ViewModel.ListTakeoutFolderAsync(youtube, "/Takeout");
                // From the top of the Sources card, so the frame shows the card whole (TOOLS_SCREENSHOT_SIZE tall enough)
                ScrollTo(SourcesCard);
                break;
        }
    }

    /// <summary>Puts a source's row (or a tagged panel inside it) at the top of the viewport.</summary>
    private void ScrollTo(SourceRow row, string? childTag = null)
    {
        PageContent.Padding = new Thickness(PageContent.Padding.Left, PageContent.Padding.Top, PageContent.Padding.Right, 4000);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            var container = FindTagged(SourcesCard, row);
            var target = container is not null && childTag is not null ? FindByTag(container, childTag) ?? container : container;
            var top = target is null ? 0 : target.TransformToVisual(PageContent).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            PageScroll.ChangeView(null, Math.Max(0, top - 8), null, true);
        });
    }

    /// <summary>Scrolls so the bottom of a tagged panel inside a row sits cleanly inside the viewport.</summary>
    private void ScrollToBottomOf(SourceRow row, string childTag)
    {
        PageContent.Padding = new Thickness(PageContent.Padding.Left, PageContent.Padding.Top, PageContent.Padding.Right, 4000);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            var container = FindTagged(SourcesCard, row);
            var target = container is not null ? FindByTag(container, childTag) ?? container : null;
            if (target is null) return;
            var bottom = target.TransformToVisual(PageContent).TransformPoint(new Windows.Foundation.Point(0, target.ActualHeight)).Y;
            var viewport = PageScroll.ViewportHeight > 0 ? PageScroll.ViewportHeight : 640;
            PageScroll.ChangeView(null, Math.Max(0, bottom - viewport + 120), null, true);
        });
    }

    private static FrameworkElement? FindByTag(DependencyObject parent, string tag)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Tag as string == tag) return fe;
            if (FindByTag(child, tag) is { } found) return found;
        }
        return null;
    }

    /// <summary>The row's own Border (its DataContext), found in the sources list.</summary>
    private static FrameworkElement? FindTagged(DependencyObject parent, SourceRow row)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Border { DataContext: SourceRow r } border && ReferenceEquals(r, row)) return border;
            if (FindTagged(child, row) is { } found) return found;
        }
        return null;
    }

    /// <summary>Puts a card at the top, with room below so a short card can get there.</summary>
    private void ScrollTo(FrameworkElement card)
    {
        PageContent.Padding = new Thickness(PageContent.Padding.Left, PageContent.Padding.Top, PageContent.Padding.Right, 4000);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            var top = card.TransformToVisual(PageContent).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            PageScroll.ChangeView(null, Math.Max(0, top - 8), null, true);
        });
    }

    /// <summary>The page's own wrapping text (headings and help) never ends a line on a lone word.</summary>
    private static void KeepLastWords(DependencyObject parent)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock { TextWrapping: TextWrapping.Wrap } text && text.Text is { Length: > 0 } value) text.Text = Loc.KeepLastWords(value);
            KeepLastWords(child);
        }
    }

    private void ValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box) SetValueDirection(box, latinOnly: ReferenceEquals(box, AddressBox));
    }

    /// <summary>
    /// A typed value in a field of an Arabic page: Arabic text runs right to left; a Latin value keeps its left-to-right
    /// order but starts at the right edge. English pages: left. (The Import page's SetValueDirection, TA-13.)
    /// </summary>
    private static void SetValueDirection(TextBox box, bool latinOnly = false)
    {
        var text = box.Text ?? "";
        var arabicText = !latinOnly && text.Any(c => c is >= '\u0600' and <= '\u06FF');
        var direction = Loc.IsArabic && (text.Length == 0 && !latinOnly || arabicText) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        // In a left-to-right box, Right is the visual right edge
        var align = Loc.IsArabic ? TextAlignment.Right : TextAlignment.Left;
        if (box.FlowDirection != direction) box.FlowDirection = direction;
        if (box.TextAlignment != align) box.TextAlignment = align;
    }

    /// <summary>The channel box's own text box: in Arabic its value starts at the right edge; the box keeps its mirrored search icon.</summary>
    private void SetChannelValueDirection()
    {
        if (Loc.IsArabic && FindChild<TextBox>(ChannelBox) is { } typed && typed.TextAlignment != TextAlignment.Right) typed.TextAlignment = TextAlignment.Right;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : class
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    // ------------------------------------------------------------ Channel

    private void ChannelBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => SetChannelValueDirection();

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowAllChannelsCommand.Execute(null);
        ChannelBox.Text = "";
    }

    // ------------------------------------------------------------ Source rows

    private static SourceRow? RowOf(object sender) => (sender as FrameworkElement)?.Tag as SourceRow;

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await ViewModel.SyncAsync(row);
    }

    private async void TurnOff_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await ViewModel.SetEnabledAsync(row, false);
    }

    private async void TurnOn_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await ViewModel.SetEnabledAsync(row, true);
    }

    private async void Schedule_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Only the admin's own choice: the row setting its schedule from the server changes nothing
        if (sender is ComboBox box && RowOf(box) is { } row && box.SelectedIndex >= 0 && box.SelectedIndex != row.ScheduleIndex)
        {
            await ViewModel.SetScheduleAsync(row, box.SelectedIndex);
            // Not saved (the reason shows under the source): the box goes back to the schedule the source has
            if (box.SelectedIndex != row.ScheduleIndex) box.SelectedIndex = row.ScheduleIndex;
        }
    }

    private void Job_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) ViewModel.ToggleJob(row);
    }

    private async void Items_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await ViewModel.ToggleItemsAsync(row);
    }

    private async void RetryJob_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await ViewModel.RetryJobAsync(row);
    }

    /// <summary>Cancelling stops the sync between items; what it already posted stays. Ask first.</summary>
    private async void CancelJob_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (await ConfirmCancelAsync(SocialSourcesViewModel.CancelDialogLines(row.Label, ViewModel.UploadsOn))) await ViewModel.CancelJobAsync(row);
    }

    /// <summary>Asks before cancelling. Each line is short enough for one line: the popup's Arabic order only holds on one.</summary>
    private async System.Threading.Tasks.Task<bool> ConfirmCancelAsync(params string[] lines)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var line in lines)
            panel.Children.Add(new TextBlock { Text = Loc.PopupLine(Loc.KeepLastWords(line)), TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Social_CancelTitle"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Social_CancelConfirm"),
            CloseButtonText = Loc.Get("Social_CancelKeep"),
            DefaultButton = ContentDialogButton.Close,
            FlowDirection = FlowDirection,
            XamlRoot = XamlRoot,
        };
        DialogLook.Apply(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ------------------------------------------------------------ Takeout

    private async void Takeout_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await ViewModel.ToggleTakeoutAsync(row);
    }

    private async void ChooseTakeout_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder, ViewMode = Windows.Storage.Pickers.PickerViewMode.List };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow));
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) await ViewModel.ListTakeoutFolderAsync(row, folder.Path);
        }
        catch (Exception ex)
        {
            row.TakeoutError = Loc.GetWithDetail("Takeout_FolderFailed", ex.Message);
        }
    }

    private async void UploadTakeout_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await ViewModel.UploadTakeoutAsync(row);
    }

    private async void PickCandidate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TakeoutCandidateRow candidate) return;
        var row = ViewModel.Sources.FirstOrDefault(r => r.TakeoutFiles.Contains(candidate.File));
        if (row is not null) await ViewModel.ConfirmTakeoutAsync(row, candidate.File, candidate);
    }

    private async void CancelTakeout_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (await ConfirmCancelAsync(row.Label, Loc.Get("Takeout_CancelBody"), Loc.Get("Takeout_CancelBody2"))) await ViewModel.CancelTakeoutJobAsync(row);
    }

    private void CopyJob_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var package = new DataPackage();
        package.SetText(ViewModel.JobLogTextFor(row));
        Clipboard.SetContent(package);
        row.JobCopied = true;
    }

    /// <summary>Removing stops the imports; what the source already posted stays on the channel, so ask first.</summary>
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var panel = new StackPanel { Spacing = 6 };
        foreach (var line in new[] { row.Label, Loc.Get("Social_RemoveBody") })
            panel.Children.Add(new TextBlock { Text = Loc.PopupLine(Loc.KeepLastWords(line)), TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Social_RemoveTitle"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Social_RemoveConfirm"),
            CloseButtonText = Loc.Get("Import_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            FlowDirection = FlowDirection,
            XamlRoot = XamlRoot,
        };
        DialogLook.Apply(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.RemoveAsync(row);
    }

    // ------------------------------------------------------------ Add

    private async void AddConnection_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ConnectionRow row) await ViewModel.AddConnectionAsync(row);
    }
}

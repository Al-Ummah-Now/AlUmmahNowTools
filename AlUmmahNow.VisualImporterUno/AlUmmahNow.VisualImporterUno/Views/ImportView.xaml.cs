using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AlUmmahNow.VisualImporterUno.Views;

/// <summary>The Import page (wiki TOOLS-ADMIN TA-11): channel, folder, review, and the work queue.</summary>
public sealed partial class ImportView : Page
{
    private const double NarrowWidth = 640;

    public BulkImportViewModel ViewModel { get; }

    public ImportView()
    {
        ViewModel = App.GetService<BulkImportViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        if (ViewModel.SelectedChannel is { } channel) ChannelBox.Text = channel.Name;
        SuggestionsUnderBox.Attach(ChannelBox);
        Loaded += (_, _) =>
        {
            ViewModel.Attach();
            KeepLastWords(PageContent);
            OpenDemoDialog();
        };
        Unloaded += (_, _) => ViewModel.Detach();
    }

    /// <summary>
    /// Screenshots of the dialogs and the channel suggestions (<c>TOOLS_DEMO_IMPORT_DIALOG=log|remove|channel|suggest</c>)
    /// and of the queue (<c>TOOLS_DEMO_IMPORT_SCROLL=end</c>: the queue card at the top, so no line is cut under the
    /// header), demo only.
    /// </summary>
    private async void OpenDemoDialog()
    {
        if (!ImportWorkspace.Current.IsDemo) return;
        if (Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT_SCROLL") == "end")
        {
            // Room below the queue, so its card can sit at the top however short it is. A fixed amount, not the
            // viewport's height: the screenshot window grows after this, and a smaller room clamped the scroll back
            // (the page subtitle's divider showed above the card, AUDIT-2 2026-11-22)
            PageContent.Padding = new Thickness(PageContent.Padding.Left, PageContent.Padding.Top, PageContent.Padding.Right, Math.Max(PageScroll.ViewportHeight, 4000));
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                var top = QueueCard.TransformToVisual(PageContent).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                PageScroll.ChangeView(null, Math.Max(0, top - 8), null, true);
            });
        }
        var dialog = Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT_DIALOG");
        // No caret in a frame unless it shows typing: focus rests on a button, without its keyboard ring
        if (dialog != "suggest") DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => BrowseButton.Focus(FocusState.Programmatic));
        if (dialog is null) return;
        // A dialog needs the window's XamlRoot, which is ready a moment after the page loads
        await System.Threading.Tasks.Task.Delay(1000);
        // TOOLS_DEMO_IMPORT_ROW picks the row (the transcribed one is 10); the upload retry otherwise
        var row = ViewModel.QueueRows.Skip(int.TryParse(Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT_ROW"), out var at) ? at : 3).FirstOrDefault();
        var tag = new Border { Tag = row };
        switch (dialog)
        {
            case "log" when row is not null: ViewModel.ToggleLog(row); break;
            case "remove" when row is not null: Remove_Click(tag, new RoutedEventArgs()); break;
            case "channel": NewChannel_Click(this, new RoutedEventArgs()); break;
            case "suggest":
                ChannelBox.Focus(FocusState.Keyboard);
                // The caret after the typed name, as when typing, never through its first letter
                if (FindChild<TextBox>(ChannelBox) is { } typed) typed.Select(typed.Text.Length, 0);
                ChannelBox.IsSuggestionListOpen = true;
                break;
        }
    }

    /// <summary>A path reads left to right; the empty field's placeholder reads in the interface's language, on its side.</summary>
    private void FolderBox_TextChanged(object sender, RoutedEventArgs e)
    {
        var direction = Loc.IsArabic && string.IsNullOrEmpty(FolderBox.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (FolderBox.FlowDirection != direction) FolderBox.FlowDirection = direction;
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

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Bindings.Update();
    }

    /// <summary>Narrow windows stack the queue's buttons under its title and the four counts under each other.</summary>
    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < NarrowWidth;
        // The suggestions are placed where the box was when they opened: closed when the page lays out again
        ChannelBox.IsSuggestionListOpen = false;
        foreach (var (row, button) in new (Grid, FrameworkElement)[] { (ChannelRow, NewChannelButton), (FolderRow, BrowseButton) })
        {
            Grid.SetRow(button, narrow ? 1 : 0);
            Grid.SetColumn(button, narrow ? 0 : 1);
            row.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : GridLength.Auto;
            row.ColumnSpacing = narrow ? 0 : 12;
        }
        Grid.SetRow(QueueActions, narrow ? 1 : 0);
        Grid.SetColumn(QueueActions, narrow ? 0 : 1);
        if (QueueActions.Parent is Grid header && header.RowDefinitions.Count < 2)
        {
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        // Two by two, or one column when narrow
        var boxes = new FrameworkElement[] { HashBox, EncodeBox, UploadBox, AttachBox };
        while (narrow && ParallelGrid.RowDefinitions.Count < boxes.Length) ParallelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ParallelGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ParallelGrid.ColumnSpacing = narrow ? 0 : 12;
        for (var i = 0; i < boxes.Length; i++)
        {
            Grid.SetRow(boxes[i], narrow ? i : i / 2);
            Grid.SetColumn(boxes[i], narrow ? 0 : i % 2);
        }
    }

    // ------------------------------------------------------------ Channel

    private void ChannelBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _ = ViewModel.SearchChannelsAsync(sender.Text);
        sender.IsSuggestionListOpen = true;
    }

    /// <summary>Suggestions when the admin moves to the box, never when the page puts focus there itself.</summary>
    private void ChannelBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (ChannelBox.FocusState == FocusState.Programmatic) return;
        if (ViewModel.Channels.Count == 0) _ = ViewModel.SearchChannelsAsync(ChannelBox.Text);
    }

    private void ChannelBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not ImportChannel channel) return;
        ViewModel.SelectedChannel = channel;
        sender.Text = channel.Name;
    }

    private async void NewChannel_Click(object sender, RoutedEventArgs e)
    {
        // Name and description are typed in the interface's language, so an Arabic field starts on the right
        var typed = Loc.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        // Uno places an empty field's caret by its alignment, not its direction: an Arabic field is aligned right too
        var typedAlign = Loc.IsArabic ? TextAlignment.Right : TextAlignment.Left;
        var name = new TextBox { Header = Loc.PopupLine(Loc.Get("Import_NewChannelName")), FlowDirection = typed, TextAlignment = typedAlign };
        // The handle is typed left to right in both languages; its label follows the dialog's direction
        var handle = new TextBox { FlowDirection = FlowDirection.LeftToRight, PlaceholderText = "@" };
        var handleLabel = new TextBlock { Text = Loc.PopupLine(Loc.Get("Import_NewChannelHandle")), Margin = new Thickness(0, 0, 0, -6) };
        AutomationProperties.SetName(handle, Loc.Get("Import_NewChannelHandle"));
        var description = new TextBox { Header = Loc.PopupLine(Loc.Get("Import_NewChannelDescription")), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, FlowDirection = typed, TextAlignment = typedAlign };
        var error = new TextBlock { Style = (Style)Application.Current.Resources["ToolsMuted"], Visibility = Visibility.Collapsed };
        error.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FanousErrorTextBrush"];
        var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
        panel.Children.Add(DialogText(Loc.Get("Import_NewChannelBody")));
        panel.Children.Add(name);
        panel.Children.Add(handleLabel);
        panel.Children.Add(handle);
        panel.Children.Add(description);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Import_NewChannelTitle"),
            PrimaryButtonText = Loc.Get("Import_NewChannelCreate"),
            CloseButtonText = Loc.Get("Import_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            Content = panel,
            FlowDirection = FlowDirection,
            XamlRoot = XamlRoot,
        };
        DialogLook.Apply(dialog);
        dialog.PrimaryButtonClick += async (d, args) =>
        {
            // Keep the dialog open until admin-api has answered, and show its reason when it says no
            var deferral = args.GetDeferral();
            try
            {
                var problem = await ViewModel.CreateChannelAsync(name.Text, handle.Text, description.Text);
                if (problem is not null)
                {
                    error.Text = Loc.PopupLine(problem);
                    error.Visibility = Visibility.Visible;
                    args.Cancel = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && ViewModel.SelectedChannel is { } channel) ChannelBox.Text = channel.Name;
    }

    // ------------------------------------------------------------ Folder

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder, ViewMode = PickerViewMode.List };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.Current.MainWindow));
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) ViewModel.FolderPath = folder.Path;
        }
        catch (Exception ex)
        {
            ViewModel.ScanMessage = Loc.GetWithDetail("Import_FolderFailed", ex.Message);
        }
    }

    // ------------------------------------------------------------ Queue rows

    /// <summary>Lines of text for a dialog, each in its own block and put in order for the popup layer (see <see cref="Loc.PopupLine"/>).</summary>
    private static StackPanel DialogText(params string[] lines)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var line in lines) panel.Children.Add(new TextBlock { Text = Loc.PopupLine(Loc.KeepLastWords(line)), TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private static QueueRow? RowOf(object sender) => (sender as FrameworkElement)?.Tag as QueueRow;

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) ViewModel.Pause(row);
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) ViewModel.Resume(row);
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) ViewModel.Retry(row);
    }

    /// <summary>Removing stops the file's work here; what already reached the channel stays there, so ask first.</summary>
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Import_RemoveTitle"),
            Content = DialogText(row.Title, Loc.Get("Import_RemoveBody")),
            PrimaryButtonText = Loc.Get("Import_RemoveConfirm"),
            CloseButtonText = Loc.Get("Import_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            FlowDirection = FlowDirection,
            XamlRoot = XamlRoot,
        };
        DialogLook.Apply(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) ViewModel.Remove(row);
    }

    /// <summary>Opens or closes the file's log under its row.</summary>
    private void Log_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) ViewModel.ToggleLog(row);
    }

    /// <summary>Copies the file's log for a bug report.</summary>
    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var package = new DataPackage();
        package.SetText(ViewModel.LogTextFor(row));
        Clipboard.SetContent(package);
        row.LogCopied = true;
    }
}

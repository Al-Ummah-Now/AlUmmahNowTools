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
        Loaded += (_, _) =>
        {
            ViewModel.Attach();
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
            // Room below the queue, so its card can sit at the top however short it is
            PageContent.Padding = new Thickness(PageContent.Padding.Left, PageContent.Padding.Top, PageContent.Padding.Right, PageScroll.ViewportHeight);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                var top = QueueCard.TransformToVisual(PageContent).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                PageScroll.ChangeView(null, Math.Max(0, top - 8), null, true);
            });
        }
        var dialog = Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT_DIALOG");
        if (dialog is null) return;
        // A dialog needs the window's XamlRoot, which is ready a moment after the page loads
        await System.Threading.Tasks.Task.Delay(1000);
        var row = ViewModel.QueueRows.Skip(3).FirstOrDefault();
        var tag = new Border { Tag = row };
        switch (dialog)
        {
            case "log" when row is not null: ViewModel.ToggleLog(row); break;
            case "remove" when row is not null: Remove_Click(tag, new RoutedEventArgs()); break;
            case "channel": NewChannel_Click(this, new RoutedEventArgs()); break;
            case "suggest":
                ChannelBox.Focus(FocusState.Keyboard);
                ChannelBox.IsSuggestionListOpen = true;
                break;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Bindings.Update();
    }

    /// <summary>Narrow windows stack the queue's buttons under its title and the three counts under each other.</summary>
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
        var boxes = new FrameworkElement[] { HashBox, UploadBox, AttachBox };
        if (narrow && ParallelGrid.RowDefinitions.Count < 3)
        {
            for (var i = 0; i < 3; i++) ParallelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        ParallelGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ParallelGrid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ParallelGrid.ColumnSpacing = narrow ? 0 : 12;
        for (var i = 0; i < boxes.Length; i++)
        {
            Grid.SetRow(boxes[i], narrow ? i : 0);
            Grid.SetColumn(boxes[i], narrow ? 0 : i);
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
        var name = new TextBox { Header = Loc.PopupLine(Loc.Get("Import_NewChannelName")) };
        // The handle is typed left to right in both languages; its label follows the dialog's direction
        var handle = new TextBox { FlowDirection = FlowDirection.LeftToRight, PlaceholderText = "@" };
        var handleLabel = new TextBlock { Text = Loc.PopupLine(Loc.Get("Import_NewChannelHandle")), Margin = new Thickness(0, 0, 0, -6) };
        AutomationProperties.SetName(handle, Loc.Get("Import_NewChannelHandle"));
        var description = new TextBox { Header = Loc.PopupLine(Loc.Get("Import_NewChannelDescription")), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
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
        foreach (var line in lines) panel.Children.Add(new TextBlock { Text = Loc.PopupLine(line), TextWrapping = TextWrapping.Wrap });
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

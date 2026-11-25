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
        if (ViewModel.SelectedChannel is { } channel) ChannelBox.Text = channel.LocalizedName;
        SuggestionsUnderBox.Attach(ChannelBox);
        AutomationProperties.SetName(RemoveAllButton, Loc.Get("Import_RemoveAllConfirm"));
        Loaded += (_, _) =>
        {
            ViewModel.Attach();
            SetChannelValueDirection();
            KeepLastWords(PageContent);
            OpenDemoDialog();
        };
        Unloaded += (_, _) => ViewModel.Detach();
    }

    /// <summary>
    /// Screenshots of the dialogs and the channel suggestions (<c>TOOLS_DEMO_IMPORT_DIALOG=log|remove|remove-all|channel|suggest</c>)
    /// and of the queue (<c>TOOLS_DEMO_IMPORT_SCROLL=end</c>: the queue card at the top, so no line is cut under the
    /// header), demo only.
    /// </summary>
    private async void OpenDemoDialog()
    {
        if (!ImportWorkspace.Current.IsDemo) return;
        // Frames of the import form alone (TOOLS_DEMO_IMPORT_FRAME=form): the queue card is left out, so the frame ends at
        // the form card's edge in every language, never part way into the queue (AUDIT-2 2026-11-22)
        if (Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT_FRAME") == "form") QueueCard.Visibility = Visibility.Collapsed;
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
            case "remove-all": RemoveAll_Click(this, new RoutedEventArgs()); break;
            case "channel": NewChannel_Click(this, new RoutedEventArgs()); break;
            case "suggest":
                ChannelBox.Focus(FocusState.Keyboard);
                // The caret after the typed name, as when typing, never through its first letter
                if (FindChild<TextBox>(ChannelBox) is { } typed) typed.Select(typed.Text.Length, 0);
                ChannelBox.IsSuggestionListOpen = true;
                break;
        }
    }

    /// <summary>
    /// A path reads left to right but starts at the field's start edge, the right in Arabic (AUDIT-2 2026-11-22); the empty
    /// field's placeholder reads in the interface's language, on its side.
    /// </summary>
    private void FolderBox_TextChanged(object sender, RoutedEventArgs e) => SetValueDirection(FolderBox, latinOnly: true);

    /// <summary>
    /// A typed value in a field of an Arabic page: Arabic text runs right to left; a Latin value (a path, a Latin channel
    /// name) keeps its left-to-right order but starts at the right edge, like the rows under it. English pages: left.
    /// </summary>
    private static void SetValueDirection(TextBox box, bool latinOnly = false)
    {
        var text = box.Text ?? "";
        var arabicText = !latinOnly && text.Any(c => c is >= '\u0600' and <= '\u06FF');
        var direction = Loc.IsArabic && (text.Length == 0 || arabicText) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        // In a left-to-right box, Right is the visual right edge
        var align = Loc.IsArabic && direction == FlowDirection.LeftToRight ? TextAlignment.Right : TextAlignment.Left;
        if (box.FlowDirection != direction) box.FlowDirection = direction;
        if (box.TextAlignment != align) box.TextAlignment = align;
    }

    /// <summary>The channel box's own text box, once its template has loaded.</summary>
    private void SetChannelValueDirection()
    {
        // The box stays right to left in Arabic, so its search button keeps the mirrored side; only the value moves
        if (FindChild<TextBox>(ChannelBox) is { } typed && Loc.IsArabic && typed.TextAlignment != TextAlignment.Right) typed.TextAlignment = TextAlignment.Right;
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
        // Pause all, Resume all, Remove all and Clear finished: under the title unless there is room beside it
        var under = e.NewSize.Width < 960;
        Grid.SetRow(QueueActions, under ? 1 : 0);
        Grid.SetColumn(QueueActions, under ? 0 : 1);
        Grid.SetColumnSpan(QueueActions, under ? 2 : 1);
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
        SetChannelValueDirection();
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
        sender.Text = channel.LocalizedName;
    }

    private async void NewChannel_Click(object sender, RoutedEventArgs e)
    {
        var namePair = new LocalizedFieldPair(
            Loc.Get("Import_NewChannelName"),
            "channels.name",
            ViewModel.TranslateFieldAsync,
            required: true,
            multiline: false,
            maxLength: ChannelValidation.MaxNameLength);
        // The handle is typed left to right in both languages; its label follows the dialog's direction
        var handle = new TextBox { FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Left, PlaceholderText = "@" };
        var handleLabel = new TextBlock
        {
            Text = Loc.PopupLine(Loc.Get("Import_NewChannelHandle")),
            Style = (Style)Application.Current.Resources["ToolsLabel"],
            Margin = new Thickness(0, 0, 0, -6),
        };
        AutomationProperties.SetName(handle, Loc.Get("Import_NewChannelHandle"));
        var descriptionPair = new LocalizedFieldPair(
            Loc.Get("Import_NewChannelDescription"),
            "channels.description",
            ViewModel.TranslateFieldAsync,
            required: false,
            multiline: true,
            maxLength: ChannelValidation.MaxDescriptionLength);

        if (ImportWorkspace.Current.IsDemo && Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT_CHANNEL") is { Length: > 0 } demoMode)
        {
            switch (demoMode)
            {
                case "filled":
                    namePair.SetValues("Fiqh Academy", "أكاديمية الفقه");
                    handle.Text = "fiqhacademy";
                    descriptionPair.SetValues("Weekly lessons on the jurisprudence of worship.", "دروس أسبوعية في فقه العبادات.");
                    break;
                case "working":
                    namePair.SetValues("Fiqh Academy", "");
                    handle.Text = "fiqhacademy";
                    descriptionPair.SetValues("Weekly lessons on the jurisprudence of worship.", "دروس أسبوعية في فقه العبادات.");
                    namePair.SetDemoWorking();
                    break;
                case "undo":
                    namePair.SetValues("Fiqh Academy", "أكاديمية الفقه الإسلامي");
                    handle.Text = "fiqhacademy";
                    descriptionPair.SetValues("Weekly lessons on the jurisprudence of worship.", "دروس أسبوعية في فقه العبادات.");
                    namePair.SetDemoUndo("ar", "أكاديمية الفقه", Loc.Format("Localized_Replaced", Loc.Get("Localized_LangArabic")));
                    break;
                case "error-source-empty":
                    namePair.SetValues("", "");
                    namePair.SetDemoStatus(Loc.Format("Localized_SourceEmpty", Loc.Get("Localized_LangEnglish")), isError: true);
                    break;
                case "error-quran":
                    namePair.SetValues("Quran Reflections", "تأملات قرآنية");
                    descriptionPair.SetValues("", "الله لا إله إلا هو الحي القيوم");
                    descriptionPair.SetDemoStatus(
                        Loc.Get("Localized_QuranRefused"),
                        isError: true,
                        detailLabel: Loc.Get("Localized_QuranRefs"),
                        detailValue: "Al-Baqarah 2:255");
                    break;
                case "error-limit":
                    namePair.SetValues("Fiqh Academy", "");
                    namePair.SetDemoStatus(Loc.Get("Localized_ErrorLimit"), isError: true);
                    break;
                case "error-failed":
                    namePair.SetValues("Fiqh Academy", "");
                    namePair.SetDemoStatus(Loc.Get("Localized_ErrorFailed"), isError: true);
                    break;
                case "errors-validation":
                    namePair.SetValues("Fiqh Academy", "");
                    descriptionPair.SetValues("Weekly lessons on the jurisprudence of worship.", "");
                    // What Create answers for these texts: one language is missing in each pair
                    namePair.SetErrors(null, ChannelFieldErrorText("NameAr", "required"));
                    descriptionPair.SetErrors(null, ChannelFieldErrorText("DescAr", "required_pair"));
                    break;
            }
        }

        var error = new TextBlock { Style = (Style)Application.Current.Resources["ToolsMuted"], Visibility = Visibility.Collapsed };
        error.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FanousErrorTextBrush"];
        // The window has no smallest size: the dialog's content never asks for more than the window has
        var panel = new StackPanel { Spacing = 14, MinWidth = Math.Min(520, Math.Max(0, XamlRoot.Size.Width - 96)) };
        panel.Children.Add(DialogText(Loc.Get("Import_NewChannelBody")));
        panel.Children.Add(namePair.Element);
        panel.Children.Add(handleLabel);
        panel.Children.Add(handle);
        panel.Children.Add(descriptionPair.Element);
        panel.Children.Add(error);
        var scroller = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 560,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var dialog = new ContentDialog
        {
            Title = Loc.PopupLine(Loc.Get("Import_NewChannelTitle")),
            PrimaryButtonText = Loc.PopupLine(Loc.Get("Import_NewChannelCreate")),
            CloseButtonText = Loc.PopupLine(Loc.Get("Import_Cancel")),
            DefaultButton = ContentDialogButton.Primary,
            Content = scroller,
            FlowDirection = FlowDirection,
            XamlRoot = XamlRoot,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 660.0;
        if (Loc.IsArabic && ShellState.TextFontFor("ar") is { } arFont) dialog.FontFamily = arFont;
        DialogLook.Apply(dialog);
        dialog.PrimaryButtonClick += async (d, args) =>
        {
            // Keep the dialog open until admin-api has answered, and show inline or server errors when refused
            var deferral = args.GetDeferral();
            try
            {
                namePair.SetErrors(null, null);
                descriptionPair.SetErrors(null, null);
                error.Visibility = Visibility.Collapsed;
                var outcome = await ViewModel.CreateChannelAsync(
                    namePair.English,
                    namePair.Arabic,
                    handle.Text,
                    descriptionPair.English,
                    descriptionPair.Arabic);
                if (!outcome.Succeeded)
                {
                    if (outcome.FieldErrors is { } fe)
                    {
                        namePair.SetErrors(
                            ChannelFieldErrorText("NameEn", fe.NameEn),
                            ChannelFieldErrorText("NameAr", fe.NameAr));
                        descriptionPair.SetErrors(
                            ChannelFieldErrorText("DescEn", fe.DescriptionEn),
                            ChannelFieldErrorText("DescAr", fe.DescriptionAr));
                    }
                    if (outcome.Error is not null)
                    {
                        error.Text = Loc.PopupLine(outcome.Error);
                        error.Visibility = Visibility.Visible;
                    }
                    args.Cancel = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && ViewModel.SelectedChannel is { } channel) ChannelBox.Text = channel.LocalizedName;
    }

    private static string? ChannelFieldErrorText(string slot, string? code) => code switch
    {
        null => null,
        "required" => Loc.Get($"Import_NewChannelError_{slot}_Required"),
        "required_pair" => Loc.Get($"Import_NewChannelError_{slot}_Pair"),
        "too_long" => Loc.Format(
            $"Import_NewChannelError_{slot}_TooLong",
            Loc.Number(slot.StartsWith("Name", StringComparison.Ordinal) ? ChannelValidation.MaxNameLength : ChannelValidation.MaxDescriptionLength)),
        _ => Loc.Get("Import_NewChannelNameNeeded"),
    };

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

    private bool _removeAllAsked;

    /// <summary>Empties the queue, after asking: what is on the site and on the computer stays (pressing twice asks once).</summary>
    private async void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_removeAllAsked || !ViewModel.HasQueue) return;
        _removeAllAsked = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = ViewModel.RemoveAllTitle,
                Content = DialogText(ViewModel.RemoveAllLines().ToArray()),
                PrimaryButtonText = Loc.Get("Import_RemoveAllConfirm"),
                CloseButtonText = Loc.Get("Import_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = FlowDirection,
                XamlRoot = XamlRoot,
            };
            DialogLook.Apply(dialog);
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            ViewModel.RemoveAll();
            // The button went with the queue: the keyboard goes on from the folder
            BrowseButton.Focus(FocusState.Programmatic);
        }
        finally
        {
            _removeAllAsked = false;
        }
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

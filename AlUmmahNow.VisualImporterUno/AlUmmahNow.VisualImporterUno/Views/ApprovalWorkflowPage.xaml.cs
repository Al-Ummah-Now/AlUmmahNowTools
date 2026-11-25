using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Linq;

namespace AlUmmahNow.VisualImporterUno.Views;

/// <summary>Videos that arrived in watched folders, waiting for a yes or a no before they upload.</summary>
public sealed partial class ApprovalWorkflowPage : Page
{
    public ApprovalViewModel ViewModel { get; }

    public ApprovalWorkflowPage()
    {
        ViewModel = App.GetService<ApprovalViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RefreshButton, Loc.Get("Watch_Refresh"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SortBox, Loc.Get("Approve_SortLabel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SortDirection, Loc.Get("Approve_SortReverse"));
        // Undo goes with the line it is on
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.HasDeletedPreset) && !ViewModel.HasDeletedPreset && UndoPreset.FocusState != FocusState.Unfocused) AppDialogs.KeepFocus(PresetBox);
        };
        // Review frames of its questions (TOOLS_DEMO_DIALOG), over what the demo scanner lists
        PendingApprovalDto[] Files(int count) => ViewModel.FilteredApprovals.Where(a => a.Status == ApprovalStatus.Pending).Take(count).ToArray();
        DemoDialogs.OnLoaded(this, "reject", () => ApprovalViewModel.AskToRejectAsync(Files(1), typed: false));
        DemoDialogs.OnLoaded(this, "reject-many", () => ApprovalViewModel.AskToRejectAsync(Files(3), typed: false));
        foreach (var all in new[] { "reject-all", "reject-all-typed" })
            DemoDialogs.OnLoaded(this, all, () => ApprovalViewModel.AskToRejectAsync(Files(int.MaxValue), typed: true));
        DemoDialogs.OnLoaded(this, "approve-all", () => ApprovalViewModel.AskToApproveAsync(Files(int.MaxValue).Length));
        DemoDialogs.OnLoaded(this, "preset-deleted", async () =>
        {
            ViewModel.DeletedPreset = new AlUmmahNow.Data.Models.FilterPreset { Name = Loc.IsArabic ? "دروس السيرة" : "Seerah lessons" };
            FiltersExpander.IsExpanded = true;
            await System.Threading.Tasks.Task.Delay(800);
            UndoPreset.StartBringIntoView();
        });
    }

    private void OfflineCard_Connected(object? sender, EventArgs e)
    {
        ViewModel.RefreshApprovalsCommand.NotifyCanExecuteChanged();
        if (ViewModel.RefreshApprovalsCommand.CanExecute(null)) _ = ViewModel.RefreshApprovalsCommand.ExecuteAsync(null);
    }

    private void ApprovalsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SetSelection(ApprovalsList.SelectedItems.OfType<PendingApprovalDto>().ToList());

    private void SelectAll_Click(object sender, RoutedEventArgs e) => ApprovalsList.SelectAll();

    private void SelectNone_Click(object sender, RoutedEventArgs e) => ApprovalsList.SelectedItems.Clear();

    private static PendingApprovalDto? ItemOf(object sender) => (sender as FrameworkElement)?.Tag as PendingApprovalDto;

    private void Preview_Click(object sender, RoutedEventArgs e) => ViewModel.PreviewFileCommand.Execute(ItemOf(sender));

    private async void ApproveOne_Click(object sender, RoutedEventArgs e) => await ViewModel.ApproveOneCommand.ExecuteAsync(ItemOf(sender));

    private async void RejectOne_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RejectOneCommand.ExecuteAsync(ItemOf(sender));
        AppDialogs.KeepFocus(RefreshButton);
    }

    private async void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = Loc.Get("Approve_PresetName"), Text = ViewModel.NewPresetName };
        var descriptionBox = new TextBox
        {
            Header = Loc.Get("Approve_PresetDescription"),
            Text = ViewModel.NewPresetDescription,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80,
        };
        var defaultBox = new CheckBox { Content = Loc.Get("Approve_PresetAsDefault"), IsChecked = ViewModel.SetAsDefault };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(nameBox);
        panel.Children.Add(descriptionBox);
        panel.Children.Add(defaultBox);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Approve_PresetDialogTitle"),
            PrimaryButtonText = Loc.Get("Approve_PresetSaveButton"),
            CloseButtonText = Loc.Get("Approve_PresetCancel"),
            DefaultButton = ContentDialogButton.Primary,
            Content = panel,
            FlowDirection = FlowDirection,
            XamlRoot = XamlRoot,
        };
        DialogLook.Apply(dialog);
        if (await AppDialogs.ShowAsync(_ => dialog, AlUmmahNow.ToolsCore.DialogKind.Question, XamlRoot) != ContentDialogResult.Primary) return;
        ViewModel.NewPresetName = nameBox.Text;
        ViewModel.NewPresetDescription = descriptionBox.Text;
        ViewModel.SetAsDefault = defaultBox.IsChecked ?? false;
        await ViewModel.SavePresetCommand.ExecuteAsync(nameBox.Text);
    }
}

/// <summary>Visible when the value is set; ConverterParameter "Inverse" flips it.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var present = value is string text ? !string.IsNullOrEmpty(text) : value != null;
        if (parameter as string == "Inverse") present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>A size filter typed as text.</summary>
public class StringToLongConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is long number ? number.ToString() : string.Empty;

    public object? ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is string text && long.TryParse(text, out var number) ? number : null;
}

/// <summary>A date filter for the calendar picker.</summary>
public class DateTimeToDateConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) => value is DateTime date ? new DateTimeOffset(date) : null;

    public object? ConvertBack(object value, Type targetType, object parameter, string language) => value is DateTimeOffset offset ? offset.DateTime : null;
}

/// <summary>A thumbnail file on disk as an image.</summary>
public class PathToUriConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is string path && !string.IsNullOrEmpty(path) ? new BitmapImage(new Uri(path)) : null;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

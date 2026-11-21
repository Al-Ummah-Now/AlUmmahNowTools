using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AlUmmahNow.VisualImporterUno.Views;

/// <summary>Folders the scanner watches for new videos, and adding one or several of them.</summary>
public sealed partial class WatchManagementPage : Page
{
    public WatchViewModel ViewModel { get; }

    public WatchManagementPage()
    {
        ViewModel = App.GetService<WatchViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RefreshButton, Shell.Loc.Get("Watch_Refresh"));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Bindings.Update();
    }

    private void OfflineCard_Connected(object? sender, EventArgs e)
    {
        ViewModel.RefreshWatchesCommand.NotifyCanExecuteChanged();
        if (ViewModel.RefreshWatchesCommand.CanExecute(null)) _ = ViewModel.RefreshWatchesCommand.ExecuteAsync(null);
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder, ViewMode = PickerViewMode.List };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.Current.MainWindow));
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) ViewModel.NewWatchPath = folder.Path;
        }
        catch (Exception ex)
        {
            ViewModel.CreateWatchError = Shell.Loc.GetWithDetail("Import_FolderFailed", ex.Message);
        }
    }

    private async void BrowseImportFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, ViewMode = PickerViewMode.List };
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.Current.MainWindow));
            var file = await picker.PickSingleFileAsync();
            if (file != null) ViewModel.ImportFilePath = file.Path;
        }
        catch (Exception ex)
        {
            ViewModel.ImportError = Shell.Loc.GetWithDetail("Settings_BrowseFailed", ex.Message);
        }
    }

    private void StopWatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not WatchStatusDto watch) return;
        ViewModel.SelectedWatch = watch;
        if (ViewModel.StopWatchCommand.CanExecute(null)) ViewModel.StopWatchCommand.Execute(null);
    }

    private void ResumeWatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not WatchStatusDto watch) return;
        ViewModel.SelectedWatch = watch;
        if (ViewModel.ResumeWatchCommand.CanExecute(null)) ViewModel.ResumeWatchCommand.Execute(null);
    }

    private void Approvals_Click(object sender, RoutedEventArgs e) => (App.Current.MainWindow as MainWindow)?.NavigateToApprovalWorkflowView();
}

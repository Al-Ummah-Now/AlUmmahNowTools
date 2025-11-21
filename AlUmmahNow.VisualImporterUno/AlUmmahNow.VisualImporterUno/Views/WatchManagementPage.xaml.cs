using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;
using System;
using AlUmmahNow.VisualImporterUno.ViewModels;
using AlUmmahNow.Data.Models.FileScanning;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AlUmmahNow.VisualImporterUno.Views;

/// <summary>
/// Watch Management page for managing file system watches and approvals
/// </summary>
public sealed partial class WatchManagementPage : Page
{
    /// <summary>
    /// Gets the ViewModel for this page
    /// </summary>
    public WatchViewModel ViewModel { get; private set; }

    public WatchManagementPage()
    {
        this.InitializeComponent();

        // Get ViewModel from DI container (registered as singleton)
        ViewModel = App.GetService<WatchViewModel>();

        // Set DataContext for binding
        this.DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // ViewModel is already initialized from DI
        // Additional initialization can be done here if needed

        // Update bindings
        Bindings.Update();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // Cleanup if necessary
        // Note: Don't dispose ViewModel here as it might be reused
    }

    /// <summary>
    /// Opens a folder picker dialog for selecting watch directory
    /// </summary>
    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                ViewMode = PickerViewMode.List
            };

            folderPicker.FileTypeFilter.Add("*");

            // Initialize the folder picker with the window handle
            var window = App.Current.MainWindow;
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                ViewModel.NewWatchPath = folder.Path;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error picking folder: {ex.Message}");
        }
    }
}

/// <summary>
/// Converter for status color based on IsActive property
/// </summary>
public class StatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isActive)
        {
            return isActive ? Windows.UI.Color.FromArgb(255, 22, 163, 74) : // Green #16A34A
                             Windows.UI.Color.FromArgb(255, 156, 163, 175);  // Gray #9CA3AF
        }
        return Windows.UI.Color.FromArgb(255, 156, 163, 175);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

// Note: FileSizeConverter, DateTimeConverter, ZeroToVisibilityConverter, StringToVisibilityConverter, 
// and BooleanToVisibilityConverter are defined in Converters/ValueConverters.cs
// and referenced in XAML via xmlns:converters namespace

/// <summary>
/// DTO for watch status display
/// Note: This should match the model used in WatchViewModel
/// </summary>
public class WatchStatusDto
{
    public Guid WatchId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string WatchPath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public int TotalFilesDetected { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// DTO for pending approval display
/// Note: This should match the model used in WatchViewModel
/// </summary>
public class PendingApprovalDto
{
    public Guid Id { get; set; }
    public Guid WatchId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public ApprovalStatus Status { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessedBy { get; set; }
}

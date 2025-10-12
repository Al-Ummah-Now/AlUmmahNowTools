using Microsoft.UI.Xaml.Controls;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AlUmmahNow.VisualImporterUno.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            this.InitializeComponent();
            ViewModel = App.GetService<SettingsViewModel>();
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.TestConnectionAsync();
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var success = await ViewModel.SaveSettingsAsync();
                if (success)
                {
                    var message = $"Settings have been saved successfully!\n\n" +
                                 $"Settings are stored using Windows 11 native storage and will roam across your devices.\n\n" +
                                 $"The application will use these settings immediately.\n\n" +
                                 $"For system-level configuration, you can still use:\n" +
                                 $"dotnet user-secrets set \"FileScanService:Url\" \"{ViewModel.FileScanServiceUrl}\"\n" +
                                 $"dotnet user-secrets set \"ServiceInstallation:ExecutablePath\" \"{ViewModel.ServiceExecutablePath}\"\n" +
                                 $"dotnet user-secrets set \"ServiceInstallation:StartAutomatically\" \"{ViewModel.AutoStartService.ToString().ToLower()}\"";
                    
                    ShowInfoMessage("Settings Saved", message);
                }
                else
                {
                    ShowErrorMessage("Save Failed", "Failed to save settings. Please check the application logs for more details.");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Save Error", $"An error occurred while saving settings: {ex.Message}");
            }
        }

        private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetToDefaults();
            ShowInfoMessage("Settings Reset", "Settings have been reset to default values.");
        }

        private async void BrowseServiceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openPicker = new FileOpenPicker();
                openPicker.ViewMode = PickerViewMode.List;
                openPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                openPicker.FileTypeFilter.Add(".exe");

                // Get the current window handle for the picker
                var window = App.Current.MainWindow;
                if (window == null)
                {
                    return;
                }

                var hWnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(openPicker, hWnd);

                var file = await openPicker.PickSingleFileAsync();
                if (file != null)
                {
                    ViewModel.ServiceExecutablePath = file.Path;
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error", $"Failed to browse for service executable: {ex.Message}");
            }
        }

        private async void ShowInfoMessage(string title, string message)
        {
            var dialog = new ContentDialog()
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async void ShowErrorMessage(string title, string message)
        {
            var dialog = new ContentDialog()
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}
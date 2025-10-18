using Microsoft.UI.Xaml.Controls;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System;

namespace AlUmmahNow.VisualImporterUno.Views
{
    public sealed partial class FFmpegSettingsPage : Page
    {
        public FFmpegSettingsViewModel ViewModel { get; }

        public FFmpegSettingsPage()
        {
            this.InitializeComponent();
            ViewModel = App.GetService<FFmpegSettingsViewModel>();
        }

        private async void AutoDetectButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.AutoDetectAsync();
        }

        private async void VerifyButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.VerifyInstallationAsync();
        }

        private async void SavePathsButton_Click(object sender, RoutedEventArgs e)
        {
            var success = await ViewModel.SavePathsAsync();
            if (success)
            {
                ShowInfoMessage("Success", "FFmpeg paths saved successfully and verified to be working.");
            }
            else
            {
                ShowErrorMessage("Save Failed", "Failed to save FFmpeg paths. Please ensure the paths are correct and the executables are working.");
            }
        }

        private async void BrowseFFmpegButton_Click(object sender, RoutedEventArgs e)
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
                    ViewModel.FFmpegPath = file.Path;
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error", $"Failed to browse for FFmpeg executable: {ex.Message}");
            }
        }

        private async void BrowseFFprobeButton_Click(object sender, RoutedEventArgs e)
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
                    ViewModel.FFprobePath = file.Path;
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error", $"Failed to browse for FFprobe executable: {ex.Message}");
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

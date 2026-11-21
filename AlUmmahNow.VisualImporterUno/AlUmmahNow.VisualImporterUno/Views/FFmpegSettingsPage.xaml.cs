using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AlUmmahNow.VisualImporterUno.Views
{
    /// <summary>The video programs Tools uses (ffmpeg and ffprobe): whether they work, where they are, how to install them.</summary>
    public sealed partial class FFmpegSettingsPage : Page
    {
        public FFmpegSettingsViewModel ViewModel { get; }

        public FFmpegSettingsPage()
        {
            ViewModel = App.GetService<FFmpegSettingsViewModel>();
            InitializeComponent();
        }

        private async void AutoDetectButton_Click(object sender, RoutedEventArgs e) => await ViewModel.AutoDetectAsync();

        private async void VerifyButton_Click(object sender, RoutedEventArgs e) => await ViewModel.VerifyInstallationAsync();

        private async void SavePathsButton_Click(object sender, RoutedEventArgs e) =>
            ShowStatus(await ViewModel.SavePathsAsync() ? Loc.Get("Video_Saved") : ViewModel.StatusMessage);

        private async void BrowseFFmpegButton_Click(object sender, RoutedEventArgs e)
        {
            var path = await PickProgramAsync();
            if (path != null) ViewModel.FFmpegPath = path;
        }

        private async void BrowseFFprobeButton_Click(object sender, RoutedEventArgs e)
        {
            var path = await PickProgramAsync();
            if (path != null) ViewModel.FFprobePath = path;
        }

        private async Task<string?> PickProgramAsync()
        {
            try
            {
                var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.ComputerFolder };
                picker.FileTypeFilter.Add(OperatingSystem.IsWindows() ? ".exe" : "*");
                if (App.Current.MainWindow is not { } window) return null;
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
                return (await picker.PickSingleFileAsync())?.Path;
            }
            catch (Exception ex)
            {
                ShowStatus(Loc.GetWithDetail("Settings_BrowseFailed", ex.Message));
                return null;
            }
        }

        private void ShowStatus(string text)
        {
            SaveStatus.Text = text;
            SaveStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}

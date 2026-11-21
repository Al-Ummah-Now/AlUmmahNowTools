using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AlUmmahNow.VisualImporterUno.Views
{
    /// <summary>How Tools reaches the folder scanner, whether it starts it, and notifications.</summary>
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            ViewModel = App.GetService<SettingsViewModel>();
            InitializeComponent();
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e) => await ViewModel.TestConnectionAsync();

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowStatus(await ViewModel.SaveSettingsAsync() ? Loc.Get("Settings_Saved") : Loc.Get("Settings_SaveFailed"));
            }
            catch (Exception ex)
            {
                ShowStatus(Loc.GetWithDetail("Settings_SaveFailed", ex.Message));
            }
        }

        private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetToDefaults();
            ShowStatus(Loc.Get("Settings_ResetDone"));
        }

        private async void BrowseServiceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.ComputerFolder };
                picker.FileTypeFilter.Add(OperatingSystem.IsWindows() ? ".exe" : "*");
                if (App.Current.MainWindow is not { } window) return;
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
                var file = await picker.PickSingleFileAsync();
                if (file != null) ViewModel.ServiceExecutablePath = file.Path;
            }
            catch (Exception ex)
            {
                ShowStatus(Loc.GetWithDetail("Settings_BrowseFailed", ex.Message));
            }
        }

        private void ShowStatus(string text)
        {
            SaveStatus.Text = text;
            SaveStatus.Visibility = Visibility.Visible;
        }
    }
}

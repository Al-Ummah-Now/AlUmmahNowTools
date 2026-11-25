using AlUmmahNow.ToolsCore.Updates;
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
            ShowUpdates();
        }

        /// <summary>Settings → Updates (wiki AUN-APP-UPDATES §5.3): version, last check, install automatically, Check now.</summary>
        private void ShowUpdates()
        {
            if (AppUpdates.Service is not { Enabled: true } updates) return;
            UpdatesCard.Visibility = Visibility.Visible;
            UpdatesTitle.Text = Loc.Get("Update_SettingsTitle");
            UpdatesVersion.Text = Loc.Format("Update_SettingsVersion", Loc.Version(updates.InstalledVersion));
            UpdatesLastChecked.Text = updates.LastChecked is { } at ? Loc.Format("Update_SettingsLastChecked", Loc.When(at.LocalDateTime)) : Loc.Get("Update_SettingsNeverChecked");
            UpdatesAuto.Visibility = UpdatesAutoHint.Visibility = updates.CanSelfUpdate ? Visibility.Visible : Visibility.Collapsed;
            UpdatesAuto.Header = Loc.Get("Update_SettingsAuto");
            UpdatesAuto.IsOn = updates.AutoInstall;
            UpdatesAutoHint.Text = Loc.Get("Update_SettingsAutoHint");
            UpdatesCheckNow.Content = Loc.Get("Update_SettingsCheckNow");
        }

        private void UpdatesAuto_Toggled(object sender, RoutedEventArgs e)
        {
            if (AppUpdates.Service is { } updates && updates.AutoInstall != UpdatesAuto.IsOn) updates.AutoInstall = UpdatesAuto.IsOn;
        }

        private async void UpdatesCheckNow_Click(object sender, RoutedEventArgs e)
        {
            if (AppUpdates.Service is not { } updates) return;
            UpdatesCheckNow.IsEnabled = false;
            UpdatesStatus.Text = Loc.Get("Update_SettingsChecking");
            var reached = await updates.CheckAsync(userAsked: true);
            UpdatesCheckNow.IsEnabled = true;
            UpdatesStatus.Text = !reached
                ? Loc.Get("Update_SettingsCheckFailed")
                // Too old with nothing to update to: not "the latest version"
                : updates.View.Stage == UpdateStage.RequiredSoon ? Loc.Get("Update_RequiredSoon")
                : updates.View is { Stage: not UpdateStage.Hidden, Version: { } v } ? Loc.Format("Update_SettingsFound", Loc.Version(v)) : Loc.Get("Update_SettingsUpToDate");
            UpdatesLastChecked.Text = updates.LastChecked is { } at ? Loc.Format("Update_SettingsLastChecked", Loc.When(at.LocalDateTime)) : Loc.Get("Update_SettingsNeverChecked");
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

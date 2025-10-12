using AlUmmahNow.VisualImporterUno.ViewModels;
using AlUmmahNow.VisualImporterUno.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace AlUmmahNow.VisualImporterUno.Views
{
    public sealed partial class DashboardView : Page
    {
        public ServiceStatusViewModel? ViewModel { get; private set; }

        public DashboardView()
        {
            this.InitializeComponent();
            
            try
            {
                ViewModel = App.GetService<ServiceStatusViewModel>();
                this.DataContext = ViewModel;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get ServiceStatusViewModel: {ex.Message}");
                
                // Create a fallback viewmodel to prevent complete failure
                try
                {
                    var signalRClient = App.GetService<FileScanSignalRClient>();
                    var serviceManager = App.GetService<FileScanServiceManager>();
                    ViewModel = new ServiceStatusViewModel(signalRClient, serviceManager);
                    this.DataContext = ViewModel;
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Fallback also failed: {fallbackEx.Message}");
                    // As a last resort, create a minimal UI
                    // The XAML will show empty/default values
                    ViewModel = null;
                }
            }
        }

        private void StartNewImportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Navigate to Import page via the main window
                if (App.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.NavigateToImportView();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to import view: {ex.Message}");
            }
        }
    }
}
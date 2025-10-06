using AlUmmahNow.VisualImporter.ViewModels;
using AlUmmahNow.VisualImporter.Services;
using AlUmmahNow.VisualImporter.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AlUmmahNow.VisualImporter
{
    public sealed partial class MainWindow : Window
    {
        public enum ViewState
        {
            Dashboard,
            Import,
            ServiceStatus,
            Settings
        }

        private ViewState _currentView = ViewState.Dashboard;
        private ServiceStatusViewModel? _serviceStatusViewModel;
        private bool _isClosing = false;

        public MainWindow()
        {
            this.InitializeComponent();
            
            // Initialize service status monitoring
            InitializeServiceStatusMonitoring();
            
            // Set initial navigation to Dashboard
            ContentFrame.Navigate(typeof(DashboardView));
            
            // Set initial status
            UpdateStatusBarForCurrentView();
        }

        private void InitializeServiceStatusMonitoring()
        {
            try
            {
                // Get the service status view model from DI container
                _serviceStatusViewModel = App.GetService<ServiceStatusViewModel>();
                
                // Subscribe to service status changes
                if (_serviceStatusViewModel != null)
                {
                    _serviceStatusViewModel.PropertyChanged += ServiceStatusViewModel_PropertyChanged;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize service status monitoring: {ex.Message}");
                // Continue without service monitoring if it fails
            }
        }

        private void ServiceStatusViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Skip property change handling if window is closing
            if (_isClosing) return;

            try
            {
                if (e.PropertyName == nameof(ServiceStatusViewModel.ServiceHealthStatus) ||
                    e.PropertyName == nameof(ServiceStatusViewModel.ServiceHealthMessage) ||
                    e.PropertyName == nameof(ServiceStatusViewModel.ServiceStatus) ||
                    e.PropertyName == nameof(ServiceStatusViewModel.ServiceStatusMessage))
                {
                    UpdateStatusBarForCurrentView();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling service status property change: {ex.Message}");
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isClosing) return;

            try
            {
                if (args.SelectedItem is NavigationViewItem selectedItem)
                {
                    var tag = selectedItem.Tag?.ToString();
                    switch (tag)
                    {
                        case "Dashboard":
                            _currentView = ViewState.Dashboard;
                            ContentFrame.Navigate(typeof(DashboardView));
                            break;
                        case "Import":
                            _currentView = ViewState.Import;
                            ContentFrame.Navigate(typeof(ImportView));
                            break;
                        case "ServiceStatus":
                            _currentView = ViewState.ServiceStatus;
                            ContentFrame.Navigate(typeof(ServiceStatusPage));
                            break;
                        case "Settings":
                            _currentView = ViewState.Settings;
                            ContentFrame.Navigate(typeof(SettingsPage));
                            break;
                    }
                    
                    UpdateStatusBarForCurrentView();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during navigation: {ex.Message}");
            }
        }

        private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation failed: {e.Exception?.Message}");
            // Fallback to Dashboard
            if (!_isClosing)
            {
                try
                {
                    ContentFrame.Navigate(typeof(DashboardView));
                    _currentView = ViewState.Dashboard;
                    UpdateStatusBarForCurrentView();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in navigation fallback: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Navigate to the import view
        /// </summary>
        public void NavigateToImportView()
        {
            if (_isClosing) return;

            try
            {
                // Update navigation view selection
                foreach (var item in NavView.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Import")
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to import view: {ex.Message}");
            }
        }

        /// <summary>
        /// Navigate to the dashboard view
        /// </summary>
        public void NavigateToDashboardView()
        {
            if (_isClosing) return;

            try
            {
                // Update navigation view selection
                foreach (var item in NavView.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Dashboard")
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to dashboard view: {ex.Message}");
            }
        }

        /// <summary>
        /// Navigate to the settings view
        /// </summary>
        public void NavigateToSettingsView()
        {
            if (_isClosing) return;

            try
            {
                // Update navigation view selection
                foreach (var item in NavView.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Settings")
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to settings view: {ex.Message}");
            }
        }

        /// <summary>
        /// Update status bar text based on current view and service status
        /// </summary>
        private void UpdateStatusBarForCurrentView()
        {
            if (_isClosing) return;

            try
            {
                string baseStatus = _currentView switch
                {
                    ViewState.Dashboard => "Dashboard",
                    ViewState.Import => "Import",
                    ViewState.ServiceStatus => "Service Status",
                    ViewState.Settings => "Settings",
                    _ => "Ready"
                };

                string serviceStatus = GetServiceStatusText();
                
                string statusText = !string.IsNullOrEmpty(serviceStatus) 
                    ? $"{baseStatus} - {serviceStatus}"
                    : baseStatus;

                // Ensure we're on the UI thread
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isClosing && StatusBarTextBlock != null)
                    {
                        StatusBarTextBlock.Text = statusText;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating status bar: {ex.Message}");
            }
        }

        private string GetServiceStatusText()
        {
            try
            {
                if (_serviceStatusViewModel == null)
                    return "Service status unavailable";

                // Prioritize service health status for more detailed information
                var healthStatus = _serviceStatusViewModel.ServiceHealthStatus;
                var connectionStatus = _serviceStatusViewModel.ServiceStatus;
                
                return healthStatus switch
                {
                    ServiceHealthStatus.Running when connectionStatus == ServiceStatus.Connected => 
                        $"Service running & connected{(_serviceStatusViewModel.IsServiceManaged ? " (managed)" : "")}",
                    ServiceHealthStatus.Running when connectionStatus != ServiceStatus.Connected =>
                        $"Service running, not connected{(_serviceStatusViewModel.IsServiceManaged ? " (managed)" : "")}",
                    ServiceHealthStatus.Starting => "Service starting...",
                    ServiceHealthStatus.Stopping => "Service stopping...",
                    ServiceHealthStatus.Stopped => "Service stopped",
                    ServiceHealthStatus.Error => "Service error",
                    ServiceHealthStatus.NotFound => "Service not found",
                    ServiceHealthStatus.Unknown => "Service status unknown",
                    _ => _serviceStatusViewModel.ServiceHealthMessage ?? "Service status unknown"
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting service status text: {ex.Message}");
                return "Service status error";
            }
        }

        /// <summary>
        /// Update scan progress (called by ViewModel)
        /// </summary>
        public void UpdateScanProgress(double progressPercentage, string currentOperation, int processedFiles, int totalFiles, int filesFound)
        {
            if (_isClosing) return;

            try
            {
                // Only update if we're in import view
                if (_currentView == ViewState.Import)
                {
                    var statusText = $"Scanning: {currentOperation} - {processedFiles}/{totalFiles} folders ({filesFound} files found)";
                    
                    // Ensure we're on the UI thread
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!_isClosing && StatusBarTextBlock != null)
                        {
                            StatusBarTextBlock.Text = statusText;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating scan progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Update operation progress (called by ViewModel)
        /// </summary>
        public void UpdateOperationProgress(double globalProgress, double localProgress, string operation, int processedFiles, int totalFiles, string currentItem)
        {
            if (_isClosing) return;

            try
            {
                // Update status bar with operation details
                if (_currentView == ViewState.Import)
                {
                    string statusText;
                    if (!string.IsNullOrEmpty(currentItem))
                    {
                        statusText = $"Import: {operation} - {processedFiles}/{totalFiles} files - {currentItem}";
                    }
                    else
                    {
                        statusText = $"Import: {operation} - {processedFiles}/{totalFiles} files";
                    }
                    
                    // Ensure we're on the UI thread
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!_isClosing && StatusBarTextBlock != null)
                        {
                            StatusBarTextBlock.Text = statusText;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating operation progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Get current view state (for testing/debugging)
        /// </summary>
        public ViewState CurrentView => _currentView;

        /// <summary>
        /// Cleanup when window is closed
        /// </summary>
        private void Window_Closed(object sender, WindowEventArgs args)
        {
            _isClosing = true; // Set flag to prevent further operations

            try
            {
                System.Diagnostics.Debug.WriteLine("MainWindow closing - starting cleanup");

                // Unsubscribe from events
                if (_serviceStatusViewModel != null)
                {
                    try
                    {
                        _serviceStatusViewModel.PropertyChanged -= ServiceStatusViewModel_PropertyChanged;
                        System.Diagnostics.Debug.WriteLine("Unsubscribed from service status events");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error unsubscribing from service status events: {ex.Message}");
                    }

                    try
                    {
                        _serviceStatusViewModel.Dispose();
                        System.Diagnostics.Debug.WriteLine("Disposed service status view model");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error disposing service status view model: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine("MainWindow cleanup completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during window cleanup: {ex.Message}");
            }
        }
    }
}
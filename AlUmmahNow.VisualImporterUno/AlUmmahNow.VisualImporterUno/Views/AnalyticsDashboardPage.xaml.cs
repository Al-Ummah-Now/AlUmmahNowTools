using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace AlUmmahNow.VisualImporterUno.Views
{
    /// <summary>
    /// Analytics Dashboard Page showing approval metrics and trends
    /// </summary>
    public sealed partial class AnalyticsDashboardPage : Page
    {
        public AnalyticsDashboardViewModel ViewModel { get; }

        public AnalyticsDashboardPage()
        {
            this.InitializeComponent();

            // Get ViewModel from DI container
            try
            {
                ViewModel = App.GetService<AnalyticsDashboardViewModel>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get AnalyticsDashboardViewModel: {ex.Message}");
                throw;
            }

            // Set DataContext
            this.DataContext = ViewModel;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                // Refresh data when navigating to the page
                _ = ViewModel.RefreshAnalyticsCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing analytics on navigation: {ex.Message}");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
        }
    }
}

using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AlUmmahNow.VisualImporterUno.Views
{
    /// <summary>How watched-folder videos and their approvals have gone over a period.</summary>
    public sealed partial class AnalyticsDashboardPage : Page
    {
        public AnalyticsDashboardViewModel ViewModel { get; }

        public AnalyticsDashboardPage()
        {
            ViewModel = App.GetService<AnalyticsDashboardViewModel>();
            InitializeComponent();
            DataContext = ViewModel;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RefreshButton, Loc.Get("Watch_Refresh"));
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Refresh();
        }

        private void OfflineCard_Connected(object? sender, System.EventArgs e) => Refresh();

        private void Refresh()
        {
            ViewModel.RefreshAnalyticsCommand.NotifyCanExecuteChanged();
            if (ViewModel.RefreshAnalyticsCommand.CanExecute(null)) _ = ViewModel.RefreshAnalyticsCommand.ExecuteAsync(null);
        }
    }
}

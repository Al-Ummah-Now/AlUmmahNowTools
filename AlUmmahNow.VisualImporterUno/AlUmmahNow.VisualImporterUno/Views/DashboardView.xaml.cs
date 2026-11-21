using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AlUmmahNow.VisualImporterUno.Views
{
    /// <summary>The workspace home: the folder scanner, bulk import, scans running now and problems to act on.</summary>
    public sealed partial class DashboardView : Page
    {
        public ServiceStatusViewModel ViewModel { get; }

        public DashboardView()
        {
            ViewModel = App.GetService<ServiceStatusViewModel>();
            InitializeComponent();
            DataContext = ViewModel;
        }

        private void StartNewImportButton_Click(object sender, RoutedEventArgs e) => (App.Current.MainWindow as MainWindow)?.NavigateToImportView();

        private void WatchedFolders_Click(object sender, RoutedEventArgs e) => (App.Current.MainWindow as MainWindow)?.NavigateToWatchManagementView();

        private void ScannerDetails_Click(object sender, RoutedEventArgs e) => (App.Current.MainWindow as MainWindow)?.NavigateToServiceStatusView();
    }
}

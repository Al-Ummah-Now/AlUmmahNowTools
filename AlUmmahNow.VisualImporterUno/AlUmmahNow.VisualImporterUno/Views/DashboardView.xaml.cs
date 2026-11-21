using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;

namespace AlUmmahNow.VisualImporterUno.Views
{
    /// <summary>The workspace home: what waits for approval, the folder scanner, bulk import and problems to act on.</summary>
    public sealed partial class DashboardView : Page
    {
        public ServiceStatusViewModel ViewModel { get; }

        public DashboardView()
        {
            ViewModel = App.GetService<ServiceStatusViewModel>();
            _scanner = App.GetService<FileScanSignalRClient>();
            InitializeComponent();
            DataContext = ViewModel;
            Unloaded += (_, _) => _scanner.ServiceStatusChanged -= OnScannerStatus;
        }

        private readonly FileScanSignalRClient _scanner;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _scanner.ServiceStatusChanged += OnScannerStatus;
            _ = LoadFiguresAsync();
        }

        private void OnScannerStatus(object? sender, ServiceStatusEventArgs e) => DispatcherQueue.TryEnqueue(() => _ = LoadFiguresAsync());

        /// <summary>The watched-folder figures, read from the scanner; "–" while it isn't connected.</summary>
        private async System.Threading.Tasks.Task LoadFiguresAsync()
        {
            if (!_scanner.IsConnected)
            {
                ShowFigures(null, null, null, null);
                return;
            }
            try
            {
                var watches = await _scanner.GetActiveWatchesAsync();
                ShowFigures(watches.Sum(w => w.PendingApprovals), watches.Sum(w => w.TotalFilesDetected), watches.Count(w => w.IsActive), watches.Count);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Couldn't read the watched-folder figures for the dashboard");
                ShowFigures(null, null, null, null);
            }
        }

        private void ShowFigures(int? waiting, int? arrived, int? watching, int? folders)
        {
            WaitingValue.Text = waiting is int w ? Loc.Number(w) : "–";
            ArrivedValue.Text = arrived is int a ? Loc.Number(a) : "–";
            WatchingValue.Text = watching is int n ? Loc.Number(n) : "–";
            WatchingHint.Text = folders is int f ? Loc.Format("Dashboard_WatchingOf", f) : Loc.Get("Dashboard_ScannerNeeded");
            ReviewLink.Visibility = waiting > 0 ? Visibility.Visible : Visibility.Collapsed;
            WaitingHint.Visibility = waiting > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Approvals_Click(object sender, RoutedEventArgs e) => (App.Current.MainWindow as MainWindow)?.NavigateToApprovalWorkflowView();

        private void StartNewImportButton_Click(object sender, RoutedEventArgs e) => (App.Current.MainWindow as MainWindow)?.NavigateToImportView();

        private void WatchedFolders_Click(object sender, RoutedEventArgs e) => (App.Current.MainWindow as MainWindow)?.NavigateToWatchManagementView();

        private void ScannerDetails_Click(object sender, RoutedEventArgs e) => (App.Current.MainWindow as MainWindow)?.NavigateToServiceStatusView();
    }
}

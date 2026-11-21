using AlUmmahNow.VisualImporterUno.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;

namespace AlUmmahNow.VisualImporterUno.Shell
{
    /// <summary>The app's pages behind the navigation, shown by the shell once the account is staff.</summary>
    public sealed partial class WorkspaceView : UserControl, IDisposable
    {
        public enum ViewState
        {
            Dashboard,
            Import,
            WatchManagement,
            ApprovalWorkflow,
            Analytics,
            ServiceStatus,
            Settings,
            FFmpegSettings
        }

        private static readonly (ViewState View, Type Page, string Key)[] Pages =
        {
            (ViewState.Dashboard, typeof(DashboardView), "Dashboard"),
            (ViewState.Import, typeof(ImportView), "Import"),
            (ViewState.WatchManagement, typeof(WatchManagementPage), "WatchManagement"),
            (ViewState.ApprovalWorkflow, typeof(ApprovalWorkflowPage), "ApprovalWorkflow"),
            (ViewState.Analytics, typeof(AnalyticsDashboardPage), "Analytics"),
            (ViewState.ServiceStatus, typeof(ServiceStatusPage), "ServiceStatus"),
            (ViewState.Settings, typeof(SettingsPage), "Settings"),
            (ViewState.FFmpegSettings, typeof(FFmpegSettingsPage), "FFmpegSettings"),
        };

        private ViewState _currentView = ViewState.Dashboard;
        private bool _isClosing;

        public WorkspaceView(string? startPage = null)
        {
            InitializeComponent();
            var start = Pages.FirstOrDefault(p => string.Equals(p.Key, startPage, StringComparison.OrdinalIgnoreCase));
            Select(start.Page == null ? ViewState.Dashboard : start.View);
        }

        public ViewState CurrentView => _currentView;

        public void NavigateToImportView() => Select(ViewState.Import);
        public void NavigateToDashboardView() => Select(ViewState.Dashboard);
        public void NavigateToSettingsView() => Select(ViewState.Settings);
        public void NavigateToWatchManagementView() => Select(ViewState.WatchManagement);
        public void NavigateToApprovalWorkflowView() => Select(ViewState.ApprovalWorkflow);
        public void NavigateToAnalyticsView() => Select(ViewState.Analytics);
        public void NavigateToServiceStatusView() => Select(ViewState.ServiceStatus);

        private void Select(ViewState view)
        {
            if (_isClosing) return;
            var key = Pages.First(p => p.View == view).Key;
            var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == key);
            if (item == null) return;
            if (ReferenceEquals(NavView.SelectedItem, item)) Show(view);
            else NavView.SelectedItem = item;
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isClosing || args.SelectedItem is not NavigationViewItem item) return;
            var page = Pages.FirstOrDefault(p => p.Key == (string?)item.Tag);
            if (page.Page != null) Show(page.View);
        }

        private void Show(ViewState view)
        {
            var page = Pages.First(p => p.View == view);
            _currentView = view;
            PageTitle.Text = Loc.Get($"Page_{page.Key}_Title");
            PageSubtitle.Text = Loc.Get($"Page_{page.Key}_Subtitle");
            if (ContentFrame.CurrentSourcePageType == page.Page) return;
            try
            {
                ContentFrame.Navigate(page.Page);
                // A page that opens with a text box first (Settings) would otherwise open mid-edit, caret blinking;
                // focus stays with the menu item that opened it
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (XamlRoot != null && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is TextBox or AutoSuggestBox or PasswordBox)
                        (NavView.SelectedItem as NavigationViewItem)?.Focus(FocusState.Programmatic);
                });
            }
            catch (Exception ex)
            {
                // A page that can't be built must not leave the workspace blank: the dashboard opens instead
                Serilog.Log.Error(ex, "Couldn't open the {Page} page", page.Key);
                if (view != ViewState.Dashboard) Select(ViewState.Dashboard);
            }
        }

        private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            // The menu button only earns its place when the pane is folded; a wide window keeps the pane open
            sender.IsPaneToggleButtonVisible = args.DisplayMode != NavigationViewDisplayMode.Expanded;
            // Folded away, the menu button sits at the start of the top row; the title steps past it
            PageHeader.Margin = args.DisplayMode == NavigationViewDisplayMode.Minimal ? new Thickness(64, 12, 16, 8) : new Thickness(24, 20, 24, 12);
            PageSubtitle.Visibility = args.DisplayMode == NavigationViewDisplayMode.Minimal ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation failed: {e.Exception?.Message}");
            e.Handled = true;
            if (!_isClosing && PageOf(ViewState.Dashboard) != e.SourcePageType) Select(ViewState.Dashboard);
        }

        private static Type PageOf(ViewState state) => Pages.First(p => p.View == state).Page;

        /// <summary>Shows a scan's progress in the strip under the pages.</summary>
        public void UpdateScanProgress(double progressPercentage, string currentOperation, int processedFiles, int totalFiles, int filesFound) =>
            ShowProgress(progressPercentage, Loc.Format("Progress_Scanning", processedFiles, totalFiles, filesFound));

        /// <summary>Shows an import's progress in the strip under the pages.</summary>
        public void UpdateOperationProgress(double globalProgress, double localProgress, string operation, int processedFiles, int totalFiles, string currentItem) =>
            ShowProgress(globalProgress, string.IsNullOrEmpty(currentItem)
                ? Loc.Format("Progress_Importing", processedFiles, totalFiles)
                : Loc.Format("Progress_ImportingItem", processedFiles, totalFiles, currentItem));

        private void ShowProgress(double percent, string text)
        {
            if (_isClosing) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosing) return;
                var running = percent < 100;
                ProgressStrip.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
                ProgressText.Text = text;
                ProgressValue.Value = percent;
            });
        }

        public void Dispose()
        {
            _isClosing = true;
        }
    }
}

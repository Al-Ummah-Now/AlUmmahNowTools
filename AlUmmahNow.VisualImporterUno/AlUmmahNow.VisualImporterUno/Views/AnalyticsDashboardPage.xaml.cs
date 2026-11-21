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

        /// <summary>The period buttons and the actions share a row while both fit; otherwise the actions go underneath.</summary>
        private void PeriodBar_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        {
            PeriodButtons.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            PeriodActions.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var stacked = PeriodButtons.DesiredSize.Width + PeriodBar.ColumnSpacing + PeriodActions.DesiredSize.Width > e.NewSize.Width;
            Grid.SetRow(PeriodActions, stacked ? 1 : 0);
            Grid.SetColumn(PeriodActions, stacked ? 0 : 1);
            PeriodActions.HorizontalAlignment = stacked ? Microsoft.UI.Xaml.HorizontalAlignment.Left : Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        }

        private BarStrip? _dailyStrip;

        private void DailyStrip_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _dailyStrip = (BarStrip)sender;
            _dailyStrip.FillWidth = DailyScroll.ActualWidth;
        }

        private void DailyScroll_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        {
            if (_dailyStrip != null) _dailyStrip.FillWidth = e.NewSize.Width;
        }

        private void OfflineCard_Connected(object? sender, System.EventArgs e) => Refresh();

        private void Refresh()
        {
            ViewModel.RefreshAnalyticsCommand.NotifyCanExecuteChanged();
            if (ViewModel.RefreshAnalyticsCommand.CanExecute(null)) _ = ViewModel.RefreshAnalyticsCommand.ExecuteAsync(null);
        }
    }
}

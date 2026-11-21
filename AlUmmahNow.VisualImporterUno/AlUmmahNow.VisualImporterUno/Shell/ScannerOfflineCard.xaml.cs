using AlUmmahNow.VisualImporterUno.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Shown in place of a page's content while the folder scanner isn't reachable, with the way to turn it on.
/// Pages bind their content to <see cref="IsConnected"/> and refresh on <see cref="Connected"/>.
/// </summary>
public sealed partial class ScannerOfflineCard : UserControl
{
    public static readonly DependencyProperty IsConnectedProperty =
        DependencyProperty.Register(nameof(IsConnected), typeof(bool), typeof(ScannerOfflineCard), new PropertyMetadata(false));

    private readonly FileScanSignalRClient _client;
    private readonly DispatcherQueue _dispatcher;

    public ScannerOfflineCard()
    {
        InitializeComponent();
        _client = App.GetService<FileScanSignalRClient>();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Detail.Text = Loc.Get("Offline_Detail");
        Loaded += OnLoaded;
        Unloaded += (_, _) => _client.ServiceStatusChanged -= OnStatusChanged;
        Apply(_client.IsConnected);
    }

    /// <summary>The scanner is reachable; the card hides itself</summary>
    public bool IsConnected
    {
        get => (bool)GetValue(IsConnectedProperty);
        private set => SetValue(IsConnectedProperty, value);
    }

    /// <summary>The scanner became reachable after the page opened</summary>
    public event EventHandler? Connected;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _client.ServiceStatusChanged += OnStatusChanged;
        if (!_client.IsConnected) await TryConnectAsync();
    }

    private void OnStatusChanged(object? sender, ServiceStatusEventArgs e) =>
        _dispatcher.TryEnqueue(() => Apply(_client.IsConnected));

    private void Apply(bool connected)
    {
        var wasConnected = IsConnected;
        IsConnected = connected;
        Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        if (connected && !wasConnected) Connected?.Invoke(this, EventArgs.Empty);
    }

    private async Task TryConnectAsync()
    {
        RetryButton.IsEnabled = false;
        try
        {
            Apply(await _client.ConnectAsync());
        }
        finally
        {
            RetryButton.IsEnabled = true;
        }
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await TryConnectAsync();

    private void OpenScanner_Click(object sender, RoutedEventArgs e) =>
        (App.Current.MainWindow as MainWindow)?.NavigateToServiceStatusView();
}

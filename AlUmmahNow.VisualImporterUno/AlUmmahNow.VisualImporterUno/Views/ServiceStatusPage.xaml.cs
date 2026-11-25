using Microsoft.UI.Xaml.Controls;
using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;

namespace AlUmmahNow.VisualImporterUno.Views;

/// <summary>The folder scanner: its state, its process, the scans it runs and everything it reported.</summary>
public sealed partial class ServiceStatusPage : Page
{
    public ServiceStatusViewModel ViewModel { get; }

    public ServiceStatusPage()
    {
        ViewModel = App.GetService<ServiceStatusViewModel>();
        InitializeComponent();
        DemoDialogs.OnLoaded(this, "clear", ClearAsync);
        DemoDialogs.OnLoaded(this, "scanner-off", () =>
        {
            foreach (var minutes in new[] { 3, 6 }) ViewModel.OngoingOperations.Add(new OperationInfo { SessionId = Guid.NewGuid(), ItemsProcessed = 40 * minutes, StartTime = DateTime.Now.AddMinutes(-minutes) });
            (ViewModel.HasActiveOperation, ViewModel.ActiveOperationsText) = (true, Loc.Number(2));
            return ServiceStatusViewModel.AskToStopAsync(ViewModel.OngoingOperations.Count);
        });
    }

    private async void Clear_Click(object sender, RoutedEventArgs e) => await ClearAsync();

    /// <summary>The problems go too, and with them their tries again.</summary>
    private async Task ClearAsync()
    {
        if (await ConfirmDialog.AskAsync(new ConfirmRequest(Loc.Get("Scanner_ClearTitle"), [Loc.Get("Scanner_ClearBody"), Loc.Get("Scanner_ClearBody2")], Loc.Get("Scanner_ClearConfirm")), XamlRoot))
        {
            ViewModel.ClearActivityCommand.Execute(null);
            AppDialogs.KeepFocus(CheckButton);
        }
    }
}

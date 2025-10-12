using Microsoft.UI.Xaml.Controls;
using AlUmmahNow.VisualImporterUno.ViewModels;

namespace AlUmmahNow.VisualImporterUno.Views;

public sealed partial class ServiceStatusPage : Page
{
    public ServiceStatusViewModel ViewModel { get; }

    public ServiceStatusPage()
    {
        this.InitializeComponent();
        ViewModel = App.GetService<ServiceStatusViewModel>();
        
        // Auto-connect when page loads
        this.Loaded += async (s, e) =>
        {
            await ViewModel.ConnectToServiceCommand.ExecuteAsync(null);
        };
    }
}

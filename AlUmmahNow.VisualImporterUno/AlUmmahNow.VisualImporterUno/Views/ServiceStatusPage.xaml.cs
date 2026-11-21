using Microsoft.UI.Xaml.Controls;
using AlUmmahNow.VisualImporterUno.ViewModels;

namespace AlUmmahNow.VisualImporterUno.Views;

/// <summary>The folder scanner: its state, its process, the scans it runs and everything it reported.</summary>
public sealed partial class ServiceStatusPage : Page
{
    public ServiceStatusViewModel ViewModel { get; }

    public ServiceStatusPage()
    {
        ViewModel = App.GetService<ServiceStatusViewModel>();
        InitializeComponent();
    }
}

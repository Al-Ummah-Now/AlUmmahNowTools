using Microsoft.UI.Xaml.Controls;
using AlUmmahNow.VisualImporterUno.ViewModels;

namespace AlUmmahNow.VisualImporterUno.Views
{
    public sealed partial class FileDiscoveryPage : Page
    {
        public MainViewModel ViewModel { get; }

        public FileDiscoveryPage()
        {
            this.InitializeComponent();
            ViewModel = App.GetService<MainViewModel>();
        }
    }
}
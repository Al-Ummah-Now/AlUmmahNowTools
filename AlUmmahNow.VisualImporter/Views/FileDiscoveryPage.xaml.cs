using Microsoft.UI.Xaml.Controls;
using AlUmmahNow.VisualImporter.ViewModels;

namespace AlUmmahNow.VisualImporter.Views
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
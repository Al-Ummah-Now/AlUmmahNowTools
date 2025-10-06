using AlUmmahNow.VisualImporter.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace AlUmmahNow.VisualImporter.Views
{
    public sealed partial class ImportView : Page
    {
        public MainViewModel ViewModel { get; }

        public ImportView()
        {
            this.InitializeComponent();
            ViewModel = App.GetService<MainViewModel>();
            this.DataContext = ViewModel;
        }

        private void BackToDashboardButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Navigate back to Dashboard via the main window
                if (App.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.NavigateToDashboardView();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to dashboard: {ex.Message}");
            }
        }
    }
}
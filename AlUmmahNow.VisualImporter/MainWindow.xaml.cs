using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using AlUmmahNow.VisualImporter.ViewModels;

namespace AlUmmahNow.VisualImporter
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = App.GetService<MainViewModel>();
            
            // Set window icon
            SetWindowIcon();
            
            // Subscribe to ViewModel property changes
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Initial load
            _ = ViewModel.RefreshSessionsCommand.ExecuteAsync(null);
        }

        private void SetWindowIcon()
        {
            try
            {
                // Set the window icon using the app icon
                var iconUri = new System.Uri("ms-appx:///Assets/Square44x44Logo.scale-200.png");
                var iconSource = new BitmapImage(iconUri);
                this.AppWindow.SetIcon("Assets/Square44x44Logo.scale-200.png");
            }
            catch
            {
                // Fallback - icon setting is not critical for functionality
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(ViewModel.IsImporting):
                        ProgressPanel.Visibility = ViewModel.IsImporting ? Visibility.Visible : Visibility.Collapsed;
                        StartImportButton.IsEnabled = !ViewModel.IsImporting && !string.IsNullOrEmpty(ViewModel.SelectedFolderPath);
                        break;
                    case nameof(ViewModel.StatusMessage):
                        StatusTextBlock.Text = ViewModel.StatusMessage;
                        StatusBarTextBlock.Text = ViewModel.StatusMessage;
                        break;
                    case nameof(ViewModel.ProgressPercentage):
                        ImportProgressBar.Value = ViewModel.ProgressPercentage;
                        break;
                    case nameof(ViewModel.SelectedFolderPath):
                        FolderPathTextBox.Text = ViewModel.SelectedFolderPath;
                        StartImportButton.IsEnabled = !ViewModel.IsImporting && !string.IsNullOrEmpty(ViewModel.SelectedFolderPath);
                        break;
                    case nameof(ViewModel.CurrentImportStatus):
                        UpdateSessionDetails();
                        break;
                    case nameof(ViewModel.SelectedSession):
                        UpdateButtonStates();
                        break;
                    case nameof(ViewModel.ImportSessions):
                        UpdateSessionsList();
                        break;
                    case nameof(ViewModel.SessionVideos):
                        UpdateVideosList();
                        break;
                }
            });
        }

        private void UpdateSessionDetails()
        {
            if (ViewModel.CurrentImportStatus != null)
            {
                SessionDetailsPanel.Visibility = Visibility.Visible;
                ProgressTextBlock.Text = $"{ViewModel.CurrentImportStatus.ProgressPercentage:F1}%";
                VideosProcessedTextBlock.Text = $"{ViewModel.CurrentImportStatus.VideosProcessed}/{ViewModel.CurrentImportStatus.TotalVideosFound}";
                UploadedTextBlock.Text = ViewModel.CurrentImportStatus.VideosUploaded.ToString();
                FailedTextBlock.Text = ViewModel.CurrentImportStatus.VideosFailed.ToString();
            }
            else
            {
                SessionDetailsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateButtonStates()
        {
            var hasSelection = ViewModel.SelectedSession != null;
            ViewDetailsButton.IsEnabled = hasSelection;
            ResumeButton.IsEnabled = hasSelection;
            CancelButton.IsEnabled = hasSelection;
        }

        private void UpdateSessionsList()
        {
            SessionsListView.Items.Clear();
            foreach (var session in ViewModel.ImportSessions)
            {
                var border = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(12),
                    Tag = session
                };

                var stackPanel = new StackPanel();

                var folderText = new TextBlock
                {
                    Text = session.RootFolder,
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                stackPanel.Children.Add(folderText);

                var statusText = new TextBlock
                {
                    Text = $"Status: {session.Status}",
                    Margin = new Thickness(0, 4, 0, 0)
                };
                stackPanel.Children.Add(statusText);

                var timeText = new TextBlock
                {
                    Text = session.StartTime.ToString(),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Margin = new Thickness(0, 2, 0, 0)
                };
                stackPanel.Children.Add(timeText);

                var progressText = new TextBlock
                {
                    Text = $"Videos: {session.VideosUploaded}/{session.TotalVideosFound} uploaded",
                    Margin = new Thickness(0, 2, 0, 0)
                };
                stackPanel.Children.Add(progressText);

                var progressBar = new ProgressBar
                {
                    Value = session.VideosProcessed,
                    Maximum = session.TotalVideosFound,
                    Height = 4,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                stackPanel.Children.Add(progressBar);

                border.Child = stackPanel;
                SessionsListView.Items.Add(border);
            }
        }

        private void UpdateVideosList()
        {
            VideosListView.Items.Clear();
            foreach (var video in ViewModel.SessionVideos)
            {
                var border = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4),
                    Margin = new Thickness(0, 1, 0, 1),
                    Padding = new Thickness(8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftPanel = new StackPanel();
                Grid.SetColumn(leftPanel, 0);

                var fileNameText = new TextBlock
                {
                    Text = video.FileName,
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                leftPanel.Children.Add(fileNameText);

                var channelText = new TextBlock
                {
                    Text = video.Channel?.ChannelName ?? "",
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Margin = new Thickness(0, 2, 0, 0)
                };
                leftPanel.Children.Add(channelText);

                var rightPanel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(rightPanel, 1);

                var statusBorder = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorAttentionBrush"],
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(12),
                    Padding = new Thickness(8, 2, 8, 2),
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var statusText = new TextBlock
                {
                    Text = video.Status.ToString(),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                statusBorder.Child = statusText;
                rightPanel.Children.Add(statusBorder);

                grid.Children.Add(leftPanel);
                grid.Children.Add(rightPanel);
                border.Child = grid;
                VideosListView.Items.Add(border);
            }
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.SelectFolderCommand.ExecuteAsync(null);
        }

        private async void StartImportButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.StartImportCommand.ExecuteAsync(null);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.RefreshSessionsCommand.ExecuteAsync(null);
        }

        private async void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ViewSessionDetailsCommand.ExecuteAsync(null);
        }

        private async void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ResumeImportCommand.ExecuteAsync(null);
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.CancelImportCommand.ExecuteAsync(null);
        }

        private async void SessionsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Border border && border.Tag is AlUmmahNow.Data.Models.ImportSession session)
            {
                ViewModel.SelectedSession = session;
                await ViewModel.ViewSessionDetailsCommand.ExecuteAsync(null);
            }
        }
    }
}

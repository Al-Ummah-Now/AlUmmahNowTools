using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using AlUmmahNow.VisualImporter.ViewModels;
using AlUmmahNow.VisualImporter.Models;
using AlUmmahNow.VisualImporter.Services;
using Microsoft.UI;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace AlUmmahNow.VisualImporter
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainViewModel? ViewModel { get; private set; }

        public MainWindow()
        {
            try
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1300,
                    "MainWindow Initialization Started", "Beginning main window setup");

                InitializeComponent();
                
                // Try to get the ViewModel safely with multiple fallback strategies
                try
                {
                    ViewModel = App.GetService<MainViewModel>();
                    WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1301,
                        "ViewModel Initialized Successfully", "MainViewModel service retrieved successfully");
                }
                catch (Exception ex)
                {
                    // Log the error but don't crash
                    WindowsEventLogger.LogServiceInitializationFailure("MainViewModel", ex);
                    
                    // Try alternative initialization
                    try
                    {
                        // Emergency fallback: create services manually
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 1302,
                            "Attempting Emergency ViewModel Initialization", "Primary ViewModel initialization failed, trying fallback");
                        // For now, continue without ViewModel
                        ViewModel = null;
                    }
                    catch (Exception fallbackEx)
                    {
                        WindowsEventLogger.LogCriticalCrash(fallbackEx, "Fallback ViewModel Creation");
                        ViewModel = null;
                    }
                }
                
                // Set window icon (non-critical)
                try
                {
                    SetWindowIcon();
                }
                catch (Exception ex)
                {
                    WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 1303,
                        "Window Icon Setup Failed", $"Non-critical error: {ex.Message}");
                }
                
                // Only subscribe to events if ViewModel is available
                if (ViewModel != null)
                {
                    try
                    {
                        // Subscribe to ViewModel property changes
                        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                        
                        // Initial load (run async without waiting)
                        SafeExecuteAsync(async () => await ViewModel.RefreshSessionsCommand.ExecuteAsync(null));
                        
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1304,
                            "ViewModel Events Subscribed", "ViewModel property change events subscribed successfully");
                    }
                    catch (Exception ex)
                    {
                        WindowsEventLogger.LogServiceInitializationFailure("ViewModel Events", ex);
                    }
                }
                else
                {
                    // Set a default status message when ViewModel is not available
                    try
                    {
                        StatusTextBlock.Text = "Service initialization failed - running in limited mode";
                        StatusBarTextBlock.Text = "Service initialization failed - running in limited mode";
                        
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 1305,
                            "Running in Limited Mode", "MainWindow initialized without ViewModel - limited functionality available");
                    }
                    catch (Exception ex)
                    {
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Error, 1306,
                            "Status Message Update Failed", $"Failed to set status message: {ex.Message}");
                    }
                }

                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1307,
                    "MainWindow Initialization Completed", "Main window setup completed successfully");
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "MainWindow Initialization");
                // Don't throw here - let the app continue with a broken but non-crashing window
            }
        }

        /// <summary>
        /// Safely execute async operations with error handling
        /// </summary>
        private async void SafeExecuteAsync(Func<Task> asyncAction)
        {
            try
            {
                await asyncAction();
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "Async Operation");
                
                if (ex is COMException comEx)
                {
                    CrashHandler.TryHandleCOMException(comEx, "SafeExecuteAsync");
                }
            }
        }

        /// <summary>
        /// Safely execute synchronous operations with error handling
        /// </summary>
        private void SafeExecute(Action action, string operationName)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, operationName);
                
                if (ex is COMException comEx)
                {
                    CrashHandler.TryHandleCOMException(comEx, operationName);
                }
            }
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
            if (ViewModel == null) return;
            
            // Use TryEnqueue to safely marshal to UI thread
            var success = DispatcherQueue.TryEnqueue(() =>
            {
                SafeExecute(() => {
                    switch (e.PropertyName)
                    {
                        case nameof(ViewModel.IsImporting):
                            UpdateImportingState();
                            break;
                        case nameof(ViewModel.IsScanning):
                            UpdateScanningState();
                            break;
                        case nameof(ViewModel.StatusMessage):
                            UpdateStatusMessage();
                            break;
                        case nameof(ViewModel.ProgressPercentage):
                            UpdateProgressPercentage();
                            break;
                        case nameof(ViewModel.SelectedFolderPath):
                            UpdateSelectedFolderPath();
                            break;
                        case nameof(ViewModel.HasScannedFiles):
                            UpdateFilesVisibility();
                            UpdateButtonStates();
                            break;
                        case nameof(ViewModel.FilteredVideoFiles):
                            UpdateDiscoveredFilesList();
                            break;
                        case nameof(ViewModel.FileStatistics):
                            UpdateFileStatistics();
                            break;
                        case nameof(ViewModel.AvailableChannels):
                            UpdateChannelFilter();
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
                }, $"PropertyChanged.{e.PropertyName}");
            });
            
            if (!success)
            {
                WindowsEventLogger.LogDispatcherFailure($"PropertyChanged.{e.PropertyName}");
            }
        }

        // Separate methods for each UI update operation to isolate failures
        private void UpdateImportingState()
        {
            try
            {
                ProgressPanel.Visibility = ViewModel.IsImporting ? Visibility.Visible : Visibility.Collapsed;
                StartImportButton.IsEnabled = !ViewModel.IsImporting && ViewModel.CanStartImport;
                UpdateImportingVisibility();
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 2000,
                    "UI Update Error: ImportingState", ex.Message);
            }
        }

        private void UpdateScanningState()
        {
            try
            {
                ScanButton.IsEnabled = !ViewModel.IsScanning && !ViewModel.IsImporting && !string.IsNullOrEmpty(ViewModel.SelectedFolderPath);
                StartImportButton.IsEnabled = !ViewModel.IsImporting && ViewModel.CanStartImport;
                UpdateScanningVisibility();
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 2001,
                    "UI Update Error: ScanningState", ex.Message);
            }
        }

        private void UpdateStatusMessage()
        {
            try
            {
                StatusTextBlock.Text = ViewModel.StatusMessage;
                StatusBarTextBlock.Text = ViewModel.StatusMessage;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 2002,
                    "UI Update Error: StatusMessage", ex.Message);
            }
        }

        private void UpdateProgressPercentage()
        {
            try
            {
                ImportProgressBar.Value = ViewModel.ProgressPercentage;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 2003,
                    "UI Update Error: ProgressPercentage", ex.Message);
            }
        }

        private void UpdateSelectedFolderPath()
        {
            try
            {
                FolderPathTextBox.Text = ViewModel.SelectedFolderPath;
                ScanButton.IsEnabled = !ViewModel.IsScanning && !ViewModel.IsImporting && !string.IsNullOrEmpty(ViewModel.SelectedFolderPath);
                StartImportButton.IsEnabled = !ViewModel.IsImporting && ViewModel.CanStartImport;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 2004,
                    "UI Update Error: SelectedFolderPath", ex.Message);
            }
        }

        private void UpdateScanningVisibility()
        {
            if (ViewModel == null) return;
            
            SafeExecute(() => {
                if (ViewModel.IsScanning)
                {
                    ShowOperationProgress("Scanning for video files", "This may take a moment for large directories");
                    NoFilesPanel.Visibility = Visibility.Collapsed;
                    FilesScrollViewer.Visibility = Visibility.Collapsed;
                    FileStatsPanel.Visibility = Visibility.Collapsed;
                    FileControlsPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    HideOperationProgress();
                    UpdateFilesVisibility();
                }
            }, "UpdateScanningVisibility");
        }

        private void UpdateImportingVisibility()
        {
            if (ViewModel == null) return;
            
            SafeExecute(() => {
                if (ViewModel.IsImporting)
                {
                    ShowOperationProgress("Importing video files", "Uploading selected files to cloud storage");
                    // Keep the files visible but disabled during import
                    FileStatsPanel.Visibility = Visibility.Visible;
                    FileControlsPanel.Visibility = Visibility.Collapsed; // Hide controls during import
                    FilesScrollViewer.Visibility = Visibility.Visible;
                    NoFilesPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    HideOperationProgress();
                    // Re-enable controls after import
                    if (ViewModel.HasScannedFiles)
                    {
                        FileControlsPanel.Visibility = Visibility.Visible;
                    }
                }
            }, "UpdateImportingVisibility");
        }

        private void ShowOperationProgress(string title, string subtitle)
        {
            SafeExecute(() => {
                OperationProgressPanel.Visibility = Visibility.Visible;
                OperationTitleLabel.Text = title;
                OperationSubtitleLabel.Text = subtitle;
                ResetOperationProgress();
            }, "ShowOperationProgress");
        }

        private void HideOperationProgress()
        {
            SafeExecute(() => {
                OperationProgressPanel.Visibility = Visibility.Collapsed;
            }, "HideOperationProgress");
        }

        private void ResetOperationProgress()
        {
            SafeExecute(() => {
                OperationProgressBar.Value = 0;
                OperationProgressLabel.Text = "0%";
                OperationStatusLabel.Text = "Initializing...";
                OperationCounterLabel.Text = "Items processed: 0";
                OperationCurrentItemLabel.Text = "";
            }, "ResetOperationProgress");
        }

        public void UpdateOperationProgress(double globalProgress, double localProgress, string currentOperation, int itemsProcessed, int totalItems, string currentItem = "")
        {
            var success = DispatcherQueue.TryEnqueue(() =>
            {
                SafeExecute(() => {
                    OperationProgressBar.Value = localProgress;
                    OperationProgressLabel.Text = $"{localProgress:F1}%";
                    
                    OperationStatusLabel.Text = currentOperation;
                    
                    if (totalItems > 0)
                    {
                        OperationCounterLabel.Text = $"Items processed: {itemsProcessed}/{totalItems}";
                    }
                    else
                    {
                        OperationCounterLabel.Text = $"Items processed: {itemsProcessed}";
                    }
                    
                    OperationCurrentItemLabel.Text = currentItem;
                }, "UpdateOperationProgress");
            });

            if (!success)
            {
                WindowsEventLogger.LogDispatcherFailure("UpdateOperationProgress");
            }
        }

        // Keep the old method for backward compatibility during scanning
        public void UpdateScanProgress(double progressPercentage, string currentOperation, int foldersScanned, int totalFolders, int filesFound)
        {
            // For scanning, use the same value for both progress bars initially
            UpdateOperationProgress(progressPercentage, progressPercentage, currentOperation, foldersScanned, totalFolders, $"Video files found: {filesFound}");
        }

        private void UpdateFilesVisibility()
        {
            if (ViewModel == null) return;
            
            SafeExecute(() => {
                if (ViewModel.HasScannedFiles)
                {
                    FileStatsPanel.Visibility = Visibility.Visible;
                    FileControlsPanel.Visibility = Visibility.Visible;
                    FilesScrollViewer.Visibility = Visibility.Visible;
                    NoFilesPanel.Visibility = Visibility.Collapsed;
                    OperationProgressPanel.Visibility = Visibility.Collapsed;
                }
                else if (!ViewModel.IsScanning)
                {
                    FileStatsPanel.Visibility = Visibility.Collapsed;
                    FileControlsPanel.Visibility = Visibility.Collapsed;
                    FilesScrollViewer.Visibility = Visibility.Collapsed;
                    NoFilesPanel.Visibility = Visibility.Visible;
                    OperationProgressPanel.Visibility = Visibility.Collapsed;
                }
            }, "UpdateFilesVisibility");
        }

        private void UpdateFileStatistics()
        {
            if (ViewModel?.FileStatistics == null) return;
            
            SafeExecute(() => {
                TotalFilesText.Text = $"{ViewModel.FileStatistics.TotalFiles} files";
                TotalSizeText.Text = ViewModel.FileStatistics.TotalSizeFormatted;
                DuplicatesText.Text = $"{ViewModel.FileStatistics.DuplicateFiles} duplicates";
                
                // Calculate selected files count
                var selectedCount = ViewModel.FilteredVideoFiles?.Count(f => f.IsSelected) ?? 0;
                SelectedFilesText.Text = $"{selectedCount} selected";
                
                // Show import status statistics if any files have been processed
                var hasImportActivity = ViewModel.FileStatistics.UploadedFiles > 0 || ViewModel.FileStatistics.FailedFiles > 0;
                
                if (hasImportActivity)
                {
                    UploadedStatsPanel.Visibility = Visibility.Visible;
                    FailedStatsPanel.Visibility = Visibility.Visible;
                    
                    UploadedStatsText.Text = $"{ViewModel.FileStatistics.UploadedFiles} uploaded";
                    FailedStatsText.Text = $"{ViewModel.FileStatistics.FailedFiles} failed";
                }
                else
                {
                    UploadedStatsPanel.Visibility = Visibility.Collapsed;
                    FailedStatsPanel.Visibility = Visibility.Collapsed;
                }
            }, "UpdateFileStatistics");
        }

        private void UpdateChannelFilter()
        {
            if (ViewModel == null) return;
            
            SafeExecute(() => {
                ChannelFilterComboBox.Items.Clear();
                foreach (var channel in ViewModel.AvailableChannels)
                {
                    ChannelFilterComboBox.Items.Add(channel);
                }
                if (ChannelFilterComboBox.Items.Count > 0)
                {
                    ChannelFilterComboBox.SelectedIndex = 0; // Select "All Channels"
                }
            }, "UpdateChannelFilter");
        }

        private void UpdateDiscoveredFilesList()
        {
            if (ViewModel == null) return;
            
            SafeExecute(() => {
                DiscoveredFilesListView.Items.Clear();
                foreach (var file in ViewModel.FilteredVideoFiles)
                {
                    var border = CreateFileListItem(file);
                    DiscoveredFilesListView.Items.Add(border);
                }
                
                // Update selected count
                UpdateFileStatistics();
            }, "UpdateDiscoveredFilesList");
        }

        private Border CreateFileListItem(VideoFileInfo file)
        {
            try
            {
                var border = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
                    BorderBrush = file.IsDuplicate ? 
                        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"] :
                        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(file.IsDuplicate ? 2 : 1),
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(6),
                    Margin = new Thickness(0, 1, 0, 1),
                    Padding = new Thickness(12),
                    Tag = file
                };

                var mainGrid = new Grid();
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Checkbox
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Thumbnail
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Metadata
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Status/Icons

                // Checkbox
                var checkBox = new CheckBox
                {
                    IsChecked = file.IsSelected,
                    IsEnabled = !file.IsDuplicate,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkBox.Click += (s, e) => 
                {
                    SafeExecute(() => {
                        file.IsSelected = checkBox.IsChecked ?? false;
                        UpdateFileStatistics();
                    }, "FileListItem.CheckBox.Click");
                };
                Grid.SetColumn(checkBox, 0);

                // Thumbnail placeholder
                var thumbnailBorder = new Border
                {
                    Width = 64,
                    Height = 36,
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4),
                    Margin = new Thickness(0, 0, 12, 0)
                };

                var thumbnailIcon = new FontIcon
                {
                    Glyph = file.HasThumbnail ? "\uE8B2" : "\uE714", // Video icon or placeholder
                    FontSize = 20,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                thumbnailBorder.Child = thumbnailIcon;
                Grid.SetColumn(thumbnailBorder, 1);

                // File content
                var contentPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                
                var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                
                var fileNameText = new TextBlock
                {
                    Text = file.FileNameWithoutExtension,
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                };
                titlePanel.Children.Add(fileNameText);

                if (file.IsDuplicate)
                {
                    var duplicateChip = new Border
                    {
                        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"],
                        CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    var duplicateText = new TextBlock
                    {
                        Text = "DUPLICATE",
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    };
                    duplicateChip.Child = duplicateText;
                    titlePanel.Children.Add(duplicateChip);
                }

                contentPanel.Children.Add(titlePanel);

                var detailsText = new TextBlock
                {
                    Text = $"{file.Extension.ToUpper()} • {file.ChannelName}",
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                    Margin = new Thickness(0, 2, 0, 0)
                };
                contentPanel.Children.Add(detailsText);

                var pathText = new TextBlock
                {
                    Text = file.RelativePath,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1,
                    Margin = new Thickness(0, 1, 0, 0)
                };
                contentPanel.Children.Add(pathText);

                Grid.SetColumn(contentPanel, 2);

                // Metadata
                var metadataPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 12, 0) };
                
                var sizeText = new TextBlock
                {
                    Text = file.FileSizeFormatted,
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                metadataPanel.Children.Add(sizeText);

                var dateText = new TextBlock
                {
                    Text = file.ModifiedDate.ToString("MMM dd, yyyy"),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                metadataPanel.Children.Add(dateText);

                Grid.SetColumn(metadataPanel, 3);

                // Status and icons
                var statusPanel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };

                // Import Status Icon and Badge
                var statusContainer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };

                if (file.HasThumbnail)
                {
                    var thumbnailIndicator = new FontIcon
                    {
                        Glyph = "\uE91B", // Image icon
                        FontSize = 14,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                    };
                    ToolTipService.SetToolTip(thumbnailIndicator, "Has thumbnail");
                    statusContainer.Children.Add(thumbnailIndicator);
                }

                // Import Status Badge
                var statusBadge = new Border
                {
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(10),
                    Padding = new Thickness(8, 3, 8, 3),
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                // Set badge color based on import status
                if (file.IsDuplicate)
                {
                    statusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"];
                }
                else
                {
                    statusBadge.Background = file.ImportStatus switch
                    {
                        ImportFileStatus.NotImported => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
                        ImportFileStatus.Pending => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"],
                        ImportFileStatus.Uploading => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                        ImportFileStatus.Uploaded => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"],
                        ImportFileStatus.Failed => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBackgroundBrush"],
                        ImportFileStatus.Skipped => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
                        _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerFillColorDefaultBrush"]
                    };
                }

                var statusBadgeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

                var statusIcon = new FontIcon
                {
                    Glyph = file.StatusIcon,
                    FontSize = 12,
                    Foreground = file.IsDuplicate ? 
                        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"] :
                        file.ImportStatus switch
                        {
                            ImportFileStatus.NotImported => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                            ImportFileStatus.Pending => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorAttentionBrush"],
                            ImportFileStatus.Uploading => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                            ImportFileStatus.Uploaded => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
                            ImportFileStatus.Failed => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                            ImportFileStatus.Skipped => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                            _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                        }
                };
                statusBadgeContent.Children.Add(statusIcon);

                var statusText = new TextBlock
                {
                    Text = file.StatusText,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = file.IsDuplicate ? 
                        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"] :
                        file.ImportStatus switch
                        {
                            ImportFileStatus.NotImported => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                            ImportFileStatus.Pending => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorAttentionBrush"],
                            ImportFileStatus.Uploading => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                            ImportFileStatus.Uploaded => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
                            ImportFileStatus.Failed => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                            ImportFileStatus.Skipped => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                            _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                        }
                };
                statusBadgeContent.Children.Add(statusText);

                statusBadge.Child = statusBadgeContent;
                statusContainer.Children.Add(statusBadge);

                statusPanel.Children.Add(statusContainer);

                Grid.SetColumn(statusPanel, 4);

                mainGrid.Children.Add(checkBox);
                mainGrid.Children.Add(thumbnailBorder);
                mainGrid.Children.Add(contentPanel);
                mainGrid.Children.Add(metadataPanel);
                mainGrid.Children.Add(statusPanel);

                border.Child = mainGrid;
                return border;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 2100,
                    "File List Item Creation Error", $"Failed to create UI element for {file.FileName}: {ex.Message}");
                
                // Return a minimal error border
                return new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBackgroundBrush"],
                    Padding = new Thickness(12),
                    Child = new TextBlock { Text = $"Error displaying: {file.FileName}" }
                };
            }
        }

        private void UpdateSessionDetails()
        {
            SafeExecute(() => {
                if (ViewModel?.CurrentImportStatus == null)
                {
                    SessionDetailsPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                SessionDetailsPanel.Visibility = Visibility.Visible;
                ProgressTextBlock.Text = $"{ViewModel.CurrentImportStatus.ProgressPercentage:F1}%";
                VideosProcessedTextBlock.Text = $"{ViewModel.CurrentImportStatus.VideosProcessed}/{ViewModel.CurrentImportStatus.TotalVideosFound}";
                UploadedTextBlock.Text = ViewModel.CurrentImportStatus.VideosUploaded.ToString();
                FailedTextBlock.Text = ViewModel.CurrentImportStatus.VideosFailed.ToString();
            }, "UpdateSessionDetails");
        }

        private void UpdateButtonStates()
        {
            SafeExecute(() => {
                var hasSelection = ViewModel?.SelectedSession != null;
                ViewDetailsButton.IsEnabled = hasSelection;
                ResumeButton.IsEnabled = hasSelection;
                CancelButton.IsEnabled = hasSelection;
            }, "UpdateButtonStates");
        }

        private void UpdateSessionsList()
        {
            if (ViewModel == null) return;
            
            SafeExecute(() => {
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
            }, "UpdateSessionsList");
        }

        private void UpdateVideosList()
        {
            if (ViewModel == null) return;
            
            SafeExecute(() => {
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
            }, "UpdateVideosList");
        }

        // Event Handlers with error handling
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.SelectFolderCommand.Execute(null), "BrowseButton.Click");
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.ScanFolderCommand.Execute(null), "ScanButton.Click");
        }

        private void StartImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.StartImportCommand.Execute(null), "StartImportButton.Click");
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.RefreshSessionsCommand.Execute(null), "RefreshButton.Click");
        }

        private void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.ViewSessionDetailsCommand.Execute(null), "ViewDetailsButton.Click");
        }

        private void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.ResumeImportCommand.Execute(null), "ResumeButton.Click");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.CancelImportCommand.Execute(null), "CancelButton.Click");
        }

        private void SessionsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null && e.AddedItems.Count > 0 && e.AddedItems[0] is Border border && border.Tag is AlUmmahNow.Data.Models.ImportSession session)
            {
                ViewModel.SelectedSession = session;
                SafeExecute(() => ViewModel.ViewSessionDetailsCommand.Execute(null), "SessionsListView.SelectionChanged");
            }
        }

        // Tab Management
        private void FilesTabButton_Click(object sender, RoutedEventArgs e)
        {
            SafeExecute(() => {
                FilesTabContent.Visibility = Visibility.Visible;
                SessionTabContent.Visibility = Visibility.Collapsed;
            }, "FilesTabButton.Click");
        }

        private void SessionTabButton_Click(object sender, RoutedEventArgs e)
        {
            SafeExecute(() => {
                FilesTabContent.Visibility = Visibility.Collapsed;
                SessionTabContent.Visibility = Visibility.Visible;
            }, "SessionTabButton.Click");
        }

        // File Management
        private void HideDuplicatesCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.HideDuplicates = HideDuplicatesCheckBox.IsChecked ?? false, "HideDuplicatesCheckBox.Click");
        }

        private void ShowOnlySelectedCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.ShowOnlySelected = ShowOnlySelectedCheckBox.IsChecked ?? false, "ShowOnlySelectedCheckBox.Click");
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.SelectAllFilesCommand.Execute(null), "SelectAllButton.Click");
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                SafeExecute(() => ViewModel.DeselectAllFilesCommand.Execute(null), "DeselectAllButton.Click");
        }

        // Sorting and Filtering
        private void SortByComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null && SortByComboBox.SelectedItem is ComboBoxItem item && item.Tag is string sortOption)
            {
                SafeExecute(() => {
                    if (System.Enum.TryParse<VideoSortOption>(sortOption, out var option))
                    {
                        ViewModel.CurrentSortOption = option;
                    }
                }, "SortByComboBox.SelectionChanged");
            }
        }

        private void SortDirectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                SafeExecute(() => {
                    ViewModel.ToggleSortDirectionCommand.Execute(null);
                    SortDirectionButton.Content = ViewModel.CurrentSortDirection == SortDirection.Ascending ? "?" : "?";
                }, "SortDirectionButton.Click");
            }
        }

        private void ChannelFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null && ChannelFilterComboBox.SelectedItem is string selectedChannel)
            {
                SafeExecute(() => {
                    ViewModel.ChannelFilter = selectedChannel == "All Channels" ? null : selectedChannel;
                }, "ChannelFilterComboBox.SelectionChanged");
            }
        }
    }
}
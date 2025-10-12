using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.VisualImporterUno.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AlUmmahNow.VisualImporterUno.ViewModels
{
    /// <summary>
    /// Enhanced MainViewModel that uses database-based scanning instead of direct file system scanning
    /// This prevents UI crashes when scanning large numbers of files
    /// </summary>
    public partial class DatabaseMainViewModel : ObservableObject, IDisposable
    {
        private readonly DatabaseFileScanService _fileScanService;
        private readonly ILogger<DatabaseMainViewModel> _logger;
        private bool _disposed = false;

        // Current scan session
        private Guid? _currentScanSessionId;

        [ObservableProperty]
        private string _selectedFolderPath = string.Empty;

        [ObservableProperty]
        private bool _isScanning = false;

        [ObservableProperty]
        private bool _isImporting = false;

        [ObservableProperty]
        private string _statusMessage = "Ready to scan";

        [ObservableProperty]
        private double _progressPercentage = 0;

        [ObservableProperty]
        private bool _hasScanResults = false;

        [ObservableProperty]
        private ScanSession? _selectedScanSession;

        [ObservableProperty]
        private ScannedChannel? _selectedChannel;

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private int _pageSize = 100;

        [ObservableProperty]
        private int _totalFiles = 0;

        [ObservableProperty]
        private bool _hideDuplicates = true;

        [ObservableProperty]
        private bool _showOnlySelected = false;

        // Collections
        public ObservableCollection<ScanSession> ScanSessions { get; } = new();
        public ObservableCollection<ScannedChannel> Channels { get; } = new();
        public ObservableCollection<VideoFileInfo> ChannelFiles { get; } = new();
        public ObservableCollection<string> AvailableChannels { get; } = new();

        [ObservableProperty]
        private FileStatistics? _fileStatistics;

        public DatabaseMainViewModel(DatabaseFileScanService fileScanService, ILogger<DatabaseMainViewModel> logger)
        {
            _fileScanService = fileScanService ?? throw new ArgumentNullException(nameof(fileScanService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to scan service events
            _fileScanService.ScanProgressChanged += OnScanProgressChanged;
            _fileScanService.ScanCompleted += OnScanCompleted;
            _fileScanService.ChannelUpdated += OnChannelUpdated;

            // Load existing scan sessions
            _ = LoadScanSessionsAsync();
        }

        [RelayCommand]
        private async Task SelectFolderAsync()
        {
            try
            {
                _logger.LogInformation("User initiated folder selection");

                var folderPicker = new FolderPicker();
                folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                folderPicker.FileTypeFilter.Add("*");

                var window = App.Current.MainWindow;
                if (window == null)
                {
                    throw new InvalidOperationException("Main window is not available");
                }

                var hWnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(folderPicker, hWnd);

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    SelectedFolderPath = folder.Path;
                    StatusMessage = $"Selected folder: {folder.Path}";
                    
                    _logger.LogInformation("Selected folder: {Path}", folder.Path);
                }
                else
                {
                    _logger.LogInformation("Folder selection cancelled");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error selecting folder");
                StatusMessage = $"Error selecting folder: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartScan))]
        private async Task StartScanAsync()
        {
            try
            {
                IsScanning = true;
                StatusMessage = "Starting scan...";
                ProgressPercentage = 0;

                _logger.LogInformation("Starting scan for path: {Path}", SelectedFolderPath);

                // Start the scan via the service
                var scanSessionId = await _fileScanService.StartScanAsync(SelectedFolderPath);
                _currentScanSessionId = scanSessionId;

                // Refresh scan sessions list
                await LoadScanSessionsAsync();

                // Select the new scan session
                SelectedScanSession = ScanSessions.FirstOrDefault(s => s.Id == scanSessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting scan");
                StatusMessage = $"Error starting scan: {ex.Message}";
                IsScanning = false;
            }
        }

        [RelayCommand]
        private async Task RefreshScanSessionsAsync()
        {
            await LoadScanSessionsAsync();
        }

        [RelayCommand]
        private async Task LoadChannelsAsync()
        {
            if (SelectedScanSession == null) return;

            try
            {
                StatusMessage = "Loading channels...";
                
                var channels = await _fileScanService.GetChannelsAsync(SelectedScanSession.Id);
                
                Channels.Clear();
                AvailableChannels.Clear();
                AvailableChannels.Add("All Channels");

                foreach (var channel in channels)
                {
                    Channels.Add(channel);
                    AvailableChannels.Add(channel.Name);
                }

                HasScanResults = channels.Any();
                
                // Load file statistics
                FileStatistics = await _fileScanService.GetFileStatisticsAsync(SelectedScanSession.Id);

                StatusMessage = $"Loaded {channels.Count} channels";
                _logger.LogInformation("Loaded {ChannelCount} channels for scan session {ScanId}", 
                    channels.Count, SelectedScanSession.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading channels for scan session {ScanId}", SelectedScanSession?.Id);
                StatusMessage = $"Error loading channels: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task LoadChannelFilesAsync()
        {
            if (SelectedChannel == null || SelectedScanSession == null) return;

            try
            {
                StatusMessage = $"Loading files for channel: {SelectedChannel.Name}...";

                var (files, totalCount) = await _fileScanService.GetChannelFilesAsync(
                    SelectedScanSession.Id,
                    SelectedChannel.Name,
                    CurrentPage,
                    PageSize,
                    !HideDuplicates,
                    ShowOnlySelected);

                ChannelFiles.Clear();
                foreach (var file in files)
                {
                    var videoFileInfo = _fileScanService.ConvertToVideoFileInfo(file);
                    ChannelFiles.Add(videoFileInfo);
                }

                TotalFiles = totalCount;
                StatusMessage = $"Loaded {files.Count} files from {SelectedChannel.Name} (Page {CurrentPage} of {Math.Ceiling((double)totalCount / PageSize)})";

                _logger.LogInformation("Loaded {FileCount} files for channel {ChannelName} (page {Page})", 
                    files.Count, SelectedChannel.Name, CurrentPage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading files for channel {ChannelName}", SelectedChannel?.Name);
                StatusMessage = $"Error loading files: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task NextPageAsync()
        {
            if (TotalFiles > CurrentPage * PageSize)
            {
                CurrentPage++;
                await LoadChannelFilesAsync();
            }
        }

        [RelayCommand]
        private async Task PreviousPageAsync()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                await LoadChannelFilesAsync();
            }
        }

        [RelayCommand]
        private async Task CancelScanAsync()
        {
            if (_currentScanSessionId.HasValue)
            {
                try
                {
                    var success = await _fileScanService.CancelScanAsync(_currentScanSessionId.Value);
                    if (success)
                    {
                        StatusMessage = "Scan cancelled";
                        IsScanning = false;
                        _logger.LogInformation("Cancelled scan session {ScanId}", _currentScanSessionId.Value);
                    }
                    else
                    {
                        StatusMessage = "Failed to cancel scan";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cancelling scan");
                    StatusMessage = $"Error cancelling scan: {ex.Message}";
                }
            }
        }

        private async Task LoadScanSessionsAsync()
        {
            try
            {
                var sessions = await _fileScanService.GetScanSessionsAsync();
                
                ScanSessions.Clear();
                foreach (var session in sessions)
                {
                    ScanSessions.Add(session);
                }

                _logger.LogInformation("Loaded {SessionCount} scan sessions", sessions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading scan sessions");
                StatusMessage = $"Error loading scan sessions: {ex.Message}";
            }
        }

        private void OnScanProgressChanged(object? sender, ScanProgressEventArgs e)
        {
            try
            {
                // Update UI on main thread
                App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_currentScanSessionId == e.ScanSessionId)
                    {
                        ProgressPercentage = e.Progress;
                        StatusMessage = e.Operation;
                        
                        // Update the selected scan session if it matches
                        if (SelectedScanSession?.Id == e.ScanSessionId)
                        {
                            SelectedScanSession.ProgressPercentage = e.Progress;
                            SelectedScanSession.CurrentOperation = e.Operation;
                            SelectedScanSession.FilesFound = e.FilesFound;
                            SelectedScanSession.DirectoriesProcessed = e.DirectoriesProcessed;
                            SelectedScanSession.TotalDirectories = e.TotalDirectories;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling scan progress update");
            }
        }

        private void OnScanCompleted(object? sender, ScanCompletedEventArgs e)
        {
            try
            {
                // Update UI on main thread
                App.Current.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
                {
                    if (_currentScanSessionId == e.ScanSessionId)
                    {
                        IsScanning = false;
                        
                        if (e.IsSuccess)
                        {
                            StatusMessage = $"Scan completed! Found {e.FilesFound} files";
                            ProgressPercentage = 100;
                            
                            // Auto-load channels for the completed scan
                            if (SelectedScanSession?.Id == e.ScanSessionId)
                            {
                                await LoadChannelsAsync();
                            }
                        }
                        else
                        {
                            StatusMessage = $"Scan failed: {e.ErrorMessage ?? "Unknown error"}";
                            ProgressPercentage = 0;
                        }

                        _logger.LogInformation("Scan {ScanId} completed. Success: {Success}, Files: {FileCount}", 
                            e.ScanSessionId, e.IsSuccess, e.FilesFound);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling scan completion");
            }
        }

        private void OnChannelUpdated(object? sender, ChannelUpdatedEventArgs e)
        {
            try
            {
                // Update channel statistics in real-time
                App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    var channel = Channels.FirstOrDefault(c => c.Id == e.ChannelId);
                    if (channel != null)
                    {
                        channel.TotalFiles = e.TotalFiles;
                        channel.TotalSize = e.TotalSize;
                        channel.UniqueFiles = e.UniqueFiles;
                        channel.ReadyForImport = e.ReadyForImport;
                        channel.LastUpdated = DateTime.UtcNow;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling channel update");
            }
        }

        // Property change notifications for command CanExecute
        partial void OnSelectedFolderPathChanged(string value)
        {
            StartScanCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsScanningChanged(bool value)
        {
            StartScanCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedScanSessionChanged(ScanSession? value)
        {
            if (value != null)
            {
                _ = LoadChannelsAsync();
            }
        }

        partial void OnSelectedChannelChanged(ScannedChannel? value)
        {
            if (value != null)
            {
                CurrentPage = 1; // Reset to first page
                _ = LoadChannelFilesAsync();
            }
        }

        partial void OnHideDuplicatesChanged(bool value)
        {
            if (SelectedChannel != null)
            {
                CurrentPage = 1; // Reset to first page
                _ = LoadChannelFilesAsync();
            }
        }

        partial void OnShowOnlySelectedChanged(bool value)
        {
            if (SelectedChannel != null)
            {
                CurrentPage = 1; // Reset to first page
                _ = LoadChannelFilesAsync();
            }
        }

        public bool CanStartScan => !string.IsNullOrEmpty(SelectedFolderPath) && !IsScanning;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // Unsubscribe from events
                    _fileScanService.ScanProgressChanged -= OnScanProgressChanged;
                    _fileScanService.ScanCompleted -= OnScanCompleted;
                    _fileScanService.ChannelUpdated -= OnChannelUpdated;

                    // Dispose the scan service
                    _fileScanService?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing DatabaseMainViewModel");
                }

                _disposed = true;
            }
        }
    }
}
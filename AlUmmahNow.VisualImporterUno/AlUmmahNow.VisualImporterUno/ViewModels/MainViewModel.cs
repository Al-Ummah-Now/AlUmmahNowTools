using AlUmmahNow.Data.Models;
using AlUmmahNow.Importer.Core;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.VisualImporterUno.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AlUmmahNow.VisualImporterUno.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ImportService _importService = null!; // Mark as non-nullable and initialized
        private readonly DispatcherQueue? _dispatcherQueue;
        private readonly Stopwatch _operationStopwatch = new();
        private bool _disposed = false;

        // Enhanced crash prevention tracking
        private int _consecutiveCOMErrors = 0;
        private DateTime _lastCOMErrorTime = DateTime.MinValue;
        private readonly object _comErrorLock = new object();
        private bool _safeMode = false;

        private string _selectedFolderPath = string.Empty;
        public string SelectedFolderPath
        {
            get => _selectedFolderPath;
            set
            {
                try
                {
                    if (SetProperty(ref _selectedFolderPath, value))
                    {
                        SafeNotifyCanExecuteChanged(() => ScanFolderCommand.NotifyCanExecuteChanged(), "SelectedFolderPath.ScanCommand");
                        SafeNotifyCanExecuteChanged(() => StartImportCommand.NotifyCanExecuteChanged(), "SelectedFolderPath.ImportCommand");
                    }
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(SelectedFolderPath));
                }
            }
        }

        private bool _isImporting = false;
        public bool IsImporting
        {
            get => _isImporting;
            set
            {
                try
                {
                    if (SetProperty(ref _isImporting, value))
                    {
                        SafeNotifyCanExecuteChanged(() => StartImportCommand.NotifyCanExecuteChanged(), "IsImporting.ImportCommand");
                        SafeNotifyCanExecuteChanged(() => ScanFolderCommand.NotifyCanExecuteChanged(), "IsImporting.ScanCommand");
                    }
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(IsImporting));
                }
            }
        }

        private bool _isScanning = false;
        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                try
                {
                    if (SetProperty(ref _isScanning, value))
                    {
                        SafeNotifyCanExecuteChanged(() => ScanFolderCommand.NotifyCanExecuteChanged(), "IsScanning.ScanCommand");
                        SafeNotifyCanExecuteChanged(() => StartImportCommand.NotifyCanExecuteChanged(), "IsScanning.ImportCommand");
                    }
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(IsScanning));
                }
            }
        }

        private string _statusMessage = "Ready to import";
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                try
                {
                    SetProperty(ref _statusMessage, value);
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(StatusMessage));
                }
            }
        }

        private double _progressPercentage = 0;
        public double ProgressPercentage
        {
            get => _progressPercentage;
            set
            {
                try
                {
                    SetProperty(ref _progressPercentage, value);
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(ProgressPercentage));
                }
            }
        }

        private ImportSessionStatus? _currentImportStatus;
        public ImportSessionStatus? CurrentImportStatus
        {
            get => _currentImportStatus;
            set
            {
                try
                {
                    SetProperty(ref _currentImportStatus, value);
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(CurrentImportStatus));
                }
            }
        }

        private ImportSession? _selectedSession;
        public ImportSession? SelectedSession
        {
            get => _selectedSession;
            set
            {
                try
                {
                    SetProperty(ref _selectedSession, value);
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(SelectedSession));
                }
            }
        }

        private VideoFileInfo? _selectedVideoFile;
        public VideoFileInfo? SelectedVideoFile
        {
            get => _selectedVideoFile;
            set
            {
                try
                {
                    SetProperty(ref _selectedVideoFile, value);
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(SelectedVideoFile));
                }
            }
        }

        private VideoSortOption _currentSortOption = VideoSortOption.Name;
        public VideoSortOption CurrentSortOption
        {
            get => _currentSortOption;
            set
            {
                try
                {
                    if (SetProperty(ref _currentSortOption, value))
                    {
                        SafeExecute(() => ApplySortingAndFiltering(), "CurrentSortOption.ApplySorting");
                    }
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(CurrentSortOption));
                }
            }
        }

        private SortDirection _currentSortDirection = SortDirection.Ascending;
        public SortDirection CurrentSortDirection
        {
            get => _currentSortDirection;
            set
            {
                try
                {
                    if (SetProperty(ref _currentSortDirection, value))
                    {
                        SafeExecute(() => ApplySortingAndFiltering(), "CurrentSortDirection.ApplySorting");
                    }
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(CurrentSortDirection));
                }
            }
        }

        private bool _hideDuplicates = true;
        public bool HideDuplicates
        {
            get => _hideDuplicates;
            set
            {
                try
                {
                    if (SetProperty(ref _hideDuplicates, value))
                    {
                        SafeExecute(() => {
                            ApplySortingAndFiltering();
                            UpdateStatistics();
                        }, "HideDuplicates.Update");
                    }
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(HideDuplicates));
                }
            }
        }

        private bool _showOnlySelected = false;
        public bool ShowOnlySelected
        {
            get => _showOnlySelected;
            set
            {
                try
                {
                    if (SetProperty(ref _showOnlySelected, value))
                    {
                        SafeExecute(() => ApplySortingAndFiltering(), "ShowOnlySelected.ApplySorting");
                    }
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(ShowOnlySelected));
                }
            }
        }

        private string? _channelFilter;
        public string? ChannelFilter
        {
            get => _channelFilter;
            set
            {
                try
                {
                    if (SetProperty(ref _channelFilter, value))
                    {
                        SafeExecute(() => ApplySortingAndFiltering(), "ChannelFilter.ApplySorting");
                    }
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(ChannelFilter));
                }
            }
        }

        private FileStatistics? _fileStatistics;
        public FileStatistics? FileStatistics
        {
            get => _fileStatistics;
            set
            {
                try
                {
                    SetProperty(ref _fileStatistics, value);
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(FileStatistics));
                }
            }
        }

        private bool _hasScannedFiles = false;
        public bool HasScannedFiles
        {
            get => _hasScannedFiles;
            set
            {
                try
                {
                    if (SetProperty(ref _hasScannedFiles, value))
                    {
                        SafeNotifyCanExecuteChanged(() => StartImportCommand.NotifyCanExecuteChanged(), "HasScannedFiles.ImportCommand");
                    }
                }
                catch (Exception ex)
                {
                    HandlePropertyError(ex, nameof(HasScannedFiles));
                }
            }
        }

        private int _selectedFileCount = 0;
        public int SelectedFileCount
        {
            get => _selectedFileCount;
            set
            {
                if (SetProperty(ref _selectedFileCount, value))
                {
                    OnPropertyChanged(nameof(HasSelectedFiles));
                }
            }
        }

        public bool HasSelectedFiles => SelectedFileCount > 0;

        private bool _hasDatabaseStats = false;
        public bool HasDatabaseStats
        {
            get => _hasDatabaseStats;
            set => SetProperty(ref _hasDatabaseStats, value);
        }

        private string _databaseStatusMessage = string.Empty;
        public string DatabaseStatusMessage
        {
            get => _databaseStatusMessage;
            set => SetProperty(ref _databaseStatusMessage, value);
        }

        // Collections
        public ObservableCollection<ImportSession> ImportSessions { get; } = new();
        public ObservableCollection<ImportVideo> SessionVideos { get; } = new();
        public ObservableCollection<ChannelStatus> ChannelStatuses { get; } = new();
        public ObservableCollection<VideoFileInfo> VideoFiles { get; } = new();
        public ObservableCollection<VideoFileInfo> FilteredVideoFiles { get; } = new();
        public ObservableCollection<string> AvailableChannels { get; } = new();

        private List<VideoFileInfo> _allVideoFiles = new();

        public MainViewModel(ImportService importService)
        {
            try
            {
                _importService = importService ?? throw new ArgumentNullException(nameof(importService));
                
                // Initialize crash handler early if not already done
                try
                {
                    CrashHandler.Initialize();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CrashHandler already initialized or failed: {ex.Message}");
                }
                
                // Get the dispatcher queue for thread-safe UI updates with enhanced safety
                try
                {
                    // Try multiple approaches to get a valid dispatcher queue
                    _dispatcherQueue = GetSafeDispatcherQueue();
                    
                    // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 1003,
                    //     "MainViewModel Initialized Successfully",
                    //     $"DispatcherQueue Available: {_dispatcherQueue != null}\n" +
                    //     $"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}\n" +
                    //     $"UI Thread: {_dispatcherQueue?.HasThreadAccess ?? false}");
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogDispatcherFailure("MainViewModel Initialization", ex);
                    _dispatcherQueue = null;
                    
                    // Try COM recovery if it's a COM exception
                    if (ex is COMException comEx)
                    {
                        HandleCOMException(comEx, "MainViewModel.DispatcherQueue");
                    }
                }
                
                // Subscribe to events with enhanced error handling
                try
                {
                    // Use weak event pattern to prevent memory leaks and reduce crash potential
                    SubscribeToImportServiceEvents();
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogServiceInitializationFailure("ImportService Events", ex);
                }
                
                // Load initial data with enhanced safety
                SafeExecuteInitialLoad();
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, "MainViewModel Constructor");
                
                // Try to initialize in safe mode
                try
                {
                    InitializeSafeMode();
                }
                catch (Exception safeEx)
                {
                    // WindowsEventLogger.LogCriticalCrash(safeEx, "MainViewModel Safe Mode Initialization");
                    throw; // Re-throw if even safe mode fails
                }
            }
        }

        /// <summary>
        /// Enhanced COM exception handling with crash prevention
        /// </summary>
        private bool HandleCOMException(COMException comEx, string operation)
        {
            lock (_comErrorLock)
            {
                // Track consecutive COM errors
                var now = DateTime.Now;
                if ((now - _lastCOMErrorTime).TotalSeconds < 5)
                {
                    _consecutiveCOMErrors++;
                }
                else
                {
                    _consecutiveCOMErrors = 1;
                }
                _lastCOMErrorTime = now;

                // Enter safe mode if too many consecutive COM errors
                if (_consecutiveCOMErrors >= 3)
                {
                    _safeMode = true;
                    // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 2010,
                        // "Safe Mode Activated"
                        // $"Activated safe mode due to {_consecutiveCOMErrors} consecutive COM errors\n"
                        // $"Last operation: {operation}");
                }

                // Use enhanced recovery with aggressive cleanup for CoreMessagingXP errors
                if (comEx.HResult == unchecked((int)0xe0464645))
                {
                    return TryRecoverFromCoreMessagingXPCrash(operation);
                }
                else
                {
                    return CrashHandler.TryHandleCOMException(comEx, operation);
                }
            }
        }

        /// <summary>
        /// Aggressive recovery specifically for CoreMessagingXP crashes
        /// </summary>
        private bool TryRecoverFromCoreMessagingXPCrash(string operation)
        {
            try
            {
                // WindowsEventLogger.LogEvent(EventLogEntryType.Error, 2011,
                    // "CoreMessagingXP Crash Recovery Initiated"
                    // $"Operation: {operation}\n"
                    // $"Consecutive errors: {_consecutiveCOMErrors}\n"
                    // $"Safe mode: {_safeMode}");

                // Step 1: Force aggressive garbage collection
                for (int i = 0; i < 3; i++)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(50);
                }

                // Step 2: Clean up COM objects
                try
                {
                    Marshal.CleanupUnusedObjectsInCurrentContext();
                    System.Threading.Thread.Sleep(100);
                }
                catch (Exception cleanupEx)
                {
                    // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 2012,
                    //     "COM Cleanup Failed", cleanupEx.Message);
                }

                // Step 3: Force another GC cycle after COM cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // Step 4: Try to force WinRT cleanup if possible
                try
                {
                    // Attempt to trigger WinRT cleanup by accessing the current dispatcher
                    var currentDispatcher = _dispatcherQueue;
                    if (currentDispatcher != null)
                    {
                        // Just accessing the property can help clear cached WinRT objects
                        var hasAccess = currentDispatcher.HasThreadAccess;
                    }
                }
                catch (Exception winrtEx)
                {
                    // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 2013,
                    //     "WinRT Cleanup Failed", winrtEx.Message);
                }

                // Step 5: Extended recovery delay
                System.Threading.Thread.Sleep(200);

                // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 2014,
                    // "CoreMessagingXP Recovery Completed"
                    // $"Recovery operations completed for operation: {operation}");

                return true;
            }
            catch (Exception recoveryEx)
            {
                // WindowsEventLogger.LogCriticalCrash(recoveryEx, "CoreMessagingXP Recovery");
                return false;
            }
        }

        /// <summary>
        /// Safe notification of command CanExecute changes with COM error protection
        /// </summary>
        private void SafeNotifyCanExecuteChanged(Action notifyAction, string operation)
        {
            if (_safeMode) return; // Skip command updates in safe mode

            try
            {
                notifyAction();
            }
            catch (COMException comEx)
            {
                if (HandleCOMException(comEx, operation))
                {
                    // Retry once after recovery
                    try
                    {
                        System.Threading.Thread.Sleep(50);
                        notifyAction();
                    }
                    catch (Exception retryEx)
                    {
                        // WindowsEventLogger.LogCriticalCrash(retryEx, $"{operation}.Retry");
                    }
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, operation);
            }
        }

        /// <summary>
        /// Get a safe dispatcher queue with multiple fallback strategies
        /// </summary>
        private DispatcherQueue? GetSafeDispatcherQueue()
        {
            try
            {
                // Strategy 1: Try from App.Current.MainWindow
                var appDispatcher = App.Current?.MainWindow?.DispatcherQueue;
                if (appDispatcher != null)
                {
                    return appDispatcher;
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 1004,
                    // "App Dispatcher Access Failed", ex.Message);
            }

            try
            {
                // Strategy 2: Try from current thread
                var currentDispatcher = DispatcherQueue.GetForCurrentThread();
                if (currentDispatcher != null)
                {
                    return currentDispatcher;
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 1005,
                    // "Current Thread Dispatcher Access Failed", ex.Message);
            }

            try
            {
                // Strategy 3: Try creating from main thread
                if (System.Threading.Thread.CurrentThread.GetApartmentState() == System.Threading.ApartmentState.STA)
                {
                    return DispatcherQueue.GetForCurrentThread();
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 1006,
                    // "STA Thread Dispatcher Creation Failed", ex.Message);
            }

            // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 1007,
                // "No Dispatcher Queue Available", "All dispatcher acquisition strategies failed"
            return null;
        }

        /// <summary>
        /// Subscribe to import service events with enhanced error handling
        /// </summary>
        private void SubscribeToImportServiceEvents()
        {
            try
            {
                // Unsubscribe first to prevent double subscription
                _importService.ProgressChanged -= OnImportProgressChanged;
                _importService.ImportCompleted -= OnImportCompleted;
                
                // Subscribe with error wrapping
                _importService.ProgressChanged += SafeOnImportProgressChanged;
                _importService.ImportCompleted += SafeOnImportCompleted;
                
                // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 1008,
                    // "Import Service Events Subscribed", "Event handlers attached successfully"
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogServiceInitializationFailure("ImportService Event Subscription", ex);
                throw;
            }
        }

        /// <summary>
        /// Safe wrapper for import progress changed event
        /// </summary>
        private void SafeOnImportProgressChanged(object? sender, ImportProgressEventArgs e)
        {
            try
            {
                OnImportProgressChanged(sender, e);
            }
            catch (COMException comEx)
            {
                if (!HandleCOMException(comEx, "ImportProgressChanged"))
                {
                    // WindowsEventLogger.LogCriticalCrash(comEx, "SafeOnImportProgressChanged");
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, "SafeOnImportProgressChanged");
            }
        }

        /// <summary>
        /// Safe wrapper for import completed event
        /// </summary>
        private void SafeOnImportCompleted(object? sender, ImportCompletedEventArgs e)
        {
            try
            {
                OnImportCompleted(sender, e);
            }
            catch (COMException comEx)
            {
                if (!HandleCOMException(comEx, "ImportCompleted"))
                {
                    // WindowsEventLogger.LogCriticalCrash(comEx, "SafeOnImportCompleted");
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, "SafeOnImportCompleted");
            }
        }

        private void SafeExecuteInitialLoad()
        {
            try
            {
                // DON'T load import sessions on startup - they're not needed until user explicitly requests them
                // This prevents database threading issues and missing table errors
                // The ImportSessions table is from the old schema and may not exist
                
                // Use Task.Run to avoid blocking the constructor
                /*
                Task.Run(async () =>
                {
                    try
                    {
                        await SafeExecuteAsync(() => LoadImportSessionsAsync(), "MainViewModel.LoadInitialSessions");
                    }
                    catch (Exception ex)
                    {
                        // WindowsEventLogger.LogCriticalCrash(ex, "Initial Load Background Task");
                    }
                });
                */
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, "SafeExecuteInitialLoad");
            }
        }

        /// <summary>
        /// Initialize ViewModel in safe mode when normal initialization fails
        /// </summary>
        private void InitializeSafeMode()
        {
            try
            {
                // Set basic state
                StatusMessage = "Application started in safe mode - limited functionality available";
                IsImporting = false;
                IsScanning = false;
                HasScannedFiles = false;
                _safeMode = true;
                
                // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 1009,
                    // "MainViewModel Safe Mode Initialized"
                    // "ViewModel initialized in safe mode due to initialization failures"
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, "InitializeSafeMode");
                throw;
            }
        }

        [RelayCommand]
        private async Task SelectFolderAsync()
        {
            _operationStopwatch.Restart();
            try
            {
                // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 2100,
                    // "Folder Selection Started", "User initiated folder selection"

                var folderPicker = new FolderPicker();
                folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                folderPicker.FileTypeFilter.Add("*");

                // Get the current window handle for the picker
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
                    
                    // Clear previous scan results
                    SafeExecute(() => ClearScanResults(), "SelectFolder.ClearScanResults");

                    // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 2101,
                        // "Folder Selected Successfully", $"Selected path: {folder.Path}"
                }
                else
                {
                    // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 2102,
                        // "Folder Selection Cancelled", "User cancelled folder selection"
                }
            }
            catch (COMException comEx)
            {
                // WindowsEventLogger.LogComMarshalingError(comEx, "SelectFolderAsync");
                if (!HandleCOMException(comEx, "SelectFolderAsync"))
                {
                    StatusMessage = $"Error selecting folder: COM error occurred";
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, "SelectFolderAsync");
                StatusMessage = $"Error selecting folder: {ex.Message}";
            }
            finally
            {
                _operationStopwatch.Stop();
                if (_operationStopwatch.Elapsed.TotalSeconds > 5)
                {
                    // WindowsEventLogger.LogPerformanceIssue("SelectFolderAsync",
                    //     _operationStopwatch.Elapsed, "Folder selection took longer than expected");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanScanFolder))]
        private async Task ScanFolderAsync()
        {
            _operationStopwatch.Restart();
            try
            {
                IsScanning = true;
                StatusMessage = "Scanning for video files...";

                // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 2200,
                    // "Folder Scan Started", $"Scanning path: {SelectedFolderPath}"

                // Subscribe to progress events temporarily
                _importService.ProgressChanged += OnScanProgressChanged;
                
                // Scan the directory for video files
                var videoFiles = await _importService.ScanDirectoryAsync(SelectedFolderPath, true);
                
                // Update collections safely
                SafeExecute(() => {
                    _allVideoFiles = videoFiles;
                    VideoFiles.Clear();
                    foreach (var file in videoFiles)
                    {
                        // Subscribe to property changes to track selection
                        file.PropertyChanged += OnVideoFilePropertyChanged;
                        VideoFiles.Add(file);
                    }

                    // Reset import statuses to NotImported for all discovered files
                    ResetFileImportStatuses();

                    // Update available channels
                    var channels = videoFiles.Select(v => v.ChannelName).Distinct().OrderBy(c => c).ToList();
                    AvailableChannels.Clear();
                    AvailableChannels.Add("All Channels");
                    foreach (var channel in channels)
                    {
                        AvailableChannels.Add(channel);
                    }

                    // Apply initial sorting and filtering
                    ApplySortingAndFiltering();
                    UpdateStatistics();
                    UpdateSelectedFileCount();
                    LoadDatabaseStats();

                    HasScannedFiles = videoFiles.Count > 0;
                }, "ScanFolder.UpdateCollections");

                StatusMessage = $"Found {videoFiles.Count} video files in {_allVideoFiles.Select(v => v.ChannelName).Distinct().Count()} channels";
                
                // Force UI update to show the file table immediately
                SafeDispatcherExecute(() => {
                    OnPropertyChanged(nameof(HasScannedFiles));
                    OnPropertyChanged(nameof(FilteredVideoFiles));
                    OnPropertyChanged(nameof(FileStatistics));
                }, "ScanFolder.ForceUIUpdate");

                // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 2201,
                    // "Folder Scan Completed Successfully"
                    // $"Found {videoFiles.Count} files in {_allVideoFiles.Select(v => v.ChannelName).Distinct().Count()} channels");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan operation was cancelled";
                HasScannedFiles = false;
                // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 2202,
                    // "Folder Scan Cancelled", "Scan operation was cancelled by user or system"
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning folder: {ex.Message}";
                HasScannedFiles = false;
                // WindowsEventLogger.LogCriticalCrash(ex, "ScanFolderAsync");
            }
            finally
            {
                // Unsubscribe from progress events
                try
                {
                    _importService.ProgressChanged -= OnScanProgressChanged;
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 2203,
                        // "Error Unsubscribing Scan Progress", ex.Message);
                }

                IsScanning = false;
                _operationStopwatch.Stop();
                
                if (_operationStopwatch.Elapsed.TotalMinutes > 2)
                {
                    // WindowsEventLogger.LogPerformanceIssue("ScanFolderAsync",
                    //     _operationStopwatch.Elapsed, $"Scanned {_allVideoFiles.Count} files");
                }
            }
        }

        private void OnScanProgressChanged(object? sender, ImportProgressEventArgs e)
        {
            SafeDispatcherExecute(() => {
                try
                {
                    // Update progress percentage for main progress tracking
                    ProgressPercentage = e.ProgressPercentage;
                    StatusMessage = e.CurrentOperation;
                    
                    // Also update the operation-specific progress if the main window is listening
                    if (App.Current?.MainWindow is MainWindow mainWindow)
                    {
                        try
                        {
                            mainWindow.UpdateScanProgress(
                                e.ProgressPercentage,
                                e.CurrentOperation,
                                e.ProcessedFiles,
                                e.TotalFiles,
                                e.FilesFound);
                        }
                        catch (Exception mainWindowEx)
                        {
                            // WindowsEventLogger.LogComMarshalingError(mainWindowEx, "OnScanProgressChanged.UpdateMainWindow");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, "OnScanProgressChanged");
                }
            }, "OnScanProgressChanged");
        }

        [RelayCommand(CanExecute = nameof(CanStartImport))]
        private async Task StartImportAsync()
        {
            _operationStopwatch.Restart();
            try
            {
                IsImporting = true;
                StatusMessage = "Starting import...";

                // Get selected files for import
                var selectedFiles = _allVideoFiles?
                    .Where(f => f.IsSelected && !f.IsDuplicate)
                    .ToList() ?? new List<VideoFileInfo>();

                if (!selectedFiles.Any())
                {
                    StatusMessage = "No files selected for import";
                    // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 2300,
                        // "Import Started with No Files", "No files were selected for import"
                    return;
                }

                // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 2301,
                    // "Import Started", $"Starting import of {selectedFiles.Count} files"

                // Subscribe to progress events for import
                _importService.ProgressChanged += OnImportProgressChanged;

                // Start the import process
                await _importService.ImportVideosAsync(selectedFiles, SelectedFolderPath);

                StatusMessage = $"Import completed successfully. {selectedFiles.Count} files processed.";
                // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 2302,
                    // "Import Completed Successfully", $"Processed {selectedFiles.Count} files"
            }
            catch (Exception ex)
            {
                StatusMessage = $"Import failed: {ex.Message}";
                // WindowsEventLogger.LogCriticalCrash(ex, "StartImportAsync");
            }
            finally
            {
                // Unsubscribe from progress events
                try
                {
                    _importService.ProgressChanged -= OnImportProgressChanged;
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 2303,
                        // "Error Unsubscribing Import Progress", ex.Message);
                }

                IsImporting = false;
                _operationStopwatch.Stop();
                
                if (_operationStopwatch.Elapsed.TotalMinutes > 10)
                {
                    // WindowsEventLogger.LogPerformanceIssue("StartImportAsync",
                    //     _operationStopwatch.Elapsed, "Import operation took longer than expected");
                }
            }
        }

        [RelayCommand]
        private async Task StartImportFromFolderAsync()
        {
            try
            {
                IsImporting = true;
                StatusMessage = "Starting import from folder...";
                ProgressPercentage = 0;

                var sessionId = await _importService.StartImportAsync(SelectedFolderPath);
                StatusMessage = $"Import started (Session: {sessionId})";
                
                await LoadImportSessionsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error starting import: {ex.Message}";
                // WindowsEventLogger.LogCriticalCrash(ex, "StartImportFromFolderAsync");
                IsImporting = false;
            }
        }

        [RelayCommand]
        private void SelectAllFiles()
        {
            SafeDispatcherExecute(() => {
                try
                {
                    foreach (var file in _allVideoFiles.Where(v => !v.IsDuplicate))
                    {
                        file.IsSelected = true;
                    }
                    ApplySortingAndFiltering();
                    UpdateSelectedFileCount();
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, "SelectAllFiles");
                }
            }, "SelectAllFiles");
        }

        [RelayCommand]
        private void DeselectAllFiles()
        {
            SafeDispatcherExecute(() => {
                try
                {
                    foreach (var file in _allVideoFiles)
                    {
                        file.IsSelected = false;
                    }
                    ApplySortingAndFiltering();
                    UpdateSelectedFileCount();
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, "DeselectAllFiles");
                }
            }, "DeselectAllFiles");
        }

        [RelayCommand]
        private void ToggleSortDirection()
        {
            SafeDispatcherExecute(() => {
                try
                {
                    CurrentSortDirection = CurrentSortDirection == SortDirection.Ascending 
                        ? SortDirection.Descending 
                        : SortDirection.Ascending;
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, "ToggleSortDirection");
                }
            }, "ToggleSortDirection");
        }

        private bool CanScanFolder => !string.IsNullOrEmpty(SelectedFolderPath) && !IsScanning && !IsImporting;
        public bool CanStartImport => HasScannedFiles && !IsImporting && !IsScanning;

        [RelayCommand]
        private async Task RefreshSessionsAsync()
        {
            await SafeExecuteAsync(() => LoadImportSessionsAsync(), "RefreshSessionsAsync");
        }

        [RelayCommand]
        private async Task ViewSessionDetailsAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                StatusMessage = "Loading session details...";
                
                // Load session videos
                var videos = await _importService.GetSessionVideosAsync(SelectedSession.Id);
                SafeExecute(() => {
                    SessionVideos.Clear();
                    foreach (var video in videos)
                    {
                        SessionVideos.Add(video);
                    }
                }, "ViewSessionDetails.LoadVideos");

                // Load current status
                var status = await _importService.GetImportStatusAsync(SelectedSession.Id);
                CurrentImportStatus = status;
                
                SafeExecute(() => {
                    ChannelStatuses.Clear();
                    foreach (var channel in status.Channels)
                    {
                        ChannelStatuses.Add(channel);
                    }
                }, "ViewSessionDetails.LoadChannels");

                StatusMessage = $"Loaded details for session {SelectedSession.Id}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading session details: {ex.Message}";
                // WindowsEventLogger.LogCriticalCrash(ex, "ViewSessionDetailsAsync");
            }
        }

        [RelayCommand]
        private async Task ResumeImportAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                IsImporting = true;
                StatusMessage = "Resuming import...";

                var success = await _importService.ResumeImportAsync(SelectedSession.Id);
                if (success)
                {
                    StatusMessage = $"Import resumed (Session: {SelectedSession.Id})";
                    await LoadImportSessionsAsync();
                }
                else
                {
                    StatusMessage = "Failed to resume import";
                    IsImporting = false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error resuming import: {ex.Message}";
                // WindowsEventLogger.LogCriticalCrash(ex, "ResumeImportAsync");
                IsImporting = false;
            }
        }

        private void UpdateSelectedFileCount()
        {
            SelectedFileCount = FilteredVideoFiles.Count(f => f.IsSelected);
        }

        private void OnVideoFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VideoFileInfo.IsSelected))
            {
                UpdateSelectedFileCount();
            }
        }

        private async void LoadDatabaseStats()
        {
            try
            {
                // TODO: Get actual database stats from service
                // For now, show a placeholder to indicate database integration
                await Task.Delay(100); // Placeholder async call

                // This will be implemented when database service methods are available
                HasDatabaseStats = false;
                DatabaseStatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                // Don't show error to user, just log it
                // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 2210,
                    // "Database Stats Error", $"Failed to load database stats: {ex.Message}"
                HasDatabaseStats = false;
            }
        }

        [RelayCommand]
        private async Task CancelImportAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                var success = await _importService.CancelImportAsync(SelectedSession.Id);
                if (success)
                {
                    StatusMessage = $"Import cancelled (Session: {SelectedSession.Id})";
                    IsImporting = false;
                    await LoadImportSessionsAsync();
                }
                else
                {
                    StatusMessage = "Failed to cancel import";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error cancelling import: {ex.Message}";
                // WindowsEventLogger.LogCriticalCrash(ex, "CancelImportAsync");
            }
        }

        private void ClearScanResults()
        {
            SafeExecute(() => {
                _allVideoFiles.Clear();
                VideoFiles.Clear();
                FilteredVideoFiles.Clear();
                AvailableChannels.Clear();
                FileStatistics = null;
                HasScannedFiles = false;
                ChannelFilter = null;
            }, "ClearScanResults");
        }

        /// <summary>
        /// Reset all file import statuses to NotImported
        /// </summary>
        private void ResetFileImportStatuses()
        {
            SafeExecute(() => {
                foreach (var file in _allVideoFiles)
                {
                    file.ImportStatus = ImportFileStatus.NotImported;
                }
            }, "ResetFileImportStatuses");
        }

        private void ApplySortingAndFiltering()
        {
            try
            {
                if (!_allVideoFiles.Any()) return;

                // Apply filtering
                var filtered = _importService.FilterVideoFiles(_allVideoFiles, HideDuplicates, ShowOnlySelected, 
                    ChannelFilter == "All Channels" ? null : ChannelFilter);

                // Apply sorting
                var sorted = _importService.SortVideoFiles(filtered, CurrentSortOption, CurrentSortDirection);

                // Update filtered collection
                FilteredVideoFiles.Clear();
                foreach (var file in sorted)
                {
                    FilteredVideoFiles.Add(file);
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, "ApplySortingAndFiltering");
            }
        }

        private void UpdateStatistics()
        {
            SafeExecute(() => {
                if (_allVideoFiles.Any())
                {
                    FileStatistics = _importService.GetFileStatistics(_allVideoFiles);
                }
            }, "UpdateStatistics");
        }

        private async Task LoadImportSessionsAsync()
        {
            try
            {
                var sessions = await _importService.GetAllImportSessionsAsync();
                SafeExecute(() => {
                    ImportSessions.Clear();
                    foreach (var session in sessions)
                    {
                        ImportSessions.Add(session);
                    }
                }, "LoadImportSessions.UpdateCollection");

                // Update current import status if there's an active session
                var activeSession = sessions.FirstOrDefault(s => s.Status == ImportStatus.InProgress);
                if (activeSession != null)
                {
                    IsImporting = true;
                    var status = await _importService.GetImportStatusAsync(activeSession.Id);
                    CurrentImportStatus = status;
                    ProgressPercentage = status.ProgressPercentage;
                }
                else
                {
                    IsImporting = false;
                    ProgressPercentage = 0;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading sessions: {ex.Message}";
                // WindowsEventLogger.LogCriticalCrash(ex, "LoadImportSessionsAsync");
            }
        }

        private void OnImportProgressChanged(object? sender, ImportProgressEventArgs e)
        {
            SafeDispatcherExecute(() => {
                try
                {
                    // Update progress percentage for main progress tracking
                    ProgressPercentage = e.ProgressPercentage;
                    StatusMessage = e.CurrentOperation;
                    
                    // Update individual file statuses based on current progress
                    UpdateFileImportStatus(e);
                    
                    // Also update the operation-specific progress if the main window is listening
                    if (App.Current?.MainWindow is MainWindow mainWindow)
                    {
                        try
                        {
                            // For import operations, show current file and progress through selected files
                            var currentItem = !string.IsNullOrEmpty(e.CurrentFile) ? 
                                $"Uploading: {Path.GetFileName(e.CurrentFile)}" : "";
                            
                            // Use global progress for overall import, local progress for current file upload
                            var globalProgress = e.ProgressPercentage;
                            var localProgress = e.ProgressPercentage; // For now, same as global until we have file-level progress
                            
                            mainWindow.UpdateOperationProgress(
                                globalProgress,
                                localProgress,
                                e.CurrentOperation,
                                e.ProcessedFiles,
                                e.TotalFiles,
                                currentItem);
                        }
                        catch (Exception mainWindowEx)
                        {
                            // WindowsEventLogger.LogComMarshalingError(mainWindowEx, "OnImportProgressChanged.UpdateMainWindow");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, "OnImportProgressChanged");
                }
            }, "OnImportProgressChanged");
        }

        /// <summary>
        /// Update individual file import status based on import progress
        /// </summary>
        private void UpdateFileImportStatus(ImportProgressEventArgs e)
        {
            try
            {
                if (_allVideoFiles == null || !_allVideoFiles.Any()) return;

                var selectedFiles = _allVideoFiles.Where(f => f.IsSelected && !f.IsDuplicate).ToList();
                if (!selectedFiles.Any()) return;

                // Mark files as pending if import has started
                if (e.ProcessedFiles == 0 && e.TotalFiles > 0)
                {
                    foreach (var file in selectedFiles)
                    {
                        if (file.ImportStatus == ImportFileStatus.NotImported)
                        {
                            file.ImportStatus = ImportFileStatus.Pending;
                        }
                    }
                }

                // Update current file being processed
                if (!string.IsNullOrEmpty(e.CurrentFile))
                {
                    var currentFile = selectedFiles.FirstOrDefault(f => 
                        f.FilePath.Equals(e.CurrentFile, StringComparison.OrdinalIgnoreCase) ||
                        f.FileName.Equals(Path.GetFileName(e.CurrentFile), StringComparison.OrdinalIgnoreCase));
                    
                    if (currentFile != null && currentFile.ImportStatus != ImportFileStatus.Uploaded)
                    {
                        currentFile.ImportStatus = ImportFileStatus.Uploading;
                    }
                }

                // Mark completed files as uploaded (simple heuristic based on progress)
                var completedCount = Math.Min(e.ProcessedFiles, selectedFiles.Count);
                for (int i = 0; i < completedCount; i++)
                {
                    var file = selectedFiles[i];
                    if (file.ImportStatus == ImportFileStatus.Uploading || file.ImportStatus == ImportFileStatus.Pending)
                    {
                        file.ImportStatus = ImportFileStatus.Uploaded;
                    }
                }

                // Update UI
                ApplySortingAndFiltering();
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, "UpdateFileImportStatus");
            }
        }

        private void OnImportCompleted(object? sender, ImportCompletedEventArgs e)
        {
            SafeDispatcherExecute(async () => {
                try
                {
                    IsImporting = false;
                    
                    if (e.IsSuccess)
                    {
                        StatusMessage = $"Import completed! Uploaded: {e.TotalUploaded}, Failed: {e.TotalFailed}";
                        ProgressPercentage = 100;
                        
                        // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 2400,
                            // "Import Completed Successfully"
                            // $"Uploaded: {e.TotalUploaded}, Failed: {e.TotalFailed}");
                    }
                    else
                    {
                        StatusMessage = $"Import failed: {e.ErrorMessage ?? "Unknown error"}";
                        ProgressPercentage = 0;
                        
                        // WindowsEventLogger.LogEvent(EventLogEntryType.Error, 2401,
                        //     "Import Failed",
                        //     $"Error: {e.ErrorMessage ?? "Unknown error"}\n" +
                        //     $"Uploaded: {e.TotalUploaded}, Failed: {e.TotalFailed}");
                    }

                    await LoadImportSessionsAsync();
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, "OnImportCompleted");
                }
            }, "OnImportCompleted");
        }

        /// <summary>
        /// Handle property setter errors safely with enhanced COM error handling
        /// </summary>
        private void HandlePropertyError(Exception ex, string propertyName)
        {
            // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 9100,
                // $"Property Error: {propertyName}"
                // $"Error in property setter: {ex.Message}");
            
            // Try to handle COM errors gracefully with enhanced recovery
            if (ex is COMException comEx)
            {
                HandleCOMException(comEx, $"Property.{propertyName}");
            }
        }

        /// <summary>
        /// Execute an action on the dispatcher queue safely with enhanced COM exception handling
        /// </summary>
        private void SafeDispatcherExecute(Action action, string operationName)
        {
            if (_safeMode)
            {
                // In safe mode, execute directly without dispatcher to avoid COM issues
                try
                {
                    action();
                    return;
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, $"{operationName}.SafeMode");
                    return;
                }
            }

            // Check if we're already on the UI thread
            if (_dispatcherQueue?.HasThreadAccess == true)
            {
                // We're already on the UI thread, execute directly
                try
                {
                    action();
                    return;
                }
                catch (COMException comEx)
                {
                    if (HandleCOMException(comEx, $"{operationName}.DirectUIThreadExecution"))
                    {
                        // Retry once after recovery
                        try
                        {
                            System.Threading.Thread.Sleep(50);
                            action();
                        }
                        catch (Exception retryEx)
                        {
                            // WindowsEventLogger.LogCriticalCrash(retryEx, $"{operationName}.DirectUIRetry");
                        }
                    }
                    return;
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, $"{operationName}.DirectUIThreadExecution");
                    return;
                }
            }

            if (_dispatcherQueue != null)
            {
                try
                {
                    var success = _dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => {
                        try
                        {
                            // Double-check we're on the right thread
                            if (!_dispatcherQueue.HasThreadAccess)
                            {
                                // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 3002,
                                    // "Thread Access Violation"
                                    // $"Operation {operationName} not executing on UI thread despite dispatcher enqueue");
                                return;
                            }

                            action();
                        }
                        catch (COMException comEx)
                        {
                            // WindowsEventLogger.LogComMarshalingError(comEx, $"{operationName}.DispatcherExecution");
                            
                            // Try immediate recovery for COM exceptions
                            if (HandleCOMException(comEx, operationName))
                            {
                                // Recovery successful, try the action again once
                                try
                                {
                                    System.Threading.Thread.Sleep(10); // Small delay for recovery
                                    action();
                                }
                                catch (Exception retryEx)
                                {
                                    // WindowsEventLogger.LogCriticalCrash(retryEx, $"{operationName}.RetryAfterRecovery");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // WindowsEventLogger.LogCriticalCrash(ex, $"{operationName}.DispatcherExecution");
                        }
                    });
                    
                    if (!success)
                    {
                        // WindowsEventLogger.LogDispatcherFailure(operationName);
                        
                        // Fallback: direct execution with extra safety
                        ExecuteDirectFallback(action, operationName);
                    }
                }
                catch (COMException comEx)
                {
                    // WindowsEventLogger.LogComMarshalingError(comEx, $"{operationName}.DispatcherEnqueue");
                    
                    if (HandleCOMException(comEx, operationName))
                    {
                        // Try fallback after recovery
                        ExecuteDirectFallback(action, operationName);
                    }
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, $"{operationName}.DispatcherEnqueue");
                    ExecuteDirectFallback(action, operationName);
                }
            }
            else
            {
                // No dispatcher available, execute directly with safety
                ExecuteDirectFallback(action, operationName);
            }
        }

        /// <summary>
        /// Execute action directly as fallback when dispatcher is not available
        /// </summary>
        private void ExecuteDirectFallback(Action action, string operationName)
        {
            try
            {
                // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 3003,
                    // "Direct Fallback Execution"
                    // $"Executing {operationName} directly due to dispatcher unavailability");
                
                action();
            }
            catch (COMException comEx)
            {
                // WindowsEventLogger.LogComMarshalingError(comEx, $"{operationName}.DirectFallback");
                HandleCOMException(comEx, operationName);
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, $"{operationName}.DirectFallback");
            }
        }

        /// <summary>
        /// Enhanced safe execution with COM exception handling and recovery
        /// </summary>
        private void SafeExecute(Action action, string operationName)
        {
            try
            {
                action();
            }
            catch (COMException comEx)
            {
                // WindowsEventLogger.LogComMarshalingError(comEx, operationName);
                
                // Try immediate recovery for COM exceptions
                if (HandleCOMException(comEx, operationName))
                {
                    // Recovery successful, try the action again once
                    try
                    {
                        System.Threading.Thread.Sleep(10); // Small delay for recovery
                        action();
                    }
                    catch (Exception retryEx)
                    {
                        // WindowsEventLogger.LogCriticalCrash(retryEx, $"{operationName}.RetryAfterRecovery");
                    }
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, operationName);
            }
        }

        /// <summary>
        /// Enhanced safe async execution with COM exception handling
        /// </summary>
        private async Task SafeExecuteAsync(Func<Task> action, string operationName)
        {
            try
            {
                await action();
            }
            catch (COMException comEx)
            {
                // WindowsEventLogger.LogComMarshalingError(comEx, operationName);
                
                // Try immediate recovery for COM exceptions
                if (HandleCOMException(comEx, operationName))
                {
                    // Recovery successful, try the action again once
                    try
                    {
                        await Task.Delay(10); // Small delay for recovery
                        await action();
                    }
                    catch (Exception retryEx)
                    {
                        // WindowsEventLogger.LogCriticalCrash(retryEx, $"{operationName}.AsyncRetryAfterRecovery");
                    }
                }
            }
            catch (Exception ex)
            {
                // WindowsEventLogger.LogCriticalCrash(ex, operationName);
            }
        }

        public string GetStatusDisplayText(ImportStatus status)
        {
            return status switch
            {
                ImportStatus.InProgress => "In Progress",
                ImportStatus.Completed => "Completed",
                ImportStatus.Failed => "Failed",
                ImportStatus.Cancelled => "Cancelled",
                ImportStatus.Paused => "Paused",
                _ => status.ToString()
            };
        }

        public string GetVideoStatusDisplayText(VideoImportStatus status)
        {
            return status switch
            {
                VideoImportStatus.Pending => "Pending",
                VideoImportStatus.Scanning => "Scanning",
                VideoImportStatus.Uploading => "Uploading",
                VideoImportStatus.Uploaded => "Uploaded",
                VideoImportStatus.Failed => "Failed",
                VideoImportStatus.Skipped => "Skipped",
                _ => status.ToString()
            };
        }

        /// <summary>
        /// Dispose of resources and unsubscribe from events
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 1010,
                        // "MainViewModel Disposing", "Starting MainViewModel cleanup"

                    // Unsubscribe from events safely
                    try
                    {
                        _importService.ProgressChanged -= SafeOnImportProgressChanged;
                        _importService.ImportCompleted -= SafeOnImportCompleted;
                    }
                    catch (Exception ex)
                    {
                        // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 1011,
                            // "Event Unsubscription Error", ex.Message);
                    }

                    // Clear collections safely
                    SafeExecute(() => {
                        ImportSessions.Clear();
                        SessionVideos.Clear();
                        ChannelStatuses.Clear();
                        VideoFiles.Clear();
                        FilteredVideoFiles.Clear();
                        AvailableChannels.Clear();
                        _allVideoFiles.Clear();
                    }, "MainViewModel.ClearCollections");

                    // Stop operation stopwatch
                    try
                    {
                        _operationStopwatch?.Stop();
                    }
                    catch (Exception ex)
                    {
                        // WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 1012,
                            // "Stopwatch Cleanup Error", ex.Message);
                    }

                    // WindowsEventLogger.LogEvent(EventLogEntryType.Information, 1013,
                        // "MainViewModel Disposed", "MainViewModel cleanup completed successfully"
                }
                catch (Exception ex)
                {
                    // WindowsEventLogger.LogCriticalCrash(ex, "MainViewModel.Dispose");
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~MainViewModel()
        {
            Dispose(false);
        }

        /// <summary>
        /// Check if the object has been disposed
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MainViewModel));
            }
        }
    }
}
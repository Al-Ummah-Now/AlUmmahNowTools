using AlUmmahNow.VisualImporterUno.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.UI.Dispatching;

namespace AlUmmahNow.VisualImporterUno.ViewModels;

/// <summary>
/// ViewModel for managing file system watches and pending approvals
/// </summary>
public partial class WatchViewModel : ObservableObject, IDisposable
{
    private readonly FileScanSignalRClient _signalRClient;
    private readonly INotificationService _notificationService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _disposed = false;

    // Observable Properties - Active Watches
    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusMessage = Loc.Get("Watch_Ready");

    [ObservableProperty]
    private WatchStatusDto? _selectedWatch;

    [ObservableProperty]
    private bool _hasSelectedWatch = false;

    // Observable Properties - Statistics
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalWatchesText))]
    private int _totalWatches = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveWatchesText))]
    private int _activeWatchesCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesDetectedText))]
    private int _totalFilesDetected = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingApprovalsText))]
    private int _pendingApprovalsCount = 0;

    [ObservableProperty]
    private int _approvedCount = 0;

    [ObservableProperty]
    private int _rejectedCount = 0;

    // Observable Properties - Create Watch Dialog
    [ObservableProperty]
    private bool _isCreateDialogOpen = false;

    [ObservableProperty]
    private string _newWatchChannelName = string.Empty;

    [ObservableProperty]
    private string _newWatchPath = string.Empty;

    [ObservableProperty]
    private int _processingDelaySeconds = 5;

    [ObservableProperty]
    private bool _includeSubdirectories = true;

    [ObservableProperty]
    private string _selectedExtensions = ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm";

    [ObservableProperty]
    private bool _isCreatingWatch = false;

    [ObservableProperty]
    private string _createWatchError = string.Empty;

    // Validation Properties
    [ObservableProperty]
    private bool _isChannelNameValid = false;

    [ObservableProperty]
    private bool _isWatchPathValid = false;

    [ObservableProperty]
    private string _channelNameValidationMessage = string.Empty;

    [ObservableProperty]
    private string _watchPathValidationMessage = string.Empty;

    // Observable Properties - Pending Approvals
    [ObservableProperty]
    private PendingApprovalDto? _selectedApproval;

    [ObservableProperty]
    private bool _hasSelectedApproval = false;

    [ObservableProperty]
    private bool _hasSelectedApprovals = false;

    [ObservableProperty]
    private int _selectedApprovalsCount = 0;

    [ObservableProperty]
    private bool _showOnlyPending = true;

    [ObservableProperty]
    private string? _channelFilter = null;

    // Observable Properties - Notification Settings
    [ObservableProperty]
    private bool _enableNotifications = true;

    // Observable Properties - Batch Watch Creation
    [ObservableProperty]
    private bool _isBatchCreateDialogOpen = false;

    [ObservableProperty]
    private int _batchDialogTab = 0; // 0=Template, 1=Import

    [ObservableProperty]
    private WatchTemplate? _selectedTemplate;

    [ObservableProperty]
    private int _numberOfWatches = 3;

    [ObservableProperty]
    private int _startingNumber = 1;

    [ObservableProperty]
    private string _batchBasePath = "C:\\Videos";

    [ObservableProperty]
    private string _importFilePath = string.Empty;

    [ObservableProperty]
    private bool _isGeneratingPreview = false;

    [ObservableProperty]
    private bool _isCreatingBatch = false;

    [ObservableProperty]
    private bool _isImporting = false;

    [ObservableProperty]
    private string _batchCreateError = string.Empty;

    [ObservableProperty]
    private string _importError = string.Empty;

    [ObservableProperty]
    private int _batchCreateProgress = 0;

    [ObservableProperty]
    private int _batchCreateTotal = 0;

    [ObservableProperty]
    private WatchBatchImport? _importResult;

    public string TotalWatchesText => Loc.Number(TotalWatches);
    public string ActiveWatchesText => Loc.Number(ActiveWatchesCount);
    public string FilesDetectedText => Loc.Count(TotalFilesDetected);
    public string PendingApprovalsText => Loc.Number(PendingApprovalsCount);
    public bool HasWatches => ActiveWatches.Count > 0;
    public bool HasPendingApprovals => PendingApprovalsCount > 0;
    public bool IsTemplateTab => BatchDialogTab == 0;
    public bool IsFileTab => BatchDialogTab == 1;

    /// <summary>Problems found in an imported file, one row per entry, each with its row number.</summary>
    public string ImportProblemsText => ImportResult == null ? string.Empty : string.Join("\n\n",
        ImportResult.ValidationErrors.OrderBy(e => e.Key).Select(e => Loc.WithDetail(Loc.Format("Watch_FileRow", e.Key + 1), string.Join("\n", e.Value))));

    public bool HasImportProblems => ImportResult?.ValidationErrors.Count > 0;

    [RelayCommand]
    private void ShowBatchTab(string tab) => BatchDialogTab = tab == "file" ? 1 : 0;

    partial void OnBatchDialogTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsTemplateTab));
        OnPropertyChanged(nameof(IsFileTab));
    }

    partial void OnImportResultChanged(WatchBatchImport? value)
    {
        OnPropertyChanged(nameof(ImportProblemsText));
        OnPropertyChanged(nameof(HasImportProblems));
    }

    partial void OnPendingApprovalsCountChanged(int value) => OnPropertyChanged(nameof(HasPendingApprovals));

    // Collections
    public ObservableCollection<WatchStatusDto> ActiveWatches { get; } = new();
    public ObservableCollection<PendingApprovalDto> PendingApprovals { get; } = new();
    public ObservableCollection<PendingApprovalDto> FilteredPendingApprovals { get; } = new();
    public ObservableCollection<string> AvailableChannels { get; } = new();
    /// <summary>Channel names already watched: suggested while typing a new watch's channel name.</summary>
    public ObservableCollection<string> KnownChannelNames { get; } = new();
    public ObservableCollection<string> SupportedExtensions { get; } = new();
    public ObservableCollection<WatchTemplate> SavedTemplates { get; } = new();
    public ObservableCollection<CreateWatchRequest> PreviewWatches { get; } = new();

    // Track selected approvals for bulk operations
    private readonly HashSet<Guid> _selectedApprovalIds = new();

    public WatchViewModel(FileScanSignalRClient signalRClient, INotificationService notificationService)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        ActiveWatches.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasWatches));

        // Get dispatcher queue for UI updates with enhanced safety
        try
        {
            _dispatcherQueue = App.Current?.MainWindow?.DispatcherQueue
                ?? DispatcherQueue.GetForCurrentThread();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get dispatcher queue: {ex.Message}");
            _dispatcherQueue = null;
        }

        // Initialize supported extensions
        InitializeSupportedExtensions();

        // Subscribe to SignalR events
        try
        {
            SubscribeToEvents();

            // Initialize data in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500); // Give UI time to initialize
                    await InitializeAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background initialization failed: {ex.Message}");
                    DispatcherExecute(() =>
                    {
                        StatusMessage = Loc.GetWithDetail("Watch_StartFailed", ex.Message);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WatchViewModel initialization failed: {ex.Message}");
            StatusMessage = Loc.GetWithDetail("Watch_StartFailed", ex.Message);
        }
    }

    private void InitializeSupportedExtensions()
    {
        var extensions = new[]
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv",
            ".m4v", ".flv", ".webm", ".mpg", ".mpeg",
            ".3gp", ".ts", ".vob", ".ogv"
        };

        foreach (var ext in extensions)
        {
            SupportedExtensions.Add(ext);
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            if (!_signalRClient.IsConnected)
            {
                DispatcherExecute(() =>
                {
                    StatusMessage = Loc.Get("Watch_NotConnected");
                });
                return;
            }

            await RefreshWatchesAsync();
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = Loc.GetWithDetail("Watch_StartFailed", ex.Message);
            });
        }
    }

    private void SubscribeToEvents()
    {
        // Subscribe to SignalR connection status changes
        _signalRClient.ServiceStatusChanged += OnServiceStatusChanged;
        _signalRClient.ErrorOccurred += OnErrorOccurred;

        // Subscribe to watch-specific events
        _signalRClient.FileDetected += OnFileDetected;
        _signalRClient.WatchStatusChanged += OnWatchStatusChanged;
        _signalRClient.WatchStopped += OnWatchStopped;
        _signalRClient.WatchUpdated += OnWatchUpdated;
        _signalRClient.WatchDeleted += OnWatchDeleted;

        // Subscribe to approval-specific events
        _signalRClient.NewFileForApproval += OnNewFileForApproval;
        _signalRClient.ApprovalStatusChanged += OnApprovalStatusChanged;
        _signalRClient.ApprovalDecisionProcessed += OnApprovalDecisionProcessed;
        _signalRClient.BulkApprovalProcessed += OnBulkApprovalProcessed;
    }

    private void OnServiceStatusChanged(object? sender, ServiceStatusEventArgs e)
    {
        DispatcherExecute(() =>
        {
            if (e.Status == ServiceStatus.Connected)
            {
                StatusMessage = Loc.Get("Watch_Connected");
                _ = Task.Run(async () => await RefreshWatchesAsync());
            }
            else if (e.Status == ServiceStatus.Disconnected || e.Status == ServiceStatus.NotAvailable)
            {
                StatusMessage = Loc.Get("Watch_Disconnected");
                ActiveWatches.Clear();
                PendingApprovals.Clear();
                FilteredPendingApprovals.Clear();
            }
        });
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = Loc.GetWithDetail("Watch_ScannerProblem", error);
        });
    }

    private void OnFileDetected(object? sender, FileDetectedEventArgs e)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = Loc.GetWithDetail("Watch_FileDetected", e.FileName);

            // Update the watch's file count if it exists in our list
            var watch = ActiveWatches.FirstOrDefault(w => w.WatchId == e.WatchId);
            if (watch != null)
            {
                watch.TotalFilesDetected++;
                UpdateStatistics();
            }

            // Show notification if enabled
            if (EnableNotifications)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notificationService.ShowFileDetectedAsync(e.FileName, e.ChannelName, e.FileSize);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to show file detected notification: {ex.Message}");
                    }
                });
            }

            // Refresh pending approvals to show the new file
            _ = Task.Run(async () => await RefreshPendingApprovalsAsync());
        });
    }

    private void OnWatchStatusChanged(object? sender, WatchStatusChangedEventArgs e)
    {
        DispatcherExecute(() =>
        {
            var watch = ActiveWatches.FirstOrDefault(w => w.WatchId == e.WatchId);
            if (watch != null)
            {
                watch.IsActive = e.IsActive;
                UpdateStatistics();
            }

            StatusMessage = Loc.GetWithDetail("Watch_StatusChanged", e.ChannelName);
        });
    }

    private void OnWatchStopped(object? sender, Guid watchId)
    {
        DispatcherExecute(() =>
        {
            var watch = ActiveWatches.FirstOrDefault(w => w.WatchId == watchId);
            if (watch != null)
            {
                watch.IsActive = false;
                UpdateStatistics();
                StatusMessage = Loc.GetWithDetail("Watch_Stopped", watch.ChannelName);
            }
        });
    }

    private void OnWatchUpdated(object? sender, Guid watchId)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = Loc.Get("Watch_Updated");
            // Refresh the specific watch or all watches
            _ = Task.Run(async () => await RefreshWatchesAsync());
        });
    }

    private void OnWatchDeleted(object? sender, Guid watchId)
    {
        DispatcherExecute(() =>
        {
            var watch = ActiveWatches.FirstOrDefault(w => w.WatchId == watchId);
            if (watch != null)
            {
                ActiveWatches.Remove(watch);
                UpdateStatistics();
                UpdateAvailableChannels();
                StatusMessage = Loc.GetWithDetail("Watch_Deleted", watch.ChannelName);

                if (SelectedWatch?.WatchId == watchId)
                {
                    SelectedWatch = null;
                }
            }
        });
    }

    private void OnNewFileForApproval(object? sender, NewFileForApprovalEventArgs e)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = Loc.GetWithDetail("Watch_NewForApproval", e.FileName);

            // Refresh pending approvals
            _ = Task.Run(async () => await RefreshPendingApprovalsAsync());
        });
    }

    private void OnApprovalStatusChanged(object? sender, ApprovalStatusChangedEventArgs e)
    {
        DispatcherExecute(() =>
        {
            var approval = PendingApprovals.FirstOrDefault(a => a.Id == e.ApprovalId);
            if (approval != null)
            {
                approval.Status = e.NewStatus;
                ApplyApprovalFilters();
                UpdateStatistics();
            }

            StatusMessage = Loc.GetWithDetail("Watch_ApprovalChanged", e.FileName);
        });
    }

    private void OnApprovalDecisionProcessed(object? sender, ApprovalDecisionRequest request)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = Loc.Format("Watch_DecisionDone", request.ApprovalIds.Count);

            // Refresh approvals to reflect changes
            _ = Task.Run(async () => await RefreshPendingApprovalsAsync());
        });
    }

    private void OnBulkApprovalProcessed(object? sender, BulkApprovalProcessedEventArgs e)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = Loc.Format("Watch_DecisionDone", e.ProcessedCount);

            // Refresh approvals to reflect changes
            _ = Task.Run(async () => await RefreshPendingApprovalsAsync());
        });
    }

    // Commands - Watch Management

    [RelayCommand]
    private void OpenCreateWatchDialog()
    {
        ClearCreateWatchForm();
        IsCreateDialogOpen = true;
    }

    [RelayCommand]
    private void CloseCreateWatchDialog()
    {
        IsCreateDialogOpen = false;
        ClearCreateWatchForm();
    }

    [RelayCommand(CanExecute = nameof(CanCreateWatch))]
    private async Task CreateWatchAsync()
    {
        try
        {
            IsCreatingWatch = true;
            CreateWatchError = string.Empty;
            StatusMessage = Loc.Get("Watch_Creating");

            if (!_signalRClient.IsConnected)
            {
                CreateWatchError = Loc.Get("Watch_NotConnected");
                return;
            }

            // Validate inputs
            if (!ValidateCreateWatchInputs())
            {
                return;
            }

            // Create the watch using FileScanSignalRClient API
            var request = new CreateWatchRequest
            {
                ChannelName = NewWatchChannelName.Trim(),
                WatchPath = NewWatchPath.Trim(),
                IncludeSubdirectories = IncludeSubdirectories,
                ProcessingDelayMs = ProcessingDelaySeconds * 1000,
                FileExtensions = SelectedExtensions,
                CreatedBy = Environment.UserName
            };

            var watchId = await _signalRClient.StartWatchAsync(request);
            StatusMessage = Loc.GetWithDetail("Watch_Created", request.ChannelName);

            // Close dialog and refresh watches
            IsCreateDialogOpen = false;
            await RefreshWatchesAsync();
        }
        catch (Exception ex)
        {
            CreateWatchError = Loc.GetWithDetail("Watch_CreateFailed", ex.Message);
            StatusMessage = Loc.GetWithDetail("Watch_CreateFailed", ex.Message);
        }
        finally
        {
            IsCreatingWatch = false;
        }
    }

    private bool CanCreateWatch()
    {
        return IsChannelNameValid && IsWatchPathValid && !IsCreatingWatch;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWatch))]
    private async Task StopWatchAsync()
    {
        if (SelectedWatch == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = Loc.GetWithDetail("Watch_Stopping", SelectedWatch.ChannelName);

            var result = await _signalRClient.StopWatchAsync(SelectedWatch.WatchId);

            if (result)
            {
                StatusMessage = Loc.GetWithDetail("Watch_Stopped", SelectedWatch.ChannelName);
                await RefreshWatchesAsync();
            }
            else
            {
                StatusMessage = Loc.GetWithDetail("Watch_StopFailed", SelectedWatch.ChannelName);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Watch_StopFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWatch))]
    private async Task DeleteWatchAsync()
    {
        if (SelectedWatch == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = Loc.GetWithDetail("Watch_Deleting", SelectedWatch.ChannelName);

            var result = await _signalRClient.DeleteWatchAsync(SelectedWatch.WatchId);

            if (result)
            {
                StatusMessage = Loc.GetWithDetail("Watch_Deleted", SelectedWatch.ChannelName);
                SelectedWatch = null;
                await RefreshWatchesAsync();
            }
            else
            {
                StatusMessage = Loc.GetWithDetail("Watch_DeleteFailed", SelectedWatch.ChannelName);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Watch_DeleteFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshWatchesAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = Loc.Get("Watch_Refreshing");

            if (!_signalRClient.IsConnected)
            {
                StatusMessage = Loc.Get("Watch_NotConnected");
                return;
            }

            var watches = await _signalRClient.GetActiveWatchesAsync();

            DispatcherExecute(() =>
            {
                ActiveWatches.Clear();
                foreach (var watch in watches)
                {
                    ActiveWatches.Add(watch);
                }

                UpdateStatistics();
                UpdateAvailableChannels();
            });

            StatusMessage = Loc.Format("Watch_Loaded", watches.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Watch_RefreshFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWatch))]
    private void ViewWatchDetails()
    {
        if (SelectedWatch == null) return;

        StatusMessage = Loc.GetWithDetail("Watch_Viewing", SelectedWatch.ChannelName);

        // Filter pending approvals by selected watch
        ChannelFilter = SelectedWatch.ChannelName;
        ApplyApprovalFilters();
    }

    // Commands - Pending Approvals Management

    [RelayCommand]
    private async Task RefreshPendingApprovalsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = Loc.Get("Approve_Loading");

            if (!_signalRClient.IsConnected)
            {
                StatusMessage = Loc.Get("Watch_NotConnected");
                return;
            }

            var approvals = await _signalRClient.GetPendingApprovalsAsync();

            DispatcherExecute(() =>
            {
                PendingApprovals.Clear();
                foreach (var approval in approvals)
                {
                    PendingApprovals.Add(approval);
                }

                ApplyApprovalFilters();
                UpdateStatistics();
            });

            StatusMessage = Loc.Format("Approve_Loaded", approvals.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Approve_LoadFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApproval))]
    private async Task ApproveSelectedAsync()
    {
        if (SelectedApproval == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = Loc.Get("Approve_Approving");

            var fileName = SelectedApproval.FileName;
            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = new List<Guid> { SelectedApproval.Id },
                Decision = ApprovalStatus.Approved,
                ApprovedBy = Environment.UserName
            };

            var result = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (result)
            {
                StatusMessage = Loc.GetWithDetail("Approve_ApprovedOne", fileName);

                // Show notification if enabled
                if (EnableNotifications)
                {
                    await _notificationService.ShowApprovalProcessedAsync(fileName, approved: true, count: 1);
                }

                await RefreshPendingApprovalsAsync();
            }
            else
            {
                StatusMessage = Loc.GetWithDetail("Approve_ApproveFailed", fileName);
                if (EnableNotifications)
                {
                    await _notificationService.ShowErrorAsync(Loc.Get("Approve_ApproveFailed"), fileName);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Approve_ApproveFailed", ex.Message);
            if (EnableNotifications)
            {
                await _notificationService.ShowErrorAsync(Loc.Get("Approve_ApproveFailed"), ex.Message);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApproval))]
    private async Task RejectSelectedAsync()
    {
        if (SelectedApproval == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = Loc.Get("Approve_Rejecting");

            var fileName = SelectedApproval.FileName;
            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = new List<Guid> { SelectedApproval.Id },
                Decision = ApprovalStatus.Rejected,
                ApprovedBy = Environment.UserName
            };

            var result = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (result)
            {
                StatusMessage = Loc.GetWithDetail("Approve_RejectedOne", fileName);

                // Show notification if enabled
                if (EnableNotifications)
                {
                    await _notificationService.ShowApprovalProcessedAsync(fileName, approved: false, count: 1);
                }

                await RefreshPendingApprovalsAsync();
            }
            else
            {
                StatusMessage = Loc.GetWithDetail("Approve_RejectFailed", fileName);
                if (EnableNotifications)
                {
                    await _notificationService.ShowErrorAsync(Loc.Get("Approve_RejectFailed"), fileName);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Approve_RejectFailed", ex.Message);
            if (EnableNotifications)
            {
                await _notificationService.ShowErrorAsync(Loc.Get("Approve_RejectFailed"), ex.Message);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApprovals))]
    private async Task ApproveSelectedItemsAsync()
    {
        try
        {
            IsLoading = true;
            var count = _selectedApprovalIds.Count;
            StatusMessage = Loc.Format("Approve_ApprovingMany", count);

            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = _selectedApprovalIds.ToList(),
                Decision = ApprovalStatus.Approved,
                ApprovedBy = Environment.UserName
            };

            var result = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (result)
            {
                StatusMessage = Loc.Format("Approve_ApprovedMany", count);

                // Show notification if enabled
                if (EnableNotifications)
                {
                    await _notificationService.ShowApprovalProcessedAsync(string.Empty, approved: true, count: count);
                }

                _selectedApprovalIds.Clear();
                await RefreshPendingApprovalsAsync();
            }
            else
            {
                StatusMessage = Loc.Format("Approve_ApproveManyFailed", count);
                if (EnableNotifications)
                {
                    await _notificationService.ShowErrorAsync(Loc.Get("Approve_ApproveFailed"), Loc.Format("Approve_ApproveManyFailed", count));
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Approve_ApproveFailed", ex.Message);
            if (EnableNotifications)
            {
                await _notificationService.ShowErrorAsync(Loc.Get("Approve_ApproveFailed"), ex.Message);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApprovals))]
    private async Task RejectSelectedItemsAsync()
    {
        try
        {
            IsLoading = true;
            var count = _selectedApprovalIds.Count;
            StatusMessage = Loc.Format("Approve_RejectingMany", count);

            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = _selectedApprovalIds.ToList(),
                Decision = ApprovalStatus.Rejected,
                ApprovedBy = Environment.UserName
            };

            var result = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (result)
            {
                StatusMessage = Loc.Format("Approve_RejectedMany", count);

                // Show notification if enabled
                if (EnableNotifications)
                {
                    await _notificationService.ShowApprovalProcessedAsync(string.Empty, approved: false, count: count);
                }

                _selectedApprovalIds.Clear();
                await RefreshPendingApprovalsAsync();
            }
            else
            {
                StatusMessage = Loc.Format("Approve_RejectManyFailed", count);
                if (EnableNotifications)
                {
                    await _notificationService.ShowErrorAsync(Loc.Get("Approve_RejectFailed"), Loc.Format("Approve_RejectManyFailed", count));
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Approve_RejectFailed", ex.Message);
            if (EnableNotifications)
            {
                await _notificationService.ShowErrorAsync(Loc.Get("Approve_RejectFailed"), ex.Message);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectAllApprovals()
    {
        _selectedApprovalIds.Clear();
        foreach (var approval in FilteredPendingApprovals.Where(a => a.Status == ApprovalStatus.Pending))
        {
            _selectedApprovalIds.Add(approval.Id);
        }
        UpdateSelectionState();
    }

    [RelayCommand]
    private void DeselectAllApprovals()
    {
        _selectedApprovalIds.Clear();
        UpdateSelectionState();
    }

    [RelayCommand]
    private void ToggleApprovalSelection(PendingApprovalDto approval)
    {
        if (approval == null) return;

        if (_selectedApprovalIds.Contains(approval.Id))
        {
            _selectedApprovalIds.Remove(approval.Id);
        }
        else
        {
            _selectedApprovalIds.Add(approval.Id);
        }

        UpdateSelectionState();
    }

    // Commands - Batch Watch Creation

    [RelayCommand]
    private async Task OpenBatchCreateDialogAsync()
    {
        IsBatchCreateDialogOpen = true;
        BatchDialogTab = 0;
        BatchCreateError = string.Empty;
        ImportError = string.Empty;
        PreviewWatches.Clear();

        // Load templates when opening the dialog
        await LoadTemplatesAsync();
    }

    [RelayCommand]
    private void CloseBatchCreateDialog()
    {
        IsBatchCreateDialogOpen = false;
        PreviewWatches.Clear();
        ImportResult = null;
    }

    [RelayCommand]
    private async Task GeneratePreviewAsync()
    {
        if (SelectedTemplate == null) return;

        try
        {
            IsGeneratingPreview = true;
            BatchCreateError = string.Empty;
            PreviewWatches.Clear();

            // Create a temp service instance (in prod, this should be injected)
            var templateService = new AlUmmahNow.Data.Services.WatchTemplateService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AlUmmahNow.Data.Services.WatchTemplateService>.Instance);

            // Clone template and update base path
            var template = new WatchTemplate
            {
                Id = SelectedTemplate.Id,
                Name = SelectedTemplate.Name,
                Description = SelectedTemplate.Description,
                BaseChannelNamePattern = SelectedTemplate.BaseChannelNamePattern,
                BasePathPattern = BatchBasePath,
                ProcessingDelayMs = SelectedTemplate.ProcessingDelayMs,
                IncludeSubdirectories = SelectedTemplate.IncludeSubdirectories,
                SupportedExtensions = SelectedTemplate.SupportedExtensions,
                IsBuiltIn = SelectedTemplate.IsBuiltIn,
                CreatedBy = SelectedTemplate.CreatedBy
            };

            var preview = await templateService.GenerateWatchesFromTemplateAsync(
                template,
                NumberOfWatches,
                StartingNumber);

            DispatcherExecute(() =>
            {
                foreach (var watch in preview.PreviewWatches)
                {
                    PreviewWatches.Add(watch);
                }
            });

            StatusMessage = Loc.Format("Watch_PreviewReady", preview.Count);
        }
        catch (Exception ex)
        {
            BatchCreateError = Loc.GetWithDetail("Watch_PreviewFailed", ex.Message);
            StatusMessage = Loc.GetWithDetail("Watch_PreviewFailed", ex.Message);
        }
        finally
        {
            IsGeneratingPreview = false;
        }
    }

    [RelayCommand]
    private async Task CreateWatchesFromTemplateAsync()
    {
        if (PreviewWatches.Count == 0)
        {
            BatchCreateError = Loc.Get("Watch_PreviewFirst");
            return;
        }

        try
        {
            IsCreatingBatch = true;
            BatchCreateError = string.Empty;
            BatchCreateProgress = 0;
            BatchCreateTotal = PreviewWatches.Count;

            if (!_signalRClient.IsConnected)
            {
                BatchCreateError = Loc.Get("Watch_NotConnected");
                return;
            }

            var successCount = 0;
            var failureCount = 0;

            foreach (var watch in PreviewWatches)
            {
                try
                {
                    await _signalRClient.StartWatchAsync(watch);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    System.Diagnostics.Debug.WriteLine($"Failed to create watch {watch.ChannelName}: {ex.Message}");
                }

                BatchCreateProgress++;
                StatusMessage = Loc.Format("Watch_CreatingMany", BatchCreateProgress, BatchCreateTotal);
            }

            StatusMessage = Loc.Format("Watch_ManyDone", successCount, failureCount);

            if (successCount > 0)
            {
                IsBatchCreateDialogOpen = false;
                await RefreshWatchesAsync();

                if (EnableNotifications)
                {
                    await _notificationService.ShowSuccessAsync(Loc.Get("Watch_ManyDoneTitle"),
                        Loc.Format("Watch_ManyCreated", successCount));
                }
            }
            else
            {
                BatchCreateError = Loc.Get("Watch_ManyAllFailed");
            }
        }
        catch (Exception ex)
        {
            BatchCreateError = Loc.GetWithDetail("Watch_CreateFailed", ex.Message);
            StatusMessage = Loc.GetWithDetail("Watch_CreateFailed", ex.Message);
        }
        finally
        {
            IsCreatingBatch = false;
            BatchCreateProgress = 0;
            BatchCreateTotal = 0;
        }
    }

    [RelayCommand]
    private async Task ImportWatchesFromFileAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ImportFilePath))
            {
                ImportError = Loc.Get("Watch_FileNeeded");
                return;
            }

            if (!System.IO.File.Exists(ImportFilePath))
            {
                ImportError = Loc.Get("Watch_FileMissing");
                return;
            }

            IsImporting = true;
            ImportError = string.Empty;

            // Create a temp service instance (in prod, this should be injected)
            var templateService = new AlUmmahNow.Data.Services.WatchTemplateService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AlUmmahNow.Data.Services.WatchTemplateService>.Instance);

            WatchBatchImport? result;
            var ext = System.IO.Path.GetExtension(ImportFilePath).ToLowerInvariant();

            if (ext == ".csv")
            {
                result = await templateService.ImportFromCsvAsync(ImportFilePath);
            }
            else if (ext == ".json")
            {
                result = await templateService.ImportFromJsonAsync(ImportFilePath);
            }
            else
            {
                ImportError = Loc.Get("Watch_FileFormat");
                return;
            }

            ImportResult = result;

            if (result.IsValid)
            {
                DispatcherExecute(() =>
                {
                    PreviewWatches.Clear();
                    foreach (var watch in result.WatchConfigurations)
                    {
                        PreviewWatches.Add(watch);
                    }
                });

                StatusMessage = Loc.Format("Watch_FileChecked", result.ValidCount, result.InvalidCount);
            }
            else
            {
                ImportError = Loc.Format("Watch_FileChecked", result.ValidCount, result.InvalidCount);
                StatusMessage = Loc.Format("Watch_FileChecked", result.ValidCount, result.InvalidCount);
            }
        }
        catch (Exception ex)
        {
            ImportError = Loc.GetWithDetail("Watch_FileFailed", ex.Message);
            StatusMessage = Loc.GetWithDetail("Watch_FileFailed", ex.Message);
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task CreateImportedWatchesAsync()
    {
        if (ImportResult == null || !ImportResult.IsValid)
        {
            ImportError = Loc.Get("Watch_FileNoneValid");
            return;
        }

        try
        {
            IsCreatingBatch = true;
            ImportError = string.Empty;
            BatchCreateProgress = 0;
            BatchCreateTotal = ImportResult.WatchConfigurations.Count;

            if (!_signalRClient.IsConnected)
            {
                ImportError = Loc.Get("Watch_NotConnected");
                return;
            }

            var successCount = 0;
            var failureCount = 0;

            foreach (var watch in ImportResult.WatchConfigurations)
            {
                try
                {
                    await _signalRClient.StartWatchAsync(watch);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    System.Diagnostics.Debug.WriteLine($"Failed to create watch {watch.ChannelName}: {ex.Message}");
                }

                BatchCreateProgress++;
                StatusMessage = Loc.Format("Watch_CreatingMany", BatchCreateProgress, BatchCreateTotal);
            }

            StatusMessage = Loc.Format("Watch_ManyDone", successCount, failureCount);

            if (successCount > 0)
            {
                IsBatchCreateDialogOpen = false;
                await RefreshWatchesAsync();

                if (EnableNotifications)
                {
                    await _notificationService.ShowSuccessAsync(Loc.Get("Watch_ManyDoneTitle"),
                        Loc.Format("Watch_ManyCreated", successCount));
                }
            }
            else
            {
                ImportError = Loc.Get("Watch_ManyAllFailed");
            }
        }
        catch (Exception ex)
        {
            ImportError = Loc.GetWithDetail("Watch_FileFailed", ex.Message);
            StatusMessage = Loc.GetWithDetail("Watch_FileFailed", ex.Message);
        }
        finally
        {
            IsCreatingBatch = false;
            BatchCreateProgress = 0;
            BatchCreateTotal = 0;
        }
    }

    [RelayCommand]
    private async Task LoadTemplatesAsync()
    {
        try
        {
            // Create a temp service instance (in prod, this should be injected)
            var templateService = new AlUmmahNow.Data.Services.WatchTemplateService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AlUmmahNow.Data.Services.WatchTemplateService>.Instance);

            var templates = await templateService.GetTemplatesAsync();

            DispatcherExecute(() =>
            {
                SavedTemplates.Clear();
                foreach (var template in templates)
                {
                    SavedTemplates.Add(template);
                }
            });

            StatusMessage = Loc.Format("Watch_TemplatesLoaded", templates.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Watch_TemplatesFailed", ex.Message);
        }
    }

    // Helper Methods - Validation

    partial void OnNewWatchChannelNameChanged(string value)
    {
        ValidateChannelName(value);
        CreateWatchCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewWatchPathChanged(string value)
    {
        ValidateWatchPath(value);
        CreateWatchCommand.NotifyCanExecuteChanged();
    }

    private void ValidateChannelName(string channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            IsChannelNameValid = false;
            ChannelNameValidationMessage = Loc.Get("Watch_NameRequired");
        }
        else if (channelName.Length < 2)
        {
            IsChannelNameValid = false;
            ChannelNameValidationMessage = Loc.Get("Watch_NameShort");
        }
        else if (channelName.Length > 100)
        {
            IsChannelNameValid = false;
            ChannelNameValidationMessage = Loc.Get("Watch_NameLong");
        }
        else if (ActiveWatches.Any(w => w.ChannelName.Equals(channelName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            IsChannelNameValid = false;
            ChannelNameValidationMessage = Loc.Get("Watch_NameTaken");
        }
        else
        {
            IsChannelNameValid = true;
            ChannelNameValidationMessage = string.Empty;
        }
    }

    private void ValidateWatchPath(string watchPath)
    {
        if (string.IsNullOrWhiteSpace(watchPath))
        {
            IsWatchPathValid = false;
            WatchPathValidationMessage = Loc.Get("Watch_PathRequired");
        }
        else if (!System.IO.Directory.Exists(watchPath))
        {
            IsWatchPathValid = false;
            WatchPathValidationMessage = Loc.Get("Watch_PathMissing");
        }
        else if (ActiveWatches.Any(w => w.WatchPath.Equals(watchPath.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            IsWatchPathValid = false;
            WatchPathValidationMessage = Loc.Get("Watch_PathTaken");
        }
        else
        {
            IsWatchPathValid = true;
            WatchPathValidationMessage = string.Empty;
        }
    }

    private bool ValidateCreateWatchInputs()
    {
        ValidateChannelName(NewWatchChannelName);
        ValidateWatchPath(NewWatchPath);

        if (!IsChannelNameValid || !IsWatchPathValid)
        {
            CreateWatchError = Loc.Get("Watch_FixFields");
            return false;
        }

        if (ProcessingDelaySeconds < 1 || ProcessingDelaySeconds > 300)
        {
            CreateWatchError = Loc.Get("Watch_DelayRange");
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedExtensions))
        {
            CreateWatchError = Loc.Get("Watch_ExtensionsNeeded");
            return false;
        }

        return true;
    }

    private void ClearCreateWatchForm()
    {
        NewWatchChannelName = string.Empty;
        NewWatchPath = string.Empty;
        ProcessingDelaySeconds = 5;
        IncludeSubdirectories = true;
        SelectedExtensions = ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm";
        CreateWatchError = string.Empty;
        IsChannelNameValid = false;
        IsWatchPathValid = false;
        ChannelNameValidationMessage = string.Empty;
        WatchPathValidationMessage = string.Empty;
    }

    // Helper Methods - Filtering and Statistics

    partial void OnSelectedWatchChanged(WatchStatusDto? value)
    {
        HasSelectedWatch = value != null;
        StopWatchCommand.NotifyCanExecuteChanged();
        DeleteWatchCommand.NotifyCanExecuteChanged();
        ViewWatchDetailsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedApprovalChanged(PendingApprovalDto? value)
    {
        HasSelectedApproval = value != null;
        ApproveSelectedCommand.NotifyCanExecuteChanged();
        RejectSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowOnlyPendingChanged(bool value)
    {
        ApplyApprovalFilters();
    }

    partial void OnChannelFilterChanged(string? value)
    {
        ApplyApprovalFilters();
    }

    private void ApplyApprovalFilters()
    {
        var filtered = PendingApprovals.AsEnumerable();

        // Filter by status
        if (ShowOnlyPending)
        {
            filtered = filtered.Where(a => a.Status == ApprovalStatus.Pending);
        }

        // Filter by channel (MainViewModel.AllChannels is the list's own entry for no filter)
        if (!string.IsNullOrWhiteSpace(ChannelFilter) && ChannelFilter != MainViewModel.AllChannels)
        {
            filtered = filtered.Where(a => a.ChannelName.Equals(ChannelFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Sort by detected date (newest first)
        filtered = filtered.OrderByDescending(a => a.DetectedAt);

        FilteredPendingApprovals.Clear();
        foreach (var approval in filtered)
        {
            FilteredPendingApprovals.Add(approval);
        }
    }

    private void UpdateStatistics()
    {
        TotalWatches = ActiveWatches.Count;
        ActiveWatchesCount = ActiveWatches.Count(w => w.IsActive);
        TotalFilesDetected = ActiveWatches.Sum(w => w.TotalFilesDetected);
        // Each watch reports its own waiting count, so this is right before the approvals list has loaded
        PendingApprovalsCount = ActiveWatches.Sum(w => w.PendingApprovals);
        ApprovedCount = PendingApprovals.Count(a => a.Status == ApprovalStatus.Approved || a.Status == ApprovalStatus.AutoApproved);
        RejectedCount = PendingApprovals.Count(a => a.Status == ApprovalStatus.Rejected);
    }

    private void UpdateAvailableChannels()
    {
        var channels = ActiveWatches
            .Select(w => w.ChannelName)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        AvailableChannels.Clear();
        AvailableChannels.Add(MainViewModel.AllChannels);
        KnownChannelNames.Clear();
        foreach (var channel in channels)
        {
            AvailableChannels.Add(channel);
            KnownChannelNames.Add(channel);
        }
    }

    private void UpdateSelectionState()
    {
        HasSelectedApprovals = _selectedApprovalIds.Count > 0;
        SelectedApprovalsCount = _selectedApprovalIds.Count;
        ApproveSelectedItemsCommand.NotifyCanExecuteChanged();
        RejectSelectedItemsCommand.NotifyCanExecuteChanged();
    }

    // Helper Methods - UI Thread Safety

    private void DispatcherExecute(Action action)
    {
        if (_dispatcherQueue?.HasThreadAccess == true)
        {
            action();
        }
        else if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => action());
        }
        else
        {
            // Fallback: execute directly
            action();
        }
    }

    // IDisposable Implementation

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            try
            {
                // Unsubscribe from SignalR connection status events
                _signalRClient.ServiceStatusChanged -= OnServiceStatusChanged;
                _signalRClient.ErrorOccurred -= OnErrorOccurred;

                // Unsubscribe from watch-specific events
                _signalRClient.FileDetected -= OnFileDetected;
                _signalRClient.WatchStatusChanged -= OnWatchStatusChanged;
                _signalRClient.WatchStopped -= OnWatchStopped;
                _signalRClient.WatchUpdated -= OnWatchUpdated;
                _signalRClient.WatchDeleted -= OnWatchDeleted;

                // Unsubscribe from approval-specific events
                _signalRClient.NewFileForApproval -= OnNewFileForApproval;
                _signalRClient.ApprovalStatusChanged -= OnApprovalStatusChanged;
                _signalRClient.ApprovalDecisionProcessed -= OnApprovalDecisionProcessed;
                _signalRClient.BulkApprovalProcessed -= OnBulkApprovalProcessed;

                // Clear collections
                ActiveWatches.Clear();
                PendingApprovals.Clear();
                FilteredPendingApprovals.Clear();
                AvailableChannels.Clear();
                SupportedExtensions.Clear();
                _selectedApprovalIds.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during WatchViewModel disposal: {ex.Message}");
            }
        }
    }
}

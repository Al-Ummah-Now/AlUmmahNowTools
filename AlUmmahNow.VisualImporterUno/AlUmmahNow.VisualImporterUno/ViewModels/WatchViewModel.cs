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
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _disposed = false;

    // Observable Properties - Active Watches
    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusMessage = "Ready to manage watches";

    [ObservableProperty]
    private WatchStatusDto? _selectedWatch;

    [ObservableProperty]
    private bool _hasSelectedWatch = false;

    // Observable Properties - Statistics
    [ObservableProperty]
    private int _totalWatches = 0;

    [ObservableProperty]
    private int _activeWatchesCount = 0;

    [ObservableProperty]
    private int _totalFilesDetected = 0;

    [ObservableProperty]
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

    // Collections
    public ObservableCollection<WatchStatusDto> ActiveWatches { get; } = new();
    public ObservableCollection<PendingApprovalDto> PendingApprovals { get; } = new();
    public ObservableCollection<PendingApprovalDto> FilteredPendingApprovals { get; } = new();
    public ObservableCollection<string> AvailableChannels { get; } = new();
    public ObservableCollection<string> SupportedExtensions { get; } = new();

    // Track selected approvals for bulk operations
    private readonly HashSet<Guid> _selectedApprovalIds = new();

    public WatchViewModel(FileScanSignalRClient signalRClient)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));

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
                        StatusMessage = $"Initialization failed: {ex.Message}";
                    });
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WatchViewModel initialization failed: {ex.Message}");
            StatusMessage = $"Initialization failed: {ex.Message}";
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
                    StatusMessage = "SignalR not connected. Please connect to the service first.";
                });
                return;
            }

            await RefreshWatchesAsync();
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Failed to initialize: {ex.Message}";
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
                StatusMessage = "Connected to FileScan service";
                _ = Task.Run(async () => await RefreshWatchesAsync());
            }
            else if (e.Status == ServiceStatus.Disconnected || e.Status == ServiceStatus.NotAvailable)
            {
                StatusMessage = "Disconnected from FileScan service";
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
            StatusMessage = $"Error: {error}";
        });
    }

    private void OnFileDetected(object? sender, FileDetectedEventArgs e)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = $"File detected: {e.FileName} in {e.ChannelName}";

            // Update the watch's file count if it exists in our list
            var watch = ActiveWatches.FirstOrDefault(w => w.WatchId == e.WatchId);
            if (watch != null)
            {
                watch.TotalFilesDetected++;
                UpdateStatistics();
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

            StatusMessage = $"Watch status changed: {e.ChannelName} - {e.StatusMessage}";
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
                StatusMessage = $"Watch stopped: {watch.ChannelName}";
            }
        });
    }

    private void OnWatchUpdated(object? sender, Guid watchId)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = "Watch updated";
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
                StatusMessage = $"Watch deleted: {watch.ChannelName}";

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
            StatusMessage = $"New file for approval: {e.FileName} in {e.ChannelName}";

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

            StatusMessage = $"Approval status changed: {e.FileName} - {e.NewStatus}";
        });
    }

    private void OnApprovalDecisionProcessed(object? sender, ApprovalDecisionRequest request)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = $"Approval decision processed: {request.ApprovalIds.Count} files";

            // Refresh approvals to reflect changes
            _ = Task.Run(async () => await RefreshPendingApprovalsAsync());
        });
    }

    private void OnBulkApprovalProcessed(object? sender, BulkApprovalProcessedEventArgs e)
    {
        DispatcherExecute(() =>
        {
            StatusMessage = $"Bulk approval processed: {e.ProcessedCount} files";

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
            StatusMessage = "Creating watch...";

            if (!_signalRClient.IsConnected)
            {
                CreateWatchError = "Not connected to FileScan service";
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
            StatusMessage = $"Watch created for channel '{request.ChannelName}' (ID: {watchId})";

            // Close dialog and refresh watches
            IsCreateDialogOpen = false;
            await RefreshWatchesAsync();
        }
        catch (Exception ex)
        {
            CreateWatchError = $"Failed to create watch: {ex.Message}";
            StatusMessage = $"Failed to create watch: {ex.Message}";
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
            StatusMessage = $"Stopping watch for '{SelectedWatch.ChannelName}'...";

            var result = await _signalRClient.StopWatchAsync(SelectedWatch.WatchId);

            if (result)
            {
                StatusMessage = $"Watch stopped for '{SelectedWatch.ChannelName}'";
                await RefreshWatchesAsync();
            }
            else
            {
                StatusMessage = $"Failed to stop watch for '{SelectedWatch.ChannelName}'";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to stop watch: {ex.Message}";
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
            StatusMessage = $"Deleting watch for '{SelectedWatch.ChannelName}'...";

            var result = await _signalRClient.DeleteWatchAsync(SelectedWatch.WatchId);

            if (result)
            {
                StatusMessage = $"Watch deleted for '{SelectedWatch.ChannelName}'";
                SelectedWatch = null;
                await RefreshWatchesAsync();
            }
            else
            {
                StatusMessage = $"Failed to delete watch for '{SelectedWatch.ChannelName}'";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete watch: {ex.Message}";
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
            StatusMessage = "Refreshing watches...";

            if (!_signalRClient.IsConnected)
            {
                StatusMessage = "Not connected to FileScan service";
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

            StatusMessage = $"Loaded {watches.Count} active watches";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to refresh watches: {ex.Message}";
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

        StatusMessage = $"Viewing details for '{SelectedWatch.ChannelName}'";

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
            StatusMessage = "Refreshing pending approvals...";

            if (!_signalRClient.IsConnected)
            {
                StatusMessage = "Not connected to FileScan service";
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

            StatusMessage = $"Loaded {approvals.Count} pending approvals";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to refresh approvals: {ex.Message}";
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
            StatusMessage = "Approving file...";

            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = new List<Guid> { SelectedApproval.Id },
                Decision = ApprovalStatus.Approved,
                ApprovedBy = Environment.UserName
            };

            var result = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (result)
            {
                StatusMessage = $"File approved: {SelectedApproval.FileName}";
                await RefreshPendingApprovalsAsync();
            }
            else
            {
                StatusMessage = $"Failed to approve file: {SelectedApproval.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to approve file: {ex.Message}";
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
            StatusMessage = "Rejecting file...";

            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = new List<Guid> { SelectedApproval.Id },
                Decision = ApprovalStatus.Rejected,
                ApprovedBy = Environment.UserName
            };

            var result = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (result)
            {
                StatusMessage = $"File rejected: {SelectedApproval.FileName}";
                await RefreshPendingApprovalsAsync();
            }
            else
            {
                StatusMessage = $"Failed to reject file: {SelectedApproval.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to reject file: {ex.Message}";
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
            StatusMessage = $"Approving {_selectedApprovalIds.Count} files...";

            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = _selectedApprovalIds.ToList(),
                Decision = ApprovalStatus.Approved,
                ApprovedBy = Environment.UserName
            };

            var result = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (result)
            {
                StatusMessage = $"Approved {_selectedApprovalIds.Count} files";
                _selectedApprovalIds.Clear();
                await RefreshPendingApprovalsAsync();
            }
            else
            {
                StatusMessage = $"Failed to approve {_selectedApprovalIds.Count} files";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to approve files: {ex.Message}";
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
            StatusMessage = $"Rejecting {_selectedApprovalIds.Count} files...";

            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = _selectedApprovalIds.ToList(),
                Decision = ApprovalStatus.Rejected,
                ApprovedBy = Environment.UserName
            };

            var result = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (result)
            {
                StatusMessage = $"Rejected {_selectedApprovalIds.Count} files";
                _selectedApprovalIds.Clear();
                await RefreshPendingApprovalsAsync();
            }
            else
            {
                StatusMessage = $"Failed to reject {_selectedApprovalIds.Count} files";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to reject files: {ex.Message}";
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
            ChannelNameValidationMessage = "Channel name is required";
        }
        else if (channelName.Length < 2)
        {
            IsChannelNameValid = false;
            ChannelNameValidationMessage = "Channel name must be at least 2 characters";
        }
        else if (channelName.Length > 100)
        {
            IsChannelNameValid = false;
            ChannelNameValidationMessage = "Channel name must not exceed 100 characters";
        }
        else if (ActiveWatches.Any(w => w.ChannelName.Equals(channelName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            IsChannelNameValid = false;
            ChannelNameValidationMessage = "A watch for this channel already exists";
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
            WatchPathValidationMessage = "Watch path is required";
        }
        else if (!System.IO.Directory.Exists(watchPath))
        {
            IsWatchPathValid = false;
            WatchPathValidationMessage = "Directory does not exist";
        }
        else if (ActiveWatches.Any(w => w.WatchPath.Equals(watchPath.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            IsWatchPathValid = false;
            WatchPathValidationMessage = "This path is already being watched";
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
            CreateWatchError = "Please fix validation errors";
            return false;
        }

        if (ProcessingDelaySeconds < 1 || ProcessingDelaySeconds > 300)
        {
            CreateWatchError = "Processing delay must be between 1 and 300 seconds";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedExtensions))
        {
            CreateWatchError = "At least one file extension must be selected";
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

        // Filter by channel
        if (!string.IsNullOrWhiteSpace(ChannelFilter))
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
        PendingApprovalsCount = PendingApprovals.Count(a => a.Status == ApprovalStatus.Pending);
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
        AvailableChannels.Add("All Channels");
        foreach (var channel in channels)
        {
            AvailableChannels.Add(channel);
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

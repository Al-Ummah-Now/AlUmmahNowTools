using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Services;
using AlUmmahNow.Data.Models.Video;
using AlUmmahNow.Data.Video;
using AlUmmahNow.VisualImporterUno.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using System.ComponentModel;

namespace AlUmmahNow.VisualImporterUno.ViewModels;

/// <summary>
/// ViewModel for managing file approval workflow with real-time SignalR updates
/// </summary>
public partial class ApprovalViewModel : ObservableObject, IDisposable
{
    private readonly FileScanSignalRClient _signalRClient;
    private readonly IFilterPresetService _filterPresetService;
    private readonly INotificationService _notificationService;
    private readonly VideoThumbnailService _videoThumbnailService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _disposed = false;

    #region Observable Properties

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private PendingApprovalDto? _currentApproval;

    [ObservableProperty]
    private ApprovalStatistics _statistics = new ApprovalStatistics();

    [ObservableProperty]
    private string? _channelFilter;

    [ObservableProperty]
    private int? _priorityFilter;

    [ObservableProperty]
    private long? _minSizeFilter;

    [ObservableProperty]
    private long? _maxSizeFilter;

    [ObservableProperty]
    private DateTime? _dateFromFilter;

    [ObservableProperty]
    private DateTime? _dateToFilter;

    [ObservableProperty]
    private string? _extensionFilter;

    [ObservableProperty]
    private bool _hasSelection = false;

    [ObservableProperty]
    private int _selectedCount = 0;

    [ObservableProperty]
    private bool _isProcessingApproval = false;

    [ObservableProperty]
    private bool _isProcessingBulkApproval = false;

    [ObservableProperty]
    private string _sortBy = "DetectedAt";

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private bool _showOnlyPending = true;

    [ObservableProperty]
    private FilterPreset? _selectedPreset;

    [ObservableProperty]
    private bool _isLoadingPresets = false;

    [ObservableProperty]
    private string _newPresetName = string.Empty;

    [ObservableProperty]
    private string _newPresetDescription = string.Empty;

    [ObservableProperty]
    private bool _setAsDefault = false;

    [ObservableProperty]
    private bool _enableNotifications = true;

    [ObservableProperty]
    private string? _currentThumbnailPath;

    [ObservableProperty]
    private VideoMetadata? _currentVideoMetadata;

    [ObservableProperty]
    private bool _isGeneratingThumbnail = false;

    #endregion

    #region Collections

    /// <summary>
    /// All pending approvals loaded from the service
    /// </summary>
    public ObservableCollection<PendingApprovalDto> PendingApprovals { get; } = new();

    /// <summary>
    /// Filtered and sorted approvals based on current filters
    /// </summary>
    public ObservableCollection<PendingApprovalDto> FilteredApprovals { get; } = new();

    /// <summary>
    /// Currently selected approvals for bulk operations
    /// </summary>
    public ObservableCollection<PendingApprovalDto> SelectedApprovals { get; } = new();

    /// <summary>
    /// Available channels for filtering
    /// </summary>
    public ObservableCollection<string> AvailableChannels { get; } = new();

    /// <summary>
    /// Log messages for approval activities
    /// </summary>
    public ObservableCollection<ApprovalLogMessage> ActivityLog { get; } = new();

    /// <summary>
    /// Saved filter presets
    /// </summary>
    public ObservableCollection<FilterPreset> SavedPresets { get; } = new();

    #endregion

    public ApprovalViewModel(FileScanSignalRClient signalRClient, IFilterPresetService filterPresetService, INotificationService notificationService, VideoThumbnailService videoThumbnailService)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
        _filterPresetService = filterPresetService ?? throw new ArgumentNullException(nameof(filterPresetService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _videoThumbnailService = videoThumbnailService ?? throw new ArgumentNullException(nameof(videoThumbnailService));

        // Get dispatcher queue for thread-safe UI updates
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

        // Subscribe to SignalR events for real-time updates
        SubscribeToSignalREvents();

        // Initialize with background data load
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
                    AddLog($"Failed to initialize: {ex.Message}", LogType.Error);
                });
            }
        });

        // Subscribe to property changes for filter application
        PropertyChanged += OnPropertyChanged;
    }

    #region Initialization

    private async Task InitializeAsync()
    {
        try
        {
            DispatcherExecute(() =>
            {
                IsLoading = true;
                StatusMessage = "Connecting to approval service...";
            });

            // Ensure SignalR connection
            if (!_signalRClient.IsConnected)
            {
                var connected = await _signalRClient.ConnectAsync();
                if (!connected)
                {
                    throw new InvalidOperationException("Failed to connect to FileScan service");
                }
            }

            // Join the approvals group for notifications
            await JoinApprovalsGroupAsync();

            // Load initial data
            await RefreshApprovalsAsync();

            // Load filter presets
            await LoadPresetsAsync();

            // Apply default preset if exists
            await ApplyDefaultPresetAsync();

            DispatcherExecute(() =>
            {
                StatusMessage = "Ready";
                AddLog("Approval workflow initialized successfully", LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Initialization error: {ex.Message}";
                AddLog($"Initialization error: {ex.Message}", LogType.Error);
            });
        }
        finally
        {
            DispatcherExecute(() => IsLoading = false);
        }
    }

    private async Task JoinApprovalsGroupAsync()
    {
        try
        {
            // This would typically be a method on the SignalR client
            // For now, we assume the connection automatically subscribes
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to join approvals group: {ex.Message}");
        }
    }

    #endregion

    #region SignalR Event Handlers

    private void SubscribeToSignalREvents()
    {
        // Note: These events would need to be added to FileScanSignalRClient
        // For now, we'll document what events we need to subscribe to

        // _signalRClient.NewFileForApproval += OnNewFileForApproval;
        // _signalRClient.ApprovalStatusChanged += OnApprovalStatusChanged;
        // _signalRClient.ApprovalDecisionProcessed += OnApprovalDecisionProcessed;
        // _signalRClient.BulkApprovalProcessed += OnBulkApprovalProcessed;
    }

    private void OnNewFileForApproval(object? sender, EventArgs e)
    {
        DispatcherExecute(async () =>
        {
            try
            {
                AddLog("New file detected - refreshing approvals", LogType.Information);
                await RefreshApprovalsAsync();
            }
            catch (Exception ex)
            {
                AddLog($"Failed to refresh after new file: {ex.Message}", LogType.Error);
            }
        });
    }

    private void OnApprovalStatusChanged(object? sender, EventArgs e)
    {
        DispatcherExecute(async () =>
        {
            try
            {
                AddLog("Approval status changed - refreshing", LogType.Information);
                await RefreshApprovalsAsync();
            }
            catch (Exception ex)
            {
                AddLog($"Failed to refresh after status change: {ex.Message}", LogType.Error);
            }
        });
    }

    #endregion

    #region Commands

    [RelayCommand(CanExecute = nameof(CanRefreshApprovals))]
    private async Task RefreshApprovalsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading pending approvals...";

            if (!_signalRClient.IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            // Load pending approvals from service
            var approvals = await _signalRClient.GetPendingApprovalsAsync();

            // Load statistics
            var stats = await _signalRClient.GetApprovalStatisticsAsync();

            DispatcherExecute(() =>
            {
                // Update collections
                PendingApprovals.Clear();
                foreach (var approval in approvals)
                {
                    PendingApprovals.Add(approval);
                }

                // Update statistics
                Statistics = stats;

                // Update available channels
                UpdateAvailableChannels();

                // Apply filters
                ApplyFiltersAndSorting();

                StatusMessage = $"Loaded {approvals.Count} pending approvals";
                AddLog($"Loaded {approvals.Count} pending approvals", LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Error loading approvals: {ex.Message}";
                AddLog($"Error loading approvals: {ex.Message}", LogType.Error);
            });
        }
        finally
        {
            DispatcherExecute(() => IsLoading = false);
        }
    }

    private bool CanRefreshApprovals => !IsLoading && _signalRClient.IsConnected;

    [RelayCommand(CanExecute = nameof(CanApproveSelected))]
    private async Task ApproveSelectedAsync()
    {
        try
        {
            IsProcessingApproval = true;
            var selectedIds = SelectedApprovals.Select(a => a.Id).ToList();
            StatusMessage = $"Approving {selectedIds.Count} files...";

            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = selectedIds,
                Decision = ApprovalStatus.Approved,
                Notes = "Manually approved",
                ApprovedBy = Environment.UserName
            };

            var success = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (success)
            {
                var count = selectedIds.Count;
                var fileName = count == 1 ? SelectedApprovals.First().FileName : string.Empty;

                DispatcherExecute(() =>
                {
                    StatusMessage = $"Successfully approved {count} files";
                    AddLog($"Approved {count} files", LogType.Success);
                });

                // Show notification if enabled
                if (EnableNotifications)
                {
                    await _notificationService.ShowApprovalProcessedAsync(fileName, approved: true, count: count);
                }

                await RefreshApprovalsAsync();
            }
            else
            {
                throw new InvalidOperationException("Approval operation failed");
            }
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Error approving files: {ex.Message}";
                AddLog($"Error approving files: {ex.Message}", LogType.Error);
            });

            if (EnableNotifications)
            {
                await _notificationService.ShowErrorAsync("Approval Error", ex.Message);
            }
        }
        finally
        {
            IsProcessingApproval = false;
        }
    }

    private bool CanApproveSelected => HasSelection && !IsProcessingApproval && _signalRClient.IsConnected;

    [RelayCommand(CanExecute = nameof(CanRejectSelected))]
    private async Task RejectSelectedAsync()
    {
        try
        {
            IsProcessingApproval = true;
            var selectedIds = SelectedApprovals.Select(a => a.Id).ToList();
            StatusMessage = $"Rejecting {selectedIds.Count} files...";

            var request = new ApprovalDecisionRequest
            {
                ApprovalIds = selectedIds,
                Decision = ApprovalStatus.Rejected,
                Notes = "Manually rejected",
                ApprovedBy = Environment.UserName
            };

            var success = await _signalRClient.ProcessApprovalDecisionAsync(request);

            if (success)
            {
                var count = selectedIds.Count;
                var fileName = count == 1 ? SelectedApprovals.First().FileName : string.Empty;

                DispatcherExecute(() =>
                {
                    StatusMessage = $"Successfully rejected {count} files";
                    AddLog($"Rejected {count} files", LogType.Warning);
                });

                // Show notification if enabled
                if (EnableNotifications)
                {
                    await _notificationService.ShowApprovalProcessedAsync(fileName, approved: false, count: count);
                }

                await RefreshApprovalsAsync();
            }
            else
            {
                throw new InvalidOperationException("Rejection operation failed");
            }
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Error rejecting files: {ex.Message}";
                AddLog($"Error rejecting files: {ex.Message}", LogType.Error);
            });

            if (EnableNotifications)
            {
                await _notificationService.ShowErrorAsync("Rejection Error", ex.Message);
            }
        }
        finally
        {
            IsProcessingApproval = false;
        }
    }

    private bool CanRejectSelected => HasSelection && !IsProcessingApproval && _signalRClient.IsConnected;

    [RelayCommand(CanExecute = nameof(CanProcessBulkApproval))]
    private async Task BulkApproveAsync()
    {
        try
        {
            IsProcessingBulkApproval = true;
            StatusMessage = "Processing bulk approval...";

            var request = BuildBulkApprovalRequest(ApprovalStatus.Approved);

            var processedCount = await _signalRClient.ProcessBulkApprovalAsync(request);

            DispatcherExecute(() =>
            {
                StatusMessage = $"Bulk approved {processedCount} files";
                AddLog($"Bulk approved {processedCount} files matching filters", LogType.Success);
            });

            // Show notification if enabled
            if (EnableNotifications)
            {
                await _notificationService.ShowApprovalProcessedAsync(string.Empty, approved: true, count: processedCount);
            }

            await RefreshApprovalsAsync();
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Error in bulk approval: {ex.Message}";
                AddLog($"Error in bulk approval: {ex.Message}", LogType.Error);
            });

            if (EnableNotifications)
            {
                await _notificationService.ShowErrorAsync("Bulk Approval Error", ex.Message);
            }
        }
        finally
        {
            IsProcessingBulkApproval = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanProcessBulkApproval))]
    private async Task BulkRejectAsync()
    {
        try
        {
            IsProcessingBulkApproval = true;
            StatusMessage = "Processing bulk rejection...";

            var request = BuildBulkApprovalRequest(ApprovalStatus.Rejected);

            var processedCount = await _signalRClient.ProcessBulkApprovalAsync(request);

            DispatcherExecute(() =>
            {
                StatusMessage = $"Bulk rejected {processedCount} files";
                AddLog($"Bulk rejected {processedCount} files matching filters", LogType.Warning);
            });

            // Show notification if enabled
            if (EnableNotifications)
            {
                await _notificationService.ShowApprovalProcessedAsync(string.Empty, approved: false, count: processedCount);
            }

            await RefreshApprovalsAsync();
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Error in bulk rejection: {ex.Message}";
                AddLog($"Error in bulk rejection: {ex.Message}", LogType.Error);
            });

            if (EnableNotifications)
            {
                await _notificationService.ShowErrorAsync("Bulk Rejection Error", ex.Message);
            }
        }
        finally
        {
            IsProcessingBulkApproval = false;
        }
    }

    private bool CanProcessBulkApproval => !IsProcessingBulkApproval && _signalRClient.IsConnected && HasActiveFilters;

    [RelayCommand]
    private void PreviewFile(PendingApprovalDto? approval)
    {
        if (approval == null) return;

        DispatcherExecute(() =>
        {
            CurrentApproval = approval;
            AddLog($"Previewing: {approval.FileName}", LogType.Information);
        });
    }

    [RelayCommand]
    private void SelectAll()
    {
        DispatcherExecute(() =>
        {
            SelectedApprovals.Clear();
            foreach (var approval in FilteredApprovals)
            {
                SelectedApprovals.Add(approval);
            }
            UpdateSelectionStatus();
            AddLog($"Selected all {FilteredApprovals.Count} visible approvals", LogType.Information);
        });
    }

    [RelayCommand]
    private void DeselectAll()
    {
        DispatcherExecute(() =>
        {
            SelectedApprovals.Clear();
            UpdateSelectionStatus();
            AddLog("Cleared selection", LogType.Information);
        });
    }

    [RelayCommand]
    private void ToggleSelection(PendingApprovalDto approval)
    {
        if (approval == null) return;

        DispatcherExecute(() =>
        {
            if (SelectedApprovals.Contains(approval))
            {
                SelectedApprovals.Remove(approval);
            }
            else
            {
                SelectedApprovals.Add(approval);
            }
            UpdateSelectionStatus();
        });
    }

    [RelayCommand]
    private void ClearFilters()
    {
        DispatcherExecute(() =>
        {
            ChannelFilter = null;
            PriorityFilter = null;
            MinSizeFilter = null;
            MaxSizeFilter = null;
            DateFromFilter = null;
            DateToFilter = null;
            ExtensionFilter = null;

            ApplyFiltersAndSorting();
            AddLog("Filters cleared", LogType.Information);
        });
    }

    [RelayCommand]
    private void ClearActivityLog()
    {
        DispatcherExecute(() => ActivityLog.Clear());
    }

    [RelayCommand]
    private async Task ApprovalsByChannelAsync(string? channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName)) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Loading approvals for channel: {channelName}";

            var approvals = await _signalRClient.GetPendingApprovalsByChannelAsync(channelName);

            DispatcherExecute(() =>
            {
                PendingApprovals.Clear();
                foreach (var approval in approvals)
                {
                    PendingApprovals.Add(approval);
                }

                ApplyFiltersAndSorting();
                StatusMessage = $"Loaded {approvals.Count} approvals for {channelName}";
                AddLog($"Loaded {approvals.Count} approvals for channel: {channelName}", LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Error loading channel approvals: {ex.Message}";
                AddLog($"Error loading channel approvals: {ex.Message}", LogType.Error);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Filter Preset Commands

    [RelayCommand]
    private async Task LoadPresetsAsync()
    {
        try
        {
            IsLoadingPresets = true;
            var presets = await _filterPresetService.GetAllPresetsAsync();

            DispatcherExecute(() =>
            {
                SavedPresets.Clear();
                foreach (var preset in presets)
                {
                    SavedPresets.Add(preset);
                }
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog($"Error loading presets: {ex.Message}", LogType.Error);
            });
        }
        finally
        {
            IsLoadingPresets = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadPreset))]
    private async Task LoadPresetAsync(FilterPreset? preset)
    {
        if (preset == null) return;

        try
        {
            DispatcherExecute(() =>
            {
                // Apply all filter values from the preset
                ChannelFilter = preset.ChannelFilter;
                PriorityFilter = preset.PriorityFilter;
                MinSizeFilter = preset.MinSizeFilter;
                MaxSizeFilter = preset.MaxSizeFilter;
                DateFromFilter = preset.DateFromFilter;
                DateToFilter = preset.DateToFilter;
                ExtensionFilter = preset.ExtensionFilter;

                SelectedPreset = preset;

                AddLog($"Applied preset: {preset.Name}", LogType.Success);
            });

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog($"Error loading preset: {ex.Message}", LogType.Error);
            });
        }
    }

    private bool CanLoadPreset(FilterPreset? preset) => preset != null && !IsLoadingPresets;

    [RelayCommand]
    private async Task SaveCurrentFiltersAsPresetAsync()
    {
        // This will be triggered from the UI dialog
        // The dialog will collect the name, description, and isDefault flag
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SavePresetAsync(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            DispatcherExecute(() => AddLog("Preset name is required", LogType.Warning));
            return;
        }

        try
        {
            var preset = new FilterPreset
            {
                Name = presetName,
                Description = NewPresetDescription,
                ChannelFilter = ChannelFilter,
                PriorityFilter = PriorityFilter,
                MinSizeFilter = MinSizeFilter,
                MaxSizeFilter = MaxSizeFilter,
                DateFromFilter = DateFromFilter,
                DateToFilter = DateToFilter,
                ExtensionFilter = ExtensionFilter,
                IsDefault = SetAsDefault,
                CreatedBy = Environment.UserName
            };

            await _filterPresetService.SavePresetAsync(preset);
            await LoadPresetsAsync();

            DispatcherExecute(() =>
            {
                AddLog($"Saved preset: {presetName}", LogType.Success);
                NewPresetName = string.Empty;
                NewPresetDescription = string.Empty;
                SetAsDefault = false;
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog($"Error saving preset: {ex.Message}", LogType.Error);
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private async Task DeletePresetAsync(FilterPreset? preset)
    {
        if (preset == null) return;

        try
        {
            await _filterPresetService.DeletePresetAsync(preset.Id);
            await LoadPresetsAsync();

            DispatcherExecute(() =>
            {
                if (SelectedPreset?.Id == preset.Id)
                {
                    SelectedPreset = null;
                }
                AddLog($"Deleted preset: {preset.Name}", LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog($"Error deleting preset: {ex.Message}", LogType.Error);
            });
        }
    }

    private bool CanDeletePreset(FilterPreset? preset) =>
        preset != null && !preset.IsBuiltIn && !IsLoadingPresets;

    [RelayCommand(CanExecute = nameof(CanSetDefaultPreset))]
    private async Task SetDefaultPresetAsync(FilterPreset? preset)
    {
        if (preset == null) return;

        try
        {
            await _filterPresetService.SetDefaultPresetAsync(preset.Id);
            await LoadPresetsAsync();

            DispatcherExecute(() =>
            {
                AddLog($"Set default preset: {preset.Name}", LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog($"Error setting default preset: {ex.Message}", LogType.Error);
            });
        }
    }

    private bool CanSetDefaultPreset(FilterPreset? preset) => preset != null && !IsLoadingPresets;

    [RelayCommand]
    private async Task ClearDefaultPresetAsync()
    {
        try
        {
            await _filterPresetService.ClearDefaultPresetAsync();
            await LoadPresetsAsync();

            DispatcherExecute(() =>
            {
                AddLog("Cleared default preset", LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog($"Error clearing default preset: {ex.Message}", LogType.Error);
            });
        }
    }

    private async Task ApplyDefaultPresetAsync()
    {
        try
        {
            var defaultPreset = await _filterPresetService.GetDefaultPresetAsync();
            if (defaultPreset != null)
            {
                await LoadPresetAsync(defaultPreset);
            }
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog($"Error applying default preset: {ex.Message}", LogType.Error);
            });
        }
    }

    #endregion

    #region Filtering and Sorting

    private void ApplyFiltersAndSorting()
    {
        try
        {
            var filtered = PendingApprovals.AsEnumerable();

            // Apply ShowOnlyPending filter
            if (ShowOnlyPending)
            {
                filtered = filtered.Where(a => a.Status == ApprovalStatus.Pending);
            }

            // Apply channel filter
            if (!string.IsNullOrWhiteSpace(ChannelFilter) && ChannelFilter != "All Channels")
            {
                filtered = filtered.Where(a => a.ChannelName.Equals(ChannelFilter, StringComparison.OrdinalIgnoreCase));
            }

            // Apply priority filter
            if (PriorityFilter.HasValue)
            {
                filtered = filtered.Where(a => a.Priority == PriorityFilter.Value);
            }

            // Apply size filters
            if (MinSizeFilter.HasValue)
            {
                filtered = filtered.Where(a => a.FileSize >= MinSizeFilter.Value);
            }
            if (MaxSizeFilter.HasValue)
            {
                filtered = filtered.Where(a => a.FileSize <= MaxSizeFilter.Value);
            }

            // Apply date filters
            if (DateFromFilter.HasValue)
            {
                filtered = filtered.Where(a => a.DetectedAt >= DateFromFilter.Value);
            }
            if (DateToFilter.HasValue)
            {
                filtered = filtered.Where(a => a.DetectedAt <= DateToFilter.Value);
            }

            // Apply extension filter
            if (!string.IsNullOrWhiteSpace(ExtensionFilter))
            {
                var extensions = ExtensionFilter.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().ToLowerInvariant())
                    .ToList();
                filtered = filtered.Where(a => extensions.Contains(a.Extension.ToLowerInvariant()));
            }

            // Apply sorting
            filtered = SortBy switch
            {
                "FileName" => SortDescending
                    ? filtered.OrderByDescending(a => a.FileName)
                    : filtered.OrderBy(a => a.FileName),
                "FileSize" => SortDescending
                    ? filtered.OrderByDescending(a => a.FileSize)
                    : filtered.OrderBy(a => a.FileSize),
                "Priority" => SortDescending
                    ? filtered.OrderByDescending(a => a.Priority)
                    : filtered.OrderBy(a => a.Priority),
                "ChannelName" => SortDescending
                    ? filtered.OrderByDescending(a => a.ChannelName)
                    : filtered.OrderBy(a => a.ChannelName),
                _ => SortDescending
                    ? filtered.OrderByDescending(a => a.DetectedAt)
                    : filtered.OrderBy(a => a.DetectedAt)
            };

            FilteredApprovals.Clear();
            foreach (var approval in filtered)
            {
                FilteredApprovals.Add(approval);
            }

            StatusMessage = $"Showing {FilteredApprovals.Count} of {PendingApprovals.Count} approvals";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error applying filters: {ex.Message}");
            AddLog($"Error applying filters: {ex.Message}", LogType.Error);
        }
    }

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(ChannelFilter) ||
        PriorityFilter.HasValue ||
        MinSizeFilter.HasValue ||
        MaxSizeFilter.HasValue ||
        DateFromFilter.HasValue ||
        DateToFilter.HasValue ||
        !string.IsNullOrWhiteSpace(ExtensionFilter);

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Trigger filter/sort when filter properties change
        // Generate thumbnail when CurrentApproval changes
        if (e.PropertyName == nameof(CurrentApproval))
        {
            _ = Task.Run(async () => await GenerateThumbnailForCurrentApprovalAsync());
        }

        // Trigger filter/sort when filter properties change
        if (e.PropertyName == nameof(ChannelFilter) ||
            e.PropertyName == nameof(PriorityFilter) ||
            e.PropertyName == nameof(MinSizeFilter) ||
            e.PropertyName == nameof(MaxSizeFilter) ||
            e.PropertyName == nameof(DateFromFilter) ||
            e.PropertyName == nameof(DateToFilter) ||
            e.PropertyName == nameof(ExtensionFilter) ||
            e.PropertyName == nameof(SortBy) ||
            e.PropertyName == nameof(SortDescending) ||
            e.PropertyName == nameof(ShowOnlyPending))
        {
            DispatcherExecute(() => ApplyFiltersAndSorting());
        }
    }

    #region Thumbnail Generation

    /// <summary>
    /// Generates thumbnail for the current approval video file.
    /// </summary>
    private async Task GenerateThumbnailForCurrentApprovalAsync()
    {
        if (CurrentApproval == null)
        {
            DispatcherExecute(() =>
            {
                CurrentThumbnailPath = null;
                CurrentVideoMetadata = null;
            });
            return;
        }

        try
        {
            DispatcherExecute(() => IsGeneratingThumbnail = true);

            var filePath = CurrentApproval.FilePath;

            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            {
                DispatcherExecute(() =>
                {
                    AddLog($"Video file not found: {filePath}", LogType.Warning);
                    CurrentThumbnailPath = null;
                    CurrentVideoMetadata = null;
                });
                return;
            }

            // Generate thumbnail and extract metadata in parallel
            var thumbnailTask = _videoThumbnailService.GenerateThumbnailAsync(filePath);
            var metadataTask = _videoThumbnailService.GetVideoMetadataAsync(filePath);

            await Task.WhenAll(thumbnailTask, metadataTask);

            var thumbnailPath = await thumbnailTask;
            var metadata = await metadataTask;

            DispatcherExecute(() =>
            {
                CurrentThumbnailPath = thumbnailPath;
                CurrentVideoMetadata = metadata;

                if (thumbnailPath != null)
                {
                    AddLog($"Generated thumbnail for {CurrentApproval.FileName}", LogType.Success);
                }
                else
                {
                    AddLog($"Failed to generate thumbnail for {CurrentApproval.FileName}", LogType.Warning);
                }
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog($"Error generating thumbnail: {ex.Message}", LogType.Error);
                CurrentThumbnailPath = null;
                CurrentVideoMetadata = null;
            });
        }
        finally
        {
            DispatcherExecute(() => IsGeneratingThumbnail = false);
        }
    }

    /// <summary>
    /// Command to refresh/regenerate the thumbnail for the current approval.
    /// </summary>
    [RelayCommand]
    private async Task RefreshThumbnailAsync()
    {
        if (CurrentApproval == null) return;

        try
        {
            DispatcherExecute(() =>
            {
                AddLog("Refreshing thumbnail...", LogType.Information);
            });

            await GenerateThumbnailForCurrentApprovalAsync();
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog($"Error refreshing thumbnail: {ex.Message}", LogType.Error);
            });
        }
    }

    #endregion

    #endregion

    #region Helper Methods

    private BulkApprovalRequest BuildBulkApprovalRequest(ApprovalStatus decision)
    {
        var request = new BulkApprovalRequest
        {
            Decision = decision,
            ApprovedBy = Environment.UserName,
            Notes = $"Bulk {decision} via approval workflow"
        };

        // Apply current filters to bulk request
        if (!string.IsNullOrWhiteSpace(ChannelFilter) && ChannelFilter != "All Channels")
        {
            request.ChannelName = ChannelFilter;
        }

        if (MinSizeFilter.HasValue)
        {
            request.MinFileSize = MinSizeFilter.Value;
        }

        if (MaxSizeFilter.HasValue)
        {
            request.MaxFileSize = MaxSizeFilter.Value;
        }

        if (DateFromFilter.HasValue)
        {
            request.DetectedAfter = DateFromFilter.Value;
        }

        if (DateToFilter.HasValue)
        {
            request.DetectedBefore = DateToFilter.Value;
        }

        if (!string.IsNullOrWhiteSpace(ExtensionFilter))
        {
            request.FileExtensions = ExtensionFilter.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .ToList();
        }

        return request;
    }

    private void UpdateAvailableChannels()
    {
        var channels = PendingApprovals
            .Select(a => a.ChannelName)
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

    private void UpdateSelectionStatus()
    {
        SelectedCount = SelectedApprovals.Count;
        HasSelection = SelectedApprovals.Count > 0;

        // Notify commands that selection changed
        ApproveSelectedCommand.NotifyCanExecuteChanged();
        RejectSelectedCommand.NotifyCanExecuteChanged();
    }

    private void AddLog(string message, LogType type)
    {
        try
        {
            var logMessage = new ApprovalLogMessage
            {
                Message = message,
                Timestamp = DateTime.Now,
                Type = type
            };

            ActivityLog.Insert(0, logMessage);

            // Keep only last 200 messages
            while (ActivityLog.Count > 200)
            {
                ActivityLog.RemoveAt(ActivityLog.Count - 1);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding log: {ex.Message}");
        }
    }

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
            // Fallback: execute directly (might cause threading issues)
            action();
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            // Unsubscribe from property changes
            PropertyChanged -= OnPropertyChanged;

            // Unsubscribe from SignalR events
            // _signalRClient.NewFileForApproval -= OnNewFileForApproval;
            // _signalRClient.ApprovalStatusChanged -= OnApprovalStatusChanged;

            // Clear collections
            try
            {
                PendingApprovals.Clear();
                FilteredApprovals.Clear();
                SelectedApprovals.Clear();
                AvailableChannels.Clear();
                ActivityLog.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing ApprovalViewModel: {ex.Message}");
            }
        }
    }

    #endregion
}

/// <summary>
/// Log message for approval activities
/// </summary>
public class ApprovalLogMessage
{
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public LogType Type { get; set; }

    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Message}";

    public string TypeIcon => Type switch
    {
        LogType.Success => "✓",
        LogType.Warning => "⚠",
        LogType.Error => "✗",
        LogType.Information => "ℹ",
        _ => "·"
    };
}

/// <summary>
/// Log message type enumeration
/// </summary>
public enum LogType
{
    Information,
    Success,
    Warning,
    Error
}

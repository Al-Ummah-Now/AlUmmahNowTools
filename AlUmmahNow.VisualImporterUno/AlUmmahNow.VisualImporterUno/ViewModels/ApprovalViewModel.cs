using AlUmmahNow.VisualImporterUno.Shell;
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
    private string _statusMessage = Loc.Get("Approve_Ready");

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

    // Text for the page, in the chosen language's digits
    public string PendingText => Loc.Number(Statistics.TotalPending);
    public string ApprovedText => Loc.Number(Statistics.TotalApproved);
    public string RejectedText => Loc.Number(Statistics.TotalRejected);
    public string PendingSizeText => Loc.Bytes(PendingApprovals.Where(a => a.Status == ApprovalStatus.Pending).Sum(a => a.FileSize));
    public bool HasApprovals => FilteredApprovals.Count > 0;
    public string ListTitleText => Loc.Format("Approve_ListTitle", FilteredApprovals.Count);
    public string ApproveSelectedText => SelectedCount > 0 ? Loc.Format("Approve_ApproveN", SelectedCount) : Loc.Get("Approve_Approve");
    public string RejectSelectedText => SelectedCount > 0 ? Loc.Format("Approve_RejectN", SelectedCount) : Loc.Get("Approve_Reject");

    public string CurrentSizeText => CurrentApproval == null ? string.Empty : Loc.Bytes(CurrentApproval.FileSize);
    public string CurrentDetectedText => CurrentApproval == null ? string.Empty : Loc.When(CurrentApproval.DetectedAt.ToLocalTime());
    public string DurationText => CurrentVideoMetadata == null ? string.Empty : Loc.Duration(CurrentVideoMetadata.Duration);
    public string ResolutionText => CurrentVideoMetadata == null ? string.Empty : Loc.Number(CurrentVideoMetadata.Width) + " × " + Loc.Number(CurrentVideoMetadata.Height);
    public string FrameRateText => CurrentVideoMetadata == null ? string.Empty : Loc.Format("Approve_Fps", CurrentVideoMetadata.FrameRate);
    public string BitRateText => CurrentVideoMetadata == null ? string.Empty : Loc.Format("Approve_Kbps", CurrentVideoMetadata.BitRate / 1000);

    private static readonly string[] SortKeys = { "DetectedAt", "FileName", "FileSize", "Priority", "ChannelName" };

    /// <summary>The sort order as a position in the page's list of orders.</summary>
    public int SortIndex
    {
        get => Math.Max(0, Array.IndexOf(SortKeys, SortBy));
        set { if (value >= 0 && value < SortKeys.Length) SortBy = SortKeys[value]; }
    }

    partial void OnStatisticsChanged(ApprovalStatistics value)
    {
        OnPropertyChanged(nameof(PendingText));
        OnPropertyChanged(nameof(ApprovedText));
        OnPropertyChanged(nameof(RejectedText));
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(ApproveSelectedText));
        OnPropertyChanged(nameof(RejectSelectedText));
    }

    partial void OnCurrentApprovalChanged(PendingApprovalDto? value)
    {
        OnPropertyChanged(nameof(CurrentSizeText));
        OnPropertyChanged(nameof(CurrentDetectedText));
    }

    partial void OnCurrentVideoMetadataChanged(VideoMetadata? value)
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ResolutionText));
        OnPropertyChanged(nameof(FrameRateText));
        OnPropertyChanged(nameof(BitRateText));
    }

    partial void OnSortByChanged(string value) => OnPropertyChanged(nameof(SortIndex));

    /// <summary>Makes the page's selection the one approve and reject act on.</summary>
    public void SetSelection(IEnumerable<PendingApprovalDto> items)
    {
        SelectedApprovals.Clear();
        foreach (var item in items) SelectedApprovals.Add(item);
        UpdateSelectionStatus();
    }

    [RelayCommand]
    private async Task ApproveOneAsync(PendingApprovalDto? approval)
    {
        if (approval == null) return;
        SetSelection(new[] { approval });
        if (ApproveSelectedCommand.CanExecute(null)) await ApproveSelectedAsync();
    }

    [RelayCommand]
    private async Task RejectOneAsync(PendingApprovalDto? approval)
    {
        if (approval == null) return;
        SetSelection(new[] { approval });
        if (RejectSelectedCommand.CanExecute(null)) await RejectSelectedAsync();
    }

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
        : this(signalRClient, filterPresetService, notificationService, videoThumbnailService, start: true)
    {
    }

    /// <param name="start">Connects and loads the list by itself; tests load what they need</param>
    internal ApprovalViewModel(FileScanSignalRClient signalRClient, IFilterPresetService filterPresetService, INotificationService notificationService, VideoThumbnailService videoThumbnailService, bool start)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
        _filterPresetService = filterPresetService ?? throw new ArgumentNullException(nameof(filterPresetService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _videoThumbnailService = videoThumbnailService ?? throw new ArgumentNullException(nameof(videoThumbnailService));
        PendingApprovals.CollectionChanged += (_, _) => OnPropertyChanged(nameof(PendingSizeText));
        FilteredApprovals.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasApprovals));
            OnPropertyChanged(nameof(ListTitleText));
        };

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

        // Subscribe to property changes for filter application
        PropertyChanged += OnPropertyChanged;
        _signalRClient.ServiceStatusChanged += OnScannerStatusChanged;
        if (!start) return;

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
                    StatusMessage = Loc.GetWithDetail("Approve_StartFailed", ex.Message);
                    AddLog(Loc.GetWithDetail("Approve_StartFailed", ex.Message), LogType.Error);
                });
            }
        });
    }

    #region Initialization

    private async Task InitializeAsync()
    {
        try
        {
            DispatcherExecute(() =>
            {
                IsLoading = true;
                StatusMessage = Loc.Get("Approve_Connecting");
            });

            // Ensure SignalR connection
            if (!_signalRClient.IsConnected)
            {
                var connected = await _signalRClient.ConnectAsync();
                if (!connected)
                {
                    throw new InvalidOperationException(Loc.Get("Watch_NotConnected"));
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
                StatusMessage = Loc.Get("Approve_Ready");
                AddLog(Loc.Get("Approve_Started"), LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = Loc.GetWithDetail("Approve_StartFailed", ex.Message);
                AddLog(Loc.GetWithDetail("Approve_StartFailed", ex.Message), LogType.Error);
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
                AddLog(Loc.Get("Approve_NewFile"), LogType.Information);
                await RefreshApprovalsAsync();
            }
            catch (Exception ex)
            {
                AddLog(Loc.GetWithDetail("Approve_LoadFailed", ex.Message), LogType.Error);
            }
        });
    }

    private void OnApprovalStatusChanged(object? sender, EventArgs e)
    {
        DispatcherExecute(async () =>
        {
            try
            {
                AddLog(Loc.Get("Approve_Changed"), LogType.Information);
                await RefreshApprovalsAsync();
            }
            catch (Exception ex)
            {
                AddLog(Loc.GetWithDetail("Approve_LoadFailed", ex.Message), LogType.Error);
            }
        });
    }

    #endregion

    #region Commands

    [RelayCommand(CanExecute = nameof(CanRefreshApprovals))]
    private Task RefreshApprovalsAsync() => LoadApprovalsAsync();

    /// <summary>Loads the list; null when it could not be.</summary>
    private async Task<IReadOnlyList<PendingApprovalDto>?> LoadApprovalsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = Loc.Get("Approve_Loading");

            if (!_signalRClient.IsConnected)
            {
                throw new InvalidOperationException(Loc.Get("Watch_NotConnected"));
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

                StatusMessage = Loc.Format("Approve_Loaded", approvals.Count(a => a.Status == ApprovalStatus.Pending));
                AddLog(Loc.Format("Approve_Loaded", approvals.Count(a => a.Status == ApprovalStatus.Pending)), LogType.Success);
            });
            return approvals;
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = Loc.GetWithDetail("Approve_LoadFailed", ex.Message);
                AddLog(Loc.GetWithDetail("Approve_LoadFailed", ex.Message), LogType.Error);
            });
            return null;
        }
        finally
        {
            DispatcherExecute(() => IsLoading = false);
        }
    }

    private bool CanRefreshApprovals => !IsLoading && _signalRClient.IsConnected;

    [RelayCommand(CanExecute = nameof(CanDecideSelected))]
    private Task ApproveSelectedAsync() => DecideSelectedAsync(ApprovalStatus.Approved, "Manually approved");

    [RelayCommand(CanExecute = nameof(CanDecideSelected))]
    private Task RejectSelectedAsync() => DecideSelectedAsync(ApprovalStatus.Rejected, "Manually rejected");

    [RelayCommand(CanExecute = nameof(CanDecideAll))]
    private Task BulkApproveAsync() => DecideAllAsync(ApprovalStatus.Approved);

    [RelayCommand(CanExecute = nameof(CanDecideAll))]
    private Task BulkRejectAsync() => DecideAllAsync(ApprovalStatus.Rejected);

    /// <summary>One decision at a time, whichever button it came from.</summary>
    private bool CanDecide => !IsProcessingApproval && !IsProcessingBulkApproval && _signalRClient.IsConnected;

    private bool CanDecideSelected => CanDecide && HasSelection;

    private bool CanDecideAll => CanDecide && HasActiveFilters;

    private void NotifyDecisions()
    {
        ApproveSelectedCommand.NotifyCanExecuteChanged();
        RejectSelectedCommand.NotifyCanExecuteChanged();
        BulkApproveCommand.NotifyCanExecuteChanged();
        BulkRejectCommand.NotifyCanExecuteChanged();
        RefreshApprovalsCommand.NotifyCanExecuteChanged();
    }

    private void OnScannerStatusChanged(object? sender, ServiceStatusEventArgs e) => DispatcherExecute(NotifyDecisions);

    private async Task DecideSelectedAsync(ApprovalStatus decision, string notes)
    {
        var files = SelectedApprovals.Where(a => a.Status == ApprovalStatus.Pending).ToList();
        if (files.Count == 0) return;
        try
        {
            IsProcessingApproval = true;
            await DecideAsync(files, decision, notes);
        }
        finally
        {
            IsProcessingApproval = false;
        }
    }

    private async Task DecideAllAsync(ApprovalStatus decision)
    {
        var files = Listed();
        if (files.Count == 0) return;
        try
        {
            IsProcessingBulkApproval = true;
            await DecideAsync(files, decision, $"Bulk {decision} via approval workflow");
        }
        finally
        {
            IsProcessingBulkApproval = false;
        }
    }

    /// <summary>
    /// What is listed and waiting now. Approve all and Reject all decide these by id: the scanner's own filters lack some of the
    /// page's (priority), and a file that came after the list was loaded was not seen.
    /// </summary>
    private List<PendingApprovalDto> Listed() => FilteredApprovals.Where(a => a.Status == ApprovalStatus.Pending).ToList();

    /// <summary>How many ids go in one message: the scanner takes 32 KB, and an id is about 39 bytes of it</summary>
    private const int DecisionBatch = 500;

    /// <summary>
    /// Decides these files, in parts, then loads the list again and says what came of it: how many were decided, how many still
    /// wait and why. The scanner answers a part with false both when none of it was waiting any more and when it failed; the list
    /// loaded again tells them apart.
    /// </summary>
    private async Task DecideAsync(IReadOnlyList<PendingApprovalDto> files, ApprovalStatus decision, string notes)
    {
        var approve = decision == ApprovalStatus.Approved;
        StatusMessage = Loc.Format(approve ? "Approve_ApprovingMany" : "Approve_RejectingMany", files.Count);
        var accepted = new List<PendingApprovalDto>();
        PendingApprovalDto[] unanswered = [];
        Exception? error = null;
        foreach (var part in files.Chunk(DecisionBatch))
        {
            try
            {
                var request = new ApprovalDecisionRequest { ApprovalIds = part.Select(f => f.Id).ToList(), Decision = decision, Notes = notes, ApprovedBy = Environment.UserName };
                if (await _signalRClient.ProcessApprovalDecisionAsync(request)) accepted.AddRange(part);
            }
            catch (Exception ex)
            {
                (error, unanswered) = (ex, part);
                break;
            }
        }

        // Also when the list cannot be loaded again, what was decided leaves it
        foreach (var file in accepted) file.Status = decision;
        var ids = files.Select(f => f.Id).ToHashSet();
        var loaded = await LoadApprovalsAsync();
        if (loaded is null) DispatcherExecute(ApplyFiltersAndSorting);
        var waiting = loaded?.Where(a => a.Status == ApprovalStatus.Pending && ids.Contains(a.Id)).Select(a => a.Id).ToHashSet();
        var decided = waiting is null ? accepted : accepted.Concat(unanswered).Where(f => !waiting.Contains(f.Id)).ToList();

        // Unknown without the list: what the scanner did not accept may have been decided all the same
        var left = waiting?.Count ?? files.Count - accepted.Count;
        var reason = error?.Message ?? Loc.Get("Approve_NotAccepted");
        var done = decided.Count > 0 ? Loc.Plural(approve ? "Approve_ApprovedMany" : "Approve_RejectedMany", decided.Count) : "";
        var rest = left == 0 ? ""
            : waiting is null ? Loc.GetWithDetail("Approve_RestUnknown", reason)
            : Loc.WithDetail(Loc.Plural(approve ? "Approve_ApproveManyFailed" : "Approve_RejectManyFailed", left), reason);
        var said = string.Join(" ", new[] { done, rest }.Where(t => t.Length > 0));
        if (said.Length == 0) said = Loc.Get("Approve_DecidedAlready");
        DispatcherExecute(() =>
        {
            StatusMessage = said;
            AddLog(said, rest.Length > 0 ? LogType.Error : decided.Count == 0 ? LogType.Information : approve ? LogType.Success : LogType.Warning);
        });
        if (!EnableNotifications) return;
        if (decided.Count > 0) await _notificationService.ShowApprovalProcessedAsync(decided.Count == 1 ? decided[0].FileName : string.Empty, approve, decided.Count);
        if (rest.Length > 0) await _notificationService.ShowErrorAsync(Loc.Get(approve ? "Approve_ApproveFailed" : "Approve_RejectFailed"), rest);
    }

    [RelayCommand]
    private void PreviewFile(PendingApprovalDto? approval)
    {
        if (approval == null) return;

        DispatcherExecute(() =>
        {
            CurrentApproval = approval;
            AddLog(Loc.GetWithDetail("Approve_Previewing", approval.FileName), LogType.Information);
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
            AddLog(Loc.Format("Approve_SelectedAll", FilteredApprovals.Count), LogType.Information);
        });
    }

    [RelayCommand]
    private void DeselectAll()
    {
        DispatcherExecute(() =>
        {
            SelectedApprovals.Clear();
            UpdateSelectionStatus();
            AddLog(Loc.Get("Approve_SelectionCleared"), LogType.Information);
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
            AddLog(Loc.Get("Approve_FiltersCleared"), LogType.Information);
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
            StatusMessage = Loc.GetWithDetail("Approve_Loading", channelName);

            var approvals = await _signalRClient.GetPendingApprovalsByChannelAsync(channelName);

            DispatcherExecute(() =>
            {
                PendingApprovals.Clear();
                foreach (var approval in approvals)
                {
                    PendingApprovals.Add(approval);
                }

                ApplyFiltersAndSorting();
                StatusMessage = Loc.Format("Approve_Loaded", approvals.Count(a => a.Status == ApprovalStatus.Pending));
                AddLog(Loc.Format("Approve_Loaded", approvals.Count(a => a.Status == ApprovalStatus.Pending)), LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = Loc.GetWithDetail("Approve_LoadFailed", ex.Message);
                AddLog(Loc.GetWithDetail("Approve_LoadFailed", ex.Message), LogType.Error);
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
                AddLog(Loc.GetWithDetail("Approve_PresetsFailed", ex.Message), LogType.Error);
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

                AddLog(Loc.GetWithDetail("Approve_PresetApplied", preset.Name), LogType.Success);
            });

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog(Loc.GetWithDetail("Approve_PresetsFailed", ex.Message), LogType.Error);
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
            DispatcherExecute(() => AddLog(Loc.Get("Approve_PresetNameNeeded"), LogType.Warning));
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
                AddLog(Loc.GetWithDetail("Approve_PresetSaved", presetName), LogType.Success);
                NewPresetName = string.Empty;
                NewPresetDescription = string.Empty;
                SetAsDefault = false;
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog(Loc.GetWithDetail("Approve_PresetSaveFailed", ex.Message), LogType.Error);
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
                AddLog(Loc.GetWithDetail("Approve_PresetDeleted", preset.Name), LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog(Loc.GetWithDetail("Approve_PresetSaveFailed", ex.Message), LogType.Error);
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
                AddLog(Loc.GetWithDetail("Approve_PresetDefault", preset.Name), LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog(Loc.GetWithDetail("Approve_PresetSaveFailed", ex.Message), LogType.Error);
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
                AddLog(Loc.Get("Approve_PresetDefaultCleared"), LogType.Success);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog(Loc.GetWithDetail("Approve_PresetSaveFailed", ex.Message), LogType.Error);
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
                AddLog(Loc.GetWithDetail("Approve_PresetsFailed", ex.Message), LogType.Error);
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
            if (!string.IsNullOrWhiteSpace(ChannelFilter) && ChannelFilter != MainViewModel.AllChannels)
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

            StatusMessage = Loc.Format("Approve_Showing", FilteredApprovals.Count, PendingApprovals.Count);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(Loc.GetWithDetail("Approve_FilterFailed", ex.Message));
            AddLog(Loc.GetWithDetail("Approve_FilterFailed", ex.Message), LogType.Error);
        }
    }

    private bool HasActiveFilters =>
        (!string.IsNullOrWhiteSpace(ChannelFilter) && ChannelFilter != MainViewModel.AllChannels) ||
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

        // Every button that decides follows the filters, the selection and a decision under way
        if (e.PropertyName is nameof(ChannelFilter) or nameof(PriorityFilter) or nameof(MinSizeFilter) or nameof(MaxSizeFilter) or nameof(DateFromFilter) or nameof(DateToFilter)
            or nameof(ExtensionFilter) or nameof(HasSelection) or nameof(IsProcessingApproval) or nameof(IsProcessingBulkApproval) or nameof(IsLoading))
        {
            DispatcherExecute(NotifyDecisions);
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
                    AddLog(Loc.GetWithDetail("Approve_VideoMissing", filePath), LogType.Warning);
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
                    AddLog(Loc.Get("Approve_ThumbMade"), LogType.Success);
                }
                else
                {
                    AddLog(Loc.Get("Approve_ThumbFailed"), LogType.Warning);
                }
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog(Loc.GetWithDetail("Approve_ThumbFailed", ex.Message), LogType.Error);
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
                AddLog(Loc.Get("Approve_ThumbMaking"), LogType.Information);
            });

            await GenerateThumbnailForCurrentApprovalAsync();
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddLog(Loc.GetWithDetail("Approve_ThumbFailed", ex.Message), LogType.Error);
            });
        }
    }

    #endregion

    #endregion

    #region Helper Methods

    private void UpdateAvailableChannels()
    {
        var channels = PendingApprovals
            .Select(a => a.ChannelName)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        AvailableChannels.Clear();
        AvailableChannels.Add(MainViewModel.AllChannels);
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
        NotifyDecisions();
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
            _signalRClient.ServiceStatusChanged -= OnScannerStatusChanged;

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

    public string TimeText => Loc.Time(Timestamp);

    public string Glyph => Type switch
    {
        LogType.Success => "\uE73E",
        LogType.Warning => "\uE7BA",
        LogType.Error => "\uE783",
        _ => "\uE946",
    };

    public string GlyphColor => Type switch
    {
        LogType.Success => "#FF2FA866",
        LogType.Warning => "#FFE0962A",
        LogType.Error => "#FFE0605A",
        _ => "#FF7F97A3",
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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.UI.Dispatching;

namespace AlUmmahNow.VisualImporterUno.ViewModels;

/// <summary>What the folder scanner (the local FileScan service) is doing, in the words staff see.</summary>
public enum ScannerState
{
    Checking,
    Off,
    Starting,
    On,
    Stopping,
    Missing,
    Failed,
}

/// <summary>
/// The local folder scanner: its state, the scans it is running, its activity and its problems. One instance serves the
/// dashboard, the Service status page and the workspace, so every view shows the same numbers. Every change to a bound
/// property or collection happens on the UI thread.
/// </summary>
public partial class ServiceStatusViewModel : ObservableObject, IDisposable
{
    /// <summary>Start the scanner when the app opens. Screenshot runs turn it off so they never launch processes.</summary>
    public static bool AutoStart { get; set; } = true;

    private const int ActivityLimit = 200;

    private readonly FileScanSignalRClient _signalRClient;
    private readonly FileScanServiceManager _serviceManager;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly System.Threading.Timer _metricsTimer;
    private bool _disposed;

    [ObservableProperty] private ServiceStatus _serviceStatus = ServiceStatus.NotAvailable;
    [ObservableProperty] private ServiceHealthStatus _serviceHealthStatus = ServiceHealthStatus.Unknown;
    [ObservableProperty] private bool _isServiceManaged;

    [ObservableProperty] private ScannerState _scannerState = ScannerState.Checking;
    [ObservableProperty] private string _scannerTitle = Loc.Get("Scanner_CheckingTitle");
    [ObservableProperty] private string _scannerDetail = Loc.Get("Scanner_CheckingDetail");
    [ObservableProperty] private string _scannerActionText = Loc.Get("Scanner_ActionTurnOn");
    [ObservableProperty] private bool _canRunScannerAction;
    [ObservableProperty] private bool _isScannerBusy = true;
    [ObservableProperty] private string _scannerStatusColor = "#FF8BA3AE";

    [ObservableProperty] private bool _canStartService;
    [ObservableProperty] private bool _canStopService;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isStartingService;
    [ObservableProperty] private bool _isStoppingService;

    [ObservableProperty] private string _connectionText = Loc.Get("Scanner_ConnectionNone");
    [ObservableProperty] private string _processText = "–";
    [ObservableProperty] private string _uptimeText = "–";
    [ObservableProperty] private string _memoryText = "–";
    [ObservableProperty] private string _threadsText = "–";
    [ObservableProperty] private string _cpuText = "–";

    [ObservableProperty] private int _filesScanned;
    [ObservableProperty] private string _filesScannedText = Loc.Number(0);
    [ObservableProperty] private bool _hasActiveOperation;
    [ObservableProperty] private string _activeOperationsText = Loc.Number(0);
    [ObservableProperty] private int _problemCount;
    [ObservableProperty] private string _problemCountText = Loc.Number(0);
    [ObservableProperty] private bool _hasProblems;
    [ObservableProperty] private bool _hasActivity;

    /// <summary>Everything the scanner reported, newest first.</summary>
    public ObservableCollection<ActivityEntry> Activity { get; } = new();

    /// <summary>The problems among <see cref="Activity"/>, newest first.</summary>
    public ObservableCollection<ActivityEntry> Problems { get; } = new();

    /// <summary>The scans running now.</summary>
    public ObservableCollection<OperationInfo> OngoingOperations { get; } = new();

    public ServiceStatusViewModel(FileScanSignalRClient signalRClient, FileScanServiceManager serviceManager)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
        _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));

        try
        {
            _dispatcherQueue = App.Current?.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"No dispatcher for the scanner view model: {ex.Message}");
        }

        _metricsTimer = new System.Threading.Timer(async _ => await UpdateMetricsAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        SubscribeToEvents();
        _ = Task.Run(InitializeAsync);
    }

    private async Task InitializeAsync()
    {
        try
        {
            await Task.Delay(500);
            await RefreshHealthAsync();
            if (await _serviceManager.IsServiceRunningAsync())
            {
                Log(ActivityKind.Success, "Activity_ScannerFound");
                await ConnectAsync();
            }
            else if (AutoStart)
            {
                await StartAsync();
            }
            else
            {
                OnUi(() => SetHealth(ServiceHealthStatus.Stopped));
            }
        }
        catch (Exception ex)
        {
            Problem("Problem_ScannerCheck", ex.Message);
            OnUi(() => SetHealth(ServiceHealthStatus.Error));
        }
    }

    private void SubscribeToEvents()
    {
        _signalRClient.ServiceStatusChanged += OnConnectionChanged;
        _signalRClient.ScanProgressReceived += OnScanProgress;
        _signalRClient.ScanStarted += OnScanStarted;
        _signalRClient.ScanCompleted += OnScanCompleted;
        _signalRClient.ScanFailed += OnScanFailed;
        _signalRClient.ScanCancelled += OnScanCancelled;
        _signalRClient.ErrorOccurred += OnConnectionError;
        _serviceManager.ServiceStatusChanged += OnHealthChanged;
    }

    /// <summary>The scanner card's one button: turn on, turn off, or try again after a problem.</summary>
    [RelayCommand]
    private async Task ScannerActionAsync()
    {
        if (ScannerState == ScannerState.On) await StopAsync();
        else await StartAsync();
    }

    [RelayCommand]
    private Task StartServiceAsync() => StartAsync();

    [RelayCommand]
    private Task StopServiceAsync() => StopAsync();

    [RelayCommand]
    private Task ConnectToServiceAsync() => ConnectAsync();

    [RelayCommand]
    private async Task DisconnectFromServiceAsync()
    {
        try
        {
            await _signalRClient.DisconnectAsync();
            Log(ActivityKind.Info, "Activity_Disconnected");
        }
        catch (Exception ex)
        {
            Problem("Problem_Disconnect", ex.Message);
        }
    }

    /// <summary>Checks the scanner again, reconnects when it is on, and reloads the running scans.</summary>
    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        try
        {
            await RefreshHealthAsync();
            if (ServiceHealthStatus == ServiceHealthStatus.Running && !_signalRClient.IsConnected) await ConnectAsync();
            if (!_signalRClient.IsConnected) return;

            var sessions = await _signalRClient.GetAllSessionsAsync();
            OnUi(() =>
            {
                OngoingOperations.Clear();
                foreach (var scan in sessions.Where(s => s.Status == "InProgress"))
                {
                    OngoingOperations.Add(new OperationInfo { SessionId = scan.Id, ItemsProcessed = scan.FilesScanned, StartTime = scan.CreatedAt });
                }
                UpdateOperationCounts();
            });
        }
        catch (Exception ex)
        {
            Problem("Problem_Refresh", ex.Message);
        }
    }

    [RelayCommand]
    private void ClearActivity() => OnUi(() =>
    {
        Activity.Clear();
        Problems.Clear();
        UpdateProblemCounts();
    });

    private async Task StartAsync()
    {
        OnUi(() =>
        {
            IsStartingService = true;
            SetHealth(ServiceHealthStatus.Starting);
        });
        try
        {
            if (await _serviceManager.EnsureServiceRunningAsync())
            {
                Log(ActivityKind.Success, "Activity_ScannerStarted");
                await Task.Delay(1500);
                await ConnectAsync();
            }
            else
            {
                await RefreshHealthAsync();
                if (ServiceHealthStatus != ServiceHealthStatus.NotFound) Problem("Problem_ScannerStart", null);
            }
        }
        catch (Exception ex)
        {
            Problem("Problem_ScannerStart", ex.Message);
        }
        finally
        {
            OnUi(() => IsStartingService = false);
            await RefreshHealthAsync();
        }
    }

    private async Task StopAsync()
    {
        OnUi(() =>
        {
            IsStoppingService = true;
            SetHealth(ServiceHealthStatus.Stopping);
        });
        try
        {
            await _signalRClient.DisconnectAsync();
            if (await _serviceManager.StopServiceAsync()) Log(ActivityKind.Info, "Activity_ScannerStopped");
            else Problem("Problem_ScannerStop", null);
        }
        catch (Exception ex)
        {
            Problem("Problem_ScannerStop", ex.Message);
        }
        finally
        {
            OnUi(() => IsStoppingService = false);
            await RefreshHealthAsync();
        }
    }

    private async Task ConnectAsync()
    {
        OnUi(() => IsConnecting = true);
        try
        {
            if (await _signalRClient.ConnectAsync()) Log(ActivityKind.Success, "Activity_Connected");
            else Problem("Problem_Connect", null);
        }
        catch (Exception ex)
        {
            Problem("Problem_Connect", ex.Message);
        }
        finally
        {
            OnUi(() => IsConnecting = false);
        }
    }

    private async Task RefreshHealthAsync()
    {
        try
        {
            var info = await _serviceManager.GetServiceInfoAsync();
            OnUi(() =>
            {
                IsServiceManaged = info.IsManaged;
                SetHealth(info.Status);
            });
        }
        catch (Exception ex)
        {
            Problem("Problem_ScannerCheck", ex.Message);
        }
    }

    private void OnHealthChanged(object? sender, ServiceStatusChangedEventArgs e)
    {
        OnUi(() =>
        {
            SetHealth(e.CurrentStatus);
            if (e.CurrentStatus == ServiceHealthStatus.Error) Problem("Problem_ScannerStopped", null);
        });
        if (e.CurrentStatus == ServiceHealthStatus.Running && ServiceStatus != ServiceStatus.Connected && !IsConnecting)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                await ConnectAsync();
            });
        }
    }

    private void OnConnectionChanged(object? sender, ServiceStatusEventArgs e)
    {
        OnUi(() =>
        {
            ServiceStatus = e.Status;
            ConnectionText = Loc.Get(e.Status switch
            {
                ServiceStatus.Connected => "Scanner_ConnectionConnected",
                ServiceStatus.Reconnecting => "Scanner_ConnectionReconnecting",
                _ => "Scanner_ConnectionNone",
            });
            if (e.Status is ServiceStatus.Disconnected or ServiceStatus.NotAvailable)
            {
                OngoingOperations.Clear();
                UpdateOperationCounts();
            }
            UpdateScanner();
        });
    }

    private void OnConnectionError(object? sender, string error) => Problem("Problem_Connection", error);

    private void OnScanProgress(object? sender, FileScanProgressEventArgs e)
    {
        OnUi(() =>
        {
            FilesScanned = e.FilesScanned;
            FilesScannedText = Loc.Number(e.FilesScanned);
            var operation = OngoingOperations.FirstOrDefault(o => o.SessionId == e.SessionId);
            if (operation == null) return;
            operation.Progress = e.ProgressPercentage;
            operation.ItemsProcessed = e.FilesScanned;
        });
    }

    private void OnScanStarted(object? sender, Guid sessionId)
    {
        OnUi(() =>
        {
            OngoingOperations.Add(new OperationInfo { SessionId = sessionId, StartTime = DateTime.Now });
            UpdateOperationCounts();
        });
        Log(ActivityKind.Info, "Activity_ScanStarted");
    }

    private void OnScanCompleted(object? sender, Guid sessionId)
    {
        OnUi(() =>
        {
            var operation = OngoingOperations.FirstOrDefault(o => o.SessionId == sessionId);
            if (operation != null) OngoingOperations.Remove(operation);
            UpdateOperationCounts();
        });
        Log(ActivityKind.Success, "Activity_ScanCompleted");
    }

    private void OnScanFailed(object? sender, (Guid sessionId, string error) args)
    {
        OnUi(() =>
        {
            var operation = OngoingOperations.FirstOrDefault(o => o.SessionId == args.sessionId);
            if (operation != null) OngoingOperations.Remove(operation);
            UpdateOperationCounts();
        });
        Problem("Problem_ScanFailed", args.error);
    }

    private void OnScanCancelled(object? sender, Guid sessionId)
    {
        OnUi(() =>
        {
            var operation = OngoingOperations.FirstOrDefault(o => o.SessionId == sessionId);
            if (operation != null) OngoingOperations.Remove(operation);
            UpdateOperationCounts();
        });
        Log(ActivityKind.Info, "Activity_ScanCancelled");
    }

    private void SetHealth(ServiceHealthStatus status)
    {
        ServiceHealthStatus = status;
        CanStartService = status is not (ServiceHealthStatus.Running or ServiceHealthStatus.Starting or ServiceHealthStatus.Stopping);
        CanStopService = IsServiceManaged && status is ServiceHealthStatus.Running or ServiceHealthStatus.Starting;
        UpdateScanner();
    }

    private void UpdateScanner()
    {
        var state = ServiceHealthStatus switch
        {
            ServiceHealthStatus.Running => ScannerState.On,
            ServiceHealthStatus.Starting => ScannerState.Starting,
            ServiceHealthStatus.Stopping => ScannerState.Stopping,
            ServiceHealthStatus.Stopped => ScannerState.Off,
            ServiceHealthStatus.NotFound => ScannerState.Missing,
            ServiceHealthStatus.Error => ScannerState.Failed,
            _ => ScannerState.Checking,
        };
        ScannerState = state;
        (ScannerTitle, ScannerDetail, ScannerActionText) = state switch
        {
            ScannerState.On => (Loc.Get("Scanner_OnTitle"), Loc.Get(IsServiceManaged ? "Scanner_OnDetail" : "Scanner_OnExternalDetail"), Loc.Get("Scanner_ActionTurnOff")),
            ScannerState.Off => (Loc.Get("Scanner_OffTitle"), Loc.Get("Scanner_OffDetail"), Loc.Get("Scanner_ActionTurnOn")),
            ScannerState.Starting => (Loc.Get("Scanner_StartingTitle"), Loc.Get("Scanner_StartingDetail"), Loc.Get("Scanner_ActionTurnOn")),
            ScannerState.Stopping => (Loc.Get("Scanner_StoppingTitle"), Loc.Get("Scanner_StoppingDetail"), Loc.Get("Scanner_ActionTurnOff")),
            ScannerState.Missing => (Loc.Get("Scanner_MissingTitle"), Loc.Get("Scanner_MissingDetail"), Loc.Get("Scanner_ActionTryAgain")),
            ScannerState.Failed => (Loc.Get("Scanner_FailedTitle"), Loc.Get("Scanner_FailedDetail"), Loc.Get("Scanner_ActionTryAgain")),
            _ => (Loc.Get("Scanner_CheckingTitle"), Loc.Get("Scanner_CheckingDetail"), Loc.Get("Scanner_ActionTurnOn")),
        };
        IsScannerBusy = state is ScannerState.Checking or ScannerState.Starting or ScannerState.Stopping;
        CanRunScannerAction = !IsScannerBusy && (state != ScannerState.On || IsServiceManaged);
        ScannerStatusColor = state switch
        {
            ScannerState.On => "#FF2FA866",
            ScannerState.Failed or ScannerState.Missing => "#FFE0605A",
            ScannerState.Off => "#FF7F97A3",
            _ => "#FFE0962A",
        };
    }

    private void UpdateOperationCounts()
    {
        HasActiveOperation = OngoingOperations.Count > 0;
        ActiveOperationsText = Loc.Number(OngoingOperations.Count);
    }

    private void UpdateProblemCounts()
    {
        ProblemCount = Problems.Count;
        ProblemCountText = Loc.Number(Problems.Count);
        HasProblems = Problems.Count > 0;
        HasActivity = Activity.Count > 0;
    }

    private void Log(ActivityKind kind, string titleKey, string? detail = null)
    {
        var entry = new ActivityEntry(kind, Loc.Get(titleKey), detail, DateTime.Now);
        OnUi(() =>
        {
            Activity.Insert(0, entry);
            while (Activity.Count > ActivityLimit) Activity.RemoveAt(Activity.Count - 1);
            if (kind == ActivityKind.Problem)
            {
                Problems.Insert(0, entry);
                while (Problems.Count > ActivityLimit) Problems.RemoveAt(Problems.Count - 1);
            }
            UpdateProblemCounts();
        });
    }

    /// <summary>A problem staff can act on: a plain title, with the technical detail kept for the details line.</summary>
    private void Problem(string titleKey, string? detail) => Log(ActivityKind.Problem, titleKey, detail);

    private async Task UpdateMetricsAsync()
    {
        if (_disposed) return;
        try
        {
            var info = await _serviceManager.GetServiceInfoAsync();
            System.Diagnostics.Process? process = null;
            if (info.IsRunning && info.ProcessId is int pid)
            {
                try
                {
                    process = System.Diagnostics.Process.GetProcessById(pid);
                }
                catch (Exception)
                {
                    process = null;
                }
            }

            if (process == null || process.HasExited)
            {
                OnUi(() => ProcessText = UptimeText = MemoryText = ThreadsText = CpuText = "–");
                return;
            }

            var uptime = DateTime.Now - process.StartTime;
            var memory = process.WorkingSet64;
            var threads = process.Threads.Count;
            var id = process.Id;
            OnUi(() =>
            {
                ProcessText = Loc.IsArabic ? Loc.ToArabicDigits(id.ToString(System.Globalization.CultureInfo.InvariantCulture)) : id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                UptimeText = Loc.Duration(uptime);
                MemoryText = Loc.Bytes(memory);
                ThreadsText = Loc.Number(threads);
                CpuText = "–";
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scanner metrics: {ex.Message}");
        }
    }

    private void OnUi(Action action)
    {
        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess) action();
        else _dispatcherQueue.TryEnqueue(() => action());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _metricsTimer.Dispose();
        _signalRClient.ServiceStatusChanged -= OnConnectionChanged;
        _signalRClient.ScanProgressReceived -= OnScanProgress;
        _signalRClient.ScanStarted -= OnScanStarted;
        _signalRClient.ScanCompleted -= OnScanCompleted;
        _signalRClient.ScanFailed -= OnScanFailed;
        _signalRClient.ScanCancelled -= OnScanCancelled;
        _signalRClient.ErrorOccurred -= OnConnectionError;
        _serviceManager.ServiceStatusChanged -= OnHealthChanged;
    }
}

/// <summary>A scan the scanner is running.</summary>
public partial class OperationInfo : ObservableObject
{
    [ObservableProperty] private Guid _sessionId;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProgressText))] private double _progress;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ItemsText))] private int _itemsProcessed;
    [ObservableProperty] private DateTime _startTime;

    public string Title => Loc.Get("Operation_Scan");
    public string ProgressText => Loc.Percent(Progress);
    public string ItemsText => Loc.Format("Operation_FilesFound", ItemsProcessed);
    public string StartedText => Loc.Format("Operation_Started", Loc.Time(StartTime));
}

public enum ActivityKind
{
    Info,
    Success,
    Problem,
}

/// <summary>One line of scanner activity: a plain title, an optional technical detail, and when it happened.</summary>
public sealed record ActivityEntry(ActivityKind Kind, string Title, string? Detail, DateTime Time)
{
    public string TimeText => Loc.Time(Time);
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public string Glyph => Kind switch
    {
        ActivityKind.Problem => "",
        ActivityKind.Success => "",
        _ => "",
    };
    public string GlyphColor => Kind switch
    {
        ActivityKind.Problem => "#FFE0605A",
        ActivityKind.Success => "#FF2FA866",
        _ => "#FF7F97A3",
    };
}

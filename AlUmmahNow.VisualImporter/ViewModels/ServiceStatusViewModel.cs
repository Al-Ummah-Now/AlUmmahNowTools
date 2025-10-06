using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AlUmmahNow.VisualImporter.Services;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.UI.Dispatching;

namespace AlUmmahNow.VisualImporter.ViewModels;

/// <summary>
/// ViewModel for monitoring FileScan service status and managing service lifecycle
/// </summary>
public partial class ServiceStatusViewModel : ObservableObject, IDisposable
{
    private readonly FileScanSignalRClient _signalRClient;
    private readonly FileScanServiceManager _serviceManager;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _disposed = false;

    [ObservableProperty]
    private ServiceStatus _serviceStatus = ServiceStatus.NotAvailable;

    [ObservableProperty]
    private string _serviceStatusMessage = "Not connected";

    [ObservableProperty]
    private string _serviceStatusColor = "#FF6B6B"; // Red

    [ObservableProperty]
    private ServiceHealthStatus _serviceHealthStatus = ServiceHealthStatus.Unknown;

    [ObservableProperty]
    private string _serviceHealthMessage = "Unknown";

    [ObservableProperty]
    private bool _isServiceManaged = false;

    [ObservableProperty]
    private bool _canStartService = true;

    [ObservableProperty]
    private bool _canStopService = false;

    [ObservableProperty]
    private string _currentOperation = "No operations";

    [ObservableProperty]
    private double _progressPercentage = 0;

    [ObservableProperty]
    private int _filesScanned = 0;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private bool _hasActiveOperation = false;

    [ObservableProperty]
    private bool _isConnecting = false;

    [ObservableProperty]
    private bool _isStartingService = false;

    [ObservableProperty]
    private bool _isStoppingService = false;

    public ObservableCollection<ErrorMessage> Errors { get; } = new();
    public ObservableCollection<OperationInfo> OngoingOperations { get; } = new();

    public ServiceStatusViewModel(FileScanSignalRClient signalRClient, FileScanServiceManager serviceManager)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
        _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
        
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

        // Subscribe to events with safe initialization
        try
        {
            SubscribeToEvents();
            
            // Initialize connection in background to avoid blocking constructor
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000); // Give UI time to initialize
                    await InitializeServiceAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background initialization failed: {ex.Message}");
                    DispatcherExecute(() => AddServiceLog($"?? Initialization failed: {ex.Message}", MessageType.Error));
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ServiceStatusViewModel initialization failed: {ex.Message}");
            // Don't throw - let the view model exist in a degraded state
            ServiceStatusMessage = $"Initialization failed: {ex.Message}";
            ServiceStatus = ServiceStatus.NotAvailable;
        }
    }

    private async Task InitializeServiceAsync()
    {
        try
        {
            AddServiceLog("?? Initializing FileScan service management...", MessageType.Information);
            
            // First update service manager status
            await UpdateServiceManagerStatusAsync();

            DispatcherExecute(() =>
            {
                IsConnecting = true;
                ServiceStatusMessage = "Checking service availability...";
            });

            // Check if service is already running
            if (await _serviceManager.IsServiceRunningAsync())
            {
                AddServiceLog("? FileScan service is already running", MessageType.Success);
                
                // Try to connect to existing service
                var connected = await _signalRClient.ConnectAsync();
                
                DispatcherExecute(() =>
                {
                    if (connected)
                    {
                        ServiceStatusMessage = "Connected to existing FileScan service";
                        ServiceStatus = ServiceStatus.Connected;
                        AddServiceLog("?? Connected to existing FileScan service", MessageType.Success);
                    }
                    else
                    {
                        ServiceStatusMessage = "Service running but connection failed";
                        ServiceStatus = ServiceStatus.NotAvailable;
                        AddServiceLog("?? Service is running but SignalR connection failed", MessageType.Warning);
                    }
                    IsConnecting = false;
                });
            }
            else
            {
                AddServiceLog("?? No running FileScan service detected", MessageType.Information);
                
                DispatcherExecute(() =>
                {
                    ServiceStatusMessage = "FileScan service not running";
                    ServiceStatus = ServiceStatus.NotAvailable;
                    IsConnecting = false;
                });
                
                // Optionally start the service automatically
                AddServiceLog("?? Auto-starting FileScan service...", MessageType.Information);
                await StartServiceAsync();
            }
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                ServiceStatusMessage = $"Initialization failed: {ex.Message}";
                ServiceStatus = ServiceStatus.NotAvailable;
                IsConnecting = false;
                AddServiceLog($"?? Service initialization error: {ex.Message}", MessageType.Error);
            });
        }
    }

    private async Task UpdateServiceManagerStatusAsync()
    {
        try
        {
            var serviceInfo = await _serviceManager.GetServiceInfoAsync();
            
            DispatcherExecute(() =>
            {
                ServiceHealthStatus = serviceInfo.Status;
                IsServiceManaged = serviceInfo.IsManaged;
                
                ServiceHealthMessage = ServiceHealthStatus switch
                {
                    ServiceHealthStatus.Running => GetRunningStatusMessage(serviceInfo),
                    ServiceHealthStatus.Starting => "Service starting...",
                    ServiceHealthStatus.Stopping => "Service stopping...",
                    ServiceHealthStatus.Stopped => "Service stopped",
                    ServiceHealthStatus.Error => "Service error",
                    ServiceHealthStatus.NotFound => "Service executable not found",
                    _ => "Unknown"
                };
                
                CanStartService = ServiceHealthStatus != ServiceHealthStatus.Running && 
                                 ServiceHealthStatus != ServiceHealthStatus.Starting &&
                                 ServiceHealthStatus != ServiceHealthStatus.NotFound;
                                 
                CanStopService = IsServiceManaged && 
                               (ServiceHealthStatus == ServiceHealthStatus.Running || 
                                ServiceHealthStatus == ServiceHealthStatus.Starting);
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                AddError($"Failed to get service status: {ex.Message}");
            });
        }
    }

    private string GetRunningStatusMessage(ServiceInfo serviceInfo)
    {
        var baseMessage = $"Service running{(IsServiceManaged ? " (managed)" : " (external)")}";
        
        if (serviceInfo.SingletonInfo != null)
        {
            var singletonInfo = serviceInfo.SingletonInfo;
            if (singletonInfo.IsLockHeld)
            {
                baseMessage += $" - PID: {singletonInfo.CurrentProcessId}";
                if (singletonInfo.LockHolderStartTime.HasValue)
                {
                    var uptime = DateTime.Now - singletonInfo.LockHolderStartTime.Value;
                    if (uptime.TotalHours >= 1)
                        baseMessage += $", up {uptime.Hours}h {uptime.Minutes}m";
                    else if (uptime.TotalMinutes >= 1)
                        baseMessage += $", up {uptime.Minutes}m";
                    else
                        baseMessage += $", up {uptime.Seconds}s";
                }
            }
        }
        
        return baseMessage;
    }

    private void SubscribeToEvents()
    {
        // SignalR client events
        _signalRClient.ServiceStatusChanged += OnServiceStatusChanged;
        _signalRClient.ScanProgressReceived += OnScanProgressReceived;
        _signalRClient.ScanStarted += OnScanStarted;
        _signalRClient.ScanCompleted += OnScanCompleted;
        _signalRClient.ScanFailed += OnScanFailed;
        _signalRClient.ScanCancelled += OnScanCancelled;
        _signalRClient.ErrorOccurred += OnErrorOccurred;

        // Service manager events
        _serviceManager.ServiceStatusChanged += OnServiceManagerStatusChanged;
    }

    private void OnScanProgressReceived(object? sender, FileScanProgressEventArgs e)
    {
        DispatcherExecute(() =>
        {
            try
            {
                ProgressPercentage = e.ProgressPercentage;
                FilesScanned = e.FilesScanned;
                CurrentFile = e.CurrentFile;
                CurrentOperation = e.CurrentOperation;

                // Update the operation info for this session
                var operation = OngoingOperations.FirstOrDefault(o => o.SessionId == e.SessionId);
                if (operation != null)
                {
                    operation.Progress = e.ProgressPercentage;
                    operation.CurrentOperation = e.CurrentOperation;
                    operation.ItemsProcessed = e.FilesScanned;
                }

                AddServiceLog($"?? Scan progress: {e.ProgressPercentage:F1}% ({e.FilesScanned} files scanned)", MessageType.Information);
            }
            catch (Exception ex)
            {
                AddServiceLog($"?? Error processing scan progress: {ex.Message}", MessageType.Error);
            }
        });
    }

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        try
        {
            IsStartingService = true;
            CanStartService = false;
            AddServiceLog("?? Starting FileScan service...", MessageType.Information);
            
            var started = await _serviceManager.EnsureServiceRunningAsync();
            
            if (started)
            {
                AddServiceLog("? FileScan service started successfully", MessageType.Success);
                
                // Try to connect after service starts
                await Task.Delay(2000); // Give service time to fully initialize
                AddServiceLog("?? Attempting to connect to started service...", MessageType.Information);
                await ConnectToServiceAsync();
            }
            else
            {
                AddServiceLog("? Failed to start FileScan service", MessageType.Error);
            }
        }
        catch (Exception ex)
        {
            AddServiceLog($"?? Error starting service: {ex.Message}", MessageType.Error);
        }
        finally
        {
            IsStartingService = false;
            await UpdateServiceManagerStatusAsync();
        }
    }

    [RelayCommand]
    private async Task StopServiceAsync()
    {
        try
        {
            IsStoppingService = true;
            CanStopService = false;
            AddServiceLog("?? Stopping FileScan service...", MessageType.Information);
            
            // Disconnect first
            AddServiceLog("?? Disconnecting from service...", MessageType.Information);
            await _signalRClient.DisconnectAsync();
            
            // Then stop the service
            var stopped = await _serviceManager.StopServiceAsync();
            
            if (stopped)
            {
                AddServiceLog("? FileScan service stopped successfully", MessageType.Success);
            }
            else
            {
                AddServiceLog("?? Failed to stop FileScan service gracefully", MessageType.Warning);
            }
        }
        catch (Exception ex)
        {
            AddServiceLog($"?? Error stopping service: {ex.Message}", MessageType.Error);
        }
        finally
        {
            IsStoppingService = false;
            await UpdateServiceManagerStatusAsync();
        }
    }

    [RelayCommand]
    private async Task ConnectToServiceAsync()
    {
        try
        {
            IsConnecting = true;
            ServiceStatusMessage = "Connecting to FileScan service...";
            AddServiceLog("?? Connecting to FileScan service...", MessageType.Information);
            
            var connected = await _signalRClient.ConnectAsync();
            
            if (!connected)
            {
                AddServiceLog("? Failed to connect to FileScan service. Make sure the service is running.", MessageType.Error);
            }
            else
            {
                AddServiceLog("? Successfully connected to FileScan service", MessageType.Success);
            }
        }
        catch (Exception ex)
        {
            AddServiceLog($"?? Connection error: {ex.Message}", MessageType.Error);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectFromServiceAsync()
    {
        try
        {
            AddServiceLog("?? Disconnecting from FileScan service...", MessageType.Information);
            await _signalRClient.DisconnectAsync();
            ServiceStatus = ServiceStatus.Disconnected;
            ServiceStatusMessage = "Disconnected from service";
            UpdateStatusColor();
            AddServiceLog("? Disconnected from FileScan service", MessageType.Success);
        }
        catch (Exception ex)
        {
            AddServiceLog($"?? Disconnect error: {ex.Message}", MessageType.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        try
        {
            AddServiceLog("?? Refreshing service status...", MessageType.Information);
            
            // Update service manager status
            await UpdateServiceManagerStatusAsync();
            
            if (_signalRClient.IsConnected)
            {
                // Refresh ongoing operations by querying the service
                var sessions = await _signalRClient.GetAllSessionsAsync();
                
                DispatcherExecute(() =>
                {
                    // Update ongoing operations
                    OngoingOperations.Clear();
                    
                    var activeScans = sessions.Where(s => s.Status == "InProgress").ToList();
                    
                    foreach (var scan in activeScans)
                    {
                        OngoingOperations.Add(new OperationInfo
                        {
                            SessionId = scan.Id,
                            Type = "Scan",
                            Progress = 0, // Progress percentage not available in session DTO
                            CurrentOperation = scan.Status,
                            ItemsProcessed = scan.FilesScanned,
                            StartTime = scan.CreatedAt
                        });
                    }
                    
                    HasActiveOperation = OngoingOperations.Count > 0;
                    
                    if (!HasActiveOperation)
                    {
                        CurrentOperation = "No operations";
                        ProgressPercentage = 0;
                    }
                });
                
                AddServiceLog($"? Status refreshed: {sessions.Count} total sessions, {sessions.Count(s => s.Status == "InProgress")} active", MessageType.Success);
            }
            else
            {
                AddServiceLog("?? Not connected to service - cannot refresh operations", MessageType.Warning);
            }
        }
        catch (Exception ex)
        {
            AddServiceLog($"?? Refresh failed: {ex.Message}", MessageType.Error);
        }
    }

    private void OnServiceManagerStatusChanged(object? sender, ServiceStatusChangedEventArgs e)
    {
        DispatcherExecute(async () =>
        {
            ServiceHealthStatus = e.CurrentStatus;
            
            ServiceHealthMessage = ServiceHealthStatus switch
            {
                ServiceHealthStatus.Running => $"Service running{(IsServiceManaged ? " (managed)" : " (external)")}",
                ServiceHealthStatus.Starting => "Service starting...",
                ServiceHealthStatus.Stopping => "Service stopping...",
                ServiceHealthStatus.Stopped => "Service stopped",
                ServiceHealthStatus.Error => "Service error",
                ServiceHealthStatus.NotFound => "Service executable not found",
                _ => "Unknown"
            };
            
            // Log service status changes
            var statusMessage = ServiceHealthStatus switch
            {
                ServiceHealthStatus.Running => $"?? Service is now running {(IsServiceManaged ? "(managed by this app)" : "(external process)")}",
                ServiceHealthStatus.Starting => "?? Service is starting up...",
                ServiceHealthStatus.Stopping => "?? Service is shutting down...",
                ServiceHealthStatus.Stopped => "?? Service has stopped",
                ServiceHealthStatus.Error => "?? Service encountered an error",
                ServiceHealthStatus.NotFound => "? Service executable not found",
                _ => $"? Service status unknown: {ServiceHealthStatus}"
            };
            
            var messageType = ServiceHealthStatus switch
            {
                ServiceHealthStatus.Running => MessageType.Success,
                ServiceHealthStatus.Starting or ServiceHealthStatus.Stopping => MessageType.Information,
                ServiceHealthStatus.Stopped => MessageType.Warning,
                ServiceHealthStatus.Error or ServiceHealthStatus.NotFound => MessageType.Error,
                _ => MessageType.Information
            };
            
            AddServiceLog(statusMessage, messageType);
            
            CanStartService = ServiceHealthStatus != ServiceHealthStatus.Running && 
                             ServiceHealthStatus != ServiceHealthStatus.Starting &&
                             ServiceHealthStatus != ServiceHealthStatus.NotFound;
                             
            CanStopService = IsServiceManaged && 
                           (ServiceHealthStatus == ServiceHealthStatus.Running || 
                            ServiceHealthStatus == ServiceHealthStatus.Starting);

            // Try to connect when service becomes available
            if (ServiceHealthStatus == ServiceHealthStatus.Running && 
                ServiceStatus != ServiceStatus.Connected && 
                !IsConnecting)
            {
                AddServiceLog("?? Service is running, attempting automatic connection...", MessageType.Information);
                await Task.Delay(1000); // Give service time to fully start
                await ConnectToServiceAsync();
            }
        });
    }

    private void OnServiceStatusChanged(object? sender, ServiceStatusEventArgs e)
    {
        DispatcherExecute(() =>
        {
            ServiceStatus = e.Status;
            ServiceStatusMessage = e.Message;
            UpdateStatusColor();
            
            // Log SignalR connection status changes
            var connectionMessage = ServiceStatus switch
            {
                ServiceStatus.Connected => "?? SignalR connection established",
                ServiceStatus.Reconnecting => "?? SignalR connection lost, reconnecting...",
                ServiceStatus.Disconnected => "?? SignalR disconnected",
                ServiceStatus.NotAvailable => "? SignalR service not available",
                ServiceStatus.Stopped => "?? SignalR service stopped",
                _ => $"? SignalR status: {ServiceStatus}"
            };
            
            var messageType = ServiceStatus switch
            {
                ServiceStatus.Connected => MessageType.Success,
                ServiceStatus.Reconnecting => MessageType.Information,
                ServiceStatus.Disconnected or ServiceStatus.Stopped => MessageType.Warning,
                ServiceStatus.NotAvailable => MessageType.Error,
                _ => MessageType.Information
            };
            
            AddServiceLog(connectionMessage, messageType);
            
            // Clear ongoing operations if disconnected
            if (e.Status == ServiceStatus.Disconnected || e.Status == ServiceStatus.NotAvailable)
            {
                OngoingOperations.Clear();
                HasActiveOperation = false;
                CurrentOperation = "No operations";
                ProgressPercentage = 0;
            }
        });
    }

    private void OnScanStarted(object? sender, Guid sessionId)
    {
        DispatcherExecute(() =>
        {
            var operation = new OperationInfo
            {
                SessionId = sessionId,
                Type = "Scan",
                Progress = 0,
                CurrentOperation = "Starting scan...",
                ItemsProcessed = 0,
                StartTime = DateTime.Now
            };
            
            OngoingOperations.Add(operation);
            HasActiveOperation = true;
            CurrentOperation = "Scan started";
            
            AddServiceLog($"?? Scan operation started (Session: {sessionId:N})", MessageType.Success);
        });
    }

    private void OnScanCompleted(object? sender, Guid sessionId)
    {
        DispatcherExecute(() =>
        {
            var operation = OngoingOperations.FirstOrDefault(o => o.SessionId == sessionId);
            if (operation != null)
            {
                operation.Progress = 100;
                operation.CurrentOperation = "Completed";
                operation.EndTime = DateTime.Now;
                
                AddServiceLog($"? Scan operation completed (Session: {sessionId:N})", MessageType.Success);
                
                // Remove after a delay
                Task.Delay(3000).ContinueWith(_ =>
                {
                    DispatcherExecute(() =>
                    {
                        OngoingOperations.Remove(operation);
                        HasActiveOperation = OngoingOperations.Count > 0;
                        
                        if (!HasActiveOperation)
                        {
                            CurrentOperation = "No operations";
                            ProgressPercentage = 0;
                        }
                    });
                });
            }
        });
    }

    private void OnScanFailed(object? sender, (Guid sessionId, string error) args)
    {
        DispatcherExecute(() =>
        {
            var operation = OngoingOperations.FirstOrDefault(o => o.SessionId == args.sessionId);
            if (operation != null)
            {
                operation.CurrentOperation = "Failed";
                operation.EndTime = DateTime.Now;
                OngoingOperations.Remove(operation);
            }
            
            AddServiceLog($"? Scan operation failed (Session: {args.sessionId:N}): {args.error}", MessageType.Error);
            HasActiveOperation = OngoingOperations.Count > 0;
        });
    }

    private void OnScanCancelled(object? sender, Guid sessionId)
    {
        DispatcherExecute(() =>
        {
            var operation = OngoingOperations.FirstOrDefault(o => o.SessionId == sessionId);
            if (operation != null)
            {
                operation.CurrentOperation = "Cancelled";
                operation.EndTime = DateTime.Now;
                OngoingOperations.Remove(operation);
            }
            
            AddServiceLog($"?? Scan operation cancelled (Session: {sessionId:N})", MessageType.Warning);
            HasActiveOperation = OngoingOperations.Count > 0;
            
            if (!HasActiveOperation)
            {
                CurrentOperation = "No operations";
                ProgressPercentage = 0;
            }
        });
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        DispatcherExecute(() => AddServiceLog($"?? SignalR error: {error}", MessageType.Error));
    }

    private void AddError(string message)
    {
        var error = new ErrorMessage
        {
            Message = message,
            Timestamp = DateTime.Now,
            Type = GetMessageType(message)
        };
        
        Errors.Insert(0, error); // Add to beginning
        
        // Keep only last 100 messages
        while (Errors.Count > 100)
        {
            Errors.RemoveAt(Errors.Count - 1);
        }
    }

    private MessageType GetMessageType(string message)
    {
        var lowerMessage = message.ToLowerInvariant();
        
        if (lowerMessage.Contains("error") || lowerMessage.Contains("failed") || lowerMessage.Contains("exception"))
            return MessageType.Error;
        
        if (lowerMessage.Contains("warning") || lowerMessage.Contains("timeout") || lowerMessage.Contains("retry"))
            return MessageType.Warning;
        
        if (lowerMessage.Contains("connected") || lowerMessage.Contains("started") || lowerMessage.Contains("completed") || lowerMessage.Contains("successful"))
            return MessageType.Success;
        
        return MessageType.Information;
    }

    private void AddServiceLog(string message, MessageType type = MessageType.Information)
    {
        var error = new ErrorMessage
        {
            Message = message,
            Timestamp = DateTime.Now,
            Type = type
        };
        
        Errors.Insert(0, error);
        
        // Keep only last 100 messages
        while (Errors.Count > 100)
        {
            Errors.RemoveAt(Errors.Count - 1);
        }
    }

    [RelayCommand]
    private void ClearErrors()
    {
        Errors.Clear();
    }

    private void UpdateStatusColor()
    {
        ServiceStatusColor = ServiceStatus switch
        {
            ServiceStatus.Connected => "#51CF66", // Green
            ServiceStatus.Reconnecting => "#FFD43B", // Yellow
            ServiceStatus.Disconnected => "#FF8787", // Light red
            ServiceStatus.NotAvailable => "#FF6B6B", // Red
            ServiceStatus.Stopped => "#868E96", // Gray
            _ => "#FF6B6B"
        };
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
            // Fallback: execute directly
            action();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Unsubscribe from SignalR events
            _signalRClient.ServiceStatusChanged -= OnServiceStatusChanged;
            _signalRClient.ScanProgressReceived -= OnScanProgressReceived;
            _signalRClient.ScanStarted -= OnScanStarted;
            _signalRClient.ScanCompleted -= OnScanCompleted;
            _signalRClient.ScanFailed -= OnScanFailed;
            _signalRClient.ScanCancelled -= OnScanCancelled;
            _signalRClient.ErrorOccurred -= OnErrorOccurred;
            
            // Unsubscribe from service manager events
            _serviceManager.ServiceStatusChanged -= OnServiceManagerStatusChanged;
            
            _disposed = true;
        }
    }
}

/// <summary>
/// Information about an ongoing operation
/// </summary>
public partial class OperationInfo : ObservableObject
{
    [ObservableProperty]
    private Guid _sessionId;

    [ObservableProperty]
    private string _type = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _currentOperation = string.Empty;

    [ObservableProperty]
    private int _itemsProcessed;

    [ObservableProperty]
    private DateTime _startTime;

    [ObservableProperty]
    private DateTime? _endTime;

    public string DisplayText => $"{Type}: {CurrentOperation} ({Progress:F1}%)";
    
    public string Duration
    {
        get
        {
            var end = EndTime ?? DateTime.Now;
            var duration = end - StartTime;
            return duration.TotalSeconds < 60 
                ? $"{duration.TotalSeconds:F0}s" 
                : $"{duration.TotalMinutes:F1}m";
        }
    }
}

/// <summary>
/// Error message information
/// </summary>
public class ErrorMessage
{
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public MessageType Type { get; set; } = MessageType.Information;
    
    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Message}";
    public string TypeIcon => Type switch
    {
        MessageType.Error => "?",
        MessageType.Warning => "??",
        MessageType.Success => "?",
        MessageType.Information => "??",
        _ => "•"
    };
}

/// <summary>
/// Message type enumeration
/// </summary>
public enum MessageType
{
    Information,
    Success,
    Warning,
    Error
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AlUmmahNow.VisualImporter.Services;
using Microsoft.UI.Dispatching;

namespace AlUmmahNow.VisualImporter.ViewModels;

/// <summary>
/// ViewModel for monitoring FileScan service status
/// </summary>
public partial class ServiceStatusViewModel : ObservableObject, IDisposable
{
    private readonly FileScanSignalRClient _signalRClient;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _disposed = false;

    [ObservableProperty]
    private ServiceStatus _serviceStatus = ServiceStatus.NotAvailable;

    [ObservableProperty]
    private string _serviceStatusMessage = "Not connected";

    [ObservableProperty]
    private string _serviceStatusColor = "#FF6B6B"; // Red

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

    public ObservableCollection<ErrorMessage> Errors { get; } = new();
    public ObservableCollection<OperationInfo> OngoingOperations { get; } = new();

    public ServiceStatusViewModel(FileScanSignalRClient signalRClient)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
        
        // Get dispatcher queue for UI updates
        try
        {
            _dispatcherQueue = App.Current?.MainWindow?.DispatcherQueue 
                ?? DispatcherQueue.GetForCurrentThread();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get dispatcher queue: {ex.Message}");
        }

        // Subscribe to SignalR events
        SubscribeToEvents();
    }

    private void SubscribeToEvents()
    {
        _signalRClient.ServiceStatusChanged += OnServiceStatusChanged;
        _signalRClient.ScanProgressReceived += OnScanProgressReceived;
        _signalRClient.ScanStarted += OnScanStarted;
        _signalRClient.ScanCompleted += OnScanCompleted;
        _signalRClient.ScanFailed += OnScanFailed;
        _signalRClient.ScanCancelled += OnScanCancelled;
        _signalRClient.ErrorOccurred += OnErrorOccurred;
    }

    [RelayCommand]
    private async Task ConnectToServiceAsync()
    {
        try
        {
            IsConnecting = true;
            ServiceStatusMessage = "Connecting to FileScan service...";
            
            var connected = await _signalRClient.ConnectAsync();
            
            if (!connected)
            {
                AddError("Failed to connect to FileScan service. Make sure the service is running.");
            }
        }
        catch (Exception ex)
        {
            AddError($"Connection error: {ex.Message}");
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
            await _signalRClient.DisconnectAsync();
            ServiceStatus = ServiceStatus.Disconnected;
            ServiceStatusMessage = "Disconnected from service";
            UpdateStatusColor();
        }
        catch (Exception ex)
        {
            AddError($"Disconnect error: {ex.Message}");
        }
    }

    private void OnServiceStatusChanged(object? sender, ServiceStatusEventArgs e)
    {
        DispatcherExecute(() =>
        {
            ServiceStatus = e.Status;
            ServiceStatusMessage = e.Message;
            UpdateStatusColor();
            
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

    private void OnScanProgressReceived(object? sender, FileScanProgressEventArgs e)
    {
        DispatcherExecute(() =>
        {
            CurrentOperation = e.CurrentOperation;
            ProgressPercentage = e.ProgressPercentage;
            FilesScanned = e.FilesScanned;
            CurrentFile = !string.IsNullOrEmpty(e.CurrentFile) ? 
                System.IO.Path.GetFileName(e.CurrentFile) : string.Empty;
            
            // Update or add operation info
            var operation = OngoingOperations.FirstOrDefault(o => o.SessionId == e.SessionId);
            if (operation != null)
            {
                operation.Progress = e.ProgressPercentage;
                operation.CurrentOperation = e.CurrentOperation;
                operation.ItemsProcessed = e.FilesScanned;
            }
            else
            {
                OngoingOperations.Add(new OperationInfo
                {
                    SessionId = e.SessionId,
                    Type = "Scan",
                    Progress = e.ProgressPercentage,
                    CurrentOperation = e.CurrentOperation,
                    ItemsProcessed = e.FilesScanned,
                    StartTime = DateTime.Now
                });
            }
            
            HasActiveOperation = OngoingOperations.Count > 0;
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
            
            AddError($"Scan {args.sessionId} failed: {args.error}");
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
        DispatcherExecute(() => AddError(error));
    }

    private void AddError(string message)
    {
        var error = new ErrorMessage
        {
            Message = message,
            Timestamp = DateTime.Now
        };
        
        Errors.Insert(0, error); // Add to beginning
        
        // Keep only last 50 errors
        while (Errors.Count > 50)
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
            _signalRClient.ServiceStatusChanged -= OnServiceStatusChanged;
            _signalRClient.ScanProgressReceived -= OnScanProgressReceived;
            _signalRClient.ScanStarted -= OnScanStarted;
            _signalRClient.ScanCompleted -= OnScanCompleted;
            _signalRClient.ScanFailed -= OnScanFailed;
            _signalRClient.ScanCancelled -= OnScanCancelled;
            _signalRClient.ErrorOccurred -= OnErrorOccurred;
            
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
    
    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Message}";
}

using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AlUmmahNow.Data.Models.FileScanning;

namespace AlUmmahNow.VisualImporterUno.Services;

/// <summary>
/// SignalR client for real-time communication with FileScanService
/// </summary>
public class FileScanSignalRClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly string _hubUrl;
    private bool _isConnected = false;

    // Scan Events
    public event EventHandler<ServiceStatusEventArgs>? ServiceStatusChanged;
    public event EventHandler<FileScanProgressEventArgs>? ScanProgressReceived;
    public event EventHandler<Guid>? ScanCompleted;
    public event EventHandler<(Guid sessionId, string error)>? ScanFailed;
    public event EventHandler<Guid>? ScanStarted;
    public event EventHandler<Guid>? ScanCancelled;

    // Watch Events
    public event EventHandler<FileDetectedEventArgs>? FileDetected;
    public event EventHandler<WatchStatusChangedEventArgs>? WatchStatusChanged;
    public event EventHandler<Guid>? WatchStopped;
    public event EventHandler<Guid>? WatchUpdated;
    public event EventHandler<Guid>? WatchDeleted;

    // Approval Events
    public event EventHandler<NewFileForApprovalEventArgs>? NewFileForApproval;
    public event EventHandler<ApprovalStatusChangedEventArgs>? ApprovalStatusChanged;
    public event EventHandler<ApprovalDecisionRequest>? ApprovalDecisionProcessed;
    public event EventHandler<BulkApprovalProcessedEventArgs>? BulkApprovalProcessed;

    // General Error Event
    public event EventHandler<string>? ErrorOccurred;

    public FileScanSignalRClient(string serviceUrl)
    {
        _hubUrl = $"{serviceUrl.TrimEnd('/')}/scanHub";

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        // Scan Events
        SetupScanEventHandlers();

        // Watch Events
        SetupWatchEventHandlers();

        // Approval Events
        SetupApprovalEventHandlers();

        // Connection Events
        SetupConnectionEventHandlers();
    }

    private void SetupScanEventHandlers()
    {
        _connection.On<Guid, string>("ScanStarted", (sessionId, path) =>
        {
            System.Diagnostics.Debug.WriteLine($"Scan started for session {sessionId}");
            ScanStarted?.Invoke(this, sessionId);
        });

        _connection.On<Guid, object>("ScanProgress", (sessionId, progress) =>
        {
            try
            {
                var progressData = JsonSerializer.Deserialize<ScanProgressData>(
                    JsonSerializer.Serialize(progress));

                if (progressData != null)
                {
                    ScanProgressReceived?.Invoke(this, new FileScanProgressEventArgs
                    {
                        SessionId = sessionId,
                        FilesScanned = progressData.FilesScanned,
                        CurrentFile = progressData.CurrentFile ?? "",
                        ProgressPercentage = progressData.ProgressPercentage,
                        CurrentOperation = progressData.CurrentOperation
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing scan progress: {ex.Message}");
            }
        });

        _connection.On<Guid, int>("ScanCompleted", (sessionId, fileCount) =>
        {
            System.Diagnostics.Debug.WriteLine($"Scan completed: {sessionId} ({fileCount} files found)");
            ScanCompleted?.Invoke(this, sessionId);
        });

        _connection.On<Guid, string>("ScanFailed", (sessionId, error) =>
        {
            System.Diagnostics.Debug.WriteLine($"Scan failed: {sessionId} - {error}");
            ScanFailed?.Invoke(this, (sessionId, error));
            ErrorOccurred?.Invoke(this, $"Scan {sessionId} failed: {error}");
        });

        _connection.On<Guid>("ScanCancelled", (sessionId) =>
        {
            System.Diagnostics.Debug.WriteLine($"Scan cancelled: {sessionId}");
            ScanCancelled?.Invoke(this, sessionId);
        });
    }

    private void SetupWatchEventHandlers()
    {
        _connection.On<Guid, string, string, string, long>("FileDetected",
            (watchId, channelName, fileName, filePath, fileSize) =>
        {
            System.Diagnostics.Debug.WriteLine($"File detected in watch {watchId}: {fileName} ({fileSize} bytes)");
            FileDetected?.Invoke(this, new FileDetectedEventArgs
            {
                WatchId = watchId,
                ChannelName = channelName,
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileSize
            });
        });

        _connection.On<Guid, string, bool, string>("WatchStatusChanged",
            (watchId, channelName, isActive, statusMessage) =>
        {
            System.Diagnostics.Debug.WriteLine($"Watch status changed for {watchId}: {statusMessage}");
            WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs
            {
                WatchId = watchId,
                ChannelName = channelName,
                IsActive = isActive,
                StatusMessage = statusMessage
            });
        });

        _connection.On<Guid>("WatchStopped", (watchId) =>
        {
            System.Diagnostics.Debug.WriteLine($"Watch stopped: {watchId}");
            WatchStopped?.Invoke(this, watchId);
        });

        _connection.On<Guid>("WatchUpdated", (watchId) =>
        {
            System.Diagnostics.Debug.WriteLine($"Watch updated: {watchId}");
            WatchUpdated?.Invoke(this, watchId);
        });

        _connection.On<Guid>("WatchDeleted", (watchId) =>
        {
            System.Diagnostics.Debug.WriteLine($"Watch deleted: {watchId}");
            WatchDeleted?.Invoke(this, watchId);
        });
    }

    private void SetupApprovalEventHandlers()
    {
        _connection.On<Guid, string, string, string, long>("NewFileForApproval",
            (watchId, channelName, fileName, filePath, fileSize) =>
        {
            System.Diagnostics.Debug.WriteLine($"New file for approval: {fileName} from channel {channelName}");
            NewFileForApproval?.Invoke(this, new NewFileForApprovalEventArgs
            {
                WatchId = watchId,
                ChannelName = channelName,
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileSize
            });
        });

        _connection.On<Guid, string, string, ApprovalStatus, ApprovalStatus, string>("ApprovalStatusChanged",
            (approvalId, channelName, fileName, oldStatus, newStatus, approvedBy) =>
        {
            System.Diagnostics.Debug.WriteLine($"Approval status changed: {fileName} from {oldStatus} to {newStatus}");
            ApprovalStatusChanged?.Invoke(this, new ApprovalStatusChangedEventArgs
            {
                ApprovalId = approvalId,
                ChannelName = channelName,
                FileName = fileName,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                ApprovedBy = approvedBy
            });
        });

        _connection.On<ApprovalDecisionRequest>("ApprovalDecisionProcessed", (request) =>
        {
            System.Diagnostics.Debug.WriteLine($"Approval decision processed: {request.ApprovalIds.Count} files");
            ApprovalDecisionProcessed?.Invoke(this, request);
        });

        _connection.On<BulkApprovalRequest, int>("BulkApprovalProcessed", (request, processedCount) =>
        {
            System.Diagnostics.Debug.WriteLine($"Bulk approval processed: {processedCount} files");
            BulkApprovalProcessed?.Invoke(this, new BulkApprovalProcessedEventArgs
            {
                Request = request,
                ProcessedCount = processedCount
            });
        });
    }

    private void SetupConnectionEventHandlers()
    {
        _connection.Reconnecting += error =>
        {
            _isConnected = false;
            System.Diagnostics.Debug.WriteLine($"Connection lost. Reconnecting...");
            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs
            {
                Status = ServiceStatus.Reconnecting,
                Message = "Connection lost. Attempting to reconnect..."
            });
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            _isConnected = true;
            System.Diagnostics.Debug.WriteLine($"Reconnected to FileScan service");
            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs
            {
                Status = ServiceStatus.Connected,
                Message = "Connected to FileScan service"
            });
            return Task.CompletedTask;
        };

        _connection.Closed += error =>
        {
            _isConnected = false;
            if (error != null)
            {
                System.Diagnostics.Debug.WriteLine($"Connection closed with error: {error.Message}");
                ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs
                {
                    Status = ServiceStatus.Disconnected,
                    Message = $"Connection closed: {error.Message}"
                });
                ErrorOccurred?.Invoke(this, $"Connection closed: {error.Message}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Connection closed");
                ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs
                {
                    Status = ServiceStatus.Stopped,
                    Message = "Service stopped"
                });
            }
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Connect to the SignalR hub
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            if (_connection.State == HubConnectionState.Connected)
            {
                _isConnected = true;
                return true;
            }

            await _connection.StartAsync();
            _isConnected = true;

            System.Diagnostics.Debug.WriteLine($"Connected to FileScan service at {_hubUrl}");
            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs
            {
                Status = ServiceStatus.Connected,
                Message = "Connected to FileScan service"
            });

            return true;
        }
        catch (Exception ex)
        {
            _isConnected = false;
            System.Diagnostics.Debug.WriteLine($"Failed to connect to FileScan service at {_hubUrl}: {ex.Message}");
            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs
            {
                Status = ServiceStatus.NotAvailable,
                Message = $"Service not available: {ex.Message}"
            });
            ErrorOccurred?.Invoke(this, $"Failed to connect: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if connected to the service
    /// </summary>
    public bool IsConnected => _isConnected && _connection.State == HubConnectionState.Connected;

    /// <summary>
    /// Get current service status
    /// </summary>
    public ServiceStatus GetServiceStatus()
    {
        return _connection.State switch
        {
            HubConnectionState.Connected => ServiceStatus.Connected,
            HubConnectionState.Connecting => ServiceStatus.Reconnecting,
            HubConnectionState.Reconnecting => ServiceStatus.Reconnecting,
            HubConnectionState.Disconnected => ServiceStatus.Disconnected,
            _ => ServiceStatus.NotAvailable
        };
    }

    #region Scan Operations

    /// <summary>
    /// Start a new scan
    /// </summary>
    public async Task<Guid> StartScanAsync(string folderPath, bool includeSubdirectories = true)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<Guid>(
                "StartScan",
                folderPath,
                includeSubdirectories);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to start scan: {ex.Message}");
            throw new InvalidOperationException($"Failed to start scan: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get status of a scan session
    /// </summary>
    public async Task<ScanStatusDto> GetStatusAsync(Guid sessionId)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<ScanStatusDto>("GetScanStatus", sessionId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get scan status: {ex.Message}");
            throw new InvalidOperationException($"Failed to get scan status: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get all scan sessions
    /// </summary>
    public async Task<List<ScanSessionDto>> GetAllSessionsAsync()
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<List<ScanSessionDto>>("GetAllSessions");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get sessions: {ex.Message}");
            throw new InvalidOperationException($"Failed to get sessions: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Cancel a scan
    /// </summary>
    public async Task CancelScanAsync(Guid sessionId)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            await _connection.InvokeAsync("CancelScan", sessionId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to cancel scan: {ex.Message}");
            throw new InvalidOperationException($"Failed to cancel scan: {ex.Message}", ex);
        }
    }

    #endregion

    #region Watch Operations

    /// <summary>
    /// Start a new channel watch
    /// </summary>
    public async Task<Guid> StartWatchAsync(CreateWatchRequest request)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            var watchId = await _connection.InvokeAsync<Guid>("StartWatch", request);
            System.Diagnostics.Debug.WriteLine($"Watch started: {watchId} for channel {request.ChannelName}");
            return watchId;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to start watch: {ex.Message}");
            throw new InvalidOperationException($"Failed to start watch: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Stop a channel watch
    /// </summary>
    public async Task<bool> StopWatchAsync(Guid watchId)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            var result = await _connection.InvokeAsync<bool>("StopWatch", watchId);
            System.Diagnostics.Debug.WriteLine($"Watch stop requested: {watchId}, result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to stop watch: {ex.Message}");
            throw new InvalidOperationException($"Failed to stop watch: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get all active watches
    /// </summary>
    public async Task<List<WatchStatusDto>> GetActiveWatchesAsync()
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<List<WatchStatusDto>>("GetActiveWatches");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get active watches: {ex.Message}");
            throw new InvalidOperationException($"Failed to get active watches: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get status of a specific watch
    /// </summary>
    public async Task<WatchStatusDto?> GetWatchStatusAsync(Guid watchId)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<WatchStatusDto?>("GetWatchStatus", watchId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get watch status: {ex.Message}");
            throw new InvalidOperationException($"Failed to get watch status: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Update a watch configuration
    /// </summary>
    public async Task<bool> UpdateWatchAsync(Guid watchId, CreateWatchRequest request)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            var result = await _connection.InvokeAsync<bool>("UpdateWatch", watchId, request);
            System.Diagnostics.Debug.WriteLine($"Watch update requested: {watchId}, result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to update watch: {ex.Message}");
            throw new InvalidOperationException($"Failed to update watch: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Delete a watch
    /// </summary>
    public async Task<bool> DeleteWatchAsync(Guid watchId)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            var result = await _connection.InvokeAsync<bool>("DeleteWatch", watchId);
            System.Diagnostics.Debug.WriteLine($"Watch delete requested: {watchId}, result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to delete watch: {ex.Message}");
            throw new InvalidOperationException($"Failed to delete watch: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Join a watch group to receive real-time updates
    /// </summary>
    public async Task JoinWatchGroupAsync(Guid watchId)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            await _connection.InvokeAsync("JoinWatchGroup", watchId);
            System.Diagnostics.Debug.WriteLine($"Joined watch group: {watchId}");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to join watch group: {ex.Message}");
            throw new InvalidOperationException($"Failed to join watch group: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Leave a watch group
    /// </summary>
    public async Task LeaveWatchGroupAsync(Guid watchId)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            await _connection.InvokeAsync("LeaveWatchGroup", watchId);
            System.Diagnostics.Debug.WriteLine($"Left watch group: {watchId}");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to leave watch group: {ex.Message}");
            throw new InvalidOperationException($"Failed to leave watch group: {ex.Message}", ex);
        }
    }

    #endregion

    #region Approval Operations

    /// <summary>
    /// Get all pending approvals
    /// </summary>
    public async Task<List<PendingApprovalDto>> GetPendingApprovalsAsync()
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<List<PendingApprovalDto>>("GetPendingApprovals");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get pending approvals: {ex.Message}");
            throw new InvalidOperationException($"Failed to get pending approvals: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get pending approvals for a specific channel
    /// </summary>
    public async Task<List<PendingApprovalDto>> GetPendingApprovalsByChannelAsync(string channelName)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<List<PendingApprovalDto>>("GetPendingApprovalsByChannel", channelName);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get pending approvals by channel: {ex.Message}");
            throw new InvalidOperationException($"Failed to get pending approvals by channel: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Process an approval decision (approve/reject files)
    /// </summary>
    public async Task<bool> ProcessApprovalDecisionAsync(ApprovalDecisionRequest request)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            var result = await _connection.InvokeAsync<bool>("ProcessApprovalDecision", request);
            System.Diagnostics.Debug.WriteLine($"Approval decision processed: {request.ApprovalIds.Count} files, result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to process approval decision: {ex.Message}");
            throw new InvalidOperationException($"Failed to process approval decision: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Process bulk approvals based on criteria
    /// </summary>
    public async Task<int> ProcessBulkApprovalAsync(BulkApprovalRequest request)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            var count = await _connection.InvokeAsync<int>("ProcessBulkApproval", request);
            System.Diagnostics.Debug.WriteLine($"Bulk approval processed: {count} files");
            return count;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to process bulk approval: {ex.Message}");
            throw new InvalidOperationException($"Failed to process bulk approval: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get approval statistics
    /// </summary>
    public async Task<ApprovalStatistics> GetApprovalStatisticsAsync(Guid? channelWatchId = null)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<ApprovalStatistics>("GetApprovalStatistics", channelWatchId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get approval statistics: {ex.Message}");
            throw new InvalidOperationException($"Failed to get approval statistics: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Join an approval group to receive real-time updates
    /// </summary>
    public async Task JoinApprovalGroupAsync(string channelName)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            await _connection.InvokeAsync("JoinApprovalGroup", channelName);
            System.Diagnostics.Debug.WriteLine($"Joined approval group: {channelName}");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to join approval group: {ex.Message}");
            throw new InvalidOperationException($"Failed to join approval group: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Leave an approval group
    /// </summary>
    public async Task LeaveApprovalGroupAsync(string channelName)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            await _connection.InvokeAsync("LeaveApprovalGroup", channelName);
            System.Diagnostics.Debug.WriteLine($"Left approval group: {channelName}");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to leave approval group: {ex.Message}");
            throw new InvalidOperationException($"Failed to leave approval group: {ex.Message}", ex);
        }
    }

    #endregion

    /// <summary>
    /// Disconnect from the service
    /// </summary>
    public async Task DisconnectAsync()
    {
        try
        {
            if (_connection.State != HubConnectionState.Disconnected)
            {
                await _connection.StopAsync();
            }
            _isConnected = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disconnecting: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        await _connection.DisposeAsync();
    }
}

#region Event Arguments Classes

/// <summary>
/// Event arguments for scan progress updates from FileScan service
/// </summary>
public class FileScanProgressEventArgs : EventArgs
{
    public Guid SessionId { get; set; }
    public int FilesScanned { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
}

/// <summary>
/// Event arguments for service status changes
/// </summary>
public class ServiceStatusEventArgs : EventArgs
{
    public ServiceStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Event arguments for file detected events
/// </summary>
public class FileDetectedEventArgs : EventArgs
{
    public Guid WatchId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

/// <summary>
/// Event arguments for watch status changed events
/// </summary>
public class WatchStatusChangedEventArgs : EventArgs
{
    public Guid WatchId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

/// <summary>
/// Event arguments for new file for approval events
/// </summary>
public class NewFileForApprovalEventArgs : EventArgs
{
    public Guid WatchId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

/// <summary>
/// Event arguments for approval status changed events
/// </summary>
public class ApprovalStatusChangedEventArgs : EventArgs
{
    public Guid ApprovalId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public ApprovalStatus OldStatus { get; set; }
    public ApprovalStatus NewStatus { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
}

/// <summary>
/// Event arguments for bulk approval processed events
/// </summary>
public class BulkApprovalProcessedEventArgs : EventArgs
{
    public BulkApprovalRequest Request { get; set; } = new BulkApprovalRequest();
    public int ProcessedCount { get; set; }
}

#endregion

/// <summary>
/// Service connection status
/// </summary>
public enum ServiceStatus
{
    NotAvailable,
    Disconnected,
    Reconnecting,
    Connected,
    Stopped
}

/// <summary>
/// Approval statistics DTO
/// </summary>
public class ApprovalStatistics
{
    public int TotalPending { get; set; }
    public int TotalApproved { get; set; }
    public int TotalRejected { get; set; }
    public int TotalAutoApproved { get; set; }
    public int TotalExpired { get; set; }
    public int TotalImported { get; set; }
    public int TotalFailed { get; set; }
    public DateTime? OldestPending { get; set; }
    public DateTime? NewestPending { get; set; }
    public long TotalSizePending { get; set; }
    public long TotalSizeApproved { get; set; }

    // Aliases for XAML compatibility
    public int PendingCount => TotalPending;
    public int ApprovedCount => TotalApproved;
    public int RejectedCount => TotalRejected;
    public long TotalSize => TotalSizePending + TotalSizeApproved;

    public string TotalSizePendingFormatted => FormatFileSize(TotalSizePending);
    public string TotalSizeApprovedFormatted => FormatFileSize(TotalSizeApproved);

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

# Service Logging Implementation Guide

This guide shows how to add comprehensive Splunk logging to Services in the AlUmmahNow Visual Importer application, with a focus on the FileScanSignalRClient.

## Overview

Services should log:
- Connection/disconnection events
- API calls (start, success, failure with timing)
- SignalR events
- Errors and exceptions with full context

## FileScanSignalRClient Logging Implementation

### Step 1: Add Logger to Class

```csharp
using Microsoft.Extensions.Logging;
using AlUmmahNow.VisualImporterUno.Services.Logging;

public class FileScanSignalRClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly string _hubUrl;
    private readonly ILogger<FileScanSignalRClient> _logger;
    private bool _isConnected = false;

    // Events...

    public FileScanSignalRClient(string serviceUrl, ILogger<FileScanSignalRClient> logger)
    {
        _hubUrl = $"{serviceUrl.TrimEnd('/')}/scanHub";
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("FileScanSignalRClient initializing with URL: {HubUrl}",
            _hubUrl);

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect(new[] {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10)
            })
            .Build();

        SetupEventHandlers();

        _logger.LogInformation("FileScanSignalRClient initialized successfully");
    }
}
```

### Step 2: Log Connection Events

```csharp
private void SetupConnectionEventHandlers()
{
    _connection.Reconnecting += error =>
    {
        _isConnected = false;

        _logger.LogWarning(error,
            "SignalR connection lost. Reconnecting... Attempt will continue automatically");

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

        _logger.LogInformation(
            "SignalR reconnected successfully. Connection ID: {ConnectionId}",
            connectionId);

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
            _logger.LogError(error,
                "SignalR connection closed with error");

            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs
            {
                Status = ServiceStatus.Disconnected,
                Message = $"Connection closed: {error.Message}"
            });

            ErrorOccurred?.Invoke(this, $"Connection closed: {error.Message}");
        }
        else
        {
            _logger.LogInformation("SignalR connection closed normally");

            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs
            {
                Status = ServiceStatus.Stopped,
                Message = "Service stopped"
            });
        }

        return Task.CompletedTask;
    };
}
```

### Step 3: Log API Calls with Timing

```csharp
public async Task<bool> ConnectAsync()
{
    var sw = Stopwatch.StartNew();

    try
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            _isConnected = true;
            _logger.LogDebug("Already connected to FileScan service");
            return true;
        }

        _logger.LogInformation("Connecting to FileScan service at {HubUrl}", _hubUrl);

        await _connection.StartAsync();

        _isConnected = true;
        sw.Stop();

        _logger.LogInformation(
            "Connected to FileScan service successfully in {Duration}ms. Connection ID: {ConnectionId}",
            sw.ElapsedMilliseconds,
            _connection.ConnectionId);

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
        sw.Stop();

        _logger.LogServiceCallException(ex,
            "FileScanService",
            "Connect",
            new Dictionary<string, object>
            {
                ["HubUrl"] = _hubUrl,
                ["DurationMs"] = sw.ElapsedMilliseconds,
                ["ConnectionState"] = _connection.State.ToString()
            });

        ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs
        {
            Status = ServiceStatus.NotAvailable,
            Message = $"Service not available: {ex.Message}"
        });

        ErrorOccurred?.Invoke(this, $"Failed to connect: {ex.Message}");

        return false;
    }
}
```

### Step 4: Log Watch Operations

```csharp
public async Task<Guid> StartWatchAsync(CreateWatchRequest request)
{
    var sw = Stopwatch.StartNew();

    try
    {
        if (!IsConnected)
        {
            _logger.LogWarning(
                "Cannot start watch for {ChannelName}: Not connected to service",
                request.ChannelName);

            throw new InvalidOperationException("Not connected to FileScan service");
        }

        _logger.LogInformation(
            "Starting watch for channel {ChannelName} at {WatchPath}",
            request.ChannelName,
            ExceptionLoggingExtensions.SanitizeFilePath(request.WatchPath));

        var watchId = await _connection.InvokeAsync<Guid>("StartWatch", request);

        sw.Stop();

        _logger.LogInformation(
            "Watch started successfully: {ChannelName} (ID: {WatchId}) in {Duration}ms",
            request.ChannelName, watchId, sw.ElapsedMilliseconds);

        return watchId;
    }
    catch (Exception ex)
    {
        sw.Stop();

        _logger.LogServiceCallException(ex,
            "FileScanService",
            "StartWatch",
            new Dictionary<string, object>
            {
                ["ChannelName"] = request.ChannelName,
                ["WatchPath"] = ExceptionLoggingExtensions.SanitizeFilePath(request.WatchPath),
                ["IncludeSubdirectories"] = request.IncludeSubdirectories,
                ["DurationMs"] = sw.ElapsedMilliseconds
            });

        ErrorOccurred?.Invoke(this, $"Failed to start watch: {ex.Message}");

        throw new InvalidOperationException($"Failed to start watch: {ex.Message}", ex);
    }
}

public async Task<bool> StopWatchAsync(Guid watchId)
{
    var sw = Stopwatch.StartNew();

    try
    {
        if (!IsConnected)
        {
            _logger.LogWarning(
                "Cannot stop watch {WatchId}: Not connected to service",
                watchId);

            throw new InvalidOperationException("Not connected to FileScan service");
        }

        _logger.LogInformation("Stopping watch: {WatchId}", watchId);

        var result = await _connection.InvokeAsync<bool>("StopWatch", watchId);

        sw.Stop();

        _logger.LogInformation(
            "Watch stop {Result}: {WatchId} in {Duration}ms",
            result ? "succeeded" : "failed", watchId, sw.ElapsedMilliseconds);

        return result;
    }
    catch (Exception ex)
    {
        sw.Stop();

        _logger.LogServiceCallException(ex,
            "FileScanService",
            "StopWatch",
            new Dictionary<string, object>
            {
                ["WatchId"] = watchId,
                ["DurationMs"] = sw.ElapsedMilliseconds
            });

        ErrorOccurred?.Invoke(this, $"Failed to stop watch: {ex.Message}");

        throw new InvalidOperationException($"Failed to stop watch: {ex.Message}", ex);
    }
}

public async Task<List<WatchStatusDto>> GetActiveWatchesAsync()
{
    var sw = Stopwatch.StartNew();

    try
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot get active watches: Not connected to service");
            throw new InvalidOperationException("Not connected to FileScan service");
        }

        _logger.LogDebug("Retrieving active watches");

        var watches = await _connection.InvokeAsync<List<WatchStatusDto>>("GetActiveWatches");

        sw.Stop();

        _logger.LogInformation(
            "Retrieved {WatchCount} active watches in {Duration}ms",
            watches.Count, sw.ElapsedMilliseconds);

        return watches;
    }
    catch (Exception ex)
    {
        sw.Stop();

        _logger.LogServiceCallException(ex,
            "FileScanService",
            "GetActiveWatches",
            new Dictionary<string, object>
            {
                ["DurationMs"] = sw.ElapsedMilliseconds
            });

        ErrorOccurred?.Invoke(this, $"Failed to get active watches: {ex.Message}");

        throw new InvalidOperationException($"Failed to get active watches: {ex.Message}", ex);
    }
}
```

### Step 5: Log SignalR Event Handlers

```csharp
private void SetupWatchEventHandlers()
{
    _connection.On<Guid, string, string, string, long>("FileDetected",
        (watchId, channelName, fileName, filePath, fileSize) =>
    {
        _logger.LogInformation(
            "SignalR event - File detected: {FileName} ({FileSize} bytes) in watch {WatchId} (channel: {ChannelName})",
            fileName, fileSize, watchId, channelName);

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
        _logger.LogInformation(
            "SignalR event - Watch status changed: {WatchId} (channel: {ChannelName}) - Active: {IsActive}, Message: {StatusMessage}",
            watchId, channelName, isActive, statusMessage);

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
        _logger.LogInformation("SignalR event - Watch stopped: {WatchId}", watchId);

        WatchStopped?.Invoke(this, watchId);
    });

    _connection.On<Guid>("WatchDeleted", (watchId) =>
    {
        _logger.LogInformation("SignalR event - Watch deleted: {WatchId}", watchId);

        WatchDeleted?.Invoke(this, watchId);
    });
}
```

### Step 6: Log Approval Operations

```csharp
public async Task<List<PendingApprovalDto>> GetPendingApprovalsAsync()
{
    var sw = Stopwatch.StartNew();

    try
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot get pending approvals: Not connected to service");
            throw new InvalidOperationException("Not connected to FileScan service");
        }

        _logger.LogDebug("Retrieving pending approvals");

        var approvals = await _connection.InvokeAsync<List<PendingApprovalDto>>("GetPendingApprovals");

        sw.Stop();

        _logger.LogInformation(
            "Retrieved {ApprovalCount} pending approvals in {Duration}ms",
            approvals.Count, sw.ElapsedMilliseconds);

        return approvals;
    }
    catch (Exception ex)
    {
        sw.Stop();

        _logger.LogServiceCallException(ex,
            "FileScanService",
            "GetPendingApprovals",
            new Dictionary<string, object>
            {
                ["DurationMs"] = sw.ElapsedMilliseconds
            });

        ErrorOccurred?.Invoke(this, $"Failed to get pending approvals: {ex.Message}");

        throw new InvalidOperationException($"Failed to get pending approvals: {ex.Message}", ex);
    }
}

public async Task<bool> ProcessApprovalDecisionAsync(ApprovalDecisionRequest request)
{
    var sw = Stopwatch.StartNew();

    try
    {
        if (!IsConnected)
        {
            _logger.LogWarning(
                "Cannot process approval decision: Not connected to service");

            throw new InvalidOperationException("Not connected to FileScan service");
        }

        _logger.LogInformation(
            "Processing approval decision: {Decision} for {Count} file(s) by {ApprovedBy}",
            request.Decision, request.ApprovalIds.Count, request.ApprovedBy);

        var result = await _connection.InvokeAsync<bool>("ProcessApprovalDecision", request);

        sw.Stop();

        _logger.LogInformation(
            "Approval decision processed {Result}: {Decision} for {Count} file(s) in {Duration}ms",
            result ? "successfully" : "with failure",
            request.Decision,
            request.ApprovalIds.Count,
            sw.ElapsedMilliseconds);

        return result;
    }
    catch (Exception ex)
    {
        sw.Stop();

        _logger.LogServiceCallException(ex,
            "FileScanService",
            "ProcessApprovalDecision",
            new Dictionary<string, object>
            {
                ["Decision"] = request.Decision.ToString(),
                ["ApprovalCount"] = request.ApprovalIds.Count,
                ["ApprovedBy"] = request.ApprovedBy,
                ["DurationMs"] = sw.ElapsedMilliseconds
            });

        ErrorOccurred?.Invoke(this, $"Failed to process approval decision: {ex.Message}");

        throw new InvalidOperationException($"Failed to process approval decision: {ex.Message}", ex);
    }
}
```

### Step 7: Log Disposal

```csharp
public async ValueTask DisposeAsync()
{
    _logger.LogInformation("Disposing FileScanSignalRClient");

    try
    {
        await DisconnectAsync();
        await _connection.DisposeAsync();

        _logger.LogInformation("FileScanSignalRClient disposed successfully");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during FileScanSignalRClient disposal");
    }
}
```

## Splunk Queries for Service Monitoring

```spl
# SignalR connection health
index="alummahnow_logs" source="AlUmmahNow.VisualImporter" "SignalR"
| stats count by Status, Message
| sort -count

# API call performance
index="alummahnow_logs" ServiceName="FileScanService"
| stats avg(DurationMs) as avg_ms, max(DurationMs) as max_ms, count by ServiceEndpoint
| sort -avg_ms

# API call failures
index="alummahnow_logs" ServiceName="FileScanService" EventType="ServiceCallError"
| table _time, ServiceEndpoint, ErrorType, ErrorMessage
| sort -_time

# SignalR reconnection events
index="alummahnow_logs" "SignalR" (Status="Reconnecting" OR Status="Reconnected")
| timechart span=5m count by Status

# Watch operations volume
index="alummahnow_logs" ServiceEndpoint="StartWatch" OR ServiceEndpoint="StopWatch"
| timechart span=1h count by ServiceEndpoint

# Approval processing metrics
index="alummahnow_logs" ServiceEndpoint="ProcessApprovalDecision"
| stats count, avg(DurationMs) as avg_duration_ms by Decision
| eval avg_duration_sec=round(avg_duration_ms/1000, 2)

# Failed operations by endpoint
index="alummahnow_logs" EventType="ServiceCallError"
| stats count by ServiceEndpoint, ErrorType
| sort -count
```

## Key Service Logging Principles

1. **Connection Events**: Log all connection state changes
2. **API Timing**: Use Stopwatch for all async operations
3. **Request/Response**: Log key parameters (sanitize sensitive data)
4. **SignalR Events**: Log all incoming events with context
5. **Errors**: Use `LogServiceCallException` for API failures
6. **Disposal**: Log cleanup operations
7. **Debug vs Info**: Use Debug for frequent ops, Info for significant events

## Other Services to Log

Apply similar patterns to:
- **ImportService**: Log import operations with file counts and sizes
- **VideoThumbnailService**: Log thumbnail generation with timing
- **NotificationService**: Log notification delivery
- **FileScanServiceManager**: Log service lifecycle (start, stop, status checks)

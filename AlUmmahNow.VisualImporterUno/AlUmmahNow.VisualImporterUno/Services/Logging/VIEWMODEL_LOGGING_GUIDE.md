# ViewModel Logging Implementation Guide

This guide shows how to add comprehensive Splunk logging to ViewModels in the AlUmmahNow Visual Importer application.

## Overview

ViewModels should use constructor injection to receive either `ILogger<T>` or `ILoggingService` and log key user actions, state changes, and errors.

## Step 1: Add Logger to Constructor

```csharp
using Microsoft.Extensions.Logging;
using AlUmmahNow.VisualImporterUno.Services.Logging;
using AlUmmahNow.VisualImporterUno.Models;

public partial class WatchViewModel : ObservableObject, IDisposable
{
    private readonly FileScanSignalRClient _signalRClient;
    private readonly INotificationService _notificationService;
    private readonly ILogger<WatchViewModel> _logger;
    private readonly ILoggingService _loggingService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _disposed = false;

    public WatchViewModel(
        FileScanSignalRClient signalRClient,
        INotificationService notificationService,
        ILogger<WatchViewModel> logger,
        ILoggingService loggingService)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

        // Log ViewModel initialization
        _logger.LogInformation("WatchViewModel initialized");

        // ... rest of constructor
    }
}
```

## Step 2: Log User Actions

Log all user-triggered commands with context:

```csharp
[RelayCommand(CanExecute = nameof(CanCreateWatch))]
private async Task CreateWatchAsync()
{
    var sw = Stopwatch.StartNew();

    try
    {
        IsCreatingWatch = true;
        CreateWatchError = string.Empty;

        _logger.LogInformation("Creating watch: {ChannelName} at {WatchPath}",
            NewWatchChannelName, NewWatchPath);

        if (!_signalRClient.IsConnected)
        {
            _logger.LogWarning("Watch creation aborted: SignalR not connected");
            CreateWatchError = "Not connected to FileScan service";
            return;
        }

        if (!ValidateCreateWatchInputs())
        {
            _logger.LogWarning("Watch creation aborted: Validation failed for {ChannelName}",
                NewWatchChannelName);
            return;
        }

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

        sw.Stop();

        // Log successful creation with structured data
        _loggingService.LogUserAction(
            "WatchManagement",
            "CreateWatch",
            new Dictionary<string, object>
            {
                ["WatchId"] = watchId,
                ["ChannelName"] = request.ChannelName,
                ["WatchPath"] = ExceptionLoggingExtensions.SanitizeFilePath(request.WatchPath),
                ["IncludeSubdirectories"] = request.IncludeSubdirectories,
                ["ProcessingDelayMs"] = request.ProcessingDelayMs,
                ["DurationMs"] = sw.ElapsedMilliseconds,
                ["Status"] = "Success"
            });

        _logger.LogInformation(
            "Watch created successfully: {ChannelName} (ID: {WatchId}) in {Duration}ms",
            request.ChannelName, watchId, sw.ElapsedMilliseconds);

        StatusMessage = $"Watch created for channel '{request.ChannelName}' (ID: {watchId})";
        IsCreateDialogOpen = false;
        await RefreshWatchesAsync();
    }
    catch (Exception ex)
    {
        sw.Stop();

        // Log exception with structured data
        _logger.LogStructuredException(ex,
            "Failed to create watch: {ChannelName}",
            NewWatchChannelName);

        _loggingService.LogUserAction(
            "WatchManagement",
            "CreateWatch",
            new Dictionary<string, object>
            {
                ["ChannelName"] = NewWatchChannelName,
                ["WatchPath"] = ExceptionLoggingExtensions.SanitizeFilePath(NewWatchPath),
                ["DurationMs"] = sw.ElapsedMilliseconds,
                ["Status"] = "Failed",
                ["ErrorMessage"] = ex.Message,
                ["ExceptionType"] = ex.GetType().Name
            });

        CreateWatchError = $"Failed to create watch: {ex.Message}";
        StatusMessage = $"Failed to create watch: {ex.Message}";
    }
    finally
    {
        IsCreatingWatch = false;
    }
}
```

## Step 3: Log Approval Operations

```csharp
[RelayCommand(CanExecute = nameof(HasSelectedApproval))]
private async Task ApproveSelectedAsync()
{
    if (SelectedApproval == null) return;

    var sw = Stopwatch.StartNew();
    var fileName = SelectedApproval.FileName;
    var approvalId = SelectedApproval.Id;
    var channelName = SelectedApproval.ChannelName;

    try
    {
        IsLoading = true;

        _logger.LogInformation(
            "Approving file: {FileName} (Approval ID: {ApprovalId}) in channel {ChannelName}",
            fileName, approvalId, channelName);

        var request = new ApprovalDecisionRequest
        {
            ApprovalIds = new List<Guid> { approvalId },
            Decision = ApprovalStatus.Approved,
            ApprovedBy = Environment.UserName
        };

        var result = await _signalRClient.ProcessApprovalDecisionAsync(request);
        sw.Stop();

        if (result)
        {
            // Log successful approval
            _loggingService.LogFileOperation(
                "Approval",
                fileName,
                "Approved",
                SelectedApproval.FileSize,
                new Dictionary<string, object>
                {
                    ["ApprovalId"] = approvalId,
                    ["ChannelName"] = channelName,
                    ["ApprovedBy"] = Environment.UserName,
                    ["DurationMs"] = sw.ElapsedMilliseconds
                });

            _logger.LogInformation(
                "File approved successfully: {FileName} in {Duration}ms",
                fileName, sw.ElapsedMilliseconds);

            StatusMessage = $"File approved: {fileName}";

            if (EnableNotifications)
            {
                await _notificationService.ShowApprovalProcessedAsync(fileName, approved: true, count: 1);
            }

            await RefreshPendingApprovalsAsync();
        }
        else
        {
            _logger.LogWarning(
                "File approval failed: {FileName} (result was false)",
                fileName);

            StatusMessage = $"Failed to approve file: {fileName}";
        }
    }
    catch (Exception ex)
    {
        sw.Stop();

        _logger.LogStructuredException(ex,
            "Failed to approve file: {FileName}",
            fileName);

        _loggingService.LogFileOperation(
            "Approval",
            fileName,
            "Failed",
            SelectedApproval?.FileSize,
            new Dictionary<string, object>
            {
                ["ApprovalId"] = approvalId,
                ["ChannelName"] = channelName,
                ["DurationMs"] = sw.ElapsedMilliseconds,
                ["ErrorMessage"] = ex.Message,
                ["ExceptionType"] = ex.GetType().Name
            });

        StatusMessage = $"Failed to approve file: {ex.Message}";
    }
    finally
    {
        IsLoading = false;
    }
}
```

## Step 4: Log SignalR Events

```csharp
private void OnFileDetected(object? sender, FileDetectedEventArgs e)
{
    DispatcherExecute(() =>
    {
        _logger.LogInformation(
            "File detected in watch {WatchId}: {FileName} ({FileSize} bytes) in channel {ChannelName}",
            e.WatchId, e.FileName, e.FileSize, e.ChannelName);

        _loggingService.LogSignalREvent("FileDetected", new Dictionary<string, object>
        {
            ["WatchId"] = e.WatchId,
            ["ChannelName"] = e.ChannelName,
            ["FileName"] = e.FileName,
            ["FilePath"] = ExceptionLoggingExtensions.SanitizeFilePath(e.FilePath),
            ["FileSize"] = e.FileSize
        });

        StatusMessage = $"File detected: {e.FileName} in {e.ChannelName}";

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
                    _logger.LogWarning(ex, "Failed to show file detected notification for {FileName}", e.FileName);
                }
            });
        }

        // Refresh pending approvals to show the new file
        _ = Task.Run(async () => await RefreshPendingApprovalsAsync());
    });
}

private void OnApprovalStatusChanged(object? sender, ApprovalStatusChangedEventArgs e)
{
    DispatcherExecute(() =>
    {
        _logger.LogInformation(
            "Approval status changed: {FileName} from {OldStatus} to {NewStatus} (approved by {ApprovedBy})",
            e.FileName, e.OldStatus, e.NewStatus, e.ApprovedBy);

        _loggingService.LogSignalREvent("ApprovalStatusChanged", new Dictionary<string, object>
        {
            ["ApprovalId"] = e.ApprovalId,
            ["ChannelName"] = e.ChannelName,
            ["FileName"] = e.FileName,
            ["OldStatus"] = e.OldStatus.ToString(),
            ["NewStatus"] = e.NewStatus.ToString(),
            ["ApprovedBy"] = e.ApprovedBy
        });

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
```

## Step 5: Log Validation Errors

```csharp
private void ValidateChannelName(string channelName)
{
    if (string.IsNullOrWhiteSpace(channelName))
    {
        IsChannelNameValid = false;
        ChannelNameValidationMessage = "Channel name is required";

        _logger.LogValidationError("ChannelName", "Channel name is required", channelName);
    }
    else if (channelName.Length < 2)
    {
        IsChannelNameValid = false;
        ChannelNameValidationMessage = "Channel name must be at least 2 characters";

        _logger.LogValidationError("ChannelName", "Channel name too short", channelName);
    }
    else if (channelName.Length > 100)
    {
        IsChannelNameValid = false;
        ChannelNameValidationMessage = "Channel name must not exceed 100 characters";

        _logger.LogValidationError("ChannelName", "Channel name too long", channelName);
    }
    else if (ActiveWatches.Any(w => w.ChannelName.Equals(channelName.Trim(), StringComparison.OrdinalIgnoreCase)))
    {
        IsChannelNameValid = false;
        ChannelNameValidationMessage = "A watch for this channel already exists";

        _logger.LogValidationError("ChannelName", "Duplicate channel name", channelName);
    }
    else
    {
        IsChannelNameValid = true;
        ChannelNameValidationMessage = string.Empty;
    }
}
```

## Step 6: Log Performance Metrics

```csharp
[RelayCommand]
private async Task RefreshWatchesAsync()
{
    using var timedOp = _loggingService.BeginTimedOperation("RefreshWatches");

    try
    {
        IsLoading = true;
        StatusMessage = "Refreshing watches...";

        if (!_signalRClient.IsConnected)
        {
            _logger.LogWarning("Cannot refresh watches: SignalR not connected");
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

        _logger.LogInformation("Refreshed {WatchCount} watches", watches.Count);
        StatusMessage = $"Loaded {watches.Count} active watches";
    }
    catch (Exception ex)
    {
        _logger.LogStructuredException(ex, "Failed to refresh watches");
        StatusMessage = $"Failed to refresh watches: {ex.Message}";
    }
    finally
    {
        IsLoading = false;
    }
    // timedOp.Dispose() is called automatically here, logging the operation duration
}
```

## Splunk Queries for Watch Management

```spl
# All watch creation events
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch"
| stats count by ChannelName, Status

# Watch creation failures
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch" Status="Failed"
| table _time, ChannelName, ErrorMessage, ExceptionType

# Average watch creation time
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch" Status="Success"
| stats avg(DurationMs) as avg_ms, max(DurationMs) as max_ms, min(DurationMs) as min_ms

# Approval workflow metrics
index="alummahnow_logs" FileOperation="Approval"
| stats count by OperationStatus, ChannelName

# File detection volume by channel
index="alummahnow_logs" SignalREvent="FileDetected"
| timechart span=1h count by ChannelName

# Validation errors by field
index="alummahnow_logs" EventType="ValidationError"
| stats count by ValidationField, ValidationMessage
```

## Key Logging Principles

1. **User Actions**: Log every user-initiated action with context
2. **State Changes**: Log important state transitions
3. **Performance**: Use `BeginTimedOperation` for async operations
4. **Errors**: Always log exceptions with `LogStructuredException`
5. **Validation**: Log validation failures for debugging
6. **SignalR Events**: Log real-time events for troubleshooting
7. **Sensitive Data**: Sanitize file paths and never log credentials
8. **Structured Data**: Use Dictionary for rich context

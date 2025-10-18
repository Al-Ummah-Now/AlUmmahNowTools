using System;
using System.Collections.Generic;

namespace AlUmmahNow.VisualImporterUno.Models;

/// <summary>
/// Structured context data for logging operations
/// This class provides a consistent structure for log enrichment across the application
/// </summary>
public class LogContext
{
    /// <summary>
    /// User identifier (username or user ID)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Session identifier for correlating related operations
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Correlation ID for tracking operations across services
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Feature name (e.g., "WatchManagement", "ApprovalWorkflow", "FileImport")
    /// </summary>
    public string? FeatureName { get; set; }

    /// <summary>
    /// Action name within the feature (e.g., "CreateWatch", "ApproveFile")
    /// </summary>
    public string? ActionName { get; set; }

    /// <summary>
    /// Entity ID (e.g., WatchId, ApprovalId, FileId)
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Entity type (e.g., "Watch", "Approval", "File")
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Operation duration in milliseconds
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Operation status (e.g., "Success", "Failed", "Cancelled")
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Channel name (for watch/approval operations)
    /// </summary>
    public string? ChannelName { get; set; }

    /// <summary>
    /// File path (sanitized to not expose sensitive directory structure)
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long? FileSizeBytes { get; set; }

    /// <summary>
    /// Item count (e.g., number of files processed, watches created)
    /// </summary>
    public int? ItemCount { get; set; }

    /// <summary>
    /// Data size in bytes (for batch operations)
    /// </summary>
    public long? DataSizeBytes { get; set; }

    /// <summary>
    /// Error message (if operation failed)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Exception type (if error occurred)
    /// </summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Additional custom properties
    /// </summary>
    public Dictionary<string, object> AdditionalProperties { get; set; } = new();

    /// <summary>
    /// Timestamp when the context was created
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Convert LogContext to dictionary for logging scope
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(UserId))
            dict["UserId"] = UserId;

        if (!string.IsNullOrEmpty(SessionId))
            dict["SessionId"] = SessionId;

        if (!string.IsNullOrEmpty(CorrelationId))
            dict["CorrelationId"] = CorrelationId;

        if (!string.IsNullOrEmpty(FeatureName))
            dict["FeatureName"] = FeatureName;

        if (!string.IsNullOrEmpty(ActionName))
            dict["ActionName"] = ActionName;

        if (!string.IsNullOrEmpty(EntityId))
            dict["EntityId"] = EntityId;

        if (!string.IsNullOrEmpty(EntityType))
            dict["EntityType"] = EntityType;

        if (DurationMs.HasValue)
            dict["DurationMs"] = DurationMs.Value;

        if (!string.IsNullOrEmpty(Status))
            dict["Status"] = Status;

        if (!string.IsNullOrEmpty(ChannelName))
            dict["ChannelName"] = ChannelName;

        if (!string.IsNullOrEmpty(FilePath))
            dict["FilePath"] = FilePath;

        if (FileSizeBytes.HasValue)
        {
            dict["FileSizeBytes"] = FileSizeBytes.Value;
            dict["FileSizeMB"] = Math.Round(FileSizeBytes.Value / 1024.0 / 1024.0, 2);
        }

        if (ItemCount.HasValue)
            dict["ItemCount"] = ItemCount.Value;

        if (DataSizeBytes.HasValue)
        {
            dict["DataSizeBytes"] = DataSizeBytes.Value;
            dict["DataSizeMB"] = Math.Round(DataSizeBytes.Value / 1024.0 / 1024.0, 2);
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
            dict["ErrorMessage"] = ErrorMessage;

        if (!string.IsNullOrEmpty(ExceptionType))
            dict["ExceptionType"] = ExceptionType;

        dict["Timestamp"] = Timestamp;

        // Add additional properties
        foreach (var kvp in AdditionalProperties)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return dict;
    }

    /// <summary>
    /// Create a LogContext for a user action
    /// </summary>
    public static LogContext ForUserAction(string featureName, string actionName, string? userId = null)
    {
        return new LogContext
        {
            UserId = userId ?? Environment.UserName,
            FeatureName = featureName,
            ActionName = actionName,
            SessionId = Guid.NewGuid().ToString("N")
        };
    }

    /// <summary>
    /// Create a LogContext for a file operation
    /// </summary>
    public static LogContext ForFileOperation(
        string operation,
        string fileName,
        long? fileSizeBytes = null,
        string? channelName = null)
    {
        return new LogContext
        {
            UserId = Environment.UserName,
            FeatureName = "FileOperations",
            ActionName = operation,
            FilePath = fileName,
            FileSizeBytes = fileSizeBytes,
            ChannelName = channelName,
            SessionId = Guid.NewGuid().ToString("N")
        };
    }

    /// <summary>
    /// Create a LogContext for a watch operation
    /// </summary>
    public static LogContext ForWatchOperation(
        string actionName,
        string channelName,
        Guid? watchId = null)
    {
        return new LogContext
        {
            UserId = Environment.UserName,
            FeatureName = "WatchManagement",
            ActionName = actionName,
            ChannelName = channelName,
            EntityId = watchId?.ToString(),
            EntityType = "Watch",
            SessionId = Guid.NewGuid().ToString("N")
        };
    }

    /// <summary>
    /// Create a LogContext for an approval operation
    /// </summary>
    public static LogContext ForApprovalOperation(
        string actionName,
        string fileName,
        Guid? approvalId = null,
        string? channelName = null)
    {
        return new LogContext
        {
            UserId = Environment.UserName,
            FeatureName = "ApprovalWorkflow",
            ActionName = actionName,
            FilePath = fileName,
            EntityId = approvalId?.ToString(),
            EntityType = "Approval",
            ChannelName = channelName,
            SessionId = Guid.NewGuid().ToString("N")
        };
    }

    /// <summary>
    /// Create a LogContext for a SignalR event
    /// </summary>
    public static LogContext ForSignalREvent(string eventName, Dictionary<string, object>? properties = null)
    {
        var context = new LogContext
        {
            UserId = Environment.UserName,
            FeatureName = "SignalR",
            ActionName = eventName,
            SessionId = Guid.NewGuid().ToString("N")
        };

        if (properties != null)
        {
            context.AdditionalProperties = properties;
        }

        return context;
    }

    /// <summary>
    /// Create a LogContext for a performance metric
    /// </summary>
    public static LogContext ForPerformanceMetric(
        string operationName,
        long durationMs,
        int? itemCount = null,
        long? dataSizeBytes = null)
    {
        return new LogContext
        {
            UserId = Environment.UserName,
            FeatureName = "Performance",
            ActionName = operationName,
            DurationMs = durationMs,
            ItemCount = itemCount,
            DataSizeBytes = dataSizeBytes,
            Status = "Measured",
            SessionId = Guid.NewGuid().ToString("N")
        };
    }
}

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AlUmmahNow.VisualImporterUno.Services.Logging;

/// <summary>
/// High-level logging service wrapper for easy use from ViewModels and Services
/// Provides structured logging with context enrichment
/// </summary>
public interface ILoggingService
{
    /// <summary>
    /// Log an informational message
    /// </summary>
    void LogInformation(string message, params object[] args);

    /// <summary>
    /// Log a warning message
    /// </summary>
    void LogWarning(string message, params object[] args);

    /// <summary>
    /// Log an error message
    /// </summary>
    void LogError(string message, params object[] args);

    /// <summary>
    /// Log an error with exception
    /// </summary>
    void LogError(Exception exception, string message, params object[] args);

    /// <summary>
    /// Log a debug message
    /// </summary>
    void LogDebug(string message, params object[] args);

    /// <summary>
    /// Log a user action for analytics
    /// Splunk Query: index="alummahnow_logs" ActionName="*" | stats count by ActionName, FeatureName
    /// </summary>
    void LogUserAction(string featureName, string actionName, Dictionary<string, object>? properties = null);

    /// <summary>
    /// Log a performance metric
    /// Splunk Query: index="alummahnow_logs" MetricName="*" | stats avg(MetricValue) by MetricName
    /// </summary>
    void LogPerformanceMetric(string metricName, long durationMs, Dictionary<string, object>? properties = null);

    /// <summary>
    /// Log a file operation (scan, import, approval, etc.)
    /// Splunk Query: index="alummahnow_logs" FileOperation="*" | stats count by FileOperation, OperationStatus
    /// </summary>
    void LogFileOperation(string operation, string fileName, string status, long? fileSizeBytes = null, Dictionary<string, object>? properties = null);

    /// <summary>
    /// Log a SignalR event
    /// Splunk Query: index="alummahnow_logs" SignalREvent="*" | stats count by SignalREvent
    /// </summary>
    void LogSignalREvent(string eventName, Dictionary<string, object>? properties = null);

    /// <summary>
    /// Create a timed operation scope
    /// </summary>
    IDisposable BeginTimedOperation(string operationName, Dictionary<string, object>? properties = null);

    /// <summary>
    /// Create a scoped logger with additional context
    /// </summary>
    ILoggingService WithContext(string key, object value);
}

/// <summary>
/// Implementation of ILoggingService using Microsoft.Extensions.Logging
/// </summary>
public class LoggingService : ILoggingService
{
    private readonly ILogger<LoggingService> _logger;
    private readonly Dictionary<string, object> _contextProperties;
    private readonly string _userId;
    private readonly string _sessionId;

    public LoggingService(ILogger<LoggingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextProperties = new Dictionary<string, object>();
        _userId = Environment.UserName;
        _sessionId = Guid.NewGuid().ToString("N");
    }

    private LoggingService(
        ILogger<LoggingService> logger,
        Dictionary<string, object> contextProperties,
        string userId,
        string sessionId)
    {
        _logger = logger;
        _contextProperties = new Dictionary<string, object>(contextProperties);
        _userId = userId;
        _sessionId = sessionId;
    }

    public void LogInformation(string message, params object[] args)
    {
        using var scope = CreateScope();
        _logger.LogInformation(message, args);
    }

    public void LogWarning(string message, params object[] args)
    {
        using var scope = CreateScope();
        _logger.LogWarning(message, args);
    }

    public void LogError(string message, params object[] args)
    {
        using var scope = CreateScope();
        _logger.LogError(message, args);
    }

    public void LogError(Exception exception, string message, params object[] args)
    {
        using var scope = CreateScope();
        _logger.LogError(exception, message, args);
    }

    public void LogDebug(string message, params object[] args)
    {
        using var scope = CreateScope();
        _logger.LogDebug(message, args);
    }

    public void LogUserAction(string featureName, string actionName, Dictionary<string, object>? properties = null)
    {
        var logProperties = new Dictionary<string, object>(_contextProperties)
        {
            ["EventType"] = "UserAction",
            ["FeatureName"] = featureName,
            ["ActionName"] = actionName,
            ["UserId"] = _userId,
            ["SessionId"] = _sessionId,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                logProperties[kvp.Key] = kvp.Value;
            }
        }

        using var scope = _logger.BeginScope(logProperties);
        _logger.LogInformation("User action: {FeatureName}.{ActionName}", featureName, actionName);
    }

    public void LogPerformanceMetric(string metricName, long durationMs, Dictionary<string, object>? properties = null)
    {
        var logProperties = new Dictionary<string, object>(_contextProperties)
        {
            ["EventType"] = "PerformanceMetric",
            ["MetricName"] = metricName,
            ["MetricValue"] = durationMs,
            ["MetricUnit"] = "milliseconds",
            ["UserId"] = _userId,
            ["SessionId"] = _sessionId,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                logProperties[kvp.Key] = kvp.Value;
            }
        }

        using var scope = _logger.BeginScope(logProperties);
        _logger.LogInformation("Performance metric: {MetricName} = {DurationMs}ms", metricName, durationMs);
    }

    public void LogFileOperation(
        string operation,
        string fileName,
        string status,
        long? fileSizeBytes = null,
        Dictionary<string, object>? properties = null)
    {
        var logProperties = new Dictionary<string, object>(_contextProperties)
        {
            ["EventType"] = "FileOperation",
            ["FileOperation"] = operation,
            ["FileName"] = fileName,
            ["OperationStatus"] = status,
            ["UserId"] = _userId,
            ["SessionId"] = _sessionId,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (fileSizeBytes.HasValue)
        {
            logProperties["FileSizeBytes"] = fileSizeBytes.Value;
            logProperties["FileSizeMB"] = Math.Round(fileSizeBytes.Value / 1024.0 / 1024.0, 2);
        }

        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                logProperties[kvp.Key] = kvp.Value;
            }
        }

        using var scope = _logger.BeginScope(logProperties);
        _logger.LogInformation("File operation: {Operation} on {FileName} - {Status}",
            operation, fileName, status);
    }

    public void LogSignalREvent(string eventName, Dictionary<string, object>? properties = null)
    {
        var logProperties = new Dictionary<string, object>(_contextProperties)
        {
            ["EventType"] = "SignalREvent",
            ["SignalREvent"] = eventName,
            ["UserId"] = _userId,
            ["SessionId"] = _sessionId,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                logProperties[kvp.Key] = kvp.Value;
            }
        }

        using var scope = _logger.BeginScope(logProperties);
        _logger.LogInformation("SignalR event: {EventName}", eventName);
    }

    public IDisposable BeginTimedOperation(string operationName, Dictionary<string, object>? properties = null)
    {
        var logProperties = new Dictionary<string, object>(_contextProperties)
        {
            ["EventType"] = "TimedOperation",
            ["OperationName"] = operationName,
            ["UserId"] = _userId,
            ["SessionId"] = _sessionId
        };

        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                logProperties[kvp.Key] = kvp.Value;
            }
        }

        return new TimedOperationScope(_logger, operationName, logProperties);
    }

    public ILoggingService WithContext(string key, object value)
    {
        var newContext = new Dictionary<string, object>(_contextProperties)
        {
            [key] = value
        };

        return new LoggingService(_logger, newContext, _userId, _sessionId);
    }

    private IDisposable CreateScope()
    {
        var scopeProperties = new Dictionary<string, object>(_contextProperties)
        {
            ["UserId"] = _userId,
            ["SessionId"] = _sessionId
        };

        return _logger.BeginScope(scopeProperties);
    }

    /// <summary>
    /// Helper class for timing operations with automatic logging
    /// </summary>
    private class TimedOperationScope : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _operationName;
        private readonly Dictionary<string, object> _properties;
        private readonly Stopwatch _stopwatch;
        private readonly IDisposable _scope;

        public TimedOperationScope(
            ILogger logger,
            string operationName,
            Dictionary<string, object> properties)
        {
            _logger = logger;
            _operationName = operationName;
            _properties = properties;
            _stopwatch = Stopwatch.StartNew();

            _scope = _logger.BeginScope(_properties);
            _logger.LogInformation("Operation started: {OperationName}", _operationName);
        }

        public void Dispose()
        {
            _stopwatch.Stop();

            _properties["DurationMs"] = _stopwatch.ElapsedMilliseconds;
            _properties["CompletedAt"] = DateTime.UtcNow;

            _logger.LogInformation(
                "Operation completed: {OperationName} in {DurationMs}ms",
                _operationName,
                _stopwatch.ElapsedMilliseconds);

            _scope.Dispose();
        }
    }
}

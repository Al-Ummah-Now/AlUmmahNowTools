using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlUmmahNow.VisualImporterUno.Services.Logging;

/// <summary>
/// Extension methods for enhanced exception logging with structured data
/// </summary>
public static class ExceptionLoggingExtensions
{
    /// <summary>
    /// Log exception with structured properties for better Splunk analysis
    /// Splunk Query: index="alummahnow_logs" ExceptionType="*" | stats count by ExceptionType, SourceContext
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="exception">Exception to log</param>
    /// <param name="message">Contextual message</param>
    /// <param name="args">Message format arguments</param>
    public static void LogStructuredException(
        this ILogger logger,
        Exception exception,
        string message,
        params object[] args)
    {
        var exceptionProperties = ExtractExceptionProperties(exception);

        using var scope = logger.BeginScope(exceptionProperties);
        logger.LogError(exception, message, args);
    }

    /// <summary>
    /// Log exception with additional context properties
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="exception">Exception to log</param>
    /// <param name="message">Contextual message</param>
    /// <param name="contextProperties">Additional context properties</param>
    public static void LogStructuredExceptionWithContext(
        this ILogger logger,
        Exception exception,
        string message,
        Dictionary<string, object> contextProperties)
    {
        var exceptionProperties = ExtractExceptionProperties(exception);

        // Merge context properties
        foreach (var kvp in contextProperties)
        {
            exceptionProperties[kvp.Key] = kvp.Value;
        }

        using var scope = logger.BeginScope(exceptionProperties);
        logger.LogError(exception, message);
    }

    /// <summary>
    /// Log critical exception that requires immediate attention
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="exception">Exception to log</param>
    /// <param name="message">Contextual message</param>
    /// <param name="args">Message format arguments</param>
    public static void LogCriticalException(
        this ILogger logger,
        Exception exception,
        string message,
        params object[] args)
    {
        var exceptionProperties = ExtractExceptionProperties(exception);
        exceptionProperties["Severity"] = "Critical";
        exceptionProperties["RequiresImmediateAttention"] = true;

        using var scope = logger.BeginScope(exceptionProperties);
        logger.LogCritical(exception, message, args);
    }

    /// <summary>
    /// Log validation exception with field details
    /// Splunk Query: index="alummahnow_logs" EventType="ValidationError" | stats count by ValidationField
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="fieldName">Field that failed validation</param>
    /// <param name="validationMessage">Validation error message</param>
    /// <param name="attemptedValue">Value that was attempted</param>
    public static void LogValidationError(
        this ILogger logger,
        string fieldName,
        string validationMessage,
        object? attemptedValue = null)
    {
        var properties = new Dictionary<string, object>
        {
            ["EventType"] = "ValidationError",
            ["ValidationField"] = fieldName,
            ["ValidationMessage"] = validationMessage,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (attemptedValue != null)
        {
            properties["AttemptedValue"] = attemptedValue.ToString() ?? "null";
        }

        using var scope = logger.BeginScope(properties);
        logger.LogWarning("Validation failed for {FieldName}: {ValidationMessage}", fieldName, validationMessage);
    }

    /// <summary>
    /// Log API/Service call exception with endpoint details
    /// Splunk Query: index="alummahnow_logs" EventType="ServiceCallError" | stats count by ServiceEndpoint, ErrorType
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="exception">Exception from service call</param>
    /// <param name="serviceName">Name of the service</param>
    /// <param name="endpoint">Endpoint that was called</param>
    /// <param name="requestData">Request data (sensitive data should be masked)</param>
    public static void LogServiceCallException(
        this ILogger logger,
        Exception exception,
        string serviceName,
        string endpoint,
        Dictionary<string, object>? requestData = null)
    {
        var properties = new Dictionary<string, object>
        {
            ["EventType"] = "ServiceCallError",
            ["ServiceName"] = serviceName,
            ["ServiceEndpoint"] = endpoint,
            ["ErrorType"] = exception.GetType().Name,
            ["ErrorMessage"] = exception.Message,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (requestData != null)
        {
            foreach (var kvp in requestData)
            {
                properties[$"Request_{kvp.Key}"] = kvp.Value;
            }
        }

        using var scope = logger.BeginScope(properties);
        logger.LogError(exception,
            "Service call failed: {ServiceName}.{Endpoint}",
            serviceName, endpoint);
    }

    /// <summary>
    /// Extract structured properties from exception
    /// </summary>
    private static Dictionary<string, object> ExtractExceptionProperties(Exception exception)
    {
        var properties = new Dictionary<string, object>
        {
            ["ExceptionType"] = exception.GetType().FullName ?? "Unknown",
            ["ExceptionMessage"] = exception.Message,
            ["StackTrace"] = exception.StackTrace ?? "No stack trace available",
            ["Source"] = exception.Source ?? "Unknown",
            ["HResult"] = exception.HResult,
            ["Timestamp"] = DateTime.UtcNow
        };

        // Add inner exception details if present
        if (exception.InnerException != null)
        {
            properties["HasInnerException"] = true;
            properties["InnerExceptionType"] = exception.InnerException.GetType().FullName ?? "Unknown";
            properties["InnerExceptionMessage"] = exception.InnerException.Message;
        }
        else
        {
            properties["HasInnerException"] = false;
        }

        // Add exception data if present
        if (exception.Data.Count > 0)
        {
            var dataDict = exception.Data.Keys
                .Cast<object>()
                .ToDictionary(
                    key => $"ExceptionData_{key}",
                    key => exception.Data[key] ?? "null");

            foreach (var kvp in dataDict)
            {
                properties[kvp.Key] = kvp.Value;
            }
        }

        // Add aggregate exception details if applicable
        if (exception is AggregateException aggEx)
        {
            properties["IsAggregateException"] = true;
            properties["InnerExceptionCount"] = aggEx.InnerExceptions.Count;
            properties["InnerExceptionTypes"] = string.Join(", ",
                aggEx.InnerExceptions.Select(e => e.GetType().Name));
        }

        return properties;
    }

    /// <summary>
    /// Mask sensitive data in log properties
    /// </summary>
    /// <param name="value">Value to mask</param>
    /// <param name="showFirstChars">Number of characters to show at the beginning</param>
    /// <param name="showLastChars">Number of characters to show at the end</param>
    /// <returns>Masked value</returns>
    public static string MaskSensitiveData(string value, int showFirstChars = 2, int showLastChars = 2)
    {
        if (string.IsNullOrEmpty(value))
            return "***";

        if (value.Length <= showFirstChars + showLastChars)
            return new string('*', value.Length);

        var firstPart = value.Substring(0, showFirstChars);
        var lastPart = value.Substring(value.Length - showLastChars);
        var middleLength = value.Length - showFirstChars - showLastChars;

        return $"{firstPart}{new string('*', middleLength)}{lastPart}";
    }

    /// <summary>
    /// Sanitize file path for logging (remove sensitive directory information)
    /// </summary>
    /// <param name="filePath">Full file path</param>
    /// <returns>Sanitized path showing only filename and parent directory</returns>
    public static string SanitizeFilePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return "unknown";

        try
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            var parentDir = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(filePath));

            return $"{parentDir}/{fileName}";
        }
        catch
        {
            return System.IO.Path.GetFileName(filePath) ?? "unknown";
        }
    }
}

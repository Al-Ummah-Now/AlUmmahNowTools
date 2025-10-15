using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Splunk;
using System;
using System.IO;
using SerilogLogger = Serilog.ILogger;

namespace AlUmmahNow.VisualImporterUno.Services.Logging;

/// <summary>
/// Configuration helper for Splunk logging using Serilog
/// </summary>
public static class SplunkLoggerConfiguration
{
    /// <summary>
    /// Configure Serilog with Splunk HTTP Event Collector (HEC) and file logging
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <param name="applicationName">Name of the application for log context</param>
    /// <param name="applicationVersion">Version of the application</param>
    /// <returns>Configured Serilog logger</returns>
    public static SerilogLogger ConfigureSplunkLogger(
        IConfiguration configuration,
        string applicationName = "AlUmmahNow.VisualImporter",
        string? applicationVersion = null)
    {
        // Get Splunk configuration from appsettings.json or environment variables
        var splunkSection = configuration.GetSection("Splunk");
        var serilogSection = configuration.GetSection("Serilog");

        var splunkHost = splunkSection["Host"]
            ?? Environment.GetEnvironmentVariable("SPLUNK_HOST")
            ?? "https://127.0.0.1:8089";

        var splunkToken = splunkSection["HecToken"]
            ?? Environment.GetEnvironmentVariable("SPLUNK_HEC_TOKEN")
            ?? "";

        var splunkIndex = splunkSection["Index"] ?? "alummahnow_logs";

        var minimumLevel = ParseLogLevel(
            serilogSection["MinimumLevel:Default"]?.ToString() ?? "Information");

        // Determine log file path
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AlUmmahNow", "VisualImporter", "Logs");

        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, "visual-importer-.txt");

        // Build logger configuration
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.WithProperty("ApplicationVersion", applicationVersion ?? "1.0.0")
            .Enrich.WithProperty("Environment", GetEnvironmentName())
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug();

        // Add Splunk sink if token is configured
        if (!string.IsNullOrEmpty(splunkToken))
        {
            try
            {
                var splunkUri = new Uri(splunkHost);

                loggerConfig.WriteTo.EventCollector(
                    splunkHost: splunkUri.GetLeftPart(UriPartial.Authority),
                    eventCollectorToken: splunkToken,
                    index: splunkIndex,
                    restrictedToMinimumLevel: minimumLevel,
                    messageHandler: new System.Net.Http.HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                    },
                    renderTemplate: false,
                    batchIntervalInSeconds: 2,
                    batchSizeLimit: 100,
                    source: applicationName,
                    sourceType: "_json");

                System.Diagnostics.Debug.WriteLine($"Splunk logging configured: {splunkHost} -> {splunkIndex}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to configure Splunk logging: {ex.Message}");
                // Continue with file logging only
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("Splunk HEC token not configured. Logging to file only.");
        }

        return loggerConfig.CreateLogger();
    }

    /// <summary>
    /// Parse log level from string
    /// </summary>
    private static LogEventLevel ParseLogLevel(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    /// <summary>
    /// Get environment name (Development, Staging, Production)
    /// </summary>
    private static string GetEnvironmentName()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Production";

#if DEBUG
        return "Development";
#else
        return env;
#endif
    }

    /// <summary>
    /// Create a child logger with additional context properties
    /// </summary>
    /// <param name="logger">Parent logger</param>
    /// <param name="sourceContext">Source context (typically class name)</param>
    /// <param name="additionalProperties">Additional properties to enrich logs</param>
    /// <returns>Enriched logger</returns>
    public static SerilogLogger ForContext(
        SerilogLogger logger,
        string sourceContext,
        params (string Name, object Value)[] additionalProperties)
    {
        var contextLogger = logger.ForContext("SourceContext", sourceContext);

        foreach (var (name, value) in additionalProperties)
        {
            contextLogger = contextLogger.ForContext(name, value);
        }

        return contextLogger;
    }

    /// <summary>
    /// Create a logger with structured context for a feature
    /// </summary>
    /// <param name="logger">Parent logger</param>
    /// <param name="featureName">Name of the feature (e.g., "WatchManagement", "ApprovalWorkflow")</param>
    /// <param name="userId">User ID for tracking</param>
    /// <param name="sessionId">Session ID for correlation</param>
    /// <returns>Feature-contexted logger</returns>
    public static SerilogLogger ForFeature(
        SerilogLogger logger,
        string featureName,
        string? userId = null,
        string? sessionId = null)
    {
        var contextLogger = logger.ForContext("FeatureName", featureName);

        if (!string.IsNullOrEmpty(userId))
        {
            contextLogger = contextLogger.ForContext("UserId", userId);
        }

        if (!string.IsNullOrEmpty(sessionId))
        {
            contextLogger = contextLogger.ForContext("SessionId", sessionId);
        }

        return contextLogger;
    }

    /// <summary>
    /// Log operation with timing
    /// Splunk Query: index="alummahnow_logs" OperationName="*" | stats avg(DurationMs) by OperationName
    /// </summary>
    public static IDisposable TimedOperation(
        SerilogLogger logger,
        string operationName,
        params (string Name, object Value)[] additionalProperties)
    {
        return new TimedOperationScope(logger, operationName, additionalProperties);
    }

    /// <summary>
    /// Helper class for timing operations with automatic logging
    /// </summary>
    private class TimedOperationScope : IDisposable
    {
        private readonly SerilogLogger _logger;
        private readonly string _operationName;
        private readonly (string Name, object Value)[] _additionalProperties;
        private readonly System.Diagnostics.Stopwatch _stopwatch;
        private readonly LogEventLevel _level;

        public TimedOperationScope(
            SerilogLogger logger,
            string operationName,
            (string Name, object Value)[] additionalProperties,
            LogEventLevel level = LogEventLevel.Information)
        {
            _logger = logger;
            _operationName = operationName;
            _additionalProperties = additionalProperties;
            _level = level;
            _stopwatch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Write(_level, "Operation started: {OperationName}", _operationName);
        }

        public void Dispose()
        {
            _stopwatch.Stop();

            var enrichedLogger = _logger
                .ForContext("OperationName", _operationName)
                .ForContext("DurationMs", _stopwatch.ElapsedMilliseconds);

            foreach (var (name, value) in _additionalProperties)
            {
                enrichedLogger = enrichedLogger.ForContext(name, value);
            }

            enrichedLogger.Write(_level,
                "Operation completed: {OperationName} in {DurationMs}ms",
                _operationName,
                _stopwatch.ElapsedMilliseconds);
        }
    }
}

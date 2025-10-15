# Splunk Logging for AlUmmahNow Visual Importer

## Overview

This application uses **Serilog** with the **Splunk HTTP Event Collector (HEC)** sink to provide comprehensive structured logging for monitoring, debugging, and analytics.

## Table of Contents

1. [Configuration](#configuration)
2. [Environment Setup](#environment-setup)
3. [Logging Features](#logging-features)
4. [Splunk Queries](#splunk-queries)
5. [Best Practices](#best-practices)
6. [Troubleshooting](#troubleshooting)

## Configuration

### Splunk Setup

1. **Create Splunk Index** (if not exists):
   ```spl
   | makeresults
   | eval index_name="alummahnow_logs"
   ```
   Or via Splunk Web: Settings > Indexes > New Index > "alummahnow_logs"

2. **Create HTTP Event Collector (HEC) Token**:
   - Navigate to: Settings > Data Inputs > HTTP Event Collector
   - Click "New Token"
   - Name: `AlUmmahNow Visual Importer`
   - Source Type: `_json`
   - Allowed Indexes: `alummahnow_logs`
   - Copy the generated token

3. **Configure HEC Settings**:
   - Settings > Data Inputs > HTTP Event Collector > Global Settings
   - Enabled: `Yes`
   - Enable SSL: `Yes`
   - HTTP Port Number: `8088` (default)
   - Default Source Type: `_json`

### Application Configuration

#### Method 1: Environment Variable (Recommended for Production)

Set the Splunk HEC token as an environment variable:

**Windows:**
```powershell
[System.Environment]::SetEnvironmentVariable("SPLUNK_HEC_TOKEN", "your-token-here", "User")
```

**Linux/macOS:**
```bash
export SPLUNK_HEC_TOKEN="your-token-here"
```

#### Method 2: appsettings.json (Development)

Update `/home/meywd/AlUmmahNowTools/AlUmmahNow.VisualImporterUno/AlUmmahNow.VisualImporterUno/appsettings.json`:

```json
{
  "Splunk": {
    "Host": "https://127.0.0.1:8089",
    "HecToken": "YOUR-HEC-TOKEN-HERE",
    "Index": "alummahnow_logs",
    "Source": "AlUmmahNow.VisualImporter",
    "SourceType": "_json"
  }
}
```

**IMPORTANT**: Never commit HEC tokens to source control!

### Log Levels by Environment

#### Development (appsettings.Development.json)
- Default: `Information`
- Microsoft: `Warning`
- System: `Warning`
- Application: `Debug`

#### Production (appsettings.Production.json)
- Default: `Warning`
- Microsoft: `Error`
- System: `Error`
- Application: `Information`

## Environment Setup

### Verify Configuration

1. **Check Splunk HEC Endpoint**:
   ```bash
   curl -k https://127.0.0.1:8089/services/collector/health
   ```
   Expected response: `{"text":"HEC is healthy","code":17}`

2. **Test HEC Token**:
   ```bash
   curl -k https://127.0.0.1:8089/services/collector \
     -H "Authorization: Splunk YOUR-HEC-TOKEN" \
     -d '{"event": "Test event", "sourcetype": "_json"}'
   ```

3. **Run Application and Verify Logs**:
   - Start the application
   - In Splunk: `index="alummahnow_logs" | head 10`
   - You should see logs appearing

## Logging Features

### Structured Logging

All logs include rich context properties:

```csharp
_logger.LogInformation(
    "Watch created: {ChannelName} at {WatchPath}",
    channelName, watchPath);
```

Splunk fields:
- `ChannelName`
- `WatchPath`
- `UserId`
- `SessionId`
- `MachineName`
- `ThreadId`
- `Application`
- `ApplicationVersion`

### Log Enrichers

Automatic enrichment:
- **FromLogContext**: Contextual properties
- **WithMachineName**: Host identification
- **WithThreadId**: Threading context
- **WithEnvironmentName**: Development/Production
- **Custom Properties**: UserId, SessionId, FeatureName, ActionName

### Event Types

1. **UserAction**: User-triggered operations
   - Properties: `FeatureName`, `ActionName`, `UserId`
   - Example: Button clicks, form submissions

2. **FileOperation**: File-related operations
   - Properties: `FileOperation`, `FileName`, `OperationStatus`, `FileSizeBytes`
   - Example: File approval, rejection, detection

3. **SignalREvent**: Real-time events
   - Properties: `SignalREvent`, `WatchId`, `ChannelName`
   - Example: File detected, watch status changed

4. **PerformanceMetric**: Operation timing
   - Properties: `MetricName`, `MetricValue`, `MetricUnit`
   - Example: API call duration, query execution time

5. **ValidationError**: Input validation failures
   - Properties: `ValidationField`, `ValidationMessage`, `AttemptedValue`
   - Example: Form validation errors

6. **ServiceCallError**: External service failures
   - Properties: `ServiceName`, `ServiceEndpoint`, `ErrorType`
   - Example: SignalR disconnections, API failures

### Performance Logging

Automatic timing with `IDisposable` pattern:

```csharp
using (var timedOp = _loggingService.BeginTimedOperation("RefreshWatches"))
{
    // Operation code here
    var watches = await GetWatchesAsync();
    // Automatically logs duration on dispose
}
```

### Exception Logging

Structured exception logging:

```csharp
_logger.LogStructuredException(ex,
    "Failed to create watch: {ChannelName}",
    channelName);
```

Splunk fields:
- `ExceptionType`
- `ExceptionMessage`
- `StackTrace`
- `InnerExceptionType`
- `InnerExceptionMessage`

## Splunk Queries

### Application Health

**Application startup/shutdown:**
```spl
index="alummahnow_logs" ("Application starting" OR "Application shutdown")
| table _time, Message, MachineName, UserName
| sort -_time
```

**Exception dashboard:**
```spl
index="alummahnow_logs" ExceptionType=*
| stats count by ExceptionType, SourceContext
| sort -count
```

**Critical errors (last 24 hours):**
```spl
index="alummahnow_logs" Level="Fatal" OR Level="Error"
earliest=-24h
| table _time, Level, Message, ExceptionType, SourceContext
| sort -_time
```

### Watch Management

**Watch creation metrics:**
```spl
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch"
| stats count by Status, ChannelName
| sort -count
```

**Watch creation performance:**
```spl
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch" Status="Success"
| stats avg(DurationMs) as avg_ms, max(DurationMs) as max_ms, min(DurationMs) as min_ms, count
```

**Active watches by channel:**
```spl
index="alummahnow_logs" SignalREvent="WatchStatusChanged" IsActive=true
| stats latest(_time) as last_update by ChannelName, WatchId
| sort -last_update
```

**Watch failures:**
```spl
index="alummahnow_logs" FeatureName="WatchManagement" Status="Failed"
| stats count by ActionName, ErrorMessage
| sort -count
```

### Approval Workflow

**Approval metrics:**
```spl
index="alummahnow_logs" FileOperation="Approval"
| stats count by OperationStatus, ChannelName
| sort -count
```

**Approval processing time:**
```spl
index="alummahnow_logs" FileOperation="Approval" OperationStatus="Approved"
| stats avg(DurationMs) as avg_ms, max(DurationMs) as max_ms, count
```

**File detection volume (hourly):**
```spl
index="alummahnow_logs" SignalREvent="FileDetected"
| timechart span=1h count by ChannelName
```

**Pending approvals by channel:**
```spl
index="alummahnow_logs" SignalREvent="NewFileForApproval"
| stats count by ChannelName
| sort -count
```

**Bulk approval operations:**
```spl
index="alummahnow_logs" SignalREvent="BulkApprovalProcessed"
| table _time, ProcessedCount, Decision
| sort -_time
```

### User Activity

**User actions (last 4 hours):**
```spl
index="alummahnow_logs" EventType="UserAction"
earliest=-4h
| stats count by UserId, FeatureName, ActionName
| sort -count
```

**User action timeline:**
```spl
index="alummahnow_logs" EventType="UserAction" UserId="*"
| timechart span=15m count by FeatureName
```

**Most active users:**
```spl
index="alummahnow_logs" EventType="UserAction"
| stats count by UserId
| sort -count limit=10
```

### Performance Monitoring

**Slow operations (>5 seconds):**
```spl
index="alummahnow_logs" DurationMs>5000
| table _time, OperationName, DurationMs, FeatureName
| eval DurationSec=round(DurationMs/1000, 2)
| sort -DurationMs
```

**API call performance:**
```spl
index="alummahnow_logs" ServiceName="FileScanService"
| stats avg(DurationMs) as avg_ms, max(DurationMs) as max_ms, count by ServiceEndpoint
| eval avg_sec=round(avg_ms/1000, 2)
| sort -avg_ms
```

**Performance trends (hourly average):**
```spl
index="alummahnow_logs" DurationMs=*
| timechart span=1h avg(DurationMs) as avg_ms by OperationName
```

### SignalR Connectivity

**Connection health:**
```spl
index="alummahnow_logs" ("SignalR" OR "FileScanSignalRClient")
| stats count by Status, Message
| sort -count
```

**Reconnection events:**
```spl
index="alummahnow_logs" Status="Reconnecting" OR Status="Reconnected"
| timechart span=5m count by Status
```

**Disconnection causes:**
```spl
index="alummahnow_logs" Status="Disconnected"
| stats count by ErrorMessage
| sort -count
```

### Validation Errors

**Validation failures:**
```spl
index="alummahnow_logs" EventType="ValidationError"
| stats count by ValidationField, ValidationMessage
| sort -count
```

**Validation error timeline:**
```spl
index="alummahnow_logs" EventType="ValidationError"
| timechart span=1h count by ValidationField
```

### File Operations

**File processing volume:**
```spl
index="alummahnow_logs" FileOperation=*
| stats count, sum(FileSizeBytes) as total_bytes by FileOperation
| eval total_mb=round(total_bytes/1024/1024, 2)
| sort -count
```

**Large file operations (>100MB):**
```spl
index="alummahnow_logs" FileSizeMB>100
| table _time, FileOperation, FileName, FileSizeMB, OperationStatus
| sort -FileSizeMB
```

**File operation errors:**
```spl
index="alummahnow_logs" FileOperation=* OperationStatus="Failed"
| stats count by FileOperation, ErrorMessage
| sort -count
```

## Best Practices

### Do's

1. **Always log user actions** with `LogUserAction`
2. **Use structured logging** with named properties
3. **Log exceptions** with `LogStructuredException`
4. **Sanitize sensitive data** (file paths, usernames)
5. **Use timed operations** for async calls
6. **Include correlation IDs** for tracking workflows
7. **Log state changes** (watch started/stopped, status changes)
8. **Add context properties** (ChannelName, WatchId, etc.)

### Don'ts

1. **Never log passwords or tokens**
2. **Don't log PII** (personal identifiable information)
3. **Avoid logging in tight loops** (causes performance issues)
4. **Don't use string interpolation** - use structured logging:
   - Bad: `_logger.LogInformation($"User {userId} logged in")`
   - Good: `_logger.LogInformation("User {UserId} logged in", userId)`
5. **Don't log full file paths** - use `SanitizeFilePath()`
6. **Avoid excessive Debug logs** in production

### Sensitive Data Handling

Use helper methods to sanitize data:

```csharp
// Mask tokens
var maskedToken = ExceptionLoggingExtensions.MaskSensitiveData(token, 4, 4);

// Sanitize file paths (show only filename and parent dir)
var safePath = ExceptionLoggingExtensions.SanitizeFilePath(fullPath);

_logger.LogInformation("Processing file: {FilePath}", safePath);
```

## Troubleshooting

### Logs not appearing in Splunk

1. **Check HEC endpoint**:
   ```bash
   curl -k https://127.0.0.1:8089/services/collector/health
   ```

2. **Verify token**:
   - Check `appsettings.json` or environment variable
   - Test token with curl command

3. **Check Serilog self-diagnostics**:
   - Application logs self-diagnostic messages to Debug output
   - Look for `"Serilog: "` prefix in debug console

4. **Verify SSL certificate**:
   - The application disables SSL verification for localhost
   - For production, use valid certificates

5. **Check Splunk index permissions**:
   - HEC token must have write permission to `alummahnow_logs` index

### Performance issues

1. **Reduce log level**:
   - Change from `Debug` to `Information` or `Warning`

2. **Adjust batch settings**:
   - Increase `batchIntervalInSeconds` (default: 2)
   - Increase `batchSizeLimit` (default: 100)

3. **Filter noisy loggers**:
   ```json
   "Override": {
     "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
   }
   ```

### Missing fields in Splunk

1. **Check enrichers**:
   - Verify enrichers are configured in `appsettings.json`

2. **Use structured logging**:
   - Always use named parameters: `{PropertyName}`

3. **Verify LogContext**:
   - Use `using (_logger.BeginScope(properties))` for scoped properties

## Dashboard Examples

### Create Splunk Dashboard

1. Navigate to: Search & Reporting > Dashboards > Create New Dashboard
2. Add panels with these searches:

**Panel 1: Application Health**
```xml
<search>
  <query>
    index="alummahnow_logs" Level="Error" OR Level="Fatal"
    | timechart span=1h count by Level
  </query>
</search>
```

**Panel 2: Watch Activity**
```xml
<search>
  <query>
    index="alummahnow_logs" FeatureName="WatchManagement"
    | stats count by ActionName
  </query>
</search>
```

**Panel 3: File Processing Volume**
```xml
<search>
  <query>
    index="alummahnow_logs" FileOperation=*
    | timechart span=1h sum(FileSizeMB) as total_mb by FileOperation
  </query>
</search>
```

**Panel 4: User Activity**
```xml
<search>
  <query>
    index="alummahnow_logs" EventType="UserAction"
    | stats count by UserId
    | sort -count limit=10
  </query>
</search>
```

## Support

For issues or questions:
1. Check application logs: `%LOCALAPPDATA%\AlUmmahNow\VisualImporter\Logs\`
2. Review Serilog self-diagnostics in Debug output
3. Verify Splunk HEC configuration
4. Test with curl commands provided above

## Additional Resources

- [Serilog Documentation](https://serilog.net/)
- [Splunk HEC Documentation](https://docs.splunk.com/Documentation/Splunk/latest/Data/UsetheHTTPEventCollector)
- [Serilog Splunk Sink](https://github.com/serilog/serilog-sinks-splunk)
- [Structured Logging Best Practices](https://github.com/serilog/serilog/wiki/Structured-Data)

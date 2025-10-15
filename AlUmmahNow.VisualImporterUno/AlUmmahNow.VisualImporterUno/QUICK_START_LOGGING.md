# Quick Start: Splunk Logging Setup

## 5-Minute Setup Guide

### Step 1: Configure Splunk HEC Token

**Option A: Environment Variable (Recommended)**

Windows PowerShell:
```powershell
[System.Environment]::SetEnvironmentVariable("SPLUNK_HEC_TOKEN", "your-token-here", "User")
```

Linux/macOS:
```bash
export SPLUNK_HEC_TOKEN="your-token-here"
```

**Option B: appsettings.json (Development Only)**

Edit `/home/meywd/AlUmmahNowTools/AlUmmahNow.VisualImporterUno/AlUmmahNow.VisualImporterUno/appsettings.json`:

```json
{
  "Splunk": {
    "HecToken": "YOUR-TOKEN-HERE"
  }
}
```

### Step 2: Create Splunk Index (if needed)

In Splunk Web:
1. Settings > Indexes > New Index
2. Index Name: `alummahnow_logs`
3. Click "Save"

### Step 3: Verify Configuration

Run the application and check Splunk:

```spl
index="alummahnow_logs" | head 10
```

You should see logs appearing immediately.

### Step 4: Test Logging

1. Open the application
2. Create a watch or perform an action
3. In Splunk:

```spl
index="alummahnow_logs" FeatureName="WatchManagement"
| table _time, ActionName, Status, ChannelName
| sort -_time
```

## Common Setup Issues

### Issue: Logs not appearing in Splunk

**Solution 1**: Verify HEC endpoint
```bash
curl -k https://127.0.0.1:8089/services/collector/health
```
Expected: `{"text":"HEC is healthy","code":17}`

**Solution 2**: Check token
```bash
curl -k https://127.0.0.1:8089/services/collector \
  -H "Authorization: Splunk YOUR-TOKEN" \
  -d '{"event": "Test"}'
```

**Solution 3**: Check application logs
- Location: `%LOCALAPPDATA%\AlUmmahNow\VisualImporter\Logs\`
- Look for Serilog errors

### Issue: SSL certificate errors

The application automatically disables SSL verification for localhost. For production:
1. Use valid SSL certificates
2. Update `SplunkLoggerConfiguration.cs` to enable certificate validation

### Issue: Performance degradation

1. Increase log level to `Warning` in production
2. Adjust batch settings in `SplunkLoggerConfiguration.cs`:
   - `batchIntervalInSeconds`: Default 2, increase to 5-10
   - `batchSizeLimit`: Default 100, increase to 500

## Quick Queries

### Application Status
```spl
index="alummahnow_logs" | stats latest(_time) as last_log | eval status=if((now()-last_log)<60, "Active", "Down")
```

### Errors in Last Hour
```spl
index="alummahnow_logs" Level="Error" OR Level="Fatal" earliest=-1h
| table _time, Message, ExceptionType
| sort -_time
```

### User Activity
```spl
index="alummahnow_logs" EventType="UserAction" earliest=-1h
| stats count by UserId, FeatureName
| sort -count
```

### Watch Activity
```spl
index="alummahnow_logs" FeatureName="WatchManagement"
| stats count by ActionName, Status
```

### File Approvals
```spl
index="alummahnow_logs" FileOperation="Approval"
| stats count by OperationStatus, ChannelName
```

## Next Steps

1. Read [SPLUNK_LOGGING_README.md](./SPLUNK_LOGGING_README.md) for detailed configuration
2. Explore [SPLUNK_QUERIES.md](./SPLUNK_QUERIES.md) for comprehensive queries
3. Review implementation guides:
   - [VIEWMODEL_LOGGING_GUIDE.md](./Services/Logging/VIEWMODEL_LOGGING_GUIDE.md)
   - [SERVICE_LOGGING_GUIDE.md](./Services/Logging/SERVICE_LOGGING_GUIDE.md)
4. Set up Splunk alerts for critical errors
5. Create dashboards for your team

## Support

Check these if you have issues:
1. Application logs: `%LOCALAPPDATA%\AlUmmahNow\VisualImporter\Logs\`
2. Debug output in Visual Studio (look for "Serilog:")
3. Splunk search: `index=_internal source=*http_event_collector*`

## Key Logging Points

Already implemented logging for:
- Application startup/shutdown
- Watch creation/deletion/status changes
- File detection and approval workflow
- SignalR connectivity
- User actions
- Performance metrics
- Exceptions and errors
- Validation failures

## Configuration Files Summary

| File | Purpose |
|------|---------|
| `appsettings.json` | Development configuration |
| `appsettings.Production.json` | Production log levels |
| `App.xaml.cs` | Logging initialization |
| `Services/Logging/SplunkLoggerConfiguration.cs` | Splunk configuration |
| `Services/Logging/LoggingService.cs` | Logging service wrapper |
| `Services/Logging/ExceptionLoggingExtensions.cs` | Exception logging helpers |
| `Models/LogContext.cs` | Structured logging context |

## Example: Adding Logging to a New Feature

```csharp
public class MyNewViewModel : ObservableObject
{
    private readonly ILogger<MyNewViewModel> _logger;
    private readonly ILoggingService _loggingService;

    public MyNewViewModel(
        ILogger<MyNewViewModel> logger,
        ILoggingService loggingService)
    {
        _logger = logger;
        _loggingService = loggingService;
    }

    public async Task DoSomethingAsync()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting operation: {OperationName}", "DoSomething");

            // Your code here
            await PerformOperationAsync();

            sw.Stop();

            _loggingService.LogUserAction(
                "MyFeature",
                "DoSomething",
                new Dictionary<string, object>
                {
                    ["DurationMs"] = sw.ElapsedMilliseconds,
                    ["Status"] = "Success"
                });

            _logger.LogInformation("Operation completed in {Duration}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();

            _logger.LogStructuredException(ex, "Operation failed");

            _loggingService.LogUserAction(
                "MyFeature",
                "DoSomething",
                new Dictionary<string, object>
                {
                    ["DurationMs"] = sw.ElapsedMilliseconds,
                    ["Status"] = "Failed",
                    ["ErrorMessage"] = ex.Message
                });
        }
    }
}
```

## Splunk Query for Your Feature

```spl
# Find your feature logs
index="alummahnow_logs" FeatureName="MyFeature"
| stats count by ActionName, Status

# Performance analysis
index="alummahnow_logs" FeatureName="MyFeature"
| stats avg(DurationMs) as avg_ms, max(DurationMs) as max_ms by ActionName
```

Happy logging!

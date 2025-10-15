# Dual Logging Configuration - File + Splunk

## Overview

The AlUmmahNow Visual Importer application implements **dual logging** that sends logs to **both** local files and Splunk simultaneously. This provides:

✅ **Local File Logs**: Always available for immediate debugging and offline access
✅ **Splunk Logs**: Centralized monitoring, analytics, and alerting

## How It Works

### Architecture

```
Application Logs
       ↓
   Serilog Logger
       ↓
    ┌──┴──┐
    ↓     ↓
File Sink  Splunk Sink
    ↓        ↓
Local File  Splunk HEC
```

### Configuration Location

**File**: `SplunkLoggerConfiguration.cs` (Lines 66-102)

```csharp
// Line 66-71: File Sink Configuration
.WriteTo.File(
    logPath,
    rollingInterval: RollingInterval.Day,
    retainedFileCountLimit: 7,
    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] ...")

// Lines 74-102: Splunk Sink Configuration (conditional)
if (!string.IsNullOrEmpty(splunkToken))
{
    loggerConfig.WriteTo.EventCollector(
        splunkHost: splunkUri.GetLeftPart(UriPartial.Authority),
        eventCollectorToken: splunkToken,
        index: splunkIndex,
        ...
    );
}
```

## File Logging (Always Enabled)

### Location
- **Windows**: `%LOCALAPPDATA%\AlUmmahNow\VisualImporter\Logs\`
- **Linux/Mac**: `~/.local/share/AlUmmahNow/VisualImporter/Logs/`

### File Format
```
visual-importer-20250415.txt
visual-importer-20250416.txt
...
```

### Retention
- **Rolling**: Daily (new file each day)
- **Retention**: 7 days (older files automatically deleted)

### Example Log Entry
```
[2025-04-15 10:30:45.123 +00:00] [INF] [WatchViewModel] Watch created successfully: ChannelName="Islamic Lectures" WatchPath="/videos/lectures"
```

## Splunk Logging (Conditional)

### Enabled When
✅ Splunk HEC token is configured via:
- Environment variable: `SPLUNK_HEC_TOKEN`
- OR `appsettings.json`: `Splunk.HecToken`

### Disabled When
❌ No HEC token configured
❌ Splunk endpoint unreachable (falls back to file logging only)

### Configuration

**appsettings.json:**
```json
{
  "Splunk": {
    "Host": "https://127.0.0.1:8089",
    "HecToken": "",  // ← Set this or use environment variable
    "Index": "alummahnow_logs",
    "Source": "AlUmmahNow.VisualImporter",
    "SourceType": "_json"
  }
}
```

**Environment Variable (Recommended):**
```bash
# Windows
setx SPLUNK_HEC_TOKEN "your-token-here"

# Linux/Mac
export SPLUNK_HEC_TOKEN="your-token-here"
```

### Splunk Configuration
- **Index**: `alummahnow_logs`
- **Endpoint**: Splunk HTTP Event Collector (HEC)
- **Format**: JSON (structured logs)
- **Batching**: 2-second intervals, 100 events per batch

## Verification

### Verify File Logging

**Check log directory:**
```bash
# Windows
dir %LOCALAPPDATA%\AlUmmahNow\VisualImporter\Logs\

# Linux/Mac
ls ~/.local/share/AlUmmahNow/VisualImporter/Logs/
```

**Tail logs in real-time:**
```bash
# Linux/Mac
tail -f ~/.local/share/AlUmmahNow/VisualImporter/Logs/visual-importer-*.txt

# Windows PowerShell
Get-Content "%LOCALAPPDATA%\AlUmmahNow\VisualImporter\Logs\visual-importer-*.txt" -Wait
```

### Verify Splunk Logging

**1. Check console output on startup:**
```
Splunk logging configured: https://127.0.0.1:8089 -> alummahnow_logs
```

**2. Query Splunk:**
```spl
index="alummahnow_logs" | head 10
```

**3. Check application startup event:**
```spl
index="alummahnow_logs" "Application starting"
| table _time, Message, MachineName, ApplicationVersion
| sort -_time
```

### Troubleshooting Dual Logging

#### Issue: Logs only in file, not in Splunk

**Cause**: Splunk HEC token not configured or Splunk unreachable

**Debug Steps:**

1. **Check console output:**
   ```
   Splunk HEC token not configured. Logging to file only.
   ```

2. **Verify HEC token is set:**
   ```bash
   # Windows
   echo %SPLUNK_HEC_TOKEN%

   # Linux/Mac
   echo $SPLUNK_HEC_TOKEN
   ```

3. **Test Splunk HEC endpoint:**
   ```bash
   curl -k https://127.0.0.1:8089/services/collector/health
   ```
   Expected: `{"text":"HEC is healthy","code":17}`

4. **Test HEC token:**
   ```bash
   curl -k https://127.0.0.1:8089/services/collector \
     -H "Authorization: Splunk YOUR-TOKEN" \
     -d '{"event": "Test", "sourcetype": "_json"}'
   ```

5. **Check Splunk index exists:**
   ```spl
   | eventcount summarize=false index=alummahnow_logs
   ```

#### Issue: Logs only in Splunk, not in file

**Cause**: File sink is always enabled, so this should not happen

**Debug Steps:**

1. **Check file directory permissions:**
   ```bash
   # Ensure directory exists and is writable
   ls -la ~/.local/share/AlUmmahNow/VisualImporter/Logs/
   ```

2. **Check disk space:**
   ```bash
   df -h ~/.local/share/AlUmmahNow/VisualImporter/Logs/
   ```

## Log Levels

Both sinks respect the same log levels configured in `appsettings.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

### Environment-Specific Levels

**Development:**
- Default: `Information`
- File: All events
- Splunk: All events

**Production:**
- Default: `Warning`
- File: Warning and above
- Splunk: Warning and above

## Sink-Specific Configuration

### File Sink Settings

```csharp
.WriteTo.File(
    path: logPath,                      // Log file path
    rollingInterval: RollingInterval.Day, // New file daily
    retainedFileCountLimit: 7,          // Keep 7 days
    outputTemplate: "[{Timestamp}] ..." // Custom format
)
```

**Customization:**
- Change rolling interval: `RollingInterval.Hour`, `RollingInterval.Day`
- Change retention: `retainedFileCountLimit: 30` (30 days)
- Change format: Modify `outputTemplate`

### Splunk Sink Settings

```csharp
.WriteTo.EventCollector(
    splunkHost: "https://127.0.0.1:8089",
    eventCollectorToken: token,
    index: "alummahnow_logs",
    batchIntervalInSeconds: 2,          // Batch every 2 seconds
    batchSizeLimit: 100,                // Max 100 events per batch
    renderTemplate: false,              // Send structured JSON
    messageHandler: new HttpClientHandler {
        ServerCertificateCustomValidationCallback = ... // SSL validation
    }
)
```

**Customization:**
- Increase batching: `batchIntervalInSeconds: 5`, `batchSizeLimit: 500`
- Change index: `index: "production_logs"`
- Enable SSL validation: Remove `ServerCertificateCustomValidationCallback`

## Performance Considerations

### File Logging
- **Overhead**: Minimal (~1-2ms per log entry)
- **Async**: Yes (buffered writes)
- **Impact**: Negligible on application performance

### Splunk Logging
- **Overhead**: Minimal with batching (~2-5ms per batch)
- **Async**: Yes (batched HTTP calls)
- **Impact**: Negligible on application performance
- **Failure Mode**: If Splunk is down, events are **not queued** (logs continue to file)

### Combined Impact
- **Total Overhead**: ~3-7ms for dual logging
- **Recommended**: Keep both enabled for production

## Use Cases

### Development
✅ **File Logs**: Quick debugging, offline access
✅ **Splunk Logs**: Testing Splunk integration, query development

**Recommendation**: Enable both

### Staging/QA
✅ **File Logs**: Detailed debugging if Splunk is down
✅ **Splunk Logs**: Centralized monitoring across test environments

**Recommendation**: Enable both

### Production
✅ **File Logs**: Emergency debugging, audit trail
✅ **Splunk Logs**: Real-time monitoring, alerting, analytics

**Recommendation**: Enable both with `Warning` level

## Migration Path

### From File-Only to Dual Logging

**Step 1**: Verify file logging works
```bash
# Check existing logs
ls ~/.local/share/AlUmmahNow/VisualImporter/Logs/
```

**Step 2**: Configure Splunk HEC token
```bash
export SPLUNK_HEC_TOKEN="your-token"
```

**Step 3**: Restart application

**Step 4**: Verify both sinks
- Check file: New entries continue
- Check Splunk: Events appear in index

### From Splunk-Only to Dual Logging

**Not applicable** - file logging is always enabled

## Best Practices

### DO
✅ Enable both sinks in all environments
✅ Use environment variables for HEC token
✅ Monitor both file and Splunk logs in production
✅ Set appropriate log levels per environment
✅ Use structured logging for rich Splunk queries
✅ Keep file retention at 7-30 days

### DON'T
❌ Disable file logging (no benefit, loses emergency fallback)
❌ Commit HEC tokens to source control
❌ Log sensitive data to either sink
❌ Use `Debug` level in production (excessive volume)
❌ Rely solely on Splunk (file logs provide fallback)

## Security

### File Logs
- **Location**: User-specific application data folder
- **Permissions**: User read/write only
- **Sensitive Data**: Use `SanitizeFilePath()` and `MaskSensitiveData()`

### Splunk Logs
- **Transport**: HTTPS (TLS 1.2+)
- **Authentication**: Bearer token
- **SSL**: Validated for production (disabled for localhost development)
- **Sensitive Data**: Same sanitization as file logs

## Monitoring Dual Logging Health

### Check Both Sinks Are Working

**Splunk Query:**
```spl
index="alummahnow_logs" "Logging configured"
| stats count by LogSinks
```

**Expected Output:**
```
LogSinks="File,Splunk,Debug" count=1
```

### Alert on Logging Failures

**File Write Failures:**
```spl
index="alummahnow_logs" ExceptionType="IOException" "Failed to write log"
| stats count by MachineName
```

**Splunk Connection Failures:**
```spl
index="alummahnow_logs" "Failed to configure Splunk logging"
| stats count by MachineName, ErrorMessage
```

## Summary

| Feature | File Logging | Splunk Logging |
|---------|-------------|----------------|
| **Always Enabled** | ✅ Yes | ⚠️ If token configured |
| **Async** | ✅ Yes | ✅ Yes |
| **Structured** | ⚠️ Template | ✅ JSON |
| **Retention** | 7 days default | ♾️ Splunk retention |
| **Searchable** | ⚠️ grep/text search | ✅ Full-text + fields |
| **Real-time** | ⚠️ Tail file | ✅ Live queries |
| **Alerting** | ❌ No | ✅ Yes |
| **Dashboards** | ❌ No | ✅ Yes |
| **Performance** | ~1-2ms | ~2-5ms |
| **Offline Access** | ✅ Yes | ❌ No |
| **Fallback** | N/A | ✅ File logs |

## Conclusion

The dual logging configuration provides:

1. **Reliability**: File logs always available, even if Splunk is down
2. **Flexibility**: Query locally or in Splunk
3. **Performance**: Minimal overhead with async writes
4. **Security**: Sensitive data sanitization in both sinks
5. **Auditability**: Complete audit trail in multiple locations

**Recommendation**: Keep both logging sinks enabled in all environments for maximum observability and reliability.

# Comprehensive Splunk Queries for AlUmmahNow Visual Importer

## Quick Reference Index

- [Application Health](#application-health)
- [Watch Management](#watch-management)
- [Approval Workflow](#approval-workflow)
- [User Activity](#user-activity)
- [Performance Monitoring](#performance-monitoring)
- [SignalR Connectivity](#signalr-connectivity)
- [Error Analysis](#error-analysis)
- [File Operations](#file-operations)
- [Analytics & Reporting](#analytics--reporting)
- [Alerts](#alerts)

## Application Health

### Application Status
```spl
index="alummahnow_logs"
| stats latest(_time) as last_log by MachineName, Application, ApplicationVersion
| eval status=if((now()-last_log)<300, "Active", "Inactive")
| table MachineName, Application, ApplicationVersion, status, last_log
| convert ctime(last_log)
```

### Startup and Shutdown Events
```spl
index="alummahnow_logs" ("Application starting" OR "Application shutdown" OR "Application Initialized")
| table _time, Level, Message, MachineName, UserName, ApplicationVersion
| sort -_time limit=50
```

### Log Volume by Level
```spl
index="alummahnow_logs"
| timechart span=15m count by Level
```

### Overall System Health Score
```spl
index="alummahnow_logs"
| bucket _time span=1h
| stats count(eval(Level="Error" OR Level="Fatal")) as errors,
        count(eval(Level="Warning")) as warnings,
        count as total by _time
| eval error_rate=(errors/total)*100
| eval warning_rate=(warnings/total)*100
| eval health_score=100-(error_rate*2)-(warning_rate*0.5)
| table _time, health_score, errors, warnings, total
```

## Watch Management

### Active Watches Summary
```spl
index="alummahnow_logs" SignalREvent="WatchStatusChanged" IsActive=true
| stats latest(_time) as last_update,
        latest(StatusMessage) as status
        by WatchId, ChannelName
| convert ctime(last_update)
| table ChannelName, WatchId, status, last_update
| sort ChannelName
```

### Watch Creation Timeline
```spl
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch"
| timechart span=1d count by Status
```

### Watch Creation Success Rate
```spl
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch"
| stats count(eval(Status="Success")) as successes,
        count(eval(Status="Failed")) as failures
| eval success_rate=round((successes/(successes+failures))*100, 2)
| table success_rate, successes, failures
```

### Watch Creation Performance Percentiles
```spl
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch" Status="Success"
| stats perc50(DurationMs) as p50,
        perc75(DurationMs) as p75,
        perc90(DurationMs) as p90,
        perc95(DurationMs) as p95,
        perc99(DurationMs) as p99,
        max(DurationMs) as max
| foreach p* max [eval <<FIELD>>=round(<<FIELD>>, 0)]
```

### Watch Failures by Error Type
```spl
index="alummahnow_logs" FeatureName="WatchManagement" Status="Failed"
| rex field=ErrorMessage "(?<error_category>^[^:]+)"
| stats count by error_category, ActionName
| sort -count
```

### Watch Activity Heatmap (by hour and day of week)
```spl
index="alummahnow_logs" FeatureName="WatchManagement"
| eval hour=strftime(_time, "%H")
| eval day=strftime(_time, "%A")
| stats count by hour, day
| xyseries day hour count
```

### Watches by Channel
```spl
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch" Status="Success"
| stats count as watch_count,
        latest(WatchPath) as last_path,
        latest(_time) as last_created
        by ChannelName
| convert ctime(last_created)
| sort -watch_count
```

### Watch Lifecycle (Create to Delete)
```spl
index="alummahnow_logs" FeatureName="WatchManagement" WatchId=*
| transaction WatchId maxspan=30d
| eval lifecycle_days=round(duration/86400, 1)
| table WatchId, ChannelName, lifecycle_days, eventcount
| sort -lifecycle_days
```

## Approval Workflow

### Approval Statistics Dashboard
```spl
index="alummahnow_logs" FileOperation="Approval"
| stats count(eval(OperationStatus="Approved")) as approved,
        count(eval(OperationStatus="Rejected")) as rejected,
        count(eval(OperationStatus="Failed")) as failed,
        sum(FileSizeBytes) as total_bytes
        by ChannelName
| eval total_mb=round(total_bytes/1024/1024, 2)
| eval approval_rate=round((approved/(approved+rejected))*100, 2)
| table ChannelName, approved, rejected, failed, approval_rate, total_mb
| sort -approved
```

### File Detection Rate
```spl
index="alummahnow_logs" SignalREvent="FileDetected"
| timechart span=1h count by ChannelName
```

### Approval Processing Time Analysis
```spl
index="alummahnow_logs" FileOperation="Approval" OperationStatus="Approved" OR OperationStatus="Rejected"
| stats avg(DurationMs) as avg_ms,
        median(DurationMs) as median_ms,
        max(DurationMs) as max_ms,
        count
        by OperationStatus
| eval avg_sec=round(avg_ms/1000, 2),
       median_sec=round(median_ms/1000, 2),
       max_sec=round(max_ms/1000, 2)
| table OperationStatus, avg_sec, median_sec, max_sec, count
```

### Bulk Approval Operations
```spl
index="alummahnow_logs" SignalREvent="BulkApprovalProcessed"
| stats sum(ProcessedCount) as total_processed,
        avg(ProcessedCount) as avg_per_operation,
        max(ProcessedCount) as max_per_operation
| eval avg_per_operation=round(avg_per_operation, 0)
```

### Pending Approval Backlog
```spl
index="alummahnow_logs" SignalREvent="NewFileForApproval" OR SignalREvent="ApprovalStatusChanged"
| eval is_pending=if(SignalREvent="NewFileForApproval", 1, -1)
| streamstats sum(is_pending) as backlog_count
| timechart span=1h latest(backlog_count) as current_backlog
```

### Top Files by Size (Pending Approval)
```spl
index="alummahnow_logs" SignalREvent="NewFileForApproval"
| stats latest(_time) as detected_time,
        latest(FileSizeBytes) as file_size
        by FileName, ChannelName
| eval file_size_mb=round(file_size/1024/1024, 2)
| convert ctime(detected_time)
| sort -file_size_mb limit=20
```

### Approval User Activity
```spl
index="alummahnow_logs" FileOperation="Approval" ApprovedBy=*
| stats count(eval(OperationStatus="Approved")) as approved,
        count(eval(OperationStatus="Rejected")) as rejected
        by ApprovedBy
| eval total=approved+rejected
| sort -total
```

## User Activity

### User Session Summary
```spl
index="alummahnow_logs" EventType="UserAction"
| stats count as actions,
        dc(FeatureName) as features_used,
        earliest(_time) as session_start,
        latest(_time) as session_end
        by UserId, SessionId
| eval duration_min=round((session_end-session_start)/60, 1)
| convert ctime(session_start) ctime(session_end)
| sort -actions
```

### Most Used Features
```spl
index="alummahnow_logs" EventType="UserAction"
| stats count by FeatureName, ActionName
| sort -count limit=20
```

### User Activity Timeline
```spl
index="alummahnow_logs" EventType="UserAction" UserId="*"
| timechart span=15m count by UserId limit=10
```

### User Actions by Feature
```spl
index="alummahnow_logs" EventType="UserAction"
| chart count by UserId, FeatureName
| sort -Total
```

### Peak Usage Hours
```spl
index="alummahnow_logs" EventType="UserAction"
| eval hour=strftime(_time, "%H:00")
| stats count by hour
| sort hour
```

## Performance Monitoring

### Slowest Operations
```spl
index="alummahnow_logs" DurationMs>1000
| table _time, OperationName, DurationMs, FeatureName, ActionName, Status
| eval DurationSec=round(DurationMs/1000, 2)
| sort -DurationMs limit=50
```

### Performance Trends (24h)
```spl
index="alummahnow_logs" DurationMs=*
earliest=-24h
| timechart span=1h avg(DurationMs) as avg_ms, max(DurationMs) as max_ms by OperationName
| foreach avg_ms max_ms [eval <<FIELD>>=round(<<FIELD>>, 0)]
```

### Operation Performance Baseline
```spl
index="alummahnow_logs" OperationName=*
| stats avg(DurationMs) as avg_ms,
        median(DurationMs) as median_ms,
        stdev(DurationMs) as stdev_ms,
        count
        by OperationName
| eval threshold_ms=round(avg_ms+(2*stdev_ms), 0)
| table OperationName, avg_ms, median_ms, threshold_ms, count
| sort -avg_ms
```

### API Call Performance
```spl
index="alummahnow_logs" ServiceName="FileScanService" DurationMs=*
| stats avg(DurationMs) as avg_ms,
        max(DurationMs) as max_ms,
        count(eval(DurationMs>5000)) as slow_calls,
        count as total_calls
        by ServiceEndpoint
| eval slow_rate=round((slow_calls/total_calls)*100, 2)
| eval avg_sec=round(avg_ms/1000, 2),
       max_sec=round(max_ms/1000, 2)
| table ServiceEndpoint, avg_sec, max_sec, total_calls, slow_rate
| sort -avg_sec
```

### Memory and Resource Usage (if logged)
```spl
index="alummahnow_logs" (MemoryUsageMB=* OR CpuPercent=*)
| timechart span=5m avg(MemoryUsageMB) as avg_memory, avg(CpuPercent) as avg_cpu
```

## SignalR Connectivity

### Connection Status Over Time
```spl
index="alummahnow_logs" ("SignalR" OR "FileScanSignalRClient") Status=*
| timechart span=5m count by Status
```

### Connection Stability Metrics
```spl
index="alummahnow_logs" Status="Reconnecting" OR Status="Disconnected"
| stats count as connection_issues,
        dc(MachineName) as affected_machines
| append [search index="alummahnow_logs" Status="Connected"
        | stats count as successful_connections]
| stats sum(connection_issues) as issues,
        sum(successful_connections) as successes,
        max(affected_machines) as machines
| eval stability_rate=round((successes/(successes+issues))*100, 2)
```

### Disconnection Root Causes
```spl
index="alummahnow_logs" Status="Disconnected" ErrorMessage=*
| rex field=ErrorMessage "(?<error_type>^[^:]+)"
| stats count by error_type
| sort -count
```

### Average Reconnection Time
```spl
index="alummahnow_logs" (Status="Reconnecting" OR Status="Reconnected")
| transaction MaxMachineName maxspan=5m startswith=(Status="Reconnecting") endswith=(Status="Reconnected")
| eval reconnect_time_sec=round(duration, 2)
| stats avg(reconnect_time_sec) as avg_reconnect,
        max(reconnect_time_sec) as max_reconnect,
        count as reconnect_events
```

## Error Analysis

### Error Dashboard
```spl
index="alummahnow_logs" Level="Error" OR Level="Fatal"
| stats count by ExceptionType, SourceContext
| sort -count limit=20
```

### Error Timeline with Severity
```spl
index="alummahnow_logs" Level="Error" OR Level="Fatal"
| timechart span=1h count by Level
```

### Critical Errors (requires immediate attention)
```spl
index="alummahnow_logs" (Level="Fatal" OR Severity="Critical" OR RequiresImmediateAttention=true)
| table _time, Level, Message, ExceptionType, SourceContext, MachineName
| sort -_time
```

### Error Correlation (multiple errors from same source)
```spl
index="alummahnow_logs" Level="Error"
| bucket _time span=5m
| stats count as error_count by _time, SourceContext
| where error_count>5
| sort -error_count
```

### Exception Call Stack Analysis
```spl
index="alummahnow_logs" ExceptionType=* StackTrace=*
| rex field=StackTrace "at (?<method>[^\(]+)"
| stats count by ExceptionType, method
| sort -count limit=30
```

### Error Rate by Component
```spl
index="alummahnow_logs"
| bucket _time span=1h
| stats count(eval(Level="Error" OR Level="Fatal")) as errors,
        count as total
        by _time, SourceContext
| eval error_rate=round((errors/total)*100, 2)
| where error_rate>5
| table _time, SourceContext, error_rate, errors, total
| sort -error_rate
```

## File Operations

### File Processing Summary
```spl
index="alummahnow_logs" FileOperation=*
| stats count as operations,
        sum(FileSizeBytes) as total_bytes,
        avg(FileSizeBytes) as avg_bytes
        by FileOperation, OperationStatus
| eval total_mb=round(total_bytes/1024/1024, 2),
       avg_mb=round(avg_bytes/1024/1024, 2)
| table FileOperation, OperationStatus, operations, total_mb, avg_mb
| sort -operations
```

### Large Files (>100MB)
```spl
index="alummahnow_logs" FileSizeMB>100
| table _time, FileOperation, FileName, FileSizeMB, OperationStatus, ChannelName, DurationMs
| eval DurationSec=round(DurationMs/1000, 2)
| sort -FileSizeMB limit=50
```

### File Processing Throughput
```spl
index="alummahnow_logs" FileOperation=* FileSizeBytes=*
| bucket _time span=1h
| stats sum(FileSizeBytes) as bytes_processed,
        count as files_processed
        by _time
| eval mb_processed=round(bytes_processed/1024/1024, 2),
       throughput_mbps=round((bytes_processed*8/1024/1024/3600), 2)
| table _time, files_processed, mb_processed, throughput_mbps
```

### File Operation Failures
```spl
index="alummahnow_logs" FileOperation=* OperationStatus="Failed"
| stats count as failures,
        values(ErrorMessage) as error_messages
        by FileOperation, FileName
| sort -failures
```

### File Type Distribution
```spl
index="alummahnow_logs" FileName=*
| rex field=FileName "\.(?<extension>[^\.]+)$"
| stats count by extension
| sort -count
```

## Analytics & Reporting

### Daily Activity Report
```spl
index="alummahnow_logs" earliest=-24h
| bucket _time span=1d
| stats count(eval(EventType="UserAction")) as user_actions,
        count(eval(FileOperation="Approval")) as approvals,
        count(eval(SignalREvent="FileDetected")) as files_detected,
        count(eval(Level="Error")) as errors
        by _time
| convert ctime(_time)
```

### Weekly Summary Report
```spl
index="alummahnow_logs" earliest=-7d
| stats count(eval(FeatureName="WatchManagement" AND ActionName="CreateWatch" AND Status="Success")) as watches_created,
        count(eval(FileOperation="Approval" AND OperationStatus="Approved")) as files_approved,
        count(eval(FileOperation="Approval" AND OperationStatus="Rejected")) as files_rejected,
        count(eval(Level="Error" OR Level="Fatal")) as errors,
        dc(UserId) as active_users,
        sum(eval(FileSizeBytes)) as total_bytes
| eval total_gb=round(total_bytes/1024/1024/1024, 2),
       approval_rate=round((files_approved/(files_approved+files_rejected))*100, 2)
```

### Channel Performance Comparison
```spl
index="alummahnow_logs" ChannelName=*
| stats count(eval(FileOperation="Approval")) as approvals,
        avg(eval(if(FileOperation="Approval", DurationMs, null()))) as avg_approval_time,
        count(eval(SignalREvent="FileDetected")) as files_detected,
        sum(FileSizeBytes) as total_bytes
        by ChannelName
| eval total_mb=round(total_bytes/1024/1024, 2),
       avg_approval_sec=round(avg_approval_time/1000, 2)
| table ChannelName, files_detected, approvals, avg_approval_sec, total_mb
| sort -files_detected
```

### User Productivity Metrics
```spl
index="alummahnow_logs" EventType="UserAction" UserId=*
| stats count as total_actions,
        dc(FeatureName) as features_used,
        count(eval(ActionName="CreateWatch")) as watches_created,
        count(eval(ActionName="Approval")) as approvals_processed
        by UserId
| eval productivity_score=round((total_actions*0.5)+(watches_created*10)+(approvals_processed*2), 0)
| sort -productivity_score
```

## Alerts

### Critical Error Alert
```spl
index="alummahnow_logs" Level="Fatal" OR RequiresImmediateAttention=true
| stats count as critical_errors by ExceptionType, MachineName
| where critical_errors>0
```
**Alert Condition**: Number of Results > 0
**Throttle**: 5 minutes

### High Error Rate Alert
```spl
index="alummahnow_logs" earliest=-15m
| stats count(eval(Level="Error" OR Level="Fatal")) as errors, count as total
| eval error_rate=(errors/total)*100
| where error_rate>10
```
**Alert Condition**: Number of Results > 0
**Throttle**: 15 minutes

### SignalR Disconnection Alert
```spl
index="alummahnow_logs" Status="Disconnected" earliest=-5m
| stats count as disconnections by MachineName
| where disconnections>3
```
**Alert Condition**: Number of Results > 0
**Throttle**: 10 minutes

### Slow Operation Alert
```spl
index="alummahnow_logs" DurationMs>10000 earliest=-15m
| stats count as slow_operations by OperationName
| where slow_operations>5
```
**Alert Condition**: Number of Results > 0
**Throttle**: 15 minutes

### Watch Creation Failure Alert
```spl
index="alummahnow_logs" FeatureName="WatchManagement" ActionName="CreateWatch" Status="Failed" earliest=-30m
| stats count as failures by ErrorMessage
| where failures>3
```
**Alert Condition**: Number of Results > 0
**Throttle**: 30 minutes

### Approval Backlog Alert
```spl
index="alummahnow_logs" SignalREvent="NewFileForApproval" earliest=-1h
| stats count as pending_files
| where pending_files>100
```
**Alert Condition**: Number of Results > 0
**Throttle**: 1 hour

## Custom Saved Searches

Save these searches for quick access:

1. **Application Health**: Overall system status
2. **Watch Activity**: Real-time watch monitoring
3. **Approval Queue**: Current approval backlog
4. **Performance Dashboard**: Key performance metrics
5. **Error Tracker**: Recent errors and trends
6. **User Activity**: User engagement metrics

## Report Scheduling

Schedule these reports for automated delivery:

- **Daily Summary**: Every day at 8 AM
- **Weekly Report**: Every Monday at 9 AM
- **Monthly Analytics**: First day of month at 10 AM
- **Error Digest**: Every 4 hours (if errors > threshold)

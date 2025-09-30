# Testing Strategy for CoreMessagingXP.dll Crash Fix

## Overview
The crash was occurring in `CoreMessagingXP.dll` due to COM/WinRT marshaling issues when UI updates were happening from background threads. We've implemented thread-safe fixes using `DispatcherQueue.TryEnqueue()`.

## Test Scenarios

### 1. Basic Functionality Tests
- ? Build and run the application
- ? Select a folder with video files
- ? Scan directory for files
- ? Start import process
- ? Monitor progress updates

### 2. Stress Testing Scenarios

#### A. Large Directory Scanning
- Test with folders containing 1000+ video files
- Test with deeply nested directory structures (10+ levels)
- Test with folders containing mixed file types
- Test with network drives or slow storage

#### B. Concurrent Operations Testing
- Rapidly click scan/import buttons
- Switch between tabs while operations are running
- Resize window during operations
- Minimize/restore window during operations

#### C. Threading Stress Tests
- Start multiple scans in quick succession
- Cancel operations mid-process
- Switch folders while scanning
- Update UI filters while scanning

#### D. Memory Pressure Testing
- Scan very large directories (10GB+ of files)
- Leave application running for extended periods
- Test with low system memory conditions

### 3. Error Condition Testing

#### A. Service Failures
- Test with invalid configuration
- Test with network connectivity issues
- Test with storage service failures

#### B. File System Issues
- Test with permission denied folders
- Test with locked/in-use files
- Test with corrupted files
- Test with disconnected network drives

## Automated Testing Approach

### Create Test Data
```powershell
# Create test directory structure
New-Item -ItemType Directory -Path "C:\TestVideos\LargeTest" -Force
for ($i = 1; $i -le 1000; $i++) {
    $channel = "Channel" + ($i % 10 + 1)
    $channelPath = "C:\TestVideos\LargeTest\$channel"
    New-Item -ItemType Directory -Path $channelPath -Force
    $fileName = "video$i.mp4"
    # Create dummy files for testing
    New-Item -ItemType File -Path "$channelPath\$fileName" -Force
    "dummy video content $i" | Out-File "$channelPath\$fileName"
}
```

### Performance Monitoring Script
```powershell
# Monitor application during testing
Get-Process "AlUmmahNow.VisualImporter" | Select-Object CPU, WorkingSet, Handles, Threads
```

## Manual Testing Checklist

### Before Fix (Expected Crashes)
- [ ] Rapid clicking during scan operations
- [ ] Window operations during progress updates
- [ ] Multiple concurrent operations

### After Fix (Should Be Stable)
- [ ] All previous crash scenarios work smoothly
- [ ] UI remains responsive during operations
- [ ] Progress updates work correctly
- [ ] Error handling is graceful

## Specific Test Cases

### Test Case 1: Rapid Operation Switching
1. Start scanning a large folder
2. Immediately click "Select All Files"
3. Click "Deselect All Files" rapidly
4. Change sort options while scanning
5. **Expected**: No crashes, operations complete successfully

### Test Case 2: Window Management During Operations
1. Start a file scan
2. Minimize window during scan
3. Restore window
4. Resize window multiple times
5. Switch to other applications and back
6. **Expected**: Progress continues, no crashes

### Test Case 3: Filter Changes During Operations
1. Start scanning a folder with multiple channels
2. Change channel filter while scanning
3. Toggle "Hide Duplicates" option
4. Change sort direction multiple times
5. **Expected**: UI updates safely, no crashes

### Test Case 4: Error Recovery
1. Start scanning a valid folder
2. Disconnect network drive (if applicable)
3. Delete folder during scan
4. **Expected**: Graceful error handling, no crashes

## Monitoring Tools

### Event Viewer
Check Windows Event Viewer for application errors:
- Windows Logs > Application
- Look for errors from "AlUmmahNow.VisualImporter"

### Performance Monitor
Monitor these counters:
- Process\Handle Count
- Process\Thread Count
- Process\Working Set
- .NET CLR Memory\% Time in GC

### Debug Output
Monitor Visual Studio Output Window for:
- Debug messages from error handlers
- Dispatcher queue failures
- Exception logging

## Success Criteria

### Application Stability
- No crashes during any test scenarios
- No COM/WinRT exceptions in Event Viewer
- Memory usage remains stable
- UI remains responsive

### Functionality
- All operations complete successfully
- Progress updates work correctly
- Error messages are displayed properly
- File operations work as expected

## Regression Testing

### After Each Code Change
1. Run build verification
2. Test basic folder selection and scanning
3. Run one stress test scenario
4. Check for new warnings or errors

### Weekly Testing
1. Full stress test suite
2. Performance regression checks
3. Memory leak detection
4. Long-running stability tests

## Emergency Rollback Plan

If crashes reoccur:
1. Check git history for changes since stable version
2. Review any new threading or UI code
3. Ensure all UI updates use `_dispatcherQueue.TryEnqueue()`
4. Verify error handling is comprehensive
5. Consider additional defensive coding patterns
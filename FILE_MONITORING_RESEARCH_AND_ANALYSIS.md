# File Monitoring Systems Research & Analysis Report
## C#/.NET File Monitoring Best Practices and Current Implementation Review

**Generated**: 2025-10-12
**Project**: AlUmmahNowTools
**Focus**: File Monitoring, FileSystemWatcher, and Best Practices

---

## Table of Contents
1. [Executive Summary](#executive-summary)
2. [Current Implementation Analysis](#current-implementation-analysis)
3. [FileSystemWatcher Deep Dive](#filesystemwatcher-deep-dive)
4. [Common Issues and Solutions](#common-issues-and-solutions)
5. [Best Practices for Reliable File Monitoring](#best-practices-for-reliable-file-monitoring)
6. [Alternative Approaches](#alternative-approaches)
7. [Recommendations for This Codebase](#recommendations-for-this-codebase)
8. [Code Examples](#code-examples)
9. [Performance Optimization](#performance-optimization)
10. [References](#references)

---

## Executive Summary

### Current Implementation Overview

The AlUmmahNowTools project implements a **hybrid file monitoring system** combining:
1. **Event-Driven Monitoring** via `FileSystemWatcher` (FileWatchService)
2. **Database-Backed Scanning** via periodic scans (FileScanEngine)
3. **Real-Time Communication** via SignalR for instant updates
4. **Approval Workflow** for manual validation before processing

### Key Findings

**Strengths:**
- ✅ Implements debouncing with configurable delays (5+ seconds default)
- ✅ Proper disposal and resource management
- ✅ Database persistence for watch configurations and approvals
- ✅ Duplicate detection via SHA256 hashing
- ✅ File existence validation before processing
- ✅ Event-driven architecture with real-time updates

**Areas for Improvement:**
- ⚠️ No buffer overflow protection (default 8KB buffer)
- ⚠️ Partial SHA256 hashing for approvals (first 1MB only)
- ⚠️ No retry mechanism for file access failures
- ⚠️ Limited error recovery for watcher failures
- ⚠️ Network path monitoring may be unreliable
- ⚠️ No filtering for temporary files (e.g., `.tmp`, `.part`)

---

## Current Implementation Analysis

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    File Monitoring System                     │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌──────────────────┐         ┌─────────────────────┐       │
│  │ FileWatchService │◄────────┤ FileSystemWatcher   │       │
│  │  (Event-Driven)  │         │ - Created Events    │       │
│  └────────┬─────────┘         │ - Changed Events    │       │
│           │                   │ - Renamed Events    │       │
│           │                   └─────────────────────┘       │
│           │                                                  │
│           ▼                                                  │
│  ┌──────────────────┐                                       │
│  │ Processing Queue │                                       │
│  │  - Timer-based   │                                       │
│  │  - Debouncing    │                                       │
│  └────────┬─────────┘                                       │
│           │                                                  │
│           ▼                                                  │
│  ┌──────────────────┐         ┌─────────────────────┐       │
│  │ ApprovalService  │────────►│ PendingApproval DB  │       │
│  │  - Validation    │         │ - Manual Review     │       │
│  │  - Deduplication │         │ - Auto-Approval     │       │
│  └──────────────────┘         └─────────────────────┘       │
│                                                               │
│  ┌──────────────────┐         ┌─────────────────────┐       │
│  │ FileScanEngine   │────────►│ ScannedVideoFile DB │       │
│  │  (Batch Scan)    │         │ - Full Metadata     │       │
│  │  - Periodic      │         │ - Hash Verification │       │
│  └──────────────────┘         └─────────────────────┘       │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

### Code Analysis: FileWatchService.cs

**Location**: `/home/meywd/AlUmmahNowTools/AlUmmahNow.FileScanService/Services/FileWatchService.cs`

#### Strengths

1. **Debouncing Implementation** (Lines 391-414)
```csharp
// Excellent debouncing pattern to prevent duplicate processing
var fileKey = $"{channelWatch.Id}:{e.FullPath}";
lock (_lockObject)
{
    if (_pendingFiles.TryRemove(fileKey, out var existingTimer))
    {
        existingTimer.Dispose();
    }
    var timer = new Timer(async _ => await ProcessDetectedFileAsync(...),
        null, channelWatch.ProcessingDelayMs, Timeout.Infinite);
    _pendingFiles[fileKey] = timer;
}
```
**Analysis**: This is a solid debouncing approach that prevents processing files that are still being written. Configurable delay (minimum 1 second, default 5 seconds) is appropriate for most use cases.

2. **Resource Management** (Lines 481-505)
```csharp
public void Dispose()
{
    if (!_disposed)
    {
        foreach (var watchId in _watchers.Keys.ToList())
        {
            StopFileSystemWatcher(watchId);
        }
        lock (_lockObject)
        {
            foreach (var timer in _pendingFiles.Values)
            {
                timer.Dispose();
            }
            _pendingFiles.Clear();
        }
        _disposed = true;
    }
}
```
**Analysis**: Proper IDisposable implementation with cleanup of watchers and timers.

3. **Extension Filtering** (Lines 324-329, 382-387)
```csharp
var extensions = channelWatch.FileExtensions
    .Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(ext => ext.Trim().ToLowerInvariant())
    .Where(ext => ext.StartsWith('.'))
    .ToHashSet();

var extension = Path.GetExtension(e.FullPath).ToLowerInvariant();
if (!extensions.Contains(extension))
    return;
```
**Analysis**: Clean and efficient extension filtering using HashSet for O(1) lookup.

#### Weaknesses

1. **No Buffer Overflow Protection** (Line 317)
```csharp
var watcher = new FileSystemWatcher(channelWatch.WatchPath)
{
    IncludeSubdirectories = channelWatch.IncludeSubdirectories,
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
    EnableRaisingEvents = false
};
// Missing: InternalBufferSize configuration!
```

**Issue**: Default buffer size is 8KB. In directories with high file activity, this can overflow causing missed events.

**Impact**: Silent data loss - files may be added without detection.

2. **Limited Error Recovery** (Lines 335-340)
```csharp
watcher.Error += (sender, e) =>
{
    _logger.LogError(e.GetException(), "File watcher error for watch {WatchId}", channelWatch.Id);
    WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
        channelWatch.Id, channelWatch.ChannelName, false, $"Watcher error: {e.GetException()?.Message}"));
};
```

**Issue**: Error handler logs and notifies but doesn't attempt to restart the watcher.

**Impact**: Once a watcher fails, it remains inactive until manually restarted.

3. **No Temporary File Filtering** (Line 382)
```csharp
var extension = Path.GetExtension(e.FullPath).ToLowerInvariant();
if (!extensions.Contains(extension))
    return;
// Missing: Check for .tmp, .part, .crdownload, etc.
```

**Issue**: Doesn't filter out temporary files that may be created during downloads or copies.

**Impact**: May trigger false positives for incomplete files.

### Code Analysis: FileScanEngine.cs

**Location**: `/home/meywd/AlUmmahNowTools/AlUmmahNow.FileScanService/Services/FileScanEngine.cs`

#### Strengths

1. **Comprehensive Metadata Extraction** (Lines 250-351)
```csharp
private async Task ProcessVideoFileAsync(Guid scanSessionId, ScannedChannel channel,
    FileInfo videoFile, CancellationToken cancellationToken)
{
    // Calculate file hash for change detection
    var contentHash = await _hashCalculator.CalculateFileHashAsync(videoFile.FullName, cancellationToken);

    // Find associated files (thumbnails, info files)
    var thumbnailFile = FindThumbnailForVideo(videoFile);
    var infoFile = FindInfoFileForVideo(videoFile);

    // Extract metadata from .info files
    var metadata = await _metadataExtractor.ExtractVideoMetadataAsync(
        videoFile.FullName, infoFile?.FullName, cancellationToken);
}
```
**Analysis**: Thorough file processing with proper metadata extraction and associated file detection.

2. **Change Detection** (Lines 257-270)
```csharp
var existingFile = await _dbContext.ScannedVideoFiles
    .FirstOrDefaultAsync(f => f.ScanSessionId == scanSessionId && f.FilePath == videoFile.FullName, cancellationToken);

var isNewFile = existingFile == null;
var hasChanged = !isNewFile && (existingFile.ContentHash != contentHash ||
                                existingFile.LastKnownModified != videoFile.LastWriteTime);

if (!isNewFile && !hasChanged)
{
    existingFile!.LastScanned = DateTime.UtcNow;
    existingFile.FileExists = true;
    return;
}
```
**Analysis**: Smart change detection using both hash and timestamp comparison.

3. **Duplicate Detection** (Lines 393-436)
```csharp
private async Task DetectDuplicatesAsync(Guid scanSessionId, CancellationToken cancellationToken)
{
    // Group by content hash and file size
    var duplicateGroups = await _dbContext.ScannedVideoFiles
        .Where(f => f.ScanSessionId == scanSessionId && f.FileExists)
        .GroupBy(f => new { f.ContentHash, f.FileSize })
        .Where(g => g.Count() > 1)
        .ToListAsync(cancellationToken);

    foreach (var group in duplicateGroups)
    {
        var files = group.OrderBy(f => f.FilePath).ToList();
        var originalFile = files.First();

        for (int i = 1; i < files.Count; i++)
        {
            files[i].IsDuplicate = true;
            files[i].OriginalFileId = originalFile.Id;
        }
    }
}
```
**Analysis**: Excellent duplicate detection using both hash and size. Groups are processed efficiently.

#### Weaknesses

1. **No File Lock Handling** (Lines 250-351)
```csharp
private async Task ProcessVideoFileAsync(...)
{
    // Direct file access without lock checking
    var contentHash = await _hashCalculator.CalculateFileHashAsync(videoFile.FullName, cancellationToken);
    // May throw IOException if file is locked!
}
```

**Issue**: No retry mechanism or lock detection before file access.

**Impact**: Processing may fail if files are being written or accessed by other processes.

2. **Synchronous Directory Enumeration** (Lines 178-213)
```csharp
private async Task<int> CountDirectoriesAsync(string rootPath, CancellationToken cancellationToken)
{
    return await Task.Run(() =>
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(rootPath));

        while (stack.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var currentDir = stack.Pop();
            count++;
            var subdirs = currentDir.GetDirectories(); // Blocking call!
        }
    }, cancellationToken);
}
```

**Issue**: `GetDirectories()` is synchronous and can block for slow file systems (network paths, USB drives).

**Impact**: UI may become unresponsive during large directory scans.

### Code Analysis: ApprovalService.cs

**Location**: `/home/meywd/AlUmmahNowTools/AlUmmahNow.FileScanService/Services/ApprovalService.cs`

#### Strengths

1. **File Existence Validation** (Lines 171-179)
```csharp
if (request.Decision == ApprovalStatus.Approved || request.Decision == ApprovalStatus.AutoApproved)
{
    approval.FileExists = File.Exists(approval.FilePath);
    if (!approval.FileExists)
    {
        _logger.LogWarning("File no longer exists for approval {ApprovalId}: {FilePath}",
            approval.Id, approval.FilePath);
    }
}
```
**Analysis**: Smart validation that checks file existence before approval. Logs warnings for missing files.

2. **Bulk Operations** (Lines 207-294)
```csharp
public async Task<int> ProcessBulkApprovalAsync(BulkApprovalRequest request, ...)
{
    var query = _dbContext.PendingApprovals
        .Include(p => p.ChannelWatch)
        .Where(p => p.Status == ApprovalStatus.Pending);

    // Apply comprehensive filters
    if (request.ChannelWatchId.HasValue) { /* filter */ }
    if (!string.IsNullOrEmpty(request.ChannelName)) { /* filter */ }
    if (request.DetectedAfter.HasValue) { /* filter */ }
    // ... more filters
}
```
**Analysis**: Flexible bulk approval with extensive filtering capabilities.

#### Weaknesses

1. **Partial Hash Calculation** (Lines 423-442)
```csharp
private async Task<string> CalculateFileHashAsync(string filePath)
{
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);

    // For large files, only hash the first 1MB for performance
    var buffer = new byte[Math.Min(stream.Length, 1024 * 1024)];
    var bytesRead = await stream.ReadAsync(buffer);

    var hashBytes = sha256.ComputeHash(buffer, 0, bytesRead);
    return Convert.ToHexString(hashBytes);
}
```

**Issue**: Only hashes first 1MB for performance, which can lead to false duplicates.

**Impact**: Two different files with identical first 1MB will be marked as duplicates.

**Recommendation**: Use a hybrid approach:
- For files < 100MB: Full file hash
- For files ≥ 100MB: Sample-based hashing (beginning + middle + end)

### Code Analysis: HashCalculator.cs

**Location**: `/home/meywd/AlUmmahNowTools/AlUmmahNow.FileScanService/Services/HashCalculator.cs`

#### Strengths

1. **Full File Hashing** (Lines 19-34)
```csharp
public async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken = default)
{
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);

    var hashBytes = await Task.Run(() => sha256.ComputeHash(stream), cancellationToken);
    return Convert.ToHexString(hashBytes).ToLowerInvariant();
}
```
**Analysis**: Proper full-file hashing with async support and cancellation token.

**Note**: This is used by FileScanEngine for full integrity verification, while ApprovalService uses partial hashing for speed.

---

## FileSystemWatcher Deep Dive

### How FileSystemWatcher Works

FileSystemWatcher is a .NET wrapper around operating system file monitoring APIs:

- **Windows**: Uses `ReadDirectoryChangesW` Win32 API
- **Linux**: Uses `inotify` kernel subsystem
- **macOS**: Uses `FSEvents` framework

### Internal Architecture

```
┌─────────────────────────────────────────────────────────┐
│                  FileSystemWatcher                       │
├─────────────────────────────────────────────────────────┤
│                                                           │
│  ┌────────────────┐           ┌──────────────────┐      │
│  │  File System   │──────────►│  Internal Buffer │      │
│  │  Events (OS)   │           │  (Default: 8KB)  │      │
│  └────────────────┘           └────────┬─────────┘      │
│                                         │                 │
│                                         ▼                 │
│                              ┌──────────────────┐        │
│                              │  Event Queue     │        │
│                              │  (In Memory)     │        │
│                              └────────┬─────────┘        │
│                                       │                   │
│                                       ▼                   │
│                         ┌─────────────────────┐          │
│                         │  Event Handlers     │          │
│                         │  - Created          │          │
│                         │  - Changed          │          │
│                         │  - Deleted          │          │
│                         │  - Renamed          │          │
│                         │  - Error            │          │
│                         └─────────────────────┘          │
│                                                           │
└─────────────────────────────────────────────────────────┘
```

### Pros of FileSystemWatcher

1. **Real-Time Detection**: Near-instant notification of file system changes
2. **Low CPU Usage**: Event-driven, no polling required
3. **Multiple Event Types**: Created, Changed, Deleted, Renamed, Error
4. **Configurable Filters**: File patterns, change types, subdirectories
5. **Native Performance**: Uses OS-level APIs directly

### Cons and Limitations

1. **Buffer Overflow**
   - Default 8KB buffer can overflow with rapid changes
   - Overflow results in `Error` event and missed notifications
   - No built-in recovery mechanism

2. **Duplicate Events**
   - Single file operation may trigger multiple events
   - Example: File save = Created + Changed + Changed
   - No built-in deduplication

3. **Network Path Issues**
   - Unreliable on network shares (UNC paths)
   - May miss events if network connection is unstable
   - Different reliability across SMB/CIFS/NFS

4. **Incomplete File Detection**
   - Events fire immediately when write begins
   - No indication if file is fully written
   - Requires custom logic to wait for completion

5. **Renamed Event Complexity**
   - Provides old and new names but requires special handling
   - May trigger as Delete + Create instead of Rename
   - Cross-directory moves may not trigger Renamed

6. **File Locking**
   - Event fires even if file is locked
   - No built-in check for file availability
   - Requires retry logic or waiting

7. **Performance with Large Trees**
   - Recursive watching can be resource-intensive
   - Each watched directory consumes resources
   - May impact system performance

8. **Platform Differences**
   - Behavior varies between Windows/Linux/macOS
   - Different event patterns and timing
   - Reliability differences

---

## Common Issues and Solutions

### Issue 1: Buffer Overflow and Missed Events

**Problem**: Default 8KB buffer overflows in high-activity directories.

**Symptoms**:
```csharp
watcher.Error += (sender, e) =>
{
    // ErrorEventArgs with InternalBufferOverflowException
    Console.WriteLine($"Buffer overflow! Some events were missed.");
};
```

**Solution 1: Increase Buffer Size**
```csharp
var watcher = new FileSystemWatcher(path)
{
    InternalBufferSize = 64 * 1024, // 64KB (must be multiple of 4KB)
    // Maximum: 64KB on most systems
};
```

**Solution 2: Reduce Event Load**
```csharp
watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size; // Only what you need
watcher.Filter = "*.mp4"; // Specific file types only
```

**Solution 3: Process Events Quickly**
```csharp
watcher.Created += async (sender, e) =>
{
    // Queue for background processing instead of processing inline
    await _eventQueue.EnqueueAsync(e);
};
```

**Recommendation for This Project**:
```csharp
// In FileWatchService.StartFileSystemWatcherAsync
var watcher = new FileSystemWatcher(channelWatch.WatchPath)
{
    IncludeSubdirectories = channelWatch.IncludeSubdirectories,
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
    InternalBufferSize = 64 * 1024, // ADD THIS: Maximum buffer size
    EnableRaisingEvents = false
};
```

### Issue 2: Duplicate Events

**Problem**: Single file operation triggers multiple events (Created, Changed, Changed).

**Current Implementation** (Excellent!):
```csharp
// FileWatchService.OnFileEvent (Lines 391-407)
var fileKey = $"{channelWatch.Id}:{e.FullPath}";
lock (_lockObject)
{
    if (_pendingFiles.TryRemove(fileKey, out var existingTimer))
    {
        existingTimer.Dispose(); // Cancel previous timer
    }
    var timer = new Timer(async _ => await ProcessDetectedFileAsync(...),
        null, channelWatch.ProcessingDelayMs, Timeout.Infinite);
    _pendingFiles[fileKey] = timer;
}
```

**This is a textbook debouncing implementation!** Each new event resets the timer, so only the last event triggers processing.

**Additional Enhancement** (Optional):
```csharp
// Track event counts for debugging
private readonly ConcurrentDictionary<string, int> _eventCounts = new();

private void OnFileEvent(...)
{
    var eventCount = _eventCounts.AddOrUpdate(fileKey, 1, (k, v) => v + 1);
    _logger.LogTrace("File event #{Count} for {FilePath}: {EventType}",
        eventCount, e.FullPath, eventType);
    // ... rest of debouncing logic
}
```

### Issue 3: Renamed Events and File Moves

**Problem**: Renamed events are complex and may trigger as Delete + Create.

**Current Implementation**: Does handle Renamed events (Line 333)
```csharp
watcher.Renamed += (sender, e) => OnFileEvent(channelWatch, e, extensions, "renamed");
```

**Enhancement Recommendation**:
```csharp
watcher.Renamed += (sender, e) =>
{
    if (e is RenamedEventArgs renamedArgs)
    {
        _logger.LogInformation("File renamed: {OldPath} -> {NewPath}",
            renamedArgs.OldFullPath, renamedArgs.FullPath);

        // Option 1: Process as update to existing file
        OnFileEvent(channelWatch, e, extensions, "renamed");

        // Option 2: Check if it's a move between watched directories
        var oldExt = Path.GetExtension(renamedArgs.OldFullPath).ToLowerInvariant();
        if (extensions.Contains(oldExt))
        {
            // Clean up old pending approval if exists
            await RemovePendingApprovalAsync(renamedArgs.OldFullPath);
        }
    }
};
```

### Issue 4: Network Path Monitoring Limitations

**Problem**: FileSystemWatcher is unreliable on network paths (UNC, SMB shares).

**Current Risk**: FileWatchService doesn't check if path is local or network.

**Detection**:
```csharp
private static bool IsNetworkPath(string path)
{
    if (string.IsNullOrEmpty(path)) return false;

    // Check for UNC path (\\server\share)
    if (path.StartsWith(@"\\") || path.StartsWith("//"))
        return true;

    // Check if drive is network mapped
    try
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return false;

        var drive = new DriveInfo(root);
        return drive.DriveType == DriveType.Network;
    }
    catch
    {
        return false;
    }
}
```

**Recommendation for Network Paths**:
```csharp
// In StartWatchAsync
if (IsNetworkPath(request.WatchPath))
{
    _logger.LogWarning("Network path detected: {Path}. File watching may be unreliable.",
        request.WatchPath);

    // Option 1: Fall back to polling
    // Option 2: Warn user and allow with reduced expectations
    // Option 3: Reject network path watches
}
```

### Issue 5: File Locking and Access Timing Issues

**Problem**: Files may be locked when events fire.

**Current Gap**: No retry mechanism for locked files.

**Solution: File Availability Check with Retry**:
```csharp
private async Task<bool> WaitForFileAvailableAsync(string filePath,
    TimeSpan timeout, CancellationToken cancellationToken)
{
    var stopwatch = Stopwatch.StartNew();

    while (stopwatch.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
    {
        try
        {
            // Try to open file with exclusive access
            using var stream = File.Open(filePath, FileMode.Open,
                FileAccess.Read, FileShare.None);
            return true; // File is available
        }
        catch (FileNotFoundException)
        {
            return false; // File doesn't exist
        }
        catch (IOException)
        {
            // File is locked, wait and retry
            await Task.Delay(500, cancellationToken);
        }
    }

    return false; // Timeout
}
```

**Integration Point**:
```csharp
// In ProcessDetectedFileAsync (after line 430)
if (!File.Exists(filePath))
{
    _logger.LogDebug("File no longer exists: {FilePath}", filePath);
    return;
}

// ADD THIS:
var isAvailable = await WaitForFileAvailableAsync(filePath,
    TimeSpan.FromSeconds(30), CancellationToken.None);

if (!isAvailable)
{
    _logger.LogWarning("File not available after 30 seconds: {FilePath}", filePath);
    return;
}

var fileInfo = new FileInfo(filePath);
// ... continue processing
```

### Issue 6: Performance with Large Directory Trees

**Problem**: Recursive watching of large directories can be resource-intensive.

**Current Implementation**: Allows recursive watching (Line 319)
```csharp
IncludeSubdirectories = channelWatch.IncludeSubdirectories
```

**Recommendations**:

1. **Limit Depth**:
```csharp
public class ChannelWatch
{
    public int MaxDepth { get; set; } = -1; // -1 = unlimited
}

// During watch setup
if (channelWatch.MaxDepth > 0)
{
    var depth = GetDirectoryDepth(filePath, channelWatch.WatchPath);
    if (depth > channelWatch.MaxDepth)
    {
        _logger.LogDebug("Ignoring file beyond max depth: {FilePath}", filePath);
        return;
    }
}
```

2. **Exclude Patterns**:
```csharp
public class ChannelWatch
{
    public string ExcludePatterns { get; set; } = ""; // e.g., "**/node_modules/**,**/.git/**"
}

private bool IsExcluded(string filePath, string rootPath, string[] excludePatterns)
{
    var relativePath = Path.GetRelativePath(rootPath, filePath);
    return excludePatterns.Any(pattern =>
        Matcher.Match(pattern, relativePath).HasMatches);
}
```

3. **Resource Monitoring**:
```csharp
// Track watcher count and warn if too many
private const int MaxWatchers = 20;

public async Task<Guid> StartWatchAsync(...)
{
    if (_watchers.Count >= MaxWatchers)
    {
        throw new InvalidOperationException(
            $"Maximum number of watchers ({MaxWatchers}) reached. " +
            "Stop some watches before creating new ones.");
    }
    // ... rest of implementation
}
```

---

## Best Practices for Reliable File Monitoring

### 1. Debouncing (Current Implementation: ✅ Excellent)

**Current Code**:
```csharp
// Timer-based debouncing with configurable delay
var timer = new Timer(async _ => await ProcessDetectedFileAsync(...),
    null, channelWatch.ProcessingDelayMs, Timeout.Infinite);
```

**Best Practice Validation**: ✅ Correct implementation

**Enhancement Options**:
```csharp
// Option A: Progressive delay for repeated events
private int CalculateDelay(string fileKey, int baseDelay)
{
    var eventCount = _eventCounts.GetOrAdd(fileKey, 0);
    return baseDelay * (int)Math.Min(Math.Pow(1.5, eventCount), 10);
}

// Option B: Adaptive delay based on file size
private int CalculateDelayByFileSize(long fileSize, int baseDelay)
{
    if (fileSize < 10_000_000) return baseDelay; // < 10MB
    if (fileSize < 100_000_000) return baseDelay * 2; // < 100MB
    return baseDelay * 5; // Large files
}
```

### 2. File Availability Checking

**Current Gap**: No file availability check before processing.

**Best Practice Implementation**:
```csharp
public static class FileHelper
{
    /// <summary>
    /// Waits for file to become available for reading
    /// </summary>
    public static async Task<bool> WaitForFileReadyAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var delay = TimeSpan.FromMilliseconds(250);

        while (stopwatch.Elapsed < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            try
            {
                // Check if file exists
                if (!File.Exists(filePath))
                    return false;

                // Try to open for exclusive read
                using var stream = File.Open(filePath, FileMode.Open,
                    FileAccess.Read, FileShare.None);

                // Check if file size is stable
                var currentSize = stream.Length;
                await Task.Delay(delay, cancellationToken);

                stream.Position = 0;
                if (stream.Length == currentSize)
                    return true; // File is stable
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                // File is locked or being written to
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, 2000));
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if file size is stable (not growing)
    /// </summary>
    public static async Task<bool> IsFileSizeStableAsync(
        string filePath,
        int checksCount = 3,
        TimeSpan checkInterval = default)
    {
        if (checkInterval == default)
            checkInterval = TimeSpan.FromMilliseconds(500);

        if (!File.Exists(filePath))
            return false;

        long? previousSize = null;

        for (int i = 0; i < checksCount; i++)
        {
            try
            {
                var currentSize = new FileInfo(filePath).Length;

                if (previousSize.HasValue && previousSize.Value != currentSize)
                    return false; // Size is changing

                previousSize = currentSize;
                await Task.Delay(checkInterval);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }
}
```

**Integration**:
```csharp
// In ProcessDetectedFileAsync
private async Task ProcessDetectedFileAsync(ChannelWatch channelWatch, string filePath, string fileKey)
{
    try
    {
        // ... remove timer logic ...

        if (!File.Exists(filePath))
        {
            _logger.LogDebug("File no longer exists: {FilePath}", filePath);
            return;
        }

        // ADD THIS: Wait for file to be ready
        _logger.LogDebug("Waiting for file to be ready: {FilePath}", filePath);
        var isReady = await FileHelper.WaitForFileReadyAsync(filePath,
            TimeSpan.FromSeconds(60));

        if (!isReady)
        {
            _logger.LogWarning("File did not become ready within timeout: {FilePath}", filePath);
            return;
        }

        // Verify size is stable
        var isStable = await FileHelper.IsFileSizeStableAsync(filePath);
        if (!isStable)
        {
            _logger.LogWarning("File size is not stable: {FilePath}", filePath);
            return;
        }

        _logger.LogInformation("File is ready for processing: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        // ... continue with existing logic ...
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing detected file: {FilePath}", filePath);
    }
}
```

### 3. Handling Locked Files Gracefully

**Best Practice Pattern**:
```csharp
public static class FileAccessHelper
{
    public static async Task<T?> RetryOnLockAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 5,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        var delay = initialDelay ?? TimeSpan.FromMilliseconds(250);

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await operation();
            }
            catch (IOException ex) when (i < maxRetries - 1)
            {
                // Check if it's a lock-related exception
                const int ERROR_SHARING_VIOLATION = 32;
                const int ERROR_LOCK_VIOLATION = 33;

                var hResult = ex.HResult & 0xFFFF;
                if (hResult == ERROR_SHARING_VIOLATION || hResult == ERROR_LOCK_VIOLATION)
                {
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                }
                else
                {
                    throw;
                }
            }
        }

        return default;
    }
}

// Usage
var contentHash = await FileAccessHelper.RetryOnLockAsync(async () =>
{
    return await _hashCalculator.CalculateFileHashAsync(filePath, cancellationToken);
}, maxRetries: 5);

if (contentHash == null)
{
    _logger.LogWarning("Could not access file after retries: {FilePath}", filePath);
    return;
}
```

### 4. Recursive Directory Watching Strategies

**Current Implementation**: Simple boolean flag (Line 319)

**Enhanced Strategy**:
```csharp
public enum WatchStrategy
{
    /// <summary>Watch only the root directory</summary>
    RootOnly,

    /// <summary>Watch all subdirectories recursively</summary>
    Recursive,

    /// <summary>Watch up to a specified depth level</summary>
    LimitedDepth,

    /// <summary>Watch specific subdirectories only</summary>
    SelectedFolders
}

public class ChannelWatch
{
    public WatchStrategy Strategy { get; set; } = WatchStrategy.Recursive;
    public int MaxDepth { get; set; } = -1; // For LimitedDepth strategy
    public List<string> SelectedFolders { get; set; } = new(); // For SelectedFolders strategy
}

// Implementation
private bool ShouldProcessFile(string filePath, ChannelWatch watch)
{
    switch (watch.Strategy)
    {
        case WatchStrategy.RootOnly:
            return Path.GetDirectoryName(filePath) == watch.WatchPath;

        case WatchStrategy.LimitedDepth:
            var depth = GetDepth(filePath, watch.WatchPath);
            return depth <= watch.MaxDepth;

        case WatchStrategy.SelectedFolders:
            return watch.SelectedFolders.Any(folder =>
                filePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase));

        case WatchStrategy.Recursive:
        default:
            return true;
    }
}

private int GetDepth(string filePath, string rootPath)
{
    var relativePath = Path.GetRelativePath(rootPath, filePath);
    return relativePath.Count(c => c == Path.DirectorySeparatorChar);
}
```

### 5. Filtering and Ignoring Temporary Files

**Current Gap**: No temporary file filtering.

**Best Practice Implementation**:
```csharp
public static class TemporaryFileFilter
{
    private static readonly HashSet<string> TemporaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".bak", ".crdownload", ".part",
        ".partial", ".!ut", ".download", ".filepart"
    };

    private static readonly HashSet<string> TemporaryPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "~", ".", "_"
    };

    private static readonly string[] TemporaryPatterns =
    {
        @"~\$.*", // Microsoft Office temp files
        @"\.tmp\..+", // Various temp patterns
        @"\.swp$", // Vim swap files
        @"\.~lock\..*" // LibreOffice lock files
    };

    public static bool IsTemporaryFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);

        // Check extension
        if (TemporaryExtensions.Contains(extension))
            return true;

        // Check prefix
        if (TemporaryPrefixes.Any(prefix => fileName.StartsWith(prefix)))
            return true;

        // Check patterns
        foreach (var pattern in TemporaryPatterns)
        {
            if (Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsHiddenOrSystemFile(string filePath)
    {
        try
        {
            var attributes = File.GetAttributes(filePath);
            return attributes.HasFlag(FileAttributes.Hidden) ||
                   attributes.HasFlag(FileAttributes.System);
        }
        catch
        {
            return false;
        }
    }
}

// Integration in OnFileEvent
private void OnFileEvent(ChannelWatch channelWatch, FileSystemEventArgs e,
    HashSet<string> extensions, string eventType)
{
    try
    {
        // Extension check
        var extension = Path.GetExtension(e.FullPath).ToLowerInvariant();
        if (!extensions.Contains(extension))
            return;

        // ADD THIS: Temporary file check
        if (TemporaryFileFilter.IsTemporaryFile(e.FullPath))
        {
            _logger.LogTrace("Ignoring temporary file: {FilePath}", e.FullPath);
            return;
        }

        // ADD THIS: Hidden/system file check
        if (TemporaryFileFilter.IsHiddenOrSystemFile(e.FullPath))
        {
            _logger.LogTrace("Ignoring hidden/system file: {FilePath}", e.FullPath);
            return;
        }

        // ... rest of implementation
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing file event for watch {WatchId}: {FilePath}",
            channelWatch.Id, e.FullPath);
    }
}
```

---

## Alternative Approaches

### 1. Database-Backed File Registry with Periodic Scans

**Already Implemented**: FileScanEngine provides this functionality!

**Architecture**:
```
FileSystemWatcher (Real-time) ──┐
                                 ├──► Combined System
FileScanEngine (Periodic)    ────┘
```

**Advantages**:
- ✅ Catches files missed by watcher
- ✅ Recovers from watcher failures
- ✅ Verifies database matches file system
- ✅ Detects manual file additions

**Current Implementation Analysis**:
- FileScanEngine scans directories and stores in `ScannedVideoFile`
- FileWatchService monitors changes and stores in `PendingApproval`
- **Gap**: No automatic reconciliation between the two systems

**Recommendation: Add Reconciliation Service**:
```csharp
public interface IReconciliationService
{
    /// <summary>
    /// Reconciles watch system with scan results
    /// </summary>
    Task ReconcileAsync(Guid scanSessionId, Guid channelWatchId,
        CancellationToken cancellationToken = default);
}

public class ReconciliationService : IReconciliationService
{
    private readonly ImportDbContext _dbContext;
    private readonly IApprovalService _approvalService;
    private readonly ILogger<ReconciliationService> _logger;

    public async Task ReconcileAsync(Guid scanSessionId, Guid channelWatchId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting reconciliation for scan {ScanId} and watch {WatchId}",
            scanSessionId, channelWatchId);

        // Get watch configuration
        var watch = await _dbContext.ChannelWatches
            .Include(w => w.PendingApprovals)
            .FirstOrDefaultAsync(w => w.Id == channelWatchId, cancellationToken);

        if (watch == null || !watch.IsActive)
        {
            _logger.LogWarning("Watch {WatchId} not found or inactive", channelWatchId);
            return;
        }

        // Get scanned files in watch path
        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == scanSessionId &&
                        f.Directory.StartsWith(watch.WatchPath) &&
                        f.FileExists)
            .ToListAsync(cancellationToken);

        // Find files in scan but not in approvals
        var pendingPaths = watch.PendingApprovals.Select(p => p.FilePath).ToHashSet();
        var missingFiles = scannedFiles.Where(f => !pendingPaths.Contains(f.FilePath)).ToList();

        _logger.LogInformation("Found {Count} files in scan not in approval queue",
            missingFiles.Count);

        // Add missing files to approval queue
        foreach (var file in missingFiles)
        {
            try
            {
                await _approvalService.AddPendingApprovalAsync(channelWatchId, file.FilePath,
                    cancellationToken);
                _logger.LogDebug("Added missing file to approvals: {FilePath}", file.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add file to approvals: {FilePath}", file.FilePath);
            }
        }

        // Find approvals for files that no longer exist
        var scannedPaths = scannedFiles.Select(f => f.FilePath).ToHashSet();
        var staleApprovals = watch.PendingApprovals
            .Where(p => p.Status == ApprovalStatus.Pending && !scannedPaths.Contains(p.FilePath))
            .ToList();

        if (staleApprovals.Any())
        {
            _logger.LogInformation("Found {Count} stale approvals (files no longer exist)",
                staleApprovals.Count);

            foreach (var approval in staleApprovals)
            {
                approval.FileExists = false;
                approval.Status = ApprovalStatus.Expired;
                approval.ApprovalNotes = "File no longer exists (detected during reconciliation)";
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Reconciliation completed for watch {WatchId}", channelWatchId);
    }
}
```

### 2. Hybrid Polling + Event-Driven Systems

**Current State**: Primarily event-driven with manual scans.

**Enhancement: Background Polling Worker**:
```csharp
public class WatchPollingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WatchPollingWorker> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Watch polling worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
                var approvalService = scope.ServiceProvider.GetRequiredService<IApprovalService>();

                // Get all active watches
                var watches = await dbContext.ChannelWatches
                    .Where(w => w.IsActive)
                    .ToListAsync(stoppingToken);

                foreach (var watch in watches)
                {
                    await PollWatchDirectoryAsync(watch, approvalService, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in watch polling worker");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }
    }

    private async Task PollWatchDirectoryAsync(ChannelWatch watch,
        IApprovalService approvalService, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(watch.WatchPath))
            {
                _logger.LogWarning("Watch directory no longer exists: {Path}", watch.WatchPath);
                return;
            }

            var extensions = watch.FileExtensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var searchOption = watch.IncludeSubdirectories
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var directory = new DirectoryInfo(watch.WatchPath);
            var files = directory.GetFiles("*.*", searchOption)
                .Where(f => extensions.Contains(f.Extension))
                .ToList();

            // Get existing pending approvals
            var existingPaths = await _dbContext.PendingApprovals
                .Where(p => p.ChannelWatchId == watch.Id)
                .Select(p => p.FilePath)
                .ToHashSetAsync(cancellationToken);

            // Find new files
            var newFiles = files.Where(f => !existingPaths.Contains(f.FullName)).ToList();

            if (newFiles.Any())
            {
                _logger.LogInformation("Polling found {Count} new files in watch {WatchId}",
                    newFiles.Count, watch.Id);

                foreach (var file in newFiles)
                {
                    await approvalService.AddPendingApprovalAsync(watch.Id, file.FullName,
                        cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling watch directory: {WatchId}", watch.Id);
        }
    }
}

// Register in Program.cs
builder.Services.AddHostedService<WatchPollingWorker>();
```

### 3. Using External File Monitoring Services

**Cloud-Based Options**:
- AWS S3 Event Notifications
- Azure Blob Storage Event Grid
- Cloudflare R2 Event Notifications

**Example: Azure Blob Storage Monitoring**:
```csharp
public class AzureBlobWatchService : IHostedService
{
    private readonly BlobServiceClient _blobClient;
    private readonly IApprovalService _approvalService;
    private readonly ILogger<AzureBlobWatchService> _logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to blob created events
        var eventGridClient = new EventGridPublisherClient(
            new Uri("https://..."),
            new AzureKeyCredential("..."));

        // Azure Event Grid integration
        // Events: Microsoft.Storage.BlobCreated, Microsoft.Storage.BlobDeleted
    }
}
```

**Cloudflare R2 Monitoring**:
```csharp
public class R2WatchService
{
    // R2 supports S3-compatible event notifications
    // Can use AWS SNS/SQS or webhook notifications

    public async Task SetupEventNotificationsAsync(string bucketName)
    {
        var config = new PutBucketNotificationConfigurationRequest
        {
            BucketName = bucketName,
            NotificationConfiguration = new NotificationConfiguration
            {
                QueueConfigurations = new List<QueueConfiguration>
                {
                    new QueueConfiguration
                    {
                        Events = { EventType.ObjectCreated },
                        Queue = "arn:aws:sqs:..."
                    }
                }
            }
        };

        await _s3Client.PutBucketNotificationConfigurationAsync(config);
    }
}
```

### 4. OS-Specific Native APIs

**Windows: ReadDirectoryChangesW (Direct P/Invoke)**:
```csharp
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
private static extern IntPtr FindFirstChangeNotification(
    string lpPathName,
    bool bWatchSubtree,
    uint dwNotifyFilter);

public class WindowsDirectoryWatcher
{
    private const uint FILE_NOTIFY_CHANGE_FILE_NAME = 0x00000001;
    private const uint FILE_NOTIFY_CHANGE_SIZE = 0x00000008;

    // More control over buffer sizes and performance
    // Can exceed FileSystemWatcher's 64KB limit
}
```

**Linux: inotify (Direct System Call)**:
```csharp
public class LinuxInotifyWatcher
{
    [DllImport("libc", SetLastError = true)]
    private static extern int inotify_init();

    [DllImport("libc", SetLastError = true)]
    private static extern int inotify_add_watch(int fd, string pathname, uint mask);

    // Provides more control over Linux file watching
    // Better performance and resource usage
}
```

**Recommendation**: Stick with `FileSystemWatcher` unless you need:
- Buffer sizes > 64KB
- Platform-specific optimizations
- Features not exposed by FileSystemWatcher

---

## Recommendations for This Codebase

### Priority 1: Critical Improvements (Implement Now)

#### 1.1 Add Buffer Overflow Protection
**File**: `FileWatchService.cs` (Line 317)

```csharp
// BEFORE
var watcher = new FileSystemWatcher(channelWatch.WatchPath)
{
    IncludeSubdirectories = channelWatch.IncludeSubdirectories,
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
    EnableRaisingEvents = false
};

// AFTER
var watcher = new FileSystemWatcher(channelWatch.WatchPath)
{
    IncludeSubdirectories = channelWatch.IncludeSubdirectories,
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
    InternalBufferSize = 64 * 1024, // 64KB maximum
    EnableRaisingEvents = false
};
```

#### 1.2 Add Watcher Auto-Recovery
**File**: `FileWatchService.cs` (Lines 335-340)

```csharp
// BEFORE
watcher.Error += (sender, e) =>
{
    _logger.LogError(e.GetException(), "File watcher error for watch {WatchId}", channelWatch.Id);
    WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
        channelWatch.Id, channelWatch.ChannelName, false, $"Watcher error: {e.GetException()?.Message}"));
};

// AFTER
watcher.Error += async (sender, e) =>
{
    var exception = e.GetException();
    _logger.LogError(exception, "File watcher error for watch {WatchId}", channelWatch.Id);

    // Check if it's a buffer overflow
    if (exception is InternalBufferOverflowException)
    {
        _logger.LogWarning("Buffer overflow detected for watch {WatchId}. Some events may have been missed. Attempting to recover...",
            channelWatch.Id);

        // Notify about potential missed events
        WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
            channelWatch.Id, channelWatch.ChannelName, true,
            "Buffer overflow occurred. Some file changes may have been missed. Consider running a full scan."));

        // Trigger a mini-scan to catch missed files
        try
        {
            await ScanWatchDirectoryForMissedFilesAsync(channelWatch);
        }
        catch (Exception scanEx)
        {
            _logger.LogError(scanEx, "Failed to scan for missed files");
        }
    }
    else
    {
        // For other errors, attempt to restart the watcher
        _logger.LogInformation("Attempting to restart watcher {WatchId}", channelWatch.Id);

        try
        {
            StopFileSystemWatcher(channelWatch.Id);
            await Task.Delay(1000); // Brief delay before restart
            await StartFileSystemWatcherAsync(channelWatch);

            WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
                channelWatch.Id, channelWatch.ChannelName, true, "Watcher restarted after error"));
        }
        catch (Exception restartEx)
        {
            _logger.LogError(restartEx, "Failed to restart watcher {WatchId}", channelWatch.Id);
            WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
                channelWatch.Id, channelWatch.ChannelName, false, $"Watcher failed and could not restart: {restartEx.Message}"));
        }
    }
};

// Add new method to scan for missed files
private async Task ScanWatchDirectoryForMissedFilesAsync(ChannelWatch channelWatch)
{
    _logger.LogInformation("Scanning watch directory for potentially missed files: {WatchPath}",
        channelWatch.WatchPath);

    try
    {
        if (!Directory.Exists(channelWatch.WatchPath))
            return;

        var extensions = channelWatch.FileExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant())
            .ToHashSet();

        var searchOption = channelWatch.IncludeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var files = Directory.GetFiles(channelWatch.WatchPath, "*.*", searchOption)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        // Check which files are not in pending approvals
        var existingPaths = await _dbContext.PendingApprovals
            .Where(p => p.ChannelWatchId == channelWatch.Id)
            .Select(p => p.FilePath)
            .ToListAsync();

        var existingSet = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        var missedFiles = files.Where(f => !existingSet.Contains(f)).ToList();

        if (missedFiles.Any())
        {
            _logger.LogWarning("Found {Count} potentially missed files. Adding to pending approvals.",
                missedFiles.Count);

            foreach (var filePath in missedFiles)
            {
                try
                {
                    await _approvalService.AddPendingApprovalAsync(channelWatch.Id, filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add missed file to approvals: {FilePath}", filePath);
                }
            }
        }
        else
        {
            _logger.LogInformation("No missed files found during recovery scan");
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error scanning for missed files in watch {WatchId}", channelWatch.Id);
    }
}
```

#### 1.3 Add Temporary File Filtering
**File**: `FileWatchService.cs` (after Line 387)

```csharp
// Add new helper class
public static class FileFilters
{
    private static readonly HashSet<string> TemporaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".bak", ".crdownload", ".part",
        ".partial", ".!ut", ".download", ".filepart", ".ytdl"
    };

    private static readonly Regex[] TemporaryPatterns =
    {
        new Regex(@"^~\$", RegexOptions.IgnoreCase), // Office temp
        new Regex(@"\.tmp\.", RegexOptions.IgnoreCase), // General temp
        new Regex(@"^\.~lock\.", RegexOptions.IgnoreCase), // LibreOffice lock
        new Regex(@"\.swp$", RegexOptions.IgnoreCase) // Vim swap
    };

    public static bool IsTemporaryFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);

        // Check extension
        if (TemporaryExtensions.Contains(extension))
            return true;

        // Check patterns
        foreach (var pattern in TemporaryPatterns)
        {
            if (pattern.IsMatch(fileName))
                return true;
        }

        // Check for hidden/system files
        try
        {
            var attributes = File.GetAttributes(filePath);
            if (attributes.HasFlag(FileAttributes.Hidden) ||
                attributes.HasFlag(FileAttributes.System))
                return true;
        }
        catch
        {
            // Ignore attribute errors
        }

        return false;
    }
}

// In OnFileEvent method, add after line 387:
if (!extensions.Contains(extension))
{
    return;
}

// ADD THIS:
if (FileFilters.IsTemporaryFile(e.FullPath))
{
    _logger.LogTrace("Ignoring temporary file: {FilePath}", e.FullPath);
    return;
}
```

### Priority 2: High-Value Improvements (Implement Soon)

#### 2.1 Add File Availability Check with Retry
**File**: `FileWatchService.cs` (Lines 416-479)

Add new helper method:
```csharp
private async Task<bool> WaitForFileReadyAsync(string filePath, TimeSpan timeout)
{
    var stopwatch = Stopwatch.StartNew();
    var delay = TimeSpan.FromMilliseconds(250);

    while (stopwatch.Elapsed < timeout)
    {
        try
        {
            // Check if file exists
            if (!File.Exists(filePath))
                return false;

            // Try to open for exclusive read to check if available
            using var stream = File.Open(filePath, FileMode.Open,
                FileAccess.Read, FileShare.None);

            // Check if size is stable
            var initialSize = stream.Length;
            await Task.Delay(500);

            stream.Position = 0;
            if (stream.Length == initialSize)
            {
                _logger.LogDebug("File is ready: {FilePath}", filePath);
                return true;
            }
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            // File is locked or being written
            await Task.Delay(delay);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, 2000));
        }
    }

    return false;
}
```

Integrate into `ProcessDetectedFileAsync` (after line 434):
```csharp
if (!File.Exists(filePath))
{
    _logger.LogDebug("File no longer exists: {FilePath}", filePath);
    return;
}

// ADD THIS:
_logger.LogDebug("Waiting for file to be ready: {FilePath}", filePath);
var isReady = await WaitForFileReadyAsync(filePath, TimeSpan.FromSeconds(60));

if (!isReady)
{
    _logger.LogWarning("File is not ready after 60 seconds: {FilePath}. Skipping.", filePath);
    return;
}
```

#### 2.2 Fix Partial Hash Calculation in ApprovalService
**File**: `ApprovalService.cs` (Lines 423-442)

```csharp
// BEFORE: Only hashes first 1MB
private async Task<string> CalculateFileHashAsync(string filePath)
{
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);

    // For large files, only hash the first 1MB for performance
    var buffer = new byte[Math.Min(stream.Length, 1024 * 1024)];
    var bytesRead = await stream.ReadAsync(buffer);

    var hashBytes = sha256.ComputeHash(buffer, 0, bytesRead);
    return Convert.ToHexString(hashBytes);
}

// AFTER: Use adaptive hashing strategy
private async Task<string> CalculateFileHashAsync(string filePath)
{
    const long SmallFileThreshold = 100 * 1024 * 1024; // 100MB
    const long ChunkSize = 1024 * 1024; // 1MB

    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);
    var fileSize = stream.Length;

    // For small files, hash the entire file
    if (fileSize <= SmallFileThreshold)
    {
        var hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
        return Convert.ToHexString(hashBytes);
    }

    // For large files, use sample-based hashing
    // Sample from beginning, middle, and end
    var sampleBuffer = new byte[ChunkSize * 3];

    // Beginning
    await stream.ReadAsync(sampleBuffer, 0, (int)ChunkSize);

    // Middle
    stream.Seek(fileSize / 2 - ChunkSize / 2, SeekOrigin.Begin);
    await stream.ReadAsync(sampleBuffer, (int)ChunkSize, (int)ChunkSize);

    // End
    stream.Seek(-ChunkSize, SeekOrigin.End);
    await stream.ReadAsync(sampleBuffer, (int)(ChunkSize * 2), (int)ChunkSize);

    // Hash the combined sample
    var hashBytes = sha256.ComputeHash(sampleBuffer);
    return Convert.ToHexString(hashBytes);
}
```

#### 2.3 Add Network Path Detection and Warning
**File**: `FileWatchService.cs` (Lines 34-95)

Add helper method:
```csharp
private static bool IsNetworkPath(string path)
{
    if (string.IsNullOrEmpty(path))
        return false;

    // Check for UNC path
    if (path.StartsWith(@"\\") || path.StartsWith("//"))
        return true;

    // Check if drive is network-mapped
    try
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            return false;

        var drive = new DriveInfo(root);
        return drive.DriveType == DriveType.Network;
    }
    catch
    {
        return false;
    }
}
```

Integrate into `StartWatchAsync` (after line 45):
```csharp
if (!Directory.Exists(request.WatchPath))
{
    throw new DirectoryNotFoundException($"Watch path does not exist: {request.WatchPath}");
}

// ADD THIS:
if (IsNetworkPath(request.WatchPath))
{
    _logger.LogWarning("Network path detected: {WatchPath}. File system monitoring may be unreliable on network shares.",
        request.WatchPath);

    // Consider: Add a flag to ChannelWatch to track this
    // Consider: Fall back to polling-only mode for network paths
}
```

### Priority 3: Enhancement Opportunities (Implement Later)

#### 3.1 Add Reconciliation Service
Create new file: `ReconciliationService.cs`

**Purpose**: Periodically reconcile watched directories with scan results to catch missed files.

**Implementation**: See "Alternative Approaches" section for full code.

#### 3.2 Add Background Polling Worker
Create new file: `WatchPollingWorker.cs`

**Purpose**: Complement FileSystemWatcher with periodic polling to catch missed events.

**Implementation**: See "Alternative Approaches" section for full code.

#### 3.3 Add Watch Strategy Options
**File**: `WatchModels.cs`

**Purpose**: Provide more control over recursive watching (depth limits, selected folders).

**Implementation**: See "Best Practices" section for full code.

#### 3.4 Add Advanced Metrics and Monitoring
Create new file: `WatchMetricsService.cs`

```csharp
public class WatchMetrics
{
    public int TotalEventsReceived { get; set; }
    public int EventsProcessed { get; set; }
    public int EventsSkipped { get; set; }
    public int ErrorsEncountered { get; set; }
    public int BufferOverflows { get; set; }
    public int WatcherRestarts { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public DateTime LastEventTime { get; set; }
}

public interface IWatchMetricsService
{
    Task<WatchMetrics> GetMetricsAsync(Guid watchId);
    Task RecordEventAsync(Guid watchId, string eventType, TimeSpan processingTime);
    Task RecordErrorAsync(Guid watchId, string errorType);
}
```

#### 3.5 Add Configuration for Buffer Size and Delays
**File**: `WatchModels.cs` (extend ChannelWatch)

```csharp
public class ChannelWatch
{
    // ... existing properties ...

    /// <summary>
    /// Internal buffer size for FileSystemWatcher (must be 4KB multiple)
    /// </summary>
    public int InternalBufferSize { get; set; } = 64 * 1024; // 64KB default

    /// <summary>
    /// Maximum file size to process automatically (bytes)
    /// </summary>
    public long MaxAutoProcessFileSize { get; set; } = 10L * 1024 * 1024 * 1024; // 10GB

    /// <summary>
    /// Timeout for waiting for file to be ready (seconds)
    /// </summary>
    public int FileReadyTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Enable auto-recovery from watcher errors
    /// </summary>
    public bool AutoRecovery { get; set; } = true;

    /// <summary>
    /// Enable polling backup for missed events
    /// </summary>
    public bool EnablePollingBackup { get; set; } = false;

    /// <summary>
    /// Polling interval in minutes (if EnablePollingBackup is true)
    /// </summary>
    public int PollingIntervalMinutes { get; set; } = 30;
}
```

---

## Code Examples

### Complete Robust FileSystemWatcher Implementation

Here's a production-ready implementation incorporating all best practices:

```csharp
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Production-ready FileSystemWatcher wrapper with all best practices
    /// </summary>
    public class RobustFileWatcher : IDisposable
    {
        private readonly ILogger<RobustFileWatcher> _logger;
        private FileSystemWatcher? _watcher;
        private readonly ConcurrentDictionary<string, Timer> _pendingFiles = new();
        private readonly ConcurrentDictionary<string, int> _eventCounts = new();
        private readonly object _lockObject = new object();
        private bool _disposed = false;
        private readonly RobustFileWatcherConfig _config;

        public event EventHandler<FileDetectedEventArgs>? FileDetected;
        public event EventHandler<WatcherErrorEventArgs>? WatcherError;

        public RobustFileWatcher(RobustFileWatcherConfig config, ILogger<RobustFileWatcher> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start()
        {
            if (_watcher != null)
                throw new InvalidOperationException("Watcher is already started");

            ValidateConfiguration();

            _watcher = new FileSystemWatcher(_config.WatchPath)
            {
                IncludeSubdirectories = _config.IncludeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = _config.BufferSize,
                EnableRaisingEvents = false
            };

            // Set filter if specific extensions are configured
            if (_config.FileExtensions.Any())
            {
                _watcher.Filter = "*.*"; // We'll filter manually for better control
            }

            // Wire up event handlers
            _watcher.Created += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Error += OnFileSystemError;

            _watcher.EnableRaisingEvents = true;

            _logger.LogInformation("File watcher started for path: {Path}", _config.WatchPath);
        }

        public void Stop()
        {
            if (_watcher == null)
                return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;

            // Clean up pending timers
            lock (_lockObject)
            {
                foreach (var timer in _pendingFiles.Values)
                {
                    timer.Dispose();
                }
                _pendingFiles.Clear();
                _eventCounts.Clear();
            }

            _logger.LogInformation("File watcher stopped for path: {Path}", _config.WatchPath);
        }

        private void ValidateConfiguration()
        {
            if (!Directory.Exists(_config.WatchPath))
                throw new DirectoryNotFoundException($"Watch path does not exist: {_config.WatchPath}");

            if (_config.BufferSize % 4096 != 0)
                throw new ArgumentException("Buffer size must be a multiple of 4KB (4096 bytes)");

            if (_config.BufferSize > 65536)
                throw new ArgumentException("Buffer size cannot exceed 64KB (65536 bytes)");

            if (IsNetworkPath(_config.WatchPath) && !_config.AllowNetworkPaths)
                throw new InvalidOperationException("Network paths are not allowed by configuration");

            if (IsNetworkPath(_config.WatchPath))
            {
                _logger.LogWarning("Network path detected: {Path}. Monitoring may be unreliable.",
                    _config.WatchPath);
            }
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Filter by extension
                if (!ShouldProcessFile(e.FullPath))
                    return;

                // Filter temporary files
                if (IsTemporaryFile(e.FullPath))
                {
                    _logger.LogTrace("Ignoring temporary file: {Path}", e.FullPath);
                    return;
                }

                // Debounce using timer
                DebounceFileEvent(e.FullPath, e.ChangeType.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling file system event: {Path}", e.FullPath);
            }
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                _logger.LogDebug("File renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);

                // Cancel any pending processing for the old path
                if (_pendingFiles.TryRemove(e.OldFullPath, out var oldTimer))
                {
                    oldTimer.Dispose();
                }

                // Process the new path
                if (ShouldProcessFile(e.FullPath))
                {
                    DebounceFileEvent(e.FullPath, "Renamed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling renamed event: {Path}", e.FullPath);
            }
        }

        private void OnFileSystemError(object sender, ErrorEventArgs e)
        {
            var exception = e.GetException();
            _logger.LogError(exception, "FileSystemWatcher error occurred");

            if (exception is InternalBufferOverflowException)
            {
                _logger.LogError("Buffer overflow! Some file events were missed. Consider increasing buffer size or reducing event load.");

                if (_config.AutoRecover)
                {
                    Task.Run(async () => await RecoverFromBufferOverflowAsync());
                }
            }
            else if (_config.AutoRecover)
            {
                Task.Run(async () => await RestartWatcherAsync());
            }

            WatcherError?.Invoke(this, new WatcherErrorEventArgs(exception));
        }

        private void DebounceFileEvent(string filePath, string eventType)
        {
            var fileKey = filePath.ToLowerInvariant();

            lock (_lockObject)
            {
                // Track event count
                var eventCount = _eventCounts.AddOrUpdate(fileKey, 1, (k, v) => v + 1);
                _logger.LogTrace("File event #{Count} for {Path}: {Type}",
                    eventCount, filePath, eventType);

                // Cancel existing timer
                if (_pendingFiles.TryRemove(fileKey, out var existingTimer))
                {
                    existingTimer.Dispose();
                }

                // Calculate delay (can be adaptive based on event count)
                var delay = CalculateDelay(eventCount);

                // Create new timer
                var timer = new Timer(async _ => await ProcessFileAsync(filePath, fileKey),
                    null, delay, Timeout.Infinite);

                _pendingFiles[fileKey] = timer;
            }
        }

        private int CalculateDelay(int eventCount)
        {
            // Progressive delay: more events = longer delay
            // Base delay * exponential factor, capped at max
            var factor = Math.Min(Math.Pow(1.2, eventCount - 1), 5.0);
            return (int)(_config.ProcessingDelayMs * factor);
        }

        private async Task ProcessFileAsync(string filePath, string fileKey)
        {
            try
            {
                // Clean up timer
                lock (_lockObject)
                {
                    if (_pendingFiles.TryRemove(fileKey, out var timer))
                    {
                        timer.Dispose();
                    }
                    _eventCounts.TryRemove(fileKey, out _);
                }

                // Validate file exists
                if (!File.Exists(filePath))
                {
                    _logger.LogDebug("File no longer exists: {Path}", filePath);
                    return;
                }

                // Wait for file to be ready
                if (!await WaitForFileReadyAsync(filePath, _config.FileReadyTimeout))
                {
                    _logger.LogWarning("File not ready after timeout: {Path}", filePath);
                    return;
                }

                // Get file info
                var fileInfo = new FileInfo(filePath);

                // Check max file size
                if (fileInfo.Length > _config.MaxFileSize)
                {
                    _logger.LogWarning("File exceeds max size ({Size} > {MaxSize}): {Path}",
                        fileInfo.Length, _config.MaxFileSize, filePath);
                    return;
                }

                _logger.LogInformation("Processing file: {Path} ({Size} bytes)",
                    filePath, fileInfo.Length);

                // Raise event
                FileDetected?.Invoke(this, new FileDetectedEventArgs
                {
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    CreatedTime = fileInfo.CreationTime,
                    ModifiedTime = fileInfo.LastWriteTime,
                    DetectedTime = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {Path}", filePath);
            }
        }

        private async Task<bool> WaitForFileReadyAsync(string filePath, TimeSpan timeout)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var delay = TimeSpan.FromMilliseconds(250);

            while (stopwatch.Elapsed < timeout)
            {
                try
                {
                    if (!File.Exists(filePath))
                        return false;

                    // Try to open exclusively
                    using var stream = File.Open(filePath, FileMode.Open,
                        FileAccess.Read, FileShare.None);

                    // Check size stability
                    var size1 = stream.Length;
                    await Task.Delay(500);

                    stream.Position = 0;
                    if (stream.Length == size1)
                        return true;
                }
                catch (FileNotFoundException)
                {
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
                catch (IOException)
                {
                    await Task.Delay(delay);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, 2000));
                }
            }

            return false;
        }

        private bool ShouldProcessFile(string filePath)
        {
            if (!_config.FileExtensions.Any())
                return true;

            var extension = Path.GetExtension(filePath);
            return _config.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private bool IsTemporaryFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath);

            // Check temporary extensions
            var tempExtensions = new[] { ".tmp", ".temp", ".bak", ".crdownload", ".part", ".partial" };
            if (tempExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return true;

            // Check temporary prefixes
            if (fileName.StartsWith("~") || fileName.StartsWith("."))
                return true;

            // Check hidden/system attributes
            try
            {
                var attributes = File.GetAttributes(filePath);
                return attributes.HasFlag(FileAttributes.Hidden) ||
                       attributes.HasFlag(FileAttributes.System);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNetworkPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (path.StartsWith(@"\\") || path.StartsWith("//"))
                return true;

            try
            {
                var root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root))
                    return false;

                var drive = new DriveInfo(root);
                return drive.DriveType == DriveType.Network;
            }
            catch
            {
                return false;
            }
        }

        private async Task RecoverFromBufferOverflowAsync()
        {
            _logger.LogInformation("Attempting to recover from buffer overflow...");

            try
            {
                // Perform a quick scan of the directory to find missed files
                if (!Directory.Exists(_config.WatchPath))
                    return;

                var searchOption = _config.IncludeSubdirectories
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                var recentFiles = Directory.GetFiles(_config.WatchPath, "*.*", searchOption)
                    .Where(f => ShouldProcessFile(f))
                    .Select(f => new FileInfo(f))
                    .Where(fi => fi.LastWriteTime >= DateTime.Now.AddMinutes(-5)) // Last 5 minutes
                    .ToList();

                _logger.LogInformation("Found {Count} recently modified files during recovery",
                    recentFiles.Count);

                foreach (var file in recentFiles)
                {
                    DebounceFileEvent(file.FullName, "Recovery");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during buffer overflow recovery");
            }
        }

        private async Task RestartWatcherAsync()
        {
            _logger.LogInformation("Attempting to restart file watcher...");

            try
            {
                Stop();
                await Task.Delay(1000); // Brief delay
                Start();

                _logger.LogInformation("File watcher restarted successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart file watcher");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }

    // Configuration class
    public class RobustFileWatcherConfig
    {
        public string WatchPath { get; set; } = string.Empty;
        public bool IncludeSubdirectories { get; set; } = true;
        public int BufferSize { get; set; } = 64 * 1024; // 64KB
        public int ProcessingDelayMs { get; set; } = 5000; // 5 seconds
        public TimeSpan FileReadyTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public long MaxFileSize { get; set; } = 10L * 1024 * 1024 * 1024; // 10GB
        public bool AutoRecover { get; set; } = true;
        public bool AllowNetworkPaths { get; set; } = false;
        public HashSet<string> FileExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // Event args classes
    public class FileDetectedEventArgs : EventArgs
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime ModifiedTime { get; set; }
        public DateTime DetectedTime { get; set; }
    }

    public class WatcherErrorEventArgs : EventArgs
    {
        public Exception? Exception { get; set; }

        public WatcherErrorEventArgs(Exception? exception)
        {
            Exception = exception;
        }
    }
}
```

### Usage Example

```csharp
// Configure
var config = new RobustFileWatcherConfig
{
    WatchPath = @"C:\Videos",
    IncludeSubdirectories = true,
    BufferSize = 64 * 1024,
    ProcessingDelayMs = 5000,
    FileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov"
    },
    AutoRecover = true,
    AllowNetworkPaths = false
};

// Create watcher
using var watcher = new RobustFileWatcher(config, logger);

// Subscribe to events
watcher.FileDetected += async (sender, args) =>
{
    logger.LogInformation("New file detected: {Path}", args.FilePath);
    // Process the file...
};

watcher.WatcherError += (sender, args) =>
{
    logger.LogError(args.Exception, "Watcher error occurred");
};

// Start monitoring
watcher.Start();

// ... application runs ...

// Stop when done (or Dispose will handle it)
watcher.Stop();
```

---

## Performance Optimization

### 1. Buffer Size Optimization

**Problem**: Default 8KB buffer can overflow easily.

**Recommendation**:
```csharp
// For low-activity directories (< 10 files/sec)
InternalBufferSize = 16 * 1024; // 16KB

// For medium-activity directories (10-50 files/sec)
InternalBufferSize = 32 * 1024; // 32KB

// For high-activity directories (> 50 files/sec)
InternalBufferSize = 64 * 1024; // 64KB (maximum)
```

**Monitoring**:
```csharp
private int _totalEvents = 0;
private DateTime _lastReset = DateTime.UtcNow;

watcher.Created += (s, e) =>
{
    Interlocked.Increment(ref _totalEvents);

    var elapsed = DateTime.UtcNow - _lastReset;
    if (elapsed.TotalSeconds >= 10)
    {
        var rate = _totalEvents / elapsed.TotalSeconds;
        _logger.LogInformation("Event rate: {Rate:F1} events/sec", rate);

        if (rate > 100)
        {
            _logger.LogWarning("High event rate detected. Consider increasing buffer size or reducing scope.");
        }

        _totalEvents = 0;
        _lastReset = DateTime.UtcNow;
    }
};
```

### 2. Event Processing Optimization

**Problem**: Processing events synchronously in handlers blocks the watcher.

**Solution**: Queue-based processing
```csharp
using System.Threading.Channels;

public class FileEventQueue
{
    private readonly Channel<FileEvent> _channel;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    public FileEventQueue(int capacity, ILogger logger)
    {
        _channel = Channel.CreateBounded<FileEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _logger = logger;

        // Start processing loop
        _ = ProcessEventsAsync(_cts.Token);
    }

    public async Task EnqueueAsync(FileEvent fileEvent)
    {
        if (!await _channel.Writer.WaitToWriteAsync())
        {
            _logger.LogWarning("Event queue is closed");
            return;
        }

        if (!_channel.Writer.TryWrite(fileEvent))
        {
            _logger.LogWarning("Event queue is full, oldest event dropped");
        }
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (var fileEvent in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ProcessEventAsync(fileEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing event for file: {Path}", fileEvent.FilePath);
            }
        }
    }

    private async Task ProcessEventAsync(FileEvent fileEvent)
    {
        // Do expensive processing here without blocking watcher
        await Task.Delay(100); // Simulated work
        _logger.LogInformation("Processed: {Path}", fileEvent.FilePath);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.Complete();
    }
}

public record FileEvent(string FilePath, string EventType, DateTime Timestamp);
```

### 3. Hash Calculation Optimization

**Problem**: SHA256 is expensive for large files.

**Strategies**:

**Strategy A: Partial Hashing**
```csharp
public async Task<string> CalculatePartialHashAsync(string filePath, long sampleSize = 1024 * 1024)
{
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);

    var bufferSize = (int)Math.Min(stream.Length, sampleSize);
    var buffer = new byte[bufferSize];

    await stream.ReadAsync(buffer, 0, bufferSize);

    var hash = sha256.ComputeHash(buffer);
    return Convert.ToHexString(hash);
}
```

**Strategy B: Streaming Hash (Memory Efficient)**
```csharp
public async Task<string> CalculateStreamingHashAsync(string filePath,
    CancellationToken cancellationToken = default)
{
    const int BufferSize = 81920; // 80KB buffer

    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);
    var buffer = new byte[BufferSize];
    int bytesRead;

    while ((bytesRead = await stream.ReadAsync(buffer, 0, BufferSize, cancellationToken)) > 0)
    {
        sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
    }

    sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
    return Convert.ToHexString(sha256.Hash!);
}
```

**Strategy C: Adaptive Hashing**
```csharp
public async Task<string> CalculateAdaptiveHashAsync(string filePath)
{
    var fileInfo = new FileInfo(filePath);
    var fileSize = fileInfo.Length;

    // Small files: Full hash
    if (fileSize < 100 * 1024 * 1024) // < 100MB
    {
        return await CalculateStreamingHashAsync(filePath);
    }

    // Medium files: Sample-based
    if (fileSize < 1024 * 1024 * 1024) // < 1GB
    {
        return await CalculateSampleHashAsync(filePath, samples: 5);
    }

    // Large files: Quick hash (beginning + end)
    return await CalculateQuickHashAsync(filePath);
}

private async Task<string> CalculateSampleHashAsync(string filePath, int samples)
{
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);
    var sampleSize = 1024 * 1024; // 1MB per sample
    var buffer = new byte[sampleSize * samples];
    var step = stream.Length / (samples + 1);

    for (int i = 0; i < samples; i++)
    {
        stream.Seek(step * (i + 1), SeekOrigin.Begin);
        var offset = i * sampleSize;
        await stream.ReadAsync(buffer, offset, sampleSize);
    }

    var hash = sha256.ComputeHash(buffer);
    return Convert.ToHexString(hash);
}

private async Task<string> CalculateQuickHashAsync(string filePath)
{
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);
    var sampleSize = 1024 * 1024; // 1MB
    var buffer = new byte[sampleSize * 2];

    // Read beginning
    await stream.ReadAsync(buffer, 0, sampleSize);

    // Read end
    stream.Seek(-sampleSize, SeekOrigin.End);
    await stream.ReadAsync(buffer, sampleSize, sampleSize);

    var hash = sha256.ComputeHash(buffer);
    return Convert.ToHexString(hash);
}
```

### 4. Database Query Optimization

**Problem**: Frequent database queries for duplicate checks.

**Solution A: Caching**
```csharp
public class CachedApprovalService
{
    private readonly IApprovalService _approvalService;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(10);

    public async Task<bool> IsDuplicateAsync(string contentHash)
    {
        var cacheKey = $"duplicate:{contentHash}";

        if (_cache.TryGetValue(cacheKey, out bool isDuplicate))
        {
            return isDuplicate;
        }

        // Query database
        isDuplicate = await _approvalService.HasPendingApprovalWithHashAsync(contentHash);

        // Cache result
        _cache.Set(cacheKey, isDuplicate, _cacheExpiration);

        return isDuplicate;
    }
}
```

**Solution B: Batch Processing**
```csharp
public class BatchApprovalProcessor
{
    private readonly Channel<PendingApproval> _approvalQueue;
    private readonly ImportDbContext _dbContext;
    private const int BatchSize = 100;
    private const int BatchDelayMs = 1000;

    public async Task ProcessBatchedAsync(CancellationToken cancellationToken)
    {
        var batch = new List<PendingApproval>(BatchSize);
        var timer = new Timer(BatchDelayMs);

        await foreach (var approval in _approvalQueue.Reader.ReadAllAsync(cancellationToken))
        {
            batch.Add(approval);

            if (batch.Count >= BatchSize || timer.ElapsedMilliseconds >= BatchDelayMs)
            {
                await SaveBatchAsync(batch);
                batch.Clear();
                timer.Restart();
            }
        }

        // Save remaining
        if (batch.Any())
        {
            await SaveBatchAsync(batch);
        }
    }

    private async Task SaveBatchAsync(List<PendingApproval> batch)
    {
        _dbContext.PendingApprovals.AddRange(batch);
        await _dbContext.SaveChangesAsync();
    }
}
```

### 5. Memory Optimization

**Problem**: Large directory trees can consume significant memory.

**Solution: Streaming Directory Enumeration**
```csharp
public static IEnumerable<FileInfo> EnumerateFilesOptimized(
    string path,
    string pattern,
    SearchOption searchOption)
{
    var enumOptions = new EnumerationOptions
    {
        RecurseSubdirectories = searchOption == SearchOption.AllDirectories,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
    };

    // Uses yield return for lazy evaluation (streaming)
    foreach (var file in Directory.EnumerateFiles(path, pattern, enumOptions))
    {
        FileInfo? fileInfo = null;
        try
        {
            fileInfo = new FileInfo(file);
        }
        catch (Exception)
        {
            // Skip inaccessible files
            continue;
        }

        if (fileInfo != null)
        {
            yield return fileInfo;
        }
    }
}

// Usage - processes one file at a time without loading all into memory
foreach (var file in EnumerateFilesOptimized(@"C:\Videos", "*.mp4", SearchOption.AllDirectories))
{
    await ProcessFileAsync(file);
}
```

---

## References

### Official Documentation
- [FileSystemWatcher Class (Microsoft Docs)](https://docs.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher)
- [ReadDirectoryChangesW function (Win32)](https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-readdirectorychangesw)
- [inotify - monitoring filesystem events (Linux)](https://man7.org/linux/man-pages/man7/inotify.7.html)

### Best Practices Articles
- [Robust FileSystemWatcher Implementation (CodeProject)](https://www.codeproject.com/Articles/58740/FileSystemWatcher-Pure-Chaos-Part-of)
- [FileSystemWatcher Gotchas (Stack Overflow)](https://stackoverflow.com/questions/1764809/filesystemwatcher-changed-event-is-raised-twice)
- [.NET Performance Tips (Microsoft)](https://docs.microsoft.com/en-us/dotnet/framework/performance/performance-tips)

### Related Technologies
- [System.IO.Pipelines](https://docs.microsoft.com/en-us/dotnet/standard/io/pipelines) - High-performance I/O
- [System.Threading.Channels](https://docs.microsoft.com/en-us/dotnet/api/system.threading.channels) - Producer-consumer queues
- [Polly](https://github.com/App-vNext/Polly) - Resilience and retry policies

### SHA256 Hashing Performance
- [SHA256 Performance on Different File Sizes](https://stackoverflow.com/questions/1177607/what-is-the-fastest-way-to-create-a-checksum-for-large-files-in-c-sharp)
- [Incremental Hashing with TransformBlock](https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.hashalgorithm.transformblock)

### Alternative Libraries
- [Watcher (NuGet)](https://www.nuget.org/packages/Watcher/) - Simplified FileSystemWatcher wrapper
- [FileSystemWatcherEx (NuGet)](https://www.nuget.org/packages/FileSystemWatcherEx/) - Enhanced FileSystemWatcher
- [Rx.NET](https://github.com/dotnet/reactive) - Reactive Extensions for event stream processing

---

## Conclusion

This codebase already implements many file monitoring best practices, particularly:
- Excellent debouncing implementation
- Proper resource management and disposal
- Database-backed persistence
- Real-time SignalR communication
- Duplicate detection
- Comprehensive error logging

The main areas for improvement are:
1. **Critical**: Add buffer overflow protection (64KB buffer)
2. **Critical**: Implement auto-recovery from watcher errors
3. **High**: Add temporary file filtering
4. **High**: Implement file availability checks with retry
5. **Medium**: Fix partial hashing in ApprovalService
6. **Medium**: Add network path detection and warnings
7. **Enhancement**: Add reconciliation between watch and scan systems
8. **Enhancement**: Add background polling as a backup

By implementing these improvements, the file monitoring system will be production-ready with enterprise-level reliability and performance.

---

**End of Report**

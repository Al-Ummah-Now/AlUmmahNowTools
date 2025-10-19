using System.Collections.Concurrent;
using System.Security.Cryptography;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.FileScanService.Services;

/// <summary>
/// Service for managing file system watchers and detecting new files
/// </summary>
public class FileWatchService : IFileWatchService, IDisposable
{
    private readonly ILogger<FileWatchService> _logger;
    private readonly ImportDbContext _dbContext;
    private readonly IApprovalService _approvalService;
    private readonly ConcurrentDictionary<Guid, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<Guid, ChannelWatch> _watchConfigs = new();
    private readonly ConcurrentDictionary<string, Timer> _pendingFiles = new();
    private readonly object _lockObject = new object();
    private bool _disposed = false;

    /// <summary>
    /// Maximum buffer size for FileSystemWatcher (64KB).
    /// This is the Windows API limit and helps prevent InternalBufferOverflowException.
    /// </summary>
    internal const int MaxWatcherBufferSize = 64 * 1024;

    public event EventHandler<FileDetectedEventArgs>? FileDetected;
    public event EventHandler<WatchStatusChangedEventArgs>? WatchStatusChanged;

    public FileWatchService(ILogger<FileWatchService> logger, ImportDbContext dbContext, IApprovalService approvalService)
    {
        _logger = logger;
        _dbContext = dbContext;
        _approvalService = approvalService;
    }

    public async Task<Guid> StartWatchAsync(CreateWatchRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Entered StartWatchAsync for channel {ChannelName} at path {WatchPath}",
            request.ChannelName, request.WatchPath);

        try
        {
            // Validate the watch path
            if (!Directory.Exists(request.WatchPath))
            {
                throw new DirectoryNotFoundException($"Watch path does not exist: {request.WatchPath}");
            }

            // Check if already watching this path
            var existingWatch = await _dbContext.ChannelWatches
                .FirstOrDefaultAsync(w => w.WatchPath == request.WatchPath && w.IsActive, cancellationToken);

            if (existingWatch != null)
            {
                _logger.LogWarning("Already watching path {WatchPath} for channel {ChannelName}", 
                    request.WatchPath, existingWatch.ChannelName);
                return existingWatch.Id;
            }

            // Create watch configuration
            var channelWatch = new ChannelWatch
            {
                ChannelName = request.ChannelName,
                WatchPath = request.WatchPath,
                IncludeSubdirectories = request.IncludeSubdirectories,
                ProcessingDelayMs = Math.Max(1000, request.ProcessingDelayMs), // Minimum 1 second
                FileExtensions = request.FileExtensions ?? ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm",
                CreatedBy = request.CreatedBy,
                IsActive = true
            };

            _logger.LogDebug("Creating watch configuration for channel {ChannelName} at path {WatchPath}",
                request.ChannelName, request.WatchPath);
            _dbContext.ChannelWatches.Add(channelWatch);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Watch configuration persisted with ID {WatchId}", channelWatch.Id);

            // Start the file system watcher
            _logger.LogDebug("Starting file system watcher for watch {WatchId}", channelWatch.Id);
            await StartFileSystemWatcherAsync(channelWatch);

            _logger.LogInformation("Successfully started watch {WatchId} for channel {ChannelName}", 
                channelWatch.Id, request.ChannelName);

            WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
                channelWatch.Id, request.ChannelName, true, "Watch started"));

            _logger.LogInformation("Exiting StartWatchAsync successfully. Watch ID: {WatchId}", channelWatch.Id);
            return channelWatch.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exiting StartWatchAsync with error for channel {ChannelName} at path {WatchPath}",
                request.ChannelName, request.WatchPath);
            throw;
        }
    }

    public async Task<bool> StopWatchAsync(Guid watchId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Entered StopWatchAsync for watch {WatchId}", watchId);

        try
        {
            var channelWatch = await _dbContext.ChannelWatches
                .FindAsync(new object[] { watchId }, cancellationToken);

            if (channelWatch == null)
            {
                _logger.LogWarning("Watch {WatchId} not found, exiting StopWatchAsync with result: false", watchId);
                return false;
            }

            // Stop the file system watcher
            StopFileSystemWatcher(watchId);

            // Update database
            _logger.LogDebug("Updating watch {WatchId} status to inactive", watchId);
            channelWatch.IsActive = false;
            channelWatch.LastUpdated = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully stopped watch {WatchId} for channel {ChannelName}",
                watchId, channelWatch.ChannelName);

            WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
                watchId, channelWatch.ChannelName, false, "Watch stopped"));

            _logger.LogInformation("Exiting StopWatchAsync successfully. Result: true");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exiting StopWatchAsync with error for watch {WatchId}. Result: false", watchId);
            return false;
        }
    }

    public async Task<List<WatchStatusDto>> GetActiveWatchesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entered GetActiveWatchesAsync");

        try
        {
            _logger.LogTrace("Querying database for active watches");
            var watches = await _dbContext.ChannelWatches
                .Where(w => w.IsActive)
                .Select(w => new WatchStatusDto
                {
                    WatchId = w.Id,
                    ChannelName = w.ChannelName,
                    WatchPath = w.WatchPath,
                    IsActive = w.IsActive,
                    CreatedAt = w.CreatedAt,
                    LastFileDetected = w.LastFileDetected,
                    TotalFilesDetected = w.TotalFilesDetected,
                    PendingApprovals = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.Pending),
                    AutoApproved = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.AutoApproved),
                    ManuallyApproved = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.Approved),
                    Rejected = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.Rejected),
                    Imported = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.ImportCompleted),
                    Failed = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.ImportFailed)
                })
                .ToListAsync(cancellationToken);

            _logger.LogDebug("Exiting GetActiveWatchesAsync. Found {WatchCount} active watches", watches.Count);
            return watches;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exiting GetActiveWatchesAsync with error. Returning empty list");
            return new List<WatchStatusDto>();
        }
    }

    public async Task<WatchStatusDto?> GetWatchStatusAsync(Guid watchId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entered GetWatchStatusAsync for watch {WatchId}", watchId);

        try
        {
            _logger.LogTrace("Querying database for watch {WatchId} status", watchId);
            var watch = await _dbContext.ChannelWatches
                .Where(w => w.Id == watchId)
                .Select(w => new WatchStatusDto
                {
                    WatchId = w.Id,
                    ChannelName = w.ChannelName,
                    WatchPath = w.WatchPath,
                    IsActive = w.IsActive,
                    CreatedAt = w.CreatedAt,
                    LastFileDetected = w.LastFileDetected,
                    TotalFilesDetected = w.TotalFilesDetected,
                    PendingApprovals = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.Pending),
                    AutoApproved = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.AutoApproved),
                    ManuallyApproved = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.Approved),
                    Rejected = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.Rejected),
                    Imported = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.ImportCompleted),
                    Failed = w.PendingApprovals.Count(p => p.Status == ApprovalStatus.ImportFailed)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var found = watch != null;
            _logger.LogDebug("Exiting GetWatchStatusAsync for watch {WatchId}. Found: {Found}", watchId, found);
            return watch;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exiting GetWatchStatusAsync with error for watch {WatchId}. Returning null", watchId);
            return null;
        }
    }

    public async Task<bool> UpdateWatchAsync(Guid watchId, CreateWatchRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Entered UpdateWatchAsync for watch {WatchId}", watchId);

        try
        {
            var channelWatch = await _dbContext.ChannelWatches
                .FindAsync(new object[] { watchId }, cancellationToken);

            if (channelWatch == null)
            {
                _logger.LogWarning("Watch {WatchId} not found, exiting UpdateWatchAsync with result: false", watchId);
                return false;
            }

            // Stop current watcher if path is changing
            var pathChanged = channelWatch.WatchPath != request.WatchPath;
            if (pathChanged && channelWatch.IsActive)
            {
                _logger.LogDebug("Watch path changed from {OldPath} to {NewPath}, stopping current watcher",
                    channelWatch.WatchPath, request.WatchPath);
                StopFileSystemWatcher(watchId);
            }

            // Update configuration
            _logger.LogDebug("Updating watch {WatchId} configuration", watchId);
            channelWatch.ChannelName = request.ChannelName;
            channelWatch.WatchPath = request.WatchPath;
            channelWatch.IncludeSubdirectories = request.IncludeSubdirectories;
            channelWatch.ProcessingDelayMs = Math.Max(1000, request.ProcessingDelayMs);
            channelWatch.FileExtensions = request.FileExtensions ?? channelWatch.FileExtensions;
            channelWatch.LastUpdated = DateTime.UtcNow;

            _logger.LogDebug("Persisting watch {WatchId} configuration changes", watchId);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Restart watcher if it was active and path changed
            if (pathChanged && channelWatch.IsActive)
            {
                _logger.LogDebug("Restarting file system watcher for watch {WatchId} with new path", watchId);
                await StartFileSystemWatcherAsync(channelWatch);
            }

            _logger.LogInformation("Successfully updated watch {WatchId}", watchId);

            WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
                watchId, request.ChannelName, channelWatch.IsActive, "Watch updated"));

            _logger.LogInformation("Exiting UpdateWatchAsync successfully. Result: true");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exiting UpdateWatchAsync with error for watch {WatchId}. Result: false", watchId);
            return false;
        }
    }

    public async Task<bool> DeleteWatchAsync(Guid watchId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Entered DeleteWatchAsync for watch {WatchId}", watchId);

        try
        {
            _logger.LogTrace("Loading watch {WatchId} with pending approvals", watchId);
            var channelWatch = await _dbContext.ChannelWatches
                .Include(w => w.PendingApprovals)
                .FirstOrDefaultAsync(w => w.Id == watchId, cancellationToken);

            if (channelWatch == null)
            {
                _logger.LogWarning("Watch {WatchId} not found, exiting DeleteWatchAsync with result: false", watchId);
                return false;
            }

            // Stop the watcher
            _logger.LogDebug("Stopping file system watcher for watch {WatchId}", watchId);
            StopFileSystemWatcher(watchId);

            // Remove from database (cascade delete will handle pending approvals)
            _logger.LogDebug("Removing watch {WatchId} from database ({PendingCount} pending approvals will be cascade deleted)",
                watchId, channelWatch.PendingApprovals.Count);
            _dbContext.ChannelWatches.Remove(channelWatch);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully deleted watch {WatchId} for channel {ChannelName}",
                watchId, channelWatch.ChannelName);

            WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
                watchId, channelWatch.ChannelName, false, "Watch deleted"));

            _logger.LogInformation("Exiting DeleteWatchAsync successfully. Result: true");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exiting DeleteWatchAsync with error for watch {WatchId}. Result: false", watchId);
            return false;
        }
    }

    private Task StartFileSystemWatcherAsync(ChannelWatch channelWatch)
    {
        try
        {
            var watcher = new FileSystemWatcher(channelWatch.WatchPath)
            {
                IncludeSubdirectories = channelWatch.IncludeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                EnableRaisingEvents = false,
                // Set to maximum buffer size to prevent InternalBufferOverflowException
                // This helps handle scenarios with rapid file changes
                InternalBufferSize = MaxWatcherBufferSize
            };

            // Parse file extensions
            var extensions = channelWatch.FileExtensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ext => ext.Trim().ToLowerInvariant())
                .Where(ext => ext.StartsWith('.'))
                .ToHashSet();

            watcher.Created += (sender, e) => OnFileEvent(channelWatch, e, extensions, "created");
            watcher.Changed += (sender, e) => OnFileEvent(channelWatch, e, extensions, "changed");
            watcher.Renamed += (sender, e) => OnFileEvent(channelWatch, e, extensions, "renamed");

            watcher.Error += (sender, e) => HandleFileWatcherError(channelWatch, e.GetException());

            _watchers[channelWatch.Id] = watcher;
            _watchConfigs[channelWatch.Id] = channelWatch;
            
            watcher.EnableRaisingEvents = true;

            _logger.LogInformation("Started file system watcher for watch {WatchId} at path {WatchPath}", 
                channelWatch.Id, channelWatch.WatchPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start file system watcher for watch {WatchId}", channelWatch.Id);
            throw;
        }

        return Task.CompletedTask;
    }

    private void StopFileSystemWatcher(Guid watchId)
    {
        try
        {
            if (_watchers.TryRemove(watchId, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _logger.LogInformation("Stopped file system watcher for watch {WatchId}", watchId);
            }

            _watchConfigs.TryRemove(watchId, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping file system watcher for watch {WatchId}", watchId);
        }
    }

    /// <summary>
    /// Handles errors from FileSystemWatcher. Exposed as internal for testing.
    /// </summary>
    internal void HandleFileWatcherError(ChannelWatch channelWatch, Exception? exception)
    {
        // Check if this is a buffer overflow exception
        if (exception is InternalBufferOverflowException)
        {
            _logger.LogError(exception,
                "FileSystemWatcher buffer overflow for watch {WatchId}. " +
                "File changes occurring faster than they can be processed. " +
                "Some file change notifications may have been lost. " +
                "Consider reducing activity in the watched directory or increasing the ProcessingDelayMs.",
                channelWatch.Id);

            // Notify about buffer overflow but don't stop the watcher
            WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
                channelWatch.Id,
                channelWatch.ChannelName,
                true,
                "Buffer overflow - some file notifications may have been missed"));
        }
        else
        {
            _logger.LogError(exception, "File watcher error for watch {WatchId}", channelWatch.Id);
            WatchStatusChanged?.Invoke(this, new WatchStatusChangedEventArgs(
                channelWatch.Id, channelWatch.ChannelName, false, $"Watcher error: {exception?.Message}"));
        }
    }

    private void OnFileEvent(ChannelWatch channelWatch, FileSystemEventArgs e, HashSet<string> extensions, string eventType)
    {
        try
        {
            // Check if file extension matches what we're watching
            var extension = Path.GetExtension(e.FullPath).ToLowerInvariant();
            if (!extensions.Contains(extension))
            {
                return;
            }

            _logger.LogDebug("File {EventType}: {FilePath} for watch {WatchId}", eventType, e.FullPath, channelWatch.Id);

            // Use a delayed processing approach to avoid processing files that are still being written
            var fileKey = $"{channelWatch.Id}:{e.FullPath}";
            
            lock (_lockObject)
            {
                // Cancel any existing timer for this file
                if (_pendingFiles.TryRemove(fileKey, out var existingTimer))
                {
                    existingTimer.Dispose();
                }

                // Create a new timer for delayed processing
                var timer = new Timer(async _ => await ProcessDetectedFileAsync(channelWatch, e.FullPath, fileKey), 
                    null, channelWatch.ProcessingDelayMs, Timeout.Infinite);
                
                _pendingFiles[fileKey] = timer;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file event for watch {WatchId}: {FilePath}", 
                channelWatch.Id, e.FullPath);
        }
    }

    private async Task ProcessDetectedFileAsync(ChannelWatch channelWatch, string filePath, string fileKey)
    {
        try
        {
            // Remove the timer
            lock (_lockObject)
            {
                if (_pendingFiles.TryRemove(fileKey, out var timer))
                {
                    timer.Dispose();
                }
            }

            // Check if file still exists and is accessible
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("File no longer exists: {FilePath}", filePath);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            var fileName = fileInfo.Name;
            var fileSize = fileInfo.Length;

            _logger.LogInformation("Processing detected file: {FileName} ({FileSize} bytes) for channel {ChannelName}", 
                fileName, fileSize, channelWatch.ChannelName);

            // Check if this file is already in pending approvals
            var existingApproval = await _dbContext.PendingApprovals
                .AnyAsync(p => p.FilePath == filePath && p.Status == ApprovalStatus.Pending);

            if (existingApproval)
            {
                _logger.LogDebug("File already has pending approval: {FilePath}", filePath);
                return;
            }

            // Update watch statistics
            _logger.LogDebug("Updating watch {WatchId} statistics for detected file: {FileName}",
                channelWatch.Id, fileName);
            channelWatch.LastFileDetected = DateTime.UtcNow;
            channelWatch.TotalFilesDetected++;
            channelWatch.LastUpdated = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            _logger.LogTrace("Watch {WatchId} statistics updated. Total files detected: {TotalFiles}",
                channelWatch.Id, channelWatch.TotalFilesDetected);

            // Add to pending approvals
            _logger.LogDebug("Adding file to pending approvals: {FilePath}", filePath);
            var approvalId = await _approvalService.AddPendingApprovalAsync(channelWatch.Id, filePath);

            // Raise file detected event
            FileDetected?.Invoke(this, new FileDetectedEventArgs(
                channelWatch.Id, channelWatch.ChannelName, filePath, fileName, fileSize, DateTime.UtcNow));

            _logger.LogInformation("Added file to pending approvals: {FileName} (Approval ID: {ApprovalId})", 
                fileName, approvalId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing detected file: {FilePath} for watch {WatchId}", 
                filePath, channelWatch.Id);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing FileWatchService");

            // Stop all watchers
            foreach (var watchId in _watchers.Keys.ToList())
            {
                StopFileSystemWatcher(watchId);
            }

            // Dispose pending file timers
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
}
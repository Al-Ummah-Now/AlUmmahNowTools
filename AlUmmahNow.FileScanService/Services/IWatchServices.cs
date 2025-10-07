using AlUmmahNow.Data.Models.FileScanning;

namespace AlUmmahNow.FileScanService.Services;

/// <summary>
/// Service for managing file system watchers and detecting new files
/// </summary>
public interface IFileWatchService
{
    /// <summary>
    /// Start watching a channel directory for new files
    /// </summary>
    Task<Guid> StartWatchAsync(CreateWatchRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop watching a channel directory
    /// </summary>
    Task<bool> StopWatchAsync(Guid watchId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all active watches
    /// </summary>
    Task<List<WatchStatusDto>> GetActiveWatchesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get watch status by ID
    /// </summary>
    Task<WatchStatusDto?> GetWatchStatusAsync(Guid watchId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update watch configuration
    /// </summary>
    Task<bool> UpdateWatchAsync(Guid watchId, CreateWatchRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete a watch and all associated data
    /// </summary>
    Task<bool> DeleteWatchAsync(Guid watchId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Event raised when new files are detected
    /// </summary>
    event EventHandler<FileDetectedEventArgs> FileDetected;
    
    /// <summary>
    /// Event raised when watch status changes
    /// </summary>
    event EventHandler<WatchStatusChangedEventArgs> WatchStatusChanged;
}

/// <summary>
/// Service for managing file approval workflow
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Get all pending approvals
    /// </summary>
    Task<List<PendingApprovalDto>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get pending approvals for a specific channel
    /// </summary>
    Task<List<PendingApprovalDto>> GetPendingApprovalsByChannelAsync(string channelName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get pending approval by ID
    /// </summary>
    Task<PendingApprovalDto?> GetPendingApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Make approval decisions for files
    /// </summary>
    Task<bool> ProcessApprovalDecisionAsync(ApprovalDecisionRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Bulk approve/reject files based on criteria
    /// </summary>
    Task<int> ProcessBulkApprovalAsync(BulkApprovalRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Add a new file for approval (called by watch service)
    /// </summary>
    Task<Guid> AddPendingApprovalAsync(Guid channelWatchId, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clean up expired approvals
    /// </summary>
    Task<int> CleanupExpiredApprovalsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get approval statistics
    /// </summary>
    Task<ApprovalStatistics> GetApprovalStatisticsAsync(Guid? channelWatchId = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Event raised when approval status changes
    /// </summary>
    event EventHandler<ApprovalStatusChangedEventArgs> ApprovalStatusChanged;
}

/// <summary>
/// Event arguments for file detection
/// </summary>
public class FileDetectedEventArgs : EventArgs
{
    public Guid ChannelWatchId { get; }
    public string ChannelName { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public long FileSize { get; }
    public DateTime DetectedAt { get; }
    
    public FileDetectedEventArgs(Guid channelWatchId, string channelName, string filePath, string fileName, long fileSize, DateTime detectedAt)
    {
        ChannelWatchId = channelWatchId;
        ChannelName = channelName;
        FilePath = filePath;
        FileName = fileName;
        FileSize = fileSize;
        DetectedAt = detectedAt;
    }
}

/// <summary>
/// Event arguments for watch status changes
/// </summary>
public class WatchStatusChangedEventArgs : EventArgs
{
    public Guid WatchId { get; }
    public string ChannelName { get; }
    public bool IsActive { get; }
    public string? StatusMessage { get; }
    
    public WatchStatusChangedEventArgs(Guid watchId, string channelName, bool isActive, string? statusMessage = null)
    {
        WatchId = watchId;
        ChannelName = channelName;
        IsActive = isActive;
        StatusMessage = statusMessage;
    }
}

/// <summary>
/// Event arguments for approval status changes
/// </summary>
public class ApprovalStatusChangedEventArgs : EventArgs
{
    public Guid ApprovalId { get; }
    public Guid ChannelWatchId { get; }
    public string ChannelName { get; }
    public string FileName { get; }
    public ApprovalStatus OldStatus { get; }
    public ApprovalStatus NewStatus { get; }
    public string? ApprovedBy { get; }
    
    public ApprovalStatusChangedEventArgs(Guid approvalId, Guid channelWatchId, string channelName, string fileName, 
        ApprovalStatus oldStatus, ApprovalStatus newStatus, string? approvedBy = null)
    {
        ApprovalId = approvalId;
        ChannelWatchId = channelWatchId;
        ChannelName = channelName;
        FileName = fileName;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        ApprovedBy = approvedBy;
    }
}

/// <summary>
/// Approval statistics
/// </summary>
public class ApprovalStatistics
{
    public int TotalPending { get; set; }
    public int TotalApproved { get; set; }
    public int TotalRejected { get; set; }
    public int TotalAutoApproved { get; set; }
    public int TotalExpired { get; set; }
    public int TotalImported { get; set; }
    public int TotalFailed { get; set; }
    public DateTime? OldestPending { get; set; }
    public DateTime? NewestPending { get; set; }
    public long TotalSizePending { get; set; }
    public long TotalSizeApproved { get; set; }
    
    public string TotalSizePendingFormatted => FormatFileSize(TotalSizePending);
    public string TotalSizeApprovedFormatted => FormatFileSize(TotalSizeApproved);
    
    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
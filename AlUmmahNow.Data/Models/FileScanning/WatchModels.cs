using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlUmmahNow.Data.Models.FileScanning;

/// <summary>
/// Represents a watch configuration for monitoring channel directories
/// </summary>
public class ChannelWatch
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Channel name being watched
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string ChannelName { get; set; } = string.Empty;
    
    /// <summary>
    /// Directory path being monitored
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public string WatchPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this watch is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Include subdirectories in the watch
    /// </summary>
    public bool IncludeSubdirectories { get; set; } = true;
    
    /// <summary>
    /// Delay in milliseconds before processing detected files (to avoid incomplete writes)
    /// </summary>
    public int ProcessingDelayMs { get; set; } = 5000; // 5 seconds default
    
    /// <summary>
    /// When this watch was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this watch was last updated
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// User who created this watch
    /// </summary>
    [MaxLength(256)]
    public string? CreatedBy { get; set; }
    
    /// <summary>
    /// Last time a file was detected in this watch
    /// </summary>
    public DateTime? LastFileDetected { get; set; }
    
    /// <summary>
    /// Total number of files detected by this watch
    /// </summary>
    public int TotalFilesDetected { get; set; } = 0;
    
    /// <summary>
    /// File extensions to watch for (comma-separated, e.g., ".mp4,.mkv,.avi")
    /// </summary>
    [MaxLength(500)]
    public string FileExtensions { get; set; } = ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm";
    
    /// <summary>
    /// Navigation property for pending approvals
    /// </summary>
    public virtual ICollection<PendingApproval> PendingApprovals { get; set; } = new List<PendingApproval>();
}

/// <summary>
/// Represents a file awaiting user approval for import
/// </summary>
public class PendingApproval
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Reference to the channel watch that detected this file
    /// </summary>
    public Guid ChannelWatchId { get; set; }
    
    /// <summary>
    /// Full path to the detected file
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// File name only
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }
    
    /// <summary>
    /// File extension
    /// </summary>
    [MaxLength(10)]
    public string Extension { get; set; } = string.Empty;
    
    /// <summary>
    /// When the file was first detected
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the file was last modified (from file system)
    /// </summary>
    public DateTime FileModifiedAt { get; set; }
    
    /// <summary>
    /// Current approval status
    /// </summary>
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    
    /// <summary>
    /// When the approval decision was made
    /// </summary>
    public DateTime? ApprovedAt { get; set; }
    
    /// <summary>
    /// User who made the approval decision
    /// </summary>
    [MaxLength(256)]
    public string? ApprovedBy { get; set; }
    
    /// <summary>
    /// Approval decision notes
    /// </summary>
    [MaxLength(1000)]
    public string? ApprovalNotes { get; set; }
    
    /// <summary>
    /// Whether this file still exists on disk
    /// </summary>
    public bool FileExists { get; set; } = true;
    
    /// <summary>
    /// SHA256 hash of the file content for duplicate detection
    /// </summary>
    [MaxLength(64)]
    public string? ContentHash { get; set; }
    
    /// <summary>
    /// Path to associated thumbnail if found
    /// </summary>
    [MaxLength(2000)]
    public string? ThumbnailPath { get; set; }
    
    /// <summary>
    /// Priority level for processing (1=highest, 5=lowest)
    /// </summary>
    public int Priority { get; set; } = 3;
    
    /// <summary>
    /// Auto-expiry date for pending approvals
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
    
    /// <summary>
    /// Navigation property to the channel watch
    /// </summary>
    [ForeignKey(nameof(ChannelWatchId))]
    public virtual ChannelWatch ChannelWatch { get; set; } = null!;
}

/// <summary>
/// Approval status enumeration
/// </summary>
public enum ApprovalStatus
{
    /// <summary>
    /// Awaiting user decision
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// Approved for import
    /// </summary>
    Approved = 1,
    
    /// <summary>
    /// Rejected - will not be imported
    /// </summary>
    Rejected = 2,
    
    /// <summary>
    /// Automatically approved based on rules
    /// </summary>
    AutoApproved = 3,
    
    /// <summary>
    /// Expired without decision
    /// </summary>
    Expired = 4,
    
    /// <summary>
    /// Import in progress
    /// </summary>
    ImportStarted = 5,
    
    /// <summary>
    /// Successfully imported
    /// </summary>
    ImportCompleted = 6,
    
    /// <summary>
    /// Import failed
    /// </summary>
    ImportFailed = 7
}

/// <summary>
/// DTO for watch status information
/// </summary>
public class WatchStatusDto
{
    public Guid WatchId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string WatchPath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastFileDetected { get; set; }
    public int TotalFilesDetected { get; set; }
    public int PendingApprovals { get; set; }
    public int AutoApproved { get; set; }
    public int ManuallyApproved { get; set; }
    public int Rejected { get; set; }
    public int Imported { get; set; }
    public int Failed { get; set; }
}

/// <summary>
/// DTO for pending approval information
/// </summary>
public class PendingApprovalDto
{
    public Guid Id { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Extension { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public DateTime FileModifiedAt { get; set; }
    public ApprovalStatus Status { get; set; }
    public int Priority { get; set; }
    public bool FileExists { get; set; }
    public string? ThumbnailPath { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string FileSizeFormatted => FormatFileSize(FileSize);
    
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

/// <summary>
/// Request for creating a new channel watch
/// </summary>
public class CreateWatchRequest
{
    [Required]
    public string ChannelName { get; set; } = string.Empty;
    
    [Required]
    public string WatchPath { get; set; } = string.Empty;
    
    public bool IncludeSubdirectories { get; set; } = true;
    public int ProcessingDelayMs { get; set; } = 5000;
    public string? FileExtensions { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Request for approving/rejecting files
/// </summary>
public class ApprovalDecisionRequest
{
    [Required]
    public List<Guid> ApprovalIds { get; set; } = new List<Guid>();
    
    [Required]
    public ApprovalStatus Decision { get; set; }
    
    public string? Notes { get; set; }
    public string? ApprovedBy { get; set; }
}

/// <summary>
/// Bulk approval request
/// </summary>
public class BulkApprovalRequest
{
    public Guid? ChannelWatchId { get; set; }
    public string? ChannelName { get; set; }
    public ApprovalStatus Decision { get; set; }
    public DateTime? DetectedAfter { get; set; }
    public DateTime? DetectedBefore { get; set; }
    public List<string>? FileExtensions { get; set; }
    public long? MinFileSize { get; set; }
    public long? MaxFileSize { get; set; }
    public string? Notes { get; set; }
    public string? ApprovedBy { get; set; }
}
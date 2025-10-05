using System;
using System.ComponentModel.DataAnnotations;

namespace AlUmmahNow.Data.Models.FileScanning
{
    /// <summary>
    /// Represents a file scanning session
    /// </summary>
    public class ScanSession
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Root folder path being scanned
        /// </summary>
        [Required]
        public string RootPath { get; set; } = string.Empty;
        
        /// <summary>
        /// Display name for the scan session
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Current status of the scan
        /// </summary>
        public ScanStatus Status { get; set; } = ScanStatus.Pending;
        
        /// <summary>
        /// When the scan was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// When the scan was started
        /// </summary>
        public DateTime? StartedAt { get; set; }
        
        /// <summary>
        /// When the scan was completed
        /// </summary>
        public DateTime? CompletedAt { get; set; }
        
        /// <summary>
        /// Current progress percentage (0-100)
        /// </summary>
        public double ProgressPercentage { get; set; } = 0;
        
        /// <summary>
        /// Current operation being performed
        /// </summary>
        public string? CurrentOperation { get; set; }
        
        /// <summary>
        /// Number of directories processed
        /// </summary>
        public int DirectoriesProcessed { get; set; } = 0;
        
        /// <summary>
        /// Total directories found
        /// </summary>
        public int TotalDirectories { get; set; } = 0;
        
        /// <summary>
        /// Number of files found
        /// </summary>
        public int FilesFound { get; set; } = 0;
        
        /// <summary>
        /// Total size of all files found (in bytes)
        /// </summary>
        public long TotalSize { get; set; } = 0;
        
        /// <summary>
        /// Error message if scan failed
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// Last update timestamp
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Hash of the root path for quick lookup
        /// </summary>
        public string PathHash { get; set; } = string.Empty;
        
        /// <summary>
        /// Version of the scan (incremented on rescans)
        /// </summary>
        public int Version { get; set; } = 1;
        
        // Navigation properties
        public virtual ICollection<ScannedChannel> ScannedChannels { get; set; } = new List<ScannedChannel>();
        public virtual ICollection<ScannedVideoFile> ScannedVideoFiles { get; set; } = new List<ScannedVideoFile>();
    }
    
    /// <summary>
    /// Status of a scan session
    /// </summary>
    public enum ScanStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4,
        Paused = 5
    }
}
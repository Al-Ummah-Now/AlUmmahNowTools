using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlUmmahNow.Data.Models.FileScanning
{
    /// <summary>
    /// Represents a channel/folder with aggregate statistics
    /// </summary>
    public class ScannedChannel
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Foreign key to the scan session
        /// </summary>
        public Guid ScanSessionId { get; set; }
        
        /// <summary>
        /// Navigation property to the scan session
        /// </summary>
        [ForeignKey(nameof(ScanSessionId))]
        public ScanSession ScanSession { get; set; } = null!;
        
        /// <summary>
        /// Channel name
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Full path to the channel directory
        /// </summary>
        [Required]
        public string Path { get; set; } = string.Empty;
        
        /// <summary>
        /// Relative path from scan root
        /// </summary>
        public string RelativePath { get; set; } = string.Empty;
        
        /// <summary>
        /// Total number of video files in this channel
        /// </summary>
        public int TotalFiles { get; set; } = 0;
        
        /// <summary>
        /// Total size of all files in bytes
        /// </summary>
        public long TotalSize { get; set; } = 0;
        
        /// <summary>
        /// Number of unique (non-duplicate) files
        /// </summary>
        public int UniqueFiles { get; set; } = 0;
        
        /// <summary>
        /// Size of unique files in bytes
        /// </summary>
        public long UniqueSize { get; set; } = 0;
        
        /// <summary>
        /// Number of duplicate files
        /// </summary>
        public int DuplicateFiles { get; set; } = 0;
        
        /// <summary>
        /// Size of duplicate files in bytes
        /// </summary>
        public long DuplicateSize { get; set; } = 0;
        
        /// <summary>
        /// Number of files with thumbnails
        /// </summary>
        public int FilesWithThumbnails { get; set; } = 0;
        
        /// <summary>
        /// Number of files with .info files
        /// </summary>
        public int FilesWithInfo { get; set; } = 0;
        
        /// <summary>
        /// Number of files ready for import
        /// </summary>
        public int ReadyForImport { get; set; } = 0;
        
        /// <summary>
        /// Number of files already imported
        /// </summary>
        public int ImportedFiles { get; set; } = 0;
        
        /// <summary>
        /// Number of files failed to import
        /// </summary>
        public int FailedFiles { get; set; } = 0;
        
        /// <summary>
        /// Average file size in bytes
        /// </summary>
        public double AverageFileSize { get; set; } = 0;
        
        /// <summary>
        /// Largest file size in bytes
        /// </summary>
        public long LargestFileSize { get; set; } = 0;
        
        /// <summary>
        /// Smallest file size in bytes
        /// </summary>
        public long SmallestFileSize { get; set; } = 0;
        
        /// <summary>
        /// Total duration of all videos in seconds (if available)
        /// </summary>
        public double? TotalDurationSeconds { get; set; }
        
        /// <summary>
        /// Hash for quick change detection
        /// </summary>
        public string ContentHash { get; set; } = string.Empty;
        
        /// <summary>
        /// When the channel was first scanned
        /// </summary>
        public DateTime FirstScanned { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// When the channel was last updated
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Whether the channel directory still exists
        /// </summary>
        public bool DirectoryExists { get; set; } = true;
        
        /// <summary>
        /// Number of subdirectories in this channel
        /// </summary>
        public int SubdirectoryCount { get; set; } = 0;
        
        /// <summary>
        /// Depth level from scan root (0 = root level)
        /// </summary>
        public int DepthLevel { get; set; } = 0;
        
        /// <summary>
        /// Parent channel ID if this is a subchannel
        /// </summary>
        public Guid? ParentChannelId { get; set; }
        
        /// <summary>
        /// Navigation property to parent channel
        /// </summary>
        [ForeignKey(nameof(ParentChannelId))]
        public ScannedChannel? ParentChannel { get; set; }
    }
}
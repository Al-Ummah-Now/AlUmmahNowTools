using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlUmmahNow.Data.Models.FileScanning
{
    /// <summary>
    /// Represents a scanned video file with all its metadata
    /// </summary>
    public class ScannedVideoFile
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
        /// Full file path
        /// </summary>
        [Required]
        public string FilePath { get; set; } = string.Empty;
        
        /// <summary>
        /// File name with extension
        /// </summary>
        [Required]
        public string FileName { get; set; } = string.Empty;
        
        /// <summary>
        /// Directory containing the file
        /// </summary>
        [Required]
        public string Directory { get; set; } = string.Empty;
        
        /// <summary>
        /// Channel name (derived from folder structure)
        /// </summary>
        [Required]
        public string ChannelName { get; set; } = string.Empty;
        
        /// <summary>
        /// Relative path from scan root
        /// </summary>
        public string RelativePath { get; set; } = string.Empty;
        
        /// <summary>
        /// File extension (lowercase)
        /// </summary>
        public string Extension { get; set; } = string.Empty;
        
        /// <summary>
        /// File size in bytes
        /// </summary>
        public long FileSize { get; set; }
        
        /// <summary>
        /// File creation date
        /// </summary>
        public DateTime CreatedDate { get; set; }
        
        /// <summary>
        /// File last modified date
        /// </summary>
        public DateTime ModifiedDate { get; set; }
        
        /// <summary>
        /// Hash of file content and metadata for change detection
        /// </summary>
        public string ContentHash { get; set; } = string.Empty;
        
        /// <summary>
        /// Metadata hash including .info files and thumbnails
        /// </summary>
        public string MetadataHash { get; set; } = string.Empty;
        
        /// <summary>
        /// Path to thumbnail file if exists
        /// </summary>
        public string? ThumbnailPath { get; set; }
        
        /// <summary>
        /// Thumbnail file size in bytes
        /// </summary>
        public long? ThumbnailSize { get; set; }
        
        /// <summary>
        /// Hash of thumbnail file
        /// </summary>
        public string? ThumbnailHash { get; set; }
        
        /// <summary>
        /// Path to .info file if exists
        /// </summary>
        public string? InfoFilePath { get; set; }
        
        /// <summary>
        /// Content of .info file (JSON)
        /// </summary>
        public string? InfoFileContent { get; set; }
        
        /// <summary>
        /// Hash of .info file content
        /// </summary>
        public string? InfoFileHash { get; set; }
        
        /// <summary>
        /// Video duration in seconds (if available from info)
        /// </summary>
        public double? DurationSeconds { get; set; }
        
        /// <summary>
        /// Video resolution width
        /// </summary>
        public int? Width { get; set; }
        
        /// <summary>
        /// Video resolution height
        /// </summary>
        public int? Height { get; set; }
        
        /// <summary>
        /// Video bitrate (if available)
        /// </summary>
        public long? Bitrate { get; set; }
        
        /// <summary>
        /// Video codec (if available)
        /// </summary>
        public string? Codec { get; set; }
        
        /// <summary>
        /// Whether this file is a duplicate of another
        /// </summary>
        public bool IsDuplicate { get; set; } = false;
        
        /// <summary>
        /// ID of the original file if this is a duplicate
        /// </summary>
        public Guid? OriginalFileId { get; set; }
        
        /// <summary>
        /// Navigation property to original file
        /// </summary>
        [ForeignKey(nameof(OriginalFileId))]
        public ScannedVideoFile? OriginalFile { get; set; }
        
        /// <summary>
        /// Current import status
        /// </summary>
        public FileImportStatus ImportStatus { get; set; } = FileImportStatus.NotImported;
        
        /// <summary>
        /// Import session ID if imported
        /// </summary>
        public Guid? ImportSessionId { get; set; }
        
        /// <summary>
        /// When the file was first scanned
        /// </summary>
        public DateTime FirstScanned { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// When the file was last scanned/updated
        /// </summary>
        public DateTime LastScanned { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Whether the file still exists on disk
        /// </summary>
        public bool FileExists { get; set; } = true;
        
        /// <summary>
        /// Last known modification time for change detection
        /// </summary>
        public DateTime LastKnownModified { get; set; }

        /// <summary>
        /// SQL Server <c>rowversion</c>: a tracked update fails with <c>DbUpdateConcurrencyException</c> when the row
        /// changed after it was read, instead of silently overwriting it. Not mapped on other providers.
        /// </summary>
        public byte[]? RowVersion { get; set; }
    }
    
    /// <summary>
    /// Import status for individual files
    /// </summary>
    public enum FileImportStatus
    {
        NotImported = 0,
        Selected = 1,
        Pending = 2,
        Uploading = 3,
        Uploaded = 4,
        Failed = 5,
        Skipped = 6
    }
}
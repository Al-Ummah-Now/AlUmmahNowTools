using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlUmmahNow.Data.Models
{
    public class ImportSession
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public string RootFolder { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public ImportStatus Status { get; set; } = ImportStatus.InProgress;
        public int TotalVideosFound { get; set; }
        public int VideosProcessed { get; set; }
        public int VideosUploaded { get; set; }
        public int VideosFailed { get; set; }
        public string? ErrorMessage { get; set; }
        
        public virtual ICollection<ImportChannel> Channels { get; set; } = new List<ImportChannel>();
        public virtual ICollection<ImportVideo> Videos { get; set; } = new List<ImportVideo>();
    }

    public class ImportChannel
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SessionId { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string VideoStoreRoot { get; set; } = string.Empty;
        public string VideoStoreUrl { get; set; } = string.Empty;
        public string ImageStoreRoot { get; set; } = string.Empty;
        public string ImageStoreUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int TotalVideos { get; set; }
        public int ProcessedVideos { get; set; }
        
        [ForeignKey(nameof(SessionId))]
        public virtual ImportSession Session { get; set; } = null!;
        public virtual ICollection<ImportVideo> Videos { get; set; } = new List<ImportVideo>();
    }

    public class ImportVideo
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SessionId { get; set; }
        public Guid ChannelId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? VideoId { get; set; }
        public string? VideoUrl { get; set; }
        public string? ThumbnailPath { get; set; }
        public string? ThumbnailUrl { get; set; }
        public VideoImportStatus Status { get; set; } = VideoImportStatus.Pending;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
        public DateTime? UploadedAt { get; set; }
        public int RetryCount { get; set; } = 0;
        public string? VideoMetadata { get; set; } // JSON string for additional video info
        
        [ForeignKey(nameof(SessionId))]
        public virtual ImportSession Session { get; set; } = null!;
        
        [ForeignKey(nameof(ChannelId))]
        public virtual ImportChannel Channel { get; set; } = null!;
    }

    public enum ImportStatus
    {
        InProgress = 0,
        Completed = 1,
        Failed = 2,
        Cancelled = 3,
        Paused = 4
    }

    public enum VideoImportStatus
    {
        Pending = 0,
        Scanning = 1,
        Uploading = 2,
        Uploaded = 3,
        Failed = 4,
        Skipped = 5
    }

    // Status DTOs for API responses
    public class ImportSessionStatus
    {
        public Guid SessionId { get; set; }
        public ImportStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int TotalVideosFound { get; set; }
        public int VideosProcessed { get; set; }
        public int VideosUploaded { get; set; }
        public int VideosFailed { get; set; }
        public string? ErrorMessage { get; set; }
        public List<ChannelStatus> Channels { get; set; } = new List<ChannelStatus>();
        
        public double ProgressPercentage => TotalVideosFound > 0 ? (double)VideosProcessed / TotalVideosFound * 100 : 0;
        public bool IsCompleted => Status == ImportStatus.Completed || Status == ImportStatus.Failed || Status == ImportStatus.Cancelled;
    }

    public class ChannelStatus
    {
        public string ChannelName { get; set; } = string.Empty;
        public int TotalVideos { get; set; }
        public int ProcessedVideos { get; set; }
        public int UploadedVideos { get; set; }
        public int FailedVideos { get; set; }
        
        public double ProgressPercentage => TotalVideos > 0 ? (double)ProcessedVideos / TotalVideos * 100 : 0;
    }
}
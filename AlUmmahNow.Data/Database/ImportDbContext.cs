using Microsoft.EntityFrameworkCore;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Models.FileScanning;

namespace AlUmmahNow.Data.Database
{
    public class ImportDbContext : DbContext
    {
        public ImportDbContext(DbContextOptions<ImportDbContext> options) : base(options)
        {
        }

        // Existing import session tracking
        public DbSet<ImportSession> ImportSessions { get; set; }
        public DbSet<ImportChannel> ImportChannels { get; set; }
        public DbSet<ImportVideo> ImportVideos { get; set; }
        
        // New file scanning tracking
        public DbSet<ScanSession> ScanSessions { get; set; }
        public DbSet<ScannedVideoFile> ScannedVideoFiles { get; set; }
        public DbSet<ScannedChannel> ScannedChannels { get; set; }
        
        // Watch and approval models
        public DbSet<ChannelWatch> ChannelWatches { get; set; }
        public DbSet<PendingApproval> PendingApprovals { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure existing models
            modelBuilder.Entity<ImportSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.RootFolder).IsRequired();
                entity.Property(e => e.RootFolderHash).IsRequired().HasMaxLength(PathHash.Length);
                entity.HasIndex(e => e.Status);
                // At most one running import per folder (Status 0 = InProgress). Sessions created before
                // RootFolderHash existed have an empty hash and are excluded.
                entity.HasIndex(e => e.RootFolderHash)
                    .IsUnique()
                    .HasFilter("[Status] = 0 AND [RootFolderHash] <> ''")
                    .HasDatabaseName(ImportDbSchema.ImportSessionsOneRunningPerRootIndex);
                entity.HasIndex(e => e.StartTime);
            });

            modelBuilder.Entity<ImportChannel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ChannelName).IsRequired();
                entity.Property(e => e.FolderPath).IsRequired();
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.ChannelName);
            });

            modelBuilder.Entity<ImportVideo>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FileName).IsRequired();
                entity.Property(e => e.FilePath).IsRequired();
                entity.Property(e => e.FilePathHash).IsRequired().HasMaxLength(PathHash.Length);
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.ChannelId);
                // One row per file per session: lookups are by (SessionId, path), and a second insert for
                // the same file in the same session fails instead of creating a row nothing will process.
                entity.HasIndex(e => new { e.SessionId, e.FilePathHash })
                    .IsUnique()
                    .HasDatabaseName(ImportDbSchema.ImportVideosSessionPathIndex);
                entity.HasIndex(e => e.Status);
                
                // Configure relationships to avoid cascade path conflicts
                entity.HasOne(e => e.Session)
                    .WithMany(s => s.Videos)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.NoAction);
                    
                entity.HasOne(e => e.Channel)
                    .WithMany(c => c.Videos)
                    .HasForeignKey(e => e.ChannelId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Configure new file scanning models
            modelBuilder.Entity<ScanSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.RootPath).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.Name).HasMaxLength(500);
                entity.Property(e => e.PathHash).HasMaxLength(64);
                entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
                entity.Property(e => e.CurrentOperation).HasMaxLength(500);
                
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.PathHash);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.LastUpdated);
            });

            modelBuilder.Entity<ScannedVideoFile>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Directory).IsRequired().HasMaxLength(1500);
                entity.Property(e => e.ChannelName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.RelativePath).HasMaxLength(1500);
                entity.Property(e => e.Extension).HasMaxLength(10);
                entity.Property(e => e.ContentHash).HasMaxLength(64);
                entity.Property(e => e.MetadataHash).HasMaxLength(64);
                entity.Property(e => e.ThumbnailPath).HasMaxLength(2000);
                entity.Property(e => e.ThumbnailHash).HasMaxLength(64);
                entity.Property(e => e.InfoFilePath).HasMaxLength(2000);
                entity.Property(e => e.InfoFileHash).HasMaxLength(64);
                entity.Property(e => e.Codec).HasMaxLength(50);
                
                entity.HasIndex(e => e.ScanSessionId);
                entity.HasIndex(e => e.ChannelName);
                entity.HasIndex(e => e.ContentHash);
                entity.HasIndex(e => e.MetadataHash);
                entity.HasIndex(e => e.ImportStatus);
                entity.HasIndex(e => e.IsDuplicate);
                entity.HasIndex(e => e.FileExists);
                entity.HasIndex(e => e.LastScanned);
                
                // Composite indexes for common queries
                entity.HasIndex(e => new { e.ScanSessionId, e.ChannelName });
                entity.HasIndex(e => new { e.ScanSessionId, e.ImportStatus });
                entity.HasIndex(e => new { e.ContentHash, e.FileSize });
                
                // Self-referencing relationship for duplicate detection
                // Use NoAction to avoid SQL Server cascade path conflicts
                entity.HasOne(e => e.OriginalFile)
                    .WithMany()
                    .HasForeignKey(e => e.OriginalFileId)
                    .OnDelete(DeleteBehavior.NoAction);
                
                // Relationship to scan session
                entity.HasOne(e => e.ScanSession)
                    .WithMany()
                    .HasForeignKey(e => e.ScanSessionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ScannedChannel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Path).IsRequired().HasMaxLength(1500);
                entity.Property(e => e.RelativePath).HasMaxLength(1500);
                entity.Property(e => e.ContentHash).HasMaxLength(64);
                
                entity.HasIndex(e => e.ScanSessionId);
                entity.HasIndex(e => e.Name);
                entity.HasIndex(e => e.DepthLevel);
                entity.HasIndex(e => e.DirectoryExists);
                entity.HasIndex(e => e.LastUpdated);
                
                // Composite indexes
                entity.HasIndex(e => new { e.ScanSessionId, e.Name });
                entity.HasIndex(e => new { e.ScanSessionId, e.DepthLevel });
                
                // Self-referencing relationship for parent/child channels
                // Use NoAction to avoid SQL Server cascade path conflicts
                entity.HasOne(e => e.ParentChannel)
                    .WithMany()
                    .HasForeignKey(e => e.ParentChannelId)
                    .OnDelete(DeleteBehavior.NoAction);
                
                // Relationship to scan session
                entity.HasOne(e => e.ScanSession)
                    .WithMany()
                    .HasForeignKey(e => e.ScanSessionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure watch and approval models
            modelBuilder.Entity<ChannelWatch>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ChannelName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.WatchPath).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.CreatedBy).HasMaxLength(256);
                entity.Property(e => e.FileExtensions).HasMaxLength(500);
                
                entity.HasIndex(e => e.ChannelName);
                entity.HasIndex(e => e.WatchPath);
                entity.Property(e => e.WatchPathHash).IsRequired().HasMaxLength(PathHash.Length);
                entity.HasIndex(e => e.WatchPathHash)
                    .IsUnique()
                    .HasFilter("[IsActive] = 1")
                    .HasDatabaseName(ImportDbSchema.ActiveWatchPathIndex);
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.LastFileDetected);
                
                // Composite indexes for common queries
                entity.HasIndex(e => new { e.IsActive, e.ChannelName });
                entity.HasIndex(e => new { e.IsActive, e.LastFileDetected });
            });

            modelBuilder.Entity<PendingApproval>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Extension).HasMaxLength(10);
                entity.Property(e => e.ApprovedBy).HasMaxLength(256);
                entity.Property(e => e.ApprovalNotes).HasMaxLength(1000);
                entity.Property(e => e.ContentHash).HasMaxLength(64);
                entity.Property(e => e.ThumbnailPath).HasMaxLength(2000);
                
                entity.HasIndex(e => e.ChannelWatchId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.DetectedAt);
                entity.HasIndex(e => e.ApprovedAt);
                entity.HasIndex(e => e.Priority);
                entity.HasIndex(e => e.ExpiresAt);
                entity.HasIndex(e => e.ContentHash);
                entity.HasIndex(e => e.FileExists);
                entity.Property(e => e.FilePathHash).IsRequired().HasMaxLength(PathHash.Length);
                entity.HasIndex(e => e.FilePathHash)
                    .IsUnique()
                    .HasFilter("[Status] = 0")
                    .HasDatabaseName(ImportDbSchema.PendingApprovalPathIndex);
                
                // Composite indexes for common queries
                entity.HasIndex(e => new { e.Status, e.Priority });
                entity.HasIndex(e => new { e.Status, e.DetectedAt });
                entity.HasIndex(e => new { e.ChannelWatchId, e.Status });
                entity.HasIndex(e => new { e.Status, e.ExpiresAt });
                
                // Relationship to channel watch
                entity.HasOne(e => e.ChannelWatch)
                    .WithMany(w => w.PendingApprovals)
                    .HasForeignKey(e => e.ChannelWatchId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
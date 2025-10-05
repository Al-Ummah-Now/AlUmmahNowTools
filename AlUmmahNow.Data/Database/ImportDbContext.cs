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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure existing models
            modelBuilder.Entity<ImportSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.RootFolder).IsRequired();
                entity.HasIndex(e => e.Status);
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
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.ChannelId);
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
        }
    }
}
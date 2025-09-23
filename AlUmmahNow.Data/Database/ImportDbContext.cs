using Microsoft.EntityFrameworkCore;
using AlUmmahNow.Data.Models;
using System;

namespace AlUmmahNow.Data.Database
{
    public class ImportDbContext : DbContext
    {
        public DbSet<ImportSession> ImportSessions { get; set; }
        public DbSet<ImportChannel> ImportChannels { get; set; }
        public DbSet<ImportVideo> ImportVideos { get; set; }

        public ImportDbContext(DbContextOptions<ImportDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationships
            modelBuilder.Entity<ImportChannel>()
                .HasOne(c => c.Session)
                .WithMany(s => s.Channels)
                .HasForeignKey(c => c.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ImportVideo>()
                .HasOne(v => v.Session)
                .WithMany(s => s.Videos)
                .HasForeignKey(v => v.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ImportVideo>()
                .HasOne(v => v.Channel)
                .WithMany(c => c.Videos)
                .HasForeignKey(v => v.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure indexes for better performance
            modelBuilder.Entity<ImportVideo>()
                .HasIndex(v => v.Status);

            modelBuilder.Entity<ImportVideo>()
                .HasIndex(v => v.FilePath)
                .IsUnique();

            modelBuilder.Entity<ImportChannel>()
                .HasIndex(c => new { c.SessionId, c.ChannelName })
                .IsUnique();

            // Configure string lengths
            modelBuilder.Entity<ImportSession>()
                .Property(s => s.RootFolder)
                .HasMaxLength(500);

            modelBuilder.Entity<ImportChannel>()
                .Property(c => c.ChannelName)
                .HasMaxLength(255);

            modelBuilder.Entity<ImportChannel>()
                .Property(c => c.FolderPath)
                .HasMaxLength(500);

            modelBuilder.Entity<ImportVideo>()
                .Property(v => v.FileName)
                .HasMaxLength(255);

            modelBuilder.Entity<ImportVideo>()
                .Property(v => v.FilePath)
                .HasMaxLength(500);
        }
    }
}
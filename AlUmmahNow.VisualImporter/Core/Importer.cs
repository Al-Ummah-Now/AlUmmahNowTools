using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Database;
using Microsoft.EntityFrameworkCore;
using ShellProgressBar;

namespace AlUmmahNow.VisualImporter.Core
{
    // Copy of Video model from DataImporter for compatibility
    public class Video
    {
        public Guid ID { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string VideoUrl { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public List<string> AvailableResolutions { get; set; } = new List<string>();
        public string ChannelThumbnailUrl { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime DateModified { get; set; }
    }

    // Copy of Image model from DataImporter for compatibility
    public class Image
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime DateModified { get; set; }
    }

    public class ImportArgs
    {
        public string FolderPath { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string BaseVideoUrl { get; set; } = "https://cdn.alummahnow.net";
        public string BaseImageUrl { get; set; } = "https://images.alummahnow.net";
        public Guid? ResumeSessionId { get; set; }
        public Guid? StatusSessionId { get; set; }
    }

    public class Importer
    {
        Repository<Video> _repo;
        IStorageClient<Video> _videoStorageClient;
        IStorageClient<Image> _imageStorageClient;
        BunnyStreamClient _bunnyStreamClient;
        ImportDbContext _dbContext;

        public Importer(Repository<Video> repo, IStorageClient<Video> videoStorageClient, IStorageClient<Image> imageStorageClient, BunnyStreamClient bunnyStreamClient, ImportDbContext dbContext)
        {
            _repo = repo;
            _videoStorageClient = videoStorageClient;
            _imageStorageClient = imageStorageClient;
            _bunnyStreamClient = bunnyStreamClient;
            _dbContext = dbContext;
        }

        /// <summary>
        /// Recursively imports videos from multiple channel folders
        /// </summary>
        /// <param name="rootFolder">Root folder containing channel subfolders</param>
        /// <param name="userId">User ID for the import</param>
        /// <param name="baseVideoStoreUrl">Base URL for video storage</param>
        /// <param name="baseImageStoreUrl">Base URL for image storage</param>
        /// <returns>Import session ID for tracking</returns>
        public async Task<Guid> ImportMultiChannelAsync(string rootFolder, string userId, string baseVideoStoreUrl, string baseImageStoreUrl)
        {
            if (!Directory.Exists(rootFolder))
            {
                throw new DirectoryNotFoundException($"Root folder not found: {rootFolder}");
            }

            // Create import session
            var session = new ImportSession
            {
                RootFolder = rootFolder,
                UserId = userId,
                Status = ImportStatus.InProgress
            };

            _dbContext.ImportSessions.Add(session);
            await _dbContext.SaveChangesAsync();

            try
            {
                // Scan for channels (subdirectories in root folder)
                var channelDirectories = new DirectoryInfo(rootFolder)
                    .GetDirectories()
                    .Where(d => !d.Name.StartsWith(".")) // Skip hidden folders
                    .ToArray();

                // Discover all videos first
                var allVideos = new List<(DirectoryInfo channel, FileInfo video, FileInfo? thumbnail)>();
                
                foreach (var channelDir in channelDirectories)
                {
                    var channelVideos = await ScanChannelForVideosAsync(channelDir);
                    allVideos.AddRange(channelVideos.Select(v => (channelDir, v.video, v.thumbnail)));
                }

                session.TotalVideosFound = allVideos.Count;
                await _dbContext.SaveChangesAsync();

                if (allVideos.Count == 0)
                {
                    session.Status = ImportStatus.Completed;
                    session.EndTime = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    return session.Id;
                }

                // Create channels and videos in database
                await CreateChannelsAndVideosInDbAsync(session, allVideos, baseVideoStoreUrl, baseImageStoreUrl);

                // Process all videos with progress tracking
                foreach (var channelGroup in allVideos.GroupBy(v => v.channel.Name))
                {
                    var channelName = channelGroup.Key;
                    var channelVideos = channelGroup.ToList();
                    await ProcessChannelVideosAsync(session.Id, channelName, channelVideos);
                }

                // Update session status
                await UpdateSessionStatusAsync(session.Id);
                
                return session.Id;
            }
            catch (Exception ex)
            {
                session.Status = ImportStatus.Failed;
                session.ErrorMessage = ex.Message;
                session.EndTime = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                throw;
            }
        }

        /// <summary>
        /// Resume a previously interrupted import session
        /// </summary>
        /// <param name="sessionId">Session ID to resume</param>
        public async Task<bool> ResumeImportAsync(Guid sessionId)
        {
            var session = await _dbContext.ImportSessions
                .Include(s => s.Channels)
                .Include(s => s.Videos)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
                return false;

            if (session.Status == ImportStatus.Completed)
                return true;

            session.Status = ImportStatus.InProgress;
            await _dbContext.SaveChangesAsync();

            try
            {
                // Get pending and failed videos
                var pendingVideos = await _dbContext.ImportVideos
                    .Include(v => v.Channel)
                    .Where(v => v.SessionId == sessionId && 
                               (v.Status == VideoImportStatus.Pending || 
                                v.Status == VideoImportStatus.Failed))
                    .ToListAsync();

                if (pendingVideos.Count == 0)
                {
                    await UpdateSessionStatusAsync(sessionId);
                    return true;
                }

                foreach (var videoGroup in pendingVideos.GroupBy(v => v.Channel.ChannelName))
                {
                    await ProcessPendingVideosAsync(videoGroup.ToList());
                }

                await UpdateSessionStatusAsync(sessionId);
                return true;
            }
            catch (Exception ex)
            {
                session.Status = ImportStatus.Failed;
                session.ErrorMessage = ex.Message;
                session.EndTime = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return false;
            }
        }

        /// <summary>
        /// Get import session status and progress
        /// </summary>
        public async Task<ImportSessionStatus> GetImportStatusAsync(Guid sessionId)
        {
            var session = await _dbContext.ImportSessions
                .Include(s => s.Channels)
                .Include(s => s.Videos)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
            {
                throw new InvalidOperationException($"Session {sessionId} not found");
            }

            var channelStatuses = await _dbContext.ImportChannels
                .Where(c => c.SessionId == sessionId)
                .Select(c => new ChannelStatus
                {
                    ChannelName = c.ChannelName,
                    TotalVideos = c.TotalVideos,
                    ProcessedVideos = c.ProcessedVideos,
                    UploadedVideos = c.Videos.Count(v => v.Status == VideoImportStatus.Uploaded),
                    FailedVideos = c.Videos.Count(v => v.Status == VideoImportStatus.Failed)
                })
                .ToListAsync();

            return new ImportSessionStatus
            {
                SessionId = session.Id,
                Status = session.Status,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                TotalVideosFound = session.TotalVideosFound,
                VideosProcessed = session.VideosProcessed,
                VideosUploaded = session.VideosUploaded,
                VideosFailed = session.VideosFailed,
                ErrorMessage = session.ErrorMessage,
                Channels = channelStatuses
            };
        }

        private Task<List<(FileInfo video, FileInfo? thumbnail)>> ScanChannelForVideosAsync(DirectoryInfo channelDir)
        {
            var videos = new List<(FileInfo video, FileInfo? thumbnail)>();
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv" };

            // Recursively scan for videos
            ScanDirectoryRecursive(channelDir, channelDir, videos, videoExtensions);

            return Task.FromResult(videos);
        }

        private void ScanDirectoryRecursive(DirectoryInfo currentDir, DirectoryInfo channelRoot, 
            List<(FileInfo video, FileInfo? thumbnail)> videos, string[] videoExtensions)
        {
            try
            {
                // Get video files in current directory
                var videoFiles = currentDir.GetFiles()
                    .Where(f => videoExtensions.Contains(f.Extension.ToLowerInvariant()))
                    .ToArray();

                // Find thumbnails for each video
                foreach (var videoFile in videoFiles)
                {
                    var thumbnail = FindThumbnailForVideo(videoFile, currentDir);
                    videos.Add((videoFile, thumbnail));
                }

                // Recursively scan subdirectories
                foreach (var subDir in currentDir.GetDirectories())
                {
                    if (!subDir.Name.StartsWith(".")) // Skip hidden folders
                    {
                        ScanDirectoryRecursive(subDir, channelRoot, videos, videoExtensions);
                    }
                }
            }
            catch (Exception)
            {
                // Continue on error
            }
        }

        private FileInfo? FindThumbnailForVideo(FileInfo videoFile, DirectoryInfo directory)
        {
            var baseFileName = Path.GetFileNameWithoutExtension(videoFile.Name);
            var thumbnailExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            foreach (var ext in thumbnailExtensions)
            {
                var thumbnailPath = Path.Combine(directory.FullName, baseFileName + ext);
                if (File.Exists(thumbnailPath))
                {
                    return new FileInfo(thumbnailPath);
                }
            }

            // Check for thumbnails in .thumbs folder
            var thumbsDir = Path.Combine(directory.FullName, ".thumbs");
            if (Directory.Exists(thumbsDir))
            {
                foreach (var ext in thumbnailExtensions)
                {
                    var thumbnailPath = Path.Combine(thumbsDir, baseFileName + ext);
                    if (File.Exists(thumbnailPath))
                    {
                        return new FileInfo(thumbnailPath);
                    }
                }
            }

            return null;
        }

        private async Task CreateChannelsAndVideosInDbAsync(ImportSession session, 
            List<(DirectoryInfo channel, FileInfo video, FileInfo? thumbnail)> allVideos, 
            string baseVideoStoreUrl, string baseImageStoreUrl)
        {
            var channelGroups = allVideos.GroupBy(v => v.channel.Name);

            foreach (var channelGroup in channelGroups)
            {
                var channelName = channelGroup.Key;
                var channelDir = channelGroup.First().channel;
                var channelId = Guid.NewGuid().ToString();

                // Create channel record
                var channel = new ImportChannel
                {
                    SessionId = session.Id,
                    ChannelName = channelName,
                    ChannelId = channelId,
                    FolderPath = channelDir.FullName,
                    VideoStoreRoot = $"/videos-{channelName.ToLowerInvariant()}/{channelName}/",
                    VideoStoreUrl = $"{baseVideoStoreUrl.TrimEnd('/')}/{channelName}/",
                    ImageStoreRoot = $"/images-{channelName.ToLowerInvariant}/",
                    ImageStoreUrl = $"{baseImageStoreUrl.TrimEnd('/')}/{channelName}/",
                    TotalVideos = channelGroup.Count()
                };

                _dbContext.ImportChannels.Add(channel);
                await _dbContext.SaveChangesAsync(); // Save to get the channel ID

                // Create video records
                foreach (var (_, video, thumbnail) in channelGroup)
                {
                    var importVideo = new ImportVideo
                    {
                        SessionId = session.Id,
                        ChannelId = channel.Id,
                        FileName = video.Name,
                        FilePath = video.FullName,
                        FileSize = video.Length,
                        ThumbnailPath = thumbnail?.FullName,
                        Status = VideoImportStatus.Pending
                    };

                    _dbContext.ImportVideos.Add(importVideo);
                }
            }

            await _dbContext.SaveChangesAsync();
        }

        private async Task ProcessChannelVideosAsync(Guid sessionId, string channelName, 
            List<(DirectoryInfo channel, FileInfo video, FileInfo? thumbnail)> channelVideos)
        {
            var channel = await _dbContext.ImportChannels
                .FirstOrDefaultAsync(c => c.SessionId == sessionId && c.ChannelName == channelName);

            if (channel == null) return;

            foreach (var (_, videoFile, thumbnailFile) in channelVideos)
            {
                try
                {
                    var importVideo = await _dbContext.ImportVideos
                        .FirstOrDefaultAsync(v => v.FilePath == videoFile.FullName);

                    if (importVideo == null) continue;

                    // Skip if already processed
                    if (importVideo.Status == VideoImportStatus.Uploaded)
                        continue;

                    // Check if already exists in remote storage
                    if (await CheckIfVideoExistsAsync(importVideo))
                    {
                        importVideo.Status = VideoImportStatus.Uploaded;
                        await _dbContext.SaveChangesAsync();
                        continue;
                    }

                    // Update status to uploading
                    importVideo.Status = VideoImportStatus.Uploading;
                    await _dbContext.SaveChangesAsync();

                    // Upload video
                    await UploadVideoFileAsync(videoFile, channel.VideoStoreRoot);
                    
                    // Upload thumbnail if exists
                    if (thumbnailFile != null)
                    {
                        await UploadThumbnailFileAsync(thumbnailFile, channel.ImageStoreRoot);
                        importVideo.ThumbnailUrl = $"{channel.ImageStoreUrl}{thumbnailFile.Name}";
                    }

                    // Create video metadata
                    var video = new Video
                    {
                        Title = Path.GetFileNameWithoutExtension(videoFile.Name),
                        Description = Path.GetFileNameWithoutExtension(videoFile.Name),
                        UserId = (await _dbContext.ImportSessions.FindAsync(sessionId))?.UserId ?? "",
                        VideoUrl = $"{channel.VideoStoreUrl}{videoFile.Name}",
                        ThumbnailUrl = importVideo.ThumbnailUrl ?? "",
                        Channel = channelName,
                        ChannelId = channel.ChannelId,
                        DateModified = DateTime.UtcNow
                    };

                    // Save to repository
                    await _repo.SaveObjectAsync("videos", video.ID.ToString(), video);

                    // Update import video record
                    importVideo.VideoId = video.ID.ToString();
                    importVideo.VideoUrl = video.VideoUrl;
                    importVideo.Status = VideoImportStatus.Uploaded;
                    importVideo.ProcessedAt = DateTime.UtcNow;
                    importVideo.UploadedAt = DateTime.UtcNow;

                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    var importVideo = await _dbContext.ImportVideos
                        .FirstOrDefaultAsync(v => v.FilePath == videoFile.FullName);
                    
                    if (importVideo != null)
                    {
                        importVideo.Status = VideoImportStatus.Failed;
                        importVideo.ErrorMessage = ex.Message;
                        importVideo.RetryCount++;
                        importVideo.ProcessedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();
                    }
                }
            }

            // Update channel processed count
            channel.ProcessedVideos = await _dbContext.ImportVideos
                .CountAsync(v => v.ChannelId == channel.Id && 
                               v.Status != VideoImportStatus.Pending);
            await _dbContext.SaveChangesAsync();
        }

        private async Task ProcessPendingVideosAsync(List<ImportVideo> pendingVideos)
        {
            foreach (var importVideo in pendingVideos)
            {
                try
                {
                    var videoFile = new FileInfo(importVideo.FilePath);
                    if (!videoFile.Exists)
                    {
                        importVideo.Status = VideoImportStatus.Failed;
                        importVideo.ErrorMessage = "Video file not found";
                        await _dbContext.SaveChangesAsync();
                        continue;
                    }

                    var channel = importVideo.Channel;

                    // Update status to uploading
                    importVideo.Status = VideoImportStatus.Uploading;
                    await _dbContext.SaveChangesAsync();

                    // Upload video
                    await UploadVideoFileAsync(videoFile, channel.VideoStoreRoot);
                    
                    // Upload thumbnail if exists
                    if (!string.IsNullOrEmpty(importVideo.ThumbnailPath) && File.Exists(importVideo.ThumbnailPath))
                    {
                        var thumbnailFile = new FileInfo(importVideo.ThumbnailPath);
                        await UploadThumbnailFileAsync(thumbnailFile, channel.ImageStoreRoot);
                        importVideo.ThumbnailUrl = $"{channel.ImageStoreUrl}{thumbnailFile.Name}";
                    }

                    // Update status
                    importVideo.Status = VideoImportStatus.Uploaded;
                    importVideo.ProcessedAt = DateTime.UtcNow;
                    importVideo.UploadedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    importVideo.Status = VideoImportStatus.Failed;
                    importVideo.ErrorMessage = ex.Message;
                    importVideo.RetryCount++;
                    importVideo.ProcessedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                }
            }
        }

        private async Task<bool> CheckIfVideoExistsAsync(ImportVideo importVideo)
        {
            try
            {
                // Check if video info file exists locally
                var infoPath = Path.ChangeExtension(importVideo.FilePath, ".r2.json");
                if (File.Exists(infoPath))
                {
                    return true;
                }

                // Could also check remote storage here if needed
                await Task.Delay(1); // Satisfy async requirement
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task UploadVideoFileAsync(FileInfo videoFile, string videoStoreRoot)
        {
            using (var reader = videoFile.OpenRead())
            {
                await _videoStorageClient.UploadAsync(reader, videoStoreRoot + videoFile.Name);
            }
        }

        private async Task UploadThumbnailFileAsync(FileInfo thumbnailFile, string imageStoreRoot)
        {
            using (var reader = thumbnailFile.OpenRead())
            {
                await _imageStorageClient.UploadAsync(reader, imageStoreRoot + thumbnailFile.Name);
            }
        }

        private async Task UpdateSessionStatusAsync(Guid sessionId)
        {
            var session = await _dbContext.ImportSessions.FindAsync(sessionId);
            if (session == null) return;

            var videoStats = await _dbContext.ImportVideos
                .Where(v => v.SessionId == sessionId)
                .GroupBy(v => v.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            session.VideosProcessed = videoStats.Where(s => s.Status != VideoImportStatus.Pending).Sum(s => s.Count);
            session.VideosUploaded = videoStats.FirstOrDefault(s => s.Status == VideoImportStatus.Uploaded)?.Count ?? 0;
            session.VideosFailed = videoStats.FirstOrDefault(s => s.Status == VideoImportStatus.Failed)?.Count ?? 0;

            var pendingVideos = videoStats.FirstOrDefault(s => s.Status == VideoImportStatus.Pending)?.Count ?? 0;

            if (pendingVideos == 0)
            {
                session.Status = session.VideosFailed > 0 ? ImportStatus.Failed : ImportStatus.Completed;
                session.EndTime = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}
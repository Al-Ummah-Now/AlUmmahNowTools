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
using AlUmmahNow.Importer.Core;

namespace AlUmmahNow.Importer
{
    /// <summary>
    /// Core video import functionality for uploading and managing video content
    /// </summary>
    public class VideoImporter
    {
        private readonly Repository<Video> _repo;
        private readonly IStorageClient<Video> _videoStorageClient;
        private readonly IStorageClient<Image> _imageStorageClient;
        private readonly BunnyStreamClient _bunnyStreamClient;
        private readonly ImportDbContext? _dbContext;

        /// <summary>
        /// Raised when import progress changes
        /// </summary>
        public event EventHandler<ImportProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Raised when import operation completes
        /// </summary>
        public event EventHandler<ImportCompletedEventArgs>? ImportCompleted;

        /// <summary>
        /// Raised when channel processing progress updates
        /// </summary>
        public event EventHandler<ChannelProgressEventArgs>? ChannelProgressChanged;

        /// <summary>
        /// Raised when an individual file import fails
        /// </summary>
        public event EventHandler<FileImportErrorEventArgs>? FileImportError;

        /// <summary>
        /// Initializes a new instance with database session tracking support
        /// </summary>
        public VideoImporter(
            Repository<Video> repo, 
            IStorageClient<Video> videoStorageClient, 
            IStorageClient<Image> imageStorageClient, 
            BunnyStreamClient bunnyStreamClient, 
            ImportDbContext dbContext)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _videoStorageClient = videoStorageClient ?? throw new ArgumentNullException(nameof(videoStorageClient));
            _imageStorageClient = imageStorageClient ?? throw new ArgumentNullException(nameof(imageStorageClient));
            _bunnyStreamClient = bunnyStreamClient ?? throw new ArgumentNullException(nameof(bunnyStreamClient));
            _dbContext = dbContext;
        }

        /// <summary>
        /// Initializes a new instance for basic upload operations without session tracking
        /// </summary>
        public VideoImporter(
            Repository<Video> repo,
            IStorageClient<Video> videoStorageClient,
            IStorageClient<Image> imageStorageClient,
            BunnyStreamClient bunnyStreamClient)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _videoStorageClient = videoStorageClient ?? throw new ArgumentNullException(nameof(videoStorageClient));
            _imageStorageClient = imageStorageClient ?? throw new ArgumentNullException(nameof(imageStorageClient));
            _bunnyStreamClient = bunnyStreamClient ?? throw new ArgumentNullException(nameof(bunnyStreamClient));
            _dbContext = null;
        }

        #region Multi-Channel Import with Session Management

        /// <summary>
        /// Recursively imports videos from multiple channel folders with session tracking
        /// </summary>
        /// <param name="rootFolder">Root folder containing channel subfolders</param>
        /// <param name="userId">User ID for the import</param>
        /// <param name="baseVideoStoreUrl">Base URL for video storage</param>
        /// <param name="baseImageStoreUrl">Base URL for image storage</param>
        /// <returns>Import session ID for tracking</returns>
        public async Task<Guid> ImportMultiChannelAsync(string rootFolder, string userId, string baseVideoStoreUrl, string baseImageStoreUrl)
        {
            if (_dbContext == null)
                throw new InvalidOperationException("Database context is required for session-based imports. Use constructor with ImportDbContext parameter.");

            if (!Directory.Exists(rootFolder))
                throw new DirectoryNotFoundException($"Root folder not found: {rootFolder}");

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
                    ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(0, 0, 0));
                    return session.Id;
                }

                // Create channels and videos in database
                await CreateChannelsAndVideosInDbAsync(session, allVideos, baseVideoStoreUrl, baseImageStoreUrl);

                // Process all videos with progress tracking
                int totalProcessed = 0;
                foreach (var channelGroup in allVideos.GroupBy(v => v.channel.Name))
                {
                    var channelName = channelGroup.Key;
                    var channelVideos = channelGroup.ToList();
                    
                    await ProcessChannelVideosAsync(session.Id, channelName, channelVideos, allVideos.Count, totalProcessed);
                    totalProcessed += channelVideos.Count;
                }

                // Update session status
                await UpdateSessionStatusAsync(session.Id);
                
                // Report completion
                var finalStats = await GetSessionStatsAsync(session.Id);
                ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(
                    finalStats.uploaded, finalStats.failed, finalStats.processed));
                
                return session.Id;
            }
            catch (Exception ex)
            {
                session.Status = ImportStatus.Failed;
                session.ErrorMessage = ex.Message;
                session.EndTime = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                
                ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(0, 0, 0, ex.Message));
                throw;
            }
        }

        /// <summary>
        /// Resume a previously interrupted import session
        /// </summary>
        /// <param name="sessionId">Session ID to resume</param>
        public async Task<bool> ResumeImportAsync(Guid sessionId)
        {
            if (_dbContext == null)
                throw new InvalidOperationException("Database context is required for session-based imports.");

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

                int totalProcessed = 0;
                foreach (var videoGroup in pendingVideos.GroupBy(v => v.Channel.ChannelName))
                {
                    var channelName = videoGroup.Key;
                    var channelVideos = videoGroup.ToList();
                    
                    await ProcessPendingVideosAsync(channelVideos, pendingVideos.Count, totalProcessed);
                    totalProcessed += channelVideos.Count;
                }

                await UpdateSessionStatusAsync(sessionId);
                
                // Report completion
                var finalStats = await GetSessionStatsAsync(sessionId);
                ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(
                    finalStats.uploaded, finalStats.failed, finalStats.processed));
                
                return true;
            }
            catch (Exception ex)
            {
                session.Status = ImportStatus.Failed;
                session.ErrorMessage = ex.Message;
                session.EndTime = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                
                ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(0, 0, 0, ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Get import session status and progress
        /// </summary>
        public async Task<ImportSessionStatus> GetImportStatusAsync(Guid sessionId)
        {
            if (_dbContext == null)
                throw new InvalidOperationException("Database context is required for session-based imports.");

            var session = await _dbContext.ImportSessions
                .Include(s => s.Channels)
                .Include(s => s.Videos)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
                throw new InvalidOperationException($"Session {sessionId} not found");

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

        #endregion

        #region Simple Upload Operations

        /// <summary>
        /// Simple bulk upload of videos from a folder
        /// </summary>
        /// <param name="folder">Folder containing video files</param>
        /// <param name="videoStoreRoot">Storage root path for videos</param>
        public async Task BulkUploadAsync(string folder, string videoStoreRoot)
        {
            var directory = new DirectoryInfo(folder);
            var files = directory.GetFiles()
                .Where(f => f.Extension.ToLowerInvariant() is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv")
                .ToArray();

            int successfulUploads = 0;
            int failedUploads = 0;

            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                
                // Report progress
                ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                    files.Length, i, file.Name, directory.Name, "Uploading"));

                try
                {
                    await UploadVideoFileAsync(file, videoStoreRoot);
                    successfulUploads++;
                    
                    // Report progress after successful upload
                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        files.Length, i + 1, file.Name, directory.Name, "Uploaded"));
                }
                catch (Exception ex)
                {
                    failedUploads++;
                    
                    // Report the error
                    FileImportError?.Invoke(this, new FileImportErrorEventArgs(file.Name, directory.Name, ex));
                    
                    // Still report progress to keep counter accurate
                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        files.Length, i + 1, file.Name, directory.Name, "Failed"));
                }
            }

            // Report completion
            ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(successfulUploads, failedUploads, files.Length));
        }

        /// <summary>
        /// Legacy single-channel import for backward compatibility
        /// </summary>
        public async Task ImportSingleChannelAsync(string folder, string userId, string channelName, 
            string channelThumbnail, string videoStoreRoot, string videoStoreUrl, string imageStoreRoot, string imageStoreUrl)
        {
            if (_dbContext != null)
            {
                // Use multi-channel import for session-based scenarios
                var parentFolder = Path.GetDirectoryName(folder) ?? folder;
                await ImportMultiChannelAsync(parentFolder, userId, videoStoreUrl, imageStoreUrl);
            }
            else
            {
                // Simple upload for non-session scenarios
                await BulkUploadAsync(folder, videoStoreRoot);
            }
        }

        #endregion

        #region Bunny API Import

        /// <summary>
        /// Import videos from Bunny.net API
        /// </summary>
        public async Task ImportFromBunnyApiAsync(string libraryId, string userId, string channelName, 
            string channelThumbnail, string videoStoreRoot, string videoStoreUrl, string imageStoreRoot, string imageStoreUrl)
        {
            var response = await _bunnyStreamClient.GetVideosAsync(libraryId, 1);
            var thumbnails = await _imageStorageClient.ListAsync(imageStoreRoot);

            int totalItems = response.TotalItems;
            int processedItems = 0;
            int successfulImports = 0;
            int failedImports = 0;

            do
            {
                foreach (var bunnyVideo in response.Items)
                {
                    // Report progress
                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalItems, processedItems, bunnyVideo.Title ?? "Unknown", channelName, "Processing"));

                    try
                    {
                        var video = await _repo.GetObjectAsync("videos", bunnyVideo.Guid ?? string.Empty);
                        if (video != null)
                        {
                            processedItems++;
                            ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                                totalItems, processedItems, bunnyVideo.Title ?? "Unknown", channelName, "Skipped (exists)"));
                            continue;
                        }

                        var thumbnail = thumbnails.FirstOrDefault(f => f.ObjectName == bunnyVideo.Title?.Replace(Path.GetExtension(bunnyVideo.Title), ".jpg"));
                        var thumbnailUrl = thumbnail?.ObjectName != null ? 
                            $"{imageStoreUrl}{thumbnail.ObjectName}" : 
                            $"https://vz-215a1ffd-a5a.b-cdn.net/{bunnyVideo.Guid}/{bunnyVideo.ThumbnailFileName}";

                        video = new Video
                        {
                            ID = Guid.Parse(bunnyVideo.Guid!),
                            Title = bunnyVideo.Title ?? "",
                            Description = bunnyVideo.Description ?? "",
                            UserId = userId,
                            VideoUrl = bunnyVideo.VideoUrl,
                            ThumbnailUrl = thumbnailUrl,
                            Channel = channelName,
                            ChannelThumbnailUrl = channelThumbnail,
                            DateModified = DateTime.UtcNow,
                            DateCreated = bunnyVideo.DateUploaded,
                            Width = bunnyVideo.Width,
                            Height = bunnyVideo.Height,
                            AvailableResolutions = bunnyVideo.AvailableResolutions?.Split(',').ToList() ?? new List<string>()
                        };

                        await _repo.SaveObjectAsync("videos", video.ID.ToString(), video);
                        successfulImports++;
                        processedItems++;
                        
                        ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                            totalItems, processedItems, bunnyVideo.Title ?? "Unknown", channelName, "Imported"));
                    }
                    catch (Exception ex)
                    {
                        failedImports++;
                        processedItems++;
                        
                        FileImportError?.Invoke(this, new FileImportErrorEventArgs(
                            bunnyVideo.Title ?? "Unknown", channelName, ex));
                        
                        ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                            totalItems, processedItems, bunnyVideo.Title ?? "Unknown", channelName, "Failed"));
                    }
                }
                response = await _bunnyStreamClient.GetVideosAsync(libraryId, response.CurrentPage + 1);
            } while ((response.CurrentPage - 1) * response.ItemsPerPage < response.TotalItems);

            // Report completion
            ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(successfulImports, failedImports, totalItems));
        }

        #endregion

        #region Private Helper Methods

        private async Task<List<(FileInfo video, FileInfo? thumbnail)>> ScanChannelForVideosAsync(DirectoryInfo channelDir)
        {
            var videos = new List<(FileInfo video, FileInfo? thumbnail)>();
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv" };

            await ScanDirectoryRecursiveAsync(channelDir, channelDir, videos, videoExtensions);
            return videos;
        }

        private Task ScanDirectoryRecursiveAsync(DirectoryInfo currentDir, DirectoryInfo channelRoot, 
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
                        ScanDirectoryRecursiveAsync(subDir, channelRoot, videos, videoExtensions);
                    }
                }
            }
            catch (Exception)
            {
                // Continue on error - don't fail entire import for one directory
            }

            return Task.CompletedTask;
        }

        private static FileInfo? FindThumbnailForVideo(FileInfo videoFile, DirectoryInfo directory)
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
            if (_dbContext == null) return;

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
            List<(DirectoryInfo channel, FileInfo video, FileInfo? thumbnail)> channelVideos, 
            int totalVideosAcrossAllChannels, int processedSoFar)
        {
            if (_dbContext == null) return;

            var channel = await _dbContext.ImportChannels
                .FirstOrDefaultAsync(c => c.SessionId == sessionId && c.ChannelName == channelName);

            if (channel == null) return;

            int channelProcessed = 0;
            foreach (var (_, videoFile, thumbnailFile) in channelVideos)
            {
                try
                {
                    var importVideo = await _dbContext.ImportVideos
                        .FirstOrDefaultAsync(v => v.FilePath == videoFile.FullName);

                    if (importVideo == null) continue;

                    // Report progress
                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalVideosAcrossAllChannels, processedSoFar + channelProcessed, 
                        videoFile.Name, channelName, "Processing"));

                    // Skip if already processed
                    if (importVideo.Status == VideoImportStatus.Uploaded)
                    {
                        channelProcessed++;
                        ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                            totalVideosAcrossAllChannels, processedSoFar + channelProcessed,
                            videoFile.Name, channelName, "Skipped (already uploaded)"));
                        continue;
                    }

                    // Check if already exists in remote storage
                    if (await CheckIfVideoExistsAsync(importVideo))
                    {
                        importVideo.Status = VideoImportStatus.Uploaded;
                        await _dbContext.SaveChangesAsync();
                        channelProcessed++;
                        ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                            totalVideosAcrossAllChannels, processedSoFar + channelProcessed,
                            videoFile.Name, channelName, "Skipped (already exists)"));
                        continue;
                    }

                    // Update status to uploading
                    importVideo.Status = VideoImportStatus.Uploading;
                    await _dbContext.SaveChangesAsync();

                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalVideosAcrossAllChannels, processedSoFar + channelProcessed,
                        videoFile.Name, channelName, "Uploading"));

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
                    channelProcessed++;
                    
                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalVideosAcrossAllChannels, processedSoFar + channelProcessed,
                        videoFile.Name, channelName, "Uploaded"));
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

                    channelProcessed++;
                    FileImportError?.Invoke(this, new FileImportErrorEventArgs(videoFile.Name, channelName, ex));
                    
                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalVideosAcrossAllChannels, processedSoFar + channelProcessed,
                        videoFile.Name, channelName, "Failed"));
                }
            }

            // Update channel processed count
            channel.ProcessedVideos = await _dbContext.ImportVideos
                .CountAsync(v => v.ChannelId == channel.Id && 
                               v.Status != VideoImportStatus.Pending);
            await _dbContext.SaveChangesAsync();

            // Report channel completion
            ChannelProgressChanged?.Invoke(this, new ChannelProgressEventArgs(
                channelName, channelVideos.Count, channelProcessed));
        }

        private async Task ProcessPendingVideosAsync(List<ImportVideo> pendingVideos, int totalPending, int processedSoFar)
        {
            if (_dbContext == null) return;

            int localProcessed = 0;
            foreach (var importVideo in pendingVideos)
            {
                try
                {
                    var videoFile = new FileInfo(importVideo.FilePath);
                    var channelName = importVideo.Channel.ChannelName;

                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalPending, processedSoFar + localProcessed,
                        videoFile.Name, channelName, "Processing"));

                    if (!videoFile.Exists)
                    {
                        importVideo.Status = VideoImportStatus.Failed;
                        importVideo.ErrorMessage = "Video file not found";
                        await _dbContext.SaveChangesAsync();
                        localProcessed++;
                        
                        FileImportError?.Invoke(this, new FileImportErrorEventArgs(
                            videoFile.Name, channelName, new FileNotFoundException("Video file not found")));
                        
                        ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                            totalPending, processedSoFar + localProcessed,
                            videoFile.Name, channelName, "Failed (not found)"));
                        continue;
                    }

                    var channel = importVideo.Channel;

                    // Update status to uploading
                    importVideo.Status = VideoImportStatus.Uploading;
                    await _dbContext.SaveChangesAsync();

                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalPending, processedSoFar + localProcessed,
                        videoFile.Name, channelName, "Uploading"));

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

                    localProcessed++;
                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalPending, processedSoFar + localProcessed,
                        videoFile.Name, channelName, "Uploaded"));
                }
                catch (Exception ex)
                {
                    importVideo.Status = VideoImportStatus.Failed;
                    importVideo.ErrorMessage = ex.Message;
                    importVideo.RetryCount++;
                    importVideo.ProcessedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    localProcessed++;
                    FileImportError?.Invoke(this, new FileImportErrorEventArgs(
                        importVideo.FileName, importVideo.Channel.ChannelName, ex));

                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalPending, processedSoFar + localProcessed,
                        importVideo.FileName, importVideo.Channel.ChannelName, "Failed"));
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

                // Future enhancement: Check remote storage
                await Task.Delay(1);
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task UploadVideoFileAsync(FileInfo videoFile, string videoStoreRoot)
        {
            using var reader = videoFile.OpenRead();
            await _videoStorageClient.UploadAsync(reader, videoStoreRoot + videoFile.Name);
        }

        private async Task UploadThumbnailFileAsync(FileInfo thumbnailFile, string imageStoreRoot)
        {
            using var reader = thumbnailFile.OpenRead();
            await _imageStorageClient.UploadAsync(reader, imageStoreRoot + thumbnailFile.Name);
        }

        private async Task UpdateSessionStatusAsync(Guid sessionId)
        {
            if (_dbContext == null) return;

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

        private async Task<(int uploaded, int failed, int processed)> GetSessionStatsAsync(Guid sessionId)
        {
            if (_dbContext == null) return (0, 0, 0);

            var videoStats = await _dbContext.ImportVideos
                .Where(v => v.SessionId == sessionId)
                .GroupBy(v => v.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var uploaded = videoStats.FirstOrDefault(s => s.Status == VideoImportStatus.Uploaded)?.Count ?? 0;
            var failed = videoStats.FirstOrDefault(s => s.Status == VideoImportStatus.Failed)?.Count ?? 0;
            var processed = videoStats.Where(s => s.Status != VideoImportStatus.Pending).Sum(s => s.Count);

            return (uploaded, failed, processed);
        }

        #endregion
    }
}
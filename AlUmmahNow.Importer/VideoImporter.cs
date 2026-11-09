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
using AlUmmahNow.Data.Translation;
using Microsoft.EntityFrameworkCore;
using AlUmmahNow.Importer.Core;
using AlUmmahNow.Importer.Services;

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
        private readonly VideoTranslationService? _translationService;
        private ImportSessionLease? _lease;

        /// <summary>
        /// How long a running session may go without a heartbeat before another run may take it over
        /// (the runner refreshes it every quarter of this). Default 10 minutes.
        /// </summary>
        public TimeSpan SessionLease { get; set; } = TimeSpan.FromMinutes(10);

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
        /// Initializes a new instance with database session tracking and optional translation support
        /// </summary>
        public VideoImporter(
            Repository<Video> repo, 
            IStorageClient<Video> videoStorageClient, 
            IStorageClient<Image> imageStorageClient, 
            BunnyStreamClient bunnyStreamClient, 
            ImportDbContext dbContext,
            VideoTranslationService? translationService = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _videoStorageClient = videoStorageClient ?? throw new ArgumentNullException(nameof(videoStorageClient));
            _imageStorageClient = imageStorageClient ?? throw new ArgumentNullException(nameof(imageStorageClient));
            _bunnyStreamClient = bunnyStreamClient ?? throw new ArgumentNullException(nameof(bunnyStreamClient));
            _dbContext = dbContext;
            _translationService = translationService;
        }

        /// <summary>
        /// Initializes a new instance for basic upload operations without session tracking
        /// </summary>
        public VideoImporter(
            Repository<Video> repo,
            IStorageClient<Video> videoStorageClient,
            IStorageClient<Image> imageStorageClient,
            BunnyStreamClient bunnyStreamClient,
            VideoTranslationService? translationService = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _videoStorageClient = videoStorageClient ?? throw new ArgumentNullException(nameof(videoStorageClient));
            _imageStorageClient = imageStorageClient ?? throw new ArgumentNullException(nameof(imageStorageClient));
            _bunnyStreamClient = bunnyStreamClient ?? throw new ArgumentNullException(nameof(bunnyStreamClient));
            _dbContext = null;
            _translationService = translationService;
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

            // Create the session; refused if another run is importing this folder.
            var (session, lease) = await ImportSessionLease.StartNewAsync(_dbContext, rootFolder, userId, SessionLease);
            await using var _ = lease;
            _lease = lease;

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
                if (ex is not ImportLeaseLostException)
                {
                    session.Status = ImportStatus.Failed;
                    session.ErrorMessage = ex.Message;
                    session.EndTime = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                }
                
                ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(0, 0, 0, ex.Message));
                throw;
            }
            finally
            {
                _lease = null;
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

            // Take the session over; refused while another run holds it or imports the same folder.
            await using var lease = await ImportSessionLease.ResumeAsync(_dbContext, session, SessionLease);
            _lease = lease;

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
                if (ex is not ImportLeaseLostException)
                {
                    session.Status = ImportStatus.Failed;
                    session.ErrorMessage = ex.Message;
                    session.EndTime = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                }
                
                ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(0, 0, 0, ex.Message));
                return false;
            }
            finally
            {
                _lease = null;
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
                    await _videoStorageClient.UploadIfAbsentAsync(file, StorageUploads.EnsureTrailingSlash(videoStoreRoot) + file.Name);
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

                        // Create-only: a parallel import of the same library can't overwrite the record.
                        if (!await _repo.CreateObjectAsync("videos", video.ID.ToString(), video))
                        {
                            processedItems++;
                            ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                                totalItems, processedItems, bunnyVideo.Title ?? "Unknown", channelName, "Skipped (exists)"));
                            continue;
                        }
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

        /// <summary>
        /// Translate video metadata if translation service is available
        /// </summary>
        private async Task<TranslatedVideoMetadata?> TranslateVideoMetadataIfEnabledAsync(string title, string description, string channelName)
        {
            if (_translationService == null || !_translationService.IsTranslationAvailable)
                return null;

            try
            {
                var metadata = new VideoMetadata
                {
                    Title = title,
                    Description = description,
                    ChannelName = channelName
                };

                // Get translation settings - you can customize these
                var targetLanguages = new[] { "AR", "EN", "ES", "FR", "DE" }; // Default languages

                var translatedMetadata = await _translationService.TranslateVideoMetadataAsync(
                    metadata, 
                    targetLanguages);

                return translatedMetadata;
            }
            catch (Exception ex)
            {
                // Log translation failure but don't fail the import
                Console.WriteLine($"Translation failed for video '{title}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Store translated metadata as JSON string
        /// </summary>
        private string? SerializeTranslatedMetadata(TranslatedVideoMetadata? translatedMetadata)
        {
            if (translatedMetadata == null)
                return null;

            try
            {
                return JsonSerializer.Serialize(translatedMetadata, new JsonSerializerOptions 
                { 
                    WriteIndented = false 
                });
            }
            catch
            {
                return null;
            }
        }

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
                    ImageStoreRoot = $"/images-{channelName.ToLowerInvariant()}/",
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

            // This session's rows for this channel, keyed by path. Looking rows up by path alone would
            // pick up another session's row for the same file (and update or skip based on it).
            var sessionRows = await _dbContext.ImportVideos
                .Where(v => v.SessionId == sessionId && v.ChannelId == channel.Id)
                .ToDictionaryAsync(v => v.FilePath, StringComparer.Ordinal);

            int channelProcessed = 0;
            foreach (var (_, videoFile, thumbnailFile) in channelVideos)
            {
                sessionRows.TryGetValue(videoFile.FullName, out var importVideo);
                try
                {
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

                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalVideosAcrossAllChannels, processedSoFar + channelProcessed,
                        videoFile.Name, channelName, "Uploading"));

                    var outcome = await UploadRecordAndMarkAsync(channel, importVideo, videoFile, thumbnailFile);
                    channelProcessed++;

                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalVideosAcrossAllChannels, processedSoFar + channelProcessed,
                        videoFile.Name, channelName, Describe(outcome)));
                }
                catch (Exception ex) when (ex is not ImportLeaseLostException)
                {
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

                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalPending, processedSoFar + localProcessed,
                        videoFile.Name, channelName, "Uploading"));

                    var thumbnailFile = !string.IsNullOrEmpty(importVideo.ThumbnailPath) && File.Exists(importVideo.ThumbnailPath)
                        ? new FileInfo(importVideo.ThumbnailPath)
                        : null;
                    var outcome = await UploadRecordAndMarkAsync(importVideo.Channel, importVideo, videoFile, thumbnailFile);

                    localProcessed++;
                    ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                        totalPending, processedSoFar + localProcessed,
                        videoFile.Name, channelName, Describe(outcome)));
                }
                catch (Exception ex) when (ex is not ImportLeaseLostException)
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

        private enum ItemOutcome { Uploaded, AlreadyDone, ClaimedElsewhere }

        private static string Describe(ItemOutcome outcome) => outcome switch
        {
            ItemOutcome.AlreadyDone => "Skipped (already exists)",
            ItemOutcome.ClaimedElsewhere => "Skipped (being imported by another run)",
            _ => "Uploaded",
        };

        /// <summary>
        /// The one path that imports a file, used by both the first run and Resume: upload the video and
        /// thumbnail, write the <c>videos/&lt;id&gt;</c> catalogue record, then mark the row Uploaded.
        /// The row is only marked Uploaded after the record is written, so a failure at any step leaves it
        /// retryable and a row never says Uploaded without its record.
        /// </summary>
        private async Task<ItemOutcome> UploadRecordAndMarkAsync(ImportChannel channel, ImportVideo importVideo,
            FileInfo videoFile, FileInfo? thumbnailFile)
        {
            if (_dbContext == null) throw new InvalidOperationException("Database context is required.");
            _lease?.ThrowIfLost();

            // Claim the row: only one run moves it out of Pending/Failed.
            var now = DateTime.UtcNow;
            var claimed = await _dbContext.ImportVideos
                .Where(v => v.Id == importVideo.Id
                    && (v.Status == VideoImportStatus.Pending || v.Status == VideoImportStatus.Failed))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(v => v.Status, VideoImportStatus.Uploading)
                    .SetProperty(v => v.ProcessedAt, now));
            await _dbContext.Entry(importVideo).ReloadAsync();
            if (claimed == 0)
            {
                return ItemOutcome.ClaimedElsewhere;
            }

            // Upload video (keyed by its path inside the channel folder; skipped when the same file is
            // already stored there, refused when a different one is)
            var videoKey = StorageKeys.VideoKey(channel, videoFile);
            var videoUpload = await _videoStorageClient.UploadIfAbsentAsync(videoFile, channel.VideoStoreRoot + videoKey);

            if (thumbnailFile != null)
            {
                var thumbnailKey = StorageKeys.ThumbnailKey(videoKey, thumbnailFile);
                await _imageStorageClient.UploadIfAbsentAsync(thumbnailFile, channel.ImageStoreRoot + thumbnailKey);
                importVideo.ThumbnailUrl = $"{channel.ImageStoreUrl}{thumbnailKey}";
            }

            var videoTitle = Path.GetFileNameWithoutExtension(videoFile.Name);
            var videoDescription = videoTitle; // Use title as description by default
            var translatedMetadata = await TranslateVideoMetadataIfEnabledAsync(videoTitle, videoDescription, channel.ChannelName);

            var videoUrl = $"{channel.VideoStoreUrl}{videoKey}";
            var video = new Video
            {
                ID = await ResolveVideoIdAsync(importVideo, channel, videoKey, videoUrl),
                Title = videoTitle,
                Description = videoDescription,
                UserId = (await _dbContext.ImportSessions.FindAsync(importVideo.SessionId))?.UserId ?? "",
                VideoUrl = videoUrl,
                ThumbnailUrl = importVideo.ThumbnailUrl ?? "",
                Channel = channel.ChannelName,
                ChannelId = channel.ChannelId,
                DateModified = DateTime.UtcNow
            };

            // Create-only: a rerun or a parallel run that reaches the same file finds the record already there.
            var created = await _repo.CreateObjectAsync("videos", video.ID.ToString(), video);
            if (!created)
            {
                var existing = await _repo.GetObjectAsync("videos", video.ID.ToString());
                if (existing != null && !string.Equals(existing.VideoUrl, video.VideoUrl, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Record videos/{video.ID} already exists for '{existing.VideoUrl}', not '{video.VideoUrl}'.");
                }
            }

            importVideo.VideoId = video.ID.ToString();
            importVideo.VideoUrl = video.VideoUrl;
            importVideo.Status = VideoImportStatus.Uploaded;
            importVideo.ErrorMessage = null;
            importVideo.ProcessedAt = DateTime.UtcNow;
            importVideo.UploadedAt = DateTime.UtcNow;
            if (translatedMetadata != null)
            {
                importVideo.VideoMetadata = SerializeTranslatedMetadata(translatedMetadata);
            }
            await _dbContext.SaveChangesAsync();
            return !created && videoUpload == UploadOutcome.AlreadyPresent ? ItemOutcome.AlreadyDone : ItemOutcome.Uploaded;
        }

        /// <summary>
        /// The record id for a file: the id an earlier session already recorded for this exact file and URL
        /// (records written before ids were deterministic used random ids; reusing it avoids a second record),
        /// otherwise <see cref="VideoIds.ForFile"/>.
        /// </summary>
        private async Task<Guid> ResolveVideoIdAsync(ImportVideo importVideo, ImportChannel channel, string videoKey, string videoUrl)
        {
            var earlier = await _dbContext!.ImportVideos.AsNoTracking()
                .Where(v => v.FilePathHash == importVideo.FilePathHash
                    && v.Id != importVideo.Id
                    && v.Status == VideoImportStatus.Uploaded
                    && v.VideoId != null
                    && v.VideoUrl == videoUrl)
                .OrderBy(v => v.UploadedAt)
                .Select(v => v.VideoId)
                .FirstOrDefaultAsync();
            return Guid.TryParse(earlier, out var id) ? id : VideoIds.ForFile(channel.ChannelName, videoKey);
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
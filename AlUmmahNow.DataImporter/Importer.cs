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

namespace AlUmmahNow.DataImporter
{
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
                Console.WriteLine($"Starting multi-channel import from: {rootFolder}");
                
                // Scan for channels (subdirectories in root folder)
                var channelDirectories = new DirectoryInfo(rootFolder)
                    .GetDirectories()
                    .Where(d => !d.Name.StartsWith(".")) // Skip hidden folders
                    .ToArray();

                Console.WriteLine($"Found {channelDirectories.Length} potential channels");

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
                    Console.WriteLine("No videos found in any channel folders");
                    session.Status = ImportStatus.Completed;
                    session.EndTime = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    return session.Id;
                }

                // Create channels and videos in database
                await CreateChannelsAndVideosInDbAsync(session, allVideos, baseVideoStoreUrl, baseImageStoreUrl);

                // Process all videos with progress tracking
                using (var pbar = new ProgressBar(allVideos.Count, "Processing videos across all channels"))
                {
                    foreach (var channelGroup in allVideos.GroupBy(v => v.channel.Name))
                    {
                        var channelName = channelGroup.Key;
                        var channelVideos = channelGroup.ToList();
                        
                        using (var channelPbar = pbar.Spawn(channelVideos.Count, $"Processing {channelName}"))
                        {
                            await ProcessChannelVideosAsync(session.Id, channelName, channelVideos, channelPbar);
                        }
                    }
                }

                // Update session status
                await UpdateSessionStatusAsync(session.Id);
                
                Console.WriteLine($"Import session {session.Id} completed");
                return session.Id;
            }
            catch (Exception ex)
            {
                session.Status = ImportStatus.Failed;
                session.ErrorMessage = ex.Message;
                session.EndTime = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                
                Console.WriteLine($"Import session {session.Id} failed: {ex.Message}");
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
            {
                Console.WriteLine($"Session {sessionId} not found");
                return false;
            }

            if (session.Status == ImportStatus.Completed)
            {
                Console.WriteLine($"Session {sessionId} is already completed");
                return true;
            }

            Console.WriteLine($"Resuming import session {sessionId}");
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

                using (var pbar = new ProgressBar(pendingVideos.Count, "Resuming video processing"))
                {
                    foreach (var videoGroup in pendingVideos.GroupBy(v => v.Channel.ChannelName))
                    {
                        var channelName = videoGroup.Key;
                        var channelVideos = videoGroup.ToList();
                        
                        using (var channelPbar = pbar.Spawn(channelVideos.Count, $"Resuming {channelName}"))
                        {
                            await ProcessPendingVideosAsync(channelVideos, channelPbar);
                        }
                    }
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
                
                Console.WriteLine($"Resume failed for session {sessionId}: {ex.Message}");
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

        private async Task<List<(FileInfo video, FileInfo? thumbnail)>> ScanChannelForVideosAsync(DirectoryInfo channelDir)
        {
            var videos = new List<(FileInfo video, FileInfo? thumbnail)>();
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv" };

            // Recursively scan for videos
            await ScanDirectoryRecursiveAsync(channelDir, channelDir, videos, videoExtensions);

            Console.WriteLine($"Found {videos.Count} videos in channel: {channelDir.Name}");
            return videos;
        }

        private async Task ScanDirectoryRecursiveAsync(DirectoryInfo currentDir, DirectoryInfo channelRoot, 
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
                        await ScanDirectoryRecursiveAsync(subDir, channelRoot, videos, videoExtensions);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning directory {currentDir.FullName}: {ex.Message}");
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
            List<(DirectoryInfo channel, FileInfo video, FileInfo? thumbnail)> channelVideos, 
            IProgressBar channelPbar)
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
                    {
                        channelPbar.Tick($"Skipping {videoFile.Name} (already uploaded)");
                        continue;
                    }

                    // Check if already exists in remote storage
                    if (await CheckIfVideoExistsAsync(importVideo))
                    {
                        importVideo.Status = VideoImportStatus.Uploaded;
                        await _dbContext.SaveChangesAsync();
                        channelPbar.Tick($"Skipping {videoFile.Name} (already exists)");
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

                    channelPbar.Tick($"Uploaded {videoFile.Name}");
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

                    channelPbar.Tick($"Failed {videoFile.Name}: {ex.Message}");
                }
            }

            // Update channel processed count
            channel.ProcessedVideos = await _dbContext.ImportVideos
                .CountAsync(v => v.ChannelId == channel.Id && 
                               v.Status != VideoImportStatus.Pending);
            await _dbContext.SaveChangesAsync();
        }

        private async Task ProcessPendingVideosAsync(List<ImportVideo> pendingVideos, IProgressBar channelPbar)
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
                        channelPbar.Tick($"Failed {videoFile.Name} (not found)");
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

                    channelPbar.Tick($"Uploaded {videoFile.Name}");
                }
                catch (Exception ex)
                {
                    importVideo.Status = VideoImportStatus.Failed;
                    importVideo.ErrorMessage = ex.Message;
                    importVideo.RetryCount++;
                    importVideo.ProcessedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    channelPbar.Tick($"Failed {importVideo.FileName}: {ex.Message}");
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

                // Could also check remote storage here
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

            var totalVideos = session.TotalVideosFound;
            var pendingVideos = videoStats.FirstOrDefault(s => s.Status == VideoImportStatus.Pending)?.Count ?? 0;

            if (pendingVideos == 0)
            {
                session.Status = session.VideosFailed > 0 ? ImportStatus.Failed : ImportStatus.Completed;
                session.EndTime = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// Executes import operation based on provided arguments
        /// </summary>
        /// <param name="args">Import arguments (already validated by McMaster.Extensions.CommandLineUtils)</param>
        /// <returns>Exit code (0 for success, 1 for failure)</returns>
        public async Task<int> ExecuteImportAsync(ImportArgs args)
        {
            try
            {
                // Handle status request
                if (args.StatusSessionId.HasValue)
                {
                    return await HandleStatusRequestAsync(args.StatusSessionId.Value);
                }

                // Handle resume request  
                if (args.ResumeSessionId.HasValue)
                {
                    return await HandleResumeRequestAsync(args.ResumeSessionId.Value);
                }

                // Handle new multi-channel import
                return await HandleNewImportAsync(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Import operation failed: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Handles status request for an import session
        /// </summary>
        private async Task<int> HandleStatusRequestAsync(Guid sessionId)
        {
            try
            {
                var status = await GetImportStatusAsync(sessionId);
                
                Console.WriteLine($"Session: {status.SessionId}");
                Console.WriteLine($"Status: {status.Status}");
                Console.WriteLine($"Progress: {status.ProgressPercentage:F1}% ({status.VideosProcessed}/{status.TotalVideosFound})");
                Console.WriteLine($"Uploaded: {status.VideosUploaded}");
                Console.WriteLine($"Failed: {status.VideosFailed}");
                
                if (!string.IsNullOrEmpty(status.ErrorMessage))
                {
                    Console.WriteLine($"Error: {status.ErrorMessage}");
                }

                Console.WriteLine("\nChannels:");
                foreach (var channel in status.Channels)
                {
                    Console.WriteLine($"  {channel.ChannelName}: {channel.ProgressPercentage:F1}% ({channel.ProcessedVideos}/{channel.TotalVideos}) - Uploaded: {channel.UploadedVideos}, Failed: {channel.FailedVideos}");
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting status: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Handles resume request for an import session
        /// </summary>
        private async Task<int> HandleResumeRequestAsync(Guid sessionId)
        {
            try
            {
                var success = await ResumeImportAsync(sessionId);
                Console.WriteLine(success ? "Import resumed successfully" : "Failed to resume import");
                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resuming import: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Handles new multi-channel import
        /// </summary>
        private async Task<int> HandleNewImportAsync(ImportArgs args)
        {
            try
            {
                var sessionId = await ImportMultiChannelAsync(
                    args.FolderPath, 
                    args.UserId, 
                    args.BaseVideoUrl, 
                    args.BaseImageUrl);

                Console.WriteLine($"Import completed. Session ID: {sessionId}");
                
                // Show final status
                var finalStatus = await GetImportStatusAsync(sessionId);
                Console.WriteLine($"Final Status: {finalStatus.Status}");
                Console.WriteLine($"Total Videos: {finalStatus.TotalVideosFound}");
                Console.WriteLine($"Uploaded: {finalStatus.VideosUploaded}");
                Console.WriteLine($"Failed: {finalStatus.VideosFailed}");
                
                return finalStatus.Status == ImportStatus.Completed ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Import failed: {ex.Message}");
                return 1;
            }
        }

        // Keep existing methods for backward compatibility
        public async Task Import(string folder, string userId, string channelName, string channelThumbnail, string videoStoreRoot, string videoStoreUrl, string imageStoreRoot, string imageStoreUrl)
        {
            // Legacy single-channel import - redirect to new method
            await ImportMultiChannelAsync(Path.GetDirectoryName(folder) ?? folder, userId, videoStoreUrl, imageStoreUrl);
        }

        public async Task ImportFromBunnyApi(string libraryId, string userId, string channelName, string channelThumbnail, string videoStoreRoot, string videoStoreUrl, string imageStoreRoot, string imageStoreUrl)
        {
            //await Purge();
            var response = await _bunnyStreamClient.GetVideosAsync(libraryId, 1);
            var thumbnails = await _imageStorageClient.ListAsync(imageStoreRoot);

            using (var pbar = new ProgressBar(response.TotalItems, $"Importing videos from Bunny.net API", new ProgressBarOptions { }))
            {
                do
                {
                    foreach (var bunnyVideo in response.Items)
                    {
                        try
                        {
                            var name = bunnyVideo.Title;
                            var video = await _repo.GetObjectAsync("videos", bunnyVideo.Guid);
                            if (video != null)
                            {
                                //pbar.Tick($"{name} found, {name} already uploaded.");
                                //continue;
                            }
                            string? thumbnailUrl;
                            var thumbnail = thumbnails.FirstOrDefault(f => f.ObjectName == name.Replace(Path.GetExtension(name), ".jpg"));
                            thumbnailUrl = thumbnail == null ? $"https://vz-215a1ffd-a5a.b-cdn.net/{bunnyVideo.Guid}/{bunnyVideo.ThumbnailFileName}" : imageStoreUrl + thumbnail.ObjectName;
                            video = new Video
                            {
                                ID = Guid.Parse(bunnyVideo.Guid),
                                Title = name,
                                Description = bunnyVideo.Description,
                                UserId = userId,
                                VideoUrl = bunnyVideo.VideoUrl,
                                ThumbnailUrl = thumbnailUrl,
                                Channel = channelName,
                                ChannelThumbnailUrl = channelThumbnail,
                                DateModified = DateTime.UtcNow,
                                DateCreated = bunnyVideo.DateUploaded,
                                Width = bunnyVideo.Width,
                                Height = bunnyVideo.Height,
                                AvailableResolutions = bunnyVideo.AvailableResolutions?.Split(',').ToList() ?? new List<string>(),
                            };
                            await _repo.SaveObjectAsync("videos", video.ID.ToString(), video);
                            pbar.Tick($"{name} uploaded.");
                        }
                        catch (Exception ex)
                        {
                            pbar.Tick($"{bunnyVideo.Title} was not saved, {ex.Message}");
                        }
                    }
                    response = await _bunnyStreamClient.GetVideosAsync(libraryId, response.CurrentPage + 1);
                } while ((response.CurrentPage - 1) * response.ItemsPerPage < response.TotalItems);
            }
        }

        private async Task Purge()
        {
            try
            {
                var videos = new List<string>();
                do
                {
                    videos = await _repo.ListObjectsAsync("videos");
                    foreach (var item in videos)
                    {
                        await _repo.DeleteObjectAsync("videos", item);
                    }
                } while (videos.Count > 0);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async Task<Video?> GetVideoInfo(FileInfo file)
        {
            var infoFile = new FileInfo(Path.ChangeExtension(file.FullName, ".r2.json"));
            if (infoFile.Exists)
            {
                using (var reader = infoFile.OpenRead())
                {
                    var jsonString = await new StreamReader(reader).ReadToEndAsync();
                    return JsonSerializer.Deserialize<Video>(jsonString);
                }
            }
            return null;
        }

        private async Task UploadVideo(FileInfo file, string videoStoreRoot)
        {
            using (var reader = file.OpenRead())
            {
                await _videoStorageClient.UploadAsync(reader, videoStoreRoot + file.Name);
            }
        }

        private FileInfo? GetThumbnail(DirectoryInfo directory, DirectoryInfo thumbs, FileInfo file)
        {
            var thumbnail = directory.GetFiles().FirstOrDefault(f => f.Name == file.Name.Replace(file.Extension, ".jpg"));
            if (thumbnail == null && thumbs.Exists)
            {
                thumbnail = thumbs.GetFiles(file.Name.Replace(file.Extension, ".jpg")).FirstOrDefault();
            }
            return thumbnail;
        }

        private async Task UploadThumbnail(FileInfo thumbnail, string imageStoreRoot)
        {
            using (var reader = thumbnail.OpenRead())
            {
                await _imageStorageClient.UploadAsync(reader, imageStoreRoot + thumbnail.Name);
            }
        }

        private async Task SaveVideoMetadata(FileInfo file, Video video)
        {
            await _repo.SaveObjectAsync("videos", video.ID.ToString(), video);
            using (var saved = File.OpenWrite(Path.ChangeExtension(file.FullName, ".r2.json")))
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                var jsonString = JsonSerializer.Serialize(video, options);
                var bytes = Encoding.UTF8.GetBytes(jsonString);
                await saved.WriteAsync(bytes, 0, bytes.Length);
            }
        }
    }
}
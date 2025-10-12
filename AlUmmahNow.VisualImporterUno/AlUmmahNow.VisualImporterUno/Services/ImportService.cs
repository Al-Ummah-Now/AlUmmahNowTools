using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Importer;
using AlUmmahNow.Importer.Core;
using AlUmmahNow.VisualImporterUno.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.IO;

namespace AlUmmahNow.VisualImporterUno.Services
{
    public class ImportService
    {
        private readonly VideoImporter _importer;
        private readonly ImportDbContext _dbContext;
        private readonly FileDiscoveryService _fileDiscoveryService;

        public event EventHandler<ImportProgressEventArgs>? ProgressChanged;
        public event EventHandler<ImportCompletedEventArgs>? ImportCompleted;

        public ImportService(VideoImporter importer, ImportDbContext dbContext, FileDiscoveryService fileDiscoveryService)
        {
            _importer = importer ?? throw new ArgumentNullException(nameof(importer));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _fileDiscoveryService = fileDiscoveryService ?? throw new ArgumentNullException(nameof(fileDiscoveryService));

            // Subscribe to importer events
            _importer.ProgressChanged += OnImporterProgressChanged;
            _importer.ImportCompleted += OnImporterCompleted;
        }

        /// <summary>
        /// Scan directory for video files without starting import
        /// </summary>
        /// <param name="folderPath">Path to scan</param>
        /// <param name="scanChannels">Whether to scan as multi-channel (subdirectories) or single directory</param>
        /// <returns>Discovered video files</returns>
        public async Task<List<VideoFileInfo>> ScanDirectoryAsync(string folderPath, bool scanChannels = true)
        {
            // Subscribe to file discovery progress events
            _fileDiscoveryService.ProgressChanged += OnFileDiscoveryProgressChanged;
            
            try
            {
                if (scanChannels)
                {
                    var channels = await _fileDiscoveryService.ScanChannelsAsync(folderPath);
                    var allFiles = channels.SelectMany(c => c.Value).ToList();
                    
                    // Debug output
                    System.Diagnostics.Debug.WriteLine($"Channel scan complete: {channels.Count} channels, {allFiles.Count} total files");
                    foreach (var channel in channels)
                    {
                        System.Diagnostics.Debug.WriteLine($"Channel '{channel.Key}': {channel.Value.Count} files");
                    }
                    
                    return allFiles;
                }
                else
                {
                    var files = await _fileDiscoveryService.ScanDirectoryAsync(folderPath, true);
                    System.Diagnostics.Debug.WriteLine($"Direct scan complete: {files.Count} files");
                    return files;
                }
            }
            finally
            {
                // Unsubscribe to prevent memory leaks
                _fileDiscoveryService.ProgressChanged -= OnFileDiscoveryProgressChanged;
            }
        }

        /// <summary>
        /// Scan directory and group by channels
        /// </summary>
        /// <param name="folderPath">Path to scan</param>
        /// <returns>Dictionary with channel names and their video files</returns>
        public async Task<Dictionary<string, List<VideoFileInfo>>> ScanChannelsAsync(string folderPath)
        {
            return await _fileDiscoveryService.ScanChannelsAsync(folderPath);
        }

        /// <summary>
        /// Get file statistics for discovered videos
        /// </summary>
        /// <param name="videoFiles">List of video files</param>
        /// <returns>File statistics</returns>
        public FileStatistics GetFileStatistics(List<VideoFileInfo> videoFiles)
        {
            var stats = _fileDiscoveryService.GetFileStatistics(videoFiles);
            
            // Add import status statistics
            stats.ReadyFiles = videoFiles.Count(f => f.ImportStatus == ImportFileStatus.NotImported && !f.IsDuplicate);
            stats.PendingFiles = videoFiles.Count(f => f.ImportStatus == ImportFileStatus.Pending);
            stats.UploadingFiles = videoFiles.Count(f => f.ImportStatus == ImportFileStatus.Uploading);
            stats.UploadedFiles = videoFiles.Count(f => f.ImportStatus == ImportFileStatus.Uploaded);
            stats.FailedFiles = videoFiles.Count(f => f.ImportStatus == ImportFileStatus.Failed);
            stats.SkippedFiles = videoFiles.Count(f => f.ImportStatus == ImportFileStatus.Skipped);
            
            return stats;
        }

        /// <summary>
        /// Start import with selected video files
        /// </summary>
        /// <param name="videoFiles">Selected video files to import</param>
        /// <param name="userId">User ID for the import</param>
        /// <param name="baseVideoUrl">Base video storage URL</param>
        /// <param name="baseImageUrl">Base image storage URL</param>
        /// <returns>Import session ID</returns>
        public async Task<Guid> StartImportWithFilesAsync(List<VideoFileInfo> videoFiles, string? userId = null, string? baseVideoUrl = null, string? baseImageUrl = null)
        {
            if (!videoFiles.Any())
                throw new ArgumentException("No video files selected for import");

            // Use default values if not provided
            userId ??= "visual-importer-user";
            baseVideoUrl ??= "https://cdn.alummahnow.net";
            baseImageUrl ??= "https://images.alummahnow.net";

            // Create temporary folder structure for the importer
            var tempRoot = Path.Combine(Path.GetTempPath(), $"visual-import-{Guid.NewGuid():N}");
            
            try
            {
                await CreateTemporaryFolderStructure(videoFiles, tempRoot);
                
                var sessionId = await _importer.ImportMultiChannelAsync(tempRoot, userId, baseVideoUrl, baseImageUrl);
                return sessionId;
            }
            finally
            {
                // Clean up temporary folder structure
                if (Directory.Exists(tempRoot))
                {
                    try
                    {
                        Directory.Delete(tempRoot, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }

        /// <summary>
        /// Start a new import session with default parameters
        /// </summary>
        public async Task<Guid> StartImportAsync(string folderPath)
        {
            // Use default values for the required parameters
            var userId = "visual-importer-user";
            var baseVideoUrl = "https://cdn.alummahnow.net";
            var baseImageUrl = "https://images.alummahnow.net";

            return await StartImportAsync(folderPath, userId, baseVideoUrl, baseImageUrl);
        }

        /// <summary>
        /// Start a new import session
        /// </summary>
        public async Task<Guid> StartImportAsync(string folderPath, string userId, string baseVideoUrl, string baseImageUrl)
        {
            var sessionId = await _importer.ImportMultiChannelAsync(folderPath, userId, baseVideoUrl, baseImageUrl);
            return sessionId;
        }

        /// <summary>
        /// Resume an existing import session
        /// </summary>
        public async Task<bool> ResumeImportAsync(Guid sessionId)
        {
            return await _importer.ResumeImportAsync(sessionId);
        }

        /// <summary>
        /// Get status of an import session
        /// </summary>
        public async Task<ImportSessionStatus> GetImportStatusAsync(Guid sessionId)
        {
            return await _importer.GetImportStatusAsync(sessionId);
        }

        /// <summary>
        /// Get all import sessions
        /// </summary>
        public async Task<List<ImportSession>> GetAllImportSessionsAsync()
        {
            return await _dbContext.ImportSessions
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();
        }

        /// <summary>
        /// Get videos for a specific session
        /// </summary>
        public async Task<List<ImportVideo>> GetSessionVideosAsync(Guid sessionId)
        {
            return await _dbContext.ImportVideos
                .Where(v => v.SessionId == sessionId)
                .Include(v => v.Channel)
                .OrderBy(v => v.FileName)
                .ToListAsync();
        }

        /// <summary>
        /// Cancel an import session (mark as cancelled)
        /// </summary>
        public async Task<bool> CancelImportAsync(Guid sessionId)
        {
            var session = await _dbContext.ImportSessions.FindAsync(sessionId);
            if (session == null) return false;

            session.Status = ImportStatus.Cancelled;
            session.EndTime = DateTime.UtcNow;
            
            await _dbContext.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Sort video files by specified criteria
        /// </summary>
        /// <param name="videoFiles">Files to sort</param>
        /// <param name="sortOption">Sort criteria</param>
        /// <param name="direction">Sort direction</param>
        /// <returns>Sorted list of video files</returns>
        public List<VideoFileInfo> SortVideoFiles(IEnumerable<VideoFileInfo> videoFiles, VideoSortOption sortOption, SortDirection direction)
        {
            IOrderedEnumerable<VideoFileInfo> sorted = sortOption switch
            {
                VideoSortOption.Name => direction == SortDirection.Ascending ? 
                    videoFiles.OrderBy(v => v.FileName) : 
                    videoFiles.OrderByDescending(v => v.FileName),
                VideoSortOption.Size => direction == SortDirection.Ascending ? 
                    videoFiles.OrderBy(v => v.FileSize) : 
                    videoFiles.OrderByDescending(v => v.FileSize),
                VideoSortOption.DateCreated => direction == SortDirection.Ascending ? 
                    videoFiles.OrderBy(v => v.CreatedDate) : 
                    videoFiles.OrderByDescending(v => v.CreatedDate),
                VideoSortOption.DateModified => direction == SortDirection.Ascending ? 
                    videoFiles.OrderBy(v => v.ModifiedDate) : 
                    videoFiles.OrderByDescending(v => v.ModifiedDate),
                VideoSortOption.Directory => direction == SortDirection.Ascending ? 
                    videoFiles.OrderBy(v => v.Directory).ThenBy(v => v.FileName) : 
                    videoFiles.OrderByDescending(v => v.Directory).ThenByDescending(v => v.FileName),
                VideoSortOption.ImportStatus => direction == SortDirection.Ascending ? 
                    videoFiles.OrderBy(v => v.ImportStatus).ThenBy(v => v.FileName) : 
                    videoFiles.OrderByDescending(v => v.ImportStatus).ThenByDescending(v => v.FileName),
                _ => videoFiles.OrderBy(v => v.FileName)
            };

            return sorted.ToList();
        }

        /// <summary>
        /// Filter video files based on criteria
        /// </summary>
        /// <param name="videoFiles">Files to filter</param>
        /// <param name="hideDuplicates">Whether to hide duplicate files</param>
        /// <param name="onlySelected">Whether to show only selected files</param>
        /// <param name="channelFilter">Channel name filter (null for all channels)</param>
        /// <returns>Filtered list of video files</returns>
        public List<VideoFileInfo> FilterVideoFiles(IEnumerable<VideoFileInfo> videoFiles, bool hideDuplicates, bool onlySelected, string? channelFilter = null)
        {
            var filtered = videoFiles.AsEnumerable();

            if (hideDuplicates)
            {
                filtered = filtered.Where(v => !v.IsDuplicate);
            }

            if (onlySelected)
            {
                filtered = filtered.Where(v => v.IsSelected);
            }

            if (!string.IsNullOrEmpty(channelFilter))
            {
                filtered = filtered.Where(v => v.ChannelName.Equals(channelFilter, StringComparison.OrdinalIgnoreCase));
            }

            return filtered.ToList();
        }

        /// <summary>
        /// Import selected video files with progress tracking
        /// </summary>
        /// <param name="videoFiles">Selected video files to import</param>
        /// <param name="folderPath">Original folder path for context</param>
        /// <returns>Import session ID</returns>
        public async Task<Guid> ImportVideosAsync(List<VideoFileInfo> videoFiles, string folderPath)
        {
            if (!videoFiles.Any())
                throw new ArgumentException("No video files selected for import");

            // Report initial progress
            ProgressChanged?.Invoke(this, new ImportProgressEventArgs
            {
                CurrentOperation = "Preparing import...",
                CurrentFile = "",
                TotalFiles = videoFiles.Count,
                ProcessedFiles = 0,
                ProgressPercentage = 0,
                Status = "Preparing"
            });

            // Start the import using the selected files
            var sessionId = await StartImportWithFilesAsync(videoFiles);

            return sessionId;
        }

        private async Task CreateTemporaryFolderStructure(List<VideoFileInfo> videoFiles, string tempRoot)
        {
            // Group files by channel (directory)
            var channelGroups = videoFiles.Where(v => v.IsSelected && !v.IsDuplicate)
                .GroupBy(v => v.ChannelName)
                .ToList();

            foreach (var channelGroup in channelGroups)
            {
                var channelPath = Path.Combine(tempRoot, channelGroup.Key);
                Directory.CreateDirectory(channelPath);

                foreach (var videoFile in channelGroup)
                {
                    // Create a file list that the importer can process
                    // For now, we'll store the original paths in a manifest file
                    var manifestPath = Path.Combine(channelPath, "file-manifest.txt");
                    await File.AppendAllTextAsync(manifestPath, $"{videoFile.FilePath}\n");

                    // Also copy thumbnail if exists
                    if (!string.IsNullOrEmpty(videoFile.ThumbnailPath) && File.Exists(videoFile.ThumbnailPath))
                    {
                        var thumbnailDestination = Path.Combine(channelPath, Path.GetFileName(videoFile.ThumbnailPath));
                        if (!File.Exists(thumbnailDestination))
                        {
                            File.Copy(videoFile.ThumbnailPath, thumbnailDestination, false);
                        }
                    }
                }
            }
        }

        private void OnImporterProgressChanged(object? sender, ImportProgressEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }

        private void OnImporterCompleted(object? sender, ImportCompletedEventArgs e)
        {
            ImportCompleted?.Invoke(this, e);
        }

        private void OnFileDiscoveryProgressChanged(object? sender, FileDiscoveryProgressEventArgs e)
        {
            // Create import progress event args and forward to UI
            ProgressChanged?.Invoke(this, new ImportProgressEventArgs
            {
                CurrentOperation = e.CurrentOperation,
                CurrentFile = "", // Not applicable during discovery
                TotalFiles = e.TotalFolders,
                ProcessedFiles = e.FoldersScanned,
                ProgressPercentage = e.ProgressPercentage,
                Status = "Scanning",
                FilesFound = e.FilesFound // Pass the files found count from discovery
            });
        }

        /// <summary>
        /// Simulate progress updates for UI testing
        /// </summary>
        public void SimulateProgress(ImportSessionStatus status)
        {
            ProgressChanged?.Invoke(this, new ImportProgressEventArgs
            {
                CurrentOperation = "Processing videos...",
                CurrentFile = "video.mp4",
                TotalFiles = status.TotalVideosFound,
                ProcessedFiles = status.VideosProcessed,
                ProgressPercentage = status.ProgressPercentage,
                Status = status.Status.ToString()
            });
        }
    }
}
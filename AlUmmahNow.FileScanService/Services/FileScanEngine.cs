using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Main file scanning engine that processes directories and files
    /// </summary>
    public class FileScanEngine : IFileScanEngine
    {
        private readonly ILogger<FileScanEngine> _logger;
        private readonly ImportDbContext _dbContext;
        private readonly IHashCalculator _hashCalculator;
        private readonly IMetadataExtractor _metadataExtractor;
        
        private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".flv", ".webm" };
        private static readonly string[] ThumbnailExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

        public event EventHandler<ScanProgressEventArgs>? ProgressChanged;

        public FileScanEngine(
            ILogger<FileScanEngine> logger,
            ImportDbContext dbContext,
            IHashCalculator hashCalculator,
            IMetadataExtractor metadataExtractor)
        {
            _logger = logger;
            _dbContext = dbContext;
            _hashCalculator = hashCalculator;
            _metadataExtractor = metadataExtractor;
        }

        public async Task ProcessScanAsync(ScanSession scanSession, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(scanSession.RootPath))
            {
                throw new DirectoryNotFoundException($"Scan root directory not found: {scanSession.RootPath}");
            }

            _logger.LogInformation("Starting scan of {RootPath}", scanSession.RootPath);

            // Phase 1: Count directories for progress tracking
            await UpdateScanProgress(scanSession, "Analyzing directory structure...", 0, 0, 0, 0);
            var totalDirectories = await CountDirectoriesAsync(scanSession.RootPath, cancellationToken);
            
            scanSession.TotalDirectories = totalDirectories;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Phase 2: Scan directories and files
            var directoryQueue = new Queue<(DirectoryInfo dir, int depth, string channelName, Guid? parentChannelId)>();
            var rootDir = new DirectoryInfo(scanSession.RootPath);
            
            // Determine initial scanning strategy
            var rootFiles = rootDir.GetFiles()
                .Where(f => VideoExtensions.Contains(f.Extension.ToLowerInvariant()))
                .ToArray();
            
            var subdirectories = rootDir.GetDirectories()
                .Where(d => !d.Name.StartsWith(".") && !d.Name.StartsWith("_"))
                .ToArray();

            if (rootFiles.Length > 0 && subdirectories.Length == 0)
            {
                // Single channel scenario - treat root as one channel
                directoryQueue.Enqueue((rootDir, 0, rootDir.Name, null));
            }
            else if (rootFiles.Length > 0)
            {
                // Mixed content - create root channel and scan subdirectories
                directoryQueue.Enqueue((rootDir, 0, "Root", null));
                foreach (var subdir in subdirectories)
                {
                    directoryQueue.Enqueue((subdir, 1, subdir.Name, null));
                }
            }
            else
            {
                // Multi-channel scenario - each subdirectory is a channel
                foreach (var subdir in subdirectories)
                {
                    directoryQueue.Enqueue((subdir, 0, subdir.Name, null));
                }
            }

            var processedDirectories = 0;
            var totalFilesFound = 0;
            var channels = new Dictionary<string, ScannedChannel>();

            while (directoryQueue.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var (currentDir, depth, channelName, parentChannelId) = directoryQueue.Dequeue();
                processedDirectories++;

                var progress = (double)processedDirectories / totalDirectories * 80; // Reserve 20% for duplicate detection
                await UpdateScanProgress(scanSession, $"Scanning: {GetDisplayPath(currentDir.FullName)}", progress, totalFilesFound, processedDirectories, totalDirectories);

                try
                {
                    // Get or create channel
                    if (!channels.TryGetValue(channelName, out var channel))
                    {
                        channel = await GetOrCreateChannelAsync(scanSession.Id, channelName, currentDir.FullName, depth, parentChannelId);
                        channels[channelName] = channel;
                    }

                    // Scan files in current directory
                    var videoFiles = currentDir.GetFiles()
                        .Where(f => VideoExtensions.Contains(f.Extension.ToLowerInvariant()))
                        .ToArray();

                    foreach (var videoFile in videoFiles)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        await ProcessVideoFileAsync(scanSession.Id, channel, videoFile, cancellationToken);
                        totalFilesFound++;
                        
                        // Report progress with current file
                        await UpdateScanProgress(scanSession, $"Scanning: {GetDisplayPath(currentDir.FullName)}", progress, totalFilesFound, processedDirectories, totalDirectories, videoFile.Name);
                    }

                    // Queue subdirectories
                    var subdirs = currentDir.GetDirectories()
                        .Where(d => !d.Name.StartsWith(".") && !d.Name.StartsWith("_"));

                    foreach (var subdir in subdirs)
                    {
                        // For multi-level structures, continue with same channel name but track depth
                        directoryQueue.Enqueue((subdir, depth + 1, channelName, channel.Id));
                    }

                    // Update channel statistics
                    await UpdateChannelStatisticsAsync(channel.Id);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning("Access denied to directory {Directory}: {Error}", currentDir.FullName, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scanning directory {Directory}", currentDir.FullName);
                }
            }

            // Phase 3: Detect duplicates
            await UpdateScanProgress(scanSession, "Detecting duplicates...", 85, totalFilesFound, processedDirectories, totalDirectories);
            await DetectDuplicatesAsync(scanSession.Id, cancellationToken);

            // Phase 4: Update final statistics
            await UpdateScanProgress(scanSession, "Finalizing scan results...", 95, totalFilesFound, processedDirectories, totalDirectories);
            scanSession.FilesFound = totalFilesFound;
            scanSession.DirectoriesProcessed = processedDirectories;
            
            // Calculate total size
            var totalSize = await _dbContext.ScannedVideoFiles
                .Where(f => f.ScanSessionId == scanSession.Id)
                .SumAsync(f => f.FileSize, cancellationToken);
            
            scanSession.TotalSize = totalSize;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Scan completed. Found {FileCount} files in {DirectoryCount} directories", 
                totalFilesFound, processedDirectories);
        }

        private async Task<int> CountDirectoriesAsync(string rootPath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var count = 0;
                var stack = new Stack<DirectoryInfo>();
                stack.Push(new DirectoryInfo(rootPath));

                while (stack.Count > 0 && !cancellationToken.IsCancellationRequested)
                {
                    var currentDir = stack.Pop();
                    count++;

                    try
                    {
                        var subdirs = currentDir.GetDirectories()
                            .Where(d => !d.Name.StartsWith(".") && !d.Name.StartsWith("_"));

                        foreach (var subdir in subdirs)
                        {
                            stack.Push(subdir);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip directories we can't access
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Skip directories that no longer exist
                    }
                }

                return count;
            }, cancellationToken);
        }

        private async Task<ScannedChannel> GetOrCreateChannelAsync(Guid scanSessionId, string channelName, string path, int depth, Guid? parentChannelId)
        {
            var existingChannel = await _dbContext.ScannedChannels
                .FirstOrDefaultAsync(c => c.ScanSessionId == scanSessionId && c.Name == channelName);

            if (existingChannel != null)
                return existingChannel;

            var rootPath = await _dbContext.ScanSessions
                .Where(s => s.Id == scanSessionId)
                .Select(s => s.RootPath)
                .FirstAsync();

            var relativePath = Path.GetRelativePath(rootPath, path);

            var channel = new ScannedChannel
            {
                ScanSessionId = scanSessionId,
                Name = channelName,
                Path = path,
                RelativePath = relativePath,
                DepthLevel = depth,
                ParentChannelId = parentChannelId,
                DirectoryExists = Directory.Exists(path)
            };

            _dbContext.ScannedChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            return channel;
        }

        private async Task ProcessVideoFileAsync(Guid scanSessionId, ScannedChannel channel, FileInfo videoFile, CancellationToken cancellationToken)
        {
            try
            {
                // Calculate file hash for change detection
                var contentHash = await _hashCalculator.CalculateFileHashAsync(videoFile.FullName, cancellationToken);

                // Check if file already exists in database
                var existingFile = await _dbContext.ScannedVideoFiles
                    .FirstOrDefaultAsync(f => f.ScanSessionId == scanSessionId && f.FilePath == videoFile.FullName, cancellationToken);

                var isNewFile = existingFile == null;
                var hasChanged = !isNewFile && (existingFile.ContentHash != contentHash || existingFile.LastKnownModified != videoFile.LastWriteTime);

                if (!isNewFile && !hasChanged)
                {
                    // File hasn't changed, just update last scanned time
                    existingFile!.LastScanned = DateTime.UtcNow;
                    existingFile.FileExists = true;
                    return;
                }

                // Find associated files
                var thumbnailFile = FindThumbnailForVideo(videoFile);
                var infoFile = FindInfoFileForVideo(videoFile);

                // Extract metadata
                var metadata = await _metadataExtractor.ExtractVideoMetadataAsync(
                    videoFile.FullName, infoFile?.FullName, cancellationToken);

                // Calculate metadata hash
                var metadataContent = $"{metadata.DurationSeconds}|{metadata.Width}|{metadata.Height}|{metadata.Codec}|{metadata.InfoFileContent}";
                if (thumbnailFile != null)
                    metadataContent += $"|{thumbnailFile.Length}|{thumbnailFile.LastWriteTime:O}";
                
                var metadataHash = await _hashCalculator.CalculateContentHashAsync(metadataContent, cancellationToken);

                var scannedFile = existingFile ?? new ScannedVideoFile { ScanSessionId = scanSessionId };

                // Update file information
                scannedFile.FilePath = videoFile.FullName;
                scannedFile.FileName = videoFile.Name;
                scannedFile.Directory = videoFile.DirectoryName ?? string.Empty;
                scannedFile.ChannelName = channel.Name;
                scannedFile.RelativePath = Path.GetRelativePath(
                    await _dbContext.ScanSessions.Where(s => s.Id == scanSessionId).Select(s => s.RootPath).FirstAsync(),
                    videoFile.FullName);
                scannedFile.Extension = videoFile.Extension.ToLowerInvariant();
                scannedFile.FileSize = videoFile.Length;
                scannedFile.CreatedDate = videoFile.CreationTime;
                scannedFile.ModifiedDate = videoFile.LastWriteTime;
                scannedFile.ContentHash = contentHash;
                scannedFile.MetadataHash = metadataHash;
                scannedFile.LastKnownModified = videoFile.LastWriteTime;
                scannedFile.LastScanned = DateTime.UtcNow;
                scannedFile.FileExists = true;

                // Thumbnail information
                if (thumbnailFile != null)
                {
                    scannedFile.ThumbnailPath = thumbnailFile.FullName;
                    scannedFile.ThumbnailSize = thumbnailFile.Length;
                    scannedFile.ThumbnailHash = await _hashCalculator.CalculateFileHashAsync(thumbnailFile.FullName, cancellationToken);
                }

                // Info file information
                if (infoFile != null)
                {
                    scannedFile.InfoFilePath = infoFile.FullName;
                    scannedFile.InfoFileContent = metadata.InfoFileContent;
                    scannedFile.InfoFileHash = metadata.InfoFileHash;
                }

                // Video metadata
                scannedFile.DurationSeconds = metadata.DurationSeconds;
                scannedFile.Width = metadata.Width;
                scannedFile.Height = metadata.Height;
                scannedFile.Bitrate = metadata.Bitrate;
                scannedFile.Codec = metadata.Codec;

                if (isNewFile)
                {
                    scannedFile.FirstScanned = DateTime.UtcNow;
                    _dbContext.ScannedVideoFiles.Add(scannedFile);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing video file {FilePath}", videoFile.FullName);
            }
        }

        private FileInfo? FindThumbnailForVideo(FileInfo videoFile)
        {
            var baseFileName = Path.GetFileNameWithoutExtension(videoFile.Name);
            var directory = videoFile.Directory!;

            // Check for thumbnail in same directory
            foreach (var ext in ThumbnailExtensions)
            {
                var thumbnailPath = Path.Combine(directory.FullName, baseFileName + ext);
                if (File.Exists(thumbnailPath))
                {
                    return new FileInfo(thumbnailPath);
                }
            }

            // Check for thumbnail in .thumbs subdirectory
            var thumbsDir = Path.Combine(directory.FullName, ".thumbs");
            if (Directory.Exists(thumbsDir))
            {
                foreach (var ext in ThumbnailExtensions)
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

        private FileInfo? FindInfoFileForVideo(FileInfo videoFile)
        {
            var baseFileName = Path.GetFileNameWithoutExtension(videoFile.Name);
            var infoPath = Path.Combine(videoFile.Directory!.FullName, baseFileName + ".info.json");
            
            return File.Exists(infoPath) ? new FileInfo(infoPath) : null;
        }

        private async Task DetectDuplicatesAsync(Guid scanSessionId, CancellationToken cancellationToken)
        {
            // Reset duplicate flags
            await _dbContext.ScannedVideoFiles
                .Where(f => f.ScanSessionId == scanSessionId)
                .ExecuteUpdateAsync(f => f
                    .SetProperty(p => p.IsDuplicate, false)
                    .SetProperty(p => p.OriginalFileId, (Guid?)null), cancellationToken);

            // Group by content hash and file size
            var duplicateGroups = await _dbContext.ScannedVideoFiles
                .Where(f => f.ScanSessionId == scanSessionId && f.FileExists)
                .GroupBy(f => new { f.ContentHash, f.FileSize })
                .Where(g => g.Count() > 1)
                .ToListAsync(cancellationToken);

            foreach (var group in duplicateGroups)
            {
                var files = group.OrderBy(f => f.FilePath).ToList();
                var originalFile = files.First();

                // Mark subsequent files as duplicates
                for (int i = 1; i < files.Count; i++)
                {
                    files[i].IsDuplicate = true;
                    files[i].OriginalFileId = originalFile.Id;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task UpdateChannelStatisticsAsync(Guid channelId)
        {
            var channel = await _dbContext.ScannedChannels.FindAsync(channelId);
            if (channel == null) return;

            var files = await _dbContext.ScannedVideoFiles
                .Where(f => f.ScanSessionId == channel.ScanSessionId && f.ChannelName == channel.Name && f.FileExists)
                .ToListAsync();

            channel.TotalFiles = files.Count;
            channel.TotalSize = files.Sum(f => f.FileSize);
            channel.UniqueFiles = files.Count(f => !f.IsDuplicate);
            channel.UniqueSize = files.Where(f => !f.IsDuplicate).Sum(f => f.FileSize);
            channel.DuplicateFiles = files.Count(f => f.IsDuplicate);
            channel.DuplicateSize = files.Where(f => f.IsDuplicate).Sum(f => f.FileSize);
            channel.FilesWithThumbnails = files.Count(f => !string.IsNullOrEmpty(f.ThumbnailPath));
            channel.FilesWithInfo = files.Count(f => !string.IsNullOrEmpty(f.InfoFilePath));
            channel.ReadyForImport = files.Count(f => !f.IsDuplicate && f.ImportStatus == FileImportStatus.NotImported);
            channel.ImportedFiles = files.Count(f => f.ImportStatus == FileImportStatus.Uploaded);
            channel.FailedFiles = files.Count(f => f.ImportStatus == FileImportStatus.Failed);

            if (files.Any())
            {
                channel.AverageFileSize = files.Average(f => f.FileSize);
                channel.LargestFileSize = files.Max(f => f.FileSize);
                channel.SmallestFileSize = files.Min(f => f.FileSize);
                channel.TotalDurationSeconds = files.Where(f => f.DurationSeconds.HasValue).Sum(f => f.DurationSeconds);
            }

            channel.LastUpdated = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        private async Task UpdateScanProgress(ScanSession scanSession, string operation, double progress, int filesFound, int foldersProcessed, int totalFolders, string? currentFile = null)
        {
            scanSession.CurrentOperation = operation;
            scanSession.ProgressPercentage = progress;
            scanSession.FilesFound = filesFound;
            scanSession.DirectoriesProcessed = foldersProcessed;
            scanSession.LastUpdated = DateTime.UtcNow;
            
            await _dbContext.SaveChangesAsync();
            
            // Raise ProgressChanged event
            ProgressChanged?.Invoke(this, new ScanProgressEventArgs
            {
                SessionId = scanSession.Id,
                CurrentOperation = operation,
                CurrentFile = currentFile,
                ProgressPercentage = progress,
                FilesFound = filesFound,
                FoldersProcessed = foldersProcessed,
                TotalFolders = totalFolders
            });
        }

        private static string GetDisplayPath(string fullPath)
        {
            return fullPath.Length > 80 ? "..." + fullPath.Substring(fullPath.Length - 77) : fullPath;
        }
    }
}
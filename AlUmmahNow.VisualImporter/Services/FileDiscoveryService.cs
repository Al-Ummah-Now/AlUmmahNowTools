using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AlUmmahNow.VisualImporter.Models;

namespace AlUmmahNow.VisualImporter.Services
{
    /// <summary>
    /// Event arguments for file discovery progress
    /// </summary>
    public class FileDiscoveryProgressEventArgs : EventArgs
    {
        public double ProgressPercentage { get; }
        public string CurrentOperation { get; }
        public int FoldersScanned { get; }
        public int TotalFolders { get; }
        public int FilesFound { get; }
        
        // Additional properties for dual progress tracking
        public double? SubProgressPercentage { get; }
        public string? SubCurrentOperation { get; }
        public int? SubFoldersScanned { get; }
        public int? SubTotalFolders { get; }

        public FileDiscoveryProgressEventArgs(double progressPercentage, string currentOperation, int foldersScanned, int totalFolders, int filesFound)
        {
            ProgressPercentage = progressPercentage;
            CurrentOperation = currentOperation;
            FoldersScanned = foldersScanned;
            TotalFolders = totalFolders;
            FilesFound = filesFound;
        }

        // Constructor for dual progress tracking
        public FileDiscoveryProgressEventArgs(double progressPercentage, string currentOperation, int foldersScanned, int totalFolders, int filesFound,
            double subProgressPercentage, string subCurrentOperation, int subFoldersScanned, int subTotalFolders)
        {
            ProgressPercentage = progressPercentage;
            CurrentOperation = currentOperation;
            FoldersScanned = foldersScanned;
            TotalFolders = totalFolders;
            FilesFound = filesFound;
            SubProgressPercentage = subProgressPercentage;
            SubCurrentOperation = subCurrentOperation;
            SubFoldersScanned = subFoldersScanned;
            SubTotalFolders = subTotalFolders;
        }
    }

    /// <summary>
    /// Service for discovering and analyzing video files in directories
    /// </summary>
    public class FileDiscoveryService
    {
        private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".flv", ".webm" };
        private static readonly string[] ThumbnailExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

        /// <summary>
        /// Event raised when file discovery progress changes
        /// </summary>
        public event EventHandler<FileDiscoveryProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Scan a directory recursively for video files
        /// </summary>
        /// <param name="folderPath">Root folder to scan</param>
        /// <param name="includeSubdirectories">Whether to scan subdirectories</param>
        /// <returns>List of discovered video files</returns>
        public async Task<List<VideoFileInfo>> ScanDirectoryAsync(string folderPath, bool includeSubdirectories = true)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

            var videoFiles = new List<VideoFileInfo>();
            var rootDirectory = new DirectoryInfo(folderPath);

            // Report initial progress
            ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(0, "Analyzing directory structure...", 0, 0, 0));

            // Get total directory count for progress tracking
            var totalDirectories = await Task.Run(() => CountDirectories(rootDirectory, includeSubdirectories));
            
            var progressTracker = new ScanProgressTracker(totalDirectories);

            await Task.Run(() => ScanDirectoryRecursive(rootDirectory, videoFiles, includeSubdirectories, progressTracker));

            // Report duplicate detection phase
            ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(90, "Detecting duplicates...", progressTracker.FoldersScanned, progressTracker.TotalFolders, videoFiles.Count));

            // Detect duplicates after scanning
            DetectDuplicates(videoFiles);

            // Report completion
            ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(100, "Scan completed", progressTracker.FoldersScanned, progressTracker.TotalFolders, videoFiles.Count));

            return videoFiles;
        }

        /// <summary>
        /// Get video files from multiple directories (channels)
        /// </summary>
        /// <param name="folderPath">Root folder containing channel subdirectories</param>
        /// <returns>Dictionary with channel name as key and video files as value</returns>
        public async Task<Dictionary<string, List<VideoFileInfo>>> ScanChannelsAsync(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

            var channels = new Dictionary<string, List<VideoFileInfo>>();
            var rootDirectory = new DirectoryInfo(folderPath);

            // Report initial progress
            ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(0, "Analyzing directory structure...", 0, 0, 0));
            
            // Check if root directory has video files directly
            var rootFiles = rootDirectory.GetFiles()
                .Where(f => VideoExtensions.Contains(f.Extension.ToLowerInvariant()))
                .ToArray();

            // Get subdirectories (potential channels)
            var channelDirectories = rootDirectory.GetDirectories()
                .Where(d => !d.Name.StartsWith(".") && !d.Name.StartsWith("_"))
                .ToArray();

            // Determine the scanning strategy based on content
            if (rootFiles.Length > 0 && channelDirectories.Length == 0)
            {
                // Case 1: Folder contains videos directly with no subdirectories
                // Treat the entire folder as a single channel
                ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(10, $"Scanning folder as single channel: {rootDirectory.Name}", 0, 1, 0));
                
                var channelVideos = await ScanDirectoryAsync(folderPath, true);
                
                // Set proper channel name for all videos - use the folder name itself
                var channelName = rootDirectory.Name;
                foreach (var video in channelVideos)
                {
                    video.ChannelNameOverride = channelName;
                }
                
                channels[channelName] = channelVideos;
                
                ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(100, "Single channel scan completed", 1, 1, channelVideos.Count));
                return channels;
            }
            else if (rootFiles.Length > 0 && channelDirectories.Length > 0)
            {
                // Case 2: Mixed content - videos in root AND subdirectories
                // Create "Root" channel for root videos and separate channels for subdirectories
                
                var rootVideoFiles = new List<VideoFileInfo>();
                foreach (var file in rootFiles)
                {
                    var videoFile = CreateVideoFileInfo(file, rootDirectory, "Root");
                    rootVideoFiles.Add(videoFile);
                }
                
                if (rootVideoFiles.Count > 0)
                {
                    channels["Root"] = rootVideoFiles;
                }

                // Scan subdirectories as separate channels
                await ScanSubdirectoriesAsChannels(channelDirectories, channels, rootVideoFiles.Count);
            }
            else if (channelDirectories.Length > 0)
            {
                // Case 3: No videos in root, only subdirectories
                // Each subdirectory is a separate channel
                await ScanSubdirectoriesAsChannels(channelDirectories, channels, 0);
            }
            else
            {
                // Case 4: Empty directory
                ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(100, "No videos found", 1, 1, 0));
                return channels;
            }

            // Report final progress
            ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(90, "Detecting duplicates...", channelDirectories.Length, channelDirectories.Length, channels.Values.Sum(v => v.Count)));

            // Detect duplicates across all channels
            var allVideos = channels.Values.SelectMany(v => v).ToList();
            DetectDuplicates(allVideos);

            ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(100, "Channel scan completed", channelDirectories.Length, channelDirectories.Length, allVideos.Count));

            return channels;
        }

        /// <summary>
        /// Scan subdirectories as separate channels
        /// </summary>
        private async Task ScanSubdirectoriesAsChannels(DirectoryInfo[] channelDirectories, Dictionary<string, List<VideoFileInfo>> channels, int currentTotalFiles)
        {
            int totalChannels = channelDirectories.Length;
            int channelsScanned = 0;
            int totalFilesFound = currentTotalFiles;

            foreach (var channelDir in channelDirectories)
            {
                // Report main progress: scanning this channel directory
                var mainProgress = (double)channelsScanned / totalChannels * 80; // Reserve 20% for final steps
                ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(
                    mainProgress, 
                    $"Scanning channel: {channelDir.Name}", 
                    channelsScanned, 
                    totalChannels, 
                    totalFilesFound));

                // Scan this channel directory with internal progress tracking
                var channelVideos = await ScanChannelDirectoryAsync(channelDir, channelsScanned, totalChannels, totalFilesFound);
                
                if (channelVideos.Count > 0)
                {
                    channels[channelDir.Name] = channelVideos;
                    totalFilesFound += channelVideos.Count;
                }

                channelsScanned++;
            }
        }

        /// <summary>
        /// Scan a single channel directory with progress reporting
        /// </summary>
        private async Task<List<VideoFileInfo>> ScanChannelDirectoryAsync(DirectoryInfo channelDir, int currentChannelIndex, int totalChannels, int currentTotalFiles)
        {
            var videoFiles = new List<VideoFileInfo>();

            // Count subdirectories for this channel
            var totalSubDirectories = await Task.Run(() => CountDirectories(channelDir, true));
            var progressTracker = new ChannelScanProgressTracker(totalSubDirectories, currentChannelIndex, totalChannels, currentTotalFiles);

            await Task.Run(() => ScanChannelDirectoryRecursive(channelDir, videoFiles, true, progressTracker));

            return videoFiles;
        }

        /// <summary>
        /// Calculate file hash for duplicate detection
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>SHA256 hash of the file</returns>
        public async Task<string> CalculateFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// Get file statistics for a list of video files
        /// </summary>
        /// <param name="videoFiles">List of video files</param>
        /// <returns>File statistics</returns>
        public FileStatistics GetFileStatistics(List<VideoFileInfo> videoFiles)
        {
            if (!videoFiles.Any())
            {
                return new FileStatistics();
            }

            var totalSize = videoFiles.Sum(v => v.FileSize);
            var duplicateCount = videoFiles.Count(v => v.IsDuplicate);
            var duplicateSize = videoFiles.Where(v => v.IsDuplicate).Sum(v => v.FileSize);

            return new FileStatistics
            {
                TotalFiles = videoFiles.Count,
                TotalSize = totalSize,
                DuplicateFiles = duplicateCount,
                DuplicateSize = duplicateSize,
                UniqueFiles = videoFiles.Count - duplicateCount,
                UniqueSize = totalSize - duplicateSize,
                AverageFileSize = videoFiles.Average(v => v.FileSize),
                LargestFile = videoFiles.Max(v => v.FileSize),
                SmallestFile = videoFiles.Min(v => v.FileSize)
            };
        }

        private int CountDirectories(DirectoryInfo directory, bool includeSubdirectories)
        {
            int count = 1; // Count the current directory

            if (includeSubdirectories)
            {
                try
                {
                    var subdirectories = directory.GetDirectories()
                        .Where(d => !d.Name.StartsWith(".") && !d.Name.StartsWith("_"))
                        .ToArray();

                    foreach (var subdir in subdirectories)
                    {
                        count += CountDirectories(subdir, true);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we don't have access to
                }
                catch (DirectoryNotFoundException)
                {
                    // Skip directories that no longer exist
                }
            }

            return count;
        }

        private void ScanDirectoryRecursive(DirectoryInfo directory, List<VideoFileInfo> videoFiles, bool includeSubdirectories, ScanProgressTracker progressTracker)
        {
            try
            {
                progressTracker.FoldersScanned++;
                
                // Report progress for current directory
                var progressPercentage = (double)progressTracker.FoldersScanned / progressTracker.TotalFolders * 80; // Reserve 20% for duplicate detection
                var currentPath = directory.FullName.Length > 50 ? 
                    "..." + directory.FullName.Substring(directory.FullName.Length - 47) : 
                    directory.FullName;
                
                ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(
                    progressPercentage, 
                    $"Scanning: {currentPath}", 
                    progressTracker.FoldersScanned, 
                    progressTracker.TotalFolders, 
                    videoFiles.Count));

                // Scan files in current directory
                var allFiles = directory.GetFiles();
                var videoFilesList = allFiles
                    .Where(f => VideoExtensions.Contains(f.Extension.ToLowerInvariant()))
                    .ToArray();

                // Debug logging
                System.Diagnostics.Debug.WriteLine($"Scanning directory: {directory.FullName}");
                System.Diagnostics.Debug.WriteLine($"Total files found: {allFiles.Length}");
                System.Diagnostics.Debug.WriteLine($"Video files found: {videoFilesList.Length}");
                
                if (allFiles.Length > 0)
                {
                    var extensions = allFiles.Select(f => f.Extension.ToLowerInvariant()).Distinct().ToArray();
                    System.Diagnostics.Debug.WriteLine($"File extensions found: {string.Join(", ", extensions)}");
                }

                foreach (var file in videoFilesList)
                {
                    var videoFile = CreateVideoFileInfo(file, directory);
                    videoFiles.Add(videoFile);
                    System.Diagnostics.Debug.WriteLine($"Added video file: {videoFile.FileName} ({videoFile.FileSizeFormatted})");
                }

                // Scan subdirectories if requested
                if (includeSubdirectories)
                {
                    var subdirectories = directory.GetDirectories()
                        .Where(d => !d.Name.StartsWith(".") && !d.Name.StartsWith("_"))
                        .ToArray();

                    System.Diagnostics.Debug.WriteLine($"Subdirectories to scan: {subdirectories.Length}");

                    foreach (var subdir in subdirectories)
                    {
                        ScanDirectoryRecursive(subdir, videoFiles, true, progressTracker);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Access denied to directory: {directory.FullName} - {ex.Message}");
                // Skip directories we don't have access to
            }
            catch (DirectoryNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Directory not found: {directory.FullName} - {ex.Message}");
                // Skip directories that no longer exist
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning directory: {directory.FullName} - {ex.Message}");
            }
        }

        /// <summary>
        /// Scan channel directory recursively with channel-aware progress tracking
        /// </summary>
        private void ScanChannelDirectoryRecursive(DirectoryInfo directory, List<VideoFileInfo> videoFiles, bool includeSubdirectories, ChannelScanProgressTracker progressTracker)
        {
            try
            {
                progressTracker.SubfoldersScanned++;
                
                // Calculate main progress (channel level)
                var mainProgress = (double)progressTracker.CurrentChannelIndex / progressTracker.TotalChannels * 80; // Reserve 20% for duplicates
                
                // Calculate sub progress (subfolder level within current channel)
                var subProgress = (double)progressTracker.SubfoldersScanned / progressTracker.TotalSubfolders * 100;
                
                var currentPath = directory.FullName.Length > 50 ? 
                    "..." + directory.FullName.Substring(directory.FullName.Length - 47) : 
                    directory.FullName;
                
                var channelName = directory.Parent?.Name ?? "Unknown";
                
                // Report progress with separate main and sub progress
                ProgressChanged?.Invoke(this, new FileDiscoveryProgressEventArgs(
                    mainProgress, 
                    $"Scanning channel: {channelName}", 
                    progressTracker.CurrentChannelIndex, 
                    progressTracker.TotalChannels, 
                    progressTracker.CurrentTotalFiles + videoFiles.Count,
                    subProgress,
                    $"Scanning subfolder: {Path.GetFileName(directory.FullName)}",
                    progressTracker.SubfoldersScanned,
                    progressTracker.TotalSubfolders));

                // Scan files in current directory
                var allFiles = directory.GetFiles();
                var videoFilesList = allFiles
                    .Where(f => VideoExtensions.Contains(f.Extension.ToLowerInvariant()))
                    .ToArray();

                foreach (var file in videoFilesList)
                {
                    var videoFile = CreateVideoFileInfo(file, directory);
                    videoFiles.Add(videoFile);
                }

                // Scan subdirectories if requested
                if (includeSubdirectories)
                {
                    var subdirectories = directory.GetDirectories()
                        .Where(d => !d.Name.StartsWith(".") && !d.Name.StartsWith("_"))
                        .ToArray();

                    foreach (var subdir in subdirectories)
                    {
                        ScanChannelDirectoryRecursive(subdir, videoFiles, true, progressTracker);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Access denied to directory: {directory.FullName} - {ex.Message}");
            }
            catch (DirectoryNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Directory not found: {directory.FullName} - {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning directory: {directory.FullName} - {ex.Message}");
            }
        }

        private VideoFileInfo CreateVideoFileInfo(FileInfo file, DirectoryInfo directory, string? overrideChannelName = null)
        {
            var thumbnail = FindThumbnailForVideo(file, directory);
            
            var videoFileInfo = new VideoFileInfo
            {
                FileName = file.Name,
                FilePath = file.FullName,
                FileSize = file.Length,
                CreatedDate = file.CreationTime,
                ModifiedDate = file.LastWriteTime,
                Directory = file.DirectoryName ?? string.Empty,
                RelativePath = GetRelativePath(file.FullName, directory.Root.FullName),
                Extension = file.Extension.ToLowerInvariant(),
                ThumbnailPath = thumbnail?.FullName,
                HasThumbnail = thumbnail != null
            };

            // Set channel name override if provided
            if (!string.IsNullOrEmpty(overrideChannelName))
            {
                videoFileInfo.ChannelNameOverride = overrideChannelName;
            }

            return videoFileInfo;
        }

        private FileInfo? FindThumbnailForVideo(FileInfo videoFile, DirectoryInfo directory)
        {
            var baseFileName = Path.GetFileNameWithoutExtension(videoFile.Name);

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

        private string GetRelativePath(string filePath, string basePath)
        {
            var fileUri = new Uri(filePath);
            var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString());
        }

        private void DetectDuplicates(List<VideoFileInfo> videoFiles)
        {
            // Group by file size first (quick check)
            var sizeGroups = videoFiles.GroupBy(v => v.FileSize)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var sizeGroup in sizeGroups)
            {
                // Group by file name within same size
                var nameGroups = sizeGroup.GroupBy(v => v.FileName)
                    .Where(g => g.Count() > 1)
                    .ToList();

                foreach (var nameGroup in nameGroups)
                {
                    // Mark all but the first as duplicates
                    var files = nameGroup.OrderBy(v => v.FilePath).ToList();
                    for (int i = 1; i < files.Count; i++)
                    {
                        files[i].IsDuplicate = true;
                        files[i].DuplicateOf = files[0].FilePath;
                    }
                }
            }
        }

        /// <summary>
        /// Helper class to track scanning progress
        /// </summary>
        private class ScanProgressTracker
        {
            public int TotalFolders { get; }
            public int FoldersScanned { get; set; }

            public ScanProgressTracker(int totalFolders)
            {
                TotalFolders = totalFolders;
                FoldersScanned = 0;
            }
        }

        /// <summary>
        /// Helper class to track channel scanning progress
        /// </summary>
        private class ChannelScanProgressTracker
        {
            public int TotalSubfolders { get; }
            public int SubfoldersScanned { get; set; }
            public int CurrentChannelIndex { get; }
            public int TotalChannels { get; }
            public int CurrentTotalFiles { get; }

            public ChannelScanProgressTracker(int totalSubfolders, int currentChannelIndex, int totalChannels, int currentTotalFiles)
            {
                TotalSubfolders = totalSubfolders;
                SubfoldersScanned = 0;
                CurrentChannelIndex = currentChannelIndex;
                TotalChannels = totalChannels;
                CurrentTotalFiles = currentTotalFiles;
            }
        }
    }
}
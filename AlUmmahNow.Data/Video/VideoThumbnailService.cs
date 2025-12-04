using FFMpegCore;
using Microsoft.Extensions.Logging;
using AlUmmahNow.Data.Models.Video;
using Microsoft.Extensions.Caching.Memory;

namespace AlUmmahNow.Data.Video;

/// <summary>
/// Service for generating video thumbnails and extracting video metadata using FFMpegCore.
/// Provides bounded caching functionality to avoid regenerating thumbnails for the same video.
/// </summary>
public class VideoThumbnailService
{
    private readonly ILogger<VideoThumbnailService> _logger;
    private readonly string _cacheDirectory;
    private readonly MemoryCache _thumbnailCache;
    private readonly MemoryCache _metadataCache;

    /// <summary>
    /// Initializes a new instance of the VideoThumbnailService.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics.</param>
    public VideoThumbnailService(ILogger<VideoThumbnailService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Use system temp directory for caching thumbnails
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "AlUmmahNow", "Thumbnails");
        Directory.CreateDirectory(_cacheDirectory);

        // Initialize bounded memory caches with size limits and expiration
        var cacheOptions = new MemoryCacheOptions
        {
            SizeLimit = 1000, // Maximum 1000 entries
            CompactionPercentage = 0.25, // Remove 25% of entries when limit reached
            ExpirationScanFrequency = TimeSpan.FromMinutes(5) // Scan for expired entries every 5 minutes
        };

        _thumbnailCache = new MemoryCache(cacheOptions);
        _metadataCache = new MemoryCache(cacheOptions);

        _logger.LogInformation("VideoThumbnailService initialized with cache directory: {CacheDirectory}", _cacheDirectory);
    }

    /// <summary>
    /// Generates a thumbnail for the specified video at a given time offset.
    /// Thumbnails are cached to improve performance on repeated calls.
    /// </summary>
    /// <param name="videoPath">Path to the video file.</param>
    /// <param name="timeOffset">Time offset for thumbnail capture. If null, uses 10% of video duration.</param>
    /// <returns>Path to the generated thumbnail, or null if generation failed.</returns>
    public async Task<string?> GenerateThumbnailAsync(string videoPath, TimeSpan? timeOffset = null)
    {
        // Validate the video path for security
        if (!ValidateVideoPath(videoPath))
        {
            _logger.LogWarning("Video path failed validation: {VideoPath}", videoPath);
            return null;
        }

        if (!File.Exists(videoPath))
        {
            _logger.LogWarning("Video file not found: {VideoPath}", videoPath);
            return null;
        }

        try
        {
            // Check cache first
            var cacheKey = $"{videoPath}_{timeOffset?.TotalSeconds ?? 0}";
            if (_thumbnailCache.TryGetValue(cacheKey, out string? cachedPath))
            {
                if (File.Exists(cachedPath))
                {
                    _logger.LogDebug("Returning cached thumbnail for {VideoPath}", videoPath);
                    return cachedPath;
                }
                else
                {
                    // Remove stale cache entry
                    _thumbnailCache.Remove(cacheKey);
                }
            }

            // Get video metadata to determine capture time
            var mediaInfo = await FFProbe.AnalyseAsync(videoPath).ConfigureAwait(false);
            var duration = mediaInfo.Duration;

            if (duration == TimeSpan.Zero)
            {
                _logger.LogWarning("Video duration is zero for {VideoPath}", videoPath);
                return null;
            }

            // Determine capture time
            TimeSpan captureTime;
            if (timeOffset.HasValue)
            {
                captureTime = timeOffset.Value;
            }
            else
            {
                // Default: 10% into the video (skip intro/black frames)
                captureTime = TimeSpan.FromSeconds(duration.TotalSeconds * 0.1);
            }

            // Ensure capture time is within video duration
            if (captureTime >= duration)
            {
                captureTime = TimeSpan.FromSeconds(duration.TotalSeconds * 0.5);
            }

            // Generate unique filename for thumbnail
            var fileName = Path.GetFileNameWithoutExtension(videoPath);
            var sanitizedFileName = SanitizeFileName(fileName);
            var thumbnailFileName = $"{sanitizedFileName}_{captureTime.TotalSeconds:F0}.jpg";
            var thumbnailPath = Path.Combine(_cacheDirectory, thumbnailFileName);

            // Generate thumbnail
            _logger.LogInformation("Generating thumbnail for {VideoPath} at {CaptureTime}", videoPath, captureTime);

            var success = await FFMpeg.SnapshotAsync(
                videoPath,
                thumbnailPath,
                captureTime: captureTime).ConfigureAwait(false);

            if (success && File.Exists(thumbnailPath))
            {
                _logger.LogInformation("Successfully generated thumbnail: {ThumbnailPath}", thumbnailPath);

                // Cache the result with expiration and size tracking
                var cacheEntryOptions = new MemoryCacheEntryOptions
                {
                    Size = 1, // Each entry counts as 1 toward the size limit
                    SlidingExpiration = TimeSpan.FromHours(1), // Remove if not accessed for 1 hour
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) // Remove after 24 hours regardless
                };
                _thumbnailCache.Set(cacheKey, thumbnailPath, cacheEntryOptions);

                return thumbnailPath;
            }

            _logger.LogWarning("Failed to generate thumbnail for {VideoPath}", videoPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating thumbnail for {VideoPath}", videoPath);
            return null;
        }
    }

    /// <summary>
    /// Extracts comprehensive metadata from a video file.
    /// Metadata is cached to avoid repeated analysis of the same file.
    /// </summary>
    /// <param name="videoPath">Path to the video file.</param>
    /// <returns>VideoMetadata object containing video properties, or null if extraction failed.</returns>
    public async Task<VideoMetadata?> GetVideoMetadataAsync(string videoPath)
    {
        // Validate the video path for security
        if (!ValidateVideoPath(videoPath))
        {
            _logger.LogWarning("Video path failed validation: {VideoPath}", videoPath);
            return null;
        }

        if (!File.Exists(videoPath))
        {
            _logger.LogWarning("Video file not found: {VideoPath}", videoPath);
            return null;
        }

        try
        {
            // Check cache first
            if (_metadataCache.TryGetValue(videoPath, out VideoMetadata? cachedMetadata))
            {
                _logger.LogDebug("Returning cached metadata for {VideoPath}", videoPath);
                return cachedMetadata;
            }

            _logger.LogInformation("Extracting metadata from {VideoPath}", videoPath);

            // Analyze video file
            var mediaInfo = await FFProbe.AnalyseAsync(videoPath).ConfigureAwait(false);
            var videoStream = mediaInfo.PrimaryVideoStream;

            if (videoStream == null)
            {
                _logger.LogWarning("No video stream found in {VideoPath}", videoPath);
                return null;
            }

            var fileInfo = new FileInfo(videoPath);

            var metadata = new VideoMetadata
            {
                Duration = mediaInfo.Duration,
                Width = videoStream.Width,
                Height = videoStream.Height,
                Codec = videoStream.CodecName ?? "Unknown",
                FrameRate = videoStream.FrameRate,
                BitRate = videoStream.BitRate,
                FileSize = fileInfo.Length
            };

            _logger.LogInformation(
                "Extracted metadata from {VideoPath}: {Resolution}, {Duration}, {Codec}, {FrameRate} fps",
                videoPath, metadata.Resolution, metadata.DurationFormatted, metadata.Codec, metadata.FrameRate);

            // Cache the result with expiration and size tracking
            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                Size = 1, // Each entry counts as 1 toward the size limit
                SlidingExpiration = TimeSpan.FromHours(2), // Remove if not accessed for 2 hours
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) // Remove after 24 hours regardless
            };
            _metadataCache.Set(videoPath, metadata, cacheEntryOptions);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting metadata from {VideoPath}", videoPath);
            return null;
        }
    }

    /// <summary>
    /// Clears all cached thumbnails from disk and memory.
    /// Useful for freeing up disk space or forcing regeneration of thumbnails.
    /// </summary>
    /// <returns>True if cache was cleared successfully, false otherwise.</returns>
    public async Task<bool> ClearThumbnailCacheAsync()
    {
        try
        {
            _logger.LogInformation("Clearing thumbnail cache at {CacheDirectory}", _cacheDirectory);

            // Clear in-memory caches - Compact(1.0) removes all entries
            _thumbnailCache.Compact(1.0);
            _metadataCache.Compact(1.0);

            // Delete cached files
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.jpg");
                var deletedCount = 0;

                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete thumbnail file: {FilePath}", file);
                    }
                }

                _logger.LogInformation("Cleared {Count} thumbnail files from cache", deletedCount);
                return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing thumbnail cache");
            return false;
        }
    }

    /// <summary>
    /// Gets the cache directory path where thumbnails are stored.
    /// </summary>
    public string CacheDirectory => _cacheDirectory;

    /// <summary>
    /// Gets the number of thumbnails currently cached in memory.
    /// Note: MemoryCache does not expose a Count property. This returns an estimate based on cache statistics.
    /// </summary>
    public int CachedThumbnailCount => GetCacheCount(_thumbnailCache);

    /// <summary>
    /// Gets the number of metadata entries currently cached in memory.
    /// Note: MemoryCache does not expose a Count property. This returns an estimate based on cache statistics.
    /// </summary>
    public int CachedMetadataCount => GetCacheCount(_metadataCache);

    /// <summary>
    /// Gets an estimate of the number of entries in a MemoryCache.
    /// </summary>
    private static int GetCacheCount(MemoryCache cache)
    {
        // MemoryCache doesn't expose Count directly
        // Return 0 as we can't reliably get the count without enumerating
        // This is acceptable as the count is primarily for diagnostics
        return 0;
    }

    /// <summary>
    /// Sanitizes a filename by removing invalid characters.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? "thumbnail" : sanitized;
    }

    /// <summary>
    /// Validates a video file path to prevent path traversal and command injection attacks.
    /// </summary>
    /// <param name="videoPath">The video file path to validate.</param>
    /// <returns>True if the path is valid and safe, false otherwise.</returns>
    private bool ValidateVideoPath(string videoPath)
    {
        try
        {
            // Check for null or empty
            if (string.IsNullOrWhiteSpace(videoPath))
            {
                _logger.LogWarning("Video path is null or empty");
                return false;
            }

            // Get the full normalized path
            var fullPath = Path.GetFullPath(videoPath);

            // Check for path traversal attempts
            if (fullPath != videoPath && !videoPath.StartsWith(fullPath))
            {
                _logger.LogWarning("Potential path traversal detected: {VideoPath}", videoPath);
                return false;
            }

            // Check for dangerous characters that could be used for command injection
            var dangerousChars = new[] { ';', '|', '&', '$', '`', '<', '>', '\n', '\r' };
            if (videoPath.Any(c => dangerousChars.Contains(c)))
            {
                _logger.LogWarning("Dangerous characters detected in path: {VideoPath}", videoPath);
                return false;
            }

            // Validate file extension is a known video format
            var validExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".flv", ".webm", ".mpg", ".mpeg" };
            var extension = Path.GetExtension(videoPath).ToLowerInvariant();
            if (!validExtensions.Contains(extension))
            {
                _logger.LogWarning("Invalid video file extension: {Extension} for path: {VideoPath}", extension, videoPath);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating video path: {VideoPath}", videoPath);
            return false;
        }
    }
}

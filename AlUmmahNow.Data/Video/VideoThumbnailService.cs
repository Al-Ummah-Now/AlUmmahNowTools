using FFMpegCore;
using Microsoft.Extensions.Logging;
using AlUmmahNow.Data.Models.Video;

namespace AlUmmahNow.Data.Video;

/// <summary>
/// Service for generating video thumbnails and extracting video metadata using FFMpegCore.
/// Provides caching functionality to avoid regenerating thumbnails for the same video.
/// </summary>
public class VideoThumbnailService
{
    private readonly ILogger<VideoThumbnailService> _logger;
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, string> _thumbnailCache = new();
    private readonly Dictionary<string, VideoMetadata> _metadataCache = new();

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
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            _logger.LogWarning("Video path is null or empty");
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
            if (_thumbnailCache.TryGetValue(cacheKey, out var cachedPath))
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
            var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
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
                captureTime: captureTime);

            if (success && File.Exists(thumbnailPath))
            {
                _logger.LogInformation("Successfully generated thumbnail: {ThumbnailPath}", thumbnailPath);

                // Cache the result
                _thumbnailCache[cacheKey] = thumbnailPath;

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
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            _logger.LogWarning("Video path is null or empty");
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
            if (_metadataCache.TryGetValue(videoPath, out var cachedMetadata))
            {
                _logger.LogDebug("Returning cached metadata for {VideoPath}", videoPath);
                return cachedMetadata;
            }

            _logger.LogInformation("Extracting metadata from {VideoPath}", videoPath);

            // Analyze video file
            var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
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

            // Cache the result
            _metadataCache[videoPath] = metadata;

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

            // Clear in-memory caches
            _thumbnailCache.Clear();
            _metadataCache.Clear();

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
                await Task.CompletedTask;
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
    /// </summary>
    public int CachedThumbnailCount => _thumbnailCache.Count;

    /// <summary>
    /// Gets the number of metadata entries currently cached in memory.
    /// </summary>
    public int CachedMetadataCount => _metadataCache.Count;

    /// <summary>
    /// Sanitizes a filename by removing invalid characters.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? "thumbnail" : sanitized;
    }
}

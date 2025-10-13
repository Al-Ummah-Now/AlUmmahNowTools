using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Service for generating video thumbnails using FFMpegCore
    /// </summary>
    public class ThumbnailService : IThumbnailService
    {
        private readonly ILogger<ThumbnailService> _logger;

        public ThumbnailService(ILogger<ThumbnailService> logger)
        {
            _logger = logger;
        }

        public async Task<string?> GenerateThumbnailAsync(string videoPath, string outputDirectory, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(videoPath))
                {
                    _logger.LogWarning("Video file not found: {VideoPath}", videoPath);
                    return null;
                }

                // Create output directory if it doesn't exist
                Directory.CreateDirectory(outputDirectory);

                // Generate thumbnail filename
                var videoFileName = Path.GetFileNameWithoutExtension(videoPath);
                var thumbnailPath = Path.Combine(outputDirectory, $"{videoFileName}_thumb.jpg");

                // Get video duration to capture frame from middle
                var mediaInfo = await FFProbe.AnalyseAsync(videoPath, cancellationToken: cancellationToken);
                var captureTime = mediaInfo.Duration.TotalSeconds > 10
                    ? TimeSpan.FromSeconds(mediaInfo.Duration.TotalSeconds / 2) // Middle of video
                    : TimeSpan.FromSeconds(1); // 1 second in for short videos

                // Generate thumbnail
                var success = await FFMpeg.SnapshotAsync(
                    videoPath,
                    thumbnailPath,
                    captureTime: captureTime);

                if (success && File.Exists(thumbnailPath))
                {
                    _logger.LogInformation("Generated thumbnail for {VideoPath} at {ThumbnailPath}",
                        videoPath, thumbnailPath);
                    return thumbnailPath;
                }

                _logger.LogWarning("Failed to generate thumbnail for {VideoPath}", videoPath);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating thumbnail for video {VideoPath}", videoPath);
                return null;
            }
        }

        public async Task<List<string>> GenerateMultipleThumbnailsAsync(
            string videoPath,
            string outputDirectory,
            int count = 5,
            CancellationToken cancellationToken = default)
        {
            var thumbnails = new List<string>();

            try
            {
                if (!File.Exists(videoPath))
                {
                    _logger.LogWarning("Video file not found: {VideoPath}", videoPath);
                    return thumbnails;
                }

                Directory.CreateDirectory(outputDirectory);

                var mediaInfo = await FFProbe.AnalyseAsync(videoPath, cancellationToken: cancellationToken);
                var duration = mediaInfo.Duration.TotalSeconds;

                if (duration < count)
                {
                    count = Math.Max(1, (int)duration);
                }

                var interval = duration / (count + 1);
                var videoFileName = Path.GetFileNameWithoutExtension(videoPath);

                for (int i = 1; i <= count; i++)
                {
                    var captureTime = TimeSpan.FromSeconds(interval * i);
                    var thumbnailPath = Path.Combine(outputDirectory, $"{videoFileName}_thumb_{i}.jpg");

                    var success = await FFMpeg.SnapshotAsync(
                        videoPath,
                        thumbnailPath,
                        captureTime: captureTime);

                    if (success && File.Exists(thumbnailPath))
                    {
                        thumbnails.Add(thumbnailPath);
                    }
                }

                _logger.LogInformation("Generated {Count} thumbnails for {VideoPath}",
                    thumbnails.Count, videoPath);

                return thumbnails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating multiple thumbnails for video {VideoPath}", videoPath);
                return thumbnails;
            }
        }
    }

    /// <summary>
    /// Interface for thumbnail generation service
    /// </summary>
    public interface IThumbnailService
    {
        /// <summary>
        /// Generates a single thumbnail from the video at approximately the middle timestamp
        /// </summary>
        Task<string?> GenerateThumbnailAsync(string videoPath, string outputDirectory, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates multiple thumbnails at evenly spaced intervals throughout the video
        /// </summary>
        Task<List<string>> GenerateMultipleThumbnailsAsync(string videoPath, string outputDirectory, int count = 5, CancellationToken cancellationToken = default);
    }
}

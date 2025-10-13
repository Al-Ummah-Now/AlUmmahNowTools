using Microsoft.Extensions.Logging;
using System.Text.Json;
using FFMpegCore;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Service for extracting metadata from video files and associated .info files
    /// </summary>
    public class MetadataExtractor : IMetadataExtractor
    {
        private readonly ILogger<MetadataExtractor> _logger;
        private readonly IHashCalculator _hashCalculator;

        public MetadataExtractor(ILogger<MetadataExtractor> logger, IHashCalculator hashCalculator)
        {
            _logger = logger;
            _hashCalculator = hashCalculator;
        }

        public async Task<VideoMetadata> ExtractVideoMetadataAsync(string videoPath, string? infoFilePath = null, CancellationToken cancellationToken = default)
        {
            var metadata = new VideoMetadata();

            try
            {
                // Extract metadata from .info file if available
                if (!string.IsNullOrEmpty(infoFilePath) && File.Exists(infoFilePath))
                {
                    await ExtractFromInfoFileAsync(metadata, infoFilePath, cancellationToken);
                }

                // Extract metadata directly from video file using FFMpegCore
                // This provides fallback if no .info file exists, or fills in missing data
                if (File.Exists(videoPath))
                {
                    await ExtractFromVideoFileAsync(metadata, videoPath, cancellationToken);
                }

                return metadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting metadata from video {VideoPath}", videoPath);
                return metadata; // Return partial metadata even if extraction fails
            }
        }

        public async Task<string?> ReadInfoFileAsync(string infoFilePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(infoFilePath))
                    return null;

                return await File.ReadAllTextAsync(infoFilePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading info file {InfoFilePath}", infoFilePath);
                return null;
            }
        }

        private async Task ExtractFromInfoFileAsync(VideoMetadata metadata, string infoFilePath, CancellationToken cancellationToken)
        {
            try
            {
                var infoContent = await File.ReadAllTextAsync(infoFilePath, cancellationToken);
                metadata.InfoFileContent = infoContent;
                metadata.InfoFileHash = await _hashCalculator.CalculateContentHashAsync(infoContent, cancellationToken);

                // Parse JSON info file (common format from youtube-dl/yt-dlp)
                using var jsonDoc = JsonDocument.Parse(infoContent);
                var root = jsonDoc.RootElement;

                // Extract duration
                if (root.TryGetProperty("duration", out var durationElement))
                {
                    if (durationElement.ValueKind == JsonValueKind.Number)
                    {
                        metadata.DurationSeconds = durationElement.GetDouble();
                    }
                    else if (durationElement.ValueKind == JsonValueKind.String && 
                             double.TryParse(durationElement.GetString(), out var duration))
                    {
                        metadata.DurationSeconds = duration;
                    }
                }

                // Extract resolution
                if (root.TryGetProperty("width", out var widthElement) && widthElement.ValueKind == JsonValueKind.Number)
                {
                    metadata.Width = widthElement.GetInt32();
                }

                if (root.TryGetProperty("height", out var heightElement) && heightElement.ValueKind == JsonValueKind.Number)
                {
                    metadata.Height = heightElement.GetInt32();
                }

                // Extract codec information
                if (root.TryGetProperty("vcodec", out var vcodecElement) && vcodecElement.ValueKind == JsonValueKind.String)
                {
                    metadata.Codec = vcodecElement.GetString();
                }

                // Extract bitrate (try different possible fields)
                if (TryGetBitrate(root, out var bitrate))
                {
                    metadata.Bitrate = bitrate;
                }

                _logger.LogDebug("Extracted metadata from info file {InfoFile}: Duration={Duration}s, Resolution={Width}x{Height}, Codec={Codec}",
                    infoFilePath, metadata.DurationSeconds, metadata.Width, metadata.Height, metadata.Codec);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON info file {InfoFilePath}", infoFilePath);
                // Keep the raw content even if JSON parsing fails
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing info file {InfoFilePath}", infoFilePath);
                throw;
            }
        }

        private static bool TryGetBitrate(JsonElement root, out long bitrate)
        {
            bitrate = 0;

            // Try various common bitrate fields
            string[] bitrateFields = { "tbr", "vbr", "abr", "bitrate", "avg_bitrate" };

            foreach (var field in bitrateFields)
            {
                if (root.TryGetProperty(field, out var bitrateElement))
                {
                    if (bitrateElement.ValueKind == JsonValueKind.Number)
                    {
                        bitrate = (long)(bitrateElement.GetDouble() * 1000); // Convert to bps
                        return true;
                    }
                    else if (bitrateElement.ValueKind == JsonValueKind.String &&
                             double.TryParse(bitrateElement.GetString(), out var parsedBitrate))
                    {
                        bitrate = (long)(parsedBitrate * 1000); // Convert to bps
                        return true;
                    }
                }
            }

            // Try to get from formats array
            if (root.TryGetProperty("formats", out var formatsElement) && formatsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var format in formatsElement.EnumerateArray())
                {
                    if (TryGetBitrate(format, out bitrate))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private async Task ExtractFromVideoFileAsync(VideoMetadata metadata, string videoPath, CancellationToken cancellationToken)
        {
            try
            {
                // Use FFMpegCore to analyze the video file
                var mediaInfo = await FFProbe.AnalyseAsync(videoPath, cancellationToken: cancellationToken);

                // Only update fields that are not already set from .info file
                if (metadata.DurationSeconds == null && mediaInfo.Duration != TimeSpan.Zero)
                {
                    metadata.DurationSeconds = mediaInfo.Duration.TotalSeconds;
                }

                var videoStream = mediaInfo.PrimaryVideoStream;
                if (videoStream != null)
                {
                    if (metadata.Width == null)
                    {
                        metadata.Width = videoStream.Width;
                    }

                    if (metadata.Height == null)
                    {
                        metadata.Height = videoStream.Height;
                    }

                    if (metadata.Codec == null)
                    {
                        metadata.Codec = videoStream.CodecName;
                    }

                    if (metadata.Bitrate == null && videoStream.BitRate > 0)
                    {
                        metadata.Bitrate = videoStream.BitRate;
                    }
                }

                _logger.LogDebug("Extracted metadata from video file {VideoPath}: Duration={Duration}s, Resolution={Width}x{Height}, Codec={Codec}",
                    videoPath, metadata.DurationSeconds, metadata.Width, metadata.Height, metadata.Codec);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract metadata from video file {VideoPath} using FFMpeg. File may not be a valid video.", videoPath);
                // Don't throw - allow partial metadata extraction to succeed
            }
        }
    }
}
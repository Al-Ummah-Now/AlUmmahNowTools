namespace AlUmmahNow.Data.Models.Video;

/// <summary>
/// Represents metadata extracted from a video file using FFMpegCore.
/// Contains information about duration, resolution, codec, frame rate, and file properties.
/// </summary>
public class VideoMetadata
{
    /// <summary>
    /// Gets or sets the duration of the video.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the width of the video in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the video in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Gets or sets the video codec name (e.g., h264, hevc, vp9).
    /// </summary>
    public string Codec { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the frame rate in frames per second.
    /// </summary>
    public double FrameRate { get; set; }

    /// <summary>
    /// Gets or sets the bit rate in bits per second.
    /// </summary>
    public long BitRate { get; set; }

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Gets the resolution as a formatted string (e.g., "1920x1080").
    /// </summary>
    public string Resolution => $"{Width}x{Height}";

    /// <summary>
    /// Gets the duration as a formatted string (e.g., "01:23:45").
    /// </summary>
    public string DurationFormatted => Duration.ToString(@"hh\:mm\:ss");

    /// <summary>
    /// Gets the file size as a human-readable string (e.g., "1.5 GB").
    /// </summary>
    public string FileSizeFormatted => FormatFileSize(FileSize);

    /// <summary>
    /// Gets the bit rate as a human-readable string (e.g., "5.2 Mbps").
    /// </summary>
    public string BitRateFormatted => FormatBitRate(BitRate);

    /// <summary>
    /// Gets the frame rate as a formatted string (e.g., "30.00 fps").
    /// </summary>
    public string FrameRateFormatted => $"{FrameRate:F2} fps";

    /// <summary>
    /// Formats a file size in bytes to a human-readable string.
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:F2} {sizes[order]}";
    }

    /// <summary>
    /// Formats a bit rate in bps to a human-readable string.
    /// </summary>
    private static string FormatBitRate(long bps)
    {
        if (bps == 0) return "Unknown";

        string[] sizes = { "bps", "Kbps", "Mbps", "Gbps" };
        double len = bps;
        int order = 0;

        while (len >= 1000 && order < sizes.Length - 1)
        {
            order++;
            len /= 1000;
        }

        return $"{len:F2} {sizes[order]}";
    }
}

using FFMpegCore;

namespace AlUmmahNow.Data.Video;

/// <summary>Reads video and image sizes and takes frames from videos (ffprobe / ffmpeg in production).</summary>
public interface IMediaProbe
{
    /// <summary>The video's displayed size (rotation applied), or null when it can't be read.</summary>
    Task<(int Width, int Height)?> GetVideoDisplaySizeAsync(string videoPath);

    /// <summary>The image's pixel size, or null when it can't be read.</summary>
    Task<(int Width, int Height)?> GetImageSizeAsync(string imagePath);

    /// <summary>
    /// Writes a JPEG frame from the video at exactly <paramref name="width"/>x<paramref name="height"/>
    /// (the caller passes the video's own shape). True when the file was written.
    /// </summary>
    Task<bool> ExtractFrameAsync(string videoPath, string outputPath, int width, int height);
}

/// <summary><see cref="IMediaProbe"/> on ffprobe / ffmpeg via FFMpegCore (both must be on PATH).</summary>
public sealed class FfmpegMediaProbe : IMediaProbe
{
    public async Task<(int Width, int Height)?> GetVideoDisplaySizeAsync(string videoPath)
    {
        try
        {
            var info = await FFProbe.AnalyseAsync(videoPath).ConfigureAwait(false);
            var stream = info.PrimaryVideoStream;
            if (stream is not { Width: > 0, Height: > 0 })
            {
                return null;
            }
            return ThumbnailShape.DisplaySize(stream.Width, stream.Height, stream.Rotation);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<(int Width, int Height)?> GetImageSizeAsync(string imagePath)
    {
        try
        {
            var info = await FFProbe.AnalyseAsync(imagePath).ConfigureAwait(false);
            var stream = info.PrimaryVideoStream;
            return stream is { Width: > 0, Height: > 0 } ? (stream.Width, stream.Height) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> ExtractFrameAsync(string videoPath, string outputPath, int width, int height)
    {
        try
        {
            var info = await FFProbe.AnalyseAsync(videoPath).ConfigureAwait(false);
            // 10% in skips intro / black frames; very short clips use their first frame.
            var at = info.Duration >= TimeSpan.FromSeconds(1)
                ? TimeSpan.FromSeconds(info.Duration.TotalSeconds * 0.1)
                : TimeSpan.Zero;
            // ffmpeg auto-rotates before the filter graph, so the scale applies to the displayed frame and
            // width/height (already the video's shape) never stretch it.
            var ok = await FFMpegArguments
                .FromFileInput(videoPath, verifyExists: true, o => o.Seek(at))
                .OutputToFile(outputPath, overwrite: true, o => o
                    .WithCustomArgument($"-frames:v 1 -vf scale={width}:{height} -q:v 2"))
                .ProcessAsynchronously(throwOnError: false)
                .ConfigureAwait(false);
            return ok && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

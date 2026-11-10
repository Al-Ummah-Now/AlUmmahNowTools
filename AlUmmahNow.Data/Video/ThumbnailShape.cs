namespace AlUmmahNow.Data.Video;

/// <summary>What the importer does with a video's thumbnail.</summary>
public enum ThumbnailDecision
{
    /// <summary>The source thumbnail has the video's shape (within <see cref="ThumbnailShape.Tolerance"/>); it is used.</summary>
    KeepSource,

    /// <summary>The source thumbnail's shape differs from the video's; a frame from the video is used instead.</summary>
    FrameShapeMismatch,

    /// <summary>There is no source thumbnail; a frame from the video is used.</summary>
    FrameNoThumbnail,

    /// <summary>The source thumbnail's size could not be read; a frame from the video is used.</summary>
    FrameUnreadableThumbnail,

    /// <summary>
    /// The video's size could not be read, so the shape can't be checked; the source thumbnail is used as is.
    /// </summary>
    KeepSourceUnverified,
}

/// <summary>
/// Pure helpers that decide whether a thumbnail has a video's shape, and the size of a frame taken from the video.
/// </summary>
/// <remarks>
/// Thumbnails are never stretched or cropped to fit: either the source art already has the video's aspect
/// ratio, or a real frame is taken from the video at its own shape.
/// </remarks>
public static class ThumbnailShape
{
    /// <summary>The largest relative aspect difference still treated as the same shape (5%).</summary>
    public const double Tolerance = 0.05;

    /// <summary>The longest edge of a generated frame.</summary>
    public const int FrameLongEdge = 1280;

    /// <summary>
    /// True when the thumbnail's aspect ratio is within <see cref="Tolerance"/> of the video's:
    /// <c>|thumbRatio / videoRatio - 1| &lt;= 0.05</c>. False when any size is missing or not positive.
    /// </summary>
    public static bool Matches(int thumbWidth, int thumbHeight, int videoWidth, int videoHeight)
    {
        if (thumbWidth <= 0 || thumbHeight <= 0 || videoWidth <= 0 || videoHeight <= 0)
        {
            return false;
        }
        var thumbRatio = (double)thumbWidth / thumbHeight;
        var videoRatio = (double)videoWidth / videoHeight;
        return Math.Abs(thumbRatio / videoRatio - 1) <= Tolerance;
    }

    /// <summary>
    /// The size a video is shown at: the coded size, with width and height swapped when the rotation
    /// metadata turns it a quarter turn (90 or 270 degrees, either sign).
    /// </summary>
    public static (int Width, int Height) DisplaySize(int codedWidth, int codedHeight, double rotationDegrees)
    {
        var quarterTurns = (int)Math.Round(rotationDegrees / 90.0);
        var normalized = ((quarterTurns % 4) + 4) % 4;
        return normalized % 2 == 1 ? (codedHeight, codedWidth) : (codedWidth, codedHeight);
    }

    /// <summary>
    /// The size of a frame taken from a video shown at <paramref name="videoWidth"/>x<paramref name="videoHeight"/>:
    /// the same shape, longest edge <paramref name="longEdge"/> (never upscaled), both sides even.
    /// </summary>
    public static (int Width, int Height) FrameSize(int videoWidth, int videoHeight, int longEdge = FrameLongEdge)
    {
        if (videoWidth <= 0 || videoHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(videoWidth), "The video size must be positive.");
        }
        var scale = Math.Min(1.0, (double)longEdge / Math.Max(videoWidth, videoHeight));
        return (Even(videoWidth * scale), Even(videoHeight * scale));
    }

    /// <summary>
    /// Chooses between the source thumbnail and a frame from the video.
    /// </summary>
    /// <param name="hasThumbnail">Whether a source thumbnail file exists.</param>
    /// <param name="thumbSize">The thumbnail's pixel size, or null when it couldn't be read.</param>
    /// <param name="videoSize">The video's displayed size, or null when it couldn't be read.</param>
    public static ThumbnailDecision Choose(bool hasThumbnail, (int Width, int Height)? thumbSize, (int Width, int Height)? videoSize)
    {
        var videoKnown = videoSize is { Width: > 0, Height: > 0 };
        if (!hasThumbnail)
        {
            return ThumbnailDecision.FrameNoThumbnail;
        }
        if (!videoKnown)
        {
            return ThumbnailDecision.KeepSourceUnverified;
        }
        if (thumbSize is not { Width: > 0, Height: > 0 } thumb)
        {
            return ThumbnailDecision.FrameUnreadableThumbnail;
        }
        var video = videoSize!.Value;
        return Matches(thumb.Width, thumb.Height, video.Width, video.Height)
            ? ThumbnailDecision.KeepSource
            : ThumbnailDecision.FrameShapeMismatch;
    }

    /// <summary>True when the decision uses a frame taken from the video.</summary>
    public static bool UsesFrame(this ThumbnailDecision decision)
        => decision is ThumbnailDecision.FrameShapeMismatch or ThumbnailDecision.FrameNoThumbnail
            or ThumbnailDecision.FrameUnreadableThumbnail;

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2.0) * 2);
}

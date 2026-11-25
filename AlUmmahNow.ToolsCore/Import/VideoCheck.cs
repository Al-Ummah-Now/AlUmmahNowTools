using System.Buffers.Binary;
using AlUmmahNow.ToolsCore.Import.Sources;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>What a file's own index says.</summary>
public enum VideoContent
{
    /// <summary>It has a video track.</summary>
    Video,
    /// <summary>It is complete, and has sound alone.</summary>
    NoVideo,
    /// <summary>It is no whole video: nothing in it, empty inside, or cut off before its index.</summary>
    Damaged,
    /// <summary>Not a kind of file this reads.</summary>
    Unknown,
}

/// <summary>
/// Reads an MP4 or MOV file's index (its boxes' headers alone, so a few small reads however large the file is).
/// </summary>
public static class Mp4Index
{
    private const int MaxBoxes = 100_000;

    private static readonly HashSet<string> FileBoxes = new(StringComparer.Ordinal) { "ftyp", "moov", "mdat", "free", "skip", "wide", "pnot", "uuid", "styp", "sidx", "moof", "mfra", "meta", "junk", "PICT" };

    public static async Task<VideoContent> ReadAsync(Stream file, long size, CancellationToken ct)
    {
        var start = new byte[(int)Math.Min(size, 4096)];
        file.Position = 0;
        await file.ReadExactlyAsync(start, ct).ConfigureAwait(false);
        // A file whose room was reserved and never filled
        if (!start.AsSpan().ContainsAnyExcept((byte)0)) return VideoContent.Damaged;
        if (size < 8 || !FileBoxes.Contains(Type(start.AsSpan(4, 4)))) return VideoContent.Unknown;

        long at = 0;
        for (var boxes = 0; boxes < MaxBoxes && size - at >= 8; boxes++)
        {
            if (await HeaderAsync(file, at, size, ct).ConfigureAwait(false) is not { } box) return VideoContent.Damaged;
            if (box.Type == "moov")
            {
                // Cut off inside its index
                if (box.End > size) return VideoContent.Damaged;
                if (await HasTrackAsync(file, box, "vide", ct).ConfigureAwait(false) is not { } video) return VideoContent.Damaged;
                if (video) return VideoContent.Video;
                return await HasTrackAsync(file, box, "soun", ct).ConfigureAwait(false) == true ? VideoContent.NoVideo : VideoContent.Damaged;
            }
            at = box.End;
        }
        return VideoContent.Damaged;
    }

    /// <summary>Null when the index itself cannot be read.</summary>
    private static async Task<bool?> HasTrackAsync(Stream file, Box moov, string kind, CancellationToken ct)
    {
        var budget = MaxBoxes;
        return await AnyAsync(moov, "trak", async trak => await AnyAsync(trak, "mdia", async mdia => await AnyAsync(mdia, "hdlr", async hdlr =>
        {
            // Version and flags, the reserved type, then what the track is
            if (hdlr.End - hdlr.Content < 12) return null;
            var handler = new byte[12];
            file.Position = hdlr.Content;
            await file.ReadExactlyAsync(handler, ct).ConfigureAwait(false);
            return Type(handler.AsSpan(8, 4)) == kind;
        }).ConfigureAwait(false)).ConfigureAwait(false)).ConfigureAwait(false);

        async Task<bool?> AnyAsync(Box parent, string type, Func<Box, Task<bool?>> look)
        {
            var at = parent.Content;
            while (parent.End - at >= 8)
            {
                if (--budget < 0 || await HeaderAsync(file, at, parent.End, ct).ConfigureAwait(false) is not { } box || box.End > parent.End) return null;
                if (box.Type == type)
                {
                    var found = await look(box).ConfigureAwait(false);
                    if (found != false) return found;
                }
                at = box.End;
            }
            return false;
        }
    }

    private readonly record struct Box(string Type, long Content, long End);

    /// <summary>The box at <paramref name="at"/>; null when there is none (its size makes no sense). Its end may be past <paramref name="limit"/>.</summary>
    private static async Task<Box?> HeaderAsync(Stream file, long at, long limit, CancellationToken ct)
    {
        var head = new byte[(int)Math.Min(16, limit - at)];
        file.Position = at;
        await file.ReadExactlyAsync(head, ct).ConfigureAwait(false);
        long size = BinaryPrimitives.ReadUInt32BigEndian(head);
        var header = 8;
        if (size == 1)
        {
            if (head.Length < 16) return null;
            var large = BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(8));
            if (large > long.MaxValue / 2) return null;
            (size, header) = ((long)large, 16);
        }
        // To the end of what holds it
        else if (size == 0) size = limit - at;
        return size < header ? null : new Box(Type(head.AsSpan(4, 4)), at + header, at + size);
    }

    private static string Type(ReadOnlySpan<byte> bytes) => System.Text.Encoding.Latin1.GetString(bytes);
}

/// <summary>
/// A file goes up only when it has a video in it: the server takes no sound alone, and finds out only once everything was
/// sent. MP4 and MOV files are read here; the rest by ffprobe, when this computer has it.
/// </summary>
internal static class VideoCheck
{
    public static async Task RunAsync(QueueItem item, SourceAccess source, IMediaTools? media, IStepContext context, CancellationToken ct)
    {
        if (item.VideoChecked) return;
        var video = item.Video;
        VideoContent content;
        var stream = source.Open(video.Path, video.Size, video.LastModifiedMs, context);
        await using (stream.ConfigureAwait(false))
        {
            content = await Mp4Index.ReadAsync(stream, video.Size, ct).ConfigureAwait(false);
        }
        if (content == VideoContent.Unknown) content = await ProbeAsync(video.Path, source, media, context, ct).ConfigureAwait(false);
        switch (content)
        {
            case VideoContent.NoVideo:
                throw new StepFailedException("no_video", "The file has sound and no video", retryable: false);
            case VideoContent.Damaged:
                throw new StepFailedException("not_video", "The file cannot be read as a video: it may have been cut off while it was copied", retryable: false);
            case VideoContent.Unknown:
                context.Log(LogLevel.Warning, "video_unchecked", "Whether the file has a video in it was not checked: ffprobe is not on this computer");
                break;
        }
        item.VideoChecked = true;
        context.Save();
    }

    private static async Task<VideoContent> ProbeAsync(string path, SourceAccess source, IMediaTools? media, IStepContext context, CancellationToken ct)
    {
        if (media is null) return VideoContent.Unknown;
        try
        {
            await source.WithPathAsync(path, context, p => media.ProbeAsync(p, ct), ct).ConfigureAwait(false);
            return VideoContent.Video;
        }
        catch (LocalEncodeException probe)
        {
            return probe.Code switch
            {
                "no_video" => VideoContent.NoVideo,
                "probe_failed" or "no_media" => VideoContent.Damaged,
                "source_read" => throw new StepFailedException("file_read", probe.Message, retryable: true, probe),
                _ => VideoContent.Unknown,
            };
        }
    }
}

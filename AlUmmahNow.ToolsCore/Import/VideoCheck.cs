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
    /// <summary>It is no whole video: nothing in it, empty inside, or cut off.</summary>
    Damaged,
    /// <summary>Not a kind of file this reads.</summary>
    Unknown,
    /// <summary>A kind this reads, and it could not tell (too many boxes, too little time, a packed index): never a reason to refuse it.</summary>
    Undecided,
}

/// <summary>
/// Reads an MP4 or MOV file's boxes' headers, once over the whole file and bounded, so a few small reads however large the file
/// is; and looks at three places in its data, for a file whose room was reserved and never filled.
/// </summary>
public static class Mp4Index
{
    /// <summary>All the boxes looked at, at every depth. Real files have hundreds; long fragmented ones some thousands.</summary>
    public const int MaxBoxes = 20_000;

    private const int Sample = 16 * 1024;

    private static readonly HashSet<string> FileBoxes = new(StringComparer.Ordinal) { "ftyp", "moov", "mdat", "free", "skip", "wide", "pnot", "uuid", "styp", "sidx", "moof", "mfra", "meta", "junk", "PICT" };

    /// <summary>One still picture, as a cover is kept: not a video.</summary>
    private static readonly HashSet<string> Stills = new(StringComparer.Ordinal) { "jpeg", "png ", "gif ", "tiff", "mjpa", "mjpb", "mjp2" };

    public static async Task<VideoContent> ReadAsync(Stream file, long size, CancellationToken ct)
    {
        var reader = new Reader(file, size);
        var start = await reader.ReadAsync(0, (int)Math.Min(size, 4096), ct).ConfigureAwait(false);
        // A file whose room was reserved and never filled
        if (!start.Span.ContainsAnyExcept((byte)0)) return VideoContent.Damaged;
        if (size < 8 || !FileBoxes.Contains(Type(start.Span.Slice(4, 4)))) return VideoContent.Unknown;

        var tracks = new Tracks();
        var (index, fragments) = (false, false);
        var data = new List<(long Start, long End)>();
        long at = 0;
        while (size - at >= 8)
        {
            if (--reader.Boxes < 0) return VideoContent.Undecided;
            if (await reader.HeaderAsync(at, size, ct).ConfigureAwait(false) is not { } box)
            {
                // No box: what comes after the index and the data is nobody's (some apps leave padding there)
                if (index && data.Count > 0) break;
                return VideoContent.Damaged;
            }
            if (box.End > size)
            {
                // Cut off inside its index, its data or a fragment; any other box at the very end is not missed
                if (!index || box.Type is "moov" or "mdat" or "moof") return VideoContent.Damaged;
                break;
            }
            switch (box.Type)
            {
                case "moov":
                    index = true;
                    if (!await TracksAsync(reader, box, tracks, ct).ConfigureAwait(false)) return reader.Boxes < 0 ? VideoContent.Undecided : VideoContent.Damaged;
                    if (tracks.Packed) return VideoContent.Undecided;
                    break;
                case "mdat":
                    data.Add((box.Content, box.End));
                    break;
                case "moof":
                    fragments = true;
                    break;
            }
            at = box.End;
        }
        // No index, or nothing it could be about
        if (!index || (data.Count == 0 && (fragments || !tracks.Elsewhere))) return VideoContent.Damaged;
        // The pictures of a sound's chapters are no video (ffprobe's attached pictures)
        if (!tracks.Videos.Exists(id => !tracks.Chapters.Contains(id))) return tracks.Sound ? VideoContent.NoVideo : VideoContent.Damaged;
        return await EmptyAsync(reader, data, ct).ConfigureAwait(false) ? VideoContent.Damaged : VideoContent.Video;
    }

    /// <summary>
    /// Its data, all of it together, is nothing but zeros at the middle, at three quarters and at the end. Only the data: room left
    /// free in a file, however large, is meant to be empty.
    /// </summary>
    private static async Task<bool> EmptyAsync(Reader reader, List<(long Start, long End)> data, CancellationToken ct)
    {
        var length = data.Sum(d => d.End - d.Start);
        if (length < 4 * Sample) return false;
        foreach (var place in new[] { length / 2, length / 4 * 3, length - Sample })
        {
            var before = place;
            var piece = data.First(d =>
            {
                if (before < d.End - d.Start) return true;
                before -= d.End - d.Start;
                return false;
            });
            var count = (int)Math.Min(Sample, piece.End - piece.Start - before);
            if ((await reader.ReadAsync(piece.Start + before, count, ct).ConfigureAwait(false)).Span.ContainsAnyExcept((byte)0)) return false;
        }
        return true;
    }

    private sealed class Tracks
    {
        public bool Sound;
        /// <summary>The ids of the video tracks, and of every track that holds another one's chapters</summary>
        public readonly List<uint> Videos = [];
        public readonly HashSet<uint> Chapters = [];
        /// <summary>The index is compressed (old QuickTime)</summary>
        public bool Packed;
        /// <summary>The data may be in another file (a reference movie)</summary>
        public bool Elsewhere;
    }

    private sealed class Track
    {
        public string Handler = "", Codec = "";
        public long Samples = -1;
        public uint Id;
        public readonly List<uint> Chapters = [];
    }

    /// <summary>False when the index itself cannot be read.</summary>
    private static async Task<bool> TracksAsync(Reader reader, Box moov, Tracks tracks, CancellationToken ct)
    {
        var at = moov.Content;
        while (moov.End - at >= 8)
        {
            if (--reader.Boxes < 0 || await reader.HeaderAsync(at, moov.End, ct).ConfigureAwait(false) is not { } box || box.End > moov.End) return false;
            if (box.Type == "cmov") tracks.Packed = true;
            if (box.Type == "rmra") tracks.Elsewhere = true;
            if (box.Type == "trak")
            {
                var track = new Track();
                if (!await TrackAsync(reader, box, track, ct).ConfigureAwait(false)) return false;
                if (track.Handler == "soun") tracks.Sound = true;
                // A cover is one still picture
                else if (track.Handler == "vide" && !(track.Samples == 1 && Stills.Contains(track.Codec))) tracks.Videos.Add(track.Id);
                tracks.Chapters.UnionWith(track.Chapters);
            }
            at = box.End;
        }
        return true;
    }

    private static async Task<bool> TrackAsync(Reader reader, Box parent, Track track, CancellationToken ct)
    {
        var at = parent.Content;
        while (parent.End - at >= 8)
        {
            if (--reader.Boxes < 0 || await reader.HeaderAsync(at, parent.End, ct).ConfigureAwait(false) is not { } box || box.End > parent.End) return false;
            var length = box.End - box.Content;
            switch (box.Type)
            {
                // Version and flags, when it was made and changed (8 bytes each from version 1 on), then its number
                case "tkhd" when length >= 24:
                    var version = (await reader.ReadAsync(box.Content, 1, ct).ConfigureAwait(false)).Span[0];
                    track.Id = BinaryPrimitives.ReadUInt32BigEndian((await reader.ReadAsync(box.Content + (version == 0 ? 12 : 20), 4, ct).ConfigureAwait(false)).Span);
                    break;
                case "chap" when parent.Type == "tref":
                    for (var i = 0; i < Math.Min(length / 4, 16); i++)
                        track.Chapters.Add(BinaryPrimitives.ReadUInt32BigEndian((await reader.ReadAsync(box.Content + (i * 4), 4, ct).ConfigureAwait(false)).Span));
                    break;
                case "tref" or "mdia" or "minf" or "stbl":
                    if (!await TrackAsync(reader, box, track, ct).ConfigureAwait(false)) return false;
                    break;
                // Version and flags, the reserved type, then what the track is. The first: QuickTime has another in its minf
                case "hdlr" when parent.Type == "mdia":
                    if (length < 12) return false;
                    track.Handler = Type((await reader.ReadAsync(box.Content + 8, 4, ct).ConfigureAwait(false)).Span);
                    break;
                // Version and flags, how many, then the first one's size and kind
                case "stsd" when length >= 16:
                    track.Codec = Type((await reader.ReadAsync(box.Content + 12, 4, ct).ConfigureAwait(false)).Span);
                    break;
                // Version and flags, the samples' size, then how many
                case "stsz" or "stz2" when length >= 12:
                    track.Samples = BinaryPrimitives.ReadUInt32BigEndian((await reader.ReadAsync(box.Content + 8, 4, ct).ConfigureAwait(false)).Span);
                    break;
            }
            at = box.End;
        }
        return true;
    }

    private readonly record struct Box(string Type, long Content, long End);

    /// <summary>Headers stand close together in an index: read a piece at a time.</summary>
    private sealed class Reader(Stream file, long size)
    {
        private readonly byte[] _buffer = new byte[64 * 1024];
        private long _at;
        private int _length;

        public int Boxes = MaxBoxes;

        public async Task<ReadOnlyMemory<byte>> ReadAsync(long at, int count, CancellationToken ct)
        {
            if (at < _at || at + count > _at + _length)
            {
                ct.ThrowIfCancellationRequested();
                _length = 0;
                _at = at;
                var piece = (int)Math.Min(_buffer.Length, size - at);
                // A header alone in a large file: the rest of the piece would be its data
                if (count <= 16 && piece > 4096) piece = 4096;
                file.Position = at;
                await file.ReadExactlyAsync(_buffer.AsMemory(0, Math.Max(piece, count)), ct).ConfigureAwait(false);
                _length = Math.Max(piece, count);
            }
            return _buffer.AsMemory((int)(at - _at), count);
        }

        /// <summary>The box at <paramref name="at"/>; null when there is none (its size makes no sense). Its end may be past <paramref name="limit"/>.</summary>
        public async Task<Box?> HeaderAsync(long at, long limit, CancellationToken ct)
        {
            var head = await ReadAsync(at, (int)Math.Min(16, limit - at), ct).ConfigureAwait(false);
            var type = Type(head.Span.Slice(4, 4));
            long length = BinaryPrimitives.ReadUInt32BigEndian(head.Span);
            var header = 8;
            if (length == 1)
            {
                if (head.Length < 16) return null;
                var large = BinaryPrimitives.ReadUInt64BigEndian(head.Span[8..]);
                if (large > long.MaxValue / 2) return null;
                (length, header) = ((long)large, 16);
            }
            // To the end of what holds it; zeros are no box
            else if (length == 0) length = type == "\0\0\0\0" ? 0 : limit - at;
            return length < header ? null : new Box(type, at + header, at + length);
        }
    }

    private static string Type(ReadOnlySpan<byte> bytes) => System.Text.Encoding.Latin1.GetString(bytes);
}

/// <summary>
/// A file goes up only when it has a video in it: the server takes no sound alone, and finds out only once everything was
/// sent. MP4 and MOV files are read here, the rest by ffprobe, when this computer has it. ffprobe, which is what the server
/// looks with, is also asked whether it finds a video in what is one by its index: its "sound alone" holds. What cannot be told
/// is no reason to refuse a file.
/// </summary>
internal static class VideoCheck
{
    /// <summary>How long the index may take to read (a slow share)</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public static async Task RunAsync(QueueItem item, SourceAccess source, IMediaTools? media, IStepContext context, CancellationToken ct, TimeSpan? limit = null)
    {
        if (item.VideoChecked && item.VideoProbed) return;
        var video = item.Video;
        var content = VideoContent.Video;
        if (!item.VideoChecked) content = await IndexAsync(video, source, context, limit ?? Limit, ct).ConfigureAwait(false);
        if (content == VideoContent.Unknown) content = await ProbeAsync(video.Path, source, media, context, ct).ConfigureAwait(false);
        else if (content is VideoContent.Video or VideoContent.Undecided && await HasNoVideoAsync(video.Path, source, media, context, ct).ConfigureAwait(false)) content = VideoContent.NoVideo;
        switch (content)
        {
            case VideoContent.NoVideo:
                throw new StepFailedException("no_video", "The file has sound and no video", retryable: false);
            case VideoContent.Damaged:
                throw new StepFailedException("not_video", "The file cannot be read as a video: it may have been cut off while it was copied", retryable: false);
            case VideoContent.Unknown:
                context.Log(LogLevel.Warning, "video_unchecked", "Whether the file has a video in it was not checked: ffprobe is not on this computer");
                break;
            case VideoContent.Undecided:
                context.Log(LogLevel.Warning, "video_undecided", "Whether the file has a video in it could not be told; it goes up");
                break;
        }
        item.VideoChecked = item.VideoProbed = true;
        context.Save();
    }

    private static async Task<VideoContent> IndexAsync(ScannedVideo video, SourceAccess source, IStepContext context, TimeSpan allowed, CancellationToken ct)
    {
        using (var time = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            if (allowed > TimeSpan.Zero) time.CancelAfter(allowed);
            else await time.CancelAsync().ConfigureAwait(false);
            try
            {
                var stream = source.Open(video.Path, video.Size, video.LastModifiedMs, context);
                await using (stream.ConfigureAwait(false))
                {
                    return await Mp4Index.ReadAsync(stream, video.Size, time.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return VideoContent.Undecided;
            }
        }
    }

    /// <summary>How long ffprobe may look at a file whose index was read (a slow share)</summary>
    private static readonly TimeSpan ProbeLimit = TimeSpan.FromMinutes(1);

    /// <summary>ffprobe found sound and no video; anything else it says, or cannot say, leaves what the index said.</summary>
    private static async Task<bool> HasNoVideoAsync(string path, SourceAccess source, IMediaTools? media, IStepContext context, CancellationToken ct)
    {
        if (media is null) return false;
        using var time = CancellationTokenSource.CreateLinkedTokenSource(ct);
        time.CancelAfter(ProbeLimit);
        try
        {
            await source.WithPathAsync(path, context, p => media.ProbeAsync(p, time.Token), time.Token).ConfigureAwait(false);
            return false;
        }
        catch (LocalEncodeException probe)
        {
            return probe.Code == "no_video";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<VideoContent> ProbeAsync(string path, SourceAccess source, IMediaTools? media, IStepContext context, CancellationToken ct)
    {
        if (media is null) return VideoContent.Unknown;
        try
        {
            await source.ProbeAsync(media, path, context, ct).ConfigureAwait(false);
            return VideoContent.Video;
        }
        catch (LocalEncodeException probe)
        {
            return probe.Code switch
            {
                "no_video" => VideoContent.NoVideo,
                "no_media" => VideoContent.Damaged,
                // It ended without saying what is wrong with the file
                "probe_failed" => VideoContent.Undecided,
                "source_read" => throw new StepFailedException("file_read", probe.Message, retryable: true, probe),
                _ => VideoContent.Unknown,
            };
        }
    }
}

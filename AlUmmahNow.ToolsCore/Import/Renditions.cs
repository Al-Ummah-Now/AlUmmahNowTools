using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AlUmmahNow.ToolsCore.Import.Sources;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>The renditions encoded on this computer for one file, until the server has them (TA-12).</summary>
public sealed class RenditionSet
{
    /// <summary>The folder with one sub-folder per rung (<c>index.m3u8</c>, <c>init.mp4</c>, <c>seg_00001.m4s</c>…)</summary>
    public string Directory { get; set; } = "";
    public List<string> Rungs { get; set; } = new();
    public List<RenditionFile> Files { get; set; } = new();
    public long Bytes { get; set; }
    /// <summary>What encoded them, sent with the staging (e.g. "Al Ummah Now Tools 1.0, ffmpeg libx264")</summary>
    public string Encoder { get; set; } = "";
    public string? StagingId { get; set; }
    /// <summary>Every file was sent once; after a restart only the files the server says are missing go again</summary>
    public bool FirstPassDone { get; set; }
    /// <summary>The server took the staging; the local files are deleted</summary>
    public bool Sent { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>What the server did with the renditions once its transcode ran.</summary>
public sealed record RenditionVerdict(
    [property: JsonPropertyName("used")] bool Used,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("rung")] string? Rung,
    [property: JsonPropertyName("transcodeFailed")] bool TranscodeFailed = false);

/// <summary>A local encode that cannot be done here; the server then encodes the file itself.</summary>
public sealed class LocalEncodeException : Exception
{
    public LocalEncodeException(string code, string message, Exception? inner = null) : base(message, inner) => Code = code;

    /// <summary>source_read (the file stopped being readable), ffmpeg_missing, probe_failed, no_video (sound alone), no_media (neither), no_dimensions (a video whose size could not be told), no_audio, disk_space, ffmpeg_failed, too_short (the encoded video), copy_differs, took_too_long, package_failed, too_large, settings_invalid, colour_unsupported (no conversion shows it right), hdr_settings (the server's HDR tables are others) or colour_tags (the encoder wrote other colours)</summary>
    public string Code { get; }
}

/// <summary>A source as ffprobe sees it.</summary>
/// <param name="DurationSeconds">How long the container says it is</param>
/// <param name="VideoSeconds">How long its video is; 0 when it does not say</param>
/// <param name="AudioSeconds">How long its first sound is; 0 when it does not say</param>
public sealed record SourceInfo(int Width, int Height, int Rotation, double Fps, bool HasAudio, double DurationSeconds, double VideoSeconds = 0, double AudioSeconds = 0)
{
    /// <summary>The picture's colours; null when nothing was read of them.</summary>
    public SourceColour? Colour { get; init; }

    /// <summary>How long what is encoded of it is: a container is as long as its longest track, which may be a subtitle.</summary>
    public double Seconds
    {
        get
        {
            // Both known, or it is not known which is the longer
            var streams = VideoSeconds > 0 && (!HasAudio || AudioSeconds > 0) ? Math.Max(VideoSeconds, AudioSeconds) : 0;
            return streams <= 0 ? DurationSeconds : DurationSeconds <= 0 ? streams : Math.Min(streams, DurationSeconds);
        }
    }

    /// <summary>
    /// What is missing of what was encoded from it: its video and its sound each reach their own length in the source, to the
    /// second (half of it for a clip shorter than two). A track that does not say how long it is must be as long as the container.
    /// </summary>
    /// <returns>Null: nothing</returns>
    public string? Missing(SourceInfo made)
    {
        // An encoded file that says neither (no MP4 of ours) is taken at its container's word
        var silent = made.VideoSeconds <= 0 && made.AudioSeconds <= 0;
        if (Short("video", VideoSeconds, silent ? made.DurationSeconds : made.VideoSeconds) is { } video) return video;
        if (!HasAudio) return null;
        return made.HasAudio ? Short("sound", AudioSeconds, silent ? made.DurationSeconds : made.AudioSeconds) : "The encoded video has no sound";
    }

    private string? Short(string what, double known, double made)
    {
        var all = known <= 0 ? DurationSeconds : DurationSeconds <= 0 ? known : Math.Min(known, DurationSeconds);
        if (made > 0 && made >= all - Math.Min(1, all / 2)) return null;
        return string.Create(CultureInfo.InvariantCulture, $"The encoded video's {what} is {made:0.##} s long and the file's {all:0.##} s");
    }
}

/// <summary>One rung to encode: the server's rung at this source's size.</summary>
public sealed record PlannedRung(string Name, int Width, int Height, int Maxrate, int Bufsize, int Audio, string Profile);

/// <summary>The server's ladder for a source, the same rule as the media container's <c>ladderFor</c> (apps/media/container/ladder.mjs).</summary>
public static class EncodeLadder
{
    /// <summary>A rung's name names its folder and files here, so only names like the server's ("360p", "1080p") are taken.</summary>
    private static readonly System.Text.RegularExpressions.Regex RungName = new("^[0-9]{3,4}p$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static IReadOnlyList<PlannedRung> For(SourceInfo source, EncodeLadderSettings settings)
    {
        if (settings.Rungs.Count == 0 || settings.Rungs.Any(r => r.Name is null || !RungName.IsMatch(r.Name)))
        {
            throw new LocalEncodeException("settings_invalid", "The server's encoding settings name a rung the app does not take");
        }
        var quarter = Math.Abs(source.Rotation) % 180 == 90;
        var width = quarter ? source.Height : source.Width;
        var height = quarter ? source.Width : source.Height;
        if (width <= 0 || height <= 0) throw new LocalEncodeException("no_dimensions", "The size of the picture could not be read");
        var portrait = height > width;
        var shortSide = Math.Min(width, height);
        var aspect = (double)width / height;
        var rungs = settings.Rungs.Where(r => r.Short <= shortSide + settings.ShortSideTolerance).ToList();
        if (rungs.Count == 0) rungs = new List<EncodeRungSetting> { settings.Rungs[0] with { Short = EvenDown(shortSide) } };
        var high = settings.Video.Profile?.High;
        return rungs.Select(r =>
        {
            var s = Math.Min(r.Short, EvenDown(shortSide));
            var w = portrait ? s : Even(s * aspect);
            var h = portrait ? Even(s / aspect) : s;
            var profile = h >= (high?.MinHeight ?? 720) || w >= (high?.MinWidth ?? 1280) ? "high" : "main";
            return new PlannedRung(r.Name, w, h, r.Maxrate, (int)(r.Maxrate * settings.Video.BufsizeFactor), r.Audio, profile);
        }).ToList();
    }

    /// <summary>The output frame rate: sources above the cap's threshold are encoded at the cap.</summary>
    public static double? CappedFps(SourceInfo source, EncodeLadderSettings settings) =>
        source.Fps > settings.Video.FpsCap.AboveFps ? settings.Video.FpsCap.Fps : null;

    // As JavaScript's Math.round (halves up), never below 2
    private static int Even(double n) => Math.Max(2, (int)Math.Round(n / 2, MidpointRounding.AwayFromZero) * 2);
    private static int EvenDown(int n) => Math.Max(2, n / 2 * 2);
}

/// <summary>The H.264 encoders the app can use, best first: libx264 is what the server uses; hardware encoders and OpenH264 follow.</summary>
public static class H264Encoders
{
    public static readonly string[] Preferred = { "libx264", "h264_videotoolbox", "h264_nvenc", "h264_qsv", "h264_amf", "h264_mf", "libopenh264" };

    /// <summary>The first preferred encoder in <c>ffmpeg -encoders</c>' list, or null.</summary>
    public static string? Pick(string encodersOutput)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in encodersOutput.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // " V....D libx264  libx264 H.264 …": capabilities, then the name
            if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] == 'V') names.Add(parts[1]);
        }
        return Preferred.FirstOrDefault(names.Contains);
    }

    /// <summary>The encoder's rate and keyframe options for one output: a keyframe every GOP and never one from a scene cut.</summary>
    public static IEnumerable<string> Options(string encoder, PlannedRung rung, int crf, int keyint, int index)
    {
        string S(string name) => $"-{name}:v:{index}";
        var average = (rung.Maxrate * 6 / 10).ToString(CultureInfo.InvariantCulture);
        var max = rung.Maxrate.ToString(CultureInfo.InvariantCulture);
        var buf = rung.Bufsize.ToString(CultureInfo.InvariantCulture);
        var gop = keyint.ToString(CultureInfo.InvariantCulture);
        var common = new[] { S("profile"), rung.Profile, S("g"), gop };
        var rate = encoder switch
        {
            "libx264" => new[] { S("crf"), crf.ToString(CultureInfo.InvariantCulture), S("maxrate"), max, S("bufsize"), buf, S("preset"), "veryfast", S("sc_threshold"), "0" },
            "h264_videotoolbox" => new[] { S("b"), average, S("maxrate"), max, S("bufsize"), buf, S("allow_sw"), "1" },
            "h264_nvenc" => new[] { S("rc"), "vbr", S("cq"), crf.ToString(CultureInfo.InvariantCulture), S("b"), average, S("maxrate"), max, S("bufsize"), buf, S("no-scenecut"), "1", S("forced-idr"), "1" },
            "h264_qsv" => new[] { S("b"), average, S("maxrate"), max, S("bufsize"), buf, S("forced_idr"), "1", S("idr_interval"), "0" },
            "h264_amf" => new[] { S("rc"), "vbr_peak", S("b"), average, S("maxrate"), max, S("bufsize"), buf },
            _ => new[] { S("b"), average, S("maxrate"), max, S("bufsize"), buf },
        };
        return common.Concat(rate);
    }

    /// <summary>The pixel format the encoder takes from the scaler (the stream it writes is 4:2:0 either way).</summary>
    public static string InputFormat(string encoder) => encoder == "h264_qsv" ? "nv12" : "yuv420p";
}

/// <summary>Runs ffprobe and ffmpeg.</summary>
public interface IMediaTools
{
    Task<SourceInfo> ProbeAsync(string path, CancellationToken ct);
    /// <summary>The H.264 encoder to use, or null when this ffmpeg has none of the preferred ones.</summary>
    Task<string?> PickEncoderAsync(CancellationToken ct);
    /// <summary>One decode, one progressive MP4 per rung (<paramref name="outputs"/>, in rung order). Progress 0–1.</summary>
    Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct);
    /// <summary>
    /// The first audio track as 16 kHz mono 16-bit WAV, what whisper reads (TA-13): any container and codec ffmpeg
    /// reads, where aun-core's own decoder takes only WAV, MP3, AAC/M4A, FLAC and Vorbis.
    /// </summary>
    Task ExtractAudioAsync(string source, string wavPath, CancellationToken ct);
}

/// <summary>Packages a rung's MP4 into fMP4 HLS cut like the server's encode (aun-core <c>hls_package_mp4</c>).</summary>
public interface IRenditionPackager
{
    Task PackageAsync(string mp4, string outputDirectory, int segmentSeconds, CancellationToken ct);
}

/// <summary>aun-core's packaging: no re-encoding, segments cut on the keyframes the encode placed.</summary>
public sealed class CoreRenditionPackager : IRenditionPackager
{
    public async Task PackageAsync(string mp4, string outputDirectory, int segmentSeconds, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(outputDirectory);
        using var cancel = new AlUmmahNow.Core.MediaCancel();
        using var registration = ct.Register(cancel.Cancel);
        try
        {
            await AlUmmahNow.Core.AunCoreMethods.HlsPackageMp4(mp4, outputDirectory, (uint)segmentSeconds, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            throw new LocalEncodeException("package_failed", "The renditions could not be packaged: " + ex.Message, ex);
        }
        ct.ThrowIfCancellationRequested();
    }
}

/// <summary>ffprobe and ffmpeg from the paths the app found (the FFmpeg settings page, or the system's).</summary>
public sealed class FfmpegMediaTools : IMediaTools
{
    private readonly Func<CancellationToken, Task<(string Ffmpeg, string Ffprobe)?>> _locate;
    private string? _encoder;

    private readonly SourceAccess _source;

    /// <summary>How long ffprobe may take to read what a file is (a slow share)</summary>
    internal TimeSpan ProbeLimit { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long ffmpeg may go without getting any further (it moves each MP4's index to its front at the end, which says nothing)</summary>
    internal TimeSpan StallLimit { get; init; } = TimeSpan.FromMinutes(30);

    public FfmpegMediaTools(Func<CancellationToken, Task<(string Ffmpeg, string Ffprobe)?>> locate, SourceAccess? source = null) => (_locate, _source) = (locate, source ?? SourceAccess.Default);

    private async Task<(string Ffmpeg, string Ffprobe)> Paths(CancellationToken ct) =>
        await _locate(ct).ConfigureAwait(false) ?? throw new LocalEncodeException("ffmpeg_missing", "ffmpeg was not found on this computer");

    public async Task<SourceInfo> ProbeAsync(string path, CancellationToken ct)
    {
        var (_, ffprobe) = await Paths(ct).ConfigureAwait(false);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(ProbeLimit);
        int code;
        string output, error;
        try
        {
            (code, output, error) = await RunAsync(ffprobe, new[] { "-v", "error", "-print_format", "json", "-show_streams", "-show_format", path }, null, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw await StalledAsync(path, "ffprobe", ProbeLimit, "probe_failed", ct).ConfigureAwait(false);
        }
        // Also when it ended well: what it could not read is not what the file lacks
        if (await SourceStoppedAsync(path, error, code, ct).ConfigureAwait(false) is { } stopped) throw stopped;
        // It says so itself: what it was given is no media file, or not a whole one. Anything else it ends with says nothing of the file
        if (code != 0) throw new LocalEncodeException(code == 1 && NoMedia.IsMatch(error) ? "no_media" : "probe_failed", "ffprobe could not read the file: " + Why(error, path));
        return ParseProbe(output);
    }

    private async Task<LocalEncodeException> StalledAsync(string source, string tool, TimeSpan limit, string code, CancellationToken ct) =>
        new(await _source.IsNetworkAsync(source, ct).ConfigureAwait(false) ? "source_read" : code,
            string.Create(CultureInfo.InvariantCulture, $"{tool} got no further for {limit.TotalMinutes:0.##} minutes"));

    /// <summary>ffprobe's JSON: the first video stream's size, rotation and frame rate, whether there is sound, the duration.</summary>
    public static SourceInfo ParseProbe(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LocalEncodeException("probe_failed", "ffprobe answered something the app cannot read", ex);
        }
        var streams = root?["streams"]?.AsArray() ?? new JsonArray();
        var video = streams.FirstOrDefault(s => s?["codec_type"]?.GetValue<string>() == "video" && s?["disposition"]?["attached_pic"]?.GetValue<int>() != 1);
        if (video is null)
        {
            throw streams.Any(s => s?["codec_type"]?.GetValue<string>() == "audio")
                ? new LocalEncodeException("no_video", "The file has sound and no video track")
                : new LocalEncodeException("no_media", "The file has neither a video nor a sound track");
        }
        var rotation = 0;
        foreach (var side in video["side_data_list"]?.AsArray() ?? new JsonArray())
        {
            if (side?["rotation"] is JsonNode r) rotation = (int)Math.Round(Number(r));
        }
        if (rotation == 0 && video["tags"]?["rotate"] is JsonNode rotate) rotation = (int)Math.Round(Number(rotate));
        var fps = Rate(video["avg_frame_rate"]?.GetValue<string>());
        if (fps <= 0) fps = Rate(video["r_frame_rate"]?.GetValue<string>());
        var duration = root?["format"]?["duration"] is JsonNode d ? Number(d) : video["duration"] is JsonNode vd ? Number(vd) : 0;
        var sound = streams.FirstOrDefault(s => s?["codec_type"]?.GetValue<string>() == "audio");
        return new SourceInfo(
            (int)Number(video["width"]),
            (int)Number(video["height"]),
            rotation,
            fps,
            sound is not null,
            duration,
            Length(video),
            sound is null ? 0 : Length(sound)) { Colour = SourceColour.Of(video) };
    }

    /// <summary>A stream's own length; Matroska keeps where it ends in a tag ("00:00:10.023000000").</summary>
    private static double Length(JsonNode stream)
    {
        if (Number(stream["duration"]) is > 0 and var seconds) return seconds;
        var tag = stream["tags"]?.AsObject().FirstOrDefault(t => t.Key.Equals("DURATION", StringComparison.OrdinalIgnoreCase)).Value;
        var parts = (tag is JsonValue value && value.TryGetValue<string>(out var text) ? text : "").Split(':');
        if (parts.Length != 3) return 0;
        var numbers = parts.Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : -1).ToArray();
        if (numbers.Any(n => n < 0)) return 0;
        return Math.Max(0, (numbers[0] * 3600) + (numbers[1] * 60) + numbers[2] - Math.Max(0, Number(stream["start_time"])));
    }

    public async Task<string?> PickEncoderAsync(CancellationToken ct)
    {
        if (_encoder is not null) return _encoder;
        var (ffmpeg, _) = await Paths(ct).ConfigureAwait(false);
        var (_, output, _) = await RunAsync(ffmpeg, new[] { "-hide_banner", "-encoders" }, null, ct).ConfigureAwait(false);
        return _encoder = H264Encoders.Pick(output);
    }

    public async Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct)
    {
        var (ffmpeg, _) = await Paths(ct).ConfigureAwait(false);
        var args = Arguments(source, info, rungs, outputs, encoder, settings);
        string? folder = null;
        if (SdrPlan.For(info.Colour) is { IsHdr: true } hdr)
        {
            // ffmpeg reads the table by its bare name, from the folder it runs in
            folder = Path.GetDirectoryName(Path.GetFullPath(outputs[0]))!;
            await File.WriteAllBytesAsync(Path.Combine(folder, HdrCubes.FileName), HdrCubes.For(hdr, settings), ct).ConfigureAwait(false);
        }
        var duration = info.Seconds;
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(StallLimit);
        long furthest = 0;
        void OnLine(string line)
        {
            // -progress pipe:1 writes "out_time_us=12345678" lines, also while it waits
            if (!line.StartsWith("out_time_us=", StringComparison.Ordinal) || !long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us)) return;
            if (us > furthest)
            {
                furthest = us;
                stall.CancelAfter(StallLimit);
            }
            if (duration > 0) progress.Report(Math.Clamp(us / 1_000_000.0 / duration, 0, 1));
        }
        int code;
        string error;
        try
        {
            (code, _, error) = await RunAsync(ffmpeg, args, OnLine, stall.Token, folder).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw await StalledAsync(source, "ffmpeg", StallLimit, "ffmpeg_failed", ct).ConfigureAwait(false);
        }
        finally
        {
            if (folder is not null) File.Delete(Path.Combine(folder, HdrCubes.FileName));
        }
        var network = await _source.IsNetworkAsync(source, ct).ConfigureAwait(false);
        // It ends well all the same, with what it had: whether it read the file to its end is in what it made. The same words come
        // from a file whose last bytes make no sense
        if (StoppedLine(source, error, network) is { } line && (code != 0 || !await IsWholeAsync(info, outputs, ct).ConfigureAwait(false))) throw Stopped(line, source, code);
        if (code != 0) throw new LocalEncodeException("ffmpeg_failed", $"ffmpeg stopped ({encoder}, {code.ToString(CultureInfo.InvariantCulture)}): " + Why(error, source));
    }

    /// <summary>Nothing is missing of any rung.</summary>
    private async Task<bool> IsWholeAsync(SourceInfo info, IReadOnlyList<string> outputs, CancellationToken ct)
    {
        if (info.Seconds <= 0) return false;
        try
        {
            foreach (var output in outputs)
            {
                if (info.Missing(await ProbeAsync(output, ct).ConfigureAwait(false)) is not null) return false;
            }
            return true;
        }
        catch (LocalEncodeException)
        {
            // Not even a file
            return false;
        }
    }

    /// <summary>
    /// One decode split to every rung, scaled with bicubic to the rung's size, a keyframe every GOP from the start
    /// (as the server's <c>-force_key_frames</c>), AAC stereo at the rung's rate when there is sound.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings)
    {
        var format = H264Encoders.InputFormat(encoder);
        // Every rung is SDR BT.709 and says so, whatever the source (Colour.cs)
        var sdr = rungs.Count == 0 ? "" : SdrPlan.For(info.Colour).Filters(rungs[^1].Width, rungs[^1].Height, HdrCubes.FileName);
        var split = $"[0:v]{sdr}{(sdr.Length > 0 ? "," : "")}split={rungs.Count}" + string.Concat(rungs.Select((_, i) => $"[v{i}]"));
        var scales = rungs.Select((r, i) => $"[v{i}]scale=w={r.Width}:h={r.Height}:flags=bicubic,format={format},{SdrPlan.End}[v{i}o]");
        // Its warnings and errors, each with its level: the encoders' closing figures are no reason for anything
        var args = new List<string> { "-hide_banner", "-loglevel", "level+warning", "-nostdin", "-y", "-i", source, "-filter_complex", string.Join(";", new[] { split }.Concat(scales)) };
        var fps = EncodeLadder.CappedFps(info, settings);
        var outFps = fps ?? (info.Fps > 0 ? info.Fps : 30);
        var keyint = Math.Max(1, (int)Math.Round(outFps * settings.GopSeconds));
        var gop = settings.GopSeconds.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < rungs.Count; i++)
        {
            var r = rungs[i];
            args.AddRange(new[] { "-map", $"[v{i}o]" });
            if (info.HasAudio) args.AddRange(new[] { "-map", "0:a:0" });
            args.AddRange(new[] { "-c:v:0", encoder });
            args.AddRange(new[] { "-color_range:v:0", "tv", "-colorspace:v:0", "bt709", "-color_trc:v:0", "bt709", "-color_primaries:v:0", "bt709" });
            args.AddRange(H264Encoders.Options(encoder, r, settings.Video.Crf, keyint, 0));
            if (fps is { } f) args.AddRange(new[] { "-r:v:0", f.ToString(CultureInfo.InvariantCulture) });
            args.AddRange(new[] { "-force_key_frames:v:0", $"expr:gte(t,n_forced*{gop})" });
            if (info.HasAudio) args.AddRange(new[] { "-c:a:0", "aac", "-b:a:0", r.Audio.ToString(CultureInfo.InvariantCulture), "-ac:a:0", "2" });
            args.AddRange(new[] { "-movflags", "+faststart", outputs[i] });
        }
        args.AddRange(new[] { "-progress", "pipe:1", "-nostats" });
        return args;
    }

    private static double Number(JsonNode? node) => node switch
    {
        null => 0,
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
        _ => 0,
    };

    private static double Rate(string? fraction)
    {
        if (string.IsNullOrEmpty(fraction)) return 0;
        var parts = fraction.Split('/');
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0) return n / d;
        return double.TryParse(fraction, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>
    /// ffmpeg's own words for a read that failed half way. It ends well all the same (exit code 0, a short output), so its
    /// messages count; Windows' network errors reach it as "Invalid argument", which is no read error on a local disk.
    /// </summary>
    private static readonly Regex ReadError = new(
        @"(Input/output error|Stale (NFS )?file handle|timed out|Host is down|No route to host|Network is (down|unreachable)|Socket is not connected|No such device|Device not configured|Unknown error)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex NetworkReadError = new("(Invalid argument|Bad file descriptor|Permission denied)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex NoMedia = new("(Invalid data found when processing input|moov atom not found|EBML header parsing failed)", RegexOptions.CultureInvariant);

    internal static bool IsReading(string line) => Reading.IsMatch(line);

    internal static bool IsError(string line) => line.Contains("[error]", StringComparison.Ordinal) || line.Contains("[fatal]", StringComparison.Ordinal) || line.Contains("[panic]", StringComparison.Ordinal);

    /// <summary>The lines that are about reading the source, not about an output.</summary>
    private static readonly Regex Reading = new("(Error during demuxing|Error reading|Error opening input|Error retrieving a packet)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static bool SourceStopped(string source, string error, bool network) => StoppedLine(source, error, network) is not null;

    /// <summary>The line that says the source could not be read.</summary>
    internal static string? StoppedLine(string source, string error, bool network) => Lines(error).FirstOrDefault(line =>
        (Reading.IsMatch(line) || line.Contains(source + ":", StringComparison.Ordinal)) && (ReadError.IsMatch(line) || (network && NetworkReadError.IsMatch(line))));

    private async Task<LocalEncodeException?> SourceStoppedAsync(string source, string error, int code, CancellationToken ct)
    {
        var network = await _source.IsNetworkAsync(source, ct).ConfigureAwait(false);
        // On a local disk only a run that failed is one that could not read
        return (network || code != 0) && StoppedLine(source, error, network) is { } line ? Stopped(line, source, code) : null;
    }

    private static LocalEncodeException Stopped(string line, string source, int code) =>
        new("source_read", $"ffmpeg could not read the file to its end ({code.ToString(CultureInfo.InvariantCulture)}): " + Shown(line, source));

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static readonly Regex Level = new(@"\[(panic|fatal|error|warning|info|verbose|debug)\] ?", RegexOptions.CultureInvariant);

    /// <summary>
    /// Why ffmpeg failed: its last error line but for what it says after every failure; else its last line. (The encoders' closing
    /// figures come last of all, also when everything went well.)
    /// </summary>
    internal static string Why(string text, string source)
    {
        var lines = Lines(text);
        var said = lines.LastOrDefault(l => IsError(l) && !l.Contains("Conversion failed", StringComparison.Ordinal))
            ?? lines.LastOrDefault() ?? "";
        return Shown(said, source);
    }

    /// <summary>A line of ffmpeg's, without its level and with the file's name in place of its path (it may be reported).</summary>
    private static string Shown(string line, string source)
    {
        line = Level.Replace(line, "", 1).Replace(source, Path.GetFileName(source), StringComparison.Ordinal);
        return line.Length > 300 ? line[..300] : line;
    }

    public async Task ExtractAudioAsync(string source, string wavPath, CancellationToken ct)
    {
        var (ffmpeg, _) = await Paths(ct).ConfigureAwait(false);
        var (code, _, error) = await RunAsync(ffmpeg, new[] { "-nostdin", "-y", "-v", "error", "-i", source, "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wavPath }, null, ct).ConfigureAwait(false);
        var stopped = await SourceStoppedAsync(source, error, code, ct).ConfigureAwait(false);
        if (code == 0 && stopped is null) return;
        try { File.Delete(wavPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (stopped is not null) throw stopped;
        // "-map 0:a:0" matches nothing in a file without sound
        throw new LocalEncodeException(error.Contains("matches no streams", StringComparison.Ordinal) ? "no_audio" : "ffmpeg_failed", "ffmpeg could not read the file's sound: " + Why(error, source));
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(string exe, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct, string? folder = null)
    {
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, UseShellExecute = false, CreateNoWindow = true };
        if (folder is not null) start.WorkingDirectory = folder;
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        var error = new FfmpegMessages();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (onLine is not null) onLine(e.Data);
            else output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) error.Add(e.Data);
        };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new LocalEncodeException("ffmpeg_missing", $"{Path.GetFileName(exe)} could not be started: {ex.Message}", ex);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                // Gone, with its files let go, before the step says it has stopped
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or AggregateException or TimeoutException)
            {
                // It had already ended, or is ending
            }
            throw;
        }
        return (process.ExitCode, output.ToString(), error.ToString());
    }
}

/// <summary>
/// What ffmpeg says, which may be a lot: its end, and apart from that what its warnings must not push out, the lines about
/// reading a file and its errors (the first and the last of them).
/// </summary>
internal sealed class FfmpegMessages
{
    private const int Most = 100;
    private readonly List<string> _reading = new();
    private readonly List<string> _first = new();
    private readonly Queue<string> _last = new();
    private readonly StringBuilder _end = new();

    public void Add(string line)
    {
        // Added on one thread, asked for after the end
        lock (_end)
        {
            if (FfmpegMediaTools.IsReading(line) && _reading.Count < Most) _reading.Add(line);
            else if (FfmpegMediaTools.IsError(line))
            {
                if (_first.Count < Most) _first.Add(line);
                else
                {
                    _last.Enqueue(line);
                    if (_last.Count > Most) _last.Dequeue();
                }
            }
            if (_end.Length > 64_000) _end.Remove(0, 32_000);
            _end.AppendLine(line);
        }
    }

    public override string ToString()
    {
        lock (_end) return string.Concat(_reading.Concat(_first).Concat(_last).Select(line => line + "\n")) + _end;
    }
}

/// <summary>
/// The free space the encodes of a queue share: each says how much it will write in all, and what the others have yet to write
/// is not free.
/// </summary>
internal sealed class SpaceReservations
{
    private readonly Func<string, long?> _free;
    private readonly long _margin;
    private readonly Dictionary<string, long> _taken = new();

    /// <param name="margin">Left free whatever is written: the queue itself is saved there too</param>
    public SpaceReservations(Func<string, long?> free, long margin) => (_free, _margin) = (free, margin);

    public enum Answer
    {
        Yes,
        /// <summary>Once the others are done</summary>
        Later,
        No,
    }

    /// <summary>Takes (or changes) what the folder will hold in all.</summary>
    public Answer Take(string directory, long bytes)
    {
        lock (_taken)
        {
            if (_free(directory) is { } free)
            {
                free -= _margin;
                var needed = Left(directory, bytes);
                if (free < needed) return Answer.No;
                if (free - _taken.Where(t => t.Key != directory).Sum(t => Left(t.Key, t.Value)) < needed) return Answer.Later;
            }
            _taken[directory] = bytes;
            return Answer.Yes;
        }
    }

    public void Give(string directory)
    {
        lock (_taken) _taken.Remove(directory);
    }

    /// <summary>What of it is not written yet.</summary>
    private static long Left(string directory, long bytes)
    {
        try
        {
            var folder = new DirectoryInfo(directory);
            return folder.Exists ? Math.Max(0, bytes - folder.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)) : bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return bytes;
        }
    }
}

/// <summary>
/// Encodes the server's HLS ladder on this computer (wiki TOOLS-ADMIN TA-12), unless the file was left to the
/// server ("Encode on this computer" off) or the server does not take renditions for the file (its delivery target is not R2). Opens the file on
/// admin-api first, so a file the channel already has is never encoded. A local failure is not the file's failure:
/// the step logs why, reports it, and the server encodes the file as it would any upload.
/// </summary>
public sealed class EncodeStep : IImportStep
{
    private readonly ImportApiClient _api;
    private readonly Uri _tusEndpoint;
    private readonly IMediaTools _media;
    private readonly IRenditionPackager _packager;
    private readonly string _workRoot;
    private readonly string _encoderName;
    private readonly Action<Exception, QueueItem>? _report;
    private readonly SpaceReservations _space;
    private readonly SourceAccess _source;
    private EncodingSettings? _settings;

    /// <summary>The copy of a file that could not be read from its share: this and the file's own extension</summary>
    internal const string CopyName = "source";

    /// <summary>Left free on the disk, whatever is made here</summary>
    internal const long SpaceMargin = 512L << 20;

    /// <summary>How long all of it may take: a slow computer, and a share that answers now and then, are no reason to hold a file for days.</summary>
    internal Func<double, long, TimeSpan> Limit { get; init; } = (seconds, bytes) =>
        TimeSpan.FromHours(1) + TimeSpan.FromSeconds(Math.Min(20 * 86_400, (Math.Max(0, seconds) * 20) + (bytes / (double)(1 << 20))));

    /// <summary>How often another encode's space is looked at again</summary>
    internal TimeSpan SpacePause { get; init; } = TimeSpan.FromSeconds(5);

    public EncodeStep(ImportApiClient api, Uri videosApi, IMediaTools media, IRenditionPackager packager, string workRoot, string encoderName, Action<Exception, QueueItem>? report = null, Func<string, long?>? freeSpace = null, SourceAccess? source = null)
    {
        _source = source ?? SourceAccess.Default;
        _api = api;
        _tusEndpoint = new Uri(videosApi, "/api/videos/tus");
        _media = media;
        _packager = packager;
        _workRoot = workRoot;
        _encoderName = encoderName;
        _report = report;
        _space = new SpaceReservations(freeSpace ?? FreeSpace, SpaceMargin);
    }

    public ImportStepKind Kind => ImportStepKind.Encode;

    public static string DefaultWorkRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNowTools", "renditions");

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        // The choice this run was started for: one made meanwhile starts it again (ImportQueue)
        var server = item.EncodeRun ?? item.ServerEncodes;
        // Left to the server after all, or of another file that was in this one's place
        if (server) item.Renditions = null;
        if (item.Renditions is null) WorkFiles.DeleteFolder(Path.Combine(_workRoot, item.Id), _workRoot, item.Id);
        if (server)
        {
            context.Log(LogLevel.Info, "server_encode_chosen", "Let the server encode was chosen: the server encodes this file");
            return StepResult.Skipped;
        }
        // Encoded before the app stopped: kept while every file is still there
        if (item.Renditions is { } done && (done.Sent || done.Files.All(f => File.Exists(Path.Combine(done.Directory, f.Path))))) return StepResult.Done;
        if (item.Sha256 is null || item.FingerprintHash is null) throw new StepFailedException("not_hashed", "The file has not been checked yet", retryable: false);
        await _source.CheckUnchangedAsync(item.Video, context, ct).ConfigureAwait(false);
        // Hashed by a version that did not check: before anything is opened on the server
        await VideoCheck.RunAsync(item, _source, _media, context, ct).ConfigureAwait(false);
        if (item.ServerItemId is null || item.TusSessionId is null)
        {
            if (!await UploadStep.OpenAsync(_api, _tusEndpoint, item, context, ct).ConfigureAwait(false)) return StepResult.SkipRest;
        }
        var delivery = await _api.GetDeliveryAsync(item.ServerItemId!, ct).ConfigureAwait(false);
        if (!delivery.TakesRenditions)
        {
            context.Log(LogLevel.Info, "server_encodes_target", "This channel's videos are encoded by its delivery service: nothing to encode here");
            return StepResult.Skipped;
        }
        if (delivery.R2 is not null)
        {
            context.Log(LogLevel.Info, "server_encodes_started", "The server already started encoding this file: nothing to encode here");
            return StepResult.Skipped;
        }
        _settings ??= await _api.GetEncodingAsync(null, ct).ConfigureAwait(false);
        var settings = _settings;

        var directory = Path.Combine(_workRoot, item.Id);
        try
        {
            WorkFiles.DeleteFolder(directory, _workRoot, item.Id);
            var (rungs, outputs, encoder) = await MakeAsync(item, settings, directory, context, ct).ConfigureAwait(false);
            for (var i = 0; i < rungs.Count; i++)
            {
                await _packager.PackageAsync(outputs[i], Path.Combine(directory, rungs[i].Name), settings.Ladder.SegmentSeconds, ct).ConfigureAwait(false);
                File.Delete(outputs[i]);
                context.Progress(0.9 + (0.1 * (i + 1) / rungs.Count));
            }

            var files = ListFiles(directory, rungs.Select(r => r.Name).ToList());
            var bytes = files.Sum(f => f.Bytes);
            var limits = settings.PrebuiltLimits;
            if (files.Count > limits.MaxFiles || bytes > limits.MaxTotalBytes || files.Any(f => f.Bytes > limits.MaxFileBytes))
            {
                throw new LocalEncodeException("too_large", "The renditions are larger than the server takes");
            }
            item.Renditions = new RenditionSet
            {
                Directory = directory,
                Rungs = rungs.Select(r => r.Name).ToList(),
                Files = files.ToList(),
                Bytes = bytes,
                Encoder = $"{_encoderName}, ffmpeg {encoder}",
            };
            context.Save();
            var size = (bytes / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture);
            // Args: rungs, encoded bytes, the original file's bytes (shown beside each other)
            context.Log(LogLevel.Info, "encoded", $"Encoded {item.Renditions.Rungs.Count} rungs here ({size} MB); they go with the upload", item.Renditions.Rungs.Count.ToString(CultureInfo.InvariantCulture), bytes.ToString(CultureInfo.InvariantCulture), item.Video.Size.ToString(CultureInfo.InvariantCulture));
            return StepResult.Done;
        }
        catch (LocalEncodeException ex) when (ex.Code is "no_video" or "no_media")
        {
            // The server would find the same once everything was sent: refused as when it is checked
            WorkFiles.DeleteFolder(directory, _workRoot, item.Id);
            if (ex.Code == "no_media") context.Log(LogLevel.Warning, "unreadable", $"ffprobe cannot read it: {ex.Message}", ex.Message);
            throw ex.Code == "no_video"
                ? new StepFailedException("no_video", "The file has sound and no video", retryable: false, ex)
                : new StepFailedException("not_video", "The file cannot be read as a video: it may have been cut off while it was copied", retryable: false, ex);
        }
        catch (Exception ex) when (ex is OperationCanceledException || ct.IsCancellationRequested)
        {
            // Stopped (paused, removed or the app closing), whatever it stopped with: encoded from the start next time
            WorkFiles.DeleteFolder(directory, _workRoot, item.Id);
            throw;
        }
        catch (Exception ex) when (ex is SourceReadException or StepFailedException)
        {
            // The file itself cannot be read: the upload could not either
            WorkFiles.DeleteFolder(directory, _workRoot, item.Id);
            throw;
        }
        catch (Exception ex) when (ex is LocalEncodeException or IOException or UnauthorizedAccessException)
        {
            // Not the file's failure: the server encodes it as any upload
            WorkFiles.DeleteFolder(directory, _workRoot, item.Id);
            item.Renditions = null;
            context.Save();
            var code = ex is LocalEncodeException local ? local.Code : "disk";
            var why = ImportQueue.Describe(ex);
            context.Log(LogLevel.Warning, "encode_failed", $"Could not encode here: {why}. The server encodes this file", code, why);
            _report?.Invoke(ex, item);
            return StepResult.Skipped;
        }
        finally
        {
            _space.Give(directory);
        }
    }

    /// <summary>What is made of a source: what it is, the rungs, their MP4s and the encoder.</summary>
    private sealed record Plan(SourceInfo Info, IReadOnlyList<PlannedRung> Rungs, IReadOnlyList<string> Outputs, string Encoder)
    {
        /// <summary>Room for the rungs at their peak rates, and the same again for the MP4s before packaging.</summary>
        public long Bytes => Estimate(Info, Rungs);
    }

    internal static long Estimate(SourceInfo info, IReadOnlyList<PlannedRung> rungs) =>
        (long)(rungs.Sum(r => (double)(r.Maxrate + r.Audio)) / 8 * Math.Max(1, info.Seconds) * 2.2);

    /// <summary>Every rung's MP4, as whole as the source: read from where the file is, else from a copy of it.</summary>
    private async Task<(IReadOnlyList<PlannedRung> Rungs, IReadOnlyList<string> Outputs, string Encoder)> MakeAsync(QueueItem item, EncodingSettings settings, string directory, IStepContext context, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var time = limit.Token;
        var video = item.Video;
        async Task<Plan> PlanAsync(SourceInfo info)
        {
            var rungs = EncodeLadder.For(info, settings.Ladder);
            // What cannot be shown right, or not as the server would, is the server's: before anything is encoded
            if (SdrPlan.For(info.Colour) is { IsHdr: true } hdr) HdrCubes.For(hdr, settings.Ladder);
            var encoder = await _media.PickEncoderAsync(time).ConfigureAwait(false)
                ?? throw new LocalEncodeException("ffmpeg_failed", "This ffmpeg has no H.264 encoder");
            return new Plan(info, rungs, rungs.Select(r => Path.Combine(directory, r.Name + ".mp4")).ToList(), encoder);
        }
        async Task<bool> EncodeAsync(string path, Plan made)
        {
            // 90% for the encode, 10% for packaging
            await _media.EncodeAsync(path, made.Info, made.Rungs, made.Outputs, made.Encoder, settings.Ladder, new DirectProgress(p => context.Progress(p * 0.9)), time).ConfigureAwait(false);
            return true;
        }
        void Start(Plan made)
        {
            System.IO.Directory.CreateDirectory(directory);
            var names = string.Join(", ", made.Rungs.Select(r => r.Name));
            context.Log(LogLevel.Info, "encode_plan", $"Encoding {names} with {made.Encoder}", made.Encoder, names);
            if (SdrPlan.For(made.Info.Colour) is { IsHdr: true } hdr)
            {
                var kind = hdr.Kind == SdrPlan.Hlg ? "HLG" : "PQ";
                context.Log(LogLevel.Info, "encode_hdr", $"HDR ({kind}), converted to standard colors", kind);
            }
        }
        Plan? plan = null;
        try
        {
            try
            {
                var there = plan = await PlanAsync(await _source.ProbeAsync(_media, video.Path, context, time).ConfigureAwait(false)).ConfigureAwait(false);
                await RoomAsync(directory, there.Bytes, context, time).ConfigureAwait(false);
                limit.CancelAfter(Limit(there.Info.Seconds, video.Size));
                Start(there);
                await _source.WithPathAsync(video.Path, context, p => EncodeAsync(p, there), time, tries: 2).ConfigureAwait(false);
            }
            catch (LocalEncodeException stopped) when (stopped.Code == "source_read")
            {
                // ffmpeg ends at a share's first hiccup; the app's own reading, which the check and the upload use, goes on after it
                if (!await _source.IsNetworkAsync(video.Path, time).ConfigureAwait(false)) throw;
                // What those runs left is in the way
                foreach (var output in plan?.Outputs ?? Array.Empty<string>()) File.Delete(output);
                if (_space.Take(directory, video.Size + (plan?.Bytes ?? 0)) != SpaceReservations.Answer.Yes) throw;
                if (plan is null) limit.CancelAfter(Limit(0, video.Size));
                context.Log(LogLevel.Info, "encode_copy", $"{stopped.Message}. Copied here first, and encoded from the copy", stopped.Message);
                System.IO.Directory.CreateDirectory(directory);
                var copy = await CopyAsync(item, directory, context, time).ConfigureAwait(false);
                var read = plan is not null;
                plan = await PlanAsync(await _media.ProbeAsync(copy, time).ConfigureAwait(false)).ConfigureAwait(false);
                if (_space.Take(directory, video.Size + plan.Bytes) != SpaceReservations.Answer.Yes) throw NoRoom(plan.Bytes);
                if (!read)
                {
                    limit.CancelAfter(Limit(plan.Info.Seconds, video.Size));
                    Start(plan);
                }
                await EncodeAsync(copy, plan).ConfigureAwait(false);
                File.Delete(copy);
                _space.Take(directory, plan.Bytes);
            }
            await _source.CheckUnchangedAsync(video, context, time, read: true).ConfigureAwait(false);
            // A read that stops half way may end ffmpeg well, with a short video or without its sound
            foreach (var output in plan.Outputs)
            {
                SourceInfo made;
                try
                {
                    made = await _media.ProbeAsync(output, time).ConfigureAwait(false);
                }
                catch (LocalEncodeException unreadable)
                {
                    // That is what was made, not the file
                    throw new LocalEncodeException("ffmpeg_failed", "The encoded video cannot be read: " + unreadable.Message, unreadable);
                }
                if (plan.Info.Missing(made) is { } missing) throw new LocalEncodeException("too_short", missing);
                if (made.Colour is { } colours && SdrPlan.For(plan.Info.Colour).Wrong(colours) is { } wrong) throw new LocalEncodeException("colour_tags", wrong);
            }
            return (plan.Rungs, plan.Outputs, plan.Encoder);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && limit.IsCancellationRequested)
        {
            throw new LocalEncodeException("took_too_long", "Encoding here took too long");
        }
    }

    private static LocalEncodeException NoRoom(long bytes) => new("disk_space", $"Not enough free space for the renditions (about {bytes / 1048576} MB needed)");

    /// <summary>Takes the space of an encode, waiting for the others' when it is only theirs that is in the way.</summary>
    private async Task RoomAsync(string directory, long bytes, IStepContext context, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                switch (_space.Take(directory, bytes))
                {
                    case SpaceReservations.Answer.Yes: return;
                    case SpaceReservations.Answer.No: throw NoRoom(bytes);
                }
                context.Waiting(StepWait.Space);
                await Task.Delay(SpacePause, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Waiting(StepWait.None);
        }
    }

    /// <summary>The rung folders' files in the server's order: playlist, init, then the segments.</summary>
    public static IReadOnlyList<RenditionFile> ListFiles(string root, IReadOnlyList<string> rungs)
    {
        var files = new List<RenditionFile>();
        foreach (var rung in rungs)
        {
            var dir = new DirectoryInfo(Path.Combine(root, rung));
            if (!dir.Exists) throw new LocalEncodeException("package_failed", $"No files were written for {rung}");
            foreach (var f in dir.EnumerateFiles().OrderBy(f => f.Name == "index.m3u8" ? 0 : f.Name == "init.mp4" ? 1 : 2).ThenBy(f => f.Name, StringComparer.Ordinal))
            {
                files.Add(new RenditionFile($"{rung}/{f.Name}", f.Length));
            }
        }
        return files;
    }

    /// <summary>
    /// The source, read as the upload reads it, beside what is made of it (and deleted with it). A share may read differently the
    /// second time: it is the file that was checked, to the byte, or nothing is made of it.
    /// </summary>
    private async Task<string> CopyAsync(QueueItem item, string directory, IStepContext context, CancellationToken ct)
    {
        var video = item.Video;
        var path = Path.Combine(directory, CopyName + Path.GetExtension(video.Path));
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        context.Copying(true);
        context.Progress(0);
        try
        {
            var from = _source.Open(video.Path, video.Size, video.LastModifiedMs, context);
            await using (from.ConfigureAwait(false))
            {
                var to = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
                await using (to.ConfigureAwait(false))
                {
                    var buffer = new byte[1 << 20];
                    long done = 0;
                    int n;
                    while ((n = await from.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        hash.AppendData(buffer, 0, n);
                        await to.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                        done += n;
                        context.Progress(video.Size == 0 ? 1 : (double)done / video.Size);
                    }
                }
            }
        }
        finally
        {
            context.Copying(false);
            context.Progress(0);
        }
        if (Convert.ToHexStringLower(hash.GetHashAndReset()) != item.Sha256)
        {
            await _source.CheckUnchangedAsync(video, context, ct, read: true).ConfigureAwait(false);
            throw new LocalEncodeException("copy_differs", "What was read to be copied here is not what was checked before");
        }
        return path;
    }

    private static long? FreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Sends a file's renditions to the server before its original completes (the server takes them only until its
/// transcode starts): stage, send each file, complete. After a restart, only what the server says is missing goes
/// again. When the server cannot take them (too late, or it refuses the plan) they are dropped and the server encodes.
/// </summary>
public sealed class RenditionSender
{
    private const int Parallel = 4;
    private readonly ImportApiClient _api;
    private readonly string _workRoot;

    public RenditionSender(ImportApiClient api, string? workRoot = null)
    {
        _api = api;
        _workRoot = workRoot ?? EncodeStep.DefaultWorkRoot;
    }

    /// <param name="progress">0–1 over the renditions' bytes.</param>
    public async Task SendAsync(QueueItem item, IStepContext context, IProgress<double> progress, CancellationToken ct)
    {
        var set = item.Renditions!;
        var restaged = false;
        while (true)
        {
            try
            {
                await SendOnceAsync(item, set, context, progress, ct).ConfigureAwait(false);
                return;
            }
            catch (ImportApiException gone) when (gone.Code == "staging_not_found" && !restaged)
            {
                // Replaced or too old on the server: stage again and send everything
                restaged = true;
                set.StagingId = null;
                set.FirstPassDone = false;
                context.Save();
            }
            catch (ImportApiException refused) when (refused.Code is "too_late" or "not_uploading" || (refused.Status == 400 && !refused.IsRetryable))
            {
                Drop(item, context, refused.Code, refused.Message);
                return;
            }
        }
    }

    private async Task SendOnceAsync(QueueItem item, RenditionSet set, IStepContext context, IProgress<double> progress, CancellationToken ct)
    {
        if (set.Files.Any(f => !File.Exists(Path.Combine(set.Directory, f.Path))))
        {
            if (set.StagingId is { } stale) await _api.CancelRenditionsAsync(item.ServerItemId!, stale, ct).ConfigureAwait(false);
            Drop(item, context, "local_files_missing", "The encoded files are no longer on this computer");
            return;
        }
        if (set.StagingId is null)
        {
            var staging = await _api.StageRenditionsAsync(item.ServerItemId!, set.Rungs, set.Files, set.Encoder, ct).ConfigureAwait(false);
            set.StagingId = staging.StagingId;
            set.FirstPassDone = false;
            context.Save();
            context.Log(LogLevel.Info, "renditions_staged", $"Sending the renditions ({set.Files.Count} files)", set.Files.Count.ToString(CultureInfo.InvariantCulture), set.Bytes.ToString(CultureInfo.InvariantCulture));
        }
        var sent = 0L;
        if (!set.FirstPassDone)
        {
            await PutAllAsync(item, set, set.Files, b => progress.Report(set.Bytes == 0 ? 1 : (double)Interlocked.Add(ref sent, b) / set.Bytes), ct).ConfigureAwait(false);
            set.FirstPassDone = true;
            context.Save();
        }
        // The server lists what it does not have, at most 50 at a time
        for (var round = 0; round < 1000; round++)
        {
            var missing = await _api.CompleteRenditionsAsync(item.ServerItemId!, set.StagingId!, ct).ConfigureAwait(false);
            if (missing.Count == 0)
            {
                set.Sent = true;
                set.SentAt = DateTimeOffset.UtcNow;
                WorkFiles.DeleteFolder(set.Directory, _workRoot, item.Id);
                context.Save();
                progress.Report(1);
                context.Log(LogLevel.Info, "renditions_sent", "The server has the renditions; it checks them when it processes the video");
                return;
            }
            var again = set.Files.Where(f => missing.Contains(f.Path)).ToList();
            if (again.Count == 0) throw new StepFailedException("renditions_unknown", "The server is missing files that were not encoded here", retryable: false);
            await PutAllAsync(item, set, again, _ => { }, ct).ConfigureAwait(false);
        }
        throw new StepFailedException("renditions_incomplete", "The server kept missing rendition files", retryable: true);
    }

    private async Task PutAllAsync(QueueItem item, RenditionSet set, IReadOnlyList<RenditionFile> files, Action<long> sent, CancellationToken ct)
    {
        await System.Threading.Tasks.Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Parallel, CancellationToken = ct }, async (file, token) =>
        {
            await using var stream = new FileStream(Path.Combine(set.Directory, file.Path), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous);
            await _api.PutRenditionFileAsync(item.ServerItemId!, set.StagingId!, file.Path, stream, file.Bytes, token).ConfigureAwait(false);
            sent(file.Bytes);
        }).ConfigureAwait(false);
    }

    private void Drop(QueueItem item, IStepContext context, string code, string reason)
    {
        WorkFiles.DeleteFolder(item.Renditions?.Directory, _workRoot, item.Id);
        item.Renditions = null;
        context.Save();
        context.Log(LogLevel.Warning, "renditions_dropped", $"The server did not take the renditions ({reason}); it encodes this file itself", code, reason);
    }
}

/// <summary>
/// Waits, without holding a slot, for the server's transcode of a file whose renditions it took, and records what it
/// did: used them as they are, or encoded again and why (shared-types PREBUILT_REJECTIONS). Gives up waiting after a day.
/// </summary>
public sealed class CheckStep : IImportStep
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(24);
    private readonly ImportApiClient _api;
    private readonly TimeProvider _time;

    public CheckStep(ImportApiClient api, TimeProvider? time = null)
    {
        _api = api;
        _time = time ?? TimeProvider.System;
    }

    public ImportStepKind Kind => ImportStepKind.Check;

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        if (item.Renditions is not { Sent: true } set || item.ServerItemId is null) return StepResult.Skipped;
        var delivery = await _api.GetDeliveryAsync(item.ServerItemId, ct).ConfigureAwait(false);
        var r2 = delivery.R2;
        switch (r2?.Status)
        {
            case "ready":
                var outcome = r2.Prebuilt;
                item.Verdict = new RenditionVerdict(outcome?.Used ?? false, outcome is null ? "not_carried" : outcome.Reason, outcome?.Rung);
                if (item.Verdict.Used)
                {
                    context.Log(LogLevel.Info, "renditions_used", "The server checked the renditions and serves them as they are");
                }
                else
                {
                    var reason = item.Verdict.Reason ?? "error";
                    var rung = item.Verdict.Rung ?? "";
                    context.Log(LogLevel.Warning, "renditions_rejected", $"The server encoded this file itself: {reason}{(rung.Length > 0 ? " (" + rung + ")" : "")}", reason, rung);
                }
                return StepResult.Done;
            case "failed" or "cancelled":
                item.Verdict = new RenditionVerdict(false, null, null, TranscodeFailed: true);
                context.Log(LogLevel.Warning, "transcode_failed", $"The server's processing of this file stopped: {r2.Error ?? r2.Status}. Videos admin shows it with Retry", r2.Error ?? r2.Status);
                return StepResult.Done;
        }
        var waited = _time.GetUtcNow() - (set.SentAt ?? _time.GetUtcNow());
        if (waited > GiveUpAfter)
        {
            context.Log(LogLevel.Warning, "check_gave_up", "The server has not processed this file after a day; videos admin shows where it is");
            return StepResult.Done;
        }
        // Soon after the upload, then less often
        context.Later(waited < TimeSpan.FromMinutes(10) ? TimeSpan.FromSeconds(20) : TimeSpan.FromMinutes(2));
        return StepResult.Later;
    }
}

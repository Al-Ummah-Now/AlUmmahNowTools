using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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

    /// <summary>ffmpeg_missing, probe_failed, no_video, no_audio, disk_space, ffmpeg_failed, package_failed, too_large or settings_invalid</summary>
    public string Code { get; }
}

/// <summary>A source as ffprobe sees it.</summary>
public sealed record SourceInfo(int Width, int Height, int Rotation, double Fps, bool HasAudio, double DurationSeconds);

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
        if (width <= 0 || height <= 0) throw new LocalEncodeException("no_video", "The file has no picture size");
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

/// <summary>
/// H.264 encoder detection, OS-scoped hardware-first ordering, probing, and fallback rules.
/// <para>
/// Ported from Maktaba (<c>github.com/Hamza-Labs-Core/Maktaba</c>, <c>origin/main</c>; same owner):
/// header-safe <c>ffmpeg -encoders</c> legend skipping (<c>cmd/maktaba-server/internal/ffmpeg/check.go:parseCodecs</c>),
/// OS-scoped hardware-first priority for macOS VideoToolbox, Linux NVENC → QSV → VA-API, and Windows NVENC → QSV
/// (<c>streaming/internal/ffmpeg/hwaccel.go</c>), 0.2 s black-frame null probing (<c>hwaccel.go:encodes</c>),
/// Linux <c>/dev/dri/renderD*</c> render-node discovery for <c>h264_vaapi</c> (<c>streaming/internal/ffmpeg/vaapi.go</c>;
/// Tools relies directly on the ffmpeg test encode as Maktaba does when <c>vainfo</c> is absent),
/// VideoToolbox <c>-allow_sw 0 -realtime 1</c> probe flags (<c>streaming/internal/channel/config.go</c> and <c>cluster/capability.go</c>),
/// and transient session-limit refusal detection + 30 s fallback window (<c>streaming/internal/ffmpeg/transcode.go</c>).
/// </para>
/// <para>
/// Extended in Tools for desktop/LGPL (TA-19) and TA-12 prebuilt parity:
/// Windows <c>h264_amf</c> and <c>h264_mf</c>, <c>libopenh264</c> LGPL software fallback,
/// per-file candidate-chain retry (committing permanent blacklisting only after a fallback succeeds),
/// and local output/segment verification aligned with <c>apps/media/container/prebuilt.mjs</c>.
/// </para>
/// </summary>
public static class H264Encoders
{
    public const string VaapiDevice = "/dev/dri/renderD128";

    /// <summary>
    /// Maktaba <c>transcode.go</c> fallback window: only a hardware failure that dies within 30 s is treated as an
    /// encoder-startup/compatibility failure eligible for local fallback; late failures avoid permanently blacklisting the GPU.
    /// </summary>
    public static readonly TimeSpan FallbackWindow = TimeSpan.FromSeconds(30);

    /// <summary>How long a transiently busy GPU encoder (e.g. NVENC session limit) is skipped before re-probing.</summary>
    public static readonly TimeSpan BusyCooldown = TimeSpan.FromSeconds(30);

    /// <summary>Canonical hardware-first encoder order across all platforms, ending in software encoders.</summary>
    public static readonly string[] Preferred = { "h264_videotoolbox", "h264_nvenc", "h264_qsv", "h264_amf", "h264_vaapi", "h264_mf", "libx264", "libopenh264" };

    public static bool IsHardware(string encoder) => encoder is not ("libx264" or "libopenh264");

    /// <summary>
    /// OS-scoped encoder priority order:
    /// macOS tries VideoToolbox then software (Maktaba <c>hwaccel.go</c>);
    /// Linux tries NVENC, QSV, VA-API then software (Maktaba <c>hwaccel.go</c> + <c>vaapi.go</c>);
    /// Windows tries NVENC, QSV (Maktaba), then AMF, Media Foundation (Tools extensions), then software.
    /// </summary>
    public static IReadOnlyList<string> PreferredForPlatform(string? os = null)
    {
        var kind = (os ?? CurrentOs()).ToLowerInvariant();
        return kind switch
        {
            "darwin" or "osx" or "macos" => new[] { "h264_videotoolbox", "libx264", "libopenh264" },
            "linux" => new[] { "h264_nvenc", "h264_qsv", "h264_vaapi", "libx264", "libopenh264" },
            "windows" => new[] { "h264_nvenc", "h264_qsv", "h264_amf", "h264_mf", "libx264", "libopenh264" },
            _ => Preferred,
        };
    }

    internal static string CurrentOs() =>
        OperatingSystem.IsMacOS() ? "darwin" :
        OperatingSystem.IsLinux() ? "linux" :
        OperatingSystem.IsWindows() ? "windows" : "other";

    /// <summary>
    /// Parses video encoder names from <c>ffmpeg -encoders</c> (skipping legend/header lines before <c>------</c>
    /// when present, matching Maktaba's <c>parseCodecs</c>).
    /// </summary>
    public static HashSet<string> ParseVideoEncoders(string encodersOutput)
    {
        var lines = encodersOutput.Split('\n');
        var hasSeparator = lines.Any(l =>
        {
            var trimmed = l.Trim();
            return trimmed.Length >= 3 && trimmed.All(c => c == '-');
        });
        var started = !hasSeparator;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!started)
            {
                if (trimmed.Length >= 3 && trimmed.All(c => c == '-')) started = true;
                continue;
            }
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // " V....D libx264  libx264 H.264 …": capabilities, then the name
            if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] == 'V') names.Add(parts[1]);
        }
        return names;
    }

    /// <summary>
    /// Returns preferred encoders present in <c>ffmpeg -encoders</c>, filtered by OS and optional
    /// <paramref name="prefer"/> override (<c>auto</c>, <c>software</c>/<c>off</c>/<c>none</c>/<c>libx264</c>, or a specific encoder name,
    /// mirroring Maktaba's <c>MAKTABA_HWACCEL_PREFER</c>).
    /// </summary>
    public static IReadOnlyList<string> Candidates(string encodersOutput, string? os = null, string? prefer = null)
    {
        var names = ParseVideoEncoders(encodersOutput);
        var mode = (prefer ?? "").Trim().ToLowerInvariant();
        if (mode is "software" or "off" or "none" or "cpu" or "libx264")
        {
            return new[] { "libx264", "libopenh264" }.Where(names.Contains).ToList();
        }
        if (mode.Length > 0 && mode != "auto")
        {
            var explicitName = mode switch
            {
                "vt" or "videotoolbox" => "h264_videotoolbox",
                "nvenc" => "h264_nvenc",
                "qsv" => "h264_qsv",
                "amf" => "h264_amf",
                "vaapi" => "h264_vaapi",
                "mf" => "h264_mf",
                "openh264" => "libopenh264",
                _ => mode,
            };
            var list = new List<string>();
            if (names.Contains(explicitName)) list.Add(explicitName);
            foreach (var sw in new[] { "libx264", "libopenh264" })
            {
                if (names.Contains(sw) && !list.Contains(sw, StringComparer.Ordinal)) list.Add(sw);
            }
            return list;
        }
        var order = os is null ? Preferred : PreferredForPlatform(os);
        return order.Where(names.Contains).ToList();
    }

    /// <summary>The first preferred encoder in <c>ffmpeg -encoders</c>' list, or null. VAAPI is opt-in unless <paramref name="vaapiAvailable"/> is true.</summary>
    public static string? Pick(string encodersOutput, bool vaapiAvailable = false) =>
        Candidates(encodersOutput).FirstOrDefault(name => vaapiAvailable || name != "h264_vaapi");

    /// <summary>DRM render nodes on Linux, sorted so <c>renderD128</c> is probed before <c>renderD129</c> on dual-GPU machines.</summary>
    public static IReadOnlyList<string> FindVaapiDevices(string driDirectory = "/dev/dri")
    {
        if (!Directory.Exists(driDirectory)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFileSystemEntries(driDirectory, "renderD*")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Arguments for a short test encode (0.2 s black 256×144 to null, matching Maktaba's <c>hwaccel.go:encodes</c>
    /// and <c>channel/config.go:DetectEncoderFor</c>) that verifies a hardware encoder's device and driver actually work.
    /// VideoToolbox is probed with <c>-allow_sw 0 -realtime 1</c> so a Mac without hardware VideoToolbox falls back to <c>libx264</c>.
    /// </summary>
    public static IReadOnlyList<string> ProbeArguments(string encoder, string? vaapiDevice = null)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error" };
        if (encoder == "h264_vaapi") args.AddRange(new[] { "-vaapi_device", vaapiDevice ?? VaapiDevice });
        args.AddRange(new[]
        {
            "-f", "lavfi", "-i", "color=c=black:s=256x144:r=25:d=0.2",
            "-vf", FilterFormat(encoder),
            "-c:v", encoder,
        });
        if (encoder == "h264_videotoolbox") args.AddRange(new[] { "-allow_sw", "0", "-realtime", "1" });
        args.AddRange(new[] { "-f", "null", "-" });
        return args;
    }

    /// <summary>
    /// True when ffmpeg's stderr indicates a transient GPU session-limit refusal rather than missing hardware
    /// (ported from Maktaba <c>streaming/internal/ffmpeg/transcode.go:refusedSession</c>).
    /// </summary>
    public static bool IsRefusedSession(string stderr)
    {
        var lower = stderr.ToLowerInvariant();
        return lower.Contains("openencodesessionex failed", StringComparison.Ordinal)
            && !lower.Contains("unsupported device", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when ffmpeg's stderr clearly points to a corrupt/missing source or full disk rather than an encoder failure,
    /// so hardware encoders are not permanently blacklisted for the rest of the session.
    /// </summary>
    public static bool IsSourceOrDiskError(string stderr)
    {
        var lower = stderr.ToLowerInvariant();
        return lower.Contains("no space left on device", StringComparison.Ordinal)
            || lower.Contains("disk full", StringComparison.Ordinal)
            || lower.Contains("invalid data found when processing input", StringComparison.Ordinal)
            || lower.Contains("no such file or directory", StringComparison.Ordinal)
            || lower.Contains("permission denied", StringComparison.Ordinal)
            || lower.Contains("moov atom not found", StringComparison.Ordinal)
            || lower.Contains("input/output error", StringComparison.Ordinal);
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
            // The probe uses -allow_sw 0 (hardware-only) so Apple's software VideoToolbox never masks libx264;
            // once hardware VideoToolbox is confirmed, real encodes keep allow_sw 1 to survive transient VT contention.
            "h264_videotoolbox" => new[] { S("b"), average, S("maxrate"), max, S("bufsize"), buf, S("allow_sw"), "1" },
            "h264_nvenc" => new[] { S("rc"), "vbr", S("cq"), crf.ToString(CultureInfo.InvariantCulture), S("b"), average, S("maxrate"), max, S("bufsize"), buf, S("no-scenecut"), "1", S("forced-idr"), "1" },
            "h264_qsv" => new[] { S("b"), average, S("maxrate"), max, S("bufsize"), buf, S("forced_idr"), "1", S("idr_interval"), "0" },
            "h264_amf" => new[] { S("rc"), "vbr_peak", S("b"), average, S("maxrate"), max, S("bufsize"), buf },
            "h264_vaapi" => new[] { S("rc_mode"), "VBR", S("b"), average, S("maxrate"), max, S("bufsize"), buf },
            _ => new[] { S("b"), average, S("maxrate"), max, S("bufsize"), buf },
        };
        return common.Concat(rate);
    }

    /// <summary>The pixel format the encoder takes from the scaler (the stream it writes is 4:2:0 either way).</summary>
    public static string InputFormat(string encoder) => encoder is "h264_qsv" or "h264_vaapi" ? "nv12" : "yuv420p";

    /// <summary>Filter tail after <c>scale</c> for the encoder: VAAPI uploads <c>nv12</c> frames to the hardware surface.</summary>
    public static string FilterFormat(string encoder) => encoder == "h264_vaapi" ? "format=nv12,hwupload" : $"format={InputFormat(encoder)}";
}

/// <summary>Runs ffprobe and ffmpeg.</summary>
public interface IMediaTools
{
    Task<SourceInfo> ProbeAsync(string path, CancellationToken ct);
    /// <summary>The H.264 encoder to use, or null when this ffmpeg has none of the preferred ones.</summary>
    Task<string?> PickEncoderAsync(CancellationToken ct);
    /// <summary>Picks the best working H.264 encoder, skipping any encoders already attempted for the current file.</summary>
    Task<string?> PickEncoderAsync(IReadOnlySet<string>? skipEncoders, CancellationToken ct) => PickEncoderAsync(ct);
    /// <summary>
    /// Marks a hardware encoder unusable for the rest of this session so the next pick falls back.
    /// When <paramref name="transientBusy"/> is true (e.g. NVENC session limit reached, Maktaba <c>refusedSession</c>),
    /// the encoder enters a temporary cooldown without being permanently blacklisted for later files.
    /// </summary>
    void MarkEncoderFailed(string encoder, bool transientBusy = false) { }
    /// <summary>One decode, one progressive MP4 per rung (<paramref name="outputs"/>, in rung order). Progress 0–1.</summary>
    Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct);
    /// <summary>
    /// Verifies each encoded rung MP4 matches the server's structural prebuilt checks (H.264, yuv420p, rung dimensions,
    /// stereo AAC or absent audio on silent sources, duration within slack, keyframe gaps ≤ GOP×2+0.05) before packaging and uploading.
    /// Throws <see cref="LocalEncodeException"/> with code <c>output_invalid</c> on mismatch so hardware encoders fall back locally.
    /// </summary>
    Task VerifyOutputsAsync(IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, bool expectAudio, CancellationToken ct) => Task.CompletedTask;
    /// <summary>Full local output verification including source duration slack and keyframe-gap checks matching <c>prebuilt.mjs:verifyRenditions</c>.</summary>
    Task VerifyOutputsAsync(IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, SourceInfo source, EncodeLadderSettings settings, CancellationToken ct) =>
        VerifyOutputsAsync(rungs, outputs, source.HasAudio, ct);
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
    private readonly TimeProvider _time;
    private readonly string? _osOverride;
    private readonly Func<string?> _preferOverride;
    private readonly Func<IReadOnlyList<string>> _findVaapiDevices;
    private readonly HashSet<string> _failedEncoders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedVaapiDevices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _busyUntil = new(StringComparer.Ordinal);
    private string? _encoder;
    private string? _vaapiDevice;

    public FfmpegMediaTools(Func<CancellationToken, Task<(string Ffmpeg, string Ffprobe)?>> locate, TimeProvider? time = null)
        : this(locate, time, osOverride: null, preferOverride: null, findVaapiDevices: null)
    {
    }

    internal FfmpegMediaTools(
        Func<CancellationToken, Task<(string Ffmpeg, string Ffprobe)?>> locate,
        TimeProvider? time,
        string? osOverride,
        Func<string?>? preferOverride,
        Func<IReadOnlyList<string>>? findVaapiDevices)
    {
        _locate = locate;
        _time = time ?? TimeProvider.System;
        _osOverride = osOverride;
        _preferOverride = preferOverride ?? (() => Environment.GetEnvironmentVariable("TOOLS_ENCODER"));
        _findVaapiDevices = findVaapiDevices ?? (() => H264Encoders.FindVaapiDevices());
    }

    private async Task<(string Ffmpeg, string Ffprobe)> Paths(CancellationToken ct) =>
        await _locate(ct).ConfigureAwait(false) ?? throw new LocalEncodeException("ffmpeg_missing", "ffmpeg was not found on this computer");

    public async Task<SourceInfo> ProbeAsync(string path, CancellationToken ct)
    {
        var (_, ffprobe) = await Paths(ct).ConfigureAwait(false);
        var (code, output, error) = await RunAsync(ffprobe, new[] { "-v", "error", "-print_format", "json", "-show_streams", "-show_format", path }, null, ct).ConfigureAwait(false);
        if (code != 0) throw new LocalEncodeException("probe_failed", "ffprobe could not read the file: " + LastLine(error));
        return ParseProbe(output);
    }

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
        if (video is null) throw new LocalEncodeException("no_video", "The file has no video track");
        var rotation = 0;
        foreach (var side in video["side_data_list"]?.AsArray() ?? new JsonArray())
        {
            if (side?["rotation"] is JsonNode r) rotation = (int)Math.Round(Number(r));
        }
        if (rotation == 0 && video["tags"]?["rotate"] is JsonNode rotate) rotation = (int)Math.Round(Number(rotate));
        var fps = Rate(video["avg_frame_rate"]?.GetValue<string>());
        if (fps <= 0) fps = Rate(video["r_frame_rate"]?.GetValue<string>());
        var duration = root?["format"]?["duration"] is JsonNode d ? Number(d) : video["duration"] is JsonNode vd ? Number(vd) : 0;
        return new SourceInfo(
            (int)Number(video["width"]),
            (int)Number(video["height"]),
            rotation,
            fps,
            streams.Any(s => s?["codec_type"]?.GetValue<string>() == "audio"),
            duration);
    }

    public Task<string?> PickEncoderAsync(CancellationToken ct) => PickEncoderAsync(null, ct);

    public async Task<string?> PickEncoderAsync(IReadOnlySet<string>? skipEncoders, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        foreach (var expired in _busyUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
        {
            _busyUntil.Remove(expired);
        }

        if (_encoder is not null
            && !_failedEncoders.Contains(_encoder)
            && !_busyUntil.ContainsKey(_encoder)
            && (skipEncoders is null || !skipEncoders.Contains(_encoder)))
        {
            return _encoder;
        }

        var (ffmpeg, _) = await Paths(ct).ConfigureAwait(false);
        var (_, output, _) = await RunAsync(ffmpeg, new[] { "-hide_banner", "-encoders" }, null, ct).ConfigureAwait(false);
        var os = _osOverride ?? H264Encoders.CurrentOs();
        var prefer = _preferOverride();
        var vaapiDevices = os == "linux"
            ? _findVaapiDevices().Where(d => !_failedVaapiDevices.Contains(d)).ToList()
            : (IReadOnlyList<string>)Array.Empty<string>();
        var transientSkip = new HashSet<string>(_busyUntil.Keys, StringComparer.Ordinal);
        if (skipEncoders is not null) transientSkip.UnionWith(skipEncoders);

        var sawBusyOrSkipped = false;
        var (encoder, device) = await SelectWorkingEncoderAsync(
            H264Encoders.Candidates(output, os, prefer),
            _failedEncoders,
            transientSkip,
            vaapiDevices,
            async (candidate, vaapiDevice, token) =>
            {
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                probeCts.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var (code, _, err) = await RunAsync(ffmpeg, H264Encoders.ProbeArguments(candidate, vaapiDevice), null, probeCts.Token).ConfigureAwait(false);
                    if (code == 0) return (true, false);
                    if (H264Encoders.IsRefusedSession(err))
                    {
                        _busyUntil[candidate] = _time.GetUtcNow() + H264Encoders.BusyCooldown;
                        sawBusyOrSkipped = true;
                        return (false, true);
                    }
                    return (false, false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return (false, false);
                }
            },
            onTransientSkip: () => sawBusyOrSkipped = true,
            ct).ConfigureAwait(false);

        _vaapiDevice = device;
        // Never permanently cache a lower-priority/software fallback while a higher-priority GPU was only skipped
        // for this file or transiently busy (mirrors Maktaba's non-sticky busy fallback).
        _encoder = sawBusyOrSkipped ? null : encoder;
        return encoder;
    }

    public void MarkEncoderFailed(string encoder, bool transientBusy = false)
    {
        if (transientBusy)
        {
            _busyUntil[encoder] = _time.GetUtcNow() + H264Encoders.BusyCooldown;
        }
        else if (encoder == "h264_vaapi" && _vaapiDevice is not null)
        {
            _failedVaapiDevices.Add(_vaapiDevice);
            var remaining = _findVaapiDevices().Any(d => !_failedVaapiDevices.Contains(d));
            if (!remaining) _failedEncoders.Add(encoder);
        }
        else
        {
            _failedEncoders.Add(encoder);
        }

        if (string.Equals(_encoder, encoder, StringComparison.Ordinal))
        {
            _encoder = null;
            if (encoder == "h264_vaapi") _vaapiDevice = null;
        }
    }

    public Task VerifyOutputsAsync(IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, bool expectAudio, CancellationToken ct) =>
        VerifyOutputsCoreAsync(rungs, outputs, new SourceInfo(0, 0, 0, 0, expectAudio, 0), settings: null, ct);

    public Task VerifyOutputsAsync(IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, SourceInfo source, EncodeLadderSettings settings, CancellationToken ct) =>
        VerifyOutputsCoreAsync(rungs, outputs, source, settings, ct);

    private async Task VerifyOutputsCoreAsync(IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, SourceInfo source, EncodeLadderSettings? settings, CancellationToken ct)
    {
        var (_, ffprobe) = await Paths(ct).ConfigureAwait(false);
        for (var i = 0; i < rungs.Count; i++)
        {
            var (code, output, error) = await RunAsync(ffprobe, new[] { "-v", "error", "-print_format", "json", "-show_streams", "-show_format", outputs[i] }, null, ct).ConfigureAwait(false);
            if (code != 0) throw new LocalEncodeException("output_invalid", $"ffprobe could not verify {rungs[i].Name}: " + LastLine(error));
            VerifyEncodedRungJson(output, rungs[i], source.HasAudio, source.DurationSeconds);

            if (settings is not null)
            {
                var (kfCode, kfOutput, kfError) = await RunAsync(ffprobe, new[]
                {
                    "-v", "error",
                    "-select_streams", "v:0",
                    "-skip_frame", "nokey",
                    "-show_entries", "frame=pts_time,best_effort_timestamp_time",
                    "-print_format", "json",
                    outputs[i]
                }, null, ct).ConfigureAwait(false);
                if (kfCode != 0) throw new LocalEncodeException("output_invalid", $"ffprobe could not read keyframes for {rungs[i].Name}: " + LastLine(kfError));
                VerifyKeyframesJson(kfOutput, rungs[i], settings.GopSeconds, source.DurationSeconds);
            }
        }
    }

    /// <summary>
    /// Verifies an encoded rung's <c>ffprobe</c> JSON against <c>apps/media/container/prebuilt.mjs:verifyRenditions</c>
    /// (H.264 video, <c>yuv420p</c> 4:2:0 pixel format, matching dimensions, positive duration within source slack,
    /// stereo AAC when expected, and no audio stream on silent sources).
    /// </summary>
    public static void VerifyEncodedRungJson(string json, PlannedRung rung, bool expectAudio, double sourceDurationSeconds = 0)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LocalEncodeException("output_invalid", $"ffprobe returned unreadable JSON for {rung.Name}", ex);
        }
        var streams = root?["streams"]?.AsArray() ?? new JsonArray();
        var video = streams.FirstOrDefault(s => s?["codec_type"]?.GetValue<string>() == "video" && s?["disposition"]?["attached_pic"]?.GetValue<int>() != 1);
        if (video is null) throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} has no video stream");
        var codec = video["codec_name"]?.GetValue<string>();
        if (!string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase))
            throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} uses codec '{codec}', expected h264");
        var pixFmt = video["pix_fmt"]?.GetValue<string>();
        if (!string.Equals(pixFmt, "yuv420p", StringComparison.OrdinalIgnoreCase))
            throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} uses pixel format '{pixFmt}', expected yuv420p");
        var w = (int)Number(video["width"]);
        var h = (int)Number(video["height"]);
        if (w != rung.Width || h != rung.Height)
            throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} is {w}x{h}, expected {rung.Width}x{rung.Height}");
        var duration = root?["format"]?["duration"] is JsonNode d ? Number(d) : video["duration"] is JsonNode vd ? Number(vd) : 0;
        if (duration <= 0)
            throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} has zero duration");
        if (sourceDurationSeconds > 0)
        {
            var slack = Math.Max(1.0, sourceDurationSeconds * 0.01);
            if (Math.Abs(duration - sourceDurationSeconds) > slack)
                throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} duration {duration:0.##}s differs from source {sourceDurationSeconds:0.##}s beyond {slack:0.##}s slack");
        }
        var audio = streams.FirstOrDefault(s => s?["codec_type"]?.GetValue<string>() == "audio");
        if (expectAudio)
        {
            var audioCodec = audio?["codec_name"]?.GetValue<string>();
            var channels = (int)Number(audio?["channels"]);
            if (!string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase) || channels != 2)
                throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} has audio '{audioCodec ?? "none"}' ({channels}ch), expected stereo aac");
        }
        else if (audio is not null)
        {
            throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} has an unexpected audio stream for a silent source");
        }
    }

    /// <summary>
    /// Verifies keyframe intervals from <c>ffprobe -skip_frame nokey</c> JSON stay within
    /// <c>GOP_SECONDS * 2 + 0.05</c>, matching <c>apps/media/container/prebuilt.mjs:verifyRenditions</c>.
    /// </summary>
    public static void VerifyKeyframesJson(string json, PlannedRung rung, int gopSeconds, double sourceDurationSeconds = 0)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LocalEncodeException("output_invalid", $"ffprobe returned unreadable keyframe JSON for {rung.Name}", ex);
        }
        var frames = root?["frames"]?.AsArray() ?? new JsonArray();
        var times = new List<double>(frames.Count);
        foreach (var frame in frames)
        {
            if (frame is null) continue;
            var node = frame["pts_time"] ?? frame["best_effort_timestamp_time"];
            if (node is not null) times.Add(Number(node));
        }
        times.Sort();
        var maxGapAllowed = gopSeconds * 2 + 0.05;
        if (sourceDurationSeconds > maxGapAllowed && times.Count <= 1)
        {
            throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} has too few keyframes ({times.Count}) for {sourceDurationSeconds:0.##}s duration");
        }
        for (var i = 1; i < times.Count; i++)
        {
            var gap = times[i] - times[i - 1];
            if (gap > maxGapAllowed)
            {
                throw new LocalEncodeException("output_invalid", $"Encoded {rung.Name} keyframe gap {gap:0.##}s exceeds {maxGapAllowed:0.##}s");
            }
        }
    }

    /// <summary>
    /// Verifies packaged HLS segment lengths, total duration slack, and peak segment bitrate against
    /// <c>apps/media/container/prebuilt.mjs:verifyRenditions</c> (<c>SEGMENT_SLACK_SECONDS = 1</c>, <c>PEAK_FACTOR = 1.5</c>).
    /// </summary>
    public static void VerifyPackagedRung(string rungDirectory, PlannedRung rung, SourceInfo source, EncodeLadderSettings settings)
    {
        var playlistPath = Path.Combine(rungDirectory, "index.m3u8");
        if (!File.Exists(playlistPath))
            throw new LocalEncodeException("output_invalid", $"Packaged {rung.Name} is missing index.m3u8");

        var lines = File.ReadAllLines(playlistPath);
        double? pendingSeconds = null;
        var segments = new List<(string File, double Seconds)>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                var value = line[8..].TrimEnd(',');
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs) || secs <= 0)
                    throw new LocalEncodeException("output_invalid", $"Packaged {rung.Name} has invalid EXTINF '{line}'");
                pendingSeconds = secs;
                continue;
            }
            if (line.StartsWith('#')) continue;
            if (pendingSeconds is { } s)
            {
                segments.Add((line, s));
                pendingSeconds = null;
            }
        }

        if (segments.Count == 0)
            throw new LocalEncodeException("output_invalid", $"Packaged {rung.Name} playlist has no segments");

        const double segmentSlackSeconds = 1.0;
        const double peakFactor = 1.5;
        var maxSegSeconds = settings.SegmentSeconds + segmentSlackSeconds;
        var maxPeakBitrate = (rung.Maxrate + (source.HasAudio ? rung.Audio : 0)) * peakFactor;
        var totalSeconds = 0.0;

        foreach (var (file, seconds) in segments)
        {
            if (seconds > maxSegSeconds)
                throw new LocalEncodeException("output_invalid", $"Packaged {rung.Name} segment {file} is {seconds:0.##}s, exceeding {maxSegSeconds:0.##}s");
            var fullPath = Path.Combine(rungDirectory, file);
            if (!File.Exists(fullPath))
                throw new LocalEncodeException("output_invalid", $"Packaged {rung.Name} segment {file} is missing");
            var bytes = new FileInfo(fullPath).Length;
            if (bytes <= 0)
                throw new LocalEncodeException("output_invalid", $"Packaged {rung.Name} segment {file} is empty");
            var bitrate = (bytes * 8.0) / seconds;
            if (bitrate > maxPeakBitrate)
                throw new LocalEncodeException("output_invalid", $"Packaged {rung.Name} peak segment bitrate {bitrate:0} bps exceeds {maxPeakBitrate:0} bps");
            totalSeconds += seconds;
        }

        if (source.DurationSeconds > 0)
        {
            var slack = Math.Max(1.0, source.DurationSeconds * 0.01);
            if (Math.Abs(totalSeconds - source.DurationSeconds) > slack)
                throw new LocalEncodeException("output_invalid", $"Packaged {rung.Name} duration {totalSeconds:0.##}s differs from source {source.DurationSeconds:0.##}s beyond {slack:0.##}s slack");
        }
    }

    /// <summary>
    /// Walks preferred encoder candidates in priority order, probing hardware encoders with a 0.2 s test encode
    /// (trying each Linux DRM render node for VAAPI) so missing drivers/GPUs fall cleanly down to the next working encoder.
    /// </summary>
    internal static Task<(string? Encoder, string? VaapiDevice)> SelectWorkingEncoderAsync(
        IReadOnlyList<string> candidates,
        IReadOnlySet<string> failedEncoders,
        IReadOnlyList<string> vaapiDevices,
        Func<string, string?, CancellationToken, Task<bool>> probeAsync,
        CancellationToken ct) =>
        SelectWorkingEncoderAsync(
            candidates,
            failedEncoders,
            transientSkippedEncoders: new HashSet<string>(StringComparer.Ordinal),
            vaapiDevices,
            async (enc, dev, token) => (await probeAsync(enc, dev, token).ConfigureAwait(false), false),
            onTransientSkip: null,
            ct);

    internal static async Task<(string? Encoder, string? VaapiDevice)> SelectWorkingEncoderAsync(
        IReadOnlyList<string> candidates,
        IReadOnlySet<string> failedEncoders,
        IReadOnlySet<string> transientSkippedEncoders,
        IReadOnlyList<string> vaapiDevices,
        Func<string, string?, CancellationToken, Task<(bool Ok, bool Busy)>> probeAsync,
        Action? onTransientSkip,
        CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            if (failedEncoders.Contains(candidate)) continue;
            if (transientSkippedEncoders.Contains(candidate))
            {
                onTransientSkip?.Invoke();
                continue;
            }
            if (!H264Encoders.IsHardware(candidate)) return (candidate, null);
            if (candidate == "h264_vaapi")
            {
                foreach (var device in vaapiDevices)
                {
                    var (ok, busy) = await probeAsync(candidate, device, ct).ConfigureAwait(false);
                    if (ok) return (candidate, device);
                    if (busy) onTransientSkip?.Invoke();
                }
                continue;
            }
            var (probeOk, probeBusy) = await probeAsync(candidate, null, ct).ConfigureAwait(false);
            if (probeOk) return (candidate, null);
            if (probeBusy) onTransientSkip?.Invoke();
        }
        return (null, null);
    }

    public async Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct)
    {
        var (ffmpeg, _) = await Paths(ct).ConfigureAwait(false);
        var args = Arguments(source, info, rungs, outputs, encoder, settings, _vaapiDevice);
        var duration = info.DurationSeconds;
        void OnLine(string line)
        {
            // -progress pipe:1 writes "out_time_us=12345678" lines
            if (duration > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
            {
                progress.Report(Math.Clamp(us / 1_000_000.0 / duration, 0, 1));
            }
        }
        var (code, _, error) = await RunAsync(ffmpeg, args, OnLine, ct).ConfigureAwait(false);
        if (code != 0)
        {
            var errCode = H264Encoders.IsRefusedSession(error)
                ? "gpu_busy"
                : H264Encoders.IsSourceOrDiskError(error)
                    ? "source_error"
                    : "ffmpeg_failed";
            throw new LocalEncodeException(errCode, $"ffmpeg stopped ({encoder}): " + LastLine(error));
        }
    }

    /// <summary>
    /// One decode split to every rung, scaled with bicubic to the rung's size, a keyframe every GOP from the start
    /// (as the server's <c>-force_key_frames</c>), AAC stereo at the rung's rate when there is sound.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, string? vaapiDevice = null)
    {
        var filterFormat = H264Encoders.FilterFormat(encoder);
        var split = $"[0:v]split={rungs.Count}" + string.Concat(rungs.Select((_, i) => $"[v{i}]"));
        var scales = rungs.Select((r, i) => $"[v{i}]scale=w={r.Width}:h={r.Height}:flags=bicubic,{filterFormat}[v{i}o]");
        var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
        if (encoder == "h264_vaapi") args.AddRange(new[] { "-vaapi_device", vaapiDevice ?? H264Encoders.VaapiDevice });
        args.AddRange(new[] { "-i", source, "-filter_complex", string.Join(";", new[] { split }.Concat(scales)) });
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

    private static string LastLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        return line.Length > 300 ? line[..300] : line;
    }

    public async Task ExtractAudioAsync(string source, string wavPath, CancellationToken ct)
    {
        var (ffmpeg, _) = await Paths(ct).ConfigureAwait(false);
        var (code, _, error) = await RunAsync(ffmpeg, new[] { "-nostdin", "-y", "-v", "error", "-i", source, "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wavPath }, null, ct).ConfigureAwait(false);
        if (code == 0) return;
        try { File.Delete(wavPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        // "-map 0:a:0" matches nothing in a file without sound
        throw new LocalEncodeException(error.Contains("matches no streams", StringComparison.Ordinal) ? "no_audio" : "ffmpeg_failed", "ffmpeg could not read the file's sound: " + LastLine(error));
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(string exe, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct)
    {
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (onLine is not null) onLine(e.Data);
            else output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            // Only the end matters for the reason; a long encode writes a lot
            if (error.Length > 64_000) error.Remove(0, 32_000);
            error.AppendLine(e.Data);
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
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return (process.ExitCode, output.ToString(), error.ToString());
    }
}

/// <summary>
/// Encodes the server's HLS ladder on this computer (wiki TOOLS-ADMIN TA-12), unless the batch chose "Let the server
/// encode" or the server does not take renditions for the file (its delivery target is not R2). Opens the file on
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
    private readonly Func<string, long?> _freeSpace;
    private readonly TimeProvider _time;
    private EncodingSettings? _settings;

    public EncodeStep(ImportApiClient api, Uri videosApi, IMediaTools media, IRenditionPackager packager, string workRoot, string encoderName, Action<Exception, QueueItem>? report = null, Func<string, long?>? freeSpace = null, TimeProvider? time = null)
    {
        _api = api;
        _tusEndpoint = new Uri(videosApi, "/api/videos/tus");
        _media = media;
        _packager = packager;
        _workRoot = workRoot;
        _encoderName = encoderName;
        _report = report;
        _freeSpace = freeSpace ?? FreeSpace;
        _time = time ?? TimeProvider.System;
    }

    public ImportStepKind Kind => ImportStepKind.Encode;

    public static string DefaultWorkRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNowTools", "renditions");

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        if (item.ServerEncodes)
        {
            context.Log(LogLevel.Info, "server_encode_chosen", "Let the server encode was chosen: the server encodes this file");
            return StepResult.Skipped;
        }
        // Encoded before the app stopped: kept while every file is still there
        if (item.Renditions is { } done && (done.Sent || done.Files.All(f => File.Exists(Path.Combine(done.Directory, f.Path))))) return StepResult.Done;
        if (item.Sha256 is null || item.FingerprintHash is null) throw new StepFailedException("not_hashed", "The file has not been checked yet", retryable: false);
        UploadStep.CheckUnchanged(item);
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
            DeleteFolder(directory);
            var info = await _media.ProbeAsync(item.Video.Path, ct).ConfigureAwait(false);
            var rungs = EncodeLadder.For(info, settings.Ladder);
            CheckSpace(directory, info, rungs);
            System.IO.Directory.CreateDirectory(directory);

            // 90% for the encode, 10% for packaging. If a hardware encoder fails within Maktaba's 30 s fallback
            // window, hits a transient GPU session limit, or produces output/segments failing local verification,
            // fall back locally down the candidate list before giving up to the server. Permanent blacklisting is
            // committed only after a subsequent encoder succeeds on the same input so corrupt sources or disk-full
            // errors never disable the GPU for later files.
            var outputs = rungs.Select(r => Path.Combine(directory, r.Name + ".mp4")).ToList();
            var tried = new HashSet<string>(StringComparer.Ordinal);
            var pendingBlacklist = new List<string>();
            string encoder;
            while (true)
            {
                encoder = await _media.PickEncoderAsync(tried, ct).ConfigureAwait(false)
                    ?? throw new LocalEncodeException("ffmpeg_failed", "This ffmpeg has no H.264 encoder");
                if (!tried.Add(encoder))
                {
                    throw new LocalEncodeException("ffmpeg_failed", $"No working H.264 encoder left after {encoder} failed");
                }
                context.Log(LogLevel.Info, "encode_plan", $"Encoding {string.Join(", ", rungs.Select(r => r.Name))} with {encoder}", encoder, string.Join(", ", rungs.Select(r => r.Name)));
                var startedAt = _time.GetUtcNow();
                try
                {
                    await _media.EncodeAsync(item.Video.Path, info, rungs, outputs, encoder, settings.Ladder, new Progress<double>(p => context.Progress(p * 0.9)), ct).ConfigureAwait(false);
                    await _media.VerifyOutputsAsync(rungs, outputs, info, settings.Ladder, ct).ConfigureAwait(false);
                    for (var i = 0; i < rungs.Count; i++)
                    {
                        var rungDir = Path.Combine(directory, rungs[i].Name);
                        await _packager.PackageAsync(outputs[i], rungDir, settings.Ladder.SegmentSeconds, ct).ConfigureAwait(false);
                        File.Delete(outputs[i]);
                        FfmpegMediaTools.VerifyPackagedRung(rungDir, rungs[i], info, settings.Ladder);
                        context.Progress(0.9 + (0.1 * (i + 1) / rungs.Count));
                    }
                    foreach (var failedHw in pendingBlacklist)
                    {
                        _media.MarkEncoderFailed(failedHw, transientBusy: false);
                    }
                    break;
                }
                catch (LocalEncodeException ex) when (ex.Code is "ffmpeg_failed" or "gpu_busy" or "output_invalid" && H264Encoders.IsHardware(encoder))
                {
                    foreach (var o in outputs) { try { if (File.Exists(o)) File.Delete(o); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                    foreach (var r in rungs) DeleteFolder(Path.Combine(directory, r.Name));

                    var elapsed = _time.GetUtcNow() - startedAt;
                    if (ex.Code == "gpu_busy")
                    {
                        _media.MarkEncoderFailed(encoder, transientBusy: true);
                    }
                    else if (ex.Code == "ffmpeg_failed" && elapsed > H264Encoders.FallbackWindow)
                    {
                        // Past Maktaba's 30 s fallbackWindow: cool down briefly without permanently blacklisting the GPU.
                        _media.MarkEncoderFailed(encoder, transientBusy: true);
                        throw;
                    }
                    else
                    {
                        pendingBlacklist.Add(encoder);
                    }

                    var next = await _media.PickEncoderAsync(tried, ct).ConfigureAwait(false);
                    if (next is null || tried.Contains(next)) throw;
                    context.Log(LogLevel.Warning, "encoder_fallback", $"Hardware encoder {encoder} failed ({ex.Message}); retrying with {next}", encoder, next);
                }
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
        catch (OperationCanceledException)
        {
            DeleteFolder(directory);
            throw;
        }
        catch (Exception ex) when (ex is LocalEncodeException or IOException or UnauthorizedAccessException)
        {
            // Not the file's failure: the server encodes it as any upload
            DeleteFolder(directory);
            item.Renditions = null;
            context.Save();
            var code = ex is LocalEncodeException local ? local.Code : "disk";
            context.Log(LogLevel.Warning, "encode_failed", $"Could not encode here: {ex.Message}. The server encodes this file", code, ex.Message);
            _report?.Invoke(ex, item);
            return StepResult.Skipped;
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

    /// <summary>Room for the rungs at their peak rates, and the same again for the MP4s before packaging.</summary>
    private void CheckSpace(string directory, SourceInfo info, IReadOnlyList<PlannedRung> rungs)
    {
        var free = _freeSpace(directory);
        if (free is null) return;
        var estimate = (long)(rungs.Sum(r => (double)(r.Maxrate + r.Audio)) / 8 * Math.Max(1, info.DurationSeconds) * 2.2);
        if (free < estimate)
        {
            throw new LocalEncodeException("disk_space", $"Not enough free space for the renditions (about {estimate / 1048576} MB needed)");
        }
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

    internal static void DeleteFolder(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return;
        try
        {
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next run's clean-up; the queue does not depend on it
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

    public RenditionSender(ImportApiClient api) => _api = api;

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
                EncodeStep.DeleteFolder(set.Directory);
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

    private static void Drop(QueueItem item, IStepContext context, string code, string reason)
    {
        EncodeStep.DeleteFolder(item.Renditions?.Directory);
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

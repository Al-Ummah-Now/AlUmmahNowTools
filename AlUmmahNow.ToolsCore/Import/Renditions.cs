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

    /// <summary>ffmpeg_missing, probe_failed, no_video, disk_space, ffmpeg_failed, package_failed, too_large or settings_invalid</summary>
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

    public FfmpegMediaTools(Func<CancellationToken, Task<(string Ffmpeg, string Ffprobe)?>> locate) => _locate = locate;

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
        if (code != 0) throw new LocalEncodeException("ffmpeg_failed", $"ffmpeg stopped ({encoder}): " + LastLine(error));
    }

    /// <summary>
    /// One decode split to every rung, scaled with bicubic to the rung's size, a keyframe every GOP from the start
    /// (as the server's <c>-force_key_frames</c>), AAC stereo at the rung's rate when there is sound.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings)
    {
        var format = H264Encoders.InputFormat(encoder);
        var split = $"[0:v]split={rungs.Count}" + string.Concat(rungs.Select((_, i) => $"[v{i}]"));
        var scales = rungs.Select((r, i) => $"[v{i}]scale=w={r.Width}:h={r.Height}:flags=bicubic,format={format}[v{i}o]");
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", source, "-filter_complex", string.Join(";", new[] { split }.Concat(scales)) };
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
    private EncodingSettings? _settings;

    public EncodeStep(ImportApiClient api, Uri videosApi, IMediaTools media, IRenditionPackager packager, string workRoot, string encoderName, Action<Exception, QueueItem>? report = null, Func<string, long?>? freeSpace = null)
    {
        _api = api;
        _tusEndpoint = new Uri(videosApi, "/api/videos/tus");
        _media = media;
        _packager = packager;
        _workRoot = workRoot;
        _encoderName = encoderName;
        _report = report;
        _freeSpace = freeSpace ?? FreeSpace;
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
            var encoder = await _media.PickEncoderAsync(ct).ConfigureAwait(false)
                ?? throw new LocalEncodeException("ffmpeg_failed", "This ffmpeg has no H.264 encoder");
            CheckSpace(directory, info, rungs);
            System.IO.Directory.CreateDirectory(directory);
            context.Log(LogLevel.Info, "encode_plan", $"Encoding {string.Join(", ", rungs.Select(r => r.Name))} with {encoder}", encoder, string.Join(", ", rungs.Select(r => r.Name)));

            // 90% for the encode, 10% for packaging
            var outputs = rungs.Select(r => Path.Combine(directory, r.Name + ".mp4")).ToList();
            await _media.EncodeAsync(item.Video.Path, info, rungs, outputs, encoder, settings.Ladder, new Progress<double>(p => context.Progress(p * 0.9)), ct).ConfigureAwait(false);
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
            context.Log(LogLevel.Info, "encoded", $"Encoded {item.Renditions.Rungs.Count} rungs here ({size} MB); they go with the upload", item.Renditions.Rungs.Count.ToString(CultureInfo.InvariantCulture), bytes.ToString(CultureInfo.InvariantCulture));
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
            context.Log(LogLevel.Info, "renditions_staged", $"Sending the renditions ({set.Files.Count} files)", set.Files.Count.ToString(CultureInfo.InvariantCulture));
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

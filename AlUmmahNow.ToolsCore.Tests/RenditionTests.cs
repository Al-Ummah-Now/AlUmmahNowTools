using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>The server's ladder as admin-api serves it (shared-types ENCODE_LADDER).</summary>
internal static class Ladder
{
    public const string Json = """
        {"ladder":{"version":1,"rungs":[{"name":"360p","short":360,"maxrate":800000,"audio":96000},{"name":"480p","short":480,"maxrate":1400000,"audio":128000},{"name":"720p","short":720,"maxrate":2800000,"audio":128000},{"name":"1080p","short":1080,"maxrate":5000000,"audio":128000}],
        "shortSideTolerance":8,"segmentSeconds":6,"gopSeconds":2,
        "video":{"codec":"h264","pixelFormat":"yuv420p","crf":23,"bufsizeFactor":2,"profile":{"high":{"minHeight":720,"minWidth":1280},"otherwise":"main"},"fpsCap":{"aboveFps":31,"fps":30},"scaler":"bicubic","evenDimensions":true},
        "audio":{"codec":"aac","channels":2},"hls":{"segmentType":"fmp4"}},
        "prebuiltLimits":{"maxFiles":16000,"maxFileBytes":33554432,"maxTotalBytes":42949672960,"staleAfterMs":172800000}}
        """;

    public static EncodingSettings Settings => JsonSerializer.Deserialize<EncodingSettings>(Json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}

public class EncodeLadderTests
{
    private static string Sizes(IReadOnlyList<PlannedRung> rungs) => string.Join(" ", rungs.Select(r => $"{r.Name}:{r.Width}x{r.Height}:{r.Profile}"));

    [Fact]
    public void The_ladder_follows_the_media_containers_rule()
    {
        var ladder = Ladder.Settings.Ladder;
        Assert.Equal("360p:640x360:main 480p:854x480:main 720p:1280x720:high 1080p:1920x1080:high", Sizes(EncodeLadder.For(new SourceInfo(1920, 1080, 0, 30, true, 10), ladder)));
        // A few pixels short is still 1080p, never above the source
        Assert.Equal("1080p:1916x1076:high", Sizes(EncodeLadder.For(new SourceInfo(1916, 1076, 0, 30, true, 10), ladder)).Split(' ')[^1]);
        // A Short is named by its short side; a quarter turn makes coded landscape portrait
        Assert.Equal("360p:360x640:main 480p:480x854:high 720p:720x1280:high", Sizes(EncodeLadder.For(new SourceInfo(720, 1280, 0, 30, false, 10), ladder)));
        Assert.Equal("360p:360x640:main 480p:480x854:high 720p:720x1280:high", Sizes(EncodeLadder.For(new SourceInfo(1280, 720, 90, 30, false, 10), ladder)));
        // Below the lowest rung: one rung at the source's size
        Assert.Equal("360p:320x240:main", Sizes(EncodeLadder.For(new SourceInfo(320, 240, 0, 25, true, 10), ladder)));
        Assert.Equal(1600000, EncodeLadder.For(new SourceInfo(640, 360, 0, 30, true, 10), ladder)[0].Bufsize);
        Assert.Equal(30, EncodeLadder.CappedFps(new SourceInfo(1920, 1080, 0, 59.94, true, 1), ladder));
        Assert.Null(EncodeLadder.CappedFps(new SourceInfo(1920, 1080, 0, 29.97, true, 1), ladder));
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("360p/../x")]
    [InlineData("C:\\x")]
    [InlineData("")]
    public void A_rung_name_that_is_not_like_the_servers_is_refused(string name)
    {
        // Rung names become folder and file names on this computer
        var ladder = Ladder.Settings.Ladder;
        ladder = ladder with { Rungs = ladder.Rungs.Select((r, i) => i == 1 ? r with { Name = name } : r).ToList() };
        var refused = Assert.Throws<LocalEncodeException>(() => EncodeLadder.For(new SourceInfo(1920, 1080, 0, 30, true, 10), ladder));
        Assert.Equal("settings_invalid", refused.Code);
    }

    [Fact]
    public void Picks_hardware_first_per_platform_and_skips_ffmpeg_legend_lines()
    {
        const string list = """
            Encoders:
             V..... = Video
             ------
             V....D libopenh264          OpenH264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
             V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
             V....D h264_videotoolbox    VideoToolbox H.264 Encoder (codec h264)
             A....D aac                  AAC (Advanced Audio Coding)
            """;
        // Hardware-first policy (Maktaba): VideoToolbox ahead of libx264, legend tokens ignored
        Assert.Equal("h264_videotoolbox", H264Encoders.Pick(list));
        Assert.DoesNotContain("=", H264Encoders.ParseVideoEncoders(list));
        Assert.DoesNotContain("------", H264Encoders.ParseVideoEncoders(list));
        Assert.Equal("libx264", H264Encoders.Pick(" V....D libopenh264 OpenH264\n V....D libx264 libx264 H.264\n"));
        Assert.Equal("libopenh264", H264Encoders.Pick(" V....D libopenh264 OpenH264\n"));
        Assert.Null(H264Encoders.Pick(" A....D aac AAC\n"));

        const string linuxVaapi = """
             V....D libopenh264          OpenH264 H.264
             V....D h264_vaapi           H.264/AVC (VAAPI) (codec h264)
            """;
        // Default vaapiAvailable is false so non-Linux callers don't pick VAAPI by mistake
        Assert.Equal("libopenh264", H264Encoders.Pick(linuxVaapi));
        Assert.Equal("h264_vaapi", H264Encoders.Pick(linuxVaapi, vaapiAvailable: true));
        Assert.Equal("libopenh264", H264Encoders.Pick(linuxVaapi, vaapiAvailable: false));

        // OS-scoped priority and explicit preference override (Maktaba hwaccel.go)
        const string allEncoders = """
             V....D libx264              libx264 H.264
             V....D h264_videotoolbox    VideoToolbox
             V....D h264_nvenc           NVIDIA NVENC
             V....D h264_qsv             Intel QSV
             V....D h264_amf             AMD AMF
             V....D h264_vaapi           VAAPI
             V....D h264_mf              Media Foundation
             V....D libopenh264          OpenH264
            """;
        Assert.Equal(new[] { "h264_videotoolbox", "libx264", "libopenh264" }, H264Encoders.Candidates(allEncoders, os: "darwin"));
        Assert.Equal(new[] { "h264_nvenc", "h264_qsv", "h264_vaapi", "libx264", "libopenh264" }, H264Encoders.Candidates(allEncoders, os: "linux"));
        Assert.Equal(new[] { "h264_nvenc", "h264_qsv", "h264_amf", "h264_mf", "libx264", "libopenh264" }, H264Encoders.Candidates(allEncoders, os: "windows"));
        Assert.Equal(new[] { "libx264", "libopenh264" }, H264Encoders.Candidates(allEncoders, os: "linux", prefer: "software"));
        Assert.Equal(new[] { "h264_vaapi", "libx264", "libopenh264" }, H264Encoders.Candidates(allEncoders, os: "linux", prefer: "vaapi"));
    }

    [Fact]
    public async Task Hardware_encoder_probe_skips_broken_gpu_encoders_and_tries_all_vaapi_render_nodes()
    {
        const string lgplLinuxList = """
             V....D h264_nvenc           NVIDIA NVENC H.264 encoder
             V....D h264_qsv             H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (Intel Quick Sync Video acceleration)
             V....D h264_amf             AMD AMF H.264 Encoder
             V....D h264_vaapi           H.264/AVC (VAAPI) (codec h264)
             V....D libopenh264          OpenH264 H.264
            """;
        var candidates = H264Encoders.Candidates(lgplLinuxList, os: "linux");
        Assert.Equal(new[] { "h264_nvenc", "h264_qsv", "h264_vaapi", "libopenh264" }, candidates);

        // Dual-GPU Linux machine: nvenc/qsv fail probe, renderD128 (dGPU) fails VAAPI, renderD129 (iGPU) succeeds
        var probed = new List<string>();
        var (encoder, device) = await FfmpegMediaTools.SelectWorkingEncoderAsync(
            candidates,
            new HashSet<string>(),
            new[] { "/dev/dri/renderD128", "/dev/dri/renderD129" },
            (enc, dev, _) =>
            {
                probed.Add(dev is null ? enc : $"{enc}@{dev}");
                return Task.FromResult(enc == "h264_vaapi" && dev == "/dev/dri/renderD129");
            },
            default);
        Assert.Equal("h264_vaapi", encoder);
        Assert.Equal("/dev/dri/renderD129", device);
        Assert.Equal(new[] { "h264_nvenc", "h264_qsv", "h264_vaapi@/dev/dri/renderD128", "h264_vaapi@/dev/dri/renderD129" }, probed);

        // When every hardware probe fails, falls through cleanly to software (libx264 when present, else libopenh264)
        var (sw, swDev) = await FfmpegMediaTools.SelectWorkingEncoderAsync(
            candidates,
            new HashSet<string>(),
            new[] { "/dev/dri/renderD128" },
            (_, _, _) => Task.FromResult(false),
            default);
        Assert.Equal("libopenh264", sw);
        Assert.Null(swDev);
    }

    [Fact]
    public void Vaapi_videotoolbox_probe_refused_session_and_output_verification_follow_maktaba_policy()
    {
        var info = new SourceInfo(1280, 720, 0, 30, true, 10);
        var settings = Ladder.Settings.Ladder;
        var rungs = EncodeLadder.For(info, settings).Take(2).ToList();
        var args = (List<string>)FfmpegMediaTools.Arguments("/in.mp4", info, rungs, new[] { "/w/360p.mp4", "/w/480p.mp4" }, "h264_vaapi", settings, "/dev/dri/renderD129");
        var text = string.Join(" ", args);

        Assert.Equal("nv12", H264Encoders.InputFormat("h264_vaapi"));
        Assert.Equal("format=nv12,hwupload", H264Encoders.FilterFormat("h264_vaapi"));
        Assert.True(args.IndexOf("-vaapi_device") >= 0 && args.IndexOf("-vaapi_device") < args.IndexOf("-i"));
        Assert.Equal("/dev/dri/renderD129", args[args.IndexOf("-vaapi_device") + 1]);
        Assert.Contains("[v0]scale=w=640:h=360:flags=bicubic,format=nv12,hwupload[v0o]", text);
        Assert.Contains("-c:v:0 h264_vaapi", text);
        Assert.Contains("-rc_mode:v:0 VBR -b:v:0 480000 -maxrate:v:0 800000 -bufsize:v:0 1600000", text);

        var vaapiProbe = string.Join(" ", H264Encoders.ProbeArguments("h264_vaapi", "/dev/dri/renderD129"));
        Assert.Contains("-vaapi_device /dev/dri/renderD129", vaapiProbe);
        Assert.Contains("color=c=black:s=256x144:r=25:d=0.2", vaapiProbe);
        Assert.Contains("-vf format=nv12,hwupload", vaapiProbe);
        Assert.Contains("-c:v h264_vaapi -f null -", vaapiProbe);

        // VideoToolbox probe enforces -allow_sw 0 -realtime 1 (Maktaba channel/config.go)
        var vtProbe = string.Join(" ", H264Encoders.ProbeArguments("h264_videotoolbox"));
        Assert.Contains("-c:v h264_videotoolbox -allow_sw 0 -realtime 1 -f null -", vtProbe);

        // Transient NVENC session refusal vs unsupported device (Maktaba transcode.go)
        Assert.True(H264Encoders.IsRefusedSession("OpenEncodeSessionEx failed: out of memory (10): (no details)"));
        Assert.False(H264Encoders.IsRefusedSession("OpenEncodeSessionEx failed: unsupported device (2)"));
        Assert.False(H264Encoders.IsRefusedSession("Cannot load libcuda.so.1"));

        // Source/disk errors are separated from encoder failures
        Assert.True(H264Encoders.IsSourceOrDiskError("av_interleaved_write_frame(): No space left on device"));
        Assert.True(H264Encoders.IsSourceOrDiskError("in.mp4: Invalid data found when processing input"));
        Assert.True(H264Encoders.IsSourceOrDiskError("[mov,mp4] moov atom not found"));
        Assert.False(H264Encoders.IsSourceOrDiskError("Cannot load libcuda.so.1"));

        // Local output verification catches bad pix_fmt, codec, size, audio channels, unexpected silent audio, duration slack, keyframes, and peak bitrate
        var rung = rungs[0]; // 360p: 640x360, maxrate 800000, audio 96000
        FfmpegMediaTools.VerifyEncodedRungJson("""
            {"streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p","width":640,"height":360},{"codec_type":"audio","codec_name":"aac","channels":2}],"format":{"duration":"10.0"}}
            """, rung, expectAudio: true, sourceDurationSeconds: 10.0);
        // Silent source succeeds without audio, rejects unexpected audio stream
        FfmpegMediaTools.VerifyEncodedRungJson("""
            {"streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p","width":640,"height":360}],"format":{"duration":"10.0"}}
            """, rung, expectAudio: false, sourceDurationSeconds: 10.0);
        Assert.Equal("output_invalid", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.VerifyEncodedRungJson("""
            {"streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p","width":640,"height":360},{"codec_type":"audio","codec_name":"aac","channels":2}],"format":{"duration":"10.0"}}
            """, rung, expectAudio: false, sourceDurationSeconds: 10.0)).Code);
        // Bad pix_fmt
        Assert.Equal("output_invalid", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.VerifyEncodedRungJson("""
            {"streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p10le","width":640,"height":360},{"codec_type":"audio","codec_name":"aac","channels":2}],"format":{"duration":"10.0"}}
            """, rung, expectAudio: true, sourceDurationSeconds: 10.0)).Code);
        // Missing audio or non-stereo audio
        Assert.Equal("output_invalid", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.VerifyEncodedRungJson("""
            {"streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p","width":640,"height":360}],"format":{"duration":"10.0"}}
            """, rung, expectAudio: true, sourceDurationSeconds: 10.0)).Code);
        Assert.Equal("output_invalid", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.VerifyEncodedRungJson("""
            {"streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p","width":640,"height":360},{"codec_type":"audio","codec_name":"aac","channels":1}],"format":{"duration":"10.0"}}
            """, rung, expectAudio: true, sourceDurationSeconds: 10.0)).Code);
        // Duration outside slack (> max(1s, 1%))
        Assert.Equal("output_invalid", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.VerifyEncodedRungJson("""
            {"streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p","width":640,"height":360},{"codec_type":"audio","codec_name":"aac","channels":2}],"format":{"duration":"8.5"}}
            """, rung, expectAudio: true, sourceDurationSeconds: 10.0)).Code);

        // Keyframe-gap verification (GOP=2s => max gap 4.05s)
        FfmpegMediaTools.VerifyKeyframesJson("""
            {"frames":[{"pts_time":"0.000"},{"pts_time":"2.000"},{"pts_time":"4.000"},{"pts_time":"6.000"},{"pts_time":"8.000"}]}
            """, rung, gopSeconds: 2, sourceDurationSeconds: 10.0);
        Assert.Equal("output_invalid", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.VerifyKeyframesJson("""
            {"frames":[{"pts_time":"0.000"},{"pts_time":"4.500"},{"pts_time":"6.500"}]}
            """, rung, gopSeconds: 2, sourceDurationSeconds: 10.0)).Code);
        Assert.Equal("output_invalid", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.VerifyKeyframesJson("""
            {"frames":[{"pts_time":"0.000"}]}
            """, rung, gopSeconds: 2, sourceDurationSeconds: 10.0)).Code);

        // Packaged HLS verification: segment length <= SegmentSeconds+1, peak bitrate <= (maxrate+audio)*1.5, duration within slack
        var pkgDir = Temp.Folder();
        File.WriteAllText(Path.Combine(pkgDir, "index.m3u8"), "#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:6\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:5.000000,\nseg_00001.m4s\n#EXTINF:5.000000,\nseg_00002.m4s\n#EXT-X-ENDLIST\n");
        File.WriteAllBytes(Path.Combine(pkgDir, "seg_00001.m4s"), new byte[100_000]);
        File.WriteAllBytes(Path.Combine(pkgDir, "seg_00002.m4s"), new byte[100_000]);
        FfmpegMediaTools.VerifyPackagedRung(pkgDir, rung, info, settings);

        // Segment bitrate spike > (800000 + 96000) * 1.5 = 1,344,000 bps -> 5s * 1,344,000 / 8 = 840,000 bytes
        File.WriteAllBytes(Path.Combine(pkgDir, "seg_00002.m4s"), new byte[900_000]);
        Assert.Equal("output_invalid", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.VerifyPackagedRung(pkgDir, rung, info, settings)).Code);

        // Oversized segment duration (> 6 + 1 = 7s)
        File.WriteAllBytes(Path.Combine(pkgDir, "seg_00002.m4s"), new byte[100_000]);
        File.WriteAllText(Path.Combine(pkgDir, "index.m3u8"), "#EXTM3U\n#EXTINF:7.500000,\nseg_00001.m4s\n#EXTINF:2.500000,\nseg_00002.m4s\n#EXT-X-ENDLIST\n");
        Assert.Equal("output_invalid", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.VerifyPackagedRung(pkgDir, rung, info, settings)).Code);
    }

    [Fact]
    public void One_decode_writes_every_rung_with_a_keyframe_every_gop_and_no_scene_cuts()
    {
        var info = new SourceInfo(1920, 1080, 0, 59.94, true, 10);
        var settings = Ladder.Settings.Ladder;
        var rungs = EncodeLadder.For(info, settings).Take(2).ToList();
        var args = (List<string>)FfmpegMediaTools.Arguments("/in.mov", info, rungs, new[] { "/w/360p.mp4", "/w/480p.mp4" }, "libx264", settings);
        var text = string.Join(" ", args);
        Assert.Contains("[0:v]split=2[v0][v1];[v0]scale=w=640:h=360:flags=bicubic,format=yuv420p[v0o];[v1]scale=w=854:h=480:flags=bicubic,format=yuv420p[v1o]", text);
        Assert.Equal(2, args.Count(a => a == "expr:gte(t,n_forced*2)"));
        Assert.Contains("-crf:v:0 23 -maxrate:v:0 800000 -bufsize:v:0 1600000 -preset:v:0 veryfast -sc_threshold:v:0 0", text);
        Assert.Contains("-r:v:0 30", text);
        Assert.Contains("-g:v:0 60", text);
        Assert.Contains("-map 0:a:0", text);
        Assert.Contains("-c:a:0 aac -b:a:0 96000 -ac:a:0 2", text);
        Assert.Equal("/w/480p.mp4", args[args.IndexOf("-progress") - 1]);

        var silent = (List<string>)FfmpegMediaTools.Arguments("/in.mov", info with { HasAudio = false, Fps = 25 }, rungs, new[] { "/a", "/b" }, "h264_videotoolbox", settings);
        Assert.DoesNotContain("0:a:0", silent);
        Assert.DoesNotContain("-r:v:0", silent);
        Assert.Contains("-g:v:0", silent);
        Assert.Equal("50", silent[silent.IndexOf("-g:v:0") + 1]);
    }

    [Fact]
    public void Reads_size_rotation_rate_sound_and_duration_from_ffprobe()
    {
        var info = FfmpegMediaTools.ParseProbe("""
            {"streams":[{"codec_type":"video","width":1920,"height":1080,"avg_frame_rate":"30000/1001","side_data_list":[{"rotation":-90}]},{"codec_type":"audio"}],"format":{"duration":"125.5"}}
            """);
        Assert.Equal(new SourceInfo(1920, 1080, -90, 30000 / 1001.0, true, 125.5), info);
        Assert.Equal("no_video", Assert.Throws<LocalEncodeException>(() => FfmpegMediaTools.ParseProbe("""{"streams":[{"codec_type":"audio"}],"format":{}}""")).Code);
    }
}

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now += delta;
}

/// <summary>ffmpeg for tests: writes a file per rung, or fails as told.</summary>
internal sealed class FakeMedia : IMediaTools
{
    public SourceInfo Info = new(1280, 720, 0, 30, true, 12);
    public Exception? Fail;
    public HashSet<string> FailEncoders = new(StringComparer.Ordinal);
    public HashSet<string> BusyEncoders = new(StringComparer.Ordinal);
    public HashSet<string> InvalidOutputEncoders = new(StringComparer.Ordinal);
    public Action<string>? OnBeforeEncode;
    public List<string> EncoderOrder = new();
    public List<string> MarkedFailed = new();
    public List<string> TransientBusy = new();
    public string? Encoder = "libx264";
    public int Encodes;
    private string? _lastEncodedWith;

    public Task<SourceInfo> ProbeAsync(string path, CancellationToken ct) => Fail is LocalEncodeException { Code: "probe_failed" } ? throw Fail : Task.FromResult(Info);

    public Task<string?> PickEncoderAsync(CancellationToken ct) => PickEncoderAsync(null, ct);

    public Task<string?> PickEncoderAsync(IReadOnlySet<string>? skipEncoders, CancellationToken ct)
    {
        if (EncoderOrder.Count > 0)
        {
            return Task.FromResult(EncoderOrder.FirstOrDefault(e =>
                !MarkedFailed.Contains(e)
                && !TransientBusy.Contains(e)
                && (skipEncoders is null || !skipEncoders.Contains(e))));
        }
        return Task.FromResult(skipEncoders is not null && Encoder is not null && skipEncoders.Contains(Encoder) ? null : Encoder);
    }

    public void MarkEncoderFailed(string encoder, bool transientBusy = false)
    {
        if (transientBusy) TransientBusy.Add(encoder);
        else MarkedFailed.Add(encoder);
    }

    public Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct)
    {
        Encodes++;
        _lastEncodedWith = encoder;
        OnBeforeEncode?.Invoke(encoder);
        if (BusyEncoders.Contains(encoder)) throw new LocalEncodeException("gpu_busy", $"ffmpeg stopped ({encoder}): OpenEncodeSessionEx failed: out of memory (10)");
        if (FailEncoders.Contains(encoder)) throw new LocalEncodeException("ffmpeg_failed", $"ffmpeg stopped ({encoder}): driver error");
        if (Fail is not null) throw Fail;
        foreach (var o in outputs) File.WriteAllBytes(o, new byte[64]);
        return Task.CompletedTask;
    }

    public Task VerifyOutputsAsync(IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, bool expectAudio, CancellationToken ct)
    {
        if (_lastEncodedWith is not null && InvalidOutputEncoders.Contains(_lastEncodedWith))
        {
            throw new LocalEncodeException("output_invalid", $"Encoded 360p from {_lastEncodedWith} had bad pix_fmt");
        }
        return Task.CompletedTask;
    }

    /// <summary>Set to fail the sound's extraction (TA-13); otherwise writes a small stand-in WAV.</summary>
    public Exception? AudioFail;
    public int Extracts;

    public Task ExtractAudioAsync(string source, string wavPath, CancellationToken ct)
    {
        Extracts++;
        if (AudioFail is not null) throw AudioFail;
        File.WriteAllBytes(wavPath, new byte[44]);
        return Task.CompletedTask;
    }
}

/// <summary>aun-core's packaging for tests: a playlist, an init and two segments per rung.</summary>
internal sealed class FakePackager : IRenditionPackager
{
    public int SegmentBytes = 100;

    public Task PackageAsync(string mp4, string outputDirectory, int segmentSeconds, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "index.m3u8"),
            "#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:6\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n#EXT-X-INDEPENDENT-SEGMENTS\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:6.000000,\nseg_00001.m4s\n#EXTINF:6.000000,\nseg_00002.m4s\n#EXT-X-ENDLIST\n");
        File.WriteAllBytes(Path.Combine(outputDirectory, "init.mp4"), new byte[10]);
        File.WriteAllBytes(Path.Combine(outputDirectory, "seg_00001.m4s"), new byte[SegmentBytes]);
        File.WriteAllBytes(Path.Combine(outputDirectory, "seg_00002.m4s"), new byte[Math.Max(1, SegmentBytes - 10)]);
        return Task.CompletedTask;
    }
}

/// <summary>admin-api's renditions and delivery routes over <see cref="FakeServer"/> (open, TUS, uploaded).</summary>
internal sealed class RenditionServer : HttpMessageHandler
{
    private readonly HttpMessageInvoker _inner;
    public readonly FakeServer Inner = new();
    public readonly ConcurrentQueue<string> Order = new();
    public readonly ConcurrentDictionary<string, long> Stored = new();
    public bool TakesRenditions = true;
    public string? R2Status;
    public string? Prebuilt;
    /// <summary>Lose these files the first time (complete then lists them)</summary>
    public HashSet<string> LoseOnce = new();
    public string? StageRefusal;
    public JsonNode? Staged;

    public RenditionServer() => _inner = new HttpMessageInvoker(Inner);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var m = request.Method;
        HttpResponseMessage Ok(string result, HttpStatusCode status = HttpStatusCode.OK) => FakeHandler.Json(status, "{\"success\":true,\"result\":" + result + "}");
        if (path == "/api/admin/import/encoding") return Ok(Ladder.Json);
        if (path.EndsWith("/delivery"))
        {
            var r2 = R2Status is null ? "null" : "{\"status\":\"" + R2Status + "\"" + (Prebuilt is null ? "" : ",\"prebuilt\":" + Prebuilt) + "}";
            return Ok("{\"takesRenditions\":" + (TakesRenditions ? "true" : "false") + ",\"staging\":null,\"r2\":" + r2 + "}");
        }
        if (path.EndsWith("/renditions") && m == HttpMethod.Post)
        {
            Order.Enqueue("stage");
            Staged = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (StageRefusal is not null) return FakeHandler.Json(HttpStatusCode.Conflict, "{\"success\":false,\"code\":\"" + StageRefusal + "\",\"error\":\"" + StageRefusal + "\"}");
            return Ok("{\"stagingId\":\"0123456789abcdef\",\"status\":\"uploading\"}", HttpStatusCode.Created);
        }
        if (path.Contains("/renditions/0123456789abcdef/files/") && m == HttpMethod.Put)
        {
            var file = path[(path.IndexOf("/files/", StringComparison.Ordinal) + 7)..];
            var bytes = (await request.Content!.ReadAsByteArrayAsync(ct)).LongLength;
            Assert.Equal(request.Content.Headers.ContentLength, bytes);
            if (!LoseOnce.Remove(file)) Stored[file] = bytes;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        if (path.EndsWith("/complete"))
        {
            Order.Enqueue("complete");
            var declared = Staged!["files"]!.AsArray().Select(f => f!["path"]!.GetValue<string>()).ToList();
            var missing = declared.Where(p => !Stored.ContainsKey(p)).ToList();
            if (missing.Count > 0) return FakeHandler.Json(HttpStatusCode.Conflict, "{\"success\":false,\"code\":\"files_missing\",\"error\":\"files_missing\",\"missing\":" + JsonSerializer.Serialize(missing) + "}");
            return Ok("{\"stagingId\":\"0123456789abcdef\",\"status\":\"ready\"}");
        }
        if (path == "/api/videos/tus" && m == HttpMethod.Post) Order.Enqueue("tus");
        return await _inner.SendAsync(request, ct);
    }
}

public class EncodeStepTests
{
    private static (QueueItem Item, RenditionServer Server, ImportApiClient Api, string Work) Setup(bool serverEncodes = false)
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "lesson.mp4", 2500);
        var scan = FolderScanner.Scan(dir, false, null).Videos[0];
        var item = QueueItem.For(scan, "b1", "ch1", DateTimeOffset.UtcNow, serverEncodes);
        item.Sha256 = new string('a', 64);
        item.FingerprintHash = new string('c', 64);
        var server = new RenditionServer();
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), server);
        return (item, server, api, Temp.Folder());
    }

    private static EncodeStep Step(ImportApiClient api, string work, IMediaTools media, List<Exception>? reported = null, IRenditionPackager? packager = null, TimeProvider? time = null) =>
        new(api, new Uri("https://videos.api.example"), media, packager ?? new FakePackager(), work, "Tools test", (ex, _) => reported?.Add(ex), _ => long.MaxValue, time);

    [Fact]
    public async Task Encodes_the_servers_rungs_after_opening_the_file_and_keeps_them_for_the_upload()
    {
        var (item, server, api, work) = Setup();
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await Step(api, work, new FakeMedia()).RunAsync(item, context, default));

        Assert.Equal("vid-1", item.VideoId);
        var set = item.Renditions!;
        Assert.Equal(new[] { "360p", "480p", "720p" }, set.Rungs);
        Assert.Equal(12, set.Files.Count);
        Assert.Equal(new[] { "360p/index.m3u8", "360p/init.mp4", "360p/seg_00001.m4s", "360p/seg_00002.m4s" }, set.Files.Take(4).Select(f => f.Path));
        Assert.All(set.Files, f => Assert.True(File.Exists(Path.Combine(set.Directory, f.Path))));
        Assert.Empty(Directory.GetFiles(set.Directory, "*.mp4"));
        Assert.Equal("Tools test, ffmpeg libx264", set.Encoder);
        Assert.Contains(context.Lines, l => l.Code == "encoded");
        Assert.Equal(1, context.LastProgress, 3);
    }

    [Fact]
    public async Task The_server_encodes_when_asked_when_its_target_is_not_r2_or_when_it_already_started()
    {
        var (chosen, _, api1, work1) = Setup(serverEncodes: true);
        var media = new FakeMedia();
        Assert.Equal(StepResult.Skipped, await Step(api1, work1, media).RunAsync(chosen, new RecordingContext(), default));
        Assert.Null(chosen.ServerItemId);

        var (bunny, server, api2, work2) = Setup();
        server.TakesRenditions = false;
        var context = new RecordingContext();
        Assert.Equal(StepResult.Skipped, await Step(api2, work2, media).RunAsync(bunny, context, default));
        Assert.Contains(context.Lines, l => l.Code == "server_encodes_target");

        var (started, server3, api3, work3) = Setup();
        server3.R2Status = "transcoding";
        Assert.Equal(StepResult.Skipped, await Step(api3, work3, media).RunAsync(started, new RecordingContext(), default));
        Assert.Equal(0, media.Encodes);
    }

    [Fact]
    public async Task A_local_failure_leaves_the_encode_to_the_server_logs_why_and_reports_it()
    {
        var (item, _, api, work) = Setup();
        var reported = new List<Exception>();
        var context = new RecordingContext();
        var media = new FakeMedia { Fail = new LocalEncodeException("ffmpeg_failed", "ffmpeg stopped (libx264): Invalid data") };
        Assert.Equal(StepResult.Skipped, await Step(api, work, media, reported).RunAsync(item, context, default));
        Assert.Null(item.Renditions);
        Assert.Contains(context.Lines, l => l.Code == "encode_failed" && l.Level == LogLevel.Warning);
        Assert.Single(reported);
        Assert.Empty(Directory.GetFileSystemEntries(work));

        var (noEncoder, _, api2, work2) = Setup();
        Assert.Equal(StepResult.Skipped, await Step(api2, work2, new FakeMedia { Encoder = null }).RunAsync(noEncoder, new RecordingContext(), default));
    }

    [Fact]
    public async Task A_failed_hardware_encoder_marks_itself_unusable_and_falls_back_locally_to_the_next_encoder()
    {
        var (item, _, api, work) = Setup();
        var media = new FakeMedia
        {
            EncoderOrder = new List<string> { "h264_vaapi", "libopenh264" },
            FailEncoders = new HashSet<string> { "h264_vaapi" },
        };
        var context = new RecordingContext();

        Assert.Equal(StepResult.Done, await Step(api, work, media).RunAsync(item, context, default));
        Assert.Equal(2, media.Encodes);
        Assert.Equal(new[] { "h264_vaapi" }, media.MarkedFailed);
        Assert.NotNull(item.Renditions);
        Assert.Equal("Tools test, ffmpeg libopenh264", item.Renditions!.Encoder);
        Assert.Contains(context.Lines, l => l.Code == "encoder_fallback" && l.Level == LogLevel.Warning);
        // Subsequent picks skip the failed hardware encoder
        Assert.Equal("libopenh264", await media.PickEncoderAsync(default));
    }

    [Fact]
    public async Task Hardware_encoder_busy_or_invalid_output_falls_back_locally_to_libx264()
    {
        // Busy GPU session falls back for this file without permanently blacklisting NVENC
        var (busyItem, _, api1, work1) = Setup();
        var busyMedia = new FakeMedia
        {
            EncoderOrder = new List<string> { "h264_nvenc", "libx264" },
            BusyEncoders = new HashSet<string> { "h264_nvenc" },
        };
        Assert.Equal(StepResult.Done, await Step(api1, work1, busyMedia).RunAsync(busyItem, new RecordingContext(), default));
        Assert.Empty(busyMedia.MarkedFailed);
        Assert.Equal(new[] { "h264_nvenc" }, busyMedia.TransientBusy);
        Assert.Equal("Tools test, ffmpeg libx264", busyItem.Renditions!.Encoder);

        // Invalid hardware output (caught by local ffprobe verification) permanently invalidates that HW encoder and retries on libx264
        var (badOutItem, _, api2, work2) = Setup();
        var badOutMedia = new FakeMedia
        {
            EncoderOrder = new List<string> { "h264_vaapi", "libx264" },
            InvalidOutputEncoders = new HashSet<string> { "h264_vaapi" },
        };
        Assert.Equal(StepResult.Done, await Step(api2, work2, badOutMedia).RunAsync(badOutItem, new RecordingContext(), default));
        Assert.Equal(new[] { "h264_vaapi" }, badOutMedia.MarkedFailed);
        Assert.Equal("Tools test, ffmpeg libx264", badOutItem.Renditions!.Encoder);
    }

    [Fact]
    public async Task Source_disk_late_and_both_failed_errors_do_not_permanently_blacklist_the_gpu()
    {
        // 1. Source/disk error does not blacklist GPU and does not waste a software retry
        var (diskItem, _, api1, work1) = Setup();
        var diskMedia = new FakeMedia
        {
            EncoderOrder = new List<string> { "h264_nvenc", "libx264" },
            Fail = new LocalEncodeException("source_error", "ffmpeg stopped (h264_nvenc): No space left on device"),
        };
        Assert.Equal(StepResult.Skipped, await Step(api1, work1, diskMedia).RunAsync(diskItem, new RecordingContext(), default));
        Assert.Equal(1, diskMedia.Encodes);
        Assert.Empty(diskMedia.MarkedFailed);
        Assert.Equal("h264_nvenc", await diskMedia.PickEncoderAsync(default));

        // 2. Corrupt input where BOTH GPU and software fail does NOT blacklist the GPU for later good files
        var (badInputItem, _, api2, work2) = Setup();
        var badInputMedia = new FakeMedia
        {
            EncoderOrder = new List<string> { "h264_nvenc", "libx264" },
            FailEncoders = new HashSet<string> { "h264_nvenc", "libx264" },
        };
        Assert.Equal(StepResult.Skipped, await Step(api2, work2, badInputMedia).RunAsync(badInputItem, new RecordingContext(), default));
        Assert.Equal(2, badInputMedia.Encodes);
        Assert.Empty(badInputMedia.MarkedFailed);
        Assert.Equal("h264_nvenc", await badInputMedia.PickEncoderAsync(default));

        // 3. Late failure (> 30s Maktaba fallbackWindow) cools down briefly without permanently blacklisting GPU
        var clock = new FakeTimeProvider();
        var (lateItem, _, api3, work3) = Setup();
        var lateMedia = new FakeMedia
        {
            EncoderOrder = new List<string> { "h264_nvenc", "libx264" },
            FailEncoders = new HashSet<string> { "h264_nvenc" },
            OnBeforeEncode = _ => clock.Advance(TimeSpan.FromSeconds(35)),
        };
        Assert.Equal(StepResult.Skipped, await Step(api3, work3, lateMedia, time: clock).RunAsync(lateItem, new RecordingContext(), default));
        Assert.Equal(1, lateMedia.Encodes);
        Assert.Empty(lateMedia.MarkedFailed);
        Assert.Equal(new[] { "h264_nvenc" }, lateMedia.TransientBusy);
    }

    [Fact]
    public async Task Real_FfmpegMediaTools_transient_busy_falls_back_locally_and_recovers_after_cooldown()
    {
        if (OperatingSystem.IsWindows()) return;

        var binDir = Temp.Folder();
        var ffmpegPath = Path.Combine(binDir, "fake-ffmpeg.sh");
        var ffprobePath = Path.Combine(binDir, "fake-ffprobe.sh");
        var probeBusyFlag = Path.Combine(binDir, "probe-busy");

        // Fake ffmpeg: lists h264_nvenc + libx264; probes succeed unless probe-busy flag exists;
        // real encode with h264_nvenc refuses session (Maktaba refusedSession), libx264 writes valid dummy outputs.
        File.WriteAllText(ffmpegPath, """
            #!/bin/sh
            for a in "$@"; do
              if [ "$a" = "-encoders" ]; then
                printf 'Encoders:\n V..... = Video\n ------\n V....D h264_nvenc NVIDIA NVENC\n V....D libx264 libx264 H.264\n'
                exit 0
              fi
            done
            # Probe run writes to "-f null -"
            is_probe=0
            encoder=""
            prev=""
            for a in "$@"; do
              if [ "$prev" = "-c:v" ] || [ "$prev" = "-c:v:0" ]; then encoder="$a"; fi
              if [ "$a" = "null" ] && [ "$prev" = "-f" ]; then is_probe=1; fi
              prev="$a"
            done
            if [ "$is_probe" = "1" ]; then
              if [ "$encoder" = "h264_nvenc" ] && [ -f "__PROBE_BUSY__" ]; then
                echo "OpenEncodeSessionEx failed: out of memory (10)" >&2
                exit 1
              fi
              exit 0
            fi
            if [ "$encoder" = "h264_nvenc" ]; then
              echo "[h264_nvenc] OpenEncodeSessionEx failed: out of memory (10): (no details)" >&2
              exit 1
            fi
            # Write each *.mp4 output argument
            for a in "$@"; do
              case "$a" in
                *.mp4) printf 'mp4data' > "$a" ;;
              esac
            done
            exit 0
            """.Replace("__PROBE_BUSY__", probeBusyFlag));

        File.WriteAllText(ffprobePath, """
            #!/bin/sh
            for a in "$@"; do
              if [ "$a" = "nokey" ]; then
                printf '{"frames":[{"pts_time":"0.0"},{"pts_time":"2.0"},{"pts_time":"4.0"},{"pts_time":"6.0"},{"pts_time":"8.0"},{"pts_time":"10.0"}]}\n'
                exit 0
              fi
            done
            # Return matching width/height based on filename
            w=1280; h=720
            for a in "$@"; do
              case "$a" in
                *360p.mp4) w=640; h=360 ;;
                *480p.mp4) w=854; h=480 ;;
                *720p.mp4) w=1280; h=720 ;;
              esac
            done
            printf '{"streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p","width":%s,"height":%s,"avg_frame_rate":"30/1"},{"codec_type":"audio","codec_name":"aac","channels":2}],"format":{"duration":"12.0"}}\n' "$w" "$h"
            exit 0
            """);

        File.SetUnixFileMode(ffmpegPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(ffprobePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var clock = new FakeTimeProvider();
        var realMedia = new FfmpegMediaTools(
            _ => Task.FromResult<(string, string)?>((ffmpegPath, ffprobePath)),
            time: clock,
            osOverride: "linux",
            preferOverride: () => "auto",
            findVaapiDevices: () => Array.Empty<string>());

        // 1. Probe succeeds -> h264_nvenc; real encode hits OpenEncodeSessionEx -> falls back locally to libx264
        var (item, _, api, work) = Setup();
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await Step(api, work, realMedia, time: clock).RunAsync(item, context, default));
        Assert.Equal("Tools test, ffmpeg libx264", item.Renditions!.Encoder);
        Assert.Contains(context.Lines, l => l.Code == "encoder_fallback");

        // 2. Within 30s cooldown, NVENC is skipped without re-probing (returns libx264)
        Assert.Equal("libx264", await realMedia.PickEncoderAsync(default));

        // 3. After 31s cooldown expires, NVENC is NOT permanently blacklisted and is selected again
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal("h264_nvenc", await realMedia.PickEncoderAsync(default));

        // 4. Probe-time refusal also does not permanently cache libx264
        var realMedia2 = new FfmpegMediaTools(
            _ => Task.FromResult<(string, string)?>((ffmpegPath, ffprobePath)),
            time: clock,
            osOverride: "linux",
            preferOverride: () => "auto",
            findVaapiDevices: () => Array.Empty<string>());
        File.WriteAllText(probeBusyFlag, "1");
        Assert.Equal("libx264", await realMedia2.PickEncoderAsync(default));
        File.Delete(probeBusyFlag);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal("h264_nvenc", await realMedia2.PickEncoderAsync(default));
    }

    [Fact]
    public async Task A_file_the_channel_already_has_is_never_encoded()
    {
        var (item, server, api, work) = Setup();
        server.Inner.AlreadyImportedSha = item.Sha256;
        var media = new FakeMedia();
        Assert.Equal(StepResult.SkipRest, await Step(api, work, media).RunAsync(item, new RecordingContext(), default));
        Assert.Equal(0, media.Encodes);
    }

    [Fact]
    public async Task The_upload_sends_the_renditions_before_the_original_and_resends_only_what_the_server_lost()
    {
        var (item, server, api, work) = Setup();
        await Step(api, work, new FakeMedia()).RunAsync(item, new RecordingContext(), default);
        var folder = item.Renditions!.Directory;
        server.LoseOnce.Add("480p/seg_00002.m4s");
        var upload = new UploadStep(api, new TusClient(server), new Uri("https://videos.api.example"), _ => Task.FromResult<string?>("tok"));
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await upload.RunAsync(item, context, default));

        Assert.Equal(new[] { "stage", "complete", "complete", "tus" }, server.Order);
        Assert.Equal(12, server.Stored.Count);
        Assert.Equal("Tools test, ffmpeg libx264", server.Staged!["encoder"]!.GetValue<string>());
        Assert.True(item.Renditions!.Sent);
        Assert.False(Directory.Exists(folder));
        Assert.Contains(context.Lines, l => l.Code == "renditions_sent");
        Assert.Contains("item-1", server.Inner.Uploaded);
        Assert.Equal(1, context.LastProgress);
    }

    [Fact]
    public async Task Renditions_the_server_no_longer_takes_are_dropped_and_the_original_still_goes()
    {
        var (item, server, api, work) = Setup();
        await Step(api, work, new FakeMedia()).RunAsync(item, new RecordingContext(), default);
        server.StageRefusal = "too_late";
        var context = new RecordingContext();
        var upload = new UploadStep(api, new TusClient(server), new Uri("https://videos.api.example"), _ => Task.FromResult<string?>("tok"));
        Assert.Equal(StepResult.Done, await upload.RunAsync(item, context, default));
        Assert.Null(item.Renditions);
        Assert.Contains(context.Lines, l => l.Code == "renditions_dropped");
        Assert.Contains("tus", server.Order);
    }

    [Fact]
    public async Task The_check_waits_for_the_servers_transcode_then_records_its_verdict()
    {
        var (item, server, api, _) = Setup();
        item.ServerItemId = "item-1";
        item.Renditions = new RenditionSet { Sent = true, SentAt = DateTimeOffset.UtcNow };
        var check = new CheckStep(api);

        server.R2Status = "transcoding";
        var waiting = new RecordingContext();
        Assert.Equal(StepResult.Later, await check.RunAsync(item, waiting, default));
        Assert.Equal(TimeSpan.FromSeconds(20), waiting.LaterDelay);

        server.R2Status = "ready";
        server.Prebuilt = "{\"used\":false,\"reason\":\"keyframes\",\"rung\":\"720p\"}";
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await check.RunAsync(item, context, default));
        Assert.Equal(new RenditionVerdict(false, "keyframes", "720p"), item.Verdict);
        Assert.Contains(context.Lines, l => l.Code == "renditions_rejected" && l.Level == LogLevel.Warning);

        server.Prebuilt = "{\"used\":true}";
        Assert.Equal(StepResult.Done, await check.RunAsync(item, new RecordingContext(), default));
        Assert.True(item.Verdict!.Used);

        Assert.Equal(StepResult.Skipped, await check.RunAsync(QueueItem.For(item.Video, "b1", "ch1", DateTimeOffset.UtcNow), new RecordingContext(), default));
    }
}

public class QueueWaitingTests
{
    [Fact]
    public async Task A_step_waiting_on_the_server_holds_no_slot_counts_no_attempt_and_logs_once()
    {
        var runs = 0;
        var check = new ScriptedStep(ImportStepKind.Check, (_, ctx, _) =>
        {
            if (++runs < 3)
            {
                ctx.Later(TimeSpan.Zero);
                return Task.FromResult(StepResult.Later);
            }
            return Task.FromResult(StepResult.Done);
        });
        using var queue = new ImportQueue(Path.Combine(Temp.Folder(), "queue.json"), ImportQueueTests.All(check));
        var item = QueueItem.For(new ScannedVideo("/x/a.mp4", 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = "a" }, Array.Empty<ScanNote>()), "b1", "ch1", DateTimeOffset.UtcNow);
        queue.Add(new[] { item });
        await Temp.WaitUntil(() =>
        {
            queue.Pump();
            return queue.Items[0].Phase == ItemPhase.Done;
        });
        Assert.Equal(3, runs);
        Assert.Equal(1, queue.Items[0].Steps.Single(s => s.Kind == ImportStepKind.Check).Attempts);
        Assert.Single(queue.LogLines(item.Id), l => l.Code == "server_working");
        Assert.Single(queue.LogLines(item.Id), l => l.Code == "started" && l.Args![0] == "Check");
    }

    [Fact]
    public void A_queue_saved_before_the_encode_and_check_steps_loads_with_them_as_nothing_to_do()
    {
        var path = Path.Combine(Temp.Folder(), "queue.json");
        File.WriteAllText(path, """
            {"settings":{"parallel":{"Hash":2,"Upload":3,"Attach":2},"maxAttempts":6},"items":[{"id":"i1","batchId":"b1","channelId":"ch1",
            "video":{"path":"/x/a.mp4","size":10,"lastModifiedMs":0,"mimeType":"video/mp4","captions":[],"metadata":{"fallbackTitle":"a"},"notes":[]},
            "steps":[{"kind":"Hash","status":"Done"},{"kind":"Upload","status":"Pending"},{"kind":"Attach","status":"Pending"}],"log":[]}]}
            """);
        using var queue = new ImportQueue(path, Array.Empty<IImportStep>());
        var item = Assert.Single(queue.Items);
        Assert.Equal(Enum.GetValues<ImportStepKind>(), item.Steps.Select(s => s.Kind));
        Assert.Equal(StepStatus.Skipped, item.Steps.Single(s => s.Kind == ImportStepKind.Encode).Status);
        Assert.Equal(StepStatus.Skipped, item.Steps.Single(s => s.Kind == ImportStepKind.Check).Status);
        Assert.Equal(ImportStepKind.Upload, item.CurrentStep!.Kind);
        Assert.Equal(1, queue.Settings.ParallelFor(ImportStepKind.Encode));
    }
}

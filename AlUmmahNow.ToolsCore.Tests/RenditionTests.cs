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
    public void Picks_libx264_first_then_hardware_then_openh264()
    {
        const string list = """
             V....D libopenh264          OpenH264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
             V....D h264_videotoolbox    VideoToolbox H.264 Encoder (codec h264)
             A....D aac                  AAC (Advanced Audio Coding)
            """;
        Assert.Equal("h264_videotoolbox", H264Encoders.Pick(list));
        Assert.Equal("libx264", H264Encoders.Pick(list + "\n V....D libx264              libx264 H.264\n"));
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
        var candidates = H264Encoders.Candidates(lgplLinuxList);
        Assert.Equal(new[] { "h264_nvenc", "h264_qsv", "h264_amf", "h264_vaapi", "libopenh264" }, candidates);

        // Dual-GPU Linux machine: nvenc/qsv/amf fail probe, renderD128 (dGPU) fails VAAPI, renderD129 (iGPU) succeeds
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
        Assert.Equal(new[] { "h264_nvenc", "h264_qsv", "h264_amf", "h264_vaapi@/dev/dri/renderD128", "h264_vaapi@/dev/dri/renderD129" }, probed);

        // When every hardware probe fails, falls through cleanly to software libopenh264
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
    public void Vaapi_passes_render_device_hwupload_vbr_options_and_probe_arguments()
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

        var probe = string.Join(" ", H264Encoders.ProbeArguments("h264_vaapi", "/dev/dri/renderD129"));
        Assert.Contains("-vaapi_device /dev/dri/renderD129", probe);
        Assert.Contains("-vf format=nv12,hwupload", probe);
        Assert.Contains("-c:v h264_vaapi -frames:v 1 -f null -", probe);
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

/// <summary>ffmpeg for tests: writes a file per rung, or fails as told.</summary>
internal sealed class FakeMedia : IMediaTools
{
    public SourceInfo Info = new(1280, 720, 0, 30, true, 12);
    public Exception? Fail;
    public HashSet<string> FailEncoders = new(StringComparer.Ordinal);
    public List<string> EncoderOrder = new();
    public List<string> MarkedFailed = new();
    public string? Encoder = "libx264";
    public int Encodes;

    public Task<SourceInfo> ProbeAsync(string path, CancellationToken ct) => Fail is LocalEncodeException { Code: "probe_failed" } ? throw Fail : Task.FromResult(Info);

    public Task<string?> PickEncoderAsync(CancellationToken ct)
    {
        if (EncoderOrder.Count > 0)
        {
            return Task.FromResult(EncoderOrder.FirstOrDefault(e => !MarkedFailed.Contains(e)));
        }
        return Task.FromResult(Encoder);
    }

    public void MarkEncoderFailed(string encoder) => MarkedFailed.Add(encoder);

    public Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct)
    {
        Encodes++;
        if (FailEncoders.Contains(encoder)) throw new LocalEncodeException("ffmpeg_failed", $"ffmpeg stopped ({encoder}): driver error");
        if (Fail is not null) throw Fail;
        foreach (var o in outputs) File.WriteAllBytes(o, new byte[64]);
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
    public Task PackageAsync(string mp4, string outputDirectory, int segmentSeconds, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "index.m3u8"), "#EXTM3U\n");
        File.WriteAllBytes(Path.Combine(outputDirectory, "init.mp4"), new byte[10]);
        File.WriteAllBytes(Path.Combine(outputDirectory, "seg_00001.m4s"), new byte[100]);
        File.WriteAllBytes(Path.Combine(outputDirectory, "seg_00002.m4s"), new byte[90]);
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

    private static EncodeStep Step(ImportApiClient api, string work, FakeMedia media, List<Exception>? reported = null) =>
        new(api, new Uri("https://videos.api.example"), media, new FakePackager(), work, "Tools test", (ex, _) => reported?.Add(ex), _ => long.MaxValue);

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

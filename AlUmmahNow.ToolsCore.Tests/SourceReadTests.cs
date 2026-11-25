using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Import.Sources;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>Whether a local encode read its source to the end, and why it did not (production, 2026-11-25).</summary>
public sealed class SourceReadTests
{
    /// <summary>A Matroska file of ten seconds with a subtitle that ends at forty, as ffprobe 8.1 says it.</summary>
    private const string LongerContainer = """
        {"streams":[
          {"codec_type":"video","width":1280,"height":720,"avg_frame_rate":"25/1","tags":{"DURATION":"00:00:10.023000000"}},
          {"codec_type":"audio","tags":{"DURATION":"00:00:10.023000000"}},
          {"codec_type":"subtitle","duration":"40.023000","tags":{"DURATION":"00:00:40.023000000"}}],
         "format":{"duration":"40.023000"}}
        """;

    /// <summary>The end of a run's messages: what went wrong, then what its encoders say when they close.</summary>
    private const string Messages = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'PATH':
        [in#0/mov,mp4,m4a,3gp,3g2,mj2 @ 0x1] [error] Error during demuxing: REASON
        [libx264 @ 0x2] kb/s:86.47
        [aac @ 0x3] Qavg: 534.645
        """;

    private static (QueueItem Item, EncodeStep Step, List<Exception> Reported, RecordingContext Context, string Work) Rig(IMediaTools media, SourceAccess? access = null, long? free = null, Func<double, long, TimeSpan>? limit = null)
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "lesson.mkv", 2500);
        var item = QueueItem.For(FolderScanner.Scan(dir, false, null).Videos[0], "b1", "ch1", DateTimeOffset.UtcNow);
        item.Sha256 = Temp.Sha(item.Video.Path);
        item.FingerprintHash = new string('c', 64);
        item.VideoChecked = item.VideoProbed = true;
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), new RenditionServer());
        var reported = new List<Exception>();
        var work = Temp.Folder();
        var step = new EncodeStep(api, new Uri("https://videos.api.example"), media, new FakePackager(), work, "Tools test", (ex, _) => reported.Add(ex), _ => free ?? long.MaxValue, access)
        {
            SpacePause = TimeSpan.FromMilliseconds(20),
            Limit = limit ?? ((_, _) => TimeSpan.FromHours(1)),
        };
        return (item, step, reported, new RecordingContext(), work);
    }

    [Fact]
    public async Task A_container_longer_than_its_video_and_sound_is_encoded_here()
    {
        var info = FfmpegMediaTools.ParseProbe(LongerContainer);
        var media = new FakeMedia { Info = info, Output = FfmpegMediaTools.ParseProbe(LongerContainer.Replace("40.023000", "10.023000", StringComparison.Ordinal)) };
        var rig = Rig(media);
        Assert.Equal(StepResult.Done, await rig.Step.RunAsync(rig.Item, rig.Context, default));
        Assert.Empty(rig.Reported);
        Assert.NotNull(rig.Item.Renditions);
    }

    [Fact]
    public async Task A_video_that_stopped_half_way_is_still_not_sent()
    {
        var info = FfmpegMediaTools.ParseProbe(LongerContainer);
        var media = new FakeMedia { Info = info, Output = new SourceInfo(1280, 720, 0, 25, true, 5) };
        var rig = Rig(media);
        Assert.Equal(StepResult.Skipped, await rig.Step.RunAsync(rig.Item, rig.Context, default));
        Assert.Equal("too_short", Assert.IsType<LocalEncodeException>(Assert.Single(rig.Reported)).Code);
    }

    /// <summary>An ffmpeg and an ffprobe that say and make what they are told.</summary>
    private static FfmpegMediaTools? Tools(string source, string reason, int code, double seconds, bool network = false) =>
        Tools(source, reason, code, "{\"streams\":[{\"codec_type\":\"video\",\"width\":640,\"height\":360}],\"format\":{\"duration\":\"" + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}}", network);

    private static FfmpegMediaTools? Tools(string source, string reason, int code, string probe, bool network, string before = "", string probeSays = "")
    {
        if (OperatingSystem.IsWindows()) return null;
        var dir = Temp.Folder();
        File.WriteAllText(Path.Combine(dir, "said"), Messages.Replace("PATH", source, StringComparison.Ordinal).Replace("REASON", reason, StringComparison.Ordinal) + "\n");
        string Script(string name, string body)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
        var ffmpeg = Script("ffmpeg", $"{before}cat '{Path.Combine(dir, "said")}' >&2; exit {code}");
        var ffprobe = Script("ffprobe", $"echo '{probe}'; echo '{probeSays}' >&2");
        return new FfmpegMediaTools(_ => Task.FromResult<(string, string)?>((ffmpeg, ffprobe)), network ? Share() : null) { StallLimit = TimeSpan.FromSeconds(2), ProbeLimit = TimeSpan.FromSeconds(20) };
    }

    private sealed class OnAShare : INetworkPaths
    {
        public string? Mapped(string path) => null;
        public bool IsNetwork(string path) => true;
    }

    private static SourceAccess Share() => new(new OnAShare(), new SourceFileSystem(), delay: (_, _) => Task.CompletedTask);

    private static Task Encode(FfmpegMediaTools tools, string source) =>
        tools.EncodeAsync(source, new SourceInfo(640, 360, 0, 25, false, 10), Array.Empty<PlannedRung>(), new[] { "/w/360p.mp4" }, "libx264", Ladder.Settings.Ladder, new Progress<double>(), default);

    [Theory]
    [InlineData("Input/output error", "source_read")]
    [InlineData("Invalid data found when processing input", "ffmpeg_failed")]
    public async Task A_run_that_failed_says_its_error_never_the_encoders_closing_figures(string reason, string code)
    {
        if (Tools("/media/a.mp4", reason, 1, 10) is not { } tools) return;
        var failed = await Assert.ThrowsAsync<LocalEncodeException>(() => Encode(tools, "/media/a.mp4"));
        Assert.Equal(code, failed.Code);
        Assert.Contains("Error during demuxing: " + reason, failed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Qavg", failed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("[error]", failed.Message, StringComparison.Ordinal);
    }

    [Theory]
    // Windows' network errors reach ffmpeg as "Invalid argument", and it ends well all the same
    [InlineData("Invalid argument", 10, false)]
    [InlineData("Invalid argument", 9.5, false)]
    [InlineData("Invalid argument", 8.5, true)]
    [InlineData("Input/output error", 4, true)]
    [InlineData("Invalid data found when processing input", 4, false)]
    public async Task On_a_share_a_run_that_ended_well_is_judged_by_how_much_it_made(string reason, double made, bool stopped)
    {
        if (Tools(@"\\nas\v\a.mp4", reason, 0, made, network: true) is not { } tools) return;
        var encode = () => Encode(tools, @"\\nas\v\a.mp4");
        if (!stopped)
        {
            await encode();
            return;
        }
        var failed = await Assert.ThrowsAsync<LocalEncodeException>(encode);
        Assert.Equal("source_read", failed.Code);
        Assert.Contains("(0): ", failed.Message, StringComparison.Ordinal);
        Assert.Contains("Error during demuxing: " + reason, failed.Message, StringComparison.Ordinal);
    }

    /// <summary>Cannot read the share, only a copy.</summary>
    private sealed class OnlyCopies : IMediaTools
    {
        private readonly FakeMedia _inner = new();
        public readonly List<string> Encoded = new();
        public bool Never;
        public bool NotEvenProbed;
        public Action? OnEncode;
        public Task<SourceInfo> ProbeAsync(string path, CancellationToken ct) => NotEvenProbed && Path.GetFileName(path) == "lesson.mkv"
            ? throw new LocalEncodeException("source_read", "ffmpeg could not read the file to its end (0): x")
            : _inner.ProbeAsync(path, ct);
        public Task<string?> PickEncoderAsync(CancellationToken ct) => _inner.PickEncoderAsync(ct);
        public Task ExtractAudioAsync(string source, string wavPath, CancellationToken ct) => throw new NotSupportedException();
        public Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct)
        {
            OnEncode?.Invoke();
            Encoded.Add(Path.GetFileName(source) + " " + new FileInfo(source).Length);
            if (Never || Path.GetFileName(source) != "source.mkv") throw new LocalEncodeException("source_read", "ffmpeg could not read the file to its end (0): x");
            return _inner.EncodeAsync(source, info, rungs, outputs, encoder, settings, progress, ct);
        }
    }

    [Fact]
    public async Task What_ffmpeg_cannot_read_from_a_share_twice_is_encoded_from_a_copy()
    {
        var media = new OnlyCopies();
        var rig = Rig(media, Share());
        Assert.Equal(StepResult.Done, await rig.Step.RunAsync(rig.Item, rig.Context, default));
        Assert.Equal(new[] { "lesson.mkv 2500", "lesson.mkv 2500", "source.mkv 2500" }, media.Encoded);
        Assert.Empty(rig.Reported);
        Assert.Contains(rig.Context.Lines, l => l.Code == "encode_copy");
        // The copy goes; nothing but the renditions is left
        Assert.Empty(Directory.GetFiles(rig.Item.Renditions!.Directory));
    }

    [Fact]
    public async Task Without_room_for_a_copy_or_when_that_fails_too_the_server_encodes_it()
    {
        var info = new FakeMedia().Info;
        var estimate = EncodeStep.Estimate(info, EncodeLadder.For(info, Ladder.Settings.Ladder));
        var full = Rig(new OnlyCopies(), Share(), free: EncodeStep.SpaceMargin + estimate + 2499);
        Assert.Equal(StepResult.Skipped, await full.Step.RunAsync(full.Item, full.Context, default));
        Assert.Equal("source_read", Assert.IsType<LocalEncodeException>(Assert.Single(full.Reported)).Code);

        var never = new OnlyCopies { Never = true };
        var rig = Rig(never, Share());
        Assert.Equal(StepResult.Skipped, await rig.Step.RunAsync(rig.Item, rig.Context, default));
        Assert.Equal(3, never.Encoded.Count);
        Assert.Single(rig.Reported);
        Assert.Null(rig.Item.Renditions);

        // On this computer's own disk: once, and no copy
        var local = new OnlyCopies();
        var here = Rig(local);
        Assert.Equal(StepResult.Skipped, await here.Step.RunAsync(here.Item, here.Context, default));
        Assert.Single(local.Encoded);
    }

    [Fact]
    public async Task What_cannot_even_be_probed_on_the_share_is_probed_and_encoded_from_the_copy()
    {
        var media = new OnlyCopies { NotEvenProbed = true };
        var rig = Rig(media, Share());
        Assert.Equal(StepResult.Done, await rig.Step.RunAsync(rig.Item, rig.Context, default));
        Assert.Equal(new[] { "source.mkv 2500" }, media.Encoded);
        Assert.Empty(Directory.GetFiles(rig.Item.Renditions!.Directory));
    }

    [Fact]
    public async Task A_probe_that_ended_well_but_could_not_read_the_share_is_no_probe()
    {
        // As it was read, the file has no sound
        const string probe = """{"streams":[{"codec_type":"video","width":640,"height":360,"duration":"10"}],"format":{"duration":"10"}}""";
        if (Tools(@"\\nas\v\a.mp4", "x", 0, probe, network: true, probeSays: "[mov @ 0x1] Error reading header: Invalid argument") is not { } tools) return;
        Assert.Equal("source_read", (await Assert.ThrowsAsync<LocalEncodeException>(() => tools.ProbeAsync(@"\\nas\v\a.mp4", default))).Code);
    }

    /// <summary>The bytes the copy reads are others, and the file's size and modified time are the same.</summary>
    [Fact]
    public async Task A_copy_of_other_bytes_than_were_checked_is_not_encoded()
    {
        var media = new OnlyCopies();
        var rig = Rig(media, Share());
        media.OnEncode = () =>
        {
            var at = File.GetLastWriteTimeUtc(rig.Item.Video.Path);
            File.WriteAllBytes(rig.Item.Video.Path, new byte[2500]);
            File.SetLastWriteTimeUtc(rig.Item.Video.Path, at);
        };
        Assert.Equal(StepResult.Skipped, await rig.Step.RunAsync(rig.Item, rig.Context, default));
        Assert.Equal("copy_differs", Assert.IsType<LocalEncodeException>(Assert.Single(rig.Reported)).Code);
        Assert.DoesNotContain(media.Encoded, e => e.StartsWith("source", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(rig.Work, rig.Item.Id)));
    }

    [Fact]
    public async Task A_file_that_changes_before_it_is_copied_is_the_files_own_failure()
    {
        var media = new OnlyCopies();
        var rig = Rig(media, Share());
        media.OnEncode = () => File.WriteAllBytes(rig.Item.Video.Path, new byte[2600]);
        var failed = await Assert.ThrowsAsync<StepFailedException>(() => rig.Step.RunAsync(rig.Item, rig.Context, default));
        Assert.Contains(failed.Code, new[] { "file_changed", "file_writing" });
        Assert.False(Directory.Exists(Path.Combine(rig.Work, rig.Item.Id)));
    }

    /// <summary>Paused, removed, left to the server or the app closing: one cancellation.</summary>
    [Fact]
    public async Task Stopped_while_it_is_copied_nothing_is_left()
    {
        var media = new OnlyCopies();
        var rig = Rig(media, Share());
        using var stop = new CancellationTokenSource();
        var folder = Path.Combine(rig.Work, rig.Item.Id);
        var copying = new List<bool>();
        rig.Context.OnCopying = on =>
        {
            copying.Add(on);
            if (on) stop.Cancel();
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Step.RunAsync(rig.Item, rig.Context, stop.Token));
        Assert.Equal(new[] { true, false }, copying);
        Assert.False(Directory.Exists(folder));
        Assert.Null(rig.Item.Renditions);
        Assert.Empty(rig.Reported);
    }

    [Fact]
    public async Task The_copy_has_its_own_progress()
    {
        var rig = Rig(new OnlyCopies(), Share());
        var at = new List<double>();
        rig.Context.OnCopying = on => at.Add(on ? -1 : rig.Context.LastProgress);
        Assert.Equal(StepResult.Done, await rig.Step.RunAsync(rig.Item, rig.Context, default));
        Assert.Equal(new[] { -1.0, 1.0 }, at);
    }

    /// <summary>Encodes until it is let go.</summary>
    private sealed class Held : IMediaTools
    {
        private readonly FakeMedia _inner = new();
        public readonly SemaphoreSlim Go = new(0);
        public int Running;
        public Task<SourceInfo> ProbeAsync(string path, CancellationToken ct) => _inner.ProbeAsync(path, ct);
        public Task<string?> PickEncoderAsync(CancellationToken ct) => _inner.PickEncoderAsync(ct);
        public Task ExtractAudioAsync(string source, string wavPath, CancellationToken ct) => throw new NotSupportedException();
        public async Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct)
        {
            Interlocked.Increment(ref Running);
            await Go.WaitAsync(ct);
            await _inner.EncodeAsync(source, info, rungs, outputs, encoder, settings, progress, ct);
        }
    }

    [Fact]
    public async Task Two_encodes_never_take_the_same_space()
    {
        var info = new FakeMedia().Info;
        var estimate = EncodeStep.Estimate(info, EncodeLadder.For(info, Ladder.Settings.Ladder));
        var media = new Held();
        // Room for one at a time
        var rig = Rig(media, free: EncodeStep.SpaceMargin + (estimate * 3 / 2));
        var other = QueueItem.For(rig.Item.Video, "b1", "ch1", DateTimeOffset.UtcNow);
        (other.Sha256, other.FingerprintHash, other.VideoChecked, other.VideoProbed) = (rig.Item.Sha256, rig.Item.FingerprintHash, true, true);
        var waits = new RecordingContext();
        var first = rig.Step.RunAsync(rig.Item, rig.Context, default);
        await Temp.WaitUntil(() => media.Running == 1);
        var second = rig.Step.RunAsync(other, waits, default);
        await Temp.WaitUntil(() => waits.Wait == StepWait.Space);
        Assert.Equal(1, media.Running);
        media.Go.Release();
        Assert.Equal(StepResult.Done, await first);
        await Temp.WaitUntil(() => media.Running == 2);
        Assert.Equal(StepWait.None, waits.Wait);
        media.Go.Release();
        Assert.Equal(StepResult.Done, await second);

        // And one there never is room for is the server's
        var full = Rig(media, free: EncodeStep.SpaceMargin + estimate - 1);
        Assert.Equal(StepResult.Skipped, await full.Step.RunAsync(full.Item, full.Context, default));
        Assert.Equal("disk_space", Assert.IsType<LocalEncodeException>(Assert.Single(full.Reported)).Code);
    }

    [Fact]
    public async Task What_takes_too_long_is_the_servers()
    {
        var rig = Rig(new Held(), limit: (_, _) => TimeSpan.FromMilliseconds(100));
        Assert.Equal(StepResult.Skipped, await rig.Step.RunAsync(rig.Item, rig.Context, default).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("took_too_long", Assert.IsType<LocalEncodeException>(Assert.Single(rig.Reported)).Code);
        Assert.False(Directory.Exists(Path.Combine(rig.Work, rig.Item.Id)));
    }

    [Theory]
    [InlineData(true, "source_read")]
    [InlineData(false, "ffmpeg_failed")]
    public async Task An_ffmpeg_that_gets_no_further_is_stopped(bool network, string code)
    {
        if (Tools("/media/a.mp4", "x", 0, "{}", network, before: "i=0; while [ $i -lt 40 ]; do echo out_time_us=1000; sleep 0.5; i=$((i+1)); done; ") is not { } tools) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(code, (await Assert.ThrowsAsync<LocalEncodeException>(() => Encode(tools, "/media/a.mp4"))).Code);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), clock.Elapsed.ToString());
    }

    private static string Made(double video, double? sound)
    {
        static string Text(double n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "{\"streams\":[{\"codec_type\":\"video\",\"width\":640,\"height\":360,\"duration\":\"" + Text(video) + "\"}"
            + (sound is { } s ? ",{\"codec_type\":\"audio\",\"duration\":\"" + Text(s) + "\"}" : "")
            + "],\"format\":{\"duration\":\"" + Text(Math.Max(video, sound ?? 0)) + "\"}}";
    }

    [Theory]
    [InlineData(10, 6.0, false)]
    // Cut off, or not there at all, while the video is whole; and the other way round
    [InlineData(10, 4.5, true)]
    [InlineData(10, 0.0, true)]
    [InlineData(10, null, true)]
    [InlineData(5, 6.0, true)]
    public async Task On_a_share_each_stream_is_judged_by_its_own_length(double video, double? sound, bool stopped)
    {
        if (Tools(@"\\nas\v\a.mp4", "Invalid argument", 0, Made(video, sound), network: true) is not { } tools) return;
        var encode = () => tools.EncodeAsync(@"\\nas\v\a.mp4", new SourceInfo(640, 360, 0, 25, true, 10, 10, 6), Array.Empty<PlannedRung>(), new[] { "/w/360p.mp4" }, "libx264", Ladder.Settings.Ladder, new Progress<double>(), default);
        if (stopped) Assert.Equal("source_read", (await Assert.ThrowsAsync<LocalEncodeException>(encode)).Code);
        else await encode();
    }

    [Theory]
    [InlineData(10, 6.0, true)]
    [InlineData(10, 4.9, false)]
    [InlineData(10, null, false)]
    [InlineData(8.9, 6.0, false)]
    public async Task Nor_is_a_video_sent_whose_sound_stopped_whatever_it_was_read_from(double video, double? sound, bool sent)
    {
        var media = new FakeMedia { Info = new SourceInfo(1280, 720, 0, 25, true, 10, 10, 6), Output = FfmpegMediaTools.ParseProbe(Made(video, sound)) };
        var rig = Rig(media);
        Assert.Equal(sent ? StepResult.Done : StepResult.Skipped, await rig.Step.RunAsync(rig.Item, rig.Context, default));
        if (!sent) Assert.Equal("too_short", Assert.IsType<LocalEncodeException>(Assert.Single(rig.Reported)).Code);
    }

    [Theory]
    // Clips: half of it at least, never nothing
    [InlineData(1.0, 0.0, false)]
    [InlineData(0.8, 0.39, false)]
    [InlineData(0.8, 0.41, true)]
    [InlineData(0.04, 0.04, true)]
    [InlineData(2.0, 0.9, false)]
    [InlineData(2.0, 1.0, true)]
    // An hour, thirty seconds short
    [InlineData(3600, 3570, false)]
    [InlineData(3600, 3599.2, true)]
    [InlineData(0, 5, true)]
    [InlineData(0, 0, false)]
    public void A_clip_is_held_to_its_own_length(double source, double made, bool whole)
    {
        var info = new SourceInfo(640, 360, 0, 25, false, source, source);
        Assert.Equal(whole, info.Missing(new SourceInfo(640, 360, 0, 25, false, made, made)) is null);
        // The same for what does not say how long its video is
        Assert.Equal(whole, (info with { VideoSeconds = 0 }).Missing(new SourceInfo(640, 360, 0, 25, false, made)) is null);
    }

    [Fact]
    public void What_a_stream_does_not_say_is_the_containers_length_and_a_trimmed_one_no_longer_than_it()
    {
        var unsaid = new SourceInfo(640, 360, 0, 25, true, 10, 10, 0);
        Assert.NotNull(unsaid.Missing(new SourceInfo(640, 360, 0, 25, true, 10, 10, 6)));
        Assert.Null(unsaid.Missing(new SourceInfo(640, 360, 0, 25, true, 10, 10, 9.5)));
        Assert.Null(new SourceInfo(640, 360, 0, 25, true, 6.7, 10, 10).Missing(new SourceInfo(640, 360, 0, 25, true, 6.7, 6.68, 6.7)));
    }

    /// <summary>A track that starts late: Matroska's tag is where it ends.</summary>
    [Fact]
    public void A_late_track_is_as_long_as_what_it_has()
    {
        var info = FfmpegMediaTools.ParseProbe("""{"streams":[{"codec_type":"video","width":4,"height":4,"start_time":"0.000000","tags":{"DURATION":"00:00:10.000000000"}},{"codec_type":"audio","start_time":"4.000000","tags":{"DURATION":"00:00:10.023000000"}}],"format":{"duration":"10.023"}}""");
        Assert.Equal((10.0, 6.023), (info.VideoSeconds, Math.Round(info.AudioSeconds, 3)));
    }

    [Fact]
    public void A_flood_of_warnings_or_errors_does_not_push_out_the_line_about_reading()
    {
        var said = new FfmpegMessages();
        said.Add("[error] first");
        for (var i = 0; i < 3000; i++) said.Add($"[h264 @ 0x1] [error] error while decoding MB {i} {new string('x', 60)}");
        said.Add(@"[in#0/mov @ 0x1] [error] Error during demuxing: Invalid argument");
        for (var i = 0; i < 6000; i++) said.Add($"[out#0 @ 0x1] [warning] Non-monotonic DTS {i} {new string('x', 60)}");
        var text = said.ToString();
        Assert.True(text.Length < 200_000, text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotNull(FfmpegMediaTools.StoppedLine(@"\\nas\v\a.mp4", text, network: true));
        Assert.Contains("[error] first", text, StringComparison.Ordinal);
        Assert.Contains("decoding MB 2999", FfmpegMediaTools.Why(text, "a.mp4"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copies_an_ended_app_left_are_swept_and_go_with_their_file()
    {
        var dir = Temp.Folder();
        var work = new WorkFolders(Path.Combine(dir, "app", "renditions"), Path.Combine(dir, "app", "transcripts"));
        var path = Path.Combine(dir, "queue.json");
        QueueItem Paused(string name)
        {
            var item = QueueItem.For(new ScannedVideo(Path.Combine(dir, name), 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = name }, Array.Empty<ScanNote>()), "b1", "ch1", DateTimeOffset.UtcNow);
            item.Paused = true;
            return item;
        }
        var (left, made) = (Paused("a.mp4"), Paused("b.mp4"));
        made.Renditions = new RenditionSet { Directory = Path.Combine(work.Renditions, made.Id) };
        string Put(string id, string name) => Temp.Write(Path.Combine(work.Renditions, id), name, "x");
        var copy = Put(left.Id, "source.mkv");
        var part = Put(left.Id, "360p.mp4");
        var kept = Put(made.Id, Path.Combine("360p", "index.m3u8"));
        var gone = new string('a', 32);
        var orphan = Put(gone, "source.mp4");
        var others = Put(gone, Path.Combine("360p", "index.m3u8"));
        var foreign = Put("not-an-id", "source.mp4");
        using (var queue = new ImportQueue(path, ImportQueueTests.All(), work: work))
        {
            queue.Add(new[] { left, made });
            queue.Start();
            await Temp.WaitUntil(() => !File.Exists(copy) && !File.Exists(part) && !File.Exists(orphan));
            Assert.All(new[] { kept, others, foreign }, f => Assert.True(File.Exists(f), f));
            await queue.StopAsync();
            // Left since, by an encode that did not end in time: it goes with its file
            copy = Put(left.Id, "source.mkv");
            queue.Remove(left.Id);
            await Temp.WaitUntil(() => !Directory.Exists(Path.Combine(work.Renditions, left.Id)));
            Assert.True(File.Exists(kept));
        }
    }

    [Theory]
    [InlineData("""{"streams":[{"codec_type":"video","width":4,"height":4,"duration":"10.0"},{"codec_type":"audio","duration":"12.5"},{"codec_type":"data","duration":"90.0"}],"format":{"duration":"90.0"}}""", 12.5)]
    [InlineData("""{"streams":[{"codec_type":"video","width":4,"height":4,"duration":"10.0"}],"format":{"duration":"10.2"}}""", 10.0)]
    [InlineData("""{"streams":[{"codec_type":"video","width":4,"height":4,"duration":"10.0"},{"codec_type":"audio"}],"format":{"duration":"12.0"}}""", 12.0)]
    [InlineData("""{"streams":[{"codec_type":"video","width":4,"height":4},{"codec_type":"audio","duration":"9.0"}],"format":{"duration":"12.0"}}""", 12.0)]
    [InlineData("""{"streams":[{"codec_type":"video","width":4,"height":4,"tags":{"DURATION-eng":"00:00:09.000000000","DURATION":"nonsense"}}],"format":{"duration":"12.0"}}""", 12.0)]
    [InlineData("""{"streams":[{"codec_type":"video","width":4,"height":4,"tags":{"duration":"01:02:03.500000000"}}],"format":{"duration":"9999"}}""", 3723.5)]
    public void A_source_is_as_long_as_its_video_and_sound(string json, double seconds) =>
        Assert.Equal(seconds, FfmpegMediaTools.ParseProbe(json).Seconds, 3);
}

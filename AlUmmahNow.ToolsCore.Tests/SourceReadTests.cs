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

    private static (QueueItem Item, EncodeStep Step, List<Exception> Reported, RecordingContext Context) Rig(IMediaTools media, SourceAccess? access = null, long? free = null)
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "lesson.mkv", 2500);
        var item = QueueItem.For(FolderScanner.Scan(dir, false, null).Videos[0], "b1", "ch1", DateTimeOffset.UtcNow);
        item.Sha256 = Temp.Sha(item.Video.Path);
        item.FingerprintHash = new string('c', 64);
        item.VideoChecked = item.VideoProbed = true;
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), new RenditionServer());
        var reported = new List<Exception>();
        return (item, new EncodeStep(api, new Uri("https://videos.api.example"), media, new FakePackager(), Temp.Folder(), "Tools test", (ex, _) => reported.Add(ex), _ => free ?? long.MaxValue, access), reported, new RecordingContext());
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
    private static FfmpegMediaTools? Tools(string source, string reason, int code, double seconds, bool network = false)
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
        var ffmpeg = Script("ffmpeg", $"cat '{Path.Combine(dir, "said")}' >&2; exit {code}");
        var ffprobe = Script("ffprobe", "echo '{\"streams\":[{\"codec_type\":\"video\",\"width\":640,\"height\":360}],\"format\":{\"duration\":\"" + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}}'");
        return new FfmpegMediaTools(_ => Task.FromResult<(string, string)?>((ffmpeg, ffprobe)), network ? Share() : null);
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
        public Task<SourceInfo> ProbeAsync(string path, CancellationToken ct) => _inner.ProbeAsync(path, ct);
        public Task<string?> PickEncoderAsync(CancellationToken ct) => _inner.PickEncoderAsync(ct);
        public Task ExtractAudioAsync(string source, string wavPath, CancellationToken ct) => throw new NotSupportedException();
        public Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct)
        {
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
        var full = Rig(new OnlyCopies(), Share(), free: 17_662_500);
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

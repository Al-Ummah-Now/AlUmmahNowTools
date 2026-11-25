using AlUmmahNow.ToolsCore.Import;
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

    private static (QueueItem Item, EncodeStep Step, List<Exception> Reported, RecordingContext Context) Rig(IMediaTools media)
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "lesson.mkv", 2500);
        var item = QueueItem.For(FolderScanner.Scan(dir, false, null).Videos[0], "b1", "ch1", DateTimeOffset.UtcNow);
        item.Sha256 = Temp.Sha(item.Video.Path);
        item.FingerprintHash = new string('c', 64);
        item.VideoChecked = item.VideoProbed = true;
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), new RenditionServer());
        var reported = new List<Exception>();
        return (item, new EncodeStep(api, new Uri("https://videos.api.example"), media, new FakePackager(), Temp.Folder(), "Tools test", (ex, _) => reported.Add(ex), _ => long.MaxValue), reported, new RecordingContext());
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
    private static FfmpegMediaTools? Tools(string source, string reason, int code, double seconds)
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
        return Make(ffmpeg, ffprobe);
    }

    private static FfmpegMediaTools Make(string ffmpeg, string ffprobe) => new(_ => Task.FromResult<(string, string)?>((ffmpeg, ffprobe)));

    private static Task Encode(FfmpegMediaTools tools, string source) =>
        tools.EncodeAsync(source, new SourceInfo(640, 360, 0, 25, false, 10), Array.Empty<PlannedRung>(), Array.Empty<string>(), "libx264", Ladder.Settings.Ladder, new Progress<double>(), default);

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
}

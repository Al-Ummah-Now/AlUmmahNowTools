using System.Text.Json;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Importer;
using AlUmmahNow.Importer.Core;
using AlUmmahNow.Tests.Infrastructure;
using CoreImage = AlUmmahNow.Importer.Core.Image;
using CoreVideo = AlUmmahNow.Importer.Core.Video;

namespace AlUmmahNow.Tests.Importer;

/// <summary>
/// The importer keeps a source thumbnail only when it has the video's shape, otherwise uploads a frame
/// from the video, and writes the video's displayed width / height into the record.
/// </summary>
[Trait("Category", "Integration")]
public class ThumbnailShapeImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aun-thumbshape-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteImportDb _db = new();
    private readonly InMemoryStorageClient<CoreVideo> _videos = new();
    private readonly InMemoryStorageClient<CoreImage> _images = new();
    private readonly InMemoryR2Client _r2 = new();
    private readonly List<ThumbnailChoiceEventArgs> _choices = new();

    public ThumbnailShapeImportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Sizes by file name; frames are written as a few bytes recording their requested size.</summary>
    private sealed class FakeProbe : IMediaProbe
    {
        public Dictionary<string, (int, int)> Sizes { get; } = new(StringComparer.Ordinal);
        public List<(int Width, int Height)> Frames { get; } = new();

        public Task<(int Width, int Height)?> GetVideoDisplaySizeAsync(string videoPath)
            => Task.FromResult<(int, int)?>(Sizes.TryGetValue(Path.GetFileName(videoPath), out var s) ? s : null);

        public Task<(int Width, int Height)?> GetImageSizeAsync(string imagePath)
            => Task.FromResult<(int, int)?>(Sizes.TryGetValue(Path.GetFileName(imagePath), out var s) ? s : null);

        public async Task<bool> ExtractFrameAsync(string videoPath, string outputPath, int width, int height)
        {
            lock (Frames) Frames.Add((width, height));
            await File.WriteAllTextAsync(outputPath, $"frame {width}x{height}");
            return true;
        }
    }

    private void Video(string name, string? thumbName = null)
    {
        var path = Path.Combine(_root, "Chan", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "video " + name);
        if (thumbName != null) File.WriteAllText(Path.Combine(_root, "Chan", thumbName), "thumb " + thumbName);
    }

    private async Task ImportAsync(IMediaProbe probe)
    {
        using var db = _db.NewContext();
        var importer = new VideoImporter(new Repository<CoreVideo>(_r2), _videos, _images, new BunnyStreamClient("k"), db)
        {
            MediaProbe = probe,
        };
        importer.ThumbnailChosen += (_, e) => { lock (_choices) _choices.Add(e); };
        await importer.ImportMultiChannelAsync(_root, "u", "https://v/", "https://i/");
    }

    private JsonElement Record(string fileName)
    {
        foreach (var json in _r2.Bucket("videos").Values)
        {
            var doc = JsonDocument.Parse(json).RootElement;
            if (doc.GetProperty("videoUrl").GetString()!.EndsWith("/" + fileName, StringComparison.Ordinal)) return doc;
        }
        throw new KeyNotFoundException(fileName);
    }

    private ThumbnailChoiceEventArgs Choice(string fileName) => _choices.Single(c => c.FileName == fileName);

    [Fact]
    public async Task Matching_art_is_kept_and_mismatched_or_missing_art_is_replaced_by_a_frame()
    {
        var probe = new FakeProbe();
        Video("wide.mp4", "wide.jpg");            probe.Sizes["wide.mp4"] = (1920, 1080); probe.Sizes["wide.jpg"] = (1280, 720);
        Video("odd.mp4", "odd.jpg");              probe.Sizes["odd.mp4"] = (1280, 720);   probe.Sizes["odd.jpg"] = (1280, 696);
        Video("short.mp4", "short.jpg");          probe.Sizes["short.mp4"] = (720, 1280); probe.Sizes["short.jpg"] = (1280, 720);
        Video("square.mp4", "square.jpg");        probe.Sizes["square.mp4"] = (1080, 1080); probe.Sizes["square.jpg"] = (640, 480);
        Video("hq.mp4", "hq.jpg");                probe.Sizes["hq.mp4"] = (1920, 1080);   probe.Sizes["hq.jpg"] = (480, 360);
        Video("bare.mp4");                        probe.Sizes["bare.mp4"] = (1080, 1920);

        await ImportAsync(probe);

        Assert.Equal(ThumbnailDecision.KeepSource, Choice("wide.mp4").Decision);
        Assert.Equal("https://i/Chan/wide.jpg", Record("wide.mp4").GetProperty("thumbnailUrl").GetString());
        Assert.Equal(ThumbnailDecision.KeepSource, Choice("odd.mp4").Decision);
        Assert.Equal("https://i/Chan/odd.jpg", Record("odd.mp4").GetProperty("thumbnailUrl").GetString());

        foreach (var name in new[] { "short", "square", "hq" })
        {
            Assert.Equal(ThumbnailDecision.FrameShapeMismatch, Choice(name + ".mp4").Decision);
            Assert.Equal($"https://i/Chan/{name}.frame.jpg", Record(name + ".mp4").GetProperty("thumbnailUrl").GetString());
            Assert.False(_images.Objects.ContainsKey($"/images-chan/{name}.jpg"), "mismatched art must not be uploaded");
        }
        Assert.Equal(ThumbnailDecision.FrameNoThumbnail, Choice("bare.mp4").Decision);
        Assert.Equal("https://i/Chan/bare.frame.jpg", Record("bare.mp4").GetProperty("thumbnailUrl").GetString());

        // Frames have the video's own shape, longest edge at most 1280 (never upscaled).
        Assert.Equal(new[] { (720, 1280), (1080, 1080), (1280, 720), (720, 1280) }.OrderBy(f => f),
            probe.Frames.OrderBy(f => f));

        Assert.Equal(720, Record("short.mp4").GetProperty("width").GetInt32());
        Assert.Equal(1280, Record("short.mp4").GetProperty("height").GetInt32());
        Assert.Equal(1920, Record("wide.mp4").GetProperty("width").GetInt32());
        Assert.Equal(1080, Record("wide.mp4").GetProperty("height").GetInt32());
        Assert.Contains("shape mismatch", Choice("short.mp4").Describe());
        Assert.Contains("kept source thumbnail", Choice("wide.mp4").Describe());
    }

    [Fact]
    public async Task A_rerun_reuses_the_uploaded_frame()
    {
        var probe = new FakeProbe();
        Video("short.mp4", "short.jpg"); probe.Sizes["short.mp4"] = (720, 1280); probe.Sizes["short.jpg"] = (1280, 720);
        await ImportAsync(probe);
        using (var db = _db.NewContext())
        {
            // Reset the row so the next run imports it again.
            foreach (var v in db.ImportVideos) v.Status = AlUmmahNow.Data.Models.VideoImportStatus.Pending;
            await db.SaveChangesAsync();
        }
        await ImportAsync(probe);
        Assert.Single(probe.Frames);
        Assert.Equal(1, _images.Puts);
    }

    [FfmpegFact]
    public async Task Real_ffmpeg_reads_rotation_and_takes_a_frame_in_the_video_shape()
    {
        var chan = Path.Combine(_root, "Chan");
        Directory.CreateDirectory(chan);
        // A 640x360 landscape encode tagged to display rotated 90 degrees (a phone video held upright).
        var rotated = Path.Combine(chan, "phone.mp4");
        await Ffmpeg($"-f lavfi -i testsrc=size=640x360:rate=10:duration=1 -pix_fmt yuv420p -c:v libx264 -y \"{Path.Combine(chan, "raw.mp4")}\"");
        await Ffmpeg($"-display_rotation 90 -i \"{Path.Combine(chan, "raw.mp4")}\" -c copy -y \"{rotated}\"");
        File.Delete(Path.Combine(chan, "raw.mp4"));
        await Ffmpeg($"-f lavfi -i color=c=red:size=1280x720 -frames:v 1 -y \"{Path.Combine(chan, "phone.jpg")}\"");

        var probe = new FfmpegMediaProbe();
        Assert.Equal((360, 640), await probe.GetVideoDisplaySizeAsync(rotated));
        Assert.Equal((1280, 720), await probe.GetImageSizeAsync(Path.Combine(chan, "phone.jpg")));

        await ImportAsync(probe);

        Assert.Equal(ThumbnailDecision.FrameShapeMismatch, Choice("phone.mp4").Decision);
        var record = Record("phone.mp4");
        Assert.Equal(360, record.GetProperty("width").GetInt32());
        Assert.Equal(640, record.GetProperty("height").GetInt32());
        Assert.Equal("https://i/Chan/phone.frame.jpg", record.GetProperty("thumbnailUrl").GetString());

        var frame = Path.Combine(_root, "frame.jpg");
        await File.WriteAllBytesAsync(frame, _images.Objects["/images-chan/phone.frame.jpg"]);
        Assert.Equal((360, 640), await probe.GetImageSizeAsync(frame));
    }

    private static async Task Ffmpeg(string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", "-hide_banner -loglevel error " + args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.True(p.ExitCode == 0, err);
    }
}

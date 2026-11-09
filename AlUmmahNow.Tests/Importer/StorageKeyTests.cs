using System.Text;
using AlUmmahNow.BunnyBulkUploader;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Importer;
using AlUmmahNow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using CoreImage = AlUmmahNow.Importer.Core.Image;
using CoreVideo = AlUmmahNow.Importer.Core.Video;

namespace AlUmmahNow.Tests.Importer;

[Trait("Category", "Unit")]
public class StorageKeyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aun-keys-" + Guid.NewGuid().ToString("N"));

    public StorageKeyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private FileInfo Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return new FileInfo(path);
    }

    [Fact]
    public void Keys_include_the_sub_folder_and_keep_top_level_keys_unchanged()
    {
        var channel = new ImportChannel { FolderPath = Path.Combine(_root, "ChanA") };
        var top = Write("ChanA/intro.mp4", "x");
        var s1 = Write("ChanA/Season1/ep1.mp4", "x");
        var s2 = Write("ChanA/Season2/ep1.mp4", "x");

        Assert.Equal("intro.mp4", StorageKeys.VideoKey(channel, top));
        Assert.Equal("Season1/ep1.mp4", StorageKeys.VideoKey(channel, s1));
        Assert.Equal("Season2/ep1.mp4", StorageKeys.VideoKey(channel, s2));

        var thumb = Write("ChanA/Season1/.thumbs/ep1.jpg", "t");
        Assert.Equal("Season1/ep1.jpg", StorageKeys.ThumbnailKey("Season1/ep1.mp4", thumb));
        Assert.Equal("a.b.png", StorageKeys.ThumbnailKey("a.b.mp4", new FileInfo("/x/a.b.png")));
    }

    [Fact]
    public void Relative_key_rejects_files_outside_the_channel()
    {
        Assert.Throws<ArgumentException>(() => StorageUploads.RelativeKey(Path.Combine(_root, "A"), Path.Combine(_root, "B", "x.mp4")));
    }

    [Fact]
    public async Task Upload_if_absent_uploads_once_skips_identical_and_refuses_a_different_file()
    {
        var storage = new InMemoryStorageClient<CoreVideo>();
        var file = Write("a.mp4", "first");

        Assert.Equal(UploadOutcome.Uploaded, await storage.UploadIfAbsentAsync(file, "/z/a.mp4"));
        Assert.Equal(UploadOutcome.AlreadyPresent, await storage.UploadIfAbsentAsync(file, "/z/a.mp4"));
        Assert.Equal(1, storage.Puts);

        var other = Write("b.mp4", "a different, longer file");
        var ex = await Assert.ThrowsAsync<StorageKeyConflictException>(() => storage.UploadIfAbsentAsync(other, "/z/a.mp4"));
        Assert.Equal("/z/a.mp4", ex.Path);
        Assert.Equal("first", Encoding.UTF8.GetString(storage.Objects["/z/a.mp4"]));
        Assert.Equal(1, storage.Puts);
    }

    [Fact]
    public async Task Parallel_uploads_of_same_named_files_in_different_folders_never_overwrite_each_other()
    {
        var storage = new InMemoryStorageClient<CoreVideo> { UploadDelay = TimeSpan.FromMilliseconds(2) };
        var channel = new ImportChannel { FolderPath = Path.Combine(_root, "Chan"), VideoStoreRoot = "/videos-chan/Chan/" };
        var files = Enumerable.Range(0, 200).Select(i => Write($"Chan/Folder{i}/lecture.mp4", $"content-{i}")).ToList();

        await Task.WhenAll(files.Select(f => Task.Run(() =>
            storage.UploadIfAbsentAsync(f, channel.VideoStoreRoot + StorageKeys.VideoKey(channel, f)))));

        Assert.Equal(200, storage.Objects.Count);
        for (var i = 0; i < 200; i++)
        {
            Assert.Equal($"content-{i}", Encoding.UTF8.GetString(storage.Objects[$"/videos-chan/Chan/Folder{i}/lecture.mp4"]));
        }
    }

    [Fact]
    public async Task Import_keeps_channels_and_sub_folders_apart_and_lowercases_the_image_root()
    {
        Write("A/01.mp4", "video A");
        Write("A/01.jpg", "thumb A");
        Write("B/01.mp4", "video B!");
        Write("B/01.jpg", "thumb B!");
        Write("B/Season1/ep1.mp4", "s1");
        Write("B/Season2/ep1.mp4", "s2 longer");

        using var sqlite = new SqliteImportDb();
        using var db = sqlite.NewContext();
        var videos = new InMemoryStorageClient<CoreVideo>();
        var images = new InMemoryStorageClient<CoreImage>();
        var r2 = new Mock<IR2Client>();
        var importer = new VideoImporter(new Repository<CoreVideo>(r2.Object), videos, images, new BunnyStreamClient("k"), db);

        await importer.ImportMultiChannelAsync(_root, "user", "https://v.example/", "https://i.example/");

        Assert.Equal(
            new[] { "/videos-a/A/01.mp4", "/videos-b/B/01.mp4", "/videos-b/B/Season1/ep1.mp4", "/videos-b/B/Season2/ep1.mp4" },
            videos.Objects.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new[] { "/images-a/01.jpg", "/images-b/01.jpg" }, images.Objects.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.DoesNotContain(images.Objects.Keys, k => k.Contains("System.Func"));
        Assert.Equal("thumb B!", Encoding.UTF8.GetString(images.Objects["/images-b/01.jpg"]));

        var urls = db.ImportVideos.Select(v => v.VideoUrl).OrderBy(u => u).ToList();
        Assert.Contains("https://v.example/B/Season1/ep1.mp4", urls);
        Assert.Contains("https://v.example/B/Season2/ep1.mp4", urls);
        Assert.All(db.ImportVideos, v => Assert.Equal(VideoImportStatus.Uploaded, v.Status));
    }

    [Theory]
    [InlineData("/zone/chan/", "zone", true, "/zone/chan/")]
    [InlineData("zone/chan", "zone", true, "/zone/chan/")]
    [InlineData(@"\zone\chan\sub", "zone", true, "/zone/chan/sub/")]
    [InlineData("/zone/", "zone", false, "")]
    [InlineData("/other/chan/", "zone", false, "")]
    [InlineData("/zone/../x/", "zone", false, "")]
    [InlineData("", "zone", false, "")]
    public void Bulk_uploader_root_must_be_a_folder_inside_the_zone(string value, string zone, bool ok, string expected)
    {
        Assert.Equal(ok, UploadRoot.TryNormalize(value, zone, out var root, out var error));
        Assert.Equal(expected, root);
        Assert.Equal(ok, error.Length == 0);
    }

    [Fact]
    public void Record_audit_lists_records_that_share_a_video_url()
    {
        var shared = "https://v.example/B/ep1.mp4";
        var groups = VideoRecordAudit.FindSharedVideoUrls(new[]
        {
            new CoreVideo { VideoUrl = shared, Title = "s1" },
            new CoreVideo { VideoUrl = shared, Title = "s2" },
            new CoreVideo { VideoUrl = "https://v.example/B/other.mp4" },
            new CoreVideo { VideoUrl = "" },
            new CoreVideo { VideoUrl = "" },
        });
        var g = Assert.Single(groups);
        Assert.Equal(shared, g.VideoUrl);
        Assert.Equal(2, g.Records.Count);
    }
}

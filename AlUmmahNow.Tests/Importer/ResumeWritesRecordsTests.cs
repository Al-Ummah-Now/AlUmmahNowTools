using System.Text.Json;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Importer;
using AlUmmahNow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using CoreImage = AlUmmahNow.Importer.Core.Image;
using CoreVideo = AlUmmahNow.Importer.Core.Video;

namespace AlUmmahNow.Tests.Importer;

[Trait("Category", "Integration")]
public class ResumeWritesRecordsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aun-resume-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteImportDb _db = new();

    public ResumeWritesRecordsTests()
    {
        for (var v = 0; v < 20; v++)
        {
            var path = Path.Combine(_root, "Chan", v % 2 == 0 ? "Even" : "Odd", $"v{v}.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"video {v}");
            File.WriteAllText(Path.ChangeExtension(path, ".jpg"), $"thumb {v}");
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FlakyStorage<T> : IStorageClient<T>
    {
        private readonly InMemoryStorageClient<T> _inner = new();
        public volatile bool Failing;
        public Task UploadAsync(Stream stream, string path)
            => Failing && path.Contains("/Odd/") ? throw new HttpRequestException("network blip") : _inner.UploadAsync(stream, path);
        public Task<StorageObject?> GetAsync(string path) => _inner.GetAsync(path);
        public Task<List<StorageObject>> ListAsync(string path) => _inner.ListAsync(path);
        public Task<Stream> DownloadAsync(string path) => _inner.DownloadAsync(path);
    }

    [Fact]
    public async Task Resume_writes_the_catalogue_record_for_every_video_it_marks_uploaded()
    {
        var videos = new FlakyStorage<CoreVideo> { Failing = true };
        var images = new InMemoryStorageClient<CoreImage>();
        var r2 = new InMemoryR2Client();
        VideoImporter Importer(ImportDbContext db) => new(new Repository<CoreVideo>(r2), videos, images, new BunnyStreamClient("k"), db);

        Guid sessionId;
        using (var db = _db.NewContext())
        {
            sessionId = await Importer(db).ImportMultiChannelAsync(_root, "user-1", "https://v/", "https://i/");
        }
        using (var db = _db.NewContext())
        {
            Assert.Equal(10, await db.ImportVideos.CountAsync(v => v.SessionId == sessionId && v.Status == VideoImportStatus.Failed));
            Assert.Equal(10, r2.Bucket("videos").Count);
        }

        videos.Failing = false;
        using (var db = _db.NewContext())
        {
            Assert.True(await Importer(db).ResumeImportAsync(sessionId));
        }

        using var check = _db.NewContext();
        var rows = await check.ImportVideos.Where(v => v.SessionId == sessionId).ToListAsync();
        Assert.All(rows, r => Assert.Equal(VideoImportStatus.Uploaded, r.Status));
        Assert.Equal(ImportStatus.Completed, (await check.ImportSessions.SingleAsync(s => s.Id == sessionId)).Status);

        var records = r2.Bucket("videos");
        Assert.Equal(20, records.Count);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        foreach (var row in rows)
        {
            Assert.False(string.IsNullOrEmpty(row.VideoId));
            var record = JsonSerializer.Deserialize<CoreVideo>(records[row.VideoId!], json)!;
            Assert.Equal(row.VideoUrl, record.VideoUrl);
            Assert.Equal("user-1", record.UserId);
            Assert.Equal(row.ThumbnailUrl, record.ThumbnailUrl);
            Assert.EndsWith(".jpg", record.ThumbnailUrl);
        }
    }

    [Fact]
    public async Task A_row_is_not_marked_uploaded_when_its_record_cannot_be_written()
    {
        var r2 = new InMemoryR2Client { BeforePut = (_, _) => throw new IOException("R2 down") };
        using var db = _db.NewContext();
        var importer = new VideoImporter(new Repository<CoreVideo>(r2), new InMemoryStorageClient<CoreVideo>(),
            new InMemoryStorageClient<CoreImage>(), new BunnyStreamClient("k"), db);

        var sessionId = await importer.ImportMultiChannelAsync(_root, "u", "https://v/", "https://i/");

        Assert.All(await db.ImportVideos.Where(v => v.SessionId == sessionId).ToListAsync(),
            r => Assert.Equal(VideoImportStatus.Failed, r.Status));
    }
}

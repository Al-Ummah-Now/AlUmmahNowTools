using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Importer;
using AlUmmahNow.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Moq;
using CoreImage = AlUmmahNow.Importer.Core.Image;
using CoreVideo = AlUmmahNow.Importer.Core.Video;

namespace AlUmmahNow.Tests.Importer;

[Trait("Category", "Integration")]
public class SessionScopedImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aun-sessions-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteImportDb _db = new();
    private readonly InMemoryStorageClient<CoreVideo> _videos = new();
    private readonly InMemoryStorageClient<CoreImage> _images = new();

    public SessionScopedImportTests()
    {
        for (var c = 0; c < 2; c++)
        for (var v = 0; v < 5; v++)
        {
            var path = Path.Combine(_root, $"Chan{c}", $"v{v}.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"video {c}/{v}");
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private VideoImporter Importer(ImportDbContext db)
    {
        var r2 = new Mock<IR2Client>();
        return new VideoImporter(new Repository<CoreVideo>(r2.Object), _videos, _images, new BunnyStreamClient("k"), db);
    }

    [Fact]
    public async Task Unique_index_allows_one_row_per_file_per_session()
    {
        using var db = _db.NewContext();
        var s1 = new ImportSession { RootFolder = _root, UserId = "u" };
        var s2 = new ImportSession { RootFolder = _root, UserId = "u", Status = ImportStatus.Completed };
        var ch1 = new ImportChannel { Session = s1, ChannelName = "c", FolderPath = _root };
        var ch2 = new ImportChannel { Session = s2, ChannelName = "c", FolderPath = _root };
        db.AddRange(s1, s2, ch1, ch2,
            new ImportVideo { Session = s1, Channel = ch1, FileName = "x.mp4", FilePath = "/r/x.mp4" },
            new ImportVideo { Session = s2, Channel = ch2, FileName = "x.mp4", FilePath = "/r/x.mp4" });
        await db.SaveChangesAsync();

        db.Add(new ImportVideo { SessionId = s1.Id, ChannelId = ch1.Id, FileName = "x.mp4", FilePath = "/r/x.mp4" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Path_hash_matches_sql_server_hashbytes_of_nvarchar()
    {
        // SELECT CONVERT(nvarchar(64), HASHBYTES('SHA2_256', N'C:\Videos\ep1.mp4'), 2)
        Assert.Equal(PathHash.Of(@"C:\Videos\ep1.mp4"), new ImportVideo { FilePath = @"C:\Videos\ep1.mp4" }.FilePathHash);
        Assert.Equal(64, PathHash.Of("x").Length);
        Assert.Equal(PathHash.Of("x").ToUpperInvariant(), PathHash.Of("x"));
    }

    [Fact]
    public async Task A_new_session_processes_its_own_rows_even_when_an_older_session_uploaded_the_same_files()
    {
        Guid first, second;
        using (var db = _db.NewContext()) first = await Importer(db).ImportMultiChannelAsync(_root, "u", "https://v/", "https://i/");
        using (var db = _db.NewContext()) second = await Importer(db).ImportMultiChannelAsync(_root, "u", "https://v/", "https://i/");

        using var check = _db.NewContext();
        foreach (var id in new[] { first, second })
        {
            var session = await check.ImportSessions.SingleAsync(s => s.Id == id);
            Assert.Equal(ImportStatus.Completed, session.Status);
            var rows = await check.ImportVideos.Where(v => v.SessionId == id).ToListAsync();
            Assert.Equal(10, rows.Count);
            Assert.All(rows, r => Assert.Equal(VideoImportStatus.Uploaded, r.Status));
        }
        // The second run found every file already stored and sent nothing again.
        Assert.Equal(10, _videos.Puts);
    }

    [SqlServerFact]
    public async Task Sql_server_upgrade_backfills_dedupes_and_adds_the_index_and_migrations_match()
    {
        var name = "aun_test_" + Guid.NewGuid().ToString("N");
        var cs = new SqlConnectionStringBuilder(SqlServerFactAttribute.ConnectionString) { InitialCatalog = name, TrustServerCertificate = true }.ToString();
        var options = new DbContextOptionsBuilder<ImportDbContext>().UseSqlServer(cs).Options;
        try
        {
            // A database created before this change: no FilePathHash, no unique index, duplicate rows.
            await using (var db = new ImportDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync(
                    $"DROP INDEX {ImportDbSchema.ImportVideosSessionPathIndex} ON dbo.ImportVideos; ALTER TABLE dbo.ImportVideos DROP COLUMN FilePathHash;" +
                    $"DROP INDEX {ImportDbSchema.ImportSessionsOneRunningPerRootIndex} ON dbo.ImportSessions; ALTER TABLE dbo.ImportSessions DROP COLUMN RootFolderHash, HeartbeatAt;");
                var s = Guid.NewGuid(); var c = Guid.NewGuid();
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT dbo.ImportSessions (Id, UserId, RootFolder, StartTime, Status, TotalVideosFound, VideosProcessed, VideosUploaded, VideosFailed) VALUES ({0}, N'u', N'C:\\r', SYSUTCDATETIME(), 0, 0, 0, 0, 0);" +
                    "INSERT dbo.ImportChannels (Id, SessionId, ChannelName, ChannelId, FolderPath, VideoStoreRoot, VideoStoreUrl, ImageStoreRoot, ImageStoreUrl, CreatedAt, TotalVideos, ProcessedVideos) VALUES ({1}, {0}, N'c', N'c', N'C:\\r\\c', N'/v/', N'u', N'/i/', N'u', SYSUTCDATETIME(), 0, 0);",
                    s, c);
                foreach (var (status, path) in new[] { (0, @"C:\r\c\شريط.mp4"), (3, @"C:\r\c\شريط.mp4"), (4, @"C:\r\c\شريط.mp4"), (0, @"C:\r\c\b.mp4") })
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT dbo.ImportVideos (Id, SessionId, ChannelId, FileName, FilePath, FileSize, Status, CreatedAt, RetryCount) VALUES (NEWID(), {0}, {1}, N'f', {2}, 1, {3}, SYSUTCDATETIME(), 0);",
                        s, c, path, status);
                }

                await ImportDbSchema.EnsureUpToDateAsync(db);
                await ImportDbSchema.EnsureUpToDateAsync(db); // idempotent

                var rows = await db.ImportVideos.AsNoTracking().ToListAsync();
                Assert.Equal(2, rows.Count);
                Assert.All(rows, r => Assert.Equal(PathHash.Of(r.FilePath), r.FilePathHash));
                Assert.Equal(VideoImportStatus.Uploaded, rows.Single(r => r.FilePath.EndsWith("شريط.mp4")).Status);

                // TOOLS-1: the legacy InProgress session keeps an empty hash and blocks nothing; two new
                // InProgress sessions for one root can't coexist, a finished one doesn't count.
                Assert.Equal("", (await db.ImportSessions.AsNoTracking().SingleAsync(x => x.Id == s)).RootFolderHash);
                db.ImportSessions.Add(new ImportSession { RootFolder = @"C:\r", UserId = "u", Status = ImportStatus.InProgress });
                db.ImportSessions.Add(new ImportSession { RootFolder = @"C:\r", UserId = "u", Status = ImportStatus.Completed });
                await db.SaveChangesAsync();
                db.ImportSessions.Add(new ImportSession { RootFolder = @"c:\R", UserId = "u", Status = ImportStatus.InProgress });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // A fresh database built by the migrations has the same index.
            await using (var db = new ImportDbContext(options))
            {
                await db.Database.EnsureDeletedAsync();
                await db.Database.MigrateAsync();
                var exists = await db.Database.SqlQueryRaw<int>(
                    $"SELECT COUNT(*) AS Value FROM sys.indexes WHERE is_unique = 1 AND (name = N'{ImportDbSchema.ImportVideosSessionPathIndex}' OR (name = N'{ImportDbSchema.ImportSessionsOneRunningPerRootIndex}' AND has_filter = 1))").SingleAsync();
                Assert.Equal(2, exists);
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            await using var db = new ImportDbContext(options);
            await db.Database.EnsureDeletedAsync();
        }
    }
}

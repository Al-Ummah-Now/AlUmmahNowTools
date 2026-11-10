using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlUmmahNow.Tests.FileScanService.Services;

/// <summary>
/// TOOLS-6: overlapping file events and StartWatch calls must not create duplicate approvals or active watches,
/// and the detection counter must not lose updates. Runs on SQLite, which enforces the filtered unique indexes.
/// </summary>
[Trait("Category", "Integration")]
public class FileWatchConcurrencyTests : FileSystemTestBase
{
    private readonly SqliteImportDb _sqlite = new();
    private FileWatchService _service = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _service = new FileWatchService(NullLogger<FileWatchService>.Instance, TestScopes.For(_sqlite.Options));
    }

    public override async Task DisposeAsync()
    {
        _service.Dispose();
        _sqlite.Dispose();
        await base.DisposeAsync();
    }

    private async Task<ChannelWatch> AddWatchAsync(string path)
    {
        var watch = new ChannelWatch { ChannelName = "Channel", WatchPath = path, IsActive = true };
        await using var db = _sqlite.NewContext();
        db.ChannelWatches.Add(watch);
        await db.SaveChangesAsync();
        return watch;
    }

    private static Task RunTogether(int count, Func<int, Task> body) =>
        Task.WhenAll(Enumerable.Range(0, count).Select(i => Task.Run(() => body(i))));

    [Fact]
    public async Task One_file_raised_many_times_by_overlapping_watches_gets_one_pending_approval_and_one_count()
    {
        // A watch on the channel folder and one on its parent (with sub-folders) both see the same file, and a
        // large copy raises Created, Changed and Renamed for it.
        var channel = await AddWatchAsync(TestChannel1Directory);
        var parent = await AddWatchAsync(TestVideosDirectory);
        var file = Path.Combine(TestChannel1Directory, "copy.mp4");
        await File.WriteAllTextAsync(file, "video");

        await RunTogether(40, i => _service.ProcessDetectedFileAsync(i % 2 == 0 ? channel : parent, file, $"{i}"));

        await using var db = _sqlite.NewContext();
        var approval = Assert.Single(await db.PendingApprovals.ToListAsync());
        Assert.Equal(file, approval.FilePath);
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Equal(1, await db.ChannelWatches.SumAsync(w => w.TotalFilesDetected));
    }

    [Fact]
    public async Task Concurrent_detections_of_different_files_are_all_counted()
    {
        var watch = await AddWatchAsync(TestChannel1Directory);
        var files = Enumerable.Range(0, 50).Select(i => Path.Combine(TestChannel1Directory, $"v{i}.mp4")).ToList();
        foreach (var (f, i) in files.Select((f, i) => (f, i)))
        {
            await File.WriteAllTextAsync(f, $"video {i}");
        }

        await RunTogether(files.Count, i => _service.ProcessDetectedFileAsync(watch, files[i], files[i]));

        await using var db = _sqlite.NewContext();
        Assert.Equal(50, await db.PendingApprovals.CountAsync());
        var stored = await db.ChannelWatches.SingleAsync(w => w.Id == watch.Id);
        Assert.Equal(50, stored.TotalFilesDetected);
        Assert.NotNull(stored.LastFileDetected);
    }

    [Fact]
    public async Task A_file_can_be_queued_again_once_its_previous_approval_is_decided()
    {
        var watch = await AddWatchAsync(TestChannel1Directory);
        var file = Path.Combine(TestChannel1Directory, "again.mp4");
        await File.WriteAllTextAsync(file, "video");

        await _service.ProcessDetectedFileAsync(watch, file, "1");
        await using (var db = _sqlite.NewContext())
        {
            await db.PendingApprovals.ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, ApprovalStatus.ImportCompleted));
        }
        await _service.ProcessDetectedFileAsync(watch, file, "2");

        await using var check = _sqlite.NewContext();
        Assert.Equal(new[] { ApprovalStatus.Pending, ApprovalStatus.ImportCompleted },
            (await check.PendingApprovals.Select(p => p.Status).ToListAsync()).OrderBy(s => s));
        Assert.Equal(2, (await check.ChannelWatches.SingleAsync()).TotalFilesDetected);
    }

    [Fact]
    public async Task Concurrent_StartWatch_calls_for_one_path_share_one_active_watch()
    {
        var ids = new Guid[20];
        await RunTogether(ids.Length, async i => ids[i] = await _service.StartWatchAsync(new CreateWatchRequest
        {
            ChannelName = $"window {i}",
            WatchPath = TestChannel1Directory,
        }));

        await using var db = _sqlite.NewContext();
        var active = Assert.Single(await db.ChannelWatches.Where(w => w.IsActive).ToListAsync());
        Assert.All(ids, id => Assert.Equal(active.Id, id));
        Assert.Equal(new[] { active.Id }, _service.RunningWatchIds);
    }

    [Fact]
    public async Task A_stopped_watch_does_not_block_watching_the_path_again()
    {
        var first = await _service.StartWatchAsync(new CreateWatchRequest { ChannelName = "c", WatchPath = TestChannel1Directory });
        await _service.StopWatchAsync(first);

        var second = await _service.StartWatchAsync(new CreateWatchRequest { ChannelName = "c", WatchPath = TestChannel1Directory });

        Assert.NotEqual(first, second);
        Assert.Equal(new[] { second }, _service.RunningWatchIds);
    }

    [Fact]
    public async Task Moving_a_watch_onto_a_path_another_active_watch_uses_is_refused_and_keeps_the_old_watcher()
    {
        Directory.CreateDirectory(TestChannel2Directory);
        var one = await _service.StartWatchAsync(new CreateWatchRequest { ChannelName = "one", WatchPath = TestChannel1Directory });
        var two = await _service.StartWatchAsync(new CreateWatchRequest { ChannelName = "two", WatchPath = TestChannel2Directory });

        Assert.False(await _service.UpdateWatchAsync(two, new CreateWatchRequest
        {
            ChannelName = "two",
            WatchPath = TestChannel1Directory,
        }));

        Assert.Equal(new[] { one, two }.OrderBy(x => x), _service.RunningWatchIds.OrderBy(x => x));
        await using var db = _sqlite.NewContext();
        Assert.Equal(TestChannel2Directory, (await db.ChannelWatches.SingleAsync(w => w.Id == two)).WatchPath);
    }

    [SqlServerFact]
    public async Task Sql_server_upgrade_resolves_existing_duplicates_and_adds_the_indexes_and_migrations_match()
    {
        var name = "aun_test_" + Guid.NewGuid().ToString("N");
        var cs = new SqlConnectionStringBuilder(SqlServerFactAttribute.ConnectionString) { InitialCatalog = name, TrustServerCertificate = true }.ToString();
        var options = new DbContextOptionsBuilder<ImportDbContext>().UseSqlServer(cs).Options;
        try
        {
            await using (var db = new ImportDbContext(options))
            {
                // A database from before this change: no hash columns or indexes, and duplicates already present.
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync(
                    $"DROP INDEX {ImportDbSchema.ActiveWatchPathIndex} ON dbo.ChannelWatches; ALTER TABLE dbo.ChannelWatches DROP COLUMN WatchPathHash;" +
                    $"DROP INDEX {ImportDbSchema.PendingApprovalPathIndex} ON dbo.PendingApprovals; ALTER TABLE dbo.PendingApprovals DROP COLUMN FilePathHash;");
                var path = @"D:\قنوات\one";
                var oldest = Guid.NewGuid(); var newer = Guid.NewGuid();
                foreach (var (id, created) in new[] { (oldest, "2024-01-01"), (newer, "2024-02-01") })
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT dbo.ChannelWatches (Id, ChannelName, WatchPath, IsActive, IncludeSubdirectories, ProcessingDelayMs, FileExtensions, CreatedAt, LastUpdated, TotalFilesDetected) " +
                        "VALUES ({0}, N'c', {1}, 1, 1, 5000, N'.mp4', {2}, {2}, 0);", id, path, DateTime.Parse(created));
                }
                foreach (var (status, detected) in new[] { (0, "2024-03-01"), (0, "2024-03-02"), (1, "2024-01-05") })
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT dbo.PendingApprovals (Id, ChannelWatchId, FilePath, FileName, FileSize, Extension, DetectedAt, FileModifiedAt, Status, Priority, FileExists, ExpiresAt) " +
                        "VALUES (NEWID(), {0}, {1}, N'a.mp4', 1, N'.mp4', {2}, {2}, {3}, 3, 1, {2});", oldest, path + @"\a.mp4", DateTime.Parse(detected), status);
                }

                await ImportDbSchema.EnsureUpToDateAsync(db);
                await ImportDbSchema.EnsureUpToDateAsync(db); // idempotent

                var watches = await db.ChannelWatches.AsNoTracking().ToListAsync();
                Assert.All(watches, w => Assert.Equal(PathHash.Of(w.WatchPath), w.WatchPathHash));
                Assert.Equal(oldest, watches.Single(w => w.IsActive).Id);
                var approvals = await db.PendingApprovals.AsNoTracking().ToListAsync();
                Assert.All(approvals, a => Assert.Equal(PathHash.Of(a.FilePath), a.FilePathHash));
                Assert.Equal(DateTime.Parse("2024-03-01"), approvals.Single(a => a.Status == ApprovalStatus.Pending).DetectedAt);
                Assert.Single(approvals, a => a.Status == ApprovalStatus.Approved);

                // The indexes now reject new duplicates.
                db.ChannelWatches.Add(new ChannelWatch { ChannelName = "c", WatchPath = path, IsActive = true });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            await using (var db = new ImportDbContext(options))
            {
                await db.Database.EnsureDeletedAsync();
                await db.Database.MigrateAsync();
                var filtered = await db.Database.SqlQueryRaw<int>(
                    $"SELECT COUNT(*) AS Value FROM sys.indexes WHERE name IN (N'{ImportDbSchema.ActiveWatchPathIndex}', N'{ImportDbSchema.PendingApprovalPathIndex}') AND is_unique = 1 AND has_filter = 1").SingleAsync();
                Assert.Equal(2, filtered);
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

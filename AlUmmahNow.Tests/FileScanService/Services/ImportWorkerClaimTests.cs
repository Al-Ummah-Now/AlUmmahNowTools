using System.Collections.Concurrent;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AlUmmahNow.Data.Models;

namespace AlUmmahNow.Tests.FileScanService.Services;

/// <summary>
/// TOOLS-8: import status changes are conditional, so the worker, the UI and a second service host on the same
/// database never overwrite each other and a file is imported once per selection.
/// </summary>
[Trait("Category", "Integration")]
public class ImportWorkerClaimTests : IDisposable
{
    private readonly SqliteImportDb _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    /// <summary>The real worker with the import step replaced by a recorder.</summary>
    private sealed class RecordingWorker : ImportWorker
    {
        private readonly ConcurrentDictionary<Guid, int> _imports;
        private readonly Func<ScannedVideoFile, Task>? _during;

        public RecordingWorker(IServiceProvider services, ConcurrentDictionary<Guid, int> imports, Func<ScannedVideoFile, Task>? during = null)
            : base(NullLogger<ImportWorker>.Instance, services)
        {
            _imports = imports;
            _during = during;
        }

        protected override async Task ImportFileAsync(ScannedVideoFile file, CancellationToken cancellationToken)
        {
            _imports.AddOrUpdate(file.Id, 1, (_, n) => n + 1);
            if (_during != null)
            {
                await _during(file);
            }
            await Task.Delay(5, cancellationToken);
        }
    }

    /// <summary>A service host: its own DI container (its own scopes and contexts) over the shared database.</summary>
    private IServiceProvider Host()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _sqlite.NewContext());
        return services.BuildServiceProvider();
    }

    private async Task<List<Guid>> AddFilesAsync(int count, FileImportStatus status)
    {
        var session = new ScanSession { Name = "s", RootPath = "/r" };
        var files = Enumerable.Range(0, count).Select(i => new ScannedVideoFile
        {
            ScanSessionId = session.Id,
            FilePath = $"/r/c/{i}.mp4",
            FileName = $"{i}.mp4",
            Directory = "/r/c",
            ChannelName = "c",
            ImportStatus = status,
            FirstScanned = DateTime.UtcNow.AddSeconds(i),
        }).ToList();
        await using var db = _sqlite.NewContext();
        db.ScanSessions.Add(session);
        db.ScannedVideoFiles.AddRange(files);
        await db.SaveChangesAsync();
        return files.Select(f => f.Id).ToList();
    }

    private async Task<Dictionary<Guid, FileImportStatus>> StatusesAsync()
    {
        await using var db = _sqlite.NewContext();
        return await db.ScannedVideoFiles.ToDictionaryAsync(f => f.Id, f => f.ImportStatus);
    }

    [Fact]
    public async Task Workers_on_several_hosts_import_each_selected_file_exactly_once()
    {
        var ids = await AddFilesAsync(30, FileImportStatus.Selected);
        var imports = new ConcurrentDictionary<Guid, int>();
        var workers = Enumerable.Range(0, 4).Select(_ => new RecordingWorker(Host(), imports)).ToList();

        // Every worker polls (10 files per poll) until nothing is left, all at the same time, like service hosts
        // on one shared SQL Server.
        async Task PollUntilDone(RecordingWorker worker)
        {
            while (true)
            {
                await using (var db = _sqlite.NewContext())
                {
                    if (!await db.ScannedVideoFiles.AnyAsync(f => f.ImportStatus == FileImportStatus.Selected))
                    {
                        return;
                    }
                }
                await worker.ProcessPendingImports(CancellationToken.None);
            }
        }
        await Task.WhenAll(workers.SelectMany(w => Enumerable.Range(0, 3).Select(_ => Task.Run(() => PollUntilDone(w)))));

        Assert.Equal(ids.OrderBy(x => x), imports.Keys.OrderBy(x => x));
        Assert.All(imports.Values, n => Assert.Equal(1, n));
        Assert.All((await StatusesAsync()).Values, s => Assert.Equal(FileImportStatus.Uploaded, s));
    }

    [Fact]
    public async Task A_file_unticked_before_the_worker_claims_it_is_not_imported()
    {
        var ids = await AddFilesAsync(2, FileImportStatus.Selected);
        var imports = new ConcurrentDictionary<Guid, int>();

        // While the first file imports, the user unticks the second one, which the worker has already read.
        var worker = new RecordingWorker(Host(), imports, async file =>
        {
            if (file.Id == ids[0])
            {
                await using var db = _sqlite.NewContext();
                Assert.True(await ScannedFileStatus.SetSelectedAsync(db, ids[1], selected: false));
            }
        });
        await worker.ProcessPendingImports(CancellationToken.None);

        Assert.Equal(new[] { ids[0] }, imports.Keys);
        var statuses = await StatusesAsync();
        Assert.Equal(FileImportStatus.Uploaded, statuses[ids[0]]);
        Assert.Equal(FileImportStatus.NotImported, statuses[ids[1]]);
    }

    [Fact]
    public async Task The_user_cannot_untick_a_file_being_imported_or_requeue_an_imported_one()
    {
        var ids = await AddFilesAsync(1, FileImportStatus.Selected);
        var imports = new ConcurrentDictionary<Guid, int>();
        bool? untickDuringUpload = null;
        var worker = new RecordingWorker(Host(), imports, async file =>
        {
            await using var db = _sqlite.NewContext();
            untickDuringUpload = await ScannedFileStatus.SetSelectedAsync(db, file.Id, selected: false);
        });

        await worker.ProcessPendingImports(CancellationToken.None);

        Assert.False(untickDuringUpload);
        Assert.Equal(FileImportStatus.Uploaded, (await StatusesAsync())[ids[0]]);

        await using (var db = _sqlite.NewContext())
        {
            Assert.False(await ScannedFileStatus.SetSelectedAsync(db, ids[0], selected: true));
        }
        await worker.ProcessPendingImports(CancellationToken.None);
        Assert.Equal(1, imports[ids[0]]);
        Assert.Equal(FileImportStatus.Uploaded, (await StatusesAsync())[ids[0]]);
    }

    [Fact]
    public async Task A_failed_import_is_marked_failed_and_can_be_selected_again()
    {
        var ids = await AddFilesAsync(1, FileImportStatus.Selected);
        var worker = new RecordingWorker(Host(), new ConcurrentDictionary<Guid, int>(), _ => throw new IOException("upload failed"));

        await worker.ProcessPendingImports(CancellationToken.None);

        Assert.Equal(FileImportStatus.Failed, (await StatusesAsync())[ids[0]]);
        await using var db = _sqlite.NewContext();
        Assert.True(await ScannedFileStatus.SetSelectedAsync(db, ids[0], selected: true));
        Assert.Equal(FileImportStatus.Selected, (await StatusesAsync())[ids[0]]);
    }

    [Fact]
    public async Task Concurrent_ticks_and_unticks_leave_a_user_selectable_state_and_never_touch_claimed_files()
    {
        var selectable = await AddFilesAsync(10, FileImportStatus.NotImported);
        var claimed = await AddFilesAsync(10, FileImportStatus.Uploading);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
        {
            await using var db = _sqlite.NewContext();
            var id = i % 2 == 0 ? selectable[i % 10] : claimed[i % 10];
            await ScannedFileStatus.SetSelectedAsync(db, id, selected: i % 3 == 0);
        })));

        var statuses = await StatusesAsync();
        Assert.All(selectable, id => Assert.Contains(statuses[id], new[] { FileImportStatus.Selected, FileImportStatus.NotImported }));
        Assert.All(claimed, id => Assert.Equal(FileImportStatus.Uploading, statuses[id]));
    }

    [SqlServerFact]
    public async Task Sql_server_rowversion_rejects_stale_tracked_updates_and_upgrade_and_migrations_add_it()
    {
        var name = "aun_test_" + Guid.NewGuid().ToString("N");
        var cs = new SqlConnectionStringBuilder(SqlServerFactAttribute.ConnectionString) { InitialCatalog = name, TrustServerCertificate = true }.ToString();
        var options = new DbContextOptionsBuilder<ImportDbContext>().UseSqlServer(cs).Options;
        try
        {
            Guid fileId;
            await using (var db = new ImportDbContext(options))
            {
                // A database from before this change, with a file already in it.
                await db.Database.EnsureCreatedAsync();
                var session = new ScanSession { Name = "s", RootPath = @"C:\r" };
                var seeded = new ScannedVideoFile
                {
                    ScanSessionId = session.Id, FilePath = @"C:\r\a.mp4", FileName = "a.mp4", Directory = @"C:\r",
                    ChannelName = "c", RelativePath = "a.mp4", ImportStatus = FileImportStatus.Selected,
                };
                db.ScanSessions.Add(session);
                db.ScannedVideoFiles.Add(seeded);
                await db.SaveChangesAsync();
                fileId = seeded.Id;
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.ScannedVideoFiles DROP COLUMN RowVersion;");

                await ImportDbSchema.EnsureUpToDateAsync(db);
                await ImportDbSchema.EnsureUpToDateAsync(db); // idempotent
            }

            // Two readers of the same row; the status changes in between (the worker's claim), so the stale
            // tracked save is rejected instead of silently winning.
            await using (var stale = new ImportDbContext(options))
            await using (var other = new ImportDbContext(options))
            {
                var file = await stale.ScannedVideoFiles.SingleAsync(f => f.Id == fileId);
                Assert.NotNull(file.RowVersion);
                Assert.True(await ScannedFileStatus.TransitionAsync(other, fileId, new[] { FileImportStatus.Selected }, FileImportStatus.Pending));
                file.ImportStatus = FileImportStatus.NotImported;
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            }

            await using (var db = new ImportDbContext(options))
            {
                Assert.Equal(FileImportStatus.Pending, (await db.ScannedVideoFiles.SingleAsync(f => f.Id == fileId)).ImportStatus);
                await db.Database.EnsureDeletedAsync();
                await db.Database.MigrateAsync();
                var hasRowVersion = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id " +
                    "WHERE c.object_id = OBJECT_ID(N'dbo.ScannedVideoFiles') AND c.name = N'RowVersion' AND t.name = N'timestamp'").SingleAsync();
                Assert.Equal(1, hasRowVersion);
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            await using var db = new ImportDbContext(options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [SqlServerFact]
    public async Task Sql_server_rescan_saves_changed_files_while_their_status_changes_concurrently()
    {
        var name = "aun_test_" + Guid.NewGuid().ToString("N");
        var cs = new SqlConnectionStringBuilder(SqlServerFactAttribute.ConnectionString) { InitialCatalog = name, TrustServerCertificate = true }.ToString();
        var options = new DbContextOptionsBuilder<ImportDbContext>().UseSqlServer(cs).Options;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "aun-rescan-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            for (var i = 0; i < 40; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, $"v{i}.mp4"), "first");
            }
            var session = new ScanSession { Name = "s", RootPath = root };
            await using (var db = new ImportDbContext(options))
            {
                await ImportDbSchema.EnsureUpToDateAsync(db);
                db.ScanSessions.Add(session);
                await db.SaveChangesAsync();
            }

            var hash = new Mock<IHashCalculator>();
            hash.Setup(h => h.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string p, CancellationToken _) => PathHash.Of(File.ReadAllText(p)));
            hash.Setup(h => h.CalculateContentHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string c, CancellationToken _) => PathHash.Of(c));
            var metadata = new Mock<IMetadataExtractor>();
            metadata.Setup(m => m.ExtractVideoMetadataAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VideoMetadata());

            async Task ScanAsync()
            {
                await using var db = new ImportDbContext(options);
                var s = await db.ScanSessions.SingleAsync(x => x.Id == session.Id);
                await new FileScanEngine(NullLogger<FileScanEngine>.Instance, db, hash.Object, metadata.Object)
                    .ProcessScanAsync(s, CancellationToken.None);
            }

            await ScanAsync();
            foreach (var f in Directory.GetFiles(root))
            {
                await File.WriteAllTextAsync(f, "second, longer");
            }

            // The user ticks and unticks files throughout the rescan, so rows change between the engine's read
            // and its save.
            using var stop = new CancellationTokenSource();
            var toggler = Task.Run(async () =>
            {
                await using var db = new ImportDbContext(options);
                var ids = await db.ScannedVideoFiles.Select(f => f.Id).ToListAsync();
                for (var n = 0; !stop.IsCancellationRequested; n++)
                {
                    await ScannedFileStatus.SetSelectedAsync(db, ids[n % ids.Count], n % 2 == 0);
                }
            });
            await ScanAsync();
            stop.Cancel();
            await toggler;

            await using var check = new ImportDbContext(options);
            var files = await check.ScannedVideoFiles.ToListAsync();
            Assert.Equal(40, files.Count);
            Assert.All(files, f => Assert.Equal("second, longer".Length, f.FileSize));
            Assert.All(files, f => Assert.Contains(f.ImportStatus, new[] { FileImportStatus.Selected, FileImportStatus.NotImported }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            await using var db = new ImportDbContext(options);
            await db.Database.EnsureDeletedAsync();
        }
    }
}

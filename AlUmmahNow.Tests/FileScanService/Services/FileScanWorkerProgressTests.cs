using System.Collections.Concurrent;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Hubs;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AlUmmahNow.Tests.FileScanService.Services;

/// <summary>
/// TOOLS-7: the worker's progress handler ran SaveChangesAsync on the scan engine's own scoped DbContext while the
/// engine carried on querying it, so large scans failed with "A second operation was started on this context".
/// The scan also has to get through duplicate detection, whose GroupBy query no relational provider could run.
/// </summary>
[Trait("Category", "Integration")]
public class FileScanWorkerProgressTests : FileSystemTestBase
{
    /// <summary>Makes every save yield, as a real database round-trip does, so an overlapping operation is caught.</summary>
    private sealed class SlowSaves : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await Task.Delay(2, cancellationToken);
            return result;
        }
    }

    [Fact]
    public async Task A_scan_with_many_progress_events_completes_and_streams_progress_without_sharing_the_engine_context()
    {
        // Many folders and files -> many progress events, each followed straight away by engine queries.
        var root = Path.Combine(TestRootDirectory, "ScanRoot");
        for (var c = 0; c < 6; c++)
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, $"Channel{c}"));
            for (var f = 0; f < 8; f++)
            {
                // Two channels hold the same video, which duplicate detection must flag once.
                await File.WriteAllTextAsync(Path.Combine(dir.FullName, $"v{f}.mp4"), c < 2 && f == 0 ? "same video" : $"{c}-{f}");
            }
        }

        using var sqlite = new SqliteImportDb();
        var options = new DbContextOptionsBuilder<ImportDbContext>(sqlite.Options).AddInterceptors(new SlowSaves()).Options;
        var session = new ScanSession { Name = "scan", RootPath = root, Status = ScanStatus.Pending };
        await using (var db = new ImportDbContext(options))
        {
            db.ScanSessions.Add(session);
            await db.SaveChangesAsync();
        }

        var hash = new Mock<IHashCalculator>();
        hash.Setup(h => h.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string p, CancellationToken _) => PathHash.Of(File.ReadAllText(p)));
        hash.Setup(h => h.CalculatePathHash(It.IsAny<string>())).Returns((string p) => PathHash.Of(p));
        hash.Setup(h => h.CalculateContentHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string c, CancellationToken _) => PathHash.Of(c));
        var metadata = new Mock<IMetadataExtractor>();
        metadata.Setup(m => m.ExtractVideoMetadataAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VideoMetadata());

        var sent = new ConcurrentQueue<(string Method, object?[] Args)>();
        var all = new Mock<IClientProxy>();
        all.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback((string m, object?[] a, CancellationToken _) => sent.Enqueue((m, a)))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(all.Object);
        var hub = new Mock<IHubContext<FileScanHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        // Registered as in Program.cs: one ImportDbContext per scope, shared by the worker and the engine.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new ImportDbContext(options));
        services.AddScoped<IFileScanEngine, FileScanEngine>();
        services.AddSingleton(hash.Object);
        services.AddSingleton(metadata.Object);
        await using var provider = services.BuildServiceProvider();

        var worker = new FileScanWorker(NullLogger<FileScanWorker>.Instance, provider, hub.Object);
        await worker.ProcessPendingScans(CancellationToken.None);

        await using var check = new ImportDbContext(sqlite.Options);
        var stored = await check.ScanSessions.SingleAsync(s => s.Id == session.Id);
        Assert.True(stored.ErrorMessage == null, stored.ErrorMessage);
        Assert.Equal(ScanStatus.Completed, stored.Status);
        Assert.Equal(48, await check.ScannedVideoFiles.CountAsync());
        var duplicate = Assert.Single(await check.ScannedVideoFiles.Where(f => f.IsDuplicate).ToListAsync());
        var original = await check.ScannedVideoFiles.SingleAsync(f => f.Id == duplicate.OriginalFileId);
        Assert.Equal(duplicate.ContentHash, original.ContentHash);
        Assert.NotEqual(duplicate.FilePath, original.FilePath);

        var methods = sent.Select(s => s.Method).ToList();
        Assert.Equal("ScanStarted", methods.First());
        Assert.Equal("ScanCompleted", methods.Last());
        Assert.DoesNotContain("ScanFailed", methods);
        Assert.True(methods.Count(m => m == "ScanProgress") >= 6, string.Join(",", methods));
    }
}

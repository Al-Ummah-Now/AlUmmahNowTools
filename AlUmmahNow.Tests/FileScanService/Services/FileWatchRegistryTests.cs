using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlUmmahNow.Tests.FileScanService.Services;

/// <summary>
/// TOOLS-5: watches belong to one process-wide registry, not to the DI scope of the hub call that started them.
/// </summary>
[Trait("Category", "Integration")]
public class FileWatchRegistryTests : FileSystemTestBase
{
    private DbContextOptions<ImportDbContext> _options = null!;
    private ServiceProvider _host = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // The same registrations as FileScanService/Program.cs.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new ImportDbContext(_options));
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddSingleton<FileWatchService>();
        services.AddSingleton<IFileWatchService>(sp => sp.GetRequiredService<FileWatchService>());
        services.AddHostedService(sp => sp.GetRequiredService<FileWatchService>());
        _host = services.BuildServiceProvider();
    }

    public override async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await base.DisposeAsync();
    }

    private FileWatchService Registry => _host.GetRequiredService<FileWatchService>();

    private CreateWatchRequest Request(string path) => new()
    {
        ChannelName = "Channel",
        WatchPath = path,
        ProcessingDelayMs = 1000,
    };

    [Fact]
    public async Task A_watch_started_in_one_hub_scope_keeps_running_and_can_be_stopped_from_another()
    {
        Guid watchId;
        using (var hubCall = _host.CreateScope())
        {
            watchId = await hubCall.ServiceProvider.GetRequiredService<IFileWatchService>().StartWatchAsync(Request(TestChannel1Directory));
        }

        Assert.Contains(watchId, Registry.RunningWatchIds);

        using (var hubCall = _host.CreateScope())
        {
            Assert.True(await hubCall.ServiceProvider.GetRequiredService<IFileWatchService>().StopWatchAsync(watchId));
        }

        Assert.DoesNotContain(watchId, Registry.RunningWatchIds);
        await using var db = new ImportDbContext(_options);
        Assert.False((await db.ChannelWatches.SingleAsync(w => w.Id == watchId)).IsActive);
    }

    [Fact]
    public async Task Active_watches_are_restored_when_the_service_starts()
    {
        var active1 = new ChannelWatch { ChannelName = "a", WatchPath = TestChannel1Directory, IsActive = true };
        var active2 = new ChannelWatch { ChannelName = "b", WatchPath = TestChannel2Directory, IsActive = true };
        var inactive = new ChannelWatch { ChannelName = "c", WatchPath = TestVideosDirectory, IsActive = false };
        var missing = new ChannelWatch { ChannelName = "d", WatchPath = Path.Combine(TestRootDirectory, "gone"), IsActive = true };
        await using (var db = new ImportDbContext(_options))
        {
            db.ChannelWatches.AddRange(active1, active2, inactive, missing);
            await db.SaveChangesAsync();
        }

        foreach (var hosted in _host.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        Assert.Equal(new[] { active1.Id, active2.Id }.OrderBy(x => x), Registry.RunningWatchIds.OrderBy(x => x));

        await Registry.StopAsync(CancellationToken.None);
        Assert.Empty(Registry.RunningWatchIds);
    }

    [Fact]
    public async Task Detected_files_are_processed_in_their_own_scope_even_many_at_once()
    {
        Guid watchId;
        using (var hubCall = _host.CreateScope())
        {
            watchId = await hubCall.ServiceProvider.GetRequiredService<IFileWatchService>().StartWatchAsync(Request(TestChannel1Directory));
        }
        var config = new ChannelWatch { Id = watchId, ChannelName = "Channel" };

        // Timer callbacks fire on thread-pool threads after the creating scope is gone, possibly together.
        var files = Enumerable.Range(0, 40).Select(i =>
        {
            var path = Path.Combine(TestChannel1Directory, $"new-{i}.mp4");
            File.WriteAllText(path, $"video {i}");
            return path;
        }).ToList();
        await Task.WhenAll(files.Select(f => Task.Run(() => Registry.ProcessDetectedFileAsync(config, f, $"{watchId}:{f}"))));

        await using var db = new ImportDbContext(_options);
        var approvals = await db.PendingApprovals.Where(p => p.ChannelWatchId == watchId).Select(p => p.FilePath).ToListAsync();
        Assert.Equal(files.OrderBy(f => f), approvals.OrderBy(f => f));
    }

    [Fact]
    public async Task Files_detected_after_a_watch_is_stopped_are_ignored()
    {
        var watchId = await Registry.StartWatchAsync(Request(TestChannel1Directory));
        await Registry.StopWatchAsync(watchId);
        var path = Path.Combine(TestChannel1Directory, "late.mp4");
        File.WriteAllText(path, "x");

        await Registry.ProcessDetectedFileAsync(new ChannelWatch { Id = watchId }, path, "k");

        await using var db = new ImportDbContext(_options);
        Assert.Empty(await db.PendingApprovals.ToListAsync());
    }
}

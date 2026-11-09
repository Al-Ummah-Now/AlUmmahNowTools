using System.Text.Json;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Importer;
using AlUmmahNow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using CoreImage = AlUmmahNow.Importer.Core.Image;
using CoreVideo = AlUmmahNow.Importer.Core.Video;

namespace AlUmmahNow.Tests.Importer;

[Trait("Category", "Integration")]
public class IdempotentImportTests : IDisposable
{
    private const int Files = 12;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aun-idem-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteImportDb _db = new();
    private readonly InMemoryStorageClient<CoreVideo> _videos = new();
    private readonly InMemoryStorageClient<CoreImage> _images = new();
    private readonly InMemoryR2Client _r2 = new();

    public IdempotentImportTests()
    {
        for (var i = 0; i < Files; i++)
        {
            var path = Path.Combine(_root, $"Chan{i % 3}", i % 2 == 0 ? "" : "Sub", $"v{i}.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"video {i}");
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private VideoImporter Importer(ImportDbContext db, TimeSpan? lease = null) =>
        new(new Repository<CoreVideo>(_r2), _videos, _images, new BunnyStreamClient("k"), db)
        {
            SessionLease = lease ?? TimeSpan.FromMinutes(10),
        };

    private Task<Guid> ImportAsync(TimeSpan? lease = null) => Task.Run(async () =>
    {
        using var db = _db.NewContext();
        return await Importer(db, lease).ImportMultiChannelAsync(_root, "u", "https://v/", "https://i/");
    });

    private void AssertExactlyOneRecordAndUploadPerFile()
    {
        Assert.Equal(Files, _r2.Bucket("videos").Count);
        Assert.Equal(Files, _videos.Objects.Count);
        Assert.Equal(Files, _videos.Puts);
    }

    [Fact]
    public void Video_ids_are_deterministic_version_8_guids()
    {
        var a = VideoIds.ForFile("Chan", "Sub/v1.mp4");
        Assert.Equal(a, VideoIds.ForFile("Chan", "Sub/v1.mp4"));
        Assert.NotEqual(a, VideoIds.ForFile("Chan", "v1.mp4"));
        Assert.NotEqual(a, VideoIds.ForFile("Other", "Sub/v1.mp4"));
        Assert.Equal('8', a.ToString()[14]);
        Assert.Contains(a.ToString()[19], "89ab");
    }

    [Fact]
    public async Task Parallel_imports_of_one_folder_run_exactly_once()
    {
        const int runCount = 16;
        // Hold the winner inside its first upload until every other run has been refused, so all runs
        // overlap (a run started after the winner finished would legitimately be allowed). If two runs
        // got in, fewer than 15 are refused and the gate opens on the timeout instead.
        var refused = 0;
        var gate = new TaskCompletionSource();
        _videos.Gate = Task.WhenAny(gate.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        var runs = Enumerable.Range(0, runCount).Select(_ => ImportAsync()).ToList();
        var results = await Task.WhenAll(runs.Select(async r =>
        {
            try { return (Id: await r, Error: (Exception?)null); }
            catch (Exception ex)
            {
                if (Interlocked.Increment(ref refused) == runCount - 1) gate.TrySetResult();
                return (Id: Guid.Empty, Error: ex);
            }
        }));
        Assert.True(gate.Task.IsCompleted, "every other run should have been refused while the winner was running");

        Assert.Single(results, r => r.Error == null);
        Assert.All(results.Where(r => r.Error != null), r => Assert.IsType<ImportAlreadyRunningException>(r.Error));
        AssertExactlyOneRecordAndUploadPerFile();

        using var check = _db.NewContext();
        var winner = results.Single(r => r.Error == null).Id;
        Assert.Equal(ImportStatus.Completed, (await check.ImportSessions.SingleAsync(s => s.Id == winner)).Status);
    }

    [Fact]
    public async Task Reruns_reuse_the_same_records_and_send_nothing_again()
    {
        for (var run = 0; run < 3; run++)
        {
            await ImportAsync();
        }
        AssertExactlyOneRecordAndUploadPerFile();
        using var check = _db.NewContext();
        Assert.Equal(Files, await check.ImportVideos.Select(v => v.VideoId).Distinct().CountAsync());
    }

    [Fact]
    public async Task Resume_is_refused_while_the_import_is_running()
    {
        _videos.UploadDelay = TimeSpan.FromMilliseconds(200);
        var import = ImportAsync();

        Guid sessionId = Guid.Empty;
        for (var i = 0; i < 200 && sessionId == Guid.Empty; i++)
        {
            await Task.Delay(10);
            using var probe = _db.NewContext();
            sessionId = await probe.ImportSessions.Select(s => s.Id).FirstOrDefaultAsync();
        }
        Assert.NotEqual(Guid.Empty, sessionId);

        var resumes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var db = _db.NewContext();
            try { await Importer(db).ResumeImportAsync(sessionId); return null; }
            catch (Exception ex) { return ex; }
        })));
        Assert.All(resumes, e => Assert.IsType<ImportAlreadyRunningException>(e));

        Assert.Equal(sessionId, await import);
        AssertExactlyOneRecordAndUploadPerFile();
    }

    private async Task<Guid> CrashMidwayAsync()
    {
        var id = await ImportAsync();
        using var db = _db.NewContext();
        // Make it look like the process died: still InProgress, stale heartbeat, rows mid-upload.
        await db.ImportSessions.Where(s => s.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.Status, ImportStatus.InProgress)
            .SetProperty(s => s.HeartbeatAt, DateTime.UtcNow.AddHours(-1)));
        var rows = await db.ImportVideos.Where(v => v.SessionId == id).OrderBy(v => v.FilePath).Take(5).Select(v => v.Id).ToListAsync();
        await db.ImportVideos.Where(v => rows.Contains(v.Id)).ExecuteUpdateAsync(u => u.SetProperty(v => v.Status, VideoImportStatus.Uploading));
        return id;
    }

    [Fact]
    public async Task A_crashed_session_does_not_block_a_new_import_and_no_records_are_duplicated()
    {
        var crashed = await CrashMidwayAsync();

        var next = await ImportAsync();

        using var check = _db.NewContext();
        var old = await check.ImportSessions.SingleAsync(s => s.Id == crashed);
        Assert.Equal(ImportStatus.Failed, old.Status);
        Assert.StartsWith("Interrupted", old.ErrorMessage);
        Assert.Equal(ImportStatus.Completed, (await check.ImportSessions.SingleAsync(s => s.Id == next)).Status);
        AssertExactlyOneRecordAndUploadPerFile();
    }

    [Fact]
    public async Task Resuming_a_crashed_session_finishes_rows_left_mid_upload()
    {
        var crashed = await CrashMidwayAsync();

        using (var db = _db.NewContext())
        {
            Assert.True(await Importer(db).ResumeImportAsync(crashed));
        }

        using var check = _db.NewContext();
        Assert.Equal(ImportStatus.Completed, (await check.ImportSessions.SingleAsync(s => s.Id == crashed)).Status);
        Assert.All(await check.ImportVideos.Where(v => v.SessionId == crashed).ToListAsync(),
            v => Assert.Equal(VideoImportStatus.Uploaded, v.Status));
        AssertExactlyOneRecordAndUploadPerFile();
    }

    [Fact]
    public async Task A_live_session_cannot_be_resumed_by_another_run()
    {
        var id = await ImportAsync();
        using (var db = _db.NewContext())
        {
            await db.ImportSessions.Where(s => s.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, ImportStatus.InProgress)
                .SetProperty(s => s.HeartbeatAt, DateTime.UtcNow));
        }
        using var other = _db.NewContext();
        await Assert.ThrowsAsync<ImportAlreadyRunningException>(() => Importer(other).ResumeImportAsync(id));
        await Assert.ThrowsAsync<ImportAlreadyRunningException>(() => Importer(other).ImportMultiChannelAsync(_root, "u", "https://v/", "https://i/"));
    }

    [Fact]
    public async Task A_run_whose_session_is_taken_over_stops()
    {
        _videos.UploadDelay = TimeSpan.FromMilliseconds(250);
        var import = ImportAsync(lease: TimeSpan.FromMilliseconds(400));

        // Wait until the run is under way (it has uploaded something), then take its session away.
        for (var i = 0; i < 500 && _videos.Puts == 0; i++) await Task.Delay(10);
        Assert.True(_videos.Puts > 0);
        using (var db = _db.NewContext())
        {
            // What another run does when it finds the heartbeat stale.
            await db.ImportSessions.ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, ImportStatus.Failed));
        }

        await Assert.ThrowsAsync<ImportLeaseLostException>(() => import);
        Assert.True(_videos.Puts < Files);
    }

    [Fact]
    public async Task Records_from_before_deterministic_ids_are_reused_not_duplicated()
    {
        var first = await ImportAsync();
        // Pretend the first run was an old version: random record ids.
        using (var db = _db.NewContext())
        {
            foreach (var row in await db.ImportVideos.Where(v => v.SessionId == first).ToListAsync())
            {
                var body = _r2.Bucket("videos")[row.VideoId!];
                await _r2.DeleteObjectAsync("videos", row.VideoId!);
                row.VideoId = Guid.NewGuid().ToString();
                await _r2.PutObjectAsync("videos", row.VideoId, body);
            }
            await db.SaveChangesAsync();
        }

        await ImportAsync();

        Assert.Equal(Files, _r2.Bucket("videos").Count);
    }

    [Fact]
    public async Task Concurrent_record_creation_for_one_key_creates_exactly_one()
    {
        var repo = new Repository<CoreVideo>(_r2);
        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() =>
            repo.CreateObjectAsync("videos", "same-id", new CoreVideo { Title = $"t{i}" }))));
        Assert.Single(results, created => created);
        var stored = JsonSerializer.Deserialize<CoreVideo>(_r2.Bucket("videos")["same-id"],
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        Assert.StartsWith("t", stored.Title);
    }
}

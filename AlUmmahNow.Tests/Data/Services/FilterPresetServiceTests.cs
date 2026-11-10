using System.Text.Json;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Services;

namespace AlUmmahNow.Tests.Data.Services;

/// <summary>
/// TOOLS-9: FilterPresetService took its non-re-entrant lock and then called methods that took it again, so the
/// first load (no presets file yet) and every save, delete or default change hung forever. Its file writes were
/// also in place, so a concurrent reader could see a half-written file.
/// </summary>
public class FilterPresetServiceTests : IDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aun-presets-" + Guid.NewGuid().ToString("N"));
    private string PresetsFile => Path.Combine(_dir, "filter-presets.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    /// <summary>Fails the test instead of hanging the run when an operation deadlocks.</summary>
    private static async Task<T> Within<T>(Task<T> task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(Deadline)));
        return await task;
    }

    private static Task Within(Task task) => Within(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return 0; }));

    [Fact]
    public async Task First_load_without_a_presets_file_creates_the_built_ins()
    {
        var service = new FilterPresetService(PresetsFile);

        var presets = await Within(service.GetAllPresetsAsync());

        Assert.Equal(6, presets.Count);
        Assert.All(presets, p => Assert.True(p.IsBuiltIn));
        Assert.True(File.Exists(PresetsFile));
    }

    [Fact]
    public async Task Every_operation_completes_on_a_fresh_service()
    {
        // Each mutator used to call GetAllPresetsAsync while holding the lock; run each on a fresh instance so
        // nothing is cached and the first-load path runs under the lock too.
        var mine = new FilterPreset { Name = "Mine", ChannelFilter = "c" };
        await Within(new FilterPresetService(PresetsFile).SavePresetAsync(mine));
        await Within(new FilterPresetService(PresetsFile).SetDefaultPresetAsync(mine.Id));
        Assert.Equal(mine.Id, (await Within(new FilterPresetService(PresetsFile).GetDefaultPresetAsync()))?.Id);
        await Within(new FilterPresetService(PresetsFile).ClearDefaultPresetAsync());
        Assert.Null(await Within(new FilterPresetService(PresetsFile).GetDefaultPresetAsync()));
        await Within(new FilterPresetService(PresetsFile).InitializeBuiltInPresetsAsync());
        await Within(new FilterPresetService(PresetsFile).DeletePresetAsync(mine.Id));

        var presets = await Within(new FilterPresetService(PresetsFile).GetAllPresetsAsync());
        Assert.Equal(6, presets.Count);
        Assert.DoesNotContain(presets, p => p.Id == mine.Id);
    }

    [Fact]
    public async Task Built_in_presets_cannot_be_deleted_and_the_lock_is_released_after_the_error()
    {
        var service = new FilterPresetService(PresetsFile);
        var builtIn = (await Within(service.GetAllPresetsAsync())).First();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Within(service.DeletePresetAsync(builtIn.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Within(service.SetDefaultPresetAsync(Guid.NewGuid())));

        Assert.Equal(6, (await Within(service.GetAllPresetsAsync())).Count);
    }

    [Fact]
    public async Task Concurrent_saves_from_many_callers_are_all_kept()
    {
        var service = new FilterPresetService(PresetsFile);
        var added = Enumerable.Range(0, 50).Select(i => new FilterPreset { Name = $"p{i:00}", IsDefault = i == 49 }).ToList();

        await Within(Task.WhenAll(added.Select(p => Task.Run(() => service.SavePresetAsync(p)))));

        var fromDisk = await Within(new FilterPresetService(PresetsFile).GetAllPresetsAsync());
        Assert.Equal(56, fromDisk.Count);
        Assert.All(added, p => Assert.Contains(fromDisk, d => d.Id == p.Id));
        Assert.True(fromDisk.Count(p => p.IsDefault) <= 1);
    }

    [Fact]
    public async Task Readers_never_see_a_partly_written_file_and_no_temp_files_are_left()
    {
        var service = new FilterPresetService(PresetsFile);
        await Within(service.GetAllPresetsAsync());

        using var stop = new CancellationTokenSource();
        var reads = 0;
        // Another app instance reading the file while this one writes it.
        var reader = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                string json;
                try
                {
                    json = await File.ReadAllTextAsync(PresetsFile);
                }
                catch (IOException)
                {
                    continue; // Windows can refuse the open during the rename; the content is never partial.
                }
                Assert.NotNull(JsonSerializer.Deserialize<List<FilterPreset>>(json));
                reads++;
            }
        });

        for (var i = 0; i < 100; i++)
        {
            await Within(service.SavePresetAsync(new FilterPreset { Name = $"p{i}", Description = new string('x', 2000) }));
        }
        stop.Cancel();
        await reader;

        Assert.True(reads > 0);
        Assert.Equal(new[] { PresetsFile }, Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Changing_a_returned_preset_does_not_change_the_stored_presets()
    {
        var service = new FilterPresetService(PresetsFile);
        var first = (await Within(service.GetAllPresetsAsync())).First();

        first.Name = "edited but not saved";

        Assert.DoesNotContain(await Within(service.GetAllPresetsAsync()), p => p.Name == "edited but not saved");
    }
}

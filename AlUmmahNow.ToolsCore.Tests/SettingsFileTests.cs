using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

public sealed class SettingsFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tools-settings-").FullName;
    private string FilePath => Path.Combine(_dir, "sub", "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void What_is_saved_is_there_after_a_restart_and_sections_are_kept_apart()
    {
        var store = new SettingsFile(FilePath);
        Assert.Null(store.Get("A", "k"));
        store.Set("A", new Dictionary<string, string> { ["k"] = "1", ["other"] = "x" });
        store.Set("B", new Dictionary<string, string> { ["k"] = "2" });
        store.Set("A", new Dictionary<string, string> { ["k"] = "مسار" });
        var restarted = new SettingsFile(FilePath);
        Assert.Equal(("مسار", "x", "2"), (restarted.Get("A", "k"), restarted.Get("A", "other"), restarted.Get("B", "k")));
        Assert.Null(restarted.Get("B", "other"));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("null")]
    [InlineData("{\"A\":null}")]
    [InlineData("[1]")]
    public void A_damaged_file_is_an_empty_one_and_is_written_again(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, text);
        var store = new SettingsFile(FilePath);
        Assert.Null(store.Get("A", "k"));
        store.Set("A", new Dictionary<string, string> { ["k"] = "1" });
        Assert.Equal("1", new SettingsFile(FilePath).Get("A", "k"));
    }

    [Fact]
    public void The_old_settings_file_is_taken_over_once()
    {
        var old = Path.Combine(_dir, "AlUmmahNow", "VisualImporter", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        var file = new UserSettingsFile(_dir);
        Assert.Null(file.Read());
        File.WriteAllText(old, "old");
        Assert.Equal("old", file.Read());
        File.WriteAllText(old, "older app");
        Assert.Equal("old", file.Read());
        file.Write("new");
        Assert.Equal("new", new UserSettingsFile(_dir).Read());
        Assert.Equal("older app", File.ReadAllText(old));
    }

    [Fact]
    public void The_old_settings_file_is_not_read_when_the_new_one_is_there()
    {
        var file = new UserSettingsFile(_dir);
        file.Write("new");
        var old = Path.Combine(_dir, "AlUmmahNow", "VisualImporter", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, "old");
        Assert.Equal("new", file.Read());
    }

    [Fact]
    public async Task Taking_over_the_old_file_never_writes_over_a_newer_save()
    {
        for (var round = 0; round < 40; round++)
        {
            var root = Path.Combine(_dir, "r" + round);
            var old = Path.Combine(root, "AlUmmahNow", "VisualImporter", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(old)!);
            File.WriteAllText(old, "old");
            // One copy of the app saves, others (with no lock of its own in common) read
            var readers = Enumerable.Range(0, 4).Select(_ => new UserSettingsFile(root)).ToArray();
            var writer = new UserSettingsFile(root);
            using var start = new Barrier(readers.Length + 1);
            var reading = readers.Select(r => Task.Factory.StartNew(() => { start.SignalAndWait(); return r.Read(); }, TaskCreationOptions.LongRunning)).ToArray();
            var writing = Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                // Held by a reader for a moment (Windows): the page says it was not saved, and the person saves again
                for (var i = 0; ; i++)
                {
                    try { writer.Write("new"); return; }
                    catch (Exception ex) when (i < 50 && ex is IOException or UnauthorizedAccessException) { Thread.Sleep(10); }
                }
            }, TaskCreationOptions.LongRunning);
            await writing;
            foreach (var read in reading)
            {
                try { Assert.Contains(await read, new[] { "old", "new" }); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            Assert.Equal("new", new UserSettingsFile(root).Read());
        }
    }

    [Fact]
    public void A_settings_file_that_cannot_be_read_now_is_not_written_over()
    {
        var store = new SettingsFile(FilePath);
        store.Set("A", new Dictionary<string, string> { ["k"] = "1" });
        // Something else holds it
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(store.Get("A", "k"));
            Assert.ThrowsAny<IOException>(() => store.Set("B", new Dictionary<string, string> { ["k"] = "2" }));
        }
        store.Set("B", new Dictionary<string, string> { ["k"] = "2" });
        Assert.Equal(("1", "2"), (store.Get("A", "k"), store.Get("B", "k")));
    }

    [Fact]
    public void The_notification_switches_are_taken_over_once_and_saved_in_one_place()
    {
        var page = new UserSettingsFile(_dir);
        page.Write("{\"Notifications\":{\"Enabled\":false,\"SoundEnabled\":true}}");
        var store = new SettingsFile(FilePath);
        var switches = new NotificationSwitches(store, page);
        Assert.Equal((false, true), (switches.Enabled, switches.Sound));
        switches.Save(true, false);
        // The page's old copy has no say any more
        var restarted = new NotificationSwitches(new SettingsFile(FilePath), page);
        Assert.Equal((true, false), (restarted.Enabled, restarted.Sound));
        foreach (var text in new[] { "{ broken", "[]", "{\"Notifications\":1}", "{\"Notifications\":{\"Enabled\":\"x\"}}" })
        {
            page.Write(text);
            var fresh = new NotificationSwitches(new SettingsFile(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json")), page);
            Assert.Equal((true, true), (fresh.Enabled, fresh.Sound));
        }
    }

    private sealed class Throws(Exception error) : ISettingsStore
    {
        public string? Get(string section, string key) => throw error;
        public void Set(string section, IReadOnlyDictionary<string, string> values) => throw error;
    }

    public static TheoryData<Exception> Errors => new() { new NotImplementedException("Containers"), new InvalidOperationException(), new UnauthorizedAccessException(), new TypeInitializationException("x", null) };

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Saved_paths_that_cannot_be_read_are_no_reason_not_to_look(Exception error)
    {
        Exception? told = null;
        Assert.Equal(("found", "probe"), await FfmpegPaths.LocateAsync(new Throws(error), _ => Task.FromResult<(string, string)?>(("found", "probe")), ex => told = ex, default));
        Assert.Same(error, told);
        // Nothing found is nothing found, not a failure
        Assert.Null(await FfmpegPaths.LocateAsync(new Throws(error), _ => Task.FromResult<(string, string)?>(null), null, default));
    }

    [Fact]
    public async Task The_saved_paths_come_first_when_both_are_there()
    {
        var store = new SettingsFile(FilePath);
        var (ffmpeg, ffprobe) = (Path.Combine(_dir, "ffmpeg"), Path.Combine(_dir, "ffprobe"));
        store.Set(FfmpegPaths.Section, new Dictionary<string, string> { [FfmpegPaths.Ffmpeg] = ffmpeg, [FfmpegPaths.Ffprobe] = ffprobe });
        Task<(string, string)?> Locate() => FfmpegPaths.LocateAsync(store, _ => Task.FromResult<(string, string)?>(("found", "probe")), null, default)!;
        File.WriteAllText(ffmpeg, "");
        Assert.Equal(("found", "probe"), await Locate());
        File.WriteAllText(ffprobe, "");
        Assert.Equal((ffmpeg, ffprobe), await Locate());
    }
}

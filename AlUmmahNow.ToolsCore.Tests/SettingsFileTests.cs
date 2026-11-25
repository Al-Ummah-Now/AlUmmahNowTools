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

    private string OldPath => Path.Combine(_dir, "AlUmmahNow", "VisualImporter", "settings.json");

    private void SaveOld(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OldPath)!);
        File.WriteAllText(OldPath, text);
    }

    [Fact]
    public void The_old_settings_file_is_taken_over_once_at_the_start()
    {
        var file = new UserSettingsFile(_dir);
        file.TakeOverOld();
        Assert.Null(file.Read());
        SaveOld("old");
        // Only the start takes it over
        Assert.Null(file.Read());
        file.TakeOverOld();
        Assert.Equal("old", file.Read());
        file.Write("new");
        SaveOld("older app");
        var next = new UserSettingsFile(_dir);
        next.TakeOverOld();
        Assert.Equal("new", next.Read());
        Assert.Equal("older app", File.ReadAllText(OldPath));
    }

    [Fact]
    public void A_take_over_that_fails_leaves_the_app_working_and_is_done_at_the_next_start()
    {
        SaveOld("old");
        // A file where the folder should be
        var folder = Path.Combine(_dir, "AlUmmahNow", "VisualImporterUno");
        File.WriteAllText(folder, "");
        var file = new UserSettingsFile(_dir);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        file.TakeOverOld();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "nothing is waited for");
        File.Delete(folder);
        var next = new UserSettingsFile(_dir);
        next.TakeOverOld();
        Assert.Equal("old", next.Read());
    }

    [Fact]
    public void A_file_that_cannot_be_read_now_is_as_it_was_last()
    {
        var file = new UserSettingsFile(_dir);
        file.Write("saved");
        var store = new SettingsFile(FilePath);
        store.Set("A", new Dictionary<string, string> { ["k"] = "1" });
        var (newFile, newStore) = (new UserSettingsFile(_dir), new SettingsFile(FilePath));
        using (new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal("saved", file.Read());
            Assert.Equal("1", store.Get("A", "k"));
            // Never read: not "nothing saved"
            Assert.ThrowsAny<IOException>(() => newFile.Read());
            Assert.ThrowsAny<IOException>(() => newStore.Get("A", "k"));
            // And nothing is written over what else is in it
            Assert.ThrowsAny<IOException>(() => store.Set("B", new Dictionary<string, string> { ["k"] = "2" }));
        }
        store.Set("B", new Dictionary<string, string> { ["k"] = "2" });
        Assert.Equal(("1", "2"), (newStore.Get("A", "k"), newStore.Get("B", "k")));
        Assert.Equal("saved", newFile.Read());
    }

    [Fact]
    public async Task Saves_and_reads_at_the_same_moment_are_whole()
    {
        var file = new UserSettingsFile(_dir);
        file.Write("0");
        var tasks = Enumerable.Range(1, 6).Select(n => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                file.Write(new string((char)('a' + n), 2000));
                var read = file.Read()!;
                Assert.True(read.Length == 2000 && read.Distinct().Count() == 1);
            }
        }));
        await Task.WhenAll(tasks);
    }

    [Fact]
    public void The_notification_switches_are_taken_over_once_and_saved_in_one_place()
    {
        var page = new UserSettingsFile(_dir);
        page.Write("{\"Notifications\":{\"Enabled\":false,\"SoundEnabled\":true}}");
        var store = new SettingsFile(FilePath);
        var switches = new NotificationSwitches(store, page);
        Assert.Equal((false, true), (switches.Enabled, switches.Sound));
        switches.SetEnabled(true);
        switches.SetSound(false);
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

    private sealed class Flaky(ISettingsStore inner) : ISettingsStore
    {
        public bool CannotRead, CannotWrite;
        public string? Get(string section, string key) => CannotRead ? throw new IOException("held") : inner.Get(section, key);
        public void Set(string section, IReadOnlyDictionary<string, string> values)
        {
            if (CannotWrite) throw new IOException("held");
            inner.Set(section, values);
        }
    }

    [Fact]
    public void Switches_that_could_not_be_taken_over_work_and_are_saved_with_the_next_change()
    {
        var page = new UserSettingsFile(_dir);
        page.Write("{\"Notifications\":{\"Enabled\":false,\"SoundEnabled\":false}}");
        var store = new Flaky(new SettingsFile(FilePath)) { CannotWrite = true };
        var switches = new NotificationSwitches(store, page);
        Assert.Equal((false, false), (switches.Enabled, switches.Sound));
        Assert.Throws<IOException>(() => switches.SetSound(true));
        Assert.Equal((false, true), (switches.Enabled, switches.Sound));
        store.CannotWrite = false;
        switches.SetEnabled(false);
        var restarted = new NotificationSwitches(new SettingsFile(FilePath));
        Assert.Equal((false, true), (restarted.Enabled, restarted.Sound));
    }

    [Fact]
    public void Switches_that_could_not_be_read_write_nothing_and_later_only_what_was_changed()
    {
        var file = new SettingsFile(FilePath);
        file.Set("Notifications", new Dictionary<string, string> { ["IsEnabled"] = "False", ["IsSoundEnabled"] = "False" });
        var before = File.ReadAllText(FilePath);
        var page = new UserSettingsFile(_dir);
        page.Write("{\"Notifications\":{\"Enabled\":true,\"SoundEnabled\":true}}");
        var store = new Flaky(file) { CannotRead = true };
        var switches = new NotificationSwitches(store, page);
        // They work, as if never changed
        Assert.Equal((true, true), (switches.Enabled, switches.Sound));
        Assert.Equal(before, File.ReadAllText(FilePath));
        store.CannotRead = false;
        switches.SetSound(true);
        var restarted = new NotificationSwitches(new SettingsFile(FilePath));
        Assert.Equal((false, true), (restarted.Enabled, restarted.Sound));
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

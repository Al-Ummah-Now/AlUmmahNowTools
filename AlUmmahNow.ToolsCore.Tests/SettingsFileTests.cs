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

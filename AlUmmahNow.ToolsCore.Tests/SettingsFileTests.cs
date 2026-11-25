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

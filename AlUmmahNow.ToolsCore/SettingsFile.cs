using System.Text.Json;

namespace AlUmmahNow.ToolsCore;

/// <summary>What the person chose on a page, kept between runs.</summary>
public interface ISettingsStore
{
    /// <exception cref="IOException">What was saved cannot be read, and never was (<see cref="UnauthorizedAccessException"/> too)</exception>
    string? Get(string section, string key);

    void Set(string section, IReadOnlyDictionary<string, string> values);
}

/// <summary>
/// A small JSON file beside the app's other data. Windows' ApplicationData is not used: Uno has no containers on the desktop,
/// so what was saved there was never read, and reading it threw.
/// </summary>
public sealed class SettingsFile : ISettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, Dictionary<string, string>?>? _last;

    public SettingsFile(string path) => _path = path;

    public static SettingsFile Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNow", "Tools", "settings.json"));

    public string? Get(string section, string key)
    {
        lock (_gate)
        {
            Dictionary<string, Dictionary<string, string>?> all;
            try
            {
                all = _last = Read();
            }
            catch (Exception ex) when (_last is not null && (ex is IOException or UnauthorizedAccessException))
            {
                // Not readable now: as it was last
                all = _last;
            }
            return all.TryGetValue(section, out var values) && values is not null && values.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <exception cref="IOException">The file could not be read (so what else is in it cannot be kept) or written</exception>
    public void Set(string section, IReadOnlyDictionary<string, string> values)
    {
        lock (_gate)
        {
            var all = Read();
            if (!all.TryGetValue(section, out var saved) || saved is null) all[section] = saved = new Dictionary<string, string>();
            foreach (var (key, value) in values) saved[key] = value;
            AtomicFile.Write(_path, JsonSerializer.Serialize(all, Json));
            _last = all;
        }
    }

    /// <summary>A file that is missing or damaged is an empty one; one that cannot be read now is not.</summary>
    private Dictionary<string, Dictionary<string, string>?> Read()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>?>>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return new();
        }
    }
}

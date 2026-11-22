using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlUmmahNow.ToolsCore;

public enum ToolsTheme
{
    Dark,
    Light,
}

/// <summary>
/// What the app remembers between runs: language (en/ar), theme, environment and the speech model. A small JSON file
/// next to the app's other data. Theme is Light or Dark, never "System" (wiki Theming, owner
/// 2026-11-13): until the first click nothing is saved and the app follows the device.
/// </summary>
public sealed class ToolsPreferences
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public string Language { get; set; } = "en";
    /// <summary>Null until the viewer picks one: follow the device</summary>
    public ToolsTheme? Theme { get; set; }
    public ToolsEnvironmentKind Environment { get; set; } = ToolsEnvironmentKind.Production;
    /// <summary>The whisper model for transcription on this computer (TA-13); null: the default</summary>
    public string? SpeechModel { get; set; }

    [JsonIgnore] public bool IsRightToLeft => Language == "ar";

    /// <summary>The theme to show: the saved choice, else the device's</summary>
    public ToolsTheme EffectiveTheme(bool deviceIsDark) => Theme ?? (deviceIsDark ? ToolsTheme.Dark : ToolsTheme.Light);

    public static string DefaultPath =>
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "AlUmmahNow", "Tools", "preferences.json");

    /// <summary>Reads the file; anything missing or unreadable falls back to the defaults.</summary>
    public static ToolsPreferences Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new ToolsPreferences();
            var loaded = JsonSerializer.Deserialize<ToolsPreferences>(File.ReadAllText(path), Json) ?? new ToolsPreferences();
            if (loaded.Language is not ("en" or "ar")) loaded.Language = "en";
            if (loaded.Theme is { } theme && !Enum.IsDefined(theme)) loaded.Theme = null;
            if (!Enum.IsDefined(loaded.Environment)) loaded.Environment = ToolsEnvironmentKind.Production;
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ToolsPreferences();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}

using System.Text.Json;

namespace AlUmmahNow.ToolsCore;

/// <summary>The two notification switches: one place they are saved in, and both are on until they were changed.</summary>
public sealed class NotificationSwitches
{
    private const string Section = "Notifications";
    private readonly ISettingsStore _store;

    /// <param name="old">The Settings page's file, where they were saved too: taken over once</param>
    public NotificationSwitches(ISettingsStore store, UserSettingsFile? old = null)
    {
        _store = store;
        var (enabled, sound) = (Saved("IsEnabled"), Saved("IsSoundEnabled"));
        if (enabled is null && sound is null && Old(old) is { } before)
        {
            (enabled, sound) = before;
            Save(enabled ?? true, sound ?? true);
        }
        (Enabled, Sound) = (enabled ?? true, sound ?? true);
    }

    public bool Enabled { get; private set; }

    public bool Sound { get; private set; }

    public void Save(bool enabled, bool sound)
    {
        (Enabled, Sound) = (enabled, sound);
        _store.Set(Section, new Dictionary<string, string> { ["IsEnabled"] = enabled.ToString(), ["IsSoundEnabled"] = sound.ToString() });
    }

    private bool? Saved(string key) => bool.TryParse(_store.Get(Section, key), out var value) ? value : null;

    private static (bool? Enabled, bool? Sound)? Old(UserSettingsFile? file)
    {
        try
        {
            if (file?.Read() is not { } text) return null;
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Notifications", out var saved) || saved.ValueKind != JsonValueKind.Object) return null;
            bool? Value(string key) => saved.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? saved.GetProperty(key).GetBoolean() : null;
            return (Value("Enabled"), Value("SoundEnabled"));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

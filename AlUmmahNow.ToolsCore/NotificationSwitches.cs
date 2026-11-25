using System.Text.Json;

namespace AlUmmahNow.ToolsCore;

/// <summary>
/// The two notification switches: one place they are saved in, and both are on until they were changed. A place that cannot be
/// read or written now leaves them working, and only what was changed is saved later.
/// </summary>
public sealed class NotificationSwitches
{
    private const string Section = "Notifications";
    private readonly ISettingsStore _store;
    // What is to be saved with the next change: what was taken over and could not be saved
    private readonly Dictionary<string, string> _unsaved = new();

    /// <param name="old">The Settings page's file, where they were saved too: taken over once</param>
    public NotificationSwitches(ISettingsStore store, UserSettingsFile? old = null)
    {
        _store = store;
        bool? enabled = null, sound = null;
        try
        {
            (enabled, sound) = (Saved("IsEnabled"), Saved("IsSoundEnabled"));
            if (enabled is null && sound is null && Old(old) is { } before)
            {
                (enabled, sound) = before;
                if (enabled is { } e) _unsaved["IsEnabled"] = e.ToString();
                if (sound is { } s) _unsaved["IsSoundEnabled"] = s.ToString();
                Save();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not readable: nothing is written over it
        }
        (Enabled, Sound) = (enabled ?? true, sound ?? true);
    }

    public bool Enabled { get; private set; }

    public bool Sound { get; private set; }

    /// <exception cref="IOException">Changed, and not saved yet: saved with the next change (<see cref="UnauthorizedAccessException"/> too)</exception>
    public void SetEnabled(bool value)
    {
        Enabled = value;
        _unsaved["IsEnabled"] = value.ToString();
        Save();
    }

    /// <inheritdoc cref="SetEnabled"/>
    public void SetSound(bool value)
    {
        Sound = value;
        _unsaved["IsSoundEnabled"] = value.ToString();
        Save();
    }

    private void Save()
    {
        if (_unsaved.Count == 0) return;
        _store.Set(Section, new Dictionary<string, string>(_unsaved));
        _unsaved.Clear();
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

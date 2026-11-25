namespace AlUmmahNow.ToolsCore;

/// <summary>
/// What the Settings page saves and the app's start reads: one file. It was saved in one folder and read from another; what
/// is in the old place is taken over once, when the file is not there yet.
/// </summary>
public sealed class UserSettingsFile
{
    private readonly object _gate = new();
    private readonly string _old;

    /// <param name="root">The folder the app's data is in</param>
    public UserSettingsFile(string root)
    {
        Path = System.IO.Path.Combine(root, "AlUmmahNow", "VisualImporterUno", "settings.json");
        _old = System.IO.Path.Combine(root, "AlUmmahNow", "VisualImporter", "settings.json");
    }

    public static UserSettingsFile Default { get; } = new(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public string Path { get; }

    /// <summary>The file's text, or null when nothing was saved.</summary>
    public string? Read()
    {
        lock (_gate)
        {
            if (File.Exists(Path)) return File.ReadAllText(Path);
            if (!File.Exists(_old)) return null;
            var text = File.ReadAllText(_old);
            try
            {
                // Another copy may have saved meanwhile: what is there is never written over
                if (!AtomicFile.WriteNew(Path, text)) return File.ReadAllText(Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Taken over at the next read
            }
            return text;
        }
    }

    public void Write(string text)
    {
        lock (_gate) AtomicFile.Write(Path, text);
    }
}

namespace AlUmmahNow.ToolsCore;

/// <summary>
/// What the Settings page saves and the app's start reads: one file. It was saved in one folder and read from another; what
/// is in the old place is taken over once, as the app starts, before anything saves (one copy of the app runs at a time).
/// </summary>
public sealed class UserSettingsFile
{
    private readonly object _gate = new();
    private readonly string _old;
    private string? _last;
    private bool _known;

    /// <param name="root">The folder the app's data is in</param>
    public UserSettingsFile(string root)
    {
        Path = System.IO.Path.Combine(root, "AlUmmahNow", "VisualImporterUno", "settings.json");
        _old = System.IO.Path.Combine(root, "AlUmmahNow", "VisualImporter", "settings.json");
    }

    public static UserSettingsFile Default { get; } = new(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public string Path { get; }

    /// <summary>Call once, as the app starts. A failure is no one's: what was saved before is taken over at the next start.</summary>
    public void TakeOverOld()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(Path) && File.Exists(_old)) AtomicFile.Write(Path, File.ReadAllText(_old));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>The file's text, or null when nothing was saved. One that cannot be read now is as it was last.</summary>
    /// <exception cref="IOException">It cannot be read, and never was (<see cref="UnauthorizedAccessException"/> too)</exception>
    public string? Read()
    {
        lock (_gate)
        {
            try
            {
                _last = File.ReadAllText(Path);
                _known = true;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                _last = null;
                _known = true;
            }
            catch (Exception ex) when (_known && (ex is IOException or UnauthorizedAccessException))
            {
            }
            return _last;
        }
    }

    public void Write(string text)
    {
        lock (_gate)
        {
            AtomicFile.Write(Path, text);
            (_last, _known) = (text, true);
        }
    }
}

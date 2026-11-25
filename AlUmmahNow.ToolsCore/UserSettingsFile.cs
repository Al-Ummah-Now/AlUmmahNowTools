namespace AlUmmahNow.ToolsCore;

/// <summary>
/// What the Settings page saves and the app's start reads: one file. It was saved in one folder and read from another; what
/// is in the old place is taken over once, when the file is not there yet.
/// </summary>
public sealed class UserSettingsFile
{
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
        if (File.Exists(Path)) return File.ReadAllText(Path);
        if (!File.Exists(_old)) return null;
        try
        {
            using (Hold())
            {
                // Another part of the app, or another copy, may have saved meanwhile: what is there is never written over
                if (File.Exists(Path)) return File.ReadAllText(Path);
                var text = File.ReadAllText(_old);
                AtomicFile.Write(Path, text);
                return text;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Taken over at the next read
            return File.ReadAllText(File.Exists(Path) ? Path : _old);
        }
    }

    public void Write(string text)
    {
        using (Hold()) AtomicFile.Write(Path, text);
    }

    /// <summary>One at a time, of every copy of the app: a move that must not write over a file is not safe on its own everywhere.</summary>
    private FileStream Hold()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        for (var tries = 0; ; tries++)
        {
            try
            {
                return new FileStream(Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (tries < 100)
            {
                Thread.Sleep(20);
            }
        }
    }
}

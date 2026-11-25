using System.Text.RegularExpressions;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>The app's own folders for what it makes of the person's files: one folder per file for the renditions, one JSON per transcript.</summary>
public sealed record WorkFolders(string Renditions, string Transcripts)
{
    public static WorkFolders Default => new(EncodeStep.DefaultWorkRoot, TranscribeStep.DefaultWorkRoot);
}

/// <summary>
/// The one way the import deletes anything. A path may come from the saved queue, so it is deleted only when it is, written
/// out in full, exactly the name the app gives the file's work directly inside the app's own folder, and neither it nor that
/// folder is a link or a junction. Anything else is left alone and noted: the person's videos, sidecars and folders are never
/// deleted.
/// </summary>
public static class WorkFiles
{
    /// <summary>An item's id as the app makes it (a GUID's 32 hex digits); never a path</summary>
    private static readonly Regex Id = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    /// <summary>Told what was not deleted and why (the app's log on this computer).</summary>
    public static Action<string>? Skipped { get; set; }

    /// <summary>Deletes a file's renditions: <c>root/id</c>, and nothing else.</summary>
    internal static bool DeleteFolder(string? path, string root, string id)
    {
        if (string.IsNullOrEmpty(path) || !Check(path, root, id, id)) return false;
        path = Path.Combine(Path.GetFullPath(root), id);
        try
        {
            if (Directory.Exists(path)) DeleteTree(new DirectoryInfo(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next run's clean-up; the queue does not depend on it
            return false;
        }
    }

    /// <summary>
    /// Links are removed themselves, never followed. Not .NET's own recursive delete: on Windows it takes a junction for a
    /// mounted volume, and stops with an error after removing it.
    /// </summary>
    private static void DeleteTree(DirectoryInfo folder)
    {
        foreach (var entry in folder.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }).ToList())
        {
            var link = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            var directory = (entry.Attributes & FileAttributes.Directory) != 0;
            if (directory && !link) DeleteTree((DirectoryInfo)entry);
            // A junction or a directory link on Windows goes as an (empty) directory; everything else, and any link elsewhere, as a file
            else if (directory && OperatingSystem.IsWindows()) Directory.Delete(entry.FullName, recursive: false);
            else File.Delete(entry.FullName);
        }
        Directory.Delete(folder.FullName, recursive: false);
    }

    /// <summary>Deletes a file's transcript or sound: <c>root/id.json</c> or <c>root/id.wav</c>, and nothing else.</summary>
    internal static bool DeleteFile(string? path, string root, string id, string extension)
    {
        if (string.IsNullOrEmpty(path) || !Check(path, root, id, id + extension)) return false;
        path = Path.Combine(Path.GetFullPath(root), id + extension);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Check(string path, string root, string id, string name)
    {
        var why = Problem(path, root, id, name);
        if (why is null) return true;
        Skipped?.Invoke($"Not deleted ({why}): {path}");
        return false;
    }

    private static string? Problem(string path, string root, string id, string name)
    {
        if (!Id.IsMatch(id)) return "not an id the app gave";
        try
        {
            if (!Path.IsPathFullyQualified(path)) return "not a full path";
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var same = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(full, Path.Combine(folder, name), same)) return "not the app's own work";
            // The work, the folder it is in, and the app's folder around that
            string? at = full;
            for (var level = 0; level < 3 && at is not null; level++)
            {
                if (IsLink(at)) return "a link on the way";
                at = Path.GetDirectoryName(at);
            }
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return ex.GetType().Name;
        }
    }

    /// <summary>A symbolic link, a junction or a mount point; false for what is not there.</summary>
    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }
}

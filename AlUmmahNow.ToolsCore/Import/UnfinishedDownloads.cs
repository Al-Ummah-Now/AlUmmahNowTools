using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>
/// Files a downloader has not finished, or made on its way, told by the names the downloaders themselves give: never by a word in
/// an ordinary name ("temp", "part two"). The scan of the Import page and the watched folders' service (which compiles this very
/// file) leave them out.
/// </summary>
internal static class UnfinishedDownloads
{
    /// <summary>yt-dlp's file while it merges: <c>Title.temp.webm</c></summary>
    private static readonly Regex Merging = new(@"^.+\.temp\.[^.]+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>One format of a video, before yt-dlp merges it: <c>Title.f137.mp4</c>, <c>Title.f251-drc.webm</c></summary>
    private static readonly Regex Format = new(@"^(?<title>.+)\.f\d+(-\w+)?\.[^.]+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly string[] Endings = { ".part", ".ytdl", ".crdownload", ".download", ".partial" };

    private static readonly string[] Videos = { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".flv" };

    /// <summary>A file where it is: by its name, what is beside it, and the folders it is in below <paramref name="root"/>.</summary>
    public static bool IsAt(string path, string root)
    {
        if (Path.GetDirectoryName(path) is not { } folder) return false;
        if (IsIn(folder, root)) return true;
        var name = Path.GetFileName(path);
        // Only for one format of a video it matters what is beside it
        if (!Format.IsMatch(name)) return Is(name, Array.Empty<string>());
        var names = new List<string>();
        try
        {
            names.AddRange(Directory.EnumerateFiles(folder).Select(f => Path.GetFileName(f)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // By its own name alone
        }
        return Is(name, names);
    }

    /// <summary>The folder is one a downloader keeps its file in, or inside one, below <paramref name="root"/> (which was chosen).</summary>
    public static bool IsIn(string folder, string root) =>
        Path.GetRelativePath(root, folder).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(IsFolder);

    /// <summary>Safari keeps what it downloads in a folder of this name.</summary>
    public static bool IsFolder(string name) => name.EndsWith(".download", StringComparison.OrdinalIgnoreCase) && name.Length > ".download".Length;

    /// <param name="beside">The names of the files in its folder</param>
    public static bool Is(string name, IReadOnlyCollection<string> beside)
    {
        if (Merging.IsMatch(name) || Endings.Any(e => Ends(name, e))) return true;
        if (Format.Match(name) is not { Success: true } format) return false;
        // Kept on purpose (yt-dlp -k) when nothing says it is on its way: the merged file, or one that is still downloaded, is beside it
        var title = format.Groups["title"].Value;
        return beside.Any(other => other.StartsWith(title + ".", StringComparison.OrdinalIgnoreCase)
            && (Merging.IsMatch(other) || Ends(other, ".part") || Ends(other, ".ytdl")
                || (IsVideo(other) && Path.GetFileNameWithoutExtension(other).Equals(title, StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>The video it would become (<c>lesson.mp4</c> of <c>lesson.mp4.part</c>); null: it is none.</summary>
    public static string? VideoName(string name)
    {
        if (IsVideo(name)) return name;
        var ending = Endings.FirstOrDefault(e => Ends(name, e));
        if (ending is null or ".ytdl") return null;
        var video = name[..^ending.Length];
        // Chrome does not say what it is until it is confirmed
        return IsVideo(video) || (ending == ".crdownload" && !Path.HasExtension(video)) ? video : null;
    }

    private static bool IsVideo(string name) => Videos.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    private static bool Ends(string name, string ending) => name.Length > ending.Length && name.EndsWith(ending, StringComparison.OrdinalIgnoreCase);
}

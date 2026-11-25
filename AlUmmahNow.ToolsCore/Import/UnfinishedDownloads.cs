using System.Text.RegularExpressions;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>
/// Files a downloader has not finished, or made on its way, told by the names the downloaders themselves give: never by a word in
/// an ordinary name ("temp", "part two").
/// </summary>
public static class UnfinishedDownloads
{
    /// <summary>yt-dlp's file while it merges: <c>Title.temp.webm</c></summary>
    private static readonly Regex Merging = new(@"^.+\.temp\.[^.]+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>One format of a video, before yt-dlp merges it: <c>Title.f137.mp4</c>, <c>Title.f251-drc.webm</c></summary>
    private static readonly Regex Format = new(@"^(?<title>.+)\.f\d+(-\w+)?\.[^.]+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly string[] Endings = { ".part", ".ytdl", ".crdownload", ".download", ".partial" };

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
                || (FolderScanner.IsVideo(other) && Path.GetFileNameWithoutExtension(other).Equals(title, StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>The video it would become (<c>lesson.mp4</c> of <c>lesson.mp4.part</c>); null: it is none.</summary>
    public static string? VideoName(string name)
    {
        if (FolderScanner.IsVideo(name)) return name;
        var ending = Endings.FirstOrDefault(e => Ends(name, e));
        if (ending is null or ".ytdl") return null;
        var video = name[..^ending.Length];
        // Chrome does not say what it is until it is confirmed
        return FolderScanner.IsVideo(video) || (ending == ".crdownload" && !Path.HasExtension(video)) ? video : null;
    }

    private static bool Ends(string name, string ending) => name.Length > ending.Length && name.EndsWith(ending, StringComparison.OrdinalIgnoreCase);
}

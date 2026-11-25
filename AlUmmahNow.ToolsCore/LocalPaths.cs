using System.Text.RegularExpressions;

namespace AlUmmahNow.ToolsCore;

/// <summary>
/// Takes the folders out of what leaves this computer: a path on a share, a drive or a Unix root keeps its last name alone, and
/// neither the server's nor the person's folders are told.
/// </summary>
public static class LocalPaths
{
    private const string Name = @"[^\\/:*?""<>|\r\n]+";

    private static readonly Regex Folders = new(
        // \\server\share\…, \\?\UNC\server\…, \\?\C:\…
        @"\\{2,}(?:\?\\+)?(?:[A-Za-z]:)?" + @"(?:\\*" + Name + @"[\\/]+)*"
        // C:\… and C:/…, not the "s:/" of https://
        + @"|(?<![A-Za-z])[A-Za-z]:(?:\\+|/(?!/))(?:" + Name + @"[\\/]+)*"
        // Where people's files, mounts and temporary files are on macOS and Linux; not an address' path
        + @"|(?<![\w/:.\-])/(?:Users|home|Volumes|mnt|media|run|private|tmp|var|opt|srv|root|data|Library|Applications)/(?:" + Name + "/)*",
        RegexOptions.CultureInvariant);

    public static string? Strip(string? text) => text is null ? null : Folders.Replace(text, "");
}

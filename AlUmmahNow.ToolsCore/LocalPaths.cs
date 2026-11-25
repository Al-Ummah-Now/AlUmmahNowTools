using System.Text;
using System.Text.RegularExpressions;

namespace AlUmmahNow.ToolsCore;

/// <summary>
/// Takes the folders out of what leaves this computer: a path on a share, a drive or a Unix root keeps its last name alone, and
/// neither the server's nor the person's folders are told. What stands after the path on its line stays.
/// </summary>
public static class LocalPaths
{
    /// <summary>Where a path begins.</summary>
    private static readonly Regex Roots = new(
        // \\server\…, \\?\UNC\server\…, \\?\C:\…
        @"(?<windows>\\{2,}(?:\?\\+(?:UNC\\+)?)?(?:[A-Za-z]:\\+)?"
        // C:\… and C:/…, not the "s:/" of https://
        + @"|(?<![A-Za-z])[A-Za-z]:(?:\\+|/(?!/)))"
        // Where people's files, mounts and temporary files are on macOS and Linux; not an address' path
        + @"|(?<![\w/:.\-])/(?i:Users|home|Volumes|mnt|media|run|private|tmp|var|opt|srv|root|data|Library|Applications)/",
        RegexOptions.CultureInvariant);

    /// <summary>A name ends with the extension of a file: not one a longer name goes on after, and not a folder's.</summary>
    private static readonly Regex Extension = new(
        @"\.(?:mp4|m4v|mov|mkv|webm|avi|wmv|flv|mpe?g|ts|m4a|mp3|wav|aac|flac|opus|ogg|srt|vtt|sbv|ass|json|csv|txt|xml|html?|jpe?g|png|webp|gif|bmp|part|ytdl|crdownload|partial|tmp|log|db|m3u8|m4s|zip|nupkg|exe|dll|dylib|so|cs|xaml)(?![\w\-\\/]|\.\w)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string? Strip(string? text)
    {
        if (text is null) return null;
        var result = new StringBuilder(text.Length);
        var at = 0;
        while (Roots.Match(text, at) is { Success: true } root)
        {
            result.Append(text, at, root.Index - at);
            at = FoldersEnd(text, root);
        }
        return result.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>Behind the last separator of the path.</summary>
    private static int FoldersEnd(string text, Match root)
    {
        var windows = root.Groups["windows"].Success;
        var begin = root.Index + root.Length;
        var limit = text.IndexOfAny([':', '*', '?', '"', '<', '>', '|', '\r', '\n'], begin) is var stop and >= 0 ? stop : text.Length;
        // Quoted, it ends with its quote (which a name may have within it)
        if (root.Index > 0 && Closing(text[root.Index - 1]) is { } quote)
        {
            for (var i = text.IndexOf(quote, begin); i >= 0 && i < limit; i = text.IndexOf(quote, i + 1))
                if (i + 1 == text.Length || !char.IsLetterOrDigit(text[i + 1])) limit = i;
        }
        if (Extension.Match(text, begin, limit - begin) is { Success: true } file && Bounded(text, file)) limit = file.Index + file.Length;

        var end = begin;
        for (var i = begin; i < limit; i++)
        {
            if (text[i] is not ('\\' or '/')) continue;
            var name = text.AsSpan(end, i - end);
            if (name.Length > 0)
            {
                // "a to \\nas": no name ends with a space; "lesson (video/mp4)": a slash in words is none of a Windows path
                if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1])) break;
                if (windows && text[i] == '/' && name.IndexOf(' ') >= 0) break;
                if (!windows && text[i] == '\\') break;
            }
            end = i + 1;
        }
        return end;
    }

    /// <summary>The expression saw the text up to the limit only.</summary>
    private static bool Bounded(string text, Match file) => Extension.Match(text, file.Index) is { Success: true } whole && whole.Index == file.Index;

    private static char? Closing(char opening) => opening switch
    {
        '\'' => '\'',
        '"' => '"',
        '‘' => '’',
        '“' => '”',
        '`' => '`',
        _ => null,
    };
}

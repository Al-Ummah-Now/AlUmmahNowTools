using System.Text.RegularExpressions;
using AlUmmahNow.ToolsCore.Import;

namespace AlUmmahNow.ToolsCore;

/// <summary>
/// No folder of this computer, or of its shares, leaves it. Paths the app knows are replaced as they are, by their file's name; a
/// line that still looks as if it had one is not sent at all. Where an unknown path ends is never guessed.
/// </summary>
public static class LocalPaths
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    /// <summary>However long a line is; one that takes longer is not sent. Before the expressions that use it: fields start in order</summary>
    private static readonly TimeSpan Time = TimeSpan.FromSeconds(1);

    /// <summary>A backslash, a drive, the home folder, a slash that no word is before; also written as in an address.</summary>
    private static readonly Regex Absolute = new(@"\\|(?<!\p{L})[a-z]:/|(?<!\w)~(?:[a-z_][a-z0-9._-]*)?/|(?<!\w)/(?!\s|$)|%2f|%5c", Options, Time);

    private static readonly Regex WebAddress = new(@"\bhttps?://[^\s\\]+", Options, Time);

    private static readonly string[] Own =
    [
        Path.GetTempPath(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        EncodeStep.DefaultWorkRoot,
        TranscribeStep.DefaultWorkRoot,
    ];

    /// <param name="files">The files it may be about, with their whole paths</param>
    /// <param name="instead">What is sent in place of a line that may still name a folder</param>
    public static string Clean(string? text, string instead, IEnumerable<string>? files = null)
    {
        text ??= "";
        var known = (files ?? []).Where(f => Segments(f).Length > 0).Distinct().OrderByDescending(f => f.Length).ToList();
        var names = known.Select(f => (Pattern: new Regex(Exactly(f) + @"(?![\\/\w])", Options, Time), Name: Segments(f)[^1])).ToList();
        // A folder alone, at the end of what is said or before a colon or a quote
        var folders = known.SelectMany(Parents).Concat(Own.SelectMany(o => Parents(o).Prepend(o))).Where(f => Segments(f) is { Length: > 0 } s && !(s.Length == 1 && s[0].EndsWith(':'))).Distinct().OrderByDescending(f => f.Length)
            .Select(f => new Regex(Exactly(f) + @"(?>[\\/]*)(?=$|[:""])", Options, Time)).ToList();
        return string.Join("\n", text.Split('\n').Select(line => Line(line) ?? instead).Distinct());

        string? Line(string line)
        {
            try
            {
                line = line.TrimEnd('\r').Normalize();
            }
            catch (ArgumentException)
            {
                return null;
            }
            try
            {
                foreach (var (pattern, name) in names) line = pattern.Replace(line, _ => name);
                var before = line;
                foreach (var folder in folders) line = folder.Replace(line, "…");
                var rest = WebAddress.Replace(line, " ");
                // With a folder taken out, any separator left may be the rest of a longer path
                return Absolute.IsMatch(rest) || (line != before && rest.IndexOfAny(['/', '\\']) >= 0) ? null : line;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }
    }

    /// <summary>What the app knows of a file's places: as it was chosen, through its share, and the files beside it.</summary>
    public static IEnumerable<string> Of(ScannedVideo video) =>
        new[] { video.Path, video.ChaptersPath, video.ThumbnailPath, video.MetadataPath }.Concat(video.Captions.Select(c => c.Path)).OfType<string>().SelectMany(Import.Sources.SourceAccess.Default.Known);

    private static string[] Segments(string path) => path.Normalize().Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<string> Parents(string path)
    {
        for (var at = path.TrimEnd('/', '\\').LastIndexOfAny(['/', '\\']); at > 0; at = path.LastIndexOfAny(['/', '\\'], at - 1))
            yield return path[..at];
    }

    /// <summary>With every separator, doubled or not, and the prefixes Windows puts before a long path; quotes and spaces as they may be written.</summary>
    private static string Exactly(string path) =>
        // Atomic: a line of thousands of slashes is no reason to try every way of splitting them
        (path[0] is '/' or '\\' ? @"(?>[\\/]+\?[\\/]+UNC[\\/]+|[\\/]+)" : @"(?>[\\/]+\?[\\/]+)?")
        + string.Join(@"(?>[\\/]+)", Segments(path).Select(s => Regex.Escape(s).Replace("'", "['’‘]").Replace(@"\ ", @"(?:\ |%20)")));
}

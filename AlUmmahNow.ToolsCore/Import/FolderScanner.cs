using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlUmmahNow.ToolsCore.Import.Sources;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>Something the scan noticed about a file, shown in the review table in the app's words.</summary>
public enum ScanNoteCode
{
    /// <summary>A caption file without a language in its name, and no language in the metadata or the batch</summary>
    CaptionLanguageUnknown,
    /// <summary>Two caption files for one language: the first is kept</summary>
    CaptionLanguageTwice,
    /// <summary>The video's <c>name.json</c> could not be read</summary>
    MetadataUnreadable,
    /// <summary>The folder's CSV could not be read</summary>
    CsvUnreadable,
    /// <summary>The file is larger than an upload may be (5 GB)</summary>
    TooLarge,
    /// <summary>Its name is one a downloader gives a file it has not finished</summary>
    UnfinishedDownload,
}

public sealed record ScanNote(ScanNoteCode Code, string Detail);

public sealed record CaptionFile(string Language, string Path);

/// <summary>A video found in the folder, with the files next to it that belong to it.</summary>
public sealed record ScannedVideo(
    string Path,
    long Size,
    long LastModifiedMs,
    string MimeType,
    IReadOnlyList<CaptionFile> Captions,
    string? ChaptersPath,
    string? ThumbnailPath,
    string? MetadataPath,
    ImportMetadata Metadata,
    IReadOnlyList<ScanNote> Notes)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed record ScanResult(IReadOnlyList<ScannedVideo> Videos, IReadOnlyList<ScanNote> Notes)
{
    /// <summary>Left out: unfinished downloads (<see cref="UnfinishedDownloads"/>), which can be added all the same</summary>
    public IReadOnlyList<ScannedVideo> Unfinished { get; init; } = Array.Empty<ScannedVideo>();
}

/// <summary>
/// Finds the videos in a folder and their sidecars, named after the video (<c>lesson-01.mp4</c>):
/// <list type="bullet">
/// <item>captions <c>lesson-01.ar.srt</c>, <c>lesson-01.en.vtt</c>, <c>lesson-01.sbv</c> (a file without a language takes
/// the video's language from its metadata, else the batch's);</item>
/// <item>chapters <c>lesson-01.chapters.txt</c> (lines such as <c>0:00 Introduction</c>) or <c>lesson-01.chapters.json</c>;</item>
/// <item>a thumbnail <c>lesson-01.jpg</c> (or <c>.thumbnail.jpg</c>; jpeg, png, webp);</item>
/// <item>metadata <c>lesson-01.json</c>, and one CSV per folder with a row per video (<see cref="MetadataCsv"/>).</item>
/// </list>
/// </summary>
public static class FolderScanner
{
    /// <summary>The TUS proxy's Tus-Max-Size</summary>
    public const long MaxUploadBytes = 5L * 1024 * 1024 * 1024;

    internal static readonly Regex LanguagePattern = new("^[a-z]{2,3}(-[a-z0-9]{2,8})?$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> VideoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/x-m4v",
        [".mov"] = "video/quicktime",
        [".mkv"] = "video/x-matroska",
        [".webm"] = "video/webm",
        [".avi"] = "video/x-msvideo",
    };

    /// <summary>Hidden and system files, and links (a link could point outside the folder, or loop back into it)</summary>
    private const System.IO.FileAttributes Skipped = System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System | System.IO.FileAttributes.ReparsePoint;

    private static readonly string[] CaptionTypes = { ".vtt", ".srt", ".sbv" };

    internal static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    private static readonly string[] ThumbnailWords = { "", "thumbnail", "thumb", "cover" };

    public static bool IsVideo(string path) => VideoTypes.ContainsKey(Path.GetExtension(path));

    public static string MimeTypeOf(string path) => VideoTypes.TryGetValue(Path.GetExtension(path), out var t) ? t : "video/mp4";

    /// <param name="folder">The folder to scan.</param>
    /// <param name="includeSubfolders">Also every folder inside it (each may have its own CSV).</param>
    /// <param name="batchLanguage">The language of caption files named without one, when the video's metadata has none.</param>
    /// <remarks>A folder on a mapped drive is read through its network path; every path in the result is under <paramref name="folder"/>, as it was chosen.</remarks>
    public static ScanResult Scan(string folder, bool includeSubfolders, string? batchLanguage, CancellationToken ct = default, SourceAccess? source = null) =>
        Scan(source ?? SourceAccess.Default, folder, includeSubfolders, batchLanguage, ct);

    private static ScanResult Scan(SourceAccess source, string folder, bool includeSubfolders, string? batchLanguage, CancellationToken ct) =>
        source.Scan(folder, root => ScanRoot(source, root, folder, includeSubfolders, batchLanguage, ct), ct);

    private static ScanResult ScanRoot(SourceAccess source, string root, string folder, bool includeSubfolders, string? batchLanguage, CancellationToken ct)
    {
        string Show(string path) => root == folder ? path : Path.Combine(folder, Path.GetRelativePath(root, path));
        var videos = new List<ScannedVideo>();
        var notes = new List<ScanNote>();
        var unfinished = new List<ScannedVideo>();
        var folders = includeSubfolders
            ? new[] { root }.Concat(Directory.EnumerateDirectories(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = Skipped }))
            : new[] { root };
        foreach (var dir in folders)
        {
            ct.ThrowIfCancellationRequested();
            var downloads = dir != root && Path.GetRelativePath(root, dir).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(UnfinishedDownloads.IsFolder);
            ScanOne(source, dir, Show, batchLanguage, downloads ? unfinished : videos, unfinished, notes);
        }
        return new ScanResult(videos, notes) { Unfinished = unfinished };
    }

    /// <summary>That file alone: a share that drops is the scan's to try again.</summary>
    private static bool Unreadable(SourceAccess source, Exception ex, string path) => ex is FormatException
        || (ex is IOException or UnauthorizedAccessException && ReadFailure.Of(ex, source.IsNetwork(path)).Kind != ReadKind.Network);

    private static string Why(Exception ex) => ex is FormatException ? ex.Message : ImportQueue.Describe(ex);

    private static void ScanOne(SourceAccess source, string dir, Func<string, string> show, string? batchLanguage, List<ScannedVideo> videos, List<ScannedVideo> unfinished, List<ScanNote> notes)
    {
        var files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = Skipped })
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        MetadataCsv? csv = null;
        var csvs = files.Where(f => Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase)).ToList();
        var csvPath = csvs.FirstOrDefault(f => Path.GetFileName(f).Equals("metadata.csv", StringComparison.OrdinalIgnoreCase)) ?? (csvs.Count == 1 ? csvs[0] : null);
        if (csvPath is not null)
        {
            try
            {
                csv = MetadataCsv.Parse(File.ReadAllText(csvPath, Encoding.UTF8));
            }
            catch (Exception ex) when (Unreadable(source, ex, csvPath))
            {
                notes.Add(new ScanNote(ScanNoteCode.CsvUnreadable, $"{Path.GetFileName(csvPath)}: {Why(ex)}"));
            }
        }

        var names = files.Select(f => Path.GetFileName(f)!).ToList();
        var downloads = files.Where(f => UnfinishedDownloads.Is(Path.GetFileName(f), names)).ToHashSet();
        var videoFiles = files.Where(f => downloads.Contains(f) ? UnfinishedDownloads.VideoName(Path.GetFileName(f)) is not null : IsVideo(f)).ToList();
        var bases = videoFiles.ToDictionary(v => v, v => Path.GetFileNameWithoutExtension(v));
        var sidecars = videoFiles.ToDictionary(v => v, _ => new List<(string Path, string Remainder)>());
        foreach (var file in files)
        {
            if (bases.ContainsKey(file)) continue;
            var name = Path.GetFileName(file);
            // The video whose name is the longest prefix: "talk.part2.ar.srt" belongs to "talk.part2.mp4", not "talk.mp4"
            var owner = videoFiles
                .Where(v => name.Length > bases[v].Length + 1 && name.StartsWith(bases[v] + ".", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => bases[v].Length)
                .FirstOrDefault();
            if (owner is not null) sidecars[owner].Add((file, name[(bases[owner].Length + 1)..]));
        }

        foreach (var video in videoFiles)
        {
            var info = new FileInfo(video);
            var own = new List<ScanNote>();
            string? metadataPath = null, chaptersPath = null, thumbnailPath = null;
            ImportMetadata? fromJson = null;
            var captionFiles = new List<(string Path, string? Language)>();
            foreach (var (path, rest) in sidecars[video])
            {
                // "ar.srt" → ".srt" and "ar"; "jpg" → ".jpg" and ""
                var dot = rest.LastIndexOf('.');
                var ext = "." + rest[(dot + 1)..].ToLowerInvariant();
                var middle = dot < 0 ? "" : rest[..dot];
                if (rest.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    metadataPath = path;
                    try
                    {
                        fromJson = MetadataJson.Parse(File.ReadAllText(path, Encoding.UTF8));
                    }
                    catch (Exception ex) when (Unreadable(source, ex, path))
                    {
                        own.Add(new ScanNote(ScanNoteCode.MetadataUnreadable, $"{Path.GetFileName(path)}: {Why(ex)}"));
                    }
                }
                else if (middle.Equals("chapters", StringComparison.OrdinalIgnoreCase) && ext is ".txt" or ".json")
                {
                    chaptersPath ??= path;
                }
                else if (CaptionTypes.Contains(ext))
                {
                    var language = middle.Length == 0 ? null : middle.ToLowerInvariant();
                    if (language is not null && !LanguagePattern.IsMatch(language)) continue;
                    captionFiles.Add((path, language));
                }
                else if (ImageTypes.ContainsKey(ext) && ThumbnailWords.Contains(middle.ToLowerInvariant()))
                {
                    thumbnailPath ??= path;
                }
            }

            var metadata = new ImportMetadata { FallbackTitle = Path.GetFileNameWithoutExtension(video) }
                .Merge(csv?.For(info.Name))
                .Merge(fromJson);
            var captions = new List<CaptionFile>();
            foreach (var (path, given) in captionFiles)
            {
                var language = given ?? metadata.Language ?? ImportMetadata.LanguageCode(batchLanguage);
                if (language is null)
                {
                    own.Add(new ScanNote(ScanNoteCode.CaptionLanguageUnknown, Path.GetFileName(path)));
                    continue;
                }
                if (captions.Any(c => c.Language == language))
                {
                    own.Add(new ScanNote(ScanNoteCode.CaptionLanguageTwice, Path.GetFileName(path)));
                    continue;
                }
                captions.Add(new CaptionFile(language, show(path)));
            }
            if (info.Length > MaxUploadBytes) own.Add(new ScanNote(ScanNoteCode.TooLarge, info.Name));

            var isDownload = downloads.Contains(video) || ReferenceEquals(videos, unfinished);
            if (isDownload) own.Add(new ScanNote(ScanNoteCode.UnfinishedDownload, info.Name));

            (isDownload ? unfinished : videos).Add(new ScannedVideo(
                show(video),
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                MimeTypeOf(isDownload ? UnfinishedDownloads.VideoName(info.Name) ?? video : video),
                captions,
                chaptersPath is null ? null : show(chaptersPath),
                thumbnailPath is null ? null : show(thumbnailPath),
                metadataPath is null ? null : show(metadataPath),
                metadata,
                own));
        }
    }
}

/// <summary>Chapters for the attach step: the text file as it is, or a JSON list turned into the same lines.</summary>
public static class ChapterFile
{
    /// <summary>
    /// The chapter lines (<c>0:00 Introduction</c>). A JSON file is a list, or <c>{ "chapters": [...] }</c>, of
    /// <c>{ "start": "1:05" | 65, "title": "…" }</c> (also <c>time</c>, <c>startTime</c>, <c>name</c>).
    /// </summary>
    /// <exception cref="FormatException">The JSON is not a chapter list.</exception>
    public static string Read(string path) => Parse(path, File.ReadAllText(path, Encoding.UTF8));

    /// <param name="path">For its extension: JSON or text.</param>
    public static string Parse(string path, string text)
    {
        return Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) ? FromJson(text) : text.Trim();
    }

    public static string FromJson(string json)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("chapters", out var inner)) root = inner;
        if (root.ValueKind != JsonValueKind.Array) throw new FormatException("The chapters file is not a list");
        var lines = new List<string>();
        foreach (var c in root.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object) throw new FormatException("Each chapter needs a start and a title");
            var start = Seconds(Get(c, "start", "time", "startTime"));
            var title = Get(c, "title", "name") is { ValueKind: JsonValueKind.String } t ? t.GetString()?.Trim() : null;
            if (start is null || string.IsNullOrEmpty(title)) throw new FormatException("Each chapter needs a start and a title");
            lines.Add($"{Stamp(start.Value)} {title}");
        }
        if (lines.Count == 0) throw new FormatException("The chapters file is empty");
        return string.Join("\n", lines);
    }

    private static JsonElement? Get(JsonElement o, params string[] names)
    {
        foreach (var p in o.EnumerateObject())
        {
            if (names.Any(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase))) return p.Value;
        }
        return null;
    }

    private static double? Seconds(JsonElement? v)
    {
        if (v is null) return null;
        if (v.Value.ValueKind == JsonValueKind.Number) return v.Value.GetDouble() >= 0 ? v.Value.GetDouble() : null;
        if (v.Value.ValueKind != JsonValueKind.String) return null;
        var parts = v.Value.GetString()!.Trim().Split(':');
        if (parts.Length is < 1 or > 3) return null;
        double total = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0) return null;
            total = total * 60 + n;
        }
        return total;
    }

    private static string Stamp(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{t.Minutes}:{t.Seconds:00}");
    }
}

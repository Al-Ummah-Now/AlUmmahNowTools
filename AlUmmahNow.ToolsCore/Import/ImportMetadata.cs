using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>
/// A video's metadata for the import: from its <c>name.json</c>, its row in the folder's CSV, or only its file name.
/// </summary>
public sealed record ImportMetadata
{
    public static readonly IReadOnlyList<string> Visibilities = new[] { "public", "unlisted", "private" };

    public string? TitleEn { get; init; }
    public string? TitleAr { get; init; }
    public string? DescriptionEn { get; init; }
    public string? DescriptionAr { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Category { get; init; }
    /// <summary>public, unlisted or private; null keeps the server's default</summary>
    public string? Visibility { get; init; }
    /// <summary>ISO 8601 (UTC)</summary>
    public string? PublishAt { get; init; }
    public string? PlaylistId { get; init; }
    /// <summary>The spoken language, also the language of a caption file named without one (<c>name.srt</c>)</summary>
    public string? Language { get; init; }
    /// <summary>The file name without its extension, the title when no other is given</summary>
    public string FallbackTitle { get; init; } = "";

    [JsonIgnore] public string Title => FirstText(TitleEn, TitleAr) ?? FallbackTitle;
    [JsonIgnore] public string? Description => FirstText(DescriptionEn, DescriptionAr);

    /// <summary>The values of <paramref name="over"/> where it has one, else these.</summary>
    public ImportMetadata Merge(ImportMetadata? over) => over is null ? this : this with
    {
        TitleEn = over.TitleEn ?? TitleEn,
        TitleAr = over.TitleAr ?? TitleAr,
        DescriptionEn = over.DescriptionEn ?? DescriptionEn,
        DescriptionAr = over.DescriptionAr ?? DescriptionAr,
        Tags = over.Tags.Count > 0 ? over.Tags : Tags,
        Category = over.Category ?? Category,
        Visibility = over.Visibility ?? Visibility,
        PublishAt = over.PublishAt ?? PublishAt,
        PlaylistId = over.PlaylistId ?? PlaylistId,
        Language = over.Language ?? Language,
    };

    private static string? FirstText(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    internal static string? Text(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    internal static IReadOnlyList<string> SplitTags(string? v) => string.IsNullOrWhiteSpace(v)
        ? Array.Empty<string>()
        : v.Split(new[] { ',', '،', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static string? Visible(string? v)
    {
        var t = Text(v)?.ToLowerInvariant();
        return t is not null && Visibilities.Contains(t) ? t : null;
    }

    internal static string? Date(string? v)
    {
        var t = Text(v);
        if (t is null) return null;
        return DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
            : null;
    }

    internal static string? LanguageCode(string? v)
    {
        var t = Text(v)?.ToLowerInvariant();
        return t is not null && FolderScanner.LanguagePattern.IsMatch(t) ? t : null;
    }
}

/// <summary>Reads <c>name.json</c>: flat keys (<c>title</c>, <c>titleAr</c>, …) or per-language objects (<c>title: { en, ar }</c>).</summary>
public static class MetadataJson
{
    /// <exception cref="FormatException">The file is not a JSON object.</exception>
    public static ImportMetadata Parse(string json)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }).RootElement;
        }
        catch (JsonException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("The metadata file is not a JSON object");

        string? Str(params string[] names)
        {
            foreach (var n in names)
            {
                foreach (var p in root.EnumerateObject())
                {
                    if (!string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.Value.ValueKind == JsonValueKind.String) return ImportMetadata.Text(p.Value.GetString());
                    if (p.Value.ValueKind == JsonValueKind.Number) return p.Value.GetRawText();
                }
            }
            return null;
        }
        string? PerLanguage(string name, string language)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) || p.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var l in p.Value.EnumerateObject())
                {
                    if (string.Equals(l.Name, language, StringComparison.OrdinalIgnoreCase) && l.Value.ValueKind == JsonValueKind.String) return ImportMetadata.Text(l.Value.GetString());
                }
            }
            return null;
        }
        IReadOnlyList<string> Tags()
        {
            foreach (var p in root.EnumerateObject())
            {
                if (!string.Equals(p.Name, "tags", StringComparison.OrdinalIgnoreCase)) continue;
                if (p.Value.ValueKind == JsonValueKind.String) return ImportMetadata.SplitTags(p.Value.GetString());
                if (p.Value.ValueKind == JsonValueKind.Array)
                {
                    return p.Value.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }
            }
            return Array.Empty<string>();
        }

        return new ImportMetadata
        {
            TitleEn = PerLanguage("title", "en") ?? Str("titleEn", "title_en", "title"),
            TitleAr = PerLanguage("title", "ar") ?? Str("titleAr", "title_ar"),
            DescriptionEn = PerLanguage("description", "en") ?? Str("descriptionEn", "description_en", "description"),
            DescriptionAr = PerLanguage("description", "ar") ?? Str("descriptionAr", "description_ar"),
            Tags = Tags(),
            Category = Str("category"),
            Visibility = ImportMetadata.Visible(Str("visibility")),
            PublishAt = ImportMetadata.Date(Str("publishAt", "publish_at", "publishDate", "publish_date")),
            PlaylistId = Str("playlistId", "playlist_id", "playlist"),
            Language = ImportMetadata.LanguageCode(Str("language", "lang")),
        };
    }
}

/// <summary>
/// One CSV for a folder: a header row, then a row per video, matched on its <c>file</c> column (the file name, with
/// or without its extension). Columns, in any order and case: file, title (or title_en), title_ar, description (or
/// description_en), description_ar, tags, category, visibility, publish_at, playlist, language.
/// </summary>
public sealed class MetadataCsv
{
    private readonly Dictionary<string, ImportMetadata> _rows = new(StringComparer.OrdinalIgnoreCase);

    private MetadataCsv() { }

    public int Count => _rows.Count;

    /// <summary>The row of a video file, by its name or its name without the extension.</summary>
    public ImportMetadata? For(string fileName) =>
        _rows.TryGetValue(fileName, out var m) || _rows.TryGetValue(Path.GetFileNameWithoutExtension(fileName), out m) ? m : null;

    /// <exception cref="FormatException">No header with a <c>file</c> column.</exception>
    public static MetadataCsv Parse(string text)
    {
        var rows = ReadRows(text);
        if (rows.Count == 0) throw new FormatException("The CSV file is empty");
        var header = rows[0].Select(h => h.Trim().TrimStart('﻿').ToLowerInvariant().Replace(' ', '_')).ToArray();
        int Col(params string[] names) => Array.FindIndex(header, h => names.Contains(h));
        var file = Col("file", "filename", "file_name");
        if (file < 0) throw new FormatException("The CSV file has no \"file\" column");
        var titleEn = Col("title", "title_en");
        var titleAr = Col("title_ar");
        var descEn = Col("description", "description_en");
        var descAr = Col("description_ar");
        var tags = Col("tags");
        var category = Col("category");
        var visibility = Col("visibility");
        var publish = Col("publish_at", "publishat", "publish_date", "publish");
        var playlist = Col("playlist", "playlist_id", "playlistid");
        var language = Col("language", "lang");

        var csv = new MetadataCsv();
        foreach (var row in rows.Skip(1))
        {
            string? Cell(int i) => i >= 0 && i < row.Count ? ImportMetadata.Text(row[i]) : null;
            var name = Cell(file);
            if (name is null) continue;
            csv._rows[name] = new ImportMetadata
            {
                TitleEn = Cell(titleEn),
                TitleAr = Cell(titleAr),
                DescriptionEn = Cell(descEn),
                DescriptionAr = Cell(descAr),
                Tags = ImportMetadata.SplitTags(Cell(tags)),
                Category = Cell(category),
                Visibility = ImportMetadata.Visible(Cell(visibility)),
                PublishAt = ImportMetadata.Date(Cell(publish)),
                PlaylistId = Cell(playlist),
                Language = ImportMetadata.LanguageCode(Cell(language)),
            };
        }
        return csv;
    }

    /// <summary>RFC 4180: commas, quoted fields with doubled quotes, line breaks inside quotes.</summary>
    internal static List<List<string>> ReadRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
                continue;
            }
            switch (c)
            {
                case '"' when cell.Length == 0: quoted = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(cell.ToString()); cell.Clear();
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                    row = new List<string>();
                    break;
                default: cell.Append(c); break;
            }
        }
        row.Add(cell.ToString());
        if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
        return rows;
    }
}

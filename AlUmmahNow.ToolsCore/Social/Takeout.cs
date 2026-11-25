using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using AlUmmahNow.ToolsCore.Import;

namespace AlUmmahNow.ToolsCore.Social;

/// <summary>
/// YouTube Takeout files for a YouTube source (wiki TOOLS-ADMIN TA-9/TA-13). YouTube's terms forbid downloading, so a
/// YouTube source's records wait for their file, which comes from the owner's Google Takeout export. The app lists the
/// export's videos (name, size, and what the export's <c>videos.csv</c> says: video id, title, duration); api-videos'
/// queued job <c>import.takeout-match</c> matches them to the records; the admin settles the ambiguous ones; the app
/// uploads each matched file (the record's resumable upload) and the same job posts the record's video.
/// </summary>
public static class TakeoutStates
{
    public const string New = "new";
    public const string Matched = "matched";
    public const string Unmatched = "unmatched";
    public const string Ambiguous = "ambiguous";
    public const string Uploading = "uploading";
    public const string Uploaded = "uploaded";
    public const string Attached = "attached";
    public const string Failed = "failed";

    /// <summary>The order the page lists them in: what needs the admin first.</summary>
    public static readonly IReadOnlyList<string> Order = new[] { Ambiguous, Matched, Uploading, Uploaded, Attached, Unmatched, Failed, New };
}

/// <summary>A file of the export as the app read it (shared-types <c>TakeoutFileInput</c>).</summary>
public sealed record TakeoutFileInput(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("videoId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VideoId,
    [property: JsonPropertyName("title"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
    [property: JsonPropertyName("durationMs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DurationMs,
    [property: JsonPropertyName("mimeType"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MimeType)
{
    /// <summary>Where the file is on this computer (never sent).</summary>
    [JsonIgnore] public string? Path { get; init; }
}

/// <summary>A record a file could be: the source item, with what the admin needs to choose.</summary>
public sealed record TakeoutCandidate(
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("originalUrl")] string? OriginalUrl,
    [property: JsonPropertyName("originalAt")] DateTimeOffset? OriginalAt);

/// <summary>A listed file and what became of it (shared-types <c>TakeoutFileView</c>).</summary>
public sealed record TakeoutFile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sourceId")] string SourceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("videoId")] string? VideoId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("durationMs")] long? DurationMs,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("matchedBy")] string? MatchedBy,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("itemId")] string? ItemId,
    [property: JsonPropertyName("item")] TakeoutCandidate? Item,
    [property: JsonPropertyName("candidates")] IReadOnlyList<TakeoutCandidate>? Candidates,
    [property: JsonPropertyName("uploadVideoId")] string? UploadVideoId)
{
    /// <summary>Matched, or its upload was opened but not finished: the app can send it.</summary>
    public bool CanUpload => State is TakeoutStates.Matched or TakeoutStates.Uploading;
}

/// <summary>GET …/takeout: the listed files, how many are in each state and the latest match job.</summary>
public sealed record TakeoutReport(
    [property: JsonPropertyName("files")] IReadOnlyList<TakeoutFile> Files,
    [property: JsonPropertyName("counts")] IReadOnlyDictionary<string, int>? Counts,
    [property: JsonPropertyName("jobId")] string? JobId)
{
    public int Count(string state) => Counts is not null && Counts.TryGetValue(state, out var n) ? n : 0;
}

/// <summary>What listing the files answered: how many were new, and the match job (the one already waiting, if any).</summary>
public sealed record TakeoutListed(
    [property: JsonPropertyName("listed")] int Listed,
    [property: JsonPropertyName("added")] int Added,
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("status")] string Status);

/// <summary>A matched file's upload: the record's video and its TUS session (the admin's).</summary>
public sealed record TakeoutUpload(TakeoutFile File, UploadSession Upload);

/// <summary>
/// Reads a Takeout export folder: its video files and the export's metadata (<c>video metadata/videos.csv</c>, or any CSV
/// with a <c>Video ID</c> column). A file gets its row's video id, title and duration when exactly one row's title is the
/// file's name (Takeout names each file after its title); otherwise the server matches it by its name.
/// </summary>
public static class TakeoutExport
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".3gp", ".flv", ".wmv", ".mpg", ".mpeg" };

    /// <summary>Files listed per request (api-videos <c>TAKEOUT_MAX_FILES_PER_REQUEST</c>).</summary>
    public const int MaxPerRequest = 500;

    public sealed record Row(string VideoId, string? Title, long? DurationMs);

    /// <summary>Every video file under <paramref name="folder"/> and the export's rows, as the server takes them.</summary>
    public static IReadOnlyList<TakeoutFileInput> Read(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint })
            .Where(p => VideoExtensions.Contains(System.IO.Path.GetExtension(p)))
            .Select(p => (Path: p, Info: new FileInfo(p)))
            .Where(f => f.Info.Length > 0)
            .ToList();
        var rows = new List<Row>();
        foreach (var csv in Directory.EnumerateFiles(folder, "*.csv", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            rows.AddRange(ParseCsv(File.ReadAllText(csv, Encoding.UTF8)));
        }
        return Build(files.Select(f => (f.Path, f.Info.Name, f.Info.Length)), rows);
    }

    /// <summary>The request rows for the files, with each file's metadata row when one row fits it alone.</summary>
    public static IReadOnlyList<TakeoutFileInput> Build(IEnumerable<(string Path, string Name, long Size)> files, IReadOnlyList<Row> rows)
    {
        var byTitle = rows.Where(r => !string.IsNullOrWhiteSpace(r.Title)).GroupBy(r => Normalize(r.Title)).ToDictionary(g => g.Key, g => g.ToList());
        var seen = new HashSet<(string, long)>();
        var result = new List<TakeoutFileInput>();
        foreach (var (path, name, size) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            // The server keys a file on its name and size: the same file in two folders is one file
            if (!seen.Add((name, size))) continue;
            var title = System.IO.Path.GetFileNameWithoutExtension(name);
            var row = byTitle.TryGetValue(Normalize(title), out var found) && found.Count == 1 ? found[0] : null;
            result.Add(new TakeoutFileInput(name, size, row?.VideoId, row?.Title ?? title, row?.DurationMs, MimeOf(name)) { Path = path });
        }
        return result;
    }

    /// <summary>A title as compared: letters and digits only, any case (the server's <c>normalizeTitle</c>).</summary>
    public static string Normalize(string? value)
    {
        var text = (value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (space && sb.Length > 0) sb.Append(' ');
                sb.Append(ch);
                space = false;
            }
            else space = true;
        }
        return sb.ToString();
    }

    /// <summary>
    /// The export's <c>videos.csv</c>: a header row naming <c>Video ID</c>, a title column (<c>Video Title (Original)</c> or
    /// <c>Video Title</c>) and <c>Approx Duration (ms)</c>. A CSV without a <c>Video ID</c> column gives no rows.
    /// </summary>
    public static IReadOnlyList<Row> ParseCsv(string csv)
    {
        var lines = SplitCsv(csv).ToList();
        if (lines.Count == 0) return Array.Empty<Row>();
        var header = lines[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
        var id = header.FindIndex(h => h.Equals("Video ID", StringComparison.OrdinalIgnoreCase));
        if (id < 0) return Array.Empty<Row>();
        var title = header.FindIndex(h => h.Equals("Video Title (Original)", StringComparison.OrdinalIgnoreCase));
        if (title < 0) title = header.FindIndex(h => h.StartsWith("Video Title", StringComparison.OrdinalIgnoreCase));
        var duration = header.FindIndex(h => h.StartsWith("Approx Duration", StringComparison.OrdinalIgnoreCase));
        var rows = new List<Row>();
        foreach (var cells in lines.Skip(1))
        {
            string? Cell(int i) => i >= 0 && i < cells.Count ? cells[i].Trim() : null;
            var videoId = Cell(id);
            if (string.IsNullOrEmpty(videoId)) continue;
            long? ms = long.TryParse(Cell(duration), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null;
            rows.Add(new Row(videoId, Cell(title) is { Length: > 0 } t ? t : null, ms));
        }
        return rows;
    }

    private static IEnumerable<List<string>> SplitCsv(string text)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    if (row.Count > 1 || row[0].Length > 0) yield return row;
                    row = new List<string>();
                    break;
                default: cell.Append(ch); break;
            }
        }
        row.Add(cell.ToString());
        if (row.Count > 1 || row[0].Length > 0) yield return row;
    }

    private static string MimeOf(string name) => System.IO.Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        ".avi" => "video/x-msvideo",
        ".3gp" => "video/3gpp",
        ".flv" => "video/x-flv",
        ".wmv" => "video/x-ms-wmv",
        ".mpg" or ".mpeg" => "video/mpeg",
        ".m4v" => "video/x-m4v",
        _ => "video/mp4",
    };
}

/// <summary>
/// Sends a matched Takeout file: opens the record's upload on admin-api (opening it again resumes the same video), sends
/// the file over TUS from where the server has it, then says it is uploaded so the server's job posts the video.
/// </summary>
public sealed class TakeoutUploader
{
    private const long DefaultChunk = 8L * 1024 * 1024;
    private readonly ImportApiClient _api;
    private readonly TusClient _tus;
    private readonly Uri _tusEndpoint;
    private readonly Func<CancellationToken, Task<string?>> _token;

    public TakeoutUploader(ImportApiClient api, TusClient tus, Uri videosApi, Func<CancellationToken, Task<string?>> token)
    {
        _api = api;
        _tus = tus;
        _tusEndpoint = new Uri(videosApi, "/api/videos/tus");
        _token = token;
    }

    public async Task<TakeoutFile> UploadAsync(string sourceId, TakeoutFile file, string path, IProgress<double>? progress, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != file.Size) throw new TakeoutUploadException("file_changed", "The file is no longer the one that was listed");
        var reopened = false;
        while (true)
        {
            try
            {
                await SendAsync(sourceId, file, path, progress, fresh: reopened, ct).ConfigureAwait(false);
                break;
            }
            catch (TusGoneException) when (!reopened)
            {
                // The upload expired on the server (404/410): open the record's upload again and start a new TUS upload
                reopened = true;
            }
            catch (TusGoneException gone)
            {
                throw new TakeoutUploadException("upload_gone", gone.Message, gone);
            }
        }
        return await _api.MarkTakeoutUploadedAsync(sourceId, file.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Opens the record's upload and sends the file from where the server has it; <paramref name="fresh"/> starts a new TUS upload.</summary>
    private async Task SendAsync(string sourceId, TakeoutFile file, string path, IProgress<double>? progress, bool fresh, CancellationToken ct)
    {
        var opened = await _api.OpenTakeoutUploadAsync(sourceId, file.Id, ct).ConfigureAwait(false);
        var upload = opened.Upload;
        var token = await _token(ct).ConfigureAwait(false) ?? throw new TakeoutUploadException("signed_out", "Not signed in");
        Uri url;
        if (!fresh && !string.IsNullOrEmpty(upload.TusUploadId))
        {
            url = new Uri(_tusEndpoint, _tusEndpoint.AbsolutePath.TrimEnd('/') + "/" + upload.TusUploadId);
        }
        else
        {
            var metadata = new Dictionary<string, string>
            {
                ["filename"] = file.Name,
                ["filetype"] = "video/mp4",
                ["title"] = file.Item?.Title ?? file.Title ?? file.Name,
                ["tusSessionId"] = upload.TusSessionId,
                ["videoId"] = upload.VideoId,
            };
            url = await _tus.CreateAsync(_tusEndpoint, token, file.Size, metadata, ct).ConfigureAwait(false);
        }
        var chunk = upload.ChunkSize > 0 ? upload.ChunkSize : DefaultChunk;
        var offset = await _tus.OffsetAsync(url, token, ct).ConfigureAwait(false);
        await using var stream = Import.Sources.SourceAccess.Default.Open(path, file.Size, null, null);
        while (offset < file.Size)
        {
            ct.ThrowIfCancellationRequested();
            token = await _token(ct).ConfigureAwait(false) ?? token;
            try
            {
                offset = await _tus.PatchAsync(url, token, stream, offset, (int)Math.Min(chunk, file.Size - offset), ct).ConfigureAwait(false);
            }
            catch (TusException conflict) when (conflict.Status == 409)
            {
                offset = await _tus.OffsetAsync(url, token, ct).ConfigureAwait(false);
            }
            progress?.Report(file.Size == 0 ? 1 : (double)offset / file.Size);
        }
    }
}

/// <summary>
/// A Takeout file the app could not send, with a code the app says in the admin's words: <c>file_changed</c> (not the
/// file that was listed), <c>signed_out</c>, <c>upload_gone</c> (the upload expired twice).
/// </summary>
public sealed class TakeoutUploadException : InvalidOperationException
{
    public TakeoutUploadException(string code, string message, Exception? inner = null) : base(message, inner) => Code = code;

    public string Code { get; }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>A channel the admin can import into (admin-api <c>GET /api/content/channels</c>).</summary>
public sealed record ImportChannel(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string? Handle,
    [property: JsonPropertyName("ownerEmail")] string? OwnerEmail);

public sealed record ImportBatch(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("channelId")] string ChannelId,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>admin-api's record of one file (<c>import_items</c>): unique per channel by the file's SHA-256.</summary>
public sealed record ImportItemRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("batchId")] string BatchId,
    [property: JsonPropertyName("videoId")] string? VideoId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>The TUS session api-videos opened for the file: the admin's, used with their Admin Tools token.</summary>
public sealed record UploadSession(
    [property: JsonPropertyName("videoId")] string VideoId,
    [property: JsonPropertyName("tusSessionId")] string TusSessionId,
    [property: JsonPropertyName("tusUploadId")] string? TusUploadId,
    [property: JsonPropertyName("chunkSize")] long ChunkSize,
    [property: JsonPropertyName("resumable")] bool Resumable,
    [property: JsonPropertyName("reused")] bool Reused);

public sealed record OpenedItem(ImportItemRecord Item, UploadSession Upload);

/// <summary>The fingerprint api-videos keys an unfinished upload on (web lib/upload/fingerprint.ts).</summary>
public sealed record UploadFingerprint(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("lastModified")] long LastModified,
    [property: JsonPropertyName("hash")] string Hash);

/// <summary>A caption track to attach: the file's text as it is (api-videos converts SRT and SBV to WebVTT).</summary>
public sealed record CaptionSidecar(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("content")] string Content);

public sealed record ThumbnailSidecar(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("mimeType")] string MimeType);

/// <summary>What <c>POST /api/admin/import/items/:id/sidecars</c> takes (TA-6).</summary>
public sealed record SidecarPayload(
    [property: JsonPropertyName("captions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CaptionSidecar>? Captions,
    [property: JsonPropertyName("chapters"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Chapters,
    [property: JsonPropertyName("thumbnail"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ThumbnailSidecar? Thumbnail,
    [property: JsonPropertyName("playlistId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PlaylistId,
    [property: JsonPropertyName("publishAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PublishAt)
{
    public bool IsEmpty => (Captions is null || Captions.Count == 0) && Chapters is null && Thumbnail is null && PlaylistId is null && PublishAt is null;
}

/// <summary>One rung of the server's ladder (shared-types ENCODE_LADDER): named by the short side.</summary>
public sealed record EncodeRungSetting(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("short")] int Short,
    [property: JsonPropertyName("maxrate")] int Maxrate,
    [property: JsonPropertyName("audio")] int Audio);

public sealed record EncodeFpsCap(
    [property: JsonPropertyName("aboveFps")] double AboveFps,
    [property: JsonPropertyName("fps")] double Fps);

public sealed record EncodeProfileHigh(
    [property: JsonPropertyName("minHeight")] int MinHeight,
    [property: JsonPropertyName("minWidth")] int MinWidth);

/// <summary><c>high</c> from these sizes, else <c>main</c>.</summary>
public sealed record EncodeProfileRule(
    [property: JsonPropertyName("high")] EncodeProfileHigh? High);

public sealed record EncodeVideoSettings(
    [property: JsonPropertyName("codec")] string Codec,
    [property: JsonPropertyName("pixelFormat")] string PixelFormat,
    [property: JsonPropertyName("crf")] int Crf,
    [property: JsonPropertyName("bufsizeFactor")] double BufsizeFactor,
    [property: JsonPropertyName("fpsCap")] EncodeFpsCap FpsCap,
    [property: JsonPropertyName("profile")] EncodeProfileRule? Profile = null);

/// <summary>The server's HLS encode settings (<c>GET /api/admin/import/encoding</c>), the same the media container uses.</summary>
public sealed record EncodeLadderSettings(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("rungs")] IReadOnlyList<EncodeRungSetting> Rungs,
    [property: JsonPropertyName("shortSideTolerance")] int ShortSideTolerance,
    [property: JsonPropertyName("segmentSeconds")] int SegmentSeconds,
    [property: JsonPropertyName("gopSeconds")] int GopSeconds,
    [property: JsonPropertyName("video")] EncodeVideoSettings Video);

public sealed record PrebuiltLimits(
    [property: JsonPropertyName("maxFiles")] int MaxFiles,
    [property: JsonPropertyName("maxFileBytes")] long MaxFileBytes,
    [property: JsonPropertyName("maxTotalBytes")] long MaxTotalBytes);

public sealed record EncodingSettings(
    [property: JsonPropertyName("ladder")] EncodeLadderSettings Ladder,
    [property: JsonPropertyName("prebuiltLimits")] PrebuiltLimits PrebuiltLimits,
    [property: JsonPropertyName("takesRenditions")] bool? TakesRenditions);

/// <summary>A rendition file as <c>POST /renditions</c> declares it: <c>&lt;rung&gt;/&lt;file&gt;</c> and its size.</summary>
public sealed record RenditionFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("bytes")] long Bytes);

public sealed record RenditionStaging(
    [property: JsonPropertyName("stagingId")] string StagingId,
    [property: JsonPropertyName("status")] string Status);

/// <summary>What the server's transcode did with the renditions (shared-types TranscodeResult.prebuilt).</summary>
public sealed record PrebuiltOutcome(
    [property: JsonPropertyName("used")] bool Used,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("rung")] string? Rung);

public sealed record DeliveryTranscode(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("progress")] double? Progress,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("prebuilt")] PrebuiltOutcome? Prebuilt);

public sealed record DeliveryStaging(
    [property: JsonPropertyName("stagingId")] string StagingId,
    [property: JsonPropertyName("status")] string Status);

/// <summary><c>GET /api/admin/import/items/:id/delivery</c>.</summary>
public sealed record DeliveryState(
    [property: JsonPropertyName("takesRenditions")] bool TakesRenditions,
    [property: JsonPropertyName("staging")] DeliveryStaging? Staging,
    [property: JsonPropertyName("r2")] DeliveryTranscode? R2);

/// <summary>An error the app reports to the Errors page (<c>POST /api/admin/import/errors</c>, app <c>tools</c>).</summary>
public sealed record ErrorReport(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonPropertyName("stack"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Stack = null,
    [property: JsonPropertyName("route"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Route = null,
    [property: JsonPropertyName("status"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Status = null,
    [property: JsonPropertyName("release"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Release = null,
    [property: JsonPropertyName("locale"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Locale = null,
    [property: JsonPropertyName("occurredAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OccurredAt = null,
    [property: JsonPropertyName("extra"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, object?>? Extra = null)
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>A new reference in the Errors page's format: ERR-XXXX-XXXX, Crockford base32.</summary>
    public static string NewRef()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var chars = new char[8];
        for (var i = 0; i < 8; i++) chars[i] = Alphabet[bytes[i] & 31];
        return $"ERR-{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }
}

/// <summary>A refused or failed admin-api call: the status (0 when it was never answered), code and message.</summary>
public sealed class ImportApiException : Exception
{
    public ImportApiException(int status, string code, string message, TimeSpan? retryAfter = null, Exception? inner = null, JsonNode? body = null)
        : base(message, inner)
    {
        Status = status;
        Code = code;
        RetryAfter = retryAfter;
        Body = body;
    }

    /// <summary>The whole answer, for details next to the code (files_missing lists the files still to send)</summary>
    public JsonNode? Body { get; }

    public int Status { get; }
    public string Code { get; }
    public TimeSpan? RetryAfter { get; }

    /// <summary>Worth trying again later: not answered, timed out, rate limited, busy or a server error.</summary>
    public bool IsRetryable => Status is 0 or 408 or 429 or >= 500 || Code == "busy";
}

/// <summary>The file is already in the channel (<c>409 already_imported</c>): nothing to upload.</summary>
public sealed class AlreadyImportedException : Exception
{
    public AlreadyImportedException(ImportItemRecord item) : base("This file is already in this channel") => Item = item;
    public ImportItemRecord Item { get; }
}

/// <summary>Everything the importer asks admin-api, with the signed-in account's token on each call.</summary>
public class ImportApiClient
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly Uri _base;
    private readonly Func<CancellationToken, Task<string?>> _token;

    public ImportApiClient(Uri adminApi, Func<CancellationToken, Task<string?>> token, HttpMessageHandler? handler = null)
    {
        _base = adminApi;
        _token = token;
        _http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AlUmmahNowTools/1.0");
    }

    /// <summary>Channels matching <paramref name="query"/> (name or handle), 20 per page.</summary>
    public virtual async Task<IReadOnlyList<ImportChannel>> ListChannelsAsync(string? query, int page = 1, CancellationToken ct = default)
    {
        var q = string.IsNullOrWhiteSpace(query) ? "" : "&q=" + Uri.EscapeDataString(query.Trim());
        var result = await SendAsync(HttpMethod.Get, $"/api/content/channels?page={page}&pageSize=20&sort=name{q}", null, ct).ConfigureAwait(false);
        return result?["items"]?.Deserialize<List<ImportChannel>>(Json) ?? new List<ImportChannel>();
    }

    public virtual async Task<ImportChannel> CreateChannelAsync(string name, string? handle, string? description, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, "/api/admin/import/channels", new { name, handle, description }, ct).ConfigureAwait(false);
        return result?["channel"]?.Deserialize<ImportChannel>(Json) ?? throw Unreadable();
    }

    public virtual async Task<ImportBatch> CreateBatchAsync(string channelId, string? name, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, "/api/admin/import/batches", new { channelId, name }, ct).ConfigureAwait(false);
        return result?["batch"]?.Deserialize<ImportBatch>(Json) ?? throw Unreadable();
    }

    /// <summary>
    /// Opens the file's upload: a new video, or the unfinished one of the same file. Throws
    /// <see cref="AlreadyImportedException"/> when the channel already has this file.
    /// </summary>
    public virtual async Task<OpenedItem> OpenItemAsync(string batchId, ItemUploadRequest request, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, $"/api/admin/import/batches/{Uri.EscapeDataString(batchId)}/items", request.ToBody(), ct).ConfigureAwait(false);
        var item = result?["item"]?.Deserialize<ImportItemRecord>(Json);
        var upload = result?["upload"]?.Deserialize<UploadSession>(Json);
        if (item is null || upload is null || string.IsNullOrEmpty(upload.TusSessionId)) throw Unreadable();
        return new OpenedItem(item, upload);
    }

    public virtual Task MarkUploadedAsync(string itemId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/admin/import/items/{Uri.EscapeDataString(itemId)}/uploaded", new { }, ct);

    /// <summary>Queues the attach job for the item's video; answers its job id.</summary>
    public virtual async Task<string?> AttachSidecarsAsync(string itemId, SidecarPayload sidecars, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, $"/api/admin/import/items/{Uri.EscapeDataString(itemId)}/sidecars", sidecars, ct).ConfigureAwait(false);
        return result?["jobId"]?.GetValue<string>();
    }

    public virtual Task MarkFailedAsync(string itemId, string error, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/admin/import/items/{Uri.EscapeDataString(itemId)}/failed", new { error }, ct);

    /// <summary>The server's ladder and limits; with a channel, whether R2 transcodes its uploads (renditions are used only then).</summary>
    public virtual async Task<EncodingSettings> GetEncodingAsync(string? channelId, CancellationToken ct = default)
    {
        var q = string.IsNullOrEmpty(channelId) ? "" : "?channelId=" + Uri.EscapeDataString(channelId);
        var result = await SendAsync(HttpMethod.Get, "/api/admin/import/encoding" + q, null, ct).ConfigureAwait(false);
        return result?.Deserialize<EncodingSettings>(Json) ?? throw Unreadable();
    }

    /// <summary>Declares the rungs and files for the item's video; answers the staging.</summary>
    public virtual async Task<RenditionStaging> StageRenditionsAsync(string itemId, IReadOnlyList<string> rungs, IReadOnlyList<RenditionFile> files, string encoder, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, $"/api/admin/import/items/{Uri.EscapeDataString(itemId)}/renditions", new { rungs, files, encoder }, ct).ConfigureAwait(false);
        return result?.Deserialize<RenditionStaging>(Json) ?? throw Unreadable();
    }

    /// <summary>One file of the staging, exactly its declared size.</summary>
    public virtual Task PutRenditionFileAsync(string itemId, string stagingId, string path, Stream content, long length, CancellationToken ct = default)
    {
        var slash = path.IndexOf('/');
        var url = $"/api/admin/import/items/{Uri.EscapeDataString(itemId)}/renditions/{Uri.EscapeDataString(stagingId)}/files/{Uri.EscapeDataString(path[..slash])}/{Uri.EscapeDataString(path[(slash + 1)..])}";
        var body = new StreamContent(content);
        body.Headers.ContentLength = length;
        body.Headers.ContentType = new MediaTypeHeaderValue(path.EndsWith(".m3u8", StringComparison.Ordinal) ? "application/vnd.apple.mpegurl" : "video/mp4");
        return SendAsync(HttpMethod.Put, url, body, ct);
    }

    /// <summary>
    /// Every file is there: the renditions wait for the original's transcode. Answers the files the server is still
    /// missing (at most 50 at a time), empty when it took the staging.
    /// </summary>
    public virtual async Task<IReadOnlyList<string>> CompleteRenditionsAsync(string itemId, string stagingId, CancellationToken ct = default)
    {
        try
        {
            await SendAsync(HttpMethod.Post, $"/api/admin/import/items/{Uri.EscapeDataString(itemId)}/renditions/{Uri.EscapeDataString(stagingId)}/complete", new { }, ct).ConfigureAwait(false);
            return Array.Empty<string>();
        }
        catch (ImportApiException missing) when (missing.Code == "files_missing" && missing.Body?["missing"] is JsonArray list)
        {
            return list.Select(n => n?.GetValue<string>()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        }
    }

    public virtual Task CancelRenditionsAsync(string itemId, string stagingId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/admin/import/items/{Uri.EscapeDataString(itemId)}/renditions/{Uri.EscapeDataString(stagingId)}", null, ct);

    /// <summary>Whether the server takes renditions for the item's video, and what its transcode did with them.</summary>
    public virtual async Task<DeliveryState> GetDeliveryAsync(string itemId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Get, $"/api/admin/import/items/{Uri.EscapeDataString(itemId)}/delivery", null, ct).ConfigureAwait(false);
        return result?.Deserialize<DeliveryState>(Json) ?? throw Unreadable();
    }

    public virtual Task ReportErrorsAsync(IReadOnlyList<ErrorReport> events, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/admin/import/errors", new { events }, ct);

    private static ImportApiException Unreadable() => new(200, "unreadable", "admin-api answered something the app cannot read");

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = await _token(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token)) throw new ImportApiException(401, "signed_out", "Sign in again");
        using var request = new HttpRequestMessage(method, new Uri(_base, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is HttpContent raw) request.Content = raw;
        else if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ImportApiException(0, "unreachable", "admin-api could not be reached", inner: ex);
        }

        using (response)
        {
            JsonNode? json = null;
            try
            {
                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text)) json = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                json = null;
            }

            if (response.IsSuccessStatusCode) return json?["result"];
            var code = json?["code"]?.GetValue<string>() ?? "http_" + (int)response.StatusCode;
            var message = json?["error"]?.GetValue<string>() ?? response.ReasonPhrase ?? "Request failed";
            if (response.StatusCode == HttpStatusCode.Conflict && code == "already_imported")
            {
                var item = json?["item"]?.Deserialize<ImportItemRecord>(Json);
                if (item is not null) throw new AlreadyImportedException(item);
            }
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new ImportApiException((int)response.StatusCode, code, message, retryAfter, body: json);
        }
    }
}

/// <summary>A file to open on admin-api, with the initiate's metadata (api-videos InitiateBodySchema).</summary>
public sealed record ItemUploadRequest(
    string Sha256,
    string FileName,
    long FileSize,
    string MimeType,
    UploadFingerprint Fingerprint,
    ImportMetadata Metadata)
{
    internal Dictionary<string, object?> ToBody()
    {
        var m = Metadata;
        var body = new Dictionary<string, object?>
        {
            ["sha256"] = Sha256,
            ["fileName"] = FileName,
            ["fileSize"] = FileSize,
            ["mimeType"] = MimeType,
            ["fingerprint"] = Fingerprint,
            ["title"] = m.Title,
        };
        if (!string.IsNullOrWhiteSpace(m.Description)) body["description"] = m.Description;
        var titles = Localized(m.TitleEn, m.TitleAr);
        if (titles is not null) body["titleLocalized"] = titles;
        var descriptions = Localized(m.DescriptionEn, m.DescriptionAr);
        if (descriptions is not null) body["descriptionLocalized"] = descriptions;
        if (m.Tags.Count > 0) body["tags"] = string.Join(",", m.Tags);
        if (!string.IsNullOrWhiteSpace(m.Category)) body["category"] = m.Category;
        if (m.Visibility is not null) body["visibility"] = m.Visibility;
        return body;
    }

    private static Dictionary<string, string>? Localized(string? en, string? ar)
    {
        var map = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(en)) map["en"] = en.Trim();
        if (!string.IsNullOrWhiteSpace(ar)) map["ar"] = ar.Trim();
        return map.Count == 0 ? null : map;
    }
}

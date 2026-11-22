using System.Text.Json;
using AlUmmahNow.ToolsCore.Social;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>
/// Social import sources (wiki TOOLS-ADMIN TA-13, on the TA-9 API): <c>/api/admin/import/sources</c>, super admins only
/// (<c>import_sources:manage</c>, 403 for anyone else); every change is audited by admin-api. Sync jobs are read and
/// retried through the admin jobs routes (<c>/api/content/jobs</c>, TA-8), which also allow a sync's Retry to super
/// admins only.
/// </summary>
public partial class ImportApiClient
{
    /// <summary>The sources of one channel, or of every channel.</summary>
    public virtual async Task<IReadOnlyList<ImportSource>> ListSourcesAsync(string? channelId, CancellationToken ct = default)
    {
        var q = string.IsNullOrEmpty(channelId) ? "" : "?channelId=" + Uri.EscapeDataString(channelId);
        var result = await SendAsync(HttpMethod.Get, "/api/admin/import/sources" + q, null, ct).ConfigureAwait(false);
        return result?["sources"]?.Deserialize<List<ImportSource>>(Json) ?? throw Unreadable();
    }

    /// <summary>The accounts the channel's owner connected: each can be added without a recorded permission.</summary>
    public virtual async Task<IReadOnlyList<SourceConnection>> ListConnectionsAsync(string channelId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Get, $"/api/admin/import/sources/channels/{Uri.EscapeDataString(channelId)}/connections", null, ct).ConfigureAwait(false);
        return result?["connections"]?.Deserialize<List<SourceConnection>>(Json) ?? throw Unreadable();
    }

    public virtual async Task<ImportSource> AddSourceAsync(NewSource source, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, "/api/admin/import/sources", source, ct).ConfigureAwait(false);
        return result?["source"]?.Deserialize<ImportSource>(Json) ?? throw Unreadable();
    }

    /// <summary>
    /// Changes a source's schedule or turns it on or off. Turning a connected source back on fails with
    /// <c>409 not_connected</c> until the owner connects that account again.
    /// </summary>
    public virtual async Task<ImportSource> UpdateSourceAsync(string sourceId, string? schedule = null, bool? enabled = null, string? label = null, CancellationToken ct = default)
    {
        var patch = new Dictionary<string, object>();
        if (schedule is not null) patch["schedule"] = schedule;
        if (enabled is not null) patch["enabled"] = enabled.Value;
        if (label is not null) patch["label"] = label;
        var result = await SendAsync(HttpMethod.Patch, "/api/admin/import/sources/" + Uri.EscapeDataString(sourceId), patch, ct).ConfigureAwait(false);
        return result?["source"]?.Deserialize<ImportSource>(Json) ?? throw Unreadable();
    }

    /// <summary>Removes the source; the posts and videos it already made stay on the channel.</summary>
    public virtual Task RemoveSourceAsync(string sourceId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, "/api/admin/import/sources/" + Uri.EscapeDataString(sourceId), null, ct);

    /// <summary>Queues a sync now; when one is already waiting, the server answers that one instead of queuing another.</summary>
    public virtual async Task<SyncStarted> SyncSourceAsync(string sourceId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, $"/api/admin/import/sources/{Uri.EscapeDataString(sourceId)}/sync", new { }, ct).ConfigureAwait(false);
        return result?.Deserialize<SyncStarted>(Json) is { JobId.Length: > 0 } started ? started : throw Unreadable();
    }

    /// <summary>The source's latest items, newest first; with <paramref name="state"/>, only those in it.</summary>
    public virtual async Task<IReadOnlyList<ImportSourceItem>> ListSourceItemsAsync(string sourceId, string? state = null, int limit = 50, CancellationToken ct = default)
    {
        var q = $"?limit={Math.Clamp(limit, 1, 500)}" + (state is null ? "" : "&state=" + Uri.EscapeDataString(state));
        var result = await SendAsync(HttpMethod.Get, $"/api/admin/import/sources/{Uri.EscapeDataString(sourceId)}/items{q}", null, ct).ConfigureAwait(false);
        return result?["items"]?.Deserialize<List<ImportSourceItem>>(Json) ?? throw Unreadable();
    }

    /// <summary>A sync job with its attempts, next try and log.</summary>
    public virtual async Task<SyncJob> GetJobAsync(string jobId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Get, "/api/content/jobs/" + Uri.EscapeDataString(jobId), null, ct).ConfigureAwait(false);
        return result?.Deserialize<SyncJob>(Json) is { Id.Length: > 0 } job ? job : throw Unreadable();
    }

    /// <summary>Runs a failed or cancelled sync job again (the same Retry as the Errors and Jobs pages; audited).</summary>
    public virtual Task RetryJobAsync(string jobId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/content/jobs/{Uri.EscapeDataString(jobId)}/retry", new { }, ct);

    /// <summary>
    /// Cancels one of the source's jobs (its sync, or its Takeout match). Idempotent: a job already cancelled or finished is
    /// answered as it is. The answer is the job with its log, whose last line names the admin who cancelled it.
    /// </summary>
    public virtual async Task<SyncJob> CancelSourceJobAsync(string sourceId, string jobId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, $"/api/admin/import/sources/{Uri.EscapeDataString(sourceId)}/cancel", new { jobId }, ct).ConfigureAwait(false);
        return result?["job"]?.Deserialize<SyncJob>(Json) is { Id.Length: > 0 } job ? job : throw Unreadable();
    }

    // ---------------------------------------------------------------- YouTube Takeout files (YouTube sources only)

    /// <summary>Lists the export's video files (at most <see cref="TakeoutExport.MaxPerRequest"/>); the server's job matches them.</summary>
    public virtual async Task<TakeoutListed> ListTakeoutFilesAsync(string sourceId, IReadOnlyList<TakeoutFileInput> files, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, $"/api/admin/import/sources/{Uri.EscapeDataString(sourceId)}/takeout/files", new { files }, ct).ConfigureAwait(false);
        return result?.Deserialize<TakeoutListed>(Json) is { JobId.Length: > 0 } listed ? listed : throw Unreadable();
    }

    /// <summary>The listed files with what became of each, the counts per state and the latest match job.</summary>
    public virtual async Task<TakeoutReport> GetTakeoutAsync(string sourceId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Get, $"/api/admin/import/sources/{Uri.EscapeDataString(sourceId)}/takeout?limit=500", null, ct).ConfigureAwait(false);
        return result?.Deserialize<TakeoutReport>(Json) is { Files: not null } report ? report : throw Unreadable();
    }

    /// <summary>The admin's pick for an ambiguous file: one of its candidates.</summary>
    public virtual async Task<TakeoutFile> ConfirmTakeoutFileAsync(string sourceId, string fileId, string itemId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, $"/api/admin/import/sources/{Uri.EscapeDataString(sourceId)}/takeout/files/{Uri.EscapeDataString(fileId)}/confirm", new { itemId }, ct).ConfigureAwait(false);
        return result?["file"]?.Deserialize<TakeoutFile>(Json) ?? throw Unreadable();
    }

    /// <summary>Opens (or resumes) the record's upload for a matched file.</summary>
    public virtual async Task<TakeoutUpload> OpenTakeoutUploadAsync(string sourceId, string fileId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, $"/api/admin/import/sources/{Uri.EscapeDataString(sourceId)}/takeout/files/{Uri.EscapeDataString(fileId)}/upload", new { }, ct).ConfigureAwait(false);
        var file = result?["file"]?.Deserialize<TakeoutFile>(Json);
        var upload = result?["upload"]?.Deserialize<UploadSession>(Json);
        if (file is null || upload is null || string.IsNullOrEmpty(upload.TusSessionId)) throw Unreadable();
        return new TakeoutUpload(file, upload);
    }

    /// <summary>The whole file was sent: the server's job posts the record's video.</summary>
    public virtual async Task<TakeoutFile> MarkTakeoutUploadedAsync(string sourceId, string fileId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Post, $"/api/admin/import/sources/{Uri.EscapeDataString(sourceId)}/takeout/files/{Uri.EscapeDataString(fileId)}/uploaded", new { }, ct).ConfigureAwait(false);
        return result?["file"]?.Deserialize<TakeoutFile>(Json) ?? throw Unreadable();
    }

    /// <summary>A channel's name, for sources listed across channels; null when admin-api has none.</summary>
    public virtual async Task<string?> GetChannelNameAsync(string channelId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Get, "/api/content/channels/" + Uri.EscapeDataString(channelId), null, ct).ConfigureAwait(false);
        return result?["name"]?.GetValue<string>();
    }
}

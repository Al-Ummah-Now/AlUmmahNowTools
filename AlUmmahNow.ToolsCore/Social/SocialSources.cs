using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AlUmmahNow.ToolsCore.Social;

/// <summary>
/// Social import sources (wiki TOOLS-ADMIN TA-9 and TA-13): a channel owner's public feed or account, imported as the
/// channel's community posts on demand and on a schedule. Super admins only (<c>import_sources:manage</c>); admin-api
/// keeps nothing itself, api-videos stores the sources and runs one queued <c>import.source-sync</c> job per source.
/// The shapes match shared-types <c>import-sources.ts</c>.
/// </summary>
public static class SourceNetworks
{
    public const string Rss = "rss";
    public const string Bluesky = "bluesky";
    public const string Mastodon = "mastodon";
    public const string Telegram = "telegram";
    public const string YouTube = "youtube";

    /// <summary>Every network a source can read, in the order the app lists them. X, Meta and TikTok are never imported.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Rss, Bluesky, Mastodon, Telegram, YouTube };

    /// <summary>
    /// Telegram posts come through our bot, which only the owner's own connection adds to the channel: a Telegram source
    /// is always a connected one, never a recorded permission (api-videos authorize.ts).
    /// </summary>
    public static bool ConnectedOnly(string network) => network == Telegram;
}

public static class SourceSchedules
{
    public const string Manual = "manual";
    public const string Hourly = "hourly";
    public const string Daily = "daily";
    public const string Weekly = "weekly";

    public static readonly IReadOnlyList<string> All = new[] { Manual, Hourly, Daily, Weekly };
}

/// <summary>Whose written permission a super admin recorded for a source the owner did not connect, and when.</summary>
public sealed record SourcePermission(
    [property: JsonPropertyName("by")] string By,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("at")] DateTimeOffset? At);

/// <summary>One source (shared-types <c>ImportSourceView</c>).</summary>
public sealed record ImportSource(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("channelId")] string ChannelId,
    [property: JsonPropertyName("network")] string Network,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("auth")] string Auth,
    [property: JsonPropertyName("permission")] SourcePermission? Permission,
    [property: JsonPropertyName("schedule")] string Schedule,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("nextSyncAt")] DateTimeOffset? NextSyncAt,
    [property: JsonPropertyName("lastSyncAt")] DateTimeOffset? LastSyncAt,
    [property: JsonPropertyName("lastJobId")] string? LastJobId,
    [property: JsonPropertyName("lastError")] string? LastError,
    [property: JsonPropertyName("counts")] IReadOnlyDictionary<string, int>? Counts,
    [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt)
{
    /// <summary>The owner connected this account themselves (Studio, or the YouTube import's Google consent).</summary>
    public bool IsConnected => Auth == "connected";

    public int Count(string state) => Counts is not null && Counts.TryGetValue(state, out var n) ? n : 0;

    /// <summary>
    /// The code at the start of <see cref="LastError"/> (<c>not_connected: …</c>), so the app can say it in the admin's
    /// language; null when the error has none.
    /// </summary>
    public string? LastErrorCode => SourceText.ErrorCode(LastError);
}

/// <summary>A post, image, link or video of a source and what became of it (shared-types <c>ImportSourceItemView</c>).</summary>
public sealed record ImportSourceItem(
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("postId")] string? PostId,
    [property: JsonPropertyName("videoId")] string? VideoId,
    [property: JsonPropertyName("originalUrl")] string? OriginalUrl,
    [property: JsonPropertyName("originalAt")] DateTimeOffset? OriginalAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt);

/// <summary>An account the channel's owner connected, which can become a source without a recorded permission.</summary>
public sealed record SourceConnection(
    [property: JsonPropertyName("network")] string Network,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("label")] string Label);

/// <summary>One line of a job's log (shared-types <c>AdminJobLogEntry</c>): an event, not a sentence, so the app words it.</summary>
public sealed record SyncJobLogLine(
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("attempt")] int? Attempt,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("delaySeconds")] int? DelaySeconds,
    [property: JsonPropertyName("retried")] bool? Retried,
    [property: JsonPropertyName("by")] JobActor? By = null);

/// <summary>The admin a log line names (a Cancel): their id, and their name when admin-api knew it.</summary>
public sealed record JobActor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>A source's sync job (admin-api <c>GET /api/content/jobs/:id</c>, shared-types <c>AdminJob</c>, TA-8).</summary>
public sealed record SyncJob(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("attempts")] int Attempts,
    [property: JsonPropertyName("maxAttempts")] int MaxAttempts,
    [property: JsonPropertyName("nextRunAt")] DateTimeOffset? NextRunAt,
    [property: JsonPropertyName("lastError")] string? LastError,
    [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("finishedAt")] DateTimeOffset? FinishedAt,
    [property: JsonPropertyName("log")] IReadOnlyList<SyncJobLogLine>? Log)
{
    /// <summary>Waiting in the queue, running, or waiting to try again after a failed attempt.</summary>
    public bool IsActive => Status is "queued" or "running" or "retrying";

    /// <summary>Waiting or running: Cancel stops it (it ends as cancelled, not as a failure).</summary>
    public bool CanCancel => IsActive;

    /// <summary>
    /// It gave up after its last attempt (the DLQ, and a row on the Errors page), or an admin cancelled it: Retry runs it
    /// again (a cancelled sync starts a new run).
    /// </summary>
    public bool CanRetry => Status is "failed" or "cancelled";

    /// <summary>An admin cancelled it.</summary>
    public bool IsCancelled => Status == "cancelled";

    /// <summary>Who cancelled it: the name on its last <c>cancelled</c> log line, if any.</summary>
    public string? CancelledBy => Log?.LastOrDefault(l => l.Event == "cancelled")?.By?.Name;
}

/// <summary>What <c>POST /api/admin/import/sources</c> takes.</summary>
public sealed record NewSource(
    [property: JsonPropertyName("channelId")] string ChannelId,
    [property: JsonPropertyName("network")] string Network,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("label"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label,
    [property: JsonPropertyName("schedule")] string Schedule,
    [property: JsonPropertyName("permission"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NewSourcePermission? Permission);

public sealed record NewSourcePermission(
    [property: JsonPropertyName("by")] string By,
    [property: JsonPropertyName("note")] string Note);

/// <summary>A sync the server queued, or the one already waiting for that source (a sync is idempotent).</summary>
public sealed record SyncStarted(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("status")] string Status);

/// <summary>Where a source stands, for the line under its name.</summary>
public enum SourcePhase
{
    /// <summary>Synced only when an admin presses Sync now</summary>
    Manual,
    /// <summary>On a schedule (hourly, daily, weekly), next sync known</summary>
    Scheduled,
    /// <summary>Its sync job is waiting in the queue or running</summary>
    Syncing,
    /// <summary>Its sync job failed an attempt and tries again later on its own</summary>
    Retrying,
    /// <summary>Its last sync gave up: on the Errors page, with Retry</summary>
    Failed,
    /// <summary>An admin cancelled its last sync (not a failure); a schedule carries on</summary>
    Cancelled,
    /// <summary>A super admin turned it off</summary>
    Off,
    /// <summary>Turned off by the server because the owner's connection is gone; on again only once they connect</summary>
    OffNotConnected,
}

/// <summary>A stored reason, explained (<see cref="SourceText.Explain"/>).</summary>
public sealed record Reason(string? Code, int? HttpStatus, string? Detail);

public static class SourceText
{
    private static readonly Regex Code = new("^([a-z][a-z0-9_]{1,40}):", RegexOptions.CultureInvariant);

    /// <summary>The <c>code</c> of a server message written as <c>code: text</c>, else null.</summary>
    public static string? ErrorCode(string? message) =>
        message is not null && Code.Match(message) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>
    /// The codes api-videos stores as a source's or a job's reason (<c>"&lt;code&gt;: &lt;detail&gt;"</c>, lib/importSources/errors.ts),
    /// each worded by the app (<c>SocialReason_&lt;code&gt;</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> ReasonCodes = new[]
    {
        "not_connected", "channel_unavailable", "source_unreachable", "source_not_found", "source_forbidden", "source_bad_response",
        "source_not_a_feed", "sync_failed", "post_refused", "takeout_failed", "upload_incomplete",
    };

    /// <summary>Codes whose detail is the server's own words about an unexpected failure: shown on their own line for troubleshooting.</summary>
    private static readonly HashSet<string> DetailShown = new() { "sync_failed", "takeout_failed" };

    private static readonly Regex HttpStatus = new(@"\(http_(\d{3})\)\s*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// What the app shows for a stored reason: its <see cref="ReasonCodes"/> code (worded by the app), the HTTP status a
    /// fetch answered (a number the app writes in the admin's digits), and the detail to show on its own line. An unknown
    /// code, or none, gives no code and the whole text as the detail.
    /// </summary>
    public static Reason Explain(string error)
    {
        var code = ErrorCode(error);
        if (code is null || !ReasonCodes.Contains(code)) return new Reason(null, null, ErrorDetail(error) ?? error);
        var detail = ErrorDetail(error);
        int? status = detail is not null && HttpStatus.Match(detail) is { Success: true } m ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        return new Reason(code, status, DetailShown.Contains(code) && !string.IsNullOrWhiteSpace(detail) ? detail : null);
    }

    /// <summary>The text of a <c>code: text</c> message without its code.</summary>
    public static string? ErrorDetail(string? message) =>
        message is not null && Code.Match(message) is { Success: true } m ? message[m.Length..].Trim() : message;

    /// <summary>
    /// The source's phase. Off comes first (nothing runs), then what its latest job is doing, then a last error the
    /// job left on the source, then whether it has a schedule.
    /// </summary>
    public static SourcePhase PhaseOf(ImportSource source, SyncJob? job)
    {
        if (!source.Enabled) return source.LastErrorCode == "not_connected" ? SourcePhase.OffNotConnected : SourcePhase.Off;
        if (job is not null && job.Id == source.LastJobId)
        {
            if (job.Status is "queued" or "running") return SourcePhase.Syncing;
            if (job.Status == "retrying") return SourcePhase.Retrying;
            if (job.Status == "failed") return SourcePhase.Failed;
            if (job.Status == "cancelled") return SourcePhase.Cancelled;
            if (job.Status == "succeeded") return source.Schedule == SourceSchedules.Manual ? SourcePhase.Manual : SourcePhase.Scheduled;
        }
        if (!string.IsNullOrEmpty(source.LastError)) return SourcePhase.Failed;
        return source.Schedule == SourceSchedules.Manual ? SourcePhase.Manual : SourcePhase.Scheduled;
    }
}

/// <summary>
/// The Add source form, checked before it is sent: the same address rules as api-videos (<c>normalizeAddress</c>), so a
/// mistyped address is caught at once, and the owner's permission required unless they connected that account.
/// </summary>
public static class SourceForm
{
    private static readonly Regex BlueskyDid = new("^did:(plc:[a-z2-7]{24}|web:[a-z0-9.-]+)$", RegexOptions.CultureInvariant);
    private static readonly Regex BlueskyHandle = new("^@?([a-z0-9][a-z0-9-]*(\\.[a-z0-9][a-z0-9-]*)+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MastodonAccount = new("^@?([A-Za-z0-9_]{1,30})@([a-z0-9.-]+\\.[a-z]{2,})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TelegramChat = new("^-?\\d{5,20}$", RegexOptions.CultureInvariant);
    private static readonly Regex YouTubeChannel = new("^UC[A-Za-z0-9_-]{22}$", RegexOptions.CultureInvariant);

    /// <summary>The longest values the server takes (api-videos importSources.router.ts).</summary>
    public const int MaxAddress = 500;
    public const int MaxLabel = 120;
    public const int MaxPermissionBy = 200;
    public const int MaxPermissionNote = 1000;

    /// <summary>
    /// The address in the form the server stores it, or a problem: <c>address_missing</c> or <c>invalid_address</c>.
    /// </summary>
    public static (string? Address, string? Problem) NormalizeAddress(string network, string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Length == 0) return (null, "address_missing");
        if (value.Length > MaxAddress) return (null, "invalid_address");
        switch (network)
        {
            case SourceNetworks.Rss:
                return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.Host.Length > 0 && uri.UserInfo.Length == 0
                    ? (uri.ToString(), null)
                    : (null, "invalid_address");
            case SourceNetworks.Bluesky:
                if (BlueskyDid.IsMatch(value)) return (value, null);
                var handle = BlueskyHandle.Match(value);
                return handle.Success ? (handle.Groups[1].Value.ToLowerInvariant(), null) : (null, "invalid_address");
            case SourceNetworks.Mastodon:
                var account = MastodonAccount.Match(value);
                return account.Success ? ($"{account.Groups[1].Value}@{account.Groups[2].Value.ToLowerInvariant()}", null) : (null, "invalid_address");
            case SourceNetworks.Telegram:
                return TelegramChat.IsMatch(value) ? (value, null) : (null, "invalid_address");
            case SourceNetworks.YouTube:
                return YouTubeChannel.IsMatch(value) ? (value, null) : (null, "invalid_address");
            default:
                return (null, "invalid_network");
        }
    }

    /// <summary>
    /// The request for a new source, or the first problem with the form: <c>channel_missing</c>, an address problem,
    /// <c>not_connected</c> (Telegram that the owner did not connect), <c>permission_required</c> (no connection and no
    /// recorded permission) or <c>invalid_schedule</c>. <paramref name="connections"/> are the accounts the owner connected.
    /// </summary>
    public static (NewSource? Request, string? Problem) Build(
        string? channelId,
        string network,
        string? address,
        string? label,
        string schedule,
        string? permissionBy,
        string? permissionNote,
        IReadOnlyCollection<SourceConnection> connections)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return (null, "channel_missing");
        if (!SourceSchedules.All.Contains(schedule)) return (null, "invalid_schedule");
        var (normalized, problem) = NormalizeAddress(network, address);
        if (problem is not null) return (null, problem);
        var connected = connections.Any(c => c.Network == network && string.Equals(c.Address, normalized, StringComparison.Ordinal));
        // The server turns a Bluesky handle into the account's DID before it compares, and connections are listed by DID:
        // a handle may still be the connected account, so the server decides (403 permission_required when it is not)
        var mayBeConnected = network == SourceNetworks.Bluesky && !normalized!.StartsWith("did:", StringComparison.Ordinal)
            && connections.Any(c => c.Network == SourceNetworks.Bluesky);
        NewSourcePermission? permission = null;
        var givenBy = (permissionBy ?? "").Trim();
        var givenNote = (permissionNote ?? "").Trim();
        if (mayBeConnected && givenBy.Length > 0 && givenNote.Length > 0)
        {
            permission = new NewSourcePermission(Cut(givenBy, MaxPermissionBy), Cut(givenNote, MaxPermissionNote));
        }
        else if (!connected && !mayBeConnected)
        {
            if (SourceNetworks.ConnectedOnly(network)) return (null, "not_connected");
            if (givenBy.Length == 0 || givenNote.Length == 0) return (null, "permission_required");
            permission = new NewSourcePermission(Cut(givenBy, MaxPermissionBy), Cut(givenNote, MaxPermissionNote));
        }
        var name = (label ?? "").Trim();
        return (new NewSource(channelId.Trim(), network, normalized!, name.Length == 0 ? null : Cut(name, MaxLabel), schedule, permission), null);
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}

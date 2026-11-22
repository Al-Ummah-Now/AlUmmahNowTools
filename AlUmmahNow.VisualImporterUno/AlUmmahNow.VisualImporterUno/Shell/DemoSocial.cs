using System.Text.Json;
using System.Text.Json.Nodes;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.VisualImporterUno.ViewModels;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The Social sources page's demo data for screenshots (<c>TOOLS_DEMO=1</c>, <c>TOOLS_DEMO_ROLE=super_admin</c>):
/// a channel's sources in every state (scheduled, syncing, failed with its log, off because the owner disconnected,
/// waiting for Takeout files) and its connected accounts. <c>TOOLS_DEMO_SOCIAL=all</c> lists every channel,
/// <c>empty</c> a channel with nothing yet, <c>add</c> a filled form with its error. Nothing is sent anywhere.
/// </summary>
internal static class DemoSocial
{
    public const string RssId = "isrc_demo_rss";
    public const string TelegramId = "isrc_demo_telegram";
    public const string FailedId = "isrc_demo_mastodon";
    public const string BlueskyId = "isrc_demo_bluesky";
    public const string YouTubeId = "isrc_demo_youtube";
    private const string TelegramChat = "-1001876543210";

    private static string? Mode => Environment.GetEnvironmentVariable("TOOLS_DEMO_SOCIAL");

    /// <summary>The channel the page opens on; none for <c>TOOLS_DEMO_SOCIAL=all</c>.</summary>
    public static ImportChannel? Channel => Mode == "all" ? null : DemoImport.Channels[0];

    private static DateTimeOffset Now => DemoImport.Now;

    private static string Iso(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The filled form of the <c>add</c> frame: a feed whose permission note is missing.</summary>
    public static void FillForm(SocialSourcesViewModel vm)
    {
        if (Mode != "add") return;
        vm.NetworkIndex = 0;
        vm.Address = "https://ahmadalazhari.org/podcast.xml";
        vm.Label = "Podcast";
        vm.PermissionBy = "Shaykh Ahmad al-Azhari";
        vm.FormError = Loc.KeepLastWords(Loc.Get("SocialError_permission_required"));
    }

    private static JsonObject Source(string id, string channelId, string network, string address, string label, bool connected, (string By, string Note, DateTimeOffset At)? permission,
        string schedule, bool enabled, DateTimeOffset? next, DateTimeOffset? last, string? jobId, string? error, object counts) =>
        JsonSerializer.SerializeToNode(new
        {
            id,
            channelId,
            network,
            address,
            label,
            auth = connected ? "connected" : "recorded",
            socialAccountId = connected && network != "youtube" ? "sa_" + id : null,
            ytImportId = network == "youtube" ? "yti_demo" : null,
            permission = permission is { } p ? new { by = p.By, note = p.Note, at = Iso(p.At) } : null,
            schedule,
            enabled,
            nextSyncAt = next is { } n ? Iso(n) : null,
            lastSyncAt = last is { } l ? Iso(l) : null,
            lastJobId = jobId,
            lastError = error,
            counts,
            createdBy = "usr_demo",
            createdAt = Iso(Now.AddDays(-12)),
            updatedAt = Iso(Now.AddHours(-1)),
        })!.AsObject();

    private static IEnumerable<JsonObject> Sources()
    {
        if (Mode == "empty") yield break;
        var channel = DemoImport.ChannelId;
        yield return Source(TelegramId, channel, "telegram", TelegramChat, "دروس الشيخ أحمد الأزهري", true, null, "hourly", true, Now.AddMinutes(40), Now.AddMinutes(-20), "job_demo_telegram", null,
            new { posted = 126, pending = 6, awaiting_file = 2 });
        yield return Source(RssId, channel, "rss", "https://ahmadalazhari.org/feed.xml", "Lessons and articles", false,
            ("Shaykh Ahmad al-Azhari", "Email of 18 Nov 2026: every article and lesson on the blog", Now.AddDays(-4)), "daily", true, Now.AddHours(3).AddMinutes(40), Now.AddHours(-20).AddMinutes(-20), "job_demo_rss", null,
            new { posted = 48, skipped = 3 });
        yield return Source(FailedId, channel, "mastodon", "ahmadalazhari@mastodon.social", "Mastodon posts", false,
            ("Shaykh Ahmad al-Azhari", "Signed letter of 20 Nov 2026, kept by the office", Now.AddDays(-2)), "weekly", true, Now.AddDays(6), Now.AddDays(-1), "job_demo_mastodon",
            "The source could not be read (http_503)", new { posted = 14 });
        yield return Source(BlueskyId, channel, "bluesky", "did:plc:4kq7x2m3w5r6tay7u2i3o4pa", "ahmadalazhari.bsky.social", true, null, "daily", false, null, Now.AddDays(-3), "job_demo_bluesky",
            "not_connected: the owner's connection to this account was removed; the source is off until they connect it again", new { posted = 31 });
        yield return Source(YouTubeId, channel, "youtube", "UCq3Zx9LmT4Kd7Vn2Wp8RbYs", "Shaykh Ahmad al-Azhari", true, null, "manual", true, null, Now.AddDays(-2), "job_demo_youtube", null,
            new { awaiting_file = 214 });
        if (Mode == "all")
        {
            yield return Source("isrc_demo_seerah", "ch_demo_seerah", "rss", "https://seerahcircle.org/feed", "Seerah Circle articles", false,
                ("Seerah Circle board", "Board minutes of 2 Nov 2026", Now.AddDays(-20)), "weekly", true, Now.AddDays(3), Now.AddDays(-4), "job_demo_seerah", null, new { posted = 9 });
        }
    }

    private static JsonObject Job(string id, string status, int attempts, DateTimeOffset created, DateTimeOffset? finished, DateTimeOffset? nextRun, string? error, IEnumerable<object> log) =>
        JsonSerializer.SerializeToNode(new
        {
            id,
            kind = "import.source-sync",
            targetKind = "channel",
            targetId = DemoImport.ChannelId,
            @params = new { },
            status,
            attempts,
            maxAttempts = 6,
            nextRunAt = nextRun is { } n ? Iso(n) : null,
            lastError = error,
            result = (object?)null,
            actorId = "usr_demo",
            createdAt = Iso(created),
            updatedAt = Iso(finished ?? created),
            finishedAt = finished is { } f ? Iso(f) : null,
            log,
        })!.AsObject();

    private static JsonObject? JobFor(string id)
    {
        switch (id)
        {
            case "job_demo_telegram":
            {
                var at = Now.AddMinutes(-3);
                return Job(id, "running", 1, at, null, null, null, new object[]
                {
                    new { at = Iso(at), @event = "queued" },
                    new { at = Iso(at.AddSeconds(2)), @event = "started", attempt = 1 },
                    new { at = Iso(at.AddSeconds(9)), @event = "waiting", delaySeconds = 1 },
                });
            }
            case "job_demo_mastodon":
            {
                // Six attempts, backing off 10 s × 2ⁿ, then the DLQ and the Errors page
                const string error = "The source could not be read (http_503)";
                var at = Now.AddMinutes(-58);
                var log = new List<object> { new { at = Iso(at), @event = "queued" } };
                var t = at.AddSeconds(1);
                for (var attempt = 1; attempt <= 6; attempt++)
                {
                    log.Add(new { at = Iso(t), @event = "started", attempt });
                    t = t.AddSeconds(3);
                    if (attempt < 6)
                    {
                        var delay = Math.Min(300, 10 * (1 << (attempt - 1)));
                        log.Add(new { at = Iso(t), @event = "failed_attempt", attempt, error, delaySeconds = delay });
                        t = t.AddSeconds(delay);
                    }
                    else
                    {
                        log.Add(new { at = Iso(t), @event = "failed", attempt, error });
                    }
                }
                return Job(id, "failed", 6, at, t, null, error, log);
            }
            case "job_demo_rss" or "job_demo_bluesky" or "job_demo_youtube" or "job_demo_seerah":
            {
                var at = id == "job_demo_rss" ? Now.AddHours(-20).AddMinutes(-21) : Now.AddDays(-2);
                return Job(id, "succeeded", 1, at, at.AddMinutes(1), null, null, new object[]
                {
                    new { at = Iso(at), @event = "queued" },
                    new { at = Iso(at.AddSeconds(1)), @event = "started", attempt = 1 },
                    new { at = Iso(at.AddMinutes(1)), @event = "succeeded" },
                });
            }
            default:
                return null;
        }
    }

    private static IEnumerable<object> Items()
    {
        var at = Now.AddMinutes(-20);
        object Item(string itemId, string kind, string state, string? note, int minutesAgo) => new
        {
            sourceId = TelegramId,
            itemId,
            kind,
            state,
            note,
            postId = state == "posted" ? "post_" + itemId : null,
            videoId = state == "posted" && kind == "video" ? "v" + itemId : null,
            originalUrl = "https://t.me/ahmadalazhari_lessons/" + itemId,
            originalAt = Iso(Now.AddMinutes(-minutesAgo)),
            createdAt = Iso(at),
            updatedAt = Iso(at),
        };
        yield return Item("413", "text", "pending", null, 25);
        yield return Item("412", "text", "posted", null, 70);
        yield return Item("411", "image", "posted", null, 190);
        yield return Item("410", "video", "awaiting_file", "too_large_for_bot_api", 300);
        yield return Item("409", "link", "posted", null, 26 * 60);
        yield return Item("408", "text", "skipped", "posted_by_us", 27 * 60);
        yield return Item("407", "video", "posted", null, 30 * 60);
    }

    private static IEnumerable<object> Connections()
    {
        if (Mode == "empty") yield break;
        yield return new { network = "telegram", address = TelegramChat, label = "دروس الشيخ أحمد الأزهري", socialAccountId = "sa_1", ytImportId = (string?)null };
        yield return new { network = "telegram", address = "-1002233445566", label = "فتاوى الصيام", socialAccountId = "sa_2", ytImportId = (string?)null };
        yield return new { network = "youtube", address = "UCq3Zx9LmT4Kd7Vn2Wp8RbYs", label = "Shaykh Ahmad al-Azhari", socialAccountId = (string?)null, ytImportId = "yti_demo" };
    }

    private static string Ok(object result) => JsonSerializer.Serialize(new { success = true, result });

    /// <summary>The demo admin-api's answer for a sources or jobs path, or null for any other path.</summary>
    public static string? Answer(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        const string sources = "/api/admin/import/sources";
        if (path == sources && request.Method == HttpMethod.Get)
        {
            var channel = query["channelId"];
            return Ok(new { sources = Sources().Where(s => channel is null || (string?)s["channelId"] == channel).ToArray() });
        }
        if (path == sources && request.Method == HttpMethod.Post) return Ok(new { source = Sources().First() });
        if (path.StartsWith(sources + "/channels/", StringComparison.Ordinal)) return Ok(new { connections = Connections().ToArray() });
        if (path.StartsWith(sources + "/", StringComparison.Ordinal))
        {
            var rest = path[(sources.Length + 1)..];
            if (rest.EndsWith("/items", StringComparison.Ordinal)) return Ok(new { items = rest.StartsWith(TelegramId, StringComparison.Ordinal) ? Items().ToArray() : Array.Empty<object>() });
            if (rest.EndsWith("/sync", StringComparison.Ordinal)) return Ok(new { jobId = "job_demo_new", status = "queued" });
            if (request.Method == HttpMethod.Delete) return Ok(new { deleted = true });
            var source = Sources().FirstOrDefault(s => (string?)s["id"] == rest) ?? Sources().First();
            return Ok(new { source });
        }
        if (path.StartsWith("/api/content/jobs/", StringComparison.Ordinal))
        {
            var id = path["/api/content/jobs/".Length..].Split('/')[0];
            return path.EndsWith("/retry", StringComparison.Ordinal) ? Ok(new { id, status = "queued" }) : JobFor(id) is { } job ? Ok(job) : null;
        }
        if (path.StartsWith("/api/content/channels/", StringComparison.Ordinal))
        {
            var id = path["/api/content/channels/".Length..];
            return DemoImport.Channels.FirstOrDefault(c => c.Id == id) is { } c ? Ok(new { id, name = c.Name }) : null;
        }
        return null;
    }
}

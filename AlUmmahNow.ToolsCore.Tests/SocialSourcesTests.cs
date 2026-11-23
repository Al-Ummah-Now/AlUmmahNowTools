using System.Net;
using System.Text.Json.Nodes;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Social;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

public class SourceFormTests
{
    private static readonly SourceConnection[] None = Array.Empty<SourceConnection>();

    [Theory]
    [InlineData("rss", " https://alummahnow.com/feed.xml ", "https://alummahnow.com/feed.xml")]
    [InlineData("bluesky", "@Scholar.bsky.social", "scholar.bsky.social")]
    [InlineData("bluesky", "did:plc:abcdefghijklmnopqrstuvwx", "did:plc:abcdefghijklmnopqrstuvwx")]
    [InlineData("mastodon", "@shaykh@Mastodon.Social", "shaykh@mastodon.social")]
    [InlineData("telegram", "-1001234567890", "-1001234567890")]
    [InlineData("youtube", "UCabcdefghijklmnopqrstuv", "UCabcdefghijklmnopqrstuv")]
    public void Addresses_are_normalized_as_the_server_stores_them(string network, string raw, string expected)
    {
        var (address, problem) = SourceForm.NormalizeAddress(network, raw);
        Assert.Null(problem);
        Assert.Equal(expected, address);
    }

    [Theory]
    [InlineData("rss", "ftp://alummahnow.com/feed")]
    [InlineData("rss", "alummahnow.com/feed")]
    [InlineData("rss", "https://user:pw@alummahnow.com/feed")]
    [InlineData("bluesky", "not a handle")]
    [InlineData("mastodon", "shaykh")]
    [InlineData("telegram", "@channel")]
    [InlineData("youtube", "https://youtube.com/@scholar")]
    public void Wrong_addresses_are_caught_before_sending(string network, string raw)
    {
        Assert.Equal("invalid_address", SourceForm.NormalizeAddress(network, raw).Problem);
    }

    [Fact]
    public void An_empty_address_or_an_unknown_network_says_so()
    {
        Assert.Equal("address_missing", SourceForm.NormalizeAddress("rss", "  ").Problem);
        Assert.Equal("invalid_network", SourceForm.NormalizeAddress("x", "@someone").Problem);
    }

    [Fact]
    public void A_source_the_owner_did_not_connect_needs_a_recorded_permission()
    {
        var (request, problem) = SourceForm.Build("ch_1", "rss", "https://alummahnow.com/feed", null, "daily", "", "", None);
        Assert.Null(request);
        Assert.Equal("permission_required", problem);

        (request, problem) = SourceForm.Build("ch_1", "rss", "https://alummahnow.com/feed", "  Lessons feed ", "daily", " Shaykh Ahmad ", " Email of 12 Nov 2026 ", None);
        Assert.Null(problem);
        Assert.NotNull(request);
        Assert.Equal("Lessons feed", request!.Label);
        Assert.Equal(new NewSourcePermission("Shaykh Ahmad", "Email of 12 Nov 2026"), request.Permission);
        Assert.Equal("daily", request.Schedule);
    }

    [Fact]
    public void A_connected_account_needs_no_permission_and_sends_none()
    {
        var connections = new[] { new SourceConnection("mastodon", "shaykh@mastodon.social", "Shaykh Ahmad") };
        var (request, problem) = SourceForm.Build("ch_1", "mastodon", "@shaykh@Mastodon.social", null, "hourly", "ignored", "ignored", connections);
        Assert.Null(problem);
        Assert.Null(request!.Permission);
        Assert.Equal("shaykh@mastodon.social", request.Address);
    }

    [Fact]
    public void Telegram_is_only_ever_a_connected_source()
    {
        var (_, problem) = SourceForm.Build("ch_1", "telegram", "-1001234567890", null, "manual", "Shaykh Ahmad", "Letter", None);
        Assert.Equal("not_connected", problem);
        var (request, ok) = SourceForm.Build("ch_1", "telegram", "-1001234567890", null, "manual", null, null, new[] { new SourceConnection("telegram", "-1001234567890", "Lessons") });
        Assert.Null(ok);
        Assert.NotNull(request);
    }

    [Fact]
    public void A_bluesky_handle_is_left_to_the_server_when_the_owner_connected_bluesky()
    {
        // Connections are listed by DID; the server resolves the handle and decides
        var connections = new[] { new SourceConnection("bluesky", "did:plc:abcdefghijklmnopqrstuvwx", "Scholar") };
        var (request, problem) = SourceForm.Build("ch_1", "bluesky", "scholar.bsky.social", null, "manual", null, null, connections);
        Assert.Null(problem);
        Assert.Null(request!.Permission);
        // Without any Bluesky connection, a permission is required as for any other source
        Assert.Equal("permission_required", SourceForm.Build("ch_1", "bluesky", "scholar.bsky.social", null, "manual", null, null, None).Problem);
    }

    [Fact]
    public void A_channel_and_a_known_schedule_are_required_and_long_values_are_cut()
    {
        Assert.Equal("channel_missing", SourceForm.Build(" ", "rss", "https://alummahnow.com/feed", null, "daily", "a", "b", None).Problem);
        Assert.Equal("invalid_schedule", SourceForm.Build("ch_1", "rss", "https://alummahnow.com/feed", null, "monthly", "a", "b", None).Problem);
        var (request, _) = SourceForm.Build("ch_1", "rss", "https://alummahnow.com/feed", new string('l', 300), "daily", new string('b', 300), new string('n', 2000), None);
        Assert.Equal(SourceForm.MaxLabel, request!.Label!.Length);
        Assert.Equal(SourceForm.MaxPermissionBy, request.Permission!.By.Length);
        Assert.Equal(SourceForm.MaxPermissionNote, request.Permission.Note.Length);
    }
}

public class SourcePhaseTests
{
    private static ImportSource Source(bool enabled = true, string schedule = "daily", string? lastError = null, string? lastJobId = "job_1") =>
        new("isrc_1", "ch_1", "rss", "https://alummahnow.com/feed", "Feed", "recorded", null, schedule, enabled, null, null, lastJobId, lastError, null, null);

    private static SyncJob Job(string status, string id = "job_1") => new(id, "import.source-sync", status, 1, 6, null, null, null, null, null);

    [Fact]
    public void Off_comes_first_and_says_why_when_the_connection_is_gone()
    {
        Assert.Equal(SourcePhase.Off, SourceText.PhaseOf(Source(enabled: false), Job("running")));
        Assert.Equal(SourcePhase.OffNotConnected, SourceText.PhaseOf(Source(enabled: false, lastError: "not_connected: the owner's connection to this account was removed"), null));
    }

    [Theory]
    [InlineData("queued", SourcePhase.Syncing)]
    [InlineData("running", SourcePhase.Syncing)]
    [InlineData("retrying", SourcePhase.Retrying)]
    [InlineData("failed", SourcePhase.Failed)]
    [InlineData("succeeded", SourcePhase.Scheduled)]
    public void The_latest_job_decides_while_the_source_is_on(string status, SourcePhase expected)
    {
        Assert.Equal(expected, SourceText.PhaseOf(Source(), Job(status)));
    }

    [Fact]
    public void An_older_job_does_not_speak_for_the_source()
    {
        Assert.Equal(SourcePhase.Scheduled, SourceText.PhaseOf(Source(lastJobId: "job_2"), Job("failed", "job_1")));
    }

    [Fact]
    public void A_last_error_without_its_job_is_a_failure_and_a_manual_source_is_manual()
    {
        Assert.Equal(SourcePhase.Failed, SourceText.PhaseOf(Source(lastError: "fetch_failed: 502"), null));
        Assert.Equal(SourcePhase.Manual, SourceText.PhaseOf(Source(schedule: "manual"), null));
        Assert.Equal(SourcePhase.Manual, SourceText.PhaseOf(Source(schedule: "manual"), Job("succeeded")));
    }

    [Fact]
    public void Server_messages_split_into_code_and_text()
    {
        Assert.Equal("not_connected", SourceText.ErrorCode("not_connected: gone"));
        Assert.Equal("gone", SourceText.ErrorDetail("not_connected: gone"));
        Assert.Null(SourceText.ErrorCode("Feed answered 502"));
        Assert.Equal("Feed answered 502", SourceText.ErrorDetail("Feed answered 502"));
        Assert.Null(SourceText.ErrorCode(null));
    }

    [Fact]
    public void Only_a_failed_job_offers_retry()
    {
        Assert.True(Job("failed").CanRetry);
        Assert.False(Job("retrying").CanRetry);
        Assert.True(Job("retrying").IsActive);
        Assert.False(Job("succeeded").IsActive);
    }
}

public class SourcesApiTests
{
    private const string SourceJson = "{\"id\":\"isrc_0123456789abcdef0123456789abcdef\",\"channelId\":\"ch_1\",\"network\":\"rss\",\"address\":\"https://alummahnow.com/feed\"," +
        "\"label\":\"Lessons\",\"auth\":\"recorded\",\"socialAccountId\":null,\"ytImportId\":null,\"permission\":{\"by\":\"Shaykh Ahmad\",\"note\":\"Letter\",\"at\":\"2026-11-20T10:00:00.000Z\"}," +
        "\"schedule\":\"daily\",\"enabled\":true,\"nextSyncAt\":\"2026-11-23T10:00:00.000Z\",\"lastSyncAt\":\"2026-11-22T10:00:00.000Z\",\"lastJobId\":\"job_1\",\"lastError\":null," +
        "\"counts\":{\"posted\":12,\"skipped\":2,\"awaiting_file\":1},\"createdBy\":\"usr_admin\",\"createdAt\":\"2026-11-20T10:00:00.000Z\",\"updatedAt\":\"2026-11-22T10:00:00.000Z\"}";

    private static (ImportApiClient Api, FakeHandler Handler) Client(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var handler = new FakeHandler { Answer = answer };
        return (new ImportApiClient(new Uri("https://admin.example"), _ => Task.FromResult<string?>("tok"), handler), handler);
    }

    private static HttpResponseMessage Ok(string result, HttpStatusCode status = HttpStatusCode.OK) => FakeHandler.Json(status, "{\"success\":true,\"result\":" + result + "}");

    [Fact]
    public async Task Lists_a_channels_sources_with_their_counts_and_permission()
    {
        var (api, handler) = Client(_ => Ok("{\"sources\":[" + SourceJson + "]}"));
        var sources = await api.ListSourcesAsync("ch 1");
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/admin/import/sources?channelId=ch%201", request.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer tok", request.Headers.Authorization!.ToString());
        var source = Assert.Single(sources);
        Assert.Equal(12, source.Count("posted"));
        Assert.Equal(0, source.Count("pending"));
        Assert.False(source.IsConnected);
        Assert.Equal("Shaykh Ahmad", source.Permission!.By);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero), source.NextSyncAt);
    }

    [Fact]
    public async Task Adds_a_source_with_the_recorded_permission()
    {
        string? sent = null;
        var (api, handler) = Client(r =>
        {
            sent = r.Content!.ReadAsStringAsync().Result;
            return Ok("{\"source\":" + SourceJson + "}", HttpStatusCode.Created);
        });
        var source = await api.AddSourceAsync(new NewSource("ch_1", "rss", "https://alummahnow.com/feed", null, "daily", new NewSourcePermission("Shaykh Ahmad", "Letter")));
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/api/admin/import/sources", handler.Requests[0].RequestUri!.AbsolutePath);
        var body = JsonNode.Parse(sent!)!;
        Assert.Equal("ch_1", body["channelId"]!.GetValue<string>());
        Assert.Equal("Letter", body["permission"]!["note"]!.GetValue<string>());
        Assert.Null(body["label"]);
        Assert.Equal("Lessons", source.Label);
    }

    [Fact]
    public async Task Changes_only_the_fields_given()
    {
        string? sent = null;
        var (api, handler) = Client(r =>
        {
            sent = r.Content!.ReadAsStringAsync().Result;
            return Ok("{\"source\":" + SourceJson + "}");
        });
        await api.UpdateSourceAsync("isrc_0123456789abcdef0123456789abcdef", enabled: false);
        Assert.Equal(HttpMethod.Patch, handler.Requests[0].Method);
        Assert.Equal("{\"enabled\":false}", sent);
    }

    [Fact]
    public async Task Turning_on_a_source_whose_connection_is_gone_is_refused_with_its_code()
    {
        var (api, _) = Client(_ => FakeHandler.Json(HttpStatusCode.Conflict, "{\"success\":false,\"code\":\"not_connected\",\"error\":\"The owner's connection to this account is gone\"}"));
        var error = await Assert.ThrowsAsync<ImportApiException>(() => api.UpdateSourceAsync("isrc_0123456789abcdef0123456789abcdef", enabled: true));
        Assert.Equal(409, error.Status);
        Assert.Equal("not_connected", error.Code);
        Assert.False(error.IsRetryable);
    }

    [Fact]
    public async Task An_admin_who_is_not_a_super_admin_is_refused()
    {
        var (api, _) = Client(_ => FakeHandler.Json(HttpStatusCode.Forbidden, "{\"success\":false,\"code\":\"forbidden\",\"error\":\"The import_sources:manage permission is required\"}"));
        var error = await Assert.ThrowsAsync<ImportApiException>(() => api.ListSourcesAsync(null));
        Assert.Equal(403, error.Status);
        Assert.Equal("forbidden", error.Code);
    }

    [Fact]
    public async Task Sync_now_answers_the_queued_job()
    {
        var (api, handler) = Client(_ => Ok("{\"jobId\":\"job_9\",\"status\":\"queued\"}", HttpStatusCode.Accepted));
        var started = await api.SyncSourceAsync("isrc_0123456789abcdef0123456789abcdef");
        Assert.Equal("/api/admin/import/sources/isrc_0123456789abcdef0123456789abcdef/sync", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(new SyncStarted("job_9", "queued"), started);
    }

    [Fact]
    public async Task Reads_a_sync_jobs_log_and_retries_it()
    {
        const string job = "{\"id\":\"job_1\",\"kind\":\"import.source-sync\",\"targetKind\":\"channel\",\"targetId\":\"ch_1\",\"params\":{},\"status\":\"failed\",\"attempts\":6,\"maxAttempts\":6," +
            "\"nextRunAt\":null,\"lastError\":\"fetch_failed: the feed answered 502\",\"result\":null,\"actorId\":\"usr_admin\",\"createdAt\":\"2026-11-22T09:00:00.000Z\",\"updatedAt\":\"2026-11-22T09:30:00.000Z\"," +
            "\"finishedAt\":\"2026-11-22T09:30:00.000Z\",\"log\":[{\"at\":\"2026-11-22T09:00:00.000Z\",\"event\":\"queued\"},{\"at\":\"2026-11-22T09:00:01.000Z\",\"event\":\"started\",\"attempt\":1}," +
            "{\"at\":\"2026-11-22T09:00:02.000Z\",\"event\":\"failed_attempt\",\"attempt\":1,\"error\":\"fetch_failed: the feed answered 502\",\"delaySeconds\":10}]}";
        var (api, handler) = Client(r => r.Method == HttpMethod.Get ? Ok(job) : Ok("{\"id\":\"job_1\",\"status\":\"queued\"}"));
        var read = await api.GetJobAsync("job_1");
        Assert.True(read.CanRetry);
        Assert.Equal(3, read.Log!.Count);
        Assert.Equal(10, read.Log[2].DelaySeconds);
        Assert.Equal("/api/content/jobs/job_1", handler.Requests[0].RequestUri!.AbsolutePath);
        await api.RetryJobAsync("job_1");
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("/api/content/jobs/job_1/retry", handler.Requests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Lists_items_connections_removes_and_names_channels()
    {
        var (api, handler) = Client(r => r.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/items") => Ok("{\"items\":[{\"sourceId\":\"isrc_1\",\"itemId\":\"42\",\"kind\":\"video\",\"state\":\"awaiting_file\",\"note\":\"too_large_for_bot_api\",\"postId\":null,\"videoId\":null,\"originalUrl\":\"https://t.me/c/1/42\",\"originalAt\":\"2026-11-21T08:00:00.000Z\",\"createdAt\":\"2026-11-21T08:00:00.000Z\",\"updatedAt\":\"2026-11-21T08:00:00.000Z\"}]}"),
            var p when p.EndsWith("/connections") => Ok("{\"connections\":[{\"network\":\"telegram\",\"address\":\"-1001234567890\",\"label\":\"Lessons\",\"socialAccountId\":\"sa_1\",\"ytImportId\":null}]}"),
            var p when p.StartsWith("/api/content/channels/") => Ok("{\"id\":\"ch_1\",\"name\":\"Shaykh Ahmad al-Azhari\"}"),
            _ => Ok("{\"deleted\":true}"),
        });
        var item = Assert.Single(await api.ListSourceItemsAsync("isrc_1", "awaiting_file", 900));
        Assert.Equal("/api/admin/import/sources/isrc_1/items?limit=500&state=awaiting_file", handler.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("too_large_for_bot_api", item.Note);
        var connection = Assert.Single(await api.ListConnectionsAsync("ch_1"));
        Assert.Equal("telegram", connection.Network);
        await api.RemoveSourceAsync("isrc_1");
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
        Assert.Equal("Shaykh Ahmad al-Azhari", await api.GetChannelNameAsync("ch_1"));
    }

    [Fact]
    public async Task An_unreadable_answer_is_an_error_not_an_empty_list()
    {
        var (api, _) = Client(_ => Ok("{}"));
        var error = await Assert.ThrowsAsync<ImportApiException>(() => api.ListSourcesAsync(null));
        Assert.Equal("unreadable", error.Code);
    }
}

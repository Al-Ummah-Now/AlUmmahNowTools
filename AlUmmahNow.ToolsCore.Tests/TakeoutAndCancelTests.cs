using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Social;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>Cancel on a source's sync, and the YouTube Takeout match (wiki TOOLS-ADMIN TA-9/TA-13).</summary>
public class SyncCancelTests
{
    private const string SourceId = "isrc_0123456789abcdef0123456789abcdef";
    private const string JobId = "0b6f7c1e-5d2a-4a8e-9f3c-1d2e3f4a5b6c";

    private static (ImportApiClient Api, FakeHandler Handler) Client(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var handler = new FakeHandler { Answer = answer };
        return (new ImportApiClient(new Uri("https://admin.example"), _ => Task.FromResult<string?>("tok"), handler), handler);
    }

    private static HttpResponseMessage Ok(string result) => FakeHandler.Json(HttpStatusCode.OK, "{\"success\":true,\"result\":" + result + "}");

    private const string CancelledJob = "{\"job\":{\"id\":\"" + JobId + "\",\"kind\":\"import.source-sync\",\"status\":\"cancelled\",\"attempts\":1,\"maxAttempts\":6,\"nextRunAt\":null," +
        "\"lastError\":null,\"createdAt\":\"2026-11-22T10:00:00.000Z\",\"finishedAt\":\"2026-11-22T10:01:00.000Z\",\"log\":[{\"at\":\"2026-11-22T10:00:00.000Z\",\"event\":\"queued\"}," +
        "{\"at\":\"2026-11-22T10:01:00.000Z\",\"event\":\"cancelled\",\"by\":{\"id\":\"usr_1\",\"name\":\"Amina Haddad\"}}]}}";

    [Fact]
    public async Task Cancel_posts_the_job_id_and_reads_back_the_cancelled_job_with_who_cancelled_it()
    {
        string? sent = null;
        var (api, handler) = Client(r =>
        {
            sent = r.Content!.ReadAsStringAsync().Result;
            return Ok(CancelledJob);
        });
        var job = await api.CancelSourceJobAsync(SourceId, JobId);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal($"/api/admin/import/sources/{SourceId}/cancel", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(JobId, JsonNode.Parse(sent!)!["jobId"]!.GetValue<string>());
        Assert.True(job.IsCancelled);
        Assert.False(job.CanCancel);
        Assert.True(job.CanRetry, "a cancelled sync can start again");
        Assert.Equal("Amina Haddad", job.CancelledBy);
    }

    [Theory]
    [InlineData("queued", true, false)]
    [InlineData("running", true, false)]
    [InlineData("retrying", true, false)]
    [InlineData("failed", false, true)]
    [InlineData("cancelled", false, true)]
    [InlineData("succeeded", false, false)]
    public void Cancel_applies_to_a_waiting_or_running_job_and_Retry_to_a_failed_or_cancelled_one(string status, bool cancel, bool retry)
    {
        var job = new SyncJob(JobId, "import.source-sync", status, 1, 6, null, null, null, null, null);
        Assert.Equal(cancel, job.CanCancel);
        Assert.Equal(retry, job.CanRetry);
    }

    [Fact]
    public void A_cancelled_sync_is_its_own_phase_not_a_failure()
    {
        var source = JsonSerializer.Deserialize<ImportSource>("{\"id\":\"" + SourceId + "\",\"channelId\":\"ch_1\",\"network\":\"rss\",\"address\":\"https://alummahnow.com/feed\",\"label\":\"L\",\"auth\":\"recorded\"," +
            "\"schedule\":\"daily\",\"enabled\":true,\"lastJobId\":\"" + JobId + "\",\"lastError\":null,\"counts\":{}}", ImportApiClient.Json)!;
        var job = new SyncJob(JobId, "import.source-sync", "cancelled", 1, 6, null, null, null, null, null);
        Assert.Equal(SourcePhase.Cancelled, SourceText.PhaseOf(source, job));
    }

    [Fact]
    public async Task Retrying_a_failed_job_when_uploads_are_off_reports_feature_off()
    {
        var (api, _) = Client(_ => FakeHandler.Json(HttpStatusCode.NotFound, "{\"success\":false,\"code\":\"feature_off\",\"feature\":\"upload\",\"error\":\"Not Found\",\"message\":\"Uploads are off.\",\"lang\":\"en\"}"));
        var error = await Assert.ThrowsAsync<ImportApiException>(() => api.RetryJobAsync(JobId));
        Assert.True(error.IsFeatureOff);
        Assert.Equal(404, error.Status);
        Assert.Equal("feature_off", error.Code);
    }

    [Fact]
    public async Task Cancelling_another_sources_job_is_refused_with_its_code()
    {
        var (api, _) = Client(_ => FakeHandler.Json(HttpStatusCode.NotFound, "{\"success\":false,\"code\":\"job_not_found\",\"error\":\"No such job for this source\"}"));
        var error = await Assert.ThrowsAsync<ImportApiException>(() => api.CancelSourceJobAsync(SourceId, JobId));
        Assert.Equal(404, error.Status);
        Assert.Equal("job_not_found", error.Code);
    }
}

public class TakeoutTests
{
    private const string SourceId = "isrc_0123456789abcdef0123456789abcdef";
    private const string FileId = "tkf_0123456789abcdef0123456789abcdef";

    private static (ImportApiClient Api, FakeHandler Handler) Client(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var handler = new FakeHandler { Answer = answer };
        return (new ImportApiClient(new Uri("https://admin.example"), _ => Task.FromResult<string?>("tok"), handler), handler);
    }

    private static HttpResponseMessage Ok(string result, HttpStatusCode status = HttpStatusCode.OK) => FakeHandler.Json(status, "{\"success\":true,\"result\":" + result + "}");

    private const string FileJson = "{\"id\":\"" + FileId + "\",\"sourceId\":\"" + SourceId + "\",\"name\":\"Q&A.mp4\",\"size\":4000,\"videoId\":null,\"title\":\"Q&A\",\"durationMs\":null," +
        "\"state\":\"ambiguous\",\"matchedBy\":null,\"note\":\"several_records\",\"itemId\":null,\"item\":null,\"candidates\":[{\"itemId\":\"CCCCCCCCCCC\",\"title\":\"Q&A\",\"originalUrl\":\"https://www.youtube.com/watch?v=CCCCCCCCCCC\",\"originalAt\":\"2026-11-01T10:00:00.000Z\"}," +
        "{\"itemId\":\"DDDDDDDDDDD\",\"title\":\"Q&A\",\"originalUrl\":null,\"originalAt\":null}],\"uploadVideoId\":null,\"createdAt\":\"2026-11-22T10:00:00.000Z\",\"updatedAt\":\"2026-11-22T10:00:00.000Z\"}";

    [Fact]
    public void Reads_the_exports_videos_csv_with_quoted_titles_and_durations()
    {
        var csv = "﻿Video ID,Approx Duration (ms),Video Audio Language,Video Title (Original),Privacy\n" +
                  "AAAAAAAAAAA,3512000,ar,\"تفسير سورة الكهف, المجلس الأول\",Public\r\n" +
                  "BBBBBBBBBBB,,en,\"Q&A \"\"live\"\"\",Unlisted\n" +
                  ",100,en,No id,Public\n";
        var rows = TakeoutExport.ParseCsv(csv);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new TakeoutExport.Row("AAAAAAAAAAA", "تفسير سورة الكهف, المجلس الأول", 3512000), rows[0]);
        Assert.Equal(new TakeoutExport.Row("BBBBBBBBBBB", "Q&A \"live\"", null), rows[1]);
        Assert.Empty(TakeoutExport.ParseCsv("Title,Comment\nx,y\n"));
    }

    [Fact]
    public void Gives_a_file_its_rows_id_only_when_one_row_has_its_title()
    {
        var rows = new[]
        {
            new TakeoutExport.Row("AAAAAAAAAAA", "تفسير سورة الكهف: المجلس الأول", 3_512_000),
            new TakeoutExport.Row("CCCCCCCCCCC", "Q&A", 600_000),
            new TakeoutExport.Row("DDDDDDDDDDD", "Q&A", 900_000),
        };
        var files = TakeoutExport.Build(new[]
        {
            ("/t/a/تفسير سورة الكهف المجلس الأول.mp4", "تفسير سورة الكهف المجلس الأول.mp4", 10L),
            ("/t/b/Q&A.mp4", "Q&A.mp4", 20L),
            ("/t/c/Holiday.MOV", "Holiday.MOV", 30L),
            ("/t/copy/Holiday.MOV", "Holiday.MOV", 30L),
        }, rows);
        Assert.Equal(3, files.Count);
        var tafsir = files.Single(f => f.Size == 10);
        Assert.Equal("AAAAAAAAAAA", tafsir.VideoId);
        Assert.Equal(3_512_000, tafsir.DurationMs);
        Assert.Equal("/t/a/تفسير سورة الكهف المجلس الأول.mp4", tafsir.Path);
        var qa = files.Single(f => f.Size == 20);
        Assert.Null(qa.VideoId);
        Assert.Equal("Q&A", qa.Title);
        var holiday = files.Single(f => f.Size == 30);
        Assert.Equal("video/quicktime", holiday.MimeType);
        Assert.Equal("Holiday", holiday.Title);
    }

    [Fact]
    public void The_path_is_never_sent()
    {
        var file = new TakeoutFileInput("a.mp4", 1, null, "a", null, "video/mp4") { Path = "/Users/someone/Takeout/a.mp4" };
        var json = JsonSerializer.Serialize(file, ImportApiClient.Json);
        Assert.DoesNotContain("Users", json);
        Assert.DoesNotContain("videoId", json);
    }

    [Fact]
    public async Task Lists_files_reads_the_report_and_confirms_a_choice()
    {
        var bodies = new List<string?>();
        var (api, handler) = Client(r =>
        {
            bodies.Add(r.Content?.ReadAsStringAsync().Result);
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("/takeout/files")) return Ok("{\"listed\":1,\"added\":1,\"jobId\":\"job_t\",\"status\":\"queued\"}", HttpStatusCode.Accepted);
            if (path.EndsWith("/takeout")) return Ok("{\"files\":[" + FileJson + "],\"counts\":{\"ambiguous\":1},\"jobId\":\"job_t\"}");
            return Ok("{\"file\":" + FileJson.Replace("\"state\":\"ambiguous\"", "\"state\":\"matched\"") + "}");
        });
        var listed = await api.ListTakeoutFilesAsync(SourceId, new[] { new TakeoutFileInput("Q&A.mp4", 4000, null, "Q&A", null, "video/mp4") });
        Assert.Equal("job_t", listed.JobId);
        Assert.Equal("Q&A.mp4", JsonNode.Parse(bodies[0]!)!["files"]![0]!["name"]!.GetValue<string>());

        var report = await api.GetTakeoutAsync(SourceId);
        Assert.Equal(1, report.Count("ambiguous"));
        var file = Assert.Single(report.Files);
        Assert.Equal(2, file.Candidates!.Count);
        Assert.False(file.CanUpload);

        var confirmed = await api.ConfirmTakeoutFileAsync(SourceId, FileId, "DDDDDDDDDDD");
        Assert.True(confirmed.CanUpload);
        Assert.Equal("DDDDDDDDDDD", JsonNode.Parse(bodies[2]!)!["itemId"]!.GetValue<string>());
        Assert.Equal(new[]
        {
            $"POST /api/admin/import/sources/{SourceId}/takeout/files",
            $"GET /api/admin/import/sources/{SourceId}/takeout?limit=500",
            $"POST /api/admin/import/sources/{SourceId}/takeout/files/{FileId}/confirm",
        }, handler.Requests.Select(r => $"{r.Method} {r.RequestUri!.PathAndQuery}"));
    }

    [Fact]
    public async Task Uploads_a_matched_file_through_its_records_upload_then_says_it_is_uploaded()
    {
        var path = Path.Combine(Path.GetTempPath(), "takeout-" + Guid.NewGuid().ToString("N") + ".mp4");
        await File.WriteAllBytesAsync(path, new byte[4000]);
        try
        {
            var matched = FileJson.Replace("\"state\":\"ambiguous\"", "\"state\":\"matched\"");
            var (api, handler) = Client(r =>
            {
                var p = r.RequestUri!.AbsolutePath;
                if (p.EndsWith("/upload")) return Ok("{\"file\":" + matched + ",\"upload\":{\"videoId\":\"vid_1\",\"tusSessionId\":\"tus_1\",\"tusUploadId\":null,\"chunkSize\":1500,\"resumable\":true,\"reused\":false}}");
                return Ok("{\"file\":" + matched.Replace("\"state\":\"matched\"", "\"state\":\"uploaded\"") + "}", HttpStatusCode.Accepted);
            });
            var tus = new FakeTus();
            var uploader = new TakeoutUploader(api, tus, new Uri("https://videos.example"), _ => Task.FromResult<string?>("tok"));
            var file = JsonSerializer.Deserialize<TakeoutFile>(matched, ImportApiClient.Json)!;
            var done = await uploader.UploadAsync(SourceId, file, path, null, CancellationToken.None);
            Assert.Equal("uploaded", done.State);
            Assert.Equal(new long[] { 1500, 3000, 4000 }, tus.Offsets);
            Assert.Equal("tus_1", tus.Metadata!["tusSessionId"]);
            Assert.Equal("vid_1", tus.Metadata["videoId"]);
            Assert.EndsWith("/uploaded", handler.Requests[^1].RequestUri!.AbsolutePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_file_that_changed_since_it_was_listed_is_not_sent()
    {
        var path = Path.Combine(Path.GetTempPath(), "takeout-" + Guid.NewGuid().ToString("N") + ".mp4");
        await File.WriteAllBytesAsync(path, new byte[10]);
        try
        {
            var (api, handler) = Client(_ => throw new InvalidOperationException("no call expected"));
            var uploader = new TakeoutUploader(api, new FakeTus(), new Uri("https://videos.example"), _ => Task.FromResult<string?>("tok"));
            var file = JsonSerializer.Deserialize<TakeoutFile>(FileJson.Replace("\"state\":\"ambiguous\"", "\"state\":\"matched\""), ImportApiClient.Json)!;
            var error = await Assert.ThrowsAsync<TakeoutUploadException>(() => uploader.UploadAsync(SourceId, file, path, null, CancellationToken.None));
            Assert.Equal("file_changed", error.Code);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task An_expired_upload_is_opened_again_once_then_named_upload_gone()
    {
        var path = Path.Combine(Path.GetTempPath(), "takeout-" + Guid.NewGuid().ToString("N") + ".mp4");
        await File.WriteAllBytesAsync(path, new byte[4000]);
        try
        {
            var matched = FileJson.Replace("\"state\":\"ambiguous\"", "\"state\":\"matched\"");
            var opens = 0;
            var (api, _) = Client(r =>
            {
                if (r.RequestUri!.AbsolutePath.EndsWith("/upload"))
                {
                    opens++;
                    return Ok("{\"file\":" + matched + ",\"upload\":{\"videoId\":\"vid_1\",\"tusSessionId\":\"tus_" + opens + "\",\"tusUploadId\":\"up_old\",\"chunkSize\":4000,\"resumable\":true,\"reused\":true}}");
                }
                return Ok("{\"file\":" + matched.Replace("\"state\":\"matched\"", "\"state\":\"uploaded\"") + "}", HttpStatusCode.Accepted);
            });
            // The stored upload is gone: the app opens the record's upload again and starts a new one
            var tus = new GoneTus(goneTimes: 1);
            var uploader = new TakeoutUploader(api, tus, new Uri("https://videos.example"), _ => Task.FromResult<string?>("tok"));
            var file = JsonSerializer.Deserialize<TakeoutFile>(matched, ImportApiClient.Json)!;
            var done = await uploader.UploadAsync(SourceId, file, path, null, CancellationToken.None);
            Assert.Equal("uploaded", done.State);
            Assert.Equal(2, opens);
            Assert.Equal("tus_2", tus.Metadata!["tusSessionId"]);

            // Gone again after the new upload: a code the app says in the admin's language, never a crash
            opens = 0;
            var again = new TakeoutUploader(api, new GoneTus(goneTimes: 5), new Uri("https://videos.example"), _ => Task.FromResult<string?>("tok"));
            var error = await Assert.ThrowsAsync<TakeoutUploadException>(() => again.UploadAsync(SourceId, file, path, null, CancellationToken.None));
            Assert.Equal("upload_gone", error.Code);
            Assert.Equal(2, opens);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Sends_the_apps_language_and_reads_the_servers_sentence_and_feature_off()
    {
        var (api, handler) = Client(r => r.RequestUri!.AbsolutePath == "/api/features"
            ? Ok("{\"items\":[{\"key\":\"live\",\"effective\":true},{\"key\":\"upload\",\"enabled\":true,\"effective\":false}]}")
            : FakeHandler.Json(HttpStatusCode.NotFound, "{\"success\":false,\"code\":\"feature_off\",\"feature\":\"upload\",\"error\":\"Not Found\",\"message\":\"هذه الميزة غير متاحة الآن.\",\"lang\":\"ar\",\"action\":\"none\"}"));
        api.AppLocale = () => "ar";
        var error = await Assert.ThrowsAsync<ImportApiException>(() => api.SyncSourceAsync(SourceId));
        Assert.True(error.IsFeatureOff);
        Assert.Equal("هذه الميزة غير متاحة الآن.", error.ServerMessage);
        Assert.Equal("ar", handler.Requests[^1].Headers.GetValues("X-App-Locale").Single());
        Assert.False(await api.IsFeatureOnAsync("upload"));
        Assert.True(await api.IsFeatureOnAsync("live"));
        Assert.Null(await api.IsFeatureOnAsync("unknown"));

        // Without the Features page's permission the app doesn't know, and the server's 404 feature_off decides
        var (refused, _) = Client(_ => FakeHandler.Json(HttpStatusCode.Forbidden, "{\"success\":false,\"code\":\"forbidden\",\"error\":\"Missing permission features:read\"}"));
        Assert.Null(await refused.IsFeatureOnAsync("upload"));
    }

    /// <summary>A TUS server whose stored upload is gone (404) for the first <c>goneTimes</c> offset reads.</summary>
    private sealed class GoneTus : TusClient
    {
        public IReadOnlyDictionary<string, string>? Metadata;
        private int _gone;
        private long _offset;

        public GoneTus(int goneTimes) => _gone = goneTimes;

        public override Task<Uri> CreateAsync(Uri endpoint, string token, long length, IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
        {
            Metadata = metadata;
            return Task.FromResult(new Uri(endpoint, "/api/videos/tus/up_new"));
        }

        public override Task<long> OffsetAsync(Uri upload, string token, CancellationToken ct)
        {
            if (_gone-- > 0) throw new TusGoneException("The upload is gone (HEAD 404)");
            return Task.FromResult(_offset);
        }

        public override Task<long> PatchAsync(Uri upload, string token, Stream file, long offset, int count, CancellationToken ct)
        {
            _offset = offset + count;
            return Task.FromResult(_offset);
        }
    }

    private sealed class FakeTus : TusClient
    {
        public readonly List<long> Offsets = new();
        public IReadOnlyDictionary<string, string>? Metadata;
        private long _offset;

        public override Task<Uri> CreateAsync(Uri endpoint, string token, long length, IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
        {
            Metadata = metadata;
            return Task.FromResult(new Uri(endpoint, "/api/videos/tus/up_1"));
        }

        public override Task<long> OffsetAsync(Uri upload, string token, CancellationToken ct) => Task.FromResult(_offset);

        public override Task<long> PatchAsync(Uri upload, string token, Stream file, long offset, int count, CancellationToken ct)
        {
            _offset = offset + count;
            Offsets.Add(_offset);
            return Task.FromResult(_offset);
        }
    }
}

/// <summary>The reasons api-videos stores (<c>"&lt;code&gt;: &lt;detail&gt;"</c>) and how the app explains them.</summary>
public class ReasonTests
{
    [Theory]
    [InlineData("source_unreachable: The source could not be read (http_503)", "source_unreachable", 503, null)]
    [InlineData("source_unreachable: The source could not be read (no_answer)", "source_unreachable", null, null)]
    [InlineData("source_not_found: The source was not found (http_404)", "source_not_found", 404, null)]
    [InlineData("source_forbidden: The source refused access (http_403)", "source_forbidden", 403, null)]
    [InlineData("source_bad_response: The source gave an answer the sync can't use (too_large)", "source_bad_response", null, null)]
    [InlineData("source_not_a_feed: The address is not an RSS or Atom feed", "source_not_a_feed", null, null)]
    [InlineData("not_connected: the owner's connection to this account was removed", "not_connected", null, null)]
    [InlineData("channel_unavailable: The channel is deleted or suspended", "channel_unavailable", null, null)]
    [InlineData("post_refused: The upload was refused", "post_refused", null, null)]
    [InlineData("upload_incomplete: the video v1 of a.mp4 has not finished uploading", "upload_incomplete", null, null)]
    [InlineData("sync_failed: The image could not be stored", "sync_failed", null, "The image could not be stored")]
    [InlineData("takeout_failed: D1 is busy", "takeout_failed", null, "D1 is busy")]
    public void A_coded_reason_gives_its_code_its_status_and_only_an_unexpected_failures_words(string stored, string code, int? status, string? detail)
    {
        var reason = SourceText.Explain(stored);
        Assert.Equal(code, reason.Code);
        Assert.Equal(status, reason.HttpStatus);
        Assert.Equal(detail, reason.Detail);
    }

    [Theory]
    [InlineData("The source could not be read (http_503)", "The source could not be read (http_503)")]
    [InlineData("weird_code: something new", "something new")]
    public void An_uncoded_or_unknown_reason_is_shown_as_the_servers_words(string stored, string detail)
    {
        var reason = SourceText.Explain(stored);
        Assert.Null(reason.Code);
        Assert.Null(reason.HttpStatus);
        Assert.Equal(detail, reason.Detail);
    }

    [Fact]
    public void Every_reason_code_is_worded_in_English_and_in_Arabic_without_Latin_letters()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "AlUmmahNow.VisualImporterUno"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var strings = Path.Combine(dir!.FullName, "AlUmmahNow.VisualImporterUno", "AlUmmahNow.VisualImporterUno", "Strings");
        foreach (var lang in new[] { "en", "ar" })
        {
            var resw = System.Xml.Linq.XDocument.Load(Path.Combine(strings, lang, "Resources.resw"));
            var values = resw.Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
            foreach (var key in SourceText.ReasonCodes.Select(c => "SocialReason_" + c).Append("SocialReason_HttpStatus"))
            {
                Assert.True(values.TryGetValue(key, out var text) && text.Length > 0, $"{lang}: {key} is missing");
                if (lang == "ar") Assert.DoesNotMatch("[A-Za-z]", text);
            }
        }
    }

    [Fact]
    public void A_server_without_social_sources_is_worded_in_English_and_in_Arabic_without_Latin_letters()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "AlUmmahNow.VisualImporterUno"))) dir = dir.Parent;
        Assert.NotNull(dir);
        foreach (var lang in new[] { "en", "ar" })
        {
            var resw = System.Xml.Linq.XDocument.Load(Path.Combine(dir!.FullName, "AlUmmahNow.VisualImporterUno", "AlUmmahNow.VisualImporterUno", "Strings", lang, "Resources.resw"));
            var values = resw.Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
            foreach (var key in new[] { "Social_NotOnServerTitle.Text", "Social_NotOnServerBody.Text", "SocialError_" + ImportApiException.RouteMissing })
            {
                Assert.True(values.TryGetValue(key, out var text) && text.Length > 0, $"{lang}: {key} is missing");
                if (lang == "ar") Assert.DoesNotMatch("[A-Za-z]", text);
            }
            // A 404 that is not the missing route keeps its own words: no text of the app's stands in for it
            Assert.DoesNotContain("SocialError_http_404", values.Keys);
        }
    }

    [Fact]
    public void Quiet_uploads_off_strings_exist_in_English_and_Arabic_without_naming_hidden_actions()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "AlUmmahNow.VisualImporterUno"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var strings = Path.Combine(dir!.FullName, "AlUmmahNow.VisualImporterUno", "AlUmmahNow.VisualImporterUno", "Strings");
        var quietKeys = new[]
        {
            "Social_StatusFailedQuiet",
            "Social_StatusCancelledQuiet",
            "SocialJob_failedQuiet",
            "SocialJob_cancelledQuiet",
            "SocialJob_cancelledByQuiet",
            "SocialLog_queued_retriedQuiet",
            "Social_AddedQuiet",
            "SocialReason_upload_incompleteQuiet",
            "TakeoutState_matchedQuiet",
            "TakeoutNote_upload_incompleteQuiet",
            "TakeoutJob_failedQuiet",
        };
        foreach (var lang in new[] { "en", "ar" })
        {
            var resw = System.Xml.Linq.XDocument.Load(Path.Combine(strings, lang, "Resources.resw"));
            var values = resw.Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
            foreach (var key in quietKeys)
            {
                Assert.True(values.TryGetValue(key, out var text) && text.Length > 0, $"{lang}: {key} is missing");
                if (lang == "en")
                {
                    Assert.DoesNotContain("Retry", text, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Sync now", text, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("ready to upload", text, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Upload it again", text, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Sync it now", text, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    Assert.DoesNotMatch("[A-Za-z]", text);
                    Assert.DoesNotContain("أعِد المحاولة", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("زامِن الآن", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("زامِنه الآن", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("جاهز للرفع", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("ارفعه مرة أخرى", text, StringComparison.Ordinal);
                }
            }
        }
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

public class DiagnosticLogTests
{
    private static QueueItem SampleItem(params LogLine[] log)
    {
        var video = new ScannedVideo(
            Path: @"O:\Originals\Lesson 01.mp4",
            Size: 2_500_000_000,
            LastModifiedMs: new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            MimeType: "video/mp4",
            Captions: [],
            ChaptersPath: null,
            ThumbnailPath: null,
            MetadataPath: null,
            Metadata: new ImportMetadata { FallbackTitle = "Lesson 01" },
            Notes: []);
        var item = QueueItem.For(video, "batch_1", "ch_1", new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        item.ServerItemId = "imp_123";
        item.VideoId = "vid_456";
        item.Log.AddRange(log);
        return item;
    }

    [Fact]
    public void Build_strips_local_paths_unc_shares_emails_tokens_and_query_secrets()
    {
        var at = new DateTimeOffset(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);
        var item = SampleItem(
            new LogLine(at, LogLevel.Info, @"Reading O:\Originals\Lesson 01.mp4 via \\Home-NAS\Originals\Lesson 01.mp4", "share_path"),
            new LogLine(at.AddSeconds(2), LogLevel.Info, "Relative file ./private/secret.mp4 and ../uploads/clip.mkv and دروس/clip.mp4", "rel_path"),
            new LogLine(at.AddSeconds(5), LogLevel.Warning, "Auth failed for admin@example.com with Bearer secret_token_1234567890 X-API-Key: my_synthetic_key_999 Authorization: Basic dXNlcjpwYXNz", "auth"),
            new LogLine(at.AddSeconds(6), LogLevel.Warning, "Authorization: Digest username=\"u\", realm=\"r\", uri=\"https://api.alummahnow.net/x\", response=\"deadbeefdigest999\"", "digest"),
            new LogLine(at.AddSeconds(10), LogLevel.Warning, "GET https://api.alummahnow.net/upload?token=abc123&X-Amz-Signature=deadbeef&sig=cafebabe failed with 503", "upload"),
            new LogLine(at.AddSeconds(15), LogLevel.Error, "Windows error 59 at byte 2448424960: An unexpected network error occurred", "read_error"));

        var upload = DiagnosticLog.Build(item, "2026.11.26.2", "ERR-ABCD-1234");

        Assert.Equal("ERR-ABCD-1234", upload.Ref);
        Assert.Equal("2026.11.26.2", upload.Release);
        Assert.Equal(6, upload.EntryCount);
        Assert.DoesNotContain("\r", upload.Content);
        Assert.Contains("app=tools release=2026.11.26.2", upload.Content);
        Assert.Contains("itemId=imp_123 videoId=vid_456", upload.Content);
        Assert.Contains("Windows error 59 at byte 2448424960", upload.Content);
        Assert.DoesNotContain("Home-NAS", upload.Content);
        Assert.DoesNotContain(@"O:\Originals", upload.Content);
        Assert.DoesNotContain("private/secret.mp4", upload.Content);
        Assert.DoesNotContain("uploads/clip.mkv", upload.Content);
        Assert.DoesNotContain("دروس/clip.mp4", upload.Content);
        Assert.DoesNotContain("admin@example.com", upload.Content);
        Assert.DoesNotContain("secret_token_1234567890", upload.Content);
        Assert.DoesNotContain("my_synthetic_key_999", upload.Content);
        Assert.DoesNotContain("dXNlcjpwYXNz", upload.Content);
        Assert.DoesNotContain("deadbeefdigest999", upload.Content);
        Assert.DoesNotContain("abc123", upload.Content);
        Assert.DoesNotContain("deadbeef", upload.Content);
        Assert.DoesNotContain("cafebabe", upload.Content);
    }

    [Fact]
    public void Build_truncates_oldest_entries_and_oversized_header_strictly_within_max_bytes()
    {
        var at = new DateTimeOffset(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);
        var chunk = new string('x', 520);
        var lines = Enumerable.Range(0, 500)
            .Select(i => new LogLine(at.AddSeconds(i), LogLevel.Info, $"entry-{i:D4} {chunk}", "step"))
            .ToArray();
        var item = SampleItem(lines);
        item.ServerItemId = new string('z', 10_000);

        var upload = DiagnosticLog.Build(item, new string('r', 5_000));

        Assert.DoesNotContain("\r", upload.Content);
        Assert.True(Encoding.UTF8.GetByteCount(upload.Content) <= DiagnosticLog.MaxBytes);
        Assert.True(upload.EntryCount < 500);
        Assert.Contains("# truncated:", upload.Content);
        Assert.Contains("entry-0499", upload.Content);
        Assert.DoesNotContain("entry-0000", upload.Content);
    }

    [Fact]
    public void ToolsPreferences_defaults_SendDiagnosticLogs_to_false_and_persists_opt_in()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aun-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "preferences.json");
            Assert.False(ToolsPreferences.Load(path).SendDiagnosticLogs);

            var prefs = new ToolsPreferences { SendDiagnosticLogs = true };
            prefs.Save(path);
            Assert.True(ToolsPreferences.Load(path).SendDiagnosticLogs);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ServerFailureSink_uploads_diagnostic_log_only_when_opted_in()
    {
        var requests = new List<(string Path, string Body)>();
        var handler = new StubHandler(async req =>
        {
            var body = req.Content is not null ? await req.Content.ReadAsStringAsync() : "";
            requests.Add((req.RequestUri!.AbsolutePath, body));
            if (req.RequestUri.AbsolutePath == "/api/admin/import/diagnostics")
                return JsonReply("""{"success":true,"result":{"ref":"ERR-TEST-9999","sizeBytes":100,"storedBytes":80,"entryCount":1}}""");
            return JsonReply("""{"success":true,"result":{}}""");
        });
        var api = new ImportApiClient(new Uri("https://admin.api.alummahnow.net"), _ => Task.FromResult<string?>("tok"), handler);
        using var reporter = new ErrorReporter(api.ReportErrorsAsync, "2026.11.26.2", () => "en", Timeout.InfiniteTimeSpan);
        var item = SampleItem(new LogLine(DateTimeOffset.UtcNow, LogLevel.Error, "Network error 59", "read_error"));
        var step = new StepState { Kind = ImportStepKind.Upload, Attempts = 3 };

        // Default: opt-in OFF -> no diagnostic upload
        var sinkOff = new ServerFailureSink(api, reporter, () => false, "2026.11.26.2");
        await sinkOff.ItemFailedAsync(item, step, new IOException("Network dropped"), "ERR-TEST-0001", CancellationToken.None);
        Assert.DoesNotContain(requests, r => r.Path == "/api/admin/import/diagnostics");

        // Opt-in ON -> uploads diagnostic log linked to the exact ref
        requests.Clear();
        var sinkOn = new ServerFailureSink(api, reporter, () => true, "2026.11.26.2");
        await sinkOn.ItemFailedAsync(item, step, new IOException("Network dropped"), "ERR-TEST-9999", CancellationToken.None);
        var diagReq = Assert.Single(requests, r => r.Path == "/api/admin/import/diagnostics");
        using var doc = JsonDocument.Parse(diagReq.Body);
        Assert.Equal("ERR-TEST-9999", doc.RootElement.GetProperty("ref").GetString());
        Assert.Contains("Network error 59", doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ServerFailureSink_swallows_diagnostic_upload_errors()
    {
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/admin/import/diagnostics")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(JsonReply("""{"success":true,"result":{}}"""));
        });
        var api = new ImportApiClient(new Uri("https://admin.api.alummahnow.net"), _ => Task.FromResult<string?>("tok"), handler);
        using var reporter = new ErrorReporter(api.ReportErrorsAsync, "2026.11.26.2", () => "en", Timeout.InfiniteTimeSpan);
        var item = SampleItem(new LogLine(DateTimeOffset.UtcNow, LogLevel.Error, "Network error 59", "read_error"));
        var step = new StepState { Kind = ImportStepKind.Upload, Attempts = 2 };

        var sink = new ServerFailureSink(api, reporter, () => true, "2026.11.26.2");
        await sink.ItemFailedAsync(item, step, new IOException("Network dropped"), "ERR-TEST-4040", CancellationToken.None);
    }

    private static HttpResponseMessage JsonReply(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}

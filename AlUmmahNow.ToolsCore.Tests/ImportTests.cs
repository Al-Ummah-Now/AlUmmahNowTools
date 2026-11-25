using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

internal static class Temp
{
    public static string Folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aun-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Folder(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    public static string Write(string dir, string name, string text)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    public static string Sha(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static string Bytes(string dir, string name, int length, int seed = 1)
    {
        var path = Path.Combine(dir, name);
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    public static async Task WaitUntil(Func<bool> condition, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("The condition was not met in time");
            await Task.Delay(10);
        }
    }
}

public class FolderScannerTests
{
    [Fact]
    public void Pairs_each_video_with_its_sidecars_and_merges_csv_then_json_metadata()
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "lesson-01.mp4", 100);
        Temp.Write(dir, "lesson-01.ar.srt", "1\n00:00:00,000 --> 00:00:01,000\nالسلام عليكم\n");
        Temp.Write(dir, "lesson-01.EN.vtt", "WEBVTT\n");
        Temp.Write(dir, "lesson-01.chapters.json", "[{\"start\":0,\"title\":\"Intro\"}]");
        Temp.Bytes(dir, "lesson-01.jpg", 10);
        Temp.Write(dir, "lesson-01.json", "{\"title\":{\"en\":\"Lesson one\",\"ar\":\"الدرس الأول\"},\"tags\":[\"fiqh\",\"Fiqh\"],\"visibility\":\"Unlisted\"}");
        Temp.Bytes(dir, "talk.mp4", 50);
        Temp.Bytes(dir, "talk.part2.mov", 50);
        Temp.Write(dir, "talk.part2.en.srt", "1\n");
        Temp.Write(dir, "notes.txt", "not a sidecar");
        Temp.Write(dir, "metadata.csv",
            "File,Title,Title_AR,Tags,Visibility,Publish_At,Playlist\n" +
            "lesson-01.mp4,From CSV,,csv-tag,private,,\n" +
            "talk,\"Talk, part one\",محاضرة,\"عقيدة، توحيد\",public,2026-12-01T09:00:00Z,pl_1\n");

        var result = FolderScanner.Scan(dir, includeSubfolders: false, batchLanguage: null);

        Assert.Empty(result.Notes);
        Assert.Equal(new[] { "lesson-01.mp4", "talk.mp4", "talk.part2.mov" }, result.Videos.Select(v => v.FileName));
        var lesson = result.Videos[0];
        Assert.Equal(new[] { "ar", "en" }, lesson.Captions.Select(c => c.Language).OrderBy(l => l));
        Assert.EndsWith("lesson-01.chapters.json", lesson.ChaptersPath);
        Assert.EndsWith("lesson-01.jpg", lesson.ThumbnailPath);
        Assert.Equal("Lesson one", lesson.Metadata.Title);
        Assert.Equal("الدرس الأول", lesson.Metadata.TitleAr);
        Assert.Equal(new[] { "fiqh" }, lesson.Metadata.Tags);
        // The per-video JSON wins over the folder's CSV
        Assert.Equal("unlisted", lesson.Metadata.Visibility);
        Assert.Equal("video/mp4", lesson.MimeType);

        var talk = result.Videos[1];
        Assert.Empty(talk.Captions);
        Assert.Equal("Talk, part one", talk.Metadata.Title);
        Assert.Equal("محاضرة", talk.Metadata.TitleAr);
        Assert.Equal(new[] { "عقيدة", "توحيد" }, talk.Metadata.Tags);
        Assert.Equal("2026-12-01T09:00:00.000Z", talk.Metadata.PublishAt);
        Assert.Equal("pl_1", talk.Metadata.PlaylistId);

        // "talk.part2.en.srt" belongs to "talk.part2.mov", the longer name
        var part2 = result.Videos[2];
        Assert.Equal("en", Assert.Single(part2.Captions).Language);
        Assert.Equal("talk.part2", part2.Metadata.Title);
        Assert.Equal("video/quicktime", part2.MimeType);
    }

    [Fact]
    public void A_caption_without_a_language_takes_the_videos_then_the_batchs_and_otherwise_is_noted()
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "a.mp4", 10);
        Temp.Write(dir, "a.srt", "1\n");
        Temp.Write(dir, "a.json", "{\"language\":\"ar\"}");
        Temp.Bytes(dir, "b.mp4", 10);
        Temp.Write(dir, "b.vtt", "WEBVTT\n");
        Temp.Write(dir, "b.en.vtt", "WEBVTT\n");

        var withBatch = FolderScanner.Scan(dir, false, batchLanguage: "en");
        Assert.Equal("ar", Assert.Single(withBatch.Videos[0].Captions).Language);
        var b = withBatch.Videos[1];
        Assert.Equal("en", Assert.Single(b.Captions).Language);
        Assert.Equal(ScanNoteCode.CaptionLanguageTwice, Assert.Single(b.Notes).Code);

        var without = FolderScanner.Scan(dir, false, batchLanguage: null);
        Assert.Contains(without.Videos[1].Notes, n => n.Code == ScanNoteCode.CaptionLanguageUnknown && n.Detail == "b.vtt");
    }

    [Fact]
    public void Subfolders_are_scanned_with_their_own_csv_and_unreadable_files_are_noted_not_thrown()
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "top.mp4", 10);
        Temp.Write(dir, "top.json", "{ not json");
        var sub = Path.Combine(dir, "Series 2");
        Directory.CreateDirectory(sub);
        Temp.Bytes(sub, "ep1.mp4", 10);
        Temp.Write(sub, "list.csv", "name,title\nep1,No file column\n");

        Assert.Single(FolderScanner.Scan(dir, includeSubfolders: false, null).Videos);
        var all = FolderScanner.Scan(dir, includeSubfolders: true, null);
        Assert.Equal(2, all.Videos.Count);
        Assert.Equal(ScanNoteCode.MetadataUnreadable, Assert.Single(all.Videos[0].Notes).Code);
        Assert.Equal(ScanNoteCode.CsvUnreadable, Assert.Single(all.Notes).Code);
        Assert.Equal("ep1", all.Videos[1].Metadata.Title);
    }
}

public class FolderScannerLinkTests
{
    [Fact]
    public void Links_are_not_followed_as_videos_sidecars_or_folders()
    {
        var outside = Temp.Folder();
        var secret = Temp.Write(outside, "secret.txt", "not for upload");
        var dir = Temp.Folder();
        Temp.Bytes(dir, "v.mp4", 10);
        File.CreateSymbolicLink(Path.Combine(dir, "v.en.srt"), secret);
        File.CreateSymbolicLink(Path.Combine(dir, "w.mp4"), Path.Combine(dir, "v.mp4"));
        Directory.CreateSymbolicLink(Path.Combine(dir, "loop"), dir);

        var result = FolderScanner.Scan(dir, includeSubfolders: true, null);
        var video = Assert.Single(result.Videos);
        Assert.Empty(video.Captions);
    }
}

public class MetadataFileTests
{
    [Fact]
    public void Csv_reads_quotes_doubled_quotes_and_line_breaks_inside_a_field()
    {
        var csv = MetadataCsv.Parse("﻿file,description\r\n\"a.mp4\",\"Line one,\nline \"\"two\"\"\"\r\nb.mp4,plain\r\n");
        Assert.Equal(2, csv.Count);
        Assert.Equal("Line one,\nline \"two\"", csv.For("a.mp4")!.DescriptionEn);
        Assert.Equal("plain", csv.For("B.MP4")!.DescriptionEn);
        Assert.Throws<FormatException>(() => MetadataCsv.Parse("title\nx\n"));
    }

    [Fact]
    public void Json_takes_flat_or_per_language_keys_and_drops_values_it_cannot_use()
    {
        var flat = MetadataJson.Parse("{\"titleEn\":\"T\",\"titleAr\":\"ع\",\"tags\":\"a, b\",\"visibility\":\"secret\",\"publishAt\":\"soon\",\"language\":\"Arabic\"}");
        Assert.Equal("T", flat.TitleEn);
        Assert.Equal("ع", flat.TitleAr);
        Assert.Equal(new[] { "a", "b" }, flat.Tags);
        Assert.Null(flat.Visibility);
        Assert.Null(flat.PublishAt);
        Assert.Null(flat.Language);
        Assert.Throws<FormatException>(() => MetadataJson.Parse("[1]"));
    }

    [Fact]
    public void Chapters_json_becomes_the_chapter_lines()
    {
        Assert.Equal("0:00 Intro\n1:05 Wudu\n1:02:03 Questions",
            ChapterFile.FromJson("{\"chapters\":[{\"start\":0,\"title\":\"Intro\"},{\"time\":\"1:05\",\"name\":\"Wudu\"},{\"startTime\":\"1:02:03\",\"title\":\"Questions\"}]}"));
        Assert.Throws<FormatException>(() => ChapterFile.FromJson("[{\"title\":\"no start\"}]"));
        Assert.Throws<FormatException>(() => ChapterFile.FromJson("[]"));
    }
}

public class FileHasherTests
{
    [Fact]
    public async Task A_small_file_fingerprints_whole_and_a_large_one_by_its_first_and_last_mib()
    {
        var dir = Temp.Folder();
        var small = Temp.Bytes(dir, "s.mp4", 1000);
        var (sha, fingerprint) = await FileHasher.HashAsync(small, null, default);
        var expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(small))).ToLowerInvariant();
        Assert.Equal(expected, sha);
        Assert.Equal(expected, fingerprint);

        var large = Temp.Bytes(dir, "l.mp4", 3 * 1024 * 1024 + 7, seed: 2);
        var bytes = File.ReadAllBytes(large);
        var edges = bytes.Take(1024 * 1024).Concat(bytes.Skip(bytes.Length - 1024 * 1024)).ToArray();
        var reported = new List<double>();
        var (bigSha, bigFingerprint) = await FileHasher.HashAsync(large, new SyncProgress(reported.Add), default);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bigSha);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(edges)).ToLowerInvariant(), bigFingerprint);
        Assert.Equal(1, reported.Last());
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

/// <summary>admin-api and api-videos' TUS proxy, in memory.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    public readonly ConcurrentQueue<(string Method, string Path, string Body, string? Auth)> Calls = new();
    public readonly Dictionary<string, (long Length, MemoryStream Data)> Uploads = new();
    public readonly HashSet<string> Uploaded = new();
    public string? AlreadyImportedSha;
    public int OpenCount;
    /// <summary>Answer 404 to a PATCH at or after this offset (the proxy session expired), <see cref="ExpireCount"/> times</summary>
    public long? ExpireAt;
    public int ExpireCount = 1;
    /// <summary>The answer to the PATCH at this offset is lost, once; what it sent is kept or not</summary>
    public long? LoseAt;
    public bool LoseKeeps;
    public int LoseStatus = 502;
    /// <summary>Answer this to every DELETE</summary>
    public int? DeleteStatus;
    /// <summary>Answer 503 to this many DELETEs first</summary>
    public int DeleteFailures;
    /// <summary>Answer 503 to this many sidecar calls first</summary>
    public int SidecarFailures;
    public JsonNode? LastItemBody;
    public JsonNode? LastSidecars;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? "" : request.Method == HttpMethod.Patch ? "" : await request.Content.ReadAsStringAsync(ct);
        Calls.Enqueue((request.Method.Method, path, body, request.Headers.Authorization?.Parameter));
        HttpResponseMessage Json(HttpStatusCode status, string json) => FakeHandler.Json(status, json);

        if (path.EndsWith("/items") && request.Method == HttpMethod.Post)
        {
            var b = JsonNode.Parse(body)!;
            LastItemBody = b;
            if (b["sha256"]!.GetValue<string>() == AlreadyImportedSha)
            {
                return Json(HttpStatusCode.Conflict, "{\"success\":false,\"code\":\"already_imported\",\"error\":\"x\",\"item\":{\"id\":\"item-old\",\"batchId\":\"b0\",\"videoId\":\"vid-old\",\"state\":\"uploaded\",\"error\":null}}");
            }
            OpenCount++;
            // The second opening resumes: the earlier proxy upload goes on
            var resume = OpenCount > 1 && Uploads.Count > 0 ? ",\"tusUploadId\":\"" + Uploads.Keys.First() + "\",\"reused\":true" : "";
            return Json(HttpStatusCode.Created, "{\"success\":true,\"result\":{\"item\":{\"id\":\"item-1\",\"batchId\":\"b1\",\"videoId\":\"vid-1\",\"state\":\"initiated\",\"error\":null},\"upload\":{\"videoId\":\"vid-1\",\"tusSessionId\":\"sess-" + OpenCount + "\",\"chunkSize\":1000,\"resumable\":true" + resume + "}}}");
        }
        if (path.EndsWith("/uploaded"))
        {
            Uploaded.Add(path.Split('/')[^2]);
            return Json(HttpStatusCode.OK, "{\"success\":true,\"result\":{\"item\":{\"id\":\"item-1\",\"batchId\":\"b1\",\"videoId\":\"vid-1\",\"state\":\"uploaded\",\"error\":null}}}");
        }
        if (path.EndsWith("/sidecars"))
        {
            LastSidecars = JsonNode.Parse(body);
            if (SidecarFailures-- > 0) return Json(HttpStatusCode.ServiceUnavailable, "{\"success\":false,\"code\":\"internal_error\",\"error\":\"down\"}");
            return Json(HttpStatusCode.Accepted, "{\"success\":true,\"result\":{\"jobId\":\"job-9\"}}");
        }
        if (path.EndsWith("/failed") || path.EndsWith("/errors")) return Json(HttpStatusCode.OK, "{\"success\":true,\"result\":{}}");

        if (path == "/api/videos/tus" && request.Method == HttpMethod.Post)
        {
            var id = "up" + (Uploads.Count + 1);
            Uploads[id] = (long.Parse(request.Headers.GetValues("Upload-Length").First()), new MemoryStream());
            var created = new HttpResponseMessage(HttpStatusCode.Created);
            created.Headers.Location = new Uri("/api/videos/tus/" + id, UriKind.Relative);
            return created;
        }
        if (path.StartsWith("/api/videos/tus/"))
        {
            var id = path["/api/videos/tus/".Length..];
            if (!Uploads.TryGetValue(id, out var up)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            var answer = new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Delete)
            {
                if (DeleteStatus is { } refused) return new HttpResponseMessage((HttpStatusCode)refused);
                if (DeleteFailures-- > 0) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                Uploads.Remove(id);
                return answer;
            }
            if (request.Method == HttpMethod.Head)
            {
                answer = new HttpResponseMessage(HttpStatusCode.OK);
            }
            else if (request.Method == HttpMethod.Patch)
            {
                var offset = long.Parse(request.Headers.GetValues("Upload-Offset").First());
                if (ExpireAt is { } at && offset >= at && ExpireCount > 0)
                {
                    ExpireCount--;
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                if (offset != up.Data.Length) return new HttpResponseMessage(HttpStatusCode.Conflict);
                var chunk = await request.Content!.ReadAsByteArrayAsync(ct);
                var lost = LoseAt == offset;
                if (!lost || LoseKeeps) up.Data.Write(chunk);
                if (lost)
                {
                    LoseAt = null;
                    return new HttpResponseMessage((HttpStatusCode)LoseStatus);
                }
            }
            answer.Headers.Add("Upload-Offset", up.Data.Length.ToString());
            return answer;
        }
        return Json(HttpStatusCode.NotFound, "{\"success\":false,\"code\":\"not_found\",\"error\":\"" + path + "\"}");
    }
}

internal sealed class RecordingContext : IStepContext
{
    public readonly List<(LogLevel Level, string Text, string Code)> Lines = new();
    public int Saves;
    public double LastProgress;
    public void Log(LogLevel level, string code, string text, params string[] args) => Lines.Add((level, text, code));
    public void Progress(double fraction) => LastProgress = fraction;
    public void Save() => Saves++;
    public TimeSpan? LaterDelay;
    public void Later(TimeSpan delay) => LaterDelay = delay;
    public StepWait Wait;
    public void Waiting(StepWait wait) => Wait = wait;
}

public class ImportApiClientTests
{
    private static readonly Uri Admin = new("https://admin.api.example");

    [Fact]
    public async Task Opens_a_file_with_its_metadata_and_the_bearer_token()
    {
        var server = new FakeServer();
        var api = new ImportApiClient(Admin, _ => Task.FromResult<string?>("tok"), server);
        var meta = new ImportMetadata { TitleEn = "Lesson", TitleAr = "درس", DescriptionAr = "وصف", Tags = new[] { "a", "b" }, Category = "fiqh", Visibility = "unlisted", FallbackTitle = "lesson" };
        var opened = await api.OpenItemAsync("b1", new ItemUploadRequest(new string('a', 64), "lesson.mp4", 10, "video/mp4", new UploadFingerprint("lesson.mp4", 10, 5, new string('c', 64)), meta));

        Assert.Equal("vid-1", opened.Upload.VideoId);
        Assert.Equal("sess-1", opened.Upload.TusSessionId);
        var body = server.LastItemBody!;
        Assert.Equal("Lesson", body["title"]!.GetValue<string>());
        Assert.Equal("درس", body["titleLocalized"]!["ar"]!.GetValue<string>());
        Assert.Equal("وصف", body["description"]!.GetValue<string>());
        Assert.Equal("a,b", body["tags"]!.GetValue<string>());
        Assert.Equal("unlisted", body["visibility"]!.GetValue<string>());
        Assert.Equal(new string('c', 64), body["fingerprint"]!["hash"]!.GetValue<string>());
        Assert.All(server.Calls, c => Assert.Equal("tok", c.Auth));
    }

    [Fact]
    public async Task An_already_imported_file_and_server_refusals_come_back_as_their_own_errors()
    {
        var server = new FakeServer { AlreadyImportedSha = new string('a', 64) };
        var api = new ImportApiClient(Admin, _ => Task.FromResult<string?>("tok"), server);
        var request = new ItemUploadRequest(new string('a', 64), "x.mp4", 1, "video/mp4", new UploadFingerprint("x.mp4", 1, 0, new string('c', 64)), new ImportMetadata { FallbackTitle = "x" });
        var already = await Assert.ThrowsAsync<AlreadyImportedException>(() => api.OpenItemAsync("b1", request));
        Assert.Equal("vid-old", already.Item.VideoId);

        var busy = new FakeHandler { Answer = _ => { var r = FakeHandler.Json(HttpStatusCode.ServiceUnavailable, "{\"success\":false,\"code\":\"busy\",\"error\":\"retry\"}"); r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3)); return r; } };
        var refused = await Assert.ThrowsAsync<ImportApiException>(() => new ImportApiClient(Admin, _ => Task.FromResult<string?>("tok"), busy).CreateBatchAsync("ch1", null));
        Assert.True(refused.IsRetryable);
        Assert.Equal(TimeSpan.FromSeconds(3), refused.RetryAfter);

        var signedOut = await Assert.ThrowsAsync<ImportApiException>(() => new ImportApiClient(Admin, _ => Task.FromResult<string?>(null), busy).CreateBatchAsync("ch1", null));
        Assert.Equal(401, signedOut.Status);
        Assert.False(signedOut.IsRetryable);
    }

    [Fact]
    public async Task Lists_channels_by_name_from_the_admin_content_list()
    {
        var handler = new FakeHandler { Answer = _ => FakeHandler.Json(HttpStatusCode.OK, "{\"success\":true,\"result\":{\"items\":[{\"id\":\"ch1\",\"name\":\"Al-Azhar\",\"nameLocalized\":{\"en\":\"Al-Azhar\",\"ar\":\"الأزهر\"},\"handle\":\"azhar\",\"ownerEmail\":\"o@example.org\"}],\"total\":1,\"page\":1,\"pageSize\":20,\"indexComplete\":true}}") };
        var channels = await new ImportApiClient(Admin, _ => Task.FromResult<string?>("tok"), handler).ListChannelsAsync("azhar ");
        var channel = Assert.Single(channels);
        Assert.Equal("Al-Azhar", channel.Name);
        Assert.Equal("Al-Azhar", channel.NameFor(arabic: false));
        Assert.Equal("الأزهر", channel.NameFor(arabic: true));
        Assert.Equal("/api/content/channels?page=1&pageSize=20&sort=name&q=azhar", handler.Requests[0].RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Creates_a_channel_with_bilingual_name_and_description()
    {
        string? captured = null;
        var handler = new FakeHandler
        {
            Answer = req =>
            {
                captured = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return FakeHandler.Json(HttpStatusCode.Created, "{\"success\":true,\"result\":{\"channel\":{\"id\":\"ch2\",\"name\":\"Fiqh Academy\",\"nameLocalized\":{\"en\":\"Fiqh Academy\",\"ar\":\"أكاديمية الفقه\"},\"handle\":\"fiqh\"}}}");
            },
        };
        var api = new ImportApiClient(Admin, _ => Task.FromResult<string?>("tok"), handler);
        var created = await api.CreateChannelAsync("  Fiqh Academy  ", "  أكاديمية الفقه  ", "@fiqh", "Weekly lessons", "دروس أسبوعية");

        Assert.Equal("ch2", created.Id);
        Assert.Equal("أكاديمية الفقه", created.NameFor(arabic: true));
        Assert.Equal("/api/admin/import/channels", handler.Requests[0].RequestUri!.AbsolutePath);
        var body = JsonNode.Parse(captured!)!;
        Assert.Equal("Fiqh Academy", body["name"]!.GetValue<string>());
        Assert.Equal("Fiqh Academy", body["nameLocalized"]!["en"]!.GetValue<string>());
        Assert.Equal("أكاديمية الفقه", body["nameLocalized"]!["ar"]!.GetValue<string>());
        Assert.Equal("fiqh", body["handle"]!.GetValue<string>());
        Assert.Equal("Weekly lessons", body["description"]!.GetValue<string>());
        Assert.Equal("Weekly lessons", body["descriptionLocalized"]!["en"]!.GetValue<string>());
        Assert.Equal("دروس أسبوعية", body["descriptionLocalized"]!["ar"]!.GetValue<string>());
    }

    [Fact]
    public async Task Omits_optional_description_fields_when_both_are_empty()
    {
        string? captured = null;
        var handler = new FakeHandler
        {
            Answer = req =>
            {
                captured = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return FakeHandler.Json(HttpStatusCode.Created, "{\"success\":true,\"result\":{\"channel\":{\"id\":\"ch3\",\"name\":\"Seerah\",\"nameLocalized\":{\"en\":\"Seerah\",\"ar\":\"السيرة\"}}}}");
            },
        };
        var api = new ImportApiClient(Admin, _ => Task.FromResult<string?>("tok"), handler);
        await api.CreateChannelAsync("Seerah", "السيرة", null, null, null);

        var body = JsonNode.Parse(captured!)!.AsObject();
        Assert.False(body.ContainsKey("description"));
        Assert.False(body.ContainsKey("descriptionLocalized"));
        Assert.False(body.ContainsKey("handle"));

        await Assert.ThrowsAsync<ArgumentException>(() => api.CreateChannelAsync("Seerah", "السيرة", null, "English only", null));
        await Assert.ThrowsAsync<ArgumentException>(() => api.CreateChannelAsync("Seerah", "السيرة", null, null, "عربي فقط"));
    }

    [Fact]
    public async Task Translates_fields_through_admin_api_and_handles_quran_refusal_and_errors()
    {
        string? captured = null;
        var handler = new FakeHandler
        {
            Answer = req =>
            {
                captured = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return FakeHandler.Json(HttpStatusCode.OK, "{\"success\":true,\"result\":{\"items\":[{\"text\":\"أكاديمية الفقه\",\"cached\":false}],\"provider\":\"workers-ai\",\"machineTranslation\":true}}");
            },
        };
        var api = new ImportApiClient(Admin, _ => Task.FromResult<string?>("tok"), handler);
        var item = await api.TranslateFieldAsync("en", "ar", "Fiqh Academy", "channels.name");
        Assert.Equal("أكاديمية الفقه", item.Text);
        Assert.Null(item.Refused);
        Assert.Equal("/api/translate/fields", handler.Requests[0].RequestUri!.AbsolutePath);
        var body = JsonNode.Parse(captured!)!;
        Assert.Equal("en", body["from"]!.GetValue<string>());
        Assert.Equal("ar", body["to"]!.GetValue<string>());
        Assert.Equal("channels.name", body["field"]!.GetValue<string>());
        Assert.Equal("Fiqh Academy", body["texts"]![0]!.GetValue<string>());

        var quranHandler = new FakeHandler
        {
            Answer = _ => FakeHandler.Json(HttpStatusCode.OK, "{\"success\":true,\"result\":{\"items\":[{\"text\":\"\",\"refused\":\"quran\",\"quran\":[{\"range\":\"2:255\",\"label\":\"Al-Baqarah 2:255\"}],\"cached\":false}]}}"),
        };
        var refused = await new ImportApiClient(Admin, _ => Task.FromResult<string?>("tok"), quranHandler).TranslateFieldAsync("ar", "en", "آية", "channels.description");
        Assert.Equal("quran", refused.Refused);
        Assert.Equal("Al-Baqarah 2:255", Assert.Single(refused.Quran!).Label);

        var limitedHandler = new FakeHandler
        {
            Answer = _ => FakeHandler.Json(HttpStatusCode.TooManyRequests, "{\"success\":false,\"code\":\"daily_limit\",\"error\":\"daily_limit\"}"),
        };
        var ex = await Assert.ThrowsAsync<ImportApiException>(() => new ImportApiClient(Admin, _ => Task.FromResult<string?>("tok"), limitedHandler).TranslateFieldAsync("en", "ar", "Test", "channels.name"));
        Assert.Equal(429, ex.Status);
        Assert.Equal("daily_limit", ex.Code);
    }

    [Fact]
    public void ChannelValidation_requires_both_languages_for_name_and_paired_descriptions()
    {
        var empty = ChannelValidation.Validate("", "", null, null);
        Assert.True(empty.HasErrors);
        Assert.Equal("required", empty.NameEn);
        Assert.Equal("required", empty.NameAr);
        Assert.Null(empty.DescriptionEn);
        Assert.Null(empty.DescriptionAr);

        var enOnly = ChannelValidation.Validate("Fiqh", "   ", null, null);
        Assert.Null(enOnly.NameEn);
        Assert.Equal("required", enOnly.NameAr);

        var arOnly = ChannelValidation.Validate(" ", "الفقه", null, null);
        Assert.Equal("required", arOnly.NameEn);
        Assert.Null(arOnly.NameAr);

        var halfDesc = ChannelValidation.Validate("Fiqh", "الفقه", "English only", "");
        Assert.True(halfDesc.HasErrors);
        Assert.Null(halfDesc.DescriptionEn);
        Assert.Equal("required_pair", halfDesc.DescriptionAr);

        var halfDescAr = ChannelValidation.Validate("Fiqh", "الفقه", "", "وصف فقط");
        Assert.True(halfDescAr.HasErrors);
        Assert.Equal("required_pair", halfDescAr.DescriptionEn);
        Assert.Null(halfDescAr.DescriptionAr);

        var tooLong = ChannelValidation.Validate(new string('a', 101), "الفقه", new string('b', 5001), "وصف");
        Assert.Equal("too_long", tooLong.NameEn);
        Assert.Equal("too_long", tooLong.DescriptionEn);

        var validNoDesc = ChannelValidation.Validate("Fiqh", "الفقه", "", "");
        Assert.False(validNoDesc.HasErrors);

        var validWithDesc = ChannelValidation.Validate("Fiqh", "الفقه", "Lessons", "دروس");
        Assert.False(validWithDesc.HasErrors);
    }
}

public class UploadStepTests
{
    private static (QueueItem Item, string Path) Item(int bytes, string? sha = null)
    {
        var dir = Temp.Folder();
        var path = Temp.Bytes(dir, "lesson.mp4", bytes);
        var scan = FolderScanner.Scan(dir, false, null).Videos[0];
        var item = QueueItem.For(scan, "b1", "ch1", DateTimeOffset.UtcNow);
        item.Sha256 = sha ?? Temp.Sha(path);
        item.FingerprintHash = new string('c', 64);
        return (item, path);
    }

    private static UploadStep Step(FakeServer server) => new(
        new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), server),
        new TusClient(server),
        new Uri("https://videos.api.example"),
        _ => Task.FromResult<string?>("tok"));

    [Fact]
    public async Task Sends_the_whole_file_in_the_servers_chunks_then_marks_it_uploaded()
    {
        var server = new FakeServer();
        var (item, path) = Item(2500);
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await Step(server).RunAsync(item, context, default));

        var upload = Assert.Single(server.Uploads).Value;
        Assert.Equal(File.ReadAllBytes(path), upload.Data.ToArray());
        Assert.Equal(3, server.Calls.Count(c => c.Method == "PATCH"));
        Assert.Contains("item-1", server.Uploaded);
        Assert.Equal(2500, item.UploadOffset);
        Assert.Equal(1, context.LastProgress);
        Assert.True(context.Saves >= 4, "the offset is saved after every chunk");
    }

    [Fact]
    public async Task An_expired_session_is_opened_again_and_the_upload_continues_from_the_servers_offset()
    {
        var server = new FakeServer { ExpireAt = 2000 };
        var (item, path) = Item(2500);
        var context = new RecordingContext();
        await Step(server).RunAsync(item, context, default);

        Assert.Equal(2, server.OpenCount);
        Assert.Equal(File.ReadAllBytes(path), Assert.Single(server.Uploads).Value.Data.ToArray());
        Assert.Contains(context.Lines, l => l.Code == "session_ended");
    }

    [Fact]
    public async Task A_second_lost_session_in_one_run_is_left_to_the_queue_and_the_next_run_resumes_without_opening_again()
    {
        var server = new FakeServer { ExpireAt = 1000, ExpireCount = 2 };
        var (item, path) = Item(2500);
        await Assert.ThrowsAsync<TusGoneException>(() => Step(server).RunAsync(item, new RecordingContext(), default));
        Assert.Equal(1000, item.UploadOffset);
        Assert.Equal(2, server.OpenCount);

        await Step(server).RunAsync(item, new RecordingContext(), default);
        Assert.Equal(2, server.OpenCount);
        Assert.Equal(File.ReadAllBytes(path), Assert.Single(server.Uploads).Value.Data.ToArray());
    }

    [Fact]
    public async Task A_file_the_channel_already_has_is_not_uploaded_and_skips_the_rest()
    {
        var server = new FakeServer { AlreadyImportedSha = new string('a', 64) };
        var (item, _) = Item(100, new string('a', 64));
        Assert.Equal(StepResult.SkipRest, await Step(server).RunAsync(item, new RecordingContext(), default));
        Assert.True(item.AlreadyImported);
        Assert.Equal("vid-old", item.VideoId);
        Assert.Empty(server.Uploads);
    }

    [Fact]
    public async Task A_file_changed_since_the_scan_is_refused_without_retrying()
    {
        var (item, path) = Item(100);
        File.AppendAllText(path, "more");
        var failure = await Assert.ThrowsAsync<StepFailedException>(() => Step(new FakeServer()).RunAsync(item, new RecordingContext(), default));
        Assert.False(failure.Retryable);
        Assert.Equal("file_changed", ImportQueue.ErrorCode(failure));
    }
}

public class AttachStepTests
{
    [Fact]
    public async Task Sends_captions_chapters_thumbnail_playlist_and_date_and_leaves_out_what_it_cannot_read()
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "v.mp4", 10);
        Temp.Write(dir, "v.ar.srt", "1\n00:00:00,000 --> 00:00:01,000\nنص\n");
        Temp.Write(dir, "v.en.vtt", "  ");
        Temp.Write(dir, "v.chapters.json", "{ broken");
        Temp.Bytes(dir, "v.png", 20);
        Temp.Write(dir, "v.json", "{\"playlistId\":\"pl_9\",\"publishAt\":\"2026-12-24 08:00\"}");
        var video = FolderScanner.Scan(dir, false, null).Videos[0];
        var context = new RecordingContext();

        var payload = await AttachStep.BuildAsync(video, AlUmmahNow.ToolsCore.Import.Sources.SourceAccess.Default, context, default);

        Assert.Equal("ar", Assert.Single(payload.Captions!).Language);
        Assert.Null(payload.Chapters);
        Assert.Equal("image/png", payload.Thumbnail!.MimeType);
        Assert.Equal(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(dir, "v.png"))), payload.Thumbnail.Data);
        Assert.Equal("pl_9", payload.PlaylistId);
        Assert.Equal("2026-12-24T08:00:00.000Z", payload.PublishAt);
        Assert.Equal(2, context.Lines.Count(l => l.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task Nothing_to_attach_is_skipped_without_a_call()
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "v.mp4", 10);
        var item = QueueItem.For(FolderScanner.Scan(dir, false, null).Videos[0], "b1", "ch1", DateTimeOffset.UtcNow);
        item.ServerItemId = "item-1";
        var server = new FakeServer();
        var step = new AttachStep(new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("t"), server));
        Assert.Equal(StepResult.Skipped, await step.RunAsync(item, new RecordingContext(), default));
        Assert.Empty(server.Calls);
    }
}

internal sealed class ScriptedStep : IImportStep
{
    private readonly Func<QueueItem, IStepContext, CancellationToken, Task<StepResult>> _run;
    public int Runs;
    public int Running;
    public int MostAtOnce;

    public ScriptedStep(ImportStepKind kind, Func<QueueItem, IStepContext, CancellationToken, Task<StepResult>>? run = null)
    {
        Kind = kind;
        _run = run ?? ((_, _, _) => Task.FromResult(StepResult.Done));
    }

    public ImportStepKind Kind { get; }

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        Interlocked.Increment(ref Runs);
        var now = Interlocked.Increment(ref Running);
        MostAtOnce = Math.Max(MostAtOnce, now);
        try
        {
            return await _run(item, context, ct);
        }
        finally
        {
            Interlocked.Decrement(ref Running);
        }
    }
}

internal sealed class RecordingSink : IImportFailureSink
{
    public readonly List<(string Item, ImportStepKind Step, string Reference)> Failed = new();

    public Task ItemFailedAsync(QueueItem item, StepState step, Exception error, string reference, CancellationToken ct)
    {
        lock (Failed) Failed.Add((item.Id, step.Kind, reference));
        return Task.CompletedTask;
    }
}

public class ImportQueueTests
{
    private static ScannedVideo Video(string name = "a.mp4") =>
        new(Path.Combine(Path.GetTempPath(), name), 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = name }, Array.Empty<ScanNote>());

    private static string QueuePath() => Path.Combine(Temp.Folder(), "queue.json");

    /// <summary>Every step, done at once, except the ones given.</summary>
    internal static IImportStep[] All(params ScriptedStep[] given) =>
        Enum.GetValues<ImportStepKind>().Select(k => (IImportStep?)given.FirstOrDefault(g => g.Kind == k) ?? new ScriptedStep(k)).ToArray();

    private static StepState S(QueueItem item, ImportStepKind kind) => item.Steps.Single(s => s.Kind == kind);

    private static async Task Drain(ImportQueue queue, Func<bool> done)
    {
        await Temp.WaitUntil(() =>
        {
            queue.Pump();
            return done();
        });
    }

    [Fact]
    public async Task Runs_each_files_steps_in_order_and_skips_the_rest_when_a_step_says_so()
    {
        var order = new ConcurrentQueue<string>();
        Func<ImportStepKind, ScriptedStep> step = k => new ScriptedStep(k, (item, _, _) =>
        {
            order.Enqueue($"{item.Video.FileName}:{k}");
            return Task.FromResult(k == ImportStepKind.Upload && item.Video.FileName == "b.mp4" ? StepResult.SkipRest : StepResult.Done);
        });
        using var queue = new ImportQueue(QueuePath(), Enum.GetValues<ImportStepKind>().Select(step));
        var a = QueueItem.For(Video("a.mp4"), "b1", "ch1", DateTimeOffset.UtcNow);
        var b = QueueItem.For(Video("b.mp4"), "b1", "ch1", DateTimeOffset.UtcNow.AddSeconds(1));
        queue.Add(new[] { a, b });

        await Drain(queue, () => queue.Items.All(i => i.Phase == ItemPhase.Done));

        Assert.Equal(new[] { "a.mp4:Hash", "a.mp4:Encode", "a.mp4:Upload", "a.mp4:Attach", "a.mp4:Transcribe", "a.mp4:Check", "a.mp4:Translate" }, order.Where(o => o.StartsWith("a")));
        Assert.DoesNotContain("b.mp4:Attach", order);
        Assert.Equal(StepStatus.Skipped, queue.Items[1].Steps.Single(s => s.Kind == ImportStepKind.Attach).Status);
        Assert.Contains("Finished", queue.LogText(a.Id));
    }

    [Fact]
    public async Task A_retryable_failure_waits_and_tries_again_then_fails_for_good_with_a_reported_reference()
    {
        var sink = new RecordingSink();
        var hash = new ScriptedStep(ImportStepKind.Hash, (_, _, _) => throw new ImportApiException(503, "internal_error", "down"));
        using var queue = new ImportQueue(QueuePath(), new IImportStep[] { hash }, sink, backoff: _ => TimeSpan.Zero);
        queue.Settings.MaxAttempts = 3;
        var item = QueueItem.For(Video(), "b1", "ch1", DateTimeOffset.UtcNow);
        queue.Add(new[] { item });

        await Drain(queue, () => queue.Items[0].Phase == ItemPhase.Failed);
        await Temp.WaitUntil(() => sink.Failed.Count == 1);

        Assert.Equal(3, hash.Runs);
        var step = queue.Items[0].Steps[0];
        Assert.Equal(StepStatus.Failed, step.Status);
        Assert.Equal("down (503 internal_error)", step.LastError);
        Assert.Equal("server", step.LastErrorCode);
        var failedLine = queue.LogLines(item.Id).Last(l => l.Code == "failed");
        Assert.Equal(new[] { "Hash", "3", "server", sink.Failed[0].Reference, "down (503 internal_error)" }, failedLine.Args);
        var log = queue.LogText(item.Id);
        Assert.Contains("attempt 1 failed", log);
        Assert.Contains(sink.Failed[0].Reference, log);
        Assert.Matches("^ERR-[0-9A-Z]{4}-[0-9A-Z]{4}$", sink.Failed[0].Reference);

        // Retry starts the step again with its attempts counted from zero
        queue.Retry(item.Id);
        Assert.Equal(StepStatus.Pending, queue.Items[0].Steps[0].Status);
        Assert.Equal(0, queue.Items[0].Steps[0].Attempts);
    }

    [Fact]
    public void Each_failure_has_a_code_for_the_apps_words()
    {
        Assert.Equal("admin_unreachable", ImportQueue.ErrorCode(new ImportApiException(0, "unreachable", "x")));
        Assert.Equal("signed_out", ImportQueue.ErrorCode(new ImportApiException(401, "signed_out", "x")));
        Assert.Equal("server", ImportQueue.ErrorCode(new ImportApiException(409, "channel_suspended", "x")));
        Assert.Equal("videos_unreachable", ImportQueue.ErrorCode(new TusException(0, "x")));
        Assert.Equal("upload_refused", ImportQueue.ErrorCode(new TusException(413, "x")));
        Assert.Equal("upload_gone", ImportQueue.ErrorCode(new TusGoneException("x")));
        Assert.Equal("file_missing", ImportQueue.ErrorCode(new FileNotFoundException("x")));
        Assert.Equal("file_denied", ImportQueue.ErrorCode(new UnauthorizedAccessException("x")));
        Assert.Equal("file_read", ImportQueue.ErrorCode(new IOException("x")));
        Assert.Equal("not_hashed", ImportQueue.ErrorCode(new StepFailedException("not_hashed", "x", false)));
        Assert.Equal("unexpected", ImportQueue.ErrorCode(new InvalidOperationException("x")));
    }

    [Fact]
    public async Task A_failure_retrying_cannot_fix_fails_at_once()
    {
        var hash = new ScriptedStep(ImportStepKind.Hash, (_, _, _) => throw new FileNotFoundException("gone"));
        using var queue = new ImportQueue(QueuePath(), new IImportStep[] { hash }, new RecordingSink(), backoff: _ => TimeSpan.Zero);
        queue.Add(new[] { QueueItem.For(Video(), "b1", "ch1", DateTimeOffset.UtcNow) });
        await Drain(queue, () => queue.Items[0].Phase == ItemPhase.Failed);
        Assert.Equal(1, hash.Runs);
    }

    [Fact]
    public async Task A_retry_waits_for_its_time_and_honours_the_servers_retry_after()
    {
        var calls = 0;
        var hash = new ScriptedStep(ImportStepKind.Hash, (_, _, _) => ++calls == 1 ? throw new ImportApiException(429, "rate_limited", "slow down", TimeSpan.FromMinutes(5)) : Task.FromResult(StepResult.Done));
        using var queue = new ImportQueue(QueuePath(), new IImportStep[] { hash }, backoff: _ => TimeSpan.FromSeconds(1));
        var item = QueueItem.For(Video(), "b1", "ch1", DateTimeOffset.UtcNow);
        queue.Add(new[] { item });
        await Drain(queue, () => queue.Items[0].Phase == ItemPhase.RetryWaiting);

        var next = queue.Items[0].NextAttemptAt!.Value;
        Assert.InRange(next - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
        Assert.Equal(0, queue.Pump());
        // Retry does not wait
        queue.Retry(item.Id);
        await Drain(queue, () => queue.Items[0].Steps[0].Status == StepStatus.Done);
    }

    [Fact]
    public async Task Each_step_runs_at_most_its_parallel_setting_at_once()
    {
        var gate = new TaskCompletionSource();
        var upload = new ScriptedStep(ImportStepKind.Upload, async (_, _, _) => { await gate.Task; return StepResult.Done; });
        using var queue = new ImportQueue(QueuePath(), ImportQueueTests.All(upload));
        queue.SetParallel(ImportStepKind.Upload, 2);
        queue.SetParallel(ImportStepKind.Hash, 99);
        Assert.Equal(8, queue.Settings.ParallelFor(ImportStepKind.Hash));
        queue.SetParallel(ImportStepKind.Upload, 9);
        Assert.Equal(QueueSettings.MaxUploads, queue.Settings.ParallelFor(ImportStepKind.Upload));
        queue.SetParallel(ImportStepKind.Upload, 2);
        queue.Add(Enumerable.Range(0, 5).Select(i => QueueItem.For(Video($"{i}.mp4"), "b1", "ch1", DateTimeOffset.UtcNow.AddSeconds(i))));

        await Drain(queue, () => upload.Running == 2 && queue.Items.Count(i => S(i, ImportStepKind.Upload).Status == StepStatus.Pending && S(i, ImportStepKind.Encode).Status == StepStatus.Done) == 3);
        queue.Pump();
        Assert.Equal(2, upload.Running);
        gate.SetResult();
        await Drain(queue, () => queue.Items.All(i => i.Phase == ItemPhase.Done));
        Assert.Equal(2, upload.MostAtOnce);
    }

    [Fact]
    public async Task Pausing_stops_the_running_step_without_counting_the_attempt_and_resume_runs_it_again()
    {
        var started = new TaskCompletionSource();
        var runs = 0;
        var upload = new ScriptedStep(ImportStepKind.Upload, async (_, _, ct) =>
        {
            if (++runs == 1)
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            return StepResult.Done;
        });
        using var queue = new ImportQueue(QueuePath(), ImportQueueTests.All(upload));
        var item = QueueItem.For(Video(), "b1", "ch1", DateTimeOffset.UtcNow);
        queue.Add(new[] { item });
        var pump = Drain(queue, () => started.Task.IsCompleted);
        await pump;

        queue.Pause(item.Id);
        // It reads Paused at once; the step itself stops a moment later
        Assert.Equal(ItemPhase.Paused, queue.Items[0].Phase);
        await Temp.WaitUntil(() => S(queue.Items[0], ImportStepKind.Upload).Status == StepStatus.Pending);
        Assert.Equal(0, S(queue.Items[0], ImportStepKind.Upload).Attempts);
        Assert.Equal(0, queue.Pump());

        queue.Resume(item.Id);
        await Drain(queue, () => queue.Items[0].Phase == ItemPhase.Done);
        Assert.Equal(2, runs);

        // Pause all holds every file
        queue.Add(new[] { QueueItem.For(Video("b.mp4"), "b1", "ch1", DateTimeOffset.UtcNow) });
        queue.PauseAll();
        Assert.Equal(0, queue.Pump());
        queue.ResumeAll();
        await Drain(queue, () => queue.Items.All(i => i.Phase == ItemPhase.Done));
    }

    [Fact]
    public async Task The_queue_survives_a_restart_and_a_step_that_was_running_starts_again()
    {
        var path = QueuePath();
        var hold = new TaskCompletionSource<StepResult>();
        var first = new ImportQueue(path, new IImportStep[] { new ScriptedStep(ImportStepKind.Hash, (i, _, _) => { i.Sha256 = "abc"; return Task.FromResult(StepResult.Done); }), new ScriptedStep(ImportStepKind.Encode), new ScriptedStep(ImportStepKind.Upload, (i, ctx, _) => { i.UploadOffset = 42; ctx.Save(); return hold.Task; }) });
        var item = QueueItem.For(Video(), "b1", "ch1", DateTimeOffset.UtcNow);
        first.Add(new[] { item });
        await Drain(first, () => first.Items[0].UploadOffset == 42);

        // The app closes mid-upload: the saved file still says Running
        var second = new ImportQueue(path, new IImportStep[] { new ScriptedStep(ImportStepKind.Upload), new ScriptedStep(ImportStepKind.Attach), new ScriptedStep(ImportStepKind.Transcribe), new ScriptedStep(ImportStepKind.Check), new ScriptedStep(ImportStepKind.Translate) });
        var restored = Assert.Single(second.Items);
        Assert.Equal("abc", restored.Sha256);
        Assert.Equal(42, restored.UploadOffset);
        Assert.Equal(StepStatus.Done, S(restored, ImportStepKind.Hash).Status);
        Assert.Equal(StepStatus.Pending, S(restored, ImportStepKind.Upload).Status);
        Assert.Contains("Upload: started", second.LogText(restored.Id));
        await Drain(second, () => second.Items[0].Phase == ItemPhase.Done);
        hold.SetResult(StepResult.Done);
        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task Remove_and_clear_done_take_files_out_of_the_queue()
    {
        using var queue = new ImportQueue(QueuePath(), ImportQueueTests.All());
        var a = QueueItem.For(Video("a.mp4"), "b1", "ch1", DateTimeOffset.UtcNow);
        var b = QueueItem.For(Video("b.mp4"), "b1", "ch1", DateTimeOffset.UtcNow);
        queue.Add(new[] { a, b });
        queue.Pause(b.Id);
        await Drain(queue, () => queue.Items.First(i => i.Id == a.Id).Phase == ItemPhase.Done);
        Assert.Equal(1, queue.ClearDone());
        queue.Remove(b.Id);
        Assert.Empty(queue.Items);
    }
}

public class ErrorReportingTests
{
    [Fact]
    public async Task Reports_wait_while_the_server_is_away_and_go_once_it_answers()
    {
        var sent = new List<ErrorReport>();
        var up = false;
        using var reporter = new ErrorReporter((batch, _) =>
        {
            if (!up) throw new ImportApiException(0, "unreachable", "away");
            sent.AddRange(batch);
            return Task.CompletedTask;
        }, "1.2.3", () => "ar", TimeSpan.FromHours(1));

        Exception thrown;
        try { throw new InvalidOperationException("boom"); } catch (Exception ex) { thrown = ex; }
        var reference = reporter.Report("unhandled", thrown, "boom", route: "/import");
        Assert.Matches("^ERR-[0-9A-Z]{4}-[0-9A-Z]{4}$", reference);
        await reporter.FlushAsync();
        Assert.Equal(1, reporter.Waiting);

        up = true;
        await reporter.FlushAsync();
        var report = Assert.Single(sent);
        Assert.Equal(reference, report.Ref);
        Assert.Equal("1.2.3", report.Release);
        Assert.Equal("ar", report.Locale);
        Assert.NotNull(report.Stack);
        Assert.Equal(0, reporter.Waiting);
    }

    [Fact]
    public async Task A_report_the_server_refuses_is_dropped_and_batches_hold_at_most_twenty()
    {
        var batches = new List<int>();
        using var reporter = new ErrorReporter((batch, _) =>
        {
            batches.Add(batch.Count);
            if (batches.Count == 1) throw new ImportApiException(400, "invalid_request", "No valid reports");
            return Task.CompletedTask;
        }, "1", () => "en", TimeSpan.FromHours(1));
        for (var i = 0; i < 45; i++) reporter.Report("upload", new IOException("x"), "x");
        await reporter.FlushAsync();
        Assert.Equal(new[] { 20, 20, 5 }, batches);
        Assert.Equal(0, reporter.Waiting);
    }

    [Fact]
    public async Task A_file_that_failed_for_good_marks_its_server_item_failed_and_reports_the_same_reference()
    {
        var server = new FakeServer();
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("t"), server);
        using var reporter = new ErrorReporter(api.ReportErrorsAsync, "1", () => "en", TimeSpan.FromHours(1));
        var item = QueueItem.For(new ScannedVideo("/x/a.mp4", 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata(), Array.Empty<ScanNote>()), "b1", "ch1", DateTimeOffset.UtcNow);
        item.ServerItemId = "item-7";
        var step = item.Steps.Single(s => s.Kind == ImportStepKind.Upload);
        step.Attempts = 6;

        await new ServerFailureSink(api, reporter).ItemFailedAsync(item, step, new TusException(503, "PATCH failed with 503"), "ERR-AAAA-BBBB", default);

        var failed = server.Calls.Single(c => c.Path.EndsWith("/items/item-7/failed"));
        Assert.Contains("ERR-AAAA-BBBB", failed.Body);
        var report = JsonNode.Parse(server.Calls.Single(c => c.Path.EndsWith("/errors")).Body)!["events"]![0]!;
        Assert.Equal("upload", report["kind"]!.GetValue<string>());
        Assert.Equal("ERR-AAAA-BBBB", report["ref"]!.GetValue<string>());
        Assert.Equal(503, report["status"]!.GetValue<int>());
        Assert.Equal("Upload", report["extra"]!["step"]!.GetValue<string>());
        Assert.DoesNotContain("a.mp4", report.ToJsonString());
    }

    [Theory]
    [InlineData("no_video", false)]
    [InlineData("not_video", false)]
    [InlineData("content_changed", false)]
    [InlineData("file_changed", false)]
    [InlineData("file_writing", false)]
    [InlineData("file_missing", false)]
    [InlineData("file_link", false)]
    [InlineData("not_hashed", true)]
    [InlineData("upload_left", true)]
    [InlineData("file_read", true)]
    public async Task What_was_found_out_about_a_file_is_no_error_report_and_still_marks_its_server_item(string code, bool reported)
    {
        var server = new FakeServer();
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("t"), server);
        using var reporter = new ErrorReporter(api.ReportErrorsAsync, "1", () => "en", TimeSpan.FromHours(1));
        var item = QueueItem.For(new ScannedVideo("/x/a.mp4", 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata(), Array.Empty<ScanNote>()), "b1", "ch1", DateTimeOffset.UtcNow);
        item.ServerItemId = "item-7";
        var step = item.Steps.Single(s => s.Kind == ImportStepKind.Upload);
        await new ServerFailureSink(api, reporter).ItemFailedAsync(item, step, new StepFailedException(code, "What happened", retryable: false), "ERR-AAAA-BBBB", default);
        Assert.Equal(reported, server.Calls.Any(c => c.Path.EndsWith("/errors")));
        var failed = server.Calls.Single(c => c.Path.EndsWith("/items/item-7/failed"));
        Assert.Contains("What happened", failed.Body);
        Assert.Equal(reported, failed.Body.Contains("ERR-AAAA-BBBB"));
    }
}

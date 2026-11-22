using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Transcription;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>A clock the test moves.</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now = start;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>admin-api's TA-13 calls: the Quran index, the transcript, the translations.</summary>
internal sealed class TranscribeServer : HttpMessageHandler
{
    public readonly ConcurrentQueue<string> Calls = new();
    public int IndexCalls;
    public bool IndexUnavailable;
    public DateTimeOffset IndexExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
    public JsonNode? Transcript;
    public string? TranscriptRefusal;
    public JsonNode? TranslationBody;
    /// <summary>Per language, the answers in turn (null: a job); the last one repeats</summary>
    public Dictionary<string, Queue<string?>> Translations = new();

    private static HttpResponseMessage Ok(string result, HttpStatusCode status = HttpStatusCode.OK) =>
        FakeHandler.Json(status, "{\"success\":true,\"result\":" + result + "}");

    private static HttpResponseMessage Refuse(HttpStatusCode status, string code) =>
        FakeHandler.Json(status, "{\"success\":false,\"code\":\"" + code + "\",\"error\":\"" + code + "\"}");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        Calls.Enqueue(request.Method + " " + path);
        if (path == "/api/admin/import/quran-index")
        {
            IndexCalls++;
            if (IndexUnavailable) return Refuse(HttpStatusCode.ServiceUnavailable, "quran_index_unavailable");
            return Ok(JsonSerializer.Serialize(new { index = MiniQuran.File(), expiresAt = IndexExpiresAt }));
        }
        if (path.EndsWith("/transcript", StringComparison.Ordinal))
        {
            Transcript = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (TranscriptRefusal is not null) return Refuse(TranscriptRefusal == "invalid_transcript" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, TranscriptRefusal);
            return Ok("{\"status\":\"queued\",\"segments\":3}", HttpStatusCode.Accepted);
        }
        if (path.EndsWith("/translations", StringComparison.Ordinal))
        {
            TranslationBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            var results = new JsonArray();
            foreach (var l in TranslationBody!["languages"]!.AsArray().Select(n => n!.GetValue<string>()))
            {
                string? error = null;
                if (Translations.TryGetValue(l, out var answers)) error = answers.Count > 1 ? answers.Dequeue() : answers.Peek();
                results.Add(error is null ? new JsonObject { ["language"] = l, ["jobId"] = "job-" + l, ["status"] = "queued", ["existing"] = false } : new JsonObject { ["language"] = l, ["error"] = error });
            }
            return Ok(new JsonObject { ["results"] = results }.ToJsonString());
        }
        return Refuse(HttpStatusCode.NotFound, "not_found");
    }
}

/// <summary>aun-core's whisper for tests: answers a transcript of the given words, 400 ms each.</summary>
internal sealed class FakeTranscriber : ITranscriber
{
    public bool Available { get; set; } = true;
    public SpeechDevice? Device { get; set; } = new(true, "Metal", "Apple M2");
    public IReadOnlyList<SpeechModel> Models { get; } = [new("large-v3-turbo", 1_624_555_275), new("small-q5_1", 190_085_487)];
    public HashSet<string> Installed = ["large-v3-turbo"];
    public string Language = "ar";
    public string Words = "";
    public Exception? Fail;
    public int Downloads;
    public readonly List<(string Model, string Media, string? Language)> Runs = new();

    public string? InstalledPath(string modelId) => Installed.Contains(modelId) ? "/models/" + modelId + ".bin" : null;

    public Task<string> DownloadAsync(string modelId, IProgress<double> progress, CancellationToken ct)
    {
        Downloads++;
        progress.Report(1);
        Installed.Add(modelId);
        return Task.FromResult("/models/" + modelId + ".bin");
    }

    public Task<string> TranscribeAsync(string modelPath, string mediaPath, string? language, IProgress<double> progress, CancellationToken ct)
    {
        Runs.Add((modelPath, mediaPath, language));
        if (Fail is not null) throw Fail;
        progress.Report(1);
        // Two whisper segments: the first five words, then the rest
        var words = MiniQuran.Asr(Words);
        object Seg(IEnumerable<AsrWord> ws)
        {
            var list = ws.ToList();
            return new { start_ms = list[0].StartMs, end_ms = list[^1].EndMs, text = " " + string.Join(' ', list.Select(w => w.Text)), words = list.Select(w => new { start_ms = w.StartMs, end_ms = w.EndMs, text = " " + w.Text, confidence = 0.9 }) };
        }
        var segments = words.Count <= 5 ? (words.Count == 0 ? [] : new[] { Seg(words) }) : new[] { Seg(words.Take(5)), Seg(words.Skip(5)) };
        return Task.FromResult(JsonSerializer.Serialize(new { language = language ?? Language, segments }));
    }
}

public class TranscriptUploadTests
{
    static readonly QuranIndex Index = new(MiniQuran.File());

    static CoreTranscript Transcript(string text, int perSegment = 100)
    {
        var words = MiniQuran.Asr(text);
        var segments = words.Chunk(perSegment).Select(c => new CoreTranscriptSegment(c[0].StartMs, c[^1].EndMs, string.Join(' ', c.Select(w => w.Text)), c.Select(w => new CoreTranscriptWord(w.StartMs, w.EndMs, " " + w.Text)).ToList())).ToList();
        return new CoreTranscript("ar", segments);
    }

    [Fact]
    public void A_recited_surah_becomes_one_reference_and_none_of_its_words_are_kept()
    {
        var upload = TranscriptUpload.From(Transcript("قال الشيخ قل هو الله احد الله الصمد لم يلد ولم يولد ولم يكن له كفوا احد ثم قال"), Index);

        Assert.Equal(1, upload.QuranSpans);
        Assert.Equal(3, upload.Segments.Count);
        Assert.Equal("قال الشيخ", upload.Segments[0].Text);
        var span = upload.Segments[1];
        Assert.Null(span.Text);
        Assert.Equal("112:1:1-112:4:5", span.Quran!.Range);
        Assert.Equal("confirmed", span.Quran.Status);
        Assert.Equal(800, span.StartMs);
        Assert.Equal("ثم قال", upload.Segments[2].Text);
        // What is saved and sent: no word of the surah
        var json = JsonSerializer.Serialize(upload);
        foreach (var word in new[] { "الصمد", "يلد", "كفوا" }) Assert.DoesNotContain(word, json);
    }

    [Fact]
    public void An_ambiguous_ayah_is_a_review_reference_with_its_alternatives()
    {
        var upload = TranscriptUpload.From(Transcript("فباي الاء ربكما تكذبان"), Index);
        var q = Assert.Single(upload.Segments).Quran!;
        Assert.Equal("review", q.Status);
        Assert.Equal(["55:16:1-55:16:4", "55:18:1-55:18:4"], q.Alternatives!);
    }

    [Fact]
    public void Speech_keeps_its_segments_and_the_servers_rules()
    {
        var upload = TranscriptUpload.From(Transcript("السلام عليكم ورحمة الله اليوم نتكلم عن الصبر", perSegment: 4), Index);
        Assert.Equal(0, upload.QuranSpans);
        Assert.Equal(["السلام عليكم ورحمة الله", "اليوم نتكلم عن الصبر"], upload.Segments.Select(s => s.Text));
        Assert.All(upload.Segments, s => Assert.True(s.EndMs > s.StartMs));
        Assert.True(upload.Segments[1].StartMs >= upload.Segments[0].StartMs);
    }

    [Fact]
    public void Segments_without_word_times_are_spread_and_still_matched()
    {
        var t = new CoreTranscript("ar", [new CoreTranscriptSegment(0, 4000, "يا سين والقران الحكيم انك لمن المرسلين", null)]);
        var upload = TranscriptUpload.From(t, Index);
        Assert.Equal("36:1:1-36:3:3", Assert.Single(upload.Segments).Quran!.Range);
    }

    [Fact]
    public void The_upload_has_the_shape_the_server_validates()
    {
        var upload = TranscriptUpload.From(Transcript("قال الشيخ قل هو الله احد الله الصمد لم يلد ولم يولد"), Index);
        var node = JsonNode.Parse(JsonSerializer.Serialize(upload.Segments))!.AsArray();
        Assert.Equal("قال الشيخ", node[0]!["text"]!.GetValue<string>());
        Assert.Null(node[0]!["quran"]);
        Assert.Null(node[1]!["text"]);
        Assert.Equal("112:1:1-112:3:4", node[1]!["quran"]!["range"]!.GetValue<string>());
        Assert.Null(node[1]!["quran"]!["alternatives"]);
    }
}

public class TranscribeStepTests
{
    private const string Lesson = "قال الشيخ قل هو الله احد الله الصمد لم يلد ولم يولد ولم يكن له كفوا احد ثم قال";

    private static (QueueItem Item, TranscribeServer Server, ImportApiClient Api, string Work) Setup(bool transcribes = true, string? language = null, string model = "large-v3-turbo", bool sidecar = false)
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "lesson.mp4", 2500);
        if (sidecar) Temp.Write(dir, "lesson.ar.srt", "1\n00:00:00,000 --> 00:00:01,000\nسلام\n");
        var scan = FolderScanner.Scan(dir, false, null).Videos[0];
        var item = QueueItem.For(scan, "b1", "ch1", DateTimeOffset.UtcNow, transcribe: transcribes ? new TranscribeChoice(language, model) : null);
        item.ServerItemId = "item-1";
        item.VideoId = "vid-1";
        var server = new TranscribeServer();
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), server);
        return (item, server, api, Temp.Folder());
    }

    private static TranscribeStep Step(ImportApiClient api, string work, FakeTranscriber whisper, FakeMedia? media = null, List<Exception>? reported = null, TimeProvider? time = null) =>
        new(api, whisper, media ?? new FakeMedia(), new QuranIndexSource(api, time), work, (ex, _) => reported?.Add(ex), time);

    [Fact]
    public async Task Transcribes_the_sound_on_the_gpu_and_sends_references_never_quran_words()
    {
        var (item, server, api, work) = Setup();
        var whisper = new FakeTranscriber { Words = Lesson };
        var media = new FakeMedia();
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await Step(api, work, whisper, media).RunAsync(item, context, default));

        // ffmpeg's 16 kHz WAV went to whisper, with the batch's model; the language was detected
        var run = Assert.Single(whisper.Runs);
        Assert.Equal(Path.Combine(work, item.Id + ".wav"), run.Media);
        Assert.Equal("/models/large-v3-turbo.bin", run.Model);
        Assert.Null(run.Language);
        Assert.Equal(1, media.Extracts);

        var body = server.Transcript!;
        Assert.Equal("ar", body["language"]!.GetValue<string>());
        Assert.Equal("whisper.cpp", body["engine"]!["provider"]!.GetValue<string>());
        Assert.Equal("large-v3-turbo", body["engine"]!["model"]!.GetValue<string>());
        var segments = body["segments"]!.AsArray();
        Assert.Contains(segments, s => s!["quran"]?["range"]?.GetValue<string>() == "112:1:1-112:4:5");
        Assert.DoesNotContain("الصمد", body.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

        var state = item.Transcript!;
        Assert.True(state.Sent);
        Assert.Equal(1, state.QuranSpans);
        Assert.Equal("Apple M2 (Metal)", state.Device);
        Assert.Empty(Directory.GetFiles(work));
        Assert.Contains(context.Lines, l => l.Code == "transcribed" && l.Text.Contains("Apple M2 (Metal)"));
        Assert.Contains(context.Lines, l => l.Code == "transcript_sent");
        Assert.Equal(1, context.LastProgress, 3);
    }

    [Fact]
    public async Task Downloads_the_model_once_and_passes_the_batchs_language()
    {
        var (item, _, api, work) = Setup(language: "en", model: "small-q5_1");
        var whisper = new FakeTranscriber { Words = "today we talk about patience" };
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await Step(api, work, whisper).RunAsync(item, context, default));
        Assert.Equal(1, whisper.Downloads);
        Assert.Equal("en", whisper.Runs[0].Language);
        Assert.Contains(context.Lines, l => l.Code == "model_download");

        var (second, _, api2, work2) = Setup(model: "small-q5_1");
        whisper.Runs.Clear();
        await Step(api2, work2, whisper).RunAsync(second, new RecordingContext(), default);
        Assert.Equal(1, whisper.Downloads);
    }

    [Fact]
    public async Task Sidecar_captions_an_unchosen_batch_and_a_build_without_whisper_skip_it()
    {
        var whisper = new FakeTranscriber { Words = Lesson };
        var (withCaptions, _, api1, work1) = Setup(sidecar: true);
        var context = new RecordingContext();
        Assert.Equal(StepResult.Skipped, await Step(api1, work1, whisper).RunAsync(withCaptions, context, default));
        Assert.Contains(context.Lines, l => l.Code == "transcribe_sidecar");

        var (off, _, api2, work2) = Setup(transcribes: false);
        Assert.Equal(StepResult.Skipped, await Step(api2, work2, whisper).RunAsync(off, new RecordingContext(), default));

        var (noWhisper, server3, api3, work3) = Setup();
        context = new RecordingContext();
        Assert.Equal(StepResult.Skipped, await Step(api3, work3, new FakeTranscriber { Available = false }).RunAsync(noWhisper, context, default));
        Assert.Contains(context.Lines, l => l.Code == "transcribe_unavailable");
        Assert.Empty(whisper.Runs);
        Assert.Null(server3.Transcript);
    }

    [Fact]
    public async Task Without_the_servers_quran_index_nothing_is_transcribed_and_the_step_waits()
    {
        var (item, server, api, work) = Setup();
        server.IndexUnavailable = true;
        var whisper = new FakeTranscriber { Words = Lesson };
        var context = new RecordingContext();
        Assert.Equal(StepResult.Later, await Step(api, work, whisper).RunAsync(item, context, default));
        Assert.Equal(TranscribeStep.IndexRetry, context.LaterDelay);
        Assert.Empty(whisper.Runs);
        Assert.Null(item.Transcript);
        Assert.Contains(context.Lines, l => l.Code == "quran_index_wait");
    }

    [Fact]
    public async Task The_index_stays_in_memory_until_it_expires()
    {
        var (_, server, api, _) = Setup();
        var time = new ManualTime(DateTimeOffset.UtcNow);
        server.IndexExpiresAt = time.Now.AddMinutes(30);
        var source = new QuranIndexSource(api, time);
        await source.GetAsync(default);
        await source.GetAsync(default);
        Assert.Equal(1, server.IndexCalls);
        time.Now = time.Now.AddMinutes(31);
        await source.GetAsync(default);
        Assert.Equal(2, server.IndexCalls);
    }

    [Fact]
    public async Task A_local_failure_is_reported_and_the_file_finishes_without_a_transcript()
    {
        var (item, server, api, work) = Setup();
        var reported = new List<Exception>();
        var context = new RecordingContext();
        var whisper = new FakeTranscriber { Words = Lesson, Fail = new TranscribeException("transcribe", "whisper stopped") };
        Assert.Equal(StepResult.Skipped, await Step(api, work, whisper, reported: reported).RunAsync(item, context, default));
        Assert.Single(reported);
        Assert.Null(item.Transcript);
        Assert.Null(server.Transcript);
        Assert.Empty(Directory.GetFiles(work));
        Assert.Contains(context.Lines, l => l.Code == "transcribe_failed" && l.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Sound_ffmpeg_cannot_find_is_skipped_and_without_ffmpeg_whisper_reads_the_file()
    {
        var (silent, _, api1, work1) = Setup();
        var whisper = new FakeTranscriber { Words = Lesson };
        var context = new RecordingContext();
        var media = new FakeMedia { AudioFail = new LocalEncodeException("no_audio", "no sound") };
        Assert.Equal(StepResult.Skipped, await Step(api1, work1, whisper, media).RunAsync(silent, context, default));
        Assert.Contains(context.Lines, l => l.Code == "transcribe_no_audio");

        var (item, _, api2, work2) = Setup();
        media = new FakeMedia { AudioFail = new LocalEncodeException("ffmpeg_missing", "no ffmpeg") };
        Assert.Equal(StepResult.Done, await Step(api2, work2, whisper, media).RunAsync(item, new RecordingContext(), default));
        Assert.Equal(item.Video.Path, whisper.Runs.Last().Media);
    }

    [Fact]
    public async Task A_transcript_saved_before_a_restart_is_sent_without_transcribing_again()
    {
        var (item, server, api, work) = Setup();
        server.TranscriptRefusal = "busy";
        var whisper = new FakeTranscriber { Words = Lesson };
        await Assert.ThrowsAsync<ImportApiException>(() => Step(api, work, whisper).RunAsync(item, new RecordingContext(), default));
        Assert.True(File.Exists(item.Transcript!.Path));
        Assert.False(item.Transcript.Sent);

        server.TranscriptRefusal = null;
        Assert.Equal(StepResult.Done, await Step(api, work, whisper).RunAsync(item, new RecordingContext(), default));
        Assert.Single(whisper.Runs);
        Assert.True(item.Transcript.Sent);
        Assert.False(File.Exists(item.Transcript.Path));
    }

    [Fact]
    public async Task An_existing_draft_is_kept_and_a_refused_transcript_does_not_fail_the_file()
    {
        var (item, server, api, work) = Setup();
        server.TranscriptRefusal = "draft_exists";
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await Step(api, work, new FakeTranscriber { Words = Lesson }).RunAsync(item, context, default));
        Assert.Contains(context.Lines, l => l.Code == "transcript_exists");

        var (refused, server2, api2, work2) = Setup();
        server2.TranscriptRefusal = "invalid_transcript";
        var reported = new List<Exception>();
        context = new RecordingContext();
        Assert.Equal(StepResult.Skipped, await Step(api2, work2, new FakeTranscriber { Words = Lesson }, reported: reported).RunAsync(refused, context, default));
        Assert.Null(refused.Transcript);
        Assert.Single(reported);
        Assert.Contains(context.Lines, l => l.Code == "transcript_refused");
    }

    [Fact]
    public async Task Silence_sends_nothing()
    {
        var (item, server, api, work) = Setup();
        var context = new RecordingContext();
        Assert.Equal(StepResult.Skipped, await Step(api, work, new FakeTranscriber { Words = "" }).RunAsync(item, context, default));
        Assert.Contains(context.Lines, l => l.Code == "transcribe_silent");
        Assert.Null(server.Transcript);
    }
}

public class TranslateStepTests
{
    private static (QueueItem Item, TranscribeServer Server, ImportApiClient Api, ManualTime Time) Setup(string language = "ar")
    {
        var item = new QueueItem { ServerItemId = "item-1", VideoId = "vid-1" };
        var time = new ManualTime(DateTimeOffset.UtcNow);
        item.Transcript = new TranscriptState { Language = language, Sent = true, SentAt = time.Now, Model = "large-v3-turbo" };
        var server = new TranscribeServer();
        return (item, server, new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), server), time);
    }

    [Fact]
    public async Task Waits_for_the_draft_then_asks_for_the_other_site_language()
    {
        var (item, server, api, time) = Setup();
        server.Translations["en"] = new Queue<string?>(["source_missing", "video_not_ready", null]);
        var step = new TranslateStep(api, time);

        var context = new RecordingContext();
        Assert.Equal(StepResult.Later, await step.RunAsync(item, context, default));
        Assert.Equal(TimeSpan.FromSeconds(30), context.LaterDelay);
        Assert.Equal("ar", server.TranslationBody!["sourceLanguage"]!.GetValue<string>());
        Assert.Equal(["en"], server.TranslationBody["languages"]!.AsArray().Select(n => n!.GetValue<string>()));

        time.Now = time.Now.AddMinutes(15);
        context = new RecordingContext();
        Assert.Equal(StepResult.Later, await step.RunAsync(item, context, default));
        Assert.Equal(TimeSpan.FromMinutes(2), context.LaterDelay);

        context = new RecordingContext();
        Assert.Equal(StepResult.Done, await step.RunAsync(item, context, default));
        Assert.Equal(["en"], item.Transcript!.Translations);
        Assert.Contains(context.Lines, l => l.Code == "translation_requested");
    }

    [Fact]
    public async Task An_english_transcript_is_translated_into_arabic_and_an_existing_draft_is_kept()
    {
        var (item, server, api, time) = Setup("en");
        server.Translations["ar"] = new Queue<string?>(["draft_exists"]);
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await new TranslateStep(api, time).RunAsync(item, context, default));
        Assert.Equal(["ar"], server.TranslationBody!["languages"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Contains(context.Lines, l => l.Code == "translation_exists");
    }

    [Fact]
    public async Task A_refusal_is_logged_and_the_wait_ends_after_a_day()
    {
        var (paused, server, api, time) = Setup();
        server.Translations["en"] = new Queue<string?>(["translation_paused"]);
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await new TranslateStep(api, time).RunAsync(paused, context, default));
        Assert.Contains(context.Lines, l => l.Code == "translation_refused" && l.Level == LogLevel.Warning);

        var (waiting, server2, api2, time2) = Setup();
        server2.Translations["en"] = new Queue<string?>(["source_missing"]);
        time2.Now = time2.Now.AddHours(25);
        context = new RecordingContext();
        Assert.Equal(StepResult.Done, await new TranslateStep(api2, time2).RunAsync(waiting, context, default));
        Assert.Contains(context.Lines, l => l.Code == "translate_gave_up");
    }

    [Fact]
    public async Task Nothing_to_translate_without_a_sent_transcript()
    {
        var (item, server, api, time) = Setup();
        item.Transcript = null;
        Assert.Equal(StepResult.Skipped, await new TranslateStep(api, time).RunAsync(item, new RecordingContext(), default));
        Assert.Empty(server.Calls);
    }
}

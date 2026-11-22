using System.Globalization;
using System.Text.Json;
using AlUmmahNow.ToolsCore.Transcription;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>A local transcript waiting to be sent, then sent: never a Quran word, only references (TA-13).</summary>
public sealed class TranscriptState
{
    /// <summary>The upload as it will be sent (deleted once the server has it)</summary>
    public string Path { get; set; } = "";
    public string Language { get; set; } = "";
    public int Segments { get; set; }
    public int QuranSpans { get; set; }
    public string Model { get; set; } = "";
    /// <summary>Where whisper ran, as the log shows it ("Apple M2 (Metal)")</summary>
    public string Device { get; set; } = "";
    public bool Sent { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>Languages a translation was asked for, or given up on</summary>
    public List<string> Translations { get; set; } = new();
}

/// <summary>
/// The Quran matcher's index from admin-api, kept in memory only and dropped at its expiry (QF terms: a copy is
/// never kept past its freshness; nothing of it is written to disk).
/// </summary>
public sealed class QuranIndexSource
{
    private readonly ImportApiClient _api;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (QuranIndex Index, DateTimeOffset ExpiresAt)? _cached;

    public QuranIndexSource(ImportApiClient api, TimeProvider? time = null)
    {
        _api = api;
        _time = time ?? TimeProvider.System;
    }

    public async Task<QuranIndex> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is { } c && _time.GetUtcNow() < c.ExpiresAt) return c.Index;
            _cached = null;
            var reply = await _api.GetQuranIndexAsync(ct).ConfigureAwait(false);
            QuranIndex index;
            try
            {
                index = new QuranIndex(reply.Index);
            }
            catch (InvalidDataException ex)
            {
                throw new ImportApiException(200, "unreadable", "admin-api answered a Quran index the app cannot read: " + ex.Message, inner: ex);
            }
            _cached = (index, reply.ExpiresAt);
            return index;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Speech to text on this computer (wiki TOOLS-ADMIN TA-13), after the file is on the server so its upload never
/// waits for it: whisper.cpp in aun-core on the GPU when there is one, the CPU otherwise. Quran spans become verse-key
/// references before anything is saved or sent (QF terms 2026-11-21); the transcript goes up as the language's caption
/// draft. A file with sidecar captions keeps them instead. A local failure is not the file's failure: the step logs
/// why, reports it, and the file finishes without a transcript.
/// </summary>
public sealed class TranscribeStep : IImportStep
{
    private readonly ImportApiClient _api;
    private readonly ITranscriber _transcriber;
    private readonly IMediaTools _media;
    private readonly QuranIndexSource _quran;
    private readonly string _workRoot;
    private readonly Action<Exception, QueueItem>? _report;
    private readonly TimeProvider _time;

    /// <summary>How long to wait for the server's Quran index when it has no fresh copy</summary>
    public static readonly TimeSpan IndexRetry = TimeSpan.FromMinutes(10);

    public TranscribeStep(ImportApiClient api, ITranscriber transcriber, IMediaTools media, QuranIndexSource quran, string workRoot, Action<Exception, QueueItem>? report = null, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _api = api;
        _transcriber = transcriber;
        _media = media;
        _quran = quran;
        _workRoot = workRoot;
        _report = report;
    }

    public ImportStepKind Kind => ImportStepKind.Transcribe;

    public static string DefaultWorkRoot =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNowTools", "transcripts");

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        if (!item.Transcribes)
        {
            context.Log(LogLevel.Info, "transcribe_off", "Transcribe on this computer was not chosen for this file");
            return StepResult.Skipped;
        }
        if (item.Video.Captions.Count > 0)
        {
            context.Log(LogLevel.Info, "transcribe_sidecar", "The file has its own captions: they are used instead of a transcript");
            return StepResult.Skipped;
        }
        if (item.Transcript is { Sent: true }) return StepResult.Done;
        if (item.ServerItemId is null) throw new StepFailedException("not_uploaded", "The file has not been uploaded yet", retryable: false);

        // Transcribed before the app stopped: only the sending is left
        if (item.Transcript is { } saved && File.Exists(saved.Path)) return await SendAsync(item, saved, context, ct).ConfigureAwait(false);
        item.Transcript = null;

        if (!_transcriber.Available)
        {
            context.Log(LogLevel.Warning, "transcribe_unavailable", "This build of the app cannot transcribe on this computer");
            return StepResult.Skipped;
        }
        UploadStep.CheckUnchanged(item);

        // The index first: a transcript is never held, saved or sent before its Quran spans are references
        QuranIndex index;
        try
        {
            index = await _quran.GetAsync(ct).ConfigureAwait(false);
        }
        catch (ImportApiException ex) when (ex.Code == "quran_index_unavailable" || ex.IsRetryable)
        {
            context.Log(LogLevel.Info, "quran_index_wait", "Waiting for the server's Quran index before transcribing", ex.Code);
            context.Later(IndexRetry);
            return StepResult.Later;
        }

        var model = string.IsNullOrEmpty(item.SpeechModel) ? CoreTranscriber.DefaultModel : item.SpeechModel;
        var wav = System.IO.Path.Combine(_workRoot, item.Id + ".wav");
        var upload = System.IO.Path.Combine(_workRoot, item.Id + ".json");
        try
        {
            Directory.CreateDirectory(_workRoot);
            var modelPath = _transcriber.InstalledPath(model);
            // Downloading, then transcribing, share the bar: half each when the model has to come first
            var share = 1.0;
            if (modelPath is null)
            {
                context.Log(LogLevel.Info, "model_download", $"Downloading the speech model {model} (once)", model);
                modelPath = await _transcriber.DownloadAsync(model, new Progress<double>(p => context.Progress(p * 0.5)), ct).ConfigureAwait(false);
                share = 0.5;
            }
            var offset = 1 - share;

            // ffmpeg reads any file's sound; without it, aun-core reads the common audio formats itself
            var source = item.Video.Path;
            try
            {
                await _media.ExtractAudioAsync(item.Video.Path, wav, ct).ConfigureAwait(false);
                source = wav;
            }
            catch (LocalEncodeException missing) when (missing.Code == "ffmpeg_missing")
            {
            }
            catch (LocalEncodeException silent) when (silent.Code == "no_audio")
            {
                context.Log(LogLevel.Info, "transcribe_no_audio", "The file has no sound to transcribe");
                return StepResult.Skipped;
            }

            var device = _transcriber.Device;
            var where = device is null ? "" : device.Name.Length > 0 ? $"{device.Name} ({device.Backend})" : device.Backend;
            var json = await _transcriber.TranscribeAsync(modelPath, source, item.SpokenLanguage, new Progress<double>(p => context.Progress(offset + p * share)), ct).ConfigureAwait(false);
            DeleteFile(wav);

            var transcript = TranscriptUpload.From(CoreTranscript.Parse(json), index);
            if (transcript.Segments.Count == 0)
            {
                context.Log(LogLevel.Info, "transcribe_silent", "No speech was heard in this file");
                return StepResult.Skipped;
            }
            await File.WriteAllTextAsync(upload, JsonSerializer.Serialize(transcript, ImportApiClient.Json), ct).ConfigureAwait(false);
            var state = new TranscriptState
            {
                Path = upload,
                Language = transcript.Language,
                Segments = transcript.Segments.Count,
                QuranSpans = transcript.QuranSpans,
                Model = model,
                Device = where,
            };
            item.Transcript = state;
            context.Save();
            // Args: language, segments, Quran spans, where it ran (may be empty), model
            context.Log(LogLevel.Info, "transcribed",
                $"Transcribed ({transcript.Language}): {state.Segments} segments, {state.QuranSpans} Quran references, on {(where.Length > 0 ? where : "this computer")} with {model}",
                transcript.Language, Num(state.Segments), Num(state.QuranSpans), where, model);
            return await SendAsync(item, state, context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DeleteFile(wav);
            if (item.Transcript is null) DeleteFile(upload);
            throw;
        }
        catch (Exception ex) when (ex is TranscribeException or LocalEncodeException or IOException or UnauthorizedAccessException or JsonException)
        {
            // Not the file's failure: it is imported without a transcript
            DeleteFile(wav);
            DeleteFile(upload);
            item.Transcript = null;
            context.Save();
            var code = ex switch
            {
                TranscribeException t => t.Code,
                LocalEncodeException l => l.Code,
                JsonException => "transcribe",
                _ => "disk",
            };
            context.Log(LogLevel.Warning, "transcribe_failed", $"Could not transcribe here: {ex.Message}. The file is imported without a transcript", code, ex.Message);
            _report?.Invoke(ex, item);
            return StepResult.Skipped;
        }
    }

    private async Task<StepResult> SendAsync(QueueItem item, TranscriptState state, IStepContext context, CancellationToken ct)
    {
        var transcript = JsonSerializer.Deserialize<TranscriptUpload>(await File.ReadAllTextAsync(state.Path, ct).ConfigureAwait(false), ImportApiClient.Json)
            ?? throw new IOException("The saved transcript is empty");
        try
        {
            await _api.PostTranscriptAsync(item.ServerItemId!, transcript, state.Model, ct).ConfigureAwait(false);
            context.Log(LogLevel.Info, "transcript_sent", "The transcript is with the server: it becomes this language's caption draft", state.Language);
        }
        catch (ImportApiException ex) when (ex.Code is "draft_exists" or "claim_pending")
        {
            context.Log(LogLevel.Info, "transcript_exists", "This video already has a caption draft in this language: it was kept", state.Language);
        }
        catch (ImportApiException ex) when (ex.Code == "invalid_transcript")
        {
            // The server refused what the app made: reported, and the file finishes without a transcript
            DeleteFile(state.Path);
            item.Transcript = null;
            context.Save();
            context.Log(LogLevel.Warning, "transcript_refused", $"The server refused the transcript: {ex.Message}. The file is imported without one", ex.Message);
            _report?.Invoke(ex, item);
            return StepResult.Skipped;
        }
        state.Sent = true;
        state.SentAt = _time.GetUtcNow();
        DeleteFile(state.Path);
        context.Save();
        return StepResult.Done;
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next run's cleanup
        }
    }
}

/// <summary>
/// Asks the server to translate the local transcript into the other site language (TA-13, TA-20): a caption job per
/// language from the transcript's draft, which a person publishes. The draft is written by the server's claim job and
/// a translation needs the processed video, so until both exist the step waits on the server.
/// </summary>
public sealed class TranslateStep : IImportStep
{
    /// <summary>The site's languages; more come from the captions pipeline later (nothing else here is fixed to two)</summary>
    public static readonly IReadOnlyList<string> SiteLanguages = ["ar", "en"];
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(24);

    /// <summary>Answers that mean "not yet": the draft or the video is still being made</summary>
    private static readonly HashSet<string> NotYet = ["source_missing", "video_not_ready", "try_again", "queue_unavailable"];

    private readonly ImportApiClient _api;
    private readonly TimeProvider _time;

    public TranslateStep(ImportApiClient api, TimeProvider? time = null)
    {
        _api = api;
        _time = time ?? TimeProvider.System;
    }

    public ImportStepKind Kind => ImportStepKind.Translate;

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        if (item.Transcript is not { Sent: true } transcript || item.ServerItemId is null) return StepResult.Skipped;
        var wanted = SiteLanguages.Where(l => l != transcript.Language && !transcript.Translations.Contains(l)).ToList();
        if (wanted.Count == 0) return transcript.Translations.Count > 0 ? StepResult.Done : StepResult.Skipped;

        var results = await _api.RequestTranslationsAsync(item.ServerItemId, transcript.Language, wanted, ct).ConfigureAwait(false);
        var waiting = new List<string>();
        foreach (var language in wanted)
        {
            var r = results.FirstOrDefault(x => x.Language == language);
            if (r is null || (r.Error is { } e && NotYet.Contains(e)))
            {
                waiting.Add(language);
                continue;
            }
            transcript.Translations.Add(language);
            if (r.JobId is not null) context.Log(LogLevel.Info, "translation_requested", $"Translation into {language} requested: it becomes a caption draft for a person to publish", language);
            else if (r.Error == "draft_exists") context.Log(LogLevel.Info, "translation_exists", $"This video already has a caption draft in {language}: it was kept", language);
            else context.Log(LogLevel.Warning, "translation_refused", $"The server did not translate into {language}: {r.Error}", language, r.Error ?? "");
        }
        context.Save();
        if (waiting.Count == 0) return StepResult.Done;

        var waited = _time.GetUtcNow() - (transcript.SentAt ?? _time.GetUtcNow());
        if (waited > GiveUpAfter)
        {
            transcript.Translations.AddRange(waiting);
            context.Save();
            context.Log(LogLevel.Warning, "translate_gave_up", "The server has not drafted the transcript after a day; videos admin shows the caption job", string.Join(", ", waiting));
            return StepResult.Done;
        }
        context.Later(waited < TimeSpan.FromMinutes(10) ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(2));
        return StepResult.Later;
    }
}

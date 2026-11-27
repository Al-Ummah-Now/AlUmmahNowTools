using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using AlUmmahNow.ToolsCore.Import.Sources;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>The file's SHA-256 and fingerprint. A file changed since the scan is refused (scan again).</summary>
public sealed class HashStep : IImportStep
{
    private readonly SourceAccess _source;
    private readonly IMediaTools? _media;

    public HashStep(SourceAccess? source = null, IMediaTools? media = null)
    {
        _source = source ?? SourceAccess.Default;
        _media = media;
    }

    public ImportStepKind Kind => ImportStepKind.Hash;

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        var video = item.Video;
        await _source.CheckUnchangedAsync(video, context, ct).ConfigureAwait(false);
        if (await VideoCheck.RunAsync(item, _source, _media, context, ct).ConfigureAwait(false) == StepResult.NoVideo)
            return StepResult.NoVideo;
        if (item.Hashing is { } kept && (kept.Size != video.Size || kept.ModifiedMs != video.LastModifiedMs))
        {
            kept.Dispose();
            item.Hashing = null;
        }
        var state = item.Hashing ??= new HashSoFar(video.Size, video.LastModifiedMs);
        if (state.Offset > 0)
        {
            var offset = state.Offset.ToString(CultureInfo.InvariantCulture);
            context.Log(LogLevel.Info, "hash_resumed", $"The check goes on from byte {offset}", offset);
        }
        string sha256, fingerprint;
        var stream = _source.Open(video.Path, video.Size, video.LastModifiedMs, context);
        await using (stream.ConfigureAwait(false))
        {
            (sha256, fingerprint) = await FileHasher.HashAsync(stream, state, new DirectProgress(context.Progress), ct).ConfigureAwait(false);
        }
        // Read whole: whatever comes next, this hash is used up
        item.Hashing = null;
        state.Dispose();
        await _source.CheckUnchangedAsync(video, context, ct, read: true).ConfigureAwait(false);
        item.Sha256 = sha256;
        item.FingerprintHash = fingerprint;
        context.Log(LogLevel.Info, "sha", $"SHA-256 {sha256}", sha256);
        return StepResult.Done;
    }
}

/// <summary>
/// Opens the file on admin-api (a new video for the channel, or the unfinished one of this same file), sends the
/// renditions encoded here (TA-12; the server takes them only before the original completes), sends the file over TUS
/// from where the server has it, and marks it uploaded. A file the channel already has is skipped.
/// </summary>
public sealed class UploadStep : IImportStep
{
    private const long DefaultChunk = 8L * 1024 * 1024;
    private readonly ImportApiClient _api;
    private readonly TusClient _tus;
    private readonly Uri _tusEndpoint;
    private readonly Func<CancellationToken, Task<string?>> _token;
    private readonly RenditionSender _renditions;
    private readonly SourceAccess _source;
    private readonly IMediaTools? _media;

    public UploadStep(ImportApiClient api, TusClient tus, Uri videosApi, Func<CancellationToken, Task<string?>> token, string? workRoot = null, SourceAccess? source = null, IMediaTools? media = null)
    {
        _media = media;
        _source = source ?? SourceAccess.Default;
        _api = api;
        _tus = tus;
        _tusEndpoint = new Uri(videosApi, "/api/videos/tus");
        _token = token;
        _renditions = new RenditionSender(api, workRoot);
    }

    public ImportStepKind Kind => ImportStepKind.Upload;

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        if (item.Sha256 is null || item.FingerprintHash is null) throw new StepFailedException("not_hashed", "The file has not been checked yet", retryable: false);
        await _source.CheckUnchangedAsync(item.Video, context, ct).ConfigureAwait(false);
        // Hashed by a version that did not check
        if (await VideoCheck.RunAsync(item, _source, _media, context, ct).ConfigureAwait(false) == StepResult.NoVideo)
            return StepResult.NoVideo;
        if (item.StartOver) await DropAsync(item, context, ct).ConfigureAwait(false);
        if (item.ServerItemId is null || item.TusSessionId is null)
        {
            if (!await OpenAsync(_api, _tusEndpoint, item, context, ct).ConfigureAwait(false)) return StepResult.SkipRest;
        }

        // The renditions' share of the step's progress, by bytes
        var share = 0.0;
        if (!item.ServerEncodes && item.Renditions is { Sent: false } set)
        {
            share = (double)set.Bytes / Math.Max(1, set.Bytes + item.Video.Size);
            await _renditions.SendAsync(item, context, new DirectProgress(p => context.Progress(p * share)), ct).ConfigureAwait(false);
        }

        var reopened = false;
        while (true)
        {
            try
            {
                await TransferAsync(item, context, share, ct).ConfigureAwait(false);
                break;
            }
            catch (TusGoneException) when (!reopened)
            {
                // The proxy session or upload expired: admin-api opens the same video again (from its offset)
                reopened = true;
                context.Log(LogLevel.Warning, "session_ended", "The upload session ended; opening it again");
                item.TusSessionId = null;
                item.UploadUrl = null;
                if (!await OpenAsync(_api, _tusEndpoint, item, context, ct).ConfigureAwait(false)) return StepResult.SkipRest;
            }
        }

        item.Sending?.Dispose();
        item.Sending = null;
        await _api.MarkUploadedAsync(item.ServerItemId!, ct).ConfigureAwait(false);
        context.Log(LogLevel.Info, "uploaded", $"Uploaded: video {item.VideoId}", item.VideoId ?? "");
        return StepResult.Done;
    }

    /// <summary>Opens the file on admin-api (the Encode step does it first, to encode only what will be uploaded); false when the channel already has the file.</summary>
    internal static async Task<bool> OpenAsync(ImportApiClient api, Uri tusEndpoint, QueueItem item, IStepContext context, CancellationToken ct)
    {
        var v = item.Video;
        var request = new ItemUploadRequest(
            item.Sha256!,
            v.FileName,
            v.Size,
            v.MimeType,
            new UploadFingerprint(v.FileName, v.Size, v.LastModifiedMs, item.FingerprintHash!),
            v.Metadata);
        try
        {
            var opened = await api.OpenItemAsync(item.BatchId, request, ct).ConfigureAwait(false);
            item.ServerItemId = opened.Item.Id;
            item.VideoId = opened.Upload.VideoId;
            item.TusSessionId = opened.Upload.TusSessionId;
            item.ChunkSize = opened.Upload.ChunkSize > 0 ? opened.Upload.ChunkSize : DefaultChunk;
            item.UploadUrl = string.IsNullOrEmpty(opened.Upload.TusUploadId) ? null : new Uri(tusEndpoint, tusEndpoint.AbsolutePath.TrimEnd('/') + "/" + opened.Upload.TusUploadId).ToString();
            // One that was given up and is still there is not gone on with: its bytes are not the file's
            if (item.UploadUrl is not null && item.UploadUrl == item.LeftUploadUrl) item.UploadUrl = null;
            // A resumed upload keeps its offset until the server's HEAD says where it is
            if (item.UploadUrl is null) item.UploadOffset = 0;
            context.Save();
            if (opened.Upload.Reused) context.Log(LogLevel.Info, "upload_reopened", $"Upload opened again: video {item.VideoId}", item.VideoId);
            else context.Log(LogLevel.Info, "upload_opened", $"Upload opened: video {item.VideoId}", item.VideoId);
            return true;
        }
        catch (AlreadyImportedException already)
        {
            item.AlreadyImported = true;
            item.ServerItemId = already.Item.Id;
            item.VideoId = already.Item.VideoId;
            context.Save();
            context.Log(LogLevel.Info, "already_imported", $"Already in this channel: video {already.Item.VideoId}. Nothing to upload", already.Item.VideoId ?? "");
            return false;
        }
    }

    private async Task TransferAsync(QueueItem item, IStepContext context, double from, CancellationToken ct)
    {
        var size = item.Video.Size;
        var token = await Token(ct).ConfigureAwait(false);
        if (item.UploadUrl is null)
        {
            var metadata = new Dictionary<string, string>
            {
                ["filename"] = item.Video.FileName,
                ["filetype"] = item.Video.MimeType,
                ["title"] = item.Video.Metadata.Title,
                ["tusSessionId"] = item.TusSessionId!,
                ["videoId"] = item.VideoId!,
            };
            item.UploadUrl = (await _tus.CreateAsync(_tusEndpoint, token, size, metadata, ct).ConfigureAwait(false)).ToString();
            item.UploadOffset = 0;
            (item.SentTo, item.SentSha256) = (0, null);
            context.Save();
        }

        var url = new Uri(item.UploadUrl);
        var offset = await _tus.OffsetAsync(url, token, ct).ConfigureAwait(false);
        if (offset > size) throw new TusException(502, "The server has more than the file");
        item.UploadOffset = offset;
        if (item.StartOver || item.UploadUrl == item.LeftUploadUrl)
        {
            if (offset > 0) throw new StepFailedException("upload_left", "The server still has the upload that was given up", retryable: true);
            item.StartOver = false;
            context.Save();
        }
        await using var file = _source.Open(item.Video.Path, size, item.Video.LastModifiedMs, context);
        while (offset < size)
        {
            ct.ThrowIfCancellationRequested();
            var sent = await SentAsync(item, file, offset, context, ct).ConfigureAwait(false);
            var again = sent.Offset > offset;
            var part = new byte[again ? sent.Offset - offset : Math.Min(item.ChunkSize, size - offset)];
            file.Position = offset;
            await file.ReadExactlyAsync(part, ct).ConfigureAwait(false);
            if (!again) sent.AppendPart(part);
            else if (!sent.IsLastPart(part)) throw await DifferentAsync(item, context, ct).ConfigureAwait(false);
            if (sent.Offset >= size)
            {
                // Other apps may write to the file while it is sent, and a share may read differently the second time: the last
                // part only goes when the file is the one that was checked, and what was sent has its SHA-256
                await _source.CheckUnchangedAsync(item.Video, context, ct, read: true).ConfigureAwait(false);
                if (sent.Sha256 != item.Sha256) throw await DifferentAsync(item, context, ct).ConfigureAwait(false);
            }
            token = await Token(ct).ConfigureAwait(false);
            try
            {
                offset = await _tus.PatchAsync(url, token, part, offset, ct).ConfigureAwait(false);
            }
            catch (TusException conflict) when (conflict.Status == 409)
            {
                // The server holds a different offset (a chunk landed but its answer was lost): continue from its offset
                offset = await _tus.OffsetAsync(url, token, ct).ConfigureAwait(false);
            }
            // Also when only the answer was lost
            if (offset == sent.Offset) (item.SentTo, item.SentSha256) = (offset, sent.Sha256);
            if (offset > size) throw new TusException(502, "The server has more than the file");
            item.UploadOffset = offset;
            context.Save();
            context.Progress(from + ((1 - from) * (size == 0 ? 1 : (double)offset / size)));
        }
    }

    /// <summary>The hash of what the server has. What was sent before this start of the app is read again, and must be the same.</summary>
    private async Task<SentSoFar> SentAsync(QueueItem item, Stream file, long offset, IStepContext context, CancellationToken ct)
    {
        var video = item.Video;
        var known = item.SentSha256 is not null && item.SentTo > 0 && item.SentTo <= offset ? item.SentTo : 0;
        SentSoFar sent;
        if (item.Sending is { } kept && kept.Size == video.Size && kept.ModifiedMs == video.LastModifiedMs)
        {
            if (kept.Covers(offset)) return kept;
            // A prefix check cut short by a dropped share or a pause goes on from where it stopped
            if (kept.Offset > 0 && kept.Offset < offset && (known == 0 || kept.Offset <= known || kept.VerifiedKnown == known))
            {
                sent = kept;
                var at = sent.Offset.ToString(CultureInfo.InvariantCulture);
                context.Log(LogLevel.Info, "hash_resumed", $"The check goes on from byte {at}", at);
            }
            else
            {
                kept.Dispose();
                sent = item.Sending = new SentSoFar(video.Size, video.LastModifiedMs);
            }
        }
        else
        {
            item.Sending?.Dispose();
            sent = item.Sending = new SentSoFar(video.Size, video.LastModifiedMs);
        }
        if (offset == 0) return sent;
        if (sent.Offset == 0)
        {
            var from = offset.ToString(CultureInfo.InvariantCulture);
            context.Log(LogLevel.Info, "sent_check", $"The first {from} bytes were sent before; they are checked again", from);
        }
        if (known > 0 && sent.Offset == known && sent.VerifiedKnown != known)
        {
            if (sent.Sha256 != item.SentSha256) throw await DifferentAsync(item, context, ct).ConfigureAwait(false);
            sent.VerifiedKnown = known;
        }
        var buffer = new byte[1 << 20];
        file.Position = sent.Offset;
        while (sent.Offset < offset)
        {
            var until = sent.Offset < known ? known : offset;
            var n = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, until - sent.Offset)), ct).ConfigureAwait(false);
            if (n == 0) throw SourceAccess.Changed(read: true);
            sent.Append(buffer.AsSpan(0, n));
            if (known > 0 && sent.Offset == known)
            {
                if (sent.Sha256 != item.SentSha256) throw await DifferentAsync(item, context, ct).ConfigureAwait(false);
                sent.VerifiedKnown = known;
            }
        }
        return sent;
    }

    /// <summary>What was sent is not the file that was checked: the server's copy is given up, now or at the next try.</summary>
    private async Task<StepFailedException> DifferentAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        item.StartOver = true;
        context.Save();
        try
        {
            await DropAsync(item, context, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TusException or ImportApiException)
        {
            // Its next try begins with it
        }
        return new StepFailedException("content_changed", "What was read to be sent is not what was checked before", retryable: false);
    }

    private async Task DropAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        item.Sending?.Dispose();
        item.Sending = null;
        if (item.UploadUrl is { } url)
        {
            try
            {
                await _tus.DeleteAsync(new Uri(url), await Token(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            }
            catch (TusGoneException)
            {
                // Gone already
            }
            catch (TusException refused) when (refused.Status is >= 400 and < 500 and not (401 or 408 or 423 or 429))
            {
                // Trying again would be refused again: the upload is left to the server, and another one begun
                item.LeftUploadUrl = url;
                var status = refused.Status.ToString(CultureInfo.InvariantCulture);
                context.Log(LogLevel.Warning, "upload_kept", $"The server kept the stopped upload ({status}); another one is begun", status);
            }
        }
        (item.UploadUrl, item.TusSessionId, item.UploadOffset) = (null, null, 0);
        (item.SentTo, item.SentSha256) = (0, null);
        context.Save();
    }

    private async Task<string> Token(CancellationToken ct) =>
        await _token(ct).ConfigureAwait(false) ?? throw new ImportApiException(401, "signed_out", "Sign in again");
}

/// <summary>
/// Sends the file's captions, chapters, thumbnail, playlist and publish date to admin-api, which queues the attach
/// job on the server (its state, retries and errors are on the Jobs and Errors pages of videos admin).
/// </summary>
public sealed class AttachStep : IImportStep
{
    public const int MaxCaptionChars = 2 * 1024 * 1024;
    public const long MaxThumbnailBytes = 6L * 1024 * 1024;
    private readonly ImportApiClient _api;
    private readonly SourceAccess _source;

    public AttachStep(ImportApiClient api, SourceAccess? source = null)
    {
        _api = api;
        _source = source ?? SourceAccess.Default;
    }

    public ImportStepKind Kind => ImportStepKind.Attach;

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        if (item.ServerItemId is null) throw new StepFailedException("not_uploaded", "The file was not uploaded", retryable: false);
        var payload = await BuildAsync(item.Video, _source, context, ct).ConfigureAwait(false);
        if (payload.IsEmpty) return StepResult.Skipped;
        var jobId = await _api.AttachSidecarsAsync(item.ServerItemId, payload, ct).ConfigureAwait(false);
        item.AttachJobId = jobId;
        // Arguments: the job, then each part sent (captions as "captions=ar,en")
        var parts = new List<string>();
        if (payload.Captions is { Count: > 0 } c) parts.Add("captions=" + string.Join(",", c.Select(t => t.Language)));
        if (payload.Chapters is not null) parts.Add("chapters");
        if (payload.Thumbnail is not null) parts.Add("thumbnail");
        if (payload.PlaylistId is not null) parts.Add("playlist");
        if (payload.PublishAt is not null) parts.Add("publish_date");
        context.Log(LogLevel.Info, "sent", $"Sent {string.Join(", ", parts)}; the server attaches them (job {jobId})", new[] { jobId ?? "" }.Concat(parts).ToArray());
        return StepResult.Done;
    }

    /// <summary>
    /// What can be sent: a part that cannot be read is left out with a line in the log. A share that dropped fails the step
    /// instead, so nothing is left out because of it.
    /// </summary>
    internal static async Task<SidecarPayload> BuildAsync(ScannedVideo video, SourceAccess source, IStepContext context, CancellationToken ct)
    {
        var captions = new List<CaptionSidecar>();
        foreach (var caption in video.Captions)
        {
            var text = await Read(source, caption.Path, context, ct).ConfigureAwait(false);
            if (text is null) continue;
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxCaptionChars)
            {
                context.Log(LogLevel.Warning, "left_out_size", $"{Path.GetFileName(caption.Path)} is empty or larger than 2 MB; left out", Path.GetFileName(caption.Path), "2");
                continue;
            }
            captions.Add(new CaptionSidecar(caption.Language, text));
        }

        string? chapters = null;
        if (video.ChaptersPath is not null)
        {
            try
            {
                chapters = ChapterFile.Parse(video.ChaptersPath, await source.ReadAllTextAsync(video.ChaptersPath, context, ct).ConfigureAwait(false));
                if (chapters.Length == 0) chapters = null;
            }
            catch (Exception ex) when (ex is FormatException || LeftOut(ex))
            {
                context.Log(LogLevel.Warning, "left_out_unreadable", $"{Path.GetFileName(video.ChaptersPath)} could not be read ({Why(ex)}); left out", Path.GetFileName(video.ChaptersPath), Why(ex));
            }
        }

        ThumbnailSidecar? thumbnail = null;
        if (video.ThumbnailPath is not null)
        {
            try
            {
                var name = Path.GetFileName(video.ThumbnailPath);
                if ((await source.StatAsync(video.ThumbnailPath, context, ct).ConfigureAwait(false)).Length > MaxThumbnailBytes)
                {
                    context.Log(LogLevel.Warning, "left_out_size", $"{name} is larger than 6 MB; left out", name, "6");
                }
                else
                {
                    var bytes = await source.ReadAllBytesAsync(video.ThumbnailPath, context, ct).ConfigureAwait(false);
                    thumbnail = new ThumbnailSidecar(Convert.ToBase64String(bytes), FolderScanner.ImageTypes[Path.GetExtension(name)]);
                }
            }
            catch (Exception ex) when (LeftOut(ex))
            {
                context.Log(LogLevel.Warning, "left_out_unreadable", $"{Path.GetFileName(video.ThumbnailPath)} could not be read ({Why(ex)}); left out", Path.GetFileName(video.ThumbnailPath), Why(ex));
            }
        }

        return new SidecarPayload(
            captions.Count > 0 ? captions : null,
            chapters,
            thumbnail,
            video.Metadata.PlaylistId,
            video.Metadata.PublishAt);
    }

    /// <summary>The part alone cannot be read: not a share or drive that is away, which a later try finds again.</summary>
    private static bool LeftOut(Exception ex) => ex switch
    {
        SourceReadException r => r.Failure.Kind != ReadKind.Network && r.Failure.Code is not ("share_credentials" or "drive_gone"),
        StepFailedException f => f.Code is "file_writing" or "file_changed",
        _ => ex is IOException or UnauthorizedAccessException,
    };

    private static string Why(Exception ex) => ex is IOException or UnauthorizedAccessException ? ImportQueue.Describe(ex) : ex.Message;

    private static async Task<string?> Read(SourceAccess source, string path, IStepContext context, CancellationToken ct)
    {
        try
        {
            return await source.ReadAllTextAsync(path, context, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (LeftOut(ex))
        {
            context.Log(LogLevel.Warning, "left_out_unreadable", $"{Path.GetFileName(path)} could not be read ({Why(ex)}); left out", Path.GetFileName(path), Why(ex));
            return null;
        }
    }
}

/// <summary>
/// Sends the app's errors to the Errors page (app <c>tools</c>) in the background: batches of up to 20, kept and
/// sent again when admin-api cannot take them, at most 200 waiting. Reporting never throws into the app.
/// </summary>
public sealed class ErrorReporter : IDisposable
{
    private const int MaxWaiting = 200;
    private readonly Func<IReadOnlyList<ErrorReport>, CancellationToken, Task> _send;
    private readonly string _release;
    private readonly Func<string> _locale;
    private readonly ConcurrentQueue<ErrorReport> _waiting = new();
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly Timer _timer;

    public ErrorReporter(Func<IReadOnlyList<ErrorReport>, CancellationToken, Task> send, string release, Func<string> locale, TimeSpan? every = null)
    {
        _send = send;
        _release = release;
        _locale = locale;
        var period = every ?? TimeSpan.FromSeconds(15);
        _timer = new Timer(_ => _ = FlushAsync(), null, period, period);
    }

    public int Waiting => _waiting.Count;

    /// <summary>Queues a report; answers its reference (ERR-XXXX-XXXX) to show or log.</summary>
    public string Report(string kind, Exception error, string message, string? route = null, IReadOnlyDictionary<string, object?>? extra = null, string? reference = null, IEnumerable<string>? files = null)
    {
        var known = files?.ToList();
        var report = new ErrorReport(
            kind,
            reference ?? ErrorReport.NewRef(),
            LocalPaths.Clean(message, Plain(error), known),
            Name: error.GetType().Name,
            Stack: kind is "unhandled" or "rejection" ? Stack(error, known) : null,
            Route: route,
            Status: error switch { ImportApiException a when a.Status > 0 => a.Status, TusException t when t.Status > 0 => t.Status, _ => null },
            Release: _release,
            Locale: _locale(),
            OccurredAt: DateTimeOffset.UtcNow.ToString("O"),
            Extra: extra?.ToDictionary(e => e.Key, e => e.Value is string text ? LocalPaths.Clean(text, "…", known) : e.Value));
        _waiting.Enqueue(report);
        while (_waiting.Count > MaxWaiting && _waiting.TryDequeue(out _)) { }
        return report.Ref;
    }

    /// <summary>What went wrong without a word of the system's or ffmpeg's: the code, and the code it ended with.</summary>
    internal static string Plain(Exception error)
    {
        var code = error switch { LocalEncodeException l => l.Code, Transcription.TranscribeException t => t.Code, _ => ImportQueue.ErrorCode(error) };
        var what = code switch
        {
            "probe_failed" or "no_media" => "ffprobe could not read the file",
            "source_read" => "The file could not be read to its end",
            "ffmpeg_failed" => "ffmpeg stopped",
            "ffmpeg_missing" => "ffmpeg could not be started",
            _ => error is LocalEncodeException ? "The file could not be encoded here" : "It failed",
        };
        return what + " (" + code + (error is LocalEncodeException { ExitCode: { } exit } ? ", " + exit.ToString(CultureInfo.InvariantCulture) : "") + ")";
    }

    /// <summary>The methods; where their source was when it was built is nobody's.</summary>
    private static string? Stack(Exception error, IReadOnlyList<string>? files) =>
        error.StackTrace is { } stack ? LocalPaths.Clean(System.Text.RegularExpressions.Regex.Replace(stack, @"\) in [^\r\n]*", ")"), "…", files) : null;

    /// <summary>Sends what is waiting; what admin-api does not take stays for the next try.</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (!await _flushing.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            while (!_waiting.IsEmpty)
            {
                var batch = new List<ErrorReport>();
                while (batch.Count < 20 && _waiting.TryPeek(out var next))
                {
                    batch.Add(next);
                    _waiting.TryDequeue(out _);
                }
                try
                {
                    await _send(batch, ct).ConfigureAwait(false);
                }
                catch (ImportApiException refused) when (!refused.IsRetryable)
                {
                    // Refused as invalid (or signed out): sending it again would be refused again
                    continue;
                }
                catch (Exception)
                {
                    foreach (var r in batch) _waiting.Enqueue(r);
                    return;
                }
            }
        }
        finally
        {
            _flushing.Release();
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _flushing.Dispose();
    }
}

/// <summary>A file that failed for good: admin-api's item says why, and the Errors page gets a report with its reference.</summary>
public sealed class ServerFailureSink : IImportFailureSink
{
    private readonly ImportApiClient _api;
    private readonly ErrorReporter _reporter;

    public ServerFailureSink(ImportApiClient api, ErrorReporter reporter)
    {
        _api = api;
        _reporter = reporter;
    }

    public async Task ItemFailedAsync(QueueItem item, StepState step, Exception error, string reference, CancellationToken ct)
    {
        if (error is StepFailedException { Code: "no_video" }) return;
        // It goes to the server: no folders of this computer
        var files = LocalPaths.Of(item.Video).ToList();
        var message = LocalPaths.Clean(ImportQueue.Describe(error), ErrorReporter.Plain(error), files);
        var answer = StepFailedException.Answers(error);
        var known = error is StepFailedException or ImportApiException or TusException or TusGoneException or IOException or UnauthorizedAccessException or HttpRequestException;
        if (!answer) _reporter.Report(
            known ? "upload" : "unhandled",
            error,
            $"Import {step.Kind} failed: {message}",
            route: "/import/" + step.Kind.ToString().ToLowerInvariant(),
            extra: new Dictionary<string, object?>
            {
                ["step"] = step.Kind.ToString(),
                ["attempts"] = step.Attempts,
                ["itemId"] = item.ServerItemId,
                ["videoId"] = item.VideoId,
                ["fileSize"] = item.Video.Size,
            },
            reference: reference,
            files: files);
        // The encode opens the file's video on the server before its upload
        if (step.Kind is ImportStepKind.Upload or ImportStepKind.Encode && item.ServerItemId is not null && !item.AlreadyImported)
        {
            await _api.MarkFailedAsync(item.ServerItemId, answer ? message : $"{message} (reference {reference})", ct).ConfigureAwait(false);
        }
        await _reporter.FlushAsync(ct).ConfigureAwait(false);
    }
}

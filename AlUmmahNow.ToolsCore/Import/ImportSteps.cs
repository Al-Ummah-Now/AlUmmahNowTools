using System.Collections.Concurrent;
using System.Text;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>The file's SHA-256 and fingerprint. A file changed since the scan is refused (scan again).</summary>
public sealed class HashStep : IImportStep
{
    public ImportStepKind Kind => ImportStepKind.Hash;

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        UploadStep.CheckUnchanged(item);
        var progress = new Progress<double>(context.Progress);
        var (sha256, fingerprint) = await FileHasher.HashAsync(item.Video.Path, progress, ct).ConfigureAwait(false);
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

    public UploadStep(ImportApiClient api, TusClient tus, Uri videosApi, Func<CancellationToken, Task<string?>> token, string? workRoot = null)
    {
        _api = api;
        _tus = tus;
        _tusEndpoint = new Uri(videosApi, "/api/videos/tus");
        _token = token;
        _renditions = new RenditionSender(api, workRoot);
    }

    public ImportStepKind Kind => ImportStepKind.Upload;

    /// <summary>The file must still be the one that was scanned and hashed.</summary>
    internal static void CheckUnchanged(QueueItem item)
    {
        var info = new FileInfo(item.Video.Path);
        if (!info.Exists) throw new StepFailedException("file_missing", "The file is no longer in its folder", retryable: false);
        if (info.LinkTarget is not null) throw new StepFailedException("file_link", "The file is now a link to another file. Scan the folder again", retryable: false);
        var modified = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        if (info.Length != item.Video.Size || modified != item.Video.LastModifiedMs)
        {
            throw new StepFailedException("file_changed", "The file changed after it was scanned. Scan the folder again", retryable: false);
        }
    }

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        if (item.Sha256 is null || item.FingerprintHash is null) throw new StepFailedException("not_hashed", "The file has not been checked yet", retryable: false);
        CheckUnchanged(item);
        if (item.ServerItemId is null || item.TusSessionId is null)
        {
            if (!await OpenAsync(_api, _tusEndpoint, item, context, ct).ConfigureAwait(false)) return StepResult.SkipRest;
        }

        // The renditions' share of the step's progress, by bytes
        var share = 0.0;
        if (item.Renditions is { Sent: false } set)
        {
            share = (double)set.Bytes / Math.Max(1, set.Bytes + item.Video.Size);
            await _renditions.SendAsync(item, context, new Progress<double>(p => context.Progress(p * share)), ct).ConfigureAwait(false);
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
            context.Save();
        }

        var url = new Uri(item.UploadUrl);
        var offset = await _tus.OffsetAsync(url, token, ct).ConfigureAwait(false);
        item.UploadOffset = offset;
        await using var file = new FileStream(item.Video.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous);
        while (offset < size)
        {
            ct.ThrowIfCancellationRequested();
            var count = (int)Math.Min(item.ChunkSize, size - offset);
            token = await Token(ct).ConfigureAwait(false);
            try
            {
                offset = await _tus.PatchAsync(url, token, file, offset, count, ct).ConfigureAwait(false);
            }
            catch (TusException conflict) when (conflict.Status == 409)
            {
                // The server holds a different offset (a chunk landed but its answer was lost): continue from its offset
                offset = await _tus.OffsetAsync(url, token, ct).ConfigureAwait(false);
            }
            item.UploadOffset = offset;
            context.Save();
            context.Progress(from + ((1 - from) * (size == 0 ? 1 : (double)offset / size)));
        }
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

    public AttachStep(ImportApiClient api) => _api = api;

    public ImportStepKind Kind => ImportStepKind.Attach;

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        if (item.ServerItemId is null) throw new StepFailedException("not_uploaded", "The file was not uploaded", retryable: false);
        var payload = await BuildAsync(item.Video, context, ct).ConfigureAwait(false);
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

    /// <summary>What can be sent: a part that cannot be read is left out with a line in the log.</summary>
    internal static async Task<SidecarPayload> BuildAsync(ScannedVideo video, IStepContext context, CancellationToken ct)
    {
        var captions = new List<CaptionSidecar>();
        foreach (var caption in video.Captions)
        {
            var text = await Read(caption.Path, context, ct).ConfigureAwait(false);
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
                chapters = ChapterFile.Read(video.ChaptersPath);
                if (chapters.Length == 0) chapters = null;
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                context.Log(LogLevel.Warning, "left_out_unreadable", $"{Path.GetFileName(video.ChaptersPath)} could not be read ({ex.Message}); left out", Path.GetFileName(video.ChaptersPath), ex.Message);
            }
        }

        ThumbnailSidecar? thumbnail = null;
        if (video.ThumbnailPath is not null)
        {
            try
            {
                var info = new FileInfo(video.ThumbnailPath);
                if (info.Length > MaxThumbnailBytes)
                {
                    context.Log(LogLevel.Warning, "left_out_size", $"{info.Name} is larger than 6 MB; left out", info.Name, "6");
                }
                else
                {
                    var bytes = await File.ReadAllBytesAsync(video.ThumbnailPath, ct).ConfigureAwait(false);
                    thumbnail = new ThumbnailSidecar(Convert.ToBase64String(bytes), FolderScanner.ImageTypes[info.Extension]);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Log(LogLevel.Warning, "left_out_unreadable", $"{Path.GetFileName(video.ThumbnailPath)} could not be read ({ex.Message}); left out", Path.GetFileName(video.ThumbnailPath), ex.Message);
            }
        }

        return new SidecarPayload(
            captions.Count > 0 ? captions : null,
            chapters,
            thumbnail,
            video.Metadata.PlaylistId,
            video.Metadata.PublishAt);
    }

    private static async Task<string?> Read(string path, IStepContext context, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Log(LogLevel.Warning, "left_out_unreadable", $"{Path.GetFileName(path)} could not be read ({ex.Message}); left out", Path.GetFileName(path), ex.Message);
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
    public string Report(string kind, Exception error, string message, string? route = null, IReadOnlyDictionary<string, object?>? extra = null, string? reference = null)
    {
        var report = new ErrorReport(
            kind,
            reference ?? ErrorReport.NewRef(),
            message,
            Name: error.GetType().Name,
            Stack: kind is "unhandled" or "rejection" ? error.StackTrace : null,
            Route: route,
            Status: error switch { ImportApiException a when a.Status > 0 => a.Status, TusException t when t.Status > 0 => t.Status, _ => null },
            Release: _release,
            Locale: _locale(),
            OccurredAt: DateTimeOffset.UtcNow.ToString("O"),
            Extra: extra);
        _waiting.Enqueue(report);
        while (_waiting.Count > MaxWaiting && _waiting.TryDequeue(out _)) { }
        return report.Ref;
    }

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
        var message = ImportQueue.Describe(error);
        var known = error is StepFailedException or ImportApiException or TusException or TusGoneException or IOException or UnauthorizedAccessException or HttpRequestException;
        _reporter.Report(
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
            reference: reference);
        if (step.Kind == ImportStepKind.Upload && item.ServerItemId is not null && !item.AlreadyImported)
        {
            await _api.MarkFailedAsync(item.ServerItemId, $"{message} (reference {reference})", ct).ConfigureAwait(false);
        }
        await _reporter.FlushAsync(ct).ConfigureAwait(false);
    }
}

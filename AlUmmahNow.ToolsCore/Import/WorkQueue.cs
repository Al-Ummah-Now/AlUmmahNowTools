using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlUmmahNow.ToolsCore.Import.Sources;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>The steps a file goes through, in order. Later stories add theirs (suggest).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImportStepKind
{
    Hash,
    /// <summary>The server's HLS ladder encoded on this computer (TA-12), unless the server encodes</summary>
    Encode,
    Upload,
    Attach,
    /// <summary>Speech to text on this computer, sent as the caption draft (TA-13); after the upload, so it never holds it up</summary>
    Transcribe,
    /// <summary>Waits for the server's verdict on the renditions: used, or encoded again and why</summary>
    Check,
    /// <summary>Asks the server to translate the transcript into the other site language (TA-13, TA-20)</summary>
    Translate,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepStatus
{
    Pending,
    Running,
    Done,
    /// <summary>Nothing to do (no sidecars; the file was already in the channel)</summary>
    Skipped,
    /// <summary>Gave up after its attempts, or a failure that retrying cannot fix: waits for Retry</summary>
    Failed,
}

public sealed class StepState
{
    public ImportStepKind Kind { get; set; }
    public StepStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    /// <summary>The failure in English, as the server or the system put it (for the log and support)</summary>
    public string? LastError { get; set; }
    /// <summary>What kind of failure it was, for the app's words (<see cref="ImportQueue.ErrorCode"/>)</summary>
    public string? LastErrorCode { get; set; }
    /// <summary>A failed read: the system's number and text and the offset, for the app's words</summary>
    public ReadError? ReadError { get; set; }
    /// <summary>The step is waiting on the server's own work (its transcode), not on a retry</summary>
    public bool WaitingOnServer { get; set; }
    /// <summary>
    /// The step gave up on a failure that trying again may fix (the network, a busy server), so it can be paused and
    /// resumed. Null in queues saved by older apps: <see cref="CanTryAgain"/> then reads the failure's code.
    /// </summary>
    public bool? Retryable { get; set; }

    [JsonIgnore]
    public bool CanTryAgain => Retryable ?? LastErrorCode switch
    {
        "admin_unreachable" or "videos_unreachable" or "upload_gone" or "file_read" or "renditions_incomplete" => true,
        // Either kind: one that retrying cannot fix gave up at once
        "server" or "upload_refused" or "unexpected" => Attempts > 1,
        _ => false,
    };
}

/// <summary>What the system said about a failed read ("Windows", 59, "An unexpected network error occurred") and where it stopped.</summary>
public sealed record ReadError(string System, int? Number, string Text, long? Offset)
{
    internal static ReadError? Of(Exception error) => error switch
    {
        SourceReadException r => new ReadError(r.Failure.SystemName, r.Failure.Number, r.Failure.Text, r.Offset),
        StepFailedException => null,
        IOException or UnauthorizedAccessException when ReadFailure.Of(error) is { Number: not null } f => new ReadError(f.SystemName, f.Number, f.Text, null),
        _ => null,
    };

    /// <summary>As a log line's arguments, after the line's own.</summary>
    internal string[] Args => [System, Number?.ToString(CultureInfo.InvariantCulture) ?? "", Text, Offset?.ToString(CultureInfo.InvariantCulture) ?? ""];
}

/// <summary>What a running step is waiting for.</summary>
public enum StepWait
{
    None,
    /// <summary>The network share stopped answering</summary>
    Share,
    /// <summary>The file comes down from cloud storage first</summary>
    Cloud,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A line of a file's log: the English text (copied for support), and a code with its arguments so the app can show
/// the line in its own language. An argument naming a step is the step's <see cref="ImportStepKind"/> name.
/// </summary>
public sealed record LogLine(DateTimeOffset At, LogLevel Level, string Text, string? Code = null, string[]? Args = null);

/// <summary>How a file stands, for the queue's table.</summary>
public enum ItemPhase
{
    Waiting,
    Running,
    /// <summary>A step failed and is tried again at <see cref="QueueItem.NextAttemptAt"/></summary>
    RetryWaiting,
    /// <summary>The server is working on the file (its transcode); the app looks again at <see cref="QueueItem.NextAttemptAt"/></summary>
    ServerWorking,
    Paused,
    Failed,
    Done,
}

/// <summary>One file in the queue: what it is, where each step stands, what the server gave it and its log.</summary>
public sealed class QueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BatchId { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public ScannedVideo Video { get; set; } = null!;
    public bool Paused { get; set; }
    public List<StepState> Steps { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }

    public string? Sha256 { get; set; }
    public string? FingerprintHash { get; set; }
    public string? ServerItemId { get; set; }
    public string? VideoId { get; set; }
    public string? TusSessionId { get; set; }
    public string? UploadUrl { get; set; }
    public long UploadOffset { get; set; }
    public long ChunkSize { get; set; }
    public string? AttachJobId { get; set; }
    /// <summary>The channel already had this file: nothing was uploaded</summary>
    public bool AlreadyImported { get; set; }
    /// <summary>"Let the server encode" was chosen for the file's batch: no local encode</summary>
    public bool ServerEncodes { get; set; }
    /// <summary>The renditions encoded here, waiting to be sent (deleted once the server has them)</summary>
    public RenditionSet? Renditions { get; set; }
    /// <summary>The server's verdict on the renditions, once its transcode has run</summary>
    public RenditionVerdict? Verdict { get; set; }
    /// <summary>"Transcribe on this computer" was chosen for the file's batch (TA-13)</summary>
    public bool Transcribes { get; set; }
    /// <summary>The batch's spoken language (ISO 639-1), or null to detect it per file</summary>
    public string? SpokenLanguage { get; set; }
    /// <summary>The speech model the batch chose (aun-core catalog id)</summary>
    public string? SpeechModel { get; set; }
    /// <summary>The transcript made here, waiting to be sent or sent</summary>
    public TranscriptState? Transcript { get; set; }
    /// <summary>"Ask the server to translate the captions" was chosen for the file's batch (TA-13, TA-20)</summary>
    public bool Translates { get; set; }
    /// <summary>Languages asked for (or given up on) from the file's own caption files, which the server reads published</summary>
    public List<string> CaptionTranslations { get; set; } = new();
    /// <summary>When the first translation of the file's own captions was asked for (the day's wait counts from it)</summary>
    public DateTimeOffset? TranslateSince { get; set; }
    public List<LogLine> Log { get; set; } = new();

    /// <summary>Progress of the running step, 0–1 (not saved)</summary>
    [JsonIgnore] public double Progress { get; internal set; }

    /// <summary>What the running step is waiting for (not saved)</summary>
    [JsonIgnore] public StepWait Wait { get; internal set; }

    /// <summary>The hash so far, kept over a failed attempt and a pause (not saved: a restart hashes from the start)</summary>
    [JsonIgnore] internal HashSoFar? Hashing { get; set; }

    [JsonIgnore] public StepState? CurrentStep => Steps.FirstOrDefault(s => s.Status is StepStatus.Pending or StepStatus.Running or StepStatus.Failed);
    [JsonIgnore] public DateTimeOffset? NextAttemptAt => CurrentStep?.NextAttemptAt;

    [JsonIgnore]
    public ItemPhase Phase
    {
        get
        {
            var step = CurrentStep;
            if (step is null) return ItemPhase.Done;
            // A failure that trying again cannot fix stays in sight, paused or not
            if (step.Status == StepStatus.Failed && !step.CanTryAgain) return ItemPhase.Failed;
            // Paused at once, also while a running step is still stopping: nothing of the file starts until Resume
            if (Paused) return ItemPhase.Paused;
            if (step.Status == StepStatus.Failed) return ItemPhase.Failed;
            if (step.Status == StepStatus.Running) return ItemPhase.Running;
            if (step.WaitingOnServer) return ItemPhase.ServerWorking;
            return step.NextAttemptAt is not null ? ItemPhase.RetryWaiting : ItemPhase.Waiting;
        }
    }

    /// <summary>What the row offers: exactly one of Pause and Resume for a file that is neither done nor failed.</summary>
    [JsonIgnore] public bool CanPause => Phase is ItemPhase.Waiting or ItemPhase.Running or ItemPhase.RetryWaiting or ItemPhase.ServerWorking;
    [JsonIgnore] public bool CanResume => Phase == ItemPhase.Paused;
    [JsonIgnore] public bool CanRetry => Phase is ItemPhase.Failed or ItemPhase.RetryWaiting;

    /// <summary>
    /// Pause all takes it: not done, not paused, and not failed for good. Wider than <see cref="CanPause"/>: the row of a file
    /// that gave up on something trying again may fix offers Retry, and Pause all still holds it.
    /// </summary>
    [JsonIgnore] public bool Pausable => !Paused && Phase != ItemPhase.Done && CurrentStep is not { Status: StepStatus.Failed, CanTryAgain: false };

    /// <summary>Its upload began and did not finish: removing the file stops it.</summary>
    [JsonIgnore]
    public bool UploadUnfinished => Steps.FirstOrDefault(s => s.Kind == ImportStepKind.Upload) is { Status: not (StepStatus.Done or StepStatus.Skipped) } upload
        && (upload.Status == StepStatus.Running || UploadUrl is not null);

    public static QueueItem For(ScannedVideo video, string batchId, string channelId, DateTimeOffset now, bool serverEncodes = false, TranscribeChoice? transcribe = null, bool translate = false) => new()
    {
        BatchId = batchId,
        ChannelId = channelId,
        Video = video,
        CreatedAt = now,
        ServerEncodes = serverEncodes,
        Transcribes = transcribe is not null,
        SpokenLanguage = transcribe?.Language,
        SpeechModel = transcribe?.Model,
        Translates = translate,
        Steps = Enum.GetValues<ImportStepKind>().Select(k => new StepState { Kind = k }).ToList(),
    };

    /// <summary>
    /// A file saved by an older app has fewer steps: the missing ones are put in their places, as nothing to do
    /// (the file was queued before they existed), so its saved steps keep their meaning.
    /// </summary>
    internal void AddMissingSteps()
    {
        var kinds = Enum.GetValues<ImportStepKind>();
        if (Steps.Count == kinds.Length && Steps.Select(s => s.Kind).SequenceEqual(kinds)) return;
        var saved = Steps.GroupBy(s => s.Kind).ToDictionary(g => g.Key, g => g.First());
        Steps = kinds.Select(k => saved.TryGetValue(k, out var s) ? s : new StepState { Kind = k, Status = StepStatus.Skipped }).ToList();
    }
}

/// <summary>A batch's transcription choice: the spoken language (null: detect it per file) and the speech model.</summary>
public sealed record TranscribeChoice(string? Language, string Model);

/// <summary>A step's outcome when it did not throw.</summary>
public enum StepResult
{
    Done,
    /// <summary>Nothing to do for this step</summary>
    Skipped,
    /// <summary>Done, and the steps after it have nothing to do either (the file was already imported)</summary>
    SkipRest,
    /// <summary>
    /// Not finished: the server is still working. The step runs again after the delay the step asked for
    /// (<see cref="IStepContext.Later"/>), without counting an attempt and without holding a slot meanwhile.
    /// </summary>
    Later,
}

/// <summary>A failure a step knows the nature of. Anything else is classified by <see cref="ImportQueue.IsRetryable"/>.</summary>
public sealed class StepFailedException : Exception
{
    public StepFailedException(string code, string message, bool retryable, Exception? inner = null) : base(message, inner)
    {
        Code = code;
        Retryable = retryable;
    }

    /// <summary>file_missing, file_link, file_changed, not_hashed or not_uploaded</summary>
    public string Code { get; }
    public bool Retryable { get; }
}

/// <summary>What a running step may do besides its work.</summary>
public interface IStepContext
{
    /// <param name="code">What happened, for the app's words; <paramref name="text"/> is the English line.</param>
    void Log(LogLevel level, string code, string text, params string[] args);
    void Progress(double fraction);
    /// <summary>Writes the queue to disk now (after a change a restart must not lose, such as an upload offset).</summary>
    void Save();
    /// <summary>With <see cref="StepResult.Later"/>: when to run the step again.</summary>
    void Later(TimeSpan delay);
    /// <summary>The step waits for something before it can go on.</summary>
    void Waiting(StepWait wait) { }
}

public interface IImportStep
{
    ImportStepKind Kind { get; }
    Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct);
}

/// <summary>Told when a file's step has failed for good, with the reference shown in its log.</summary>
public interface IImportFailureSink
{
    Task ItemFailedAsync(QueueItem item, StepState step, Exception error, string reference, CancellationToken ct);
}

/// <summary>How many of each step run at once, and how often a step is tried before it fails.</summary>
public sealed class QueueSettings
{
    public const int MaxUploads = 5;

    public Dictionary<ImportStepKind, int> Parallel { get; set; } = new()
    {
        [ImportStepKind.Hash] = 2,
        [ImportStepKind.Encode] = 1,
        [ImportStepKind.Upload] = 3,
        [ImportStepKind.Attach] = 2,
        [ImportStepKind.Transcribe] = 1,
        [ImportStepKind.Check] = 8,
        [ImportStepKind.Translate] = 4,
    };

    /// <summary>An encode uses the whole processor (or its one hardware encoder): at most this many at once.</summary>
    public const int MaxEncodes = 4;

    public int MaxAttempts { get; set; } = 6;

    public int ParallelFor(ImportStepKind kind)
    {
        var fallback = kind switch { ImportStepKind.Check => 8, ImportStepKind.Translate => 4, _ => 1 };
        // One transcription at a time: whisper takes the whole GPU (or every processor core)
        var max = kind switch { ImportStepKind.Upload => MaxUploads, ImportStepKind.Encode => MaxEncodes, ImportStepKind.Transcribe => 1, ImportStepKind.Check => 16, _ => 8 };
        return Math.Clamp(Parallel.TryGetValue(kind, out var n) ? n : fallback, 1, max);
    }
}

/// <summary>
/// The app's one local work queue (TA-11). Every file goes through its steps here; each step's state, the file's
/// server ids and upload offset, and its log are saved to disk after every change, so the queue picks up after a
/// restart where it stopped. A failed step is tried again with backoff; after <see cref="QueueSettings.MaxAttempts"/>
/// (or at once, when retrying cannot help) it waits as Failed for Retry, and the failure is reported.
/// </summary>
public sealed class ImportQueue : IDisposable
{
    public const int MaxLogLines = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<ImportStepKind, IImportStep> _steps;
    private readonly IImportFailureSink? _sink;
    private readonly TimeProvider _time;
    private readonly Func<int, TimeSpan> _backoff;
    private readonly List<QueueItem> _items = new();
    private readonly Dictionary<string, CancellationTokenSource> _running = new();
    private readonly List<Task> _stepTasks = new();
    private readonly SemaphoreSlim _wake = new(0);
    private CancellationTokenSource? _loop;
    private Task? _loopTask;
    private QueueSettings _settings = new();
    private bool _allPaused;
    private readonly WorkFolders _work;
    private readonly object _cleanGate = new();
    private Task _cleaning = Task.CompletedTask;
    private bool _stopped;

    public ImportQueue(string path, IEnumerable<IImportStep> steps, IImportFailureSink? sink = null, TimeProvider? time = null, Func<int, TimeSpan>? backoff = null, WorkFolders? work = null)
    {
        _work = work ?? WorkFolders.Default;
        _path = path;
        _steps = steps.ToDictionary(s => s.Kind);
        _sink = sink;
        _time = time ?? TimeProvider.System;
        _backoff = backoff ?? DefaultBackoff;
        Load();
    }

    /// <summary>Something changed; the item's id, or null for the whole queue.</summary>
    public event Action<string?>? Changed;

    public static string DefaultPath(ToolsEnvironment environment) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNowTools", "queue-" + environment.Kind.ToString().ToLowerInvariant() + ".json");

    public IReadOnlyList<QueueItem> Items
    {
        get { lock (_gate) return _items.ToList(); }
    }

    /// <summary>
    /// Pause all was pressed and something is still paused by it: files added meanwhile come in paused too. Resume all
    /// ends it, and so does resuming, finishing or removing the last paused file.
    /// </summary>
    public bool AllPaused
    {
        get { lock (_gate) return HeldLocked; }
    }

    /// <summary>Pause all has something to pause.</summary>
    public bool CanPauseAll
    {
        get { lock (_gate) return _items.Any(i => i.Pausable); }
    }

    /// <summary>Resume all has something to resume.</summary>
    public bool CanResumeAll
    {
        get { lock (_gate) return _items.Any(i => i.CanResume); }
    }

    private bool HeldLocked => _allPaused && _items.Any(i => i.Phase == ItemPhase.Paused);

    public QueueSettings Settings
    {
        get { lock (_gate) return _settings; }
    }

    /// <summary>10 s, 20 s, 40 s … at most 15 minutes, ±20%.</summary>
    public static TimeSpan DefaultBackoff(int attempt)
    {
        var seconds = Math.Min(900, 10 * Math.Pow(2, Math.Max(0, attempt - 1)));
        return TimeSpan.FromSeconds(seconds * (0.8 + Random.Shared.NextDouble() * 0.4));
    }

    /// <summary>Worth trying again: the network, a busy or failing server, a file that is still being written.</summary>
    public static bool IsRetryable(Exception error) => error switch
    {
        StepFailedException f => f.Retryable,
        ImportApiException a => a.IsRetryable,
        TusException t => t.IsRetryable,
        TusGoneException => true,
        AlreadyImportedException => false,
        FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException => false,
        IOException io => ReadFailure.Of(io).Retryable,
        HttpRequestException => true,
        TimeoutException => true,
        _ => false,
    };

    public void SetParallel(ImportStepKind kind, int count)
    {
        lock (_gate)
        {
            _settings.Parallel[kind] = count;
            _settings.Parallel[kind] = _settings.ParallelFor(kind);
            SaveLocked();
        }
        Notify(null);
    }

    public void Add(IEnumerable<QueueItem> items)
    {
        lock (_gate)
        {
            var held = HeldLocked;
            foreach (var item in items)
            {
                if (_items.Any(i => i.Id == item.Id)) continue;
                AppendLog(item, LogLevel.Info, "added", $"Added: {item.Video.FileName}", item.Video.FileName);
                // Nothing starts while all is paused
                if (held && item.Pausable) PauseLocked(item);
                _items.Add(item);
            }
            SaveLocked();
        }
        Notify(null);
    }

    /// <summary>Pauses the file: a running step stops (an upload keeps its offset) and nothing starts until Resume.</summary>
    public void Pause(string id) => Update(id, item =>
    {
        if (!item.Pausable) return false;
        PauseLocked(item);
        return true;
    });

    /// <summary>The file goes on from its step (an upload from its offset). Only this file: the others paused by Pause all stay paused.</summary>
    public void Resume(string id) => Update(id, item => ResumeLocked(item));

    /// <summary>
    /// Pauses every file that is not finished: queued, running (its step and child processes stop), waiting to try again,
    /// waiting for the server, or given up on something trying again may fix. Each then reads Paused and keeps its step,
    /// its offset and the time of its next try. A failure that trying again cannot fix stays failed. Kept over a restart.
    /// </summary>
    public void PauseAll()
    {
        lock (_gate)
        {
            var pausable = _items.Where(i => i.Pausable).ToList();
            if (pausable.Count == 0 && (_allPaused || _items.All(i => i.Phase != ItemPhase.Paused))) return;
            foreach (var item in pausable) PauseLocked(item);
            _allPaused = true;
            SaveLocked();
        }
        Notify(null);
    }

    /// <summary>Every paused file goes on from its own step.</summary>
    public void ResumeAll()
    {
        lock (_gate)
        {
            var resumed = false;
            foreach (var item in _items) resumed |= ResumeLocked(item);
            if (!resumed && !_allPaused) return;
            _allPaused = false;
            SaveLocked();
        }
        Notify(null);
        _wake.Release();
    }

    private void PauseLocked(QueueItem item)
    {
        item.Paused = true;
        AppendLog(item, LogLevel.Info, "paused", "Paused");
        if (_running.TryGetValue(item.Id, out var cts)) Cancel(cts);
    }

    private bool ResumeLocked(QueueItem item)
    {
        if (item.Phase != ItemPhase.Paused) return false;
        item.Paused = false;
        AppendLog(item, LogLevel.Info, "resumed", "Resumed");
        // It had given up: Resume is another go at it
        if (item.CurrentStep is { Status: StepStatus.Failed, CanTryAgain: true } step) Again(item, step);
        return true;
    }

    /// <summary>The step starts from its first attempt.</summary>
    private static void Again(QueueItem item, StepState step)
    {
        step.Status = StepStatus.Pending;
        step.Attempts = 0;
        step.NextAttemptAt = null;
        step.LastError = null;
        step.LastErrorCode = null;
        step.ReadError = null;
        step.Retryable = null;
        step.WaitingOnServer = false;
        AppendLog(item, LogLevel.Info, "retry", $"Retry: {step.Kind}", step.Kind.ToString());
    }

    /// <summary>Off this thread: what a step does when it is cancelled must not run under the queue's lock.</summary>
    private static void Cancel(CancellationTokenSource cts) => _ = cts.CancelAsync();

    /// <summary>A failed (or waiting-to-retry) step starts again now, with its attempts counted from zero.</summary>
    public void Retry(string id) => Update(id, item =>
    {
        var step = item.CurrentStep;
        if (step is null || step.Status == StepStatus.Running) return false;
        if (step.Status != StepStatus.Failed && step.NextAttemptAt is null) return false;
        item.Paused = false;
        Again(item, step);
        return true;
    });

    /// <summary>
    /// Takes the file out of the queue, stopping its running step, and deletes what was made for it here and not sent
    /// yet. Nothing on the server is deleted, and the person's own files are not touched.
    /// </summary>
    public void Remove(string id) => RemoveWhere(i => i.Id == id);

    /// <summary>The same for every file, in one go; answers how many left the queue.</summary>
    public int RemoveAll() => RemoveWhere(_ => true);

    private int RemoveWhere(Func<QueueItem, bool> which)
    {
        var idle = new List<QueueItem>();
        int removed;
        lock (_gate)
        {
            var items = _items.Where(which).ToHashSet();
            removed = items.Count;
            if (removed == 0) return 0;
            // Out of the queue first, so nothing of them starts while the running ones stop
            _items.RemoveAll(items.Contains);
            foreach (var item in items)
            {
                // A running step lets go of its files first (RunStepAsync)
                if (_running.TryGetValue(item.Id, out var cts)) Cancel(cts);
                else idle.Add(item);
            }
            SaveLocked();
        }
        Notify(null);
        DeleteWork(idle);
        return removed;
    }

    /// <summary>
    /// The renditions and the transcript made here for files that left the queue: only the app's own work
    /// (<see cref="WorkFiles"/>), whatever the saved queue says.
    /// </summary>
    private void DeleteWork(IReadOnlyCollection<QueueItem> items)
    {
        var made = items.Where(i => i.Renditions is not null || i.Transcript is not null).ToList();
        if (made.Count == 0) return;
        void Delete()
        {
            foreach (var item in made)
            {
                WorkFiles.DeleteFolder(item.Renditions?.Directory, _work.Renditions, item.Id);
                WorkFiles.DeleteFile(item.Transcript?.Path, _work.Transcripts, item.Id, ".json");
            }
        }
        // Thousands of segments: not on the app's own thread
        lock (_cleanGate) _cleaning = _cleaning.ContinueWith(_ => Delete(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Removes the files that are done.</summary>
    public int ClearDone()
    {
        List<QueueItem> done;
        lock (_gate)
        {
            done = _items.Where(i => i.Phase == ItemPhase.Done).ToList();
            _items.RemoveAll(i => i.Phase == ItemPhase.Done);
            if (done.Count > 0) SaveLocked();
        }
        if (done.Count > 0) Notify(null);
        // Already deleted when the server had them, unless another app held a file then
        DeleteWork(done);
        return done.Count;
    }

    /// <summary>The file's log lines, oldest first.</summary>
    public IReadOnlyList<LogLine> LogLines(string id)
    {
        lock (_gate) return _items.FirstOrDefault(i => i.Id == id)?.Log.ToList() ?? new List<LogLine>();
    }

    /// <summary>The file's log as English text, oldest first, for support.</summary>
    public string LogText(string id)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            return item is null ? "" : string.Join(Environment.NewLine, item.Log.Select(l => $"{l.At:yyyy-MM-dd HH:mm:ss}Z  {l.Level,-7}  {l.Text}"));
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null) return;
            _stopped = false;
            _loop = new CancellationTokenSource();
            var ct = _loop.Token;
            _loopTask = Task.Run(() => LoopAsync(ct));
        }
    }

    /// <summary>
    /// How long <see cref="StopAsync"/> waits in all: for running steps to stop (a step kills its ffmpeg when it is cancelled) and
    /// for what is being deleted to be deleted. Under the 8 s the process-exit handler gives the whole quit.
    /// </summary>
    public static readonly TimeSpan StepStopGrace = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Stops the loop and every running step, waits (up to <see cref="StepStopGrace"/>) until each has stopped, so its child
    /// processes are gone and its step is back to waiting, then saves the queue. A step that is still running when the wait
    /// ends is saved as running; the next start runs it again, as after a crash.
    /// </summary>
    public async Task StopAsync(TimeSpan? grace = null)
    {
        Task? loop;
        lock (_gate)
        {
            // Nothing starts from here on, so what is awaited below is all there will be
            _stopped = true;
            _loop?.Cancel();
            foreach (var cts in _running.Values.ToList()) Cancel(cts);
            loop = _loopTask;
            _loop = null;
            _loopTask = null;
        }
        // One grace for all of it: the loop ending, and every step (one a Pump started a moment ago included) ending
        var settled = Task.Run(async () =>
        {
            if (loop is not null)
            {
                try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
            while (true)
            {
                Task[] steps;
                lock (_gate)
                {
                    _stepTasks.RemoveAll(t => t.IsCompleted);
                    steps = _stepTasks.ToArray();
                }
                if (steps.Length == 0) break;
                try { await Task.WhenAll(steps).ConfigureAwait(false); }
                catch (Exception) { /* a step's own failure was handled by the step */ }
            }
            // What is being deleted (a removed file's renditions) is deleted whole, within the same grace; a step that was
            // stopping may have queued some just now, so this comes after them
            Task cleaning;
            lock (_cleanGate) cleaning = _cleaning;
            await cleaning.ConfigureAwait(false);
        });
        try { await settled.WaitAsync(grace ?? StepStopGrace).ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }
        lock (_gate) SaveLocked();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _wake.Dispose();
    }

    /// <summary>Starts every step that may run now; answers how many started (the loop calls it, and so can tests).</summary>
    internal int Pump()
    {
        var started = new List<(QueueItem Item, StepState Step, CancellationTokenSource Cts)>();
        lock (_gate)
        {
            if (_stopped) return 0;
            var now = _time.GetUtcNow();
            var busy = Enum.GetValues<ImportStepKind>().ToDictionary(k => k, k => _items.Count(i => i.CurrentStep is { Status: StepStatus.Running } s && s.Kind == k));
            foreach (var item in _items.OrderBy(i => i.CreatedAt))
            {
                if (item.Paused || _running.ContainsKey(item.Id)) continue;
                var step = item.CurrentStep;
                if (step is null || step.Status != StepStatus.Pending) continue;
                if (step.NextAttemptAt is { } at && at > now) continue;
                if (busy[step.Kind] >= _settings.ParallelFor(step.Kind)) continue;
                if (!_steps.ContainsKey(step.Kind)) continue;
                busy[step.Kind]++;
                step.Status = StepStatus.Running;
                step.Attempts++;
                step.NextAttemptAt = null;
                item.Progress = 0;
                if (step.WaitingOnServer) { }
                else if (step.Attempts == 1) AppendLog(item, LogLevel.Info, "started", $"{step.Kind}: started", step.Kind.ToString());
                else AppendLog(item, LogLevel.Info, "attempt", $"{step.Kind}: attempt {step.Attempts}", step.Kind.ToString(), step.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_loop?.Token ?? CancellationToken.None);
                _running[item.Id] = cts;
                started.Add((item, step, cts));
            }
            if (started.Count > 0) SaveLocked();
        }
        foreach (var (item, step, cts) in started)
        {
            Notify(item.Id);
            var task = Task.Run(() => RunStepAsync(item, step, cts));
            lock (_gate)
            {
                _stepTasks.RemoveAll(t => t.IsCompleted);
                _stepTasks.Add(task);
            }
        }
        return started.Count;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Pump();
            try
            {
                await _wake.WaitAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunStepAsync(QueueItem item, StepState step, CancellationTokenSource cts)
    {
        var context = new Context(this, item);
        var wasWaiting = step.WaitingOnServer;
        step.WaitingOnServer = false;
        Exception? failure = null;
        StepResult result = StepResult.Done;
        try
        {
            result = await _steps[step.Kind].RunAsync(item, context, cts.Token).ConfigureAwait(false);
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // Whatever it stopped with: a request or a file cut short by the stop is no failure of the file's
            lock (_gate)
            {
                // Paused, stopped or removed: the attempt does not count, and the step waits to run again
                step.Status = StepStatus.Pending;
                step.Attempts = Math.Max(0, step.Attempts - 1);
                step.WaitingOnServer = wasWaiting;
                AppendLog(item, LogLevel.Info, "stopped", $"{step.Kind}: stopped", step.Kind.ToString());
                Finish(item);
            }
            Notify(item.Id);
            _wake.Release();
            return;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        string? reference = null;
        lock (_gate)
        {
            if (failure is null && result == StepResult.Later)
            {
                // The server is still at it: look again later; the attempt does not count and no slot is held
                var first = !wasWaiting;
                step.Status = StepStatus.Pending;
                step.Attempts = Math.Max(0, step.Attempts - 1);
                step.WaitingOnServer = true;
                step.NextAttemptAt = _time.GetUtcNow() + (context.LaterDelay ?? TimeSpan.FromSeconds(30));
                if (first) AppendLog(item, LogLevel.Info, "server_working", $"{step.Kind}: the server is working on the file; the app looks again now and then", step.Kind.ToString());
            }
            else if (failure is null)
            {
                step.Status = result == StepResult.Skipped ? StepStatus.Skipped : StepStatus.Done;
                step.LastError = null;
                step.LastErrorCode = null;
                step.ReadError = null;
                step.Retryable = null;
                item.Progress = 1;
                if (result == StepResult.Skipped) AppendLog(item, LogLevel.Info, "skipped", $"{step.Kind}: nothing to do", step.Kind.ToString());
                else AppendLog(item, LogLevel.Info, "done", $"{step.Kind}: done", step.Kind.ToString());
                if (result == StepResult.SkipRest)
                {
                    foreach (var later in item.Steps.Where(s => s.Status == StepStatus.Pending)) later.Status = StepStatus.Skipped;
                }
                if (item.CurrentStep is null)
                {
                    item.Paused = false;
                    AppendLog(item, LogLevel.Info, "finished", "Finished");
                }
            }
            else
            {
                var message = Describe(failure);
                var code = ErrorCode(failure);
                step.LastError = message;
                step.LastErrorCode = code;
                step.Retryable = IsRetryable(failure);
                step.ReadError = ReadError.Of(failure);
                var read = step.ReadError?.Args ?? [];
                var attempts = step.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (IsRetryable(failure) && step.Attempts < _settings.MaxAttempts)
                {
                    var wait = _backoff(step.Attempts);
                    var serverWait = (failure as ImportApiException)?.RetryAfter ?? (failure as TusException)?.RetryAfter;
                    if (serverWait > wait) wait = serverWait.Value;
                    step.Status = StepStatus.Pending;
                    step.NextAttemptAt = _time.GetUtcNow() + wait;
                    var seconds = Math.Ceiling(wait.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    AppendLog(item, LogLevel.Warning, "attempt_failed", $"{step.Kind}: attempt {step.Attempts} failed: {message}. Next try in {seconds} s", [step.Kind.ToString(), attempts, code, seconds, message, .. read]);
                }
                else
                {
                    step.Status = StepStatus.Failed;
                    // Failed for good just as it was paused: it reads Failed, and Resume all has nothing to do with it
                    if (!step.CanTryAgain) item.Paused = false;
                    reference = ErrorReport.NewRef();
                    AppendLog(item, LogLevel.Error, "failed", $"{step.Kind}: failed after {step.Attempts} attempt(s): {message} (reference {reference})", [step.Kind.ToString(), attempts, code, reference, message, .. read]);
                }
            }
            Finish(item);
        }
        Notify(item.Id);
        _wake.Release();

        if (reference is not null && _sink is not null)
        {
            try
            {
                await _sink.ItemFailedAsync(item, step, failure!, reference, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_gate) AppendLog(item, LogLevel.Warning, "report_failed", $"The failure could not be reported to the server: {Describe(ex)}", ErrorCode(ex), Describe(ex));
            }
        }

        void Finish(QueueItem i)
        {
            if (_running.Remove(i.Id, out var c)) c.Dispose();
            i.Wait = StepWait.None;
            // Removed while it ran: its files are free now
            if (!_items.Contains(i)) DeleteWork(new[] { i });
            SaveLocked();
        }
    }

    /// <summary>
    /// The kind of failure, for the app's words: the step's own code, admin_unreachable, videos_unreachable,
    /// signed_out, already_imported, upload_gone, upload_refused, file_missing, file_denied, server (admin-api's own words
    /// follow), one of <see cref="ReadFailure"/>'s for a file that could not be read, or unexpected.
    /// </summary>
    public static string ErrorCode(Exception error) => error switch
    {
        StepFailedException f => f.Code,
        ImportApiException { Status: 0 } => "admin_unreachable",
        ImportApiException { Status: 401 } => "signed_out",
        ImportApiException => "server",
        TusException { Status: 0 } => "videos_unreachable",
        TusException => "upload_refused",
        TusGoneException => "upload_gone",
        AlreadyImportedException => "already_imported",
        FileNotFoundException or DirectoryNotFoundException => "file_missing",
        UnauthorizedAccessException => "file_denied",
        IOException io => ReadFailure.Of(io).Code,
        _ => "unexpected",
    };

    /// <summary>The failure in English, without any path on this computer (it goes into the server's reports).</summary>
    public static string Describe(Exception error) => error switch
    {
        SourceReadException r => r.Message,
        IOException or UnauthorizedAccessException => ReadFailure.Of(error).Message,
        ImportApiException a when a.Status == 0 => "The admin API could not be reached",
        ImportApiException a => $"{a.Message} ({a.Status} {a.Code})",
        TusException t when t.Status == 0 => "The videos API could not be reached",
        AlreadyImportedException => "This file is already in this channel",
        _ => error.Message,
    };

    private void Update(string id, Func<QueueItem, bool> change)
    {
        bool changed;
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            changed = item is not null && change(item);
            if (changed) SaveLocked();
        }
        if (changed)
        {
            Notify(id);
            _wake.Release();
        }
    }

    private void Notify(string? id) => Changed?.Invoke(id);

    private static void AppendLog(QueueItem item, LogLevel level, string code, string text, params string[] args)
    {
        item.Log.Add(new LogLine(DateTimeOffset.UtcNow, level, text, code, args.Length == 0 ? null : args));
        if (item.Log.Count > MaxLogLines) item.Log.RemoveRange(0, item.Log.Count - MaxLogLines);
    }

    private sealed record Saved(QueueSettings Settings, List<QueueItem> Items, bool AllPaused = false);

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_path), Json);
            if (saved is null) return;
            _settings = saved.Settings ?? new QueueSettings();
            _allPaused = saved.AllPaused;
            foreach (var item in saved.Items ?? new List<QueueItem>())
            {
                // A step that was running when the app closed starts again (an upload from its saved offset)
                foreach (var step in item.Steps.Where(s => s.Status == StepStatus.Running))
                {
                    step.Status = StepStatus.Pending;
                    // The app closed while the paused step was stopping: that was no attempt
                    if (item.Paused) step.Attempts = Math.Max(0, step.Attempts - 1);
                }
                item.AddMissingSteps();
                if (item.Phase != ItemPhase.Paused) item.Paused = false;
                _items.Add(item);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // An unreadable queue is kept beside the new one for support, never silently lost
            File.Copy(_path, _path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
    }

    private void SaveLocked()
    {
        // Pause all ends with the last file it paused (resumed, finished or removed)
        _allPaused = HeldLocked;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Saved(_settings, _items, _allPaused), Json));
        File.Move(temp, _path, overwrite: true);
    }

    private sealed class Context : IStepContext
    {
        private readonly ImportQueue _queue;
        private readonly QueueItem _item;
        private double _lastProgress = -1;

        public Context(ImportQueue queue, QueueItem item)
        {
            _queue = queue;
            _item = item;
        }

        public void Log(LogLevel level, string code, string text, params string[] args)
        {
            lock (_queue._gate) AppendLog(_item, level, code, text, args);
            _queue.Notify(_item.Id);
        }

        public void Progress(double fraction)
        {
            _item.Progress = Math.Clamp(fraction, 0, 1);
            // The table needs a step now and then, not every buffer
            if (Math.Abs(_item.Progress - _lastProgress) < 0.005 && _item.Progress < 1) return;
            _lastProgress = _item.Progress;
            _queue.Notify(_item.Id);
        }

        public void Save()
        {
            lock (_queue._gate) _queue.SaveLocked();
        }

        public void Waiting(StepWait wait)
        {
            if (_item.Wait == wait) return;
            _item.Wait = wait;
            _queue.Notify(_item.Id);
        }

        public TimeSpan? LaterDelay { get; private set; }

        public void Later(TimeSpan delay) => LaterDelay = delay;
    }
}

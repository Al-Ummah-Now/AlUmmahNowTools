using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>The steps a file goes through, in order. Later stories add theirs (transcode, transcribe, suggest).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImportStepKind
{
    Hash,
    Upload,
    Attach,
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
    public List<LogLine> Log { get; set; } = new();

    /// <summary>Progress of the running step, 0–1 (not saved)</summary>
    [JsonIgnore] public double Progress { get; internal set; }

    [JsonIgnore] public StepState? CurrentStep => Steps.FirstOrDefault(s => s.Status is StepStatus.Pending or StepStatus.Running or StepStatus.Failed);
    [JsonIgnore] public DateTimeOffset? NextAttemptAt => CurrentStep?.NextAttemptAt;

    [JsonIgnore]
    public ItemPhase Phase
    {
        get
        {
            var step = CurrentStep;
            if (step is null) return ItemPhase.Done;
            if (step.Status == StepStatus.Failed) return ItemPhase.Failed;
            if (step.Status == StepStatus.Running) return ItemPhase.Running;
            if (Paused) return ItemPhase.Paused;
            return step.NextAttemptAt is not null ? ItemPhase.RetryWaiting : ItemPhase.Waiting;
        }
    }

    public static QueueItem For(ScannedVideo video, string batchId, string channelId, DateTimeOffset now) => new()
    {
        BatchId = batchId,
        ChannelId = channelId,
        Video = video,
        CreatedAt = now,
        Steps = Enum.GetValues<ImportStepKind>().Select(k => new StepState { Kind = k }).ToList(),
    };
}

/// <summary>A step's outcome when it did not throw.</summary>
public enum StepResult
{
    Done,
    /// <summary>Nothing to do for this step</summary>
    Skipped,
    /// <summary>Done, and the steps after it have nothing to do either (the file was already imported)</summary>
    SkipRest,
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
        [ImportStepKind.Upload] = 3,
        [ImportStepKind.Attach] = 2,
    };

    public int MaxAttempts { get; set; } = 6;

    public int ParallelFor(ImportStepKind kind) => Math.Clamp(Parallel.TryGetValue(kind, out var n) ? n : 1, 1, kind == ImportStepKind.Upload ? MaxUploads : 8);
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
    private readonly SemaphoreSlim _wake = new(0);
    private CancellationTokenSource? _loop;
    private Task? _loopTask;
    private QueueSettings _settings = new();
    private bool _allPaused;

    public ImportQueue(string path, IEnumerable<IImportStep> steps, IImportFailureSink? sink = null, TimeProvider? time = null, Func<int, TimeSpan>? backoff = null)
    {
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

    public bool AllPaused
    {
        get { lock (_gate) return _allPaused; }
    }

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
        IOException => true,
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
            foreach (var item in items)
            {
                if (_items.Any(i => i.Id == item.Id)) continue;
                AppendLog(item, LogLevel.Info, "added", $"Added: {item.Video.FileName}", item.Video.FileName);
                _items.Add(item);
            }
            SaveLocked();
        }
        Notify(null);
    }

    /// <summary>Pauses the file: a running step stops (an upload keeps its offset) and nothing starts until Resume.</summary>
    public void Pause(string id) => Update(id, item =>
    {
        if (item.Paused || item.Phase == ItemPhase.Done) return false;
        item.Paused = true;
        AppendLog(item, LogLevel.Info, "paused", "Paused");
        if (_running.TryGetValue(id, out var cts)) cts.Cancel();
        return true;
    });

    public void Resume(string id) => Update(id, item =>
    {
        if (!item.Paused) return false;
        item.Paused = false;
        AppendLog(item, LogLevel.Info, "resumed", "Resumed");
        return true;
    });

    public void PauseAll()
    {
        lock (_gate)
        {
            _allPaused = true;
            foreach (var cts in _running.Values) cts.Cancel();
        }
        Notify(null);
    }

    public void ResumeAll()
    {
        lock (_gate) _allPaused = false;
        Notify(null);
        _wake.Release();
    }

    /// <summary>A failed (or waiting-to-retry) step starts again now, with its attempts counted from zero.</summary>
    public void Retry(string id) => Update(id, item =>
    {
        var step = item.CurrentStep;
        if (step is null || step.Status == StepStatus.Running) return false;
        if (step.Status != StepStatus.Failed && step.NextAttemptAt is null) return false;
        step.Status = StepStatus.Pending;
        step.Attempts = 0;
        step.NextAttemptAt = null;
        step.LastError = null;
        step.LastErrorCode = null;
        item.Paused = false;
        AppendLog(item, LogLevel.Info, "retry", $"Retry: {step.Kind}", step.Kind.ToString());
        return true;
    });

    /// <summary>Takes the file out of the queue, stopping its running step. Nothing on the server is deleted.</summary>
    public void Remove(string id)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null) return;
            if (_running.TryGetValue(id, out var cts)) cts.Cancel();
            _items.Remove(item);
            SaveLocked();
        }
        Notify(null);
    }

    /// <summary>Removes the files that are done.</summary>
    public int ClearDone()
    {
        int removed;
        lock (_gate)
        {
            removed = _items.RemoveAll(i => i.Phase == ItemPhase.Done);
            if (removed > 0) SaveLocked();
        }
        if (removed > 0) Notify(null);
        return removed;
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
            _loop = new CancellationTokenSource();
            var ct = _loop.Token;
            _loopTask = Task.Run(() => LoopAsync(ct));
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        lock (_gate)
        {
            _loop?.Cancel();
            foreach (var cts in _running.Values) cts.Cancel();
            loop = _loopTask;
            _loop = null;
            _loopTask = null;
        }
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
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
            if (_allPaused) return 0;
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
                if (step.Attempts == 1) AppendLog(item, LogLevel.Info, "started", $"{step.Kind}: started", step.Kind.ToString());
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
            _ = Task.Run(() => RunStepAsync(item, step, cts));
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
        Exception? failure = null;
        StepResult result = StepResult.Done;
        try
        {
            result = await _steps[step.Kind].RunAsync(item, context, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            failure = null;
            lock (_gate)
            {
                // Paused, stopped or removed: the attempt does not count, and the step waits to run again
                step.Status = StepStatus.Pending;
                step.Attempts = Math.Max(0, step.Attempts - 1);
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
            if (failure is null)
            {
                step.Status = result == StepResult.Skipped ? StepStatus.Skipped : StepStatus.Done;
                step.LastError = null;
                step.LastErrorCode = null;
                item.Progress = 1;
                if (result == StepResult.Skipped) AppendLog(item, LogLevel.Info, "skipped", $"{step.Kind}: nothing to do", step.Kind.ToString());
                else AppendLog(item, LogLevel.Info, "done", $"{step.Kind}: done", step.Kind.ToString());
                if (result == StepResult.SkipRest)
                {
                    foreach (var later in item.Steps.Where(s => s.Status == StepStatus.Pending)) later.Status = StepStatus.Skipped;
                }
                if (item.CurrentStep is null) AppendLog(item, LogLevel.Info, "finished", "Finished");
            }
            else
            {
                var message = Describe(failure);
                var code = ErrorCode(failure);
                step.LastError = message;
                step.LastErrorCode = code;
                var attempts = step.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (IsRetryable(failure) && step.Attempts < _settings.MaxAttempts)
                {
                    var wait = _backoff(step.Attempts);
                    var serverWait = (failure as ImportApiException)?.RetryAfter ?? (failure as TusException)?.RetryAfter;
                    if (serverWait > wait) wait = serverWait.Value;
                    step.Status = StepStatus.Pending;
                    step.NextAttemptAt = _time.GetUtcNow() + wait;
                    var seconds = Math.Ceiling(wait.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    AppendLog(item, LogLevel.Warning, "attempt_failed", $"{step.Kind}: attempt {step.Attempts} failed: {message}. Next try in {seconds} s", step.Kind.ToString(), attempts, code, seconds, message);
                }
                else
                {
                    step.Status = StepStatus.Failed;
                    reference = ErrorReport.NewRef();
                    AppendLog(item, LogLevel.Error, "failed", $"{step.Kind}: failed after {step.Attempts} attempt(s): {message} (reference {reference})", step.Kind.ToString(), attempts, code, reference, message);
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
            SaveLocked();
        }
    }

    /// <summary>
    /// The kind of failure, for the app's words: the step's own code, admin_unreachable, videos_unreachable,
    /// signed_out, already_imported, upload_gone, upload_refused, file_missing, file_denied, file_read, server
    /// (admin-api's own words follow) or unexpected.
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
        IOException => "file_read",
        _ => "unexpected",
    };

    internal static string Describe(Exception error) => error switch
    {
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

    private sealed record Saved(QueueSettings Settings, List<QueueItem> Items);

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_path), Json);
            if (saved is null) return;
            _settings = saved.Settings ?? new QueueSettings();
            foreach (var item in saved.Items ?? new List<QueueItem>())
            {
                // A step that was running when the app closed starts again (an upload from its saved offset)
                foreach (var step in item.Steps.Where(s => s.Status == StepStatus.Running)) step.Status = StepStatus.Pending;
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
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Saved(_settings, _items), Json));
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
    }
}

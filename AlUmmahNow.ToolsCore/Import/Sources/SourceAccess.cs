using System.Globalization;
using System.Runtime.CompilerServices;

namespace AlUmmahNow.ToolsCore.Import.Sources;

/// <summary>
/// Reads the person's files for every step of the queue (hash, encode, upload, sidecars) and for the scan. A file on a
/// mapped drive is read through its network path, with the drive letter as the fallback; the file keeps the path the person
/// chose everywhere it is shown or saved. When a share drops, the read waits for it, trying both paths, and goes on from
/// the last good byte with the file's size and modified time checked again; after several minutes it fails, saying why.
/// </summary>
public sealed class SourceAccess
{
    private static SourceAccess? _default;

    private readonly INetworkPaths _paths;
    private readonly TimeProvider _time;
    private readonly ConditionalWeakTable<IStepContext, StrongBox<string>> _announced = new();

    public SourceAccess(INetworkPaths paths, ISourceFileSystem files, TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _paths = paths;
        Files = files;
        _time = time ?? TimeProvider.System;
        Delay = delay ?? Task.Delay;
    }

    public static SourceAccess Default => _default ??= new SourceAccess(
        OperatingSystem.IsWindows() ? new WindowsNetworkPaths(new SystemDrives()) : new UnixNetworkPaths(),
        new SourceFileSystem());

    /// <summary>How long a share may be away, with nothing read in between, before the step fails.</summary>
    public TimeSpan Patience { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>A share that does not answer a call this long is away (Windows itself gives up sooner).</summary>
    public TimeSpan Stall { get; init; } = TimeSpan.FromMinutes(2);

    internal ISourceFileSystem Files { get; }
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; }
    internal DateTimeOffset Now => _time.GetUtcNow();

    /// <summary>
    /// How long the system may take to say what a path is on (a mount whose server is gone may never say). Well inside the
    /// time a stopping step has (<see cref="ImportQueue.StepStopGrace"/>).
    /// </summary>
    public TimeSpan Answer { get; init; } = TimeSpan.FromSeconds(3);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Asking> _asking = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The question a mount was asked last.</summary>
    private sealed class Asking
    {
        public Task? Task;
        public long Since;
    }

    /// <summary>A path whose mount does not answer is on a share that is away.</summary>
    public Task<bool> IsNetworkAsync(string path, CancellationToken ct) => AskAsync(path, () => _paths.IsNetwork(path), true, ct);

    /// <summary>The paths to read the file by: its network path first when it is on a mapped drive, then as it was chosen.</summary>
    public async Task<IReadOnlyList<string>> CandidatesAsync(string path, CancellationToken ct) =>
        await AskAsync<string?>(path, () => _paths.Mapped(path), null, ct).ConfigureAwait(false) is { } network && network != path ? [network, path] : [path];

    /// <summary>For the scan, on its own thread; a step asks with its cancellation.</summary>
    public bool IsNetwork(string path) => IsNetworkAsync(path, CancellationToken.None).GetAwaiter().GetResult();

    public IReadOnlyList<string> Candidates(string path) => CandidatesAsync(path, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Asks the system, one question at a time for each drive, share or mount: a question that is never answered holds one
    /// thread of its own, and while it is open the next ones are not asked but count as unanswered.
    /// </summary>
    private async Task<T> AskAsync<T>(string path, Func<T> question, T whenSilent, CancellationToken ct)
    {
        var asking = _asking.GetOrAdd(MountOf(path), _ => new Asking());
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Task<T>? mine = null;
            Task? before = null;
            lock (asking)
            {
                if (asking.Task is { IsCompleted: false } open)
                {
                    if (TimeProvider.System.GetElapsedTime(asking.Since) >= Answer) return whenSilent;
                    before = open;
                }
                else
                {
                    // A thread of its own, not one of the pool's
                    mine = Task.Factory.StartNew(question, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    // A late failure is nobody's
                    _ = mine.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    (asking.Task, asking.Since) = (mine, TimeProvider.System.GetTimestamp());
                }
            }
            var answered = mine ?? before!;
            if (await Task.WhenAny(answered, Task.Delay(Answer, ct)).ConfigureAwait(false) != answered)
            {
                ct.ThrowIfCancellationRequested();
                return whenSilent;
            }
            if (mine is not null) return await mine.ConfigureAwait(false);
        }
    }

    /// <summary>The drive, share or volume; elsewhere the first two folders (<c>/mnt/nas</c>), where mounts are.</summary>
    private static string MountOf(string path) =>
        SourceFileSystem.RootOf(path) ?? string.Join('/', path.Split('/', 4).Take(3));

    /// <summary>The file's size and modified time, waiting for its share if need be.</summary>
    public async Task<SourceStat> StatAsync(string path, IStepContext? context, CancellationToken ct) =>
        (await ReachAsync(path, context, null, ct).ConfigureAwait(false)).Stat;

    /// <summary>The file must still be the one that was scanned and hashed.</summary>
    /// <param name="read">It was just read: another app is still writing it if it changed</param>
    public async Task CheckUnchangedAsync(ScannedVideo video, IStepContext? context, CancellationToken ct, bool read = false)
    {
        SourceStat stat;
        try
        {
            stat = await StatAsync(video.Path, context, ct).ConfigureAwait(false);
        }
        catch (SourceReadException missing) when (missing.Failure.Code == "file_missing")
        {
            throw new StepFailedException("file_missing", "The file is no longer in its folder", retryable: false, missing);
        }
        if (stat.IsLink) throw new StepFailedException("file_link", "The file is now a link to another file. Scan the folder again", retryable: false);
        if (stat.Length != video.Size || stat.ModifiedMs != video.LastModifiedMs) throw Changed(read);
    }

    internal static StepFailedException Changed(bool read) => read
        ? new StepFailedException("file_writing", "The file changed while it was read: another app is still writing it. Scan the folder again when it is finished", retryable: false)
        : new StepFailedException("file_changed", "The file changed after it was scanned. Scan the folder again", retryable: false);

    /// <summary>A stream over the file that survives a dropped share. Null size and time: taken from the first open.</summary>
    public SourceStream Open(string path, long? size, long? modifiedMs, IStepContext? context) => new(this, path, size, modifiedMs, context);

    public async Task<byte[]> ReadAllBytesAsync(string path, IStepContext? context, CancellationToken ct)
    {
        var stream = Open(path, null, null, context);
        await using (stream.ConfigureAwait(false))
        {
            var size = await stream.SizeAsync(ct).ConfigureAwait(false);
            var bytes = new byte[size];
            await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            return bytes;
        }
    }

    public async Task<string> ReadAllTextAsync(string path, IStepContext? context, CancellationToken ct)
    {
        using var reader = new StreamReader(new MemoryStream(await ReadAllBytesAsync(path, context, ct).ConfigureAwait(false)), System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs something that opens the file itself (ffmpeg, ffprobe, aun-core) with a path that answers. When it reports that
    /// the file could not be read and the file is on a share, it runs again, on the other path if there is one, up to
    /// <paramref name="tries"/> times.
    /// </summary>
    public async Task<T> WithPathAsync<T>(string path, IStepContext? context, Func<string, Task<T>> work, CancellationToken ct, int tries = 3)
    {
        string? failed = null;
        for (var attempt = 1; ; attempt++)
        {
            var (use, _) = await ReachAsync(path, context, failed, ct).ConfigureAwait(false);
            Announce(path, use, context);
            try
            {
                return await work(use).ConfigureAwait(false);
            }
            catch (LocalEncodeException error) when (error.Code == "source_read" && attempt < tries)
            {
                if (!await IsNetworkAsync(use, ct).ConfigureAwait(false) && !await IsNetworkAsync(path, ct).ConfigureAwait(false)) throw;
                failed = use;
                context?.Log(LogLevel.Warning, "share_tool", $"{error.Message}. Trying again", error.Message);
                context?.Waiting(StepWait.Share);
                try
                {
                    await Delay(TimeSpan.FromSeconds(5 * attempt), ct).ConfigureAwait(false);
                }
                finally
                {
                    context?.Waiting(StepWait.None);
                }
            }
        }
    }

    /// <summary>A path of the file that answers now, waiting for the share if need be.</summary>
    private async Task<(string Path, SourceStat Stat)> ReachAsync(string path, IStepContext? context, string? avoid, CancellationToken ct)
    {
        var attempts = await SourceAttempts.CreateAsync(this, path, context, ct, avoid).ConfigureAwait(false);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var current = attempts.Current;
                try
                {
                    var stat = await CallAsync(() => Files.Stat(current), attempts.Network, ct).ConfigureAwait(false);
                    attempts.Succeeded(0);
                    return (current, stat);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
                {
                    await attempts.FailedAsync(ex, null, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            context?.Waiting(StepWait.None);
        }
    }

    /// <summary>A blocking file call off this thread; on a share, one that gets no answer ends as a timeout (and is left behind).</summary>
    internal async Task<T> CallAsync<T>(Func<T> call, bool network, CancellationToken ct)
    {
        var task = Task.Run(call, CancellationToken.None);
        try
        {
            return network ? await task.WaitAsync(Stall, ct).ConfigureAwait(false) : await task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // What it opens after all is closed again
            _ = task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result is OpenedSource late) SourceStream.CloseLater(late.Stream);
                else _ = t.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    /// <summary>Says which path a step's files are read through when it is not the one that was chosen, and when that changes.</summary>
    internal void Announce(string chosen, string used, IStepContext? context)
    {
        if (context is null) return;
        // By folder: the sidecars beside the video are read the same way
        var folder = Path.GetDirectoryName(used) ?? used;
        var last = _announced.GetValue(context, _ => new StrongBox<string>(Path.GetDirectoryName(chosen) ?? chosen));
        if (last.Value == folder) return;
        last.Value = folder;
        if (used == chosen) context.Log(LogLevel.Info, "share_chosen", $"Reading through the chosen path {used}", used);
        else context.Log(LogLevel.Info, "share_path", $"Reading through the network path {used}", used);
    }

    // ---------------------------------------------------------------- Scan

    public bool FolderExists(string folder) => Candidates(folder).Any(Files.FolderExists);

    /// <summary>
    /// Scans the folder by the first of its paths that answers; a share that drops half way is tried again a few times.
    /// <paramref name="scan"/> gets the path to read.
    /// </summary>
    public T Scan<T>(string folder, Func<string, T> scan, CancellationToken ct)
    {
        var candidates = Candidates(folder);
        for (var round = 0; ; round++)
        {
            ReadFailure? failure = null;
            Exception? last = null;
            foreach (var root in candidates)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return scan(root);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    var f = Classify(ex, root, IsNetwork(root) || candidates.Count > 1);
                    if (failure is null || f.Kind == ReadKind.Network) (failure, last) = (f, ex);
                }
            }
            if (failure!.Kind != ReadKind.Network || round >= 3) throw new SourceReadException(failure, null, last);
            Delay(TimeSpan.FromSeconds(1 << round), ct).GetAwaiter().GetResult();
        }
    }

    /// <summary>Why it failed. "Missing" on a share or drive that is itself away is the share's failure, not the file's.</summary>
    internal ReadFailure Classify(Exception error, string path, bool network)
    {
        var failure = ReadFailure.Of(error, network);
        if (failure.Code != "file_missing" || Files.RootAnswers(path)) return failure;
        return network
            ? failure with { Code = "share_not_found", Kind = ReadKind.Network }
            : failure with { Code = "drive_gone", Kind = ReadKind.Final };
    }
}

/// <summary>The tries of one read: which path is in use, how long the share has been away, where it stopped.</summary>
internal sealed class SourceAttempts
{
    /// <summary>Tries in place for a busy file or a disk that did not answer</summary>
    private const int BriefTries = 3;

    private readonly SourceAccess _access;
    private readonly string _chosen;
    private readonly IStepContext? _context;
    private readonly List<string> _candidates;
    private readonly HashSet<(string, long?, int?)> _logged = new();
    private int _index;
    private int _failedPaths;
    private int _rounds;
    private ReadFailure? _roundFailure;
    /// <summary>In this round a path opened the file and its read stopped</summary>
    private bool _roundStopped;
    private DateTimeOffset? _since;
    private bool _waiting;

    private long? _stopOffset;
    private int _stops;
    private long _carefulUntil;

    private SourceAttempts(SourceAccess access, string path, IStepContext? context, List<string> candidates, bool network)
    {
        _access = access;
        _chosen = path;
        _context = context;
        _candidates = candidates;
        Network = network;
    }

    public static async Task<SourceAttempts> CreateAsync(SourceAccess access, string path, IStepContext? context, CancellationToken ct, string? avoid = null)
    {
        var candidates = (await access.CandidatesAsync(path, ct).ConfigureAwait(false)).ToList();
        // What just failed goes last
        if (avoid is not null && candidates.Count > 1 && candidates.Remove(avoid)) candidates.Add(avoid);
        var network = false;
        foreach (var candidate in candidates) network |= await access.IsNetworkAsync(candidate, ct).ConfigureAwait(false);
        return new SourceAttempts(access, path, context, candidates, network);
    }

    public string Current => _candidates[_index];

    /// <summary>The file is on a share (asked while it was there, or while its drive was still mapped)</summary>
    public bool Network { get; private set; }

    /// <summary>Reads are kept short around a place a read stopped at, so the offset is exact (and what can be read there is)</summary>
    public bool Careful(long offset) => offset < _carefulUntil;

    /// <summary>A call answered; <paramref name="offset"/> is how far the file has been read.</summary>
    public void Succeeded(long offset)
    {
        if (_since is not null && _waiting)
        {
            _context?.Log(LogLevel.Info, "share_back", $"The network share answers again; going on from byte {Text(offset)}", Text(offset));
        }
        if (_waiting) _context?.Waiting(StepWait.None);
        _waiting = false;
        _since = null;
        _rounds = 0;
        _failedPaths = 0;
        _roundFailure = null;
        _roundStopped = false;
        if (_stopOffset is { } stop && offset > stop)
        {
            _stopOffset = null;
            _stops = 0;
        }
    }

    /// <summary>
    /// A call failed: moves to the other path, or waits; throws <see cref="SourceReadException"/> when there is nothing left to
    /// try. <paramref name="offset"/>: where a read stopped, null when the file could not be opened or looked up.
    /// </summary>
    public async Task FailedAsync(Exception error, long? offset, CancellationToken ct)
    {
        if (!Network) Network = await _access.IsNetworkAsync(Current, ct).ConfigureAwait(false);
        var path = Current;
        var network = Network;
        ReadFailure failure;
        try
        {
            failure = await _access.CallAsync(() => _access.Classify(error, path, network), network, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            failure = ReadFailure.Of(error, network);
        }
        if (offset is { } at)
        {
            if (_stopOffset == at) _stops++;
            else (_stopOffset, _stops) = (at, 1);
            _carefulUntil = at + (4 << 20);
            _roundStopped = true;
        }
        Log(failure, path, offset);

        // The round's failure is the one waiting can help with, if any path had one
        if (_roundFailure is null || (failure.Kind == ReadKind.Network && _roundFailure.Kind != ReadKind.Network)) _roundFailure = failure;
        _failedPaths++;
        _index = (_index + 1) % _candidates.Count;
        if (_failedPaths < _candidates.Count) return;

        var round = _roundFailure;
        var stopped = _roundStopped;
        _failedPaths = 0;
        _roundFailure = null;
        _roundStopped = false;
        _since ??= _access.Now;
        _rounds++;
        var over = round.Kind switch
        {
            ReadKind.Network => _access.Now - _since.Value >= _access.Patience,
            ReadKind.Brief => _rounds > BriefTries,
            _ => true,
        };
        if (over)
        {
            // A path opens the file to the last, and its read stops at one place each time: the file itself, whichever path
            // was tried last (the other may not open at all)
            if (round.Kind == ReadKind.Network && stopped && _stops >= 3) round = round with { Code = "share_damaged", Kind = ReadKind.Final };
            throw new SourceReadException(round, offset ?? _stopOffset, error);
        }
        if (round.Kind == ReadKind.Network && !_waiting)
        {
            _waiting = true;
            _context?.Waiting(StepWait.Share);
        }
        await _access.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, _rounds - 1))), ct).ConfigureAwait(false);
    }

    private void Log(ReadFailure failure, string path, long? offset)
    {
        if (_context is null || !_logged.Add((path, offset, failure.Number))) return;
        var where = offset is { } at ? $" at byte {Text(at)}" : "";
        // Args: the failure's code, the system, its number and text, the offset, the path
        _context.Log(LogLevel.Warning, "read_failed", $"Reading{where} failed: {failure.Message}. Path: {path}",
            failure.Code, failure.SystemName, failure.Number is { } n ? Text(n) : "", failure.Text, offset is { } o ? Text(o) : "", path);
    }

    private static string Text(long n) => n.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The file as one stream, whatever happens to the share under it: a failed read opens the file again (by its other path
/// when it has one), checks that it is the same file, and goes on at the same offset.
/// </summary>
public sealed class SourceStream : Stream
{
    private const int BufferSize = 1 << 20;
    private const int CarefulSize = 1 << 16;

    private readonly SourceAccess _access;
    private readonly string _path;
    private readonly IStepContext? _context;
    private SourceAttempts? _attempts;
    private long? _size;
    private long? _modifiedMs;
    private Stream? _inner;
    private byte[]? _buffer;
    private long _position;
    private bool _wasOpen;
    private bool _cloud;

    internal SourceStream(SourceAccess access, string path, long? size, long? modifiedMs, IStepContext? context)
    {
        _access = access;
        _path = path;
        _size = size;
        _modifiedMs = modifiedMs;
        _context = context;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _size ?? SizeAsync(CancellationToken.None).GetAwaiter().GetResult();

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    /// <summary>The paths are asked for with the first read, where the step can be cancelled.</summary>
    private async ValueTask<SourceAttempts> AttemptsAsync(CancellationToken ct) =>
        _attempts ??= await SourceAttempts.CreateAsync(_access, _path, _context, ct).ConfigureAwait(false);

    public async Task<long> SizeAsync(CancellationToken ct)
    {
        var attempts = await AttemptsAsync(ct).ConfigureAwait(false);
        while (_size is null)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await OpenAsync(attempts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsFailure(ex))
            {
                await attempts.FailedAsync(ex, null, ct).ConfigureAwait(false);
            }
        }
        return _size.Value;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (buffer.Length == 0 || (_size is { } size && _position >= size)) return 0;
        var attempts = await AttemptsAsync(ct).ConfigureAwait(false);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var reading = false;
            try
            {
                await OpenAsync(attempts, ct).ConfigureAwait(false);
                if (_position >= _size!.Value) return 0;
                reading = true;
                var want = (int)Math.Min(Math.Min(buffer.Length, attempts.Careful(_position) ? CarefulSize : BufferSize), _size.Value - _position);
                var n = await ReadInnerAsync(attempts, want, ct).ConfigureAwait(false);
                // Shorter than it was when it was opened
                if (n == 0) throw SourceAccess.Changed(read: true);
                _buffer.AsMemory(0, n).CopyTo(buffer);
                _position += n;
                attempts.Succeeded(_position);
                if (_cloud) Downloaded();
                return n;
            }
            catch (Exception ex) when (IsFailure(ex))
            {
                Close();
                await attempts.FailedAsync(ex, reading ? _position : null, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsFailure(Exception ex) => ex is (IOException and not SourceReadException) or UnauthorizedAccessException or TimeoutException;

    /// <summary>Into this stream's own buffer: a read that never ends is left behind with it, so Pause does not wait for a dead share.</summary>
    private async Task<int> ReadInnerAsync(SourceAttempts attempts, int want, CancellationToken ct)
    {
        var inner = _inner!;
        var buffer = _buffer ??= new byte[BufferSize];
        var position = _position;
        // Off this thread: on a share even the start of a read may block
        var read = Task.Run(async () =>
        {
            if (inner.Position != position) inner.Position = position;
            return await inner.ReadAsync(buffer.AsMemory(0, want), ct).ConfigureAwait(false);
        }, CancellationToken.None);
        try
        {
            return attempts.Network ? await read.WaitAsync(_access.Stall, ct).ConfigureAwait(false) : await read.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (!read.IsCompleted)
            {
                _buffer = null;
                _ = read.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            throw;
        }
    }

    private async Task OpenAsync(SourceAttempts attempts, CancellationToken ct)
    {
        if (_inner is not null) return;
        var path = attempts.Current;
        var opened = await _access.CallAsync(() => _access.Files.Open(path), attempts.Network, ct).ConfigureAwait(false);
        if ((_size is { } size && opened.Stat.Length != size) || (_modifiedMs is { } modified && opened.Stat.ModifiedMs != modified))
        {
            CloseLater(opened.Stream);
            throw SourceAccess.Changed(read: _wasOpen);
        }
        _size = opened.Stat.Length;
        _modifiedMs = opened.Stat.ModifiedMs;
        _inner = opened.Stream;
        _wasOpen = true;
        _access.Announce(_path, path, _context);
        if (opened.Stat.InCloud && !_cloud)
        {
            _cloud = true;
            _context?.Log(LogLevel.Info, "cloud_download", "Downloading the file from cloud storage first");
            _context?.Waiting(StepWait.Cloud);
        }
    }

    private void Downloaded()
    {
        _cloud = false;
        _context?.Waiting(StepWait.None);
    }

    private void Close()
    {
        if (_inner is { } inner) CloseLater(inner);
        _inner = null;
    }

    /// <summary>Closing a file on a dead share may block too.</summary>
    internal static void CloseLater(Stream stream) => _ = Task.Run(() =>
    {
        // Nobody waits for it: whatever closing a stream on a dead share throws ends here
        try { stream.Dispose(); } catch (Exception) { }
    });

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => Length + offset,
    };

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
            _context?.Waiting(StepWait.None);
        }
        base.Dispose(disposing);
    }
}

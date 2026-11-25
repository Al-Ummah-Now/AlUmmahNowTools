using System.Net;
using System.Reflection;
using System.Text;
using AlUmmahNow.ToolsCore;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Transcription;
using LogLevel = AlUmmahNow.ToolsCore.Import.LogLevel;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The bulk import of the signed-in environment (wiki TOOLS-ADMIN TA-11): its admin-api client, its work queue (one
/// file per environment, so a QA queue never uploads to production) and the reporter that sends the app's errors to
/// the Errors page. Made again when the environment changes.
/// </summary>
public sealed class ImportWorkspace : IDisposable
{
    private static ImportWorkspace? _current;
    private static readonly object Gate = new();
    private static bool _watching;
    private volatile bool _disposed;

    private ImportWorkspace(ShellState shell)
    {
        Environment = shell.Session.Environment;
        IsDemo = shell.Demo is not null;
        // The token of the session this workspace was made for, and none once the app has moved to another session:
        // a queue must never send one environment's token to another environment's APIs
        var session = Session = shell.Session;
        Func<CancellationToken, Task<string?>> token = _ =>
            _disposed || !ReferenceEquals(ShellState.Current.Session, session) ? Task.FromResult<string?>(null) : session.AccessTokenAsync();
        HttpMessageHandler? adminHandler = IsDemo ? new DemoImportServer() : null;
        // The server's error messages come in the app's language (admin-api's error envelope reads X-App-Locale)
        Api = new ImportApiClient(Environment.AdminApi, token, adminHandler) { AppLocale = () => Loc.IsArabic ? "ar" : "en" };
        // A YouTube source's Takeout files go up through the record's own upload (wiki TOOLS-ADMIN TA-9/TA-13)
        Takeout = new AlUmmahNow.ToolsCore.Social.TakeoutUploader(Api, new TusClient(), Environment.VideosApi, token);
        Reporter = new ErrorReporter(Api.ReportErrorsAsync, Release, () => shell.Preferences.Language);
        Media = new FfmpegMediaTools(LocateFfmpegAsync);
        Transcriber = IsDemo ? new DemoTranscriber() : new CoreTranscriber(CoreTranscriber.DefaultModelsDirectory);
        var steps = new IImportStep[]
        {
            new HashStep(),
            new EncodeStep(Api, Environment.VideosApi, Media, new CoreRenditionPackager(), EncodeStep.DefaultWorkRoot, "Al Ummah Now Tools " + Release, ReportEncodeFailure),
            new UploadStep(Api, new TusClient(), Environment.VideosApi, token),
            new AttachStep(Api),
            new TranscribeStep(Api, Transcriber, Media, new QuranIndexSource(Api), TranscribeStep.DefaultWorkRoot, ReportTranscribeFailure),
            new CheckStep(Api),
            new TranslateStep(Api),
        };
        var path = IsDemo
            ? Path.Combine(Path.GetTempPath(), "aun-tools-demo-" + Guid.NewGuid().ToString("N"), "queue.json")
            : ImportQueue.DefaultPath(Environment);
        WorkFiles.Skipped = text => Serilog.Log.Warning("[Import] {Text}", text);
        Queue = new ImportQueue(path, steps, new ServerFailureSink(Api, Reporter));
        if (IsDemo)
        {
            // Screenshots show a queue in every state; nothing runs and nothing is sent
            Queue.Add(DemoImport.QueueItems(Environment));
            DemoImport.Settle(Queue);
            DemoImport.SettleState(Queue);
        }
        else if (AppInstance.OwnsQueue)
        {
            Queue.Start();
        }
    }

    public ToolsEnvironment Environment { get; }

    /// <summary>The session the workspace belongs to; a new session gets a new workspace.</summary>
    private ToolsSession Session { get; }
    public bool IsDemo { get; }
    public ImportApiClient Api { get; }

    /// <summary>Sends a matched Takeout file to its record's upload.</summary>
    public AlUmmahNow.ToolsCore.Social.TakeoutUploader Takeout { get; }
    public ImportQueue Queue { get; }
    public ErrorReporter Reporter { get; }

    /// <summary>ffprobe and ffmpeg for encoding on this computer (TA-12)</summary>
    public IMediaTools Media { get; }

    /// <summary>Speech to text on this computer (TA-13): aun-core's whisper.cpp, GPU when there is one</summary>
    public ITranscriber Transcriber { get; }

    /// <summary>
    /// What stops encoding here, or null when it can: <c>ffmpeg_missing</c>, or <c>no_encoder</c> when this ffmpeg has
    /// no H.264 encoder. The page says so, and the server encodes those files.
    /// </summary>
    public async Task<string?> EncodeProblemAsync(CancellationToken ct = default)
    {
        if (IsDemo) return System.Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT_ENCODE") is { Length: > 0 } demo ? demo : null;
        try
        {
            return await Media.PickEncoderAsync(ct).ConfigureAwait(false) is null ? "no_encoder" : null;
        }
        catch (LocalEncodeException ex)
        {
            return ex.Code;
        }
    }

    /// <summary>The paths saved on the Video tools page, else the ones the app found on this computer.</summary>
    private static async Task<(string Ffmpeg, string Ffprobe)?> LocateFfmpegAsync(CancellationToken ct)
    {
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            if (settings.Containers.TryGetValue("FFmpegSettings", out var saved)
                && saved.Values["FFmpegPath"] is string ffmpeg && saved.Values["FFprobePath"] is string ffprobe
                && File.Exists(ffmpeg) && File.Exists(ffprobe))
            {
                return (ffmpeg, ffprobe);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // No saved settings on this platform: the found paths follow
        }
        var manager = App.GetService<AlUmmahNow.Data.Services.IFFmpegManagerService>();
        if (manager.FFmpegPath is null || manager.FFprobePath is null) await manager.AutoDetectFFmpegAsync().WaitAsync(ct).ConfigureAwait(false);
        return manager.FFmpegPath is { } found && manager.FFprobePath is { } probe ? (found, probe) : null;
    }

    /// <summary>A local encode that failed goes to the Errors page; the file itself carries on, encoded by the server.</summary>
    private void ReportEncodeFailure(Exception error, QueueItem item) =>
        Reporter.Report("caught", error, "Import Encode on this computer failed; the server encodes the file: " + ImportQueue.Describe(error), "/import/encode", new Dictionary<string, object?>
        {
            ["step"] = nameof(ImportStepKind.Encode),
            ["code"] = (error as LocalEncodeException)?.Code ?? "disk",
            ["itemId"] = item.ServerItemId,
            ["videoId"] = item.VideoId,
            ["fileSize"] = item.Video.Size,
        });

    /// <summary>A local transcription that failed goes to the Errors page; the file itself carries on without a transcript.</summary>
    private void ReportTranscribeFailure(Exception error, QueueItem item) =>
        Reporter.Report("caught", error, "Import Transcribe on this computer failed; the file is imported without a transcript: " + ImportQueue.Describe(error), "/import/transcribe", new Dictionary<string, object?>
        {
            ["step"] = nameof(ImportStepKind.Transcribe),
            ["code"] = error switch { TranscribeException t => t.Code, LocalEncodeException l => l.Code, ImportApiException a => a.Code, _ => "disk" },
            ["itemId"] = item.ServerItemId,
            ["videoId"] = item.VideoId,
            ["model"] = item.SpeechModel,
            ["device"] = Transcriber.Device is { } d ? d.Backend : null,
        });

    /// <summary>The app's version, sent with each error report</summary>
    public static string Release { get; } =
        typeof(ImportWorkspace).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "dev";

    /// <summary>The workspace of the session's environment, made on first use.</summary>
    public static ImportWorkspace Current
    {
        get
        {
            lock (Gate)
            {
                var shell = ShellState.Current;
                if (!_watching)
                {
                    // Stop the old environment's queue as soon as the environment changes, not when the page is next opened
                    shell.Changed += (_, _) => DropIfStale();
                    _watching = true;
                }
                if (_current is not null && ReferenceEquals(_current.Session, shell.Session)) return _current;
                _current?.Dispose();
                _current = new ImportWorkspace(shell);
                return _current;
            }
        }
    }

    private static void DropIfStale()
    {
        lock (Gate)
        {
            if (_current is null || ReferenceEquals(_current.Session, ShellState.Current.Session)) return;
            _current.Dispose();
            _current = null;
        }
    }

    /// <summary>The workspace if one was made, for reporting from places that must never make one.</summary>
    public static ImportWorkspace? IfStarted
    {
        get { lock (Gate) return _current; }
    }

    public void Dispose()
    {
        _disposed = true;
        Queue.Dispose();
        _ = Reporter.FlushAsync().ContinueWith(_ => Reporter.Dispose(), TaskScheduler.Default);
    }

    /// <summary>Files being imported now (running, queued, waiting to try again), for the quit question (Tools #78).</summary>
    public static int ActiveFiles => IfStarted is { } workspace ? QuitGuard.ActiveFiles(workspace.Queue.Items) : 0;

    /// <summary>
    /// Stops the queue for good: running steps stop (ffmpeg and the other child processes with them), the queue is saved so
    /// every file carries on from where it was at the next start, and the error reports waiting are sent (a few seconds at most).
    /// </summary>
    public static async Task StopForQuitAsync()
    {
        ImportWorkspace? workspace;
        lock (Gate) workspace = _current;
        if (workspace is null) return;
        workspace._disposed = true;
        await workspace.Queue.StopAsync().ConfigureAwait(false);
        try { await workspace.Reporter.FlushAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }
        workspace.Reporter.Dispose();
    }
}

/// <summary>The import's demo data for screenshots (<c>TOOLS_DEMO=1</c>; <c>TOOLS_DEMO_IMPORT=empty</c> for a fresh page, 
/// <c>paused</c> for the queue after Pause all, <c>resumed</c> with one file resumed after it, <c>remove-all</c> for its dialog,
/// <c>share-waiting</c>, <c>share-failed</c> and <c>damaged</c> for a file on a network share, in the fourth row).</summary>
internal static class DemoImport
{
    public static bool Empty => System.Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT") == "empty";

    public static string State => System.Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT") ?? "";

    public const string ChannelId = "ch_demo_fiqh";

    /// <summary>The demo's "now": 2:20 PM today in the device's zone, so every frame (en and ar) shows the same times.</summary>
    public static readonly DateTimeOffset Now = new(DateTime.Today.AddHours(14).AddMinutes(20), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today.AddHours(14)));

    public static readonly ImportChannel[] Channels =
    {
        new(ChannelId, "Shaykh Ahmad al-Azhari", "azhari", "ahmad.azhari@mail.alummahnow.net", new Dictionary<string, string> { ["en"] = "Shaykh Ahmad al-Azhari", ["ar"] = "الشيخ أحمد الأزهري" }),
        new("ch_demo_seerah", "Seerah Circle", "seerahcircle", "seerah.circle@mail.alummahnow.net", new Dictionary<string, string> { ["en"] = "Seerah Circle", ["ar"] = "حلقة السيرة" }),
        new("ch_demo_quran", "Quran Recitations Archive", "recitations", "archive@mail.alummahnow.net", new Dictionary<string, string> { ["en"] = "Quran Recitations Archive", ["ar"] = "أرشيف تلاوات القرآن" }),
    };

    private static ScannedVideo Video(string folder, string name, long size, string? titleAr, string[] captions, bool chapters, bool thumbnail, ScanNote[]? notes = null) =>
        new(
            Path.Combine(folder, name),
            size,
            0,
            "video/mp4",
            captions.Select(l => new CaptionFile(l, Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + "." + l + ".srt"))).ToArray(),
            chapters ? Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + ".chapters.txt") : null,
            thumbnail ? Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + ".jpg") : null,
            null,
            new ImportMetadata
            {
                FallbackTitle = Path.GetFileNameWithoutExtension(name),
                TitleEn = Titles.TryGetValue(name, out var t) ? t : null,
                TitleAr = titleAr,
            },
            notes ?? Array.Empty<ScanNote>());

    private static readonly Dictionary<string, string> Titles = new()
    {
        ["fiqh-of-fasting-01.mp4"] = "The Fiqh of Fasting, lesson 1: intention",
        ["fiqh-of-fasting-02.mp4"] = "The Fiqh of Fasting, lesson 2: what breaks the fast",
        ["fiqh-of-fasting-03.mp4"] = "The Fiqh of Fasting, lesson 3: travellers and the sick",
        ["fiqh-of-fasting-04.mp4"] = "The Fiqh of Fasting, lesson 4: making up missed days",
        ["fiqh-of-fasting-05.mp4"] = "The Fiqh of Fasting, lesson 5: i’tikaf",
        ["fiqh-of-fasting-06.mp4"] = "The Fiqh of Fasting, lesson 6: zakat al-fitr",
        ["fiqh-of-fasting-07.mp4"] = "The Fiqh of Fasting, lesson 7: Eid",
        ["fiqh-of-fasting-08.mp4"] = "The Fiqh of Fasting, lesson 8: the night prayer",
        ["fiqh-of-fasting-09.mp4"] = "The Fiqh of Fasting, lesson 9: Laylat al-Qadr",
        ["fiqh-of-fasting-14.mp4"] = "The Fiqh of Fasting, lesson 14: the verses of fasting",
        ["fiqh-of-fasting-15.mp4"] = "The Fiqh of Fasting, lesson 15: review",
        ["fiqh-of-fasting-10.mp4"] = "The Fiqh of Fasting, lesson 10: voluntary fasts",
        ["fiqh-of-fasting-11.mp4"] = "The Fiqh of Fasting, lesson 11: questions and answers",
        ["fiqh-of-fasting-12.mp4"] = "The Fiqh of Fasting, lesson 12: the ransom",
        ["fiqh-of-fasting-13.mp4"] = "The Fiqh of Fasting, lesson 13: closing",
    };

    public const string Folder = "/Volumes/Media/Lessons/Fiqh of Fasting";

    public static ScanResult Scan() => new(new[]
    {
        Video(Folder, "fiqh-of-fasting-08.mp4", 1_288_490_189, "فقه الصيام، الدرس الثامن: قيام الليل", new[] { "ar", "en" }, true, true),
        Video(Folder, "fiqh-of-fasting-09.mp4", 1_181_116_006, "فقه الصيام، الدرس التاسع: ليلة القدر", new[] { "ar", "en" }, true, true),
        Video(Folder, "fiqh-of-fasting-10.mp4", 1_395_864_371, "فقه الصيام، الدرس العاشر: صيام التطوع", new[] { "ar" }, true, true),
        Video(Folder, "fiqh-of-fasting-11.mp4", 966_367_641, "فقه الصيام، الدرس الحادي عشر: أسئلة وأجوبة", Array.Empty<string>(), false, false, new[] { new ScanNote(ScanNoteCode.CaptionLanguageUnknown, "fiqh-of-fasting-11.srt") }),
    }, Array.Empty<ScanNote>());

    public static IEnumerable<QueueItem> QueueItems(ToolsEnvironment environment)
    {
        if (Empty) yield break;
        var start = Now.AddMinutes(-42);
        var folder = "/Volumes/Media/Lessons/Fiqh of Fasting";
        var files = new[]
        {
            Video(folder, "fiqh-of-fasting-01.mp4", 1_288_490_189, "فقه الصيام، الدرس الأول: النية", new[] { "ar", "en" }, true, true),
            Video(folder, "fiqh-of-fasting-02.mp4", 1_181_116_006, "فقه الصيام، الدرس الثاني: المفطرات", new[] { "ar", "en" }, true, true),
            Video(folder, "fiqh-of-fasting-03.mp4", 1_395_864_371, "فقه الصيام، الدرس الثالث: المسافر والمريض", new[] { "ar" }, true, true),
            Video(folder, "fiqh-of-fasting-04.mp4", 1_073_741_824, "فقه الصيام، الدرس الرابع: قضاء الأيام", new[] { "ar" }, false, true),
            Video(folder, "fiqh-of-fasting-05.mp4", 966_367_641, "فقه الصيام، الدرس الخامس: الاعتكاف", Array.Empty<string>(), false, false),
            Video(folder, "fiqh-of-fasting-06.mp4", 1_234_567_890, "فقه الصيام، الدرس السادس: زكاة الفطر", new[] { "ar" }, false, true),
            Video(folder, "fiqh-of-fasting-07.mp4", 1_098_765_432, "فقه الصيام، الدرس السابع: العيد", new[] { "ar" }, false, true),
            Video(folder, "fiqh-of-fasting-12.mp4", 1_152_921_504, "فقه الصيام، الدرس الثاني عشر: الفدية", new[] { "ar" }, false, true),
            Video(folder, "fiqh-of-fasting-13.mp4", 1_020_054_732, "فقه الصيام، الدرس الثالث عشر: خاتمة", Array.Empty<string>(), false, false),
            // Transcribed on this computer (TA-13): no caption files of their own
            Video(folder, "fiqh-of-fasting-14.mp4", 1_110_183_936, "فقه الصيام، الدرس الرابع عشر: تفسير آيات الصيام", Array.Empty<string>(), false, true),
            Video(folder, "fiqh-of-fasting-15.mp4", 1_046_478_848, "فقه الصيام، الدرس الخامس عشر: مراجعة", Array.Empty<string>(), false, true),
        };
        for (var i = 0; i < files.Length; i++)
        {
            // The ninth file's batch was added with "Let the server encode"; the last two were transcribed here
            yield return QueueItem.For(files[i], "batch_demo", ChannelId, start.AddSeconds(i), serverEncodes: i == 8, transcribe: i >= 9 ? new TranscribeChoice(null, CoreTranscriber.DefaultModel) : null, translate: i >= 9);
        }
    }

    /// <summary>What <c>TOOLS_DEMO_IMPORT</c> asks for beyond the settled queue.</summary>
    public static void SettleState(ImportQueue queue)
    {
        var items = queue.Items;
        if (items.Count < 9) return;
        if (State is "paused" or "resumed") queue.PauseAll();
        // Queued: it goes on as queued, beside the others, all paused
        if (State == "resumed") queue.Resume(items[8].Id);
        if (State is not ("share-waiting" or "share-failed" or "damaged")) return;

        // The fourth file (or TOOLS_DEMO_IMPORT_ROW), on a share: checked as far as 41%
        var item = items[int.TryParse(System.Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT_ROW"), out var row) && row < items.Count ? row : 3];
        item.Verdict = null;
        foreach (var s in item.Steps)
        {
            s.Status = StepStatus.Pending;
            s.Attempts = 0;
            s.NextAttemptAt = null;
            s.LastError = s.LastErrorCode = null;
        }
        item.Log.RemoveRange(1, item.Log.Count - 1);
        item.VideoId = null;
        var step = item.Steps.First(s => s.Kind == ImportStepKind.Hash);
        step.Attempts = 1;
        const string offset = "440401920";
        const string text = "An unexpected network error occurred";
        var path = @"\\nas\media\Lessons\Fiqh of Fasting\" + item.Video.FileName;
        var at = Now.AddMinutes(-8);
        item.Log.AddRange(new[]
        {
            new LogLine(at, LogLevel.Info, "Hash: started", "started", new[] { "Hash" }),
            new LogLine(at, LogLevel.Info, "Reading through the network path", "share_path", new[] { path }),
            new LogLine(at.AddMinutes(2), LogLevel.Warning, "Reading failed", "read_failed", new[] { "share_lost", "Windows", "59", text, offset, path }),
        });
        if (State == "share-waiting")
        {
            step.Status = StepStatus.Running;
            typeof(QueueItem).GetProperty(nameof(QueueItem.Progress))!.SetValue(item, 0.41);
            typeof(QueueItem).GetProperty(nameof(QueueItem.Wait))!.SetValue(item, StepWait.Share);
            return;
        }
        step.Status = StepStatus.Failed;
        step.LastErrorCode = State == "damaged" ? "share_damaged" : "share_lost";
        step.LastError = text;
        step.ReadError = new ReadError("Windows", 59, text, 440401920);
        item.Log.Add(new LogLine(at.AddMinutes(7), LogLevel.Error, "Hash: failed", "failed", new[] { "Hash", "1", step.LastErrorCode, "ERR-7K2M-Q9XD", text, "Windows", "59", text, offset }));
    }

    /// <summary>
    /// Puts the demo files in the states the screenshots show: served as encoded here, encoded again by the server, the
    /// server processing, waiting to retry (its log), encoding here, uploading, failed, paused, and waiting with the
    /// server encoding.
    /// </summary>
    public static void Settle(ImportQueue queue)
    {
        var items = queue.Items;
        if (items.Count == 0) return;
        // "Added to the queue" at the moment each file was added, before everything after it in its log
        foreach (var item in items)
        {
            for (var i = 0; i < item.Log.Count; i++) if (item.Log[i].Code == "added") item.Log[i] = item.Log[i] with { At = item.CreatedAt };
        }
        StepState S(QueueItem item, ImportStepKind kind) => item.Steps.First(s => s.Kind == kind);
        // Every step before the given one done, that one in the given state, the rest pending
        void At(QueueItem item, ImportStepKind? kind, StepStatus status = StepStatus.Pending)
        {
            foreach (var step in item.Steps)
            {
                step.Status = kind is null || step.Kind < kind ? StepStatus.Done : step.Kind == kind ? status : StepStatus.Pending;
            }
        }
        void Progress(QueueItem item, double value) => typeof(QueueItem).GetProperty(nameof(QueueItem.Progress))!.SetValue(item, value);

        At(items[0], null);
        items[0].VideoId = "v8Kq2LmT4x";
        items[0].Verdict = new RenditionVerdict(true, null, null);
        At(items[1], null);
        items[1].VideoId = "v3Hn7PwR1c";
        items[1].Verdict = new RenditionVerdict(false, "keyframes", "720p");
        At(items[2], ImportStepKind.Check);
        items[2].VideoId = "v6Jd4NcY8b";
        S(items[2], ImportStepKind.Check).WaitingOnServer = true;
        S(items[2], ImportStepKind.Check).Attempts = 3;
        S(items[2], ImportStepKind.Check).NextAttemptAt = Now.AddSeconds(20);

        At(items[3], ImportStepKind.Upload);
        var upload = S(items[3], ImportStepKind.Upload);
        upload.Attempts = 2;
        upload.NextAttemptAt = Now.AddSeconds(40);
        upload.LastError = "The videos API could not be reached";
        upload.LastErrorCode = "videos_unreachable";
        var at = Now.AddMinutes(-26);
        items[3].Log.AddRange(new[]
        {
            new LogLine(at, LogLevel.Info, "Hash: started", "started", new[] { "Hash" }),
            new LogLine(at.AddSeconds(9), LogLevel.Info, "SHA-256", "sha", new[] { "9f2c4e81b7a05d3c6e1f48a2d9b07c5e3a61f8d24b9e07c1a5d36f2e8b40c917" }),
            new LogLine(at.AddSeconds(9), LogLevel.Info, "Hash: done", "done", new[] { "Hash" }),
            new LogLine(at.AddSeconds(10), LogLevel.Info, "Encode: started", "started", new[] { "Encode" }),
            new LogLine(at.AddSeconds(11), LogLevel.Info, "Upload opened", "upload_opened", new[] { "v5Tg9KdQ2m" }),
            new LogLine(at.AddSeconds(12), LogLevel.Info, "Encoding", "encode_plan", new[] { "h264_videotoolbox", "360p, 480p, 720p, 1080p" }),
            new LogLine(at.AddMinutes(19), LogLevel.Info, "Encoded", "encoded", new[] { "4", "734003200", items[3].Video.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
            new LogLine(at.AddMinutes(19), LogLevel.Info, "Encode: done", "done", new[] { "Encode" }),
            new LogLine(at.AddMinutes(19).AddSeconds(1), LogLevel.Info, "Upload: started", "started", new[] { "Upload" }),
            new LogLine(at.AddMinutes(19).AddSeconds(2), LogLevel.Info, "Sending the renditions", "renditions_staged", new[] { "1460", "734003200" }),
            new LogLine(at.AddMinutes(24), LogLevel.Warning, "Upload: attempt 1 failed", "attempt_failed", new[] { "Upload", "1", "videos_unreachable", "20", "The videos API could not be reached" }),
            new LogLine(at.AddMinutes(24).AddSeconds(20), LogLevel.Info, "Upload: attempt 2", "attempt", new[] { "Upload", "2" }),
            new LogLine(at.AddMinutes(25), LogLevel.Warning, "Upload: attempt 2 failed", "attempt_failed", new[] { "Upload", "2", "videos_unreachable", "40", "The videos API could not be reached" }),
        });

        At(items[4], ImportStepKind.Encode, StepStatus.Running);
        S(items[4], ImportStepKind.Encode).Attempts = 1;
        Progress(items[4], 0.38);
        At(items[5], ImportStepKind.Upload, StepStatus.Running);
        S(items[5], ImportStepKind.Upload).Attempts = 1;
        Progress(items[5], 0.64);
        At(items[6], ImportStepKind.Hash, StepStatus.Failed);
        S(items[6], ImportStepKind.Hash).Attempts = 1;
        S(items[6], ImportStepKind.Hash).LastError = "The file changed after it was scanned. Scan the folder again";
        S(items[6], ImportStepKind.Hash).LastErrorCode = "file_changed";
        At(items[7], ImportStepKind.Encode);
        items[7].Paused = true;
        At(items[8], ImportStepKind.Hash);
        if (items.Count < 11) return;

        // Transcribing on this computer, and a transcript sent whose draft the server is still writing (TA-13)
        At(items[9], ImportStepKind.Transcribe, StepStatus.Running);
        items[9].VideoId = "v2Wm8FsK6d";
        S(items[9], ImportStepKind.Transcribe).Attempts = 1;
        Progress(items[9], 0.52);
        At(items[10], ImportStepKind.Translate);
        items[10].VideoId = "v9Pc3ZbN5h";
        items[10].Verdict = new RenditionVerdict(true, null, null);
        S(items[10], ImportStepKind.Translate).WaitingOnServer = true;
        S(items[10], ImportStepKind.Translate).NextAttemptAt = Now.AddSeconds(30);
        items[10].Transcript = new TranscriptState { Language = "ar", Segments = 412, QuranSpans = 3, Model = CoreTranscriber.DefaultModel, Device = DemoTranscriber.DeviceName, Sent = true, SentAt = Now.AddMinutes(-4) };
        var t = Now.AddMinutes(-31);
        items[10].Log.AddRange(new[]
        {
            new LogLine(t, LogLevel.Info, "Transcribe: started", "started", new[] { "Transcribe" }),
            new LogLine(t.AddSeconds(1), LogLevel.Info, "Downloading the speech model", "model_download", new[] { CoreTranscriber.DefaultModel }),
            new LogLine(t.AddMinutes(27), LogLevel.Info, "Transcribed", "transcribed", new[] { "ar", "412", "3", DemoTranscriber.DeviceName, CoreTranscriber.DefaultModel }),
            new LogLine(t.AddMinutes(27), LogLevel.Info, "The transcript is with the server", "transcript_sent", new[] { "ar" }),
            new LogLine(t.AddMinutes(27), LogLevel.Info, "Transcribe: done", "done", new[] { "Transcribe" }),
        });
    }
}

/// <summary>
/// Transcription for screenshots: a fixed device so every frame (en and ar) shows the same one, nothing run.
/// <c>TOOLS_DEMO_IMPORT_TRANSCRIBE</c>: <c>unavailable</c> (a build without it), <c>cpu</c> (no GPU), <c>installed</c> (the model is here).
/// </summary>
internal sealed class DemoTranscriber : ITranscriber
{
    public const string DeviceName = "Apple M2 Pro (Metal)";
    private static string Mode => System.Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT_TRANSCRIBE") ?? "";

    public bool Available => Mode != "unavailable";
    public SpeechDevice? Device => !Available ? null : Mode == "cpu" ? new SpeechDevice(false, "CPU", "Apple M2 Pro") : new SpeechDevice(true, "Metal", "Apple M2 Pro");
    // aun-core's catalog sizes, pinned
    public IReadOnlyList<SpeechModel> Models => !Available ? [] :
    [
        new("large-v3-turbo", 1_624_555_275), new("large-v3-turbo-q5_0", 574_041_195), new("medium-q5_0", 539_212_467),
        new("small-q5_1", 190_085_487), new("base-q5_1", 59_707_625),
    ];
    public string? InstalledPath(string modelId) => Mode == "installed" ? "/models/" + modelId + ".bin" : null;
    public Task<string> DownloadAsync(string modelId, IProgress<double> progress, CancellationToken ct) => Task.FromException<string>(new TranscribeException("unavailable", "Demo"));
    public Task<string> TranscribeAsync(string modelPath, string mediaPath, string? language, IProgress<double> progress, CancellationToken ct) => Task.FromException<string>(new TranscribeException("unavailable", "Demo"));
}

/// <summary>admin-api for screenshots: the channel list, new channels, batches, field translation and social sources; nothing sent anywhere.</summary>
internal sealed class DemoImportServer : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (DemoSocial.IsMissing(request.RequestUri))
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("404 Not Found", Encoding.UTF8, "text/plain") };
        // Social sources and their sync jobs (TA-13)
        if (DemoSocial.Answer(request) is { } social)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(social, Encoding.UTF8, "application/json") };
        string body;
        if (path == "/api/content/channels")
        {
            body = "{\"success\":true,\"result\":{\"items\":[" + string.Join(",", DemoImport.Channels.Select(c =>
            {
                var locJson = c.NameLocalized is null ? "" : ",\"nameLocalized\":" + System.Text.Json.JsonSerializer.Serialize(c.NameLocalized);
                return $"{{\"id\":\"{c.Id}\",\"name\":\"{c.Name}\",\"handle\":\"{c.Handle}\",\"ownerEmail\":\"{c.OwnerEmail}\"{locJson}}}";
            })) + "],\"total\":3,\"page\":1,\"pageSize\":20,\"indexComplete\":true}}";
        }
        else if (path == "/api/admin/import/channels")
        {
            body = "{\"success\":true,\"result\":{\"channel\":{\"id\":\"ch_demo_new\",\"name\":\"Fiqh Academy\",\"nameLocalized\":{\"en\":\"Fiqh Academy\",\"ar\":\"أكاديمية الفقه\"},\"handle\":\"fiqhacademy\"}}}";
        }
        else if (path == "/api/admin/import/batches")
        {
            body = "{\"success\":true,\"result\":{\"batch\":{\"id\":\"batch_demo_2\",\"channelId\":\"" + DemoImport.ChannelId + "\",\"name\":null}}}";
        }
        else if (path == "/api/translate/fields")
        {
            var reqText = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            var toAr = reqText.Contains("\"to\":\"ar\"", StringComparison.OrdinalIgnoreCase);
            var translated = toAr ? "أكاديمية الفقه" : "Fiqh Academy";
            body = "{\"success\":true,\"result\":{\"items\":[{\"text\":\"" + translated + "\",\"cached\":false}],\"provider\":\"workers-ai\",\"machineTranslation\":true}}";
        }
        else
        {
            body = "{\"success\":true,\"result\":{}}";
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

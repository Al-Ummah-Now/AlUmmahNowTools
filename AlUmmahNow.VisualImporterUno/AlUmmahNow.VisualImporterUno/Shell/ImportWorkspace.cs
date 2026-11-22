using System.Net;
using System.Reflection;
using System.Text;
using AlUmmahNow.ToolsCore;
using AlUmmahNow.ToolsCore.Import;
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

    private ImportWorkspace(ShellState shell)
    {
        Environment = shell.Session.Environment;
        IsDemo = shell.Demo is not null;
        Func<CancellationToken, Task<string?>> token = _ => ShellState.Current.Session.AccessTokenAsync();
        HttpMessageHandler? adminHandler = IsDemo ? new DemoImportServer() : null;
        Api = new ImportApiClient(Environment.AdminApi, token, adminHandler);
        Reporter = new ErrorReporter(Api.ReportErrorsAsync, Release, () => shell.Preferences.Language);
        var steps = new IImportStep[]
        {
            new HashStep(),
            new UploadStep(Api, new TusClient(), Environment.VideosApi, token),
            new AttachStep(Api),
        };
        var path = IsDemo
            ? Path.Combine(Path.GetTempPath(), "aun-tools-demo-" + Guid.NewGuid().ToString("N"), "queue.json")
            : ImportQueue.DefaultPath(Environment);
        Queue = new ImportQueue(path, steps, new ServerFailureSink(Api, Reporter));
        if (IsDemo)
        {
            // Screenshots show a queue in every state; nothing runs and nothing is sent
            Queue.Add(DemoImport.QueueItems(Environment));
            DemoImport.Settle(Queue);
        }
        else
        {
            Queue.Start();
        }
    }

    public ToolsEnvironment Environment { get; }
    public bool IsDemo { get; }
    public ImportApiClient Api { get; }
    public ImportQueue Queue { get; }
    public ErrorReporter Reporter { get; }

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
                if (_current is not null && _current.Environment.Kind == shell.Session.Environment.Kind) return _current;
                _current?.Dispose();
                _current = new ImportWorkspace(shell);
                return _current;
            }
        }
    }

    /// <summary>The workspace if one was made, for reporting from places that must never make one.</summary>
    public static ImportWorkspace? IfStarted
    {
        get { lock (Gate) return _current; }
    }

    public void Dispose()
    {
        Queue.Dispose();
        _ = Reporter.FlushAsync().ContinueWith(_ => Reporter.Dispose(), TaskScheduler.Default);
    }
}

/// <summary>The import's demo data for screenshots (<c>TOOLS_DEMO=1</c>; <c>TOOLS_DEMO_IMPORT=empty</c> for a fresh page).</summary>
internal static class DemoImport
{
    public static bool Empty => System.Environment.GetEnvironmentVariable("TOOLS_DEMO_IMPORT") == "empty";

    public const string ChannelId = "ch_demo_fiqh";

    public static readonly ImportChannel[] Channels =
    {
        new(ChannelId, "Shaykh Ahmad al-Azhari", "azhari", "ahmad.azhari@alummahnow.net"),
        new("ch_demo_seerah", "Seerah Circle", "seerahcircle", "seerah.circle@alummahnow.net"),
        new("ch_demo_quran", "Quran Recitations Archive", "recitations", "archive@alummahnow.net"),
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
        ["fiqh-of-fasting-05.mp4"] = "The Fiqh of Fasting, lesson 5: i'tikaf",
        ["fiqh-of-fasting-06.mp4"] = "The Fiqh of Fasting, lesson 6: zakat al-fitr",
        ["fiqh-of-fasting-07.mp4"] = "The Fiqh of Fasting, lesson 7: Eid",
        ["fiqh-of-fasting-08.mp4"] = "The Fiqh of Fasting, lesson 8: the night prayer",
        ["fiqh-of-fasting-09.mp4"] = "The Fiqh of Fasting, lesson 9: Laylat al-Qadr",
        ["fiqh-of-fasting-10.mp4"] = "The Fiqh of Fasting, lesson 10: voluntary fasts",
        ["fiqh-of-fasting-11.mp4"] = "The Fiqh of Fasting, lesson 11: questions and answers",
    };

    public const string Folder = "/Volumes/Media/Lessons/Fiqh of Fasting";

    public static ScanResult Scan() => new(new[]
    {
        Video(Folder, "fiqh-of-fasting-08.mp4", 1_288_490_189, "فقه الصيام، الدرس الثامن: قيام الليل", new[] { "ar", "en" }, true, true),
        Video(Folder, "fiqh-of-fasting-09.mp4", 1_181_116_006, "فقه الصيام، الدرس التاسع: ليلة القدر", new[] { "ar", "en" }, true, true),
        Video(Folder, "fiqh-of-fasting-10.mp4", 1_395_864_371, "فقه الصيام، الدرس العاشر: صيام التطوع", new[] { "ar" }, true, true),
        Video(Folder, "fiqh-of-fasting-11.mp4", 966_367_641, null, Array.Empty<string>(), false, false, new[] { new ScanNote(ScanNoteCode.CaptionLanguageUnknown, "fiqh-of-fasting-11.srt") }),
    }, Array.Empty<ScanNote>());

    public static IEnumerable<QueueItem> QueueItems(ToolsEnvironment environment)
    {
        if (Empty) yield break;
        var start = DateTimeOffset.UtcNow.AddMinutes(-42);
        var folder = "/Volumes/Media/Lessons/Fiqh of Fasting";
        var files = new[]
        {
            Video(folder, "fiqh-of-fasting-01.mp4", 1_288_490_189, "فقه الصيام، الدرس الأول: النية", new[] { "ar", "en" }, true, true),
            Video(folder, "fiqh-of-fasting-02.mp4", 1_181_116_006, "فقه الصيام، الدرس الثاني: المفطرات", new[] { "ar", "en" }, true, true),
            Video(folder, "fiqh-of-fasting-03.mp4", 1_395_864_371, "فقه الصيام، الدرس الثالث: المسافر والمريض", new[] { "ar" }, true, true),
            Video(folder, "fiqh-of-fasting-04.mp4", 1_073_741_824, "فقه الصيام، الدرس الرابع: قضاء الأيام", new[] { "ar" }, false, true),
            Video(folder, "fiqh-of-fasting-05.mp4", 966_367_641, null, Array.Empty<string>(), false, false),
            Video(folder, "fiqh-of-fasting-06.mp4", 1_234_567_890, null, new[] { "ar" }, false, true),
            Video(folder, "fiqh-of-fasting-07.mp4", 1_098_765_432, null, new[] { "ar" }, false, true),
        };
        for (var i = 0; i < files.Length; i++)
        {
            yield return QueueItem.For(files[i], "batch_demo", ChannelId, start.AddSeconds(i));
        }
    }

    /// <summary>Puts the demo files in the states the screenshots show: done, uploading, waiting to retry, failed, paused, waiting.</summary>
    public static void Settle(ImportQueue queue)
    {
        var items = queue.Items;
        if (items.Count == 0) return;
        void Steps(QueueItem item, params StepStatus[] statuses)
        {
            for (var i = 0; i < statuses.Length; i++) item.Steps[i].Status = statuses[i];
        }
        Steps(items[0], StepStatus.Done, StepStatus.Done, StepStatus.Done);
        items[0].VideoId = "v8Kq2LmT4x";
        Steps(items[1], StepStatus.Done, StepStatus.Done, StepStatus.Done);
        items[1].VideoId = "v3Hn7PwR1c";
        Steps(items[2], StepStatus.Done, StepStatus.Running, StepStatus.Pending);
        items[2].Steps[1].Attempts = 1;
        typeof(QueueItem).GetProperty(nameof(QueueItem.Progress))!.SetValue(items[2], 0.64);
        Steps(items[3], StepStatus.Done, StepStatus.Pending, StepStatus.Pending);
        items[3].Steps[1].Attempts = 2;
        items[3].Steps[1].NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(40);
        items[3].Steps[1].LastError = "The videos API could not be reached";
        items[3].Steps[1].LastErrorCode = "videos_unreachable";
        var at = DateTimeOffset.UtcNow.AddMinutes(-6);
        items[3].Log.AddRange(new[]
        {
            new LogLine(at, LogLevel.Info, "Hash: started", "started", new[] { "Hash" }),
            new LogLine(at.AddSeconds(9), LogLevel.Info, "SHA-256", "sha", new[] { "9f2c4e81b7a05d3c6e1f48a2d9b07c5e3a61f8d24b9e07c1a5d36f2e8b40c917" }),
            new LogLine(at.AddSeconds(9), LogLevel.Info, "Hash: done", "done", new[] { "Hash" }),
            new LogLine(at.AddSeconds(10), LogLevel.Info, "Upload: started", "started", new[] { "Upload" }),
            new LogLine(at.AddSeconds(11), LogLevel.Info, "Upload opened", "upload_opened", new[] { "v5Tg9KdQ2m" }),
            new LogLine(at.AddMinutes(4), LogLevel.Warning, "Upload: attempt 1 failed", "attempt_failed", new[] { "Upload", "1", "videos_unreachable", "20", "The videos API could not be reached" }),
            new LogLine(at.AddMinutes(4).AddSeconds(20), LogLevel.Info, "Upload: attempt 2", "attempt", new[] { "Upload", "2" }),
            new LogLine(at.AddMinutes(5), LogLevel.Warning, "Upload: attempt 2 failed", "attempt_failed", new[] { "Upload", "2", "videos_unreachable", "40", "The videos API could not be reached" }),
        });
        Steps(items[4], StepStatus.Failed, StepStatus.Pending, StepStatus.Pending);
        items[4].Steps[0].Attempts = 1;
        items[4].Steps[0].LastError = "The file changed after it was scanned. Scan the folder again";
        items[4].Steps[0].LastErrorCode = "file_changed";
        Steps(items[5], StepStatus.Done, StepStatus.Pending, StepStatus.Pending);
        items[5].Paused = true;
    }
}

/// <summary>admin-api for screenshots: the channel list and new channels and batches, nothing sent anywhere.</summary>
internal sealed class DemoImportServer : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        string body = path switch
        {
            "/api/content/channels" => "{\"success\":true,\"result\":{\"items\":[" + string.Join(",", DemoImport.Channels.Select(c =>
                $"{{\"id\":\"{c.Id}\",\"name\":\"{c.Name}\",\"handle\":\"{c.Handle}\",\"ownerEmail\":\"{c.OwnerEmail}\"}}")) + "],\"total\":3,\"page\":1,\"pageSize\":20,\"indexComplete\":true}}",
            "/api/admin/import/channels" => "{\"success\":true,\"result\":{\"channel\":{\"id\":\"ch_demo_new\",\"name\":\"New channel\",\"handle\":\"newchannel\"}}}",
            "/api/admin/import/batches" => "{\"success\":true,\"result\":{\"batch\":{\"id\":\"batch_demo_2\",\"channelId\":\"" + DemoImport.ChannelId + "\",\"name\":null}}}",
            _ => "{\"success\":true,\"result\":{}}",
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

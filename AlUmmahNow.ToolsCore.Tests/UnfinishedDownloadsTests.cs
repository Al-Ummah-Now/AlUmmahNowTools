using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>An unfinished download is no video (production, 2026-11-25: a *.temp.webm was uploaded).</summary>
public class UnfinishedDownloadsTests
{
    [Theory]
    [InlineData("Lesson 1.temp.webm", true)]
    [InlineData("Lesson.1.TEMP.mp4", true)]
    [InlineData("lesson.mp4.part", true)]
    [InlineData("lesson.mp4.ytdl", true)]
    [InlineData("lesson.mp4.crdownload", true)]
    [InlineData("Unconfirmed 12345.crdownload", true)]
    [InlineData("lesson.mp4.download", true)]
    [InlineData("lesson.mkv.partial", true)]
    // Ordinary names
    [InlineData("temp.mp4", false)]
    [InlineData("temperature.mp4", false)]
    [InlineData("the temp file.mp4", false)]
    [InlineData("my.temple.mp4", false)]
    [InlineData("lesson.temporary.mp4", false)]
    [InlineData("lesson-temp.mp4", false)]
    [InlineData("part.mp4", false)]
    [InlineData("talk.part2.mov", false)]
    [InlineData("talk.part.mp4", false)]
    [InlineData("download.mp4", false)]
    [InlineData("lesson.download.mp4", false)]
    [InlineData("lesson.f16.mp4", false)]
    [InlineData(".part", false)]
    public void By_the_names_downloaders_give(string name, bool unfinished) =>
        Assert.Equal(unfinished, UnfinishedDownloads.Is(name, new[] { name, "other.mp4", "another.mp4.part" }));

    [Theory]
    [InlineData("Title.f137.mp4", "Title.mkv", true)]
    [InlineData("Title.f251-drc.webm", "title.mp4", true)]
    [InlineData("Title.f137.mp4", "Title.f140.m4a.part", true)]
    [InlineData("Title.f137.mp4", "Title.mp4.ytdl", true)]
    [InlineData("Title.f137.mp4", "Title.temp.mp4", true)]
    // Two finished formats kept on purpose; another title; a sidecar
    [InlineData("Title.f137.mp4", "Title.f136.mp4", false)]
    [InlineData("Title.f137.mp4", "Title 2.mp4", false)]
    [InlineData("Title.f137.mp4", "Title.ar.srt", false)]
    [InlineData("Title.fast.mp4", "Title.mp4", false)]
    [InlineData("Title.f.mp4", "Title.mp4", false)]
    public void One_format_of_a_video_counts_when_the_merged_file_or_a_download_is_beside_it(string name, string beside, bool unfinished) =>
        Assert.Equal(unfinished, UnfinishedDownloads.Is(name, new[] { name, beside }));

    [Fact]
    public void The_scan_leaves_them_out_and_lists_them()
    {
        var dir = Temp.Folder();
        Temp.Bytes(dir, "done.mp4", 10);
        Temp.Bytes(dir, "temperature.mp4", 10);
        Temp.Bytes(dir, "a.temp.webm", 11);
        Temp.Write(dir, "a.temp.ar.srt", "1\n");
        Temp.Bytes(dir, "b.mp4", 10);
        Temp.Bytes(dir, "b.f137.mp4", 12);
        Temp.Bytes(dir, "c.mkv.part", 13);
        Temp.Bytes(dir, "c.mkv.ytdl", 3);
        Temp.Bytes(dir, "notes.txt.part", 3);
        Temp.Write(dir, "done.ar.srt", "1\n");
        // Safari's
        Directory.CreateDirectory(Path.Combine(dir, "d.mp4.download"));
        Temp.Bytes(Path.Combine(dir, "d.mp4.download"), "d.mp4", 14);

        var result = FolderScanner.Scan(dir, includeSubfolders: true, batchLanguage: null);
        Assert.Equal(new[] { "b.mp4", "done.mp4", "temperature.mp4" }, result.Videos.Select(v => v.FileName));
        Assert.Equal(new[] { "a.temp.webm", "b.f137.mp4", "c.mkv.part", "d.mp4" }, result.Unfinished.Select(v => v.FileName).Order(StringComparer.Ordinal));
        Assert.All(result.Unfinished, v => Assert.Equal(ScanNoteCode.UnfinishedDownload, Assert.Single(v.Notes).Code));
        Assert.Empty(result.Videos.SelectMany(v => v.Notes));
        Assert.Equal("video/x-matroska", result.Unfinished.Single(v => v.FileName == "c.mkv.part").MimeType);
        Assert.Single(result.Videos.Single(v => v.FileName == "done.mp4").Captions);
        // Added all the same it has what is its own
        Assert.Single(result.Unfinished.Single(v => v.FileName == "a.temp.webm").Captions);

        // The folder itself was chosen: its videos are what was asked for
        Assert.Equal("d.mp4", Assert.Single(FolderScanner.Scan(Path.Combine(dir, "d.mp4.download"), false, null).Videos).FileName);
    }
}

public class LocalPathsTests
{
    private const string Instead = "ffprobe could not read the file (probe_failed, 1)";

    private static string Clean(string text, params string[] files) => LocalPaths.Clean(text, Instead, files);

    [Theory]
    [InlineData(@"\\nas\media\Lessons 2026\a.mp4")]
    [InlineData(@"C:\Users\Someone Else\Lessons 2026\a.mp4")]
    [InlineData("C:/Users/Someone Else/Lessons 2026/a.mp4")]
    [InlineData("/Volumes/Media/Lessons 2026/a.mp4")]
    [InlineData(@"C:\Users\someone\video.mp4 (backup)\a.mp4")]
    [InlineData(@"C:\Users\someone\Series.part 1\day 1\a.mp4")]
    [InlineData("/Users/someone/archive.zip (2026)/lessons/a.mp4")]
    [InlineData(@"C:\Users\someone/دروس الفقه/الصيام ١\a.mp4")]
    [InlineData("/Users/someone/Qur'an lessons/a.mp4")]
    [InlineData(@"\\nas\media\a.mp4")]
    public void A_known_file_is_its_name_however_it_is_written(string path)
    {
        Assert.Equal("ffprobe could not read the file: a.mp4: Invalid data", Clean($"ffprobe could not read the file: {path}: Invalid data", path));
        foreach (var after in new[] { "", " (100 kb/s)", " (video/mp4)", " and/or b.mp4", " [in#0/mov] error", "'", "\".", ", 3/4" })
            Assert.Equal("read a.mp4" + after, Clean("read " + path + after, path));
        foreach (var written in new[] { path.ToUpperInvariant(), path.ToLowerInvariant(), path.Replace('\\', '/'), path.Replace('/', '\\'), path.Replace("\\", "\\\\"), path.Replace(" ", "%20"), path.Replace('\'', '’'), path.Normalize(System.Text.NormalizationForm.FormD) })
            Assert.Equal("read 'a.mp4' (1/2)", Clean($"read '{written}' (1/2)", path));
        // Its folders, where nothing can come after them
        for (var folder = path; folder.LastIndexOfAny(['/', '\\']) is > 2 and var at;)
        {
            folder = folder[..at];
            if (folder.TrimStart('\\').IndexOfAny(['/', '\\']) < 0) break;
            Assert.Equal("no such folder …: gone", Clean($"no such folder {folder}: gone", path));
            Assert.Equal("no such folder …", Clean($"no such folder {folder}\\", path));
            // Where something does, nothing is said
            Assert.Equal(Instead, Clean($"no such folder {folder} (old)/b.mp4", path));
            Assert.Equal(Instead, Clean($"no such folder {folder}/sub folder/b.mp4", path));
        }
    }

    [Fact]
    public void The_long_paths_of_windows_are_known_too()
    {
        Assert.Equal("a.mp4: gone", Clean(@"\\?\UNC\nas\media\a.mp4: gone", @"\\nas\media\a.mp4"));
        Assert.Equal("a.mp4: gone", Clean(@"\\?\C:\x y\a.mp4: gone", @"C:\x y\a.mp4"));
        Assert.Equal("a.mp4 to b.mp4", Clean(@"Y:\x\a.mp4 to \\nas\media\b.mp4", @"Y:\x\a.mp4", @"\\nas\media\b.mp4"));
    }

    [Theory]
    [InlineData(@"\\nas\media\a.mp4 (100 kb/s)")]
    [InlineData(@"C:\Users\someone\a.mp4 (video/mp4)")]
    [InlineData("C:/Users/Mahmoud Darwish/Lessons 2026/a.mp4")]
    [InlineData("C:/Users/someone/دروس الفقه/الدرس الأول.mp4")]
    [InlineData("\"C:/Program Files/Al Ummah Now/a.mp4\"")]
    [InlineData(@"C:\Users\someone/My Lessons/a.mp4")]
    [InlineData(@"C:\Users\someone\video.mp4 (backup)\a.mp4")]
    [InlineData(@"C:\Users\someone\Series.part 1\day 1\a.mp4")]
    [InlineData("\"/Users/someone/archive.zip (2026)/lessons/a.mp4\"")]
    [InlineData("/Users/someone/a.mp4 and/or b.mp4")]
    [InlineData("ffprobe could not read /Volumes/Media/a.mp4 [in#0/mov] error")]
    [InlineData("could not read:/mnt/nas/a.mp4")]
    [InlineData("path=/opt/x")]
    [InlineData("file:///Users/someone/a.mp4")]
    [InlineData("file:%2F%2FUsers%2Fsomeone")]
    [InlineData("~/Movies/a.mp4")]
    [InlineData("~someone/Movies/a.mp4")]
    [InlineData("//nas/media/a.mp4")]
    [InlineData("in ../Lessons/a.mp4")]
    [InlineData("(/data/x)")]
    // A share's path read in another code page is nobody's known path
    [InlineData("\\\\nas\\Ø¯Ø±Ø³\\a.mp4: Invalid data")]
    public void Anything_else_that_looks_like_a_path_takes_its_line_with_it(string text)
    {
        Assert.Equal(Instead, Clean(text, "/Volumes/Media/Lessons/other.mp4"));
        Assert.Equal("before\n" + Instead + "\nafter", Clean($"before\n{text}\r\nafter"));
    }

    [Theory]
    [InlineData("[in#0/mov @ 0x1] [error] Error during demuxing: Invalid argument")]
    [InlineData("POST https://admin.api.example/api/admin/import/items (500)")]
    [InlineData("kb/s and/or 10 s, 3 / 4, a: b, ~10/20")]
    [InlineData("الملف و/أو الصوت: تعذّرت القراءة")]
    [InlineData("no path at all")]
    [InlineData("")]
    public void What_names_no_folder_stays(string text) => Assert.Equal(text, Clean(text, "/Volumes/Media/Lessons/other.mp4"));

    [Fact]
    public void The_apps_own_folders_are_known()
    {
        Assert.Equal("full: …", Clean("full: " + EncodeStep.DefaultWorkRoot));
        Assert.Equal("full: …", Clean("full: " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        if (Path.IsPathRooted(EncodeStep.DefaultWorkRoot)) Assert.Equal(Instead, Clean("full: " + Path.Combine(EncodeStep.DefaultWorkRoot, "x", "360p.mp4")));
        // Not a drive: a word before a colon stays
        Assert.Equal("the doc: gone, etc:", Clean("the doc: gone, etc:", @"C:\a b\c.mp4"));
    }

    [Fact]
    public void Nothing_and_slashes_alone_are_no_trouble()
    {
        Assert.Equal("", LocalPaths.Clean(null, Instead));
        Assert.Equal("gone", LocalPaths.Clean("gone", Instead, ["/", "\\\\", ""]));
        var slashes = new string('/', 50_000) + "x";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(Instead, LocalPaths.Clean(slashes, Instead, ["/Volumes/Media/a.mp4", @"\\nas\media\a.mp4"]));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), clock.Elapsed.ToString());
    }

    [Fact]
    public void Text_that_is_no_text_is_not_sent() => Assert.Equal(Instead, Clean("broken " + (char)0xD800 + " text"));

    [Fact]
    public void Without_free_text_the_code_the_exit_code_and_a_fixed_sentence_are_left()
    {
        Assert.Equal("ffprobe could not read the file (probe_failed, 134)", ErrorReporter.Plain(new LocalEncodeException("probe_failed", "x") { ExitCode = 134 }));
        Assert.Equal("The file could not be encoded here (disk_space)", ErrorReporter.Plain(new LocalEncodeException("disk_space", "x")));
        Assert.Equal("It failed (file_missing)", ErrorReporter.Plain(new FileNotFoundException("x")));
        Assert.Equal("It failed (unexpected)", ErrorReporter.Plain(new InvalidOperationException("x")));
    }

    private static async Task<ErrorReport> Sent(Exception error, string message, IReadOnlyDictionary<string, object?>? extra = null, params string[] files)
    {
        var sent = new List<ErrorReport>();
        using var reporter = new ErrorReporter((batch, _) =>
        {
            sent.AddRange(batch);
            return Task.CompletedTask;
        }, "1", () => "en", TimeSpan.FromHours(1));
        reporter.Report("unhandled", error, message, extra: extra, files: files);
        await reporter.FlushAsync();
        return Assert.Single(sent);
    }

    [Fact]
    public async Task A_report_carries_file_names_alone()
    {
        var extra = new Dictionary<string, object?> { ["file"] = @"\\nas\media\Lessons\a.mp4", ["other"] = @"\\nas\x\b.mp4", ["fileSize"] = 5L };
        var known = await Sent(new IOException("x"), @"Import Encode failed: \\nas\media\Lessons\a.mp4: Invalid data", extra, @"\\nas\media\Lessons\a.mp4");
        Assert.Equal("Import Encode failed: a.mp4: Invalid data", known.Message);
        Assert.Equal("a.mp4", known.Extra!["file"]);
        Assert.Equal("…", known.Extra["other"]);
        Assert.Equal(5L, known.Extra["fileSize"]);

        Exception thrown;
        try { throw new LocalEncodeException("ffmpeg_failed", "x") { ExitCode = 1 }; } catch (LocalEncodeException ex) { thrown = ex; }
        var unknown = await Sent(thrown, @"Import Encode failed: \\nas\media\Lessons\a.mp4: Invalid data");
        Assert.Equal("ffmpeg stopped (ffmpeg_failed, 1)", unknown.Message);
        Assert.Contains(nameof(A_report_carries_file_names_alone), unknown.Stack, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs", unknown.Stack, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nor_does_the_server_get_one_with_a_failed_file()
    {
        async Task<string?> Failed(string message)
        {
            var server = new FakeServer();
            var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("t"), server);
            using var reporter = new ErrorReporter(api.ReportErrorsAsync, "1", () => "en", TimeSpan.FromHours(1));
            var item = QueueItem.For(new ScannedVideo("/Volumes/Media/My lessons/a.mp4", 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = "a" }, Array.Empty<ScanNote>()), "b1", "ch1", DateTimeOffset.UtcNow);
            item.ServerItemId = "item-9";
            await new ServerFailureSink(api, reporter).ItemFailedAsync(item, item.Steps.Single(s => s.Kind == ImportStepKind.Upload), new InvalidOperationException(message), "ERR-AAAA-BBBB", default);
            Assert.All(server.Calls, c => Assert.DoesNotContain("lessons", c.Body ?? "", StringComparison.Ordinal));
            return server.Calls.Single(c => c.Path.EndsWith("/items/item-9/failed", StringComparison.Ordinal)).Body;
        }
        Assert.Contains("a.mp4 went", await Failed("/Volumes/Media/My lessons/a.mp4 went"), StringComparison.Ordinal);
        Assert.Contains("It failed (unexpected)", await Failed("/Volumes/Media/My lessons/b.mp4 went"), StringComparison.Ordinal);
    }
}

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
    [Theory]
    [InlineData(@"ffprobe could not read the file: \\nas\media\Lessons 2026\a b.temp.webm: Invalid data", "ffprobe could not read the file: a b.temp.webm: Invalid data")]
    [InlineData(@"\\?\UNC\nas\media\x.mp4", "x.mp4")]
    [InlineData(@"Could not find '\\NAS\Media\الدروس\درس.mp4'.", "Could not find 'درس.mp4'.")]
    [InlineData(@"Access to the path 'Y:\Lessons\a.mp4' is denied.", "Access to the path 'a.mp4' is denied.")]
    [InlineData("C:/Users/someone/a.mp4: gone", "a.mp4: gone")]
    [InlineData("/Volumes/Media/Lessons/a b.mp4: Input/output error", "a b.mp4: Input/output error")]
    [InlineData("in /Users/someone/Library/x.json", "in x.json")]
    [InlineData("[in#0/mov @ 0x1] [error] Error during demuxing: Invalid argument", "[in#0/mov @ 0x1] [error] Error during demuxing: Invalid argument")]
    [InlineData("POST https://admin.api.example/api/admin/import/items (500)", "POST https://admin.api.example/api/admin/import/items (500)")]
    [InlineData("kb/s and/or 10 s", "kb/s and/or 10 s")]
    [InlineData("no path at all", "no path at all")]
    // What stands after the path stays
    [InlineData(@"\\nas\media\a.mp4 (100 kb/s)", "a.mp4 (100 kb/s)")]
    [InlineData(@"C:\Users\someone\a.mp4 (video/mp4)", "a.mp4 (video/mp4)")]
    [InlineData("/Users/someone/a.mp4 and/or b.mp4", "a.mp4 and/or b.mp4")]
    [InlineData("ffprobe could not read /Volumes/Media/a.mp4 [in#0/mov] error", "ffprobe could not read a.mp4 [in#0/mov] error")]
    [InlineData(@"\\nas\media\lesson (video/mp4)", "lesson (video/mp4)")]
    [InlineData(@"copied \\nas\media\a.mp4 to \\nas\other\b.mp4, 3/4", "copied a.mp4 to b.mp4, 3/4")]
    [InlineData(@"moved /Volumes/Media/a.mp4 to /Users/someone/b.mp4 (1/2)", "moved a.mp4 to b.mp4 (1/2)")]
    // Names with spaces, dots, quotes; in Arabic
    [InlineData(@"\\nas\دروس الفقه\الصيام ١\الدرس الأول.mp4 (100 kb/s)", "الدرس الأول.mp4 (100 kb/s)")]
    [InlineData("/Volumes/الوسائط/دروس/الدرس الأول.webm and/or x", "الدرس الأول.webm and/or x")]
    [InlineData(@"C:\My Lessons v1.2 final\part 2.old\a b.c.mp4: gone", "a b.c.mp4: gone")]
    [InlineData(@"\\nas\media\a.mp4.part 5/6", "a.mp4.part 5/6")]
    [InlineData("'/Users/someone/Qur'an lessons/no extension' and 'c/d'", "'no extension' and 'c/d'")]
    [InlineData(@"""C:\a b\c d"" x/y", @"""c d"" x/y")]
    [InlineData(@"C:\Users\someone/mixed/a.mp4", "a.mp4")]
    [InlineData("/Volumes/Media/d.mp4.download/d.mp4: cut", "d.mp4: cut")]
    [InlineData(@"""\\\\nas\\media\\a.mp4""", @"""a.mp4""")]
    // The names of macOS's file system have no case
    [InlineData("/users/someone/a.mp4 in /VOLUMES/Media/x y/b.mp4", "a.mp4 in b.mp4")]
    [InlineData("", "")]
    public void Folders_stay_on_this_computer(string text, string stripped) => Assert.Equal(stripped, LocalPaths.Strip(text));

    [Fact]
    public async Task A_report_carries_file_names_alone()
    {
        var sent = new List<ErrorReport>();
        using var reporter = new ErrorReporter((batch, _) =>
        {
            sent.AddRange(batch);
            return Task.CompletedTask;
        }, "1", () => "en", TimeSpan.FromHours(1));
        reporter.Report("caught", new IOException("x"), @"Import Encode failed: \\nas\media\Lessons\a.mp4: Invalid data", extra: new Dictionary<string, object?> { ["file"] = @"\\nas\media\Lessons\a.mp4", ["fileSize"] = 5L });
        await reporter.FlushAsync();
        var report = Assert.Single(sent);
        Assert.Equal("Import Encode failed: a.mp4: Invalid data", report.Message);
        Assert.Equal("a.mp4", report.Extra!["file"]);
        Assert.Equal(5L, report.Extra["fileSize"]);
    }

    [Fact]
    public async Task Nor_does_the_server_get_one_with_a_failed_file()
    {
        var server = new FakeServer();
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("t"), server);
        using var reporter = new ErrorReporter(api.ReportErrorsAsync, "1", () => "en", TimeSpan.FromHours(1));
        var item = QueueItem.For(new ScannedVideo("/x/a.mp4", 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = "a" }, Array.Empty<ScanNote>()), "b1", "ch1", DateTimeOffset.UtcNow);
        item.ServerItemId = "item-9";
        await new ServerFailureSink(api, reporter).ItemFailedAsync(item, item.Steps.Single(s => s.Kind == ImportStepKind.Upload), new InvalidOperationException(@"\\nas\media\a.mp4 went"), "ERR-AAAA-BBBB", default);
        Assert.All(server.Calls, c => Assert.DoesNotContain("nas", c.Body ?? "", StringComparison.Ordinal));
        Assert.Contains("a.mp4 went", server.Calls.Single(c => c.Path.EndsWith("/items/item-9/failed", StringComparison.Ordinal)).Body, StringComparison.Ordinal);
    }
}

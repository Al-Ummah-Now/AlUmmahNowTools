using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Social;
using AlUmmahNow.ToolsCore.Updates;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

public sealed class InjectionAuditTests
{
    [Theory]
    [InlineData("https://auth.qa.alummahnow.net/authorize", true)]
    [InlineData("http://127.0.0.1:8080/auth/callback", true)]
    [InlineData("http://localhost:8080/auth/callback", true)]
    [InlineData("http://evil.example/phish", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-msdt:/id", false)]
    [InlineData("https://user:pass@auth.qa.alummahnow.net/", false)]
    public void ShellBrowserLauncher_RejectsUnsafeSchemesAndUserinfo(string url, bool expected)
    {
        var uri = new Uri(url);
        Assert.Equal(expected, ShellBrowserLauncher.IsAllowedUri(uri));
        if (!expected)
        {
            var launcher = new ShellBrowserLauncher();
            Assert.Throws<ArgumentException>(() => launcher.Open(uri));
        }
    }

    [Fact]
    public async Task LoopbackRedirect_RejectsPostAndControlChars()
    {
        using var loopback = new LoopbackRedirect("Done", "تم");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waitTask = loopback.WaitAsync(cts.Token);

        // POST to /auth/callback must receive 404 and not complete WaitAsync
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, loopback.Port, cts.Token);
            using var stream = client.GetStream();
            var post = Encoding.ASCII.GetBytes("POST /auth/callback?code=evil&state=s HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
            await stream.WriteAsync(post, cts.Token);
            var buf = new byte[256];
            var n = await stream.ReadAsync(buf, cts.Token);
            Assert.Contains("404 Not Found", Encoding.ASCII.GetString(buf, 0, n));
        }

        // Valid GET completes WaitAsync
        using (var http = new HttpClient())
        {
            var resp = await http.GetAsync(loopback.RedirectUri + "?code=good&state=s", cts.Token);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        var result = await waitTask;
        Assert.Equal(loopback.RedirectUri + "?code=good&state=s", result);
    }

    [Fact]
    public async Task ImportApiClient_BlocksTraversalOriginEscapeAndInvalidRenditionPath()
    {
        var handler = new FakeHandler();
        var client = new ImportApiClient(new Uri("https://admin.api.qa.alummahnow.net"), _ => Task.FromResult<string?>("tok"), handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.MarkUploadedAsync(".."));
        await Assert.ThrowsAsync<ArgumentException>(() => client.MarkUploadedAsync("../admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetJobAsync("a/b"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RemoveSourceAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => client.PutRenditionFileAsync("i1", "s1", "no-slash", Stream.Null, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => client.PutRenditionFileAsync("i1", "s1", "../evil.mp4", Stream.Null, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => client.PutRenditionFileAsync("i1", "s1", "720p/a/b.m4s", Stream.Null, 0));

        // Ensure no HTTP request was ever sent for any rejected input
        Assert.Empty(handler.Requests);

        // Unsafe X-App-Locale with CRLF is ignored instead of injected
        handler.Answer = _ => FakeHandler.Json(HttpStatusCode.OK, "{\"result\":{\"items\":[]}}");
        client.AppLocale = () => "en\r\nX-Injected: yes";
        _ = await client.ListChannelsAsync(null);
        Assert.Single(handler.Requests);
        Assert.False(handler.Requests[0].Headers.Contains("X-App-Locale"));
    }

    [Fact]
    public async Task TusClient_BlocksCrossOriginLocationTraversalUploadIdAndBadMetadataKeys()
    {
        var handler = new FakeHandler
        {
            Answer = _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Created);
                r.Headers.Location = new Uri("https://evil.example/steal");
                return r;
            },
        };
        var tus = new TusClient(handler);
        var endpoint = new Uri("https://videos.api.qa.alummahnow.net/api/videos/tus");

        // Cross-origin Location refused
        await Assert.ThrowsAsync<TusException>(() => tus.CreateAsync(endpoint, "tok", 10, new Dictionary<string, string> { ["title"] = "x" }, CancellationToken.None));

        // Invalid TUS metadata key refused
        Assert.Throws<ArgumentException>(() => TusClient.EncodeMetadata(new Dictionary<string, string> { ["bad,key"] = "v" }));
        Assert.Throws<ArgumentException>(() => TusClient.EncodeMetadata(new Dictionary<string, string> { ["bad key"] = "v" }));

        // Insecure HTTP non-loopback endpoint/upload refused
        await Assert.ThrowsAsync<TusException>(() => tus.OffsetAsync(new Uri("http://evil.example/api/videos/tus/1"), "tok", CancellationToken.None));

        // Raw TusUploadId traversal / authority escape refused
        Assert.Throws<ArgumentException>(() => TusClient.ResolveUploadUrl(endpoint, "../../admin/secret"));
        Assert.Throws<ArgumentException>(() => TusClient.ResolveUploadUrl(endpoint, ".."));
        Assert.Throws<ArgumentException>(() => TusClient.ResolveUploadUrl(endpoint, "//evil.example/steal"));
        Assert.Equal("https://videos.api.qa.alummahnow.net/api/videos/tus/abc123", TusClient.ResolveUploadUrl(endpoint, "abc123").ToString());
    }

    [Fact]
    public void TakeoutExport_SkipsSymlinkedCsvFiles()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("tools-takeout-inj-");
        var outside = Directory.CreateTempSubdirectory("tools-outside-inj-");
        try
        {
            var videoPath = Path.Combine(root.FullName, "Lecture.mp4");
            File.WriteAllBytes(videoPath, new byte[] { 1, 2, 3, 4 });

            var secretCsv = Path.Combine(outside.FullName, "videos.csv");
            File.WriteAllText(secretCsv, "Video ID,Video Title,Approx Duration (ms)\nsecret_vid_id,Lecture,1000\n", Encoding.UTF8);

            var symlinkCsv = Path.Combine(root.FullName, "videos.csv");
            File.CreateSymbolicLink(symlinkCsv, secretCsv);

            var items = TakeoutExport.Read(root.FullName);
            var item = Assert.Single(items);
            Assert.Null(item.VideoId);
        }
        finally
        {
            root.Delete(recursive: true);
            outside.Delete(recursive: true);
        }
    }

    [Fact]
    public void StagedPackage_RejectsTraversalFileNames()
    {
        var dir = Directory.CreateTempSubdirectory("tools-staged-inj-");
        try
        {
            var bytes = Encoding.UTF8.GetBytes("pkg");
            var artifact = new CoreArtifact("tools", "osx", "arm64", "velopack-full", "https://example.com/p", Convert.ToHexStringLower(SHA256.HashData(bytes)), (ulong)bytes.Length);
            Assert.Throws<ArgumentException>(() => StagedPackage.Prepare(dir.FullName, "../escape.nupkg", artifact, dir.FullName));
            Assert.Throws<ArgumentException>(() => StagedPackage.Prepare(dir.FullName, "sub/file.nupkg", artifact, dir.FullName));
            Assert.Throws<ArgumentException>(() => StagedPackage.Prepare(dir.FullName, "..", artifact, dir.FullName));
            Assert.Throws<ArgumentException>(() => StagedPackage.Prepare(dir.FullName, "", artifact, dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void DiagnosticLog_NeutralizesControlCharsInCodeAndText()
    {
        var at = new DateTimeOffset(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);
        var video = new ScannedVideo(
            Path: "/tmp/clip.mp4", Size: 100, LastModifiedMs: 0, MimeType: "video/mp4",
            Captions: [], ChaptersPath: null, ThumbnailPath: null, MetadataPath: null,
            Metadata: new ImportMetadata { FallbackTitle = "clip" }, Notes: []);
        var item = QueueItem.For(video, "b1", "c1", at);
        item.Log.Add(new LogLine(at, LogLevel.Warning, "bad\r\nFAKE_LINE\u2028\u0000end", "code\nFORGED"));

        var upload = DiagnosticLog.Build(item, "1.0.0\r\ninjected=true");
        Assert.DoesNotContain('\r', upload.Content);
        Assert.DoesNotContain('\0', upload.Content);
        Assert.DoesNotContain('\u2028', upload.Content);
        Assert.DoesNotContain("\nFAKE_LINE", upload.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("\nFORGED", upload.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("\ninjected=true", upload.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=CMD|' /C calc'!A0", "'=CMD|' /C calc'!A0")]
    [InlineData(" =CMD|' /C calc'!A0", "' =CMD|' /C calc'!A0")]
    [InlineData("\n=1+1", "\"'\n=1+1\"")]
    [InlineData("+SUM(A1:A2)", "'+SUM(A1:A2)")]
    [InlineData("@HYPERLINK(\"x\")", "\"'@HYPERLINK(\"\"x\"\")\"")]
    [InlineData("-evil", "'-evil")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("Channel, One", "\"Channel, One\"")]
    public void CsvField_NeutralizesFormulasAndPreservesNumbers(string input, string expected)
    {
        Assert.Equal(expected, CsvField.Escape(input));
    }
}

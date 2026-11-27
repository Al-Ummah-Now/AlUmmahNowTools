using System.Diagnostics;
using System.Security.Cryptography;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Import.Sources;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>Why a read failed, from the system's own error (issue 79).</summary>
public class NetworkShareReadFailureTests
{
    private static IOException Win32(int code) => new("x : 'Y:\\secret\\a.mp4'", unchecked((int)0x80070000) | code);

    [Theory]
    [InlineData(59, "share_lost")]
    [InlineData(64, "share_lost")]
    [InlineData(121, "share_lost")]
    [InlineData(1312, "share_lost")]
    [InlineData(53, "share_not_found")]
    [InlineData(67, "share_not_found")]
    [InlineData(1231, "share_not_found")]
    [InlineData(1232, "share_not_found")]
    [InlineData(1219, "share_credentials")]
    [InlineData(1326, "share_credentials")]
    [InlineData(1385, "share_credentials")]
    [InlineData(1117, "share_lost")]
    [InlineData(5, "file_denied")]
    [InlineData(32, "file_in_use")]
    [InlineData(33, "file_in_use")]
    [InlineData(206, "name_too_long")]
    [InlineData(362, "cloud_offline")]
    [InlineData(388, "cloud_offline")]
    [InlineData(404, "cloud_offline")]
    [InlineData(9999, "file_read")]
    public void Windows_errors_on_a_share(int code, string expected)
    {
        var failure = ReadFailure.Of(Win32(code), network: true, ReadSystem.Windows);
        Assert.Equal(expected, failure.Code);
        Assert.Equal(code, failure.Number);
        Assert.Equal(expected is "share_lost" or "share_not_found", failure.Kind == ReadKind.Network);
        Assert.StartsWith($"Windows error {code}", failure.Detail);
        // No path in what may be reported
        Assert.DoesNotContain("secret", failure.Message);
    }

    [Theory]
    [InlineData(1117, "disk_read")]
    [InlineData(23, "disk_read")]
    [InlineData(21, "drive_gone")]
    [InlineData(1167, "drive_gone")]
    [InlineData(59, "share_lost")]
    public void Windows_errors_on_a_local_disk(int code, string expected) =>
        Assert.Equal(expected, ReadFailure.Of(Win32(code), network: false, ReadSystem.Windows).Code);

    [Theory]
    [InlineData(true, 60, true, "share_lost")]
    [InlineData(true, 70, true, "share_lost")]
    [InlineData(true, 64, true, "share_lost")]
    [InlineData(true, 57, true, "share_lost")]
    [InlineData(true, 5, true, "share_lost")]
    [InlineData(true, 5, false, "disk_read")]
    [InlineData(true, 19, false, "drive_gone")]
    [InlineData(true, 63, false, "name_too_long")]
    [InlineData(true, 11, false, "cloud_offline")]
    [InlineData(true, 35, false, "file_in_use")]
    [InlineData(true, 80, true, "share_credentials")]
    [InlineData(false, 110, true, "share_lost")]
    [InlineData(false, 116, true, "share_lost")]
    [InlineData(false, 112, true, "share_lost")]
    [InlineData(false, 107, true, "share_lost")]
    [InlineData(false, 5, true, "share_lost")]
    [InlineData(false, 5, false, "disk_read")]
    [InlineData(false, 36, false, "name_too_long")]
    [InlineData(false, 11, false, "file_in_use")]
    public void Unix_errors(bool mac, int errno, bool network, string expected) =>
        Assert.Equal(expected, ReadFailure.Of(new IOException("x", errno), network, mac ? ReadSystem.MacOS : ReadSystem.Linux).Code);

    [Fact]
    public void Errors_without_a_number_and_what_the_queue_does_with_each()
    {
        Assert.Equal("file_read", ReadFailure.Of(new IOException("x")).Code);
        Assert.Equal("name_too_long", ReadFailure.Of(new PathTooLongException("x")).Code);
        Assert.Equal("file_denied", ReadFailure.Of(new UnauthorizedAccessException("x")).Code);
        Assert.Equal("file_missing", ReadFailure.Of(new FileNotFoundException("x")).Code);
        Assert.Equal("share_lost", ReadFailure.Of(new TimeoutException("x")).Code);

        ReadFailure F(string code) => new(code, ReadKind.Final, ReadSystem.Windows, 59);
        foreach (var code in new[] { "share_lost", "share_not_found", "file_in_use", "disk_read", "drive_gone", "cloud_offline", "file_read" })
            Assert.True(ImportQueue.IsRetryable(new SourceReadException(F(code))), code);
        foreach (var code in new[] { "share_credentials", "share_damaged", "file_denied", "name_too_long", "file_missing" })
            Assert.False(ImportQueue.IsRetryable(new SourceReadException(F(code))), code);
        Assert.Equal("share_lost", ImportQueue.ErrorCode(new SourceReadException(F("share_lost"))));
        Assert.False(ImportQueue.IsRetryable(new PathTooLongException("x")));
        Assert.DoesNotContain("secret", ImportQueue.Describe(new IOException("x : '/secret/a.mp4'")));
    }
}

/// <summary>The network path behind a drive letter.</summary>
public class NetworkSharePathTests
{
    private sealed class Drives : IWindowsDrives
    {
        public readonly Dictionary<string, string> Dos = new();
        public readonly Dictionary<string, int> Types = new();
        public readonly Dictionary<string, string> Shares = new();
        public readonly Dictionary<string, string> Saved = new();
        public bool UniversalWorks = true;

        public string? DosDevice(string drive) => Dos.GetValueOrDefault(drive);
        public int DriveType(string drive) => Types.GetValueOrDefault(drive.ToUpperInvariant(), 1);
        public string? UniversalName(string path) => UniversalWorks && Shares.TryGetValue(path[..2], out var s) ? s + path[2..] : null;
        public string? Connection(string drive) => Shares.GetValueOrDefault(drive);
        public string? Remembered(string drive) => Saved.GetValueOrDefault(drive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_mapped_drive_has_its_network_path(bool universal)
    {
        var drives = new Drives { UniversalWorks = universal };
        drives.Types["Y:"] = 4;
        drives.Shares["Y:"] = @"\\nas\videos";
        var paths = new WindowsNetworkPaths(drives);
        Assert.Equal(@"\\nas\videos\طه\54.webm", paths.Mapped(@"Y:\طه\54.webm"));
        Assert.Equal(@"\\nas\videos\a.mp4", paths.Mapped(@"y:/a.mp4"));
        Assert.Equal(@"\\nas\videos\", paths.Mapped(@"Y:\"));
        Assert.True(paths.IsNetwork(@"Y:\a.mp4"));
    }

    [Fact]
    public void An_nfs_drive_too() // the Windows NFS client answers \\server\export
    {
        var drives = new Drives();
        drives.Types["N:"] = 4;
        drives.Shares["N:"] = @"\\nas\export\media";
        Assert.Equal(@"\\nas\export\media\a.mp4", new WindowsNetworkPaths(drives).Mapped(@"N:\a.mp4"));
    }

    [Fact]
    public void A_drive_mapped_only_in_the_other_session_is_found_by_what_windows_remembers()
    {
        var drives = new Drives();
        drives.Saved["Y:"] = @"\\nas\videos";
        var paths = new WindowsNetworkPaths(drives);
        Assert.Equal(@"\\nas\videos\a.mp4", paths.Mapped(@"Y:\a.mp4"));
        Assert.True(paths.IsNetwork(@"Y:\a.mp4"));
    }

    [Fact]
    public void Subst_leads_to_the_share_behind_it_or_nowhere()
    {
        var drives = new Drives();
        drives.Dos["S:"] = @"\??\Y:\lessons";
        drives.Dos["Y:"] = @"\Device\LanmanRedirector\;Y:0000\nas\videos";
        drives.Types["Y:"] = 4;
        drives.Shares["Y:"] = @"\\nas\videos";
        drives.Dos["U:"] = @"\??\UNC\nas\videos\lessons";
        drives.Dos["L:"] = @"\??\C:\lessons";
        drives.Dos["C:"] = @"\Device\HarddiskVolume3";
        drives.Types["C:"] = drives.Types["L:"] = 3;
        // a loop ends
        drives.Dos["A:"] = @"\??\B:\";
        drives.Dos["B:"] = @"\??\A:\";
        var paths = new WindowsNetworkPaths(drives);
        Assert.Equal(@"\\nas\videos\lessons\a.mp4", paths.Mapped(@"S:\a.mp4"));
        Assert.Equal(@"\\nas\videos\lessons\a.mp4", paths.Mapped(@"U:\a.mp4"));
        Assert.Null(paths.Mapped(@"L:\a.mp4"));
        Assert.Null(paths.Mapped(@"C:\a.mp4"));
        Assert.Null(paths.Mapped(@"A:\a.mp4"));
        Assert.False(paths.IsNetwork(@"C:\a.mp4"));
    }

    /// <summary>The real calls, against a drive mapped to this computer's own share (the CI runner is an administrator).</summary>
    [Fact]
    public async Task Windows_itself_resolves_a_mapped_drive_and_a_subst_of_it()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return;
        static (int Code, string Output) Run(string exe, string args)
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }
        var folder = @"C:\aun-share-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(folder, "lessons"));
        var bytes = new byte[100_000];
        new Random(1).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(folder, "lessons", "a.mp4"), bytes);
        var share = @"\\localhost\C$\" + Path.GetFileName(folder);
        try
        {
            var mapped = Run("net", $"use V: {share} /persistent:no");
            Assert.True(mapped.Code == 0, mapped.Output);
            Assert.Equal(0, Run("subst", @"W: V:\lessons").Code);
            Assert.Equal(0, Run("subst", $@"X: {folder}\lessons").Code);
            var access = SourceAccess.Default;
            Assert.Equal(new[] { share + @"\lessons\a.mp4", @"V:\lessons\a.mp4" }, access.Candidates(@"V:\lessons\a.mp4"));
            Assert.Equal(new[] { share + @"\lessons\a.mp4", @"W:\a.mp4" }, access.Candidates(@"W:\a.mp4"));
            Assert.Equal(new[] { @"X:\a.mp4" }, access.Candidates(@"X:\a.mp4"));
            Assert.True(access.IsNetwork(@"V:\lessons\a.mp4"));
            Assert.False(access.IsNetwork(@"X:\a.mp4"));
            Assert.Equal(bytes, await access.ReadAllBytesAsync(@"V:\lessons\a.mp4", null, default));
            var scan = FolderScanner.Scan(@"W:\", false, null);
            var video = Assert.Single(scan.Videos);
            Assert.Equal(@"W:\a.mp4", video.Path);
            await access.CheckUnchangedAsync(video, null, default, read: true);
            var missing = await Assert.ThrowsAsync<SourceReadException>(() => access.StatAsync(@"V:\lessons\none.mp4", null, default));
            Assert.Equal("file_missing", missing.Failure.Code);
        }
        finally
        {
            Run("subst", "W: /d");
            Run("subst", "X: /d");
            Run("net", "use V: /delete /y");
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_network_path_is_read_as_it_is()
    {
        var paths = new WindowsNetworkPaths(new Drives());
        Assert.Null(paths.Mapped(@"\\nas\videos\a.mp4"));
        Assert.True(paths.IsNetwork(@"\\nas\videos\a.mp4"));
        Assert.True(paths.IsNetwork(@"//nas/videos/a.mp4"));
        Assert.True(paths.IsNetwork(@"\\?\UNC\nas\videos\a.mp4"));
        Assert.False(paths.IsNetwork(@"\\?\C:\a.mp4"));
    }

    [Fact]
    public void This_computers_own_disk_is_no_share()
    {
        var file = Temp.Bytes(Temp.Folder(), "a.mp4", 10);
        Assert.False(SourceAccess.Default.IsNetwork(file));
        Assert.Equal(new[] { file }, SourceAccess.Default.Candidates(file));
        var stat = new SourceFileSystem().Stat(file);
        Assert.Equal((10, false, false), (stat.Length, stat.IsLink, stat.InCloud));
        using var opened = new SourceFileSystem().Open(file).Stream;
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds(), stat.ModifiedMs);
        Assert.Throws<FileNotFoundException>(() => new SourceFileSystem().Stat(file + ".gone"));
    }
}

/// <summary>Reading a file whose share drops and comes back, on either of its two paths.</summary>
public class NetworkShareReadingTests
{
    private const string Chosen = "/y/a.mp4";
    private const string Network = "/nas/a.mp4";
    private const int Size = 3 * 1024 * 1024 + 17;

    private static int Number => OperatingSystem.IsWindows() ? 59 : OperatingSystem.IsMacOS() ? 60 : 110;
    private static IOException Dropped() => new("dropped : 'x'", OperatingSystem.IsWindows() ? unchecked((int)0x8007003B) : Number);

    private sealed class Paths : INetworkPaths
    {
        public bool Mapping = true;
        public bool OnShare = true;
        /// <summary>A mount whose server is gone: asked what it is, it answers only once this is set</summary>
        public ManualResetEventSlim? Silent;
        public int Questions;
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Asked = new();
        private void Hold()
        {
            if (Silent is not { } silent) return;
            Interlocked.Increment(ref Questions);
            silent.Wait(TimeSpan.FromSeconds(60));
        }

        public string? Mapped(string path)
        {
            Hold();
            return Resolve(path);
        }

        private string? Resolve(string path) => Mapping && path.StartsWith("/y", StringComparison.Ordinal) ? "/nas" + path[2..] : null;
        public bool IsNetwork(string path)
        {
            Asked.Enqueue(path);
            Hold();
            return OnShare;
        }
    }

    /// <summary>One file behind every path; a read, an open or a look-up fails when told to.</summary>
    private sealed class Faults : ISourceFileSystem
    {
        public byte[] Bytes = RandomNumberGenerator.GetBytes(Size);
        public long Modified = 1000;
        public bool Cloud;
        public bool Root = true;
        /// <summary>The opened file says it is this much longer than it is</summary>
        public int Longer;
        public bool CloseFails;
        public Func<string, Exception?> OnOpen = _ => null;
        public Func<string, Exception?> OnStat = _ => null;
        /// <summary>Path, offset and length: null reads</summary>
        public Func<string, long, int, Exception?> OnRead = (_, _, _) => null;
        public Func<string, long, Task>? Before;
        public readonly List<(string Path, long Offset, int Count)> Reads = new();
        public readonly List<string> Opens = new();

        public SourceStat Stat(string path) => OnStat(path) is { } e ? throw e : new SourceStat(Bytes.Length, Modified, InCloud: Cloud);

        public OpenedSource Open(string path)
        {
            lock (Opens) Opens.Add(path);
            if (OnOpen(path) is { } e) throw e;
            return new OpenedSource(new FaultyStream(this, path), new SourceStat(Bytes.Length + Longer, Modified, InCloud: Cloud));
        }

        public bool RootAnswers(string path) => Root;
        public bool FolderExists(string path) => true;
    }

    private sealed class FaultyStream(Faults faults, string path) : MemoryStream(faults.Bytes, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && faults.CloseFails) throw new InvalidOperationException("x");
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var offset = Position;
            if (faults.Before is { } before) await before(path, offset);
            if (faults.OnRead(path, offset, buffer.Length) is { } e) throw e;
            lock (faults.Reads) faults.Reads.Add((path, offset, buffer.Length));
            return await base.ReadAsync(buffer, ct);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Rig
    {
        public readonly Faults Files = new();
        public readonly Paths Paths = new();
        public readonly Clock Clock = new();
        public readonly RecordingContext Context = new();
        public readonly List<TimeSpan> Delays = new();
        public Action OnDelay = () => { };
        public readonly SourceAccess Access;

        public Rig(TimeSpan? stall = null, TimeSpan? answer = null)
        {
            Access = new SourceAccess(Paths, Files, Clock, (wait, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Delays.Add(wait);
                Clock.Now += wait;
                OnDelay();
                return Task.CompletedTask;
            }) { Stall = stall ?? TimeSpan.FromMinutes(2), Answer = answer ?? TimeSpan.FromSeconds(20) };
        }

        public ScannedVideo Video => new(Chosen, Files.Bytes.Length, Files.Modified, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = "a" }, Array.Empty<ScanNote>());

        public async Task<byte[]> ReadAll(CancellationToken ct = default)
        {
            await using var stream = Access.Open(Chosen, Files.Bytes.Length, Files.Modified, Context);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct);
            return copy.ToArray();
        }

        public IEnumerable<string> Codes => Context.Lines.Select(l => l.Code);
    }

    [Fact]
    public async Task The_network_path_is_read_and_the_log_says_so_once()
    {
        var rig = new Rig();
        Assert.Equal(rig.Files.Bytes, await rig.ReadAll());
        Assert.Equal(new[] { Network }, rig.Files.Opens);
        Assert.Equal($"Reading through the network path {Network}", Assert.Single(rig.Context.Lines).Text);
        Assert.Empty(rig.Delays);
    }

    [Fact]
    public async Task A_network_path_that_fails_gives_way_to_the_drive_letter_at_the_same_byte_without_waiting()
    {
        var rig = new Rig();
        rig.Files.OnRead = (path, offset, _) => path == Network && offset >= 1 << 20 ? Dropped() : null;
        Assert.Equal(rig.Files.Bytes, await rig.ReadAll());
        Assert.Equal(new[] { Network, Chosen }, rig.Files.Opens);
        Assert.Empty(rig.Delays);
        // Nothing was read twice
        Assert.Equal(1 << 20, rig.Files.Reads.First(r => r.Path == Chosen).Offset);
        var line = rig.Context.Lines.Single(l => l.Code == "read_failed");
        Assert.Contains("at byte 1048576", line.Text);
        Assert.Contains($"error {Number}", line.Text);
    }

    [Fact]
    public async Task A_share_that_drops_and_comes_back_is_waited_for_and_the_read_goes_on()
    {
        var rig = new Rig();
        var away = false;
        rig.Files.OnRead = (_, offset, _) =>
        {
            if (offset >= 2 << 20 && rig.Delays.Count == 0) away = true;
            return away ? Dropped() : null;
        };
        rig.Files.OnOpen = _ => away ? Dropped() : null;
        rig.OnDelay = () =>
        {
            Assert.Equal(StepWait.Share, rig.Context.Wait);
            if (rig.Delays.Count == 5) away = false;
        };
        Assert.Equal(rig.Files.Bytes, await rig.ReadAll());
        Assert.Equal(new[] { 1, 2, 4, 8, 16 }, rig.Delays.Select(d => (int)d.TotalSeconds));
        Assert.Equal(StepWait.None, rig.Context.Wait);
        Assert.Contains("share_back", rig.Codes);
        // Both paths were tried each time
        Assert.True(rig.Files.Opens.Count(p => p == Chosen) >= 4 && rig.Files.Opens.Count(p => p == Network) >= 4);
    }

    [Fact]
    public async Task After_several_minutes_away_it_fails_with_the_number_the_text_and_the_byte()
    {
        var rig = new Rig();
        var away = false;
        rig.Files.OnRead = (_, offset, _) => (away |= offset >= 1 << 20) ? Dropped() : null;
        rig.Files.OnOpen = _ => away ? Dropped() : null;
        var error = await Assert.ThrowsAsync<SourceReadException>(() => rig.ReadAll());
        Assert.Equal("share_lost", error.Failure.Code);
        Assert.Equal(Number, error.Failure.Number);
        Assert.Equal(1 << 20, error.Offset);
        Assert.Contains("at byte 1048576", error.Message);
        Assert.DoesNotContain("a.mp4", error.Message);
        Assert.InRange(rig.Delays.Sum(d => d.TotalMinutes), 4.5, 6);
        Assert.True(ImportQueue.IsRetryable(error));
    }

    [Fact]
    public async Task Reading_that_stops_at_the_same_byte_every_time_means_a_damaged_file()
    {
        var rig = new Rig();
        const int bad = (1 << 20) + 300_000;
        rig.Files.OnRead = (_, offset, count) => offset + count > bad ? Dropped() : null;
        var error = await Assert.ThrowsAsync<SourceReadException>(() => rig.ReadAll());
        Assert.Equal("share_damaged", error.Failure.Code);
        Assert.Equal(Number, error.Failure.Number);
        // To the nearest short read before the bad place
        Assert.InRange(error.Offset!.Value, bad - (1 << 16), bad);
        Assert.False(ImportQueue.IsRetryable(error));
    }

    [Fact]
    public async Task A_damaged_file_is_found_out_when_the_other_path_does_not_open_at_all()
    {
        var rig = new Rig();
        rig.Files.OnRead = (_, offset, _) => offset >= 1 << 20 ? Dropped() : null;
        // A drive mapped only in the other session: the round, and the patience, end on this path
        rig.Files.OnOpen = path => path == Chosen ? Dropped() : null;
        var error = await Assert.ThrowsAsync<SourceReadException>(() => rig.ReadAll());
        Assert.Equal(("share_damaged", 1L << 20), (error.Failure.Code, error.Offset));
        Assert.Equal(Chosen, rig.Files.Opens[^1]);
    }

    [Fact]
    public async Task A_share_that_is_gone_at_the_end_is_no_damaged_file_however_often_it_stopped_before()
    {
        var rig = new Rig();
        rig.Files.OnRead = (_, offset, _) => offset >= 1 << 20 ? Dropped() : null;
        // It stopped at one place for four rounds, then went away
        rig.Files.OnOpen = _ => rig.Delays.Count >= 4 ? Dropped() : null;
        var error = await Assert.ThrowsAsync<SourceReadException>(() => rig.ReadAll());
        Assert.Equal(("share_lost", 1L << 20), (error.Failure.Code, error.Offset));
    }

    [Fact]
    public async Task Waiting_on_a_stuck_byte_does_not_repeat_path_announcements()
    {
        var rig = new Rig();
        rig.Files.OnRead = (_, offset, _) => offset >= 1 << 20 ? Dropped() : null;
        var error = await Assert.ThrowsAsync<SourceReadException>(() => rig.ReadAll());
        Assert.Equal("share_damaged", error.Failure.Code);
        Assert.True(rig.Delays.Count >= 10);
        // Only the initial network path and the first fallback to the chosen path, never ping-pong spam on every wait round
        Assert.Equal(1, rig.Context.Lines.Count(l => l.Code == "share_path"));
        Assert.Equal(1, rig.Context.Lines.Count(l => l.Code == "share_chosen"));
        Assert.Equal(2, rig.Context.Lines.Count(l => l.Code == "read_failed"));
    }

    [Fact]
    public async Task A_recovered_share_announces_its_path_only_once_reads_resume()
    {
        var rig = new Rig();
        var stuck = false;
        rig.Files.OnRead = (path, offset, _) =>
        {
            if (offset >= 1 << 20 && rig.Delays.Count == 0) stuck = true;
            if (!stuck) return null;
            // After four waiting rounds, only the network path answers again
            return rig.Delays.Count >= 4 && path == Network ? null : Dropped();
        };
        Assert.Equal(rig.Files.Bytes, await rig.ReadAll());
        var codes = rig.Context.Lines.Select(l => l.Code).ToList();
        var back = codes.IndexOf("share_back");
        Assert.True(back > 0);
        // While waiting, neither path was announced again; once reads resumed on Network, it was announced once after share_back
        Assert.Equal(new[] { "share_path", "share_chosen" }, codes[..back].Where(c => c is "share_path" or "share_chosen").ToArray());
        Assert.Equal(new[] { "share_path" }, codes[(back + 1)..].Where(c => c is "share_path" or "share_chosen").ToArray());
    }

    [Fact]
    public async Task Slow_network_failures_count_against_patience_from_the_first_drop()
    {
        var rig = new Rig();
        var start = rig.Clock.Now;
        rig.Files.Before = (_, offset) =>
        {
            if (offset >= 1 << 20) rig.Clock.Now += TimeSpan.FromSeconds(75);
            return Task.CompletedTask;
        };
        rig.Files.OnRead = (_, offset, _) => offset >= 1 << 20 ? Dropped() : null;
        var error = await Assert.ThrowsAsync<SourceReadException>(() => rig.ReadAll());
        Assert.Equal("share_damaged", error.Failure.Code);
        // Patience (5m) starts on the first drop, not after a full 2.5m round; 3 rounds of 2x75s = 7.5m max (not 10m+)
        Assert.InRange((rig.Clock.Now - start).TotalMinutes, 5.0, 8.0);
        Assert.All(rig.Delays, d => Assert.True(d <= TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task A_stream_that_cannot_be_closed_does_not_matter()
    {
        var rig = new Rig();
        rig.Files.CloseFails = true;
        rig.Files.OnRead = (path, offset, _) => path == Network && offset >= 1 << 20 ? Dropped() : null;
        Assert.Equal(rig.Files.Bytes, await rig.ReadAll());
    }

    [Fact]
    public async Task A_file_that_changed_while_its_share_was_away_is_still_being_written()
    {
        var rig = new Rig();
        rig.Files.OnRead = (_, offset, _) =>
        {
            if (offset < 1 << 20 || rig.Files.Modified != 1000) return null;
            rig.Files.Modified = 2000;
            return Dropped();
        };
        Assert.Equal("file_writing", (await Assert.ThrowsAsync<StepFailedException>(() => rig.ReadAll())).Code);
    }

    [Fact]
    public async Task A_file_that_got_shorter_under_the_open_stream_is_still_being_written()
    {
        var rig = new Rig();
        // It was 10 bytes longer when it was opened
        rig.Files.Longer = 10;
        await using var stream = rig.Access.Open(Chosen, Size + 10, 1000, rig.Context);
        stream.Position = Size;
        Assert.Equal("file_writing", (await Assert.ThrowsAsync<StepFailedException>(async () => await stream.ReadAsync(new byte[10]))).Code);
    }

    [Fact]
    public async Task A_read_that_never_ends_counts_as_the_share_being_away()
    {
        var rig = new Rig(stall: TimeSpan.FromSeconds(3));
        rig.Files.Before = (path, offset) => path == Network && offset > 0 ? Task.Delay(Timeout.Infinite) : Task.CompletedTask;
        Assert.Equal(rig.Files.Bytes, await rig.ReadAll());
        Assert.Equal(new[] { Network, Chosen }, rig.Files.Opens);
    }

    [Fact]
    public async Task Pausing_during_a_read_that_never_ends_stops_at_once()
    {
        var rig = new Rig();
        var reading = new TaskCompletionSource();
        rig.Files.Before = (_, _) => { reading.TrySetResult(); return Task.Delay(Timeout.Infinite); };
        using var cts = new CancellationTokenSource();
        var read = rig.ReadAll(cts.Token);
        await reading.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_mount_that_does_not_say_what_it_is_counts_as_a_share_and_holds_one_thread_only()
    {
        using var answers = new ManualResetEventSlim();
        var rig = new Rig(answer: TimeSpan.FromSeconds(1));
        rig.Paths.Silent = answers;
        try
        {
            var asked = Stopwatch.StartNew();
            // Many files on it, at once and one after another
            Assert.All(await Task.WhenAll(Enumerable.Range(0, 20).Select(i => rig.Access.IsNetworkAsync($"/y/nas/{i}.mp4", default))), Assert.True);
            for (var i = 0; i < 20; i++) Assert.Equal(new[] { "/y/nas/a.mp4" }, await rig.Access.CandidatesAsync("/y/nas/a.mp4", default));
            Assert.Equal(1, rig.Paths.Questions);
            // Read by the chosen path meanwhile
            Assert.Equal(rig.Files.Bytes, await rig.Access.ReadAllBytesAsync("/y/nas/a.mp4", rig.Context, default));
            Assert.Equal(new[] { "/y/nas/a.mp4" }, rig.Files.Opens);
            Assert.InRange(asked.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(20));
            Assert.Equal(1, rig.Paths.Questions);
        }
        finally
        {
            answers.Set();
        }
        // Once it has answered it is asked again
        rig.Paths.Silent = null;
        await Temp.WaitUntil(() => rig.Access.Candidates("/y/nas/a.mp4").Count == 2, 20_000);
    }

    [Fact]
    public async Task The_drive_letter_is_not_asked_about_when_its_network_path_is_a_share()
    {
        var rig = new Rig();
        await rig.ReadAll();
        Assert.Equal(new[] { Network }, rig.Paths.Asked);
    }

    [Fact]
    public async Task A_question_joined_late_waits_only_for_what_is_left_of_its_time()
    {
        using var answers = new ManualResetEventSlim();
        var rig = new Rig(answer: TimeSpan.FromSeconds(4));
        rig.Paths.Silent = answers;
        try
        {
            var first = rig.Access.IsNetworkAsync("/y/nas/a.mp4", default);
            await Task.Delay(TimeSpan.FromSeconds(3));
            var asked = Stopwatch.StartNew();
            Assert.True(await rig.Access.IsNetworkAsync("/y/nas/b.mp4", default));
            Assert.InRange(asked.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));
            Assert.True(await first);
        }
        finally
        {
            answers.Set();
        }
    }

    [Fact]
    public async Task An_answer_that_comes_as_its_time_is_up_is_the_answer()
    {
        var rig = new Rig();
        rig.Paths.OnShare = false;
        var access = new SourceAccess(rig.Paths, rig.Files)
        {
            // Out of time, and answered before anybody looks
            Wait = async (answer, _, _) =>
            {
                await answer;
                throw new TimeoutException();
            },
        };
        Assert.False(await access.IsNetworkAsync("/y/nas/a.mp4", default));
        Assert.Equal(["/nas/nas/a.mp4", "/y/nas/a.mp4"], await access.CandidatesAsync("/y/nas/a.mp4", default));
    }

    [Fact]
    public async Task The_next_question_is_asked_when_the_one_before_is_answered_as_its_time_is_up()
    {
        using var answers = new ManualResetEventSlim();
        var rig = new Rig();
        rig.Paths.OnShare = false;
        rig.Paths.Silent = answers;
        var waits = 0;
        var access = new SourceAccess(rig.Paths, rig.Files)
        {
            Wait = async (answer, _, _) =>
            {
                if (Interlocked.Increment(ref waits) == 2) answers.Set();
                await answer;
                throw new TimeoutException();
            },
        };
        var first = access.IsNetworkAsync("/y/nas/a.mp4", default);
        Assert.False(await access.IsNetworkAsync("/y/nas/b.mp4", default).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.False(await first);
        Assert.Equal(["/y/nas/a.mp4", "/y/nas/b.mp4"], rig.Paths.Asked);
    }

    [Fact]
    public async Task A_step_waiting_for_a_silent_mount_stops_at_once()
    {
        using var answers = new ManualResetEventSlim();
        var rig = new Rig(answer: TimeSpan.FromMinutes(5));
        rig.Paths.Silent = answers;
        try
        {
            using var cts = new CancellationTokenSource();
            var read = rig.ReadAll(cts.Token);
            await Temp.WaitUntil(() => rig.Paths.Questions == 1, 20_000);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            answers.Set();
        }
    }

    [Fact]
    public void The_systems_answer_is_waited_for_less_than_a_stopping_step_has() =>
        Assert.True(SourceAccess.Default.Answer * 1.5 < ImportQueue.StepStopGrace);

    [Fact]
    public async Task A_missing_file_is_the_files_failure_only_when_its_share_answers()
    {
        var rig = new Rig();
        rig.Files.OnStat = _ => new FileNotFoundException("gone");
        Assert.Equal("file_missing", (await Assert.ThrowsAsync<StepFailedException>(() => rig.Access.CheckUnchangedAsync(rig.Video, rig.Context, default))).Code);
        Assert.Empty(rig.Delays);

        rig.Files.Root = false;
        rig.OnDelay = () => { rig.Files.Root = true; rig.Files.OnStat = _ => null; };
        await rig.Access.CheckUnchangedAsync(rig.Video, rig.Context, default);
        Assert.Single(rig.Delays);

        // A drive that is not connected
        rig.Paths.Mapping = rig.Paths.OnShare = false;
        rig.Files.Root = false;
        rig.Files.OnStat = _ => new DirectoryNotFoundException("gone");
        Assert.Equal("drive_gone", (await Assert.ThrowsAsync<SourceReadException>(() => rig.Access.CheckUnchangedAsync(rig.Video, rig.Context, default))).Failure.Code);
    }

    [Fact]
    public async Task A_busy_file_is_tried_a_few_times_and_a_refused_one_not_at_all()
    {
        var rig = new Rig();
        var inUse = new IOException("x", OperatingSystem.IsWindows() ? unchecked((int)0x80070020) : 16);
        rig.Files.OnOpen = _ => inUse;
        Assert.Equal("file_in_use", (await Assert.ThrowsAsync<SourceReadException>(() => rig.ReadAll())).Failure.Code);
        Assert.Equal(3, rig.Delays.Count);

        rig.Delays.Clear();
        rig.Files.OnOpen = _ => new UnauthorizedAccessException("x");
        Assert.Equal("file_denied", (await Assert.ThrowsAsync<SourceReadException>(() => rig.ReadAll())).Failure.Code);
        Assert.Empty(rig.Delays);
        // but one path's refusal does not stop the other
        rig.Files.OnOpen = path => path == Network ? new UnauthorizedAccessException("x") : null;
        Assert.Equal(rig.Files.Bytes, await rig.ReadAll());
    }

    [Fact]
    public async Task A_cloud_placeholder_says_it_is_downloading_first()
    {
        var rig = new Rig();
        rig.Files.Cloud = true;
        var waits = new List<StepWait>();
        rig.Files.Before = (_, _) => { waits.Add(rig.Context.Wait); return Task.CompletedTask; };
        await rig.ReadAll();
        Assert.Contains("cloud_download", rig.Codes);
        Assert.Equal(StepWait.Cloud, waits[0]);
        Assert.Equal(StepWait.None, waits[^1]);
    }

    // ---------------------------------------------------------------- The steps

    [Fact]
    public async Task Hashing_goes_on_where_the_last_attempt_stopped()
    {
        var rig = new Rig();
        var step = new HashStep(rig.Access);
        var item = QueueItem.For(rig.Video, "b1", "ch1", DateTimeOffset.UtcNow);
        var away = true;
        rig.Files.OnRead = (_, offset, _) => away && offset >= 2 << 20 ? Dropped() : null;
        rig.Files.OnOpen = _ => away && rig.Files.Reads.Any(r => r.Offset >= 1 << 20) ? Dropped() : null;
        var error = await Assert.ThrowsAsync<SourceReadException>(() => step.RunAsync(item, rig.Context, default));
        Assert.Equal(2 << 20, error.Offset);
        Assert.Null(item.Sha256);

        away = false;
        rig.Files.Reads.Clear();
        Assert.Equal(StepResult.Done, await step.RunAsync(item, rig.Context, default));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(rig.Files.Bytes)).ToLowerInvariant(), item.Sha256);
        Assert.Equal(2 << 20, rig.Files.Reads[0].Offset);
        Assert.Contains("hash_resumed", rig.Codes);
        Assert.Null(item.Hashing);
        var edges = new byte[2 << 20];
        rig.Files.Bytes.AsSpan(0, 1 << 20).CopyTo(edges);
        rig.Files.Bytes.AsSpan(Size - (1 << 20)).CopyTo(edges.AsSpan(1 << 20));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(edges)).ToLowerInvariant(), item.FingerprintHash);
    }

    [Fact]
    public async Task A_hash_kept_for_another_version_of_the_file_is_not_used()
    {
        var rig = new Rig();
        var item = QueueItem.For(rig.Video with { LastModifiedMs = 1 }, "b1", "ch1", DateTimeOffset.UtcNow);
        item.Hashing = new HashSoFar(Size, 999);
        item.Hashing.Append(new byte[10], 10);
        item.Video = rig.Video;
        await new HashStep(rig.Access).RunAsync(item, rig.Context, default);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(rig.Files.Bytes)).ToLowerInvariant(), item.Sha256);
    }

    [Fact]
    public async Task A_file_written_to_while_it_is_hashed_is_refused()
    {
        var rig = new Rig();
        var video = rig.Video;
        var item = QueueItem.For(video, "b1", "ch1", DateTimeOffset.UtcNow);
        rig.Files.OnRead = (_, offset, _) =>
        {
            if (offset >= 2 << 20) rig.Files.Modified = 5000;
            return null;
        };
        Assert.Equal("file_writing", (await Assert.ThrowsAsync<StepFailedException>(() => new HashStep(rig.Access).RunAsync(item, rig.Context, default))).Code);
        Assert.Null(item.Sha256);
    }

    [Fact]
    public async Task An_upload_chunk_is_read_whole_over_a_dropped_share()
    {
        var rig = new Rig();
        rig.Files.OnRead = (path, offset, _) => path == Network && offset == 1 << 20 ? Dropped() : null;
        await using var stream = rig.Access.Open(Chosen, Size, 1000, rig.Context);
        var chunk = new byte[Size - 100];
        stream.Position = 100;
        await stream.ReadExactlyAsync(chunk);
        Assert.Equal(rig.Files.Bytes[100..], chunk);
    }

    [Fact]
    public async Task A_caption_file_on_a_share_that_is_away_fails_the_step_and_an_unreadable_one_is_left_out()
    {
        var rig = new Rig();
        var video = rig.Video with { Captions = new[] { new CaptionFile("ar", "/y/a.ar.srt") } };
        rig.Files.Bytes = "1\n00:00 --> 00:01\nx"u8.ToArray();
        Assert.Single((await AttachStep.BuildAsync(video, rig.Access, rig.Context, default)).Captions!);

        rig.Files.OnOpen = _ => Dropped();
        await Assert.ThrowsAsync<SourceReadException>(() => AttachStep.BuildAsync(video, rig.Access, rig.Context, default));

        rig.Files.OnOpen = _ => new UnauthorizedAccessException("x");
        Assert.Null((await AttachStep.BuildAsync(video, rig.Access, rig.Context, default)).Captions);
        Assert.Contains("left_out_unreadable", rig.Codes);
    }

    [Fact]
    public async Task Ffmpeg_runs_again_on_the_other_path_when_it_could_not_read_the_file()
    {
        var rig = new Rig();
        var used = new List<string>();
        var answer = await rig.Access.WithPathAsync(Chosen, rig.Context, path =>
        {
            used.Add(path);
            return used.Count == 1 ? throw new LocalEncodeException("source_read", "stopped") : Task.FromResult("ok");
        }, default);
        Assert.Equal("ok", answer);
        Assert.Equal(new[] { Network, Chosen }, used);
        Assert.Equal(StepWait.None, rig.Context.Wait);

        used.Clear();
        await Assert.ThrowsAsync<LocalEncodeException>(() => rig.Access.WithPathAsync<string>(Chosen, rig.Context, path =>
        {
            used.Add(path);
            throw new LocalEncodeException("source_read", "stopped");
        }, default));
        Assert.Equal(3, used.Count);

        // Not the share's fault: once
        used.Clear();
        await Assert.ThrowsAsync<LocalEncodeException>(() => rig.Access.WithPathAsync<string>(Chosen, rig.Context, path =>
        {
            used.Add(path);
            throw new LocalEncodeException("ffmpeg_failed", "x");
        }, default));
        Assert.Single(used);
    }

    [Theory]
    [InlineData("[in#0/matroska,webm @ 0x1] Error during demuxing: Input/output error", false, true)]
    [InlineData("[in#0/matroska,webm @ 0x1] Error during demuxing: Invalid argument", true, true)]
    [InlineData("[in#0/matroska,webm @ 0x1] Error during demuxing: Invalid argument", false, false)]
    [InlineData(@"\\nas\v\a.webm: Input/output error", true, true)]
    [InlineData("[in#0 @ 0x1] Error opening input: Operation timed out", true, true)]
    [InlineData("[in#0/mov @ 0x1] Error during demuxing: Invalid data found when processing input", true, false)]
    [InlineData("[out#0/mp4 @ 0x1] Error writing trailer: Input/output error", true, false)]
    public void Ffmpegs_messages_for_a_source_it_could_not_read(string line, bool network, bool stopped) =>
        Assert.Equal(stopped, FfmpegMediaTools.SourceStopped(@"\\nas\v\a.webm", "Input #0\n" + line + "\nframe=1\n", network));

    [Fact]
    public void A_scan_reads_the_network_path_and_shows_the_folder_that_was_chosen()
    {
        var real = Temp.Folder();
        Temp.Bytes(real, "a.mp4", 10);
        Temp.Write(real, "a.ar.srt", "1");
        Temp.Write(real, "a.json", "{\"title\":\"A\"}");
        Temp.Write(real, Path.Combine("sub", "b.mp4"), "x");
        Temp.Write(real, Path.Combine("sub", "b.jpg"), "x");
        var chosen = Path.Combine(Path.GetTempPath(), "y-" + Guid.NewGuid().ToString("N"));
        var access = new SourceAccess(new OnePath(chosen, real), new SourceFileSystem());
        var result = FolderScanner.Scan(chosen, true, null, source: access);
        Assert.Equal(new[] { Path.Combine(chosen, "a.mp4"), Path.Combine(chosen, "sub", "b.mp4") }, result.Videos.Select(v => v.Path));
        Assert.Equal(Path.Combine(chosen, "a.ar.srt"), result.Videos[0].Captions.Single().Path);
        Assert.Equal(Path.Combine(chosen, "a.json"), result.Videos[0].MetadataPath);
        Assert.Equal(Path.Combine(chosen, "sub", "b.jpg"), result.Videos[1].ThumbnailPath);
        Assert.Equal("A", result.Videos[0].Metadata.Title);
    }

    private sealed class OnePath(string chosen, string real) : INetworkPaths
    {
        public string? Mapped(string path) => path.StartsWith(chosen, StringComparison.Ordinal) ? real + path[chosen.Length..] : null;
        public bool IsNetwork(string path) => true;
    }

    [Fact]
    public void A_scan_takes_the_path_that_answers_and_tries_a_dropped_share_again()
    {
        var rig = new Rig();
        Assert.Equal("/y/f", rig.Access.Scan("/y/f", root => root == "/nas/f" ? throw new DirectoryNotFoundException("x") : root, default));
        var tries = 0;
        Assert.Equal(7, rig.Access.Scan("/y/f", _ => ++tries < 7 ? throw Dropped() : tries, default));
        var error = Assert.Throws<SourceReadException>(() => rig.Access.Scan<int>("/y/f", _ => throw Dropped(), default));
        Assert.Equal((ReadKind.Network, Number), (error.Failure.Kind, error.Failure.Number));
    }

    [Fact]
    public async Task The_queue_shows_the_wait_and_keeps_the_number_the_text_and_the_byte()
    {
        var waiting = new TaskCompletionSource();
        var failure = new ReadFailure("share_lost", ReadKind.Network, ReadSystem.Windows, 59, "An unexpected network error occurred");
        var hash = new ScriptedStep(ImportStepKind.Hash, async (_, context, _) =>
        {
            context.Waiting(StepWait.Share);
            await waiting.Task;
            throw new SourceReadException(failure, 123_456);
        });
        using var queue = new ImportQueue(Path.Combine(Temp.Folder(), "queue.json"), ImportQueueTests.All(hash), backoff: _ => TimeSpan.FromMinutes(1));
        var item = QueueItem.For(new Rig().Video, "b1", "ch1", DateTimeOffset.UtcNow);
        queue.Add(new[] { item });
        queue.Pump();
        await Temp.WaitUntil(() => item.Wait == StepWait.Share);
        waiting.SetResult();
        await Temp.WaitUntil(() => item.Phase == ItemPhase.RetryWaiting);
        Assert.Equal(StepWait.None, item.Wait);
        var step = item.CurrentStep!;
        Assert.Equal("share_lost", step.LastErrorCode);
        Assert.Equal(new ReadError("Windows", 59, "An unexpected network error occurred", 123_456), step.ReadError);
        var line = queue.LogLines(item.Id).Single(l => l.Code == "attempt_failed");
        Assert.Equal(new[] { "Windows", "59", "An unexpected network error occurred", "123456" }, line.Args![^4..]);
        Assert.Contains("Windows error 59: An unexpected network error occurred", queue.LogText(item.Id));
        Assert.Contains("at byte 123456", queue.LogText(item.Id));
        // Saved with the queue
        using var second = new ImportQueue(Path.Combine(Temp.Folder(), "q2.json"), ImportQueueTests.All());
        Assert.Equal(step.ReadError, System.Text.Json.JsonSerializer.Deserialize<StepState>(System.Text.Json.JsonSerializer.Serialize(step))!.ReadError);
    }
}

using System.Diagnostics;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Import.Sources;
using AlUmmahNow.ToolsCore.Instance;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>One copy of the app per queue folder (issue 89).</summary>
public class InstanceLockTests
{
    [Fact]
    public void One_copy_holds_the_folder_until_it_lets_go()
    {
        var folder = Path.Combine(Temp.Folder(), "new");
        Assert.Equal(LockAnswer.Taken, QueueFolderLock.TryTake(folder, out var first));
        Assert.Equal(LockAnswer.Busy, QueueFolderLock.TryTake(folder, out var second));
        Assert.Null(second);
        // Another folder is another queue
        Assert.Equal(LockAnswer.Taken, QueueFolderLock.TryTake(Temp.Folder(), out var elsewhere));
        elsewhere!.Dispose();
        first!.Dispose();
        Assert.Equal(LockAnswer.Taken, QueueFolderLock.TryTake(folder, out var third));
        third!.Dispose();
        // The file stays: two copies must never hold two files of one name
        Assert.True(File.Exists(Path.Combine(folder, QueueFolderLock.FileName)));
    }

    [Fact]
    public void A_folder_that_takes_no_lock_is_told_apart_from_a_busy_one()
    {
        var file = Temp.Write(Temp.Folder(), "not-a-folder", "x");
        Assert.Equal(LockAnswer.Unavailable, QueueFolderLock.TryTake(Path.Combine(file, "queue"), out var held));
        Assert.Null(held);
    }

    [Fact]
    public async Task A_child_process_does_not_keep_the_lock()
    {
        var folder = Temp.Folder();
        Assert.Equal(LockAnswer.Taken, QueueFolderLock.TryTake(folder, out var held));
        // As ffmpeg, started while the lock is held and still running afterwards
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("ping", "-n 30 127.0.0.1")
            : new ProcessStartInfo("/bin/sleep", "30");
        start.RedirectStandardOutput = true;
        start.UseShellExecute = false;
        using var child = Process.Start(start)!;
        try
        {
            held!.Dispose();
            Assert.Equal(LockAnswer.Taken, QueueFolderLock.TryTake(folder, out var again));
            again!.Dispose();
        }
        finally
        {
            child.Kill();
            await child.WaitForExitAsync();
        }
    }

    [Fact]
    public void A_saved_queue_does_not_share_its_temporary_file()
    {
        var path = Path.Combine(Temp.Folder(), "queue.json");
        // What another process's save would take away, and this one could not write
        Directory.CreateDirectory(path + ".tmp");
        using var queue = new ImportQueue(path, ImportQueueTests.All());
        queue.SetParallel(ImportStepKind.Hash, 3);
        Assert.True(File.Exists(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }
}

/// <summary>A file written whole or not at all.</summary>
public class InstanceAtomicFileTests
{
    [Fact]
    public void A_save_that_cannot_take_its_place_leaves_nothing_beside_the_file()
    {
        var folder = Temp.Folder();
        var path = Path.Combine(folder, "state.json");
        AtomicFile.Write(path, "1");
        AtomicFile.Write(path, "2");
        Assert.Equal("2", File.ReadAllText(path));
        Assert.Single(Directory.GetFileSystemEntries(folder));

        // A folder in its place: nothing can be moved over it
        var blocked = Path.Combine(folder, "blocked.json");
        Directory.CreateDirectory(Path.Combine(blocked, "x"));
        Assert.ThrowsAny<Exception>(() => AtomicFile.Write(blocked, "3"));
        Assert.Equal(new[] { blocked, path }, Directory.GetFileSystemEntries(folder).Order());
    }
}

/// <summary>Saves from several threads at once (the update state is saved by the interface and by the check).</summary>
public class InstanceAtomicFileRaceTests
{
    [Fact]
    public async Task Saves_at_the_same_moment_are_all_whole_and_leave_nothing()
    {
        var folder = Temp.Folder();
        var path = Path.Combine(folder, "state.json");
        var texts = Enumerable.Range(0, 8).Select(i => new string((char)('a' + i), 200_000)).ToArray();
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        await Task.WhenAll(texts.Select(t => Task.Run(() =>
        {
            for (var n = 0; n < 25; n++)
            {
                // Two moves onto one file at the same moment may be refused on Windows: that save fails whole
                try { AtomicFile.Write(path, t); }
                catch (Exception ex) when (OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException) { failures.Enqueue(ex); }
            }
        })));
        Assert.Contains(File.ReadAllText(path), texts);
        Assert.Single(Directory.GetFileSystemEntries(folder));
        if (!OperatingSystem.IsWindows()) Assert.Empty(failures);
    }
}

/// <summary>A second copy asks the first to come forward.</summary>
public class InstanceChannelTests
{
    private static async Task<T> WithServer<T>(string folder, Func<Task<FrontAnswer>> front, Func<Task<T>> work)
    {
        using var end = new CancellationTokenSource();
        var server = InstanceChannel.ListenAsync(folder, front, ct: end.Token);
        try
        {
            return await work();
        }
        finally
        {
            end.Cancel();
            await server.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData(FrontAnswer.Shown)]
    [InlineData(FrontAnswer.Quitting)]
    public async Task The_running_copy_answers_and_says_its_process_first(FrontAnswer answer)
    {
        var folder = Temp.Folder();
        var asked = 0;
        var allowed = 0;
        var got = await WithServer(folder, () => { Assert.Equal(Environment.ProcessId, allowed); asked++; return Task.FromResult(answer); },
            () => InstanceChannel.AskAsync(folder, pid => allowed = pid, TimeSpan.FromSeconds(20)));
        Assert.Equal(answer, got);
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Several_copies_at_once_are_all_answered()
    {
        var folder = Temp.Folder();
        var answers = await WithServer(folder, () => Task.FromResult(FrontAnswer.Shown),
            () => Task.WhenAll(Enumerable.Range(0, 6).Select(_ => InstanceChannel.AskAsync(folder, limit: TimeSpan.FromSeconds(20)))));
        Assert.All(answers, a => Assert.Equal(FrontAnswer.Shown, a));
    }

    [Fact]
    public async Task Nobody_there_and_a_window_that_hangs_are_no_answer()
    {
        var folder = Temp.Folder();
        Assert.Null(await InstanceChannel.AskAsync(folder, limit: TimeSpan.FromSeconds(1)));
        var hangs = new TaskCompletionSource<FrontAnswer>();
        Assert.Null(await WithServer(folder, () => hangs.Task, () => InstanceChannel.AskAsync(folder, limit: TimeSpan.FromSeconds(2))));
    }

    [Fact]
    public async Task A_failing_answer_does_not_end_the_listening()
    {
        var folder = Temp.Folder();
        var calls = 0;
        var answer = await WithServer(folder, () => ++calls == 1 ? throw new InvalidOperationException("x") : Task.FromResult(FrontAnswer.Shown), async () =>
        {
            Assert.Null(await InstanceChannel.AskAsync(folder, limit: TimeSpan.FromSeconds(20)));
            return await InstanceChannel.AskAsync(folder, limit: TimeSpan.FromSeconds(20));
        });
        Assert.Equal(FrontAnswer.Shown, answer);
    }

    [Fact]
    public void The_name_is_the_folders_own_and_short()
    {
        var folder = Temp.Folder();
        Assert.Equal(InstanceChannel.NameFor(folder), InstanceChannel.NameFor(folder + Path.DirectorySeparatorChar));
        Assert.NotEqual(InstanceChannel.NameFor(folder), InstanceChannel.NameFor(Path.Combine(folder, "x")));
        Assert.InRange(InstanceChannel.NameFor(folder).Length, 1, 24);
    }
}

/// <summary>Run, hand over, or say it is already open.</summary>
public class InstanceDecisionTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<(StartDecision Decision, TimeSpan Waited, int Asked)> Decide(Func<TimeSpan, LockAnswer> take, FrontAnswer? answer)
    {
        var clock = new Clock();
        var start = clock.Now;
        var asked = 0;
        var decision = await SingleInstance.DecideAsync(() => take(clock.Now - start), () => { asked++; return Task.FromResult(answer); }, clock, wait => { clock.Now += wait; return Task.CompletedTask; });
        return (decision, clock.Now - start, asked);
    }

    [Fact]
    public async Task Each_start_has_one_outcome()
    {
        Assert.Equal((StartDecision.Run, TimeSpan.Zero, 0), await Decide(_ => LockAnswer.Taken, null));
        Assert.Equal((StartDecision.Run, TimeSpan.Zero, 0), await Decide(_ => LockAnswer.Unavailable, null));
        Assert.Equal((StartDecision.HandedOver, TimeSpan.Zero, 1), await Decide(_ => LockAnswer.Busy, FrontAnswer.Shown));
        // Closing: the second one starts once it has gone, also after the time for a silent one
        Assert.Equal(StartDecision.Run, (await Decide(t => t < TimeSpan.FromSeconds(20) ? LockAnswer.Busy : LockAnswer.Taken, FrontAnswer.Quitting)).Decision);
        var hung = await Decide(_ => LockAnswer.Busy, FrontAnswer.Quitting);
        Assert.Equal((StartDecision.AlreadyOpen, SingleInstance.WhileQuitting), (hung.Decision, hung.Waited));
        // Silent: it may be just starting or ending
        Assert.Equal(StartDecision.Run, (await Decide(t => t < TimeSpan.FromSeconds(5) ? LockAnswer.Busy : LockAnswer.Taken, null)).Decision);
        var silent = await Decide(_ => LockAnswer.Busy, null);
        Assert.Equal((StartDecision.AlreadyOpen, SingleInstance.WhileSilent), (silent.Decision, silent.Waited));
    }

    [Fact]
    public async Task The_copy_that_has_the_queue_listens_before_it_waits_and_waits_without_a_window_only_for_a_moment()
    {
        var order = new List<string>();
        async Task<StartDecision> Start(LockAnswer answer, bool updaterEnds) => await SingleInstance.StartAsync(() => answer, () => Task.FromResult<FrontAnswer?>(FrontAnswer.Shown),
            () => order.Add("listen"), limit => { order.Add("wait " + limit.TotalSeconds); return updaterEnds; });
        Assert.Equal(StartDecision.Run, await Start(LockAnswer.Taken, updaterEnds: true));
        Assert.Equal(new[] { "listen", "wait 3" }, order);
        order.Clear();
        Assert.Equal(StartDecision.FinishingUpdate, await Start(LockAnswer.Taken, updaterEnds: false));
        Assert.Equal(new[] { "listen", "wait 3" }, order);
        order.Clear();
        // A second copy neither listens nor waits
        Assert.Equal(StartDecision.HandedOver, await Start(LockAnswer.Busy, updaterEnds: true));
        Assert.Empty(order);
    }

    [Fact]
    public async Task A_second_copy_started_while_the_first_waits_for_the_updater_is_answered()
    {
        var folder = Temp.Folder();
        QueueFolderLock? first = null, second = null;
        using var end = new CancellationTokenSource();
        var server = Task.CompletedTask;
        var waiting = new TaskCompletionSource();
        using var updaterDone = new ManualResetEventSlim();
        var starting = Task.Run(() => SingleInstance.StartAsync(() => QueueFolderLock.TryTake(folder, out first), () => InstanceChannel.AskAsync(folder),
            () => server = Task.Run(() => InstanceChannel.ListenAsync(folder, () => Task.FromResult(FrontAnswer.Shown), ct: end.Token)),
            _ => { waiting.SetResult(); updaterDone.Wait(); return true; }));
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(StartDecision.HandedOver, await SingleInstance.StartAsync(() => QueueFolderLock.TryTake(folder, out second), () => InstanceChannel.AskAsync(folder, limit: TimeSpan.FromSeconds(20)), () => Assert.Fail("not the first"), _ => true));
        updaterDone.Set();
        Assert.Equal(StartDecision.Run, await starting);
        end.Cancel();
        await server;
        first!.Dispose();
    }

    [Fact]
    public async Task Two_copies_on_one_folder_for_real()
    {
        var folder = Temp.Folder();
        QueueFolderLock? first = null, second = null;
        Assert.Equal(StartDecision.Run, await SingleInstance.DecideAsync(() => QueueFolderLock.TryTake(folder, out first), () => InstanceChannel.AskAsync(folder)));
        using var end = new CancellationTokenSource();
        var server = InstanceChannel.ListenAsync(folder, () => Task.FromResult(FrontAnswer.Shown), ct: end.Token);
        Assert.Equal(StartDecision.HandedOver, await SingleInstance.DecideAsync(() => QueueFolderLock.TryTake(folder, out second), () => InstanceChannel.AskAsync(folder, limit: TimeSpan.FromSeconds(20))));
        Assert.Null(second);
        end.Cancel();
        await server;
        first!.Dispose();
        Assert.Equal(StartDecision.Run, await SingleInstance.DecideAsync(() => QueueFolderLock.TryTake(folder, out second), () => InstanceChannel.AskAsync(folder)));
        second!.Dispose();
    }
}

/// <summary>An update waits while another copy runs from the same install.</summary>
public class InstanceInstallGuardTests
{
    private sealed class Listed(params (int, string)[] all) : IRunningProcesses
    {
        public readonly List<(int Pid, string Path)> Running = all.Select(p => (p.Item1, p.Item2)).ToList();
        /// <summary>Short names, junctions and links, and where they lead</summary>
        public readonly Dictionary<string, string> Links = new(StringComparer.OrdinalIgnoreCase);
        public Exception? Fails;
        public IReadOnlyList<(int Pid, string Path)> All() => Fails is null ? Running : throw Fails;
        public string Final(string path)
        {
            foreach (var (from, to) in Links)
            {
                if (path.StartsWith(from, StringComparison.OrdinalIgnoreCase)) return to + path[from.Length..];
            }
            return path;
        }
    }

    private const string Windows = @"C:\Users\a\AppData\Local\AlUmmahNow.Tools\current\Tools.exe";
    private const string Mac = "/Applications/Al Ummah Now Tools.app/Contents/MacOS/Tools";

    private static InstallGuard Guard(IRunningProcesses listed, string program) => new(listed, program, 1, program == Mac ? ReadSystem.MacOS : ReadSystem.Windows);

    private static bool Other(string program, params (int, string)[] running) => Guard(new Listed(running), program).OtherCopyRunning();

    [Theory]
    [InlineData(@"C:\Users\a\AppData\Local\AlUmmahNow.Tools\current\Tools.exe", false, @"C:\Users\a\AppData\Local\AlUmmahNow.Tools")]
    [InlineData(@"C:\Tools\Tools.exe", false, @"C:\Tools")]
    [InlineData(@"C:\current\Tools.exe", false, @"C:\current")]
    [InlineData("/Applications/Al Ummah Now Tools.app/Contents/MacOS/Tools", true, "/Applications/Al Ummah Now Tools.app")]
    [InlineData("/usr/lib/tools/Tools", false, "/usr/lib/tools")]
    // Never a whole drive, file system or share
    [InlineData(@"C:\Tools.exe", false, null)]
    [InlineData("/Tools", false, null)]
    [InlineData("/Tools", true, null)]
    [InlineData(@"\\nas\share\Tools.exe", false, null)]
    [InlineData(@"\\nas\share\current\Tools.exe", false, @"\\nas\share\current")]
    [InlineData(@"\\nas\share\x\current\Tools.exe", false, @"\\nas\share\x")]
    // A bundle only on macOS, and only around Contents
    [InlineData(@"C:\dev.app\Tools\current\Tools.exe", false, @"C:\dev.app\Tools")]
    [InlineData(@"C:\dev.app\Contents\Tools\Tools.exe", false, @"C:\dev.app\Contents\Tools")]
    [InlineData("/Users/a/dev.app/Tools/Tools", true, "/Users/a/dev.app/Tools")]
    [InlineData("/Users/a/dev.app/x/Tools.app/Contents/MacOS/Tools", true, "/Users/a/dev.app/x/Tools.app")]
    public void The_folder_the_updater_replaces(string program, bool mac, string? root) => Assert.Equal(root, InstallGuard.RootOf(program, mac));

    [Fact]
    public void Only_another_copy_of_the_app_from_the_same_install_counts()
    {
        Assert.False(Other(Windows));
        Assert.False(Other(Windows, (1, Windows)));
        Assert.True(Other(Windows, (1, Windows), (2, Windows.ToUpperInvariant())));
        Assert.True(Other(Mac, (1, Mac), (2, Mac)));
        // The updater, the folder scanner the app started, another install, and a folder whose name only begins the same
        Assert.False(Other(Windows, (2, @"C:\Users\a\AppData\Local\AlUmmahNow.Tools\Update.exe"), (3, @"C:\Users\a\AppData\Local\AlUmmahNow.Tools\current\FileScanService.exe"),
            (4, @"C:\Program Files\Al Ummah Now Tools\Tools.exe"), (5, @"C:\Users\a\AppData\Local\AlUmmahNow.ToolsOld\current\Tools.exe")));
        Assert.False(Other(Mac, (2, "/Applications/Al Ummah Now Tools.app/Contents/MacOS/UpdateMac"), (3, "/Users/a/Al Ummah Now Tools.app/Contents/MacOS/Tools")));
    }

    [Fact]
    public void A_program_on_a_drive_itself_is_only_compared_with_itself()
    {
        Assert.False(Other(@"C:\Tools.exe", (2, @"C:\elsewhere\Tools.exe")));
        Assert.True(Other(@"C:\Tools.exe", (2, @"c:\tools.EXE")));
    }

    [Theory]
    [InlineData(@"C:\Users\a\AppData\Local\ALUMMA~1.TOO\current\Tools.exe", @"C:\Users\a\AppData\Local\ALUMMA~1.TOO", @"C:\Users\a\AppData\Local\AlUmmahNow.Tools")]
    [InlineData(@"C:\Users\a\AppData\Local\AlUmmahNow.Tools\current\TOOLS~1.EXE", @"C:\Users\a\AppData\Local\AlUmmahNow.Tools\current\TOOLS~1.EXE", Windows)]
    [InlineData(@"D:\link\current\Tools.exe", @"D:\link", @"C:\Users\a\AppData\Local\AlUmmahNow.Tools")]
    public void A_copy_started_by_a_short_name_or_through_a_junction_is_found(string started, string from, string to)
    {
        var listed = new Listed((1, Windows), (2, started)) { Links = { [from] = to } };
        Assert.True(Guard(listed, Windows).OtherCopyRunning());
        // And the other way round: this copy was started so, the other one plainly
        var other = new Listed((1, started), (2, Windows)) { Links = { [from] = to } };
        Assert.True(Guard(other, started).OtherCopyRunning());
        Assert.False(Guard(new Listed((1, started)) { Links = { [from] = to } }, started).OtherCopyRunning());
    }

    [Fact]
    public void A_listing_that_fails_makes_the_update_wait()
    {
        Assert.True(Guard(new Listed { Fails = new InvalidOperationException("x") }, Windows).OtherCopyRunning());
    }

    [Fact]
    public void A_start_during_an_update_waits_for_the_updater()
    {
        foreach (var (program, updater) in new[] { (Windows, @"C:\Users\a\AppData\Local\AlUmmahNow.Tools\Update.exe"), (Mac, "/Applications/Al Ummah Now Tools.app/Contents/MacOS/UpdateMac") })
        {
            var listed = new Listed((1, program), (9, updater));
            var guard = Guard(listed, program);
            Assert.True(guard.UpdaterRunning());
            var slept = 0;
            Assert.True(guard.WaitForUpdater(TimeSpan.FromSeconds(60), _ => { if (++slept == 3) listed.Running.RemoveAt(1); }));
            Assert.Equal(3, slept);
            Assert.True(guard.WaitForUpdater(TimeSpan.FromSeconds(60), _ => Assert.Fail("nothing to wait for")));
        }
        Assert.False(Guard(new Listed((9, @"C:\Users\a\AppData\Local\AlUmmahNow.Tools\Update.exe")), Windows).WaitForUpdater(TimeSpan.FromSeconds(5), _ => { }));
        // Another app's updater is none of ours
        Assert.False(Guard(new Listed((9, @"C:\Users\a\AppData\Local\Other\Update.exe")), Windows).UpdaterRunning());
    }

    /// <summary>A second run of one program, as a second copy of the app would be; also through a junction or a linked folder, and by its short name where Windows keeps them.</summary>
    [Theory]
    [InlineData("plain")]
    [InlineData("link")]
    [InlineData("short")]
    public async Task The_system_lists_a_running_program_by_its_final_path(string way)
    {
        // Ping from a folder of its own on Windows; elsewhere one nothing else here runs, where it is (macOS ends a copied system
        // program, and some Linux ones are one program by many names)
        var windows = OperatingSystem.IsWindows();
        var folder = "/usr/bin";
        var name = "awk";
        if (windows)
        {
            folder = Path.Combine(Temp.Folder(), "a long folder name");
            name = "aun second copy.exe";
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), Path.Combine(folder, name));
        }
        var program = Path.Combine(folder, name);
        var other = program;
        switch (way)
        {
            case "link":
                var link = Path.Combine(Temp.Folder(), "link");
                if (windows)
                {
                    using var make = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{folder}\"") { UseShellExecute = false, RedirectStandardOutput = true })!;
                    await make.WaitForExitAsync();
                    Assert.Equal(0, make.ExitCode);
                }
                else Directory.CreateSymbolicLink(link, folder);
                other = Path.Combine(link, name);
                break;
            case "short":
                if (!windows) return;
                using (var ask = Process.Start(new ProcessStartInfo("cmd.exe", $"/c for %I in (\"{program}\") do @echo %~sI") { UseShellExecute = false, RedirectStandardOutput = true })!)
                {
                    other = (await ask.StandardOutput.ReadToEndAsync()).Trim();
                }
                // This volume keeps no short names
                if (!other.Contains('~')) return;
                break;
        }
        Process Start(string path)
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true };
            foreach (var a in windows ? new[] { "-n", "60", "127.0.0.1" } : new[] { "BEGIN { getline }" }) start.ArgumentList.Add(a);
            return Process.Start(start)!;
        }
        using var first = Start(program);
        using var second = Start(other);
        try
        {
            var system = new SystemProcesses();
            string? path = null;
            await Temp.WaitUntil(() => (path = system.All().FirstOrDefault(p => p.Pid == first.Id).Path) is not null && path.Contains(windows ? "aun second copy" : "awk", StringComparison.OrdinalIgnoreCase), 20_000);
            var guard = new InstallGuard(system, path!, first.Id, ReadFailure.ThisSystem);
            await Temp.WaitUntil(guard.OtherCopyRunning, 20_000);
            // And seen from the other one
            await Temp.WaitUntil(new InstallGuard(system, other, second.Id, ReadFailure.ThisSystem).OtherCopyRunning, 20_000);
            second.Kill();
            await second.WaitForExitAsync();
            await Temp.WaitUntil(() => !guard.OtherCopyRunning(), 20_000);
        }
        finally
        {
            first.Kill();
            if (!second.HasExited) second.Kill();
        }
    }

    [Fact]
    public void This_process_has_a_guard()
    {
        var guard = InstallGuard.ForThisProcess();
        Assert.NotNull(guard);
        Assert.False(guard.UpdaterRunning());
    }
}

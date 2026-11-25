using System.Diagnostics;
using AlUmmahNow.ToolsCore.Import;
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
        public List<(int Pid, string Path)> All = all.Select(p => (p.Item1, p.Item2)).ToList();
        public Exception? Fails;
        public IReadOnlyList<(int Pid, string Path)> Find(IReadOnlyCollection<string> names) => Fails is null ? All : throw Fails;
    }

    private const string Windows = @"C:\Users\a\AppData\Local\AlUmmahNow.Tools\current\Tools.exe";
    private const string Mac = "/Applications/Al Ummah Now Tools.app/Contents/MacOS/Tools";

    private static bool Other(string program, params (int, string)[] running) => new InstallGuard(new Listed(running), program, 1, ignoreCase: true).OtherCopyRunning();

    [Fact]
    public void The_folder_the_updater_replaces()
    {
        Assert.Equal(@"C:\Users\a\AppData\Local\AlUmmahNow.Tools", InstallGuard.RootOf(Windows));
        Assert.Equal(@"C:\Tools", InstallGuard.RootOf(@"C:\Tools\Tools.exe"));
        Assert.Equal("/Applications/Al Ummah Now Tools.app", InstallGuard.RootOf(Mac));
        Assert.Equal("/usr/lib/tools", InstallGuard.RootOf("/usr/lib/tools/Tools"));
    }

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
    public void A_listing_that_fails_makes_the_update_wait()
    {
        Assert.True(new InstallGuard(new Listed { Fails = new InvalidOperationException("x") }, Windows, 1, true).OtherCopyRunning());
    }

    [Fact]
    public void A_start_during_an_update_waits_for_the_updater()
    {
        foreach (var (program, updater) in new[] { (Windows, @"C:\Users\a\AppData\Local\AlUmmahNow.Tools\Update.exe"), (Mac, "/Applications/Al Ummah Now Tools.app/Contents/MacOS/UpdateMac") })
        {
            var listed = new Listed((1, program), (9, updater));
            var guard = new InstallGuard(listed, program, 1, true);
            Assert.True(guard.UpdaterRunning());
            var slept = 0;
            Assert.True(guard.WaitForUpdater(TimeSpan.FromSeconds(60), _ => { if (++slept == 3) listed.All.RemoveAt(1); }));
            Assert.Equal(3, slept);
            Assert.True(guard.WaitForUpdater(TimeSpan.FromSeconds(60), _ => Assert.Fail("nothing to wait for")));
        }
        var stuck = new InstallGuard(new Listed((9, @"C:\Users\a\AppData\Local\AlUmmahNow.Tools\Update.exe")), Windows, 1, true);
        Assert.False(stuck.WaitForUpdater(TimeSpan.FromSeconds(5), _ => { }));
        // Another app's updater is none of ours
        Assert.False(new InstallGuard(new Listed((9, @"C:\Users\a\AppData\Local\Other\Update.exe")), Windows, 1, true).UpdaterRunning());
    }

    [Fact]
    public async Task The_system_lists_a_running_program_by_its_path()
    {
        // A second run of one program, as a second copy of the app would be: ping from a folder of its own on Windows; elsewhere
        // one nothing else here runs, where it is (macOS ends a copied system program, and some Linux ones are one program by many names)
        var windows = OperatingSystem.IsWindows();
        var program = "/usr/bin/awk";
        if (windows)
        {
            program = Path.Combine(Temp.Folder(), "aun-copy.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), program);
        }
        Process Start()
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true };
            foreach (var a in windows ? new[] { "-n", "60", "127.0.0.1" } : new[] { "BEGIN { getline }" }) start.ArgumentList.Add(a);
            return Process.Start(start)!;
        }
        using var first = Start();
        using var second = Start();
        try
        {
            string? path = null;
            await Temp.WaitUntil(() => (path = new SystemProcesses().Find(["aun-copy"]).FirstOrDefault(p => p.Pid == first.Id).Path) is not null && path.Contains(windows ? "aun-copy" : "awk", StringComparison.OrdinalIgnoreCase), 20_000);
            var guard = new InstallGuard(new SystemProcesses(), path!, first.Id, ignoreCase: !OperatingSystem.IsLinux());
            await Temp.WaitUntil(guard.OtherCopyRunning, 20_000);
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

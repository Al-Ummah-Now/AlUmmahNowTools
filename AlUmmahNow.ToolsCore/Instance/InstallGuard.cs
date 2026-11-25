using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace AlUmmahNow.ToolsCore.Instance;

/// <summary>The running processes and the programs they run, as far as this user may see them.</summary>
public interface IRunningProcesses
{
    /// <param name="names">Programs of interest (file names without an extension); others may be left out</param>
    IReadOnlyList<(int Pid, string Path)> Find(IReadOnlyCollection<string> names);
}

/// <summary>
/// Velopack's updater ends every process that runs from the install folder before it replaces it. So an update is never
/// installed while another copy of the app runs from the same folder, whatever its queue (Tools #89): that copy may be in
/// the middle of an import. The copies are found by their program's path, so an older version without a lock counts too.
/// </summary>
public sealed class InstallGuard
{
    private readonly IRunningProcesses _processes;
    private readonly int _pid;
    private readonly string _program;
    private readonly string _root;
    private readonly StringComparison _same;

    public InstallGuard(IRunningProcesses processes, string program, int pid, bool ignoreCase)
    {
        _processes = processes;
        _pid = pid;
        _program = program;
        _root = RootOf(program);
        _same = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    /// <summary>For this process, or null where there is no self-update.</summary>
    public static InstallGuard? ForThisProcess()
    {
        var program = OperatingSystem.IsMacOS() ? SystemProcesses.MacPath(Environment.ProcessId) ?? Environment.ProcessPath : Environment.ProcessPath;
        return program is null ? null : new InstallGuard(new SystemProcesses(), program, Environment.ProcessId, !OperatingSystem.IsLinux());
    }

    /// <summary>
    /// The folder the updater replaces: the app bundle on macOS (<c>X.app</c>), the folder around <c>current</c> on Windows,
    /// otherwise the program's own.
    /// </summary>
    public static string RootOf(string program)
    {
        var separators = new[] { '/', '\\' };
        var parts = program.Split(separators);
        for (var i = parts.Length - 2; i >= 0; i--)
        {
            if (parts[i].EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return program[..Length(parts, i)];
        }
        var folder = parts.Length - 2;
        if (folder > 0 && parts[folder].Equals("current", StringComparison.OrdinalIgnoreCase)) folder--;
        return folder < 0 ? "" : program[..Length(parts, folder)];
    }

    private static int Length(string[] parts, int last) => parts.Take(last + 1).Sum(p => p.Length) + last;

    /// <summary>Another copy of this app runs from the same install. A listing that fails counts as one: the update can wait.</summary>
    public bool OtherCopyRunning()
    {
        try
        {
            return _processes.Find([NameOf(_program)]).Any(p => p.Pid != _pid && Inside(p.Path) && FileOf(p.Path).Equals(FileOf(_program), _same));
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>The updater is at work in this install (<c>Update.exe</c>, <c>UpdateMac</c>).</summary>
    public bool UpdaterRunning()
    {
        try
        {
            return _processes.Find(["Update", "UpdateMac"]).Any(p => p.Pid != _pid && Inside(p.Path) && NameOf(p.Path) is var name && (name.Equals("Update", _same) || name.Equals("UpdateMac", _same)));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// A copy started while the updater works waits here, before it opens its queue: the updater ends what runs from the
    /// folder, and an import must not be running then.
    /// </summary>
    public bool WaitForUpdater(TimeSpan limit, Action<TimeSpan>? sleep = null)
    {
        sleep ??= Thread.Sleep;
        var step = TimeSpan.FromMilliseconds(500);
        for (var waited = TimeSpan.Zero; UpdaterRunning(); waited += step)
        {
            if (waited >= limit) return false;
            sleep(step);
        }
        return true;
    }

    private bool Inside(string path) => path.Length > _root.Length && path.StartsWith(_root, _same) && path[_root.Length] is '/' or '\\';

    private static string FileOf(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    private static string NameOf(string path)
    {
        var file = FileOf(path);
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }
}

/// <summary>What the system lists.</summary>
public sealed class SystemProcesses : IRunningProcesses
{
    public IReadOnlyList<(int Pid, string Path)> Find(IReadOnlyCollection<string> names)
    {
        if (OperatingSystem.IsWindows()) return Windows(names);
        if (OperatingSystem.IsMacOS()) return Mac();
        return Linux();
    }

    [SupportedOSPlatform("windows")]
    private static List<(int, string)> Windows(IReadOnlyCollection<string> names)
    {
        var found = new List<(int, string)>();
        foreach (var name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    // Not readable: another person's process, whose install is their own
                    if (WindowsPath(process.Id) is { } path) found.Add((process.Id, path));
                }
            }
        }
        return found;
    }

    private static string? WindowsPath(int pid)
    {
        const uint queryLimited = 0x1000;
        var handle = OpenProcess(queryLimited, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[32768];
            var length = buffer.Length;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref length) ? new string(buffer, 0, length) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static List<(int, string)> Mac()
    {
        var count = proc_listallpids(null, 0);
        var pids = new int[Math.Max(count, 0) + 256];
        count = proc_listallpids(pids, pids.Length * sizeof(int));
        if (count <= 0) throw new InvalidOperationException("The processes could not be listed");
        return pids.Take(count).Select(pid => (Pid: pid, Path: MacPath(pid))).Where(p => p.Path is not null).Select(p => (p.Pid, p.Path!)).ToList();
    }

    internal static string? MacPath(int pid)
    {
        var buffer = new byte[4096];
        var length = proc_pidpath(pid, buffer, (uint)buffer.Length);
        return length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null;
    }

    private static List<(int, string)> Linux()
    {
        var found = new List<(int, string)>();
        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            try
            {
                if (new FileInfo(Path.Combine(folder, "exe")).LinkTarget is { } path) found.Add((pid, path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone, or another person's
            }
        }
        return found;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, [Out] char[] name, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("libproc.dylib")]
    private static extern int proc_listallpids(int[]? buffer, int size);

    [DllImport("libproc.dylib")]
    private static extern int proc_pidpath(int pid, byte[] buffer, uint size);
}

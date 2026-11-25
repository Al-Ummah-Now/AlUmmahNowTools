using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AlUmmahNow.ToolsCore.Import.Sources;

/// <summary>Where a path really leads: the network path behind a mapped drive, and whether a path is on a share at all.</summary>
public interface INetworkPaths
{
    /// <summary>The network path (<c>\\server\share\…</c>) of a file on a mapped (or SUBST) drive, or null when there is none.</summary>
    string? Mapped(string path);

    bool IsNetwork(string path);
}

/// <summary>What <see cref="WindowsNetworkPaths"/> asks Windows about a drive ("Y:").</summary>
public interface IWindowsDrives
{
    /// <summary>QueryDosDevice: <c>\??\C:\folder</c> or <c>\??\UNC\server\share</c> for a SUBST drive</summary>
    string? DosDevice(string drive);
    /// <summary>GetDriveType: 1 no such drive here, 3 fixed, 4 network</summary>
    int DriveType(string drive);
    /// <summary>WNetGetUniversalName: the network path of a path on a mapped drive</summary>
    string? UniversalName(string path);
    /// <summary>WNetGetConnection: the share a drive is mapped to</summary>
    string? Connection(string drive);
    /// <summary>The share a remembered drive is mapped to, also one mapped only in this person's other (elevated) session</summary>
    string? Remembered(string drive);
}

/// <summary>
/// Windows: some files read fine through <c>\\server\share</c> and fail through the drive letter mapped to it (Win32 59),
/// and a drive mapped in an elevated session is not there for other apps (and the other way round), so mapped drives
/// are read through their network path. Drives of the Windows NFS client are included.
/// </summary>
public sealed class WindowsNetworkPaths : INetworkPaths
{
    private readonly IWindowsDrives _drives;

    public WindowsNetworkPaths(IWindowsDrives drives) => _drives = drives;

    public string? Mapped(string path)
    {
        if (!HasDrive(path)) return null;
        var drive = path[..2].ToUpperInvariant();
        var rest = path[2..].Replace('/', '\\');
        if (rest.Length > 0 && rest[0] != '\\') return null;
        // SUBST, which may lead to another drive, itself mapped
        for (var i = 0; i < 8; i++)
        {
            var target = _drives.DosDevice(drive);
            if (target is null || !target.StartsWith(@"\??\", StringComparison.Ordinal)) break;
            target = target[4..].TrimEnd('\\');
            if (target.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + target[4..] + rest;
            if (!HasDrive(target) || target[..2].Equals(drive, StringComparison.OrdinalIgnoreCase)) break;
            drive = target[..2].ToUpperInvariant();
            rest = target[2..] + rest;
        }
        var type = _drives.DriveType(drive);
        if (type == 3) return null;
        if (type == 4 && _drives.UniversalName(drive + (rest.Length == 0 ? "\\" : rest)) is { } whole && IsShare(whole)) return whole;
        var share = (type == 4 ? _drives.Connection(drive) : null) ?? (type is 1 or 4 ? _drives.Remembered(drive) : null);
        return share is not null && IsShare(share) ? share.TrimEnd('\\') + rest : null;
    }

    public bool IsNetwork(string path) => IsShare(path) || (HasDrive(path) && (_drives.DriveType(path[..2]) == 4 || Mapped(path) is not null));

    internal static bool IsShare(string path)
    {
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return true;
        return p.StartsWith(@"\\", StringComparison.Ordinal) && !p.StartsWith(@"\\?\", StringComparison.Ordinal) && !p.StartsWith(@"\\.\", StringComparison.Ordinal);
    }

    private static bool HasDrive(string path) => path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]);
}

[SupportedOSPlatform("windows")]
internal sealed class SystemDrives : IWindowsDrives
{
    private const int MoreData = 234;

    public string? DosDevice(string drive)
    {
        var buffer = new char[2048];
        var length = QueryDosDeviceW(drive, buffer, buffer.Length);
        return length == 0 ? null : Until0(buffer);
    }

    public int DriveType(string drive) => (int)GetDriveTypeW(drive[..2] + "\\");

    public string? UniversalName(string path)
    {
        var size = 2048;
        for (var i = 0; i < 2; i++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                // UNIVERSAL_NAME_INFO: one pointer to the name, which follows it in the buffer
                var result = WNetGetUniversalNameW(path, 1, buffer, ref size);
                if (result == 0) return Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer));
                if (result != MoreData) return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return null;
    }

    public string? Connection(string drive)
    {
        var buffer = new char[2048];
        var length = buffer.Length;
        return WNetGetConnectionW(drive, buffer, ref length) == 0 ? Until0(buffer) : null;
    }

    public string? Remembered(string drive)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Network\" + drive[..1]);
            return key?.GetValue("RemotePath") as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? Until0(char[] buffer)
    {
        var end = Array.IndexOf(buffer, '\0');
        return end <= 0 ? null : new string(buffer, 0, end);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int QueryDosDeviceW(string device, [Out] char[] target, int max);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDriveTypeW(string root);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WNetGetUniversalNameW(string path, int level, IntPtr buffer, ref int size);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WNetGetConnectionW(string local, [Out] char[] remote, ref int length);
}

/// <summary>macOS and Linux: a share is a mount, read by its own path; nothing to resolve.</summary>
public sealed class UnixNetworkPaths : INetworkPaths
{
    /// <summary>Mounts seen as shares: still shares after they drop off</summary>
    private readonly ConcurrentDictionary<string, bool> _shares = new(StringComparer.Ordinal);

    public string? Mapped(string path) => null;

    public bool IsNetwork(string path)
    {
        var root = SourceFileSystem.RootOf(path);
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                var network = new DriveInfo(path).DriveType == DriveType.Network;
                if (network && root is not null) _shares[root] = true;
                if (network) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Not there now: what it was is below
        }
        return root is not null && !Directory.Exists(root) && _shares.ContainsKey(root);
    }
}

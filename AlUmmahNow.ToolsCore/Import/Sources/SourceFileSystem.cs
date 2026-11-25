using System.Runtime.InteropServices;

namespace AlUmmahNow.ToolsCore.Import.Sources;

/// <param name="InCloud">Only a placeholder is here; reading it downloads it first (OneDrive, iCloud, Dropbox)</param>
public sealed record SourceStat(long Length, long ModifiedMs, bool IsLink = false, bool InCloud = false);

public sealed record OpenedSource(Stream Stream, SourceStat Stat);

/// <summary>The file calls <see cref="SourceAccess"/> makes, so tests can make them fail.</summary>
public interface ISourceFileSystem
{
    /// <exception cref="FileNotFoundException">No such file (and so on: the system's own errors, never swallowed)</exception>
    SourceStat Stat(string path);

    OpenedSource Open(string path);

    /// <summary>The share or drive the path is on is there: a missing file is then really missing.</summary>
    bool RootAnswers(string path);

    bool FolderExists(string path);
}

public sealed class SourceFileSystem : ISourceFileSystem
{
    private const FileAttributes CloudAttributes = FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

    public SourceStat Stat(string path)
    {
        // Throws what FileInfo.Exists would answer as "not there" (a share that dropped)
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0) throw new FileNotFoundException("Not a file");
        var info = new FileInfo(path);
        return new SourceStat(info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds(), info.LinkTarget is not null, IsInCloud(path, attributes));
    }

    public OpenedSource Open(string path)
    {
        // Other apps may keep the file open, even to write: its size and modified time are checked again at the end
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(stream.SafeFileHandle)).ToUnixTimeMilliseconds();
            return new OpenedSource(stream, new SourceStat(stream.Length, modified, InCloud: IsInCloud(path, File.GetAttributes(stream.SafeFileHandle))));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public bool RootAnswers(string path) => RootOf(path) is not { } root || Directory.Exists(root);

    public bool FolderExists(string path) => Directory.Exists(path);

    /// <summary>The share or drive: <c>\\server\share</c> or <c>Y:\</c> on Windows, <c>/Volumes/Name</c> on macOS; null when the path has none of its own.</summary>
    internal static string? RootOf(string path)
    {
        if (OperatingSystem.IsWindows()) return Path.GetPathRoot(path) is { Length: > 0 } root ? root : null;
        const string volumes = "/Volumes/";
        if (!OperatingSystem.IsMacOS() || !path.StartsWith(volumes, StringComparison.Ordinal)) return null;
        var end = path.IndexOf('/', volumes.Length);
        return end < 0 ? path : path[..end];
    }

    private static bool IsInCloud(string path, FileAttributes attributes) =>
        OperatingSystem.IsWindows() ? (attributes & CloudAttributes) != 0 : OperatingSystem.IsMacOS() && IsDataless(path);

    /// <summary>macOS: SF_DATALESS in the file's flags (struct stat's st_flags), which .NET does not tell.</summary>
    private static bool IsDataless(string path)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            var result = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? LStat64(path, buffer) : LStat(path, buffer);
            return result == 0 && (Marshal.ReadInt32(buffer, 116) & 0x40000000) != 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("libSystem.dylib", EntryPoint = "lstat")]
    private static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr buffer);

    [DllImport("libSystem.dylib", EntryPoint = "lstat$INODE64")]
    private static extern int LStat64([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr buffer);
}

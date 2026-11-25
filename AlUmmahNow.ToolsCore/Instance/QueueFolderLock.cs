using AlUmmahNow.ToolsCore.Import.Sources;

namespace AlUmmahNow.ToolsCore.Instance;

public enum LockAnswer
{
    Taken,
    /// <summary>Another copy of the app holds it</summary>
    Busy,
    /// <summary>The folder takes no lock (it cannot be written): nothing to be told from it</summary>
    Unavailable,
}

/// <summary>
/// One copy of the app per queue folder (Tools #89): a file in it held open for this process alone. The system lets go of
/// it when the process ends, however it ends, so nothing is left to clean up and a crash never locks anyone out. The file
/// itself stays: deleting it would let two copies hold two different files of one name.
/// </summary>
public sealed class QueueFolderLock : IDisposable
{
    public const string FileName = "tools.lock";
    private readonly FileStream _file;

    private QueueFolderLock(FileStream file) => _file = file;

    public static LockAnswer TryTake(string folder, out QueueFolderLock? held)
    {
        held = null;
        try
        {
            Directory.CreateDirectory(folder);
            held = new QueueFolderLock(new FileStream(Path.Combine(folder, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1));
            return LockAnswer.Taken;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ReadFailure.Of(ex).Code == "file_in_use" ? LockAnswer.Busy : LockAnswer.Unavailable;
        }
    }

    public void Dispose() => _file.Dispose();
}

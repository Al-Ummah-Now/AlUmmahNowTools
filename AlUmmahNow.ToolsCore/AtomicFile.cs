using System.Globalization;

namespace AlUmmahNow.ToolsCore;

/// <summary>
/// Writes a file whole or not at all: beside it first, under a name no other save has, of this process or another copy's
/// (Tools #89), then in its place. When that fails nothing is left beside it.
/// </summary>
internal static class AtomicFile
{
    private static long _saves;

    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-{Interlocked.Increment(ref _saves).ToString(CultureInfo.InvariantCulture)}.tmp";
        try
        {
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}

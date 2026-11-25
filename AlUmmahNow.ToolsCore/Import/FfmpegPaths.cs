namespace AlUmmahNow.ToolsCore.Import;

/// <summary>Where ffmpeg and ffprobe are: the paths saved on the Video tools page, else the ones found on this computer.</summary>
public static class FfmpegPaths
{
    public const string Section = "FFmpeg";
    public const string Ffmpeg = "FFmpegPath";
    public const string Ffprobe = "FFprobePath";

    /// <param name="detect">Looks for them on this computer</param>
    /// <param name="problem">Told why the saved paths could not be read; whatever it is, they are looked for</param>
    public static async Task<(string Ffmpeg, string Ffprobe)?> LocateAsync(ISettingsStore settings, Func<CancellationToken, Task<(string Ffmpeg, string Ffprobe)?>> detect, Action<Exception>? problem, CancellationToken ct)
    {
        try
        {
            if (settings.Get(Section, Ffmpeg) is { } ffmpeg && settings.Get(Section, Ffprobe) is { } ffprobe && File.Exists(ffmpeg) && File.Exists(ffprobe)) return (ffmpeg, ffprobe);
        }
        catch (Exception ex)
        {
            problem?.Invoke(ex);
        }
        ct.ThrowIfCancellationRequested();
        return await detect(ct).ConfigureAwait(false);
    }
}

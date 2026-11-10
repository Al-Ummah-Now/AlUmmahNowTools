namespace AlUmmahNow.Tests.Infrastructure;

/// <summary>
/// A test that runs the real ffmpeg / ffprobe. Skipped when either is not on PATH.
/// </summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public static bool Available { get; } = OnPath("ffmpeg") && OnPath("ffprobe");

    public FfmpegFactAttribute()
    {
        if (!Available)
        {
            Skip = "ffmpeg and ffprobe must be on PATH to run.";
        }
    }

    private static bool OnPath(string tool)
    {
        var names = OperatingSystem.IsWindows() ? new[] { tool + ".exe" } : new[] { tool };
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => names.Any(n => File.Exists(Path.Combine(dir, n))));
    }
}

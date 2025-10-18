using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AlUmmahNow.Data.Services;

/// <summary>
/// Interface for managing FFmpeg installation and configuration
/// </summary>
public interface IFFmpegManagerService
{
    /// <summary>
    /// Gets the current FFmpeg executable path
    /// </summary>
    string? FFmpegPath { get; }

    /// <summary>
    /// Gets the current FFprobe executable path
    /// </summary>
    string? FFprobePath { get; }

    /// <summary>
    /// Checks if FFmpeg is installed and accessible
    /// </summary>
    Task<bool> IsFFmpegInstalledAsync();

    /// <summary>
    /// Gets FFmpeg version information
    /// </summary>
    Task<string?> GetFFmpegVersionAsync();

    /// <summary>
    /// Verifies FFmpeg installation and returns detailed status
    /// </summary>
    Task<FFmpegStatus> VerifyFFmpegInstallationAsync();

    /// <summary>
    /// Sets custom FFmpeg binary paths
    /// </summary>
    void SetFFmpegPaths(string ffmpegPath, string ffprobePath);

    /// <summary>
    /// Attempts to auto-detect FFmpeg installation
    /// </summary>
    Task<bool> AutoDetectFFmpegAsync();

    /// <summary>
    /// Gets installation instructions for the current platform
    /// </summary>
    string GetInstallationInstructions();
}

/// <summary>
/// Service for managing FFmpeg installation and configuration
/// </summary>
public class FFmpegManagerService : IFFmpegManagerService
{
    private readonly ILogger<FFmpegManagerService> _logger;
    private string? _ffmpegPath;
    private string? _ffprobePath;

    public FFmpegManagerService(ILogger<FFmpegManagerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Try to auto-detect on initialization
        _ = AutoDetectFFmpegAsync();
    }

    public string? FFmpegPath => _ffmpegPath;
    public string? FFprobePath => _ffprobePath;

    public async Task<bool> IsFFmpegInstalledAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                await AutoDetectFFmpegAsync();
            }

            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                return false;
            }

            var result = await RunProcessAsync(_ffmpegPath, "-version", timeoutMs: 5000);
            return result.Success && result.Output.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking FFmpeg installation");
            return false;
        }
    }

    public async Task<string?> GetFFmpegVersionAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                await AutoDetectFFmpegAsync();
            }

            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                return null;
            }

            var result = await RunProcessAsync(_ffmpegPath, "-version", timeoutMs: 5000);
            if (result.Success && !string.IsNullOrEmpty(result.Output))
            {
                // Extract version from first line
                var lines = result.Output.Split('\n');
                if (lines.Length > 0)
                {
                    return lines[0].Trim();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting FFmpeg version");
            return null;
        }
    }

    public async Task<FFmpegStatus> VerifyFFmpegInstallationAsync()
    {
        var status = new FFmpegStatus();

        try
        {
            // Try auto-detect if not already set
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                status.AutoDetected = await AutoDetectFFmpegAsync();
            }

            // Check FFmpeg
            if (!string.IsNullOrEmpty(_ffmpegPath))
            {
                status.FFmpegPath = _ffmpegPath;
                status.FFmpegExists = File.Exists(_ffmpegPath);

                if (status.FFmpegExists)
                {
                    var ffmpegResult = await RunProcessAsync(_ffmpegPath, "-version", timeoutMs: 5000);
                    status.FFmpegWorks = ffmpegResult.Success;
                    status.FFmpegVersion = await GetFFmpegVersionAsync();
                }
            }

            // Check FFprobe
            if (!string.IsNullOrEmpty(_ffprobePath))
            {
                status.FFprobePath = _ffprobePath;
                status.FFprobeExists = File.Exists(_ffprobePath);

                if (status.FFprobeExists)
                {
                    var ffprobeResult = await RunProcessAsync(_ffprobePath, "-version", timeoutMs: 5000);
                    status.FFprobeWorks = ffprobeResult.Success;
                }
            }

            status.IsFullyInstalled = status.FFmpegWorks && status.FFprobeWorks;

            _logger.LogInformation("FFmpeg verification complete: {Status}", status.IsFullyInstalled ? "Installed" : "Not installed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying FFmpeg installation");
            status.ErrorMessage = ex.Message;
        }

        return status;
    }

    public void SetFFmpegPaths(string ffmpegPath, string ffprobePath)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;

        // Configure FFMpegCore to use these paths
        try
        {
            var globalOptionsType = Type.GetType("FFMpegCore.GlobalFFOptions, FFMpegCore");
            if (globalOptionsType != null)
            {
                var configProperty = globalOptionsType.GetProperty("Current");
                if (configProperty != null)
                {
                    var config = configProperty.GetValue(null);
                    if (config != null)
                    {
                        var binariesProperty = config.GetType().GetProperty("BinaryFolder");
                        if (binariesProperty != null)
                        {
                            var directory = Path.GetDirectoryName(ffmpegPath);
                            if (!string.IsNullOrEmpty(directory))
                            {
                                binariesProperty.SetValue(config, directory);
                                _logger.LogInformation("FFMpegCore configured to use: {Directory}", directory);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to configure FFMpegCore with custom paths");
        }

        _logger.LogInformation("FFmpeg paths set: FFmpeg={FFmpegPath}, FFprobe={FFprobePath}", ffmpegPath, ffprobePath);
    }

    public async Task<bool> AutoDetectFFmpegAsync()
    {
        try
        {
            _logger.LogInformation("Attempting to auto-detect FFmpeg installation");

            // Try common locations and PATH
            var possiblePaths = GetPossibleFFmpegPaths();

            foreach (var basePath in possiblePaths)
            {
                var ffmpegExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
                var ffprobeExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";

                var ffmpegPath = Path.Combine(basePath, ffmpegExe);
                var ffprobePath = Path.Combine(basePath, ffprobeExe);

                if (File.Exists(ffmpegPath) && File.Exists(ffprobePath))
                {
                    // Verify they work
                    var result = await RunProcessAsync(ffmpegPath, "-version", timeoutMs: 5000);
                    if (result.Success)
                    {
                        SetFFmpegPaths(ffmpegPath, ffprobePath);
                        _logger.LogInformation("Auto-detected FFmpeg at: {Path}", ffmpegPath);
                        return true;
                    }
                }
            }

            // Try finding in PATH
            var pathFFmpeg = await FindInPathAsync("ffmpeg");
            var pathFFprobe = await FindInPathAsync("ffprobe");

            if (!string.IsNullOrEmpty(pathFFmpeg) && !string.IsNullOrEmpty(pathFFprobe))
            {
                SetFFmpegPaths(pathFFmpeg, pathFFprobe);
                _logger.LogInformation("Found FFmpeg in PATH: {Path}", pathFFmpeg);
                return true;
            }

            _logger.LogWarning("Could not auto-detect FFmpeg installation");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during FFmpeg auto-detection");
            return false;
        }
    }

    public string GetInstallationInstructions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return @"To install FFmpeg on Windows:

1. Using Chocolatey (Recommended):
   choco install ffmpeg

2. Using Winget:
   winget install ffmpeg

3. Manual Installation:
   - Download FFmpeg from: https://www.gyan.dev/ffmpeg/builds/
   - Extract the archive
   - Add the 'bin' folder to your PATH environment variable
   - Or specify the full path to ffmpeg.exe in this application

4. Using Scoop:
   scoop install ffmpeg";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return @"To install FFmpeg on Linux:

Ubuntu/Debian:
   sudo apt update
   sudo apt install ffmpeg

Fedora:
   sudo dnf install ffmpeg

Arch Linux:
   sudo pacman -S ffmpeg

Or build from source: https://ffmpeg.org/download.html";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return @"To install FFmpeg on macOS:

Using Homebrew (Recommended):
   brew install ffmpeg

Using MacPorts:
   sudo port install ffmpeg

Or download from: https://ffmpeg.org/download.html";
        }

        return "Please visit https://ffmpeg.org/download.html for installation instructions.";
    }

    private List<string> GetPossibleFFmpegPaths()
    {
        var paths = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            paths.Add(@"C:\Program Files\ffmpeg\bin");
            paths.Add(@"C:\ffmpeg\bin");
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg", "bin"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            paths.Add("/usr/bin");
            paths.Add("/usr/local/bin");
            paths.Add("/opt/ffmpeg/bin");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            paths.Add("/usr/local/bin");
            paths.Add("/opt/homebrew/bin");
            paths.Add("/opt/local/bin");
        }

        return paths;
    }

    private async Task<string?> FindInPathAsync(string executable)
    {
        try
        {
            var extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
            var fullExecutable = executable + extension;

            var pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar))
            {
                return null;
            }

            var paths = pathVar.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, fullExecutable);
                if (File.Exists(fullPath))
                {
                    // Verify it works
                    var result = await RunProcessAsync(fullPath, "-version", timeoutMs: 5000);
                    if (result.Success)
                    {
                        return fullPath;
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, int timeoutMs = 10000)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(timeoutMs));

            if (!completed)
            {
                try { process.Kill(); } catch { }
                return new ProcessResult { Success = false, Output = "Process timed out" };
            }

            var exitCode = process.ExitCode;
            var outputText = output.ToString() + error.ToString();

            return new ProcessResult
            {
                Success = exitCode == 0,
                ExitCode = exitCode,
                Output = outputText
            };
        }
        catch (Exception ex)
        {
            return new ProcessResult
            {
                Success = false,
                Output = $"Error: {ex.Message}"
            };
        }
    }

    private class ProcessResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
    }
}

/// <summary>
/// Status information for FFmpeg installation
/// </summary>
public class FFmpegStatus
{
    public bool IsFullyInstalled { get; set; }
    public bool AutoDetected { get; set; }

    public string? FFmpegPath { get; set; }
    public bool FFmpegExists { get; set; }
    public bool FFmpegWorks { get; set; }
    public string? FFmpegVersion { get; set; }

    public string? FFprobePath { get; set; }
    public bool FFprobeExists { get; set; }
    public bool FFprobeWorks { get; set; }

    public string? ErrorMessage { get; set; }
}

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AlUmmahNow.DataImporter.Services
{
    /// <summary>
    /// Manages the FileScanService console application
    /// </summary>
    public class FileScanServiceManager
    {
        private readonly ILogger<FileScanServiceManager> _logger;
        private Process? _fileScanProcess;

        public FileScanServiceManager(ILogger<FileScanServiceManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Run the FileScan service in daemon mode
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Exit code</returns>
        public async Task<int> RunDaemonAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Find the FileScanService executable
                var fileScanServicePath = FindFileScanServiceExecutable();
                if (string.IsNullOrEmpty(fileScanServicePath))
                {
                    Console.WriteLine("? AlUmmahNow.FileScanService executable not found.");
                    Console.WriteLine("Please build the solution first.");
                    return 1;
                }

                Console.WriteLine($"Starting FileScan service from: {fileScanServicePath}");

                // Start the FileScanService in daemon mode
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileScanServicePath,
                    Arguments = "--daemon",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false
                };

                _fileScanProcess = new Process { StartInfo = startInfo };

                // Forward output from the service
                _fileScanProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[FileScan] {e.Data}");
                    }
                };

                _fileScanProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[FileScan Error] {e.Data}");
                    }
                };

                _fileScanProcess.Start();
                _fileScanProcess.BeginOutputReadLine();
                _fileScanProcess.BeginErrorReadLine();

                // Handle Ctrl+C gracefully
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    Console.WriteLine("\nShutdown requested...");
                    
                    try
                    {
                        if (!_fileScanProcess.HasExited)
                        {
                            _fileScanProcess.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error stopping FileScan service");
                    }
                };

                // Wait for the process to exit or cancellation
                await WaitForProcessOrCancellation(_fileScanProcess, cancellationToken);

                return _fileScanProcess.ExitCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running FileScan service in daemon mode");
                Console.WriteLine($"? Error running daemon mode: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Find the FileScanService executable
        /// </summary>
        /// <returns>Path to executable or null if not found</returns>
        private string? FindFileScanServiceExecutable()
        {
            // Look for the executable in common build output directories
            var currentDirectory = Environment.CurrentDirectory;
            var possiblePaths = new[]
            {
                // Same directory (if published together)
                Path.Combine(currentDirectory, "AlUmmahNow.FileScanService.exe"),
                
                // Relative to current project (development)
                Path.Combine(currentDirectory, "..", "AlUmmahNow.FileScanService", "bin", "Debug", "net8.0", "AlUmmahNow.FileScanService.exe"),
                Path.Combine(currentDirectory, "..", "AlUmmahNow.FileScanService", "bin", "Release", "net8.0", "AlUmmahNow.FileScanService.exe"),
                
                // From DataImporter bin directory to FileScanService bin
                Path.Combine(Path.GetDirectoryName(currentDirectory) ?? "", "AlUmmahNow.FileScanService", "bin", "Debug", "net8.0", "AlUmmahNow.FileScanService.exe"),
                Path.Combine(Path.GetDirectoryName(currentDirectory) ?? "", "AlUmmahNow.FileScanService", "bin", "Release", "net8.0", "AlUmmahNow.FileScanService.exe"),
                
                // Look in PATH
                "AlUmmahNow.FileScanService.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }

            // Try to find using dotnet run
            try
            {
                var solutionDir = FindSolutionDirectory();
                if (!string.IsNullOrEmpty(solutionDir))
                {
                    var projectPath = Path.Combine(solutionDir, "AlUmmahNow.FileScanService", "AlUmmahNow.FileScanService.csproj");
                    if (File.Exists(projectPath))
                    {
                        // Return a command that can be used with dotnet run
                        return $"dotnet|run|--project|{projectPath}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding FileScan service via dotnet run");
            }

            return null;
        }

        /// <summary>
        /// Find the solution directory by looking for .sln files
        /// </summary>
        /// <returns>Solution directory path or null</returns>
        private string? FindSolutionDirectory()
        {
            var currentDir = new DirectoryInfo(Environment.CurrentDirectory);

            while (currentDir != null)
            {
                if (currentDir.GetFiles("*.sln").Any())
                {
                    return currentDir.FullName;
                }
                currentDir = currentDir.Parent;
            }

            return null;
        }

        /// <summary>
        /// Wait for process to exit or cancellation token
        /// </summary>
        private async Task WaitForProcessOrCancellation(Process process, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            // Register cancellation
            cancellationToken.Register(() => tcs.TrySetCanceled());

            // Register process exit
            process.Exited += (sender, e) => tcs.TrySetResult(true);
            process.EnableRaisingEvents = true;

            // If process has already exited, complete immediately
            if (process.HasExited)
            {
                return;
            }

            try
            {
                await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                // Cancellation requested - kill the process
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error killing FileScan service process");
                    }
                }
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (_fileScanProcess != null && !_fileScanProcess.HasExited)
                {
                    _fileScanProcess.Kill();
                    _fileScanProcess.WaitForExit(5000); // Wait up to 5 seconds
                }
                _fileScanProcess?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing FileScanServiceManager");
            }
        }
    }
}
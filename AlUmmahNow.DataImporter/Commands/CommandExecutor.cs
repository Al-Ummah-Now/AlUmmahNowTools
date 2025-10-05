using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.DataImporter.Services;

namespace AlUmmahNow.DataImporter.Commands
{
    /// <summary>
    /// Service responsible for executing DataImporter commands via SignalR
    /// </summary>
    public class CommandExecutor
    {
        private readonly FileScanSignalRClient _signalRClient;

        public CommandExecutor(FileScanSignalRClient signalRClient)
        {
            _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
        }

        /// <summary>
        /// List all scan sessions
        /// </summary>
        /// <returns>Exit code (0 for success, 1 for error)</returns>
        public async Task<int> ListScanSessionsAsync()
        {
            try
            {
                var sessions = await _signalRClient.GetAllSessionsAsync();
                if (sessions.Count == 0)
                {
                    Console.WriteLine("No scan sessions found.");
                    return 0;
                }

                DisplaySessionsTable(sessions);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing sessions: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Get status of a specific scan session
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <returns>Exit code (0 for success, 1 for error)</returns>
        public async Task<int> GetSessionStatusAsync(Guid sessionId)
        {
            try
            {
                var status = await _signalRClient.GetStatusAsync(sessionId);
                DisplaySessionDetails(status);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting session status: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Start a new scan session
        /// </summary>
        /// <param name="folderPath">Folder path to scan</param>
        /// <param name="scanName">Name for the scan session (unused with SignalR)</param>
        /// <param name="waitForCompletion">Whether to wait for scan completion</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Exit code (0 for success, 1 for error)</returns>
        public async Task<int> StartScanAsync(string folderPath, string scanName, bool waitForCompletion, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine($"Starting scan of folder: {folderPath}");
                var sessionId = await _signalRClient.StartScanAsync(folderPath);

                Console.WriteLine($"? Scan session created: {sessionId}");
                Console.WriteLine($"Path: {folderPath}");

                if (!waitForCompletion)
                {
                    DisplayNextStepsInfo(sessionId);
                    return 0;
                }

                return await WaitForScanCompletionAsync(sessionId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\nOperation cancelled by user.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting scan: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Start import for a completed scan session
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <returns>Exit code (0 for success, 1 for error)</returns>
        public async Task<int> StartImportAsync(Guid sessionId)
        {
            try
            {
                // Validate session status
                var status = await _signalRClient.GetStatusAsync(sessionId);
                
                if (status.Status != "Completed")
                {
                    Console.WriteLine($"Error: Scan session must be completed before import. Current status: {status.Status}");
                    return 1;
                }

                Console.WriteLine($"Starting import for scan session: {sessionId}");
                Console.WriteLine($"Session has {status.FilesScanned} files available");
                
                // Note: Import functionality would need to be added to the SignalR hub
                Console.WriteLine("Import functionality via SignalR is not yet implemented.");
                Console.WriteLine("This will be added in a future update.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting import: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Get import status for a scan session
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <returns>Exit code (0 for success, 1 for error)</returns>
        public async Task<int> GetImportStatusAsync(Guid sessionId)
        {
            try
            {
                // This would require additional SignalR hub methods
                Console.WriteLine("Import status via SignalR is not yet implemented.");
                Console.WriteLine("This will be added in a future update.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting import status: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Check if the SignalR service is available
        /// </summary>
        /// <returns>True if available, false otherwise</returns>
        public bool IsServiceAvailable()
        {
            return _signalRClient.IsConnected;
        }

        #region Private Display Methods

        private void DisplaySessionsTable(List<ScanSessionDto> sessions)
        {
            Console.WriteLine($"{"ID",-36} {"Path",-30} {"Status",-12} {"Files",-8} {"Created",-20}");
            Console.WriteLine(new string('-', 115));

            foreach (var session in sessions)
            {
                var path = session.RootPath.Length > 28 ? "..." + session.RootPath.Substring(session.RootPath.Length - 25) : session.RootPath;
                var created = session.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                Console.WriteLine($"{session.Id,-36} {path,-30} {session.Status,-12} {session.FilesScanned,-8} {created,-20}");
            }
        }

        private void DisplaySessionDetails(ScanStatusDto status)
        {
            Console.WriteLine($"Session ID: {status.SessionId}");
            Console.WriteLine($"Status: {status.Status}");
            Console.WriteLine($"Root Path: {status.RootPath}");
            Console.WriteLine($"Progress: {status.ProgressPercentage:F1}%");
            
            if (!string.IsNullOrEmpty(status.CurrentOperation))
                Console.WriteLine($"Current Operation: {status.CurrentOperation}");
            
            Console.WriteLine($"Files Found: {status.FilesScanned}");
            Console.WriteLine($"Total Size: {FileFormatter.FormatFileSize(status.TotalSize)}");
        }

        private void DisplayNextStepsInfo(Guid sessionId)
        {
            Console.WriteLine();
            Console.WriteLine("Use the following commands to monitor progress:");
            Console.WriteLine($"  --status {sessionId}");
            Console.WriteLine($"  --start-import {sessionId}  (after scan completes)");
        }

        private async Task<int> WaitForScanCompletionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            Console.WriteLine();
            Console.WriteLine("Waiting for scan completion (real-time updates)...");
            
            var startTime = DateTime.UtcNow;
            var lastProgressUpdate = DateTime.UtcNow;

            try
            {
                var finalStatus = await _signalRClient.WaitForScanCompletionAsync(sessionId, progress =>
                {
                    // Only update console every 500ms to avoid flickering
                    if ((DateTime.UtcNow - lastProgressUpdate).TotalMilliseconds > 500)
                    {
                        var elapsed = DateTime.UtcNow - startTime;
                        Console.Write($"\rProgress: {progress.ProgressPercentage:F1}% | Files: {progress.FilesScanned} | Elapsed: {elapsed:mm\\:ss}");

                        if (!string.IsNullOrEmpty(progress.CurrentOperation))
                        {
                            var operation = progress.CurrentOperation.Length > 50 
                                ? progress.CurrentOperation.Substring(0, 47) + "..." 
                                : progress.CurrentOperation;
                            Console.Write($" | {operation}");
                        }
                        
                        lastProgressUpdate = DateTime.UtcNow;
                    }
                }, cancellationToken);

                Console.WriteLine();

                if (finalStatus.Status == "Completed")
                {
                    DisplayScanCompletedInfo(finalStatus, sessionId);
                    return 0;
                }
                else
                {
                    Console.WriteLine($"Scan {finalStatus.Status.ToLower()}");
                    return 1;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n? Scan monitoring cancelled.");
                throw;
            }
        }

        private void DisplayScanCompletedInfo(ScanStatusDto finalStatus, Guid sessionId)
        {
            Console.WriteLine($"? Scan completed successfully!");
            Console.WriteLine($"Total files found: {finalStatus.FilesScanned}");
            Console.WriteLine($"Total size: {FileFormatter.FormatFileSize(finalStatus.TotalSize)}");
            Console.WriteLine();
            Console.WriteLine($"To start import, run: --start-import {sessionId}");
        }

        #endregion
    }

    /// <summary>
    /// Utility class for file formatting operations
    /// </summary>
    public static class FileFormatter
    {
        /// <summary>
        /// Format bytes to human-readable file size
        /// </summary>
        /// <param name="bytes">Size in bytes</param>
        /// <returns>Formatted file size string</returns>
        public static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:F1} {sizes[order]}";
        }
    }
}
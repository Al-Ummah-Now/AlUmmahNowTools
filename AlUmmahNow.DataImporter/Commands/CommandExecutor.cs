using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.DataImporter.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AlUmmahNow.DataImporter.Commands
{
    /// <summary>
    /// Service responsible for executing DataImporter commands via direct database access and console service calls
    /// </summary>
    public class CommandExecutor
    {
        private readonly ImportDbContext _dbContext;
        private readonly ILogger<CommandExecutor> _logger;
        private readonly FileScanSignalRClient _signalRClient;

        public CommandExecutor(ImportDbContext dbContext, ILogger<CommandExecutor> logger, FileScanSignalRClient signalRClient)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _signalRClient = signalRClient;
        }

        /// <summary>
        /// List all scan sessions
        /// </summary>
        /// <returns>Exit code (0 for success, 1 for error)</returns>
        public async Task<int> ListScanSessionsAsync()
        {
            try
            {
                var sessions = await _dbContext.ScanSessions
                    .OrderByDescending(s => s.CreatedAt)
                    .Take(20)
                    .ToListAsync();

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
                _logger.LogError(ex, "Error listing scan sessions");
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
                Console.WriteLine($"Connecting to FileScan service...");
                
                // Connect to SignalR service
                var connected = await _signalRClient.ConnectAsync();
                if (!connected)
                {
                    // Fallback to database if SignalR is not available
                    Console.WriteLine("? SignalR service not available, checking database...");
                    return await GetSessionStatusFromDatabaseAsync(sessionId);
                }

                // Get status via SignalR
                var status = await _signalRClient.GetStatusAsync(sessionId);

                Console.WriteLine($"Session ID: {status.SessionId}");
                Console.WriteLine($"Status: {status.Status}");
                Console.WriteLine($"Path: {status.RootPath}");
                Console.WriteLine($"Progress: {status.ProgressPercentage:F1}%");
                Console.WriteLine($"Files Scanned: {status.FilesScanned}");
                Console.WriteLine($"Current Operation: {status.CurrentOperation}");

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session status via SignalR");
                Console.WriteLine($"? SignalR error: {ex.Message}");
                Console.WriteLine("Trying database fallback...");
                return await GetSessionStatusFromDatabaseAsync(sessionId);
            }
            finally
            {
                await _signalRClient.DisconnectAsync();
            }
        }

        /// <summary>
        /// Get session status from database as fallback
        /// </summary>
        private async Task<int> GetSessionStatusFromDatabaseAsync(Guid sessionId)
        {
            try
            {
                var session = await _dbContext.ScanSessions.FindAsync(sessionId);
                if (session == null)
                {
                    Console.WriteLine($"? Session {sessionId} not found.");
                    return 1;
                }

                Console.WriteLine($"Session ID: {session.Id}");
                Console.WriteLine($"Status: {session.Status}");
                Console.WriteLine($"Path: {session.RootPath}");
                Console.WriteLine($"Created: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"Started: {session.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not started"}");
                Console.WriteLine($"Completed: {session.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not completed"}");
                Console.WriteLine($"Progress: {session.ProgressPercentage:F1}%");
                Console.WriteLine($"Files Found: {session.FilesFound}");
                Console.WriteLine($"Directories Processed: {session.DirectoriesProcessed}/{session.TotalDirectories}");
                Console.WriteLine($"Current Operation: {session.CurrentOperation ?? "None"}");
                
                if (!string.IsNullOrEmpty(session.ErrorMessage))
                {
                    Console.WriteLine($"Error: {session.ErrorMessage}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session status from database");
                Console.WriteLine($"? Error getting session status: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Start a new scan session
        /// </summary>
        /// <param name="folderPath">Folder path to scan</param>
        /// <param name="scanName">Name for the scan session (currently unused)</param>
        /// <param name="waitForCompletion">Whether to wait for scan completion</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Exit code (0 for success, 1 for error)</returns>
        public async Task<int> StartScanAsync(string folderPath, string scanName, bool waitForCompletion, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine($"Connecting to FileScan service...");
                
                // Connect to SignalR service
                var connected = await _signalRClient.ConnectAsync();
                if (!connected)
                {
                    Console.WriteLine("? Failed to connect to FileScan service. Make sure the service is running.");
                    return 1;
                }

                Console.WriteLine($"? Connected to FileScan service");
                Console.WriteLine($"Starting scan of folder: {folderPath}");

                Guid sessionId = Guid.Empty;
                var scanCompleted = false;
                var scanFailed = false;
                string? errorMessage = null;

                // Subscribe to events if waiting for completion
                if (waitForCompletion)
                {
                    _signalRClient.ScanStarted += (sender, id) =>
                    {
                        sessionId = id;
                        Console.WriteLine($"? Scan started with session ID: {sessionId}");
                    };

                    _signalRClient.ScanProgressReceived += (sender, progress) =>
                    {
                        if (progress.SessionId == sessionId)
                        {
                            Console.WriteLine($"Progress: {progress.ProgressPercentage:F1}% - {progress.CurrentOperation}");
                            if (!string.IsNullOrEmpty(progress.CurrentFile))
                            {
                                Console.WriteLine($"  Current file: {Path.GetFileName(progress.CurrentFile)}");
                            }
                        }
                    };

                    _signalRClient.ScanCompleted += (sender, id) =>
                    {
                        if (id == sessionId)
                        {
                            scanCompleted = true;
                            Console.WriteLine($"? Scan completed successfully");
                        }
                    };

                    _signalRClient.ScanFailed += (sender, args) =>
                    {
                        if (args.sessionId == sessionId)
                        {
                            scanFailed = true;
                            errorMessage = args.error;
                            Console.WriteLine($"? Scan failed: {args.error}");
                        }
                    };
                }

                // Start the scan
                sessionId = await _signalRClient.StartScanAsync(folderPath, true);
                Console.WriteLine($"? Scan started with session ID: {sessionId}");

                if (!waitForCompletion)
                {
                    Console.WriteLine($"Use --status {sessionId} to check progress");
                    return 0;
                }

                // Wait for completion
                Console.WriteLine("Waiting for scan to complete...");
                var timeout = TimeSpan.FromMinutes(30); // 30 minute timeout
                var stopwatch = Stopwatch.StartNew();

                while (!scanCompleted && !scanFailed && !cancellationToken.IsCancellationRequested)
                {
                    if (stopwatch.Elapsed > timeout)
                    {
                        Console.WriteLine("? Scan timed out after 30 minutes");
                        break;
                    }

                    await Task.Delay(1000, cancellationToken);
                }

                if (scanCompleted)
                {
                    // Get final status
                    var finalStatus = await _signalRClient.GetStatusAsync(sessionId);
                    Console.WriteLine($"? Scan completed: {finalStatus.FilesScanned} files found");
                    return 0;
                }
                else if (scanFailed)
                {
                    Console.WriteLine($"? Scan failed: {errorMessage}");
                    return 1;
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("? Scan cancelled by user");
                    await _signalRClient.CancelScanAsync(sessionId);
                    return 1;
                }
                else
                {
                    Console.WriteLine("? Scan timed out");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting scan");
                Console.WriteLine($"? Error starting scan: {ex.Message}");
                return 1;
            }
            finally
            {
                await _signalRClient.DisconnectAsync();
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
                // Check if session exists and is completed
                var session = await _dbContext.ScanSessions.FindAsync(sessionId);
                if (session == null)
                {
                    Console.WriteLine($"? Session {sessionId} not found.");
                    return 1;
                }

                if (session.Status != ScanStatus.Completed)
                {
                    Console.WriteLine($"? Session {sessionId} is not completed (Status: {session.Status}).");
                    Console.WriteLine("Only completed scans can be imported.");
                    return 1;
                }

                Console.WriteLine($"? Starting import for session {sessionId}");
                Console.WriteLine($"Path: {session.RootPath}");
                Console.WriteLine($"Files found: {session.FilesFound}");

                // TODO: Implement actual import logic here
                // This would typically involve:
                // 1. Reading the scanned files from the database
                // 2. Starting the video import process
                // 3. Using the VideoImporter from AlUmmahNow.Importer
                
                Console.WriteLine("? Import functionality not yet implemented");
                Console.WriteLine("This would start the video import process for the scanned files");

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting import");
                Console.WriteLine($"? Error starting import: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Get import status for a session
        /// </summary>
        public async Task<int> GetImportStatusAsync(Guid sessionId)
        {
            try
            {
                // Check if session exists
                var session = await _dbContext.ScanSessions.FindAsync(sessionId);
                if (session == null)
                {
                    Console.WriteLine($"? Session {sessionId} not found.");
                    return 1;
                }

                Console.WriteLine($"Session ID: {sessionId}");
                Console.WriteLine($"Scan Status: {session.Status}");
                Console.WriteLine($"Files Found: {session.FilesFound}");

                // TODO: Check for import sessions related to this scan session
                // This would involve looking up ImportSession records
                Console.WriteLine("? Import status tracking not yet implemented");

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting import status");
                Console.WriteLine($"? Error getting import status: {ex.Message}");
                return 1;
            }
        }

        #region Private Display Methods

        private void DisplaySessionsTable(List<ScanSession> sessions)
        {
            Console.WriteLine($"{"ID",-36} {"Path",-30} {"Status",-12} {"Files",-8} {"Created",-20}");
            Console.WriteLine(new string('-', 115));

            foreach (var session in sessions)
            {
                var path = session.RootPath.Length > 28 ? "..." + session.RootPath.Substring(session.RootPath.Length - 25) : session.RootPath;
                var created = session.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                Console.WriteLine($"{session.Id,-36} {path,-30} {session.Status,-12} {session.FilesFound,-8} {created,-20}");
            }
        }

        private void DisplaySessionDetails(ScanSession session)
        {
            Console.WriteLine($"Session ID: {session.Id}");
            Console.WriteLine($"Status: {session.Status}");
            Console.WriteLine($"Root Path: {session.RootPath}");
            Console.WriteLine($"Created: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Started: {session.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not started"}");
            Console.WriteLine($"Completed: {session.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not completed"}");
            Console.WriteLine($"Progress: {session.ProgressPercentage:F1}%");
            
            if (!string.IsNullOrEmpty(session.CurrentOperation))
                Console.WriteLine($"Current Operation: {session.CurrentOperation}");
            
            Console.WriteLine($"Files Found: {session.FilesFound}");
            Console.WriteLine($"Directories Processed: {session.DirectoriesProcessed}/{session.TotalDirectories}");
            
            if (!string.IsNullOrEmpty(session.ErrorMessage))
            {
                Console.WriteLine($"Error: {session.ErrorMessage}");
            }
        }

        private void DisplayImportStatus(ScanSession session, List<ScannedVideoFile> files)
        {
            Console.WriteLine($"Import Status for Session: {session.Id}");
            Console.WriteLine($"Scan Status: {session.Status}");
            
            var statusGroups = files.GroupBy(f => f.ImportStatus).ToList();
            foreach (var group in statusGroups)
            {
                Console.WriteLine($"{group.Key}: {group.Count()} files");
            }

            if (files.Any(f => f.ImportStatus == FileImportStatus.Uploading || 
                              f.ImportStatus == FileImportStatus.Pending))
            {
                Console.WriteLine("\nFiles currently being processed:");
                var activeFiles = files.Where(f => f.ImportStatus == FileImportStatus.Uploading || 
                                                  f.ImportStatus == FileImportStatus.Pending)
                                      .Take(5);
                foreach (var file in activeFiles)
                {
                    Console.WriteLine($"  {file.FileName} - {file.ImportStatus}");
                }
            }
        }

        private void DisplayNextStepsInfo(Guid sessionId)
        {
            Console.WriteLine();
            Console.WriteLine("Use the following commands to monitor progress:");
            Console.WriteLine($"  --status {sessionId}");
            Console.WriteLine($"  --start-import {sessionId}  (after scan completes)");
        }

        private void DisplayScanCompletedInfo(ScanSession session, Guid sessionId)
        {
            Console.WriteLine($"? Scan completed successfully!");
            Console.WriteLine($"Total files found: {session.FilesFound}");
            Console.WriteLine($"Directories processed: {session.DirectoriesProcessed}");
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
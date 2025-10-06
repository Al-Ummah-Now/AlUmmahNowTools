using Microsoft.AspNetCore.SignalR;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.Data.Database;
using Microsoft.EntityFrameworkCore;
using AlUmmahNow.FileScanService.Services;

namespace AlUmmahNow.FileScanService.Hubs
{
    /// <summary>
    /// SignalR Hub for real-time file scanning communication
    /// </summary>
    public class FileScanHub : Hub
    {
        private readonly ILogger<FileScanHub> _logger;
        private readonly ImportDbContext _dbContext;

        public FileScanHub(ILogger<FileScanHub> logger, ImportDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        /// <summary>
        /// Client connects to hub
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Client disconnects from hub
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Join a scan session group for targeted updates
        /// </summary>
        /// <param name="sessionId">Scan session ID</param>
        public async Task JoinScanSession(string sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"scan-{sessionId}");
            _logger.LogDebug("Client {ConnectionId} joined scan session {SessionId}", Context.ConnectionId, sessionId);
        }

        /// <summary>
        /// Leave a scan session group
        /// </summary>
        /// <param name="sessionId">Scan session ID</param>
        public async Task LeaveScanSession(string sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"scan-{sessionId}");
            _logger.LogDebug("Client {ConnectionId} left scan session {SessionId}", Context.ConnectionId, sessionId);
        }

        /// <summary>
        /// Start a new scan
        /// </summary>
        /// <param name="folderPath">Path to scan</param>
        /// <param name="includeSubdirectories">Whether to include subdirectories</param>
        /// <returns>Session ID of the started scan</returns>
        public async Task<Guid> StartScan(string folderPath, bool includeSubdirectories = true)
        {
            try
            {
                _logger.LogInformation("Starting scan request from {ConnectionId} for path: {FolderPath}", 
                    Context.ConnectionId, folderPath);

                // Validate path
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    throw new ArgumentException($"Invalid or non-existent folder path: {folderPath}");
                }

                // Create scan session
                var scanSession = new ScanSession
                {
                    Id = Guid.NewGuid(),
                    RootPath = folderPath,
                    Name = $"Scan {Path.GetFileName(folderPath)} - {DateTime.Now:yyyy-MM-dd HH:mm}",
                    Status = ScanStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    CurrentOperation = "Initializing scan...",
                    PathHash = CalculatePathHash(folderPath)
                };

                _dbContext.ScanSessions.Add(scanSession);
                await _dbContext.SaveChangesAsync();

                // Join this session group for updates
                await Groups.AddToGroupAsync(Context.ConnectionId, $"scan-{scanSession.Id}");

                // Signal that scan has been queued
                await Clients.Group($"scan-{scanSession.Id}").SendAsync("ScanStarted", scanSession.Id, folderPath);

                _logger.LogInformation("Scan session {SessionId} created and queued for {FolderPath}", 
                    scanSession.Id, folderPath);

                return scanSession.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting scan for {FolderPath}", folderPath);
                throw new HubException($"Failed to start scan: {ex.Message}");
            }
        }

        /// <summary>
        /// Get status of a scan session
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <returns>Scan status information</returns>
        public async Task<ScanStatusDto> GetScanStatus(Guid sessionId)
        {
            try
            {
                var session = await _dbContext.ScanSessions.FindAsync(sessionId);
                if (session == null)
                {
                    throw new ArgumentException($"Scan session {sessionId} not found");
                }

                return new ScanStatusDto
                {
                    SessionId = session.Id,
                    Status = session.Status.ToString(),
                    RootPath = session.RootPath,
                    FilesScanned = session.FilesFound,
                    TotalSize = session.TotalSize,
                    ProgressPercentage = session.ProgressPercentage,
                    CurrentOperation = session.CurrentOperation ?? "Unknown"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scan status for {SessionId}", sessionId);
                throw new HubException($"Failed to get scan status: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all scan sessions
        /// </summary>
        /// <returns>List of scan sessions</returns>
        public async Task<List<ScanSessionDto>> GetAllSessions()
        {
            try
            {
                var sessions = await _dbContext.ScanSessions
                    .OrderByDescending(s => s.CreatedAt)
                    .Take(50) // Limit to recent sessions
                    .Select(s => new ScanSessionDto
                    {
                        Id = s.Id,
                        RootPath = s.RootPath,
                        Status = s.Status.ToString(),
                        CreatedAt = s.CreatedAt,
                        CompletedAt = s.CompletedAt,
                        FilesScanned = s.FilesFound
                    })
                    .ToListAsync();

                return sessions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all sessions");
                throw new HubException($"Failed to get sessions: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel a scan
        /// </summary>
        /// <param name="sessionId">Session ID to cancel</param>
        public async Task CancelScan(Guid sessionId)
        {
            try
            {
                var session = await _dbContext.ScanSessions.FindAsync(sessionId);
                if (session == null)
                {
                    throw new ArgumentException($"Scan session {sessionId} not found");
                }

                if (session.Status == ScanStatus.Completed || session.Status == ScanStatus.Failed || session.Status == ScanStatus.Cancelled)
                {
                    throw new InvalidOperationException($"Cannot cancel scan in status: {session.Status}");
                }

                session.Status = ScanStatus.Cancelled;
                session.CompletedAt = DateTime.UtcNow;
                session.CurrentOperation = "Cancelled by user";
                await _dbContext.SaveChangesAsync();

                // Notify all clients in this session group
                await Clients.Group($"scan-{sessionId}").SendAsync("ScanCancelled", sessionId);

                _logger.LogInformation("Scan session {SessionId} cancelled", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling scan {SessionId}", sessionId);
                throw new HubException($"Failed to cancel scan: {ex.Message}");
            }
        }

        /// <summary>
        /// Send progress update to clients in a session group
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="progress">Progress data</param>
        public async Task SendProgressUpdate(Guid sessionId, object progress)
        {
            await Clients.Group($"scan-{sessionId}").SendAsync("ScanProgress", sessionId, progress);
        }

        /// <summary>
        /// Send scan completion notification
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="fileCount">Number of files found</param>
        public async Task SendScanCompleted(Guid sessionId, int fileCount)
        {
            await Clients.Group($"scan-{sessionId}").SendAsync("ScanCompleted", sessionId, fileCount);
        }

        /// <summary>
        /// Send scan failure notification
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="error">Error message</param>
        public async Task SendScanFailed(Guid sessionId, string error)
        {
            await Clients.Group($"scan-{sessionId}").SendAsync("ScanFailed", sessionId, error);
        }

        /// <summary>
        /// Calculate a hash of the folder path for quick lookup
        /// </summary>
        private string CalculatePathHash(string path)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant());
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash)[..16]; // Take first 16 characters
        }
    }
}
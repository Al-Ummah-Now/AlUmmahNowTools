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
        private readonly IFileWatchService _fileWatchService;
        private readonly IApprovalService _approvalService;

        public FileScanHub(ILogger<FileScanHub> logger, ImportDbContext dbContext, 
            IFileWatchService fileWatchService, IApprovalService approvalService)
        {
            _logger = logger;
            _dbContext = dbContext;
            _fileWatchService = fileWatchService;
            _approvalService = approvalService;
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
            if (exception != null)
            {
                _logger.LogWarning(exception, "Client disconnected with exception: {ConnectionId}", Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
            }
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
        /// Join a watch group for targeted updates
        /// </summary>
        /// <param name="watchId">Watch ID</param>
        public async Task JoinWatchGroup(string watchId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"watch-{watchId}");
            _logger.LogDebug("Client {ConnectionId} joined watch group {WatchId}", Context.ConnectionId, watchId);
        }

        /// <summary>
        /// Leave a watch group
        /// </summary>
        /// <param name="watchId">Watch ID</param>
        public async Task LeaveWatchGroup(string watchId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"watch-{watchId}");
            _logger.LogDebug("Client {ConnectionId} left watch group {WatchId}", Context.ConnectionId, watchId);
        }

        /// <summary>
        /// Join the approvals group for approval notifications
        /// </summary>
        public async Task JoinApprovalsGroup()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "approvals");
            _logger.LogDebug("Client {ConnectionId} joined approvals group", Context.ConnectionId);
        }

        /// <summary>
        /// Leave the approvals group
        /// </summary>
        public async Task LeaveApprovalsGroup()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "approvals");
            _logger.LogDebug("Client {ConnectionId} left approvals group", Context.ConnectionId);
        }

        #region Scan Operations

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
                _logger.LogDebug("Sending ScanStarted message to group scan-{SessionId}: SessionId={SessionId}, FolderPath={FolderPath}",
                    scanSession.Id, scanSession.Id, folderPath);
                await Clients.Group($"scan-{scanSession.Id}").SendAsync("ScanStarted", scanSession.Id, folderPath);

                _logger.LogInformation("Scan session {SessionId} created and queued for {FolderPath}",
                    scanSession.Id, folderPath);
                _logger.LogDebug("StartScan returning SessionId: {SessionId}", scanSession.Id);

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

                var statusDto = new ScanStatusDto
                {
                    SessionId = session.Id,
                    Status = session.Status.ToString(),
                    RootPath = session.RootPath,
                    FilesScanned = session.FilesFound,
                    TotalSize = session.TotalSize,
                    ProgressPercentage = session.ProgressPercentage,
                    CurrentOperation = session.CurrentOperation ?? "Unknown"
                };

                _logger.LogDebug("GetScanStatus returning: SessionId={SessionId}, Status={Status}, Progress={Progress}%",
                    statusDto.SessionId, statusDto.Status, statusDto.ProgressPercentage);
                return statusDto;
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

                _logger.LogDebug("GetAllSessions returning {Count} sessions", sessions.Count);
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
                _logger.LogDebug("Sending ScanCancelled message to group scan-{SessionId}: SessionId={SessionId}",
                    sessionId, sessionId);
                await Clients.Group($"scan-{sessionId}").SendAsync("ScanCancelled", sessionId);

                _logger.LogInformation("Scan session {SessionId} cancelled", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling scan {SessionId}", sessionId);
                throw new HubException($"Failed to cancel scan: {ex.Message}");
            }
        }

        #endregion

        #region Watch Operations

        /// <summary>
        /// Start watching a channel directory
        /// </summary>
        /// <param name="request">Watch creation request</param>
        /// <returns>Watch ID</returns>
        public async Task<Guid> StartWatch(CreateWatchRequest request)
        {
            try
            {
                _logger.LogInformation("Starting watch request from {ConnectionId} for channel: {ChannelName}, path: {WatchPath}", 
                    Context.ConnectionId, request.ChannelName, request.WatchPath);

                var watchId = await _fileWatchService.StartWatchAsync(request);

                // Join the watch group for updates
                await Groups.AddToGroupAsync(Context.ConnectionId, $"watch-{watchId}");

                _logger.LogInformation("Watch {WatchId} started successfully", watchId);
                _logger.LogDebug("StartWatch returning WatchId: {WatchId}", watchId);
                return watchId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting watch for channel {ChannelName}", request.ChannelName);
                throw new HubException($"Failed to start watch: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop watching a channel directory
        /// </summary>
        /// <param name="watchId">Watch ID to stop</param>
        /// <returns>Success status</returns>
        public async Task<bool> StopWatch(Guid watchId)
        {
            try
            {
                _logger.LogInformation("Stopping watch {WatchId} from {ConnectionId}", watchId, Context.ConnectionId);

                var success = await _fileWatchService.StopWatchAsync(watchId);

                if (success)
                {
                    // Notify clients in the watch group
                    _logger.LogDebug("Sending WatchStopped message to group watch-{WatchId}: WatchId={WatchId}",
                        watchId, watchId);
                    await Clients.Group($"watch-{watchId}").SendAsync("WatchStopped", watchId);
                    _logger.LogInformation("Watch {WatchId} stopped successfully", watchId);
                }

                _logger.LogDebug("StopWatch returning: {Success}", success);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping watch {WatchId}", watchId);
                throw new HubException($"Failed to stop watch: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all active watches
        /// </summary>
        /// <returns>List of active watches</returns>
        public async Task<List<WatchStatusDto>> GetActiveWatches()
        {
            try
            {
                var watches = await _fileWatchService.GetActiveWatchesAsync();
                _logger.LogDebug("GetActiveWatches returning {Count} watches", watches.Count);
                return watches;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active watches");
                throw new HubException($"Failed to get active watches: {ex.Message}");
            }
        }

        /// <summary>
        /// Get watch status
        /// </summary>
        /// <param name="watchId">Watch ID</param>
        /// <returns>Watch status</returns>
        public async Task<WatchStatusDto?> GetWatchStatus(Guid watchId)
        {
            try
            {
                var status = await _fileWatchService.GetWatchStatusAsync(watchId);
                _logger.LogDebug("GetWatchStatus returning: WatchId={WatchId}, IsActive={IsActive}",
                    watchId, status?.IsActive ?? false);
                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting watch status for {WatchId}", watchId);
                throw new HubException($"Failed to get watch status: {ex.Message}");
            }
        }

        /// <summary>
        /// Update watch configuration
        /// </summary>
        /// <param name="watchId">Watch ID</param>
        /// <param name="request">Updated watch configuration</param>
        /// <returns>Success status</returns>
        public async Task<bool> UpdateWatch(Guid watchId, CreateWatchRequest request)
        {
            try
            {
                var success = await _fileWatchService.UpdateWatchAsync(watchId, request);

                if (success)
                {
                    _logger.LogDebug("Sending WatchUpdated message to group watch-{WatchId}: WatchId={WatchId}",
                        watchId, watchId);
                    await Clients.Group($"watch-{watchId}").SendAsync("WatchUpdated", watchId);
                }

                _logger.LogDebug("UpdateWatch returning: {Success}", success);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating watch {WatchId}", watchId);
                throw new HubException($"Failed to update watch: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete a watch
        /// </summary>
        /// <param name="watchId">Watch ID</param>
        /// <returns>Success status</returns>
        public async Task<bool> DeleteWatch(Guid watchId)
        {
            try
            {
                var success = await _fileWatchService.DeleteWatchAsync(watchId);

                if (success)
                {
                    _logger.LogDebug("Sending WatchDeleted message to group watch-{WatchId}: WatchId={WatchId}",
                        watchId, watchId);
                    await Clients.Group($"watch-{watchId}").SendAsync("WatchDeleted", watchId);
                }

                _logger.LogDebug("DeleteWatch returning: {Success}", success);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting watch {WatchId}", watchId);
                throw new HubException($"Failed to delete watch: {ex.Message}");
            }
        }

        #endregion

        #region Approval Operations

        /// <summary>
        /// Get pending approvals
        /// </summary>
        /// <returns>List of pending approvals</returns>
        public async Task<List<PendingApprovalDto>> GetPendingApprovals()
        {
            try
            {
                var approvals = await _approvalService.GetPendingApprovalsAsync();
                _logger.LogDebug("GetPendingApprovals returning {Count} approvals", approvals.Count);
                return approvals;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending approvals");
                throw new HubException($"Failed to get pending approvals: {ex.Message}");
            }
        }

        /// <summary>
        /// Get pending approvals for a specific channel
        /// </summary>
        /// <param name="channelName">Channel name</param>
        /// <returns>List of pending approvals for the channel</returns>
        public async Task<List<PendingApprovalDto>> GetPendingApprovalsByChannel(string channelName)
        {
            try
            {
                var approvals = await _approvalService.GetPendingApprovalsByChannelAsync(channelName);
                _logger.LogDebug("GetPendingApprovalsByChannel returning {Count} approvals for channel {ChannelName}",
                    approvals.Count, channelName);
                return approvals;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending approvals for channel {ChannelName}", channelName);
                throw new HubException($"Failed to get pending approvals for channel: {ex.Message}");
            }
        }

        /// <summary>
        /// Process approval decision
        /// </summary>
        /// <param name="request">Approval decision request</param>
        /// <returns>Success status</returns>
        public async Task<bool> ProcessApprovalDecision(ApprovalDecisionRequest request)
        {
            try
            {
                _logger.LogInformation("Processing approval decision from {ConnectionId}: {Decision} for {Count} items", 
                    Context.ConnectionId, request.Decision, request.ApprovalIds.Count);

                var success = await _approvalService.ProcessApprovalDecisionAsync(request);

                if (success)
                {
                    // Notify all clients in the approvals group
                    _logger.LogDebug("Sending ApprovalDecisionProcessed message to group approvals: Decision={Decision}, Count={Count}",
                        request.Decision, request.ApprovalIds.Count);
                    await Clients.Group("approvals").SendAsync("ApprovalDecisionProcessed", request);
                }

                _logger.LogDebug("ProcessApprovalDecision returning: {Success}", success);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing approval decision");
                throw new HubException($"Failed to process approval decision: {ex.Message}");
            }
        }

        /// <summary>
        /// Process bulk approval
        /// </summary>
        /// <param name="request">Bulk approval request</param>
        /// <returns>Number of items processed</returns>
        public async Task<int> ProcessBulkApproval(BulkApprovalRequest request)
        {
            try
            {
                _logger.LogInformation("Processing bulk approval from {ConnectionId}: {Decision}", 
                    Context.ConnectionId, request.Decision);

                var processedCount = await _approvalService.ProcessBulkApprovalAsync(request);

                if (processedCount > 0)
                {
                    // Notify all clients in the approvals group
                    _logger.LogDebug("Sending BulkApprovalProcessed message to group approvals: Decision={Decision}, ProcessedCount={Count}",
                        request.Decision, processedCount);
                    await Clients.Group("approvals").SendAsync("BulkApprovalProcessed", request, processedCount);
                }

                _logger.LogDebug("ProcessBulkApproval returning: {ProcessedCount}", processedCount);
                return processedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing bulk approval");
                throw new HubException($"Failed to process bulk approval: {ex.Message}");
            }
        }

        /// <summary>
        /// Get approval statistics
        /// </summary>
        /// <param name="channelWatchId">Optional channel watch ID filter</param>
        /// <returns>Approval statistics</returns>
        public async Task<ApprovalStatistics> GetApprovalStatistics(Guid? channelWatchId = null)
        {
            try
            {
                var stats = await _approvalService.GetApprovalStatisticsAsync(channelWatchId);
                _logger.LogDebug("GetApprovalStatistics returning: Pending={Pending}, Approved={Approved}, Rejected={Rejected}",
                    stats.TotalPending, stats.TotalApproved, stats.TotalRejected);
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting approval statistics");
                throw new HubException($"Failed to get approval statistics: {ex.Message}");
            }
        }

        #endregion

        #region SignalR Event Handlers

        /// <summary>
        /// Send progress update to clients in a session group
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="progress">Progress data</param>
        public async Task SendProgressUpdate(Guid sessionId, object progress)
        {
            _logger.LogDebug("Sending ScanProgress message to group scan-{SessionId}", sessionId);
            await Clients.Group($"scan-{sessionId}").SendAsync("ScanProgress", sessionId, progress);
        }

        /// <summary>
        /// Send scan completion notification
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="fileCount">Number of files found</param>
        public async Task SendScanCompleted(Guid sessionId, int fileCount)
        {
            _logger.LogInformation("Sending ScanCompleted message to group scan-{SessionId}: FileCount={FileCount}",
                sessionId, fileCount);
            await Clients.Group($"scan-{sessionId}").SendAsync("ScanCompleted", sessionId, fileCount);
        }

        /// <summary>
        /// Send scan failure notification
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="error">Error message</param>
        public async Task SendScanFailed(Guid sessionId, string error)
        {
            _logger.LogWarning("Sending ScanFailed message to group scan-{SessionId}: Error={Error}",
                sessionId, error);
            await Clients.Group($"scan-{sessionId}").SendAsync("ScanFailed", sessionId, error);
        }

        /// <summary>
        /// Send file detected notification
        /// </summary>
        /// <param name="watchId">Watch ID</param>
        /// <param name="channelName">Channel name</param>
        /// <param name="fileName">File name</param>
        /// <param name="filePath">File path</param>
        /// <param name="fileSize">File size</param>
        public async Task SendFileDetected(Guid watchId, string channelName, string fileName, string filePath, long fileSize)
        {
            _logger.LogInformation("Sending FileDetected message to group watch-{WatchId}: Channel={Channel}, File={File}, Size={Size}",
                watchId, channelName, fileName, fileSize);
            await Clients.Group($"watch-{watchId}").SendAsync("FileDetected", watchId, channelName, fileName, filePath, fileSize);

            _logger.LogDebug("Sending NewFileForApproval message to group approvals: Watch={WatchId}, Channel={Channel}, File={File}",
                watchId, channelName, fileName);
            await Clients.Group("approvals").SendAsync("NewFileForApproval", watchId, channelName, fileName, filePath, fileSize);
        }

        /// <summary>
        /// Send watch status change notification
        /// </summary>
        /// <param name="watchId">Watch ID</param>
        /// <param name="channelName">Channel name</param>
        /// <param name="isActive">Whether watch is active</param>
        /// <param name="statusMessage">Status message</param>
        public async Task SendWatchStatusChanged(Guid watchId, string channelName, bool isActive, string? statusMessage = null)
        {
            _logger.LogInformation("Sending WatchStatusChanged message to group watch-{WatchId}: Channel={Channel}, IsActive={IsActive}, Status={Status}",
                watchId, channelName, isActive, statusMessage ?? "N/A");
            await Clients.Group($"watch-{watchId}").SendAsync("WatchStatusChanged", watchId, channelName, isActive, statusMessage);
        }

        /// <summary>
        /// Send approval status change notification
        /// </summary>
        /// <param name="approvalId">Approval ID</param>
        /// <param name="channelName">Channel name</param>
        /// <param name="fileName">File name</param>
        /// <param name="oldStatus">Old status</param>
        /// <param name="newStatus">New status</param>
        /// <param name="approvedBy">Who approved it</param>
        public async Task SendApprovalStatusChanged(Guid approvalId, string channelName, string fileName,
            ApprovalStatus oldStatus, ApprovalStatus newStatus, string? approvedBy = null)
        {
            _logger.LogInformation("Sending ApprovalStatusChanged message to group approvals: Approval={ApprovalId}, Channel={Channel}, File={File}, OldStatus={OldStatus}, NewStatus={NewStatus}, By={ApprovedBy}",
                approvalId, channelName, fileName, oldStatus, newStatus, approvedBy ?? "N/A");
            await Clients.Group("approvals").SendAsync("ApprovalStatusChanged", approvalId, channelName, fileName, oldStatus, newStatus, approvedBy);
        }

        #endregion

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
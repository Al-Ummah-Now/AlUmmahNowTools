using Microsoft.AspNetCore.SignalR;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.EntityFrameworkCore;

namespace AlUmmahNow.FileScanService.Hubs;

public class FileScanHub : Hub
{
    private readonly ImportDbContext _dbContext;
    private readonly ILogger<FileScanHub> _logger;

    public FileScanHub(ImportDbContext dbContext, ILogger<FileScanHub> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Client requests a new scan
    /// </summary>
    public async Task<Guid> StartScan(string folderPath, bool includeSubdirectories = true)
    {
        try
        {
            _logger.LogInformation("Starting scan for folder: {FolderPath}", folderPath);
            
            var session = new ScanSession
            {
                RootPath = folderPath,
                Name = Path.GetFileName(folderPath) ?? folderPath,
                Status = ScanStatus.Pending,
                PathHash = ComputePathHash(folderPath)
            };

            _dbContext.ScanSessions.Add(session);
            await _dbContext.SaveChangesAsync();

            await Clients.All.SendAsync("ScanQueued", session.Id, folderPath);

            _logger.LogInformation("Scan session created with ID: {SessionId}", session.Id);
            return session.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting scan for folder: {FolderPath}", folderPath);
            throw new HubException($"Failed to start scan: {ex.Message}");
        }
    }

    /// <summary>
    /// Client requests scan status
    /// </summary>
    public async Task<ScanStatusDto> GetScanStatus(Guid sessionId)
    {
        try
        {
            var session = await _dbContext.ScanSessions
                .Include(s => s.ScannedVideoFiles)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
                throw new HubException("Session not found");

            return new ScanStatusDto
            {
                SessionId = session.Id,
                Status = session.Status.ToString(),
                RootPath = session.RootPath,
                FilesScanned = session.FilesFound,
                TotalSize = session.TotalSize,
                ProgressPercentage = session.ProgressPercentage,
                CurrentOperation = session.CurrentOperation ?? ""
            };
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scan status for session: {SessionId}", sessionId);
            throw new HubException($"Failed to get scan status: {ex.Message}");
        }
    }

    /// <summary>
    /// Client requests all sessions
    /// </summary>
    public async Task<List<ScanSessionDto>> GetAllSessions()
    {
        try
        {
            var sessions = await _dbContext.ScanSessions
                .OrderByDescending(s => s.CreatedAt)
                .Take(50)
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
    /// Client requests to cancel a scan
    /// </summary>
    public async Task CancelScan(Guid sessionId)
    {
        try
        {
            var session = await _dbContext.ScanSessions.FindAsync(sessionId);
            if (session == null)
                throw new HubException("Session not found");

            session.Status = ScanStatus.Cancelled;
            session.LastUpdated = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await Clients.All.SendAsync("ScanCancelled", sessionId);
            
            _logger.LogInformation("Scan cancelled for session: {SessionId}", sessionId);
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling scan for session: {SessionId}", sessionId);
            throw new HubException($"Failed to cancel scan: {ex.Message}");
        }
    }

    /// <summary>
    /// Compute a hash for a path for quick lookups
    /// </summary>
    private string ComputePathHash(string path)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant());
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}

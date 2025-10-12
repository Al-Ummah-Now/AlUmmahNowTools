using System.Security.Cryptography;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.FileScanService.Services;

/// <summary>
/// Service for managing file approval workflow
/// </summary>
public class ApprovalService : IApprovalService
{
    private readonly ILogger<ApprovalService> _logger;
    private readonly ImportDbContext _dbContext;

    public event EventHandler<ApprovalStatusChangedEventArgs>? ApprovalStatusChanged;

    public ApprovalService(ILogger<ApprovalService> logger, ImportDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task<List<PendingApprovalDto>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var approvals = await _dbContext.PendingApprovals
                .Include(p => p.ChannelWatch)
                .Where(p => p.Status == ApprovalStatus.Pending)
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.DetectedAt)
                .Select(p => new PendingApprovalDto
                {
                    Id = p.Id,
                    ChannelName = p.ChannelWatch.ChannelName,
                    FileName = p.FileName,
                    FilePath = p.FilePath,
                    FileSize = p.FileSize,
                    Extension = p.Extension,
                    DetectedAt = p.DetectedAt,
                    FileModifiedAt = p.FileModifiedAt,
                    Status = p.Status,
                    Priority = p.Priority,
                    FileExists = p.FileExists,
                    ThumbnailPath = p.ThumbnailPath,
                    ExpiresAt = p.ExpiresAt
                })
                .ToListAsync(cancellationToken);

            return approvals;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending approvals");
            return new List<PendingApprovalDto>();
        }
    }

    public async Task<List<PendingApprovalDto>> GetPendingApprovalsByChannelAsync(string channelName, CancellationToken cancellationToken = default)
    {
        try
        {
            var approvals = await _dbContext.PendingApprovals
                .Include(p => p.ChannelWatch)
                .Where(p => p.ChannelWatch.ChannelName == channelName && p.Status == ApprovalStatus.Pending)
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.DetectedAt)
                .Select(p => new PendingApprovalDto
                {
                    Id = p.Id,
                    ChannelName = p.ChannelWatch.ChannelName,
                    FileName = p.FileName,
                    FilePath = p.FilePath,
                    FileSize = p.FileSize,
                    Extension = p.Extension,
                    DetectedAt = p.DetectedAt,
                    FileModifiedAt = p.FileModifiedAt,
                    Status = p.Status,
                    Priority = p.Priority,
                    FileExists = p.FileExists,
                    ThumbnailPath = p.ThumbnailPath,
                    ExpiresAt = p.ExpiresAt
                })
                .ToListAsync(cancellationToken);

            return approvals;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending approvals for channel {ChannelName}", channelName);
            return new List<PendingApprovalDto>();
        }
    }

    public async Task<PendingApprovalDto?> GetPendingApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default)
    {
        try
        {
            var approval = await _dbContext.PendingApprovals
                .Include(p => p.ChannelWatch)
                .Where(p => p.Id == approvalId)
                .Select(p => new PendingApprovalDto
                {
                    Id = p.Id,
                    ChannelName = p.ChannelWatch.ChannelName,
                    FileName = p.FileName,
                    FilePath = p.FilePath,
                    FileSize = p.FileSize,
                    Extension = p.Extension,
                    DetectedAt = p.DetectedAt,
                    FileModifiedAt = p.FileModifiedAt,
                    Status = p.Status,
                    Priority = p.Priority,
                    FileExists = p.FileExists,
                    ThumbnailPath = p.ThumbnailPath,
                    ExpiresAt = p.ExpiresAt
                })
                .FirstOrDefaultAsync(cancellationToken);

            return approval;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending approval {ApprovalId}", approvalId);
            return null;
        }
    }

    public async Task<bool> ProcessApprovalDecisionAsync(ApprovalDecisionRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.ApprovalIds.Any())
        {
            return false;
        }

        _logger.LogInformation("Processing approval decision for {Count} items: {Decision}", 
            request.ApprovalIds.Count, request.Decision);

        try
        {
            var approvals = await _dbContext.PendingApprovals
                .Include(p => p.ChannelWatch)
                .Where(p => request.ApprovalIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            if (!approvals.Any())
            {
                _logger.LogWarning("No approvals found for the provided IDs");
                return false;
            }

            var updatedCount = 0;
            foreach (var approval in approvals)
            {
                // Only update if status is currently Pending
                if (approval.Status != ApprovalStatus.Pending)
                {
                    _logger.LogWarning("Approval {ApprovalId} is not in Pending status, skipping", approval.Id);
                    continue;
                }

                var oldStatus = approval.Status;
                approval.Status = request.Decision;
                approval.ApprovedAt = DateTime.UtcNow;
                approval.ApprovedBy = request.ApprovedBy;
                approval.ApprovalNotes = request.Notes;

                // Check if file still exists when approving
                if (request.Decision == ApprovalStatus.Approved || request.Decision == ApprovalStatus.AutoApproved)
                {
                    approval.FileExists = File.Exists(approval.FilePath);
                    if (!approval.FileExists)
                    {
                        _logger.LogWarning("File no longer exists for approval {ApprovalId}: {FilePath}", 
                            approval.Id, approval.FilePath);
                    }
                }

                // Raise status changed event
                ApprovalStatusChanged?.Invoke(this, new ApprovalStatusChangedEventArgs(
                    approval.Id, approval.ChannelWatchId, approval.ChannelWatch.ChannelName, 
                    approval.FileName, oldStatus, approval.Status, request.ApprovedBy));

                updatedCount++;
                
                _logger.LogDebug("Updated approval {ApprovalId} for file {FileName} to status {Status}", 
                    approval.Id, approval.FileName, request.Decision);
            }

            if (updatedCount > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully updated {UpdatedCount} approval decisions", updatedCount);
            }

            return updatedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process approval decision");
            return false;
        }
    }

    public async Task<int> ProcessBulkApprovalAsync(BulkApprovalRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing bulk approval request: {Decision}", request.Decision);

        try
        {
            var query = _dbContext.PendingApprovals
                .Include(p => p.ChannelWatch)
                .Where(p => p.Status == ApprovalStatus.Pending);

            // Apply filters
            if (request.ChannelWatchId.HasValue)
            {
                query = query.Where(p => p.ChannelWatchId == request.ChannelWatchId.Value);
            }

            if (!string.IsNullOrEmpty(request.ChannelName))
            {
                query = query.Where(p => p.ChannelWatch.ChannelName == request.ChannelName);
            }

            if (request.DetectedAfter.HasValue)
            {
                query = query.Where(p => p.DetectedAt >= request.DetectedAfter.Value);
            }

            if (request.DetectedBefore.HasValue)
            {
                query = query.Where(p => p.DetectedAt <= request.DetectedBefore.Value);
            }

            if (request.FileExtensions?.Any() == true)
            {
                query = query.Where(p => request.FileExtensions.Contains(p.Extension));
            }

            if (request.MinFileSize.HasValue)
            {
                query = query.Where(p => p.FileSize >= request.MinFileSize.Value);
            }

            if (request.MaxFileSize.HasValue)
            {
                query = query.Where(p => p.FileSize <= request.MaxFileSize.Value);
            }

            var approvals = await query.ToListAsync(cancellationToken);
            
            if (!approvals.Any())
            {
                _logger.LogInformation("No approvals found matching bulk approval criteria");
                return 0;
            }

            var updatedCount = 0;
            foreach (var approval in approvals)
            {
                var oldStatus = approval.Status;
                approval.Status = request.Decision;
                approval.ApprovedAt = DateTime.UtcNow;
                approval.ApprovedBy = request.ApprovedBy;
                approval.ApprovalNotes = request.Notes;

                // Check if file still exists when approving
                if (request.Decision == ApprovalStatus.Approved || request.Decision == ApprovalStatus.AutoApproved)
                {
                    approval.FileExists = File.Exists(approval.FilePath);
                }

                // Raise status changed event
                ApprovalStatusChanged?.Invoke(this, new ApprovalStatusChangedEventArgs(
                    approval.Id, approval.ChannelWatchId, approval.ChannelWatch.ChannelName, 
                    approval.FileName, oldStatus, approval.Status, request.ApprovedBy));

                updatedCount++;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Successfully processed bulk approval for {UpdatedCount} items", updatedCount);
            return updatedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process bulk approval");
            return 0;
        }
    }

    public async Task<Guid> AddPendingApprovalAsync(Guid channelWatchId, string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Adding pending approval for file: {FilePath}", filePath);

        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var fileInfo = new FileInfo(filePath);
            var contentHash = await CalculateFileHashAsync(filePath);
            var thumbnailPath = FindThumbnailForVideo(fileInfo);

            // Check for duplicates based on content hash
            var isDuplicate = await _dbContext.PendingApprovals
                .AnyAsync(p => p.ContentHash == contentHash && p.Status != ApprovalStatus.Rejected, cancellationToken);

            var pendingApproval = new PendingApproval
            {
                ChannelWatchId = channelWatchId,
                FilePath = filePath,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                Extension = fileInfo.Extension.ToLowerInvariant(),
                FileModifiedAt = fileInfo.LastWriteTime,
                ContentHash = contentHash,
                ThumbnailPath = thumbnailPath,
                Priority = isDuplicate ? 5 : 3, // Lower priority for duplicates
                ExpiresAt = DateTime.UtcNow.AddDays(30) // 30-day default expiry
            };

            _dbContext.PendingApprovals.Add(pendingApproval);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Added pending approval {ApprovalId} for file {FileName}", 
                pendingApproval.Id, fileInfo.Name);

            return pendingApproval.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add pending approval for file: {FilePath}", filePath);
            throw;
        }
    }

    public async Task<int> CleanupExpiredApprovalsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cleaning up expired approvals");

        try
        {
            var expiredApprovals = await _dbContext.PendingApprovals
                .Where(p => p.Status == ApprovalStatus.Pending && 
                           p.ExpiresAt.HasValue && 
                           p.ExpiresAt.Value <= DateTime.UtcNow)
                .ToListAsync(cancellationToken);

            if (!expiredApprovals.Any())
            {
                return 0;
            }

            foreach (var approval in expiredApprovals)
            {
                var oldStatus = approval.Status;
                approval.Status = ApprovalStatus.Expired;
                approval.ApprovedAt = DateTime.UtcNow;

                // Raise status changed event
                ApprovalStatusChanged?.Invoke(this, new ApprovalStatusChangedEventArgs(
                    approval.Id, approval.ChannelWatchId, approval.ChannelWatch?.ChannelName ?? "Unknown", 
                    approval.FileName, oldStatus, approval.Status, "System"));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Expired {Count} pending approvals", expiredApprovals.Count);
            return expiredApprovals.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired approvals");
            return 0;
        }
    }

    public async Task<ApprovalStatistics> GetApprovalStatisticsAsync(Guid? channelWatchId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.PendingApprovals.AsQueryable();
            
            if (channelWatchId.HasValue)
            {
                query = query.Where(p => p.ChannelWatchId == channelWatchId.Value);
            }

            var statistics = await query
                .GroupBy(p => 1) // Group all records together
                .Select(g => new ApprovalStatistics
                {
                    TotalPending = g.Count(p => p.Status == ApprovalStatus.Pending),
                    TotalApproved = g.Count(p => p.Status == ApprovalStatus.Approved),
                    TotalRejected = g.Count(p => p.Status == ApprovalStatus.Rejected),
                    TotalAutoApproved = g.Count(p => p.Status == ApprovalStatus.AutoApproved),
                    TotalExpired = g.Count(p => p.Status == ApprovalStatus.Expired),
                    TotalImported = g.Count(p => p.Status == ApprovalStatus.ImportCompleted),
                    TotalFailed = g.Count(p => p.Status == ApprovalStatus.ImportFailed),
                    OldestPending = g.Where(p => p.Status == ApprovalStatus.Pending).Min(p => (DateTime?)p.DetectedAt),
                    NewestPending = g.Where(p => p.Status == ApprovalStatus.Pending).Max(p => (DateTime?)p.DetectedAt),
                    TotalSizePending = g.Where(p => p.Status == ApprovalStatus.Pending).Sum(p => p.FileSize),
                    TotalSizeApproved = g.Where(p => p.Status == ApprovalStatus.Approved || p.Status == ApprovalStatus.AutoApproved).Sum(p => p.FileSize)
                })
                .FirstOrDefaultAsync(cancellationToken);

            return statistics ?? new ApprovalStatistics();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get approval statistics");
            return new ApprovalStatistics();
        }
    }

    private async Task<string> CalculateFileHashAsync(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            
            // For large files, only hash the first 1MB for performance
            var buffer = new byte[Math.Min(stream.Length, 1024 * 1024)];
            var bytesRead = await stream.ReadAsync(buffer);
            
            var hashBytes = sha256.ComputeHash(buffer, 0, bytesRead);
            return Convert.ToHexString(hashBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate hash for file: {FilePath}", filePath);
            return string.Empty;
        }
    }

    private string? FindThumbnailForVideo(FileInfo videoFile)
    {
        try
        {
            var baseFileName = Path.GetFileNameWithoutExtension(videoFile.Name);
            var directory = videoFile.Directory!;
            var thumbnailExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

            // Check for thumbnail in same directory
            foreach (var ext in thumbnailExtensions)
            {
                var thumbnailPath = Path.Combine(directory.FullName, baseFileName + ext);
                if (File.Exists(thumbnailPath))
                {
                    return thumbnailPath;
                }
            }

            // Check for thumbnail in .thumbs subdirectory
            var thumbsDir = Path.Combine(directory.FullName, ".thumbs");
            if (Directory.Exists(thumbsDir))
            {
                foreach (var ext in thumbnailExtensions)
                {
                    var thumbnailPath = Path.Combine(thumbsDir, baseFileName + ext);
                    if (File.Exists(thumbnailPath))
                    {
                        return thumbnailPath;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding thumbnail for video: {VideoPath}", videoFile.FullName);
            return null;
        }
    }
}
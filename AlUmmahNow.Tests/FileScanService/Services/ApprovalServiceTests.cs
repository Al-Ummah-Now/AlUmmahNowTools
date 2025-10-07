using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AlUmmahNow.Tests.FileScanService.Services;

public class ApprovalServiceTests : FileSystemTestBase
{
    private readonly Mock<ILogger<ApprovalService>> _mockLogger;
    private readonly ImportDbContext _dbContext;
    private ApprovalService _approvalService;

    public ApprovalServiceTests()
    {
        _mockLogger = new Mock<ILogger<ApprovalService>>();
        
        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ImportDbContext(options);
        _dbContext.Database.EnsureCreated();

        _approvalService = new ApprovalService(_mockLogger.Object, _dbContext);
    }

    public override async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task GetPendingApprovalsAsync_ShouldReturnOnlyPendingApprovals()
    {
        // Arrange
        var channelWatch = new ChannelWatch
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory
        };
        _dbContext.ChannelWatches.Add(channelWatch);

        var approvals = new List<PendingApproval>
        {
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "video1.mp4", FilePath = "path1", FileSize = 1024, Extension = ".mp4", Priority = 1 },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Approved, FileName = "video2.mp4", FilePath = "path2", FileSize = 2048, Extension = ".mp4", Priority = 2 },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "video3.mp4", FilePath = "path3", FileSize = 3072, Extension = ".mp4", Priority = 3 }
        };

        _dbContext.PendingApprovals.AddRange(approvals);
        await _dbContext.SaveChangesAsync();

        // Act
        var pendingApprovals = await _approvalService.GetPendingApprovalsAsync();

        // Assert
        pendingApprovals.Should().HaveCount(2);
        pendingApprovals.Should().OnlyContain(a => a.Status == ApprovalStatus.Pending);
        pendingApprovals.Should().BeInAscendingOrder(a => a.Priority).And.ThenBeInAscendingOrder(a => a.DetectedAt);
    }

    [Fact]
    public async Task GetPendingApprovalsByChannelAsync_ShouldReturnApprovalsForSpecificChannel()
    {
        // Arrange
        var channel1 = new ChannelWatch { ChannelName = "Channel1", WatchPath = TestChannel1Directory };
        var channel2 = new ChannelWatch { ChannelName = "Channel2", WatchPath = TestChannel2Directory };
        _dbContext.ChannelWatches.AddRange(channel1, channel2);

        var approvals = new List<PendingApproval>
        {
            new() { ChannelWatchId = channel1.Id, Status = ApprovalStatus.Pending, FileName = "video1.mp4", FilePath = "path1", FileSize = 1024, Extension = ".mp4" },
            new() { ChannelWatchId = channel2.Id, Status = ApprovalStatus.Pending, FileName = "video2.mp4", FilePath = "path2", FileSize = 2048, Extension = ".mp4" },
            new() { ChannelWatchId = channel1.Id, Status = ApprovalStatus.Pending, FileName = "video3.mp4", FilePath = "path3", FileSize = 3072, Extension = ".mp4" }
        };

        _dbContext.PendingApprovals.AddRange(approvals);
        await _dbContext.SaveChangesAsync();

        // Act
        var channel1Approvals = await _approvalService.GetPendingApprovalsByChannelAsync("Channel1");

        // Assert
        channel1Approvals.Should().HaveCount(2);
        channel1Approvals.Should().OnlyContain(a => a.ChannelName == "Channel1");
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_ShouldUpdateApprovalStatuses()
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);

        var approvals = new List<PendingApproval>
        {
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "video1.mp4", FilePath = "path1", FileSize = 1024, Extension = ".mp4" },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "video2.mp4", FilePath = "path2", FileSize = 2048, Extension = ".mp4" }
        };

        _dbContext.PendingApprovals.AddRange(approvals);
        await _dbContext.SaveChangesAsync();

        var request = new ApprovalDecisionRequest
        {
            ApprovalIds = approvals.Select(a => a.Id).ToList(),
            Decision = ApprovalStatus.Approved,
            Notes = "Test approval",
            ApprovedBy = "TestUser"
        };

        ApprovalStatusChangedEventArgs? lastEventArgs = null;
        _approvalService.ApprovalStatusChanged += (sender, e) => lastEventArgs = e;

        // Act
        var result = await _approvalService.ProcessApprovalDecisionAsync(request);

        // Assert
        result.Should().BeTrue();

        var updatedApprovals = await _dbContext.PendingApprovals
            .Where(a => request.ApprovalIds.Contains(a.Id))
            .ToListAsync();

        updatedApprovals.Should().OnlyContain(a => a.Status == ApprovalStatus.Approved);
        updatedApprovals.Should().OnlyContain(a => a.ApprovedBy == "TestUser");
        updatedApprovals.Should().OnlyContain(a => a.ApprovalNotes == "Test approval");
        updatedApprovals.Should().OnlyContain(a => a.ApprovedAt.HasValue);

        // Verify event was raised
        lastEventArgs.Should().NotBeNull();
        lastEventArgs!.NewStatus.Should().Be(ApprovalStatus.Approved);
        lastEventArgs.ApprovedBy.Should().Be("TestUser");
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_WithEmptyList_ShouldReturnFalse()
    {
        // Arrange
        var request = new ApprovalDecisionRequest
        {
            ApprovalIds = new List<Guid>(),
            Decision = ApprovalStatus.Approved
        };

        // Act
        var result = await _approvalService.ProcessApprovalDecisionAsync(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_WithNonPendingStatus_ShouldSkipThoseApprovals()
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);

        var approvals = new List<PendingApproval>
        {
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "video1.mp4", FilePath = "path1", FileSize = 1024, Extension = ".mp4" },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Approved, FileName = "video2.mp4", FilePath = "path2", FileSize = 2048, Extension = ".mp4" } // Already approved
        };

        _dbContext.PendingApprovals.AddRange(approvals);
        await _dbContext.SaveChangesAsync();

        var request = new ApprovalDecisionRequest
        {
            ApprovalIds = approvals.Select(a => a.Id).ToList(),
            Decision = ApprovalStatus.Rejected
        };

        // Act
        var result = await _approvalService.ProcessApprovalDecisionAsync(request);

        // Assert
        result.Should().BeTrue(); // Should succeed for the pending one

        var updatedApprovals = await _dbContext.PendingApprovals
            .Where(a => request.ApprovalIds.Contains(a.Id))
            .ToListAsync();

        // Only the pending one should be updated to rejected
        updatedApprovals.Count(a => a.Status == ApprovalStatus.Rejected).Should().Be(1);
        updatedApprovals.Count(a => a.Status == ApprovalStatus.Approved).Should().Be(1);
    }

    [Fact]
    public async Task ProcessBulkApprovalAsync_ShouldApplyFiltersCorrectly()
    {
        // Arrange
        var channelWatch1 = new ChannelWatch { ChannelName = "Channel1", WatchPath = TestChannel1Directory };
        var channelWatch2 = new ChannelWatch { ChannelName = "Channel2", WatchPath = TestChannel2Directory };
        _dbContext.ChannelWatches.AddRange(channelWatch1, channelWatch2);

        var baseDate = DateTime.UtcNow.AddDays(-5);
        var approvals = new List<PendingApproval>
        {
            new() { ChannelWatchId = channelWatch1.Id, Status = ApprovalStatus.Pending, FileName = "video1.mp4", FilePath = "path1", FileSize = 1024 * 1024, Extension = ".mp4", DetectedAt = baseDate.AddDays(1) },
            new() { ChannelWatchId = channelWatch1.Id, Status = ApprovalStatus.Pending, FileName = "video2.mkv", FilePath = "path2", FileSize = 2048 * 1024, Extension = ".mkv", DetectedAt = baseDate.AddDays(2) },
            new() { ChannelWatchId = channelWatch2.Id, Status = ApprovalStatus.Pending, FileName = "video3.mp4", FilePath = "path3", FileSize = 3072 * 1024, Extension = ".mp4", DetectedAt = baseDate.AddDays(3) },
            new() { ChannelWatchId = channelWatch1.Id, Status = ApprovalStatus.Pending, FileName = "video4.avi", FilePath = "path4", FileSize = 500 * 1024, Extension = ".avi", DetectedAt = baseDate.AddDays(4) }
        };

        _dbContext.PendingApprovals.AddRange(approvals);
        await _dbContext.SaveChangesAsync();

        var request = new BulkApprovalRequest
        {
            ChannelName = "Channel1",
            Decision = ApprovalStatus.Approved,
            FileExtensions = new List<string> { ".mp4", ".mkv" },
            MinFileSize = 1024 * 1024, // 1MB minimum
            DetectedAfter = baseDate,
            DetectedBefore = baseDate.AddDays(3),
            ApprovedBy = "BulkUser"
        };

        // Act
        var processedCount = await _approvalService.ProcessBulkApprovalAsync(request);

        // Assert
        processedCount.Should().Be(2); // Only video1.mp4 and video2.mkv should match all criteria

        var updatedApprovals = await _dbContext.PendingApprovals
            .Where(a => a.Status == ApprovalStatus.Approved)
            .ToListAsync();

        updatedApprovals.Should().HaveCount(2);
        updatedApprovals.Should().OnlyContain(a => a.ApprovedBy == "BulkUser");
        updatedApprovals.Should().Contain(a => a.FileName == "video1.mp4");
        updatedApprovals.Should().Contain(a => a.FileName == "video2.mkv");
    }

    [Fact]
    public async Task AddPendingApprovalAsync_ShouldCreateApprovalWithCorrectProperties()
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);
        await _dbContext.SaveChangesAsync();

        var testFilePath = await CreateTestVideoFileAsync(TestChannel1Directory, "testvideo.mp4", 1024 * 1024 * 10); // 10MB

        // Act
        var approvalId = await _approvalService.AddPendingApprovalAsync(channelWatch.Id, testFilePath);

        // Assert
        approvalId.Should().NotBe(Guid.Empty);

        var approval = await _dbContext.PendingApprovals.FindAsync(approvalId);
        approval.Should().NotBeNull();
        approval!.ChannelWatchId.Should().Be(channelWatch.Id);
        approval.FilePath.Should().Be(testFilePath);
        approval.FileName.Should().Be("testvideo.mp4");
        approval.FileSize.Should().Be(1024 * 1024 * 10);
        approval.Extension.Should().Be(".mp4");
        approval.Status.Should().Be(ApprovalStatus.Pending);
        approval.ContentHash.Should().NotBeNullOrEmpty();
        approval.Priority.Should().Be(3); // Default priority for non-duplicates
        approval.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AddPendingApprovalAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);
        await _dbContext.SaveChangesAsync();

        var nonExistentFilePath = Path.Combine(TestChannel1Directory, "nonexistent.mp4");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => 
            _approvalService.AddPendingApprovalAsync(channelWatch.Id, nonExistentFilePath));
    }

    [Fact]
    public async Task AddPendingApprovalAsync_WithThumbnail_ShouldSetThumbnailPath()
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);
        await _dbContext.SaveChangesAsync();

        var testFilePath = await CreateTestVideoFileAsync(TestChannel1Directory, "videowithumb.mp4", 1024 * 1024);
        var thumbnailPath = await CreateTestThumbnailAsync(TestThumbsDirectory, "videowithumb.jpg", 50 * 1024);

        // Act
        var approvalId = await _approvalService.AddPendingApprovalAsync(channelWatch.Id, testFilePath);

        // Assert
        var approval = await _dbContext.PendingApprovals.FindAsync(approvalId);
        approval!.ThumbnailPath.Should().Be(thumbnailPath);
    }

    [Fact]
    public async Task CleanupExpiredApprovalsAsync_ShouldExpireOldPendingApprovals()
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);

        var approvals = new List<PendingApproval>
        {
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "video1.mp4", FilePath = "path1", FileSize = 1024, Extension = ".mp4", ExpiresAt = DateTime.UtcNow.AddDays(-1) }, // Expired
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "video2.mp4", FilePath = "path2", FileSize = 2048, Extension = ".mp4", ExpiresAt = DateTime.UtcNow.AddDays(1) }, // Not expired
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Approved, FileName = "video3.mp4", FilePath = "path3", FileSize = 3072, Extension = ".mp4", ExpiresAt = DateTime.UtcNow.AddDays(-1) } // Already approved, should not be affected
        };

        _dbContext.PendingApprovals.AddRange(approvals);
        await _dbContext.SaveChangesAsync();

        ApprovalStatusChangedEventArgs? eventArgs = null;
        _approvalService.ApprovalStatusChanged += (sender, e) => eventArgs = e;

        // Act
        var expiredCount = await _approvalService.CleanupExpiredApprovalsAsync();

        // Assert
        expiredCount.Should().Be(1);

        var expiredApproval = await _dbContext.PendingApprovals
            .FirstAsync(a => a.FileName == "video1.mp4");
        expiredApproval.Status.Should().Be(ApprovalStatus.Expired);
        expiredApproval.ApprovedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // Verify event was raised
        eventArgs.Should().NotBeNull();
        eventArgs!.NewStatus.Should().Be(ApprovalStatus.Expired);
        eventArgs.ApprovedBy.Should().Be("System");
    }

    [Fact]
    public async Task GetApprovalStatisticsAsync_ShouldReturnCorrectStatistics()
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);

        var baseDate = DateTime.UtcNow.AddDays(-10);
        var approvals = new List<PendingApproval>
        {
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "video1.mp4", FilePath = "path1", FileSize = 1024, Extension = ".mp4", DetectedAt = baseDate.AddDays(1) },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "video2.mp4", FilePath = "path2", FileSize = 2048, Extension = ".mp4", DetectedAt = baseDate.AddDays(5) },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Approved, FileName = "video3.mp4", FilePath = "path3", FileSize = 3072, Extension = ".mp4", DetectedAt = baseDate.AddDays(3) },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Rejected, FileName = "video4.mp4", FilePath = "path4", FileSize = 4096, Extension = ".mp4", DetectedAt = baseDate.AddDays(2) },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.AutoApproved, FileName = "video5.mp4", FilePath = "path5", FileSize = 5120, Extension = ".mp4", DetectedAt = baseDate.AddDays(4) },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.ImportCompleted, FileName = "video6.mp4", FilePath = "path6", FileSize = 6144, Extension = ".mp4", DetectedAt = baseDate.AddDays(6) },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.ImportFailed, FileName = "video7.mp4", FilePath = "path7", FileSize = 7168, Extension = ".mp4", DetectedAt = baseDate.AddDays(7) }
        };

        _dbContext.PendingApprovals.AddRange(approvals);
        await _dbContext.SaveChangesAsync();

        // Act
        var statistics = await _approvalService.GetApprovalStatisticsAsync(channelWatch.Id);

        // Assert
        statistics.TotalPending.Should().Be(2);
        statistics.TotalApproved.Should().Be(1);
        statistics.TotalRejected.Should().Be(1);
        statistics.TotalAutoApproved.Should().Be(1);
        statistics.TotalImported.Should().Be(1);
        statistics.TotalFailed.Should().Be(1);
        statistics.TotalSizePending.Should().Be(1024 + 2048);
        statistics.TotalSizeApproved.Should().Be(3072 + 5120); // Approved + AutoApproved
        statistics.OldestPending.Should().Be(baseDate.AddDays(1));
        statistics.NewestPending.Should().Be(baseDate.AddDays(5));
    }

    [Fact]
    public async Task GetPendingApprovalAsync_WithValidId_ShouldReturnApprovalDto()
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);

        var approval = new PendingApproval
        {
            ChannelWatchId = channelWatch.Id,
            Status = ApprovalStatus.Pending,
            FileName = "test.mp4",
            FilePath = "test/path/test.mp4",
            FileSize = 1024 * 1024 * 50, // 50MB
            Extension = ".mp4",
            Priority = 2,
            DetectedAt = DateTime.UtcNow.AddHours(-2),
            FileExists = true
        };

        _dbContext.PendingApprovals.Add(approval);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _approvalService.GetPendingApprovalAsync(approval.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(approval.Id);
        result.ChannelName.Should().Be("TestChannel");
        result.FileName.Should().Be("test.mp4");
        result.FilePath.Should().Be("test/path/test.mp4");
        result.FileSize.Should().Be(1024 * 1024 * 50);
        result.FileSizeFormatted.Should().Be("50.0 MB");
        result.Extension.Should().Be(".mp4");
        result.Priority.Should().Be(2);
        result.FileExists.Should().BeTrue();
        result.Status.Should().Be(ApprovalStatus.Pending);
    }

    [Fact]
    public async Task GetPendingApprovalAsync_WithInvalidId_ShouldReturnNull()
    {
        // Arrange
        var invalidId = Guid.NewGuid();

        // Act
        var result = await _approvalService.GetPendingApprovalAsync(invalidId);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    [InlineData(1024 * 1024 * 1024, "1.0 GB")]
    [InlineData(500, "500.0 B")]
    [InlineData(1536, "1.5 KB")]
    public async Task PendingApprovalDto_ShouldFormatFileSizeCorrectly(long fileSize, string expectedFormat)
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);

        var approval = new PendingApproval
        {
            ChannelWatchId = channelWatch.Id,
            Status = ApprovalStatus.Pending,
            FileName = "test.mp4",
            FilePath = "test.mp4",
            FileSize = fileSize,
            Extension = ".mp4"
        };

        _dbContext.PendingApprovals.Add(approval);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _approvalService.GetPendingApprovalAsync(approval.Id);

        // Assert
        result!.FileSizeFormatted.Should().Be(expectedFormat);
    }

    [Fact]
    public async Task ProcessApprovalDecisionAsync_WithApprovalDecision_ShouldCheckFileExists()
    {
        // Arrange
        var channelWatch = new ChannelWatch { ChannelName = "TestChannel", WatchPath = TestChannel1Directory };
        _dbContext.ChannelWatches.Add(channelWatch);

        var realFilePath = await CreateTestVideoFileAsync(TestChannel1Directory, "realfile.mp4", 1024);
        var fakeFilePath = Path.Combine(TestChannel1Directory, "fakefile.mp4");

        var approvals = new List<PendingApproval>
        {
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "realfile.mp4", FilePath = realFilePath, FileSize = 1024, Extension = ".mp4" },
            new() { ChannelWatchId = channelWatch.Id, Status = ApprovalStatus.Pending, FileName = "fakefile.mp4", FilePath = fakeFilePath, FileSize = 1024, Extension = ".mp4" }
        };

        _dbContext.PendingApprovals.AddRange(approvals);
        await _dbContext.SaveChangesAsync();

        var request = new ApprovalDecisionRequest
        {
            ApprovalIds = approvals.Select(a => a.Id).ToList(),
            Decision = ApprovalStatus.Approved
        };

        // Act
        await _approvalService.ProcessApprovalDecisionAsync(request);

        // Assert
        var updatedApprovals = await _dbContext.PendingApprovals
            .Where(a => request.ApprovalIds.Contains(a.Id))
            .ToListAsync();

        var realFileApproval = updatedApprovals.First(a => a.FileName == "realfile.mp4");
        var fakeFileApproval = updatedApprovals.First(a => a.FileName == "fakefile.mp4");

        realFileApproval.FileExists.Should().BeTrue();
        fakeFileApproval.FileExists.Should().BeFalse();
    }
}
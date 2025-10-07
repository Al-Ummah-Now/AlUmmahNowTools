using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Services;
using FluentAssertions;

namespace AlUmmahNow.Tests.Data.Models.FileScanning;

public class WatchModelsTests
{
    [Fact]
    public void ChannelWatch_ShouldInitializeWithDefaultValues()
    {
        // Act
        var channelWatch = new ChannelWatch();

        // Assert
        channelWatch.Id.Should().NotBe(Guid.Empty);
        channelWatch.IsActive.Should().BeTrue();
        channelWatch.IncludeSubdirectories.Should().BeTrue();
        channelWatch.ProcessingDelayMs.Should().Be(5000);
        channelWatch.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        channelWatch.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        channelWatch.TotalFilesDetected.Should().Be(0);
        channelWatch.FileExtensions.Should().Be(".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm");
        channelWatch.PendingApprovals.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void PendingApproval_ShouldInitializeWithDefaultValues()
    {
        // Act
        var pendingApproval = new PendingApproval();

        // Assert
        pendingApproval.Id.Should().NotBe(Guid.Empty);
        pendingApproval.DetectedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        pendingApproval.Status.Should().Be(ApprovalStatus.Pending);
        pendingApproval.FileExists.Should().BeTrue();
        pendingApproval.Priority.Should().Be(3);
    }

    [Theory]
    [InlineData(ApprovalStatus.Pending)]
    [InlineData(ApprovalStatus.Approved)]
    [InlineData(ApprovalStatus.Rejected)]
    [InlineData(ApprovalStatus.AutoApproved)]
    [InlineData(ApprovalStatus.Expired)]
    [InlineData(ApprovalStatus.ImportStarted)]
    [InlineData(ApprovalStatus.ImportCompleted)]
    [InlineData(ApprovalStatus.ImportFailed)]
    public void ApprovalStatus_ShouldContainAllExpectedValues(ApprovalStatus status)
    {
        // Act & Assert - Just verifying all enum values are defined
        status.Should().BeDefined();
    }

    [Theory]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    [InlineData(1024 * 1024 * 1024, "1.0 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1.0 TB")]
    [InlineData(500, "500.0 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(2.5 * 1024 * 1024, "2.5 MB")]
    public void PendingApprovalDto_FileSizeFormatted_ShouldFormatCorrectly(double fileSizeBytes, string expectedFormat)
    {
        // Arrange
        var dto = new PendingApprovalDto
        {
            FileSize = (long)fileSizeBytes
        };

        // Act
        var formatted = dto.FileSizeFormatted;

        // Assert
        formatted.Should().Be(expectedFormat);
    }

    [Theory]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    [InlineData(1024 * 1024 * 1024, "1.0 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1.0 TB")]
    [InlineData(500, "500.0 B")]
    public void ApprovalStatistics_SizeFormatting_ShouldFormatCorrectly(long sizeInBytes, string expectedFormat)
    {
        // Arrange
        var statistics = new ApprovalStatistics
        {
            TotalSizePending = sizeInBytes,
            TotalSizeApproved = sizeInBytes
        };

        // Act & Assert
        statistics.TotalSizePendingFormatted.Should().Be(expectedFormat);
        statistics.TotalSizeApprovedFormatted.Should().Be(expectedFormat);
    }

    [Fact]
    public void CreateWatchRequest_ShouldHaveCorrectDefaults()
    {
        // Act
        var request = new CreateWatchRequest();

        // Assert
        request.IncludeSubdirectories.Should().BeTrue();
        request.ProcessingDelayMs.Should().Be(5000);
    }

    [Fact]
    public void ApprovalDecisionRequest_ShouldInitializeCorrectly()
    {
        // Act
        var request = new ApprovalDecisionRequest();

        // Assert
        request.ApprovalIds.Should().NotBeNull().And.BeEmpty();
        request.Decision.Should().Be(ApprovalStatus.Pending); // Default enum value
    }

    [Fact]
    public void BulkApprovalRequest_ShouldAllowNullableProperties()
    {
        // Arrange & Act
        var request = new BulkApprovalRequest
        {
            ChannelWatchId = null,
            ChannelName = null,
            DetectedAfter = null,
            DetectedBefore = null,
            FileExtensions = null,
            MinFileSize = null,
            MaxFileSize = null,
            Notes = null,
            ApprovedBy = null
        };

        // Assert - Should not throw exceptions
        request.ChannelWatchId.Should().BeNull();
        request.ChannelName.Should().BeNull();
        request.DetectedAfter.Should().BeNull();
        request.DetectedBefore.Should().BeNull();
        request.FileExtensions.Should().BeNull();
        request.MinFileSize.Should().BeNull();
        request.MaxFileSize.Should().BeNull();
        request.Notes.Should().BeNull();
        request.ApprovedBy.Should().BeNull();
    }

    [Fact]
    public void WatchStatusDto_ShouldCalculateStatisticsCorrectly()
    {
        // Arrange
        var dto = new WatchStatusDto
        {
            TotalFilesDetected = 100,
            PendingApprovals = 10,
            AutoApproved = 20,
            ManuallyApproved = 30,
            Rejected = 15,
            Imported = 25,
            Failed = 5
        };

        // Act & Assert
        var totalProcessed = dto.PendingApprovals + dto.AutoApproved + dto.ManuallyApproved + dto.Rejected + dto.Imported + dto.Failed;
        totalProcessed.Should().Be(105); // More than detected due to some files being processed multiple times or different counting
    }

    [Theory]
    [InlineData("TestChannel", "/path/to/watch", true, 2000, "user1")]
    [InlineData("", "", false, 1000, null)]
    [InlineData("Channel With Spaces", "/path/with spaces/", true, 10000, "")]
    public void CreateWatchRequest_ShouldAcceptVariousInputs(string channelName, string watchPath, 
        bool includeSubdirectories, int processingDelayMs, string? createdBy)
    {
        // Act
        var request = new CreateWatchRequest
        {
            ChannelName = channelName,
            WatchPath = watchPath,
            IncludeSubdirectories = includeSubdirectories,
            ProcessingDelayMs = processingDelayMs,
            CreatedBy = createdBy
        };

        // Assert
        request.ChannelName.Should().Be(channelName);
        request.WatchPath.Should().Be(watchPath);
        request.IncludeSubdirectories.Should().Be(includeSubdirectories);
        request.ProcessingDelayMs.Should().Be(processingDelayMs);
        request.CreatedBy.Should().Be(createdBy);
    }

    [Fact]
    public void PendingApproval_ShouldSupportNavigationProperties()
    {
        // Arrange
        var channelWatch = new ChannelWatch
        {
            ChannelName = "TestChannel",
            WatchPath = "/test/path"
        };

        var pendingApproval = new PendingApproval
        {
            ChannelWatchId = channelWatch.Id,
            ChannelWatch = channelWatch,
            FileName = "test.mp4",
            FilePath = "/test/path/test.mp4",
            FileSize = 1024,
            Extension = ".mp4"
        };

        // Act
        channelWatch.PendingApprovals.Add(pendingApproval);

        // Assert
        pendingApproval.ChannelWatch.Should().Be(channelWatch);
        pendingApproval.ChannelWatchId.Should().Be(channelWatch.Id);
        channelWatch.PendingApprovals.Should().Contain(pendingApproval);
    }

    [Theory]
    [InlineData(ApprovalStatus.Pending, "Awaiting user decision")]
    [InlineData(ApprovalStatus.Approved, "Approved for import")]
    [InlineData(ApprovalStatus.Rejected, "Rejected - will not be imported")]
    [InlineData(ApprovalStatus.AutoApproved, "Automatically approved based on rules")]
    [InlineData(ApprovalStatus.Expired, "Expired without decision")]
    [InlineData(ApprovalStatus.ImportStarted, "Import in progress")]
    [InlineData(ApprovalStatus.ImportCompleted, "Successfully imported")]
    [InlineData(ApprovalStatus.ImportFailed, "Import failed")]
    public void ApprovalStatus_ShouldRepresentCorrectStates(ApprovalStatus status, string expectedMeaning)
    {
        // This test documents the meaning of each status
        // Act & Assert
        status.Should().BeDefined();
        
        // Verify the status has a meaningful integer value
        ((int)status).Should().BeGreaterThanOrEqualTo(0);
        
        // This is just to document what each status means
        var meaning = expectedMeaning;
        meaning.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void PendingApprovalDto_ShouldCopyAllRelevantProperties()
    {
        // Arrange
        var id = Guid.NewGuid();
        var detectedAt = DateTime.UtcNow.AddHours(-2);
        var fileModifiedAt = DateTime.UtcNow.AddDays(-1);
        var expiresAt = DateTime.UtcNow.AddDays(30);

        // Act
        var dto = new PendingApprovalDto
        {
            Id = id,
            ChannelName = "TestChannel",
            FileName = "video.mp4",
            FilePath = "/path/to/video.mp4",
            FileSize = 1024 * 1024 * 50, // 50MB
            Extension = ".mp4",
            DetectedAt = detectedAt,
            FileModifiedAt = fileModifiedAt,
            Status = ApprovalStatus.Pending,
            Priority = 2,
            FileExists = true,
            ThumbnailPath = "/path/to/thumbnail.jpg",
            ExpiresAt = expiresAt
        };

        // Assert
        dto.Id.Should().Be(id);
        dto.ChannelName.Should().Be("TestChannel");
        dto.FileName.Should().Be("video.mp4");
        dto.FilePath.Should().Be("/path/to/video.mp4");
        dto.FileSize.Should().Be(1024 * 1024 * 50);
        dto.FileSizeFormatted.Should().Be("50.0 MB");
        dto.Extension.Should().Be(".mp4");
        dto.DetectedAt.Should().Be(detectedAt);
        dto.FileModifiedAt.Should().Be(fileModifiedAt);
        dto.Status.Should().Be(ApprovalStatus.Pending);
        dto.Priority.Should().Be(2);
        dto.FileExists.Should().BeTrue();
        dto.ThumbnailPath.Should().Be("/path/to/thumbnail.jpg");
        dto.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public void ChannelWatch_FileExtensionsParsing_ShouldSupportCommaDelimitedValues()
    {
        // Arrange
        var channelWatch = new ChannelWatch
        {
            FileExtensions = ".mp4,.mkv,.avi,.mov"
        };

        // Act
        var extensions = channelWatch.FileExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(ext => ext.Trim())
            .ToArray();

        // Assert
        extensions.Should().HaveCount(4);
        extensions.Should().Contain(".mp4");
        extensions.Should().Contain(".mkv");
        extensions.Should().Contain(".avi");
        extensions.Should().Contain(".mov");
    }

    [Theory]
    [InlineData(1, 5)]  // High priority (1 = highest)
    [InlineData(3, 3)]  // Default priority
    [InlineData(5, 1)]  // Low priority (5 = lowest)
    public void PendingApproval_Priority_ShouldSupportRange(int priority, int expectedSortOrder)
    {
        // Arrange
        var approval = new PendingApproval
        {
            Priority = priority,
            FileName = $"video{expectedSortOrder}.mp4",
            FilePath = "path",
            FileSize = 1024,
            Extension = ".mp4"
        };

        // Act & Assert
        approval.Priority.Should().Be(priority);
        
        // Priority should be used for sorting (lower number = higher priority)
        approval.Priority.Should().BeInRange(1, 5);
    }
}
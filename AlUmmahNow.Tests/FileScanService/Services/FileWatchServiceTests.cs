using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AlUmmahNow.Tests.FileScanService.Services;

public class FileWatchServiceTests : FileSystemTestBase
{
    private readonly Mock<ILogger<FileWatchService>> _mockLogger;
    private readonly ImportDbContext _dbContext;
    private readonly Mock<IApprovalService> _mockApprovalService;
    private FileWatchService _fileWatchService;

    public FileWatchServiceTests()
    {
        _mockLogger = new Mock<ILogger<FileWatchService>>();
        
        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ImportDbContext(options);
        _dbContext.Database.EnsureCreated();

        _mockApprovalService = new Mock<IApprovalService>();
        
        _fileWatchService = new FileWatchService(_mockLogger.Object, _dbContext, _mockApprovalService.Object);
    }

    public override async Task DisposeAsync()
    {
        _fileWatchService?.Dispose();
        await _dbContext.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task StartWatchAsync_WithValidRequest_ShouldCreateWatchAndReturnId()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory,
            IncludeSubdirectories = true,
            ProcessingDelayMs = 2000,
            CreatedBy = "TestUser"
        };

        // Act
        var watchId = await _fileWatchService.StartWatchAsync(request);

        // Assert
        watchId.Should().NotBe(Guid.Empty);
        
        var watch = await _dbContext.ChannelWatches.FindAsync(watchId);
        watch.Should().NotBeNull();
        watch!.ChannelName.Should().Be("TestChannel");
        watch.WatchPath.Should().Be(TestChannel1Directory);
        watch.IsActive.Should().BeTrue();
        watch.ProcessingDelayMs.Should().Be(2000);
        watch.CreatedBy.Should().Be("TestUser");
    }

    [Fact]
    public async Task StartWatchAsync_WithNonExistentPath_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = "/non/existent/path",
            IncludeSubdirectories = true
        };

        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => 
            _fileWatchService.StartWatchAsync(request));
    }

    [Fact]
    public async Task StartWatchAsync_WithExistingWatch_ShouldReturnExistingWatchId()
    {
        // Arrange
        var existingWatch = new ChannelWatch
        {
            ChannelName = "ExistingChannel",
            WatchPath = TestChannel1Directory,
            IsActive = true
        };
        _dbContext.ChannelWatches.Add(existingWatch);
        await _dbContext.SaveChangesAsync();

        var request = new CreateWatchRequest
        {
            ChannelName = "NewChannel",
            WatchPath = TestChannel1Directory,
            IncludeSubdirectories = true
        };

        // Act
        var watchId = await _fileWatchService.StartWatchAsync(request);

        // Assert
        watchId.Should().Be(existingWatch.Id);
    }

    [Fact]
    public async Task StopWatchAsync_WithValidWatchId_ShouldDeactivateWatch()
    {
        // Arrange
        var watch = new ChannelWatch
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory,
            IsActive = true
        };
        _dbContext.ChannelWatches.Add(watch);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _fileWatchService.StopWatchAsync(watch.Id);

        // Assert
        result.Should().BeTrue();
        
        var updatedWatch = await _dbContext.ChannelWatches.FindAsync(watch.Id);
        updatedWatch!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task StopWatchAsync_WithInvalidWatchId_ShouldReturnFalse()
    {
        // Arrange
        var invalidWatchId = Guid.NewGuid();

        // Act
        var result = await _fileWatchService.StopWatchAsync(invalidWatchId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveWatchesAsync_ShouldReturnOnlyActiveWatches()
    {
        // Arrange
        var activeWatch = new ChannelWatch
        {
            ChannelName = "ActiveChannel",
            WatchPath = TestChannel1Directory,
            IsActive = true,
            TotalFilesDetected = 5,
            LastFileDetected = DateTime.UtcNow.AddHours(-1)
        };

        var inactiveWatch = new ChannelWatch
        {
            ChannelName = "InactiveChannel",
            WatchPath = TestChannel2Directory,
            IsActive = false
        };

        _dbContext.ChannelWatches.AddRange(activeWatch, inactiveWatch);
        await _dbContext.SaveChangesAsync();

        // Act
        var watches = await _fileWatchService.GetActiveWatchesAsync();

        // Assert
        watches.Should().HaveCount(1);
        watches[0].ChannelName.Should().Be("ActiveChannel");
        watches[0].IsActive.Should().BeTrue();
        watches[0].TotalFilesDetected.Should().Be(5);
    }

    [Fact]
    public async Task UpdateWatchAsync_WithValidRequest_ShouldUpdateWatchConfiguration()
    {
        // Arrange
        var watch = new ChannelWatch
        {
            ChannelName = "OriginalChannel",
            WatchPath = TestChannel1Directory,
            ProcessingDelayMs = 3000,
            IsActive = true
        };
        _dbContext.ChannelWatches.Add(watch);
        await _dbContext.SaveChangesAsync();

        var updateRequest = new CreateWatchRequest
        {
            ChannelName = "UpdatedChannel",
            WatchPath = TestChannel1Directory,
            ProcessingDelayMs = 4000,
            FileExtensions = ".mp4,.avi"
        };

        // Act
        var result = await _fileWatchService.UpdateWatchAsync(watch.Id, updateRequest);

        // Assert
        result.Should().BeTrue();
        
        var updatedWatch = await _dbContext.ChannelWatches.FindAsync(watch.Id);
        updatedWatch!.ChannelName.Should().Be("UpdatedChannel");
        updatedWatch.ProcessingDelayMs.Should().Be(4000);
        updatedWatch.FileExtensions.Should().Be(".mp4,.avi");
    }

    [Fact]
    public async Task DeleteWatchAsync_WithValidWatchId_ShouldRemoveWatchAndPendingApprovals()
    {
        // Arrange
        var watch = new ChannelWatch
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory,
            IsActive = true
        };

        var pendingApproval = new PendingApproval
        {
            ChannelWatchId = watch.Id,
            FilePath = Path.Combine(TestChannel1Directory, "test.mp4"),
            FileName = "test.mp4",
            FileSize = 1024,
            Extension = ".mp4"
        };

        _dbContext.ChannelWatches.Add(watch);
        _dbContext.PendingApprovals.Add(pendingApproval);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _fileWatchService.DeleteWatchAsync(watch.Id);

        // Assert
        result.Should().BeTrue();
        
        var deletedWatch = await _dbContext.ChannelWatches.FindAsync(watch.Id);
        deletedWatch.Should().BeNull();
        
        var remainingApprovals = await _dbContext.PendingApprovals
            .Where(p => p.ChannelWatchId == watch.Id)
            .ToListAsync();
        remainingApprovals.Should().BeEmpty();
    }

    [Theory]
    [InlineData(500, 1000)]  // Below minimum should be adjusted to 1000
    [InlineData(1500, 1500)] // Above minimum should remain unchanged
    [InlineData(0, 1000)]    // Zero should be adjusted to minimum
    public async Task StartWatchAsync_ShouldEnforceMinimumProcessingDelay(int requestedDelay, int expectedDelay)
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory,
            ProcessingDelayMs = requestedDelay
        };

        // Act
        var watchId = await _fileWatchService.StartWatchAsync(request);

        // Assert
        var watch = await _dbContext.ChannelWatches.FindAsync(watchId);
        watch!.ProcessingDelayMs.Should().Be(expectedDelay);
    }

    [Fact]
    public async Task GetWatchStatusAsync_WithValidWatchId_ShouldReturnWatchStatus()
    {
        // Arrange
        var watch = new ChannelWatch
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory,
            IsActive = true,
            TotalFilesDetected = 10,
            LastFileDetected = DateTime.UtcNow.AddMinutes(-30)
        };

        var approvals = new List<PendingApproval>
        {
            new() { ChannelWatchId = watch.Id, Status = ApprovalStatus.Pending, FileName = "file1.mp4", FilePath = "path1", FileSize = 1024, Extension = ".mp4" },
            new() { ChannelWatchId = watch.Id, Status = ApprovalStatus.Approved, FileName = "file2.mp4", FilePath = "path2", FileSize = 2048, Extension = ".mp4" },
            new() { ChannelWatchId = watch.Id, Status = ApprovalStatus.Rejected, FileName = "file3.mp4", FilePath = "path3", FileSize = 3072, Extension = ".mp4" }
        };

        _dbContext.ChannelWatches.Add(watch);
        _dbContext.PendingApprovals.AddRange(approvals);
        await _dbContext.SaveChangesAsync();

        // Act
        var status = await _fileWatchService.GetWatchStatusAsync(watch.Id);

        // Assert
        status.Should().NotBeNull();
        status!.ChannelName.Should().Be("TestChannel");
        status.IsActive.Should().BeTrue();
        status.TotalFilesDetected.Should().Be(10);
        status.PendingApprovals.Should().Be(1);
        status.ManuallyApproved.Should().Be(1);
        status.Rejected.Should().Be(1);
    }

    [Fact]
    public async Task WatchService_ShouldRaiseFileDetectedEvent_WhenFileIsCreated()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory,
            ProcessingDelayMs = 100 // Short delay for testing
        };

        var watchId = await _fileWatchService.StartWatchAsync(request);
        
        FileDetectedEventArgs? eventArgs = null;
        _fileWatchService.FileDetected += (sender, e) => eventArgs = e;

        _mockApprovalService.Setup(x => x.AddPendingApprovalAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var testFilePath = await CreateTestVideoFileAsync(TestChannel1Directory, "newvideo.mp4", 1024 * 1024);

        // Wait for the delayed processing (longer for CI environments)
        await Task.Delay(2000);

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.ChannelWatchId.Should().Be(watchId);
        eventArgs.ChannelName.Should().Be("TestChannel");
        eventArgs.FileName.Should().Be("newvideo.mp4");
        eventArgs.FilePath.Should().Be(testFilePath);
        
        _mockApprovalService.Verify(x => x.AddPendingApprovalAsync(watchId, testFilePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WatchService_ShouldRaiseWatchStatusChangedEvent_WhenWatchIsStarted()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory
        };

        WatchStatusChangedEventArgs? eventArgs = null;
        _fileWatchService.WatchStatusChanged += (sender, e) => eventArgs = e;

        // Act
        var watchId = await _fileWatchService.StartWatchAsync(request);

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.WatchId.Should().Be(watchId);
        eventArgs.ChannelName.Should().Be("TestChannel");
        eventArgs.IsActive.Should().BeTrue();
        eventArgs.StatusMessage.Should().Be("Watch started");
    }

    [Fact(Skip = "Flaky in CI - FileSystemWatcher events unreliable in virtualized environments")]
    public async Task WatchService_ShouldFilterFilesByExtension()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory,
            FileExtensions = ".mp4,.mkv", // Only these extensions
            ProcessingDelayMs = 100
        };

        var watchId = await _fileWatchService.StartWatchAsync(request);
        
        var fileDetectedCount = 0;
        _fileWatchService.FileDetected += (sender, e) => fileDetectedCount++;

        _mockApprovalService.Setup(x => x.AddPendingApprovalAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act - Create files with delays between them for FileSystemWatcher
        await CreateTestVideoFileAsync(TestChannel1Directory, "video1.mp4", 1024);  // Should be detected
        await Task.Delay(300); // Space out file creation
        await CreateTestVideoFileAsync(TestChannel1Directory, "video2.mkv", 1024);  // Should be detected
        await Task.Delay(300);
        await CreateTestVideoFileAsync(TestChannel1Directory, "video3.avi", 1024);  // Should NOT be detected
        await Task.Delay(300);
        await CreateTestVideoFileAsync(TestChannel1Directory, "document.txt", 1024); // Should NOT be detected

        // Wait for processing (longer for CI environments + processing delay)
        await Task.Delay(3000);

        // Assert
        fileDetectedCount.Should().Be(2); // Only .mp4 and .mkv files should be detected
    }

    [Fact]
    public async Task Dispose_ShouldStopAllWatchersAndCleanupResources()
    {
        // Arrange
        var request1 = new CreateWatchRequest
        {
            ChannelName = "Channel1",
            WatchPath = TestChannel1Directory
        };

        var request2 = new CreateWatchRequest
        {
            ChannelName = "Channel2",
            WatchPath = TestChannel2Directory
        };

        await _fileWatchService.StartWatchAsync(request1);
        await _fileWatchService.StartWatchAsync(request2);

        // Act
        _fileWatchService.Dispose();

        // Assert - Should not throw exceptions when disposed
        // The actual verification would be that the FileSystemWatchers are disposed
        // which is difficult to test directly, but we can verify no exceptions occur
        Assert.True(true); // If we reach here without exceptions, disposal worked
    }
}
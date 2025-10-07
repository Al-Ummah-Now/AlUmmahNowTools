using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Hubs;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AlUmmahNow.Tests.FileScanService.Hubs;

public class FileScanHubWatchApprovalTests : FileSystemTestBase
{
    private readonly Mock<ILogger<FileScanHub>> _mockLogger;
    private readonly ImportDbContext _dbContext;
    private readonly Mock<IFileWatchService> _mockFileWatchService;
    private readonly Mock<IApprovalService> _mockApprovalService;
    private readonly Mock<IHubCallerClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly Mock<HubCallerContext> _mockContext;
    private FileScanHub _hub;

    public FileScanHubWatchApprovalTests()
    {
        _mockLogger = new Mock<ILogger<FileScanHub>>();
        
        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ImportDbContext(options);
        _dbContext.Database.EnsureCreated();

        _mockFileWatchService = new Mock<IFileWatchService>();
        _mockApprovalService = new Mock<IApprovalService>();
        _mockClients = new Mock<IHubCallerClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockGroups = new Mock<IGroupManager>();
        _mockContext = new Mock<HubCallerContext>();

        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockGroups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockGroups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockContext.Setup(c => c.ConnectionId).Returns("test-connection-id");

        _hub = new FileScanHub(_mockLogger.Object, _dbContext, _mockFileWatchService.Object, _mockApprovalService.Object)
        {
            Clients = _mockClients.Object,
            Groups = _mockGroups.Object,
            Context = _mockContext.Object
        };
    }

    public override async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task StartWatch_WithValidRequest_ShouldCallServiceAndReturnWatchId()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory,
            IncludeSubdirectories = true
        };

        var expectedWatchId = Guid.NewGuid();
        _mockFileWatchService.Setup(s => s.StartWatchAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedWatchId);

        // Act
        var result = await _hub.StartWatch(request);

        // Assert
        result.Should().Be(expectedWatchId);
        _mockFileWatchService.Verify(s => s.StartWatchAsync(request, It.IsAny<CancellationToken>()), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync("test-connection-id", $"watch-{expectedWatchId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartWatch_WhenServiceThrows_ShouldThrowHubException()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = "/invalid/path"
        };

        _mockFileWatchService.Setup(s => s.StartWatchAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DirectoryNotFoundException("Path not found"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HubException>(() => _hub.StartWatch(request));
        exception.Message.Should().Contain("Failed to start watch");
    }

    [Fact]
    public async Task StopWatch_WithValidWatchId_ShouldCallServiceAndNotifyClients()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        _mockFileWatchService.Setup(s => s.StopWatchAsync(watchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _hub.StopWatch(watchId);

        // Assert
        result.Should().BeTrue();
        _mockFileWatchService.Verify(s => s.StopWatchAsync(watchId, It.IsAny<CancellationToken>()), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("WatchStopped", It.Is<object[]>(args => args[0].Equals(watchId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetActiveWatches_ShouldReturnWatchesFromService()
    {
        // Arrange
        var expectedWatches = new List<WatchStatusDto>
        {
            new() { WatchId = Guid.NewGuid(), ChannelName = "Channel1", WatchPath = "/path1", IsActive = true },
            new() { WatchId = Guid.NewGuid(), ChannelName = "Channel2", WatchPath = "/path2", IsActive = true }
        };

        _mockFileWatchService.Setup(s => s.GetActiveWatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedWatches);

        // Act
        var result = await _hub.GetActiveWatches();

        // Assert
        result.Should().BeEquivalentTo(expectedWatches);
        _mockFileWatchService.Verify(s => s.GetActiveWatchesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetWatchStatus_WithValidWatchId_ShouldReturnStatusFromService()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var expectedStatus = new WatchStatusDto
        {
            WatchId = watchId,
            ChannelName = "TestChannel",
            IsActive = true,
            TotalFilesDetected = 5,
            PendingApprovals = 3
        };

        _mockFileWatchService.Setup(s => s.GetWatchStatusAsync(watchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedStatus);

        // Act
        var result = await _hub.GetWatchStatus(watchId);

        // Assert
        result.Should().BeEquivalentTo(expectedStatus);
        _mockFileWatchService.Verify(s => s.GetWatchStatusAsync(watchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateWatch_WithValidRequest_ShouldCallServiceAndNotifyClients()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var request = new CreateWatchRequest
        {
            ChannelName = "UpdatedChannel",
            WatchPath = TestChannel1Directory
        };

        _mockFileWatchService.Setup(s => s.UpdateWatchAsync(watchId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _hub.UpdateWatch(watchId, request);

        // Assert
        result.Should().BeTrue();
        _mockFileWatchService.Verify(s => s.UpdateWatchAsync(watchId, request, It.IsAny<CancellationToken>()), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("WatchUpdated", It.Is<object[]>(args => args[0].Equals(watchId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteWatch_WithValidWatchId_ShouldCallServiceAndNotifyClients()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        _mockFileWatchService.Setup(s => s.DeleteWatchAsync(watchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _hub.DeleteWatch(watchId);

        // Assert
        result.Should().BeTrue();
        _mockFileWatchService.Verify(s => s.DeleteWatchAsync(watchId, It.IsAny<CancellationToken>()), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("WatchDeleted", It.Is<object[]>(args => args[0].Equals(watchId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingApprovals_ShouldReturnApprovalsFromService()
    {
        // Arrange
        var expectedApprovals = new List<PendingApprovalDto>
        {
            new() { Id = Guid.NewGuid(), ChannelName = "Channel1", FileName = "video1.mp4", Status = ApprovalStatus.Pending },
            new() { Id = Guid.NewGuid(), ChannelName = "Channel2", FileName = "video2.mp4", Status = ApprovalStatus.Pending }
        };

        _mockApprovalService.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedApprovals);

        // Act
        var result = await _hub.GetPendingApprovals();

        // Assert
        result.Should().BeEquivalentTo(expectedApprovals);
        _mockApprovalService.Verify(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingApprovalsByChannel_ShouldReturnChannelSpecificApprovals()
    {
        // Arrange
        var channelName = "TestChannel";
        var expectedApprovals = new List<PendingApprovalDto>
        {
            new() { Id = Guid.NewGuid(), ChannelName = channelName, FileName = "video1.mp4", Status = ApprovalStatus.Pending }
        };

        _mockApprovalService.Setup(s => s.GetPendingApprovalsByChannelAsync(channelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedApprovals);

        // Act
        var result = await _hub.GetPendingApprovalsByChannel(channelName);

        // Assert
        result.Should().BeEquivalentTo(expectedApprovals);
        _mockApprovalService.Verify(s => s.GetPendingApprovalsByChannelAsync(channelName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessApprovalDecision_WithValidRequest_ShouldCallServiceAndNotifyClients()
    {
        // Arrange
        var request = new ApprovalDecisionRequest
        {
            ApprovalIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
            Decision = ApprovalStatus.Approved,
            ApprovedBy = "TestUser",
            Notes = "Test approval"
        };

        _mockApprovalService.Setup(s => s.ProcessApprovalDecisionAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _hub.ProcessApprovalDecision(request);

        // Assert
        result.Should().BeTrue();
        _mockApprovalService.Verify(s => s.ProcessApprovalDecisionAsync(request, It.IsAny<CancellationToken>()), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("ApprovalDecisionProcessed", It.Is<object[]>(args => args[0].Equals(request)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBulkApproval_WithValidRequest_ShouldCallServiceAndNotifyClients()
    {
        // Arrange
        var request = new BulkApprovalRequest
        {
            ChannelName = "TestChannel",
            Decision = ApprovalStatus.Approved,
            ApprovedBy = "BulkUser"
        };

        var expectedProcessedCount = 5;
        _mockApprovalService.Setup(s => s.ProcessBulkApprovalAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedProcessedCount);

        // Act
        var result = await _hub.ProcessBulkApproval(request);

        // Assert
        result.Should().Be(expectedProcessedCount);
        _mockApprovalService.Verify(s => s.ProcessBulkApprovalAsync(request, It.IsAny<CancellationToken>()), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("BulkApprovalProcessed", 
            It.Is<object[]>(args => args[0].Equals(request) && args[1].Equals(expectedProcessedCount)), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetApprovalStatistics_ShouldReturnStatisticsFromService()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var expectedStatistics = new ApprovalStatistics
        {
            TotalPending = 5,
            TotalApproved = 10,
            TotalRejected = 2,
            TotalSizePending = 1024 * 1024 * 100 // 100MB
        };

        _mockApprovalService.Setup(s => s.GetApprovalStatisticsAsync(watchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedStatistics);

        // Act
        var result = await _hub.GetApprovalStatistics(watchId);

        // Assert
        result.Should().BeEquivalentTo(expectedStatistics);
        _mockApprovalService.Verify(s => s.GetApprovalStatisticsAsync(watchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinWatchGroup_ShouldAddConnectionToWatchGroup()
    {
        // Arrange
        var watchId = Guid.NewGuid().ToString();

        // Act
        await _hub.JoinWatchGroup(watchId);

        // Assert
        _mockGroups.Verify(g => g.AddToGroupAsync("test-connection-id", $"watch-{watchId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveWatchGroup_ShouldRemoveConnectionFromWatchGroup()
    {
        // Arrange
        var watchId = Guid.NewGuid().ToString();

        // Act
        await _hub.LeaveWatchGroup(watchId);

        // Assert
        _mockGroups.Verify(g => g.RemoveFromGroupAsync("test-connection-id", $"watch-{watchId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinApprovalsGroup_ShouldAddConnectionToApprovalsGroup()
    {
        // Act
        await _hub.JoinApprovalsGroup();

        // Assert
        _mockGroups.Verify(g => g.AddToGroupAsync("test-connection-id", "approvals", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveApprovalsGroup_ShouldRemoveConnectionFromApprovalsGroup()
    {
        // Act
        await _hub.LeaveApprovalsGroup();

        // Assert
        _mockGroups.Verify(g => g.RemoveFromGroupAsync("test-connection-id", "approvals", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendFileDetected_ShouldNotifyWatchAndApprovalsGroups()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var channelName = "TestChannel";
        var fileName = "video.mp4";
        var filePath = "/path/to/video.mp4";
        var fileSize = 1024L * 1024 * 50; // 50MB

        // Act
        await _hub.SendFileDetected(watchId, channelName, fileName, filePath, fileSize);

        // Assert
        _mockClients.Verify(c => c.Group($"watch-{watchId}"), Times.Once);
        _mockClients.Verify(c => c.Group("approvals"), Times.Once);
        
        _mockClientProxy.Verify(c => c.SendCoreAsync("FileDetected", 
            It.Is<object[]>(args => 
                args[0].Equals(watchId) && 
                args[1].Equals(channelName) && 
                args[2].Equals(fileName) && 
                args[3].Equals(filePath) && 
                args[4].Equals(fileSize)), 
            It.IsAny<CancellationToken>()), Times.Once);
        
        _mockClientProxy.Verify(c => c.SendCoreAsync("NewFileForApproval", 
            It.Is<object[]>(args => 
                args[0].Equals(watchId) && 
                args[1].Equals(channelName) && 
                args[2].Equals(fileName) && 
                args[3].Equals(filePath) && 
                args[4].Equals(fileSize)), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendWatchStatusChanged_ShouldNotifyWatchGroup()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var channelName = "TestChannel";
        var isActive = true;
        var statusMessage = "Watch started";

        // Act
        await _hub.SendWatchStatusChanged(watchId, channelName, isActive, statusMessage);

        // Assert
        _mockClients.Verify(c => c.Group($"watch-{watchId}"), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("WatchStatusChanged", 
            It.Is<object[]>(args => 
                args[0].Equals(watchId) && 
                args[1].Equals(channelName) && 
                args[2].Equals(isActive) && 
                args[3].Equals(statusMessage)), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendApprovalStatusChanged_ShouldNotifyApprovalsGroup()
    {
        // Arrange
        var approvalId = Guid.NewGuid();
        var channelName = "TestChannel";
        var fileName = "video.mp4";
        var oldStatus = ApprovalStatus.Pending;
        var newStatus = ApprovalStatus.Approved;
        var approvedBy = "TestUser";

        // Act
        await _hub.SendApprovalStatusChanged(approvalId, channelName, fileName, oldStatus, newStatus, approvedBy);

        // Assert
        _mockClients.Verify(c => c.Group("approvals"), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("ApprovalStatusChanged", 
            It.Is<object[]>(args => 
                args[0].Equals(approvalId) && 
                args[1].Equals(channelName) && 
                args[2].Equals(fileName) && 
                args[3].Equals(oldStatus) && 
                args[4].Equals(newStatus) && 
                args[5].Equals(approvedBy)), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "Watch service error")]
    [InlineData("TestChannel", "")]
    [InlineData(null, "Service error")]
    public async Task HubMethods_ShouldThrowHubExceptionOnServiceErrors(string? channelName, string errorMessage)
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = channelName ?? "TestChannel",
            WatchPath = TestChannel1Directory
        };

        _mockFileWatchService.Setup(s => s.StartWatchAsync(It.IsAny<CreateWatchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(errorMessage));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HubException>(() => _hub.StartWatch(request));
        exception.Message.Should().Contain("Failed to start watch");
    }

    [Fact]
    public async Task ProcessApprovalDecision_WhenServiceReturnsFalse_ShouldNotNotifyClients()
    {
        // Arrange
        var request = new ApprovalDecisionRequest
        {
            ApprovalIds = new List<Guid> { Guid.NewGuid() },
            Decision = ApprovalStatus.Approved
        };

        _mockApprovalService.Setup(s => s.ProcessApprovalDecisionAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _hub.ProcessApprovalDecision(request);

        // Assert
        result.Should().BeFalse();
        _mockClientProxy.Verify(c => c.SendCoreAsync("ApprovalDecisionProcessed", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessBulkApproval_WhenServiceReturnsZero_ShouldNotNotifyClients()
    {
        // Arrange
        var request = new BulkApprovalRequest
        {
            ChannelName = "TestChannel",
            Decision = ApprovalStatus.Approved
        };

        _mockApprovalService.Setup(s => s.ProcessBulkApprovalAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _hub.ProcessBulkApproval(request);

        // Assert
        result.Should().Be(0);
        _mockClientProxy.Verify(c => c.SendCoreAsync("BulkApprovalProcessed", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using AlUmmahNow.FileScanService.Hubs;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.Tests.FileScanService.Hubs;

/// <summary>
/// Unit tests for FileScanHub SignalR Hub
/// Tests cover connection lifecycle, scan operations, and client notifications
/// </summary>
public class FileScanHubTests : IDisposable
{
    private readonly ImportDbContext _dbContext;
    private readonly Mock<ILogger<FileScanHub>> _mockLogger;
    private readonly Mock<IHubCallerClients> _mockClients;
    private readonly Mock<HubCallerContext> _mockContext;
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<ISingleClientProxy> _mockSingleClientProxy;
    private readonly FileScanHub _hub;

    public FileScanHubTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ImportDbContext(options);

        // Setup mocks
        _mockLogger = new Mock<ILogger<FileScanHub>>();
        _mockClients = new Mock<IHubCallerClients>();
        _mockContext = new Mock<HubCallerContext>();
        _mockGroups = new Mock<IGroupManager>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockSingleClientProxy = new Mock<ISingleClientProxy>();

        // Setup default mock behaviors
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockClients.Setup(c => c.All).Returns(_mockClientProxy.Object);
        _mockClients.Setup(c => c.Caller).Returns(_mockSingleClientProxy.Object);

        // Create hub instance
        _hub = new FileScanHub(_mockLogger.Object, _dbContext)
        {
            Clients = _mockClients.Object,
            Context = _mockContext.Object,
            Groups = _mockGroups.Object
        };
    }

    #region Connection Tests

    [Fact]
    public async Task OnConnectedAsync_ShouldLogConnection()
    {
        // Arrange
        var connectionId = "test-connection-id";
        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

        // Act
        await _hub.OnConnectedAsync();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Client connected")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task OnDisconnectedAsync_ShouldLogDisconnection()
    {
        // Arrange
        var connectionId = "test-connection-id";
        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

        // Act
        await _hub.OnDisconnectedAsync(null);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Client disconnected")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Group Management Tests

    [Fact]
    public async Task JoinScanSession_ShouldAddToGroup()
    {
        // Arrange
        var sessionId = "test-session-123";
        var connectionId = "test-connection";
        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

        // Act
        await _hub.JoinScanSession(sessionId);

        // Assert
        _mockGroups.Verify(
            g => g.AddToGroupAsync(connectionId, $"scan-{sessionId}", default),
            Times.Once);
    }

    [Fact]
    public async Task LeaveScanSession_ShouldRemoveFromGroup()
    {
        // Arrange
        var sessionId = "test-session-123";
        var connectionId = "test-connection";
        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

        // Act
        await _hub.LeaveScanSession(sessionId);

        // Assert
        _mockGroups.Verify(
            g => g.RemoveFromGroupAsync(connectionId, $"scan-{sessionId}", default),
            Times.Once);
    }

    #endregion

    #region Start Scan Tests

    [Fact]
    public async Task StartScan_WithValidPath_ReturnsSessionIdAndCreatesDatabaseEntry()
    {
        // Arrange
        var testPath = Path.GetTempPath();
        var connectionId = "test-connection";
        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

        // Act
        var sessionId = await _hub.StartScan(testPath, true);

        // Assert
        sessionId.Should().NotBeEmpty();

        var session = await _dbContext.ScanSessions.FindAsync(sessionId);
        session.Should().NotBeNull();
        session!.RootPath.Should().Be(testPath);
        session.Status.Should().Be(ScanStatus.Pending);
        session.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        session.PathHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StartScan_ShouldAddConnectionToSessionGroup()
    {
        // Arrange
        var testPath = Path.GetTempPath();
        var connectionId = "test-connection";
        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

        // Act
        var sessionId = await _hub.StartScan(testPath, true);

        // Assert
        _mockGroups.Verify(
            g => g.AddToGroupAsync(connectionId, $"scan-{sessionId}", default),
            Times.Once);
    }

    [Fact]
    public async Task StartScan_ShouldNotifyClientsInGroup()
    {
        // Arrange
        var testPath = Path.GetTempPath();
        var connectionId = "test-connection";
        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

        // Act
        var sessionId = await _hub.StartScan(testPath, true);

        // Assert
        _mockClientProxy.Verify(
            c => c.SendCoreAsync(
                "ScanStarted",
                It.Is<object[]>(args =>
                    args.Length == 2 &&
                    (Guid)args[0] == sessionId &&
                    args[1].ToString() == testPath),
                default),
            Times.Once);
    }

    [Fact]
    public async Task StartScan_WithEmptyPath_ThrowsHubException()
    {
        // Arrange
        var emptyPath = "";
        _mockContext.Setup(c => c.ConnectionId).Returns("test-connection");

        // Act & Assert
        await _hub.Invoking(h => h.StartScan(emptyPath, true))
            .Should().ThrowAsync<HubException>()
            .WithMessage("*Invalid or non-existent folder path*");
    }

    [Fact]
    public async Task StartScan_WithNonExistentPath_ThrowsHubException()
    {
        // Arrange
        var invalidPath = "C:\\NonExistent\\Path\\That\\Does\\Not\\Exist";
        _mockContext.Setup(c => c.ConnectionId).Returns("test-connection");

        // Act & Assert
        await _hub.Invoking(h => h.StartScan(invalidPath, true))
            .Should().ThrowAsync<HubException>()
            .WithMessage("*Invalid or non-existent folder path*");
    }

    [Fact]
    public async Task StartScan_ShouldLogScanCreation()
    {
        // Arrange
        var testPath = Path.GetTempPath();
        _mockContext.Setup(c => c.ConnectionId).Returns("test-connection");

        // Act
        var sessionId = await _hub.StartScan(testPath, true);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("Scan session") &&
                    v.ToString()!.Contains("created and queued")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Get Scan Status Tests

    [Fact]
    public async Task GetScanStatus_WithValidSessionId_ReturnsCorrectStatus()
    {
        // Arrange
        var session = new ScanSession
        {
            Id = Guid.NewGuid(),
            RootPath = "C:\\Test",
            Status = ScanStatus.InProgress,
            FilesFound = 42,
            TotalSize = 1024000,
            ProgressPercentage = 50.5,
            CurrentOperation = "Scanning files..."
        };
        _dbContext.ScanSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        var status = await _hub.GetScanStatus(session.Id);

        // Assert
        status.Should().NotBeNull();
        status.SessionId.Should().Be(session.Id);
        status.Status.Should().Be("InProgress");
        status.RootPath.Should().Be("C:\\Test");
        status.FilesScanned.Should().Be(42);
        status.TotalSize.Should().Be(1024000);
        status.ProgressPercentage.Should().Be(50.5);
        status.CurrentOperation.Should().Be("Scanning files...");
    }

    [Fact]
    public async Task GetScanStatus_WithNonExistentSessionId_ThrowsHubException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        await _hub.Invoking(h => h.GetScanStatus(nonExistentId))
            .Should().ThrowAsync<HubException>()
            .WithMessage($"*Scan session {nonExistentId} not found*");
    }

    #endregion

    #region Get All Sessions Tests

    [Fact]
    public async Task GetAllSessions_ReturnsAllSessionsOrderedByCreatedDate()
    {
        // Arrange
        var sessions = new[]
        {
            new ScanSession { Id = Guid.NewGuid(), RootPath = "C:\\Path1", Status = ScanStatus.Completed, CreatedAt = DateTime.UtcNow.AddHours(-2), FilesFound = 10 },
            new ScanSession { Id = Guid.NewGuid(), RootPath = "C:\\Path2", Status = ScanStatus.InProgress, CreatedAt = DateTime.UtcNow.AddHours(-1), FilesFound = 5 },
            new ScanSession { Id = Guid.NewGuid(), RootPath = "C:\\Path3", Status = ScanStatus.Pending, CreatedAt = DateTime.UtcNow, FilesFound = 0 }
        };
        _dbContext.ScanSessions.AddRange(sessions);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _hub.GetAllSessions();

        // Assert
        result.Should().HaveCount(3);
        result[0].RootPath.Should().Be("C:\\Path3"); // Most recent first
        result[1].RootPath.Should().Be("C:\\Path2");
        result[2].RootPath.Should().Be("C:\\Path1");
    }

    [Fact]
    public async Task GetAllSessions_WhenNoSessions_ReturnsEmptyList()
    {
        // Act
        var result = await _hub.GetAllSessions();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion

    #region Cancel Scan Tests

    [Fact]
    public async Task CancelScan_WithValidInProgressScan_UpdatesStatusAndNotifies()
    {
        // Arrange
        var session = new ScanSession
        {
            Id = Guid.NewGuid(),
            RootPath = "C:\\Test",
            Status = ScanStatus.InProgress,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ScanSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        await _hub.CancelScan(session.Id);

        // Assert
        var updatedSession = await _dbContext.ScanSessions.FindAsync(session.Id);
        updatedSession!.Status.Should().Be(ScanStatus.Cancelled);
        updatedSession.CompletedAt.Should().NotBeNull();
        updatedSession.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        updatedSession.CurrentOperation.Should().Be("Cancelled by user");

        _mockClientProxy.Verify(
            c => c.SendCoreAsync(
                "ScanCancelled",
                It.Is<object[]>(args => args.Length == 1 && (Guid)args[0] == session.Id),
                default),
            Times.Once);
    }

    [Fact]
    public async Task CancelScan_WithPendingScan_SuccessfullyCancels()
    {
        // Arrange
        var session = new ScanSession
        {
            Id = Guid.NewGuid(),
            RootPath = "C:\\Test",
            Status = ScanStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ScanSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        await _hub.CancelScan(session.Id);

        // Assert
        var updatedSession = await _dbContext.ScanSessions.FindAsync(session.Id);
        updatedSession!.Status.Should().Be(ScanStatus.Cancelled);
    }

    [Fact]
    public async Task CancelScan_WithCompletedScan_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = new ScanSession
        {
            Id = Guid.NewGuid(),
            RootPath = "C:\\Test",
            Status = ScanStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ScanSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        await _hub.Invoking(h => h.CancelScan(session.Id))
            .Should().ThrowAsync<HubException>()
            .WithMessage("*Cannot cancel scan in status: Completed*");
    }

    [Fact]
    public async Task CancelScan_WithNonExistentSession_ThrowsHubException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        await _hub.Invoking(h => h.CancelScan(nonExistentId))
            .Should().ThrowAsync<HubException>()
            .WithMessage($"*Scan session {nonExistentId} not found*");
    }

    #endregion

    #region Progress Notification Tests

    [Fact]
    public async Task SendProgressUpdate_ShouldNotifyGroupClients()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var progress = new { FilesScanned = 10, CurrentFile = "test.mp4", ProgressPercentage = 25.5 };

        // Act
        await _hub.SendProgressUpdate(sessionId, progress);

        // Assert
        _mockClients.Verify(c => c.Group($"scan-{sessionId}"), Times.Once);
        _mockClientProxy.Verify(
            c => c.SendCoreAsync(
                "ScanProgress",
                It.Is<object[]>(args => args.Length == 2 && (Guid)args[0] == sessionId),
                default),
            Times.Once);
    }

    [Fact]
    public async Task SendScanCompleted_ShouldNotifyGroupClients()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var fileCount = 100;

        // Act
        await _hub.SendScanCompleted(sessionId, fileCount);

        // Assert
        _mockClientProxy.Verify(
            c => c.SendCoreAsync(
                "ScanCompleted",
                It.Is<object[]>(args => args.Length == 2 && (Guid)args[0] == sessionId && (int)args[1] == fileCount),
                default),
            Times.Once);
    }

    [Fact]
    public async Task SendScanFailed_ShouldNotifyGroupClients()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var errorMessage = "Test error message";

        // Act
        await _hub.SendScanFailed(sessionId, errorMessage);

        // Assert
        _mockClientProxy.Verify(
            c => c.SendCoreAsync(
                "ScanFailed",
                It.Is<object[]>(args => args.Length == 2 && (Guid)args[0] == sessionId && args[1].ToString() == errorMessage),
                default),
            Times.Once);
    }

    #endregion

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

using Xunit;
using Moq;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace AlUmmahNow.Tests.FileScanService.Services;

/// <summary>
/// Tests for FileSystemWatcher buffer overflow protection
/// </summary>
public class FileWatchServiceBufferOverflowTests : IDisposable
{
    private readonly Mock<ILogger<FileWatchService>> _mockLogger;
    private readonly Mock<IApprovalService> _mockApprovalService;
    private readonly ImportDbContext _dbContext;
    private readonly SqliteImportDb _sqlite = new();
    private readonly DbContextOptions<ImportDbContext> _options;
    private readonly FileWatchService _fileWatchService;
    private readonly string _testDirectory;

    public FileWatchServiceBufferOverflowTests()
    {
        _mockLogger = new Mock<ILogger<FileWatchService>>();
        _mockApprovalService = new Mock<IApprovalService>();

        var options = _sqlite.Options;
        _options = options;
        _dbContext = new ImportDbContext(options);

        _fileWatchService = new FileWatchService(_mockLogger.Object, TestScopes.For(_options, _mockApprovalService.Object));

        _testDirectory = Path.Combine(Path.GetTempPath(), "FileWatchBufferOverflowTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task FileWatcher_HasMaximumBufferSize()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "BufferSizeTestChannel",
            WatchPath = _testDirectory,
            ProcessingDelayMs = 100
        };

        // Act
        var watchId = await _fileWatchService.StartWatchAsync(request);

        // Assert - Get the internal watcher using reflection
        var watchersField = typeof(FileWatchService).GetField("_watchers",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var watchers = watchersField?.GetValue(_fileWatchService) as System.Collections.Concurrent.ConcurrentDictionary<Guid, FileSystemWatcher>;

        watchers.Should().NotBeNull();
        watchers.Should().ContainKey(watchId);

        var watcher = watchers![watchId];
        watcher.InternalBufferSize.Should().Be(64 * 1024, "Buffer should be set to maximum 64KB");

        // Cleanup
        await _fileWatchService.StopWatchAsync(watchId);
    }

    [Fact]
    public async Task FileWatcher_ErrorHandler_LogsBufferOverflow()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "ErrorHandlerTestChannel",
            WatchPath = _testDirectory,
            ProcessingDelayMs = 100
        };

        var watchId = await _fileWatchService.StartWatchAsync(request);
        var statusChangeEvents = new List<WatchStatusChangedEventArgs>();
        _fileWatchService.WatchStatusChanged += (sender, e) => statusChangeEvents.Add(e);

        // Get the watch configuration
        var watchConfig = await _dbContext.ChannelWatches.FindAsync(watchId);
        watchConfig.Should().NotBeNull();

        // Act - Call the error handler directly with a buffer overflow exception
        var bufferOverflowException = new InternalBufferOverflowException("Buffer overflow simulation");
        _fileWatchService.HandleFileWatcherError(watchConfig!, bufferOverflowException);

        await Task.Delay(100); // Give time for event to process

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Buffer overflow") || v.ToString()!.Contains("buffer overflow")),
                It.IsAny<InternalBufferOverflowException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Buffer overflow should be logged as error");

        // Verify status change event was raised
        statusChangeEvents.Should().Contain(e =>
            e.WatchId == watchId &&
            e.IsActive == true &&
            e.StatusMessage != null &&
            e.StatusMessage.Contains("Buffer overflow", StringComparison.OrdinalIgnoreCase));

        // Cleanup
        await _fileWatchService.StopWatchAsync(watchId);
    }

    [Fact]
    public async Task FileWatcher_BufferOverflow_WatcherRemainsActive()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "ActiveAfterOverflowChannel",
            WatchPath = _testDirectory,
            ProcessingDelayMs = 100
        };

        var watchId = await _fileWatchService.StartWatchAsync(request);

        // Get the watch configuration and watcher
        var watchConfig = await _dbContext.ChannelWatches.FindAsync(watchId);
        watchConfig.Should().NotBeNull();

        var watchersField = typeof(FileWatchService).GetField("_watchers",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var watchers = watchersField?.GetValue(_fileWatchService) as System.Collections.Concurrent.ConcurrentDictionary<Guid, FileSystemWatcher>;
        var watcher = watchers![watchId];

        // Act - Call the error handler with buffer overflow exception
        var bufferOverflowException = new InternalBufferOverflowException("Buffer overflow");
        _fileWatchService.HandleFileWatcherError(watchConfig!, bufferOverflowException);

        await Task.Delay(100);

        // Assert - Watcher should still be active
        watcher.EnableRaisingEvents.Should().BeTrue("Watcher should remain active after buffer overflow");

        var status = await _fileWatchService.GetWatchStatusAsync(watchId);
        status.Should().NotBeNull();
        status!.IsActive.Should().BeTrue("Watch status should still be active");

        // Cleanup
        await _fileWatchService.StopWatchAsync(watchId);
    }

    [Fact]
    public async Task FileWatcher_NonBufferOverflowError_LoggedDifferently()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "OtherErrorChannel",
            WatchPath = _testDirectory,
            ProcessingDelayMs = 100
        };

        var watchId = await _fileWatchService.StartWatchAsync(request);

        // Get the watch configuration
        var watchConfig = await _dbContext.ChannelWatches.FindAsync(watchId);
        watchConfig.Should().NotBeNull();

        // Act - Call the error handler with a different error (not buffer overflow)
        var otherException = new InvalidOperationException("Some other error");
        _fileWatchService.HandleFileWatcherError(watchConfig!, otherException);

        await Task.Delay(100);

        // Assert - Should log as generic file watcher error, not buffer overflow
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("File watcher error")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Non-buffer overflow errors should be logged differently");

        // Cleanup
        await _fileWatchService.StopWatchAsync(watchId);
    }

    [Fact]
    public void FileWatcher_BufferSizeDocumentation()
    {
        // This test documents the buffer size choice
        const int maxBufferSize = FileWatchService.MaxWatcherBufferSize;
        const int defaultBufferSize = 8 * 1024;

        // Assert - Document the decision
        maxBufferSize.Should().Be(65536, "64KB is the maximum allowed buffer size for FileSystemWatcher");
        (maxBufferSize / defaultBufferSize).Should().Be(8, "We're using 8x the default buffer size");

        // This is a documentation test to ensure the rationale is clear:
        // - Default buffer: 8KB (can miss events in high-activity scenarios)
        // - Our buffer: 64KB (maximum allowed, reduces overflow probability by 8x)
        // - Trade-off: +56KB memory per watcher for significantly better reliability
    }

    public void Dispose()
    {
        _fileWatchService?.Dispose();
        _dbContext?.Dispose();
        _sqlite.Dispose();

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

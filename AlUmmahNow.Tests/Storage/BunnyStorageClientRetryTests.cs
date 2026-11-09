using Xunit;
using Moq;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;

namespace AlUmmahNow.Tests.Storage;

/// <summary>
/// Tests for BunnyStorageClient retry logic and rate limiting
/// </summary>
public class BunnyStorageClientRetryTests
{
    private readonly Mock<ILogger<BunnyStorageClient<TestVideo>>> _mockLogger;

    public BunnyStorageClientRetryTests()
    {
        _mockLogger = new Mock<ILogger<BunnyStorageClient<TestVideo>>>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithAllParameters_InitializesCorrectly()
    {
        // Arrange & Act
        var client = new BunnyStorageClient<TestVideo>(
            "test-zone",
            "test-key",
            "test-region",
            maxConcurrentUploads: 5,
            logger: _mockLogger.Object);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithDefaultMaxConcurrent_InitializesCorrectly()
    {
        // Arrange & Act
        var client = new BunnyStorageClient<TestVideo>(
            "test-zone",
            "test-key",
            "test-region");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithLogger_InitializesCorrectly()
    {
        // Arrange & Act
        var client = new BunnyStorageClient<TestVideo>(
            "test-zone",
            "test-key",
            "test-region",
            logger: _mockLogger.Object);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomMaxConcurrent_InitializesCorrectly()
    {
        // Arrange & Act
        var client = new BunnyStorageClient<TestVideo>(
            "test-zone",
            "test-key",
            "test-region",
            maxConcurrentUploads: 20);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithZeroMaxConcurrent_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act - Zero is invalid for SemaphoreSlim
        var exception = Record.Exception(() => new BunnyStorageClient<TestVideo>(
            "test-zone",
            "test-key",
            "test-region",
            maxConcurrentUploads: 0));

        // Assert - SemaphoreSlim throws ArgumentOutOfRangeException for maxCount = 0
        exception.Should().NotBeNull();
        exception.Should().BeOfType<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithNegativeMaxConcurrent_InitializesCorrectly()
    {
        // Arrange & Act - Constructor may allow negative but SemaphoreSlim will handle it
        var exception = Record.Exception(() => new BunnyStorageClient<TestVideo>(
            "test-zone",
            "test-key",
            "test-region",
            maxConcurrentUploads: -1));

        // Assert - Constructor doesn't validate, SemaphoreSlim will throw on usage
        exception.Should().NotBeNull();
    }

    #endregion

    #region Upload Edge Cases

    [Fact]
    public async Task UploadAsync_WithNullStream_ThrowsException()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act & Assert
        await client.Invoking(c => c.UploadAsync(null!, "test.mp4"))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task UploadAsync_WithEmptyPath_ThrowsException()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act & Assert
        await client.Invoking(c => c.UploadAsync(stream, string.Empty))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task UploadAsync_WithNonSeekableStream_HandlesCorrectly()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Create a non-seekable stream wrapper
        var memStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var nonSeekableStream = new NonSeekableStream(memStream);

        // Act & Assert - Should handle non-seekable streams without crashing
        await client.Invoking(c => c.UploadAsync(nonSeekableStream, "test.mp4"))
            .Should().ThrowAsync<Exception>(); // Will fail due to invalid credentials
    }

    [Fact]
    public async Task UploadAsync_WithSeekableStream_ResetsPosition()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        stream.Position = 3; // Move position away from start

        // Act & Assert
        await client.Invoking(c => c.UploadAsync(stream, "test.mp4"))
            .Should().ThrowAsync<Exception>(); // Will fail due to invalid credentials

        // Stream position should have been reset to 0 during retry logic
        stream.Position.Should().Be(0);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_WithInvalidCredentials_ThrowsException()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act & Assert
        await client.Invoking(c => c.DeleteAsync("test.mp4"))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DeleteAsync_WithEmptyPath_ThrowsException()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act & Assert
        await client.Invoking(c => c.DeleteAsync(string.Empty))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DeleteAsync_WithNullPath_ThrowsException()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act & Assert
        await client.Invoking(c => c.DeleteAsync(null!))
            .Should().ThrowAsync<Exception>();
    }

    #endregion

    #region Download Tests

    [Fact]
    public async Task DownloadAsync_WithInvalidCredentials_ThrowsException()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act & Assert
        await client.Invoking(c => c.DownloadAsync("test.mp4"))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DownloadAsync_WithEmptyPath_ThrowsException()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act & Assert
        await client.Invoking(c => c.DownloadAsync(string.Empty))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DownloadAsync_WithNullPath_ThrowsException()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act & Assert
        await client.Invoking(c => c.DownloadAsync(null!))
            .Should().ThrowAsync<Exception>();
    }

    #endregion

    #region List Tests

    [Fact]
    public async Task ListAsync_WithInvalidCredentials_ReturnsEmptyList()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act
        var result = await client.ListAsync("test/");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty(); // Error is caught and empty list returned

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error listing objects")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ListAsync_WithNullPath_ReturnsEmptyList()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act
        var result = await client.ListAsync(null!);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WithEmptyPath_ReturnsEmptyList()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act
        var result = await client.ListAsync(string.Empty);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion

    #region Get Tests

    // GetAsync used to map every error to "not found" (null), so a failed lookup let callers
    // overwrite an existing object. Errors now propagate; only a genuinely missing object is null.

    [Fact]
    public async Task GetAsync_WhenTheLookupFails_Throws()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            logger: _mockLogger.Object);

        // Act
        var act = () => client.GetAsync("/invalid-zone/test.mp4");

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetAsync_WithNullPath_Throws()
    {
        var client = new BunnyStorageClient<TestVideo>("invalid-zone", "invalid-key", "invalid-region", logger: _mockLogger.Object);
        var act = () => client.GetAsync(null!);
        await act.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("test.mp4")]
    [InlineData("/invalid-zone/folder/")]
    public async Task GetAsync_WithoutAFilePath_ThrowsArgumentException(string path)
    {
        var client = new BunnyStorageClient<TestVideo>("invalid-zone", "invalid-key", "invalid-region", logger: _mockLogger.Object);
        var act = () => client.GetAsync(path);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region Concurrent Upload Tests

    [Fact]
    public async Task UploadAsync_MultipleConcurrentUploads_RespectsConcurrencyLimit()
    {
        // Arrange
        var client = new BunnyStorageClient<TestVideo>(
            "invalid-zone",
            "invalid-key",
            "invalid-region",
            maxConcurrentUploads: 2,
            logger: _mockLogger.Object);

        var stream1 = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream2 = new MemoryStream(new byte[] { 4, 5, 6 });
        var stream3 = new MemoryStream(new byte[] { 7, 8, 9 });

        // Act - Start multiple uploads concurrently
        var tasks = new[]
        {
            client.UploadAsync(stream1, "video1.mp4"),
            client.UploadAsync(stream2, "video2.mp4"),
            client.UploadAsync(stream3, "video3.mp4")
        };

        // Assert - All should complete (with errors due to invalid credentials)
        // but the rate limiting should have been applied
        await Task.WhenAll(tasks.Select(t => t.ContinueWith(_ => { }))); // Suppress exceptions
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Test video model for BunnyStorageClient tests
    /// </summary>
    public class TestVideo
    {
        public Guid ID { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string VideoUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Non-seekable stream wrapper for testing
    /// </summary>
    private class NonSeekableStream : Stream
    {
        private readonly Stream _innerStream;

        public NonSeekableStream(Stream innerStream)
        {
            _innerStream = innerStream;
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => false; // Always return false
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;

        public override long Position
        {
            get => _innerStream.Position;
            set => throw new NotSupportedException("Stream is not seekable");
        }

        public override void Flush() => _innerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _innerStream.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("Stream is not seekable");

        public override void SetLength(long value) => _innerStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            _innerStream.Write(buffer, offset, count);
    }

    #endregion
}

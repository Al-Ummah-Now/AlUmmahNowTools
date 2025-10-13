using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AlUmmahNow.Tests.FileScanService.Services;

/// <summary>
/// Advanced tests for HashCalculator focusing on file hashing and edge cases
/// </summary>
public class HashCalculatorAdvancedTests : FileSystemTestBase
{
    private readonly HashCalculator _hashCalculator;
    private readonly Mock<ILogger<HashCalculator>> _mockLogger;

    // SHA256 hash of an empty string/file
    private const string EmptyStringSha256Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public HashCalculatorAdvancedTests()
    {
        _mockLogger = new Mock<ILogger<HashCalculator>>();
        _hashCalculator = new HashCalculator(_mockLogger.Object);
    }

    #region CalculateFileHashAsync Tests

    [Fact]
    public async Task CalculateFileHashAsync_WithValidFile_ReturnsCorrectHash()
    {
        // Arrange
        var filePath = Path.Combine(TestChannel1Directory, "test_file.txt");
        var content = "This is test content for file hashing";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var hash1 = await _hashCalculator.CalculateFileHashAsync(filePath);
        var hash2 = await _hashCalculator.CalculateFileHashAsync(filePath);

        // Assert
        hash1.Should().NotBeNullOrEmpty();
        hash1.Should().HaveLength(64); // SHA256 hex string length
        hash1.Should().Be(hash2); // Same file should produce same hash
        hash1.Should().MatchRegex("^[a-f0-9]{64}$"); // Valid hex string
    }

    [Fact]
    public async Task CalculateFileHashAsync_WithDifferentFiles_ReturnsDifferentHashes()
    {
        // Arrange
        var file1Path = Path.Combine(TestChannel1Directory, "file1.txt");
        var file2Path = Path.Combine(TestChannel1Directory, "file2.txt");
        await File.WriteAllTextAsync(file1Path, "First file content");
        await File.WriteAllTextAsync(file2Path, "Second file content");

        // Act
        var hash1 = await _hashCalculator.CalculateFileHashAsync(file1Path);
        var hash2 = await _hashCalculator.CalculateFileHashAsync(file2Path);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public async Task CalculateFileHashAsync_WithEmptyFile_ReturnsValidHash()
    {
        // Arrange
        var filePath = Path.Combine(TestChannel1Directory, "empty.txt");
        await File.WriteAllTextAsync(filePath, string.Empty);

        // Act
        var hash = await _hashCalculator.CalculateFileHashAsync(filePath);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
        hash.Should().Be(EmptyStringSha256Hash);
    }

    [Fact]
    public async Task CalculateFileHashAsync_WithLargeFile_ReturnsValidHash()
    {
        // Arrange
        var filePath = Path.Combine(TestChannel1Directory, "large_file.txt");
        // Create a 1MB file
        var largeContent = new string('x', 1024 * 1024);
        await File.WriteAllTextAsync(filePath, largeContent);

        // Act
        var hash = await _hashCalculator.CalculateFileHashAsync(filePath);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public async Task CalculateFileHashAsync_WithBinaryFile_ReturnsValidHash()
    {
        // Arrange
        var filePath = Path.Combine(TestChannel1Directory, "binary.dat");
        var binaryData = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD };
        await File.WriteAllBytesAsync(filePath, binaryData);

        // Act
        var hash1 = await _hashCalculator.CalculateFileHashAsync(filePath);
        var hash2 = await _hashCalculator.CalculateFileHashAsync(filePath);

        // Assert
        hash1.Should().NotBeNullOrEmpty();
        hash1.Should().Be(hash2);
        hash1.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public async Task CalculateFileHashAsync_WithNonExistentFile_ThrowsException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(TestChannel1Directory, "nonexistent.txt");

        // Act & Assert
        await _hashCalculator.Invoking(h => h.CalculateFileHashAsync(nonExistentFile))
            .Should().ThrowAsync<Exception>();

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error calculating hash for file")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CalculateFileHashAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var filePath = Path.Combine(TestChannel1Directory, "test.txt");
        // Create a larger file to give time for cancellation
        var content = new string('x', 1024 * 1024); // 1MB
        await File.WriteAllTextAsync(filePath, content);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await _hashCalculator.Invoking(h => h.CalculateFileHashAsync(filePath, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CalculateFileHashAsync_WithLockedFile_ThrowsException()
    {
        // Arrange
        var filePath = Path.Combine(TestChannel1Directory, "locked.txt");
        await File.WriteAllTextAsync(filePath, "test content");

        // Lock the file by opening it exclusively
        using var lockStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

        // Act & Assert
        await _hashCalculator.Invoking(h => h.CalculateFileHashAsync(filePath))
            .Should().ThrowAsync<IOException>();

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error calculating hash for file")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CalculateFileHashAsync_WithSpecialCharactersInPath_HandlesCorrectly()
    {
        // Arrange
        var specialDir = Path.Combine(TestRootDirectory, "Special (Chars) [Test]");
        Directory.CreateDirectory(specialDir);
        var filePath = Path.Combine(specialDir, "file (1).txt");
        await File.WriteAllTextAsync(filePath, "test content");

        // Act
        var hash = await _hashCalculator.CalculateFileHashAsync(filePath);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public async Task CalculateFileHashAsync_WithIdenticalContent_ProducesSameHash()
    {
        // Arrange
        var file1Path = Path.Combine(TestChannel1Directory, "file1.txt");
        var file2Path = Path.Combine(TestChannel1Directory, "file2.txt");
        var identicalContent = "This content is identical in both files";
        await File.WriteAllTextAsync(file1Path, identicalContent);
        await File.WriteAllTextAsync(file2Path, identicalContent);

        // Act
        var hash1 = await _hashCalculator.CalculateFileHashAsync(file1Path);
        var hash2 = await _hashCalculator.CalculateFileHashAsync(file2Path);

        // Assert
        hash1.Should().Be(hash2); // Same content should produce same hash
    }

    #endregion

    #region CalculateContentHashAsync - Additional Edge Cases

    [Fact]
    public async Task CalculateContentHashAsync_WithUnicodeContent_ReturnsValidHash()
    {
        // Arrange
        var unicodeContent = "Hello 世界 مرحبا दुनिया 🌍";

        // Act
        var hash = await _hashCalculator.CalculateContentHashAsync(unicodeContent);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public async Task CalculateContentHashAsync_WithNewlineVariations_ProducesDifferentHashes()
    {
        // Arrange
        var contentWithLF = "Line1\nLine2";
        var contentWithCRLF = "Line1\r\nLine2";

        // Act
        var hashLF = await _hashCalculator.CalculateContentHashAsync(contentWithLF);
        var hashCRLF = await _hashCalculator.CalculateContentHashAsync(contentWithCRLF);

        // Assert
        hashLF.Should().NotBe(hashCRLF); // Different line endings should produce different hashes
    }

    [Fact]
    public async Task CalculateContentHashAsync_WithWhitespaceOnly_ReturnsValidHash()
    {
        // Arrange
        var whitespaceContent = "   \t\n\r   ";

        // Act
        var hash = await _hashCalculator.CalculateContentHashAsync(whitespaceContent);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public async Task CalculateContentHashAsync_WithVeryLongContent_ReturnsValidHash()
    {
        // Arrange
        var longContent = new string('A', 10 * 1024 * 1024); // 10MB string

        // Act
        var hash = await _hashCalculator.CalculateContentHashAsync(longContent);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public async Task CalculateContentHashAsync_WithSpecialCharacters_ReturnsValidHash()
    {
        // Arrange
        var specialContent = "!@#$%^&*()_+-=[]{}\\|;':\",./<>?`~";

        // Act
        var hash = await _hashCalculator.CalculateContentHashAsync(specialContent);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    #endregion

    #region CalculatePathHash - Additional Edge Cases

    [Fact]
    public void CalculatePathHash_WithEmptyPath_ReturnsValidHash()
    {
        // Arrange
        var emptyPath = string.Empty;

        // Act
        var hash = _hashCalculator.CalculatePathHash(emptyPath);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void CalculatePathHash_WithUnixPath_ReturnsValidHash()
    {
        // Arrange
        var unixPath = "/home/user/videos/test.mp4";

        // Act
        var hash = _hashCalculator.CalculatePathHash(unixPath);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void CalculatePathHash_WithWindowsPath_ReturnsValidHash()
    {
        // Arrange
        var windowsPath = @"C:\Users\Videos\test.mp4";

        // Act
        var hash = _hashCalculator.CalculatePathHash(windowsPath);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void CalculatePathHash_WithSpecialCharacters_ReturnsValidHash()
    {
        // Arrange
        var specialPath = @"C:\Test (Files)\Video [HD]\file-name_01.mp4";

        // Act
        var hash = _hashCalculator.CalculatePathHash(specialPath);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void CalculatePathHash_WithUnicodePath_ReturnsValidHash()
    {
        // Arrange
        var unicodePath = @"C:\用户\视频\test.mp4";

        // Act
        var hash = _hashCalculator.CalculatePathHash(unicodePath);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void CalculatePathHash_WithVeryLongPath_ReturnsValidHash()
    {
        // Arrange
        var longPath = @"C:\" + new string('a', 300) + @"\file.mp4";

        // Act
        var hash = _hashCalculator.CalculatePathHash(longPath);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void CalculatePathHash_WithForwardSlashesVsBackslashes_ReturnsDifferentHashes()
    {
        // Arrange
        var pathWithBackslash = @"C:\Test\Video.mp4";
        var pathWithForwardSlash = "C:/Test/Video.mp4";

        // Act
        var hash1 = _hashCalculator.CalculatePathHash(pathWithBackslash);
        var hash2 = _hashCalculator.CalculatePathHash(pathWithForwardSlash);

        // Assert
        hash1.Should().NotBe(hash2); // Different path separators should produce different hashes
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // Act & Assert - HashCalculator accepts null logger
        var exception = Record.Exception(() => new HashCalculator(null!));
        exception.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithValidLogger_CreatesInstance()
    {
        // Arrange & Act
        var calculator = new HashCalculator(_mockLogger.Object);

        // Assert
        calculator.Should().NotBeNull();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FileHashAndContentHash_WithSameData_ProduceSameHash()
    {
        // Arrange
        var content = "This is the exact same content";
        var filePath = Path.Combine(TestChannel1Directory, "same_content.txt");
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var fileHash = await _hashCalculator.CalculateFileHashAsync(filePath);
        var contentHash = await _hashCalculator.CalculateContentHashAsync(content);

        // Assert
        fileHash.Should().Be(contentHash); // File containing content should have same hash as content
    }

    [Fact]
    public async Task CalculateFileHashAsync_CalledMultipleTimes_ProducesConsistentResults()
    {
        // Arrange
        var filePath = Path.Combine(TestChannel1Directory, "consistent.txt");
        await File.WriteAllTextAsync(filePath, "Consistent content");

        // Act
        var hash1 = await _hashCalculator.CalculateFileHashAsync(filePath);
        var hash2 = await _hashCalculator.CalculateFileHashAsync(filePath);
        var hash3 = await _hashCalculator.CalculateFileHashAsync(filePath);

        // Assert
        hash1.Should().Be(hash2);
        hash2.Should().Be(hash3);
    }

    [Fact]
    public async Task CalculateFileHashAsync_AfterFileModification_ProducesDifferentHash()
    {
        // Arrange
        var filePath = Path.Combine(TestChannel1Directory, "modified.txt");
        await File.WriteAllTextAsync(filePath, "Original content");
        var hashBefore = await _hashCalculator.CalculateFileHashAsync(filePath);

        // Modify the file
        await File.WriteAllTextAsync(filePath, "Modified content");

        // Act
        var hashAfter = await _hashCalculator.CalculateFileHashAsync(filePath);

        // Assert
        hashBefore.Should().NotBe(hashAfter);
    }

    #endregion
}

using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AlUmmahNow.Tests.Examples;

/// <summary>
/// EXAMPLE TEST FILE - Template for implementing missing FileScanEngine tests
/// This demonstrates best practices for testing the core scanning logic.
///
/// TODO: Move this to AlUmmahNow.Tests/FileScanService/Services/FileScanEngineTests.cs
/// </summary>
public class EXAMPLE_FileScanEngineTests : FileSystemTestBase
{
    private readonly Mock<ILogger<FileScanEngine>> _mockLogger;
    private readonly Mock<IMetadataExtractor> _mockMetadataExtractor;
    private readonly Mock<IHashCalculator> _mockHashCalculator;
    private readonly ImportDbContext _dbContext;
    private readonly FileScanEngine _engine;

    public EXAMPLE_FileScanEngineTests()
    {
        _mockLogger = CreateMockLogger<FileScanEngine>();
        _mockMetadataExtractor = new Mock<IMetadataExtractor>();
        _mockHashCalculator = new Mock<IHashCalculator>();

        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ImportDbContext(options);
        _dbContext.Database.EnsureCreated();

        // Note: Adjust constructor based on actual FileScanEngine implementation
        _engine = new FileScanEngine(
            _mockLogger.Object,
            _mockMetadataExtractor.Object,
            _mockHashCalculator.Object,
            _dbContext);
    }

    #region Basic Scanning Tests

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "FileScan")]
    public async Task ScanDirectoryAsync_WithVideoFiles_ScansAllFiles()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Setup mocks to return predictable values
        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-hash-value");

        _mockMetadataExtractor
            .Setup(x => x.ExtractVideoMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VideoMetadata
            {
                DurationSeconds = 3600,
                Width = 1920,
                Height = 1080,
                Codec = "h264"
            });

        // Act
        var result = await _engine.ScanDirectoryAsync(
            sessionId,
            TestChannel1Directory,
            includeSubdirectories: false);

        // Assert
        result.Should().NotBeNull();
        result.FilesScanned.Should().BeGreaterThan(0);
        result.TotalSize.Should().BeGreaterThan(0);

        // Verify files were saved to database
        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .ToListAsync();

        scannedFiles.Should().NotBeEmpty();
        scannedFiles.Should().Contain(f => f.Extension == ".mp4");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "FileScan")]
    public async Task ScanDirectoryAsync_WithSubdirectories_ScansRecursively()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-hash");

        // Act - scan with subdirectories enabled
        var result = await _engine.ScanDirectoryAsync(
            sessionId,
            TestVideosDirectory, // Parent directory with Channel1 and Channel2 subdirs
            includeSubdirectories: true);

        // Assert
        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .ToListAsync();

        // Should find files from both Channel1 and Channel2 subdirectories
        scannedFiles.Should().Contain(f => f.Directory.Contains("Channel1"));
        scannedFiles.Should().Contain(f => f.Directory.Contains("Channel2"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "FileScan")]
    public async Task ScanDirectoryAsync_WithNoFiles_ReturnsZeroResults()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var emptyDir = Path.Combine(TestRootDirectory, "EmptyDirectory");
        Directory.CreateDirectory(emptyDir);

        // Act
        var result = await _engine.ScanDirectoryAsync(
            sessionId,
            emptyDir,
            includeSubdirectories: false);

        // Assert
        result.FilesScanned.Should().Be(0);
        result.TotalSize.Should().Be(0);
    }

    #endregion

    #region Duplicate Detection Tests

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "DuplicateDetection")]
    public async Task ScanDirectoryAsync_DetectsDuplicateFiles()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var duplicateHash = "same-hash-value-12345";

        // All files will have the same hash (simulating duplicates)
        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(duplicateHash);

        // Act
        await _engine.ScanDirectoryAsync(sessionId, TestVideosDirectory, true);

        // Assert
        var files = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .OrderBy(f => f.FirstScanned)
            .ToListAsync();

        // First file should be original
        files.First().IsDuplicate.Should().BeFalse();
        files.First().OriginalFileId.Should().BeNull();

        // Subsequent files with same hash should be marked as duplicates
        var duplicates = files.Skip(1).Where(f => f.ContentHash == duplicateHash).ToList();
        if (duplicates.Any())
        {
            duplicates.Should().OnlyContain(f => f.IsDuplicate == true);
            duplicates.Should().OnlyContain(f => f.OriginalFileId == files.First().Id);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "DuplicateDetection")]
    public async Task ScanDirectoryAsync_UniqueFiles_NotMarkedAsDuplicates()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var hashCounter = 0;

        // Each file gets unique hash
        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"unique-hash-{++hashCounter}");

        // Act
        await _engine.ScanDirectoryAsync(sessionId, TestChannel1Directory, false);

        // Assert
        var files = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .ToListAsync();

        files.Should().OnlyContain(f => f.IsDuplicate == false);
        files.Should().OnlyContain(f => f.OriginalFileId == null);
    }

    #endregion

    #region File Format Tests

    [Theory]
    [InlineData(".mp4")]
    [InlineData(".mkv")]
    [InlineData(".avi")]
    [InlineData(".mov")]
    [Trait("Category", "Unit")]
    [Trait("Feature", "FileFormats")]
    public async Task ScanDirectoryAsync_SupportsVideoFormat(string extension)
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var testFile = await CreateTestVideoFileAsync(
            TestChannel1Directory,
            $"test{extension}",
            1024 * 1024);

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-hash");

        // Act
        await _engine.ScanDirectoryAsync(sessionId, TestChannel1Directory, false);

        // Assert
        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId && f.Extension == extension)
            .ToListAsync();

        scannedFiles.Should().ContainSingle();
        scannedFiles[0].FileName.Should().EndWith(extension);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "FileFormats")]
    public async Task ScanDirectoryAsync_IgnoresNonVideoFiles()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Create non-video files
        await File.WriteAllTextAsync(
            Path.Combine(TestChannel1Directory, "readme.txt"),
            "This is a text file");
        await File.WriteAllTextAsync(
            Path.Combine(TestChannel1Directory, "data.json"),
            "{}");

        // Act
        await _engine.ScanDirectoryAsync(sessionId, TestChannel1Directory, false);

        // Assert
        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .ToListAsync();

        // Should only include video files, not .txt or .json
        scannedFiles.Should().NotContain(f => f.Extension == ".txt");
        scannedFiles.Should().NotContain(f => f.Extension == ".json");
    }

    #endregion

    #region Metadata Extraction Tests

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "Metadata")]
    public async Task ScanDirectoryAsync_ExtractsMetadataFromInfoFiles()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var expectedMetadata = new VideoMetadata
        {
            DurationSeconds = 7200,
            Width = 3840,
            Height = 2160,
            Codec = "vp9",
            InfoFileContent = "{\"title\": \"Test Video\"}",
            InfoFileHash = "info-hash-123"
        };

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("file-hash");

        _mockMetadataExtractor
            .Setup(x => x.ExtractVideoMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMetadata);

        // Act
        await _engine.ScanDirectoryAsync(sessionId, TestChannel1Directory, false);

        // Assert
        var scannedFile = await _dbContext.ScannedVideoFiles
            .FirstOrDefaultAsync(f => f.ScanSessionId == sessionId);

        scannedFile.Should().NotBeNull();
        scannedFile!.DurationSeconds.Should().Be(7200);
        scannedFile.Width.Should().Be(3840);
        scannedFile.Height.Should().Be(2160);
        scannedFile.Codec.Should().Be("vp9");

        // Verify metadata extractor was called
        _mockMetadataExtractor.Verify(
            x => x.ExtractVideoMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "Metadata")]
    public async Task ScanDirectoryAsync_WithoutInfoFile_StillScansVideoFile()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("file-hash");

        // Metadata extractor returns null/empty metadata (no info file)
        _mockMetadataExtractor
            .Setup(x => x.ExtractVideoMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VideoMetadata());

        // Act
        await _engine.ScanDirectoryAsync(sessionId, TestChannel1Directory, false);

        // Assert
        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .ToListAsync();

        scannedFiles.Should().NotBeEmpty();
        // Files should still be scanned even without metadata
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "Cancellation")]
    public async Task ScanDirectoryAsync_WithCancellation_StopsGracefully()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string path, CancellationToken ct) =>
            {
                await Task.Delay(100, ct); // Simulate slow hashing
                return "hash";
            });

        // Cancel after 50ms
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _engine.ScanDirectoryAsync(
                sessionId,
                TestRootDirectory,
                true,
                cts.Token));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "Cancellation")]
    public async Task ScanDirectoryAsync_CancellationMidScan_SavesPartialResults()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        var filesProcessed = 0;
        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string path, CancellationToken ct) =>
            {
                filesProcessed++;
                if (filesProcessed > 2)
                    cts.Cancel(); // Cancel after processing 2 files

                await Task.Delay(10, ct);
                return $"hash-{filesProcessed}";
            });

        // Act
        try
        {
            await _engine.ScanDirectoryAsync(sessionId, TestChannel1Directory, false, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - should have saved at least some files before cancellation
        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .ToListAsync();

        scannedFiles.Should().NotBeEmpty("partial results should be saved");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "ErrorHandling")]
    public async Task ScanDirectoryAsync_WithInvalidPath_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var invalidPath = "/nonexistent/path/that/does/not/exist";

        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _engine.ScanDirectoryAsync(sessionId, invalidPath, false));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "ErrorHandling")]
    public async Task ScanDirectoryAsync_HashCalculatorFails_LogsErrorAndContinues()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Hash calculation failed"));

        // Act
        var result = await _engine.ScanDirectoryAsync(sessionId, TestChannel1Directory, false);

        // Assert - should handle error gracefully
        result.Should().NotBeNull();

        // Verify error was logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Hash calculation")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "ErrorHandling")]
    public async Task ScanDirectoryAsync_LockedFile_SkipsAndContinues()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var lockedFile = await CreateTestVideoFileAsync(
            TestChannel1Directory,
            "locked.mp4",
            1024);

        // Lock the file
        using var stream = File.Open(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(lockedFile, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("File is locked"));

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(
                It.Is<string>(p => p != lockedFile),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash");

        // Act
        var result = await _engine.ScanDirectoryAsync(sessionId, TestChannel1Directory, false);

        // Assert - should still process other files
        result.FilesScanned.Should().BeGreaterThan(0);
    }

    #endregion

    #region Progress Reporting Tests

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "Progress")]
    public async Task ScanDirectoryAsync_ReportsProgressDuringScanning()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var progressReports = new List<ScanProgress>();

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash");

        // Subscribe to progress events (adjust based on actual implementation)
        var progressCallback = new Progress<ScanProgress>(p => progressReports.Add(p));

        // Act
        await _engine.ScanDirectoryAsync(
            sessionId,
            TestChannel1Directory,
            includeSubdirectories: false,
            CancellationToken.None,
            progressCallback);

        // Assert
        progressReports.Should().NotBeEmpty();
        progressReports.Last().ProgressPercentage.Should().Be(100);
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "EndToEnd")]
    public async Task CompleteScenario_ScanChannelWithVideosAndMetadata()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken ct) =>
                $"hash-{Path.GetFileName(path)}");

        _mockMetadataExtractor
            .Setup(x => x.ExtractVideoMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string videoPath, string infoPath, CancellationToken ct) =>
                new VideoMetadata
                {
                    DurationSeconds = 3600,
                    Width = 1920,
                    Height = 1080,
                    Codec = "h264",
                    InfoFileHash = string.IsNullOrEmpty(infoPath) ? null : "info-hash"
                });

        // Act
        var result = await _engine.ScanDirectoryAsync(
            sessionId,
            TestVideosDirectory,
            includeSubdirectories: true);

        // Assert
        result.FilesScanned.Should().BeGreaterThan(0);
        result.TotalSize.Should().BeGreaterThan(0);

        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .ToListAsync();

        scannedFiles.Should().NotBeEmpty();

        // Verify complete file information
        foreach (var file in scannedFiles)
        {
            file.FileName.Should().NotBeNullOrEmpty();
            file.FilePath.Should().NotBeNullOrEmpty();
            file.ContentHash.Should().NotBeNullOrEmpty();
            file.FileSize.Should().BeGreaterThan(0);
            file.FirstScanned.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }
    }

    #endregion

    public override async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await base.DisposeAsync();
    }
}

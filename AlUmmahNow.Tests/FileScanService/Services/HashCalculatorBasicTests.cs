using AlUmmahNow.FileScanService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AlUmmahNow.Tests.FileScanService.Services
{
    public class HashCalculatorBasicTests
    {
        private readonly HashCalculator _hashCalculator;
        private readonly Mock<ILogger<HashCalculator>> _mockLogger;

        // SHA256 hash of an empty string
        private const string EmptyStringSha256Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        public HashCalculatorBasicTests()
        {
            _mockLogger = new Mock<ILogger<HashCalculator>>();
            _hashCalculator = new HashCalculator(_mockLogger.Object);
        }

        [Fact]
        public async Task CalculateContentHashAsync_WithValidContent_ReturnsCorrectHash()
        {
            // Arrange
            var content = "This is test content for hashing";
            
            // Act
            var hash1 = await _hashCalculator.CalculateContentHashAsync(content);
            var hash2 = await _hashCalculator.CalculateContentHashAsync(content);
            
            // Assert
            hash1.Should().NotBeNullOrEmpty();
            hash1.Should().HaveLength(64); // SHA256 hex string length
            hash1.Should().Be(hash2); // Same content should produce same hash
            hash1.Should().MatchRegex("^[a-f0-9]{64}$"); // Valid hex string
        }

        [Fact]
        public async Task CalculateContentHashAsync_WithDifferentContent_ReturnsDifferentHashes()
        {
            // Arrange
            var content1 = "First content";
            var content2 = "Second content";
            
            // Act
            var hash1 = await _hashCalculator.CalculateContentHashAsync(content1);
            var hash2 = await _hashCalculator.CalculateContentHashAsync(content2);
            
            // Assert
            hash1.Should().NotBe(hash2);
        }

        [Fact]
        public async Task CalculateContentHashAsync_WithEmptyContent_ReturnsValidHash()
        {
            // Arrange
            var emptyContent = string.Empty;
            
            // Act
            var hash = await _hashCalculator.CalculateContentHashAsync(emptyContent);
            
            // Assert
            hash.Should().NotBeNullOrEmpty();
            hash.Should().HaveLength(64);
            hash.Should().Be(EmptyStringSha256Hash);
        }

        [Fact]
        public void CalculatePathHash_WithValidPath_ReturnsCorrectHash()
        {
            // Arrange
            var path = @"C:\Test\Path\Video.mp4";
            
            // Act
            var hash1 = _hashCalculator.CalculatePathHash(path);
            var hash2 = _hashCalculator.CalculatePathHash(path);
            
            // Assert
            hash1.Should().NotBeNullOrEmpty();
            hash1.Should().HaveLength(64);
            hash1.Should().Be(hash2);
            hash1.Should().MatchRegex("^[a-f0-9]{64}$");
        }

        [Fact]
        public void CalculatePathHash_WithDifferentCasing_ReturnsSameHash()
        {
            // Arrange
            var path1 = @"C:\Test\Path\Video.mp4";
            var path2 = @"c:\test\path\video.mp4";
            
            // Act
            var hash1 = _hashCalculator.CalculatePathHash(path1);
            var hash2 = _hashCalculator.CalculatePathHash(path2);
            
            // Assert
            hash1.Should().Be(hash2); // Should be case-insensitive
        }

        [Fact]
        public async Task CalculateContentHashAsync_WithCancellation_ThrowsOperationCancelledException()
        {
            // Arrange
            var content = new string('x', 1024 * 1024); // 1MB string
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately
            
            // Act & Assert
            await _hashCalculator.Invoking(h => h.CalculateContentHashAsync(content, cts.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
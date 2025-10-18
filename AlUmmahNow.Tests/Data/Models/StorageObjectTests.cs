using Xunit;
using FluentAssertions;
using AlUmmahNow.Data.Models;
using System;

namespace AlUmmahNow.Tests.Data.Models;

/// <summary>
/// Tests for StorageObject model
/// </summary>
public class StorageObjectTests
{
    [Fact]
    public void StorageObject_DefaultInitialization_SetsDefaults()
    {
        // Arrange & Act
        var obj = new StorageObject();

        // Assert
        obj.Should().NotBeNull();
        obj.Guid.Should().Be(string.Empty);
        obj.ObjectName.Should().Be(string.Empty);
        obj.Path.Should().Be(string.Empty);
        obj.FullPath.Should().Be(string.Empty);
        obj.Length.Should().Be(0);
        obj.IsDirectory.Should().BeFalse();
        obj.DateCreated.Should().Be(default(DateTime));
        obj.LastChanged.Should().Be(default(DateTime));
    }

    [Fact]
    public void StorageObject_WithValues_StoresCorrectly()
    {
        // Arrange
        var created = DateTime.UtcNow.AddDays(-7);
        var modified = DateTime.UtcNow;

        // Act
        var obj = new StorageObject
        {
            Guid = "test-guid-789",
            ObjectName = "video.mp4",
            Path = "/videos/test/",
            Length = 1024000,
            IsDirectory = false,
            DateCreated = created,
            LastChanged = modified
        };

        // Assert
        obj.Guid.Should().Be("test-guid-789");
        obj.ObjectName.Should().Be("video.mp4");
        obj.Path.Should().Be("/videos/test/");
        obj.Length.Should().Be(1024000);
        obj.IsDirectory.Should().BeFalse();
        obj.DateCreated.Should().Be(created);
        obj.LastChanged.Should().Be(modified);
    }

    [Fact]
    public void StorageObject_AsDirectory_SetsIsDirectoryTrue()
    {
        // Arrange & Act
        var directory = new StorageObject
        {
            ObjectName = "subfolder",
            IsDirectory = true,
            Length = 0
        };

        // Assert
        directory.IsDirectory.Should().BeTrue();
        directory.Length.Should().Be(0);
    }

    [Fact]
    public void StorageObject_WithLargeFileSize_HandlesCorrectly()
    {
        // Arrange & Act
        var largeFile = new StorageObject
        {
            ObjectName = "large_video.mp4",
            Length = 5_368_709_120 // 5 GB
        };

        // Assert
        largeFile.Length.Should().Be(5_368_709_120);
    }

    [Fact]
    public void StorageObject_WithSpecialCharactersInName_StoresCorrectly()
    {
        // Arrange & Act
        var obj = new StorageObject
        {
            ObjectName = "video (1) [HD].mp4",
            Path = "/videos/Special (Chars)/"
        };

        // Assert
        obj.ObjectName.Should().Be("video (1) [HD].mp4");
        obj.Path.Should().Be("/videos/Special (Chars)/");
    }

    [Fact]
    public void StorageObject_WithUnicodeCharacters_StoresCorrectly()
    {
        // Arrange & Act
        var obj = new StorageObject
        {
            ObjectName = "فيديو.mp4",
            Path = "/videos/العربية/"
        };

        // Assert
        obj.ObjectName.Should().Be("فيديو.mp4");
        obj.Path.Should().Be("/videos/العربية/");
    }
}

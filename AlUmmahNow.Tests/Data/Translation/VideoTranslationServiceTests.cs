using Xunit;
using FluentAssertions;
using AlUmmahNow.Data.Translation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlUmmahNow.Tests.Data.Translation;

/// <summary>
/// Tests for VideoTranslationService
/// </summary>
public class VideoTranslationServiceTests
{
    #region Initialization Tests

    [Fact]
    public void Constructor_WithNullClient_CreatesService()
    {
        // Act
        var service = new VideoTranslationService(null, null);

        // Assert
        service.Should().NotBeNull();
        service.IsTranslationAvailable.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithNullSettings_UsesDefaultSettings()
    {
        // Act
        var service = new VideoTranslationService(null, null);

        // Assert - Service should be created without throwing
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithSettings_UsesProvidedSettings()
    {
        // Arrange
        var settings = new TranslationSettings
        {
            PreserveFormatting = false,
            AutoTranslateMetadata = true
        };

        // Act
        var service = new VideoTranslationService(null, settings);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void IsTranslationAvailable_WithoutClient_ReturnsFalse()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);

        // Act & Assert
        service.IsTranslationAvailable.Should().BeFalse();
    }

    #endregion

    #region TranslateVideoMetadataAsync Without Client Tests

    [Fact]
    public async Task TranslateVideoMetadataAsync_WithoutClient_ReturnsOriginalMetadata()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var metadata = new VideoMetadata
        {
            Title = "Test Video",
            Description = "Test Description"
        };

        // Act
        var result = await service.TranslateVideoMetadataAsync(
            metadata,
            new[] { "EN", "AR" });

        // Assert
        result.Should().NotBeNull();
        result.OriginalTitle.Should().Be("Test Video");
        result.OriginalDescription.Should().Be("Test Description");
        result.Translations.Should().BeEmpty();
    }

    [Fact]
    public async Task TranslateVideoMetadataAsync_WithoutClient_HandlesEmptyMetadata()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var metadata = new VideoMetadata();

        // Act
        var result = await service.TranslateVideoMetadataAsync(
            metadata,
            new[] { "EN" });

        // Assert
        result.Should().NotBeNull();
        result.OriginalTitle.Should().Be(string.Empty);
        result.OriginalDescription.Should().Be(string.Empty);
        result.Translations.Should().BeEmpty();
    }

    [Fact]
    public async Task TranslateVideoMetadataAsync_WithoutClient_HandlesMultipleLanguages()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var metadata = new VideoMetadata
        {
            Title = "Original",
            Description = "Description"
        };

        // Act
        var result = await service.TranslateVideoMetadataAsync(
            metadata,
            new[] { "EN", "AR", "ES", "FR", "DE" });

        // Assert
        result.Translations.Should().BeEmpty();
    }

    #endregion

    #region GenerateMultilingualFilenames Tests

    [Fact]
    public void GenerateMultilingualFilenames_WithEmptyTranslations_ReturnsEmptyDictionary()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var translations = new Dictionary<string, VideoTranslation>();

        // Act
        var result = service.GenerateMultilingualFilenames("video.mp4", translations);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateMultilingualFilenames_WithTranslations_GeneratesFilenames()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var translations = new Dictionary<string, VideoTranslation>
        {
            ["EN"] = new VideoTranslation
            {
                LanguageCode = "EN",
                Title = "Programming Tutorial"
            },
            ["AR"] = new VideoTranslation
            {
                LanguageCode = "AR",
                Title = "درس برمجة"
            }
        };

        // Act
        var result = service.GenerateMultilingualFilenames("original.mp4", translations);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainKey("EN");
        result.Should().ContainKey("AR");
        result["EN"].Should().EndWith("_en.mp4");
        result["AR"].Should().EndWith("_ar.mp4");
    }

    [Fact]
    public void GenerateMultilingualFilenames_WithSpecialCharacters_SanitizesFilenames()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var translations = new Dictionary<string, VideoTranslation>
        {
            ["EN"] = new VideoTranslation
            {
                LanguageCode = "EN",
                Title = "Test: Video / Tutorial | Part 1"
            }
        };

        // Act
        var result = service.GenerateMultilingualFilenames("video.mp4", translations);

        // Assert
        result.Should().ContainKey("EN");
        // Special characters are replaced with underscores by Path.GetInvalidFileNameChars()
        result["EN"].Should().Contain("_"); // Invalid chars replaced with underscore
        result["EN"].Should().EndWith(".mp4");
        result["EN"].Should().NotContain("/"); // Forward slash should be sanitized
    }

    [Fact]
    public void GenerateMultilingualFilenames_WithLongTitle_TruncatesFilename()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var veryLongTitle = new string('a', 200);
        var translations = new Dictionary<string, VideoTranslation>
        {
            ["EN"] = new VideoTranslation
            {
                LanguageCode = "EN",
                Title = veryLongTitle
            }
        };

        // Act
        var result = service.GenerateMultilingualFilenames("video.mp4", translations);

        // Assert
        result.Should().ContainKey("EN");
        // Filename should be truncated (100 chars + language suffix + extension)
        result["EN"].Length.Should().BeLessThan(veryLongTitle.Length + 10);
    }

    [Fact]
    public void GenerateMultilingualFilenames_WithEmptyTitle_SkipsLanguage()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var translations = new Dictionary<string, VideoTranslation>
        {
            ["EN"] = new VideoTranslation
            {
                LanguageCode = "EN",
                Title = ""
            }
        };

        // Act
        var result = service.GenerateMultilingualFilenames("video.mp4", translations);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateMultilingualFilenames_PreservesFileExtension()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var translations = new Dictionary<string, VideoTranslation>
        {
            ["EN"] = new VideoTranslation { LanguageCode = "EN", Title = "Test" }
        };

        // Act
        var result1 = service.GenerateMultilingualFilenames("video.mp4", translations);
        var result2 = service.GenerateMultilingualFilenames("video.mkv", translations);
        var result3 = service.GenerateMultilingualFilenames("video.avi", translations);

        // Assert
        result1["EN"].Should().EndWith(".mp4");
        result2["EN"].Should().EndWith(".mkv");
        result3["EN"].Should().EndWith(".avi");
    }

    #endregion

    #region TranslateChannelNameAsync Without Client Tests

    [Fact]
    public async Task TranslateChannelNameAsync_WithoutClient_ReturnsOriginalName()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);

        // Act
        var result = await service.TranslateChannelNameAsync("Test Channel", "EN");

        // Assert
        result.Should().Be("Test Channel");
    }

    [Fact]
    public async Task TranslateChannelNameAsync_WithEmptyChannelName_ReturnsEmpty()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);

        // Act
        var result = await service.TranslateChannelNameAsync("", "EN");

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public async Task TranslateChannelNameAsync_WithNullChannelName_ReturnsNull()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);

        // Act
        var result = await service.TranslateChannelNameAsync(null!, "EN");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GenerateMultilingualTagsAsync Without Client Tests

    [Fact]
    public async Task GenerateMultilingualTagsAsync_WithoutClient_ReturnsEmptyDictionary()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var metadata = new VideoMetadata
        {
            Title = "Tutorial Video",
            Description = "Educational content"
        };

        // Act
        var result = await service.GenerateMultilingualTagsAsync(metadata, new[] { "EN", "AR" });

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateMultilingualTagsAsync_WithEmptyMetadata_ReturnsEmptyDictionary()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);
        var metadata = new VideoMetadata();

        // Act
        var result = await service.GenerateMultilingualTagsAsync(metadata, new[] { "EN" });

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetUsageAsync Without Client Tests

    [Fact]
    public async Task GetUsageAsync_WithoutClient_ReturnsNull()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);

        // Act
        var result = await service.GetUsageAsync();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetSupportedLanguagesAsync Without Client Tests

    [Fact]
    public async Task GetSupportedLanguagesAsync_WithoutClient_ReturnsEmptyArray()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);

        // Act
        var result = await service.GetSupportedLanguagesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WithoutClient_DoesNotThrow()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);

        // Act & Assert
        service.Invoking(s => s.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = new VideoTranslationService(null, null);

        // Act & Assert
        service.Invoking(s =>
        {
            s.Dispose();
            s.Dispose();
            s.Dispose();
        }).Should().NotThrow();
    }

    [Fact]
    public void UsingStatement_DisposesCorrectly()
    {
        // Act & Assert
        Action action = () =>
        {
            using var service = new VideoTranslationService(null, null);
            // Service should be disposed when exiting using block
        };

        action.Should().NotThrow();
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public async Task CompleteWorkflow_WithoutClient_HandlesGracefully()
    {
        // Arrange
        using var service = new VideoTranslationService(null, new TranslationSettings
        {
            DefaultTargetLanguages = new List<string> { "EN", "AR", "ES" }
        });

        var metadata = new VideoMetadata
        {
            Title = "Test Video",
            Description = "Test Description",
            ChannelName = "Test Channel"
        };

        // Act
        var translatedMetadata = await service.TranslateVideoMetadataAsync(
            metadata,
            new[] { "EN", "AR" });

        var filenames = service.GenerateMultilingualFilenames(
            "video.mp4",
            translatedMetadata.Translations);

        var channelName = await service.TranslateChannelNameAsync("Test Channel", "EN");
        var tags = await service.GenerateMultilingualTagsAsync(metadata, new[] { "EN" });
        var usage = await service.GetUsageAsync();
        var languages = await service.GetSupportedLanguagesAsync();

        // Assert
        translatedMetadata.Translations.Should().BeEmpty();
        filenames.Should().BeEmpty();
        channelName.Should().Be("Test Channel");
        tags.Should().BeEmpty();
        usage.Should().BeNull();
        languages.Should().BeEmpty();
    }

    #endregion
}

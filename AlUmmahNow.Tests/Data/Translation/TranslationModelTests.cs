using Xunit;
using FluentAssertions;
using AlUmmahNow.Data.Translation;
using System.Collections.Generic;

namespace AlUmmahNow.Tests.Data.Translation;

/// <summary>
/// Tests for translation model classes
/// </summary>
public class TranslationModelTests
{
    #region VideoMetadata Tests

    [Fact]
    public void VideoMetadata_DefaultInitialization_SetsDefaults()
    {
        // Act
        var metadata = new VideoMetadata();

        // Assert
        metadata.Title.Should().Be(string.Empty);
        metadata.Description.Should().Be(string.Empty);
        metadata.ChannelName.Should().Be(string.Empty);
        metadata.Tags.Should().NotBeNull();
        metadata.Tags.Should().BeEmpty();
    }

    [Fact]
    public void VideoMetadata_WithValues_StoresCorrectly()
    {
        // Arrange & Act
        var metadata = new VideoMetadata
        {
            Title = "Test Video Title",
            Description = "Test video description with detailed information",
            ChannelName = "Test Channel",
            Tags = new List<string> { "tutorial", "education", "tech" }
        };

        // Assert
        metadata.Title.Should().Be("Test Video Title");
        metadata.Description.Should().Be("Test video description with detailed information");
        metadata.ChannelName.Should().Be("Test Channel");
        metadata.Tags.Should().HaveCount(3);
        metadata.Tags.Should().Contain(new[] { "tutorial", "education", "tech" });
    }

    [Fact]
    public void VideoMetadata_WithUnicodeContent_HandlesCorrectly()
    {
        // Arrange & Act
        var metadata = new VideoMetadata
        {
            Title = "درس تعليمي في البرمجة",
            Description = "هذا درس تعليمي شامل",
            ChannelName = "قناة البرمجة",
            Tags = new List<string> { "برمجة", "تعليم", "تقنية" }
        };

        // Assert
        metadata.Title.Should().Be("درس تعليمي في البرمجة");
        metadata.Description.Should().Be("هذا درس تعليمي شامل");
        metadata.ChannelName.Should().Be("قناة البرمجة");
        metadata.Tags.Should().Contain("برمجة");
    }

    #endregion

    #region TranslatedVideoMetadata Tests

    [Fact]
    public void TranslatedVideoMetadata_DefaultInitialization_SetsDefaults()
    {
        // Act
        var translated = new TranslatedVideoMetadata();

        // Assert
        translated.OriginalTitle.Should().Be(string.Empty);
        translated.OriginalDescription.Should().Be(string.Empty);
        translated.Translations.Should().NotBeNull();
        translated.Translations.Should().BeEmpty();
    }

    [Fact]
    public void TranslatedVideoMetadata_WithTranslations_StoresCorrectly()
    {
        // Arrange & Act
        var translated = new TranslatedVideoMetadata
        {
            OriginalTitle = "Original Title",
            OriginalDescription = "Original Description",
            Translations = new Dictionary<string, VideoTranslation>
            {
                ["EN"] = new VideoTranslation
                {
                    LanguageCode = "EN",
                    Title = "English Title",
                    Description = "English Description"
                },
                ["AR"] = new VideoTranslation
                {
                    LanguageCode = "AR",
                    Title = "العنوان العربي",
                    Description = "الوصف العربي"
                }
            }
        };

        // Assert
        translated.OriginalTitle.Should().Be("Original Title");
        translated.Translations.Should().HaveCount(2);
        translated.Translations["EN"].Title.Should().Be("English Title");
        translated.Translations["AR"].Title.Should().Be("العنوان العربي");
    }

    [Fact]
    public void TranslatedVideoMetadata_AddTranslation_WorksCorrectly()
    {
        // Arrange
        var translated = new TranslatedVideoMetadata
        {
            OriginalTitle = "Test",
            Translations = new Dictionary<string, VideoTranslation>()
        };

        // Act
        translated.Translations["ES"] = new VideoTranslation
        {
            LanguageCode = "ES",
            Title = "Título en español"
        };

        // Assert
        translated.Translations.Should().ContainKey("ES");
        translated.Translations["ES"].Title.Should().Be("Título en español");
    }

    #endregion

    #region VideoTranslation Tests

    [Fact]
    public void VideoTranslation_DefaultInitialization_SetsDefaults()
    {
        // Act
        var translation = new VideoTranslation();

        // Assert
        translation.LanguageCode.Should().Be(string.Empty);
        translation.Title.Should().Be(string.Empty);
        translation.Description.Should().Be(string.Empty);
        translation.DetectedSourceLanguage.Should().BeNull();
        translation.TranslationError.Should().BeNull();
        translation.Tags.Should().NotBeNull();
        translation.Tags.Should().BeEmpty();
    }

    [Fact]
    public void VideoTranslation_WithValues_StoresCorrectly()
    {
        // Arrange & Act
        var translation = new VideoTranslation
        {
            LanguageCode = "FR",
            Title = "Titre en français",
            Description = "Description en français",
            DetectedSourceLanguage = "EN",
            Tags = new List<string> { "tutoriel", "éducation" }
        };

        // Assert
        translation.LanguageCode.Should().Be("FR");
        translation.Title.Should().Be("Titre en français");
        translation.Description.Should().Be("Description en français");
        translation.DetectedSourceLanguage.Should().Be("EN");
        translation.Tags.Should().HaveCount(2);
        translation.TranslationError.Should().BeNull();
    }

    [Fact]
    public void VideoTranslation_WithError_StoresErrorMessage()
    {
        // Arrange & Act
        var translation = new VideoTranslation
        {
            LanguageCode = "DE",
            Title = "Original Title",
            Description = "Original Description",
            TranslationError = "API quota exceeded"
        };

        // Assert
        translation.TranslationError.Should().Be("API quota exceeded");
        translation.Title.Should().Be("Original Title");
    }

    [Fact]
    public void VideoTranslation_WithAllProperties_StoresCorrectly()
    {
        // Arrange & Act
        var translation = new VideoTranslation
        {
            LanguageCode = "JA",
            Title = "日本語のタイトル",
            Description = "日本語の説明",
            DetectedSourceLanguage = "EN",
            TranslationError = null,
            Tags = new List<string> { "チュートリアル", "教育" }
        };

        // Assert
        translation.LanguageCode.Should().Be("JA");
        translation.Title.Should().Be("日本語のタイトル");
        translation.Description.Should().Be("日本語の説明");
        translation.DetectedSourceLanguage.Should().Be("EN");
        translation.TranslationError.Should().BeNull();
        translation.Tags.Should().HaveCount(2);
    }

    #endregion

    #region TranslationSettings Tests

    [Fact]
    public void TranslationSettings_DefaultInitialization_SetsDefaults()
    {
        // Act
        var settings = new TranslationSettings();

        // Assert
        settings.UseMoreFormal.Should().BeFalse();
        settings.UseLessFormal.Should().BeFalse();
        settings.PreserveFormatting.Should().BeTrue();
        settings.AutoTranslateMetadata.Should().BeFalse();
        settings.GenerateMultilingualTags.Should().BeFalse();
        settings.DefaultTargetLanguages.Should().NotBeNull();
        settings.DefaultTargetLanguages.Should().HaveCount(5);
    }

    [Fact]
    public void TranslationSettings_DefaultTargetLanguages_ContainsExpectedLanguages()
    {
        // Act
        var settings = new TranslationSettings();

        // Assert
        settings.DefaultTargetLanguages.Should().Contain("AR");
        settings.DefaultTargetLanguages.Should().Contain("EN");
        settings.DefaultTargetLanguages.Should().Contain("ES");
        settings.DefaultTargetLanguages.Should().Contain("FR");
        settings.DefaultTargetLanguages.Should().Contain("DE");
    }

    [Fact]
    public void TranslationSettings_SetFormalityLevel_DoesNotThrow()
    {
        // Arrange
        var settings = new TranslationSettings();
        var mockFormality = new object();

        // Act & Assert - Method is internal, so we just verify it doesn't throw
        settings.Invoking(s => s.SetFormalityLevel(mockFormality)).Should().NotThrow();
    }

    [Fact]
    public void TranslationSettings_SetFormalityLevel_WithNull_DoesNotThrow()
    {
        // Arrange
        var settings = new TranslationSettings();

        // Act & Assert
        settings.Invoking(s => s.SetFormalityLevel(null)).Should().NotThrow();
    }

    [Fact]
    public void TranslationSettings_ModifyProperties_StoresCorrectly()
    {
        // Arrange
        var settings = new TranslationSettings();

        // Act
        settings.UseMoreFormal = true;
        settings.PreserveFormatting = false;
        settings.AutoTranslateMetadata = true;
        settings.GenerateMultilingualTags = true;
        settings.DefaultTargetLanguages = new List<string> { "ZH", "KO" };

        // Assert
        settings.UseMoreFormal.Should().BeTrue();
        settings.PreserveFormatting.Should().BeFalse();
        settings.AutoTranslateMetadata.Should().BeTrue();
        settings.GenerateMultilingualTags.Should().BeTrue();
        settings.DefaultTargetLanguages.Should().HaveCount(2);
        settings.DefaultTargetLanguages.Should().Contain(new[] { "ZH", "KO" });
    }

    [Fact]
    public void TranslationSettings_UseMoreFormal_AndUseLessFormal_CanBothBeSet()
    {
        // Arrange
        var settings = new TranslationSettings();

        // Act - Although logically contradictory, the class allows both to be true
        settings.UseMoreFormal = true;
        settings.UseLessFormal = true;

        // Assert
        settings.UseMoreFormal.Should().BeTrue();
        settings.UseLessFormal.Should().BeTrue();
    }

    [Fact]
    public void TranslationSettings_CustomTargetLanguages_WorksCorrectly()
    {
        // Arrange & Act
        var settings = new TranslationSettings
        {
            DefaultTargetLanguages = new List<string>
            {
                "PT", // Portuguese
                "IT", // Italian
                "RU", // Russian
                "TR"  // Turkish
            }
        };

        // Assert
        settings.DefaultTargetLanguages.Should().HaveCount(4);
        settings.DefaultTargetLanguages.Should().NotContain("EN");
        settings.DefaultTargetLanguages.Should().Contain("PT");
    }

    #endregion
}

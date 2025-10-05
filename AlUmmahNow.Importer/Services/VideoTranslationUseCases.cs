using AlUmmahNow.Data.Translation;
using DeepL.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlUmmahNow.Importer.Services
{
    /// <summary>
    /// Example use cases for the VideoTranslationService
    /// NOTE: Formality feature temporarily disabled due to type access issue
    /// </summary>
    public static class VideoTranslationUseCases
    {
        /// <summary>
        /// Use Case 1: Basic video metadata translation
        /// Translate a video's title and description to multiple languages
        /// </summary>
        public static async Task<TranslatedVideoMetadata> TranslateVideoMetadataBasicAsync(
            DeepLClient deepLClient,
            string videoTitle,
            string videoDescription)
        {
            using var translationService = new VideoTranslationService(deepLClient);

            var metadata = new VideoMetadata
            {
                Title = videoTitle,
                Description = videoDescription
            };

            // Translate to Arabic, English, Spanish, French, and German
            var targetLanguages = new[] { "AR", "EN", "ES", "FR", "DE" };

            var translatedMetadata = await translationService.TranslateVideoMetadataAsync(
                metadata,
                targetLanguages);

            return translatedMetadata;
        }

        /// <summary>
        /// Use Case 2: Translate video metadata with source language specified
        /// Useful when you know the original language
        /// </summary>
        public static async Task<TranslatedVideoMetadata> TranslateVideoMetadataWithSourceAsync(
            DeepLClient deepLClient,
            string videoTitle,
            string videoDescription,
            string sourceLanguage = "EN")
        {
            using var translationService = new VideoTranslationService(deepLClient);

            var metadata = new VideoMetadata
            {
                Title = videoTitle,
                Description = videoDescription
            };

            var targetLanguages = new[] { "AR", "ES", "FR", "DE" };

            var translatedMetadata = await translationService.TranslateVideoMetadataAsync(
                metadata,
                targetLanguages,
                sourceLanguage);

            return translatedMetadata;
        }

        /// <summary>
        /// Use Case 3: Generate multilingual filenames for better organization
        /// Creates filename suggestions in different languages
        /// </summary>
        public static Dictionary<string, string> GenerateMultilingualFilenames(
            DeepLClient deepLClient,
            string originalFilename,
            TranslatedVideoMetadata translatedMetadata)
        {
            using var translationService = new VideoTranslationService(deepLClient);

            var multilingualFilenames = translationService.GenerateMultilingualFilenames(
                originalFilename,
                translatedMetadata.Translations);

            // Example output:
            // {
            //     "AR": "?????-???-???????_ar.mp4",
            //     "EN": "Introduction-to-Programming_en.mp4",
            //     "ES": "Introducción-a-la-programación_es.mp4"
            // }

            return multilingualFilenames;
        }

        /// <summary>
        /// Use Case 4: Translate channel names for better categorization
        /// Helps organize videos by translating channel names
        /// </summary>
        public static async Task<Dictionary<string, string>> TranslateChannelNamesAsync(
            DeepLClient deepLClient,
            List<string> channelNames,
            string targetLanguage = "AR")
        {
            using var translationService = new VideoTranslationService(deepLClient);

            var translatedChannels = new Dictionary<string, string>();

            foreach (var channelName in channelNames)
            {
                var translatedName = await translationService.TranslateChannelNameAsync(
                    channelName,
                    targetLanguage);

                translatedChannels[channelName] = translatedName;
            }

            return translatedChannels;
        }

        /// <summary>
        /// Use Case 5: Generate multilingual tags for video SEO
        /// Creates searchable tags in multiple languages
        /// </summary>
        public static async Task<Dictionary<string, List<string>>> GenerateMultilingualTagsAsync(
            DeepLClient deepLClient,
            string videoTitle,
            string videoDescription)
        {
            using var translationService = new VideoTranslationService(deepLClient);

            var metadata = new VideoMetadata
            {
                Title = videoTitle,
                Description = videoDescription
            };

            var targetLanguages = new[] { "AR", "EN", "ES", "FR" };

            var multilingualTags = await translationService.GenerateMultilingualTagsAsync(
                metadata,
                targetLanguages);

            // Example output:
            // {
            //     "AR": ["?????", "?????", "????"],
            //     "EN": ["programming", "education", "tutorial"],
            //     "ES": ["programación", "educación", "tutorial"]
            // }

            return multilingualTags;
        }

        /// <summary>
        /// Use Case 6: Complete workflow - Translate and organize video metadata
        /// Full example showing how to prepare video for multilingual platform
        /// </summary>
        public static async Task<VideoMultilingualPackage> PrepareVideoForMultilingualPlatformAsync(
            DeepLClient deepLClient,
            string videoFile,
            string videoTitle,
            string videoDescription,
            string channelName)
        {
            using var translationService = new VideoTranslationService(deepLClient);

            // Step 1: Translate video metadata
            var metadata = new VideoMetadata
            {
                Title = videoTitle,
                Description = videoDescription,
                ChannelName = channelName
            };

            var targetLanguages = new[] { "AR", "EN", "ES", "FR", "DE" };
            var translatedMetadata = await translationService.TranslateVideoMetadataAsync(
                metadata,
                targetLanguages);

            // Step 2: Generate multilingual filenames
            var multilingualFilenames = translationService.GenerateMultilingualFilenames(
                videoFile,
                translatedMetadata.Translations);

            // Step 3: Generate multilingual tags
            var multilingualTags = await translationService.GenerateMultilingualTagsAsync(
                metadata,
                targetLanguages);

            // Step 4: Translate channel name
            var translatedChannelNames = new Dictionary<string, string>();
            foreach (var lang in targetLanguages)
            {
                translatedChannelNames[lang] = await translationService.TranslateChannelNameAsync(
                    channelName,
                    lang);
            }

            return new VideoMultilingualPackage
            {
                OriginalTitle = videoTitle,
                OriginalDescription = videoDescription,
                OriginalChannelName = channelName,
                TranslatedMetadata = translatedMetadata,
                MultilingualFilenames = multilingualFilenames,
                MultilingualTags = multilingualTags,
                TranslatedChannelNames = translatedChannelNames
            };
        }

        /// <summary>
        /// Use Case 7: Check translation service availability and usage
        /// Monitor DeepL API usage before running bulk translations
        /// </summary>
        public static async Task<TranslationServiceInfo> CheckTranslationServiceAsync(
            DeepLClient deepLClient)
        {
            using var translationService = new VideoTranslationService(deepLClient);

            var isAvailable = translationService.IsTranslationAvailable;
            var usage = await translationService.GetUsageAsync();
            var supportedLanguages = await translationService.GetSupportedLanguagesAsync();

            return new TranslationServiceInfo
            {
                IsAvailable = isAvailable,
                CharacterCount = usage?.Character?.Count ?? 0,
                CharacterLimit = usage?.Character?.Limit ?? 0,
                RemainingCharacters = (usage?.Character?.Limit ?? 0) - (usage?.Character?.Count ?? 0),
                SupportedLanguages = supportedLanguages.Select(l => new LanguageInfo
                {
                    Code = l.Code,
                    Name = l.Name
                }).ToList()
            };
        }

        /// <summary>
        /// Use Case 8: Bulk translate multiple videos with error handling
        /// Process entire video library with translation
        /// </summary>
        public static async Task<BulkTranslationResult> BulkTranslateVideosAsync(
            DeepLClient deepLClient,
            List<(string title, string description, string filename)> videos,
            string[] targetLanguages)
        {
            using var translationService = new VideoTranslationService(deepLClient);

            var result = new BulkTranslationResult();
            var translationSettings = new TranslationSettings
            {
                DefaultTargetLanguages = targetLanguages.ToList(),
                AutoTranslateMetadata = true,
                GenerateMultilingualTags = true
            };

            foreach (var (title, description, filename) in videos)
            {
                try
                {
                    var metadata = new VideoMetadata
                    {
                        Title = title,
                        Description = description
                    };

                    var translatedMetadata = await translationService.TranslateVideoMetadataAsync(
                        metadata,
                        targetLanguages);

                    var multilingualTags = translationSettings.GenerateMultilingualTags
                        ? await translationService.GenerateMultilingualTagsAsync(metadata, targetLanguages)
                        : new Dictionary<string, List<string>>();

                    result.SuccessfulTranslations.Add(new VideoTranslationResult
                    {
                        OriginalTitle = title,
                        OriginalDescription = description,
                        Filename = filename,
                        TranslatedMetadata = translatedMetadata,
                        MultilingualTags = multilingualTags
                    });
                }
                catch (Exception ex)
                {
                    result.FailedTranslations.Add(new FailedTranslation
                    {
                        Title = title,
                        Filename = filename,
                        Error = ex.Message
                    });
                }
            }

            result.TotalProcessed = videos.Count;
            result.SuccessCount = result.SuccessfulTranslations.Count;
            result.FailureCount = result.FailedTranslations.Count;

            return result;
        }

        /// <summary>
        /// Use Case 9: Translate with custom formality settings
        /// Useful for formal educational content vs casual vlogs
        /// TEMPORARILY DISABLED: Type access issue with DeepL.Model.Formality
        /// </summary>
        /*
        public static async Task<TranslatedVideoMetadata> TranslateWithFormalityAsync(
            DeepLClient deepLClient,
            string videoTitle,
            string videoDescription,
            Formality formality = Formality.More)
        {
            var settings = new TranslationSettings
            {
                PreserveFormatting = true
            };
            settings.SetFormalityLevel(formality);

            using var translationService = new VideoTranslationService(deepLClient, settings);

            var metadata = new VideoMetadata
            {
                Title = videoTitle,
                Description = videoDescription
            };

            var targetLanguages = new[] { "DE", "FR", "ES" }; // Languages that support formality

            var translatedMetadata = await translationService.TranslateVideoMetadataAsync(
                metadata,
                targetLanguages);

            return translatedMetadata;
        }
        */
    }

    #region Result Models

    /// <summary>
    /// Complete multilingual package for a video
    /// </summary>
    public class VideoMultilingualPackage
    {
        public string OriginalTitle { get; set; } = string.Empty;
        public string OriginalDescription { get; set; } = string.Empty;
        public string OriginalChannelName { get; set; } = string.Empty;
        public TranslatedVideoMetadata TranslatedMetadata { get; set; } = new();
        public Dictionary<string, string> MultilingualFilenames { get; set; } = new();
        public Dictionary<string, List<string>> MultilingualTags { get; set; } = new();
        public Dictionary<string, string> TranslatedChannelNames { get; set; } = new();
    }

    /// <summary>
    /// Translation service information
    /// </summary>
    public class TranslationServiceInfo
    {
        public bool IsAvailable { get; set; }
        public long CharacterCount { get; set; }
        public long CharacterLimit { get; set; }
        public long RemainingCharacters { get; set; }
        public List<LanguageInfo> SupportedLanguages { get; set; } = new();
    }

    /// <summary>
    /// Language information
    /// </summary>
    public class LanguageInfo
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Bulk translation result
    /// </summary>
    public class BulkTranslationResult
    {
        public int TotalProcessed { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<VideoTranslationResult> SuccessfulTranslations { get; set; } = new();
        public List<FailedTranslation> FailedTranslations { get; set; } = new();
    }

    /// <summary>
    /// Single video translation result
    /// </summary>
    public class VideoTranslationResult
    {
        public string OriginalTitle { get; set; } = string.Empty;
        public string OriginalDescription { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public TranslatedVideoMetadata TranslatedMetadata { get; set; } = new();
        public Dictionary<string, List<string>> MultilingualTags { get; set; } = new();
    }

    /// <summary>
    /// Failed translation information
    /// </summary>
    public class FailedTranslation
    {
        public string Title { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    #endregion
}

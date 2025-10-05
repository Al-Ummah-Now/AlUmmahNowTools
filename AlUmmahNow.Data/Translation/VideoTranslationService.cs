using AlUmmahNow.Data.Translation;
using DeepL.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AlUmmahNow.Data.Translation
{
    /// <summary>
    /// Service for translating video metadata and content during import
    /// </summary>
    public class VideoTranslationService : IDisposable
    {
        private readonly DeepLClient? _deepLClient;
        private readonly TranslationSettings _settings;
        private bool _disposed = false;

        /// <summary>
        /// Initializes the translation service with DeepL client
        /// </summary>
        /// <param name="deepLClient">DeepL client for translation (optional)</param>
        /// <param name="settings">Translation settings</param>
        public VideoTranslationService(DeepLClient? deepLClient = null, TranslationSettings? settings = null)
        {
            _deepLClient = deepLClient;
            _settings = settings ?? new TranslationSettings();
        }

        /// <summary>
        /// Check if translation service is available
        /// </summary>
        public bool IsTranslationAvailable => _deepLClient != null;

        /// <summary>
        /// Translate video metadata (title, description) to multiple languages
        /// </summary>
        /// <param name="videoMetadata">Original video metadata</param>
        /// <param name="targetLanguages">Target languages to translate to</param>
        /// <param name="sourceLanguage">Source language (auto-detect if null)</param>
        /// <returns>Video metadata with translations</returns>
        public async Task<TranslatedVideoMetadata> TranslateVideoMetadataAsync(
            VideoMetadata videoMetadata,
            IEnumerable<string> targetLanguages,
            string? sourceLanguage = null)
        {
            if (_deepLClient == null)
            {
                // Return original metadata with no translations if service unavailable
                return new TranslatedVideoMetadata
                {
                    OriginalTitle = videoMetadata.Title,
                    OriginalDescription = videoMetadata.Description,
                    Translations = new Dictionary<string, VideoTranslation>()
                };
            }

            var result = new TranslatedVideoMetadata
            {
                OriginalTitle = videoMetadata.Title,
                OriginalDescription = videoMetadata.Description,
                Translations = new Dictionary<string, VideoTranslation>()
            };

            foreach (var targetLanguage in targetLanguages)
            {
                try
                {
                    // Translate title and description together for efficiency
                    var textsToTranslate = new List<string>();
                    if (!string.IsNullOrEmpty(videoMetadata.Title))
                        textsToTranslate.Add(videoMetadata.Title);
                    if (!string.IsNullOrEmpty(videoMetadata.Description))
                        textsToTranslate.Add(videoMetadata.Description);

                    if (textsToTranslate.Count == 0)
                        continue;

                    var translationResults = await _deepLClient.TranslateAsync(
                        textsToTranslate,
                        targetLanguage,
                        sourceLanguage,
                        null, // Formality - TODO: Fix type access issue
                        _settings.PreserveFormatting);

                    var translation = new VideoTranslation
                    {
                        LanguageCode = targetLanguage,
                        Title = translationResults.Length > 0 ? translationResults[0].Text : videoMetadata.Title,
                        Description = translationResults.Length > 1 ? translationResults[1].Text : videoMetadata.Description,
                        DetectedSourceLanguage = translationResults.FirstOrDefault()?.DetectedSourceLanguageCode
                    };

                    result.Translations[targetLanguage] = translation;
                }
                catch (Exception ex)
                {
                    // Log error but continue with other languages
                    Console.WriteLine($"Failed to translate to {targetLanguage}: {ex.Message}");
                    
                    // Add fallback translation
                    result.Translations[targetLanguage] = new VideoTranslation
                    {
                        LanguageCode = targetLanguage,
                        Title = videoMetadata.Title,
                        Description = videoMetadata.Description,
                        TranslationError = ex.Message
                    };
                }
            }

            return result;
        }

        /// <summary>
        /// Generate multilingual filename variations for better discoverability
        /// </summary>
        /// <param name="originalFilename">Original filename</param>
        /// <param name="translations">Available translations</param>
        /// <returns>Dictionary of language codes to suggested filenames</returns>
        public Dictionary<string, string> GenerateMultilingualFilenames(
            string originalFilename,
            Dictionary<string, VideoTranslation> translations)
        {
            var result = new Dictionary<string, string>();
            var baseFilename = Path.GetFileNameWithoutExtension(originalFilename);
            var extension = Path.GetExtension(originalFilename);

            foreach (var (languageCode, translation) in translations)
            {
                if (!string.IsNullOrEmpty(translation.Title))
                {
                    // Create safe filename from translated title
                    var safeTitle = SanitizeFilename(translation.Title);
                    var suggestedFilename = $"{safeTitle}_{languageCode.ToLower()}{extension}";
                    result[languageCode] = suggestedFilename;
                }
            }

            return result;
        }

        /// <summary>
        /// Translate channel name for better organization
        /// </summary>
        /// <param name="channelName">Original channel name</param>
        /// <param name="targetLanguage">Target language</param>
        /// <param name="sourceLanguage">Source language (auto-detect if null)</param>
        /// <returns>Translated channel name or original if translation fails</returns>
        public async Task<string> TranslateChannelNameAsync(
            string channelName,
            string targetLanguage,
            string? sourceLanguage = null)
        {
            if (_deepLClient == null || string.IsNullOrEmpty(channelName))
                return channelName;

            try
            {
                var translatedText = await _deepLClient.TranslateTextAsync(
                    channelName,
                    targetLanguage,
                    sourceLanguage,
                    null, // Formality - TODO: Fix type access issue
                    _settings.PreserveFormatting);

                return translatedText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to translate channel name '{channelName}': {ex.Message}");
                return channelName; // Return original on error
            }
        }

        /// <summary>
        /// Create multilingual tags from video metadata
        /// </summary>
        /// <param name="videoMetadata">Video metadata</param>
        /// <param name="targetLanguages">Languages to generate tags for</param>
        /// <returns>Dictionary of language codes to tag lists</returns>
        public async Task<Dictionary<string, List<string>>> GenerateMultilingualTagsAsync(
            VideoMetadata videoMetadata,
            IEnumerable<string> targetLanguages)
        {
            var result = new Dictionary<string, List<string>>();

            if (_deepLClient == null)
                return result;

            // Extract potential tags from title and description
            var potentialTags = ExtractTagsFromText(videoMetadata.Title, videoMetadata.Description);

            if (potentialTags.Count == 0)
                return result;

            foreach (var targetLanguage in targetLanguages)
            {
                try
                {
                    var translatedTags = await _deepLClient.TranslateAsync(
                        potentialTags,
                        targetLanguage,
                        null, // Auto-detect source
                        null, // Formality - TODO: Fix type access issue
                        false); // Don't preserve formatting for tags

                    result[targetLanguage] = translatedTags.Select(t => t.Text.Trim()).ToList();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to translate tags to {targetLanguage}: {ex.Message}");
                    result[targetLanguage] = potentialTags; // Use original tags as fallback
                }
            }

            return result;
        }

        /// <summary>
        /// Get translation usage statistics
        /// </summary>
        /// <returns>Usage information or null if service unavailable</returns>
        public async Task<Usage?> GetUsageAsync()
        {
            if (_deepLClient == null)
                return null;

            try
            {
                return await _deepLClient.GetUsageAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get translation usage: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get supported translation languages
        /// </summary>
        /// <returns>Supported target languages</returns>
        public async Task<TargetLanguage[]> GetSupportedLanguagesAsync()
        {
            if (_deepLClient == null)
                return Array.Empty<TargetLanguage>();

            try
            {
                return await _deepLClient.GetTargetLanguagesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get supported languages: {ex.Message}");
                return Array.Empty<TargetLanguage>();
            }
        }

        private static List<string> ExtractTagsFromText(string title, string description)
        {
            var tags = new List<string>();
            var combinedText = $"{title} {description}".ToLowerInvariant();

            // Simple keyword extraction (can be enhanced with NLP)
            var commonVideoKeywords = new[]
            {
                "tutorial", "review", "guide", "howto", "tips", "tricks",
                "gameplay", "walkthrough", "analysis", "reaction",
                "education", "entertainment", "music", "tech", "gaming"
            };

            foreach (var keyword in commonVideoKeywords)
            {
                if (combinedText.Contains(keyword))
                {
                    tags.Add(keyword);
                }
            }

            return tags.Distinct().ToList();
        }

        private static string SanitizeFilename(string filename)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", filename.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            
            // Limit length for filesystem compatibility
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100);
                
            return sanitized.Trim();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _deepLClient?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Configuration settings for translation operations
    /// </summary>
    public class TranslationSettings
    {
        private object? _formalityLevel = null;

        /// <summary>
        /// Set the formality level for translations
        /// Pass a value from DeepL.Model.Formality enum
        /// </summary>
        public void SetFormalityLevel(object? formality)
        {
            _formalityLevel = formality;
        }

        /// <summary>
        /// Get the formality level (returns object that should be cast to DeepL.Model.Formality)
        /// </summary>
        internal object? GetFormalityLevel() => _formalityLevel;

        /// <summary>
        /// Set formality to more formal
        /// </summary>
        public bool UseMoreFormal { get; set; } = false;

        /// <summary>
        /// Set formality to less formal
        /// </summary>
        public bool UseLessFormal { get; set; } = false;

        /// <summary>
        /// Whether to preserve formatting in translations
        /// </summary>
        public bool PreserveFormatting { get; set; } = true;

        /// <summary>
        /// Default target languages for automatic translation
        /// </summary>
        public List<string> DefaultTargetLanguages { get; set; } = new()
        {
            "AR", // Arabic
            "EN", // English
            "ES", // Spanish
            "FR", // French
            "DE"  // German
        };

        /// <summary>
        /// Whether to automatically translate video metadata during import
        /// </summary>
        public bool AutoTranslateMetadata { get; set; } = false;

        /// <summary>
        /// Whether to generate multilingual tags
        /// </summary>
        public bool GenerateMultilingualTags { get; set; } = false;
    }

    /// <summary>
    /// Video metadata for translation
    /// </summary>
    public class VideoMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Video metadata with translations
    /// </summary>
    public class TranslatedVideoMetadata
    {
        public string OriginalTitle { get; set; } = string.Empty;
        public string OriginalDescription { get; set; } = string.Empty;
        public Dictionary<string, VideoTranslation> Translations { get; set; } = new();
    }

    /// <summary>
    /// Translation for a specific language
    /// </summary>
    public class VideoTranslation
    {
        public string LanguageCode { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? DetectedSourceLanguage { get; set; }
        public string? TranslationError { get; set; }
        public List<string> Tags { get; set; } = new();
    }
}

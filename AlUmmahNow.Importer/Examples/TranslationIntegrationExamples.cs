using AlUmmahNow.Data.Translation;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AlUmmahNow.Importer.Examples
{
    /// <summary>
    /// Practical examples of integrating VideoTranslationService with video import workflow
    /// </summary>
    public class TranslationIntegrationExamples
    {
        /// <summary>
        /// Example 1: Initialize VideoImporter with translation support
        /// Shows how to set up the importer with DeepL translation enabled
        /// NOTE: This is a template method - actual implementation requires dependency injection
        /// </summary>
        public static VideoImporter? CreateVideoImporterWithTranslation(
            string deepLApiKey,
            bool isDeepLFreeApi)
        {
            // Initialize DeepL client
            var deepLClient = new DeepLClient(deepLApiKey, isDeepLFreeApi);

            // Configure translation settings
            var translationSettings = new TranslationSettings
            {
                AutoTranslateMetadata = true,
                GenerateMultilingualTags = true,
                DefaultTargetLanguages = new System.Collections.Generic.List<string>
                {
                    "AR", // Arabic
                    "EN", // English  
                    "ES", // Spanish
                    "FR", // French
                    "DE"  // German
                },
                PreserveFormatting = true
            };

            // Create translation service
            var translationService = new VideoTranslationService(deepLClient, translationSettings);

            // In actual implementation, inject all required dependencies:
            // var videoImporter = new VideoImporter(
            //     repo,
            //     videoStorageClient,
            //     imageStorageClient,
            //     bunnyStreamClient,
            //     dbContext,
            //     translationService  // Pass translation service here
            // );
            //
            // return videoImporter;

            // For this example, return null as it's a template
            return null;
        }

        /// <summary>
        /// Example 2: Import videos with automatic translation
        /// Shows the complete workflow from scanning to uploading with translation
        /// </summary>
        public static async Task ImportVideosWithTranslationAsync(
            VideoImporter importer,
            string rootFolder,
            string userId,
            string baseVideoStoreUrl,
            string baseImageStoreUrl)
        {
            // Subscribe to progress events to monitor translation progress
            importer.ProgressChanged += (sender, args) =>
            {
                Console.WriteLine($"Progress: {args.CurrentFile} - {args.CurrentOperation}");
                if (args.CurrentOperation.Contains("Translating"))
                {
                    Console.WriteLine($"  Translating metadata for: {args.CurrentFile}");
                }
            };

            // Start the import with automatic translation
            // Translation happens automatically for each video during import
            var sessionId = await importer.ImportMultiChannelAsync(
                rootFolder,
                userId,
                baseVideoStoreUrl,
                baseImageStoreUrl);

            Console.WriteLine($"Import session completed: {sessionId}");
            Console.WriteLine("Translated metadata stored in ImportVideo.VideoMetadata field");
        }

        /// <summary>
        /// Example 3: Retrieve and use translated metadata after import
        /// Shows how to access stored translations
        /// </summary>
        public static async Task<TranslatedVideoMetadata?> GetTranslatedMetadataAsync(
            ImportDbContext dbContext,
            Guid videoId)
        {
            var importVideo = await dbContext.ImportVideos.FindAsync(videoId);
            
            if (importVideo?.VideoMetadata == null)
                return null;

            try
            {
                // Deserialize the stored JSON metadata
                var translatedMetadata = System.Text.Json.JsonSerializer.Deserialize<TranslatedVideoMetadata>(
                    importVideo.VideoMetadata);

                // Now you can access translations
                if (translatedMetadata != null)
                {
                    Console.WriteLine($"Original Title: {translatedMetadata.OriginalTitle}");
                    
                    foreach (var (languageCode, translation) in translatedMetadata.Translations)
                    {
                        Console.WriteLine($"\n{languageCode}:");
                        Console.WriteLine($"  Title: {translation.Title}");
                        Console.WriteLine($"  Description: {translation.Description}");
                        
                        if (translation.Tags?.Count > 0)
                        {
                            Console.WriteLine($"  Tags: {string.Join(", ", translation.Tags)}");
                        }
                    }
                }

                return translatedMetadata;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to deserialize translated metadata: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Example 4: Batch translate existing videos without re-uploading
        /// Useful for adding translations to videos that are already imported
        /// </summary>
        public static async Task AddTranslationsToExistingVideosAsync(
            ImportDbContext dbContext,
            DeepLClient deepLClient,
            Guid sessionId)
        {
            using var translationService = new VideoTranslationService(deepLClient);

            var videos = await dbContext.ImportVideos
                .Where(v => v.SessionId == sessionId && 
                           v.Status == VideoImportStatus.Uploaded &&
                           string.IsNullOrEmpty(v.VideoMetadata))
                .ToListAsync();

            Console.WriteLine($"Found {videos.Count} videos without translations");

            foreach (var video in videos)
            {
                try
                {
                    var videoTitle = System.IO.Path.GetFileNameWithoutExtension(video.FileName);
                    var metadata = new VideoMetadata
                    {
                        Title = videoTitle,
                        Description = videoTitle,
                        ChannelName = video.Channel.ChannelName
                    };

                    var targetLanguages = new[] { "AR", "EN", "ES", "FR", "DE" };
                    var translatedMetadata = await translationService.TranslateVideoMetadataAsync(
                        metadata,
                        targetLanguages);

                    // Store the translations
                    video.VideoMetadata = System.Text.Json.JsonSerializer.Serialize(translatedMetadata);
                    await dbContext.SaveChangesAsync();

                    Console.WriteLine($"? Translated: {videoTitle}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"? Failed to translate {video.FileName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Example 5: Export translations for external use
        /// Generate translation reports or files for content management systems
        /// </summary>
        public static async Task ExportTranslationsAsync(
            ImportDbContext dbContext,
            Guid sessionId,
            string outputPath)
        {
            var videos = await dbContext.ImportVideos
                .Include(v => v.Channel)
                .Where(v => v.SessionId == sessionId && 
                           !string.IsNullOrEmpty(v.VideoMetadata))
                .ToListAsync();

            var exportData = new System.Collections.Generic.List<object>();

            foreach (var video in videos)
            {
                try
                {
                    var translatedMetadata = System.Text.Json.JsonSerializer.Deserialize<TranslatedVideoMetadata>(
                        video.VideoMetadata!);

                    if (translatedMetadata != null)
                    {
                        exportData.Add(new
                        {
                            VideoId = video.VideoId,
                            FileName = video.FileName,
                            Channel = video.Channel.ChannelName,
                            OriginalTitle = translatedMetadata.OriginalTitle,
                            OriginalDescription = translatedMetadata.OriginalDescription,
                            Translations = translatedMetadata.Translations.Select(t => new
                            {
                                Language = t.Key,
                                Title = t.Value.Title,
                                Description = t.Value.Description,
                                DetectedSourceLanguage = t.Value.DetectedSourceLanguage,
                                Tags = t.Value.Tags
                            })
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to process {video.FileName}: {ex.Message}");
                }
            }

            // Export to JSON file
            var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await System.IO.File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine($"Exported {exportData.Count} video translations to {outputPath}");
        }

        /// <summary>
        /// Example 6: Monitor translation usage during bulk operations
        /// Track DeepL API usage to avoid hitting limits
        /// </summary>
        public static async Task MonitorTranslationUsageAsync(
            DeepLClient deepLClient,
            Action<long, long> usageCallback)
        {
            using var translationService = new VideoTranslationService(deepLClient);

            var usage = await translationService.GetUsageAsync();
            
            if (usage?.Character != null)
            {
                var used = usage.Character.Count;
                var limit = usage.Character.Limit;
                var remaining = limit - used;
                var percentageUsed = (double)used / limit * 100;

                Console.WriteLine($"\nDeepL API Usage:");
                Console.WriteLine($"  Used: {used:N0} characters");
                Console.WriteLine($"  Limit: {limit:N0} characters");
                Console.WriteLine($"  Remaining: {remaining:N0} characters");
                Console.WriteLine($"  Percentage Used: {percentageUsed:F2}%");

                // Invoke callback for custom handling
                usageCallback?.Invoke(used, limit);

                // Warn if approaching limit
                if (percentageUsed > 80)
                {
                    Console.WriteLine("? WARNING: Approaching API character limit!");
                }
            }
        }

        /// <summary>
        /// Example 7: Configure translation based on video content type
        /// Different settings for educational vs entertainment content
        /// </summary>
        public static VideoTranslationService CreateTranslationServiceForContentType(
            DeepLClient deepLClient,
            VideoContentType contentType)
        {
            var settings = new TranslationSettings
            {
                PreserveFormatting = true,
                DefaultTargetLanguages = new System.Collections.Generic.List<string> { "AR", "EN", "ES", "FR", "DE" },
                AutoTranslateMetadata = true,
                GenerateMultilingualTags = true
            };

            switch (contentType)
            {
                case VideoContentType.Educational:
                    // settings.SetFormalityLevel(DeepL.Model.Formality.More); // TODO: Fix type access issue
                    break;
                case VideoContentType.Entertainment:
                    // settings.SetFormalityLevel(DeepL.Model.Formality.Less); // TODO: Fix type access issue
                    settings.PreserveFormatting = false;
                    settings.DefaultTargetLanguages = new System.Collections.Generic.List<string> { "AR", "EN", "ES" };
                    settings.GenerateMultilingualTags = false;
                    break;
                case VideoContentType.News:
                    // settings.SetFormalityLevel(DeepL.Model.Formality.Default); // TODO: Fix type access issue
                    settings.DefaultTargetLanguages = new System.Collections.Generic.List<string> { "AR", "EN" };
                    break;
            }

            return new VideoTranslationService(deepLClient, settings);
        }
    }

    /// <summary>
    /// Video content type classification
    /// </summary>
    public enum VideoContentType
    {
        Educational,
        Entertainment,
        News,
        Religious,
        Documentary,
        Tutorial
    }
}

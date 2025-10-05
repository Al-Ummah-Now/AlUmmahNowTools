# Video Translation Service - DeepL Integration

## Overview

The `VideoTranslationService` provides automatic translation of video metadata (titles, descriptions, channel names) using the DeepL API. This enables multilingual video platforms and improves content discoverability across language barriers.

## Features

- **Automatic Video Metadata Translation**: Translate video titles and descriptions to multiple languages
- **Channel Name Translation**: Organize content with translated channel names
- **Multilingual Filename Generation**: Create localized filenames for better file organization
- **Tag Generation**: Extract and translate keywords from video content
- **Batch Processing**: Translate entire video libraries efficiently
- **Error Handling**: Graceful fallback when translation fails
- **Usage Monitoring**: Track DeepL API character usage
- **Formality Control**: Adjust translation tone for different content types

## Prerequisites

1. **DeepL API Key**: Obtain from [DeepL API](https://www.deepl.com/pro-api)
2. **DeepL.net NuGet Package**: Already included in the project
3. **Storage Configuration**: Video import pipeline must be configured

## Quick Start

### 1. Initialize Translation Service

```csharp
using AlUmmahNow.Data.Translation;
using AlUmmahNow.Importer.Services;

// Create DeepL client
var deepLClient = new DeepLClient(
    apiKey: "your-deepl-api-key",
    isFreeApi: false  // Set to true if using free tier
);

// Create translation service with default settings
var translationService = new VideoTranslationService(deepLClient);

// Or with custom settings
var settings = new TranslationSettings
{
    AutoTranslateMetadata = true,
    GenerateMultilingualTags = true,
    DefaultTargetLanguages = new List<string> { "AR", "EN", "ES", "FR", "DE" },
    PreserveFormatting = true,
    Formality = Formality.More  // For formal content
};

var translationService = new VideoTranslationService(deepLClient, settings);
```

### 2. Integrate with Video Import

```csharp
// Create VideoImporter with translation support
var videoImporter = new VideoImporter(
    repo,
    videoStorageClient,
    imageStorageClient,
    bunnyStreamClient,
    dbContext,
    translationService  // Pass translation service here
);

// Import videos with automatic translation
var sessionId = await videoImporter.ImportMultiChannelAsync(
    rootFolder: "/path/to/videos",
    userId: "user-123",
    baseVideoStoreUrl: "https://storage.example.com/videos/",
    baseImageStoreUrl: "https://storage.example.com/images/"
);

// Translations are automatically stored in ImportVideo.VideoMetadata as JSON
```

### 3. Translate Existing Videos

```csharp
using AlUmmahNow.Importer.Examples;
using AlUmmahNow.Data.Database;

// Add translations to videos that were imported without translation
await TranslationIntegrationExamples.AddTranslationsToExistingVideosAsync(
    dbContext,
    deepLClient,
    sessionId
);
```

## Use Cases

### Use Case 1: Basic Video Metadata Translation

Translate a video's title and description to multiple languages:

```csharp
using AlUmmahNow.Importer.Services;

var translatedMetadata = await VideoTranslationUseCases.TranslateVideoMetadataBasicAsync(
    deepLClient,
    videoTitle: "Introduction to Programming",
    videoDescription: "Learn the basics of programming in this comprehensive tutorial"
);

// Access translations
foreach (var (languageCode, translation) in translatedMetadata.Translations)
{
    Console.WriteLine($"{languageCode}: {translation.Title}");
    // AR: ????? ??? ???????
    // EN: Introduction to Programming
    // ES: Introducción a la programación
    // FR: Introduction à la programmation
    // DE: Einführung in die Programmierung
}
```

### Use Case 2: Generate Multilingual Filenames

Create localized filenames for better organization:

```csharp
var multilingualFilenames = VideoTranslationUseCases.GenerateMultilingualFilenames(
    deepLClient,
    originalFilename: "intro-to-programming.mp4",
    translatedMetadata
);

// Result:
// {
//     "AR": "?????-???-???????_ar.mp4",
//     "EN": "Introduction-to-Programming_en.mp4",
//     "ES": "Introducción-a-la-programación_es.mp4"
// }
```

### Use Case 3: Translate Channel Names

Organize content with translated channel names:

```csharp
var channelNames = new List<string> 
{ 
    "Tech Tutorials", 
    "Cooking Channel", 
    "History Lessons" 
};

var translatedChannels = await VideoTranslationUseCases.TranslateChannelNamesAsync(
    deepLClient,
    channelNames,
    targetLanguage: "AR"
);

// Result:
// {
//     "Tech Tutorials": "???? ???????????",
//     "Cooking Channel": "???? ?????",
//     "History Lessons": "???? ???????"
// }
```

### Use Case 4: Generate Multilingual Tags

Create searchable tags in multiple languages for better SEO:

```csharp
var multilingualTags = await VideoTranslationUseCases.GenerateMultilingualTagsAsync(
    deepLClient,
    videoTitle: "Python Programming Tutorial",
    videoDescription: "Learn Python programming with hands-on examples"
);

// Result:
// {
//     "AR": ["?????", "?????", "????", "??????"],
//     "EN": ["programming", "education", "tutorial", "python"],
//     "ES": ["programación", "educación", "tutorial", "python"]
// }
```

### Use Case 5: Complete Multilingual Package

Prepare a video for a multilingual platform with all translations:

```csharp
var package = await VideoTranslationUseCases.PrepareVideoForMultilingualPlatformAsync(
    deepLClient,
    videoFile: "tutorial.mp4",
    videoTitle: "Introduction to Machine Learning",
    videoDescription: "Comprehensive guide to ML fundamentals",
    channelName: "AI Academy"
);

// Package includes:
// - Translated metadata (title, description)
// - Multilingual filenames
// - Multilingual tags
// - Translated channel names
```

### Use Case 6: Monitor API Usage

Track DeepL API usage before running bulk translations:

```csharp
var serviceInfo = await VideoTranslationUseCases.CheckTranslationServiceAsync(deepLClient);

Console.WriteLine($"Characters Used: {serviceInfo.CharacterCount:N0}");
Console.WriteLine($"Characters Limit: {serviceInfo.CharacterLimit:N0}");
Console.WriteLine($"Remaining: {serviceInfo.RemainingCharacters:N0}");
Console.WriteLine($"Supported Languages: {serviceInfo.SupportedLanguages.Count}");

// Warn if approaching limit
if (serviceInfo.RemainingCharacters < 50000)
{
    Console.WriteLine("? WARNING: Low on DeepL API characters!");
}
```

### Use Case 7: Bulk Translate Video Library

Process multiple videos with error handling:

```csharp
var videos = new List<(string title, string description, string filename)>
{
    ("Tutorial 1", "Learn basics", "tutorial1.mp4"),
    ("Tutorial 2", "Advanced concepts", "tutorial2.mp4"),
    ("Tutorial 3", "Expert level", "tutorial3.mp4")
};

var result = await VideoTranslationUseCases.BulkTranslateVideosAsync(
    deepLClient,
    videos,
    targetLanguages: new[] { "AR", "EN", "ES", "FR" }
);

Console.WriteLine($"Total Processed: {result.TotalProcessed}");
Console.WriteLine($"Successful: {result.SuccessCount}");
Console.WriteLine($"Failed: {result.FailureCount}");

// Process results
foreach (var translation in result.SuccessfulTranslations)
{
    Console.WriteLine($"? {translation.OriginalTitle}");
}

foreach (var failure in result.FailedTranslations)
{
    Console.WriteLine($"? {failure.Title}: {failure.Error}");
}
```

### Use Case 8: Content-Type Specific Translation

Different translation settings for different content types:

```csharp
// Formal educational content
var educationalService = TranslationIntegrationExamples.CreateTranslationServiceForContentType(
    deepLClient,
    VideoContentType.Educational
);
// Uses FormalityLevel = Formality.More

// Casual entertainment content
var entertainmentService = TranslationIntegrationExamples.CreateTranslationServiceForContentType(
    deepLClient,
    VideoContentType.Entertainment
);
// Uses FormalityLevel = Formality.Less

// News content
var newsService = TranslationIntegrationExamples.CreateTranslationServiceForContentType(
    deepLClient,
    VideoContentType.News
);
// Uses FormalityLevel = Formality.Default
```

### Use Case 9: Export Translations

Generate translation reports for external systems:

```csharp
await TranslationIntegrationExamples.ExportTranslationsAsync(
    dbContext,
    sessionId,
    outputPath: "translations-export.json"
);

// Creates JSON file with all translations:
// [
//   {
//     "VideoId": "guid",
//     "FileName": "video.mp4",
//     "Channel": "Tech Channel",
//     "OriginalTitle": "Tutorial",
//     "Translations": [
//       {
//         "Language": "AR",
//         "Title": "??? ??????",
//         "Description": "...",
//         "Tags": ["?????", "?????"]
//       }
//     ]
//   }
// ]
```

## Configuration

### Translation Settings

```csharp
public class TranslationSettings
{
    /// <summary>
    /// Formality level for translations (Default, More formal, Less formal)
    /// </summary>
    public Formality? FormalityLevel { get; set; } = null;

    /// <summary>
    /// Whether to preserve formatting in translations (line breaks, etc.)
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
```

### Supported Languages

DeepL supports translation between many languages. Common target languages:

- **AR** - Arabic (???????)
- **BG** - Bulgarian (?????????)
- **CS** - Czech (?eština)
- **DA** - Danish (Dansk)
- **DE** - German (Deutsch)
- **EL** - Greek (????????)
- **EN** - English
- **ES** - Spanish (Español)
- **ET** - Estonian (Eesti)
- **FI** - Finnish (Suomi)
- **FR** - French (Français)
- **HU** - Hungarian (Magyar)
- **ID** - Indonesian (Indonesia)
- **IT** - Italian (Italiano)
- **JA** - Japanese (???)
- **KO** - Korean (???)
- **LT** - Lithuanian (Lietuvi?)
- **LV** - Latvian (Latviešu)
- **NB** - Norwegian (Norsk Bokmål)
- **NL** - Dutch (Nederlands)
- **PL** - Polish (Polski)
- **PT** - Portuguese (Português)
- **RO** - Romanian (Român?)
- **RU** - Russian (???????)
- **SK** - Slovak (Sloven?ina)
- **SL** - Slovenian (Slovenš?ina)
- **SV** - Swedish (Svenska)
- **TR** - Turkish (Türkçe)
- **UK** - Ukrainian (??????????)
- **ZH** - Chinese (??)

Check the latest list: `await translationService.GetSupportedLanguagesAsync()`

## Database Schema

Translations are stored as JSON in the `ImportVideo.VideoMetadata` field:

```json
{
  "OriginalTitle": "Introduction to Programming",
  "OriginalDescription": "Learn the basics...",
  "Translations": {
    "AR": {
      "LanguageCode": "AR",
      "Title": "????? ??? ???????",
      "Description": "???? ?????????...",
      "DetectedSourceLanguage": "EN",
      "Tags": ["?????", "?????", "????"]
    },
    "ES": {
      "LanguageCode": "ES",
      "Title": "Introducción a la programación",
      "Description": "Aprende los conceptos básicos...",
      "DetectedSourceLanguage": "EN",
      "Tags": ["programación", "educación", "tutorial"]
    }
  }
}
```

## Error Handling

The translation service includes comprehensive error handling:

```csharp
try
{
    var translatedMetadata = await translationService.TranslateVideoMetadataAsync(
        metadata,
        targetLanguages
    );
}
catch (DeepLException ex)
{
    // DeepL API specific error (quota exceeded, invalid language, etc.)
    Console.WriteLine($"DeepL API Error: {ex.Message}");
}
catch (Exception ex)
{
    // General error (network issues, etc.)
    Console.WriteLine($"Translation Error: {ex.Message}");
}
```

If translation fails for a video, the import process continues with original metadata.

## Performance Considerations

### API Rate Limits

- **Free API**: 500,000 characters/month
- **Pro API**: Varies by plan (starts at 1M characters/month)

### Best Practices

1. **Monitor Usage**: Check API usage before bulk operations
2. **Batch Processing**: Use bulk translation methods for efficiency
3. **Cache Results**: Store translations to avoid re-translating
4. **Error Handling**: Implement retry logic for transient failures
5. **Selective Translation**: Only translate when necessary

### Character Estimation

Approximate character counts for video metadata:

- **Title**: 50-100 characters
- **Description**: 200-500 characters
- **Per Video**: ~300-600 characters
- **Per Language**: Multiply by target language count

**Example**: 100 videos × 400 chars × 5 languages = 200,000 characters

## Troubleshooting

### Common Issues

**1. API Key Invalid**
```
Error: Authorization failed. Please check your API key.
```
Solution: Verify your DeepL API key is correct and active.

**2. Quota Exceeded**
```
Error: Quota exceeded. The maximum number of characters has been translated.
```
Solution: Upgrade your DeepL plan or wait for quota reset.

**3. Unsupported Language**
```
Error: Target language 'XX' is not supported.
```
Solution: Check supported languages with `GetSupportedLanguagesAsync()`.

**4. Translation Returned Empty**
```
Warning: Translation returned empty for language 'AR'
```
Solution: Check if source text is valid and not empty.

### Debug Mode

Enable detailed logging:

```csharp
var translationService = new VideoTranslationService(deepLClient, settings);

// Translations automatically log errors to console
// Check console output for detailed error messages
```

## Integration Examples

See the following files for complete examples:

- **`VideoTranslationUseCases.cs`**: Nine comprehensive use cases
- **`TranslationIntegrationExamples.cs`**: Seven practical integration examples
- **`VideoImporter.cs`**: Automatic translation during import

## API Reference

### VideoTranslationService Methods

- `TranslateVideoMetadataAsync()` - Translate video title and description
- `GenerateMultilingualFilenames()` - Create localized filenames
- `TranslateChannelNameAsync()` - Translate channel name
- `GenerateMultilingualTagsAsync()` - Generate tags in multiple languages
- `GetTranslationUsageAsync()` - Check API usage
- `GetSupportedLanguagesAsync()` - List supported languages

## License

This integration uses the DeepL API. Review [DeepL's terms of service](https://www.deepl.com/pro-license) for usage restrictions.

## Support

For issues or questions:
1. Check this README and examples
2. Review DeepL API documentation
3. Check console output for error messages
4. Open an issue in the repository

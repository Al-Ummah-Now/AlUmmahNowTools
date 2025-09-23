using AlUmmahNow.Data.Translation;
using DeepL.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlUmmahNow.DataImporter
{
    /// <summary>
    /// Example usage of the DeepL client for translation services using official DeepL.net library
    /// </summary>
    public class DeepLUsageExample
    {
        private readonly DeepLClient _deepLClient;

        public DeepLUsageExample(DeepLClient deepLClient)
        {
            _deepLClient = deepLClient;
        }

        /// <summary>
        /// Example of translating a single text
        /// </summary>
        public async Task TranslateSingleTextExample()
        {
            try
            {
                // Translate from English to Arabic
                var translatedText = await _deepLClient.TranslateTextAsync(
                    "Hello, how are you today?", 
                    "AR", // Target: Arabic
                    "EN"  // Source: English
                );

                Console.WriteLine($"Original: Hello, how are you today?");
                Console.WriteLine($"Translated: {translatedText}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Example of translating multiple texts at once
        /// </summary>
        public async Task TranslateMultipleTextsExample()
        {
            try
            {
                var textsToTranslate = new List<string>
                {
                    "Welcome to our platform",
                    "Please enter your credentials",
                    "Thank you for using our service"
                };

                var results = await _deepLClient.TranslateAsync(
                    textsToTranslate,
                    "AR", // Target: Arabic
                    "EN"  // Source: English
                );

                if (results != null)
                {
                    for (int i = 0; i < textsToTranslate.Count; i++)
                    {
                        Console.WriteLine($"Original: {textsToTranslate[i]}");
                        Console.WriteLine($"Translated: {results[i].Text}");
                        Console.WriteLine($"Detected Language: {results[i].DetectedSourceLanguageCode}");
                        Console.WriteLine("---");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Example of checking usage statistics
        /// </summary>
        public async Task CheckUsageExample()
        {
            try
            {
                var usage = await _deepLClient.GetUsageAsync();
                
                if (usage != null)
                {
                    Console.WriteLine($"Characters used: {usage.Character.Count:N0}");
                    Console.WriteLine($"Character limit: {usage.Character.Limit:N0}");
                    Console.WriteLine($"Usage percentage: {(double)usage.Character.Count / usage.Character.Limit * 100:F2}%");
                    
                    if (usage.Document != null)
                    {
                        Console.WriteLine($"Documents translated: {usage.Document.Count:N0}");
                        Console.WriteLine($"Document limit: {usage.Document.Limit:N0}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get usage: {ex.Message}");
            }
        }

        /// <summary>
        /// Example of getting supported languages
        /// </summary>
        public async Task GetSupportedLanguagesExample()
        {
            try
            {
                Console.WriteLine("=== Supported Source Languages ===");
                var sourceLanguages = await _deepLClient.GetSourceLanguagesAsync();
                if (sourceLanguages != null)
                {
                    foreach (var lang in sourceLanguages)
                    {
                        Console.WriteLine($"{lang.Code}: {lang.Name}");
                    }
                }

                Console.WriteLine("\n=== Supported Target Languages ===");
                var targetLanguages = await _deepLClient.GetTargetLanguagesAsync();
                if (targetLanguages != null)
                {
                    foreach (var lang in targetLanguages)
                    {
                        var formalitySupport = lang.SupportsFormality ? " (Supports Formality)" : "";
                        Console.WriteLine($"{lang.Code}: {lang.Name}{formalitySupport}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get languages: {ex.Message}");
            }
        }

        /// <summary>
        /// Example of translating with formality settings
        /// </summary>
        public async Task TranslateWithFormalityExample()
        {
            try
            {
                var text = "How are you doing?";

                // Formal translation
                var formalTranslation = await _deepLClient.TranslateTextAsync(
                    text,
                    "DE", // German
                    "EN", // English
                    DeepL.Formality.More // More formal
                );

                // Informal translation
                var informalTranslation = await _deepLClient.TranslateTextAsync(
                    text,
                    "DE", // German
                    "EN", // English
                    DeepL.Formality.Less // Less formal
                );

                Console.WriteLine($"Original: {text}");
                Console.WriteLine($"Formal: {formalTranslation}");
                Console.WriteLine($"Informal: {informalTranslation}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation with formality failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Example of translating HTML content
        /// </summary>
        public async Task TranslateHtmlExample()
        {
            try
            {
                var htmlContent = "<h1>Welcome</h1><p>This is a <strong>test</strong> message.</p>";

                var translatedHtml = await _deepLClient.TranslateHtmlAsync(
                    htmlContent,
                    "ES", // Spanish
                    "EN"  // English
                );

                Console.WriteLine($"Original HTML: {htmlContent}");
                Console.WriteLine($"Translated HTML: {translatedHtml}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTML translation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Example of creating and using a glossary
        /// </summary>
        public async Task GlossaryExample()
        {
            try
            {
                // Create a glossary
                var glossaryName = "TechTerms_" + DateTime.Now.Ticks;
                var entries = new Dictionary<string, string>
                {
                    { "API", "API" }, // Keep API as API in target language
                    { "database", "base de datos" },
                    { "server", "servidor" }
                };

                var glossary = await _deepLClient.CreateGlossaryAsync(
                    glossaryName,
                    "EN", // English
                    "ES", // Spanish
                    entries
                );

                Console.WriteLine($"Created glossary: {glossary.Name} (ID: {glossary.GlossaryId})");

                // List all glossaries
                var glossaries = await _deepLClient.GetGlossariesAsync();
                Console.WriteLine($"Total glossaries: {glossaries.Length}");

                // Clean up - delete the glossary
                await _deepLClient.DeleteGlossaryAsync(glossary.GlossaryId);
                Console.WriteLine($"Deleted glossary: {glossary.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Glossary example failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Example of getting glossary language pairs
        /// </summary>
        public async Task GetGlossaryLanguagePairsExample()
        {
            try
            {
                var languagePairs = await _deepLClient.GetGlossaryLanguagesAsync();
                
                Console.WriteLine("=== Supported Glossary Language Pairs ===");
                foreach (var pair in languagePairs)
                {
                    Console.WriteLine($"{pair.SourceLanguageCode} -> {pair.TargetLanguageCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get glossary language pairs: {ex.Message}");
            }
        }
    }
}
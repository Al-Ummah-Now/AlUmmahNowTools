using DeepL;
using DeepL.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlUmmahNow.Data.Translation
{
    public class DeepLClient : IDisposable
    {
        private readonly Translator _translator;

        public DeepLClient(string apiKey, bool isFreeApi = false)
        {
            var options = new TranslatorOptions
            {
                ServerUrl = isFreeApi ? "https://api-free.deepl.com" : "https://api.deepl.com"
            };
            _translator = new Translator(apiKey, options);
        }

        /// <summary>
        /// Translates text using the DeepL API
        /// </summary>
        /// <param name="texts">Text to translate (can be multiple strings)</param>
        /// <param name="targetLanguage">Target language code (e.g., "EN", "DE", "FR")</param>
        /// <param name="sourceLanguage">Source language code (optional, auto-detect if null)</param>
        /// <param name="formality">Formality level</param>
        /// <param name="preserveFormatting">Whether to preserve formatting</param>
        /// <returns>Translation results</returns>
        public async Task<TextResult[]> TranslateAsync(
            IEnumerable<string> texts, 
            string targetLanguage, 
            string? sourceLanguage = null,
            Formality? formality = null,
            bool preserveFormatting = false)
        {
            try
            {
                var options = new TextTranslateOptions();
                
                if (formality.HasValue)
                {
                    options.Formality = formality.Value;
                }
                
                options.PreserveFormatting = preserveFormatting;

                if (!string.IsNullOrEmpty(sourceLanguage))
                {
                    return await _translator.TranslateTextAsync(texts, sourceLanguage, targetLanguage, options);
                }
                else
                {
                    return await _translator.TranslateTextAsync(texts, null, targetLanguage, options);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text with DeepL: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Translates a single text string
        /// </summary>
        /// <param name="text">Text to translate</param>
        /// <param name="targetLanguage">Target language code</param>
        /// <param name="sourceLanguage">Source language code (optional)</param>
        /// <param name="formality">Formality level</param>
        /// <param name="preserveFormatting">Whether to preserve formatting</param>
        /// <returns>Translated text</returns>
        public async Task<string> TranslateTextAsync(
            string text, 
            string targetLanguage, 
            string? sourceLanguage = null,
            Formality? formality = null,
            bool preserveFormatting = false)
        {
            try
            {
                var options = new TextTranslateOptions();
                
                if (formality.HasValue)
                {
                    options.Formality = formality.Value;
                }
                
                options.PreserveFormatting = preserveFormatting;

                TextResult result;
                if (!string.IsNullOrEmpty(sourceLanguage))
                {
                    result = await _translator.TranslateTextAsync(text, sourceLanguage, targetLanguage, options);
                }
                else
                {
                    result = await _translator.TranslateTextAsync(text, null, targetLanguage, options);
                }

                return result.Text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text with DeepL: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets usage information for the current API key
        /// </summary>
        /// <returns>Usage information</returns>
        public async Task<Usage> GetUsageAsync()
        {
            try
            {
                return await _translator.GetUsageAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting DeepL usage: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets supported source languages
        /// </summary>
        /// <returns>List of supported source languages</returns>
        public async Task<SourceLanguage[]> GetSourceLanguagesAsync()
        {
            try
            {
                return await _translator.GetSourceLanguagesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting DeepL source languages: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets supported target languages
        /// </summary>
        /// <returns>List of supported target languages</returns>
        public async Task<TargetLanguage[]> GetTargetLanguagesAsync()
        {
            try
            {
                return await _translator.GetTargetLanguagesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting DeepL target languages: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets supported glossary language pairs
        /// </summary>
        /// <returns>Glossary language pairs information</returns>
        public async Task<GlossaryLanguagePair[]> GetGlossaryLanguagesAsync()
        {
            try
            {
                return await _translator.GetGlossaryLanguagesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting DeepL glossary languages: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Translates HTML content while preserving HTML structure
        /// </summary>
        /// <param name="htmlText">HTML text to translate</param>
        /// <param name="targetLanguage">Target language code</param>
        /// <param name="sourceLanguage">Source language code (optional)</param>
        /// <param name="formality">Formality level</param>
        /// <returns>Translated HTML text</returns>
        public async Task<string> TranslateHtmlAsync(
            string htmlText,
            string targetLanguage,
            string? sourceLanguage = null,
            Formality? formality = null)
        {
            try
            {
                var options = new TextTranslateOptions
                {
                    TagHandling = "html"
                };

                if (formality.HasValue)
                {
                    options.Formality = formality.Value;
                }

                TextResult result;
                if (!string.IsNullOrEmpty(sourceLanguage))
                {
                    result = await _translator.TranslateTextAsync(htmlText, sourceLanguage, targetLanguage, options);
                }
                else
                {
                    result = await _translator.TranslateTextAsync(htmlText, null, targetLanguage, options);
                }

                return result.Text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating HTML with DeepL: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Creates a glossary for improved translation quality
        /// </summary>
        /// <param name="name">Name of the glossary</param>
        /// <param name="sourceLanguage">Source language code</param>
        /// <param name="targetLanguage">Target language code</param>
        /// <param name="entries">Dictionary of term pairs (source -> target)</param>
        /// <returns>Created glossary information</returns>
        public async Task<GlossaryInfo> CreateGlossaryAsync(
            string name,
            string sourceLanguage,
            string targetLanguage,
            Dictionary<string, string> entries)
        {
            try
            {
                var glossaryEntries = new GlossaryEntries(entries);
                return await _translator.CreateGlossaryAsync(name, sourceLanguage, targetLanguage, glossaryEntries);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating DeepL glossary: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Lists all glossaries for the account
        /// </summary>
        /// <returns>Array of glossary information</returns>
        public async Task<GlossaryInfo[]> GetGlossariesAsync()
        {
            try
            {
                var glossaries = await _translator.ListGlossariesAsync();
                return glossaries.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting DeepL glossaries: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Deletes a glossary
        /// </summary>
        /// <param name="glossaryId">ID of the glossary to delete</param>
        public async Task DeleteGlossaryAsync(string glossaryId)
        {
            try
            {
                await _translator.DeleteGlossaryAsync(glossaryId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting DeepL glossary: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            _translator?.Dispose();
        }
    }
}
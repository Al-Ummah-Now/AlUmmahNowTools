using System.Globalization;
using System.Text;
using System.Text.Json;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.Data.Services;

/// <summary>
/// Service for managing watch templates and batch watch creation
/// </summary>
public interface IWatchTemplateService
{
    /// <summary>
    /// Get all available watch templates
    /// </summary>
    Task<List<WatchTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific watch template by ID
    /// </summary>
    Task<WatchTemplate?> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save a watch template (create or update)
    /// </summary>
    Task<WatchTemplate> SaveTemplateAsync(WatchTemplate template, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a watch template
    /// </summary>
    Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate watch configurations from a template
    /// </summary>
    Task<WatchTemplatePreview> GenerateWatchesFromTemplateAsync(
        WatchTemplate template,
        int count,
        int startingNumber = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import watch configurations from CSV file
    /// </summary>
    Task<WatchBatchImport> ImportFromCsvAsync(string csvPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Import watch configurations from JSON file
    /// </summary>
    Task<WatchBatchImport> ImportFromJsonAsync(string jsonPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Export watch configurations to JSON
    /// </summary>
    Task<string> ExportToJsonAsync(List<CreateWatchRequest> watches, string outputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get built-in templates
    /// </summary>
    Task<List<WatchTemplate>> GetBuiltInTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initialize built-in templates if they don't exist
    /// </summary>
    Task InitializeBuiltInTemplatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of watch template service
/// </summary>
public class WatchTemplateService : IWatchTemplateService
{
    private readonly ILogger<WatchTemplateService> _logger;
    private readonly string _templatesDirectory;
    private readonly List<WatchTemplate> _builtInTemplates;

    public WatchTemplateService(ILogger<WatchTemplateService> logger)
    {
        _logger = logger;
        _templatesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AlUmmahNow",
            "WatchTemplates"
        );

        Directory.CreateDirectory(_templatesDirectory);

        _builtInTemplates = CreateBuiltInTemplates();
    }

    private List<WatchTemplate> CreateBuiltInTemplates()
    {
        return new List<WatchTemplate>
        {
            new WatchTemplate
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Name = "Numbered Channels",
                Description = "Creates watches for numbered channels (Channel_01, Channel_02, etc.)",
                BaseChannelNamePattern = "Channel_{number:D2}",
                BasePathPattern = "C:\\Videos\\Channel_{number:D2}",
                ProcessingDelayMs = 5000,
                IncludeSubdirectories = true,
                SupportedExtensions = ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            },
            new WatchTemplate
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Name = "Category-based",
                Description = "Creates watches for different content categories",
                BaseChannelNamePattern = "{category}",
                BasePathPattern = "C:\\Videos\\{category}",
                ProcessingDelayMs = 10000,
                IncludeSubdirectories = true,
                SupportedExtensions = ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            },
            new WatchTemplate
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Name = "Language-based",
                Description = "Creates watches for different language channels",
                BaseChannelNamePattern = "{language}_Channel",
                BasePathPattern = "C:\\Videos\\{language}",
                ProcessingDelayMs = 5000,
                IncludeSubdirectories = true,
                SupportedExtensions = ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            },
            new WatchTemplate
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                Name = "Date-based Organization",
                Description = "Creates watches organized by date (YYYY-MM structure)",
                BaseChannelNamePattern = "Videos_{date:yyyy-MM}",
                BasePathPattern = "C:\\Videos\\{date:yyyy}\\{date:MM}",
                ProcessingDelayMs = 5000,
                IncludeSubdirectories = true,
                SupportedExtensions = ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            }
        };
    }

    public async Task<List<WatchTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting all watch templates");

            var templates = new List<WatchTemplate>();

            // Add built-in templates
            templates.AddRange(_builtInTemplates);

            // Load user templates from files
            var templateFiles = Directory.GetFiles(_templatesDirectory, "*.json");
            foreach (var file in templateFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    var template = JsonSerializer.Deserialize<WatchTemplate>(json);
                    if (template != null && !template.IsBuiltIn)
                    {
                        templates.Add(template);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load template from file: {FilePath}", file);
                }
            }

            _logger.LogInformation("Loaded {Count} watch templates ({BuiltIn} built-in, {Custom} custom)",
                templates.Count, _builtInTemplates.Count, templates.Count - _builtInTemplates.Count);

            return templates;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get watch templates");
            return new List<WatchTemplate>();
        }
    }

    public async Task<WatchTemplate?> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check built-in templates first
            var builtIn = _builtInTemplates.FirstOrDefault(t => t.Id == id);
            if (builtIn != null)
            {
                return builtIn;
            }

            // Check user templates
            var filePath = Path.Combine(_templatesDirectory, $"{id}.json");
            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<WatchTemplate>(json);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get template {TemplateId}", id);
            return null;
        }
    }

    public async Task<WatchTemplate> SaveTemplateAsync(WatchTemplate template, CancellationToken cancellationToken = default)
    {
        try
        {
            if (template.IsBuiltIn)
            {
                throw new InvalidOperationException("Cannot save built-in templates");
            }

            template.UpdatedAt = DateTime.UtcNow;

            if (template.Id == Guid.Empty)
            {
                template.Id = Guid.NewGuid();
                template.CreatedAt = DateTime.UtcNow;
            }

            var filePath = Path.Combine(_templatesDirectory, $"{template.Id}.json");
            var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Saved watch template: {TemplateName} ({TemplateId})", template.Name, template.Id);

            return template;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save template: {TemplateName}", template.Name);
            throw;
        }
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            // Cannot delete built-in templates
            if (_builtInTemplates.Any(t => t.Id == id))
            {
                _logger.LogWarning("Cannot delete built-in template {TemplateId}", id);
                return false;
            }

            var filePath = Path.Combine(_templatesDirectory, $"{id}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted watch template {TemplateId}", id);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete template {TemplateId}", id);
            return false;
        }
    }

    public Task<WatchTemplatePreview> GenerateWatchesFromTemplateAsync(
        WatchTemplate template,
        int count,
        int startingNumber = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating {Count} watches from template {TemplateName}", count, template.Name);

            var watches = new List<CreateWatchRequest>();
            var now = DateTime.Now;

            for (int i = 0; i < count; i++)
            {
                var number = startingNumber + i;
                var index = i;

                // Create variable replacements
                var variables = new Dictionary<string, string>
                {
                    { "number", number.ToString() },
                    { "number:D2", number.ToString("D2") },
                    { "number:D3", number.ToString("D3") },
                    { "number:D4", number.ToString("D4") },
                    { "index", index.ToString() },
                    { "date", now.ToString("yyyy-MM-dd") },
                    { "date:yyyy", now.ToString("yyyy") },
                    { "date:MM", now.ToString("MM") },
                    { "date:dd", now.ToString("dd") },
                    { "date:yyyy-MM", now.ToString("yyyy-MM") },
                    { "time", now.ToString("HHmmss") },
                    { "time:HH", now.ToString("HH") },
                    { "time:mm", now.ToString("mm") },
                    { "time:ss", now.ToString("ss") }
                };

                // Generate channel name
                var channelName = ReplaceVariables(template.BaseChannelNamePattern, variables);

                // Generate watch path
                var watchPath = ReplaceVariables(template.BasePathPattern, variables);

                // Add channel name to variables for path replacement
                variables["channel"] = channelName.Replace(" ", "_");
                watchPath = ReplaceVariables(watchPath, variables);

                var watch = new CreateWatchRequest
                {
                    ChannelName = channelName,
                    WatchPath = watchPath,
                    IncludeSubdirectories = template.IncludeSubdirectories,
                    ProcessingDelayMs = template.ProcessingDelayMs,
                    FileExtensions = template.SupportedExtensions,
                    CreatedBy = template.CreatedBy
                };

                watches.Add(watch);
            }

            var preview = new WatchTemplatePreview
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                PreviewWatches = watches
            };

            _logger.LogInformation("Generated {Count} watch configurations from template", watches.Count);

            return Task.FromResult(preview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate watches from template");
            throw;
        }
    }

    public async Task<WatchBatchImport> ImportFromCsvAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Importing watches from CSV: {CsvPath}", csvPath);

            var result = new WatchBatchImport();

            if (!File.Exists(csvPath))
            {
                result.ValidationErrors[0] = new List<string> { "File does not exist" };
                return result;
            }

            var lines = await File.ReadAllLinesAsync(csvPath, cancellationToken).ConfigureAwait(false);
            if (lines.Length < 2)
            {
                result.ValidationErrors[0] = new List<string> { "CSV file must contain header and at least one data row" };
                return result;
            }

            // Parse header
            var header = lines[0].Split(',').Select(h => h.Trim()).ToArray();
            var expectedHeaders = new[] { "ChannelName", "WatchPath", "ProcessingDelay", "IncludeSubdirs", "Extensions" };

            if (!header.SequenceEqual(expectedHeaders))
            {
                result.ValidationErrors[0] = new List<string>
                {
                    $"Invalid CSV header. Expected: {string.Join(",", expectedHeaders)}"
                };
                return result;
            }

            // Parse data rows
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var errors = new List<string>();
                var parts = ParseCsvLine(line);

                if (parts.Length < 5)
                {
                    errors.Add($"Invalid row format. Expected 5 columns, got {parts.Length}");
                    result.ValidationErrors[i] = errors;
                    continue;
                }

                var channelName = parts[0].Trim();
                var watchPath = parts[1].Trim();
                var processingDelayStr = parts[2].Trim();
                var includeSubdirsStr = parts[3].Trim();
                var extensions = parts[4].Trim();

                // Validate fields
                if (string.IsNullOrWhiteSpace(channelName))
                {
                    errors.Add("Channel name is required");
                }

                if (string.IsNullOrWhiteSpace(watchPath))
                {
                    errors.Add("Watch path is required");
                }

                if (!int.TryParse(processingDelayStr, out var processingDelay) || processingDelay < 1)
                {
                    errors.Add("Processing delay must be a positive integer (seconds)");
                }

                if (!bool.TryParse(includeSubdirsStr, out var includeSubdirs))
                {
                    errors.Add("Include subdirectories must be true or false");
                }

                if (string.IsNullOrWhiteSpace(extensions))
                {
                    errors.Add("File extensions are required");
                }

                if (errors.Count > 0)
                {
                    result.ValidationErrors[i] = errors;
                    continue;
                }

                // Create watch request
                var watch = new CreateWatchRequest
                {
                    ChannelName = channelName,
                    WatchPath = watchPath,
                    IncludeSubdirectories = includeSubdirs,
                    ProcessingDelayMs = processingDelay * 1000,
                    FileExtensions = extensions
                };

                result.WatchConfigurations.Add(watch);
            }

            _logger.LogInformation("CSV import complete: {Valid} valid, {Invalid} invalid",
                result.ValidCount, result.InvalidCount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import from CSV: {CsvPath}", csvPath);
            var result = new WatchBatchImport();
            result.ValidationErrors[0] = new List<string> { $"Failed to import: {ex.Message}" };
            return result;
        }
    }

    public async Task<WatchBatchImport> ImportFromJsonAsync(string jsonPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Importing watches from JSON: {JsonPath}", jsonPath);

            var result = new WatchBatchImport();

            if (!File.Exists(jsonPath))
            {
                result.ValidationErrors[0] = new List<string> { "File does not exist" };
                return result;
            }

            var json = await File.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false);
            var watches = JsonSerializer.Deserialize<List<CreateWatchRequest>>(json);

            if (watches == null || watches.Count == 0)
            {
                result.ValidationErrors[0] = new List<string> { "No watch configurations found in JSON file" };
                return result;
            }

            // Validate each watch
            for (int i = 0; i < watches.Count; i++)
            {
                var watch = watches[i];
                var errors = ValidateWatchRequest(watch);

                if (errors.Count > 0)
                {
                    result.ValidationErrors[i] = errors;
                }
                else
                {
                    result.WatchConfigurations.Add(watch);
                }
            }

            _logger.LogInformation("JSON import complete: {Valid} valid, {Invalid} invalid",
                result.ValidCount, result.InvalidCount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import from JSON: {JsonPath}", jsonPath);
            var result = new WatchBatchImport();
            result.ValidationErrors[0] = new List<string> { $"Failed to import: {ex.Message}" };
            return result;
        }
    }

    public async Task<string> ExportToJsonAsync(List<CreateWatchRequest> watches, string outputPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Exporting {Count} watches to JSON: {OutputPath}", watches.Count, outputPath);

            var json = JsonSerializer.Serialize(watches, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Successfully exported watches to {OutputPath}", outputPath);

            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export to JSON: {OutputPath}", outputPath);
            throw;
        }
    }

    public Task<List<WatchTemplate>> GetBuiltInTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_builtInTemplates.ToList());
    }

    public Task InitializeBuiltInTemplatesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Built-in templates are ready ({Count} templates)", _builtInTemplates.Count);
        return Task.CompletedTask;
    }

    // Helper methods

    private string ReplaceVariables(string pattern, Dictionary<string, string> variables)
    {
        var result = pattern;

        foreach (var variable in variables.OrderByDescending(v => v.Key.Length))
        {
            result = result.Replace($"{{{variable.Key}}}", variable.Value);
        }

        return result;
    }

    private string[] ParseCsvLine(string line)
    {
        var parts = new List<string>();
        var currentPart = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                parts.Add(currentPart.ToString().Trim('"'));
                currentPart.Clear();
            }
            else
            {
                currentPart.Append(c);
            }
        }

        parts.Add(currentPart.ToString().Trim('"'));

        return parts.ToArray();
    }

    private List<string> ValidateWatchRequest(CreateWatchRequest watch)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(watch.ChannelName))
        {
            errors.Add("Channel name is required");
        }

        if (string.IsNullOrWhiteSpace(watch.WatchPath))
        {
            errors.Add("Watch path is required");
        }
        else if (!IsValidWatchPath(watch.WatchPath))
        {
            errors.Add("Watch path contains invalid or dangerous characters");
        }

        if (watch.ProcessingDelayMs < 1000)
        {
            errors.Add("Processing delay must be at least 1000ms (1 second)");
        }

        if (string.IsNullOrWhiteSpace(watch.FileExtensions))
        {
            errors.Add("File extensions are required");
        }

        return errors;
    }

    /// <summary>
    /// Validates a watch path to prevent path traversal and command injection attacks.
    /// </summary>
    /// <param name="watchPath">The watch path to validate.</param>
    /// <returns>True if the path is valid and safe, false otherwise.</returns>
    private bool IsValidWatchPath(string watchPath)
    {
        try
        {
            // Check for dangerous characters that could be used for command injection
            var dangerousChars = new[] { ';', '|', '&', '$', '`', '<', '>', '\n', '\r' };
            if (watchPath.Any(c => dangerousChars.Contains(c)))
            {
                _logger.LogWarning("Dangerous characters detected in watch path: {WatchPath}", watchPath);
                return false;
            }

            // Check for excessive path traversal attempts
            var normalizedPath = watchPath.Replace('\\', '/');
            if (normalizedPath.Contains("../") || normalizedPath.Contains("/.."))
            {
                _logger.LogWarning("Path traversal detected in watch path: {WatchPath}", watchPath);
                return false;
            }

            // Try to get the full path - this will throw if the path is invalid
            var fullPath = Path.GetFullPath(watchPath);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid watch path: {WatchPath}", watchPath);
            return false;
        }
    }
}

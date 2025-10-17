using AlUmmahNow.Data.Models;
using System.Text.Json;

namespace AlUmmahNow.Data.Services;

/// <summary>
/// Interface for managing filter presets
/// </summary>
public interface IFilterPresetService
{
    /// <summary>
    /// Gets all saved filter presets
    /// </summary>
    Task<List<FilterPreset>> GetAllPresetsAsync();

    /// <summary>
    /// Gets a specific preset by ID
    /// </summary>
    Task<FilterPreset?> GetPresetByIdAsync(Guid id);

    /// <summary>
    /// Saves or updates a filter preset
    /// </summary>
    Task SavePresetAsync(FilterPreset preset);

    /// <summary>
    /// Deletes a filter preset by ID
    /// </summary>
    Task DeletePresetAsync(Guid id);

    /// <summary>
    /// Gets the default preset (if one is set)
    /// </summary>
    Task<FilterPreset?> GetDefaultPresetAsync();

    /// <summary>
    /// Sets a preset as the default
    /// </summary>
    Task SetDefaultPresetAsync(Guid id);

    /// <summary>
    /// Clears the default preset
    /// </summary>
    Task ClearDefaultPresetAsync();

    /// <summary>
    /// Initializes built-in presets if they don't exist
    /// </summary>
    Task InitializeBuiltInPresetsAsync();
}

/// <summary>
/// Service for managing filter presets with JSON file storage
/// </summary>
public class FilterPresetService : IFilterPresetService
{
    private readonly string _presetsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private List<FilterPreset>? _cachedPresets;

    public FilterPresetService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AlUmmahNow", "VisualImporterUno");

        Directory.CreateDirectory(appDataPath);
        _presetsFilePath = Path.Combine(appDataPath, "filter-presets.json");
    }

    /// <summary>
    /// Constructor with custom file path (for testing)
    /// </summary>
    public FilterPresetService(string presetsFilePath)
    {
        _presetsFilePath = presetsFilePath;
        var directory = Path.GetDirectoryName(presetsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<List<FilterPreset>> GetAllPresetsAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cachedPresets != null)
            {
                return _cachedPresets.OrderBy(p => p.DisplayOrder)
                    .ThenBy(p => p.Name)
                    .ToList();
            }

            if (!File.Exists(_presetsFilePath))
            {
                await InitializeBuiltInPresetsAsync().ConfigureAwait(false);
                return _cachedPresets?.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name).ToList()
                    ?? new List<FilterPreset>();
            }

            var json = await File.ReadAllTextAsync(_presetsFilePath).ConfigureAwait(false);
            _cachedPresets = JsonSerializer.Deserialize<List<FilterPreset>>(json) ?? new List<FilterPreset>();

            return _cachedPresets.OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.Name)
                .ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<FilterPreset?> GetPresetByIdAsync(Guid id)
    {
        var presets = await GetAllPresetsAsync().ConfigureAwait(false);
        return presets.FirstOrDefault(p => p.Id == id);
    }

    public async Task SavePresetAsync(FilterPreset preset)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var presets = await GetAllPresetsAsync().ConfigureAwait(false);
            var existingIndex = presets.FindIndex(p => p.Id == preset.Id);

            preset.UpdatedAt = DateTime.UtcNow;

            if (existingIndex >= 0)
            {
                // Update existing preset
                presets[existingIndex] = preset;
            }
            else
            {
                // Add new preset
                preset.CreatedAt = DateTime.UtcNow;
                presets.Add(preset);
            }

            // If this preset is being set as default, clear other defaults
            if (preset.IsDefault)
            {
                foreach (var p in presets.Where(p => p.Id != preset.Id))
                {
                    p.IsDefault = false;
                }
            }

            await SavePresetsToFileAsync(presets).ConfigureAwait(false);
            _cachedPresets = presets;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task DeletePresetAsync(Guid id)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var presets = await GetAllPresetsAsync().ConfigureAwait(false);
            var preset = presets.FirstOrDefault(p => p.Id == id);

            if (preset == null)
            {
                return;
            }

            // Don't allow deletion of built-in presets
            if (preset.IsBuiltIn)
            {
                throw new InvalidOperationException("Cannot delete built-in presets");
            }

            presets.RemoveAll(p => p.Id == id);
            await SavePresetsToFileAsync(presets).ConfigureAwait(false);
            _cachedPresets = presets;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<FilterPreset?> GetDefaultPresetAsync()
    {
        var presets = await GetAllPresetsAsync().ConfigureAwait(false);
        return presets.FirstOrDefault(p => p.IsDefault);
    }

    public async Task SetDefaultPresetAsync(Guid id)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var presets = await GetAllPresetsAsync().ConfigureAwait(false);
            var targetPreset = presets.FirstOrDefault(p => p.Id == id);

            if (targetPreset == null)
            {
                throw new InvalidOperationException("Preset not found");
            }

            // Clear all existing defaults
            foreach (var preset in presets)
            {
                preset.IsDefault = preset.Id == id;
                if (preset.Id == id)
                {
                    preset.UpdatedAt = DateTime.UtcNow;
                }
            }

            await SavePresetsToFileAsync(presets).ConfigureAwait(false);
            _cachedPresets = presets;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task ClearDefaultPresetAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var presets = await GetAllPresetsAsync().ConfigureAwait(false);
            foreach (var preset in presets)
            {
                preset.IsDefault = false;
            }

            await SavePresetsToFileAsync(presets).ConfigureAwait(false);
            _cachedPresets = presets;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task InitializeBuiltInPresetsAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var presets = new List<FilterPreset>();

            // Check if built-in presets already exist
            if (File.Exists(_presetsFilePath))
            {
                var json = await File.ReadAllTextAsync(_presetsFilePath).ConfigureAwait(false);
                presets = JsonSerializer.Deserialize<List<FilterPreset>>(json) ?? new List<FilterPreset>();
            }

            // Only add built-in presets if they don't already exist
            var builtInPresets = CreateBuiltInPresets();
            foreach (var builtInPreset in builtInPresets)
            {
                if (!presets.Any(p => p.Name == builtInPreset.Name && p.IsBuiltIn))
                {
                    presets.Add(builtInPreset);
                }
            }

            await SavePresetsToFileAsync(presets).ConfigureAwait(false);
            _cachedPresets = presets;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task SavePresetsToFileAsync(List<FilterPreset> presets)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(presets, options);
        await File.WriteAllTextAsync(_presetsFilePath, json).ConfigureAwait(false);
    }

    private static List<FilterPreset> CreateBuiltInPresets()
    {
        var now = DateTime.UtcNow;

        return new List<FilterPreset>
        {
            new FilterPreset
            {
                Id = Guid.NewGuid(),
                Name = "Last 24 Hours",
                Description = "Shows files detected in the last 24 hours",
                DateFromFilter = DateTime.UtcNow.AddDays(-1),
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                DisplayOrder = 1
            },
            new FilterPreset
            {
                Id = Guid.NewGuid(),
                Name = "High Priority Only",
                Description = "Shows only high priority files (priority 1-2)",
                PriorityFilter = 2,
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                DisplayOrder = 2
            },
            new FilterPreset
            {
                Id = Guid.NewGuid(),
                Name = "Large Files",
                Description = "Shows files larger than 1GB",
                MinSizeFilter = 1024L * 1024L * 1024L, // 1GB in bytes
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                DisplayOrder = 3
            },
            new FilterPreset
            {
                Id = Guid.NewGuid(),
                Name = "Recent & High Priority",
                Description = "Shows high priority files from the last 24 hours",
                DateFromFilter = DateTime.UtcNow.AddDays(-1),
                PriorityFilter = 2,
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                DisplayOrder = 4
            },
            new FilterPreset
            {
                Id = Guid.NewGuid(),
                Name = "Last 7 Days",
                Description = "Shows files detected in the last week",
                DateFromFilter = DateTime.UtcNow.AddDays(-7),
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                DisplayOrder = 5
            },
            new FilterPreset
            {
                Id = Guid.NewGuid(),
                Name = "MP4 Files Only",
                Description = "Shows only MP4 video files",
                ExtensionFilter = "mp4",
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                DisplayOrder = 6
            }
        };
    }
}

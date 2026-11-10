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

    // Locking: every public method takes _fileLock exactly once and does its work through the *Locked helpers
    // below, which assume the lock is held and never take it. SemaphoreSlim is not re-entrant, so a public method
    // calling another public method while holding the lock would wait on itself forever (TOOLS-9).

    public async Task<List<FilterPreset>> GetAllPresetsAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return Sorted(await LoadPresetsLockedAsync().ConfigureAwait(false));
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
            var presets = await LoadPresetsLockedAsync().ConfigureAwait(false);
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

            await StorePresetsLockedAsync(presets).ConfigureAwait(false);
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
            var presets = await LoadPresetsLockedAsync().ConfigureAwait(false);
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
            await StorePresetsLockedAsync(presets).ConfigureAwait(false);
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
            var presets = await LoadPresetsLockedAsync().ConfigureAwait(false);
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

            await StorePresetsLockedAsync(presets).ConfigureAwait(false);
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
            var presets = await LoadPresetsLockedAsync().ConfigureAwait(false);
            foreach (var preset in presets)
            {
                preset.IsDefault = false;
            }

            await StorePresetsLockedAsync(presets).ConfigureAwait(false);
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
            await InitializeBuiltInPresetsLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// The presets as a private copy the caller may change freely (the cache only changes when a store succeeds).
    /// Creates the file with the built-in presets on first use. Caller holds <see cref="_fileLock"/>.
    /// </summary>
    private async Task<List<FilterPreset>> LoadPresetsLockedAsync()
    {
        if (_cachedPresets == null)
        {
            if (File.Exists(_presetsFilePath))
            {
                var json = await File.ReadAllTextAsync(_presetsFilePath).ConfigureAwait(false);
                _cachedPresets = JsonSerializer.Deserialize<List<FilterPreset>>(json) ?? new List<FilterPreset>();
            }
            else
            {
                await InitializeBuiltInPresetsLockedAsync().ConfigureAwait(false);
            }
        }

        return Clone(_cachedPresets!);
    }

    /// <summary>Adds any missing built-in presets to the file. Caller holds <see cref="_fileLock"/>.</summary>
    private async Task InitializeBuiltInPresetsLockedAsync()
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

        await StorePresetsLockedAsync(presets).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the presets to a temporary file next to the real one and renames it over the real one, so a reader
    /// (this app or another instance) sees either the old file or the new one, never a partly written file.
    /// Updates the cache once the file is in place. Caller holds <see cref="_fileLock"/>.
    /// </summary>
    private async Task StorePresetsLockedAsync(List<FilterPreset> presets)
    {
        var json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = $"{_presetsFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, _presetsFilePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch (IOException) { /* best effort: the write already failed */ }
            throw;
        }

        _cachedPresets = Clone(presets);
    }

    private static List<FilterPreset> Clone(List<FilterPreset> presets) =>
        JsonSerializer.Deserialize<List<FilterPreset>>(JsonSerializer.Serialize(presets)) ?? new List<FilterPreset>();

    private static List<FilterPreset> Sorted(List<FilterPreset> presets) =>
        presets.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name).ToList();

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

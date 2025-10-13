using System.ComponentModel.DataAnnotations;

namespace AlUmmahNow.Data.Models;

/// <summary>
/// Represents a saved filter preset for the approval workflow
/// </summary>
public class FilterPreset
{
    /// <summary>
    /// Unique identifier for this preset
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name for the preset
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this preset filters
    /// </summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Channel filter value
    /// </summary>
    [MaxLength(500)]
    public string? ChannelFilter { get; set; }

    /// <summary>
    /// Priority filter value (1-5)
    /// </summary>
    public int? PriorityFilter { get; set; }

    /// <summary>
    /// Minimum file size filter in bytes
    /// </summary>
    public long? MinSizeFilter { get; set; }

    /// <summary>
    /// Maximum file size filter in bytes
    /// </summary>
    public long? MaxSizeFilter { get; set; }

    /// <summary>
    /// Date from filter
    /// </summary>
    public DateTime? DateFromFilter { get; set; }

    /// <summary>
    /// Date to filter
    /// </summary>
    public DateTime? DateToFilter { get; set; }

    /// <summary>
    /// File extension filter (comma-separated)
    /// </summary>
    [MaxLength(500)]
    public string? ExtensionFilter { get; set; }

    /// <summary>
    /// When this preset was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this preset was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this preset is the default to apply on load
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Whether this is a built-in preset (cannot be deleted)
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// User who created this preset
    /// </summary>
    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Display order for sorting presets
    /// </summary>
    public int DisplayOrder { get; set; } = 0;

    /// <summary>
    /// Checks if this preset has any active filters
    /// </summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(ChannelFilter) ||
        PriorityFilter.HasValue ||
        MinSizeFilter.HasValue ||
        MaxSizeFilter.HasValue ||
        DateFromFilter.HasValue ||
        DateToFilter.HasValue ||
        !string.IsNullOrWhiteSpace(ExtensionFilter);

    /// <summary>
    /// Gets a summary of the active filters
    /// </summary>
    public string GetFilterSummary()
    {
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(ChannelFilter))
            filters.Add($"Channel: {ChannelFilter}");

        if (PriorityFilter.HasValue)
            filters.Add($"Priority: {PriorityFilter}");

        if (MinSizeFilter.HasValue)
            filters.Add($"Min Size: {FormatFileSize(MinSizeFilter.Value)}");

        if (MaxSizeFilter.HasValue)
            filters.Add($"Max Size: {FormatFileSize(MaxSizeFilter.Value)}");

        if (DateFromFilter.HasValue)
            filters.Add($"From: {DateFromFilter.Value:yyyy-MM-dd}");

        if (DateToFilter.HasValue)
            filters.Add($"To: {DateToFilter.Value:yyyy-MM-dd}");

        if (!string.IsNullOrWhiteSpace(ExtensionFilter))
            filters.Add($"Extensions: {ExtensionFilter}");

        return filters.Count > 0 ? string.Join(", ", filters) : "No filters";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

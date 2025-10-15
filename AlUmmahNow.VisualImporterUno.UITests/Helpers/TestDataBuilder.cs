namespace AlUmmahNow.VisualImporterUno.UITests.Helpers;

/// <summary>
/// Builder class for creating test data with fluent interface.
/// Provides consistent test data generation for various test scenarios.
/// </summary>
public class TestDataBuilder
{
    private static readonly Random _random = new Random();

    /// <summary>
    /// Creates a builder for Watch test data
    /// </summary>
    public static WatchDataBuilder Watch() => new WatchDataBuilder();

    /// <summary>
    /// Creates a builder for Approval test data
    /// </summary>
    public static ApprovalDataBuilder Approval() => new ApprovalDataBuilder();

    /// <summary>
    /// Creates a builder for Filter Preset test data
    /// </summary>
    public static FilterPresetDataBuilder FilterPreset() => new FilterPresetDataBuilder();

    /// <summary>
    /// Generates a random channel name
    /// </summary>
    public static string RandomChannelName()
    {
        var prefixes = new[] { "Islamic", "Quran", "Hadith", "Lecture", "Tafsir", "Fiqh" };
        var suffixes = new[] { "Channel", "TV", "Media", "Network", "Studio", "Productions" };

        return $"{prefixes[_random.Next(prefixes.Length)]} {suffixes[_random.Next(suffixes.Length)]}";
    }

    /// <summary>
    /// Generates a random file path
    /// </summary>
    public static string RandomFilePath()
    {
        var drives = OperatingSystem.IsWindows() ? new[] { "C:", "D:", "E:" } : new[] { "/home", "/media", "/mnt" };
        var folders = new[] { "Videos", "Media", "Import", "Watch", "Content" };

        var drive = drives[_random.Next(drives.Length)];
        var folder = folders[_random.Next(folders.Length)];

        return OperatingSystem.IsWindows()
            ? $"{drive}\\{folder}\\Test_{Guid.NewGuid():N}"
            : $"{drive}/{folder}/Test_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Generates a random file extension
    /// </summary>
    public static string RandomFileExtension()
    {
        var extensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };
        return extensions[_random.Next(extensions.Length)];
    }

    /// <summary>
    /// Generates a unique test identifier
    /// </summary>
    public static string UniqueTestId()
    {
        return $"Test_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}".Substring(0, 30);
    }
}

/// <summary>
/// Builder for Watch test data
/// </summary>
public class WatchDataBuilder
{
    private string _channelName = TestDataBuilder.RandomChannelName();
    private string _watchPath = TestDataBuilder.RandomFilePath();
    private int _processingDelay = 5;
    private bool _includeSubdirectories = true;
    private string _extensions = ".mp4,.mkv,.avi";

    public WatchDataBuilder WithChannelName(string channelName)
    {
        _channelName = channelName;
        return this;
    }

    public WatchDataBuilder WithWatchPath(string watchPath)
    {
        _watchPath = watchPath;
        return this;
    }

    public WatchDataBuilder WithProcessingDelay(int seconds)
    {
        _processingDelay = seconds;
        return this;
    }

    public WatchDataBuilder WithIncludeSubdirectories(bool include)
    {
        _includeSubdirectories = include;
        return this;
    }

    public WatchDataBuilder WithExtensions(string extensions)
    {
        _extensions = extensions;
        return this;
    }

    /// <summary>
    /// Creates a minimal valid watch (channel name and path only)
    /// </summary>
    public WatchDataBuilder Minimal()
    {
        _extensions = ".mp4";
        _processingDelay = 1;
        _includeSubdirectories = false;
        return this;
    }

    /// <summary>
    /// Creates a watch with all fields populated
    /// </summary>
    public WatchDataBuilder Complete()
    {
        _extensions = ".mp4,.mkv,.avi,.mov,.wmv";
        _processingDelay = 10;
        _includeSubdirectories = true;
        return this;
    }

    public WatchData Build()
    {
        return new WatchData
        {
            ChannelName = _channelName,
            WatchPath = _watchPath,
            ProcessingDelay = _processingDelay,
            IncludeSubdirectories = _includeSubdirectories,
            Extensions = _extensions
        };
    }
}

/// <summary>
/// Builder for Approval test data
/// </summary>
public class ApprovalDataBuilder
{
    private string _fileName = $"test_video_{Guid.NewGuid():N}.mp4";
    private string _channelName = TestDataBuilder.RandomChannelName();
    private long _fileSize = 1024 * 1024 * 100; // 100 MB
    private int _priority = 3;
    private string _extension = ".mp4";

    public ApprovalDataBuilder WithFileName(string fileName)
    {
        _fileName = fileName;
        return this;
    }

    public ApprovalDataBuilder WithChannelName(string channelName)
    {
        _channelName = channelName;
        return this;
    }

    public ApprovalDataBuilder WithFileSize(long bytes)
    {
        _fileSize = bytes;
        return this;
    }

    public ApprovalDataBuilder WithPriority(int priority)
    {
        _priority = priority;
        return this;
    }

    public ApprovalDataBuilder WithExtension(string extension)
    {
        _extension = extension;
        return this;
    }

    /// <summary>
    /// Creates a small file (< 10 MB)
    /// </summary>
    public ApprovalDataBuilder SmallFile()
    {
        _fileSize = 1024 * 1024 * 5; // 5 MB
        return this;
    }

    /// <summary>
    /// Creates a large file (> 1 GB)
    /// </summary>
    public ApprovalDataBuilder LargeFile()
    {
        _fileSize = 1024L * 1024L * 1024L * 2; // 2 GB
        return this;
    }

    /// <summary>
    /// Creates a high priority file
    /// </summary>
    public ApprovalDataBuilder HighPriority()
    {
        _priority = 5;
        return this;
    }

    /// <summary>
    /// Creates a low priority file
    /// </summary>
    public ApprovalDataBuilder LowPriority()
    {
        _priority = 1;
        return this;
    }

    public ApprovalData Build()
    {
        return new ApprovalData
        {
            FileName = _fileName,
            ChannelName = _channelName,
            FileSize = _fileSize,
            Priority = _priority,
            Extension = _extension
        };
    }
}

/// <summary>
/// Builder for Filter Preset test data
/// </summary>
public class FilterPresetDataBuilder
{
    private string _name = $"Test Preset {DateTime.Now:yyyyMMdd_HHmmss}";
    private string _description = "Test filter preset";
    private string? _channelFilter;
    private int? _priorityFilter;
    private long? _minSizeFilter;
    private long? _maxSizeFilter;
    private string? _extensionFilter;

    public FilterPresetDataBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public FilterPresetDataBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public FilterPresetDataBuilder WithChannelFilter(string channel)
    {
        _channelFilter = channel;
        return this;
    }

    public FilterPresetDataBuilder WithPriorityFilter(int priority)
    {
        _priorityFilter = priority;
        return this;
    }

    public FilterPresetDataBuilder WithSizeFilter(long? minSize, long? maxSize)
    {
        _minSizeFilter = minSize;
        _maxSizeFilter = maxSize;
        return this;
    }

    public FilterPresetDataBuilder WithExtensionFilter(string extensions)
    {
        _extensionFilter = extensions;
        return this;
    }

    /// <summary>
    /// Creates a preset with only channel filter
    /// </summary>
    public FilterPresetDataBuilder ChannelOnly(string channel)
    {
        _channelFilter = channel;
        _priorityFilter = null;
        _minSizeFilter = null;
        _maxSizeFilter = null;
        _extensionFilter = null;
        return this;
    }

    /// <summary>
    /// Creates a preset with all filters
    /// </summary>
    public FilterPresetDataBuilder AllFilters()
    {
        _channelFilter = TestDataBuilder.RandomChannelName();
        _priorityFilter = 5;
        _minSizeFilter = 1024 * 1024 * 10; // 10 MB
        _maxSizeFilter = 1024 * 1024 * 500; // 500 MB
        _extensionFilter = ".mp4,.mkv";
        return this;
    }

    public FilterPresetData Build()
    {
        return new FilterPresetData
        {
            Name = _name,
            Description = _description,
            ChannelFilter = _channelFilter,
            PriorityFilter = _priorityFilter,
            MinSizeFilter = _minSizeFilter,
            MaxSizeFilter = _maxSizeFilter,
            ExtensionFilter = _extensionFilter
        };
    }
}

#region Data Models

public class WatchData
{
    public string ChannelName { get; set; } = string.Empty;
    public string WatchPath { get; set; } = string.Empty;
    public int ProcessingDelay { get; set; }
    public bool IncludeSubdirectories { get; set; }
    public string Extensions { get; set; } = string.Empty;
}

public class ApprovalData
{
    public string FileName { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Priority { get; set; }
    public string Extension { get; set; } = string.Empty;
}

public class FilterPresetData
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ChannelFilter { get; set; }
    public int? PriorityFilter { get; set; }
    public long? MinSizeFilter { get; set; }
    public long? MaxSizeFilter { get; set; }
    public string? ExtensionFilter { get; set; }
}

#endregion

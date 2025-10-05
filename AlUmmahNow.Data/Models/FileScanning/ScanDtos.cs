namespace AlUmmahNow.Data.Models.FileScanning;

/// <summary>
/// DTO for scan status information
/// </summary>
public class ScanStatusDto
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public int FilesScanned { get; set; }
    public long TotalSize { get; set; }
    public double ProgressPercentage { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
}

/// <summary>
/// DTO for scan session summary
/// </summary>
public class ScanSessionDto
{
    public Guid Id { get; set; }
    public string RootPath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int FilesScanned { get; set; }
}

/// <summary>
/// DTO for scan progress data
/// </summary>
public class ScanProgressData
{
    public int FilesScanned { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
}

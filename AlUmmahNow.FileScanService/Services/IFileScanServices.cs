using AlUmmahNow.Data.Models.FileScanning;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Interface for the main file scanning engine
    /// </summary>
    public interface IFileScanEngine
    {
        event EventHandler<ScanProgressEventArgs>? ProgressChanged;
        Task ProcessScanAsync(ScanSession scanSession, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Event arguments for scan progress updates
    /// </summary>
    public class ScanProgressEventArgs : EventArgs
    {
        public Guid SessionId { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
        public string? CurrentFile { get; set; }
        public double ProgressPercentage { get; set; }
        public int FilesFound { get; set; }
        public int FoldersProcessed { get; set; }
        public int TotalFolders { get; set; }
    }

    /// <summary>
    /// Interface for calculating file hashes
    /// </summary>
    public interface IHashCalculator
    {
        Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken = default);
        Task<string> CalculateContentHashAsync(string content, CancellationToken cancellationToken = default);
        string CalculatePathHash(string path);
    }

    /// <summary>
    /// Interface for extracting metadata from video files and info files
    /// </summary>
    public interface IMetadataExtractor
    {
        Task<VideoMetadata> ExtractVideoMetadataAsync(string videoPath, string? infoFilePath = null, CancellationToken cancellationToken = default);
        Task<string?> ReadInfoFileAsync(string infoFilePath, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for WebSocket communication management
    /// </summary>
    public interface IWebSocketManager
    {
        Task BroadcastScanStatusUpdate(ScanSession scanSession);
        Task BroadcastScanProgress(ScanSession scanSession, string operation, double progress);
        Task BroadcastChannelUpdate(ScannedChannel channel);
        Task BroadcastFileUpdate(ScannedVideoFile file);
    }

    /// <summary>
    /// Video metadata extracted from files
    /// </summary>
    public class VideoMetadata
    {
        public double? DurationSeconds { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public long? Bitrate { get; set; }
        public string? Codec { get; set; }
        public string? InfoFileContent { get; set; }
        public string? InfoFileHash { get; set; }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.Http.Json;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.VisualImporterUno.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.VisualImporterUno.Services
{
    /// <summary>
    /// Service for managing database-based file scanning operations
    /// Replaces the direct file scanning with database and service communication
    /// </summary>
    public class DatabaseFileScanService : IDisposable
    {
        private readonly ImportDbContext _dbContext;
        private readonly ILogger<DatabaseFileScanService> _logger;
        private HubConnection? _hubConnection;
        private readonly string _apiBaseUrl;
        private readonly System.Net.Http.HttpClient _httpClient;
        private bool _disposed = false;

        public event EventHandler<ScanProgressEventArgs>? ScanProgressChanged;
        public event EventHandler<ScanCompletedEventArgs>? ScanCompleted;
        public event EventHandler<ChannelUpdatedEventArgs>? ChannelUpdated;

        public DatabaseFileScanService(ImportDbContext dbContext, ILogger<DatabaseFileScanService> logger, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _logger = logger;
            _apiBaseUrl = configuration.GetValue<string>("FileScanApi:BaseUrl") ?? "http://localhost:5001";
            _httpClient = new System.Net.Http.HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
        }

        /// <summary>
        /// Initialize WebSocket connection for real-time updates
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl($"{_apiBaseUrl}/filescanhub")
                    .WithAutomaticReconnect()
                    .Build();

                // Register event handlers
                _hubConnection.On<object>("ScanStatusUpdate", OnScanStatusUpdate);
                _hubConnection.On<object>("ScanProgress", OnScanProgress);
                _hubConnection.On<object>("ChannelUpdate", OnChannelUpdate);

                await _hubConnection.StartAsync();
                _logger.LogInformation("WebSocket connection established");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize WebSocket connection");
            }
        }

        /// <summary>
        /// Start a new scan session (replaces ScanDirectoryAsync)
        /// </summary>
        public async Task<Guid> StartScanAsync(string folderPath, string? name = null)
        {
            try
            {
                var request = new { RootPath = folderPath, Name = name };
                var response = await _httpClient.PostAsJsonAsync("/api/scan/start", request);
                response.EnsureSuccessStatusCode();

                var scanSession = await response.Content.ReadFromJsonAsync<ScanSession>();
                if (scanSession == null)
                {
                    throw new InvalidOperationException("Failed to create scan session");
                }

                // Join the scan group for real-time updates
                if (_hubConnection != null)
                {
                    await _hubConnection.InvokeAsync("JoinScanGroup", scanSession.Id.ToString());
                }

                _logger.LogInformation("Started scan session {ScanId} for path {Path}", scanSession.Id, folderPath);
                return scanSession.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting scan for path {Path}", folderPath);
                throw;
            }
        }

        /// <summary>
        /// Get all available scan sessions
        /// </summary>
        public async Task<List<ScanSession>> GetScanSessionsAsync()
        {
            try
            {
                return await _dbContext.ScanSessions
                    .OrderByDescending(s => s.CreatedAt)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scan sessions");
                throw;
            }
        }

        /// <summary>
        /// Get scan session by ID
        /// </summary>
        public async Task<ScanSession?> GetScanSessionAsync(Guid scanSessionId)
        {
            try
            {
                return await _dbContext.ScanSessions.FindAsync(scanSessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scan session {ScanId}", scanSessionId);
                throw;
            }
        }

        /// <summary>
        /// Get channels for a scan session
        /// </summary>
        public async Task<List<ScannedChannel>> GetChannelsAsync(Guid scanSessionId)
        {
            try
            {
                return await _dbContext.ScannedChannels
                    .Where(c => c.ScanSessionId == scanSessionId)
                    .OrderBy(c => c.Name)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving channels for scan session {ScanId}", scanSessionId);
                throw;
            }
        }

        /// <summary>
        /// Get video files for a channel (with pagination)
        /// </summary>
        public async Task<(List<ScannedVideoFile> Files, int TotalCount)> GetChannelFilesAsync(
            Guid scanSessionId, 
            string channelName, 
            int page = 1, 
            int pageSize = 100, 
            bool includeDuplicates = false,
            bool onlySelected = false)
        {
            try
            {
                var query = _dbContext.ScannedVideoFiles
                    .Where(f => f.ScanSessionId == scanSessionId && f.ChannelName == channelName && f.FileExists);

                if (!includeDuplicates)
                {
                    query = query.Where(f => !f.IsDuplicate);
                }

                if (onlySelected)
                {
                    query = query.Where(f => f.ImportStatus == FileImportStatus.Selected);
                }

                var totalCount = await query.CountAsync();
                
                var files = await query
                    .OrderBy(f => f.FileName)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return (files, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving files for channel {ChannelName} in scan {ScanId}", channelName, scanSessionId);
                throw;
            }
        }

        /// <summary>
        /// Convert ScannedVideoFile to VideoFileInfo for compatibility
        /// </summary>
        public VideoFileInfo ConvertToVideoFileInfo(ScannedVideoFile scannedFile)
        {
            return new VideoFileInfo
            {
                FileName = scannedFile.FileName,
                FilePath = scannedFile.FilePath,
                FileSize = scannedFile.FileSize,
                CreatedDate = scannedFile.CreatedDate,
                ModifiedDate = scannedFile.ModifiedDate,
                Directory = scannedFile.Directory,
                RelativePath = scannedFile.RelativePath,
                Extension = scannedFile.Extension,
                ThumbnailPath = scannedFile.ThumbnailPath,
                HasThumbnail = !string.IsNullOrEmpty(scannedFile.ThumbnailPath),
                IsDuplicate = scannedFile.IsDuplicate,
                DuplicateOf = scannedFile.OriginalFile?.FilePath,
                ImportStatus = ConvertImportStatus(scannedFile.ImportStatus),
                ChannelNameOverride = scannedFile.ChannelName,
                IsSelected = scannedFile.ImportStatus == FileImportStatus.Selected || scannedFile.ImportStatus == FileImportStatus.NotImported
            };
        }

        /// <summary>
        /// Get file statistics for a scan session
        /// </summary>
        public async Task<FileStatistics> GetFileStatisticsAsync(Guid scanSessionId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/scan/sessions/{scanSessionId}/statistics");
                response.EnsureSuccessStatusCode();

                var apiStats = await response.Content.ReadFromJsonAsync<ScanStatistics>();
                if (apiStats == null)
                {
                    throw new InvalidOperationException("Failed to retrieve statistics");
                }

                return new FileStatistics
                {
                    TotalFiles = apiStats.TotalFiles,
                    TotalSize = apiStats.TotalSize,
                    UniqueFiles = apiStats.UniqueFiles,
                    UniqueSize = apiStats.UniqueSize,
                    DuplicateFiles = apiStats.DuplicateFiles,
                    DuplicateSize = apiStats.DuplicateSize,
                    ReadyFiles = apiStats.ReadyForImport,
                    UploadedFiles = apiStats.ImportedFiles,
                    FailedFiles = apiStats.FailedFiles,
                    AverageFileSize = apiStats.TotalFiles > 0 ? (double)apiStats.TotalSize / apiStats.TotalFiles : 0,
                    LargestFile = 0, // Would need additional API call to get this
                    SmallestFile = 0 // Would need additional API call to get this
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving statistics for scan session {ScanId}", scanSessionId);
                throw;
            }
        }

        /// <summary>
        /// Update file selection status
        /// </summary>
        public async Task UpdateFileSelectionAsync(Guid fileId, bool isSelected)
        {
            try
            {
                var file = await _dbContext.ScannedVideoFiles.FindAsync(fileId);
                if (file != null)
                {
                    file.ImportStatus = isSelected ? FileImportStatus.Selected : FileImportStatus.NotImported;
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating file selection for file {FileId}", fileId);
                throw;
            }
        }

        /// <summary>
        /// Cancel a scan session
        /// </summary>
        public async Task<bool> CancelScanAsync(Guid scanSessionId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"/api/scan/sessions/{scanSessionId}/cancel", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling scan session {ScanId}", scanSessionId);
                return false;
            }
        }

        private void OnScanStatusUpdate(object statusData)
        {
            try
            {
                var json = JsonSerializer.Serialize(statusData);
                var status = JsonSerializer.Deserialize<ScanStatusUpdate>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (status != null)
                {
                    ScanProgressChanged?.Invoke(this, new ScanProgressEventArgs
                    {
                        ScanSessionId = status.ScanSessionId,
                        Status = status.Status,
                        Progress = status.Progress,
                        Operation = status.Operation ?? string.Empty,
                        FilesFound = status.FilesFound,
                        DirectoriesProcessed = status.DirectoriesProcessed,
                        TotalDirectories = status.TotalDirectories
                    });

                    if (status.Status == "Completed" || status.Status == "Failed" || status.Status == "Cancelled")
                    {
                        ScanCompleted?.Invoke(this, new ScanCompletedEventArgs
                        {
                            ScanSessionId = status.ScanSessionId,
                            IsSuccess = status.Status == "Completed",
                            ErrorMessage = status.ErrorMessage,
                            FilesFound = status.FilesFound,
                            TotalSize = status.TotalSize
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scan status update");
            }
        }

        private void OnScanProgress(object progressData)
        {
            try
            {
                var json = JsonSerializer.Serialize(progressData);
                var progress = JsonSerializer.Deserialize<ScanProgressUpdate>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (progress != null)
                {
                    ScanProgressChanged?.Invoke(this, new ScanProgressEventArgs
                    {
                        ScanSessionId = progress.ScanSessionId,
                        Progress = progress.Progress,
                        Operation = progress.Operation ?? string.Empty,
                        FilesFound = progress.FilesFound,
                        DirectoriesProcessed = progress.DirectoriesProcessed,
                        TotalDirectories = progress.TotalDirectories
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scan progress update");
            }
        }

        private void OnChannelUpdate(object channelData)
        {
            try
            {
                var json = JsonSerializer.Serialize(channelData);
                var channel = JsonSerializer.Deserialize<ChannelUpdate>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (channel != null)
                {
                    ChannelUpdated?.Invoke(this, new ChannelUpdatedEventArgs
                    {
                        ScanSessionId = channel.ScanSessionId,
                        ChannelId = channel.ChannelId,
                        Name = channel.Name,
                        TotalFiles = channel.TotalFiles,
                        TotalSize = channel.TotalSize,
                        UniqueFiles = channel.UniqueFiles,
                        ReadyForImport = channel.ReadyForImport
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing channel update");
            }
        }

        private static ImportFileStatus ConvertImportStatus(FileImportStatus status)
        {
            return status switch
            {
                FileImportStatus.NotImported => ImportFileStatus.NotImported,
                FileImportStatus.Selected => ImportFileStatus.NotImported, // Treat selected as not imported for UI
                FileImportStatus.Pending => ImportFileStatus.Pending,
                FileImportStatus.Uploading => ImportFileStatus.Uploading,
                FileImportStatus.Uploaded => ImportFileStatus.Uploaded,
                FileImportStatus.Failed => ImportFileStatus.Failed,
                FileImportStatus.Skipped => ImportFileStatus.Skipped,
                _ => ImportFileStatus.NotImported
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    _hubConnection?.DisposeAsync().AsTask().Wait(5000);
                    _httpClient?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing DatabaseFileScanService");
                }
                _disposed = true;
            }
        }
    }

    // Event argument classes
    public class ScanProgressEventArgs : EventArgs
    {
        public Guid ScanSessionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public double Progress { get; set; }
        public string Operation { get; set; } = string.Empty;
        public int FilesFound { get; set; }
        public int DirectoriesProcessed { get; set; }
        public int TotalDirectories { get; set; }
    }

    public class ScanCompletedEventArgs : EventArgs
    {
        public Guid ScanSessionId { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public int FilesFound { get; set; }
        public long TotalSize { get; set; }
    }

    public class ChannelUpdatedEventArgs : EventArgs
    {
        public Guid ScanSessionId { get; set; }
        public Guid ChannelId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }
        public int UniqueFiles { get; set; }
        public int ReadyForImport { get; set; }
    }

    // Data transfer objects for WebSocket communication
    internal class ScanStatusUpdate
    {
        public Guid ScanSessionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public double Progress { get; set; }
        public string? Operation { get; set; }
        public int FilesFound { get; set; }
        public int DirectoriesProcessed { get; set; }
        public int TotalDirectories { get; set; }
        public long TotalSize { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    internal class ScanProgressUpdate
    {
        public Guid ScanSessionId { get; set; }
        public string? Operation { get; set; }
        public double Progress { get; set; }
        public int FilesFound { get; set; }
        public int DirectoriesProcessed { get; set; }
        public int TotalDirectories { get; set; }
    }

    internal class ChannelUpdate
    {
        public Guid ScanSessionId { get; set; }
        public Guid ChannelId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }
        public int UniqueFiles { get; set; }
        public long UniqueSize { get; set; }
        public int DuplicateFiles { get; set; }
        public int ReadyForImport { get; set; }
        public int ImportedFiles { get; set; }
        public int FailedFiles { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    internal class ScanStatistics
    {
        public int TotalChannels { get; set; }
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }
        public int UniqueFiles { get; set; }
        public long UniqueSize { get; set; }
        public int DuplicateFiles { get; set; }
        public long DuplicateSize { get; set; }
        public int FilesWithThumbnails { get; set; }
        public int FilesWithInfo { get; set; }
        public int ReadyForImport { get; set; }
        public int ImportedFiles { get; set; }
        public int FailedFiles { get; set; }
        public double? TotalDurationHours { get; set; }
    }
}
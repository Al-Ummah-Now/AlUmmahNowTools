using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;
using AlUmmahNow.Data.Models.FileScanning;

namespace AlUmmahNow.DataImporter.Services;

/// <summary>
/// SignalR client for real-time communication with FileScanService
/// </summary>
public class FileScanSignalRClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly string _hubUrl;

    public event EventHandler<ScanProgressEventArgs>? ProgressReceived;
    public event EventHandler<Guid>? ScanCompleted;
    public event EventHandler<(Guid sessionId, string error)>? ScanFailed;
    public event EventHandler<Guid>? ScanStarted;
    public event EventHandler<Guid>? ScanCancelled;

    public FileScanSignalRClient(string serviceUrl)
    {
        _hubUrl = $"{serviceUrl.TrimEnd('/')}/scanHub";
        
        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        // Subscribe to server events
        _connection.On<Guid, string>("ScanStarted", (sessionId, path) =>
        {
            Console.WriteLine($"? Scan started for session {sessionId}");
            ScanStarted?.Invoke(this, sessionId);
        });

        _connection.On<Guid, object>("ScanProgress", (sessionId, progress) =>
        {
            var progressData = JsonSerializer.Deserialize<ScanProgressData>(
                JsonSerializer.Serialize(progress));
            
            if (progressData != null)
            {
                ProgressReceived?.Invoke(this, new ScanProgressEventArgs
                {
                    SessionId = sessionId,
                    FilesScanned = progressData.FilesScanned,
                    CurrentFile = progressData.CurrentFile ?? "",
                    ProgressPercentage = progressData.ProgressPercentage,
                    CurrentOperation = progressData.CurrentOperation
                });
            }
        });

        _connection.On<Guid, int>("ScanCompleted", (sessionId, fileCount) =>
        {
            Console.WriteLine($"? Scan completed: {sessionId} ({fileCount} files found)");
            ScanCompleted?.Invoke(this, sessionId);
        });

        _connection.On<Guid, string>("ScanFailed", (sessionId, error) =>
        {
            Console.WriteLine($"? Scan failed: {sessionId} - {error}");
            ScanFailed?.Invoke(this, (sessionId, error));
        });

        _connection.On<Guid>("ScanCancelled", (sessionId) =>
        {
            Console.WriteLine($"? Scan cancelled: {sessionId}");
            ScanCancelled?.Invoke(this, sessionId);
        });

        // Handle reconnection
        _connection.Reconnecting += error =>
        {
            Console.WriteLine($"? Connection lost. Reconnecting...");
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            Console.WriteLine($"? Reconnected to FileScan service");
            return Task.CompletedTask;
        };

        _connection.Closed += error =>
        {
            if (error != null)
            {
                Console.WriteLine($"? Connection closed with error: {error.Message}");
            }
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Connect to the SignalR hub
    /// </summary>
    public async Task ConnectAsync()
    {
        try
        {
            await _connection.StartAsync();
            Console.WriteLine($"? Connected to FileScan service at {_hubUrl}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to connect to FileScan service at {_hubUrl}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Check if connected to the service
    /// </summary>
    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    /// <summary>
    /// Start a new scan
    /// </summary>
    public async Task<Guid> StartScanAsync(string folderPath, bool includeSubdirectories = true)
    {
        try
        {
            return await _connection.InvokeAsync<Guid>(
                "StartScan", 
                folderPath, 
                includeSubdirectories);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start scan: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get status of a scan session
    /// </summary>
    public async Task<ScanStatusDto> GetStatusAsync(Guid sessionId)
    {
        try
        {
            return await _connection.InvokeAsync<ScanStatusDto>("GetScanStatus", sessionId);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get scan status: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get all scan sessions
    /// </summary>
    public async Task<List<ScanSessionDto>> GetAllSessionsAsync()
    {
        try
        {
            return await _connection.InvokeAsync<List<ScanSessionDto>>("GetAllSessions");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get sessions: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Cancel a scan
    /// </summary>
    public async Task CancelScanAsync(Guid sessionId)
    {
        try
        {
            await _connection.InvokeAsync("CancelScan", sessionId);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to cancel scan: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Wait for scan completion with real-time progress updates
    /// </summary>
    public async Task<ScanStatusDto> WaitForScanCompletionAsync(
        Guid sessionId,
        Action<ScanProgressEventArgs>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var completionSource = new TaskCompletionSource<ScanStatusDto>();

        // Subscribe to completion events
        EventHandler<Guid>? completedHandler = null;
        EventHandler<(Guid, string)>? failedHandler = null;
        EventHandler<Guid>? cancelledHandler = null;
        EventHandler<ScanProgressEventArgs>? progressHandler = null;

        completedHandler = async (sender, completedSessionId) =>
        {
            if (completedSessionId == sessionId)
            {
                try
                {
                    var status = await GetStatusAsync(sessionId);
                    completionSource.TrySetResult(status);
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            }
        };

        failedHandler = async (sender, args) =>
        {
            if (args.Item1 == sessionId)
            {
                try
                {
                    var status = await GetStatusAsync(sessionId);
                    completionSource.TrySetResult(status);
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            }
        };

        cancelledHandler = async (sender, cancelledSessionId) =>
        {
            if (cancelledSessionId == sessionId)
            {
                try
                {
                    var status = await GetStatusAsync(sessionId);
                    completionSource.TrySetResult(status);
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            }
        };

        if (progressCallback != null)
        {
            progressHandler = (sender, args) =>
            {
                if (args.SessionId == sessionId)
                {
                    progressCallback(args);
                }
            };
            ProgressReceived += progressHandler;
        }

        ScanCompleted += completedHandler;
        ScanFailed += failedHandler;
        ScanCancelled += cancelledHandler;

        // Register cancellation
        cancellationToken.Register(() =>
        {
            completionSource.TrySetCanceled(cancellationToken);
        });

        try
        {
            return await completionSource.Task;
        }
        finally
        {
            // Cleanup event handlers
            ScanCompleted -= completedHandler;
            ScanFailed -= failedHandler;
            ScanCancelled -= cancelledHandler;
            if (progressHandler != null)
            {
                ProgressReceived -= progressHandler;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}

/// <summary>
/// Event arguments for scan progress updates
/// </summary>
public class ScanProgressEventArgs : EventArgs
{
    public Guid SessionId { get; set; }
    public int FilesScanned { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
}

using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AlUmmahNow.Data.Models.FileScanning;

namespace AlUmmahNow.DataImporter.Services;

/// <summary>
/// SignalR client for real-time communication with FileScanService
/// </summary>
public class FileScanSignalRClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly string _hubUrl;
    private bool _isConnected = false;

    public event EventHandler<ServiceStatusEventArgs>? ServiceStatusChanged;
    public event EventHandler<FileScanProgressEventArgs>? ScanProgressReceived;
    public event EventHandler<Guid>? ScanCompleted;
    public event EventHandler<(Guid sessionId, string error)>? ScanFailed;
    public event EventHandler<Guid>? ScanStarted;
    public event EventHandler<Guid>? ScanCancelled;
    public event EventHandler<string>? ErrorOccurred;

    public FileScanSignalRClient(string serviceUrl)
    {
        _hubUrl = $"{serviceUrl.TrimEnd('/')}/scanHub";
        
        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        // Subscribe to server events
        _connection.On<Guid, string>("ScanStarted", (sessionId, path) =>
        {
            Console.WriteLine($"? Scan started for session {sessionId}");
            ScanStarted?.Invoke(this, sessionId);
        });

        _connection.On<Guid, object>("ScanProgress", (sessionId, progress) =>
        {
            try
            {
                var progressData = JsonSerializer.Deserialize<ScanProgressData>(
                    JsonSerializer.Serialize(progress));
                
                if (progressData != null)
                {
                    ScanProgressReceived?.Invoke(this, new FileScanProgressEventArgs
                    {
                        SessionId = sessionId,
                        FilesScanned = progressData.FilesScanned,
                        CurrentFile = progressData.CurrentFile ?? "",
                        ProgressPercentage = progressData.ProgressPercentage,
                        CurrentOperation = progressData.CurrentOperation
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing scan progress: {ex.Message}");
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
            ErrorOccurred?.Invoke(this, $"Scan {sessionId} failed: {error}");
        });

        _connection.On<Guid>("ScanCancelled", (sessionId) =>
        {
            Console.WriteLine($"? Scan cancelled: {sessionId}");
            ScanCancelled?.Invoke(this, sessionId);
        });

        // Handle reconnection
        _connection.Reconnecting += error =>
        {
            _isConnected = false;
            Console.WriteLine($"? Connection lost. Reconnecting...");
            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs 
            { 
                Status = ServiceStatus.Reconnecting,
                Message = "Connection lost. Attempting to reconnect..."
            });
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            _isConnected = true;
            Console.WriteLine($"? Reconnected to FileScan service");
            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs 
            { 
                Status = ServiceStatus.Connected,
                Message = "Connected to FileScan service"
            });
            return Task.CompletedTask;
        };

        _connection.Closed += error =>
        {
            _isConnected = false;
            if (error != null)
            {
                Console.WriteLine($"? Connection closed with error: {error.Message}");
                ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs 
                { 
                    Status = ServiceStatus.Disconnected,
                    Message = $"Connection closed: {error.Message}"
                });
                ErrorOccurred?.Invoke(this, $"Connection closed: {error.Message}");
            }
            else
            {
                Console.WriteLine($"Connection closed");
                ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs 
                { 
                    Status = ServiceStatus.Stopped,
                    Message = "Service stopped"
                });
            }
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Connect to the SignalR hub
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            if (_connection.State == HubConnectionState.Connected)
            {
                _isConnected = true;
                return true;
            }

            await _connection.StartAsync();
            _isConnected = true;
            
            Console.WriteLine($"? Connected to FileScan service at {_hubUrl}");
            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs 
            { 
                Status = ServiceStatus.Connected,
                Message = "Connected to FileScan service"
            });
            
            return true;
        }
        catch (Exception ex)
        {
            _isConnected = false;
            Console.WriteLine($"? Failed to connect to FileScan service at {_hubUrl}: {ex.Message}");
            ServiceStatusChanged?.Invoke(this, new ServiceStatusEventArgs 
            { 
                Status = ServiceStatus.NotAvailable,
                Message = $"Service not available: {ex.Message}"
            });
            ErrorOccurred?.Invoke(this, $"Failed to connect: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if connected to the service
    /// </summary>
    public bool IsConnected => _isConnected && _connection.State == HubConnectionState.Connected;

    /// <summary>
    /// Get current service status
    /// </summary>
    public ServiceStatus GetServiceStatus()
    {
        return _connection.State switch
        {
            HubConnectionState.Connected => ServiceStatus.Connected,
            HubConnectionState.Connecting => ServiceStatus.Reconnecting,
            HubConnectionState.Reconnecting => ServiceStatus.Reconnecting,
            HubConnectionState.Disconnected => ServiceStatus.Disconnected,
            _ => ServiceStatus.NotAvailable
        };
    }

    /// <summary>
    /// Start a new scan
    /// </summary>
    public async Task<Guid> StartScanAsync(string folderPath, bool includeSubdirectories = true)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<Guid>(
                "StartScan", 
                folderPath, 
                includeSubdirectories);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to start scan: {ex.Message}");
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
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<ScanStatusDto>("GetScanStatus", sessionId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get scan status: {ex.Message}");
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
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            return await _connection.InvokeAsync<List<ScanSessionDto>>("GetAllSessions");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get sessions: {ex.Message}");
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
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            await _connection.InvokeAsync("CancelScan", sessionId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to cancel scan: {ex.Message}");
            throw new InvalidOperationException($"Failed to cancel scan: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Disconnect from the service
    /// </summary>
    public async Task DisconnectAsync()
    {
        try
        {
            if (_connection.State != HubConnectionState.Disconnected)
            {
                await _connection.StopAsync();
            }
            _isConnected = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disconnecting: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>
/// Event arguments for scan progress updates from FileScan service
/// </summary>
public class FileScanProgressEventArgs : EventArgs
{
    public Guid SessionId { get; set; }
    public int FilesScanned { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
}

/// <summary>
/// Event arguments for service status changes
/// </summary>
public class ServiceStatusEventArgs : EventArgs
{
    public ServiceStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Service connection status
/// </summary>
public enum ServiceStatus
{
    NotAvailable,
    Disconnected,
    Reconnecting,
    Connected,
    Stopped
}
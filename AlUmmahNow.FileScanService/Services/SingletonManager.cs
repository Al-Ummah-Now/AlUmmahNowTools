using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Manages singleton behavior for the FileScanService to prevent multiple instances
    /// This provides defense-in-depth with the UI's service manager
    /// </summary>
    public class SingletonManager : IDisposable
    {
        private readonly ILogger<SingletonManager> _logger;
        private readonly string _mutexName;
        private readonly string _lockFilePath;
        private readonly int _port;
        
        private Mutex? _globalMutex;
        private FileStream? _lockFileStream;
        private bool _isDisposed = false;

        public SingletonManager(ILogger<SingletonManager> logger, int port = 5000)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _port = port;
            
            // Create unique identifiers for this service instance
            _mutexName = $"Global\\AlUmmahNow.FileScanService.Port{_port}";
            _lockFilePath = Path.Combine(Path.GetTempPath(), $"AlUmmahNow.FileScanService.Port{_port}.lock");
            
            _logger.LogInformation("SingletonManager initialized for port {Port}", _port);
        }

        /// <summary>
        /// Attempts to acquire singleton lock. Returns true if successful, false if another instance exists.
        /// </summary>
        /// <returns>True if singleton lock acquired, false if another instance is running</returns>
        public bool TryAcquireSingletonLock()
        {
            try
            {
                _logger.LogInformation("Attempting to acquire singleton lock for FileScanService on port {Port}", _port);

                // Strategy 1: Global Mutex (works across user sessions)
                if (!TryAcquireGlobalMutex())
                {
                    _logger.LogWarning("Failed to acquire global mutex - another instance may be running");
                    return false;
                }

                // Strategy 2: File-based lock (backup mechanism)
                if (!TryAcquireFileLock())
                {
                    _logger.LogWarning("Failed to acquire file lock - another instance may be running");
                    ReleaseMutex(); // Clean up mutex if file lock fails
                    return false;
                }

                // Strategy 3: Port binding check (final verification)
                if (!IsPortAvailable())
                {
                    _logger.LogWarning("Port {Port} is not available - another instance may be running", _port);
                    ReleaseAllLocks();
                    return false;
                }

                _logger.LogInformation("Successfully acquired singleton lock for FileScanService on port {Port}", _port);
                
                // Write process information to lock file
                WriteLockFileInfo();
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error acquiring singleton lock");
                ReleaseAllLocks();
                return false;
            }
        }

        /// <summary>
        /// Checks if another instance of the service is running
        /// </summary>
        /// <returns>True if another instance is detected, false otherwise</returns>
        public bool IsAnotherInstanceRunning()
        {
            try
            {
                // Check if we can acquire the mutex (without keeping it)
                using var testMutex = new Mutex(false, _mutexName);
                bool mutexAcquired = testMutex.WaitOne(100); // Very short timeout
                
                if (mutexAcquired)
                {
                    testMutex.ReleaseMutex();
                    
                    // Mutex was available, check other indicators
                    return File.Exists(_lockFilePath) || !IsPortAvailable();
                }
                
                // Mutex was not available - another instance is definitely running
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking for other instances - assuming another instance is running");
                return true; // Assume another instance is running if we can't check
            }
        }

        /// <summary>
        /// Gets information about the current singleton lock holder
        /// </summary>
        /// <returns>Singleton lock information</returns>
        public SingletonLockInfo GetLockInfo()
        {
            var info = new SingletonLockInfo
            {
                Port = _port,
                MutexName = _mutexName,
                LockFilePath = _lockFilePath,
                IsLockHeld = _globalMutex != null && _lockFileStream != null,
                CurrentProcessId = Environment.ProcessId,
                CurrentProcessName = Process.GetCurrentProcess().ProcessName
            };

            // Try to read lock file information
            if (File.Exists(_lockFilePath))
            {
                try
                {
                    var lockContent = File.ReadAllText(_lockFilePath);
                    var lines = lockContent.Split('\n');
                    
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("ProcessId:"))
                            info.LockHolderProcessId = int.TryParse(line.Split(':')[1].Trim(), out var pid) ? pid : null;
                        else if (line.StartsWith("ProcessName:"))
                            info.LockHolderProcessName = line.Split(':')[1].Trim();
                        else if (line.StartsWith("StartTime:"))
                            info.LockHolderStartTime = DateTime.TryParse(line.Split(':', 2)[1].Trim(), out var time) ? time : null;
                        else if (line.StartsWith("MachineName:"))
                            info.LockHolderMachineName = line.Split(':')[1].Trim();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not read lock file information");
                }
            }

            return info;
        }

        private bool TryAcquireGlobalMutex()
        {
            try
            {
                _globalMutex = new Mutex(false, _mutexName);
                
                // Try to acquire the mutex with a short timeout
                bool acquired = _globalMutex.WaitOne(1000); // 1 second timeout
                
                if (!acquired)
                {
                    _globalMutex.Dispose();
                    _globalMutex = null;
                    return false;
                }
                
                _logger.LogDebug("Global mutex acquired successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to acquire global mutex");
                _globalMutex?.Dispose();
                _globalMutex = null;
                return false;
            }
        }

        private bool TryAcquireFileLock()
        {
            try
            {
                // Ensure the directory exists
                var directory = Path.GetDirectoryName(_lockFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Try to open the file exclusively
                _lockFileStream = new FileStream(_lockFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                
                _logger.LogDebug("File lock acquired successfully at {LockFilePath}", _lockFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to acquire file lock at {LockFilePath}", _lockFilePath);
                _lockFileStream?.Dispose();
                _lockFileStream = null;
                return false;
            }
        }

        private bool IsPortAvailable()
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var result = client.BeginConnect("127.0.0.1", _port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));
                
                if (success)
                {
                    try
                    {
                        client.EndConnect(result);
                        // If connection succeeded, port is in use
                        return false;
                    }
                    catch
                    {
                        // Connection failed, port is available
                        return true;
                    }
                }
                
                // Connection timed out, assume port is available
                return true;
            }
            catch
            {
                // Any error checking port, assume it's available
                return true;
            }
        }

        private void WriteLockFileInfo()
        {
            try
            {
                if (_lockFileStream != null)
                {
                    var process = Process.GetCurrentProcess();
                    var lockInfo = $"ProcessId: {process.Id}\n" +
                                  $"ProcessName: {process.ProcessName}\n" +
                                  $"StartTime: {process.StartTime:yyyy-MM-dd HH:mm:ss}\n" +
                                  $"MachineName: {Environment.MachineName}\n" +
                                  $"Port: {_port}\n" +
                                  $"ServiceName: AlUmmahNow.FileScanService\n" +
                                  $"LockAcquired: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";

                    var bytes = System.Text.Encoding.UTF8.GetBytes(lockInfo);
                    _lockFileStream.Write(bytes, 0, bytes.Length);
                    _lockFileStream.Flush();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write lock file information");
            }
        }

        private void ReleaseMutex()
        {
            try
            {
                if (_globalMutex != null)
                {
                    _globalMutex.ReleaseMutex();
                    _globalMutex.Dispose();
                    _globalMutex = null;
                    _logger.LogDebug("Global mutex released");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing global mutex");
            }
        }

        private void ReleaseFileLock()
        {
            try
            {
                if (_lockFileStream != null)
                {
                    _lockFileStream.Dispose();
                    _lockFileStream = null;
                    _logger.LogDebug("File lock released");
                }

                // Try to delete the lock file
                if (File.Exists(_lockFilePath))
                {
                    File.Delete(_lockFilePath);
                    _logger.LogDebug("Lock file deleted");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing file lock");
            }
        }

        private void ReleaseAllLocks()
        {
            ReleaseMutex();
            ReleaseFileLock();
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _logger.LogInformation("Releasing singleton locks for FileScanService on port {Port}", _port);
                
                ReleaseAllLocks();
                
                _isDisposed = true;
                _logger.LogInformation("SingletonManager disposed successfully");
            }
        }
    }

    /// <summary>
    /// Information about singleton lock status
    /// </summary>
    public class SingletonLockInfo
    {
        public int Port { get; set; }
        public string MutexName { get; set; } = string.Empty;
        public string LockFilePath { get; set; } = string.Empty;
        public bool IsLockHeld { get; set; }
        public int CurrentProcessId { get; set; }
        public string CurrentProcessName { get; set; } = string.Empty;
        public int? LockHolderProcessId { get; set; }
        public string? LockHolderProcessName { get; set; }
        public DateTime? LockHolderStartTime { get; set; }
        public string? LockHolderMachineName { get; set; }
    }
}
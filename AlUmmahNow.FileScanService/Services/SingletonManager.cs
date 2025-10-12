using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Checks for singleton behavior for the FileScanService to prevent multiple instances
    /// Uses simple port availability check
    /// </summary>
    public class SingletonManager : IDisposable
    {
        private readonly ILogger<SingletonManager> _logger;
        private readonly int _port;
        private bool _isDisposed = false;

        public SingletonManager(ILogger<SingletonManager> logger, int port = 5000)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _port = port;

            _logger.LogInformation("SingletonManager initialized for port {Port}", _port);
        }

        /// <summary>
        /// Checks if another instance of the service is running by testing port availability
        /// </summary>
        /// <returns>True if another instance is detected, false otherwise</returns>
        public bool IsAnotherInstanceRunning()
        {
            return !IsPortAvailable();
        }

        /// <summary>
        /// Gets information about the current process
        /// </summary>
        /// <returns>Singleton information</returns>
        public SingletonLockInfo GetLockInfo()
        {
            var process = Process.GetCurrentProcess();
            return new SingletonLockInfo
            {
                Port = _port,
                IsPortInUse = !IsPortAvailable(),
                CurrentProcessId = Environment.ProcessId,
                CurrentProcessName = process.ProcessName,
                CurrentProcessStartTime = process.StartTime,
                MachineName = Environment.MachineName
            };
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

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _logger.LogInformation("SingletonManager disposed successfully");
            }
        }
    }

    /// <summary>
    /// Information about singleton status
    /// </summary>
    public class SingletonLockInfo
    {
        public int Port { get; set; }
        public bool IsPortInUse { get; set; }
        public int CurrentProcessId { get; set; }
        public string CurrentProcessName { get; set; } = string.Empty;
        public DateTime CurrentProcessStartTime { get; set; }
        public string MachineName { get; set; } = string.Empty;
    }
}
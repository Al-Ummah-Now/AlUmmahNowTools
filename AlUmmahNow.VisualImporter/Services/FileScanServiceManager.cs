using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace AlUmmahNow.VisualImporter.Services
{
    /// <summary>
    /// Manages the FileScanService lifecycle for the VisualImporter
    /// Ensures the service is running as a singleton and provides health monitoring
    /// </summary>
    public class FileScanServiceManager : IDisposable
    {
        private readonly ILogger<FileScanServiceManager> _logger;
        private readonly string _serviceUrl;
        private readonly string? _serviceExecutablePath;
        private readonly HttpClient _httpClient;
        private readonly Timer _healthCheckTimer;
        private readonly object _lockObject = new object();
        
        private Process? _serviceProcess;
        private bool _isServiceManaged = false;
        private bool _isDisposed = false;
        private ServiceHealthStatus _currentStatus = ServiceHealthStatus.Unknown;

        public event EventHandler<ServiceStatusChangedEventArgs>? ServiceStatusChanged;

        public FileScanServiceManager(ILogger<FileScanServiceManager> logger, string serviceUrl = "http://localhost:5000", string? serviceExecutablePath = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceUrl = serviceUrl.TrimEnd('/');
            _serviceExecutablePath = serviceExecutablePath;
            
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            // Start health monitoring timer (every 30 seconds)
            _healthCheckTimer = new Timer(async _ => await CheckServiceHealthAsync(), 
                null, TimeSpan.Zero, TimeSpan.FromSeconds(30));

            _logger.LogInformation("FileScanServiceManager initialized for URL: {ServiceUrl}, ExecutablePath: {ExecutablePath}", 
                _serviceUrl, _serviceExecutablePath ?? "auto-detect");
        }

        /// <summary>
        /// Gets the current service status
        /// </summary>
        public ServiceHealthStatus CurrentStatus => _currentStatus;

        /// <summary>
        /// Gets whether the service is being managed by this instance
        /// </summary>
        public bool IsServiceManaged => _isServiceManaged;

        /// <summary>
        /// Gets the service URL
        /// </summary>
        public string ServiceUrl => _serviceUrl;

        /// <summary>
        /// Ensures the FileScanService is running, starting it if necessary
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if service is running, false otherwise</returns>
        public async Task<bool> EnsureServiceRunningAsync(CancellationToken cancellationToken = default)
        {
            lock (_lockObject)
            {
                if (_isDisposed)
                {
                    _logger.LogWarning("Cannot ensure service is running - manager is disposed");
                    return false;
                }
            }

            try
            {
                _logger.LogInformation("Checking if FileScanService is running...");

                // First, check if service is already running
                if (await IsServiceRunningAsync())
                {
                    _logger.LogInformation("FileScanService is already running");
                    UpdateServiceStatus(ServiceHealthStatus.Running);
                    return true;
                }

                // Service is not running, attempt to start it
                _logger.LogInformation("FileScanService is not running, attempting to start...");
                return await StartServiceAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring FileScanService is running");
                UpdateServiceStatus(ServiceHealthStatus.Error);
                return false;
            }
        }

        /// <summary>
        /// Checks if the FileScanService is currently running by attempting to connect
        /// </summary>
        /// <returns>True if service is responding, false otherwise</returns>
        public async Task<bool> IsServiceRunningAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_serviceUrl}/health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health check failed for FileScanService");
                return false;
            }
        }

        /// <summary>
        /// Starts the FileScanService if it's not already running
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if service was started successfully, false otherwise</returns>
        public async Task<bool> StartServiceAsync(CancellationToken cancellationToken = default)
        {
            lock (_lockObject)
            {
                if (_isDisposed)
                {
                    _logger.LogWarning("Cannot start service - manager is disposed");
                    return false;
                }

                if (_isServiceManaged && _serviceProcess != null && !_serviceProcess.HasExited)
                {
                    _logger.LogInformation("FileScanService is already managed by this instance");
                    return true;
                }
            }

            try
            {
                UpdateServiceStatus(ServiceHealthStatus.Starting);

                var serviceExecutable = FindServiceExecutable();
                if (string.IsNullOrEmpty(serviceExecutable))
                {
                    _logger.LogError("FileScanService executable not found");
                    UpdateServiceStatus(ServiceHealthStatus.NotFound);
                    return false;
                }

                _logger.LogInformation("Starting FileScanService from: {ExecutablePath}", serviceExecutable);

                var processStartInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                };

                // Handle different executable types
                if (serviceExecutable.Contains("|"))
                {
                    // dotnet run command
                    var parts = serviceExecutable.Split('|');
                    processStartInfo.FileName = parts[0]; // "dotnet"
                    processStartInfo.Arguments = string.Join(" ", parts.Skip(1)); // "run --project ..."
                }
                else
                {
                    // Direct executable
                    processStartInfo.FileName = serviceExecutable;
                    processStartInfo.Arguments = "";
                }

                Process? process = null;
                bool processStarted = false;

                lock (_lockObject)
                {
                    _serviceProcess = new Process { StartInfo = processStartInfo };
                    _serviceProcess.EnableRaisingEvents = true;
                    
                    _serviceProcess.OutputDataReceived += OnServiceOutputReceived;
                    _serviceProcess.ErrorDataReceived += OnServiceErrorReceived;
                    _serviceProcess.Exited += OnServiceExited;

                    try
                    {
                        _serviceProcess.Start();
                        _serviceProcess.BeginOutputReadLine();
                        _serviceProcess.BeginErrorReadLine();
                        processStarted = true;
                        process = _serviceProcess;

                        _logger.LogInformation("FileScanService process started with PID: {ProcessId}", _serviceProcess.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start FileScanService process");
                        CleanupProcess();
                        throw;
                    }
                }

                if (!processStarted || process == null)
                {
                    UpdateServiceStatus(ServiceHealthStatus.Error);
                    return false;
                }

                // Wait for service to become available (up to 30 seconds)
                var timeout = TimeSpan.FromSeconds(30);
                var stopwatch = Stopwatch.StartNew();
                var lastLogTime = DateTime.MinValue;

                while (stopwatch.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
                {
                    // Check if process has exited unexpectedly
                    if (process.HasExited)
                    {
                        var exitCode = process.ExitCode;
                        _logger.LogWarning("FileScanService process exited during startup with code: {ExitCode}", exitCode);
                        
                        // Handle specific exit codes from the service's singleton management
                        if (exitCode == 2)
                        {
                            _logger.LogInformation("Another instance of FileScanService is already running (singleton enforcement)");
                            UpdateServiceStatus(ServiceHealthStatus.Running);
                            _isServiceManaged = false; // We're not managing it, but it's running
                            return true; // Service is available (managed by another process)
                        }
                        else if (exitCode == 3)
                        {
                            _logger.LogWarning("FileScanService failed to acquire singleton lock");
                            UpdateServiceStatus(ServiceHealthStatus.Error);
                            return false;
                        }
                        else
                        {
                            _logger.LogError("FileScanService failed to start (exit code: {ExitCode})", exitCode);
                            UpdateServiceStatus(ServiceHealthStatus.Error);
                            return false;
                        }
                    }

                    if (await IsServiceRunningAsync())
                    {
                        _logger.LogInformation("FileScanService is now running and responding");
                        UpdateServiceStatus(ServiceHealthStatus.Running);
                        _isServiceManaged = true;
                        return true;
                    }

                    // Log progress periodically
                    if (DateTime.Now - lastLogTime > TimeSpan.FromSeconds(5))
                    {
                        _logger.LogDebug("Waiting for FileScanService to become available... ({Elapsed:F1}s elapsed)", 
                            stopwatch.Elapsed.TotalSeconds);
                        lastLogTime = DateTime.Now;
                    }

                    await Task.Delay(1000, cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Service startup was cancelled");
                    UpdateServiceStatus(ServiceHealthStatus.Stopped);
                    return false;
                }

                _logger.LogError("FileScanService failed to become available within timeout period");
                UpdateServiceStatus(ServiceHealthStatus.Error);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start FileScanService");
                UpdateServiceStatus(ServiceHealthStatus.Error);
                return false;
            }
        }

        /// <summary>
        /// Stops the managed FileScanService if it's running
        /// </summary>
        /// <returns>True if service was stopped successfully, false otherwise</returns>
        public async Task<bool> StopServiceAsync()
        {
            lock (_lockObject)
            {
                if (!_isServiceManaged || _serviceProcess == null)
                {
                    _logger.LogInformation("No managed FileScanService to stop");
                    return true;
                }

                if (_serviceProcess.HasExited)
                {
                    _logger.LogInformation("FileScanService process has already exited");
                    CleanupProcess();
                    return true;
                }
            }

            try
            {
                _logger.LogInformation("Stopping FileScanService...");
                UpdateServiceStatus(ServiceHealthStatus.Stopping);

                // Try graceful shutdown first
                try
                {
                    _serviceProcess.StandardInput.WriteLine("exit");
                    _serviceProcess.StandardInput.Close();

                    if (await WaitForProcessExitAsync(_serviceProcess, TimeSpan.FromSeconds(10)))
                    {
                        _logger.LogInformation("FileScanService stopped gracefully");
                        UpdateServiceStatus(ServiceHealthStatus.Stopped);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Graceful shutdown failed, attempting force kill");
                }

                // Force kill if graceful shutdown failed
                try
                {
                    _serviceProcess.Kill();
                    if (await WaitForProcessExitAsync(_serviceProcess, TimeSpan.FromSeconds(5)))
                    {
                        _logger.LogInformation("FileScanService stopped forcefully");
                        UpdateServiceStatus(ServiceHealthStatus.Stopped);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to kill FileScanService process");
                }

                _logger.LogError("Failed to stop FileScanService");
                UpdateServiceStatus(ServiceHealthStatus.Error);
                return false;
            }
            finally
            {
                CleanupProcess();
            }
        }

        /// <summary>
        /// Gets detailed service information including singleton status
        /// </summary>
        /// <returns>Service information object</returns>
        public async Task<ServiceInfo> GetServiceInfoAsync()
        {
            var isRunning = await IsServiceRunningAsync();
            ServiceSingletonInfo? singletonInfo = null;
            
            // Try to get singleton information from the service
            if (isRunning)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{_serviceUrl}/singleton");
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var singletonData = System.Text.Json.JsonSerializer.Deserialize<ServiceSingletonResponse>(content, 
                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        singletonInfo = singletonData?.LockInfo;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not retrieve singleton information from service");
                }
            }
            
            return new ServiceInfo
            {
                Url = _serviceUrl,
                Status = _currentStatus,
                IsRunning = isRunning,
                IsManaged = _isServiceManaged,
                ProcessId = _serviceProcess?.Id,
                HasExited = _serviceProcess?.HasExited ?? true,
                LastHealthCheck = DateTime.Now,
                SingletonInfo = singletonInfo
            };
        }

        /// <summary>
        /// Finds the FileScanService executable
        /// </summary>
        /// <returns>Path to executable or command to run with dotnet</returns>
        private string? FindServiceExecutable()
        {
            // Use configured path if available
            if (!string.IsNullOrEmpty(_serviceExecutablePath))
            {
                if (File.Exists(_serviceExecutablePath))
                {
                    _logger.LogDebug("Using configured FileScanService executable: {Path}", _serviceExecutablePath);
                    return Path.GetFullPath(_serviceExecutablePath);
                }
                else
                {
                    _logger.LogWarning("Configured FileScanService executable not found: {Path}", _serviceExecutablePath);
                    // Continue with auto-detection as fallback
                }
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            
            // Look for built executable first
            var possiblePaths = new[]
            {
                // Same directory (if published together)
                Path.Combine(currentDirectory, "AlUmmahNow.FileScanService.exe"),
                
                // Relative paths from VisualImporter directory
                Path.Combine(currentDirectory, "..", "AlUmmahNow.FileScanService", "bin", "Debug", "net8.0", "AlUmmahNow.FileScanService.exe"),
                Path.Combine(currentDirectory, "..", "AlUmmahNow.FileScanService", "bin", "Release", "net8.0", "AlUmmahNow.FileScanService.exe"),
                
                // From solution directory
                Path.Combine(FindSolutionDirectory() ?? "", "AlUmmahNow.FileScanService", "bin", "Debug", "net8.0", "AlUmmahNow.FileScanService.exe"),
                Path.Combine(FindSolutionDirectory() ?? "", "AlUmmahNow.FileScanService", "bin", "Release", "net8.0", "AlUmmahNow.FileScanService.exe"),
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _logger.LogDebug("Found FileScanService executable at: {Path}", path);
                    return Path.GetFullPath(path);
                }
            }

            // Try to find project file for dotnet run
            var solutionDir = FindSolutionDirectory();
            if (!string.IsNullOrEmpty(solutionDir))
            {
                var projectPath = Path.Combine(solutionDir, "AlUmmahNow.FileScanService", "AlUmmahNow.FileScanService.csproj");
                if (File.Exists(projectPath))
                {
                    _logger.LogDebug("Found FileScanService project at: {Path}", projectPath);
                    return $"dotnet|run|--project|{projectPath}";
                }
            }

            _logger.LogError("FileScanService executable or project not found");
            return null;
        }

        /// <summary>
        /// Finds the solution directory by looking for .sln files
        /// </summary>
        /// <returns>Solution directory path or null</returns>
        private string? FindSolutionDirectory()
        {
            var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (currentDir != null)
            {
                if (currentDir.GetFiles("*.sln").Any())
                {
                    return currentDir.FullName;
                }
                currentDir = currentDir.Parent;
            }

            return null;
        }

        /// <summary>
        /// Waits for a process to exit with timeout
        /// </summary>
        private async Task<bool> WaitForProcessExitAsync(Process process, TimeSpan timeout)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                while (!process.HasExited && !cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, cts.Token);
                }
                return process.HasExited;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// Performs periodic health checks on the service
        /// </summary>
        private async Task CheckServiceHealthAsync()
        {
            if (_isDisposed) return;

            try
            {
                var isRunning = await IsServiceRunningAsync();
                var newStatus = isRunning ? ServiceHealthStatus.Running : ServiceHealthStatus.Stopped;

                // Only update if status actually changed
                if (newStatus != _currentStatus)
                {
                    UpdateServiceStatus(newStatus);
                    
                    if (!isRunning && _isServiceManaged)
                    {
                        _logger.LogWarning("Managed FileScanService appears to have stopped unexpectedly");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health check failed");
                UpdateServiceStatus(ServiceHealthStatus.Error);
            }
        }

        /// <summary>
        /// Updates the service status and notifies listeners
        /// </summary>
        private void UpdateServiceStatus(ServiceHealthStatus newStatus)
        {
            if (_currentStatus == newStatus) return;

            var previousStatus = _currentStatus;
            _currentStatus = newStatus;

            _logger.LogInformation("FileScanService status changed: {PreviousStatus} -> {NewStatus}", 
                previousStatus, newStatus);

            ServiceStatusChanged?.Invoke(this, new ServiceStatusChangedEventArgs
            {
                PreviousStatus = previousStatus,
                CurrentStatus = newStatus,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Handles service process output
        /// </summary>
        private void OnServiceOutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogDebug("FileScanService: {Output}", e.Data);
            }
        }

        /// <summary>
        /// Handles service process errors
        /// </summary>
        private void OnServiceErrorReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogWarning("FileScanService Error: {Error}", e.Data);
            }
        }

        /// <summary>
        /// Handles service process exit
        /// </summary>
        private void OnServiceExited(object? sender, EventArgs e)
        {
            _logger.LogInformation("FileScanService process exited");
            CleanupProcess();
            UpdateServiceStatus(ServiceHealthStatus.Stopped);
        }

        /// <summary>
        /// Cleans up process resources
        /// </summary>
        private void CleanupProcess()
        {
            lock (_lockObject)
            {
                if (_serviceProcess != null)
                {
                    try
                    {
                        _serviceProcess.OutputDataReceived -= OnServiceOutputReceived;
                        _serviceProcess.ErrorDataReceived -= OnServiceErrorReceived;
                        _serviceProcess.Exited -= OnServiceExited;
                        _serviceProcess.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error disposing service process");
                    }
                    finally
                    {
                        _serviceProcess = null;
                        _isServiceManaged = false;
                    }
                }
            }
        }

        /// <summary>
        /// Disposes of resources
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;

            try
            {
                _healthCheckTimer?.Dispose();
                
                // Stop the service if we're managing it
                if (_isServiceManaged)
                {
                    var stopTask = StopServiceAsync();
                    stopTask.Wait(TimeSpan.FromSeconds(10));
                }

                CleanupProcess();
                _httpClient?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing FileScanServiceManager");
            }

            _logger.LogInformation("FileScanServiceManager disposed");
        }
    }

    /// <summary>
    /// Service health status enumeration
    /// </summary>
    public enum ServiceHealthStatus
    {
        Unknown,
        Starting,
        Running,
        Stopping,
        Stopped,
        Error,
        NotFound
    }

    /// <summary>
    /// Event arguments for service status changes
    /// </summary>
    public class ServiceStatusChangedEventArgs : EventArgs
    {
        public ServiceHealthStatus PreviousStatus { get; set; }
        public ServiceHealthStatus CurrentStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Service information class
    /// </summary>
    public class ServiceInfo
    {
        public string Url { get; set; } = string.Empty;
        public ServiceHealthStatus Status { get; set; }
        public bool IsRunning { get; set; }
        public bool IsManaged { get; set; }
        public int? ProcessId { get; set; }
        public bool HasExited { get; set; }
        public DateTime LastHealthCheck { get; set; }
        public ServiceSingletonInfo? SingletonInfo { get; set; }
    }

    /// <summary>
    /// Singleton information from the service
    /// </summary>
    public class ServiceSingletonInfo
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

    /// <summary>
    /// Response from the service's singleton endpoint
    /// </summary>
    public class ServiceSingletonResponse
    {
        public bool Singleton { get; set; }
        public ServiceSingletonInfo? LockInfo { get; set; }
        public string Service { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
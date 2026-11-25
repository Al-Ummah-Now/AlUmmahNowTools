using AlUmmahNow.VisualImporterUno.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Diagnostics;
using AlUmmahNow.VisualImporterUno.Services;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using System.Linq;

namespace AlUmmahNow.VisualImporterUno.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SettingsViewModel> _logger;
        private readonly FileScanServiceManager _serviceManager;
        private readonly INotificationService _notificationService;

        private string _fileScanServiceUrl = "http://localhost:5000";
        private string _databaseConnectionString = "";
        private bool _isSaving = false;
        private bool _isTesting = false;
        private string _testResults = "";
        private string _serviceExecutablePath = "";
        private bool _autoStartService = true;
        private ObservableCollection<string> _detectedServicePaths = new();
        private string? _selectedDetectedPath;
        private bool _enableNotifications = true;
        private bool _enableNotificationSound = true;
        private readonly AlUmmahNow.ToolsCore.UserSettingsFile _file;
        private bool _unread;

        public SettingsViewModel(IConfiguration configuration, ILogger<SettingsViewModel> logger, FileScanServiceManager serviceManager, INotificationService notificationService, AlUmmahNow.ToolsCore.UserSettingsFile? file = null)
        {
            _file = file ?? AlUmmahNow.ToolsCore.UserSettingsFile.Default;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            (_enableNotifications, _enableNotificationSound) = (_notificationService.IsEnabled, _notificationService.IsSoundEnabled);
            LoadSettings();
            DetectServicePaths();
        }

        /// <summary>Where the scanner runs, in words: this computer unless the address points somewhere else.</summary>
        public string ScannerLocationText =>
            Uri.TryCreate(FileScanServiceUrl, UriKind.Absolute, out var uri) && !uri.IsLoopback
                ? Loc.Get("Settings_LocationOther")
                : Loc.Get("Settings_LocationHere");

        public string FileScanServiceUrl
        {
            get => _fileScanServiceUrl;
            set
            {
                if (SetProperty(ref _fileScanServiceUrl, value))
                {
                    TestResults = ""; // Clear previous test results when URL changes
                    OnPropertyChanged(nameof(ScannerLocationText));
                }
            }
        }

        public string DatabaseConnectionString
        {
            get => _databaseConnectionString;
            set => SetProperty(ref _databaseConnectionString, value);
        }

        public string ServiceExecutablePath
        {
            get => _serviceExecutablePath;
            set => SetProperty(ref _serviceExecutablePath, value);
        }

        public bool AutoStartService
        {
            get => _autoStartService;
            set => SetProperty(ref _autoStartService, value);
        }

        public ObservableCollection<string> DetectedServicePaths => _detectedServicePaths;

        public string? SelectedDetectedPath
        {
            get => _selectedDetectedPath;
            set
            {
                if (SetProperty(ref _selectedDetectedPath, value) && !string.IsNullOrEmpty(value))
                {
                    ServiceExecutablePath = value;
                }
            }
        }

        public bool EnableNotifications
        {
            get => _enableNotifications;
            set
            {
                if (SetProperty(ref _enableNotifications, value))
                {
                    _notificationService.IsEnabled = value;
                }
            }
        }

        public bool EnableNotificationSound
        {
            get => _enableNotificationSound;
            set
            {
                if (SetProperty(ref _enableNotificationSound, value))
                {
                    _notificationService.IsSoundEnabled = value;
                }
            }
        }

        public bool IsSaving
        {
            get => _isSaving;
            private set => SetProperty(ref _isSaving, value);
        }

        public bool IsTesting
        {
            get => _isTesting;
            private set => SetProperty(ref _isTesting, value);
        }

        public string TestResults
        {
            get => _testResults;
            private set => SetProperty(ref _testResults, value);
        }

        public async Task TestConnectionAsync()
        {
            if (IsTesting) return;

            IsTesting = true;
            TestResults = Loc.Get("Settings_Testing");

            try
            {
                // Test the service URL
                var uri = new Uri(FileScanServiceUrl);
                
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                var response = await httpClient.GetAsync($"{uri.Scheme}://{uri.Authority}/health");
                
                if (response.IsSuccessStatusCode)
                {
                    TestResults = Loc.Get("Settings_TestOk");
                }
                else
                {
                    TestResults = Loc.GetWithDetail("Settings_TestRefused", $"HTTP {(int)response.StatusCode}");
                }
            }
            catch (UriFormatException)
            {
                TestResults = Loc.Get("Settings_TestBadAddress");
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                TestResults = Loc.GetWithDetail("Settings_TestFailed", ex.Message);
            }
            catch (TaskCanceledException)
            {
                TestResults = Loc.Get("Settings_TestTimeout");
            }
            catch (Exception ex)
            {
                TestResults = Loc.GetWithDetail("Settings_TestFailed", ex.Message);
                _logger.LogError(ex, "Error testing service connection");
            }
            finally
            {
                IsTesting = false;
            }
        }

        public async Task<bool> SaveSettingsAsync()
        {
            if (IsSaving) return false;

            IsSaving = true;
            try
            {
                // What was saved could not be read: what the page shows is not it, and is not written over it
                if (_unread)
                {
                    LoadSettings();
                    return false;
                }
                var settings = new
                {
                    FileScanService = new { Url = FileScanServiceUrl },
                    // The notification switches are saved by the notification service, as they are changed
                    ServiceInstallation = new
                    {
                        ExecutablePath = ServiceExecutablePath,
                        StartAutomatically = AutoStartService
                    }
                };
                
                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await Task.Run(() => _file.Write(json));
                
                _logger.LogInformation("Settings saved to: {SettingsPath}", _file.Path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings");
                return false;
            }
            finally
            {
                IsSaving = false;
            }
        }

        public void ResetToDefaults()
        {
            ResetScannerToDefaults();
            EnableNotifications = true;
            EnableNotificationSound = true;
        }

        private void ResetScannerToDefaults()
        {
            FileScanServiceUrl = "http://localhost:5000";
            DatabaseConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true";
            ServiceExecutablePath = "";
            AutoStartService = true;
            TestResults = "";
        }

        private void LoadSettings()
        {
            try
            {
                // The user settings file
                _unread = false;
                if (_file.Read() is { } json)
                {
                    var settings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                    
                    if (settings.TryGetProperty("FileScanService", out var fileScanService) &&
                        fileScanService.TryGetProperty("Url", out var url))
                    {
                        FileScanServiceUrl = url.GetString() ?? "http://localhost:5000";
                    }
                    
                    if (settings.TryGetProperty("ServiceInstallation", out var serviceInstallation))
                    {
                        if (serviceInstallation.TryGetProperty("ExecutablePath", out var execPath))
                        {
                            ServiceExecutablePath = execPath.GetString() ?? "";
                        }
                        
                        if (serviceInstallation.TryGetProperty("StartAutomatically", out var autoStart))
                        {
                            AutoStartService = autoStart.GetBoolean();
                        }
                    }

                    _logger.LogInformation("Settings loaded from: {SettingsPath}", _file.Path);
                }
                else
                {
                    // Final fallback to configuration sources (appsettings.json, user secrets)
                    FileScanServiceUrl = _configuration.GetValue<string>("FileScanService:Url") ?? "http://localhost:5000";
                    
                    var serviceConfig = _configuration.GetSection("ServiceInstallation");
                    ServiceExecutablePath = serviceConfig["ExecutablePath"] ?? "";
                    AutoStartService = serviceConfig.GetValue<bool>("StartAutomatically", true);
                    
                    _logger.LogInformation("Settings loaded from configuration sources (final fallback)");
                }
                
                // Always load database connection from configuration (security sensitive)
                DatabaseConnectionString = _configuration.GetConnectionString("DefaultConnection") ?? 
                    "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading settings, using defaults");
                _unread = ex is IOException or UnauthorizedAccessException;
                // Not the notification switches: they are saved as they are set, and elsewhere
                ResetScannerToDefaults();
            }
        }

        private void DetectServicePaths()
        {
            try
            {
                DetectedServicePaths.Clear();

                foreach (var path in FileScanServiceManager.ScannerCandidates(null))
                {
                    if (File.Exists(path) && !DetectedServicePaths.Contains(Path.GetFullPath(path)))
                    {
                        DetectedServicePaths.Add(Path.GetFullPath(path));
                    }
                }

                // Check in PATH environment variable
                var pathVar = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathVar))
                {
                    var pathDirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var dir in pathDirs)
                    {
                        try
                        {
                            var servicePath = Path.Combine(dir, OperatingSystem.IsWindows() ? "AlUmmahNow.FileScanService.exe" : "AlUmmahNow.FileScanService");
                            if (File.Exists(servicePath) && !DetectedServicePaths.Contains(servicePath))
                            {
                                DetectedServicePaths.Add(servicePath);
                            }
                        }
                        catch
                        {
                            // Ignore invalid path entries
                        }
                    }
                }

                // If we found paths and current setting is empty, suggest the first one
                if (DetectedServicePaths.Any() && string.IsNullOrEmpty(ServiceExecutablePath))
                {
                    SelectedDetectedPath = DetectedServicePaths.First();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error detecting service paths");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            if (object.Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
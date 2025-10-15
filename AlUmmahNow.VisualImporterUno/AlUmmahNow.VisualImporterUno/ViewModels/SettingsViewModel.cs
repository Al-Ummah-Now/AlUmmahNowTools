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

        public SettingsViewModel(IConfiguration configuration, ILogger<SettingsViewModel> logger, FileScanServiceManager serviceManager, INotificationService notificationService)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            LoadSettings();
            DetectServicePaths();
        }

        public string FileScanServiceUrl
        {
            get => _fileScanServiceUrl;
            set
            {
                if (SetProperty(ref _fileScanServiceUrl, value))
                {
                    TestResults = ""; // Clear previous test results when URL changes
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
            TestResults = "Testing connection...";

            try
            {
                // Test the service URL
                var uri = new Uri(FileScanServiceUrl);
                
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                var response = await httpClient.GetAsync($"{uri.Scheme}://{uri.Authority}/health");
                
                if (response.IsSuccessStatusCode)
                {
                    TestResults = "? Service connection successful";
                }
                else
                {
                    TestResults = $"? Service responded with {response.StatusCode}";
                }
            }
            catch (UriFormatException)
            {
                TestResults = "? Invalid URL format";
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                TestResults = $"? Connection failed: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                TestResults = "? Connection timeout";
            }
            catch (Exception ex)
            {
                TestResults = $"? Unexpected error: {ex.Message}";
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
                // Use Windows ApplicationData for native Windows 11 settings storage
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var settingsContainer = localSettings.CreateContainer("AlUmmahNowSettings", 
                    Windows.Storage.ApplicationDataCreateDisposition.Always);
                
                // Store individual settings
                settingsContainer.Values["FileScanServiceUrl"] = FileScanServiceUrl;
                settingsContainer.Values["ServiceExecutablePath"] = ServiceExecutablePath;
                settingsContainer.Values["AutoStartService"] = AutoStartService;
                settingsContainer.Values["EnableNotifications"] = EnableNotifications;
                settingsContainer.Values["EnableNotificationSound"] = EnableNotificationSound;
                
                _logger.LogInformation("Settings saved using Windows ApplicationData API");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings using ApplicationData API");
                
                // Fallback to JSON file approach if ApplicationData fails
                try
                {
                    var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                        "AlUmmahNow", "VisualImporter", "settings.json");
                    
                    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                    
                    var settings = new
                    {
                        FileScanService = new { Url = FileScanServiceUrl },
                        ServiceInstallation = new
                        {
                            ExecutablePath = ServiceExecutablePath,
                            StartAutomatically = AutoStartService
                        },
                        Notifications = new
                        {
                            Enabled = EnableNotifications,
                            SoundEnabled = EnableNotificationSound
                        }
                    };
                    
                    var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                    
                    await File.WriteAllTextAsync(settingsPath, json);
                    
                    _logger.LogInformation("Settings saved to fallback JSON file: {SettingsPath}", settingsPath);
                    return true;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Fallback settings save also failed");
                    return false;
                }
            }
            finally
            {
                IsSaving = false;
            }
        }

        public void ResetToDefaults()
        {
            FileScanServiceUrl = "http://localhost:5000";
            DatabaseConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true";
            ServiceExecutablePath = "";
            AutoStartService = true;
            EnableNotifications = true;
            EnableNotificationSound = true;
            TestResults = "";
        }

        private void LoadSettings()
        {
            try
            {
                // Try to load from Windows ApplicationData first (native Windows 11 approach)
                try
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    var settingsContainer = localSettings.Containers.ContainsKey("AlUmmahNowSettings") 
                        ? localSettings.Containers["AlUmmahNowSettings"] 
                        : null;
                    
                    if (settingsContainer != null)
                    {
                        if (settingsContainer.Values.ContainsKey("FileScanServiceUrl"))
                            FileScanServiceUrl = settingsContainer.Values["FileScanServiceUrl"]?.ToString() ?? "http://localhost:5000";
                        
                        if (settingsContainer.Values.ContainsKey("ServiceExecutablePath"))
                            ServiceExecutablePath = settingsContainer.Values["ServiceExecutablePath"]?.ToString() ?? "";
                        
                        if (settingsContainer.Values.ContainsKey("AutoStartService"))
                            AutoStartService = (bool)(settingsContainer.Values["AutoStartService"] ?? true);

                        if (settingsContainer.Values.ContainsKey("EnableNotifications"))
                            EnableNotifications = (bool)(settingsContainer.Values["EnableNotifications"] ?? true);

                        if (settingsContainer.Values.ContainsKey("EnableNotificationSound"))
                            EnableNotificationSound = (bool)(settingsContainer.Values["EnableNotificationSound"] ?? true);

                        _logger.LogInformation("Settings loaded from Windows ApplicationData");
                        
                        // Load database connection from configuration (security sensitive)
                        DatabaseConnectionString = _configuration.GetConnectionString("DefaultConnection") ?? 
                            "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true";
                        return;
                    }
                }
                catch (Exception appDataEx)
                {
                    _logger.LogWarning(appDataEx, "Failed to load from ApplicationData, trying fallback methods");
                }
                
                // Fallback: Try to load from user settings file
                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                    "AlUmmahNow", "VisualImporter", "settings.json");
                
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
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

                    if (settings.TryGetProperty("Notifications", out var notifications))
                    {
                        if (notifications.TryGetProperty("Enabled", out var enabled))
                        {
                            EnableNotifications = enabled.GetBoolean();
                        }

                        if (notifications.TryGetProperty("SoundEnabled", out var soundEnabled))
                        {
                            EnableNotificationSound = soundEnabled.GetBoolean();
                        }
                    }

                    _logger.LogInformation("Settings loaded from JSON fallback file: {SettingsPath}", settingsPath);
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
                ResetToDefaults();
            }
        }

        private void DetectServicePaths()
        {
            try
            {
                DetectedServicePaths.Clear();

                // Check in current application directory
                var appDir = AppContext.BaseDirectory;
                var possiblePaths = new[]
                {
                    Path.Combine(appDir, "AlUmmahNow.FileScanService.exe"),
                    Path.Combine(Path.GetDirectoryName(appDir) ?? "", "AlUmmahNow.FileScanService", "AlUmmahNow.FileScanService.exe"),
                    Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(appDir) ?? "") ?? "", "AlUmmahNow.FileScanService", "bin", "Debug", "net8.0", "AlUmmahNow.FileScanService.exe"),
                    Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(appDir) ?? "") ?? "", "AlUmmahNow.FileScanService", "bin", "Release", "net8.0", "AlUmmahNow.FileScanService.exe")
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        DetectedServicePaths.Add(path);
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
                            var servicePath = Path.Combine(dir, "AlUmmahNow.FileScanService.exe");
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
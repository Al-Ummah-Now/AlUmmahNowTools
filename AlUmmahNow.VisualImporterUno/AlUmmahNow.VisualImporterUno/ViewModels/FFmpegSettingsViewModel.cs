using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AlUmmahNow.Data.Services;

namespace AlUmmahNow.VisualImporterUno.ViewModels
{
    public class FFmpegSettingsViewModel : INotifyPropertyChanged
    {
        private readonly ILogger<FFmpegSettingsViewModel> _logger;
        private readonly IFFmpegManagerService _ffmpegManager;

        private string _ffmpegPath = "";
        private string _ffprobePath = "";
        private bool _isVerifying = false;
        private bool _isAutoDetecting = false;
        private string _statusMessage = "";
        private string _ffmpegVersion = "";
        private bool _isInstalled = false;
        private bool _autoDetected = false;
        private string _installationInstructions = "";

        public FFmpegSettingsViewModel(
            ILogger<FFmpegSettingsViewModel> logger,
            IFFmpegManagerService ffmpegManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ffmpegManager = ffmpegManager ?? throw new ArgumentNullException(nameof(ffmpegManager));

            _installationInstructions = _ffmpegManager.GetInstallationInstructions();

            // Initialize with current paths
            _ffmpegPath = _ffmpegManager.FFmpegPath ?? "";
            _ffprobePath = _ffmpegManager.FFprobePath ?? "";

            // Verify installation on startup
            _ = VerifyInstallationAsync();
        }

        public string FFmpegPath
        {
            get => _ffmpegPath;
            set
            {
                if (SetProperty(ref _ffmpegPath, value))
                {
                    StatusMessage = "";
                }
            }
        }

        public string FFprobePath
        {
            get => _ffprobePath;
            set
            {
                if (SetProperty(ref _ffprobePath, value))
                {
                    StatusMessage = "";
                }
            }
        }

        public bool IsVerifying
        {
            get => _isVerifying;
            private set => SetProperty(ref _isVerifying, value);
        }

        public bool IsAutoDetecting
        {
            get => _isAutoDetecting;
            private set => SetProperty(ref _isAutoDetecting, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string FFmpegVersion
        {
            get => _ffmpegVersion;
            private set => SetProperty(ref _ffmpegVersion, value);
        }

        public bool IsInstalled
        {
            get => _isInstalled;
            private set => SetProperty(ref _isInstalled, value);
        }

        public bool AutoDetected
        {
            get => _autoDetected;
            private set => SetProperty(ref _autoDetected, value);
        }

        public string InstallationInstructions
        {
            get => _installationInstructions;
            private set => SetProperty(ref _installationInstructions, value);
        }

        public async Task VerifyInstallationAsync()
        {
            if (IsVerifying) return;

            IsVerifying = true;
            StatusMessage = "Verifying FFmpeg installation...";

            try
            {
                var status = await _ffmpegManager.VerifyFFmpegInstallationAsync();

                IsInstalled = status.IsFullyInstalled;
                AutoDetected = status.AutoDetected;
                FFmpegVersion = status.FFmpegVersion ?? "Not detected";

                if (status.IsFullyInstalled)
                {
                    StatusMessage = $"✓ FFmpeg is installed and working correctly";
                    FFmpegPath = status.FFmpegPath ?? "";
                    FFprobePath = status.FFprobePath ?? "";
                }
                else
                {
                    var messages = new System.Collections.Generic.List<string>();

                    if (!status.FFmpegExists)
                        messages.Add("FFmpeg executable not found");
                    else if (!status.FFmpegWorks)
                        messages.Add("FFmpeg found but not working");

                    if (!status.FFprobeExists)
                        messages.Add("FFprobe executable not found");
                    else if (!status.FFprobeWorks)
                        messages.Add("FFprobe found but not working");

                    if (!string.IsNullOrEmpty(status.ErrorMessage))
                        messages.Add($"Error: {status.ErrorMessage}");

                    StatusMessage = "✗ " + string.Join(", ", messages);
                }

                _logger.LogInformation("FFmpeg verification complete: {IsInstalled}", IsInstalled);
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ Error verifying installation: {ex.Message}";
                _logger.LogError(ex, "Error verifying FFmpeg installation");
                IsInstalled = false;
            }
            finally
            {
                IsVerifying = false;
            }
        }

        public async Task AutoDetectAsync()
        {
            if (IsAutoDetecting) return;

            IsAutoDetecting = true;
            StatusMessage = "Auto-detecting FFmpeg installation...";

            try
            {
                var detected = await _ffmpegManager.AutoDetectFFmpegAsync();

                if (detected)
                {
                    FFmpegPath = _ffmpegManager.FFmpegPath ?? "";
                    FFprobePath = _ffmpegManager.FFprobePath ?? "";
                    StatusMessage = "✓ FFmpeg auto-detected successfully";
                    AutoDetected = true;

                    // Verify the detected installation
                    await VerifyInstallationAsync();
                }
                else
                {
                    StatusMessage = "✗ Could not auto-detect FFmpeg. Please install it or specify the path manually.";
                    IsInstalled = false;
                }

                _logger.LogInformation("FFmpeg auto-detection result: {Detected}", detected);
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ Error during auto-detection: {ex.Message}";
                _logger.LogError(ex, "Error during FFmpeg auto-detection");
                IsInstalled = false;
            }
            finally
            {
                IsAutoDetecting = false;
            }
        }

        public async Task<bool> SavePathsAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(FFmpegPath) || string.IsNullOrWhiteSpace(FFprobePath))
                {
                    StatusMessage = "✗ Please provide both FFmpeg and FFprobe paths";
                    return false;
                }

                _ffmpegManager.SetFFmpegPaths(FFmpegPath, FFprobePath);

                // Verify the paths work
                await VerifyInstallationAsync();

                if (IsInstalled)
                {
                    // Save to application settings
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    var settingsContainer = localSettings.CreateContainer("FFmpegSettings",
                        Windows.Storage.ApplicationDataCreateDisposition.Always);

                    settingsContainer.Values["FFmpegPath"] = FFmpegPath;
                    settingsContainer.Values["FFprobePath"] = FFprobePath;

                    _logger.LogInformation("FFmpeg paths saved successfully");
                    return true;
                }
                else
                {
                    StatusMessage = "✗ The provided paths are not valid or not working";
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ Error saving paths: {ex.Message}";
                _logger.LogError(ex, "Error saving FFmpeg paths");
                return false;
            }
        }

        public void LoadSettings()
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var settingsContainer = localSettings.Containers.ContainsKey("FFmpegSettings")
                    ? localSettings.Containers["FFmpegSettings"]
                    : null;

                if (settingsContainer != null)
                {
                    if (settingsContainer.Values.ContainsKey("FFmpegPath"))
                        FFmpegPath = settingsContainer.Values["FFmpegPath"]?.ToString() ?? "";

                    if (settingsContainer.Values.ContainsKey("FFprobePath"))
                        FFprobePath = settingsContainer.Values["FFprobePath"]?.ToString() ?? "";

                    _logger.LogInformation("FFmpeg settings loaded from storage");

                    // Apply loaded paths to the manager
                    if (!string.IsNullOrEmpty(FFmpegPath) && !string.IsNullOrEmpty(FFprobePath))
                    {
                        _ffmpegManager.SetFFmpegPaths(FFmpegPath, FFprobePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading FFmpeg settings");
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

using AlUmmahNow.VisualImporterUno.Shell;
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
        private string _statusTitle = Loc.Get("Video_Checking");
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

            _installationInstructions = InstallCommandForThisComputer;

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

        /// <summary>One line on the state of the video programs; <see cref="StatusMessage"/> explains it.</summary>
        public string StatusTitle
        {
            get => _statusTitle;
            private set => SetProperty(ref _statusTitle, value);
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

        /// <summary>The one command that installs both programs on this kind of computer.</summary>
        public static string InstallCommandForThisComputer =>
            OperatingSystem.IsWindows() ? "winget install Gyan.FFmpeg" :
            OperatingSystem.IsMacOS() ? "brew install ffmpeg" : "sudo apt install ffmpeg";

        public string InstallationInstructions
        {
            get => _installationInstructions;
            private set => SetProperty(ref _installationInstructions, value);
        }

        public async Task VerifyInstallationAsync()
        {
            if (IsVerifying) return;

            IsVerifying = true;
            StatusTitle = Loc.Get("Video_Checking");
            StatusMessage = "";

            try
            {
                var status = await _ffmpegManager.VerifyFFmpegInstallationAsync();

                IsInstalled = status.IsFullyInstalled;
                AutoDetected = status.AutoDetected;
                FFmpegVersion = status.FFmpegVersion ?? "";

                if (status.IsFullyInstalled)
                {
                    // The path setters clear the message, so they go first
                    FFmpegPath = status.FFmpegPath ?? "";
                    FFprobePath = status.FFprobePath ?? "";
                    StatusTitle = Loc.Get("Video_ReadyTitle");
                    StatusMessage = Loc.Get("Video_ReadyBody");
                }
                else
                {
                    var lines = new System.Collections.Generic.List<string>();
                    if (!status.FFmpegExists) lines.Add(Loc.Get("Video_ConverterMissing"));
                    else if (!status.FFmpegWorks) lines.Add(Loc.Get("Video_ConverterBroken"));
                    if (!status.FFprobeExists) lines.Add(Loc.Get("Video_ReaderMissing"));
                    else if (!status.FFprobeWorks) lines.Add(Loc.Get("Video_ReaderBroken"));
                    StatusTitle = Loc.Get("Video_MissingTitle");
                    StatusMessage = Loc.WithDetail(string.Join("\n", lines), status.ErrorMessage);
                }

                _logger.LogInformation("FFmpeg verification complete: {IsInstalled}", IsInstalled);
            }
            catch (Exception ex)
            {
                StatusTitle = Loc.Get("Video_CheckFailed");
                StatusMessage = ex.Message;
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
            StatusTitle = Loc.Get("Video_Detecting");
            StatusMessage = "";

            try
            {
                var detected = await _ffmpegManager.AutoDetectFFmpegAsync();

                if (detected)
                {
                    FFmpegPath = _ffmpegManager.FFmpegPath ?? "";
                    FFprobePath = _ffmpegManager.FFprobePath ?? "";
                    AutoDetected = true;
                    await VerifyInstallationAsync();
                }
                else
                {
                    StatusTitle = Loc.Get("Video_MissingTitle");
                    StatusMessage = Loc.Get("Video_NotDetected");
                    IsInstalled = false;
                }

                _logger.LogInformation("FFmpeg auto-detection result: {Detected}", detected);
            }
            catch (Exception ex)
            {
                StatusTitle = Loc.Get("Video_CheckFailed");
                StatusMessage = ex.Message;
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
                    StatusMessage = Loc.Get("Video_NeedBoth");
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
                    StatusMessage = Loc.Get("Video_PathsInvalid");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = Loc.GetWithDetail("Video_SaveFailed", ex.Message);
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

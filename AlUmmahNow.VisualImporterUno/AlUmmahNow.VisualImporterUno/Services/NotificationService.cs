using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#if WINDOWS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
#endif

namespace AlUmmahNow.VisualImporterUno.Services;

/// <summary>
/// Interface for notification service that provides cross-platform notification support
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Gets or sets whether notifications are enabled
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether notification sounds are enabled
    /// </summary>
    bool IsSoundEnabled { get; set; }

    /// <summary>
    /// Show a success notification
    /// </summary>
    Task ShowSuccessAsync(string title, string message);

    /// <summary>
    /// Show a warning notification
    /// </summary>
    Task ShowWarningAsync(string title, string message);

    /// <summary>
    /// Show an error notification
    /// </summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Show an informational notification
    /// </summary>
    Task ShowInfoAsync(string title, string message);

    /// <summary>
    /// Show a notification when a file is detected
    /// </summary>
    Task ShowFileDetectedAsync(string fileName, string channelName, long fileSize);

    /// <summary>
    /// Show a notification when approval is processed
    /// </summary>
    Task ShowApprovalProcessedAsync(string fileName, bool approved, int count = 1);
}

/// <summary>
/// Cross-platform notification service implementation
/// Uses Windows ToastNotifications on Windows and ContentDialog fallback on other platforms
/// </summary>
public class NotificationService : INotificationService, IDisposable
{
    private readonly ILogger<NotificationService> _logger;
    private readonly ConcurrentQueue<NotificationItem> _notificationQueue = new();
    private readonly SemaphoreSlim _queueSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _disposed = false;
    private bool _isProcessing = false;

    // Notification throttling settings
    private readonly TimeSpan _minNotificationInterval = TimeSpan.FromSeconds(2);
    private DateTime _lastNotificationTime = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _notificationHistory = new();
    private readonly TimeSpan _duplicateThreshold = TimeSpan.FromSeconds(10);

    // Settings
    private bool _isEnabled = true;
    private bool _isSoundEnabled = true;

#if WINDOWS
    private AppNotificationManager? _notificationManager;
    private bool _isWindows11Supported = false;
#endif

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            InitializePlatformNotifications();
            LoadSettings();

            _logger.LogInformation("NotificationService initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize platform notifications, will use fallback");
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            SaveSettings();
        }
    }

    public bool IsSoundEnabled
    {
        get => _isSoundEnabled;
        set
        {
            _isSoundEnabled = value;
            SaveSettings();
        }
    }

    private void InitializePlatformNotifications()
    {
#if WINDOWS
        try
        {
            // Check if we're running on Windows 10/11
            var version = Environment.OSVersion.Version;
            _isWindows11Supported = version.Major >= 10;

            if (_isWindows11Supported)
            {
                // Initialize Windows App Notifications
                _notificationManager = AppNotificationManager.Default;
                _logger.LogInformation("Windows notification manager initialized");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Windows notification manager");
            _isWindows11Supported = false;
        }
#endif
    }

    private void LoadSettings()
    {
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var notificationSettings = localSettings.CreateContainer("NotificationSettings",
                Windows.Storage.ApplicationDataCreateDisposition.Always);

            if (notificationSettings.Values.ContainsKey("IsEnabled"))
            {
                _isEnabled = (bool)(notificationSettings.Values["IsEnabled"] ?? true);
            }

            if (notificationSettings.Values.ContainsKey("IsSoundEnabled"))
            {
                _isSoundEnabled = (bool)(notificationSettings.Values["IsSoundEnabled"] ?? true);
            }

            _logger.LogInformation("Notification settings loaded: Enabled={IsEnabled}, Sound={IsSoundEnabled}",
                _isEnabled, _isSoundEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load notification settings, using defaults");
        }
    }

    private void SaveSettings()
    {
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var notificationSettings = localSettings.CreateContainer("NotificationSettings",
                Windows.Storage.ApplicationDataCreateDisposition.Always);

            notificationSettings.Values["IsEnabled"] = _isEnabled;
            notificationSettings.Values["IsSoundEnabled"] = _isSoundEnabled;

            _logger.LogDebug("Notification settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save notification settings");
        }
    }

    public async Task ShowSuccessAsync(string title, string message)
    {
        await EnqueueNotificationAsync(new NotificationItem
        {
            Title = title,
            Message = message,
            Type = NotificationType.Success,
            Scenario = "success"
        });
    }

    public async Task ShowWarningAsync(string title, string message)
    {
        await EnqueueNotificationAsync(new NotificationItem
        {
            Title = title,
            Message = message,
            Type = NotificationType.Warning,
            Scenario = "warning"
        });
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        await EnqueueNotificationAsync(new NotificationItem
        {
            Title = title,
            Message = message,
            Type = NotificationType.Error,
            Scenario = "error"
        });
    }

    public async Task ShowInfoAsync(string title, string message)
    {
        await EnqueueNotificationAsync(new NotificationItem
        {
            Title = title,
            Message = message,
            Type = NotificationType.Info,
            Scenario = "info"
        });
    }

    public async Task ShowFileDetectedAsync(string fileName, string channelName, long fileSize)
    {
        var sizeText = FormatFileSize(fileSize);
        await EnqueueNotificationAsync(new NotificationItem
        {
            Title = "New Video Detected",
            Message = $"{fileName} in {channelName} ({sizeText})",
            Type = NotificationType.Info,
            Scenario = "fileDetected",
            GroupKey = $"file_{channelName}"
        });
    }

    public async Task ShowApprovalProcessedAsync(string fileName, bool approved, int count = 1)
    {
        var action = approved ? "Approved" : "Rejected";
        var title = count > 1 ? $"{action} {count} Files" : $"{action} File";
        var message = count > 1 ? $"{count} files have been {action.ToLower()}" : $"{fileName} has been {action.ToLower()}";

        await EnqueueNotificationAsync(new NotificationItem
        {
            Title = title,
            Message = message,
            Type = approved ? NotificationType.Success : NotificationType.Warning,
            Scenario = "approval"
        });
    }

    private async Task EnqueueNotificationAsync(NotificationItem notification)
    {
        if (!_isEnabled)
        {
            _logger.LogDebug("Notification suppressed (disabled): {Title}", notification.Title);
            return;
        }

        // Check for duplicate notifications
        var notificationKey = $"{notification.Title}_{notification.Message}";
        if (_notificationHistory.TryGetValue(notificationKey, out var lastTime))
        {
            if (DateTime.Now - lastTime < _duplicateThreshold)
            {
                _logger.LogDebug("Duplicate notification suppressed: {Title}", notification.Title);
                return;
            }
        }

        _notificationHistory[notificationKey] = DateTime.Now;

        // Clean up old history entries
        var oldEntries = _notificationHistory
            .Where(kvp => DateTime.Now - kvp.Value > _duplicateThreshold)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldEntries)
        {
            _notificationHistory.Remove(key);
        }

        _notificationQueue.Enqueue(notification);

        // Start processing queue if not already running
        if (!_isProcessing)
        {
            _ = Task.Run(ProcessNotificationQueueAsync);
        }
    }

    private async Task ProcessNotificationQueueAsync()
    {
        if (_isProcessing) return;

        await _queueSemaphore.WaitAsync(_cancellationTokenSource.Token);
        try
        {
            _isProcessing = true;

            while (_notificationQueue.TryDequeue(out var notification))
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                // Throttle notifications
                var timeSinceLastNotification = DateTime.Now - _lastNotificationTime;
                if (timeSinceLastNotification < _minNotificationInterval)
                {
                    var delay = _minNotificationInterval - timeSinceLastNotification;
                    await Task.Delay(delay, _cancellationTokenSource.Token);
                }

                await ShowNotificationAsync(notification);
                _lastNotificationTime = DateTime.Now;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Notification queue processing cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing notification queue");
        }
        finally
        {
            _isProcessing = false;
            _queueSemaphore.Release();
        }
    }

    private async Task ShowNotificationAsync(NotificationItem notification)
    {
        try
        {
#if WINDOWS
            if (_isWindows11Supported && _notificationManager != null)
            {
                await ShowWindowsToastNotificationAsync(notification);
                return;
            }
#endif
            // Fallback to ContentDialog on other platforms or if Windows notifications fail
            await ShowContentDialogNotificationAsync(notification);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show notification: {Title}", notification.Title);
        }
    }

#if WINDOWS
    private async Task ShowWindowsToastNotificationAsync(NotificationItem notification)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(notification.Title)
                .AddText(notification.Message);

            // Add audio based on settings
            if (_isSoundEnabled)
            {
                builder.SetAudioUri(new Uri("ms-winsoundevent:Notification.Default"));
            }
            else
            {
                builder.MuteSounds();
            }

            // Set scenario for grouping
            if (!string.IsNullOrEmpty(notification.GroupKey))
            {
                builder.SetGroup(notification.GroupKey);
            }

            var appNotification = builder.BuildNotification();

            if (_notificationManager != null)
            {
                await Task.Run(() => _notificationManager.Show(appNotification));
                _logger.LogDebug("Windows toast notification shown: {Title}", notification.Title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show Windows toast notification, falling back to ContentDialog");
            await ShowContentDialogNotificationAsync(notification);
        }
    }
#endif

    private async Task ShowContentDialogNotificationAsync(NotificationItem notification)
    {
        try
        {
            // Must be executed on UI thread
            var dispatcherQueue = App.Current.MainWindow?.DispatcherQueue;
            if (dispatcherQueue == null)
            {
                _logger.LogWarning("DispatcherQueue is not available");
                return;
            }

            var tcs = new TaskCompletionSource<bool>();

            dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var dialog = new ContentDialog
                    {
                        Title = GetIconForNotificationType(notification.Type) + " " + notification.Title,
                        Content = notification.Message,
                        CloseButtonText = "OK",
                        XamlRoot = App.Current.MainWindow?.Content?.XamlRoot
                    };

                    // Show dialog with timeout (auto-dismiss after 5 seconds for non-error notifications)
                    if (notification.Type != NotificationType.Error)
                    {
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        try
                        {
                            await dialog.ShowAsync().AsTask(cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Auto-dismissed, this is expected
                        }
                    }
                    else
                    {
                        await dialog.ShowAsync();
                    }

                    _logger.LogDebug("ContentDialog notification shown: {Title}", notification.Title);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to show ContentDialog notification");
                    tcs.TrySetException(ex);
                }
            });

            await tcs.Task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch ContentDialog notification to UI thread");
        }
    }

    private static string GetIconForNotificationType(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => "✓",
            NotificationType.Warning => "⚠",
            NotificationType.Error => "✗",
            NotificationType.Info => "ℹ",
            _ => "•"
        };
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cancellationTokenSource.Cancel();
            _queueSemaphore.Dispose();
            _cancellationTokenSource.Dispose();

#if WINDOWS
            _notificationManager = null;
#endif

            _logger.LogInformation("NotificationService disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during NotificationService disposal");
        }
    }
}

/// <summary>
/// Internal notification item for queueing
/// </summary>
internal class NotificationItem
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public string? GroupKey { get; set; }
}

/// <summary>
/// Notification type enumeration
/// </summary>
public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

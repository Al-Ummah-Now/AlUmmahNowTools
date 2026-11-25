using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Data.Services;
using AlUmmahNow.Importer;
using AlUmmahNow.Importer.Core;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.VisualImporterUno.Services.Logging;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Uno.Resizetizer;

namespace AlUmmahNow.VisualImporterUno;

public partial class App : Application
{
    private bool _isShuttingDown = false;

    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        try
        {
            this.InitializeComponent();

            // Initialize crash handling AFTER InitializeComponent
            CrashHandler.Initialize();

            // Set up global exception handler with logging
            SetupGlobalExceptionHandling();

            Log.Information("Application Initialized: AlUmmahNow Visual Importer (Uno) initialized successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App constructor failed: {ex}");
            Log.Fatal(ex, "App Constructor failed");
            throw;
        }
    }

    /// <summary>
    /// Set up global exception handling with comprehensive logging
    /// </summary>
    private void SetupGlobalExceptionHandling()
    {
        // Handle unhandled exceptions from current AppDomain
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            var isTerminating = args.IsTerminating;

            Log.Fatal(exception,
                "Unhandled exception in AppDomain. IsTerminating: {IsTerminating}",
                isTerminating);

            if (exception != null)
            {
                LogStructuredException(exception, "AppDomain unhandled exception");
                // To the Errors page in videos admin, once the signed-in import workspace exists (TOOLS-ADMIN TA-11)
                if (Shell.ImportWorkspace.IfStarted is { } workspace)
                {
                    workspace.Reporter.Report("unhandled", exception, exception.Message);
                    if (isTerminating) workspace.Reporter.FlushAsync().Wait(TimeSpan.FromSeconds(3));
                }
            }
        };

        // Handle unobserved task exceptions
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Log.Error(args.Exception,
                "Unobserved task exception. Observed: {Observed}",
                args.Observed);

            LogStructuredException(args.Exception, "Unobserved task exception");
            var inner = args.Exception.InnerException ?? args.Exception;
            Shell.ImportWorkspace.IfStarted?.Reporter.Report("rejection", inner, inner.Message);

            // Mark as observed to prevent process termination
            args.SetObserved();
        };
    }

    /// <summary>
    /// Log exception with structured properties for Splunk analysis
    /// </summary>
    private void LogStructuredException(Exception exception, string context)
    {
        var properties = new System.Collections.Generic.Dictionary<string, object>
        {
            ["Context"] = context,
            ["ExceptionType"] = exception.GetType().FullName ?? "Unknown",
            ["ExceptionMessage"] = exception.Message,
            ["StackTrace"] = exception.StackTrace ?? "No stack trace",
            ["MachineName"] = Environment.MachineName,
            ["UserName"] = Environment.UserName,
            ["OSVersion"] = Environment.OSVersion.ToString(),
            ["ProcessId"] = Environment.ProcessId,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (exception.InnerException != null)
        {
            properties["InnerExceptionType"] = exception.InnerException.GetType().FullName ?? "Unknown";
            properties["InnerExceptionMessage"] = exception.InnerException.Message;
        }

        var enrichedLogger = Log.Logger;
        foreach (var kvp in properties)
        {
            enrichedLogger = enrichedLogger.ForContext(kvp.Key, kvp.Value);
        }
        enrichedLogger.Fatal(exception, "Structured exception log: {Context}", context);
    }

    public Window? MainWindow { get; private set; }
    public IHost? Host { get; private set; }

    public static IServiceProvider Services => ((App)Current).Host?.Services
        ?? throw new InvalidOperationException("Services not initialized");

    // Add static reference to current instance
    public static new App Current => (App)Application.Current;

    /// <summary>
    /// Get a service from the DI container
    /// </summary>
    /// <typeparam name="T">Service type</typeparam>
    /// <returns>Service instance</returns>
    public static T GetService<T>() where T : class
    {
        return Services.GetRequiredService<T>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
#if WINDOWS
        // This target has a Main of its own (Program.cs is the desktop one's)
        if (!Shell.AppInstance.Start(Environment.GetCommandLineArgs()))
        {
            Environment.Exit(0);
            return;
        }
#endif
        if (Shell.AppInstance.AlreadyOpen || Shell.AppInstance.FinishingUpdate)
        {
            ShowInstanceNotice();
            return;
        }
        try
        {
            // Get the application directory
            var appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();

            // Configure log directory
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AlUmmahNow", "VisualImporterUno", "Logs");

            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "visual-importer-uno-.txt");

            // Enable Serilog self-diagnostics
            Serilog.Debugging.SelfLog.Enable(msg => System.Diagnostics.Debug.WriteLine($"Serilog: {msg}"));

            // Configure temporary logger before builder (will be replaced with full configuration)
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .WriteTo.Debug()
                .CreateLogger();

            Log.Information("Application starting with temporary logger configuration");

            // Not Application.CreateBuilder: it makes a window of its own, which macOS shows, empty, under the app's
            var builder = UnoHost.CreateDefaultBuilder();
            builder
#if DEBUG
                    // Switch to Development environment when running in DEBUG
                    .UseEnvironment(Environments.Development)
#endif
                    .UseLogging(configure: (context, logBuilder) =>
                    {
                        // Configure log levels for different categories of logging
                        logBuilder
                            .SetMinimumLevel(
                                context.HostingEnvironment.IsDevelopment() ?
                                    LogLevel.Information :
                                    LogLevel.Warning)
                            .CoreLogLevel(LogLevel.Warning);
                    }, enableUnoLogging: true)
                    .UseSerilog(consoleLoggingEnabled: true, fileLoggingEnabled: true)
                    .UseConfiguration(configure: configBuilder =>
                        configBuilder
                            .EmbeddedSource<App>()
                            .Section<AppConfig>()
                    )
                    .UseHttp((context, services) =>
                    {
#if DEBUG
                        // DelegatingHandler will be automatically injected
                        services.AddTransient<DelegatingHandler, DebugHttpHandler>();
#endif
                    })
                    .ConfigureServices((context, services) =>
                    {
                        var configuration = context.Configuration;

                        // Configure Splunk logging with full configuration
                        try
                        {
                            var appVersion = Assembly.GetExecutingAssembly()
                                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                                .InformationalVersion ?? "1.0.0";

                            Log.Logger = SplunkLoggerConfiguration.ConfigureSplunkLogger(
                                configuration,
                                "AlUmmahNow.VisualImporter",
                                appVersion);

                            Log.Information("Splunk logging configured successfully");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to configure Splunk logging, continuing with file logging only");
                        }

                        // Register LoggingService for dependency injection
                        services.AddSingleton<ILoggingService, LoggingService>();

                        // Register database with SQL Server - Use Scoped lifetime to prevent threading issues
                        services.AddDbContext<ImportDbContext>(options =>
                            options.UseSqlServer(configuration["ConnectionStrings:DefaultConnection"]
                                ?? "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true"),
                            ServiceLifetime.Scoped);

                        // Register storage clients (gracefully handle missing configuration)
                        services.AddSingleton<IR2Client>(sp =>
                        {
                            var r2Config = configuration.GetSection("R2DB");
                            if (string.IsNullOrWhiteSpace(r2Config["S3Api"])) return new UnconfiguredR2Client();
                            return new R2Client(
                                r2Config["S3Api"] ?? "",
                                r2Config["S3Key"] ?? "",
                                r2Config["S3Secret"] ?? "");
                        });

                        // Register FileScanServiceManager as singleton
                        var fileScanServiceUrl = configuration["FileScanService:Url"] ?? "http://localhost:5000";
                        services.AddSingleton<FileScanServiceManager>(sp =>
                        {
                            var logger = sp.GetRequiredService<ILogger<FileScanServiceManager>>();

                            // Try to load settings from user settings file first
                            string? serviceExecutablePath = null;
                            try
                            {
                                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "AlUmmahNow", "VisualImporterUno", "settings.json");

                                if (File.Exists(settingsPath))
                                {
                                    var json = File.ReadAllText(settingsPath);
                                    var settings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

                                    if (settings.TryGetProperty("FileScanService", out var fileScanService) &&
                                        fileScanService.TryGetProperty("Url", out var url))
                                    {
                                        fileScanServiceUrl = url.GetString() ?? fileScanServiceUrl;
                                    }

                                    if (settings.TryGetProperty("ServiceInstallation", out var serviceInstallation) &&
                                        serviceInstallation.TryGetProperty("ExecutablePath", out var execPath))
                                    {
                                        serviceExecutablePath = execPath.GetString();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Failed to load user settings, using configuration defaults");
                            }

                            // Fallback to configuration if not found in user settings
                            if (string.IsNullOrEmpty(serviceExecutablePath))
                            {
                                var serviceConfig = configuration.GetSection("ServiceInstallation");
                                serviceExecutablePath = serviceConfig["ExecutablePath"];
                            }

                            return new FileScanServiceManager(logger, fileScanServiceUrl, serviceExecutablePath);
                        });

                        // Register SignalR client for FileScan service
                        services.AddSingleton(sp =>
                        {
                            try
                            {
                                if (AlUmmahNow.VisualImporterUno.Shell.DemoScannerClient.Requested)
                                    return new AlUmmahNow.VisualImporterUno.Shell.DemoScannerClient(fileScanServiceUrl);
                                return new FileScanSignalRClient(fileScanServiceUrl);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Failed to create FileScanSignalRClient, creating placeholder");
                                return new FileScanSignalRClient(fileScanServiceUrl);
                            }
                        });

                        // Register repository
                        services.AddTransient<Repository<Video>>();

                        // Register file discovery service (local scanning)
                        services.AddTransient<FileDiscoveryService>();

                        // Register filter preset service
                        services.AddSingleton<IFilterPresetService, FilterPresetService>();

                        // Register notification service
                        services.AddSingleton<INotificationService, NotificationService>();

                        // Register video thumbnail service
                        services.AddSingleton<VideoThumbnailService>();

                        // Register view models with safe initialization
                        services.AddTransient<BulkImportViewModel>();
                        services.AddTransient<SocialSourcesViewModel>();
                        services.AddSingleton<ServiceStatusViewModel>();
                        services.AddTransient<SettingsViewModel>();

                        // Register Watch Management and Approval Workflow ViewModels as singletons
                        // (they manage SignalR connection and need to maintain state)
                        services.AddSingleton<WatchViewModel>();
                        services.AddSingleton<ApprovalViewModel>();
                        services.AddSingleton<AnalyticsDashboardViewModel>();

                        // Register FFmpeg services
                        services.AddSingleton<IFFmpegManagerService, FFmpegManagerService>();
                        services.AddTransient<FFmpegSettingsViewModel>();
                    });

            Host = builder.Build();

            Log.Information("App starting from directory: {Directory}", appDirectory);
            Log.Information("Logs will be written to: {LogPath}", logPath);

            // Initialize services
            InitializeServices();

            // The shell (wiki TOOLS-ADMIN TA-10): preferences, language and the sign-in session first
            var demo = Shell.ShellDemo.FromEnvironment();
            var shellState = Shell.ShellState.Start(AlUmmahNow.ToolsCore.ToolsPreferences.DefaultPath, demo);
            Shell.ShellState.ApplyLanguage(shellState.Preferences.Language);

            // Update checks (wiki AUN-APP-UPDATES §5): before the window, so every shell it builds shows the notice
#if WINDOWS
            Shell.AppUpdates.Start(shellState, new NoSelfUpdate());
#else
            // No root key, or Linux: no updater at all (wiki §4.1.1, APPUPD-D9)
            var selfUpdate = AlUmmahNow.ToolsCore.Updates.SelfUpdate.Supported(AlUmmahNow.ToolsCore.Updates.ToolsUpdateKeys.RootKeys.Length > 0, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
            // Only the copy that has the queue stages and installs updates: the package folder is one for all copies
            Shell.AppUpdates.Start(shellState, selfUpdate && Shell.AppInstance.OwnsQueue ? new Services.VelopackInstaller() : new NoSelfUpdate());
#endif

            MainWindow = new MainWindow(shellState);
            MainWindow.Activate();
            Shell.AppInstance.Attach(MainWindow);
            _ = demo != null ? demo.DriveAsync(shellState.Session) : shellState.Session.RestoreAsync();
            // The launch check closes the window by itself (docs/RELEASE.md): TOOLS_SMOKE_CLOSE=close|quit|terminate
            if (Environment.GetEnvironmentVariable("TOOLS_SMOKE_CLOSE") is { Length: > 0 } smokeClose) _ = Shell.QuitSmoke.RunAsync(MainWindow, smokeClose);
#if !WINDOWS
            if (Environment.GetEnvironmentVariable("TOOLS_SCREENSHOT") is { Length: > 0 } shot)
            {
                _ = Shell.ScreenshotRunner.RunAsync(MainWindow, shot);
            }
#endif

            Log.Information("Application Started: AlUmmahNow Visual Importer (Uno) application started successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to launch application");
            throw;
        }
    }

    /// <summary>Another copy has the queue and does not answer, or the updater is at work (Tools #89): only the window that says so, no services and no updates.</summary>
    private void ShowInstanceNotice()
    {
        var preferences = Shell.ShellDemo.FromEnvironment()?.Preferences ?? AlUmmahNow.ToolsCore.ToolsPreferences.Load(AlUmmahNow.ToolsCore.ToolsPreferences.DefaultPath);
        Shell.ShellState.ApplyLanguage(preferences.Language);
        MainWindow = new Shell.InstanceNoticeWindow(preferences, Shell.AppInstance.FinishingUpdate);
        MainWindow.Activate();
        Shell.AppInstance.Attach(MainWindow);
        if (Shell.AppInstance.FinishingUpdate && Environment.GetEnvironmentVariable("TOOLS_DEMO") != "1") Shell.AppInstance.StartAgainAfterUpdate(MainWindow);
#if !WINDOWS
        if (Environment.GetEnvironmentVariable("TOOLS_SCREENSHOT") is { Length: > 0 } shot) _ = Shell.ScreenshotRunner.RunAsync(MainWindow, shot);
#endif
    }

    private async void InitializeServices()
    {
        try
        {
            // Ensure database is created with proper error handling
            using var scope = Host?.Services?.CreateScope();
            if (scope != null)
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();

                // Test connection first
                try
                {
                    await dbContext.Database.CanConnectAsync();
                    await ImportDbSchema.EnsureUpToDateAsync(dbContext);
                    Log.Information("Database initialized successfully");
                }
                catch (Exception dbEx)
                {
                    Log.Warning(dbEx, "Database initialization failed, but application will continue");
                }
            }

            // Initialize and start the FileScan service if needed
            try
            {
                var serviceManager = Host?.Services?.GetRequiredService<FileScanServiceManager>();
                if (serviceManager != null)
                {
                    // Subscribe to status changes for logging
                    serviceManager.ServiceStatusChanged += (sender, e) =>
                    {
                        Log.Information("FileScan service status changed from {PreviousStatus} to {CurrentStatus}",
                            e.PreviousStatus, e.CurrentStatus);
                    };

                    // Ensure the service is running
                    var serviceStarted = await serviceManager.EnsureServiceRunningAsync();
                    if (serviceStarted)
                    {
                        Log.Information("FileScan service is running and ready");
                    }
                    else
                    {
                        Log.Warning("FileScan service could not be started - some features may not be available");
                    }
                }
            }
            catch (Exception serviceEx)
            {
                Log.Warning(serviceEx, "Failed to initialize FileScan service, but application will continue");
            }

            Log.Information("Services initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize services");
        }
    }

    /// <returns>The folder scanner does not run any more</returns>
    internal async Task<bool> ShutdownServicesAsync()
    {
        if (_isShuttingDown) return false;
        _isShuttingDown = true;
        var ended = true;

        try
        {
            Log.Information("Application shutdown initiated");

            // Stop managed FileScan service before app shutdown
            try
            {
                var serviceManager = Host?.Services?.GetService<FileScanServiceManager>();
                if (serviceManager != null)
                {
                    Log.Information("Stopping FileScan service...");

                    // One that is being started meanwhile does not start
                    serviceManager.Close();
                    ended = await serviceManager.StopServiceAsync(AlUmmahNow.ToolsCore.Import.EndLimits.ScannerAsked, AlUmmahNow.ToolsCore.Import.EndLimits.ScannerKilled);

                    Log.Information("FileScan service stopped: {Ended}", ended);
                    serviceManager.Dispose();
                }
            }
            catch (Exception serviceEx)
            {
                ended = false;
                Log.Warning(serviceEx, "Error stopping FileScan service");
            }

            // Dispose SignalR client
            try
            {
                var signalRClient = Host?.Services?.GetService<FileScanSignalRClient>();
                if (signalRClient != null)
                {
                    Log.Information("Disconnecting SignalR client...");
                    await signalRClient.DisposeAsync().AsTask().WaitAsync(AlUmmahNow.ToolsCore.Import.EndLimits.Connection);
                    Log.Information("SignalR client disconnected");
                }
            }
            catch (Exception signalREx)
            {
                Log.Warning(signalREx, "Error disposing SignalR client");
            }

            // The host is not disposed here: Uno's dispatcher and its logging keep using the host's logger factory until the
            // process ends, and disposing it under them aborts the process (ObjectDisposedException 'LoggerFactory', seen on Linux)

            Log.Information("Application shutdown completed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during application shutdown");
        }
        finally
        {
            try
            {
                // The log is closed as the process ends (AppQuit, Program): the hand-over comes after this
                CrashHandler.Cleanup();
            }
            catch (Exception cleanupEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error during final cleanup: {cleanupEx.Message}");
            }
        }
        return ended;
    }
}

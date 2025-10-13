using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Data.Services;
using AlUmmahNow.Importer;
using AlUmmahNow.Importer.Core;
using AlUmmahNow.VisualImporterUno.Services;
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

            Log.Information("Application Initialized: AlUmmahNow Visual Importer (Uno) initialized successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App constructor failed: {ex}");
            Log.Fatal(ex, "App Constructor failed");
            throw;
        }
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

            // Configure Serilog before builder
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .WriteTo.Debug()
                .CreateLogger();

            var builder = this.CreateBuilder(args)
                .Configure(host => host
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

                        // Register database with SQL Server - Use Scoped lifetime to prevent threading issues
                        services.AddDbContext<ImportDbContext>(options =>
                            options.UseSqlServer(configuration["ConnectionStrings:DefaultConnection"]
                                ?? "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true"),
                            ServiceLifetime.Scoped);

                        // Register storage clients (gracefully handle missing configuration)
                        services.AddSingleton<IR2Client>(sp =>
                        {
                            var r2Config = configuration.GetSection("R2DB");
                            return new R2Client(
                                r2Config["S3Api"] ?? "",
                                r2Config["S3Key"] ?? "",
                                r2Config["S3Secret"] ?? "");
                        });

                        services.AddSingleton<IStorageClient<Video>>(sp =>
                        {
                            var bunnyConfig = configuration.GetSection("BunnyVideo");
                            return new BunnyStorageClient<Video>(
                                bunnyConfig["Zone"] ?? "",
                                bunnyConfig["Key"] ?? "",
                                bunnyConfig["Region"] ?? "");
                        });

                        services.AddSingleton<IStorageClient<AlUmmahNow.Importer.Core.Image>>(sp =>
                        {
                            var bunnyConfig = configuration.GetSection("BunnyImage");
                            return new BunnyStorageClient<AlUmmahNow.Importer.Core.Image>(
                                bunnyConfig["Zone"] ?? "",
                                bunnyConfig["Key"] ?? "",
                                bunnyConfig["Region"] ?? "");
                        });

                        services.AddSingleton(sp =>
                        {
                            var bunnyStreamKey = configuration.GetSection("BunnyStream")["Key"] ?? "";
                            return new BunnyStreamClient(bunnyStreamKey);
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

                        // Register video importer (with all dependencies)
                        services.AddTransient<VideoImporter>();

                        // Register import service (uses FileDiscoveryService and VideoImporter)
                        services.AddTransient<ImportService>();

                        // Register filter preset service
                        services.AddSingleton<IFilterPresetService, FilterPresetService>();

                        // Register notification service
                        services.AddSingleton<INotificationService, NotificationService>();

                        // Register video thumbnail service
                        services.AddSingleton<VideoThumbnailService>();

                        // Register view models with safe initialization
                        services.AddTransient<MainViewModel>();
                        services.AddTransient<ServiceStatusViewModel>();
                        services.AddTransient<SettingsViewModel>();
                        services.AddTransient<DatabaseMainViewModel>();

                        // Register Watch Management and Approval Workflow ViewModels as singletons
                        // (they manage SignalR connection and need to maintain state)
                        services.AddSingleton<WatchViewModel>();
                        services.AddSingleton<ApprovalViewModel>();
                        services.AddSingleton<AnalyticsDashboardViewModel>();
                    })
                );

            // Don't use builder.Window, create our custom MainWindow instead
            // MainWindow = builder.Window;

#if DEBUG
            // MainWindow.UseStudio();
#endif
            // MainWindow.SetWindowIcon();

            Host = builder.Build();

            Log.Information("App starting from directory: {Directory}", appDirectory);
            Log.Information("Logs will be written to: {LogPath}", logPath);

            // Initialize services
            InitializeServices();

            // Create and activate our custom MainWindow
            MainWindow = new MainWindow();
            MainWindow.Activate();

            Log.Information("Application Started: AlUmmahNow Visual Importer (Uno) application started successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to launch application");
            throw;
        }
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
                    await dbContext.Database.EnsureCreatedAsync();
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

    private async Task ShutdownServicesAsync()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

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

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await serviceManager.StopServiceAsync().WaitAsync(cts.Token);

                    Log.Information("FileScan service stopped successfully");
                    serviceManager.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                Log.Warning("FileScan service stop operation timed out");
            }
            catch (Exception serviceEx)
            {
                Log.Warning(serviceEx, "Error stopping FileScan service");
            }

            // Dispose SignalR client
            try
            {
                var signalRClient = Host?.Services?.GetService<FileScanSignalRClient>();
                if (signalRClient != null)
                {
                    Log.Information("Disconnecting SignalR client...");
                    await signalRClient.DisposeAsync();
                    Log.Information("SignalR client disconnected");
                }
            }
            catch (Exception signalREx)
            {
                Log.Warning(signalREx, "Error disposing SignalR client");
            }

            // Dispose the host
            try
            {
                Log.Information("Disposing application host...");
                Host?.Dispose();
                Log.Information("Application host disposed");
            }
            catch (Exception hostEx)
            {
                Log.Warning(hostEx, "Error disposing application host");
            }

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
                CrashHandler.Cleanup();
                Log.CloseAndFlush();
            }
            catch (Exception cleanupEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error during final cleanup: {cleanupEx.Message}");
            }
        }
    }
}

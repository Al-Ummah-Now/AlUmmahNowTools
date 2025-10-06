using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Importer.Core;
using AlUmmahNow.VisualImporter.Services;
using AlUmmahNow.VisualImporter.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;
using System;
using System.IO;
using System.Reflection;
using AlUmmahNow.Importer;
using System.Threading.Tasks;

namespace AlUmmahNow.VisualImporter
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private readonly IHost _host;
        private bool _isShuttingDown = false;

        public static IServiceProvider Services => ((App)Current)._host.Services;

        // Add static reference to current instance
        public static new App Current => (App)Application.Current;
        
        // Add MainWindow property
        public Window? MainWindow { get; private set; }

        /// <summary>
        /// Get a service from the DI container
        /// </summary>
        /// <typeparam name="T">Service type</typeparam>
        /// <returns>Service instance</returns>
        public static T GetService<T>() where T : class
        {
            return Services.GetRequiredService<T>();
        }

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            try
            {
                // Initialize crash handling FIRST, before anything else
                CrashHandler.Initialize();
                WindowsEventLogger.LogStartup();

                this.InitializeComponent();

                // Get the application directory (for WinUI 3 apps)
                var appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();

                // Configure configuration
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(appDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddUserSecrets<App>()
                    .Build();

                // Configure Serilog
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration)
                    .CreateLogger();

                Log.Information("App starting from directory: {Directory}", appDirectory);

                // Build host with services
                _host = Host.CreateDefaultBuilder()
                    .UseSerilog()
                    .ConfigureServices((context, services) =>
                    {
                        // Register configuration
                        services.AddSingleton<IConfiguration>(configuration);

                        // Register database with SQL Server - Use Scoped lifetime to prevent threading issues
                        services.AddDbContext<ImportDbContext>(options =>
                            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection") 
                                ?? "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true"),
                            ServiceLifetime.Scoped); // Explicitly set Scoped lifetime

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

                        services.AddSingleton<IStorageClient<Image>>(sp =>
                        {
                            var bunnyConfig = configuration.GetSection("BunnyImage");
                            return new BunnyStorageClient<Image>(
                                bunnyConfig["Zone"] ?? "",
                                bunnyConfig["Key"] ?? "",
                                bunnyConfig["Region"] ?? "");
                        });

                        services.AddSingleton(sp =>
                        {
                            var bunnyStreamKey = configuration.GetSection("BunnyStream")["Key"] ?? "";
                            return new BunnyStreamClient(bunnyStreamKey);
                        });

                        // Register FileScanServiceManager as singleton - this manages the FileScan service lifecycle
                        var fileScanServiceUrl = configuration.GetValue<string>("FileScanService:Url") ?? "http://localhost:5000";
                        services.AddSingleton<FileScanServiceManager>(sp =>
                        {
                            var logger = sp.GetRequiredService<ILogger<FileScanServiceManager>>();
                            
                            // Try to load settings from user settings file first
                            string? serviceExecutablePath = null;
                            try
                            {
                                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                                    "AlUmmahNow", "VisualImporter", "settings.json");
                                
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

                        // Register SignalR client for FileScan service (with safe initialization)
                        services.AddSingleton(sp => 
                        {
                            try
                            {
                                return new FileScanSignalRClient(fileScanServiceUrl);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Failed to create FileScanSignalRClient, creating placeholder");
                                // Return a basic instance that will fail gracefully
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
                        
                        // Register view models with safe initialization
                        services.AddTransient<MainViewModel>();
                        services.AddTransient<ServiceStatusViewModel>();
                        services.AddTransient<SettingsViewModel>();
                        
                        // Register logging
                        services.AddLogging(builder =>
                        {
                            builder.ClearProviders();
                            builder.AddSerilog();
                        });
                    })
                    .Build();

                // Initialize services
                InitializeServices();

                Log.Information("App constructor completed successfully");
            }
            catch (Exception ex)
            {
                // Log the exception details
                System.Diagnostics.Debug.WriteLine($"App constructor failed: {ex}");
                
                try
                {
                    WindowsEventLogger.LogCriticalCrash(ex, "App Constructor");
                }
                catch
                {
                    // If even logging fails, write to debug output
                    System.Diagnostics.Debug.WriteLine($"Failed to log crash: {ex}");
                }
                
                // Don't throw here to prevent total crash, but ensure the app can still start
                throw;
            }
        }

        private async void InitializeServices()
        {
            try
            {
                // Ensure database is created with proper error handling
                using var scope = _host.Services.CreateScope();
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
                    // Don't throw - let the app continue without database functionality
                }

                // Initialize and start the FileScan service if needed
                try
                {
                    var serviceManager = _host.Services.GetRequiredService<FileScanServiceManager>();
                    
                    // Subscribe to status changes for logging
                    serviceManager.ServiceStatusChanged += (sender, e) =>
                    {
                        Log.Information("FileScan service status changed from {PreviousStatus} to {CurrentStatus}", 
                            e.PreviousStatus, e.CurrentStatus);
                    };

                    // Ensure the service is running (this will start it if it's not already running)
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
                catch (Exception serviceEx)
                {
                    Log.Warning(serviceEx, "Failed to initialize FileScan service, but application will continue");
                    // Don't throw - let the app start without the service
                }

                Log.Information("Services initialized successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize services");
                // Don't throw - let the app start in degraded mode
            }
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1000,
                    "Application Started", "AlUmmahNow Visual Importer application started successfully");

                MainWindow = new MainWindow();
                MainWindow.Activate();
                
                // Subscribe to the window closed event for graceful shutdown
                MainWindow.Closed += MainWindow_Closed;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to launch application");
                throw;
            }
        }

        private async void MainWindow_Closed(object sender, WindowEventArgs e)
        {
            try
            {
                Log.Information("MainWindow closing - initiating app shutdown");
                
                // Prevent the app from continuing if shutdown is already in progress
                if (_isShuttingDown)
                {
                    return;
                }

                // Don't prevent the window from closing, but start our shutdown process
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ShutdownServicesAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error during deferred shutdown");
                        System.Diagnostics.Debug.WriteLine($"Error during deferred shutdown: {ex.Message}");
                    }
                    finally
                    {
                        // Force exit the application after services are shutdown
                        System.Environment.Exit(0);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in MainWindow_Closed handler");
                System.Diagnostics.Debug.WriteLine($"Error in MainWindow_Closed handler: {ex.Message}");
            }
        }

        private async Task ShutdownServicesAsync()
        {
            if (_isShuttingDown) return; // Prevent multiple shutdown attempts
            _isShuttingDown = true;

            try
            {
                Log.Information("Application shutdown initiated");

                // Stop managed FileScan service before app shutdown
                try
                {
                    var serviceManager = _host?.Services?.GetService<FileScanServiceManager>();
                    if (serviceManager != null)
                    {
                        Log.Information("Stopping FileScan service...");
                        
                        // Use a cancellation token with timeout
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
                    var signalRClient = _host?.Services?.GetService<FileScanSignalRClient>();
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
                    _host?.Dispose();
                    Log.Information("Application host disposed");
                }
                catch (Exception hostEx)
                {
                    Log.Warning(hostEx, "Error disposing application host");
                }

                WindowsEventLogger.LogShutdown(true);
                Log.Information("Application shutdown completed successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during application shutdown");
                WindowsEventLogger.LogShutdown(false);
            }
            finally
            {
                try
                {
                    CrashHandler.Cleanup();
                    WindowsEventLogger.Dispose();
                    Log.CloseAndFlush();
                }
                catch (Exception cleanupEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during final cleanup: {cleanupEx.Message}");
                }
            }
        }
    }
}

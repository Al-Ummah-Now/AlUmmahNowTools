using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Importer;
using AlUmmahNow.Importer.Core;
using AlUmmahNow.VisualImporter.Services;
using AlUmmahNow.VisualImporter.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AlUmmahNow.VisualImporter
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private static IHost? _host;
        
        public static IHost Host => _host ?? throw new InvalidOperationException("Host is not initialized");
        
        public static T GetService<T>() where T : class => Host.Services.GetRequiredService<T>();

        // Add static reference to current instance
        public static new App Current => (App)Application.Current;
        
        // Add MainWindow property
        public Window? MainWindow { get; private set; }

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

                InitializeComponent();
                InitializeServices();
                
                // Handle application exit gracefully - use UnhandledException instead of Exit
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "App Constructor");
                // Don't throw here to prevent total crash
            }
        }

        private void OnProcessExit(object? sender, EventArgs e)
        {
            try
            {
                WindowsEventLogger.LogShutdown(true);
                CrashHandler.Cleanup();
                WindowsEventLogger.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during application exit: {ex.Message}");
            }
        }

        private void InitializeServices()
        {
            try
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1100,
                    "Service Initialization Started", "Beginning service registration and configuration");

                var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

                // Configuration
                try
                {
                    builder.Configuration.AddUserSecrets<App>();
                    WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1101,
                        "User Secrets Loaded", "User secrets configuration loaded successfully");
                }
                catch (Exception ex)
                {
                    WindowsEventLogger.LogServiceInitializationFailure("User Secrets", ex);
                    // Continue without user secrets - use defaults
                }

                // Database
                var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNowVisualImporter");
                Directory.CreateDirectory(appDataFolder);
                var dbPath = Path.Combine(appDataFolder, "imports.db");
                
                try
                {
                    builder.Services.AddDbContext<ImportDbContext>(options =>
                        options.UseSqlite($"Data Source={dbPath}"));
                    
                    WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1102,
                        "Database Context Registered", $"SQLite database configured at: {dbPath}");
                }
                catch (Exception ex)
                {
                    WindowsEventLogger.LogServiceInitializationFailure("Database Context", ex);
                }

                // R2Client - non-generic with safe configuration
                builder.Services.AddScoped<IR2Client>(provider =>
                {
                    try
                    {
                        var config = provider.GetRequiredService<IConfiguration>();
                        var r2Config = config.GetSection("R2DB");
                        var client = new R2Client(
                            r2Config["S3Api"] ?? "https://your-r2-endpoint.com",
                            r2Config["S3Key"] ?? "your-r2-key",
                            r2Config["S3Secret"] ?? "your-r2-secret"
                        );
                        
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1103,
                            "R2Client Configured", "R2 storage client configured successfully");
                        return client;
                    }
                    catch (Exception ex)
                    {
                        WindowsEventLogger.LogServiceInitializationFailure("R2Client", ex);
                        // Return a default instance
                        return new R2Client("https://default.com", "default", "default");
                    }
                });

                // Storage clients - proper generic usage with safe configuration
                builder.Services.AddScoped<IStorageClient<Video>>(provider =>
                {
                    try
                    {
                        var config = provider.GetRequiredService<IConfiguration>();
                        var r2Config = config.GetSection("BunnyVideo");
                        var client = new BunnyStorageClient<Video>(
                            r2Config["Zone"] ?? "your-bunny-zone",
                            r2Config["Key"] ?? "your-bunny-key",
                            r2Config["Region"] ?? "your-bunny-region"
                        );
                        
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1104,
                            "Video Storage Client Configured", "Bunny video storage client configured successfully");
                        return client;
                    }
                    catch (Exception ex)
                    {
                        WindowsEventLogger.LogServiceInitializationFailure("Video Storage Client", ex);
                        // Return a default instance
                        return new BunnyStorageClient<Video>("default", "default", "default");
                    }
                });

                builder.Services.AddScoped<IStorageClient<Image>>(provider =>
                {
                    try
                    {
                        var config = provider.GetRequiredService<IConfiguration>();
                        var bunnyConfig = config.GetSection("BunnyImage");
                        var client = new BunnyStorageClient<Image>(
                            bunnyConfig["Zone"] ?? "your-bunny-zone",
                            bunnyConfig["Key"] ?? "your-bunny-key",
                            bunnyConfig["Region"] ?? "your-bunny-region"
                        );
                        
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1105,
                            "Image Storage Client Configured", "Bunny image storage client configured successfully");
                        return client;
                    }
                    catch (Exception ex)
                    {
                        WindowsEventLogger.LogServiceInitializationFailure("Image Storage Client", ex);
                        // Return a default instance
                        return new BunnyStorageClient<Image>("default", "default", "default");
                    }
                });

                builder.Services.AddScoped<BunnyStreamClient>(provider =>
                {
                    try
                    {
                        var config = provider.GetRequiredService<IConfiguration>();
                        var bunnyStreamConfig = config.GetSection("BunnyStream");
                        var client = new BunnyStreamClient(
                            bunnyStreamConfig["Key"] ?? "your-bunny-stream-key"
                        );
                        
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1106,
                            "Bunny Stream Client Configured", "Bunny stream client configured successfully");
                        return client;
                    }
                    catch (Exception ex)
                    {
                        WindowsEventLogger.LogServiceInitializationFailure("Bunny Stream Client", ex);
                        // Return a default instance
                        return new BunnyStreamClient("default");
                    }
                });

                // Repository - takes only IR2Client
                builder.Services.AddScoped<Repository<Video>>(provider =>
                {
                    try
                    {
                        var r2Client = provider.GetRequiredService<IR2Client>();
                        var repo = new Repository<Video>(r2Client);
                        
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1107,
                            "Video Repository Configured", "Video repository configured successfully");
                        return repo;
                    }
                    catch (Exception ex)
                    {
                        WindowsEventLogger.LogServiceInitializationFailure("Video Repository", ex);
                        throw; // Repository is critical
                    }
                });

                // Main importer service from the new library
                builder.Services.AddScoped<VideoImporter>();
                
                // Application services
                builder.Services.AddSingleton<FileDiscoveryService>();
                builder.Services.AddSingleton<ImportService>();
                
                // View models
                builder.Services.AddTransient<MainViewModel>();

                // Build the host
                _host = builder.Build();

                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1108,
                    "Service Host Built Successfully", "All services registered and host created successfully");

                // Initialize database in a background task to avoid blocking startup
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _host.Services.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
                        await dbContext.Database.EnsureCreatedAsync();
                        
                        WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1109,
                            "Database Initialized Successfully", $"SQLite database created/verified at: {dbPath}");
                    }
                    catch (Exception ex)
                    {
                        WindowsEventLogger.LogServiceInitializationFailure("Database Initialization", ex);
                        // Database failure is not critical for basic app functionality
                    }
                });
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "Service Initialization");
                
                // Continue without services - the app will handle this gracefully
                // Create a minimal host for error scenarios
                try
                {
                    var fallbackBuilder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
                    fallbackBuilder.Services.AddSingleton<FileDiscoveryService>();
                    _host = fallbackBuilder.Build();
                    
                    WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 1110,
                        "Fallback Host Created", "Minimal host created after service initialization failure");
                }
                catch (Exception fallbackEx)
                {
                    WindowsEventLogger.LogCriticalCrash(fallbackEx, "Fallback Host Creation");
                }
            }
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1200,
                    "Application Launch Started", "OnLaunched event received, creating main window");

                MainWindow = new MainWindow();
                MainWindow.Activate();
                
                WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Information, 1201,
                    "Main Window Created Successfully", "Main window created and activated successfully");
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "Main Window Creation");
                
                // Try to create a minimal window to show error state
                try
                {
                    MainWindow = new Window();
                    MainWindow.Title = "AlUmmah Now Visual Importer - Error";
                    MainWindow.Activate();
                    
                    WindowsEventLogger.LogEvent(System.Diagnostics.EventLogEntryType.Warning, 1202,
                        "Fallback Window Created", "Minimal error window created after main window creation failure");
                }
                catch (Exception fallbackEx)
                {
                    WindowsEventLogger.LogCriticalCrash(fallbackEx, "Fallback Window Creation");
                    throw; // If we can't even create a basic window, we need to crash
                }
            }
        }
    }
}

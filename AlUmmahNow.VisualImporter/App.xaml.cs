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

namespace AlUmmahNow.VisualImporter
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private readonly IHost _host;

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

                        // Register database with SQL Server
                        services.AddDbContext<ImportDbContext>(options =>
                            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection") 
                                ?? "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true"));

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

                        // Register repository
                        services.AddTransient<Repository<Video>>();

                        // Register file discovery service (local scanning)
                        services.AddTransient<FileDiscoveryService>();
                        
                        // Register video importer (with all dependencies)
                        services.AddTransient<VideoImporter>();
                        
                        // Register import service (uses FileDiscoveryService and VideoImporter)
                        services.AddTransient<ImportService>();
                        
                        // Register view models
                        services.AddTransient<MainViewModel>();

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

        private async void InitializeServices()
        {
            try
            {
                // Ensure database is created
                using var scope = _host.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
                await dbContext.Database.EnsureCreatedAsync();

                Log.Information("Services initialized successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize services");
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
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to launch application");
                throw;
            }
        }
    }
}

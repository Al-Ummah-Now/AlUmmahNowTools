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
using AlUmmahNow.VisualImporter.Core;
using AlUmmahNow.VisualImporter.Services;
using AlUmmahNow.VisualImporter.ViewModels;
using System;
using System.IO;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

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
            InitializeComponent();
            InitializeServices();
        }

        private void InitializeServices()
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

            // Configuration
            builder.Configuration.AddUserSecrets<App>();

            // Database
            var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNowVisualImporter");
            Directory.CreateDirectory(appDataFolder);
            var dbPath = Path.Combine(appDataFolder, "imports.db");
            
            builder.Services.AddDbContext<ImportDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));

            // R2Client - non-generic
            builder.Services.AddScoped<IR2Client>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var r2Config = config.GetSection("R2DB");
                return new R2Client(
                    r2Config["S3Api"] ?? "https://your-r2-endpoint.com",
                    r2Config["S3Key"] ?? "your-r2-key",
                    r2Config["S3Secret"] ?? "your-r2-secret"
                );
            });

            // Storage clients - proper generic usage
            builder.Services.AddScoped<IStorageClient<Video>>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var r2Config = config.GetSection("BunnyVideo");
                return new BunnyStorageClient<Video>(
                    r2Config["Zone"] ?? "your-bunny-zone",
                    r2Config["Key"] ?? "your-bunny-key",
                    r2Config["Region"] ?? "your-bunny-region"
                );
            });

            builder.Services.AddScoped<IStorageClient<Image>>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var bunnyConfig = config.GetSection("BunnyImage");
                return new BunnyStorageClient<Image>(
                    bunnyConfig["Zone"] ?? "your-bunny-zone",
                    bunnyConfig["Key"] ?? "your-bunny-key",
                    bunnyConfig["Region"] ?? "your-bunny-region"
                );
            });

            builder.Services.AddScoped<BunnyStreamClient>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var bunnyStreamConfig = config.GetSection("BunnyStream");
                return new BunnyStreamClient(
                    bunnyStreamConfig["Key"] ?? "your-bunny-stream-key"
                );
            });

            // Repository - takes only IR2Client
            builder.Services.AddScoped<Repository<Video>>(provider =>
            {
                var r2Client = provider.GetRequiredService<IR2Client>();
                return new Repository<Video>(r2Client);
            });

            // Main importer service
            builder.Services.AddScoped<Importer>();
            
            // Application services
            builder.Services.AddSingleton<ImportService>();
            
            // View models
            builder.Services.AddTransient<MainViewModel>();

            _host = builder.Build();

            // Initialize database
            using var scope = _host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
            dbContext.Database.EnsureCreated();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
    }
}

using AlUmmahNow.Data.Database;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.FileScanService.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using AlUmmahNow.Data.Models.FileScanning;
using System.Linq;

namespace AlUmmahNow.FileScanService
{
    public class Program
    {
        private static IServiceProvider? _serviceProvider;
        private static ILogger<Program>? _logger;
        private static SingletonManager? _singletonManager;

        public static async Task<int> Main(string[] args)
        {
            // Configure Serilog for both console and web hosting
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/filescan-service-.txt", 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            try
            {
                Log.Information("Starting AlUmmahNow File Scan Service - Hybrid Mode");
                
                // Check for console-only commands first
                if (args.Length > 0 && await TryHandleConsoleCommand(args))
                {
                    return 0;
                }

                // Default: Run as SignalR Hub Host with back2ground workers
                return await RunAsSignalRServiceAsync(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Service terminated unexpectedly");
                return 1;
            }
            finally
            {
                _singletonManager?.Dispose();
                Log.CloseAndFlush();
            }
        }

        private static async Task<bool> TryHandleConsoleCommand(string[] args)
        {
            // Only handle console commands that don't require the full web host
            var consoleCommands = new[] { "--help", "-h", "--list-sessions", "--status", "--singleton-info" };
            
            if (args.Length == 0 || !consoleCommands.Any(cmd => args.Contains(cmd)))
            {
                return false; // Not a console-only command
            }

            // Setup minimal services for console commands
            SetupMinimalServices();
            
            if (_serviceProvider == null || _logger == null)
            {
                Console.WriteLine("Service provider not initialized");
                return true;
            }

            try
            {
                // Parse command line arguments manually
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--help":
                        case "-h":
                            ShowHelp();
                            return true;

                        case "--list-sessions":
                            await HandleListSessionsAsync();
                            return true;

                        case "--status":
                            if (i + 1 < args.Length && Guid.TryParse(args[i + 1], out var sessionId))
                            {
                                await HandleStatusRequestAsync(sessionId);
                                return true;
                            }
                            Console.WriteLine("Error: --status requires a valid session ID");
                            return true;

                        case "--singleton-info":
                            HandleSingletonInfoRequest();
                            return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return true;
            }
        }

        private static async Task<int> RunAsSignalRServiceAsync(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Get the port from configuration
            var port = builder.Configuration.GetValue<int?>("Kestrel:Endpoints:Http:Url") ?? 5000;
            if (builder.Configuration["ASPNETCORE_URLS"] != null)
            {
                // Try to extract port from ASPNETCORE_URLS
                var urls = builder.Configuration["ASPNETCORE_URLS"];
                if (Uri.TryCreate(urls?.Split(';')[0], UriKind.Absolute, out var uri))
                {
                    port = uri.Port;
                }
            }

            // Initialize and check singleton status
            var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
            var singletonLogger = loggerFactory.CreateLogger<SingletonManager>();

            _singletonManager = new SingletonManager(singletonLogger, port);

            Log.Information("Checking singleton status for FileScanService on port {Port}", port);

            if (_singletonManager.IsAnotherInstanceRunning())
            {
                Log.Warning("Another instance of FileScanService is already running on port {Port}", port);
                Console.WriteLine("⚠️  Another instance of FileScanService is already running!");
                Console.WriteLine($"   Port: {port}");
                Console.WriteLine();
                Console.WriteLine("To stop the running instance, terminate its process or use the UI's service manager.");

                return 2; // Exit code 2 indicates another instance is running
            }

            Log.Information("No other instance detected - starting FileScanService as SignalR Hub Host");
            Console.WriteLine("✓ Starting FileScanService...");

            // Configure Serilog
            builder.Host.UseSerilog();

            // Register singleton manager in DI container
            builder.Services.AddSingleton(_singletonManager);

            // Add SignalR
            builder.Services.AddSignalR();

            // Configure database with SQL Server
            builder.Services.AddDbContext<ImportDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection") 
                    ?? "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true"));

            // Register services
            builder.Services.AddScoped<IFileScanEngine, FileScanEngine>();
            builder.Services.AddScoped<IHashCalculator, HashCalculator>();
            builder.Services.AddScoped<IMetadataExtractor, MetadataExtractor>();
            // One watcher registry per process: a singleton hosted service that restores active watches at
            // startup and outlives hub calls (it opens a DI scope per operation and per file event).
            builder.Services.AddSingleton<FileWatchService>();
            builder.Services.AddSingleton<IFileWatchService>(sp => sp.GetRequiredService<FileWatchService>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<FileWatchService>());
            builder.Services.AddScoped<IApprovalService, ApprovalService>();

            // Register background services
            builder.Services.AddHostedService<FileScanWorker>();
            builder.Services.AddHostedService<ImportWorker>();

            // Configure CORS for SignalR clients
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.SetIsOriginAllowed(origin =>
                          {
                              // Allow any localhost origin in development
                              if (string.IsNullOrEmpty(origin)) return false;
                              var uri = new Uri(origin);
                              return uri.Host == "localhost" || uri.Host == "127.0.0.1";
                          })
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            var app = builder.Build();

            // Bring the shared database up to the current schema (creates it if missing; adds indexes
            // introduced since it was created). The workers rely on these constraints.
            try
            {
                using var schemaScope = app.Services.CreateScope();
                await ImportDbSchema.EnsureUpToDateAsync(schemaScope.ServiceProvider.GetRequiredService<ImportDbContext>());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Database schema upgrade failed");
                return 1;
            }

            // Enable CORS
            app.UseCors();

            // Map SignalR hub - this is the KEY component for UI communication
            app.MapHub<FileScanHub>("/scanHub");

            // Add a simple health check endpoint
            app.MapGet("/health", () => Results.Ok(new {
                status = "healthy",
                service = "FileScanService",
                signalRHub = "/scanHub",
                timestamp = DateTime.UtcNow,
                processId = Environment.ProcessId,
                port = port
            }));

            // Add singleton status endpoint
            app.MapGet("/singleton", () => 
            {
                var lockInfo = _singletonManager?.GetLockInfo();
                return Results.Ok(new {
                    singleton = true,
                    lockInfo = lockInfo,
                    service = "FileScanService",
                    timestamp = DateTime.UtcNow
                });
            });

            // Add console help endpoint
            app.MapGet("/", () => Results.Ok(new { 
                message = "AlUmmahNow File Scan Service - SignalR Hub Host",
                signalRHub = "/scanHub",
                health = "/health",
                singleton = "/singleton",
                consoleCommands = new {
                    help = "Add --help argument when starting",
                    listSessions = "Add --list-sessions argument when starting",
                    status = "Add --status <session-id> argument when starting",
                    singletonInfo = "Add --singleton-info argument for singleton status"
                }
            }));

            Log.Information("FileScanService SignalR Hub available at /scanHub on port {Port}", port);
            Console.WriteLine($"?? FileScanService running on port {port}");
            Console.WriteLine($"   SignalR Hub: http://localhost:{port}/scanHub");
            Console.WriteLine($"   Health Check: http://localhost:{port}/health");
            Console.WriteLine($"   Singleton Info: http://localhost:{port}/singleton");
            
            try
            {
                await app.RunAsync();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to start SignalR service");
                return 1;
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("AlUmmahNow File Scan Service - SignalR Hub Host with Singleton Management");
            Console.WriteLine();
            Console.WriteLine("Default mode: Runs as SignalR Hub Host on http://localhost:5000");
            Console.WriteLine("SignalR Hub available at: /scanHub");
            Console.WriteLine();
            Console.WriteLine("?? Singleton Behavior:");
            Console.WriteLine("  - Only one instance can run per port");
            Console.WriteLine("  - Uses global mutex + file lock + port binding for enforcement");
            Console.WriteLine("  - Exit codes: 0=success, 1=error, 2=another instance running, 3=lock failed");
            Console.WriteLine();
            Console.WriteLine("Console commands (when used as arguments):");
            Console.WriteLine("  -h, --help                    Show this help information");
            Console.WriteLine("  --list-sessions               List all scan sessions");
            Console.WriteLine("  --status <SESSION_ID>         Get status of a specific scan session");
            Console.WriteLine("  --singleton-info              Show singleton lock information");
            Console.WriteLine();
            Console.WriteLine("HTTP Endpoints:");
            Console.WriteLine("  GET /                         Service information");
            Console.WriteLine("  GET /health                   Health check with singleton status");
            Console.WriteLine("  GET /singleton                Detailed singleton information");
            Console.WriteLine("  WS  /scanHub                  SignalR hub for real-time communication");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  FileScanService.exe                                      # Run as SignalR service (default)");
            Console.WriteLine("  FileScanService.exe --help                               # Show this help");
            Console.WriteLine("  FileScanService.exe --singleton-info                    # Check singleton status");
            Console.WriteLine("  FileScanService.exe --list-sessions                     # List sessions");
            Console.WriteLine("  FileScanService.exe --status 12345678-1234-1234-...    # Check status");
            Console.WriteLine();
            Console.WriteLine("For file scanning operations, use SignalR clients:");
            Console.WriteLine("  - AlUmmahNow.DataImporter (SignalR client)");
            Console.WriteLine("  - AlUmmahNow.VisualImporter (SignalR client with service management)");
        }

        private static void HandleSingletonInfoRequest()
        {
            var port = 5000; // Default port
            var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
            var logger = loggerFactory.CreateLogger<SingletonManager>();

            using var singletonManager = new SingletonManager(logger, port);

            var lockInfo = singletonManager.GetLockInfo();
            var isRunning = singletonManager.IsAnotherInstanceRunning();

            Console.WriteLine("AlUmmahNow FileScanService - Singleton Status");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine();
            Console.WriteLine($"Service Port: {lockInfo.Port}");
            Console.WriteLine($"Port In Use: {lockInfo.IsPortInUse}");
            Console.WriteLine($"Another Instance Running: {(isRunning ? "YES" : "NO")}");
            Console.WriteLine();

            if (isRunning)
            {
                Console.WriteLine("Port is currently in use by another service instance.");
                Console.WriteLine();
                Console.WriteLine("Current Process Info:");
                Console.WriteLine($"  Process ID: {lockInfo.CurrentProcessId}");
                Console.WriteLine($"  Process Name: {lockInfo.CurrentProcessName}");
                Console.WriteLine($"  Machine: {lockInfo.MachineName}");
                Console.WriteLine();
                Console.WriteLine("To stop the running instance:");
                Console.WriteLine("  - Use the VisualImporter's service management UI");
                Console.WriteLine("  - Use task manager to find and end the FileScanService process");
            }
            else
            {
                Console.WriteLine("No running instance detected.");
                Console.WriteLine("Service can be started normally.");
            }
        }

        private static async Task HandleListSessionsAsync()
        {
            using var scope = _serviceProvider!.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();

            var sessions = await dbContext.ScanSessions
                .OrderByDescending(s => s.CreatedAt)
                .Take(20)
                .ToListAsync();

            if (!sessions.Any())
            {
                Console.WriteLine("No scan sessions found.");
                return;
            }

            Console.WriteLine($"{"Session ID",-36} {"Status",-12} {"Path",-40} {"Created",-20} {"Files Found",-12}");
            Console.WriteLine(new string('-', 120));

            foreach (var session in sessions)
            {
                Console.WriteLine($"{session.Id,-36} {session.Status,-12} {session.RootPath,-40} {session.CreatedAt:yyyy-MM-dd HH:mm}  {session.FilesFound,-12}");
            }
        }

        private static async Task HandleStatusRequestAsync(Guid sessionId)
        {
            using var scope = _serviceProvider!.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();

            var session = await dbContext.ScanSessions.FindAsync(sessionId);
            if (session == null)
            {
                Console.WriteLine($"Session {sessionId} not found.");
                return;
            }

            Console.WriteLine($"Session ID: {session.Id}");
            Console.WriteLine($"Status: {session.Status}");
            Console.WriteLine($"Path: {session.RootPath}");
            Console.WriteLine($"Created: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Started: {session.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not started"}");
            Console.WriteLine($"Completed: {session.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not completed"}");
            Console.WriteLine($"Progress: {session.ProgressPercentage:F1}%");
            Console.WriteLine($"Files Found: {session.FilesFound}");
            Console.WriteLine($"Directories Processed: {session.DirectoriesProcessed}/{session.TotalDirectories}");
            Console.WriteLine($"Current Operation: {session.CurrentOperation ?? "None"}");
            
            if (!string.IsNullOrEmpty(session.ErrorMessage))
            {
                Console.WriteLine($"Error: {session.ErrorMessage}");
            }
        }

        private static void SetupMinimalServices()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddUserSecrets<Program>()
                .Build();

            var services = new ServiceCollection();

            // Add configuration
            services.AddSingleton<IConfiguration>(configuration);

            // Add logging
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog();
            });

            // Configure database with SQL Server
            services.AddDbContext<ImportDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection") 
                    ?? "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true"));

            _serviceProvider = services.BuildServiceProvider();
            _logger = _serviceProvider.GetRequiredService<ILogger<Program>>();
        }
    }

    /// <summary>
    /// Simple console logger provider for singleton manager
    /// </summary>
    public class ConsoleLoggerProvider : ILoggerProvider
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
        {
            return new ConsoleLogger(categoryName);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Simple console logger implementation
    /// </summary>
    public class ConsoleLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _categoryName;

        public ConsoleLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) => null!;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Console.WriteLine($"[{timestamp}] [{logLevel}] {_categoryName}: {message}");
            
            if (exception != null)
            {
                Console.WriteLine($"[{timestamp}] [ERROR] Exception: {exception}");
            }
        }
    }
}
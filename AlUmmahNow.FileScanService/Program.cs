using AlUmmahNow.Data.Database;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.FileScanService.Hubs;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AlUmmahNow.FileScanService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .WriteTo.EventLog("AlUmmahNow.FileScanService", manageEventSource: true)
                .WriteTo.File("logs/filescan-service-.txt", 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            try
            {
                Log.Information("Starting AlUmmahNow File Scan Service with SignalR");
                var builder = WebApplication.CreateBuilder(args);

                // Configure Serilog
                builder.Host.UseSerilog();

                // Add Windows Service support
                builder.Services.AddWindowsService(options =>
                {
                    options.ServiceName = "AlUmmah FileScan Service";
                });

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
                
                // Register background services
                builder.Services.AddHostedService<FileScanWorker>();
                builder.Services.AddHostedService<ImportWorker>();

                // Configure CORS for development/cross-origin access
                builder.Services.AddCors(options =>
                {
                    options.AddDefaultPolicy(policy =>
                    {
                        policy.WithOrigins("http://localhost:*", "https://localhost:*")
                              .AllowAnyHeader()
                              .AllowAnyMethod()
                              .AllowCredentials();
                    });
                });

                var app = builder.Build();

                // Enable CORS
                app.UseCors();

                // Map SignalR hub
                app.MapHub<FileScanHub>("/scanHub");

                // Add a simple health check endpoint
                app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "FileScanService" }));

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Service terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
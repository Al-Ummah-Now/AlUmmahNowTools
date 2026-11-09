using AlUmmahNow.Data.Database;
using AlUmmahNow.DataImporter.Commands;
using AlUmmahNow.DataImporter.Services;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Diagnostics;

// Configure Serilog for console
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var services = new ServiceCollection();

ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
IConfiguration configuration = configurationBuilder
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddUserSecrets<Program>()
    .Build();

// Configure SQL Server database (for reading scan results)
var connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                      "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true";

services.AddDbContext<ImportDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add logging
services.AddLogging(builder =>
{
    builder.ClearProviders();
    builder.AddSerilog();
});

// Register SignalR client
var fileScanServiceUrl = configuration["FileScanService:Url"] ?? "http://localhost:5000";
services.AddSingleton(sp => new FileScanSignalRClient(fileScanServiceUrl));

// Register services
services.AddTransient<CommandExecutor>();
services.AddTransient<CommandValidator>();
services.AddTransient<FileScanServiceManager>();

var serviceProvider = services.BuildServiceProvider();

var app = new CommandLineApplication();
app.Name = "AlUmmahNow DataImporter Client";
app.Description = "Client for triggering scans and imports via the AlUmmahNow File Scan Service (SignalR)";

app.HelpOption();

// Define options
var optionFolder = app.Option("-f|--folder <Folder>", "The folder to scan for video files", CommandOptionType.SingleValue)
    .Accepts(v => v.ExistingDirectory());

var optionScanName = app.Option("-n|--name <Name>", "Name for the scan session", CommandOptionType.SingleValue);

var optionStartImport = app.Option("--start-import <SessionId>", "Start import for a completed scan session", CommandOptionType.SingleValue);

var optionListSessions = app.Option("--list-sessions", "List all scan sessions", CommandOptionType.NoValue);

var optionSessionStatus = app.Option("--status <SessionId>", "Get status of a scan session", CommandOptionType.SingleValue);

var optionImportStatus = app.Option("--import-status <SessionId>", "Get import status of a scan session", CommandOptionType.SingleValue);

var optionWaitForCompletion = app.Option("--wait", "Wait for scan completion before exiting", CommandOptionType.NoValue);

var optionDaemon = app.Option("--daemon", "Run scan service in daemon mode", CommandOptionType.NoValue);

app.OnExecuteAsync(async (ct) =>
{
    using var scope = serviceProvider.CreateScope();
    var commandExecutor = scope.ServiceProvider.GetRequiredService<CommandExecutor>();
    var commandValidator = scope.ServiceProvider.GetRequiredService<CommandValidator>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
    var fileScanManager = scope.ServiceProvider.GetRequiredService<FileScanServiceManager>();

    try
    {
        // Ensure database connection works
        await ImportDbSchema.EnsureUpToDateAsync(dbContext, ct);

        // Handle daemon mode
        if (optionDaemon.HasValue())
        {
            Console.WriteLine("Starting FileScan service in daemon mode...");
            return await fileScanManager.RunDaemonAsync(ct);
        }

        // Handle list sessions command
        if (optionListSessions.HasValue())
        {
            return await commandExecutor.ListScanSessionsAsync();
        }

        // Handle session status command
        if (optionSessionStatus.HasValue())
        {
            if (!commandValidator.ValidateSessionId(optionSessionStatus.Value(), out var sessionGuid))
            {
                return 1;
            }
            return await commandExecutor.GetSessionStatusAsync(sessionGuid);
        }

        // Handle start import command
        if (optionStartImport.HasValue())
        {
            if (!commandValidator.ValidateSessionId(optionStartImport.Value(), out var sessionGuid))
            {
                return 1;
            }
            return await commandExecutor.StartImportAsync(sessionGuid);
        }

        // Handle folder scan command
        if (optionFolder.HasValue())
        {
            var folderPath = optionFolder.Value();
            if (!commandValidator.ValidateFolderPath(folderPath))
            {
                return 1;
            }

            var scanName = commandValidator.GetScanName(optionScanName, folderPath!);
            return await commandExecutor.StartScanAsync(folderPath!, scanName, optionWaitForCompletion.HasValue(), ct);
        }

        // Handle import status command
        if (optionImportStatus.HasValue())
        {
            if (!commandValidator.ValidateSessionId(optionImportStatus.Value(), out var sessionGuid))
            {
                return 1;
            }
            return await commandExecutor.GetImportStatusAsync(sessionGuid);
        }

        // No valid command provided
        app.ShowHelp();
        return 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error: {ex.Message}");
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Application error");
        return 1;
    }
});

return await app.ExecuteAsync(args);
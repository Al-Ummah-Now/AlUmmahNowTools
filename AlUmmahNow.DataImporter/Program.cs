using AlUmmahNow.Data.Database;
using AlUmmahNow.DataImporter.Commands;
using AlUmmahNow.DataImporter.Services;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

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

// Configure SignalR client for real-time communication
var serviceUrl = configuration.GetValue<string>("FileScanService:Url") ?? "http://localhost:5000";

// Register services
services.AddSingleton(provider => new FileScanSignalRClient(serviceUrl));
services.AddTransient<CommandExecutor>();
services.AddTransient<CommandValidator>();

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

app.OnExecuteAsync(async (ct) =>
{
    using var scope = serviceProvider.CreateScope();
    var commandExecutor = scope.ServiceProvider.GetRequiredService<CommandExecutor>();
    var commandValidator = scope.ServiceProvider.GetRequiredService<CommandValidator>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
    var signalRClient = scope.ServiceProvider.GetRequiredService<FileScanSignalRClient>();

    try
    {
        // Ensure database connection works
        await dbContext.Database.EnsureCreatedAsync(ct);

        // Connect to SignalR service
        Console.WriteLine($"Connecting to FileScan service at {serviceUrl}...");
        await signalRClient.ConnectAsync();

        // Check if service is available
        if (!commandExecutor.IsServiceAvailable())
        {
            Console.WriteLine($"✗ Unable to connect to File Scan Service at {serviceUrl}");
            Console.WriteLine("Make sure the AlUmmahNow.FileScanService is running.");
            return 1;
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
    catch (InvalidOperationException ex) when (ex.Message.Contains("connect"))
    {
        Console.WriteLine($"✗ Unable to connect to File Scan Service at {serviceUrl}");
        Console.WriteLine($"Details: {ex.Message}");
        Console.WriteLine("Make sure the AlUmmahNow.FileScanService is running.");
        return 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error: {ex.Message}");
        return 1;
    }
    finally
    {
        // Cleanup SignalR connection
        await signalRClient.DisposeAsync();
    }
});

return await app.ExecuteAsync(args);
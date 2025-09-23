using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Data.Translation;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Database;
using AlUmmahNow.DataImporter;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

var services = new ServiceCollection();

ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
IConfiguration configuration = configurationBuilder
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddUserSecrets<Program>()
    .Build();
var r2 = configuration.GetSection("R2DB");
var bunnyVideo = configuration.GetSection("BunnyVideo");
var bunnyImage = configuration.GetSection("BunnyImage");
var bunnyStream = configuration.GetSection("BunnyStream");
var deepL = configuration.GetSection("DeepL");

// Configure SQLite database
var dbPath = configuration.GetConnectionString("DefaultConnection") ?? 
             Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "importdb.sqlite");

services.AddDbContext<ImportDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Configure services
services.AddSingleton<IR2Client>(provider => new R2Client(r2["S3Api"], r2["S3Key"], r2["S3Secret"]));
services.AddSingleton<Repository<Video>>();
services.AddSingleton<IStorageClient<Video>>(provider => new BunnyStorageClient<Video>(bunnyVideo["Zone"], bunnyVideo["Key"], bunnyVideo["Region"]));
services.AddSingleton<IStorageClient<Image>>(provider => new BunnyStorageClient<Image>(bunnyImage["Zone"], bunnyImage["Key"], bunnyImage["Region"]));
services.AddSingleton<BunnyStreamClient>(provider => new BunnyStreamClient(bunnyStream["Key"]));
services.AddSingleton<DeepLClient>(provider => new DeepLClient(deepL["ApiKey"], bool.Parse(deepL["IsFreeApi"] ?? "false")));
services.AddScoped<Importer>();
services.AddScoped(typeof(Repository<>));

var serviceProvider = services.BuildServiceProvider();

// Ensure database is created
using (var scope = serviceProvider.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

var app = new CommandLineApplication();

app.HelpOption();
var optionFolder = app.Option("-f|--folder <Folder>", "The folder", CommandOptionType.SingleValue)
    .IsRequired()
    .Accepts(v => v.ExistingDirectory());

var optionUserId = app.Option("-u|--user <UserId>", "User ID for the import", CommandOptionType.SingleValue);

var optionBaseVideoUrl = app.Option("--video-url <VideoUrl>", "Base video storage URL", CommandOptionType.SingleValue);

var optionBaseImageUrl = app.Option("--image-url <ImageUrl>", "Base image storage URL", CommandOptionType.SingleValue);

var optionResumeSession = app.Option("--resume <SessionId>", "Resume import session", CommandOptionType.SingleValue);

var optionStatusSession = app.Option("--status <SessionId>", "Get status of import session", CommandOptionType.SingleValue);

app.OnExecuteAsync(async (ct) =>
{
    using var scope = serviceProvider.CreateScope();
    var importer = scope.ServiceProvider.GetRequiredService<Importer>();

    // Check for status request
    if (optionStatusSession.HasValue())
    {
        if (!Guid.TryParse(optionStatusSession.Value(), out var statusSessionId))
        {
            Console.WriteLine("Invalid session ID format");
            return 1;
        }

        try
        {
            var status = await importer.GetImportStatusAsync(statusSessionId);
            Console.WriteLine($"Session: {status.SessionId}");
            Console.WriteLine($"Status: {status.Status}");
            Console.WriteLine($"Progress: {status.ProgressPercentage:F1}% ({status.VideosProcessed}/{status.TotalVideosFound})");
            Console.WriteLine($"Uploaded: {status.VideosUploaded}");
            Console.WriteLine($"Failed: {status.VideosFailed}");
            
            if (!string.IsNullOrEmpty(status.ErrorMessage))
            {
                Console.WriteLine($"Error: {status.ErrorMessage}");
            }

            Console.WriteLine("\nChannels:");
            foreach (var channel in status.Channels)
            {
                Console.WriteLine($"  {channel.ChannelName}: {channel.ProgressPercentage:F1}% ({channel.ProcessedVideos}/{channel.TotalVideos}) - Uploaded: {channel.UploadedVideos}, Failed: {channel.FailedVideos}");
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting status: {ex.Message}");
            return 1;
        }
    }

    // Check for resume request
    if (optionResumeSession.HasValue())
    {
        if (!Guid.TryParse(optionResumeSession.Value(), out var resumeSessionId))
        {
            Console.WriteLine("Invalid session ID format");
            return 1;
        }

        try
        {
            var success = await importer.ResumeImportAsync(resumeSessionId);
            Console.WriteLine(success ? "Import resumed successfully" : "Failed to resume import");
            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resuming import: {ex.Message}");
            return 1;
        }
    }

    // New multi-channel import
    var folderPath = optionFolder.Value() ?? "";
    var userId = optionUserId.Value() ?? Guid.NewGuid().ToString();
    var baseVideoUrl = optionBaseVideoUrl.Value() ?? "https://cdn.alummahnow.net";
    var baseImageUrl = optionBaseImageUrl.Value() ?? "https://images.alummahnow.net";

    try
    {
        var sessionId = await importer.ImportMultiChannelAsync(folderPath, userId, baseVideoUrl, baseImageUrl);
        Console.WriteLine($"Import completed. Session ID: {sessionId}");
        
        // Show final status
        var finalStatus = await importer.GetImportStatusAsync(sessionId);
        Console.WriteLine($"Final Status: {finalStatus.Status}");
        Console.WriteLine($"Total Videos: {finalStatus.TotalVideosFound}");
        Console.WriteLine($"Uploaded: {finalStatus.VideosUploaded}");
        Console.WriteLine($"Failed: {finalStatus.VideosFailed}");
        
        return finalStatus.Status == AlUmmahNow.Data.Models.ImportStatus.Completed ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Import failed: {ex.Message}");
        return 1;
    }
});

return await app.ExecuteAsync(args);
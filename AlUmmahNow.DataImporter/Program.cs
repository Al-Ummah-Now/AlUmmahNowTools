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

// Define options (let Importer handle validation/branching)
var optionFolder = app.Option("-f|--folder <Folder>", "The folder containing channel subfolders", CommandOptionType.SingleValue)
    .Accepts(v => v.ExistingDirectory());

var optionUserId = app.Option("-u|--user <UserId>", "User ID for the import", CommandOptionType.SingleValue);
var optionBaseVideoUrl = app.Option("--video-url <VideoUrl>", "Base video storage URL", CommandOptionType.SingleValue);
var optionBaseImageUrl = app.Option("--image-url <ImageUrl>", "Base image storage URL", CommandOptionType.SingleValue);
var optionResumeSession = app.Option("--resume <SessionId>", "Resume import session", CommandOptionType.SingleValue);
var optionStatusSession = app.Option("--status <SessionId>", "Get status of import session", CommandOptionType.SingleValue);

app.OnExecuteAsync(async (ct) =>
{
    var importArgs = ImportArgs.FromCommandLine(
        optionFolder.Value(),
        optionUserId.Value(),
        optionBaseVideoUrl.Value(),
        optionBaseImageUrl.Value(),
        optionResumeSession.Value(),
        optionStatusSession.Value());

    using var scope = serviceProvider.CreateScope();
    var importer = scope.ServiceProvider.GetRequiredService<Importer>();

    return await importer.ExecuteImportAsync(importArgs);
});

return await app.ExecuteAsync(args);
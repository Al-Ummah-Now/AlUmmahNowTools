using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Data.Translation;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.DataImporter;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

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

// Configure services
services.AddSingleton<IR2Client>(provider => new R2Client(r2["S3Api"], r2["S3Key"], r2["S3Secret"]));
services.AddSingleton<Repository<Video>>();
services.AddSingleton<IStorageClient<Video>>(provider => new BunnyStorageClient<Video>(bunnyVideo["Zone"], bunnyVideo["Key"], bunnyVideo["Region"]));
services.AddSingleton<IStorageClient<Image>>(provider => new BunnyStorageClient<Image>(bunnyImage["Zone"], bunnyImage["Key"], bunnyImage["Region"]));
services.AddSingleton<BunnyStreamClient>(provider => new BunnyStreamClient(bunnyStream["Key"]));
services.AddSingleton<DeepLClient>(provider => new DeepLClient(deepL["ApiKey"], bool.Parse(deepL["IsFreeApi"] ?? "false")));
services.AddSingleton<Importer>();
services.AddScoped(typeof(Repository<>));

var serviceProvider = services.BuildServiceProvider();

var app = new CommandLineApplication();

app.HelpOption();
var optionFolder = app.Option("-f|--folder <Folder>", "The folder", CommandOptionType.SingleValue)
    .IsRequired()
    .Accepts(v => v.ExistingDirectory());

var importer = serviceProvider.GetRequiredService<Importer>();

app.OnExecuteAsync(async (ct) =>
//await importer.Import(optionFolder.Value() ?? "", "d3b07384-d9a7-4f3b-8a1d-6c7b8b8f8f8f", "Mostafa Elbadry", "https://al-ummah-now-images.b-cdn.net/photo_2019-04-03_08-09-28.jpg", "/videos-mostafa-elbadry/Mostafa Elbadry/", "cdn.alummahnow.net/Mostafa Elbadry/", "/al-ummah-now-images/", "images.alummahnow.net/"));
await importer.ImportFromBunnyApi("498994", "aa0faf4f-96a2-472c-a07f-33c3e6da21b2", "Faisal Aljasem", "https://al-ummah-now-images.b-cdn.net/photo_2019-04-03_08-09-28.jpg", "/videos-mostafa-elbadry/Mostafa Elbadry/", "cdn.alummahnow.net/Mostafa Elbadry/", "/al-ummah-now-images/", "images.alummahnow.net/"));

await app.ExecuteAsync(args);
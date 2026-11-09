using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Importer;
using AlUmmahNow.Importer.Core;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShellProgressBar;

namespace AlUmmahNow.BunnyBulkUploader
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var services = new ServiceCollection();
            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            IConfiguration configuration = configurationBuilder.AddUserSecrets<Program>().Build();
            var bunnyVideo = configuration.GetSection("BunnyVideo");

            // Configure services
            services.AddSingleton<IStorageClient<Video>>(provider => new BunnyStorageClient<Video>(bunnyVideo["Zone"], bunnyVideo["Key"], bunnyVideo["Region"]));
            services.AddSingleton<SimpleVideoUploader>();

            var serviceProvider = services.BuildServiceProvider();

            var app = new CommandLineApplication();

            app.HelpOption();

            // Read-only: lists catalogue records that share one video URL (written before keys included
            // the sub-folder path). Moves and deletes nothing.
            app.Command("check-records", cmd =>
            {
                cmd.Description = "List R2 video records that point at the same storage object (read-only).";
                cmd.HelpOption();
                cmd.OnExecuteAsync(async ct =>
                {
                    var r2 = configuration.GetSection("R2DB");
                    using var r2Client = new R2Client(r2["S3Api"] ?? "", r2["S3Key"] ?? "", r2["S3Secret"] ?? "");
                    var groups = await VideoRecordAudit.FindSharedVideoUrlsAsync(new Repository<Video>(r2Client));
                    foreach (var g in groups)
                    {
                        Console.WriteLine(g.VideoUrl);
                        foreach (var r in g.Records)
                        {
                            Console.WriteLine($"  {r.ID}  {r.Channel}  {r.Title}  created {r.DateCreated:u}");
                        }
                    }
                    Console.WriteLine($"{groups.Count} shared URL(s).");
                    return 0;
                });
            });
            var optionFolder = app.Option("-f|--folder <Folder>", "The folder containing video files", CommandOptionType.SingleValue)
                .IsRequired()
                .Accepts(v => v.ExistingDirectory());
            var optionRoot = app.Option("-r|--root <Root>",
                    "Storage path to upload into, starting with the storage zone, e.g. /my-zone/channel-name/. " +
                    "Required: each channel or folder needs its own root, or files with the same name overwrite each other.",
                    CommandOptionType.SingleValue)
                .IsRequired();

            app.OnExecuteAsync(async (ct) =>
            {
                var uploader = serviceProvider.GetRequiredService<SimpleVideoUploader>();
                var folderPath = optionFolder.Value() ?? "";
                if (!UploadRoot.TryNormalize(optionRoot.Value(), bunnyVideo["Zone"], out var root, out var rootError))
                {
                    Console.Error.WriteLine(rootError);
                    return 2;
                }
                
                ProgressBar? progressBar = null;

                // Subscribe to events
                uploader.ProgressChanged += (sender, e) =>
                {
                    if (progressBar == null)
                    {
                        progressBar = new ProgressBar(e.TotalFiles, $"Uploading files from {folderPath}");
                    }
                    
                    progressBar.Tick($"Processing {e.CurrentFileName} ({e.ProcessedFiles}/{e.TotalFiles})");
                };

                uploader.FileUploadError += (sender, e) =>
                {
                    Console.WriteLine($"Failed to upload {e.FileName}: {e.ErrorMessage}");
                };

                uploader.UploadCompleted += (sender, e) =>
                {
                    progressBar?.Dispose();
                    Console.WriteLine($"Upload completed: {e.SuccessfulUploads} successful, {e.FailedUploads} failed out of {e.TotalFiles} total files.");
                };

                try
                {
                    await uploader.UploadAsync(folderPath, root);
                    return 0;
                }
                catch (Exception ex)
                {
                    progressBar?.Dispose();
                    Console.WriteLine($"Upload failed: {ex.Message}");
                    return 1;
                }
            });

            await app.ExecuteAsync(args);
        }
    }
}

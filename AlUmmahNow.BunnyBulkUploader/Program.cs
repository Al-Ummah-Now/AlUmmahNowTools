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
            var optionFolder = app.Option("-f|--folder <Folder>", "The folder containing video files", CommandOptionType.SingleValue)
                .IsRequired()
                .Accepts(v => v.ExistingDirectory());

            app.OnExecuteAsync(async (ct) =>
            {
                var uploader = serviceProvider.GetRequiredService<SimpleVideoUploader>();
                var folderPath = optionFolder.Value() ?? "";
                
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
                    await uploader.UploadAsync(folderPath, "/faisal-aljasem/");
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

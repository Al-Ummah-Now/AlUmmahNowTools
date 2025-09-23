using AlUmmahNow.Data.Storage;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;


namespace AlUmmahNow.BunnyBulkUploader
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

            var services = new ServiceCollection();
            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            IConfiguration configuration = configurationBuilder.AddUserSecrets<Program>().Build();
            var r2 = configuration.GetSection("R2DB");
            var bunnyVideo = configuration.GetSection("BunnyVideo");

            // Configure services
            services.AddSingleton<IStorageClient<Video>>(provider => new BunnyStorageClient<Video>(bunnyVideo["Zone"], bunnyVideo["Key"], bunnyVideo["Region"]));
            services.AddSingleton<Uploader>();

            var serviceProvider = services.BuildServiceProvider();

            var app = new CommandLineApplication();

            app.HelpOption();
            var optionFolder = app.Option("-f|--folder <Folder>", "The folder", CommandOptionType.SingleValue)
                .IsRequired()
                .Accepts(v => v.ExistingDirectory());

            var importer = serviceProvider.GetRequiredService<Uploader>();

            app.OnExecuteAsync(async (ct) =>
            await importer.Upload(optionFolder.Value() ?? "", "/faisal-aljasem/"));

            await app.ExecuteAsync(args);
        }
    }
}

using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Background service that handles import operations for scanned files
    /// </summary>
    public class ImportWorker : BackgroundService
    {
        private readonly ILogger<ImportWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _importCheckInterval = TimeSpan.FromSeconds(30);

        public ImportWorker(
            ILogger<ImportWorker> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Import Worker started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingImports(stoppingToken);
                    await Task.Delay(_importCheckInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ImportWorker main loop");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Import Worker stopped");
        }

        internal async Task ProcessPendingImports(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();

            // Find files that are selected for import but not yet processed. This read is only a candidate list:
            // each file is claimed below, and only claims this worker wins are imported (TOOLS-8).
            var candidates = await dbContext.ScannedVideoFiles
                .AsNoTracking()
                .Where(f => f.ImportStatus == FileImportStatus.Selected && f.FileExists)
                .OrderBy(f => f.FirstScanned)
                .Take(10) // Process up to 10 files at a time
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return; // No files to import
            }

            _logger.LogInformation("Processing {Count} files for import", candidates.Count);

            foreach (var file in candidates)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Selected -> Pending only if it is still Selected: the user may have unticked it since the read,
                // or another worker (another service host on the same database) may have claimed it.
                if (!await ScannedFileStatus.TransitionAsync(dbContext, file.Id, new[] { FileImportStatus.Selected }, FileImportStatus.Pending, cancellationToken))
                {
                    _logger.LogDebug("File {FilePath} was unselected or claimed elsewhere; skipping", file.FilePath);
                    continue;
                }

                try
                {
                    await ProcessSingleFileImport(file, dbContext, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Give the claim back so the file is picked up again on the next start.
                    await ScannedFileStatus.TransitionAsync(dbContext, file.Id,
                        new[] { FileImportStatus.Pending, FileImportStatus.Uploading }, FileImportStatus.Selected, CancellationToken.None);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing file {FilePath}", file.FilePath);
                    await ScannedFileStatus.TransitionAsync(dbContext, file.Id,
                        new[] { FileImportStatus.Pending, FileImportStatus.Uploading }, FileImportStatus.Failed, CancellationToken.None);
                }
            }
        }

        private async Task ProcessSingleFileImport(ScannedVideoFile file, ImportDbContext dbContext, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting import for file {FileName}", file.FileName);

            if (!await ScannedFileStatus.TransitionAsync(dbContext, file.Id, new[] { FileImportStatus.Pending }, FileImportStatus.Uploading, cancellationToken))
            {
                throw new InvalidOperationException($"Import claim on {file.FilePath} was lost before upload");
            }

            await ImportFileAsync(file, cancellationToken);

            if (!await ScannedFileStatus.TransitionAsync(dbContext, file.Id, new[] { FileImportStatus.Uploading }, FileImportStatus.Uploaded, cancellationToken))
            {
                throw new InvalidOperationException($"Import claim on {file.FilePath} was lost during upload");
            }

            _logger.LogInformation("Successfully imported file {FileName}", file.FileName);
        }

        /// <summary>
        /// Imports one claimed file. Only the worker that won the file's claim calls this, once per claim.
        /// </summary>
        protected virtual Task ImportFileAsync(ScannedVideoFile file, CancellationToken cancellationToken)
        {
            // TODO: Implement actual import logic here
            // This would involve:
            // 1. Reading the file
            // 2. Uploading to cloud storage (R2, BunnyCDN)
            // 3. Creating video stream entries
            // 4. Processing thumbnails
            // 5. Handling translation if needed

            // For now, simulate the import process
            return SimulateImportProcess(file, cancellationToken);
        }

        private async Task SimulateImportProcess(ScannedVideoFile file, CancellationToken cancellationToken)
        {
            // Simulate upload time based on file size (larger files take longer)
            var simulatedUploadTime = Math.Min(5000, Math.Max(1000, file.FileSize / (1024 * 1024))); // 1-5 seconds
            
            _logger.LogDebug("Simulating import of {FileName} ({FileSize} bytes) - estimated {Time}ms", 
                file.FileName, file.FileSize, simulatedUploadTime);
            
            await Task.Delay((int)simulatedUploadTime, cancellationToken);
        }
    }
}
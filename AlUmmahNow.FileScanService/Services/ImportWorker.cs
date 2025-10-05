using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.EntityFrameworkCore;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Background service that handles import operations for scanned files
    /// </summary>
    public class ImportWorker : BackgroundService
    {
        private readonly ILogger<ImportWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebSocketManager _webSocketManager;
        private readonly TimeSpan _importCheckInterval = TimeSpan.FromSeconds(30);

        public ImportWorker(
            ILogger<ImportWorker> logger,
            IServiceProvider serviceProvider,
            IWebSocketManager webSocketManager)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _webSocketManager = webSocketManager;
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

        private async Task ProcessPendingImports(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();

            // Find files that are selected for import but not yet processed
            var filesToImport = await dbContext.ScannedVideoFiles
                .Where(f => f.ImportStatus == FileImportStatus.Selected && f.FileExists)
                .OrderBy(f => f.FirstScanned)
                .Take(10) // Process up to 10 files at a time
                .ToListAsync(cancellationToken);

            if (filesToImport.Count == 0)
            {
                return; // No files to import
            }

            _logger.LogInformation("Processing {Count} files for import", filesToImport.Count);

            foreach (var file in filesToImport)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await ProcessSingleFileImport(file, dbContext, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing file {FilePath}", file.FilePath);
                    
                    // Mark file as failed
                    file.ImportStatus = FileImportStatus.Failed;
                    file.LastScanned = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }

        private async Task ProcessSingleFileImport(ScannedVideoFile file, ImportDbContext dbContext, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting import for file {FileName}", file.FileName);

            // Update status to pending
            file.ImportStatus = FileImportStatus.Pending;
            file.LastScanned = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            // Notify UI of status change
            await _webSocketManager.BroadcastFileUpdate(file);

            try
            {
                // Update status to uploading
                file.ImportStatus = FileImportStatus.Uploading;
                file.LastScanned = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                await _webSocketManager.BroadcastFileUpdate(file);

                // TODO: Implement actual import logic here
                // This would involve:
                // 1. Reading the file
                // 2. Uploading to cloud storage (R2, BunnyCDN)
                // 3. Creating video stream entries
                // 4. Processing thumbnails
                // 5. Handling translation if needed

                // For now, simulate the import process
                await SimulateImportProcess(file, cancellationToken);

                // Mark as successfully uploaded
                file.ImportStatus = FileImportStatus.Uploaded;
                file.LastScanned = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Successfully imported file {FileName}", file.FileName);
                await _webSocketManager.BroadcastFileUpdate(file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import file {FileName}", file.FileName);
                
                file.ImportStatus = FileImportStatus.Failed;
                file.LastScanned = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                await _webSocketManager.BroadcastFileUpdate(file);
                throw;
            }
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
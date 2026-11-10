using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using AlUmmahNow.FileScanService.Hubs;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Main worker service that monitors for scan requests and processes them
    /// </summary>
    public class FileScanWorker : BackgroundService
    {
        private readonly ILogger<FileScanWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<FileScanHub> _hubContext;
        private readonly TimeSpan _scanInterval = TimeSpan.FromSeconds(10);

        public FileScanWorker(
            ILogger<FileScanWorker> logger, 
            IServiceProvider serviceProvider,
            IHubContext<FileScanHub> hubContext)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("File Scan Worker started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingScans(stoppingToken);
                    await Task.Delay(_scanInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in FileScanWorker main loop");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("File Scan Worker stopped");
        }

        internal async Task ProcessPendingScans(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
            var scanEngine = scope.ServiceProvider.GetRequiredService<IFileScanEngine>();

            // Get pending scan sessions
            var pendingScans = await dbContext.ScanSessions
                .Where(s => s.Status == ScanStatus.Pending || s.Status == ScanStatus.InProgress)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(cancellationToken);

            foreach (var scanSession in pendingScans)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    _logger.LogInformation("Processing scan session {ScanId} for path {Path}", 
                        scanSession.Id, scanSession.RootPath);

                    // Update status to in progress
                    if (scanSession.Status == ScanStatus.Pending)
                    {
                        scanSession.Status = ScanStatus.InProgress;
                        scanSession.StartedAt = DateTime.UtcNow;
                        scanSession.LastUpdated = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync(cancellationToken);

                        // Notify clients via SignalR that scan started
                        await _hubContext.Clients.All.SendAsync(
                            "ScanStarted", 
                            scanSession.Id, 
                            scanSession.RootPath,
                            cancellationToken: cancellationToken);
                    }

                    // Subscribe to scan engine progress events
                    // Store handler in variable to allow proper unsubscription and prevent memory leak.
                    // The handler only forwards to SignalR. The engine has already saved these fields on the scoped
                    // ImportDbContext before raising the event and carries straight on with its next query, so the
                    // handler must neither touch that context nor modify the tracked scanSession (TOOLS-7).
                    EventHandler<ScanProgressEventArgs> progressHandler = async (sender, progress) =>
                    {
                        try
                        {
                            // Notify clients via SignalR
                            await _hubContext.Clients.All.SendAsync(
                                "ScanProgress",
                                scanSession.Id,
                                new
                                {
                                    FilesScanned = progress.FilesFound,
                                    CurrentFile = progress.CurrentFile ?? "",
                                    ProgressPercentage = progress.ProgressPercentage,
                                    CurrentOperation = progress.CurrentOperation
                                },
                                cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error broadcasting scan progress");
                        }
                    };

                    scanEngine.ProgressChanged += progressHandler;

                    try
                    {
                        // Process the scan
                        await scanEngine.ProcessScanAsync(scanSession, cancellationToken);
                    }
                    finally
                    {
                        // Always unsubscribe to prevent memory leak
                        scanEngine.ProgressChanged -= progressHandler;
                    }
                    
                    // Update completion status
                    scanSession.Status = ScanStatus.Completed;
                    scanSession.CompletedAt = DateTime.UtcNow;
                    scanSession.ProgressPercentage = 100;
                    scanSession.CurrentOperation = "Scan completed";
                    scanSession.LastUpdated = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);

                    // Notify clients via SignalR that scan completed
                    await _hubContext.Clients.All.SendAsync(
                        "ScanCompleted",
                        scanSession.Id,
                        scanSession.FilesFound,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation("Completed scan session {ScanId}", scanSession.Id);
                }
                catch (OperationCanceledException)
                {
                    // Update status to cancelled
                    scanSession.Status = ScanStatus.Cancelled;
                    scanSession.LastUpdated = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(CancellationToken.None);
                    
                    await _hubContext.Clients.All.SendAsync(
                        "ScanCancelled",
                        scanSession.Id);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing scan session {ScanId}", scanSession.Id);
                    
                    // Update status to failed
                    scanSession.Status = ScanStatus.Failed;
                    scanSession.ErrorMessage = ex.Message;
                    scanSession.LastUpdated = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(CancellationToken.None);

                    // Notify clients via SignalR that scan failed
                    await _hubContext.Clients.All.SendAsync(
                        "ScanFailed",
                        scanSession.Id,
                        ex.Message);
                }
            }
        }
    }
}
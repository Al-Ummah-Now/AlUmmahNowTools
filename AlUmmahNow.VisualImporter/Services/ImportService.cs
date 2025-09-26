using AlUmmahNow.Data.Models;
using AlUmmahNow.VisualImporter.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlUmmahNow.Data.Database;

namespace AlUmmahNow.VisualImporter.Services
{
    public class ImportService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SemaphoreSlim _importSemaphore = new SemaphoreSlim(1, 1);

        public ImportService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public event EventHandler<ImportProgressEventArgs>? ProgressChanged;
        public event EventHandler<ImportCompletedEventArgs>? ImportCompleted;

        public async Task<Guid> StartImportAsync(string folderPath, string userId = "")
        {
            if (!await _importSemaphore.WaitAsync(0))
            {
                throw new InvalidOperationException("An import operation is already in progress");
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<Importer>();

                if (string.IsNullOrEmpty(userId))
                {
                    userId = Guid.NewGuid().ToString();
                }

                // Start import in background
                var sessionId = await importer.ImportMultiChannelAsync(
                    folderPath,
                    userId,
                    "https://cdn.alummahnow.net",
                    "https://images.alummahnow.net"
                );

                // Monitor progress
                _ = Task.Run(async () => await MonitorImportProgressAsync(sessionId));

                return sessionId;
            }
            catch
            {
                _importSemaphore.Release();
                throw;
            }
        }

        public async Task<bool> ResumeImportAsync(Guid sessionId)
        {
            if (!await _importSemaphore.WaitAsync(0))
            {
                throw new InvalidOperationException("An import operation is already in progress");
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<Importer>();

                var result = await importer.ResumeImportAsync(sessionId);

                if (result)
                {
                    // Monitor progress
                    _ = Task.Run(async () => await MonitorImportProgressAsync(sessionId));
                }

                return result;
            }
            catch
            {
                _importSemaphore.Release();
                throw;
            }
        }

        public async Task<ImportSessionStatus> GetImportStatusAsync(Guid sessionId)
        {
            using var scope = _serviceProvider.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<Importer>();
            return await importer.GetImportStatusAsync(sessionId);
        }

        public async Task<List<ImportSession>> GetAllImportSessionsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
            
            return await dbContext.ImportSessions
                .Include(s => s.Channels)
                .Include(s => s.Videos)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();
        }

        public async Task<List<ImportVideo>> GetSessionVideosAsync(Guid sessionId)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
            
            return await dbContext.ImportVideos
                .Include(v => v.Channel)
                .Where(v => v.SessionId == sessionId)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> CancelImportAsync(Guid sessionId)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
            
            var session = await dbContext.ImportSessions.FindAsync(sessionId);
            if (session != null && session.Status == ImportStatus.InProgress)
            {
                session.Status = ImportStatus.Cancelled;
                session.EndTime = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
                return true;
            }
            return false;
        }

        private async Task MonitorImportProgressAsync(Guid sessionId)
        {
            try
            {
                var lastStatus = ImportStatus.InProgress;
                var lastProgress = 0;

                while (lastStatus == ImportStatus.InProgress || lastStatus == ImportStatus.Paused)
                {
                    await Task.Delay(2000); // Update every 2 seconds

                    try
                    {
                        var status = await GetImportStatusAsync(sessionId);
                        lastStatus = status.Status;

                        var currentProgress = (int)status.ProgressPercentage;
                        if (currentProgress != lastProgress || status.Status != ImportStatus.InProgress)
                        {
                            lastProgress = currentProgress;
                            ProgressChanged?.Invoke(this, new ImportProgressEventArgs
                            {
                                SessionId = sessionId,
                                Status = status
                            });
                        }

                        if (status.IsCompleted)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue monitoring
                        System.Diagnostics.Debug.WriteLine($"Error monitoring import progress: {ex.Message}");
                    }
                }

                // Final status update
                try
                {
                    var finalStatus = await GetImportStatusAsync(sessionId);
                    ImportCompleted?.Invoke(this, new ImportCompletedEventArgs
                    {
                        SessionId = sessionId,
                        Status = finalStatus,
                        Success = finalStatus.Status == ImportStatus.Completed
                    });
                }
                catch (Exception ex)
                {
                    ImportCompleted?.Invoke(this, new ImportCompletedEventArgs
                    {
                        SessionId = sessionId,
                        Status = null,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }
            finally
            {
                _importSemaphore.Release();
            }
        }
    }

    public class ImportProgressEventArgs : EventArgs
    {
        public Guid SessionId { get; set; }
        public ImportSessionStatus Status { get; set; } = new();
    }

    public class ImportCompletedEventArgs : EventArgs
    {
        public Guid SessionId { get; set; }
        public ImportSessionStatus? Status { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
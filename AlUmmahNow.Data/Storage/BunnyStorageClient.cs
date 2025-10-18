using BunnyCDN.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlUmmahNow.Data.Models;
using Polly;
using Polly.Retry;
using System.Net;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.Data.Storage
{
    public class BunnyStorageClient<T> : IStorageClient<T>
    {
        private readonly BunnyCDNStorage bunnyCDNStorage;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly SemaphoreSlim _rateLimiter;
        private readonly ILogger<BunnyStorageClient<T>>? _logger;

        public BunnyStorageClient(string zoneName, string key, string mainRegion, int maxConcurrentUploads = 10, ILogger<BunnyStorageClient<T>>? logger = null)
        {
            bunnyCDNStorage = new BunnyCDNStorage(zoneName, key, mainRegion);
            _logger = logger;

            // Configure retry policy with exponential backoff (1s, 2s, 4s)
            _retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<WebException>()
                .Or<TimeoutException>()
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger?.LogWarning("Bunny.net upload retry {RetryCount} after {Delay}s due to: {ErrorMessage}",
                            retryCount, timeSpan.TotalSeconds, exception.Message);
                    });

            // Rate limiter to prevent hitting API limits (max 100 concurrent, using 10 as safe default)
            _rateLimiter = new SemaphoreSlim(maxConcurrentUploads, maxConcurrentUploads);
        }

        public async Task DeleteAsync(string path)
        {
            await bunnyCDNStorage.DeleteObjectAsync(path);
        }

        public async Task<Stream> DownloadAsync(string path)
        {
            return await bunnyCDNStorage.DownloadObjectAsStreamAsync(path);
        }

        public async Task<List<StorageObject>> ListAsync(string path)
        {
            try
            {
                var result = await bunnyCDNStorage.GetStorageObjectsAsync(path);
                return result.Select(o =>
                    new StorageObject
                    {
                        Guid = o.Guid,
                        ObjectName = o.ObjectName,
                        DateCreated = o.DateCreated,
                        LastChanged = o.LastChanged,
                        Path = o.Path,
                        Length = o.Length,
                        IsDirectory = o.IsDirectory
                    }).ToList();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error listing objects in BunnyCDN at path: {Path}", path);
                return new List<StorageObject>();
            }
        }

        public async Task<StorageObject?> GetAsync(string path)
        {
            try
            {
                var result = await ListAsync(path);
                var obj = result.FirstOrDefault();

                if (obj == null)
                {
                    return null;
                }

                return new StorageObject
                {
                    Guid = obj.Guid,
                    ObjectName = obj.ObjectName,
                    DateCreated = obj.DateCreated,
                    LastChanged = obj.LastChanged,
                    Path = obj.Path,
                    Length = obj.Length,
                    IsDirectory = obj.IsDirectory
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting object from BunnyCDN at path: {Path}", path);
                return null;
            }
        }

        public async Task UploadAsync(Stream stream, string path)
        {
            await _rateLimiter.WaitAsync();
            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    // Reset stream position if seekable
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    await bunnyCDNStorage.UploadAsync(stream, path);
                });
            }
            finally
            {
                _rateLimiter.Release();
            }
        }
    }
}
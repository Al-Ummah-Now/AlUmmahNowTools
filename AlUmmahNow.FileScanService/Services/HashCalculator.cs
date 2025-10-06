using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Service for calculating various types of hashes
    /// </summary>
    public class HashCalculator : IHashCalculator
    {
        private readonly ILogger<HashCalculator> _logger;

        public HashCalculator(ILogger<HashCalculator> logger)
        {
            _logger = logger;
        }

        public async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                
                var hashBytes = await Task.Run(() => sha256.ComputeHash(stream), cancellationToken);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating hash for file {FilePath}", filePath);
                throw;
            }
        }

        public async Task<string> CalculateContentHashAsync(string content, CancellationToken cancellationToken = default)
        {
            try
            {
                using var sha256 = SHA256.Create();
                var contentBytes = Encoding.UTF8.GetBytes(content);
                
                var hashBytes = await Task.Run(() => sha256.ComputeHash(contentBytes), cancellationToken);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating content hash");
                throw;
            }
        }

        public string CalculatePathHash(string path)
        {
            try
            {
                using var sha256 = SHA256.Create();
                var pathBytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
                var hashBytes = sha256.ComputeHash(pathBytes);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating path hash for {Path}", path);
                throw;
            }
        }
    }
}
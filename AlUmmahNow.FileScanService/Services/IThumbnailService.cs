namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Interface for thumbnail generation service
    /// </summary>
    public interface IThumbnailService
    {
        /// <summary>
        /// Generates a single thumbnail from the video at approximately the middle timestamp
        /// </summary>
        Task<string?> GenerateThumbnailAsync(string videoPath, string outputDirectory, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates multiple thumbnails at evenly spaced intervals throughout the video
        /// </summary>
        Task<List<string>> GenerateMultipleThumbnailsAsync(string videoPath, string outputDirectory, int count = 5, CancellationToken cancellationToken = default);
    }
}

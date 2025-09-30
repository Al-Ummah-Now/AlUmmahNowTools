using System;

namespace AlUmmahNow.Importer.Core
{
    /// <summary>
    /// Arguments for import operations
    /// </summary>
    public class ImportArgs
    {
        public string FolderPath { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string BaseVideoUrl { get; set; } = "https://cdn.alummahnow.net";
        public string BaseImageUrl { get; set; } = "https://images.alummahnow.net";
        public Guid? ResumeSessionId { get; set; }
        public Guid? StatusSessionId { get; set; }

        /// <summary>
        /// Factory method to create ImportArgs from command line arguments
        /// </summary>
        public static ImportArgs FromCommandLine(string? folderPath, string? userId, string? baseVideoUrl, string? baseImageUrl, string? resumeSessionId, string? statusSessionId)
        {
            return new ImportArgs
            {
                FolderPath = folderPath ?? string.Empty,
                UserId = userId ?? string.Empty,
                BaseVideoUrl = baseVideoUrl ?? "https://cdn.alummahnow.net",
                BaseImageUrl = baseImageUrl ?? "https://images.alummahnow.net",
                ResumeSessionId = !string.IsNullOrEmpty(resumeSessionId) && Guid.TryParse(resumeSessionId, out var resumeGuid) ? resumeGuid : null,
                StatusSessionId = !string.IsNullOrEmpty(statusSessionId) && Guid.TryParse(statusSessionId, out var statusGuid) ? statusGuid : null
            };
        }
    }
}
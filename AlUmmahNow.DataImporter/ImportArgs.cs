using System;

namespace AlUmmahNow.DataImporter
{
    /// <summary>
    /// Encapsulates import operation arguments
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
        /// Creates ImportArgs from command line options
        /// </summary>
        public static ImportArgs FromCommandLine(
            string? folderPath,
            string? userId,
            string? baseVideoUrl,
            string? baseImageUrl,
            string? resumeSessionId,
            string? statusSessionId)
        {
            var args = new ImportArgs
            {
                FolderPath = folderPath ?? string.Empty,
                UserId = userId ?? Guid.NewGuid().ToString(),
                BaseVideoUrl = baseVideoUrl ?? "https://cdn.alummahnow.net",
                BaseImageUrl = baseImageUrl ?? "https://images.alummahnow.net"
            };

            // Parse session IDs
            if (!string.IsNullOrEmpty(resumeSessionId) && Guid.TryParse(resumeSessionId, out var resumeId))
            {
                args.ResumeSessionId = resumeId;
            }

            if (!string.IsNullOrEmpty(statusSessionId) && Guid.TryParse(statusSessionId, out var statusId))
            {
                args.StatusSessionId = statusId;
            }

            return args;
        }
    }
}
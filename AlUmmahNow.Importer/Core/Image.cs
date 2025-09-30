using System;

namespace AlUmmahNow.Importer.Core
{
    // Copy of Image model from DataImporter for compatibility
    public class Image
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime DateModified { get; set; }
    }
}
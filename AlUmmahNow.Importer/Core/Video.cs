using System;
using System.Collections.Generic;

namespace AlUmmahNow.Importer.Core
{
    // Copy of Video model from DataImporter for compatibility
    public class Video
    {
        public Guid ID { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string VideoUrl { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public List<string> AvailableResolutions { get; set; } = new List<string>();
        public string ChannelThumbnailUrl { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime DateModified { get; set; }
    }
}
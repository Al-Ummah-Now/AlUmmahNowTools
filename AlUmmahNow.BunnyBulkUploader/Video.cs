using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlUmmahNow.BunnyBulkUploader
{
    public class Video
    {
        public Guid ID { get; set; } = Guid.NewGuid();
        public string Title { get; set; }
        public string Description { get; set; }
        public string ThumbnailUrl { get; set; }
        public string VideoUrl { get; set; }
        public string UserId { get; set; }
        public string ChannelId { get; set; }
        public string Channel { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public List<string> AvailableResolutions { get; set; } = new List<string>();
        public string ChannelThumbnailUrl { get; set; }
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime DateModified { get; set; }
    }
    public class Image
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string UserId { get; set; }
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime DateModified { get; set; }
    }
}

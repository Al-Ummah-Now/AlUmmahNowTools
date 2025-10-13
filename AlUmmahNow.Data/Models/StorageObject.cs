namespace AlUmmahNow.Data.Models
{
    public class StorageObject
    {
        public string Guid { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Length { get; set; }
        public bool IsDirectory { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime LastChanged { get; set; }
        public string FullPath => Path + ObjectName;
    }
}
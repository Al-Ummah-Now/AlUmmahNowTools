namespace AlUmmahNow.Data.Models
{
    public class StorageObject
    {
        public string Guid { get; set; }
        public string ObjectName { get; set; }
        public string Path { get; set; }
        public long Length { get; set; }
        public bool IsDirectory { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime LastChanged { get; set; }
        public string FullPath => Path + ObjectName;
    }
}
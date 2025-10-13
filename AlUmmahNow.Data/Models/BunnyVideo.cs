using System.Text.Json.Serialization;

namespace AlUmmahNow.Data.Models
{
    public class TranscodingMessageModel
    {
        public DateTime? TimeStamp { get; set; }
        public Severity? Level { get; set; }
        public IssueCodes? IssueCode { get; set; }
        public string? Message { get; set; }
        public string? Value { get; set; }
    }

    public enum Severity
    {
        Low,
        Medium,
        High
    }

    public enum IssueCodes
    {
        Code1,
        Code2,
        Code3
    }

    public class MetaTagModel
    {
        public string? Property { get; set; }
        public string? Value { get; set; }
    }

    public class ChapterModel
    {
        public required string Title { get; set; }
        public int? Start { get; set; }
        public int? End { get; set; }
    }

    public class MomentModel
    {
        public string Label { get; set; } = string.Empty;
        public int? Timestamp { get; set; }
    }

    public enum VideoStatus
    {
        Created = 0,
        Uploaded = 1,
        Processing = 2,
        Transcoding = 3,
        Finished = 4,
        Error = 5,
        UploadFailed = 6,
        JitSegmenting = 7,
        JitPlaylistsCreated = 8
    }

    public class BunnyVideo
    {
        public int VideoLibraryId { get; set; }
        public string? Guid { get; set; }
        public string? Title { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime DateUploaded { get; set; }
        public int Views { get; set; }
        public bool IsPublic { get; set; }
        public int Length { get; set; }
        public VideoStatus Status { get; set; }
        public double Framerate { get; set; }
        public int? Rotation { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? AvailableResolutions { get; set; }
        public string? OutputCodecs { get; set; }
        public int ThumbnailCount { get; set; }
        public int EncodeProgress { get; set; }
        public long StorageSize { get; set; }
        public List<string>? Captions { get; set; }
        public bool HasMP4Fallback { get; set; }
        public string? CollectionId { get; set; }
        public string? ThumbnailFileName { get; set; }
        public int AverageWatchTime { get; set; }
        public int TotalWatchTime { get; set; }
        public string? Category { get; set; }
        public List<ChapterModel>? Chapters { get; set; }
        public List<MomentModel>? Moments { get; set; }
        public List<MetaTagModel>? MetaTags { get; set; }
        public List<TranscodingMessageModel>? TranscodingMessages { get; set; }
        public bool JitEncodingEnabled { get; set; }
        [JsonIgnore]
        public string VideoUrl => $"/{Guid}/playlist.m3u8"; 
    }
}
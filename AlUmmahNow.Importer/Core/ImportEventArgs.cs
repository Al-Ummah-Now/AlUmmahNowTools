using System;
using AlUmmahNow.Data.Video;

namespace AlUmmahNow.Importer.Core
{
    /// <summary>
    /// Event arguments for import progress updates
    /// </summary>
    public class ImportProgressEventArgs : EventArgs
    {
        public string CurrentOperation { get; set; } = string.Empty;
        public string CurrentFile { get; set; } = string.Empty;
        public string CurrentChannel { get; set; } = string.Empty;
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public double ProgressPercentage { get; set; }
        public string Status { get; set; } = string.Empty;
        public int FilesFound { get; set; } // Add FilesFound property for scanning progress

        // New constructor for backward compatibility
        public ImportProgressEventArgs()
        {
        }

        // New constructor for the enhanced version
        public ImportProgressEventArgs(int totalFiles, int processedFiles, string currentFileName, 
            string currentChannelName, string currentOperation = "Processing")
        {
            TotalFiles = totalFiles;
            ProcessedFiles = processedFiles;
            CurrentFile = currentFileName;
            CurrentChannel = currentChannelName;
            CurrentOperation = currentOperation;
            ProgressPercentage = TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;
            Status = currentOperation;
        }
    }

    /// <summary>
    /// Event arguments for import completion
    /// </summary>
    public class ImportCompletedEventArgs : EventArgs
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public Guid? SessionId { get; set; }
        public int TotalProcessed { get; set; }
        public int TotalUploaded { get; set; }
        public int TotalFailed { get; set; }

        // New constructor for backward compatibility
        public ImportCompletedEventArgs()
        {
        }

        // New constructor for the enhanced version
        public ImportCompletedEventArgs(int totalUploaded, int totalFailed, int totalProcessed, string? errorMessage = null)
        {
            TotalUploaded = totalUploaded;
            TotalFailed = totalFailed;
            TotalProcessed = totalProcessed;
            ErrorMessage = errorMessage;
            IsSuccess = totalFailed == 0 && string.IsNullOrEmpty(errorMessage);
        }
    }

    /// <summary>
    /// Event arguments for channel processing updates
    /// </summary>
    public class ChannelProgressEventArgs : EventArgs
    {
        public string ChannelName { get; }
        public int TotalVideosInChannel { get; }
        public int ProcessedVideosInChannel { get; }
        public double ChannelProgressPercentage => TotalVideosInChannel > 0 ? (double)ProcessedVideosInChannel / TotalVideosInChannel * 100 : 0;

        public ChannelProgressEventArgs(string channelName, int totalVideosInChannel, int processedVideosInChannel)
        {
            ChannelName = channelName;
            TotalVideosInChannel = totalVideosInChannel;
            ProcessedVideosInChannel = processedVideosInChannel;
        }
    }

    /// <summary>
    /// Event arguments for file processing errors
    /// </summary>
    public class FileImportErrorEventArgs : EventArgs
    {
        public string FileName { get; }
        public string ChannelName { get; }
        public Exception Exception { get; }
        public string ErrorMessage => Exception.Message;

        public FileImportErrorEventArgs(string fileName, string channelName, Exception exception)
        {
            FileName = fileName;
            ChannelName = channelName;
            Exception = exception;
        }
    }
    /// <summary>
    /// Which thumbnail a video got: its source thumbnail, or a frame from the video because the shapes
    /// differ (or there was no usable source).
    /// </summary>
    public class ThumbnailChoiceEventArgs : EventArgs
    {
        public string ChannelName { get; }
        public string FileName { get; }
        public ThumbnailDecision Decision { get; }
        public (int Width, int Height)? ThumbnailSize { get; }
        public (int Width, int Height)? VideoSize { get; }
        /// <summary>The thumbnail's key under the channel's image root, or null when the video has none.</summary>
        public string? ThumbnailKey { get; }
        /// <summary>A frame was needed but could not be taken, so the video has no thumbnail.</summary>
        public bool FrameFailed { get; }

        public ThumbnailChoiceEventArgs(string channelName, string fileName, ThumbnailDecision decision,
            (int Width, int Height)? thumbnailSize, (int Width, int Height)? videoSize, string? thumbnailKey, bool frameFailed)
        {
            ChannelName = channelName;
            FileName = fileName;
            Decision = decision;
            ThumbnailSize = thumbnailSize;
            VideoSize = videoSize;
            ThumbnailKey = thumbnailKey;
            FrameFailed = frameFailed;
        }

        /// <summary>One log line for this choice.</summary>
        public string Describe()
        {
            static string Size((int Width, int Height)? s) => s is { } v ? $"{v.Width}x{v.Height}" : "unknown";
            var what = Decision switch
            {
                ThumbnailDecision.KeepSource => "kept source thumbnail",
                ThumbnailDecision.KeepSourceUnverified => "kept source thumbnail (video size unreadable, shape not checked)",
                ThumbnailDecision.FrameShapeMismatch => "replaced source thumbnail with a video frame (shape mismatch)",
                ThumbnailDecision.FrameUnreadableThumbnail => "replaced source thumbnail with a video frame (thumbnail size unreadable)",
                _ => "used a video frame (no source thumbnail)",
            };
            if (FrameFailed) what += "; frame could not be taken, so no thumbnail";
            return $"[thumbnail] {ChannelName}/{FileName}: {what} (video {Size(VideoSize)}, thumbnail {Size(ThumbnailSize)})";
        }
    }
}
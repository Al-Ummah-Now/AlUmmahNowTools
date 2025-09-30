using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AlUmmahNow.VisualImporter.Models
{
    /// <summary>
    /// Represents a video file discovered during directory scanning
    /// </summary>
    public class VideoFileInfo : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private bool _isDuplicate;
        private string? _duplicateOf;
        private ImportFileStatus _importStatus = ImportFileStatus.NotImported;
        private string? _channelNameOverride;

        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string Directory { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string? ThumbnailPath { get; set; }
        public bool HasThumbnail { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsDuplicate
        {
            get => _isDuplicate;
            set => SetProperty(ref _isDuplicate, value);
        }

        public string? DuplicateOf
        {
            get => _duplicateOf;
            set => SetProperty(ref _duplicateOf, value);
        }

        public ImportFileStatus ImportStatus
        {
            get => _importStatus;
            set => SetProperty(ref _importStatus, value);
        }

        /// <summary>
        /// Override for the channel name (used when folder structure doesn't match desired channel naming)
        /// </summary>
        public string? ChannelNameOverride
        {
            get => _channelNameOverride;
            set => SetProperty(ref _channelNameOverride, value);
        }

        /// <summary>
        /// Formatted file size string
        /// </summary>
        public string FileSizeFormatted => FormatFileSize(FileSize);

        /// <summary>
        /// File name without extension
        /// </summary>
        public string FileNameWithoutExtension => System.IO.Path.GetFileNameWithoutExtension(FileName);

        /// <summary>
        /// Channel name (parent directory name or override)
        /// </summary>
        public string ChannelName
        {
            get
            {
                // Use override if set, otherwise derive from directory
                if (!string.IsNullOrEmpty(_channelNameOverride))
                    return _channelNameOverride;

                var dirInfo = new System.IO.DirectoryInfo(Directory);
                return dirInfo.Name;
            }
        }

        /// <summary>
        /// Status text for UI display
        /// </summary>
        public string StatusText
        {
            get
            {
                if (IsDuplicate)
                    return "Duplicate";
                if (!IsSelected)
                    return "Excluded";
                
                return ImportStatus switch
                {
                    ImportFileStatus.NotImported => "Ready",
                    ImportFileStatus.Pending => "Pending",
                    ImportFileStatus.Uploading => "Uploading",
                    ImportFileStatus.Uploaded => "Uploaded",
                    ImportFileStatus.Failed => "Failed",
                    ImportFileStatus.Skipped => "Skipped",
                    _ => "Ready"
                };
            }
        }

        /// <summary>
        /// Import status color for UI display
        /// </summary>
        public string StatusColor
        {
            get
            {
                if (IsDuplicate)
                    return "SystemFillColorCautionBrush";
                if (!IsSelected)
                    return "SystemControlForegroundBaseMediumBrush";
                
                return ImportStatus switch
                {
                    ImportFileStatus.NotImported => "SystemControlForegroundBaseMediumBrush",
                    ImportFileStatus.Pending => "SystemFillColorAttentionBrush",
                    ImportFileStatus.Uploading => "AccentTextFillColorPrimaryBrush",
                    ImportFileStatus.Uploaded => "SystemFillColorSuccessBrush",
                    ImportFileStatus.Failed => "SystemFillColorCriticalBrush",
                    ImportFileStatus.Skipped => "SystemControlForegroundBaseMediumBrush",
                    _ => "SystemControlForegroundBaseMediumBrush"
                };
            }
        }

        /// <summary>
        /// Import status icon for UI display
        /// </summary>
        public string StatusIcon
        {
            get
            {
                if (IsDuplicate)
                    return "\uE7C3"; // Warning icon
                if (!IsSelected)
                    return "\uE738"; // Remove icon
                
                return ImportStatus switch
                {
                    ImportFileStatus.NotImported => "\uE73E", // Checkmark icon
                    ImportFileStatus.Pending => "\uE916", // Clock icon
                    ImportFileStatus.Uploading => "\uE898", // Upload icon
                    ImportFileStatus.Uploaded => "\uE73E", // Checkmark icon
                    ImportFileStatus.Failed => "\uE783", // Error icon
                    ImportFileStatus.Skipped => "\uE738", // Remove icon
                    _ => "\uE73E"
                };
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            
            // Notify dependent properties when ImportStatus or ChannelNameOverride changes
            if (propertyName == nameof(ImportStatus))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusIcon));
            }
            else if (propertyName == nameof(ChannelNameOverride))
            {
                OnPropertyChanged(nameof(ChannelName));
            }
        }

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Import status for individual files
    /// </summary>
    public enum ImportFileStatus
    {
        NotImported,
        Pending,
        Uploading,
        Uploaded,
        Failed,
        Skipped
    }

    /// <summary>
    /// Statistics about discovered files
    /// </summary>
    public class FileStatistics
    {
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }
        public int DuplicateFiles { get; set; }
        public long DuplicateSize { get; set; }
        public int UniqueFiles { get; set; }
        public long UniqueSize { get; set; }
        public double AverageFileSize { get; set; }
        public long LargestFile { get; set; }
        public long SmallestFile { get; set; }

        // Import status statistics
        public int ReadyFiles { get; set; }
        public int PendingFiles { get; set; }
        public int UploadingFiles { get; set; }
        public int UploadedFiles { get; set; }
        public int FailedFiles { get; set; }
        public int SkippedFiles { get; set; }

        /// <summary>
        /// Formatted total size string
        /// </summary>
        public string TotalSizeFormatted => FormatFileSize(TotalSize);

        /// <summary>
        /// Formatted unique size string
        /// </summary>
        public string UniqueSizeFormatted => FormatFileSize(UniqueSize);

        /// <summary>
        /// Formatted duplicate size string
        /// </summary>
        public string DuplicateSizeFormatted => FormatFileSize(DuplicateSize);

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Enumeration for sorting video files
    /// </summary>
    public enum VideoSortOption
    {
        Name,
        Size,
        DateCreated,
        DateModified,
        Directory,
        ImportStatus
    }

    /// <summary>
    /// Sort direction
    /// </summary>
    public enum SortDirection
    {
        Ascending,
        Descending
    }
}
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Importer.Core;

namespace AlUmmahNow.Importer
{
    /// <summary>
    /// Event arguments for upload progress updates
    /// </summary>
    public class UploadProgressEventArgs : EventArgs
    {
        public int TotalFiles { get; }
        public int ProcessedFiles { get; }
        public string CurrentFileName { get; }
        public double ProgressPercentage => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;

        public UploadProgressEventArgs(int totalFiles, int processedFiles, string currentFileName)
        {
            TotalFiles = totalFiles;
            ProcessedFiles = processedFiles;
            CurrentFileName = currentFileName;
        }
    }

    /// <summary>
    /// Event arguments for upload completion
    /// </summary>
    public class UploadCompletedEventArgs : EventArgs
    {
        public int TotalFiles { get; }
        public int SuccessfulUploads { get; }
        public int FailedUploads { get; }
        public bool IsSuccess => FailedUploads == 0;

        public UploadCompletedEventArgs(int totalFiles, int successfulUploads, int failedUploads)
        {
            TotalFiles = totalFiles;
            SuccessfulUploads = successfulUploads;
            FailedUploads = failedUploads;
        }
    }

    /// <summary>
    /// Event arguments for individual file upload errors
    /// </summary>
    public class FileUploadErrorEventArgs : EventArgs
    {
        public string FileName { get; }
        public Exception Exception { get; }
        public string ErrorMessage => Exception.Message;

        public FileUploadErrorEventArgs(string fileName, Exception exception)
        {
            FileName = fileName;
            Exception = exception;
        }
    }

    /// <summary>
    /// Simple video uploader for basic bulk upload operations
    /// </summary>
    public class SimpleVideoUploader
    {
        private readonly IStorageClient<Video> _videoStorageClient;

        /// <summary>
        /// Raised when upload progress changes
        /// </summary>
        public event EventHandler<UploadProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Raised when upload operation completes
        /// </summary>
        public event EventHandler<UploadCompletedEventArgs>? UploadCompleted;

        /// <summary>
        /// Raised when an individual file upload fails
        /// </summary>
        public event EventHandler<FileUploadErrorEventArgs>? FileUploadError;

        public SimpleVideoUploader(IStorageClient<Video> videoStorageClient)
        {
            _videoStorageClient = videoStorageClient ?? throw new ArgumentNullException(nameof(videoStorageClient));
        }

        /// <summary>
        /// Upload all video files from a folder
        /// </summary>
        /// <param name="folder">Folder containing video files</param>
        /// <param name="videoStoreRoot">Storage root path for videos</param>
        public async Task UploadAsync(string folder, string videoStoreRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(videoStoreRoot);
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Folder not found: {folder}");

            var directory = new DirectoryInfo(folder);
            var files = directory.GetFiles()
                .Where(f => f.Extension.ToLowerInvariant() is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv")
                .ToArray();

            int successfulUploads = 0;
            int failedUploads = 0;

            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                
                // Report progress before processing file
                ProgressChanged?.Invoke(this, new UploadProgressEventArgs(files.Length, i, file.Name));

                try
                {
                    await UploadVideoAsync(file, videoStoreRoot);
                    successfulUploads++;
                    
                    // Report progress after successful upload
                    ProgressChanged?.Invoke(this, new UploadProgressEventArgs(files.Length, i + 1, file.Name));
                }
                catch (Exception ex)
                {
                    failedUploads++;
                    
                    // Report the error
                    FileUploadError?.Invoke(this, new FileUploadErrorEventArgs(file.Name, ex));
                    
                    // Still report progress to keep counter accurate
                    ProgressChanged?.Invoke(this, new UploadProgressEventArgs(files.Length, i + 1, file.Name));
                }
            }

            // Report completion
            UploadCompleted?.Invoke(this, new UploadCompletedEventArgs(files.Length, successfulUploads, failedUploads));
        }

        /// <summary>
        /// Upload a single video file
        /// </summary>
        /// <param name="file">Video file to upload</param>
        /// <param name="videoStoreRoot">Storage root path</param>
        /// <returns>Whether the file was sent or an identical one was already stored at its key.</returns>
        /// <exception cref="StorageKeyConflictException">A different file is already stored at the key; it is not overwritten.</exception>
        public Task<UploadOutcome> UploadVideoAsync(FileInfo file, string videoStoreRoot)
        {
            return _videoStorageClient.UploadIfAbsentAsync(file, StorageUploads.EnsureTrailingSlash(videoStoreRoot) + file.Name);
        }
    }
}
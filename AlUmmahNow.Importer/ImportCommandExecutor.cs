using System;
using System.Threading.Tasks;
using AlUmmahNow.Importer.Core;
using ShellProgressBar;

namespace AlUmmahNow.Importer
{
    /// <summary>
    /// Command-line execution helper for import operations
    /// </summary>
    public class ImportCommandExecutor
    {
        private readonly VideoImporter _importer;

        public ImportCommandExecutor(VideoImporter importer)
        {
            _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        }

        /// <summary>
        /// Executes import operation based on provided arguments
        /// </summary>
        /// <param name="args">Import arguments (already validated by CLI framework)</param>
        /// <returns>Exit code (0 for success, 1 for failure)</returns>
        public async Task<int> ExecuteImportAsync(ImportArgs args)
        {
            try
            {
                // Handle status request
                if (args.StatusSessionId.HasValue)
                {
                    return await HandleStatusRequestAsync(args.StatusSessionId.Value);
                }

                // Handle resume request  
                if (args.ResumeSessionId.HasValue)
                {
                    return await HandleResumeRequestAsync(args.ResumeSessionId.Value);
                }

                // Handle new multi-channel import
                return await HandleNewImportAsync(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Import operation failed: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Handles status request for an import session
        /// </summary>
        private async Task<int> HandleStatusRequestAsync(Guid sessionId)
        {
            try
            {
                var status = await _importer.GetImportStatusAsync(sessionId);
                
                Console.WriteLine($"Session: {status.SessionId}");
                Console.WriteLine($"Status: {status.Status}");
                Console.WriteLine($"Progress: {status.ProgressPercentage:F1}% ({status.VideosProcessed}/{status.TotalVideosFound})");
                Console.WriteLine($"Uploaded: {status.VideosUploaded}");
                Console.WriteLine($"Failed: {status.VideosFailed}");
                
                if (!string.IsNullOrEmpty(status.ErrorMessage))
                {
                    Console.WriteLine($"Error: {status.ErrorMessage}");
                }

                Console.WriteLine("\nChannels:");
                foreach (var channel in status.Channels)
                {
                    Console.WriteLine($"  {channel.ChannelName}: {channel.ProgressPercentage:F1}% ({channel.ProcessedVideos}/{channel.TotalVideos}) - Uploaded: {channel.UploadedVideos}, Failed: {channel.FailedVideos}");
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting status: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Handles resume request for an import session
        /// </summary>
        private async Task<int> HandleResumeRequestAsync(Guid sessionId)
        {
            try
            {
                ProgressBar? progressBar = null;
                ChildProgressBar? channelProgressBar = null;
                
                // Subscribe to events for progress tracking
                _importer.ProgressChanged += (sender, e) =>
                {
                    if (progressBar == null)
                    {
                        progressBar = new ProgressBar(e.TotalFiles, "Resuming import...");
                    }
                    
                    progressBar.Tick($"{e.CurrentOperation}: {e.CurrentFile} in {e.CurrentChannel} ({e.ProcessedFiles}/{e.TotalFiles})");
                };

                _importer.ChannelProgressChanged += (sender, e) =>
                {
                    if (channelProgressBar != null)
                    {
                        channelProgressBar.Dispose();
                    }
                    channelProgressBar = progressBar?.Spawn(e.TotalVideosInChannel, $"Processing {e.ChannelName}");
                    channelProgressBar?.Tick($"({e.ProcessedVideosInChannel}/{e.TotalVideosInChannel})");
                };

                _importer.FileImportError += (sender, e) =>
                {
                    Console.WriteLine($"Failed to process {e.FileName} in {e.ChannelName}: {e.ErrorMessage}");
                };

                _importer.ImportCompleted += (sender, e) =>
                {
                    channelProgressBar?.Dispose();
                    progressBar?.Dispose();
                    
                    if (e.IsSuccess)
                    {
                        Console.WriteLine($"Import resumed successfully: {e.TotalUploaded} uploaded, {e.TotalFailed} failed out of {e.TotalProcessed} total files.");
                    }
                    else
                    {
                        Console.WriteLine($"Import completed with errors: {e.TotalUploaded} uploaded, {e.TotalFailed} failed. {e.ErrorMessage}");
                    }
                };

                var success = await _importer.ResumeImportAsync(sessionId);
                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resuming import: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Handles new multi-channel import
        /// </summary>
        private async Task<int> HandleNewImportAsync(ImportArgs args)
        {
            try
            {
                ProgressBar? progressBar = null;
                ChildProgressBar? channelProgressBar = null;
                
                // Subscribe to events for progress tracking
                _importer.ProgressChanged += (sender, e) =>
                {
                    if (progressBar == null)
                    {
                        progressBar = new ProgressBar(e.TotalFiles, $"Importing videos from {args.FolderPath}");
                    }
                    
                    progressBar.Tick($"{e.CurrentOperation}: {e.CurrentFile} in {e.CurrentChannel} ({e.ProcessedFiles}/{e.TotalFiles})");
                };

                _importer.ChannelProgressChanged += (sender, e) =>
                {
                    if (channelProgressBar != null)
                    {
                        channelProgressBar.Dispose();
                    }
                    channelProgressBar = progressBar?.Spawn(e.TotalVideosInChannel, $"Processing {e.ChannelName}");
                    channelProgressBar?.Tick($"({e.ProcessedVideosInChannel}/{e.TotalVideosInChannel})");
                };

                _importer.FileImportError += (sender, e) =>
                {
                    Console.WriteLine($"Failed to import {e.FileName} in {e.ChannelName}: {e.ErrorMessage}");
                };

                _importer.ImportCompleted += (sender, e) =>
                {
                    channelProgressBar?.Dispose();
                    progressBar?.Dispose();
                    
                    if (e.IsSuccess)
                    {
                        Console.WriteLine($"Import completed successfully: {e.TotalUploaded} uploaded, {e.TotalFailed} failed out of {e.TotalProcessed} total files.");
                    }
                    else
                    {
                        Console.WriteLine($"Import completed with errors: {e.TotalUploaded} uploaded, {e.TotalFailed} failed. {e.ErrorMessage}");
                    }
                };

                var sessionId = await _importer.ImportMultiChannelAsync(
                    args.FolderPath, 
                    args.UserId, 
                    args.BaseVideoUrl, 
                    args.BaseImageUrl);

                Console.WriteLine($"Import completed. Session ID: {sessionId}");
                
                // Show final status
                var finalStatus = await _importer.GetImportStatusAsync(sessionId);
                Console.WriteLine($"Final Status: {finalStatus.Status}");
                Console.WriteLine($"Total Videos: {finalStatus.TotalVideosFound}");
                Console.WriteLine($"Uploaded: {finalStatus.VideosUploaded}");
                Console.WriteLine($"Failed: {finalStatus.VideosFailed}");
                
                return finalStatus.Status == AlUmmahNow.Data.Models.ImportStatus.Completed ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Import failed: {ex.Message}");
                return 1;
            }
        }
    }
}
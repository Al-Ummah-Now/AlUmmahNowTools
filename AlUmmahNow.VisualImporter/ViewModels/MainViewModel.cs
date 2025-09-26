using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AlUmmahNow.Data.Models;
using AlUmmahNow.VisualImporter.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.Storage;
using Microsoft.UI.Xaml;
using System.Linq;
using WinRT.Interop;

namespace AlUmmahNow.VisualImporter.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ImportService _importService;

        [ObservableProperty]
        private string selectedFolderPath = string.Empty;

        [ObservableProperty]
        private bool isImporting = false;

        [ObservableProperty]
        private string statusMessage = "Ready to import";

        [ObservableProperty]
        private double progressPercentage = 0;

        [ObservableProperty]
        private ImportSessionStatus? currentImportStatus;

        [ObservableProperty]
        private ImportSession? selectedSession;

        public ObservableCollection<ImportSession> ImportSessions { get; } = new();
        public ObservableCollection<ImportVideo> SessionVideos { get; } = new();
        public ObservableCollection<ChannelStatus> ChannelStatuses { get; } = new();

        public MainViewModel(ImportService importService)
        {
            _importService = importService;
            _importService.ProgressChanged += OnImportProgressChanged;
            _importService.ImportCompleted += OnImportCompleted;
            
            _ = LoadImportSessionsAsync();
        }

        [RelayCommand]
        private async Task SelectFolderAsync()
        {
            try
            {
                var folderPicker = new FolderPicker();
                folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                folderPicker.FileTypeFilter.Add("*");

                // Get the current window handle for the picker
                var window = App.Current.MainWindow;
                var hWnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(folderPicker, hWnd);

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    SelectedFolderPath = folder.Path;
                    StatusMessage = $"Selected folder: {folder.Path}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error selecting folder: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartImport))]
        private async Task StartImportAsync()
        {
            try
            {
                IsImporting = true;
                StatusMessage = "Starting import...";
                ProgressPercentage = 0;

                var sessionId = await _importService.StartImportAsync(SelectedFolderPath);
                StatusMessage = $"Import started (Session: {sessionId})";
                
                await LoadImportSessionsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error starting import: {ex.Message}";
                IsImporting = false;
            }
        }

        private bool CanStartImport() => !string.IsNullOrEmpty(SelectedFolderPath) && !IsImporting;

        [RelayCommand]
        private async Task RefreshSessionsAsync()
        {
            await LoadImportSessionsAsync();
        }

        [RelayCommand]
        private async Task ViewSessionDetailsAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                StatusMessage = "Loading session details...";
                
                // Load session videos
                var videos = await _importService.GetSessionVideosAsync(SelectedSession.Id);
                SessionVideos.Clear();
                foreach (var video in videos)
                {
                    SessionVideos.Add(video);
                }

                // Load current status
                var status = await _importService.GetImportStatusAsync(SelectedSession.Id);
                CurrentImportStatus = status;
                
                ChannelStatuses.Clear();
                foreach (var channel in status.Channels)
                {
                    ChannelStatuses.Add(channel);
                }

                StatusMessage = $"Loaded details for session {SelectedSession.Id}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading session details: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ResumeImportAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                IsImporting = true;
                StatusMessage = "Resuming import...";

                var success = await _importService.ResumeImportAsync(SelectedSession.Id);
                if (success)
                {
                    StatusMessage = $"Import resumed (Session: {SelectedSession.Id})";
                    await LoadImportSessionsAsync();
                }
                else
                {
                    StatusMessage = "Failed to resume import";
                    IsImporting = false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error resuming import: {ex.Message}";
                IsImporting = false;
            }
        }

        [RelayCommand]
        private async Task CancelImportAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                var success = await _importService.CancelImportAsync(SelectedSession.Id);
                if (success)
                {
                    StatusMessage = $"Import cancelled (Session: {SelectedSession.Id})";
                    IsImporting = false;
                    await LoadImportSessionsAsync();
                }
                else
                {
                    StatusMessage = "Failed to cancel import";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error cancelling import: {ex.Message}";
            }
        }

        private async Task LoadImportSessionsAsync()
        {
            try
            {
                var sessions = await _importService.GetAllImportSessionsAsync();
                ImportSessions.Clear();
                foreach (var session in sessions)
                {
                    ImportSessions.Add(session);
                }

                // Update current import status if there's an active session
                var activeSession = sessions.FirstOrDefault(s => s.Status == ImportStatus.InProgress);
                if (activeSession != null)
                {
                    IsImporting = true;
                    var status = await _importService.GetImportStatusAsync(activeSession.Id);
                    CurrentImportStatus = status;
                    ProgressPercentage = status.ProgressPercentage;
                }
                else
                {
                    IsImporting = false;
                    ProgressPercentage = 0;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading sessions: {ex.Message}";
            }
        }

        private void OnImportProgressChanged(object? sender, ImportProgressEventArgs e)
        {
            // Use DispatcherQueue from the main window instead of App.Current
            if (App.Current.MainWindow != null)
            {
                App.Current.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    CurrentImportStatus = e.Status;
                    ProgressPercentage = e.Status.ProgressPercentage;
                    StatusMessage = $"Importing... {e.Status.ProgressPercentage:F1}% ({e.Status.VideosProcessed}/{e.Status.TotalVideosFound})";
                    
                    // Update channel statuses if viewing this session
                    if (SelectedSession?.Id == e.SessionId)
                    {
                        ChannelStatuses.Clear();
                        foreach (var channel in e.Status.Channels)
                        {
                            ChannelStatuses.Add(channel);
                        }
                    }
                });
            }
        }

        private void OnImportCompleted(object? sender, ImportCompletedEventArgs e)
        {
            // Use DispatcherQueue from the main window instead of App.Current
            if (App.Current.MainWindow != null)
            {
                App.Current.MainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    IsImporting = false;
                    
                    if (e.Success && e.Status != null)
                    {
                        StatusMessage = $"Import completed! Uploaded: {e.Status.VideosUploaded}, Failed: {e.Status.VideosFailed}";
                        ProgressPercentage = 100;
                        CurrentImportStatus = e.Status;
                    }
                    else
                    {
                        StatusMessage = $"Import failed: {e.ErrorMessage ?? "Unknown error"}";
                        ProgressPercentage = 0;
                    }

                    await LoadImportSessionsAsync();
                });
            }
        }

        public string GetStatusDisplayText(ImportStatus status)
        {
            return status switch
            {
                ImportStatus.InProgress => "In Progress",
                ImportStatus.Completed => "Completed",
                ImportStatus.Failed => "Failed",
                ImportStatus.Cancelled => "Cancelled",
                ImportStatus.Paused => "Paused",
                _ => status.ToString()
            };
        }

        public string GetVideoStatusDisplayText(VideoImportStatus status)
        {
            return status switch
            {
                VideoImportStatus.Pending => "Pending",
                VideoImportStatus.Scanning => "Scanning",
                VideoImportStatus.Uploading => "Uploading",
                VideoImportStatus.Uploaded => "Uploaded",
                VideoImportStatus.Failed => "Failed",
                VideoImportStatus.Skipped => "Skipped",
                _ => status.ToString()
            };
        }
    }
}
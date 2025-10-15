using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.VisualImporterUno.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using System.IO;

namespace AlUmmahNow.VisualImporterUno.ViewModels;

/// <summary>
/// ViewModel for the analytics dashboard showing approval metrics and trends
/// </summary>
public partial class AnalyticsDashboardViewModel : ObservableObject, IDisposable
{
    private readonly FileScanSignalRClient _signalRClient;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _disposed = false;

    #region Observable Properties

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private TimePeriod _selectedPeriod = TimePeriod.ThisWeek;

    [ObservableProperty]
    private DateTime _periodStartDate;

    [ObservableProperty]
    private DateTime _periodEndDate;

    // Overview Statistics
    [ObservableProperty]
    private int _totalApprovals = 0;

    [ObservableProperty]
    private double _approvalRate = 0.0;

    [ObservableProperty]
    private double _rejectionRate = 0.0;

    [ObservableProperty]
    private int _filesPending = 0;

    [ObservableProperty]
    private long _totalSizeApproved = 0;

    [ObservableProperty]
    private string _mostActiveChannel = "N/A";

    [ObservableProperty]
    private double _avgApprovalTimeHours = 0.0;

    [ObservableProperty]
    private double _approvalTrend = 0.0;

    [ObservableProperty]
    private double _rejectionTrend = 0.0;

    // Collections for charts and lists
    public ObservableCollection<DailyTrendData> ApprovalTrends { get; } = new();
    public ObservableCollection<ChannelStatistic> TopChannels { get; } = new();
    public ObservableCollection<HourlyActivity> PeakHours { get; } = new();
    public ObservableCollection<PriorityDistribution> PriorityStats { get; } = new();
    public ObservableCollection<RecentActivityItem> RecentActivities { get; } = new();

    #endregion

    public AnalyticsDashboardViewModel(FileScanSignalRClient signalRClient)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));

        // Get dispatcher queue for thread-safe UI updates
        try
        {
            _dispatcherQueue = App.Current?.MainWindow?.DispatcherQueue
                ?? DispatcherQueue.GetForCurrentThread();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get dispatcher queue: {ex.Message}");
            _dispatcherQueue = null;
        }

        // Set initial period
        UpdatePeriodDates();

        // Initialize with background data load
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500); // Give UI time to initialize
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Background initialization failed: {ex.Message}");
                DispatcherExecute(() =>
                {
                    StatusMessage = $"Initialization failed: {ex.Message}";
                });
            }
        });
    }

    #region Initialization

    private async Task InitializeAsync()
    {
        try
        {
            DispatcherExecute(() =>
            {
                IsLoading = true;
                StatusMessage = "Connecting to analytics service...";
            });

            // Ensure SignalR connection
            if (!_signalRClient.IsConnected)
            {
                var connected = await _signalRClient.ConnectAsync();
                if (!connected)
                {
                    throw new InvalidOperationException("Failed to connect to FileScan service");
                }
            }

            // Load analytics data
            await RefreshAnalyticsAsync();

            DispatcherExecute(() =>
            {
                StatusMessage = "Analytics dashboard loaded successfully";
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Initialization error: {ex.Message}";
            });
        }
        finally
        {
            DispatcherExecute(() => IsLoading = false);
        }
    }

    #endregion

    #region Commands

    [RelayCommand(CanExecute = nameof(CanRefreshAnalytics))]
    private async Task RefreshAnalyticsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading analytics data...";

            if (!_signalRClient.IsConnected)
            {
                throw new InvalidOperationException("Not connected to FileScan service");
            }

            // Load statistics from service
            var stats = await _signalRClient.GetApprovalStatisticsAsync();
            var allApprovals = await _signalRClient.GetPendingApprovalsAsync();

            // Calculate analytics
            await CalculateAnalyticsAsync(stats, allApprovals);

            DispatcherExecute(() =>
            {
                StatusMessage = $"Analytics updated for {SelectedPeriod}";
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = $"Error loading analytics: {ex.Message}";
            });
        }
        finally
        {
            DispatcherExecute(() => IsLoading = false);
        }
    }

    private bool CanRefreshAnalytics => !IsLoading && _signalRClient.IsConnected;

    [RelayCommand]
    private async Task ChangePeriodAsync(TimePeriod period)
    {
        SelectedPeriod = period;
        UpdatePeriodDates();
        await RefreshAnalyticsAsync();
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        try
        {
            StatusMessage = "Exporting report...";

            // Generate CSV report
            var csv = GenerateCsvReport();

            // Save to documents folder
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var fileName = $"ApprovalAnalytics_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var filePath = Path.Combine(documentsPath, fileName);

            await File.WriteAllTextAsync(filePath, csv);

            StatusMessage = $"Report exported to: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    #endregion

    #region Analytics Calculations

    private async Task CalculateAnalyticsAsync(ApprovalStatistics stats, List<PendingApprovalDto> allApprovals)
    {
        await Task.Run(() =>
        {
            // Calculate overview statistics
            CalculateOverviewStatistics(stats);

            // Calculate trends
            CalculateTrends(stats);

            // Calculate top channels
            CalculateTopChannels(allApprovals);

            // Calculate peak hours
            CalculatePeakHours(allApprovals);

            // Calculate priority distribution
            CalculatePriorityDistribution(allApprovals);

            // Get recent activities
            GetRecentActivities(allApprovals);
        });
    }

    private void CalculateOverviewStatistics(ApprovalStatistics stats)
    {
        DispatcherExecute(() =>
        {
            TotalApprovals = stats.TotalApproved;
            FilesPending = stats.TotalPending;
            TotalSizeApproved = stats.TotalSizeApproved;

            // Calculate rates
            var total = stats.TotalApproved + stats.TotalRejected + stats.TotalPending;
            if (total > 0)
            {
                ApprovalRate = (double)stats.TotalApproved / total * 100.0;
                RejectionRate = (double)stats.TotalRejected / total * 100.0;
            }
            else
            {
                ApprovalRate = 0.0;
                RejectionRate = 0.0;
            }

            // Calculate average approval time (mock data for now)
            if (stats.OldestPending.HasValue && stats.NewestPending.HasValue)
            {
                var timeSpan = stats.NewestPending.Value - stats.OldestPending.Value;
                AvgApprovalTimeHours = timeSpan.TotalHours / Math.Max(1, stats.TotalPending);
            }
            else
            {
                AvgApprovalTimeHours = 0.0;
            }
        });
    }

    private void CalculateTrends(ApprovalStatistics stats)
    {
        // Mock trend calculation (would need historical data)
        // Positive trend = improvement
        DispatcherExecute(() =>
        {
            ApprovalTrend = 12.5; // +12.5% compared to previous period
            RejectionTrend = -3.2; // -3.2% compared to previous period
        });

        // Calculate daily trends for chart
        DispatcherExecute(() =>
        {
            ApprovalTrends.Clear();

            var daysInPeriod = GetDaysInPeriod();
            for (int i = 0; i < daysInPeriod; i++)
            {
                var date = PeriodStartDate.AddDays(i);
                // Mock data - in real implementation, would query actual data
                var approvals = new Random((int)date.Ticks).Next(10, 50);
                var rejections = new Random((int)date.Ticks + 1).Next(2, 15);

                ApprovalTrends.Add(new DailyTrendData
                {
                    Date = date,
                    Approved = approvals,
                    Rejected = rejections,
                    Total = approvals + rejections
                });
            }
        });
    }

    private void CalculateTopChannels(List<PendingApprovalDto> allApprovals)
    {
        var channelGroups = allApprovals
            .GroupBy(a => a.ChannelName)
            .Select(g => new ChannelStatistic
            {
                ChannelName = g.Key,
                TotalFiles = g.Count(),
                ApprovedFiles = g.Count(a => a.Status == ApprovalStatus.Approved),
                RejectedFiles = g.Count(a => a.Status == ApprovalStatus.Rejected),
                PendingFiles = g.Count(a => a.Status == ApprovalStatus.Pending),
                TotalSize = g.Sum(a => a.FileSize)
            })
            .OrderByDescending(c => c.TotalFiles)
            .Take(5)
            .ToList();

        DispatcherExecute(() =>
        {
            TopChannels.Clear();
            foreach (var channel in channelGroups)
            {
                TopChannels.Add(channel);
            }

            MostActiveChannel = TopChannels.FirstOrDefault()?.ChannelName ?? "N/A";
        });
    }

    private void CalculatePeakHours(List<PendingApprovalDto> allApprovals)
    {
        var hourlyGroups = allApprovals
            .GroupBy(a => a.DetectedAt.Hour)
            .Select(g => new HourlyActivity
            {
                Hour = g.Key,
                FileCount = g.Count()
            })
            .OrderBy(h => h.Hour)
            .ToList();

        // Fill in missing hours with 0 count
        var maxCount = hourlyGroups.Any() ? hourlyGroups.Max(h => h.FileCount) : 1;
        var allHours = Enumerable.Range(0, 24)
            .Select(hour => new HourlyActivity
            {
                Hour = hour,
                FileCount = hourlyGroups.FirstOrDefault(h => h.Hour == hour)?.FileCount ?? 0,
                MaxCount = maxCount
            })
            .ToList();

        DispatcherExecute(() =>
        {
            PeakHours.Clear();
            foreach (var hour in allHours)
            {
                PeakHours.Add(hour);
            }
        });
    }

    private void CalculatePriorityDistribution(List<PendingApprovalDto> allApprovals)
    {
        var priorityGroups = allApprovals
            .GroupBy(a => a.Priority)
            .Select(g => new PriorityDistribution
            {
                Priority = g.Key,
                Count = g.Count(),
                Percentage = 0 // Will calculate after
            })
            .OrderBy(p => p.Priority)
            .ToList();

        var total = priorityGroups.Sum(p => p.Count);
        if (total > 0)
        {
            foreach (var priority in priorityGroups)
            {
                priority.Percentage = (double)priority.Count / total * 100.0;
            }
        }

        DispatcherExecute(() =>
        {
            PriorityStats.Clear();
            foreach (var priority in priorityGroups)
            {
                PriorityStats.Add(priority);
            }
        });
    }

    private void GetRecentActivities(List<PendingApprovalDto> allApprovals)
    {
        var recent = allApprovals
            .Where(a => a.Status != ApprovalStatus.Pending)
            .OrderByDescending(a => a.DetectedAt)
            .Take(10)
            .Select(a => new RecentActivityItem
            {
                FileName = a.FileName,
                ChannelName = a.ChannelName,
                Status = a.Status,
                Timestamp = a.DetectedAt,
                FileSize = a.FileSize
            })
            .ToList();

        DispatcherExecute(() =>
        {
            RecentActivities.Clear();
            foreach (var activity in recent)
            {
                RecentActivities.Add(activity);
            }
        });
    }

    #endregion

    #region Helper Methods

    private void UpdatePeriodDates()
    {
        var now = DateTime.Now;
        switch (SelectedPeriod)
        {
            case TimePeriod.Today:
                PeriodStartDate = now.Date;
                PeriodEndDate = now.Date.AddDays(1).AddTicks(-1);
                break;
            case TimePeriod.ThisWeek:
                PeriodStartDate = now.Date.AddDays(-(int)now.DayOfWeek);
                PeriodEndDate = PeriodStartDate.AddDays(7).AddTicks(-1);
                break;
            case TimePeriod.ThisMonth:
                PeriodStartDate = new DateTime(now.Year, now.Month, 1);
                PeriodEndDate = PeriodStartDate.AddMonths(1).AddTicks(-1);
                break;
            case TimePeriod.AllTime:
                PeriodStartDate = DateTime.MinValue;
                PeriodEndDate = DateTime.MaxValue;
                break;
        }
    }

    private int GetDaysInPeriod()
    {
        return SelectedPeriod switch
        {
            TimePeriod.Today => 1,
            TimePeriod.ThisWeek => 7,
            TimePeriod.ThisMonth => DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month),
            TimePeriod.AllTime => 30, // Show last 30 days for all time
            _ => 7
        };
    }

    private string GenerateCsvReport()
    {
        var csv = new System.Text.StringBuilder();

        // Header
        csv.AppendLine("Approval Analytics Report");
        csv.AppendLine($"Period: {SelectedPeriod}");
        csv.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        csv.AppendLine();

        // Overview Statistics
        csv.AppendLine("Overview Statistics");
        csv.AppendLine("Metric,Value");
        csv.AppendLine($"Total Approvals,{TotalApprovals}");
        csv.AppendLine($"Approval Rate,{ApprovalRate:F2}%");
        csv.AppendLine($"Rejection Rate,{RejectionRate:F2}%");
        csv.AppendLine($"Files Pending,{FilesPending}");
        csv.AppendLine($"Total Size Approved,{FormatFileSize(TotalSizeApproved)}");
        csv.AppendLine($"Most Active Channel,{MostActiveChannel}");
        csv.AppendLine($"Avg Approval Time (hours),{AvgApprovalTimeHours:F2}");
        csv.AppendLine();

        // Top Channels
        csv.AppendLine("Top Channels");
        csv.AppendLine("Channel,Total Files,Approved,Rejected,Pending,Total Size");
        foreach (var channel in TopChannels)
        {
            csv.AppendLine($"{channel.ChannelName},{channel.TotalFiles},{channel.ApprovedFiles},{channel.RejectedFiles},{channel.PendingFiles},{FormatFileSize(channel.TotalSize)}");
        }
        csv.AppendLine();

        // Priority Distribution
        csv.AppendLine("Priority Distribution");
        csv.AppendLine("Priority,Count,Percentage");
        foreach (var priority in PriorityStats)
        {
            csv.AppendLine($"{priority.Priority},{priority.Count},{priority.Percentage:F2}%");
        }

        return csv.ToString();
    }

    private string FormatFileSize(long bytes)
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

    private void DispatcherExecute(Action action)
    {
        if (_dispatcherQueue?.HasThreadAccess == true)
        {
            action();
        }
        else if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => action());
        }
        else
        {
            // Fallback: execute directly (might cause threading issues)
            action();
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            // Clear collections
            try
            {
                ApprovalTrends.Clear();
                TopChannels.Clear();
                PeakHours.Clear();
                PriorityStats.Clear();
                RecentActivities.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing AnalyticsDashboardViewModel: {ex.Message}");
            }
        }
    }

    #endregion
}

#region Supporting Classes

/// <summary>
/// Time period enumeration for analytics filtering
/// </summary>
public enum TimePeriod
{
    Today,
    ThisWeek,
    ThisMonth,
    AllTime
}

/// <summary>
/// Daily trend data for approval charts
/// </summary>
public class DailyTrendData
{
    public DateTime Date { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Total { get; set; }
    public string DateLabel => Date.ToString("MMM dd");
    public double ApprovedPercentage => Total > 0 ? (double)Approved / Total * 100 : 0;
    public double RejectedPercentage => Total > 0 ? (double)Rejected / Total * 100 : 0;
}

/// <summary>
/// Channel statistics for top channels analysis
/// </summary>
public class ChannelStatistic
{
    public string ChannelName { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int ApprovedFiles { get; set; }
    public int RejectedFiles { get; set; }
    public int PendingFiles { get; set; }
    public long TotalSize { get; set; }
    public double ApprovalRate => TotalFiles > 0 ? (double)ApprovedFiles / TotalFiles * 100 : 0;
    public string TotalSizeFormatted => FormatFileSize(TotalSize);

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
/// Hourly activity data for peak hours visualization
/// </summary>
public class HourlyActivity
{
    public int Hour { get; set; }
    public int FileCount { get; set; }
    public int MaxCount { get; set; }
    public string HourLabel => $"{Hour:D2}:00";
    public double BarHeight => MaxCount > 0 ? (double)FileCount / MaxCount * 100 : 0;
    public string IntensityColor => GetIntensityColor();

    private string GetIntensityColor()
    {
        var percentage = MaxCount > 0 ? (double)FileCount / MaxCount * 100 : 0;
        return percentage switch
        {
            >= 75 => "#DC2626", // High - Red
            >= 50 => "#F59E0B", // Medium-High - Orange
            >= 25 => "#FCD34D", // Medium - Yellow
            > 0 => "#86EFAC",   // Low - Light Green
            _ => "#F3F4F6"      // None - Gray
        };
    }
}

/// <summary>
/// Priority distribution statistics
/// </summary>
public class PriorityDistribution
{
    public int Priority { get; set; }
    public int Count { get; set; }
    public double Percentage { get; set; }
    public string PriorityLabel => $"Priority {Priority}";
    public string Color => GetPriorityColor();

    private string GetPriorityColor()
    {
        return Priority switch
        {
            1 => "#DC2626", // Critical - Red
            2 => "#F59E0B", // High - Orange
            3 => "#3B82F6", // Medium - Blue
            4 => "#10B981", // Low - Green
            5 => "#6B7280", // Very Low - Gray
            _ => "#9CA3AF"
        };
    }
}

/// <summary>
/// Recent activity item
/// </summary>
public class RecentActivityItem
{
    public string FileName { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public ApprovalStatus Status { get; set; }
    public DateTime Timestamp { get; set; }
    public long FileSize { get; set; }

    public string StatusText => Status.ToString();
    public string StatusColor => Status switch
    {
        ApprovalStatus.Approved => "#16A34A",
        ApprovalStatus.Rejected => "#DC2626",
        ApprovalStatus.Pending => "#F59E0B",
        _ => "#6B7280"
    };
    public string TimeAgo => GetTimeAgo();

    private string GetTimeAgo()
    {
        var span = DateTime.Now - Timestamp;
        return span.TotalMinutes < 1 ? "Just now" :
               span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes} min ago" :
               span.TotalHours < 24 ? $"{(int)span.TotalHours} hours ago" :
               $"{(int)span.TotalDays} days ago";
    }
}

#endregion

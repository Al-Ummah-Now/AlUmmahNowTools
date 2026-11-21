using AlUmmahNow.VisualImporterUno.Shell;
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
    private string _statusMessage = string.Empty;

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
    private string _mostActiveChannel = "–";

    [ObservableProperty]
    private double _avgApprovalTimeHours = 0.0;

    [ObservableProperty]
    private double _approvalTrend = 0.0;

    [ObservableProperty]
    private double _rejectionTrend = 0.0;

    // Collections for charts and lists
    public string TotalApprovalsText => Loc.Count(TotalApprovals);
    public string ApprovalRateText => Loc.Percent(ApprovalRate);
    /// <summary>How long the oldest video has waited (AvgApprovalTimeHours holds it; the name predates the change).</summary>
    public string AvgApprovalTimeText => AvgApprovalTimeHours <= 0 ? "–" : Loc.Duration(TimeSpan.FromHours(AvgApprovalTimeHours));
    public string FilesPendingText => Loc.Number(FilesPending);
    public string TotalSizeApprovedText => Loc.Bytes(TotalSizeApproved);
    public string ApprovalTrendText => ApprovalTrend == 0 ? string.Empty
        : Loc.Format(ApprovalTrend > 0 ? "Stats_TrendUp" : "Stats_TrendDown", Loc.Percent(Math.Abs(ApprovalTrend)));
    public bool IsToday => SelectedPeriod == TimePeriod.Today;
    public bool IsThisWeek => SelectedPeriod == TimePeriod.ThisWeek;
    public bool IsThisMonth => SelectedPeriod == TimePeriod.ThisMonth;
    public bool IsAllTime => SelectedPeriod == TimePeriod.AllTime;
    public bool HasRecentActivity => RecentActivities.Count > 0;

    partial void OnTotalApprovalsChanged(int value) => OnPropertyChanged(nameof(TotalApprovalsText));
    partial void OnApprovalRateChanged(double value) => OnPropertyChanged(nameof(ApprovalRateText));
    partial void OnAvgApprovalTimeHoursChanged(double value) => OnPropertyChanged(nameof(AvgApprovalTimeText));
    partial void OnFilesPendingChanged(int value) => OnPropertyChanged(nameof(FilesPendingText));
    partial void OnTotalSizeApprovedChanged(long value) => OnPropertyChanged(nameof(TotalSizeApprovedText));
    partial void OnApprovalTrendChanged(double value) => OnPropertyChanged(nameof(ApprovalTrendText));
    partial void OnSelectedPeriodChanged(TimePeriod value)
    {
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(IsThisWeek));
        OnPropertyChanged(nameof(IsThisMonth));
        OnPropertyChanged(nameof(IsAllTime));
    }

    public ObservableCollection<DailyTrendData> ApprovalTrends { get; } = new();
    public ObservableCollection<ChannelStatistic> TopChannels { get; } = new();
    public ObservableCollection<HourlyActivity> PeakHours { get; } = new();
    public ObservableCollection<PriorityDistribution> PriorityStats { get; } = new();
    public ObservableCollection<RecentActivityItem> RecentActivities { get; } = new();

    #endregion

    public AnalyticsDashboardViewModel(FileScanSignalRClient signalRClient)
    {
        _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
        RecentActivities.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentActivity));

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

    }

    #region Commands

    [RelayCommand(CanExecute = nameof(CanRefreshAnalytics))]
    private async Task RefreshAnalyticsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = Loc.Get("Stats_Loading");

            if (!_signalRClient.IsConnected)
            {
                // The page shows the scanner-off card instead
                StatusMessage = string.Empty;
                return;
            }

            // Load statistics from service
            var stats = await _signalRClient.GetApprovalStatisticsAsync();
            var allApprovals = await _signalRClient.GetPendingApprovalsAsync();

            // Calculate analytics
            await CalculateAnalyticsAsync(stats, allApprovals);

            DispatcherExecute(() =>
            {
                StatusMessage = Loc.Format("Stats_UpdatedAt", Loc.Time(DateTime.Now));
            });
        }
        catch (Exception ex)
        {
            DispatcherExecute(() =>
            {
                StatusMessage = Loc.GetWithDetail("Stats_LoadFailed", ex.Message);
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
            StatusMessage = Loc.Get("Stats_Exporting");

            // Generate CSV report
            var csv = GenerateCsvReport();

            // Save to documents folder
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var fileName = $"ApprovalAnalytics_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var filePath = Path.Combine(documentsPath, fileName);

            await File.WriteAllTextAsync(filePath, csv);

            StatusMessage = Loc.GetWithDetail("Stats_Exported", filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.GetWithDetail("Stats_ExportFailed", ex.Message);
        }
    }

    #endregion

    #region Analytics Calculations

    private async Task CalculateAnalyticsAsync(ApprovalStatistics stats, List<PendingApprovalDto> allApprovals)
    {
        await Task.Run(() =>
        {
            // Everything but the trend and what's waiting right now covers only the chosen period
            var inPeriod = SelectedPeriod == TimePeriod.AllTime
                ? allApprovals
                : allApprovals.Where(a => Local(a) >= PeriodStartDate && Local(a) <= PeriodEndDate).ToList();

            CalculateOverviewStatistics(stats, inPeriod);
            CalculateTrends(allApprovals);
            CalculateTopChannels(inPeriod);
            CalculatePeakHours(inPeriod);
            CalculatePriorityDistribution(inPeriod);
            GetRecentActivities(inPeriod);
        });
    }

    private static DateTime Local(PendingApprovalDto approval) => approval.DetectedAt.ToLocalTime();

    private void CalculateOverviewStatistics(ApprovalStatistics stats, List<PendingApprovalDto> inPeriod)
    {
        var approved = inPeriod.Where(a => a.Status == ApprovalStatus.Approved).ToList();
        var rejected = inPeriod.Count(a => a.Status == ApprovalStatus.Rejected);
        var decided = approved.Count + rejected;
        DispatcherExecute(() =>
        {
            TotalApprovals = approved.Count;
            TotalSizeApproved = approved.Sum(a => a.FileSize);
            // Out of the videos decided in the period
            ApprovalRate = decided == 0 ? 0.0 : approved.Count * 100.0 / decided;
            RejectionRate = decided == 0 ? 0.0 : rejected * 100.0 / decided;
            // Waiting is about right now, whatever the period
            FilesPending = stats.TotalPending;

            // How long the oldest video has been waiting for a decision
            AvgApprovalTimeHours = stats.OldestPending.HasValue
                ? Math.Max(0, (DateTime.UtcNow - stats.OldestPending.Value.ToUniversalTime()).TotalHours)
                : 0.0;
        });
    }

    /// <summary>
    /// Videos that arrived each day of the period by what happened to them, and how arrivals compare with the period of
    /// the same length before it. All Time has nothing before it, so it has no trend.
    /// </summary>
    private void CalculateTrends(List<PendingApprovalDto> allApprovals)
    {
        var days = GetDaysInPeriod();
        // All Time charts its last 30 days
        var start = SelectedPeriod == TimePeriod.AllTime ? DateTime.Today.AddDays(1 - days) : PeriodStartDate.Date;
        var arrivals = allApprovals.Select(a => (Day: a.DetectedAt.ToLocalTime().Date, a.Status)).ToList();

        var current = arrivals.Count(a => a.Day >= start && a.Day < start.AddDays(days));
        var previous = SelectedPeriod == TimePeriod.AllTime ? 0 : arrivals.Count(a => a.Day >= start.AddDays(-days) && a.Day < start);
        var trend = previous == 0 ? 0.0 : (current - previous) * 100.0 / previous;

        var daily = Enumerable.Range(0, days).Select(i =>
        {
            var date = start.AddDays(i);
            var approved = arrivals.Count(a => a.Day == date && a.Status == ApprovalStatus.Approved);
            var rejected = arrivals.Count(a => a.Day == date && a.Status == ApprovalStatus.Rejected);
            var pending = arrivals.Count(a => a.Day == date && a.Status == ApprovalStatus.Pending);
            var total = arrivals.Count(a => a.Day == date);
            return new DailyTrendData { Date = date, Approved = approved, Rejected = rejected, Pending = pending, Total = total };
        }).ToList();
        var busiest = Math.Max(1, daily.Max(d => Math.Max(d.Approved, Math.Max(d.Rejected, d.Pending))));
        foreach (var day in daily)
        {
            day.ApprovedHeight = 140.0 * day.Approved / busiest;
            day.RejectedHeight = 140.0 * day.Rejected / busiest;
            day.PendingHeight = 140.0 * day.Pending / busiest;
        }

        DispatcherExecute(() =>
        {
            ApprovalTrend = trend;
            RejectionTrend = 0;
            ApprovalTrends.Clear();
            foreach (var day in daily) ApprovalTrends.Add(day);
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

            MostActiveChannel = TopChannels.FirstOrDefault()?.ChannelName ?? "–";
        });
    }

    private void CalculatePeakHours(List<PendingApprovalDto> allApprovals)
    {
        var hourlyGroups = allApprovals
            .GroupBy(a => Local(a).Hour)
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
            TimePeriod.AllTime => 30, // charted as the last 30 days
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
    public int Pending { get; set; }
    public int Total { get; set; }
    public string DateLabel
    {
        get
        {
            var text = Date.ToString("d MMM", System.Globalization.CultureInfo.CurrentUICulture);
            return Loc.IsArabic ? Loc.ToArabicDigits(text) : text;
        }
    }
    public double ApprovedPercentage => Total > 0 ? (double)Approved / Total * 100 : 0;
    public double RejectedPercentage => Total > 0 ? (double)Rejected / Total * 100 : 0;
    /// <summary>Bar heights in pixels for a 140px chart.</summary>
    public double ApprovedHeight { get; set; }
    public double RejectedHeight { get; set; }
    public double PendingHeight { get; set; }
    public string TotalText => Loc.Number(Total);
}

public class ChannelStatistic
{
    public string ChannelName { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int ApprovedFiles { get; set; }
    public int RejectedFiles { get; set; }
    public int PendingFiles { get; set; }
    public long TotalSize { get; set; }
    public double ApprovalRate => TotalFiles > 0 ? (double)ApprovedFiles / TotalFiles * 100 : 0;
    public string TotalSizeFormatted => Loc.Bytes(TotalSize);
    public string SummaryText => Loc.Format("Stats_ChannelSummary", TotalFiles, Loc.Bytes(TotalSize));
    public string ApprovalRateText => Loc.Percent(ApprovalRate);
}

public class HourlyActivity
{
    public int Hour { get; set; }
    public int FileCount { get; set; }
    public int MaxCount { get; set; }
    public string HourLabel => Loc.IsArabic ? Loc.ToArabicDigits(Hour.ToString("D2")) : Hour.ToString("D2");
    public string FileCountText => Loc.Count(FileCount);
    public double BarHeight => MaxCount > 0 ? (double)FileCount / MaxCount * 100 : 0;
    /// <summary>The cell's gold, stronger the busier the hour.</summary>
    // Capped so the hour and count stay readable on the busiest hour
    public double Intensity => MaxCount > 0 ? 0.1 + 0.5 * FileCount / MaxCount : 0.06;
}

public class PriorityDistribution
{
    public int Priority { get; set; }
    public int Count { get; set; }
    public double Percentage { get; set; }
    public string PriorityLabel => Loc.Format("Stats_Priority", Priority);
    public string CountText => Loc.Format("Stats_PriorityCount", Count, Loc.Percent(Percentage));
}

public class RecentActivityItem
{
    public string FileName { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public ApprovalStatus Status { get; set; }
    public DateTime Timestamp { get; set; }
    public long FileSize { get; set; }
    public string StatusText => Loc.Get(Status switch
    {
        ApprovalStatus.Approved => "Stats_StatusApproved",
        ApprovalStatus.Rejected => "Stats_StatusRejected",
        ApprovalStatus.Pending => "Stats_StatusPending",
        _ => "Stats_StatusOther",
    });
    public string StatusColor => Status switch
    {
        ApprovalStatus.Approved => "#FF2FA866",
        ApprovalStatus.Rejected => "#FFE0605A",
        ApprovalStatus.Pending => "#FFE0962A",
        _ => "#FF7F97A3",
    };
    public string TimeText => Loc.When(Timestamp.Kind == DateTimeKind.Utc ? Timestamp.ToLocalTime() : Timestamp);
}

#endregion

using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.VisualImporterUno.Services;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The folder scanner as screenshot runs see it (TOOLS_DEMO=1 with TOOLS_DEMO_SCANNER=1): connected, with a fixed set
/// of watched folders and videos so every run shows the same pages. Times are fixed hours on fixed days before today.
/// </summary>
public sealed class DemoScannerClient : FileScanSignalRClient
{
    private static readonly (string Channel, string Folder)[] Channels =
    {
        ("Seerah Series", "/Volumes/Media/Seerah"),
        ("Friday Khutbah", "/Volumes/Media/Khutbah"),
        ("Quran Recitation", "/Volumes/Media/Recitation"),
        ("Youth Circle", "/Volumes/Media/Youth"),
    };

    private readonly List<PendingApprovalDto> _approvals;
    private readonly List<WatchStatusDto> _watches;

    public DemoScannerClient(string serviceUrl) : base(serviceUrl)
    {
        _approvals = Seed();
        var today = DateTime.Today;
        _watches = Channels.Select((c, i) => new WatchStatusDto
        {
            WatchId = new Guid(i + 1, 0, 0, new byte[8]),
            ChannelName = c.Channel,
            WatchPath = c.Folder,
            IsActive = i != 3,
            CreatedAt = today.AddDays(-40 + i * 3).AddHours(9),
            LastFileDetected = _approvals.Where(a => a.ChannelName == c.Channel).Max(a => (DateTime?)a.DetectedAt),
            TotalFilesDetected = _approvals.Count(a => a.ChannelName == c.Channel),
            PendingApprovals = _approvals.Count(a => a.ChannelName == c.Channel && a.Status == ApprovalStatus.Pending),
        }).ToList();
    }

    public static bool Requested =>
        Environment.GetEnvironmentVariable("TOOLS_DEMO") == "1" && Environment.GetEnvironmentVariable("TOOLS_DEMO_SCANNER") == "1";

    public override bool IsConnected => true;

    public override Task<bool> ConnectAsync()
    {
        RaiseServiceStatus(ServiceStatus.Connected);
        return Task.FromResult(true);
    }

    public override Task<List<WatchStatusDto>> GetActiveWatchesAsync() => Task.FromResult(_watches.ToList());

    public override Task<List<PendingApprovalDto>> GetPendingApprovalsAsync() => Task.FromResult(_approvals.ToList());

    public override Task<ApprovalStatistics> GetApprovalStatisticsAsync(Guid? channelWatchId = null)
    {
        var pending = _approvals.Where(a => a.Status == ApprovalStatus.Pending).ToList();
        return Task.FromResult(new ApprovalStatistics
        {
            TotalPending = pending.Count,
            TotalApproved = _approvals.Count(a => a.Status == ApprovalStatus.Approved),
            TotalRejected = _approvals.Count(a => a.Status == ApprovalStatus.Rejected),
            OldestPending = pending.Min(a => (DateTime?)a.DetectedAt),
            NewestPending = pending.Max(a => (DateTime?)a.DetectedAt),
            TotalSizePending = pending.Sum(a => a.FileSize),
            TotalSizeApproved = _approvals.Where(a => a.Status == ApprovalStatus.Approved).Sum(a => a.FileSize),
        });
    }

    public override Task<bool> ProcessApprovalDecisionAsync(ApprovalDecisionRequest request)
    {
        foreach (var item in _approvals.Where(a => request.ApprovalIds.Contains(a.Id))) item.Status = request.Decision;
        return Task.FromResult(true);
    }

    public override Task<int> ProcessBulkApprovalAsync(BulkApprovalRequest request)
    {
        var matched = _approvals.Where(a => a.Status == ApprovalStatus.Pending && (request.ChannelName is null || a.ChannelName == request.ChannelName)).ToList();
        foreach (var item in matched) item.Status = request.Decision;
        return Task.FromResult(matched.Count);
    }

    private static List<PendingApprovalDto> Seed()
    {
        // (days ago, hour, channel index, file, size in MB, status, priority)
        var rows = new (int Days, int Hour, int Channel, string File, int Mb, ApprovalStatus Status, int Priority)[]
        {
            (0, 9, 0, "seerah-episode-24.mp4", 1840, ApprovalStatus.Pending, 2),
            (0, 11, 1, "khutbah-patience-in-hardship.mp4", 920, ApprovalStatus.Pending, 1),
            (0, 14, 2, "surah-al-kahf-full.mp4", 2410, ApprovalStatus.Pending, 2),
            (1, 10, 0, "seerah-episode-23.mp4", 1795, ApprovalStatus.Pending, 2),
            (1, 16, 3, "youth-circle-honesty.mp4", 640, ApprovalStatus.Pending, 3),
            (1, 20, 2, "surah-yasin.mp4", 860, ApprovalStatus.Approved, 1),
            (2, 9, 1, "khutbah-gratitude.mp4", 905, ApprovalStatus.Approved, 1),
            (2, 13, 0, "seerah-episode-22.mp4", 1820, ApprovalStatus.Approved, 2),
            (2, 18, 3, "youth-circle-q-and-a.mp4", 1210, ApprovalStatus.Rejected, 3),
            (3, 11, 2, "surah-ar-rahman.mp4", 780, ApprovalStatus.Approved, 1),
            (3, 15, 0, "seerah-episode-21.mp4", 1760, ApprovalStatus.Approved, 2),
            (4, 10, 1, "khutbah-family-ties.mp4", 940, ApprovalStatus.Approved, 1),
            (4, 14, 2, "surah-al-mulk.mp4", 520, ApprovalStatus.Approved, 1),
            (4, 19, 3, "youth-circle-rough-cut.mp4", 2980, ApprovalStatus.Rejected, 3),
            (5, 9, 0, "seerah-episode-20.mp4", 1805, ApprovalStatus.Approved, 2),
            (5, 12, 1, "khutbah-mercy.mp4", 910, ApprovalStatus.Approved, 1),
            (6, 11, 2, "surah-al-waqiah.mp4", 640, ApprovalStatus.Approved, 1),
            (6, 16, 0, "seerah-episode-19.mp4", 1790, ApprovalStatus.Approved, 2),
            (8, 10, 1, "khutbah-trust.mp4", 895, ApprovalStatus.Approved, 1),
            (9, 14, 0, "seerah-episode-18.mp4", 1770, ApprovalStatus.Approved, 2),
            (11, 9, 2, "surah-maryam.mp4", 1020, ApprovalStatus.Approved, 1),
            (13, 15, 3, "youth-circle-duplicate.mp4", 640, ApprovalStatus.Rejected, 3),
        };
        var today = DateTime.Today;
        return rows.Select((r, i) =>
        {
            var at = today.AddDays(-r.Days).AddHours(r.Hour).AddMinutes(7 * (i % 8));
            return new PendingApprovalDto
            {
                Id = new Guid(100 + i, 0, 0, new byte[8]),
                ChannelName = Channels[r.Channel].Channel,
                FileName = r.File,
                FilePath = Path.Combine(Channels[r.Channel].Folder, r.File),
                FileSize = r.Mb * 1024L * 1024L,
                Extension = ".mp4",
                DetectedAt = at,
                FileModifiedAt = at.AddMinutes(-3),
                Status = r.Status,
                Priority = r.Priority,
                FileExists = true,
            };
        }).ToList();
    }
}

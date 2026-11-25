using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.Data.Services;
using AlUmmahNow.Data.Video;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.VisualImporterUno.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

/// <summary>Approve all and Reject all decide exactly what the page lists under its filters, and nothing else.</summary>
public sealed class ApprovalsDecideListedTests
{
    private const int High = 1, Normal = 3, Low = 5;

    /// <summary>The scanner as it decides: by id, or everything waiting that its own filters (which have no priority) match.</summary>
    private sealed class Scanner
    {
        public readonly List<PendingApprovalDto> Files = new();
        public readonly Mock<FileScanSignalRClient> Client = new("http://127.0.0.1:59998");
        public readonly List<int> Batches = new();
        public int RefuseAfter = int.MaxValue;

        public Scanner()
        {
            Client.Setup(s => s.IsConnected).Returns(true);
            // The page is asked to look again after a decision; it keeps what the test put there
            Client.Setup(s => s.GetPendingApprovalsAsync()).ThrowsAsync(new InvalidOperationException("not in this test"));
            Client.Setup(s => s.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>())).ReturnsAsync((ApprovalDecisionRequest request) =>
            {
                if (Batches.Count >= RefuseAfter) throw new InvalidOperationException("scanner gone");
                Batches.Add(request.ApprovalIds.Count);
                return Decide(Files.Where(f => request.ApprovalIds.Contains(f.Id)), request.Decision) > 0;
            });
            Client.Setup(s => s.ProcessBulkApprovalAsync(It.IsAny<BulkApprovalRequest>())).ReturnsAsync((BulkApprovalRequest request) =>
                Decide(Files.Where(f => request.ChannelName is null || f.ChannelName == request.ChannelName), request.Decision));
        }

        private static int Decide(IEnumerable<PendingApprovalDto> files, ApprovalStatus decision)
        {
            var waiting = files.Where(f => f.Status == ApprovalStatus.Pending).ToList();
            foreach (var file in waiting) file.Status = decision;
            return waiting.Count;
        }

        public PendingApprovalDto Add(string name, int priority, ApprovalStatus status = ApprovalStatus.Pending)
        {
            var file = new PendingApprovalDto { Id = Guid.NewGuid(), FileName = name, ChannelName = "Seerah", Extension = ".mp4", Priority = priority, Status = status };
            Files.Add(file);
            return file;
        }

        /// <summary>The page, having loaded what the scanner has now (its own copies, as over the wire).</summary>
        public ApprovalViewModel Page()
        {
            var page = new ApprovalViewModel(Client.Object, new Mock<IFilterPresetService>().Object, new Mock<INotificationService>().Object, new VideoThumbnailService(NullLogger<VideoThumbnailService>.Instance)) { EnableNotifications = false };
            foreach (var f in Files)
                page.PendingApprovals.Add(new PendingApprovalDto { Id = f.Id, FileName = f.FileName, ChannelName = f.ChannelName, Extension = f.Extension, Priority = f.Priority, Status = f.Status });
            return page;
        }
    }

    private static Task RunAsync(ApprovalViewModel page, bool approve) =>
        approve ? page.BulkApproveCommand.ExecuteAsync(null) : page.BulkRejectCommand.ExecuteAsync(null);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithOnlyAPriorityChosen_OnlyThatPriorityIsDecided(bool approve)
    {
        var scanner = new Scanner();
        var (high, low) = (scanner.Add("high.mp4", High), scanner.Add("low.mp4", Low));
        var page = scanner.Page();
        page.PriorityFilter = High;
        page.FilteredApprovals.Should().ContainSingle().Which.Id.Should().Be(high.Id);
        await RunAsync(page, approve);
        high.Status.Should().Be(approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected);
        low.Status.Should().Be(ApprovalStatus.Pending);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFileThatCameAfterTheListWasLoaded_IsNotDecided(bool approve)
    {
        var scanner = new Scanner();
        var seen = scanner.Add("seen.mp4", Normal);
        var page = scanner.Page();
        page.ChannelFilter = "Seerah";
        var later = scanner.Add("later.mp4", Normal);
        await RunAsync(page, approve);
        seen.Status.Should().Be(approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected);
        later.Status.Should().Be(ApprovalStatus.Pending);
    }

    [Fact]
    public async Task AFileDecidedAlready_IsNotDecidedAgain_AndNothingListedAsksNothing()
    {
        var scanner = new Scanner();
        var done = scanner.Add("done.mp4", High, ApprovalStatus.Approved);
        var page = scanner.Page();
        page.ShowOnlyPending = false;
        page.PriorityFilter = High;
        page.FilteredApprovals.Should().ContainSingle();
        await RunAsync(page, approve: false);
        done.Status.Should().Be(ApprovalStatus.Approved);
        scanner.Batches.Should().BeEmpty();
        scanner.Client.Verify(s => s.ProcessBulkApprovalAsync(It.IsAny<BulkApprovalRequest>()), Times.Never);
    }

    [Fact]
    public async Task ManyFiles_GoInParts_TheScannersMessageLimitTakes()
    {
        var scanner = new Scanner();
        for (var i = 0; i < 1050; i++) scanner.Add($"f{i}.mp4", High);
        var other = scanner.Add("other.mp4", Low);
        var page = scanner.Page();
        page.PriorityFilter = High;
        await RunAsync(page, approve: true);
        scanner.Files.Where(f => f.Priority == High).Should().OnlyContain(f => f.Status == ApprovalStatus.Approved);
        other.Status.Should().Be(ApprovalStatus.Pending);
        // An id is under 50 characters of the message, whose limit is 32 KB
        scanner.Batches.Sum().Should().Be(1050);
        scanner.Batches.Max().Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public async Task AScannerThatGoesHalfWay_LeavesTheRestWaiting_AndThePageSaysSo()
    {
        var scanner = new Scanner { RefuseAfter = 1 };
        for (var i = 0; i < 1050; i++) scanner.Add($"f{i}.mp4", High);
        var page = scanner.Page();
        page.PriorityFilter = High;
        await RunAsync(page, approve: false);
        scanner.Files.Count(f => f.Status == ApprovalStatus.Rejected).Should().BeInRange(1, 500);
        page.ActivityLog.Should().Contain(l => l.Message.Contains("scanner gone", StringComparison.Ordinal));
    }
}

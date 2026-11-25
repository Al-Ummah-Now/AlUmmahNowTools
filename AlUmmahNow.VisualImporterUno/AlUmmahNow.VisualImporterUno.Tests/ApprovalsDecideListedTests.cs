using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.Data.Services;
using AlUmmahNow.Data.Video;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

/// <summary>Approve all and Reject all decide exactly what the page lists under its filters, and nothing else.</summary>
[Collection("CultureSensitive")]
public sealed class ApprovalsDecideListedTests : IDisposable
{
    public ApprovalsDecideListedTests() => ConfirmDialog.Answer = _ => Task.FromResult(true);

    public void Dispose() => ConfirmDialog.Answer = null;

    private const int High = 1, Normal = 3, Low = 5;

    /// <summary>The scanner as it decides: by id, or everything waiting that its own filters (which have no priority) match.</summary>
    private sealed class Scanner
    {
        public readonly List<PendingApprovalDto> Files = new();
        public readonly Mock<FileScanSignalRClient> Client = new("http://127.0.0.1:59998");
        public readonly List<int> Batches = new();
        public readonly Mock<INotificationService> Notifications = new();
        public int RefuseAfter = int.MaxValue;
        /// <summary>The part that fails inside the scanner, which answers false then.</summary>
        public int FailPart = -1;
        private bool _loads;

        public Scanner()
        {
            Client.Setup(s => s.IsConnected).Returns(true);
            // What it lists, unless it is gone
            Client.Setup(s => s.GetPendingApprovalsAsync()).ReturnsAsync(() => _loads || Batches.Count < RefuseAfter
                ? Files.Where(f => f.Status == ApprovalStatus.Pending).Select(Copy).ToList()
                : throw new InvalidOperationException("scanner gone"));
            Client.Setup(s => s.GetApprovalStatisticsAsync(It.IsAny<Guid?>())).ReturnsAsync(new ApprovalStatistics());
            Client.Setup(s => s.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>())).ReturnsAsync((ApprovalDecisionRequest request) =>
            {
                if (Batches.Count >= RefuseAfter) throw new InvalidOperationException("scanner gone");
                Batches.Add(request.ApprovalIds.Count);
                if (Batches.Count - 1 == FailPart) return false;
                return Decide(Files.Where(f => request.ApprovalIds.Contains(f.Id)), request.Decision) > 0;
            });
            Client.Setup(s => s.ProcessBulkApprovalAsync(It.IsAny<BulkApprovalRequest>())).ReturnsAsync((BulkApprovalRequest request) =>
                Decide(Files.Where(f => request.ChannelName is null || f.ChannelName == request.ChannelName), request.Decision));
        }

        /// <summary>The connection's state has changed.</summary>
        public void Reports() => typeof(FileScanSignalRClient).GetMethod("RaiseServiceStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(Client.Object, [ServiceStatus.Disconnected]);

        /// <summary>It goes, but answers again by the time the list is loaded.</summary>
        public void ComesBack() => _loads = true;

        private static PendingApprovalDto Copy(PendingApprovalDto f) =>
            new() { Id = f.Id, FileName = f.FileName, ChannelName = f.ChannelName, Extension = f.Extension, Priority = f.Priority, Status = f.Status };

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
            var page = new ApprovalViewModel(Client.Object, new Mock<IFilterPresetService>().Object, Notifications.Object, new VideoThumbnailService(NullLogger<VideoThumbnailService>.Instance), start: false) { EnableNotifications = false };
            foreach (var f in Files) page.PendingApprovals.Add(Copy(f));
            return page;
        }
    }

    private static async Task RunAsync(ApprovalViewModel page, bool approve)
    {
        var command = approve ? page.BulkApproveCommand : page.BulkRejectCommand;
        command.CanExecute(null).Should().BeTrue();
        await command.ExecuteAsync(null);
    }

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AScannerThatGoesHalfWay_ThePageSaysHowManyAreDecided_AndListsTheRest(bool comesBack)
    {
        var scanner = new Scanner { RefuseAfter = 1 };
        for (var i = 0; i < 1050; i++) scanner.Add($"f{i}.mp4", High);
        if (comesBack) scanner.ComesBack();
        var page = scanner.Page();
        page.PriorityFilter = High;
        await RunAsync(page, approve: false);
        scanner.Files.Count(f => f.Status == ApprovalStatus.Rejected).Should().Be(500);
        // What was decided leaves the list, also when it cannot be loaded again
        page.FilteredApprovals.Should().HaveCount(550).And.OnlyContain(a => a.Status == ApprovalStatus.Pending);
        page.FilteredApprovals.Select(a => a.Id).Should().BeEquivalentTo(scanner.Files.Where(f => f.Status == ApprovalStatus.Pending).Select(f => f.Id));
        page.StatusMessage.Should().Contain("Approve_RejectedMany").And.Contain(comesBack ? "Approve_RejectManyFailed" : "Approve_RestUnknown").And.Contain("scanner gone");
    }

    [Fact]
    public async Task APartThatFailsInTheScanner_IsNotCountedAsDecided()
    {
        var scanner = new Scanner { FailPart = 1 };
        for (var i = 0; i < 1050; i++) scanner.Add($"f{i}.mp4", High);
        var page = scanner.Page();
        page.PriorityFilter = High;
        await RunAsync(page, approve: true);
        page.FilteredApprovals.Should().HaveCount(500);
        page.StatusMessage.Should().Contain("Approve_ApprovedMany").And.Contain("Approve_ApproveManyFailed");
        page.ActivityLog[0].Type.Should().Be(LogType.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OneDecision_IsOneNotification(bool whole)
    {
        var scanner = new Scanner { FailPart = whole ? -1 : 1 };
        for (var i = 0; i < 1050; i++) scanner.Add($"f{i}.mp4", High);
        var page = scanner.Page();
        page.EnableNotifications = true;
        page.PriorityFilter = High;
        await RunAsync(page, approve: true);
        scanner.Notifications.Verify(n => n.ShowApprovalProcessedAsync(string.Empty, true, 1050), Times.Exactly(whole ? 1 : 0));
        scanner.Notifications.Verify(n => n.ShowErrorAsync("Approve_ApproveFailed", It.Is<string>(t => t.Contains("Approve_ApprovedMany") && t.Contains("Approve_ApproveManyFailed"))), Times.Exactly(whole ? 0 : 1));
        scanner.Notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EverythingDecidedElsewhereMeanwhile_IsSaid_AndLeavesTheList()
    {
        var scanner = new Scanner();
        var file = scanner.Add("f.mp4", High);
        var page = scanner.Page();
        page.PriorityFilter = High;
        file.Status = ApprovalStatus.Rejected;
        await RunAsync(page, approve: true);
        file.Status.Should().Be(ApprovalStatus.Rejected);
        page.FilteredApprovals.Should().BeEmpty();
        page.StatusMessage.Should().Be("Approve_DecidedAlready");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSelected_GoInPartsToo_AndOnlyThoseWaiting(bool approve)
    {
        var scanner = new Scanner();
        for (var i = 0; i < 1050; i++) scanner.Add($"f{i}.mp4", High);
        var done = scanner.Add("done.mp4", High, ApprovalStatus.Rejected);
        var page = scanner.Page();
        page.SetSelection(page.PendingApprovals.ToList());
        await (approve ? page.ApproveSelectedCommand : page.RejectSelectedCommand).ExecuteAsync(null);
        scanner.Batches.Sum().Should().Be(1050);
        scanner.Batches.Max().Should().BeLessThanOrEqualTo(500);
        done.Status.Should().Be(ApprovalStatus.Rejected);
        scanner.Files.Where(f => f != done).Should().OnlyContain(f => f.Status == (approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected));
    }

    [Fact]
    public async Task TheButtons_FollowTheFilters_TheConnection_AndADecisionUnderWay()
    {
        var scanner = new Scanner();
        scanner.Add("f.mp4", High);
        var page = scanner.Page();
        var told = 0;
        page.BulkApproveCommand.CanExecuteChanged += (_, _) => told++;
        page.BulkApproveCommand.CanExecute(null).Should().BeFalse("nothing is filtered");
        page.ChannelFilter = MainViewModel.AllChannels;
        page.BulkApproveCommand.CanExecute(null).Should().BeFalse("every channel is no filter");
        told = 0;
        page.PriorityFilter = High;
        told.Should().BeGreaterThan(0);
        page.BulkApproveCommand.CanExecute(null).Should().BeTrue();

        told = 0;
        scanner.Client.Setup(s => s.IsConnected).Returns(false);
        scanner.Reports();
        told.Should().BeGreaterThan(0);
        page.BulkApproveCommand.CanExecute(null).Should().BeFalse();
        scanner.Client.Setup(s => s.IsConnected).Returns(true);

        // While one decides, no button does
        var answer = new TaskCompletionSource<bool>();
        scanner.Client.Setup(s => s.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>())).Returns(answer.Task);
        page.SetSelection(page.PendingApprovals.ToList());
        told = 0;
        var deciding = page.BulkRejectCommand.ExecuteAsync(null);
        told.Should().BeGreaterThan(0);
        foreach (var command in new[] { page.BulkApproveCommand, page.BulkRejectCommand, page.ApproveSelectedCommand, page.RejectSelectedCommand })
            command.CanExecute(null).Should().BeFalse();
        answer.SetResult(false);
        await deciding;
        page.BulkApproveCommand.CanExecute(null).Should().BeTrue();
    }
}

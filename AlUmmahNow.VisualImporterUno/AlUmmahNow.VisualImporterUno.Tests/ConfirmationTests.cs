using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.Data.Services;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

/// <summary>What cannot simply be undone asks first, and says what it does to how many (Tools #46).</summary>
[Collection("CultureSensitive")]
public sealed class ConfirmationTests : IDisposable
{
    private readonly List<ConfirmRequest> _asked = new();
    private readonly List<ApprovalDecisionRequest> _sent = new();
    private readonly Mock<IFilterPresetService> _presets = new();
    private readonly Mock<FileScanSignalRClient> _scanner;
    private bool _yes;

    public ConfirmationTests()
    {
        ConfirmDialog.Answer = request =>
        {
            _asked.Add(request);
            return Task.FromResult(_yes);
        };
        _scanner = new Mock<FileScanSignalRClient>("http://127.0.0.1:59998") { CallBase = false };
        _scanner.Setup(s => s.IsConnected).Returns(true);
        _scanner.Setup(s => s.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>())).ReturnsAsync(true).Callback<ApprovalDecisionRequest>(_sent.Add);
        _scanner.Setup(s => s.GetPendingApprovalsAsync()).ReturnsAsync(new List<PendingApprovalDto>());
        _scanner.Setup(s => s.GetApprovalStatisticsAsync(It.IsAny<Guid?>())).ReturnsAsync(new ApprovalStatistics());
        _presets.Setup(p => p.GetAllPresetsAsync()).ReturnsAsync(new List<FilterPreset>());
    }

    public void Dispose() => ConfirmDialog.Answer = null;

    private ApprovalViewModel Page(int files)
    {
        var page = new ApprovalViewModel(_scanner.Object, _presets.Object, new Mock<INotificationService>().Object, new AlUmmahNow.Data.Video.VideoThumbnailService(NullLogger<AlUmmahNow.Data.Video.VideoThumbnailService>.Instance), start: false) { EnableNotifications = false };
        page.FilteredApprovals.Clear();
        for (var i = 0; i < files; i++) page.FilteredApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), FileName = $"f{i}.mp4", Status = ApprovalStatus.Pending });
        return page;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectSelected_AsksFirst_AndRejectsOnlyOnYes(bool yes)
    {
        _yes = yes;
        var page = Page(3);
        page.SetSelection(page.FilteredApprovals.Take(2).ToList());
        await page.RejectSelectedCommand.ExecuteAsync(null);
        var question = _asked.Should().ContainSingle().Subject;
        question.Danger.Should().BeTrue();
        question.TypeNumber.Should().BeNull();
        _sent.Should().HaveCount(yes ? 1 : 0);
        if (yes) _sent[0].ApprovalIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task RejectOne_NamesTheFile()
    {
        var page = Page(2);
        await page.RejectOneCommand.ExecuteAsync(page.FilteredApprovals[1]);
        _asked.Should().ContainSingle().Which.Lines[0].Should().Be("f1.mp4");
        _sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(9, null)]
    [InlineData(10, 10)]
    [InlineData(24, 24)]
    public async Task RejectAll_IsTypedFromTenOn_AndRejectsExactlyWhatIsListed(int files, int? typed)
    {
        _yes = true;
        var page = Page(files);
        page.FilteredApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), FileName = "done.mp4", Status = ApprovalStatus.Approved });
        var listed = page.FilteredApprovals.Where(a => a.Status == ApprovalStatus.Pending).Select(a => a.Id).ToList();
        // The priority filter alone: the scanner's own filters have no priority, and would have taken every file it has
        page.PriorityFilter = 1;
        page.FilteredApprovals.Clear();
        foreach (var id in listed) page.FilteredApprovals.Add(new PendingApprovalDto { Id = id, FileName = "f.mp4", Status = ApprovalStatus.Pending });
        await page.BulkRejectCommand.ExecuteAsync(null);
        _asked.Should().ContainSingle().Which.TypeNumber.Should().Be(typed);
        _sent.Should().ContainSingle().Which.ApprovalIds.Should().BeEquivalentTo(listed);
        _sent[0].Decision.Should().Be(ApprovalStatus.Rejected);
        _scanner.Verify(s => s.ProcessBulkApprovalAsync(It.IsAny<BulkApprovalRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApproveAll_AsksWithTheCount_AndIsNoDanger(bool yes)
    {
        _yes = yes;
        var page = Page(24);
        await page.BulkApproveCommand.ExecuteAsync(null);
        var question = _asked.Should().ContainSingle().Subject;
        question.Danger.Should().BeFalse();
        question.TypeNumber.Should().BeNull();
        question.Title.Should().Be("Approve_ApproveManyTitle", "the tests have no resources; the review frames show the count");
        _sent.Should().HaveCount(yes ? 1 : 0);
    }

    [Fact]
    public async Task NothingListed_AsksNothing()
    {
        var page = Page(0);
        await page.BulkRejectCommand.ExecuteAsync(null);
        await page.BulkApproveCommand.ExecuteAsync(null);
        _asked.Should().BeEmpty();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Undo_PressedTwice_SavesOnce()
    {
        var preset = new FilterPreset { Name = "Seerah" };
        var saving = new TaskCompletionSource();
        _presets.Setup(p => p.SavePresetAsync(preset)).Returns(saving.Task);
        var page = Page(0);
        await page.DeletePresetCommand.ExecuteAsync(preset);
        var first = page.UndoDeletePresetCommand.ExecuteAsync(null);
        var second = page.UndoDeletePresetCommand.ExecuteAsync(null);
        saving.SetResult();
        await Task.WhenAll(first, second);
        _presets.Verify(p => p.SavePresetAsync(preset), Times.Once);
        page.HasDeletedPreset.Should().BeFalse();
    }

    [Fact]
    public async Task ADecisionThatIsRunning_KeepsItsSelection()
    {
        _yes = true;
        var sending = new TaskCompletionSource<bool>();
        _scanner.Setup(s => s.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>())).Callback<ApprovalDecisionRequest>(_sent.Add).Returns(sending.Task);
        var page = Page(3);
        page.SetSelection(page.FilteredApprovals.Take(2).ToList());
        var first = page.RejectSelectedCommand.ExecuteAsync(null);
        await page.ApproveOneCommand.ExecuteAsync(page.FilteredApprovals[2]);
        await page.RejectOneCommand.ExecuteAsync(page.FilteredApprovals[2]);
        page.SelectedCount.Should().Be(2);
        sending.SetResult(true);
        await first;
        _sent.Should().ContainSingle().Which.ApprovalIds.Should().HaveCount(2);
        _asked.Should().ContainSingle();
    }

    [Fact]
    public async Task ADeletedPreset_IsPutBackByUndo_WithoutAQuestion()
    {
        var preset = new FilterPreset { Name = "Seerah", IsDefault = true };
        var page = Page(0);
        await page.DeletePresetCommand.ExecuteAsync(preset);
        _presets.Verify(p => p.DeletePresetAsync(preset.Id), Times.Once);
        page.HasDeletedPreset.Should().BeTrue();
        page.DeletedPresetText.Should().Contain("Seerah");
        await page.UndoDeletePresetCommand.ExecuteAsync(null);
        _presets.Verify(p => p.SavePresetAsync(preset), Times.Once);
        page.HasDeletedPreset.Should().BeFalse();
        _asked.Should().BeEmpty();
    }
}

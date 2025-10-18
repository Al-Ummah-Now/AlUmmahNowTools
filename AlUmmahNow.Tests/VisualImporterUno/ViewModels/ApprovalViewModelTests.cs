using Xunit;
using Moq;
using FluentAssertions;
using AlUmmahNow.VisualImporterUno.ViewModels;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.FileScanService.Services;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlUmmahNow.Tests.VisualImporterUno.ViewModels;

/// <summary>
/// Comprehensive integration tests for ApprovalViewModel
/// Tests cover approval workflow, filtering, bulk operations, SignalR events, and multi-selection
/// </summary>
public class ApprovalViewModelTests : IAsyncLifetime
{
    private Mock<FileScanSignalRClient> _mockSignalRClient = null!;
    private Mock<DispatcherQueue> _mockDispatcherQueue = null!;
    private ApprovalViewModel _viewModel = null!;

    public Task InitializeAsync()
    {
        // Setup mocks
        _mockSignalRClient = new Mock<FileScanSignalRClient>("http://localhost:5000");
        _mockDispatcherQueue = new Mock<DispatcherQueue>();

        // Setup mock SignalR client to be connected
        _mockSignalRClient.Setup(x => x.IsConnected).Returns(true);
        _mockSignalRClient.Setup(x => x.ConnectAsync()).ReturnsAsync(true);
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(new List<PendingApprovalDto>());
        _mockSignalRClient.Setup(x => x.GetApprovalStatisticsAsync())
            .ReturnsAsync(new ApprovalStatistics());

        // Setup dispatcher queue to execute actions synchronously for testing
        _mockDispatcherQueue.Setup(x => x.HasThreadAccess).Returns(true);

        // Create view model
        _viewModel = new ApprovalViewModel(_mockSignalRClient.Object);

        // Wait a bit for async initialization
        Task.Delay(100).Wait();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _viewModel?.Dispose();
        return Task.CompletedTask;
    }

    #region Constructor and Initialization Tests

    [Fact]
    public void Constructor_WithValidSignalRClient_ShouldInitializeSuccessfully()
    {
        // Arrange & Act
        var viewModel = new ApprovalViewModel(_mockSignalRClient.Object);

        // Assert
        viewModel.Should().NotBeNull();
        viewModel.StatusMessage.Should().Be("Ready");
        viewModel.IsLoading.Should().BeFalse();
        viewModel.PendingApprovals.Should().BeEmpty();
        viewModel.FilteredApprovals.Should().BeEmpty();
        viewModel.SelectedApprovals.Should().BeEmpty();
        viewModel.AvailableChannels.Should().BeEmpty();
        viewModel.ActivityLog.Should().BeEmpty();

        viewModel.Dispose();
    }

    [Fact]
    public void Constructor_WithNullSignalRClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ApprovalViewModel(null!));
    }

    #endregion

    #region Refresh Approvals Tests

    [Fact]
    public async Task RefreshApprovals_WhenConnected_ShouldLoadApprovals()
    {
        // Arrange
        var approvals = new List<PendingApprovalDto>
        {
            new() { Id = Guid.NewGuid(), ChannelName = "Channel1", FileName = "video1.mp4", FileSize = 1024000, Status = ApprovalStatus.Pending, DetectedAt = DateTime.UtcNow, Extension = ".mp4", Priority = 1, FileModifiedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), ChannelName = "Channel2", FileName = "video2.mp4", FileSize = 2048000, Status = ApprovalStatus.Pending, DetectedAt = DateTime.UtcNow, Extension = ".mp4", Priority = 2, FileModifiedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), ChannelName = "Channel1", FileName = "video3.mkv", FileSize = 3072000, Status = ApprovalStatus.Approved, DetectedAt = DateTime.UtcNow, Extension = ".mkv", Priority = 3, FileModifiedAt = DateTime.UtcNow }
        };

        var statistics = new ApprovalStatistics
        {
            TotalPending = 2,
            TotalApproved = 1,
            TotalRejected = 0
        };

        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync()).ReturnsAsync(approvals);
        _mockSignalRClient.Setup(x => x.GetApprovalStatisticsAsync()).ReturnsAsync(statistics);

        // Act
        await _viewModel.RefreshApprovalsCommand.ExecuteAsync(null);

        // Assert
        _viewModel.PendingApprovals.Should().HaveCount(3);
        _viewModel.Statistics.TotalPending.Should().Be(2);
        _viewModel.Statistics.TotalApproved.Should().Be(1);
        _viewModel.AvailableChannels.Should().Contain("Channel1");
        _viewModel.AvailableChannels.Should().Contain("Channel2");
        _viewModel.AvailableChannels.Should().Contain("All Channels");
        _viewModel.StatusMessage.Should().Contain("Loaded 3 pending approvals");
    }

    [Fact]
    public async Task RefreshApprovals_WhenNotConnected_ShouldThrowException()
    {
        // Arrange
        _mockSignalRClient.Setup(x => x.IsConnected).Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _viewModel.RefreshApprovalsCommand.ExecuteAsync(null));
    }

    [Fact]
    public void RefreshApprovalsCommand_WhenLoading_ShouldBeDisabled()
    {
        // Arrange
        _viewModel.GetType().GetProperty("IsLoading")!.SetValue(_viewModel, true);

        // Assert
        _viewModel.RefreshApprovalsCommand.CanExecute(null).Should().BeFalse();
    }

    #endregion

    #region Approve/Reject Single File Tests

    [Fact]
    public async Task ApproveSelected_WithSingleSelection_ShouldApproveFile()
    {
        // Arrange
        var approval = new PendingApprovalDto
        {
            Id = Guid.NewGuid(),
            ChannelName = "TestChannel",
            FileName = "test.mp4",
            FileSize = 1024000,
            Status = ApprovalStatus.Pending,
            DetectedAt = DateTime.UtcNow,
            Extension = ".mp4",
            Priority = 1,
            FileModifiedAt = DateTime.UtcNow
        };
        _viewModel.SelectedApprovals.Add(approval);

        _mockSignalRClient.Setup(x => x.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>()))
            .ReturnsAsync(true);
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(new List<PendingApprovalDto>());
        _mockSignalRClient.Setup(x => x.GetApprovalStatisticsAsync())
            .ReturnsAsync(new ApprovalStatistics());

        // Act
        await _viewModel.ApproveSelectedCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.ProcessApprovalDecisionAsync(It.Is<ApprovalDecisionRequest>(r =>
            r.ApprovalIds.Contains(approval.Id) &&
            r.Decision == ApprovalStatus.Approved &&
            r.Notes == "Manually approved"
        )), Times.Once);
        _viewModel.StatusMessage.Should().Contain("Successfully approved 1 files");
    }

    [Fact]
    public async Task RejectSelected_WithSingleSelection_ShouldRejectFile()
    {
        // Arrange
        var approval = new PendingApprovalDto
        {
            Id = Guid.NewGuid(),
            ChannelName = "TestChannel",
            FileName = "test.mp4",
            FileSize = 1024000,
            Status = ApprovalStatus.Pending,
            DetectedAt = DateTime.UtcNow,
            Extension = ".mp4",
            Priority = 1,
            FileModifiedAt = DateTime.UtcNow
        };
        _viewModel.SelectedApprovals.Add(approval);

        _mockSignalRClient.Setup(x => x.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>()))
            .ReturnsAsync(true);
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(new List<PendingApprovalDto>());
        _mockSignalRClient.Setup(x => x.GetApprovalStatisticsAsync())
            .ReturnsAsync(new ApprovalStatistics());

        // Act
        await _viewModel.RejectSelectedCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.ProcessApprovalDecisionAsync(It.Is<ApprovalDecisionRequest>(r =>
            r.ApprovalIds.Contains(approval.Id) &&
            r.Decision == ApprovalStatus.Rejected &&
            r.Notes == "Manually rejected"
        )), Times.Once);
        _viewModel.StatusMessage.Should().Contain("Successfully rejected 1 files");
    }

    [Fact]
    public async Task ApproveSelected_WhenOperationFails_ShouldHandleError()
    {
        // Arrange
        var approval = new PendingApprovalDto
        {
            Id = Guid.NewGuid(),
            ChannelName = "TestChannel",
            FileName = "test.mp4",
            Status = ApprovalStatus.Pending,
            DetectedAt = DateTime.UtcNow,
            Extension = ".mp4",
            FileModifiedAt = DateTime.UtcNow
        };
        _viewModel.SelectedApprovals.Add(approval);

        _mockSignalRClient.Setup(x => x.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>()))
            .ReturnsAsync(false);

        // Act
        await _viewModel.ApproveSelectedCommand.ExecuteAsync(null);

        // Assert
        _viewModel.StatusMessage.Should().Contain("Error approving files");
    }

    [Fact]
    public void ApproveSelectedCommand_WithNoSelection_ShouldBeDisabled()
    {
        // Arrange
        _viewModel.SelectedApprovals.Clear();

        // Assert
        _viewModel.ApproveSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    #endregion

    #region Bulk Approval Tests

    [Fact]
    public async Task BulkApprove_WithChannelFilter_ShouldApproveMatchingFiles()
    {
        // Arrange
        _viewModel.ChannelFilter = "Channel1";

        _mockSignalRClient.Setup(x => x.ProcessBulkApprovalAsync(It.IsAny<BulkApprovalRequest>()))
            .ReturnsAsync(5);
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(new List<PendingApprovalDto>());
        _mockSignalRClient.Setup(x => x.GetApprovalStatisticsAsync())
            .ReturnsAsync(new ApprovalStatistics());

        // Act
        await _viewModel.BulkApproveCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.ProcessBulkApprovalAsync(It.Is<BulkApprovalRequest>(r =>
            r.ChannelName == "Channel1" &&
            r.Decision == ApprovalStatus.Approved
        )), Times.Once);
        _viewModel.StatusMessage.Should().Contain("Bulk approved 5 files");
    }

    [Fact]
    public async Task BulkReject_WithMultipleFilters_ShouldRejectMatchingFiles()
    {
        // Arrange
        _viewModel.ChannelFilter = "Channel1";
        _viewModel.MinSizeFilter = 1024 * 1024; // 1MB
        _viewModel.MaxSizeFilter = 10 * 1024 * 1024; // 10MB
        _viewModel.ExtensionFilter = ".mp4,.mkv";

        _mockSignalRClient.Setup(x => x.ProcessBulkApprovalAsync(It.IsAny<BulkApprovalRequest>()))
            .ReturnsAsync(3);
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(new List<PendingApprovalDto>());
        _mockSignalRClient.Setup(x => x.GetApprovalStatisticsAsync())
            .ReturnsAsync(new ApprovalStatistics());

        // Act
        await _viewModel.BulkRejectCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.ProcessBulkApprovalAsync(It.Is<BulkApprovalRequest>(r =>
            r.ChannelName == "Channel1" &&
            r.Decision == ApprovalStatus.Rejected &&
            r.MinFileSize == 1024 * 1024 &&
            r.MaxFileSize == 10 * 1024 * 1024 &&
            r.FileExtensions != null &&
            r.FileExtensions.Count == 2
        )), Times.Once);
        _viewModel.StatusMessage.Should().Contain("Bulk rejected 3 files");
    }

    [Fact]
    public async Task BulkApprove_WithDateFilters_ShouldApplyDateRange()
    {
        // Arrange
        var dateFrom = DateTime.UtcNow.AddDays(-7);
        var dateTo = DateTime.UtcNow;

        _viewModel.ChannelFilter = "TestChannel";
        _viewModel.DateFromFilter = dateFrom;
        _viewModel.DateToFilter = dateTo;

        _mockSignalRClient.Setup(x => x.ProcessBulkApprovalAsync(It.IsAny<BulkApprovalRequest>()))
            .ReturnsAsync(2);
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(new List<PendingApprovalDto>());
        _mockSignalRClient.Setup(x => x.GetApprovalStatisticsAsync())
            .ReturnsAsync(new ApprovalStatistics());

        // Act
        await _viewModel.BulkApproveCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.ProcessBulkApprovalAsync(It.Is<BulkApprovalRequest>(r =>
            r.DetectedAfter == dateFrom &&
            r.DetectedBefore == dateTo
        )), Times.Once);
    }

    [Fact]
    public void BulkApproveCommand_WithNoActiveFilters_ShouldBeDisabled()
    {
        // Arrange - Clear all filters
        _viewModel.ChannelFilter = null;
        _viewModel.PriorityFilter = null;
        _viewModel.MinSizeFilter = null;
        _viewModel.MaxSizeFilter = null;
        _viewModel.DateFromFilter = null;
        _viewModel.DateToFilter = null;
        _viewModel.ExtensionFilter = null;

        // Assert
        _viewModel.BulkApproveCommand.CanExecute(null).Should().BeFalse();
    }

    #endregion

    #region Filtering Tests

    [Fact]
    public void ApplyFilters_WithShowOnlyPending_ShouldFilterPendingOnly()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Approved, ChannelName = "Test", FileName = "test2.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test3.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Rejected, ChannelName = "Test", FileName = "test4.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.ShowOnlyPending = true;

        // Wait a bit for property change to trigger filter
        Task.Delay(100).Wait();

        // Assert
        _viewModel.FilteredApprovals.Should().HaveCount(2);
        _viewModel.FilteredApprovals.Should().OnlyContain(a => a.Status == ApprovalStatus.Pending);
    }

    [Fact]
    public void ApplyFilters_WithChannelFilter_ShouldFilterByChannel()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Channel1", FileName = "test1.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Channel2", FileName = "test2.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Channel1", FileName = "test3.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.ChannelFilter = "Channel1";

        // Wait for filter to apply
        Task.Delay(100).Wait();

        // Assert
        _viewModel.FilteredApprovals.Should().HaveCount(2);
        _viewModel.FilteredApprovals.Should().OnlyContain(a => a.ChannelName == "Channel1");
    }

    [Fact]
    public void ApplyFilters_WithPriorityFilter_ShouldFilterByPriority()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", Priority = 1, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test2.mp4", Priority = 2, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test3.mp4", Priority = 1, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.PriorityFilter = 1;

        // Wait for filter to apply
        Task.Delay(100).Wait();

        // Assert
        _viewModel.FilteredApprovals.Should().HaveCount(2);
        _viewModel.FilteredApprovals.Should().OnlyContain(a => a.Priority == 1);
    }

    [Fact]
    public void ApplyFilters_WithSizeFilters_ShouldFilterBySizeRange()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", FileSize = 500 * 1024, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test2.mp4", FileSize = 2 * 1024 * 1024, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test3.mp4", FileSize = 15 * 1024 * 1024, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.MinSizeFilter = 1024 * 1024; // 1MB
        _viewModel.MaxSizeFilter = 10 * 1024 * 1024; // 10MB

        // Wait for filter to apply
        Task.Delay(100).Wait();

        // Assert
        _viewModel.FilteredApprovals.Should().HaveCount(1);
        _viewModel.FilteredApprovals.First().FileName.Should().Be("test2.mp4");
    }

    [Fact]
    public void ApplyFilters_WithDateFilters_ShouldFilterByDateRange()
    {
        // Arrange
        var baseDate = DateTime.UtcNow.AddDays(-10);
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", DetectedAt = baseDate.AddDays(1), Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test2.mp4", DetectedAt = baseDate.AddDays(5), Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test3.mp4", DetectedAt = baseDate.AddDays(9), Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.DateFromFilter = baseDate.AddDays(2);
        _viewModel.DateToFilter = baseDate.AddDays(7);

        // Wait for filter to apply
        Task.Delay(100).Wait();

        // Assert
        _viewModel.FilteredApprovals.Should().HaveCount(1);
        _viewModel.FilteredApprovals.First().FileName.Should().Be("test2.mp4");
    }

    [Fact]
    public void ApplyFilters_WithExtensionFilter_ShouldFilterByExtension()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", Extension = ".mp4", DetectedAt = DateTime.UtcNow, FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test2.mkv", Extension = ".mkv", DetectedAt = DateTime.UtcNow, FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test3.avi", Extension = ".avi", DetectedAt = DateTime.UtcNow, FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.ExtensionFilter = ".mp4,.mkv";

        // Wait for filter to apply
        Task.Delay(100).Wait();

        // Assert
        _viewModel.FilteredApprovals.Should().HaveCount(2);
        _viewModel.FilteredApprovals.Should().Contain(a => a.Extension == ".mp4");
        _viewModel.FilteredApprovals.Should().Contain(a => a.Extension == ".mkv");
    }

    [Fact]
    public void ClearFilters_ShouldResetAllFilters()
    {
        // Arrange
        _viewModel.ChannelFilter = "Channel1";
        _viewModel.PriorityFilter = 1;
        _viewModel.MinSizeFilter = 1024;
        _viewModel.MaxSizeFilter = 2048;
        _viewModel.DateFromFilter = DateTime.UtcNow.AddDays(-7);
        _viewModel.DateToFilter = DateTime.UtcNow;
        _viewModel.ExtensionFilter = ".mp4";

        // Act
        _viewModel.ClearFiltersCommand.Execute(null);

        // Assert
        _viewModel.ChannelFilter.Should().BeNull();
        _viewModel.PriorityFilter.Should().BeNull();
        _viewModel.MinSizeFilter.Should().BeNull();
        _viewModel.MaxSizeFilter.Should().BeNull();
        _viewModel.DateFromFilter.Should().BeNull();
        _viewModel.DateToFilter.Should().BeNull();
        _viewModel.ExtensionFilter.Should().BeNull();
    }

    #endregion

    #region Sorting Tests

    [Fact]
    public void ApplyFilters_WithFileNameSort_ShouldSortByFileName()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "charlie.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "alpha.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "bravo.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.SortBy = "FileName";
        _viewModel.SortDescending = false;

        // Wait for sort to apply
        Task.Delay(100).Wait();

        // Assert
        _viewModel.FilteredApprovals.Should().HaveCount(3);
        _viewModel.FilteredApprovals[0].FileName.Should().Be("alpha.mp4");
        _viewModel.FilteredApprovals[1].FileName.Should().Be("bravo.mp4");
        _viewModel.FilteredApprovals[2].FileName.Should().Be("charlie.mp4");
    }

    [Fact]
    public void ApplyFilters_WithFileSizeSort_ShouldSortBySize()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "large.mp4", FileSize = 10 * 1024 * 1024, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "small.mp4", FileSize = 1 * 1024 * 1024, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "medium.mp4", FileSize = 5 * 1024 * 1024, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.SortBy = "FileSize";
        _viewModel.SortDescending = true;

        // Wait for sort to apply
        Task.Delay(100).Wait();

        // Assert
        _viewModel.FilteredApprovals.Should().HaveCount(3);
        _viewModel.FilteredApprovals[0].FileName.Should().Be("large.mp4");
        _viewModel.FilteredApprovals[1].FileName.Should().Be("medium.mp4");
        _viewModel.FilteredApprovals[2].FileName.Should().Be("small.mp4");
    }

    [Fact]
    public void ApplyFilters_WithPrioritySort_ShouldSortByPriority()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", Priority = 3, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test2.mp4", Priority = 1, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test3.mp4", Priority = 2, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.SortBy = "Priority";
        _viewModel.SortDescending = false;

        // Wait for sort to apply
        Task.Delay(100).Wait();

        // Assert
        _viewModel.FilteredApprovals.Should().HaveCount(3);
        _viewModel.FilteredApprovals[0].Priority.Should().Be(1);
        _viewModel.FilteredApprovals[1].Priority.Should().Be(2);
        _viewModel.FilteredApprovals[2].Priority.Should().Be(3);
    }

    #endregion

    #region Multi-Selection Tests

    [Fact]
    public void SelectAll_ShouldSelectAllVisibleApprovals()
    {
        // Arrange
        _viewModel.FilteredApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.FilteredApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test2.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.FilteredApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test3.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        _viewModel.SelectAllCommand.Execute(null);

        // Assert
        _viewModel.SelectedApprovals.Should().HaveCount(3);
        _viewModel.HasSelection.Should().BeTrue();
        _viewModel.SelectedCount.Should().Be(3);
    }

    [Fact]
    public void DeselectAll_ShouldClearAllSelections()
    {
        // Arrange
        _viewModel.FilteredApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.SelectAllCommand.Execute(null);

        // Act
        _viewModel.DeselectAllCommand.Execute(null);

        // Assert
        _viewModel.SelectedApprovals.Should().BeEmpty();
        _viewModel.HasSelection.Should().BeFalse();
        _viewModel.SelectedCount.Should().Be(0);
    }

    [Fact]
    public void ToggleSelection_ShouldAddOrRemoveFromSelection()
    {
        // Arrange
        var approval = new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow };

        // Act - First toggle adds
        _viewModel.ToggleSelectionCommand.Execute(approval);

        // Assert
        _viewModel.SelectedApprovals.Should().Contain(approval);
        _viewModel.SelectedCount.Should().Be(1);

        // Act - Second toggle removes
        _viewModel.ToggleSelectionCommand.Execute(approval);

        // Assert
        _viewModel.SelectedApprovals.Should().NotContain(approval);
        _viewModel.SelectedCount.Should().Be(0);
    }

    [Fact]
    public async Task ApproveSelected_WithMultipleSelections_ShouldApproveAll()
    {
        // Arrange
        var approval1 = new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow };
        var approval2 = new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test2.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow };
        var approval3 = new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test3.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow };

        _viewModel.SelectedApprovals.Add(approval1);
        _viewModel.SelectedApprovals.Add(approval2);
        _viewModel.SelectedApprovals.Add(approval3);

        _mockSignalRClient.Setup(x => x.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>()))
            .ReturnsAsync(true);
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(new List<PendingApprovalDto>());
        _mockSignalRClient.Setup(x => x.GetApprovalStatisticsAsync())
            .ReturnsAsync(new ApprovalStatistics());

        // Act
        await _viewModel.ApproveSelectedCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.ProcessApprovalDecisionAsync(It.Is<ApprovalDecisionRequest>(r =>
            r.ApprovalIds.Count == 3 &&
            r.ApprovalIds.Contains(approval1.Id) &&
            r.ApprovalIds.Contains(approval2.Id) &&
            r.ApprovalIds.Contains(approval3.Id)
        )), Times.Once);
        _viewModel.StatusMessage.Should().Contain("Successfully approved 3 files");
    }

    #endregion

    #region Activity Log Tests

    [Fact]
    public void ActivityLog_ShouldTrackApprovalActivities()
    {
        // Arrange & Act
        _viewModel.FilteredApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.SelectAllCommand.Execute(null);

        // Assert
        _viewModel.ActivityLog.Should().Contain(log => log.Message.Contains("Selected all"));
    }

    [Fact]
    public void ActivityLog_ShouldLimitTo200Messages()
    {
        // Arrange & Act
        for (int i = 0; i < 250; i++)
        {
            _viewModel.FilteredApprovals.Clear();
            _viewModel.FilteredApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = $"test{i}.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
            _viewModel.SelectAllCommand.Execute(null);
            _viewModel.DeselectAllCommand.Execute(null);
        }

        // Assert
        _viewModel.ActivityLog.Should().HaveCountLessThanOrEqualTo(200);
    }

    [Fact]
    public void ClearActivityLog_ShouldRemoveAllLogEntries()
    {
        // Arrange
        _viewModel.FilteredApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });
        _viewModel.SelectAllCommand.Execute(null);

        // Act
        _viewModel.ClearActivityLogCommand.Execute(null);

        // Assert
        _viewModel.ActivityLog.Should().BeEmpty();
    }

    #endregion

    #region Preview Tests

    [Fact]
    public void PreviewFile_ShouldSetCurrentApproval()
    {
        // Arrange
        var approval = new PendingApprovalDto
        {
            Id = Guid.NewGuid(),
            ChannelName = "TestChannel",
            FileName = "test.mp4",
            Status = ApprovalStatus.Pending,
            DetectedAt = DateTime.UtcNow,
            Extension = ".mp4",
            FileModifiedAt = DateTime.UtcNow
        };

        // Act
        _viewModel.PreviewFileCommand.Execute(approval);

        // Assert
        _viewModel.CurrentApproval.Should().Be(approval);
    }

    #endregion

    #region Channel-Specific Tests

    [Fact]
    public async Task ApprovalsByChannel_ShouldLoadChannelApprovals()
    {
        // Arrange
        var channelName = "SpecificChannel";
        var approvals = new List<PendingApprovalDto>
        {
            new() { Id = Guid.NewGuid(), ChannelName = channelName, FileName = "video1.mp4", Status = ApprovalStatus.Pending, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), ChannelName = channelName, FileName = "video2.mp4", Status = ApprovalStatus.Pending, DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow }
        };

        _mockSignalRClient.Setup(x => x.GetPendingApprovalsByChannelAsync(channelName))
            .ReturnsAsync(approvals);

        // Act
        await _viewModel.ApprovalsByChannelCommand.ExecuteAsync(channelName);

        // Assert
        _viewModel.PendingApprovals.Should().HaveCount(2);
        _viewModel.PendingApprovals.Should().OnlyContain(a => a.ChannelName == channelName);
        _viewModel.StatusMessage.Should().Contain($"Loaded 2 approvals for {channelName}");
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_ShouldClearCollections()
    {
        // Arrange
        var viewModel = new ApprovalViewModel(_mockSignalRClient.Object);
        viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), ChannelName = "Test", FileName = "test.mp4", DetectedAt = DateTime.UtcNow, Extension = ".mp4", FileModifiedAt = DateTime.UtcNow });

        // Act
        viewModel.Dispose();

        // Assert
        viewModel.PendingApprovals.Should().BeEmpty();
        viewModel.FilteredApprovals.Should().BeEmpty();
        viewModel.SelectedApprovals.Should().BeEmpty();
        viewModel.AvailableChannels.Should().BeEmpty();
        viewModel.ActivityLog.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var viewModel = new ApprovalViewModel(_mockSignalRClient.Object);

        // Act & Assert
        viewModel.Dispose();
        viewModel.Invoking(vm => vm.Dispose()).Should().NotThrow();
    }

    #endregion
}

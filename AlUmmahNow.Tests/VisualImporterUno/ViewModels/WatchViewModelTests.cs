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
/// Comprehensive integration tests for WatchViewModel
/// Tests cover watch management, approval workflow, SignalR events, and validation
/// </summary>
public class WatchViewModelTests : IAsyncLifetime
{
    private Mock<FileScanSignalRClient> _mockSignalRClient = null!;
    private Mock<DispatcherQueue> _mockDispatcherQueue = null!;
    private WatchViewModel _viewModel = null!;
    private string _testDirectory = string.Empty;

    public Task InitializeAsync()
    {
        // Create test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), "WatchViewModelTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        // Setup mocks
        _mockSignalRClient = new Mock<FileScanSignalRClient>("http://localhost:5000");
        _mockDispatcherQueue = new Mock<DispatcherQueue>();

        // Setup mock SignalR client to be connected
        _mockSignalRClient.Setup(x => x.IsConnected).Returns(true);

        // Setup dispatcher queue to execute actions synchronously for testing
        _mockDispatcherQueue.Setup(x => x.HasThreadAccess).Returns(true);

        // Create view model
        _viewModel = new WatchViewModel(_mockSignalRClient.Object);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _viewModel?.Dispose();

        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    #region Constructor and Initialization Tests

    [Fact]
    public void Constructor_WithValidSignalRClient_ShouldInitializeSuccessfully()
    {
        // Arrange & Act
        var viewModel = new WatchViewModel(_mockSignalRClient.Object);

        // Assert
        viewModel.Should().NotBeNull();
        viewModel.StatusMessage.Should().Be("Ready to manage watches");
        viewModel.IsLoading.Should().BeFalse();
        viewModel.TotalWatches.Should().Be(0);
        viewModel.ActiveWatches.Should().BeEmpty();
        viewModel.PendingApprovals.Should().BeEmpty();
        viewModel.SupportedExtensions.Should().NotBeEmpty();
        viewModel.SupportedExtensions.Should().Contain(".mp4");

        viewModel.Dispose();
    }

    [Fact]
    public void Constructor_WithNullSignalRClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WatchViewModel(null!));
    }

    [Fact]
    public void SupportedExtensions_ShouldContainCommonVideoFormats()
    {
        // Assert
        _viewModel.SupportedExtensions.Should().Contain(".mp4");
        _viewModel.SupportedExtensions.Should().Contain(".mkv");
        _viewModel.SupportedExtensions.Should().Contain(".avi");
        _viewModel.SupportedExtensions.Should().Contain(".mov");
        _viewModel.SupportedExtensions.Should().Contain(".wmv");
        _viewModel.SupportedExtensions.Should().Contain(".webm");
        _viewModel.SupportedExtensions.Count.Should().BeGreaterThan(5);
    }

    #endregion

    #region Create Watch Command Tests

    [Fact]
    public void OpenCreateWatchDialog_ShouldSetDialogOpenFlag()
    {
        // Act
        _viewModel.OpenCreateWatchDialogCommand.Execute(null);

        // Assert
        _viewModel.IsCreateDialogOpen.Should().BeTrue();
        _viewModel.NewWatchChannelName.Should().BeEmpty();
        _viewModel.NewWatchPath.Should().BeEmpty();
        _viewModel.ProcessingDelaySeconds.Should().Be(5);
    }

    [Fact]
    public void CloseCreateWatchDialog_ShouldResetDialogState()
    {
        // Arrange
        _viewModel.NewWatchChannelName = "TestChannel";
        _viewModel.NewWatchPath = _testDirectory;
        _viewModel.IsCreateDialogOpen = true;

        // Act
        _viewModel.CloseCreateWatchDialogCommand.Execute(null);

        // Assert
        _viewModel.IsCreateDialogOpen.Should().BeFalse();
        _viewModel.NewWatchChannelName.Should().BeEmpty();
        _viewModel.NewWatchPath.Should().BeEmpty();
        _viewModel.CreateWatchError.Should().BeEmpty();
    }

    [Fact]
    public void ChannelNameValidation_WithEmptyName_ShouldFailValidation()
    {
        // Act
        _viewModel.NewWatchChannelName = "";

        // Assert
        _viewModel.IsChannelNameValid.Should().BeFalse();
        _viewModel.ChannelNameValidationMessage.Should().Be("Channel name is required");
    }

    [Fact]
    public void ChannelNameValidation_WithShortName_ShouldFailValidation()
    {
        // Act
        _viewModel.NewWatchChannelName = "A";

        // Assert
        _viewModel.IsChannelNameValid.Should().BeFalse();
        _viewModel.ChannelNameValidationMessage.Should().Be("Channel name must be at least 2 characters");
    }

    [Fact]
    public void ChannelNameValidation_WithLongName_ShouldFailValidation()
    {
        // Act
        _viewModel.NewWatchChannelName = new string('A', 101);

        // Assert
        _viewModel.IsChannelNameValid.Should().BeFalse();
        _viewModel.ChannelNameValidationMessage.Should().Be("Channel name must not exceed 100 characters");
    }

    [Fact]
    public void ChannelNameValidation_WithValidName_ShouldPassValidation()
    {
        // Act
        _viewModel.NewWatchChannelName = "TestChannel";

        // Assert
        _viewModel.IsChannelNameValid.Should().BeTrue();
        _viewModel.ChannelNameValidationMessage.Should().BeEmpty();
    }

    [Fact]
    public void ChannelNameValidation_WithDuplicateName_ShouldFailValidation()
    {
        // Arrange
        var existingWatch = new WatchStatusDto
        {
            WatchId = Guid.NewGuid(),
            ChannelName = "ExistingChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.ActiveWatches.Add(existingWatch);

        // Act
        _viewModel.NewWatchChannelName = "ExistingChannel";

        // Assert
        _viewModel.IsChannelNameValid.Should().BeFalse();
        _viewModel.ChannelNameValidationMessage.Should().Be("A watch for this channel already exists");
    }

    [Fact]
    public void WatchPathValidation_WithEmptyPath_ShouldFailValidation()
    {
        // Act
        _viewModel.NewWatchPath = "";

        // Assert
        _viewModel.IsWatchPathValid.Should().BeFalse();
        _viewModel.WatchPathValidationMessage.Should().Be("Watch path is required");
    }

    [Fact]
    public void WatchPathValidation_WithNonExistentPath_ShouldFailValidation()
    {
        // Act
        _viewModel.NewWatchPath = "C:\\NonExistent\\Path\\That\\Does\\Not\\Exist";

        // Assert
        _viewModel.IsWatchPathValid.Should().BeFalse();
        _viewModel.WatchPathValidationMessage.Should().Be("Directory does not exist");
    }

    [Fact]
    public void WatchPathValidation_WithValidPath_ShouldPassValidation()
    {
        // Act
        _viewModel.NewWatchPath = _testDirectory;

        // Assert
        _viewModel.IsWatchPathValid.Should().BeTrue();
        _viewModel.WatchPathValidationMessage.Should().BeEmpty();
    }

    [Fact]
    public void WatchPathValidation_WithDuplicatePath_ShouldFailValidation()
    {
        // Arrange
        var existingWatch = new WatchStatusDto
        {
            WatchId = Guid.NewGuid(),
            ChannelName = "ExistingChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.ActiveWatches.Add(existingWatch);

        // Act
        _viewModel.NewWatchPath = _testDirectory;

        // Assert
        _viewModel.IsWatchPathValid.Should().BeFalse();
        _viewModel.WatchPathValidationMessage.Should().Be("This path is already being watched");
    }

    [Fact]
    public async Task CreateWatch_WithValidInputs_ShouldCreateWatch()
    {
        // Arrange
        _viewModel.NewWatchChannelName = "TestChannel";
        _viewModel.NewWatchPath = _testDirectory;
        _viewModel.ProcessingDelaySeconds = 5;

        var expectedWatchId = Guid.NewGuid();
        _mockSignalRClient.Setup(x => x.StartWatchAsync(It.IsAny<CreateWatchRequest>()))
            .ReturnsAsync(expectedWatchId);
        _mockSignalRClient.Setup(x => x.GetActiveWatchesAsync())
            .ReturnsAsync(new List<WatchStatusDto>());

        // Act
        await _viewModel.CreateWatchCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.StartWatchAsync(It.Is<CreateWatchRequest>(r =>
            r.ChannelName == "TestChannel" &&
            r.WatchPath == _testDirectory &&
            r.ProcessingDelayMs == 5000 &&
            r.IncludeSubdirectories == true
        )), Times.Once);

        _viewModel.IsCreateDialogOpen.Should().BeFalse();
        _viewModel.StatusMessage.Should().Contain("Watch created");
    }

    [Fact]
    public async Task CreateWatch_WhenNotConnected_ShouldSetError()
    {
        // Arrange
        _mockSignalRClient.Setup(x => x.IsConnected).Returns(false);
        _viewModel.NewWatchChannelName = "TestChannel";
        _viewModel.NewWatchPath = _testDirectory;

        // Act
        await _viewModel.CreateWatchCommand.ExecuteAsync(null);

        // Assert
        _viewModel.CreateWatchError.Should().Be("Not connected to FileScan service");
        _viewModel.IsCreateDialogOpen.Should().BeTrue();
    }

    [Fact]
    public async Task CreateWatch_WithInvalidDelay_ShouldSetError()
    {
        // Arrange
        _viewModel.NewWatchChannelName = "TestChannel";
        _viewModel.NewWatchPath = _testDirectory;
        _viewModel.ProcessingDelaySeconds = 500; // Too high

        // Act
        await _viewModel.CreateWatchCommand.ExecuteAsync(null);

        // Assert
        _viewModel.CreateWatchError.Should().Be("Processing delay must be between 1 and 300 seconds");
    }

    [Fact]
    public async Task CreateWatch_WithNoExtensions_ShouldSetError()
    {
        // Arrange
        _viewModel.NewWatchChannelName = "TestChannel";
        _viewModel.NewWatchPath = _testDirectory;
        _viewModel.SelectedExtensions = "";

        // Act
        await _viewModel.CreateWatchCommand.ExecuteAsync(null);

        // Assert
        _viewModel.CreateWatchError.Should().Be("At least one file extension must be selected");
    }

    #endregion

    #region Stop Watch Command Tests

    [Fact]
    public async Task StopWatch_WithSelectedWatch_ShouldStopWatch()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var watch = new WatchStatusDto
        {
            WatchId = watchId,
            ChannelName = "TestChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.ActiveWatches.Add(watch);
        _viewModel.SelectedWatch = watch;

        _mockSignalRClient.Setup(x => x.StopWatchAsync(watchId)).ReturnsAsync(true);
        _mockSignalRClient.Setup(x => x.GetActiveWatchesAsync())
            .ReturnsAsync(new List<WatchStatusDto>());

        // Act
        await _viewModel.StopWatchCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.StopWatchAsync(watchId), Times.Once);
        _viewModel.StatusMessage.Should().Contain("Watch stopped");
    }

    [Fact]
    public async Task StopWatch_WhenStopFails_ShouldUpdateStatusMessage()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var watch = new WatchStatusDto
        {
            WatchId = watchId,
            ChannelName = "TestChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.SelectedWatch = watch;

        _mockSignalRClient.Setup(x => x.StopWatchAsync(watchId)).ReturnsAsync(false);

        // Act
        await _viewModel.StopWatchCommand.ExecuteAsync(null);

        // Assert
        _viewModel.StatusMessage.Should().Contain("Failed to stop watch");
    }

    [Fact]
    public void StopWatchCommand_WhenNoWatchSelected_ShouldBeDisabled()
    {
        // Arrange
        _viewModel.SelectedWatch = null;

        // Assert
        _viewModel.StopWatchCommand.CanExecute(null).Should().BeFalse();
    }

    #endregion

    #region Delete Watch Command Tests

    [Fact]
    public async Task DeleteWatch_WithSelectedWatch_ShouldDeleteWatch()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var watch = new WatchStatusDto
        {
            WatchId = watchId,
            ChannelName = "TestChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.SelectedWatch = watch;

        _mockSignalRClient.Setup(x => x.DeleteWatchAsync(watchId)).ReturnsAsync(true);
        _mockSignalRClient.Setup(x => x.GetActiveWatchesAsync())
            .ReturnsAsync(new List<WatchStatusDto>());

        // Act
        await _viewModel.DeleteWatchCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.DeleteWatchAsync(watchId), Times.Once);
        _viewModel.SelectedWatch.Should().BeNull();
        _viewModel.StatusMessage.Should().Contain("Watch deleted");
    }

    [Fact]
    public async Task DeleteWatch_WhenDeleteFails_ShouldUpdateStatusMessage()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var watch = new WatchStatusDto
        {
            WatchId = watchId,
            ChannelName = "TestChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.SelectedWatch = watch;

        _mockSignalRClient.Setup(x => x.DeleteWatchAsync(watchId)).ReturnsAsync(false);

        // Act
        await _viewModel.DeleteWatchCommand.ExecuteAsync(null);

        // Assert
        _viewModel.StatusMessage.Should().Contain("Failed to delete watch");
        _viewModel.SelectedWatch.Should().NotBeNull(); // Selection should remain
    }

    #endregion

    #region Refresh Watches Command Tests

    [Fact]
    public async Task RefreshWatches_WhenConnected_ShouldLoadWatches()
    {
        // Arrange
        var watches = new List<WatchStatusDto>
        {
            new() { WatchId = Guid.NewGuid(), ChannelName = "Channel1", WatchPath = _testDirectory, IsActive = true, TotalFilesDetected = 5 },
            new() { WatchId = Guid.NewGuid(), ChannelName = "Channel2", WatchPath = _testDirectory, IsActive = false, TotalFilesDetected = 3 }
        };

        _mockSignalRClient.Setup(x => x.GetActiveWatchesAsync()).ReturnsAsync(watches);

        // Act
        await _viewModel.RefreshWatchesCommand.ExecuteAsync(null);

        // Assert
        _viewModel.ActiveWatches.Should().HaveCount(2);
        _viewModel.TotalWatches.Should().Be(2);
        _viewModel.ActiveWatchesCount.Should().Be(1);
        _viewModel.TotalFilesDetected.Should().Be(8);
        _viewModel.StatusMessage.Should().Contain("Loaded 2 active watches");
    }

    [Fact]
    public async Task RefreshWatches_WhenNotConnected_ShouldUpdateStatusMessage()
    {
        // Arrange
        _mockSignalRClient.Setup(x => x.IsConnected).Returns(false);

        // Act
        await _viewModel.RefreshWatchesCommand.ExecuteAsync(null);

        // Assert
        _viewModel.StatusMessage.Should().Be("Not connected to FileScan service");
        _viewModel.ActiveWatches.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshWatches_ShouldUpdateAvailableChannels()
    {
        // Arrange
        var watches = new List<WatchStatusDto>
        {
            new() { WatchId = Guid.NewGuid(), ChannelName = "Channel1", WatchPath = _testDirectory, IsActive = true },
            new() { WatchId = Guid.NewGuid(), ChannelName = "Channel2", WatchPath = _testDirectory, IsActive = true },
            new() { WatchId = Guid.NewGuid(), ChannelName = "Channel1", WatchPath = _testDirectory + "2", IsActive = true } // Duplicate
        };

        _mockSignalRClient.Setup(x => x.GetActiveWatchesAsync()).ReturnsAsync(watches);

        // Act
        await _viewModel.RefreshWatchesCommand.ExecuteAsync(null);

        // Assert
        _viewModel.AvailableChannels.Should().Contain("All Channels");
        _viewModel.AvailableChannels.Should().Contain("Channel1");
        _viewModel.AvailableChannels.Should().Contain("Channel2");
        _viewModel.AvailableChannels.Count(c => c == "Channel1").Should().Be(1); // No duplicates
    }

    #endregion

    #region SignalR Event Handling Tests

    [Fact]
    public void FileDetected_ShouldUpdateWatchStatistics()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var watch = new WatchStatusDto
        {
            WatchId = watchId,
            ChannelName = "TestChannel",
            WatchPath = _testDirectory,
            IsActive = true,
            TotalFilesDetected = 5
        };
        _viewModel.ActiveWatches.Add(watch);

        var eventArgs = new FileDetectedEventArgs(watchId, "TestChannel", "test.mp4", "test.mp4", 1024000, DateTime.UtcNow);

        // Act
        _mockSignalRClient.Raise(x => x.FileDetected += null, _mockSignalRClient.Object, eventArgs);

        // Assert
        watch.TotalFilesDetected.Should().Be(6);
        _viewModel.TotalFilesDetected.Should().Be(6);
        _viewModel.StatusMessage.Should().Contain("File detected: test.mp4");
    }

    [Fact]
    public void WatchStatusChanged_ShouldUpdateWatchStatus()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var watch = new WatchStatusDto
        {
            WatchId = watchId,
            ChannelName = "TestChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.ActiveWatches.Add(watch);

        var eventArgs = new WatchStatusChangedEventArgs(watchId, "TestChannel", false, "Watch stopped");

        // Act
        _mockSignalRClient.Raise(x => x.WatchStatusChanged += null, _mockSignalRClient.Object, eventArgs);

        // Assert
        watch.IsActive.Should().BeFalse();
        _viewModel.ActiveWatchesCount.Should().Be(0);
        _viewModel.StatusMessage.Should().Contain("Watch status changed");
    }

    [Fact]
    public void WatchStopped_ShouldUpdateWatchActiveStatus()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var watch = new WatchStatusDto
        {
            WatchId = watchId,
            ChannelName = "TestChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.ActiveWatches.Add(watch);

        // Act
        _mockSignalRClient.Raise(x => x.WatchStopped += null, _mockSignalRClient.Object, watchId);

        // Assert
        watch.IsActive.Should().BeFalse();
        _viewModel.StatusMessage.Should().Contain("Watch stopped: TestChannel");
    }

    [Fact]
    public void WatchDeleted_ShouldRemoveWatchFromCollection()
    {
        // Arrange
        var watchId = Guid.NewGuid();
        var watch = new WatchStatusDto
        {
            WatchId = watchId,
            ChannelName = "TestChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.ActiveWatches.Add(watch);
        _viewModel.SelectedWatch = watch;

        // Act
        _mockSignalRClient.Raise(x => x.WatchDeleted += null, _mockSignalRClient.Object, watchId);

        // Assert
        _viewModel.ActiveWatches.Should().BeEmpty();
        _viewModel.SelectedWatch.Should().BeNull();
        _viewModel.StatusMessage.Should().Contain("Watch deleted: TestChannel");
    }

    [Fact]
    public void NewFileForApproval_ShouldUpdateStatusMessage()
    {
        // Arrange
        var eventArgs = new NewFileForApprovalEventArgs
        {
            ChannelName = "TestChannel",
            FileName = "test.mp4",
            ApprovalId = Guid.NewGuid()
        };

        // Act
        _mockSignalRClient.Raise(x => x.NewFileForApproval += null, _mockSignalRClient.Object, eventArgs);

        // Assert
        _viewModel.StatusMessage.Should().Contain("New file for approval");
    }

    [Fact]
    public void ApprovalStatusChanged_ShouldUpdateApprovalInCollection()
    {
        // Arrange
        var approvalId = Guid.NewGuid();
        var approval = new PendingApprovalDto
        {
            Id = approvalId,
            ChannelName = "TestChannel",
            FileName = "test.mp4",
            Status = ApprovalStatus.Pending
        };
        _viewModel.PendingApprovals.Add(approval);

        var eventArgs = new ApprovalStatusChangedEventArgs(
            approvalId,
            Guid.NewGuid(),
            "TestChannel",
            "test.mp4",
            ApprovalStatus.Pending,
            ApprovalStatus.Approved,
            "TestUser"
        );

        // Act
        _mockSignalRClient.Raise(x => x.ApprovalStatusChanged += null, _mockSignalRClient.Object, eventArgs);

        // Assert
        approval.Status.Should().Be(ApprovalStatus.Approved);
        _viewModel.StatusMessage.Should().Contain("Approval status changed");
    }

    #endregion

    #region Approval Management Tests

    [Fact]
    public async Task ApproveSelected_WithSelectedApproval_ShouldApproveFile()
    {
        // Arrange
        var approvalId = Guid.NewGuid();
        var approval = new PendingApprovalDto
        {
            Id = approvalId,
            ChannelName = "TestChannel",
            FileName = "test.mp4",
            Status = ApprovalStatus.Pending
        };
        _viewModel.SelectedApproval = approval;

        _mockSignalRClient.Setup(x => x.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>()))
            .ReturnsAsync(true);
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(new List<PendingApprovalDto>());

        // Act
        await _viewModel.ApproveSelectedCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.ProcessApprovalDecisionAsync(It.Is<ApprovalDecisionRequest>(r =>
            r.ApprovalIds.Contains(approvalId) &&
            r.Decision == ApprovalStatus.Approved
        )), Times.Once);
        _viewModel.StatusMessage.Should().Contain("File approved");
    }

    [Fact]
    public async Task RejectSelected_WithSelectedApproval_ShouldRejectFile()
    {
        // Arrange
        var approvalId = Guid.NewGuid();
        var approval = new PendingApprovalDto
        {
            Id = approvalId,
            ChannelName = "TestChannel",
            FileName = "test.mp4",
            Status = ApprovalStatus.Pending
        };
        _viewModel.SelectedApproval = approval;

        _mockSignalRClient.Setup(x => x.ProcessApprovalDecisionAsync(It.IsAny<ApprovalDecisionRequest>()))
            .ReturnsAsync(true);
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(new List<PendingApprovalDto>());

        // Act
        await _viewModel.RejectSelectedCommand.ExecuteAsync(null);

        // Assert
        _mockSignalRClient.Verify(x => x.ProcessApprovalDecisionAsync(It.Is<ApprovalDecisionRequest>(r =>
            r.ApprovalIds.Contains(approvalId) &&
            r.Decision == ApprovalStatus.Rejected
        )), Times.Once);
        _viewModel.StatusMessage.Should().Contain("File rejected");
    }

    [Fact]
    public void SelectAllApprovals_ShouldSelectOnlyPendingApprovals()
    {
        // Arrange
        _viewModel.FilteredPendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending });
        _viewModel.FilteredPendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Approved });
        _viewModel.FilteredPendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending });

        // Act
        _viewModel.SelectAllApprovalsCommand.Execute(null);

        // Assert
        _viewModel.HasSelectedApprovals.Should().BeTrue();
        _viewModel.SelectedApprovalsCount.Should().Be(2);
    }

    [Fact]
    public void DeselectAllApprovals_ShouldClearSelection()
    {
        // Arrange
        _viewModel.FilteredPendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending });
        _viewModel.SelectAllApprovalsCommand.Execute(null);

        // Act
        _viewModel.DeselectAllApprovalsCommand.Execute(null);

        // Assert
        _viewModel.HasSelectedApprovals.Should().BeFalse();
        _viewModel.SelectedApprovalsCount.Should().Be(0);
    }

    [Fact]
    public void ToggleApprovalSelection_ShouldAddOrRemoveFromSelection()
    {
        // Arrange
        var approval = new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending };

        // Act - First toggle adds
        _viewModel.ToggleApprovalSelectionCommand.Execute(approval);

        // Assert
        _viewModel.SelectedApprovalsCount.Should().Be(1);

        // Act - Second toggle removes
        _viewModel.ToggleApprovalSelectionCommand.Execute(approval);

        // Assert
        _viewModel.SelectedApprovalsCount.Should().Be(0);
    }

    #endregion

    #region Filtering Tests

    [Fact]
    public void ApplyFilters_WithShowOnlyPending_ShouldFilterByStatus()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", DetectedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Approved, ChannelName = "Test", FileName = "test2.mp4", DetectedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test3.mp4", DetectedAt = DateTime.UtcNow });

        // Act
        _viewModel.ShowOnlyPending = true;

        // Assert
        _viewModel.FilteredPendingApprovals.Should().HaveCount(2);
        _viewModel.FilteredPendingApprovals.Should().OnlyContain(a => a.Status == ApprovalStatus.Pending);
    }

    [Fact]
    public void ApplyFilters_WithChannelFilter_ShouldFilterByChannel()
    {
        // Arrange
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Channel1", FileName = "test1.mp4", DetectedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Channel2", FileName = "test2.mp4", DetectedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Channel1", FileName = "test3.mp4", DetectedAt = DateTime.UtcNow });

        // Act
        _viewModel.ChannelFilter = "Channel1";

        // Assert
        _viewModel.FilteredPendingApprovals.Should().HaveCount(2);
        _viewModel.FilteredPendingApprovals.Should().OnlyContain(a => a.ChannelName == "Channel1");
    }

    [Fact]
    public void ViewWatchDetails_ShouldSetChannelFilter()
    {
        // Arrange
        var watch = new WatchStatusDto
        {
            WatchId = Guid.NewGuid(),
            ChannelName = "TestChannel",
            WatchPath = _testDirectory,
            IsActive = true
        };
        _viewModel.SelectedWatch = watch;

        // Act
        _viewModel.ViewWatchDetailsCommand.Execute(null);

        // Assert
        _viewModel.ChannelFilter.Should().Be("TestChannel");
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public void UpdateStatistics_ShouldCalculateCorrectValues()
    {
        // Arrange
        _viewModel.ActiveWatches.Add(new WatchStatusDto { WatchId = Guid.NewGuid(), ChannelName = "Channel1", IsActive = true, TotalFilesDetected = 5 });
        _viewModel.ActiveWatches.Add(new WatchStatusDto { WatchId = Guid.NewGuid(), ChannelName = "Channel2", IsActive = false, TotalFilesDetected = 3 });
        _viewModel.ActiveWatches.Add(new WatchStatusDto { WatchId = Guid.NewGuid(), ChannelName = "Channel3", IsActive = true, TotalFilesDetected = 7 });

        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test1.mp4", DetectedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Pending, ChannelName = "Test", FileName = "test2.mp4", DetectedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Approved, ChannelName = "Test", FileName = "test3.mp4", DetectedAt = DateTime.UtcNow });
        _viewModel.PendingApprovals.Add(new PendingApprovalDto { Id = Guid.NewGuid(), Status = ApprovalStatus.Rejected, ChannelName = "Test", FileName = "test4.mp4", DetectedAt = DateTime.UtcNow });

        // Act
        _mockSignalRClient.Setup(x => x.GetActiveWatchesAsync())
            .ReturnsAsync(_viewModel.ActiveWatches.ToList());
        _mockSignalRClient.Setup(x => x.GetPendingApprovalsAsync())
            .ReturnsAsync(_viewModel.PendingApprovals.ToList());

        _mockSignalRClient.Raise(x => x.WatchStatusChanged += null, _mockSignalRClient.Object,
            new WatchStatusChangedEventArgs(Guid.NewGuid(), "Test", true, "Test"));

        // Assert
        _viewModel.TotalWatches.Should().Be(3);
        _viewModel.ActiveWatchesCount.Should().Be(2);
        _viewModel.TotalFilesDetected.Should().Be(15);
        _viewModel.PendingApprovalsCount.Should().Be(2);
        _viewModel.ApprovedCount.Should().Be(1);
        _viewModel.RejectedCount.Should().Be(1);
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_ShouldUnsubscribeFromEvents()
    {
        // Arrange
        var viewModel = new WatchViewModel(_mockSignalRClient.Object);

        // Act
        viewModel.Dispose();

        // Assert - Verify no exceptions and collections are cleared
        viewModel.ActiveWatches.Should().BeEmpty();
        viewModel.PendingApprovals.Should().BeEmpty();
        viewModel.FilteredPendingApprovals.Should().BeEmpty();
        viewModel.AvailableChannels.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var viewModel = new WatchViewModel(_mockSignalRClient.Object);

        // Act & Assert
        viewModel.Dispose();
        viewModel.Invoking(vm => vm.Dispose()).Should().NotThrow();
    }

    #endregion
}

/// <summary>
/// Event args classes for testing - these should match the actual SignalR client events
/// </summary>
public class NewFileForApprovalEventArgs : EventArgs
{
    public Guid ApprovalId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

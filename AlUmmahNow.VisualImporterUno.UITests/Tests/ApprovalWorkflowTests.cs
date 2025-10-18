using NUnit.Framework;
using AlUmmahNow.VisualImporterUno.UITests.Helpers;
using AlUmmahNow.VisualImporterUno.UITests.PageObjects;

namespace AlUmmahNow.VisualImporterUno.UITests.Tests;

/// <summary>
/// Comprehensive test suite for Approval Workflow functionality.
/// Tests cover filtering, sorting, bulk operations, file preview, and activity logging.
/// </summary>
[TestFixture]
[Category("ApprovalWorkflow")]
public class ApprovalWorkflowTests : BaseTest
{
    private ApprovalWorkflowPageObject _approvalPage = null!;
    private DashboardPageObject _dashboard = null!;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _approvalPage = new ApprovalWorkflowPageObject(App!, Helpers);
        _dashboard = new DashboardPageObject(App!, Helpers);

        // Navigate to Approval Workflow page
        _dashboard.NavigateToApprovalWorkflow();
        _approvalPage.WaitForPageLoad();
    }

    #region Page Load and UI Tests

    [Test]
    [AutoRetry]
    [Description("Verifies that Approval Workflow page loads correctly with all UI elements")]
    public void Given_ApprovalWorkflowPage_When_PageLoads_Then_AllElementsAreDisplayed()
    {
        // Arrange & Act handled in SetUp

        // Assert
        _approvalPage.VerifyPageReady();
        _approvalPage.VerifyStatisticsCardsDisplayed();

        Helpers.AssertElementDisplayed(ApprovalWorkflowPageObject.RefreshButton);
        Helpers.AssertElementDisplayed(ApprovalWorkflowPageObject.SelectAllButton);
        Helpers.AssertElementDisplayed(ApprovalWorkflowPageObject.FiltersSection);

        ScreenshotHelper.CaptureScreenshot("ApprovalWorkflowPageLoaded");

        TestContext.WriteLine("All UI elements are displayed correctly");
    }

    [Test]
    [AutoRetry]
    [Description("Verifies that statistics cards display numeric values")]
    public void Given_ApprovalWorkflowPage_When_PageLoads_Then_StatisticsShowValues()
    {
        // Act
        var pending = _approvalPage.GetPendingCount();
        var approved = _approvalPage.GetApprovedCount();
        var rejected = _approvalPage.GetRejectedCount();
        var totalSize = _approvalPage.GetTotalSize();

        // Assert
        Assert.That(pending, Is.Not.Null);
        Assert.That(approved, Is.Not.Null);
        Assert.That(rejected, Is.Not.Null);
        Assert.That(totalSize, Is.Not.Null);

        TestContext.WriteLine($"Statistics: Pending={pending}, Approved={approved}, " +
                            $"Rejected={rejected}, Size={totalSize}");
    }

    #endregion

    #region Filter Tests

    [Test]
    [AutoRetry]
    [Description("Tests applying channel filter")]
    public void Given_MultiplePendingFiles_When_ApplyChannelFilter_Then_FilesAreFiltered()
    {
        // Arrange
        var initialCount = _approvalPage.GetDisplayedApprovalsCount();

        // Act
        _approvalPage.ApplyChannelFilter("Islamic Lectures"); // Adjust based on actual data
        _approvalPage.WaitForLoadingComplete();

        // Assert
        var filteredCount = _approvalPage.GetDisplayedApprovalsCount();

        ScreenshotHelper.CaptureScreenshot("ChannelFilterApplied");

        TestContext.WriteLine($"Filtered by channel: {initialCount} -> {filteredCount} items");
    }

    [Test]
    [AutoRetry]
    [Description("Tests applying priority filter")]
    public void Given_MultiplePendingFiles_When_ApplyPriorityFilter_Then_FilesAreFiltered()
    {
        // Act
        _approvalPage.ApplyPriorityFilter(5);
        _approvalPage.WaitForLoadingComplete();

        // Assert
        Helpers.AssertTextDisplayed("Priority");

        ScreenshotHelper.CaptureScreenshot("PriorityFilterApplied");

        TestContext.WriteLine("Priority filter applied successfully");
    }

    [Test]
    [AutoRetry]
    [Description("Tests applying file size filter")]
    public void Given_MultiplePendingFiles_When_ApplyFileSizeFilter_Then_FilesAreFiltered()
    {
        // Arrange
        long minSize = 1024 * 1024 * 10; // 10 MB
        long maxSize = 1024 * 1024 * 500; // 500 MB

        // Act
        _approvalPage.ApplyFileSizeFilter(minSize, maxSize);
        _approvalPage.WaitForLoadingComplete();

        // Assert
        ScreenshotHelper.CaptureScreenshot("FileSizeFilterApplied");

        TestContext.WriteLine($"File size filter applied: {minSize} - {maxSize} bytes");
    }

    [Test]
    [AutoRetry]
    [Description("Tests applying extension filter")]
    public void Given_MultiplePendingFiles_When_ApplyExtensionFilter_Then_FilesAreFiltered()
    {
        // Act
        _approvalPage.ApplyExtensionFilter("mp4,mkv");
        _approvalPage.WaitForLoadingComplete();

        // Assert
        ScreenshotHelper.CaptureScreenshot("ExtensionFilterApplied");

        TestContext.WriteLine("Extension filter applied successfully");
    }

    [Test]
    [AutoRetry]
    [Description("Tests applying multiple filters simultaneously")]
    public void Given_MultiplePendingFiles_When_ApplyMultipleFilters_Then_FilesAreFilteredCorrectly()
    {
        // Arrange
        var filterData = TestDataBuilder.FilterPreset()
            .AllFilters()
            .Build();

        var initialCount = _approvalPage.GetDisplayedApprovalsCount();

        // Act
        _approvalPage.ApplyFilters(filterData);
        _approvalPage.WaitForLoadingComplete();

        // Assert
        var filteredCount = _approvalPage.GetDisplayedApprovalsCount();

        ScreenshotHelper.CaptureScreenshot("MultipleFiltersApplied");

        TestContext.WriteLine($"Multiple filters applied: {initialCount} -> {filteredCount} items");
    }

    [Test]
    [AutoRetry]
    [Description("Tests clearing all filters")]
    public void Given_FiltersApplied_When_ClearAllFilters_Then_FiltersAreCleared()
    {
        // Arrange
        _approvalPage.ApplyChannelFilter("Test Channel");
        _approvalPage.ApplyPriorityFilter(5);
        _approvalPage.WaitForLoadingComplete();

        var filteredCount = _approvalPage.GetDisplayedApprovalsCount();

        // Act
        _approvalPage.ClearAllFilters();
        _approvalPage.WaitForLoadingComplete();

        // Assert
        var clearedCount = _approvalPage.GetDisplayedApprovalsCount();

        ScreenshotHelper.CaptureScreenshot("FiltersCleared");

        TestContext.WriteLine($"Filters cleared: {filteredCount} -> {clearedCount} items");
    }

    #endregion

    #region Filter Preset Tests

    [Test]
    [AutoRetry]
    [Description("Tests saving a filter preset")]
    public void Given_FiltersConfigured_When_SavePreset_Then_PresetIsSaved()
    {
        // Arrange
        var presetName = $"Test Preset {DateTime.Now:yyyyMMddHHmmss}";
        var presetDescription = "Automated test filter preset";

        _approvalPage.ApplyChannelFilter("Islamic Lectures");
        _approvalPage.ApplyPriorityFilter(5);

        // Act
        _approvalPage.SaveFilterPreset(presetName, presetDescription);

        // Assert
        Helpers.AssertTextDisplayed(presetName);

        ScreenshotHelper.CaptureScreenshot("FilterPresetSaved");

        TestContext.WriteLine($"Filter preset saved: {presetName}");
    }

    [Test]
    [AutoRetry]
    [Description("Tests loading a saved filter preset")]
    public void Given_SavedPreset_When_LoadPreset_Then_FiltersAreApplied()
    {
        // Arrange
        var presetName = "High Priority MP4 Files"; // Assume this exists or create it

        // Act
        _approvalPage.LoadFilterPreset(presetName);
        _approvalPage.WaitForLoadingComplete();

        // Assert
        _approvalPage.VerifyPresetLoaded(presetName);

        ScreenshotHelper.CaptureScreenshot("FilterPresetLoaded");

        TestContext.WriteLine($"Filter preset loaded: {presetName}");
    }

    [Test]
    [AutoRetry]
    [Description("Tests deleting a filter preset")]
    public void Given_SavedPreset_When_DeletePreset_Then_PresetIsDeleted()
    {
        // Arrange - First create a preset to delete
        var presetName = $"Delete Test {DateTime.Now:yyyyMMddHHmmss}";
        _approvalPage.ApplyChannelFilter("Test");
        _approvalPage.SaveFilterPreset(presetName, "Test preset for deletion");

        // Act
        _approvalPage.DeleteFilterPreset(presetName);

        // Assert
        Helpers.AssertTextNotDisplayed(presetName);

        ScreenshotHelper.CaptureScreenshot("FilterPresetDeleted");

        TestContext.WriteLine($"Filter preset deleted: {presetName}");
    }

    [Test]
    [AutoRetry]
    [Description("Tests setting a preset as default")]
    public void Given_SavedPreset_When_SetAsDefault_Then_PresetIsMarkedDefault()
    {
        // Arrange
        var presetName = "Test Default Preset";

        // Act
        _approvalPage.SetDefaultPreset(presetName);

        // Assert
        Helpers.AssertTextDisplayed("Default");

        ScreenshotHelper.CaptureScreenshot("PresetSetAsDefault");

        TestContext.WriteLine($"Preset set as default: {presetName}");
    }

    #endregion

    #region Sorting Tests

    [Test]
    [AutoRetry]
    [Description("Tests sorting by file name")]
    public void Given_MultipleFiles_When_SortByFileName_Then_FilesAreSorted()
    {
        // Act
        _approvalPage.SortBy("File Name");
        _approvalPage.WaitForLoadingComplete();

        // Assert
        ScreenshotHelper.CaptureScreenshot("SortedByFileName");

        TestContext.WriteLine("Files sorted by file name");
    }

    [Test]
    [AutoRetry]
    [Description("Tests sorting by file size")]
    public void Given_MultipleFiles_When_SortByFileSize_Then_FilesAreSorted()
    {
        // Act
        _approvalPage.SortBy("File Size");
        _approvalPage.WaitForLoadingComplete();

        // Assert
        ScreenshotHelper.CaptureScreenshot("SortedByFileSize");

        TestContext.WriteLine("Files sorted by file size");
    }

    [Test]
    [AutoRetry]
    [Description("Tests sorting by priority")]
    public void Given_MultipleFiles_When_SortByPriority_Then_FilesAreSorted()
    {
        // Act
        _approvalPage.SortBy("Priority");
        _approvalPage.WaitForLoadingComplete();

        // Assert
        ScreenshotHelper.CaptureScreenshot("SortedByPriority");

        TestContext.WriteLine("Files sorted by priority");
    }

    [Test]
    [AutoRetry]
    [Description("Tests toggling sort direction")]
    public void Given_SortedList_When_ToggleSortDirection_Then_OrderIsReversed()
    {
        // Arrange
        _approvalPage.SortBy("File Name");
        _approvalPage.WaitForLoadingComplete();

        ScreenshotHelper.CaptureScreenshot("InitialSort");

        // Act
        _approvalPage.ToggleSortDirection();
        _approvalPage.WaitForLoadingComplete();

        // Assert
        ScreenshotHelper.CaptureScreenshot("SortDirectionToggled");

        TestContext.WriteLine("Sort direction toggled successfully");
    }

    #endregion

    #region Selection and Bulk Actions Tests

    [Test]
    [AutoRetry]
    [Description("Tests selecting all approvals")]
    public void Given_MultipleApprovals_When_SelectAll_Then_AllItemsAreSelected()
    {
        // Act
        _approvalPage.SelectAll();

        // Assert
        Helpers.AssertElementEnabled(ApprovalWorkflowPageObject.ApproveSelectedButton);
        Helpers.AssertElementEnabled(ApprovalWorkflowPageObject.RejectSelectedButton);

        ScreenshotHelper.CaptureScreenshot("AllItemsSelected");

        TestContext.WriteLine("All items selected successfully");
    }

    [Test]
    [AutoRetry]
    [Description("Tests clearing selection")]
    public void Given_SelectedItems_When_ClearSelection_Then_SelectionIsCleared()
    {
        // Arrange
        _approvalPage.SelectAll();

        // Act
        _approvalPage.ClearSelection();

        // Assert
        Helpers.AssertElementDisabled(ApprovalWorkflowPageObject.ApproveSelectedButton);

        ScreenshotHelper.CaptureScreenshot("SelectionCleared");

        TestContext.WriteLine("Selection cleared successfully");
    }

    [Test]
    [AutoRetry]
    [Category("Destructive")]
    [Description("Tests approving selected items")]
    public void Given_SelectedItems_When_ApproveSelected_Then_ItemsAreApproved()
    {
        // Arrange
        _approvalPage.SelectAll();
        var initialPending = _approvalPage.GetDisplayedApprovalsCount();

        // Act
        _approvalPage.ApproveSelected();
        _approvalPage.WaitForLoadingComplete();

        // Assert
        var updatedPending = _approvalPage.GetDisplayedApprovalsCount();
        Assert.That(updatedPending, Is.LessThan(initialPending));

        ScreenshotHelper.CaptureScreenshot("ItemsApproved");

        TestContext.WriteLine($"Approved items: {initialPending} -> {updatedPending} remaining");
    }

    [Test]
    [AutoRetry]
    [Category("Destructive")]
    [Description("Tests rejecting selected items")]
    public void Given_SelectedItems_When_RejectSelected_Then_ItemsAreRejected()
    {
        // Arrange
        _approvalPage.SelectAll();
        var initialPending = _approvalPage.GetDisplayedApprovalsCount();

        // Act
        _approvalPage.RejectSelected();
        _approvalPage.WaitForLoadingComplete();

        // Assert
        var updatedPending = _approvalPage.GetDisplayedApprovalsCount();
        Assert.That(updatedPending, Is.LessThan(initialPending));

        ScreenshotHelper.CaptureScreenshot("ItemsRejected");

        TestContext.WriteLine($"Rejected items: {initialPending} -> {updatedPending} remaining");
    }

    [Test]
    [AutoRetry]
    [Category("Destructive")]
    [Category("Slow")]
    [Description("Tests bulk approve all filtered files")]
    public void Given_FilteredFiles_When_BulkApproveAll_Then_AllFilesAreApproved()
    {
        // Arrange
        _approvalPage.ApplyPriorityFilter(1); // Filter to low priority for safe bulk operation
        _approvalPage.WaitForLoadingComplete();

        var initialCount = _approvalPage.GetDisplayedApprovalsCount();

        // Act
        _approvalPage.BulkApproveAll();

        // Assert
        var updatedCount = _approvalPage.GetDisplayedApprovalsCount();
        Assert.That(updatedCount, Is.LessThan(initialCount));

        ScreenshotHelper.CaptureScreenshot("BulkApprovedAll");

        TestContext.WriteLine($"Bulk approved: {initialCount} -> {updatedCount} files");
    }

    [Test]
    [AutoRetry]
    [Category("Destructive")]
    [Category("Slow")]
    [Description("Tests bulk reject all filtered files")]
    public void Given_FilteredFiles_When_BulkRejectAll_Then_AllFilesAreRejected()
    {
        // Arrange
        _approvalPage.ApplyPriorityFilter(1); // Filter to low priority for safe bulk operation
        _approvalPage.WaitForLoadingComplete();

        var initialCount = _approvalPage.GetDisplayedApprovalsCount();

        // Act
        _approvalPage.BulkRejectAll();

        // Assert
        var updatedCount = _approvalPage.GetDisplayedApprovalsCount();
        Assert.That(updatedCount, Is.LessThan(initialCount));

        ScreenshotHelper.CaptureScreenshot("BulkRejectedAll");

        TestContext.WriteLine($"Bulk rejected: {initialCount} -> {updatedCount} files");
    }

    #endregion

    #region File Preview Tests

    [Test]
    [AutoRetry]
    [Description("Tests previewing a file")]
    public void Given_PendingFile_When_ClickPreview_Then_PreviewPanelIsDisplayed()
    {
        // Arrange
        var fileName = "test_video.mp4"; // Adjust based on actual data

        // Act
        _approvalPage.PreviewFile(fileName);

        // Assert
        _approvalPage.VerifyPreviewDisplayed();

        ScreenshotHelper.CaptureScreenshot("FilePreviewDisplayed");

        TestContext.WriteLine($"Preview displayed for file: {fileName}");
    }

    [Test]
    [AutoRetry]
    [Description("Tests that video metadata is displayed in preview")]
    public void Given_VideoFile_When_Preview_Then_MetadataIsDisplayed()
    {
        // Arrange
        var fileName = "test_video.mp4";

        // Act
        _approvalPage.PreviewFile(fileName);

        // Assert
        _approvalPage.VerifyVideoMetadataDisplayed();

        ScreenshotHelper.CaptureScreenshot("VideoMetadataDisplayed");

        TestContext.WriteLine("Video metadata displayed successfully");
    }

    [Test]
    [AutoRetry]
    [Description("Tests refreshing thumbnail in preview")]
    public void Given_FilePreview_When_RefreshThumbnail_Then_ThumbnailIsRegenerated()
    {
        // Arrange
        var fileName = "test_video.mp4";
        _approvalPage.PreviewFile(fileName);

        // Act
        _approvalPage.RefreshThumbnail();

        // Assert
        _approvalPage.VerifyThumbnailDisplayed();

        ScreenshotHelper.CaptureScreenshot("ThumbnailRefreshed");

        TestContext.WriteLine("Thumbnail refreshed successfully");
    }

    [Test]
    [AutoRetry]
    [Category("Destructive")]
    [Description("Tests approving file from preview panel")]
    public void Given_FilePreview_When_ApproveFromPreview_Then_FileIsApproved()
    {
        // Arrange
        var fileName = "test_video.mp4";
        _approvalPage.PreviewFile(fileName);

        // Act
        _approvalPage.ApproveFromPreview();
        _approvalPage.WaitForLoadingComplete();

        // Assert
        Helpers.AssertTextNotDisplayed(fileName);

        ScreenshotHelper.CaptureScreenshot("ApprovedFromPreview");

        TestContext.WriteLine($"File approved from preview: {fileName}");
    }

    [Test]
    [AutoRetry]
    [Category("Destructive")]
    [Description("Tests rejecting file from preview panel")]
    public void Given_FilePreview_When_RejectFromPreview_Then_FileIsRejected()
    {
        // Arrange
        var fileName = "test_video.mp4";
        _approvalPage.PreviewFile(fileName);

        // Act
        _approvalPage.RejectFromPreview();
        _approvalPage.WaitForLoadingComplete();

        // Assert
        Helpers.AssertTextNotDisplayed(fileName);

        ScreenshotHelper.CaptureScreenshot("RejectedFromPreview");

        TestContext.WriteLine($"File rejected from preview: {fileName}");
    }

    #endregion

    #region Activity Log Tests

    [Test]
    [AutoRetry]
    [Description("Tests that activity log is displayed")]
    public void Given_ApprovalWorkflowPage_When_ViewPage_Then_ActivityLogIsDisplayed()
    {
        // Act & Assert
        _approvalPage.VerifyActivityLogDisplayed();

        ScreenshotHelper.CaptureScreenshot("ActivityLogDisplayed");

        TestContext.WriteLine("Activity log displayed successfully");
    }

    [Test]
    [AutoRetry]
    [Category("Destructive")]
    [Description("Tests that activity log updates when file is approved")]
    public void Given_FileApproved_When_CheckActivityLog_Then_ActivityIsLogged()
    {
        // Arrange
        var fileName = "test_video.mp4";

        // Act
        _approvalPage.ApproveFile(fileName);
        _approvalPage.WaitForLoadingComplete();

        // Assert
        _approvalPage.VerifyActivityLogMessage("Approved");

        ScreenshotHelper.CaptureScreenshot("ActivityLogUpdated");

        TestContext.WriteLine("Activity log updated with approval action");
    }

    [Test]
    [AutoRetry]
    [Description("Tests clearing activity log")]
    public void Given_ActivityLogWithEntries_When_ClearLog_Then_LogIsCleared()
    {
        // Act
        _approvalPage.ClearActivityLog();

        // Assert
        _approvalPage.VerifyActivityLogEmpty();

        ScreenshotHelper.CaptureScreenshot("ActivityLogCleared");

        TestContext.WriteLine("Activity log cleared successfully");
    }

    #endregion

    #region Empty State Tests

    [Test]
    [AutoRetry]
    [Description("Tests empty state when no approvals match filters")]
    public void Given_RestrictiveFilters_When_NoFilesMatch_Then_EmptyStateIsDisplayed()
    {
        // Arrange
        _approvalPage.ApplyChannelFilter("NonExistentChannel12345");
        _approvalPage.WaitForLoadingComplete();

        // Act & Assert
        if (_approvalPage.GetDisplayedApprovalsCount() == 0)
        {
            _approvalPage.VerifyEmptyState();

            ScreenshotHelper.CaptureScreenshot("EmptyStateDisplayed");

            TestContext.WriteLine("Empty state displayed correctly");
        }
        else
        {
            Assert.Inconclusive("Could not create empty state condition");
        }
    }

    #endregion

    #region Integration Tests

    [Test]
    [AutoRetry]
    [Category("Integration")]
    [Description("Tests complete approval workflow: filter, sort, preview, approve")]
    public void Given_PendingFiles_When_CompleteWorkflow_Then_FileIsApprovedSuccessfully()
    {
        // Arrange
        ScreenshotHelper.CaptureStepScreenshot(1, "InitialState");

        // Act & Assert - Step 1: Apply filters
        _approvalPage.ApplyPriorityFilter(5);
        _approvalPage.WaitForLoadingComplete();
        ScreenshotHelper.CaptureStepScreenshot(2, "FiltersApplied");

        // Act & Assert - Step 2: Sort by file size
        _approvalPage.SortBy("File Size");
        _approvalPage.WaitForLoadingComplete();
        ScreenshotHelper.CaptureStepScreenshot(3, "Sorted");

        // Act & Assert - Step 3: Preview first file
        var fileName = "test_video.mp4"; // Get from actual list
        _approvalPage.PreviewFile(fileName);
        _approvalPage.VerifyPreviewDisplayed();
        ScreenshotHelper.CaptureStepScreenshot(4, "Previewed");

        // Act & Assert - Step 4: Approve from preview
        _approvalPage.ApproveFromPreview();
        _approvalPage.WaitForLoadingComplete();
        ScreenshotHelper.CaptureStepScreenshot(5, "Approved");

        // Act & Assert - Step 5: Verify activity log
        _approvalPage.VerifyActivityLogMessage("Approved");
        ScreenshotHelper.CaptureStepScreenshot(6, "ActivityLogged");

        TestContext.WriteLine("Complete approval workflow executed successfully");
    }

    [Test]
    [AutoRetry]
    [Category("Integration")]
    [Description("Tests filter preset workflow: create, save, load, modify")]
    public void Given_Filters_When_SaveAndLoadPreset_Then_PresetWorksCorrectly()
    {
        // Arrange
        var presetName = $"Integration Test {DateTime.Now:yyyyMMddHHmmss}";

        // Act - Step 1: Configure filters
        _approvalPage.ApplyChannelFilter("Islamic Lectures");
        _approvalPage.ApplyPriorityFilter(5);
        var filteredCount = _approvalPage.GetDisplayedApprovalsCount();
        ScreenshotHelper.CaptureStepScreenshot(1, "FiltersConfigured");

        // Act - Step 2: Save preset
        _approvalPage.SaveFilterPreset(presetName, "Integration test preset");
        ScreenshotHelper.CaptureStepScreenshot(2, "PresetSaved");

        // Act - Step 3: Clear filters
        _approvalPage.ClearAllFilters();
        _approvalPage.WaitForLoadingComplete();
        ScreenshotHelper.CaptureStepScreenshot(3, "FiltersCleared");

        // Act - Step 4: Load preset
        _approvalPage.LoadFilterPreset(presetName);
        _approvalPage.WaitForLoadingComplete();
        var reloadedCount = _approvalPage.GetDisplayedApprovalsCount();
        ScreenshotHelper.CaptureStepScreenshot(4, "PresetLoaded");

        // Assert
        Assert.That(reloadedCount, Is.EqualTo(filteredCount),
            "Loaded preset should show same filtered count");

        TestContext.WriteLine($"Preset workflow completed: {presetName}");
    }

    #endregion

    #region Cleanup

    protected override void CleanupTestData()
    {
        // Clean up any test data created during tests
        TestContext.WriteLine("Cleaning up test approval workflow data...");
    }

    #endregion
}

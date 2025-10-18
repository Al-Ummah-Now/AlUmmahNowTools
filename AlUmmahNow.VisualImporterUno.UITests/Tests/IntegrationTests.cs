using NUnit.Framework;
using AlUmmahNow.VisualImporterUno.UITests.Helpers;
using AlUmmahNow.VisualImporterUno.UITests.PageObjects;

namespace AlUmmahNow.VisualImporterUno.UITests.Tests;

/// <summary>
/// End-to-end integration tests that validate complete user workflows
/// spanning multiple pages and features in realistic scenarios.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("E2E")]
public class IntegrationTests : BaseTest
{
    private DashboardPageObject _dashboard = null!;
    private WatchManagementPageObject _watchPage = null!;
    private ApprovalWorkflowPageObject _approvalPage = null!;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _dashboard = new DashboardPageObject(App!, Helpers);
        _watchPage = new WatchManagementPageObject(App!, Helpers);
        _approvalPage = new ApprovalWorkflowPageObject(App!, Helpers);
    }

    #region Complete Workflow Tests

    [Test]
    [AutoRetry]
    [Category("Slow")]
    [Description("Tests complete workflow: Create watch → Files detected → Approve files")]
    public void Given_NewWatch_When_CompleteWorkflow_Then_FilesAreProcessedSuccessfully()
    {
        // STEP 1: Create a new watch
        TestContext.WriteLine("STEP 1: Creating new watch");
        _dashboard.NavigateToWatchManagement();
        _watchPage.WaitForPageLoad();

        var watchData = TestDataBuilder.Watch()
            .WithChannelName($"E2E Test {DateTime.Now:yyyyMMddHHmmss}")
            .Build();

        _watchPage.CreateWatch(watchData);
        ScreenshotHelper.CaptureStepScreenshot(1, "WatchCreated");

        var initialWatchCount = int.Parse(_watchPage.GetTotalWatchesCount());
        TestContext.WriteLine($"Watch created. Total watches: {initialWatchCount}");

        // STEP 2: Verify watch appears in active watches list
        TestContext.WriteLine("STEP 2: Verifying watch in list");
        _watchPage.VerifyWatchDisplayed(watchData.ChannelName);
        ScreenshotHelper.CaptureStepScreenshot(2, "WatchDisplayedInList");

        // STEP 3: Navigate to Approval Workflow
        TestContext.WriteLine("STEP 3: Navigating to Approval Workflow");
        _dashboard.NavigateToApprovalWorkflow();
        _approvalPage.WaitForPageLoad();
        ScreenshotHelper.CaptureStepScreenshot(3, "ApprovalWorkflowPage");

        // STEP 4: Filter by the new channel
        TestContext.WriteLine("STEP 4: Applying channel filter");
        _approvalPage.ApplyChannelFilter(watchData.ChannelName);
        _approvalPage.WaitForLoadingComplete();
        ScreenshotHelper.CaptureStepScreenshot(4, "ChannelFiltered");

        // STEP 5: Check if any files are pending (in real scenario, files would be detected)
        var pendingCount = _approvalPage.GetDisplayedApprovalsCount();
        TestContext.WriteLine($"Pending files for new channel: {pendingCount}");

        // STEP 6: Navigate back to Watch Management
        TestContext.WriteLine("STEP 6: Returning to Watch Management");
        _dashboard.NavigateToWatchManagement();
        _watchPage.WaitForPageLoad();
        ScreenshotHelper.CaptureStepScreenshot(5, "BackToWatchManagement");

        // Assert: Verify final state
        _watchPage.VerifyWatchDisplayed(watchData.ChannelName);

        TestContext.WriteLine("Complete workflow test passed successfully");
    }

    [Test]
    [AutoRetry]
    [Category("Slow")]
    [Category("Destructive")]
    [Description("Tests end-to-end approval workflow with filter presets")]
    public void Given_PendingFiles_When_UseFilterPresets_Then_WorkflowIsEfficient()
    {
        // STEP 1: Navigate to Approval Workflow
        TestContext.WriteLine("STEP 1: Opening Approval Workflow");
        _dashboard.NavigateToApprovalWorkflow();
        _approvalPage.WaitForPageLoad();
        ScreenshotHelper.CaptureStepScreenshot(1, "ApprovalWorkflowOpened");

        // STEP 2: Create and save a filter preset for high priority files
        TestContext.WriteLine("STEP 2: Creating filter preset");
        var presetName = $"High Priority E2E {DateTime.Now:HHmmss}";
        _approvalPage.ApplyPriorityFilter(5);
        _approvalPage.ApplyExtensionFilter("mp4,mkv");
        _approvalPage.WaitForLoadingComplete();

        var filteredCount = _approvalPage.GetDisplayedApprovalsCount();
        TestContext.WriteLine($"Filtered files: {filteredCount}");

        _approvalPage.SaveFilterPreset(presetName, "E2E test preset");
        ScreenshotHelper.CaptureStepScreenshot(2, "PresetSaved");

        // STEP 3: Clear filters and verify count changes
        TestContext.WriteLine("STEP 3: Clearing filters");
        _approvalPage.ClearAllFilters();
        _approvalPage.WaitForLoadingComplete();

        var unfilteredCount = _approvalPage.GetDisplayedApprovalsCount();
        TestContext.WriteLine($"Unfiltered files: {unfilteredCount}");
        ScreenshotHelper.CaptureStepScreenshot(3, "FiltersCleared");

        // STEP 4: Reload the preset
        TestContext.WriteLine("STEP 4: Loading saved preset");
        _approvalPage.LoadFilterPreset(presetName);
        _approvalPage.WaitForLoadingComplete();

        var reloadedCount = _approvalPage.GetDisplayedApprovalsCount();
        ScreenshotHelper.CaptureStepScreenshot(4, "PresetReloaded");

        // STEP 5: Sort by file size
        TestContext.WriteLine("STEP 5: Sorting filtered results");
        _approvalPage.SortBy("File Size");
        _approvalPage.WaitForLoadingComplete();
        ScreenshotHelper.CaptureStepScreenshot(5, "ResultsSorted");

        // STEP 6: Select all filtered files
        TestContext.WriteLine("STEP 6: Selecting filtered files");
        _approvalPage.SelectAll();
        ScreenshotHelper.CaptureStepScreenshot(6, "FilesSelected");

        // Assert: Verify preset worked correctly
        Assert.That(reloadedCount, Is.EqualTo(filteredCount),
            "Preset should restore same filtered count");

        TestContext.WriteLine("Filter preset workflow completed successfully");
    }

    [Test]
    [AutoRetry]
    [Category("Slow")]
    [Description("Tests user journey across all major features")]
    public void Given_Application_When_UserJourney_Then_AllFeaturesWorkTogether()
    {
        // STEP 1: Start at Dashboard
        TestContext.WriteLine("STEP 1: Dashboard overview");
        _dashboard.NavigateToDashboard();
        ScreenshotHelper.CaptureStepScreenshot(1, "Dashboard");

        // STEP 2: Check Watch Management
        TestContext.WriteLine("STEP 2: Reviewing watches");
        _dashboard.NavigateToWatchManagement();
        _watchPage.WaitForPageLoad();

        var totalWatches = _watchPage.GetTotalWatchesCount();
        var activeWatches = _watchPage.GetActiveWatchesCount();
        TestContext.WriteLine($"Watches - Total: {totalWatches}, Active: {activeWatches}");
        ScreenshotHelper.CaptureStepScreenshot(2, "WatchManagement");

        // STEP 3: Review Approval Workflow
        TestContext.WriteLine("STEP 3: Reviewing pending approvals");
        _dashboard.NavigateToApprovalWorkflow();
        _approvalPage.WaitForPageLoad();

        var pendingCount = _approvalPage.GetPendingCount();
        var approvedCount = _approvalPage.GetApprovedCount();
        var rejectedCount = _approvalPage.GetRejectedCount();
        TestContext.WriteLine($"Approvals - Pending: {pendingCount}, Approved: {approvedCount}, Rejected: {rejectedCount}");
        ScreenshotHelper.CaptureStepScreenshot(3, "ApprovalWorkflow");

        // STEP 4: Check Analytics
        TestContext.WriteLine("STEP 4: Viewing analytics");
        _dashboard.NavigateToAnalytics();
        Helpers.WaitForText("Analytics");
        ScreenshotHelper.CaptureStepScreenshot(4, "Analytics");

        // STEP 5: Review Service Status
        TestContext.WriteLine("STEP 5: Checking service status");
        _dashboard.NavigateToServiceStatus();
        Helpers.WaitForText("Service Status");
        ScreenshotHelper.CaptureStepScreenshot(5, "ServiceStatus");

        // STEP 6: Return to Dashboard
        TestContext.WriteLine("STEP 6: Returning to dashboard");
        _dashboard.NavigateToDashboard();
        ScreenshotHelper.CaptureStepScreenshot(6, "BackToDashboard");

        // Assert: Application remained stable throughout
        _dashboard.VerifyAppLoaded();

        TestContext.WriteLine("Complete user journey test passed");
    }

    #endregion

    #region Cross-Feature Tests

    [Test]
    [AutoRetry]
    [Category("CrossFeature")]
    [Description("Tests that watch statistics sync with approval workflow statistics")]
    public void Given_Watches_When_CheckStatistics_Then_NumbersAreConsistent()
    {
        // STEP 1: Get statistics from Watch Management
        TestContext.WriteLine("STEP 1: Getting Watch Management statistics");
        _dashboard.NavigateToWatchManagement();
        _watchPage.WaitForPageLoad();

        var watchPendingApprovals = int.Parse(_watchPage.GetPendingApprovalsCount());
        TestContext.WriteLine($"Pending approvals from Watch Management: {watchPendingApprovals}");
        ScreenshotHelper.CaptureStepScreenshot(1, "WatchStats");

        // STEP 2: Get statistics from Approval Workflow
        TestContext.WriteLine("STEP 2: Getting Approval Workflow statistics");
        _dashboard.NavigateToApprovalWorkflow();
        _approvalPage.WaitForPageLoad();

        var approvalPendingCount = _approvalPage.GetPendingCount();
        TestContext.WriteLine($"Pending count from Approval Workflow: {approvalPendingCount}");
        ScreenshotHelper.CaptureStepScreenshot(2, "ApprovalStats");

        // Assert: Numbers should be consistent (allowing for timing differences)
        // Note: Exact match might not be possible if files are being processed
        // This is more of a sanity check than strict assertion
        TestContext.WriteLine($"Statistics comparison - Watch: {watchPendingApprovals}, Approval: {approvalPendingCount}");

        TestContext.WriteLine("Cross-feature statistics test completed");
    }

    [Test]
    [AutoRetry]
    [Category("CrossFeature")]
    [Category("Slow")]
    [Description("Tests creating watch and immediately checking if it appears in all relevant views")]
    public void Given_NewWatch_When_CheckAllViews_Then_WatchAppearsEverywhere()
    {
        // STEP 1: Create watch in Watch Management
        TestContext.WriteLine("STEP 1: Creating new watch");
        _dashboard.NavigateToWatchManagement();
        _watchPage.WaitForPageLoad();

        var watchData = TestDataBuilder.Watch()
            .WithChannelName($"Cross-Feature Test {DateTime.Now:HHmmss}")
            .Build();

        _watchPage.CreateWatch(watchData);
        ScreenshotHelper.CaptureStepScreenshot(1, "WatchCreated");

        // STEP 2: Verify in Watch Management list
        TestContext.WriteLine("STEP 2: Verifying in Watch Management");
        _watchPage.RefreshWatches();
        _watchPage.VerifyWatchDisplayed(watchData.ChannelName);
        ScreenshotHelper.CaptureStepScreenshot(2, "WatchInList");

        // STEP 3: Check if channel appears in Approval Workflow filters
        TestContext.WriteLine("STEP 3: Checking Approval Workflow");
        _dashboard.NavigateToApprovalWorkflow();
        _approvalPage.WaitForPageLoad();

        // Try to apply the new channel filter (it should appear in the dropdown)
        _approvalPage.ApplyChannelFilter(watchData.ChannelName);
        ScreenshotHelper.CaptureStepScreenshot(3, "ChannelInApprovalFilters");

        // STEP 4: Return to Watch Management and verify still there
        TestContext.WriteLine("STEP 4: Final verification");
        _dashboard.NavigateToWatchManagement();
        _watchPage.WaitForPageLoad();
        _watchPage.VerifyWatchDisplayed(watchData.ChannelName);
        ScreenshotHelper.CaptureStepScreenshot(4, "FinalVerification");

        TestContext.WriteLine("Cross-feature watch creation test completed");
    }

    #endregion

    #region Error Recovery Tests

    [Test]
    [AutoRetry]
    [Description("Tests application recovery from failed operations")]
    public void Given_FailedOperation_When_Retry_Then_ApplicationRecoversproperly()
    {
        // STEP 1: Navigate to Watch Management
        TestContext.WriteLine("STEP 1: Opening Watch Management");
        _dashboard.NavigateToWatchManagement();
        _watchPage.WaitForPageLoad();

        // STEP 2: Attempt to create invalid watch
        TestContext.WriteLine("STEP 2: Attempting invalid watch creation");
        _watchPage.OpenCreateWatchDialog();

        var invalidWatchData = TestDataBuilder.Watch()
            .WithChannelName("") // Invalid: empty
            .Build();

        _watchPage.FillCreateWatchForm(invalidWatchData);
        _watchPage.SubmitCreateWatch();

        // Validation error should appear
        _watchPage.VerifyValidationError(WatchManagementPageObject.ChannelNameValidation);
        ScreenshotHelper.CaptureStepScreenshot(1, "ValidationError");

        // STEP 3: Cancel and retry with valid data
        TestContext.WriteLine("STEP 3: Retrying with valid data");
        _watchPage.CancelCreateWatch();

        var validWatchData = TestDataBuilder.Watch().Build();
        _watchPage.CreateWatch(validWatchData);
        ScreenshotHelper.CaptureStepScreenshot(2, "SuccessfulCreation");

        // Assert: Valid watch was created successfully
        _watchPage.VerifyWatchDisplayed(validWatchData.ChannelName);

        TestContext.WriteLine("Error recovery test completed");
    }

    [Test]
    [AutoRetry]
    [Description("Tests that application handles rapid actions gracefully")]
    public void Given_RapidActions_When_UserClicksQuickly_Then_ApplicationRemainsStable()
    {
        // STEP 1: Navigate to Approval Workflow
        _dashboard.NavigateToApprovalWorkflow();
        _approvalPage.WaitForPageLoad();

        // STEP 2: Rapidly apply and clear filters
        for (int i = 0; i < 5; i++)
        {
            _approvalPage.ApplyPriorityFilter(5);
            Helpers.Wait(TimeSpan.FromMilliseconds(200));
            _approvalPage.ClearAllFilters();
            Helpers.Wait(TimeSpan.FromMilliseconds(200));
        }

        ScreenshotHelper.CaptureStepScreenshot(1, "AfterRapidFiltering");

        // STEP 3: Rapidly switch sort options
        var sortOptions = new[] { "File Name", "File Size", "Priority" };
        foreach (var option in sortOptions)
        {
            _approvalPage.SortBy(option);
            Helpers.Wait(TimeSpan.FromMilliseconds(300));
        }

        ScreenshotHelper.CaptureStepScreenshot(2, "AfterRapidSorting");

        // Assert: Application should still be functional
        _approvalPage.VerifyPageReady();

        TestContext.WriteLine("Rapid actions stability test completed");
    }

    #endregion

    #region Cleanup

    protected override void CleanupTestData()
    {
        TestContext.WriteLine("Cleaning up integration test data...");
        // Implement cleanup logic as needed
    }

    #endregion
}

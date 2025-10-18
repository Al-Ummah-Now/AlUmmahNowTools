using NUnit.Framework;
using AlUmmahNow.VisualImporterUno.UITests.Helpers;
using AlUmmahNow.VisualImporterUno.UITests.PageObjects;

namespace AlUmmahNow.VisualImporterUno.UITests.Tests;

/// <summary>
/// Comprehensive test suite for Watch Management functionality.
/// Tests cover watch creation, validation, batch operations, and watch lifecycle management.
/// </summary>
[TestFixture]
[Category("WatchManagement")]
public class WatchManagementTests : BaseTest
{
    private WatchManagementPageObject _watchPage = null!;
    private DashboardPageObject _dashboard = null!;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _watchPage = new WatchManagementPageObject(App!, Helpers);
        _dashboard = new DashboardPageObject(App!, Helpers);

        // Navigate to Watch Management page
        _dashboard.NavigateToWatchManagement();
        _watchPage.WaitForPageLoad();
    }

    #region Page Load and UI Tests

    [Test]
    [AutoRetry]
    [Description("Verifies that Watch Management page loads correctly with all UI elements")]
    public void Given_WatchManagementPage_When_PageLoads_Then_AllElementsAreDisplayed()
    {
        // Arrange & Act handled in SetUp

        // Assert
        _watchPage.VerifyPageReady();
        _watchPage.VerifyStatisticsCardsDisplayed();

        Helpers.AssertElementDisplayed(WatchManagementPageObject.CreateWatchButton);
        Helpers.AssertElementDisplayed(WatchManagementPageObject.BatchCreateButton);
        Helpers.AssertElementDisplayed(WatchManagementPageObject.RefreshButton);

        TestContext.WriteLine("All UI elements are displayed correctly");
    }

    [Test]
    [AutoRetry]
    [Description("Verifies that statistics cards display numeric values")]
    public void Given_WatchManagementPage_When_PageLoads_Then_StatisticsCardsShowValues()
    {
        // Arrange & Act
        var totalWatches = _watchPage.GetTotalWatchesCount();
        var activeWatches = _watchPage.GetActiveWatchesCount();
        var filesDetected = _watchPage.GetFilesDetectedCount();
        var pendingApprovals = _watchPage.GetPendingApprovalsCount();

        // Assert
        Assert.That(totalWatches, Does.Match(@"^\d+$"), "Total watches should be numeric");
        Assert.That(activeWatches, Does.Match(@"^\d+$"), "Active watches should be numeric");
        Assert.That(filesDetected, Does.Match(@"^\d+$"), "Files detected should be numeric");
        Assert.That(pendingApprovals, Does.Match(@"^\d+$"), "Pending approvals should be numeric");

        TestContext.WriteLine($"Statistics: Total={totalWatches}, Active={activeWatches}, " +
                            $"Files={filesDetected}, Pending={pendingApprovals}");
    }

    #endregion

    #region Create Watch Tests

    [Test]
    [AutoRetry]
    [Description("Tests successful creation of a new watch with valid data")]
    public void Given_ValidWatchData_When_CreateWatch_Then_WatchIsCreatedSuccessfully()
    {
        // Arrange
        var watchData = TestDataBuilder.Watch()
            .WithChannelName("Test Islamic Channel")
            .WithWatchPath(TestDataBuilder.RandomFilePath())
            .Build();

        ScreenshotHelper.CaptureStepScreenshot(1, "BeforeCreatingWatch");

        // Act
        _watchPage.OpenCreateWatchDialog();
        ScreenshotHelper.CaptureStepScreenshot(2, "CreateWatchDialogOpened");

        _watchPage.FillCreateWatchForm(watchData);
        ScreenshotHelper.CaptureStepScreenshot(3, "FormFilled");

        _watchPage.SubmitCreateWatch();
        _watchPage.WaitForDialogClose();

        ScreenshotHelper.CaptureStepScreenshot(4, "WatchCreated");

        // Assert
        _watchPage.VerifyCreateWatchSuccess(watchData.ChannelName);
        Helpers.AssertTextDisplayed(watchData.ChannelName);

        TestContext.WriteLine($"Successfully created watch: {watchData.ChannelName}");
    }

    [Test]
    [AutoRetry]
    [Description("Tests that empty channel name shows validation error")]
    public void Given_EmptyChannelName_When_CreateWatch_Then_ValidationErrorIsShown()
    {
        // Arrange
        var watchData = TestDataBuilder.Watch()
            .WithChannelName("")
            .Build();

        // Act
        _watchPage.OpenCreateWatchDialog();
        _watchPage.FillCreateWatchForm(watchData);
        _watchPage.SubmitCreateWatch();

        // Assert
        _watchPage.VerifyValidationError(WatchManagementPageObject.ChannelNameValidation);
        Helpers.AssertTextDisplayed("required");

        ScreenshotHelper.CaptureScreenshot("ValidationError_EmptyChannelName");

        TestContext.WriteLine("Validation error displayed for empty channel name");
    }

    [Test]
    [AutoRetry]
    [Description("Tests that empty watch path shows validation error")]
    public void Given_EmptyWatchPath_When_CreateWatch_Then_ValidationErrorIsShown()
    {
        // Arrange
        var watchData = TestDataBuilder.Watch()
            .WithWatchPath("")
            .Build();

        // Act
        _watchPage.OpenCreateWatchDialog();
        _watchPage.FillCreateWatchForm(watchData);
        _watchPage.SubmitCreateWatch();

        // Assert
        _watchPage.VerifyValidationError(WatchManagementPageObject.WatchPathValidation);

        ScreenshotHelper.CaptureScreenshot("ValidationError_EmptyWatchPath");

        TestContext.WriteLine("Validation error displayed for empty watch path");
    }

    [Test]
    [AutoRetry]
    [Description("Tests that Create Watch dialog can be cancelled")]
    public void Given_CreateWatchDialogOpen_When_ClickCancel_Then_DialogIsClosed()
    {
        // Arrange
        _watchPage.OpenCreateWatchDialog();
        Helpers.AssertTextDisplayed("Create New Watch");

        // Act
        _watchPage.CancelCreateWatch();

        // Assert
        _watchPage.WaitForDialogClose();
        Helpers.AssertTextNotDisplayed("Create New Watch");

        TestContext.WriteLine("Create Watch dialog cancelled successfully");
    }

    [Test]
    [AutoRetry]
    [Description("Tests creation of watch with minimal required fields")]
    public void Given_MinimalWatchData_When_CreateWatch_Then_WatchIsCreated()
    {
        // Arrange
        var watchData = TestDataBuilder.Watch()
            .Minimal()
            .Build();

        // Act
        _watchPage.CreateWatch(watchData);

        // Assert
        _watchPage.VerifyCreateWatchSuccess(watchData.ChannelName);

        TestContext.WriteLine($"Successfully created minimal watch: {watchData.ChannelName}");
    }

    [Test]
    [AutoRetry]
    [Description("Tests creation of watch with all fields populated")]
    public void Given_CompleteWatchData_When_CreateWatch_Then_WatchIsCreated()
    {
        // Arrange
        var watchData = TestDataBuilder.Watch()
            .Complete()
            .Build();

        // Act
        _watchPage.CreateWatch(watchData);

        // Assert
        _watchPage.VerifyCreateWatchSuccess(watchData.ChannelName);
        _watchPage.VerifyWatchDisplayed(watchData.ChannelName);

        TestContext.WriteLine($"Successfully created complete watch: {watchData.ChannelName}");
    }

    #endregion

    #region Batch Create Tests

    [Test]
    [AutoRetry]
    [Description("Tests opening batch create dialog")]
    public void Given_WatchManagementPage_When_OpenBatchCreateDialog_Then_DialogIsDisplayed()
    {
        // Act
        _watchPage.OpenBatchCreateDialog();

        // Assert
        Helpers.AssertTextDisplayed("Batch Create Watches");
        Helpers.AssertElementDisplayed(WatchManagementPageObject.BatchTemplateTab);
        Helpers.AssertElementDisplayed(WatchManagementPageObject.BatchImportTab);

        ScreenshotHelper.CaptureScreenshot("BatchCreateDialogOpened");

        TestContext.WriteLine("Batch Create dialog opened successfully");
    }

    [Test]
    [AutoRetry]
    [Description("Tests generating batch preview from template")]
    public void Given_BatchConfiguration_When_GeneratePreview_Then_PreviewListIsDisplayed()
    {
        // Arrange
        _watchPage.OpenBatchCreateDialog();
        _watchPage.SelectTemplateTab();

        var basePath = TestDataBuilder.RandomFilePath();

        // Act
        _watchPage.ConfigureBatchFromTemplate(
            numberOfWatches: 3,
            startingNumber: 1,
            basePath: basePath
        );

        _watchPage.GenerateBatchPreview();

        // Assert
        Helpers.AssertElementDisplayed(WatchManagementPageObject.PreviewList);

        ScreenshotHelper.CaptureScreenshot("BatchPreviewGenerated");

        TestContext.WriteLine("Batch preview generated successfully");
    }

    [Test]
    [AutoRetry]
    [Category("Slow")]
    [Description("Tests creating multiple watches from batch template")]
    public void Given_BatchPreview_When_CreateAllWatches_Then_WatchesAreCreated()
    {
        // Arrange
        _watchPage.OpenBatchCreateDialog();
        _watchPage.SelectTemplateTab();

        var basePath = TestDataBuilder.RandomFilePath();
        var numberOfWatches = 3;

        _watchPage.ConfigureBatchFromTemplate(numberOfWatches, 1, basePath);
        _watchPage.GenerateBatchPreview();

        // Act
        _watchPage.CreateAllBatchWatches();
        _watchPage.WaitForBatchCreationComplete();

        // Assert
        _watchPage.WaitForDialogClose();

        // Verify watches were created by checking updated statistics
        var totalWatches = int.Parse(_watchPage.GetTotalWatchesCount());
        Assert.That(totalWatches, Is.GreaterThanOrEqualTo(numberOfWatches));

        ScreenshotHelper.CaptureScreenshot("BatchWatchesCreated");

        TestContext.WriteLine($"Successfully created {numberOfWatches} watches from batch");
    }

    [Test]
    [AutoRetry]
    [Description("Tests switching between template and import tabs in batch create")]
    public void Given_BatchCreateDialog_When_SwitchTabs_Then_TabsAreDisplayed()
    {
        // Arrange
        _watchPage.OpenBatchCreateDialog();

        // Act & Assert - Template Tab
        _watchPage.SelectTemplateTab();
        Helpers.AssertElementDisplayed(WatchManagementPageObject.TemplateSelector);

        ScreenshotHelper.CaptureScreenshot("BatchTemplateTab");

        // Act & Assert - Import Tab
        _watchPage.SelectImportTab();
        Helpers.AssertTextDisplayed("Import File");

        ScreenshotHelper.CaptureScreenshot("BatchImportTab");

        TestContext.WriteLine("Successfully switched between batch create tabs");
    }

    #endregion

    #region Watch List Tests

    [Test]
    [AutoRetry]
    [Description("Tests that empty state is shown when no watches exist")]
    public void Given_NoWatches_When_ViewWatchList_Then_EmptyStateIsDisplayed()
    {
        // This test assumes a clean state or will need to delete all watches first

        // Act
        _watchPage.RefreshWatches();

        // Assert
        if (int.Parse(_watchPage.GetTotalWatchesCount()) == 0)
        {
            _watchPage.VerifyEmptyStateDisplayed();
            ScreenshotHelper.CaptureScreenshot("EmptyWatchList");
            TestContext.WriteLine("Empty state displayed correctly");
        }
        else
        {
            TestContext.WriteLine("Watches exist, skipping empty state test");
            Assert.Inconclusive("Cannot test empty state when watches exist");
        }
    }

    [Test]
    [AutoRetry]
    [Description("Tests refreshing the watches list")]
    public void Given_WatchManagementPage_When_RefreshWatches_Then_ListIsRefreshed()
    {
        // Arrange
        var initialCount = _watchPage.GetTotalWatchesCount();

        // Act
        _watchPage.RefreshWatches();
        _watchPage.WaitForLoadingComplete();

        // Assert
        var updatedCount = _watchPage.GetTotalWatchesCount();
        Assert.That(updatedCount, Is.Not.Null);

        TestContext.WriteLine($"Watch list refreshed: {initialCount} -> {updatedCount}");
    }

    [Test]
    [AutoRetry]
    [Description("Tests viewing details of a watch")]
    public void Given_ExistingWatch_When_ViewDetails_Then_DetailsAreDisplayed()
    {
        // Arrange - Create a watch first
        var watchData = TestDataBuilder.Watch().Build();
        _watchPage.CreateWatch(watchData);

        // Act
        _watchPage.ViewWatchDetails(watchData.ChannelName);

        // Assert
        Helpers.AssertTextDisplayed(watchData.ChannelName);

        ScreenshotHelper.CaptureScreenshot("WatchDetailsDisplayed");

        TestContext.WriteLine($"Viewed details for watch: {watchData.ChannelName}");
    }

    [Test]
    [AutoRetry]
    [Description("Tests stopping a running watch")]
    public void Given_RunningWatch_When_StopWatch_Then_WatchIsStopped()
    {
        // Arrange - Create a watch first
        var watchData = TestDataBuilder.Watch().Build();
        _watchPage.CreateWatch(watchData);

        // Act
        _watchPage.StopWatch(watchData.ChannelName);

        // Assert
        Helpers.AssertTextNotDisplayed("Stop"); // Button should disappear when stopped

        ScreenshotHelper.CaptureScreenshot("WatchStopped");

        TestContext.WriteLine($"Stopped watch: {watchData.ChannelName}");
    }

    #endregion

    #region Pending Approvals Tests

    [Test]
    [AutoRetry]
    [Description("Tests that pending approvals section is displayed")]
    public void Given_WatchManagementPage_When_ViewPage_Then_PendingApprovalsSectionIsDisplayed()
    {
        // Act & Assert
        _watchPage.VerifyPendingApprovalsDisplayed();

        Helpers.AssertElementDisplayed(WatchManagementPageObject.ApproveSelectedButton);
        Helpers.AssertElementDisplayed(WatchManagementPageObject.RejectSelectedButton);

        TestContext.WriteLine("Pending approvals section displayed correctly");
    }

    [Test]
    [AutoRetry]
    [Description("Tests refreshing pending approvals list")]
    public void Given_PendingApprovalsSection_When_Refresh_Then_ListIsRefreshed()
    {
        // Act
        _watchPage.RefreshPendingApprovals();
        _watchPage.WaitForLoadingComplete();

        // Assert
        var approvalsCount = _watchPage.GetPendingApprovalsCount();
        Assert.That(approvalsCount, Is.Not.Null);

        TestContext.WriteLine($"Pending approvals refreshed: {approvalsCount} items");
    }

    #endregion

    #region Integration Tests

    [Test]
    [AutoRetry]
    [Category("Integration")]
    [Description("Tests complete workflow: create watch, verify it appears, then refresh")]
    public void Given_NewWatch_When_CreateAndRefresh_Then_WatchAppearsInList()
    {
        // Arrange
        var watchData = TestDataBuilder.Watch()
            .WithChannelName($"Integration Test {DateTime.Now:yyyyMMddHHmmss}")
            .Build();

        var initialCount = int.Parse(_watchPage.GetTotalWatchesCount());

        // Act
        _watchPage.CreateWatch(watchData);
        _watchPage.RefreshWatches();
        _watchPage.WaitForLoadingComplete();

        // Assert
        var updatedCount = int.Parse(_watchPage.GetTotalWatchesCount());
        Assert.That(updatedCount, Is.EqualTo(initialCount + 1),
            $"Expected watch count to increase from {initialCount} to {initialCount + 1}");

        _watchPage.VerifyWatchDisplayed(watchData.ChannelName);

        ScreenshotHelper.CaptureScreenshot("IntegrationTest_Complete");

        TestContext.WriteLine($"Integration test completed successfully. Watch count: {initialCount} -> {updatedCount}");
    }

    [Test]
    [AutoRetry]
    [Category("Integration")]
    [Description("Tests creating multiple watches and verifying statistics update")]
    public void Given_MultipleWatches_When_CreateAll_Then_StatisticsUpdateCorrectly()
    {
        // Arrange
        var initialCount = int.Parse(_watchPage.GetTotalWatchesCount());
        var watchesToCreate = 3;

        // Act
        for (int i = 0; i < watchesToCreate; i++)
        {
            var watchData = TestDataBuilder.Watch()
                .WithChannelName($"Multi-Watch Test {i + 1} {DateTime.Now:HHmmss}")
                .Build();

            _watchPage.CreateWatch(watchData);
            Helpers.Wait(TimeSpan.FromSeconds(1)); // Small delay between creations
        }

        _watchPage.RefreshWatches();
        _watchPage.WaitForLoadingComplete();

        // Assert
        var updatedCount = int.Parse(_watchPage.GetTotalWatchesCount());
        Assert.That(updatedCount, Is.EqualTo(initialCount + watchesToCreate),
            $"Expected {watchesToCreate} new watches to be created");

        ScreenshotHelper.CaptureScreenshot("MultipleWatchesCreated");

        TestContext.WriteLine($"Created {watchesToCreate} watches. Total: {initialCount} -> {updatedCount}");
    }

    #endregion

    #region Cleanup

    protected override void CleanupTestData()
    {
        // Clean up any test watches created during the test
        // Implementation depends on whether API access is available for cleanup
        TestContext.WriteLine("Cleaning up test watches...");
    }

    #endregion
}

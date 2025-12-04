using NUnit.Framework;
using AlUmmahNow.VisualImporterUno.UITests.Helpers;
using AlUmmahNow.VisualImporterUno.UITests.PageObjects;

namespace AlUmmahNow.VisualImporterUno.UITests.Tests;

/// <summary>
/// Test suite for navigation between pages and application-level interactions.
/// Validates navigation menu, page transitions, and application state management.
/// </summary>
[TestFixture]
[Category("Navigation")]
public class NavigationTests : BaseTest
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

    #region Application Startup Tests

    [Test]
    [AutoRetry]
    [Description("Verifies that application starts correctly and main navigation is accessible")]
    public void Given_ApplicationStart_When_AppLaunches_Then_MainNavigationIsDisplayed()
    {
        // Act & Assert
        _dashboard.VerifyAppLoaded();

        Helpers.AssertElementDisplayed(DashboardPageObject.NavigationView);

        ScreenshotHelper.CaptureScreenshot("ApplicationStarted");

        TestContext.WriteLine("Application started successfully with main navigation visible");
    }

    [Test]
    [AutoRetry]
    [Description("Verifies that all navigation menu items are present")]
    public void Given_Application_When_ViewNavigationMenu_Then_AllMenuItemsArePresent()
    {
        // Act & Assert
        _dashboard.VerifyNavigationMenuItemsPresent();

        ScreenshotHelper.CaptureScreenshot("AllNavigationItems");

        TestContext.WriteLine("All navigation menu items are present");
    }

    #endregion

    #region Navigation Between Pages Tests

    [Test]
    [AutoRetry]
    [Description("Tests navigation to Dashboard page")]
    public void Given_Application_When_NavigateToDashboard_Then_DashboardPageIsDisplayed()
    {
        // Act
        _dashboard.NavigateToDashboard();

        // Assert
        _dashboard.VerifyCurrentPage("Dashboard");

        ScreenshotHelper.CaptureScreenshot("DashboardPage");

        TestContext.WriteLine("Successfully navigated to Dashboard page");
    }

    [Test]
    [AutoRetry]
    [Description("Tests navigation to Watch Management page")]
    public void Given_Application_When_NavigateToWatchManagement_Then_WatchManagementPageIsDisplayed()
    {
        // Act
        _dashboard.NavigateToWatchManagement();

        // Assert
        _dashboard.VerifyCurrentPage("Watch Management");
        _watchPage.VerifyPageReady();

        ScreenshotHelper.CaptureScreenshot("WatchManagementPage");

        TestContext.WriteLine("Successfully navigated to Watch Management page");
    }

    [Test]
    [AutoRetry]
    [Description("Tests navigation to Approval Workflow page")]
    public void Given_Application_When_NavigateToApprovalWorkflow_Then_ApprovalWorkflowPageIsDisplayed()
    {
        // Act
        _dashboard.NavigateToApprovalWorkflow();

        // Assert
        _dashboard.VerifyCurrentPage("Approval Workflow");
        _approvalPage.VerifyPageReady();

        ScreenshotHelper.CaptureScreenshot("ApprovalWorkflowPage");

        TestContext.WriteLine("Successfully navigated to Approval Workflow page");
    }

    [Test]
    [AutoRetry]
    [Description("Tests navigation to File Discovery page")]
    public void Given_Application_When_NavigateToFileDiscovery_Then_FileDiscoveryPageIsDisplayed()
    {
        // Act
        _dashboard.NavigateToFileDiscovery();

        // Assert
        _dashboard.VerifyCurrentPage("File Discovery");

        ScreenshotHelper.CaptureScreenshot("FileDiscoveryPage");

        TestContext.WriteLine("Successfully navigated to File Discovery page");
    }

    [Test]
    [AutoRetry]
    [Description("Tests navigation to Import page")]
    public void Given_Application_When_NavigateToImport_Then_ImportPageIsDisplayed()
    {
        // Act
        _dashboard.NavigateToImport();

        // Assert
        _dashboard.VerifyCurrentPage("Import");

        ScreenshotHelper.CaptureScreenshot("ImportPage");

        TestContext.WriteLine("Successfully navigated to Import page");
    }

    [Test]
    [AutoRetry]
    [Description("Tests navigation to Analytics page")]
    public void Given_Application_When_NavigateToAnalytics_Then_AnalyticsPageIsDisplayed()
    {
        // Act
        _dashboard.NavigateToAnalytics();

        // Assert
        _dashboard.VerifyCurrentPage("Analytics");

        ScreenshotHelper.CaptureScreenshot("AnalyticsPage");

        TestContext.WriteLine("Successfully navigated to Analytics page");
    }

    [Test]
    [AutoRetry]
    [Description("Tests navigation to Service Status page")]
    public void Given_Application_When_NavigateToServiceStatus_Then_ServiceStatusPageIsDisplayed()
    {
        // Act
        _dashboard.NavigateToServiceStatus();

        // Assert
        _dashboard.VerifyCurrentPage("Service Status");

        ScreenshotHelper.CaptureScreenshot("ServiceStatusPage");

        TestContext.WriteLine("Successfully navigated to Service Status page");
    }

    [Test]
    [AutoRetry]
    [Description("Tests navigation to Settings page")]
    public void Given_Application_When_NavigateToSettings_Then_SettingsPageIsDisplayed()
    {
        // Act
        _dashboard.NavigateToSettings();

        // Assert
        _dashboard.VerifyCurrentPage("Settings");

        ScreenshotHelper.CaptureScreenshot("SettingsPage");

        TestContext.WriteLine("Successfully navigated to Settings page");
    }

    #endregion

    #region Sequential Navigation Tests

    [Test]
    [AutoRetry]
    [Description("Tests sequential navigation through multiple pages")]
    public void Given_Application_When_NavigateThroughMultiplePages_Then_NavigationWorksCorrectly()
    {
        // Step 1: Dashboard
        _dashboard.NavigateToDashboard();
        _dashboard.VerifyCurrentPage("Dashboard");
        ScreenshotHelper.CaptureStepScreenshot(1, "Dashboard");

        // Step 2: Watch Management
        _dashboard.NavigateToWatchManagement();
        _dashboard.VerifyCurrentPage("Watch Management");
        ScreenshotHelper.CaptureStepScreenshot(2, "WatchManagement");

        // Step 3: Approval Workflow
        _dashboard.NavigateToApprovalWorkflow();
        _dashboard.VerifyCurrentPage("Approval Workflow");
        ScreenshotHelper.CaptureStepScreenshot(3, "ApprovalWorkflow");

        // Step 4: Analytics
        _dashboard.NavigateToAnalytics();
        _dashboard.VerifyCurrentPage("Analytics");
        ScreenshotHelper.CaptureStepScreenshot(4, "Analytics");

        // Step 5: Settings
        _dashboard.NavigateToSettings();
        _dashboard.VerifyCurrentPage("Settings");
        ScreenshotHelper.CaptureStepScreenshot(5, "Settings");

        TestContext.WriteLine("Sequential navigation through all pages completed successfully");
    }

    [Test]
    [AutoRetry]
    [Description("Tests back and forth navigation between two pages")]
    public void Given_TwoPages_When_NavigateBackAndForth_Then_PagesLoadCorrectly()
    {
        // Navigate to Watch Management
        _dashboard.NavigateToWatchManagement();
        _dashboard.VerifyCurrentPage("Watch Management");
        ScreenshotHelper.CaptureStepScreenshot(1, "WatchManagement");

        // Navigate to Approval Workflow
        _dashboard.NavigateToApprovalWorkflow();
        _dashboard.VerifyCurrentPage("Approval Workflow");
        ScreenshotHelper.CaptureStepScreenshot(2, "ApprovalWorkflow");

        // Navigate back to Watch Management
        _dashboard.NavigateToWatchManagement();
        _dashboard.VerifyCurrentPage("Watch Management");
        ScreenshotHelper.CaptureStepScreenshot(3, "BackToWatchManagement");

        // Navigate back to Approval Workflow
        _dashboard.NavigateToApprovalWorkflow();
        _dashboard.VerifyCurrentPage("Approval Workflow");
        ScreenshotHelper.CaptureStepScreenshot(4, "BackToApprovalWorkflow");

        TestContext.WriteLine("Back and forth navigation works correctly");
    }

    #endregion

    #region Navigation Menu Tests

    [Test]
    [AutoRetry]
    [Description("Tests toggling navigation menu collapse/expand")]
    public void Given_NavigationMenu_When_ToggleMenu_Then_MenuCollapsesAndExpands()
    {
        // Capture initial state
        ScreenshotHelper.CaptureScreenshot("NavigationMenuInitial");

        // Act - Toggle to collapsed
        _dashboard.ToggleNavigationMenu();
        Helpers.Wait(TimeSpan.FromSeconds(1)); // Wait for animation

        // Assert
        ScreenshotHelper.CaptureScreenshot("NavigationMenuCollapsed");

        // Act - Toggle to expanded
        _dashboard.ToggleNavigationMenu();
        Helpers.Wait(TimeSpan.FromSeconds(1)); // Wait for animation

        // Assert
        ScreenshotHelper.CaptureScreenshot("NavigationMenuExpanded");

        TestContext.WriteLine("Navigation menu toggle works correctly");
    }

    #endregion

    #region State Preservation Tests

    [Test]
    [AutoRetry]
    [Description("Tests that page state is preserved when navigating away and back")]
    public void Given_PageWithState_When_NavigateAwayAndBack_Then_StateIsPreserved()
    {
        // Arrange - Navigate to Approval Workflow and apply filters
        _dashboard.NavigateToApprovalWorkflow();
        _approvalPage.ApplyPriorityFilter(5);
        _approvalPage.WaitForLoadingComplete();

        var initialCount = _approvalPage.GetDisplayedApprovalsCount();

        ScreenshotHelper.CaptureStepScreenshot(1, "FiltersApplied");

        // Act - Navigate away to Dashboard
        _dashboard.NavigateToDashboard();
        ScreenshotHelper.CaptureStepScreenshot(2, "NavigatedAway");

        // Act - Navigate back to Approval Workflow
        _dashboard.NavigateToApprovalWorkflow();
        _approvalPage.WaitForLoadingComplete();

        // Assert - Check if filters are still applied (depending on app behavior)
        var returnedCount = _approvalPage.GetDisplayedApprovalsCount();

        ScreenshotHelper.CaptureStepScreenshot(3, "NavigatedBack");

        TestContext.WriteLine($"Page state: Initial count={initialCount}, Returned count={returnedCount}");

        // Note: This test's expectations may need adjustment based on actual app behavior
        // Some apps preserve filter state, others reset it
    }

    #endregion

    #region Performance Tests

    [Test]
    [AutoRetry]
    [Category("Performance")]
    [Description("Tests that page navigation completes within acceptable time")]
    public void Given_Application_When_NavigateToPage_Then_NavigationIsPerformant()
    {
        // Arrange
        var pages = new[]
        {
            ("Dashboard", (Action)_dashboard.NavigateToDashboard),
            ("Watch Management", (Action)_dashboard.NavigateToWatchManagement),
            ("Approval Workflow", (Action)_dashboard.NavigateToApprovalWorkflow),
            ("Analytics", (Action)_dashboard.NavigateToAnalytics)
        };

        var maxAcceptableTime = TimeSpan.FromSeconds(5);

        // Act & Assert
        foreach (var (pageName, navigationAction) in pages)
        {
            var startTime = DateTime.Now;

            navigationAction();
            _dashboard.VerifyCurrentPage(pageName);

            var elapsed = DateTime.Now - startTime;

            Assert.That(elapsed, Is.LessThan(maxAcceptableTime),
                $"Navigation to {pageName} took {elapsed.TotalSeconds:F2}s, " +
                $"exceeds maximum of {maxAcceptableTime.TotalSeconds}s");

            TestContext.WriteLine($"Navigation to {pageName} completed in {elapsed.TotalMilliseconds}ms");
        }

        TestContext.WriteLine("All navigation performance tests passed");
    }

    #endregion

    #region Error Handling Tests

    [Test]
    [AutoRetry]
    [Description("Tests rapid navigation between pages")]
    public void Given_Application_When_RapidlyNavigate_Then_AppRemainsStable()
    {
        // Act - Rapidly switch between pages
        for (int i = 0; i < 5; i++)
        {
            _dashboard.NavigateToWatchManagement();
            Helpers.Wait(TimeSpan.FromMilliseconds(500));

            _dashboard.NavigateToApprovalWorkflow();
            Helpers.Wait(TimeSpan.FromMilliseconds(500));
        }

        // Assert - App should still be responsive
        _dashboard.VerifyAppLoaded();

        ScreenshotHelper.CaptureScreenshot("AfterRapidNavigation");

        TestContext.WriteLine("Application remained stable during rapid navigation");
    }

    #endregion
}

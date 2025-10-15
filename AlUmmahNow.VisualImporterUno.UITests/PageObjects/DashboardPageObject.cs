using Uno.UITest;
using AlUmmahNow.VisualImporterUno.UITests.Helpers;

namespace AlUmmahNow.VisualImporterUno.UITests.PageObjects;

/// <summary>
/// Page Object Model for Dashboard/Main Navigation.
/// Encapsulates navigation between pages and common dashboard interactions.
/// </summary>
public class DashboardPageObject
{
    private readonly IApp _app;
    private readonly UITestHelpers _helpers;

    #region Element Identifiers

    // Navigation Menu Items
    public const string DashboardMenuItem = "DashboardMenuItem";
    public const string WatchManagementMenuItem = "WatchManagementMenuItem";
    public const string ApprovalWorkflowMenuItem = "ApprovalWorkflowMenuItem";
    public const string FileDiscoveryMenuItem = "FileDiscoveryMenuItem";
    public const string ImportMenuItem = "ImportMenuItem";
    public const string AnalyticsMenuItem = "AnalyticsMenuItem";
    public const string ServiceStatusMenuItem = "ServiceStatusMenuItem";
    public const string SettingsMenuItem = "SettingsMenuItem";

    // App Bar / Header
    public const string AppTitle = "AppTitle";
    public const string UserMenu = "UserMenu";
    public const string NotificationBell = "NotificationBell";
    public const string SearchBox = "SearchBox";

    // Navigation Panel
    public const string NavigationView = "NavigationView";
    public const string MenuToggleButton = "MenuToggleButton";

    #endregion

    public DashboardPageObject(IApp app, UITestHelpers helpers)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
    }

    #region Navigation

    /// <summary>
    /// Navigates to Dashboard page
    /// </summary>
    public void NavigateToDashboard()
    {
        _helpers.TapElement(DashboardMenuItem);
        _helpers.WaitForText("Dashboard");
    }

    /// <summary>
    /// Navigates to Watch Management page
    /// </summary>
    public void NavigateToWatchManagement()
    {
        _helpers.TapElement(WatchManagementMenuItem);
        _helpers.WaitForText("Watch Management");
    }

    /// <summary>
    /// Navigates to Approval Workflow page
    /// </summary>
    public void NavigateToApprovalWorkflow()
    {
        _helpers.TapElement(ApprovalWorkflowMenuItem);
        _helpers.WaitForText("Approval Workflow");
    }

    /// <summary>
    /// Navigates to File Discovery page
    /// </summary>
    public void NavigateToFileDiscovery()
    {
        _helpers.TapElement(FileDiscoveryMenuItem);
        _helpers.WaitForText("File Discovery");
    }

    /// <summary>
    /// Navigates to Import page
    /// </summary>
    public void NavigateToImport()
    {
        _helpers.TapElement(ImportMenuItem);
        _helpers.WaitForText("Import");
    }

    /// <summary>
    /// Navigates to Analytics page
    /// </summary>
    public void NavigateToAnalytics()
    {
        _helpers.TapElement(AnalyticsMenuItem);
        _helpers.WaitForText("Analytics");
    }

    /// <summary>
    /// Navigates to Service Status page
    /// </summary>
    public void NavigateToServiceStatus()
    {
        _helpers.TapElement(ServiceStatusMenuItem);
        _helpers.WaitForText("Service Status");
    }

    /// <summary>
    /// Navigates to Settings page
    /// </summary>
    public void NavigateToSettings()
    {
        _helpers.TapElement(SettingsMenuItem);
        _helpers.WaitForText("Settings");
    }

    /// <summary>
    /// Toggles navigation menu (collapse/expand)
    /// </summary>
    public void ToggleNavigationMenu()
    {
        _helpers.TapElement(MenuToggleButton);
    }

    #endregion

    #region Verification

    /// <summary>
    /// Verifies the app is loaded and main navigation is visible
    /// </summary>
    public void VerifyAppLoaded()
    {
        _helpers.AssertElementDisplayed(NavigationView);
        _helpers.AssertTextDisplayed("Al Ummah Now Visual Importer");
    }

    /// <summary>
    /// Verifies user is on a specific page by checking page title
    /// </summary>
    public void VerifyCurrentPage(string expectedPageTitle)
    {
        _helpers.AssertTextDisplayed(expectedPageTitle);
    }

    /// <summary>
    /// Verifies all navigation menu items are present
    /// </summary>
    public void VerifyNavigationMenuItemsPresent()
    {
        var menuItems = new[]
        {
            "Dashboard",
            "Watch Management",
            "Approval Workflow",
            "File Discovery",
            "Import",
            "Analytics",
            "Service Status",
            "Settings"
        };

        foreach (var item in menuItems)
        {
            _helpers.AssertTextDisplayed(item);
        }
    }

    #endregion

    #region Header Actions

    /// <summary>
    /// Opens user menu
    /// </summary>
    public void OpenUserMenu()
    {
        _helpers.TapElement(UserMenu);
    }

    /// <summary>
    /// Opens notifications panel
    /// </summary>
    public void OpenNotifications()
    {
        _helpers.TapElement(NotificationBell);
    }

    /// <summary>
    /// Performs search
    /// </summary>
    public void Search(string searchTerm)
    {
        _helpers.EnterText(SearchBox, searchTerm);
        _helpers.DismissKeyboard();
    }

    #endregion
}

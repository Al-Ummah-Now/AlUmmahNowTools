using NUnit.Framework;
using Uno.UITest;
using Uno.UITest.Helpers.Queries;
using AlUmmahNow.VisualImporterUno.UITests.Helpers;

namespace AlUmmahNow.VisualImporterUno.UITests;

/// <summary>
/// Base test class providing common setup, teardown, and utility methods for all UI tests.
/// Implements proper lifecycle management, screenshot capture on failure, and platform-specific configuration.
/// </summary>
[TestFixture]
public abstract class BaseTest
{
    protected IApp? App { get; private set; }
    protected Platform Platform { get; private set; }
    protected ScreenshotHelper ScreenshotHelper { get; private set; } = null!;
    protected UITestHelpers Helpers { get; private set; } = null!;

    /// <summary>
    /// Specifies which platforms this test should run on. Override in derived classes.
    /// </summary>
    protected virtual Platform[] ActivePlatforms => new[] { Platform.Browser, Platform.Windows };

    /// <summary>
    /// Specifies custom app configuration. Override to provide platform-specific settings.
    /// </summary>
    protected virtual AppConfig GetAppConfig()
    {
        return new AppConfig
        {
            WindowsAppPath = GetWindowsAppPath(),
            BrowserDefaultUri = "http://localhost:5000",
            ImplicitWaitTimeout = TimeSpan.FromSeconds(30),
            DefaultRetryFrequency = TimeSpan.FromMilliseconds(500)
        };
    }

    /// <summary>
    /// Gets the Windows application path. Override if needed.
    /// </summary>
    protected virtual string GetWindowsAppPath()
    {
        // Update this path based on your build output
        var projectDir = TestContext.CurrentContext.TestDirectory;
        return Path.Combine(projectDir, "..", "..", "..", "..",
            "AlUmmahNow.VisualImporterUno", "AlUmmahNow.VisualImporterUno",
            "bin", "Debug", "net9.0-windows10.0.26100",
            "AlUmmahNow.VisualImporterUno.exe");
    }

    [SetUp]
    public virtual void SetUp()
    {
        // Determine platform from environment or configuration
        Platform = DeterminePlatform();

        // Check if test should run on this platform
        if (!ActivePlatforms.Contains(Platform))
        {
            Assert.Ignore($"Test is not configured to run on {Platform}");
        }

        // Initialize app with configuration
        var config = GetAppConfig();
        App = ConfigureApp(Platform, config);

        // Initialize helpers
        ScreenshotHelper = new ScreenshotHelper(App, TestContext.CurrentContext);
        Helpers = new UITestHelpers(App);

        // Wait for app to be ready
        WaitForAppReady();

        TestContext.WriteLine($"Test started: {TestContext.CurrentContext.Test.Name}");
        TestContext.WriteLine($"Platform: {Platform}");
    }

    [TearDown]
    public virtual void TearDown()
    {
        var testName = TestContext.CurrentContext.Test.Name;
        var testResult = TestContext.CurrentContext.Result.Outcome.Status;

        TestContext.WriteLine($"Test completed: {testName}");
        TestContext.WriteLine($"Result: {testResult}");

        // Capture screenshot on failure
        if (testResult == NUnit.Framework.Interfaces.TestStatus.Failed)
        {
            try
            {
                ScreenshotHelper.CaptureScreenshot($"{testName}_Failure");
                TestContext.WriteLine("Screenshot captured for failed test");
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"Failed to capture screenshot: {ex.Message}");
            }
        }

        // Cleanup
        try
        {
            CleanupTestData();
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Cleanup failed: {ex.Message}");
        }

        // Close app
        App?.Dispose();
        App = null;
    }

    /// <summary>
    /// Configures and launches the app based on platform
    /// </summary>
    protected virtual IApp ConfigureApp(Platform platform, AppConfig config)
    {
        return platform switch
        {
            Platform.Browser => AppInitializer.ConfigureBrowserApp(config.BrowserDefaultUri),
            Platform.Windows => AppInitializer.ConfigureWindowsApp(config.WindowsAppPath),
            Platform.Android => AppInitializer.ConfigureAndroidApp(),
            Platform.iOS => AppInitializer.ConfigureiOSApp(),
            _ => throw new NotSupportedException($"Platform {platform} is not supported")
        };
    }

    /// <summary>
    /// Determines which platform to run tests on based on environment variables or configuration
    /// </summary>
    protected virtual Platform DeterminePlatform()
    {
        var platformEnv = Environment.GetEnvironmentVariable("UNO_UITEST_PLATFORM");

        if (!string.IsNullOrEmpty(platformEnv))
        {
            if (Enum.TryParse<Platform>(platformEnv, ignoreCase: true, out var platform))
            {
                return platform;
            }
        }

        // Default to Windows on Windows, Browser otherwise
        return OperatingSystem.IsWindows() ? Platform.Windows : Platform.Browser;
    }

    /// <summary>
    /// Waits for the application to be fully loaded and ready
    /// </summary>
    protected virtual void WaitForAppReady()
    {
        // Wait for main window or root element
        Helpers.WaitForElement("MainWindow", timeout: TimeSpan.FromSeconds(30));

        // Additional stabilization wait
        Thread.Sleep(1000);
    }

    /// <summary>
    /// Cleanup test data created during test execution
    /// Override in derived classes to implement specific cleanup logic
    /// </summary>
    protected virtual void CleanupTestData()
    {
        // To be implemented by derived classes
    }

    /// <summary>
    /// Helper method to wait for element with custom timeout
    /// </summary>
    protected void WaitForElement(string automationId, TimeSpan? timeout = null)
    {
        Helpers.WaitForElement(automationId, timeout);
    }

    /// <summary>
    /// Helper method to tap on an element
    /// </summary>
    protected void TapElement(string automationId)
    {
        Helpers.TapElement(automationId);
    }

    /// <summary>
    /// Helper method to enter text in a text box
    /// </summary>
    protected void EnterText(string automationId, string text)
    {
        Helpers.EnterText(automationId, text);
    }

    /// <summary>
    /// Helper method to verify text is displayed
    /// </summary>
    protected void AssertTextDisplayed(string text)
    {
        Helpers.AssertTextDisplayed(text);
    }
}

/// <summary>
/// Platform enumeration for test execution
/// </summary>
public enum Platform
{
    Browser,
    Windows,
    Android,
    iOS,
    Linux
}

/// <summary>
/// Application configuration for tests
/// </summary>
public class AppConfig
{
    public string WindowsAppPath { get; set; } = string.Empty;
    public string BrowserDefaultUri { get; set; } = string.Empty;
    public TimeSpan ImplicitWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DefaultRetryFrequency { get; set; } = TimeSpan.FromMilliseconds(500);
}

/// <summary>
/// App initializer for different platforms
/// Note: This is a stub implementation. The Uno.UITest API needs to be configured
/// based on the specific version and platform requirements.
/// </summary>
public static class AppInitializer
{
    public static IApp? ConfigureBrowserApp(string defaultUri)
    {
        // TODO: Configure browser app using Uno.UITest when ready
        // The Uno.UITest API varies by version
        throw new NotImplementedException("Browser app configuration not yet implemented. See Uno.UITest documentation for your version.");
    }

    public static IApp? ConfigureWindowsApp(string appPath)
    {
        // TODO: Configure Windows app using Uno.UITest when ready
        throw new NotImplementedException("Windows app configuration not yet implemented. See Uno.UITest documentation for your version.");
    }

    public static IApp? ConfigureAndroidApp()
    {
        // TODO: Configure Android app using Uno.UITest when ready
        throw new NotImplementedException("Android app configuration not yet implemented. See Uno.UITest documentation for your version.");
    }

    public static IApp? ConfigureiOSApp()
    {
        // TODO: Configure iOS app using Uno.UITest when ready
        throw new NotImplementedException("iOS app configuration not yet implemented. See Uno.UITest documentation for your version.");
    }
}

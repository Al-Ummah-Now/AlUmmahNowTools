using NUnit.Framework;
using Uno.UITest;

namespace AlUmmahNow.VisualImporterUno.UITests.Helpers;

/// <summary>
/// Helper class for capturing and managing screenshots during test execution.
/// Provides functionality for organized screenshot storage with timestamps and test context.
/// </summary>
public class ScreenshotHelper
{
    private readonly IApp _app;
    private readonly TestContext _testContext;
    private readonly string _screenshotDirectory;

    public ScreenshotHelper(IApp app, TestContext testContext)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _testContext = testContext ?? throw new ArgumentNullException(nameof(testContext));

        // Create screenshot directory with test run timestamp
        var baseDirectory = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "Screenshots",
            DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
        );

        _screenshotDirectory = baseDirectory;

        // Ensure directory exists
        Directory.CreateDirectory(_screenshotDirectory);
    }

    /// <summary>
    /// Captures a screenshot with a descriptive name
    /// </summary>
    /// <param name="description">Description to append to the screenshot name</param>
    /// <returns>Path to the saved screenshot</returns>
    public string CaptureScreenshot(string description)
    {
        try
        {
            // Sanitize description for filename
            var safeDescription = SanitizeFileName(description);

            // Create filename with timestamp
            var timestamp = DateTime.Now.ToString("HHmmss_fff");
            var fileName = $"{_testContext.Test.Name}_{safeDescription}_{timestamp}";

            // Capture screenshot
            var screenshot = _app.Screenshot(fileName);

            // Copy to our organized directory structure
            var testClassName = _testContext.Test.ClassName?.Split('.').Last() ?? "Unknown";
            var testClassDirectory = Path.Combine(_screenshotDirectory, testClassName);
            Directory.CreateDirectory(testClassDirectory);

            var destinationPath = Path.Combine(testClassDirectory, Path.GetFileName(screenshot.FullName));
            File.Copy(screenshot.FullName, destinationPath, overwrite: true);

            TestContext.WriteLine($"Screenshot saved: {destinationPath}");

            // Attach to test result if supported
            AttachToTestResult(destinationPath);

            return destinationPath;
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Failed to capture screenshot: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Captures a screenshot with step number for sequential test steps
    /// </summary>
    public string CaptureStepScreenshot(int stepNumber, string stepDescription)
    {
        var description = $"Step{stepNumber:D2}_{stepDescription}";
        return CaptureScreenshot(description);
    }

    /// <summary>
    /// Captures screenshots for a before/after comparison
    /// </summary>
    public (string beforePath, string afterPath) CaptureBeforeAfterScreenshots(string operationName)
    {
        var beforePath = CaptureScreenshot($"{operationName}_Before");
        return (beforePath, string.Empty); // After path to be captured later
    }

    /// <summary>
    /// Captures the "after" screenshot for a before/after comparison
    /// </summary>
    public string CaptureAfterScreenshot(string operationName)
    {
        return CaptureScreenshot($"{operationName}_After");
    }

    /// <summary>
    /// Captures multiple screenshots with a delay between each
    /// Useful for capturing animation sequences or async operations
    /// </summary>
    public List<string> CaptureSequence(string sequenceName, int count, TimeSpan delay)
    {
        var screenshots = new List<string>();

        for (int i = 0; i < count; i++)
        {
            screenshots.Add(CaptureScreenshot($"{sequenceName}_Frame{i:D3}"));

            if (i < count - 1) // Don't delay after the last screenshot
            {
                Thread.Sleep(delay);
            }
        }

        TestContext.WriteLine($"Captured {count} screenshots for sequence: {sequenceName}");

        return screenshots;
    }

    /// <summary>
    /// Captures a screenshot only if a condition is met
    /// </summary>
    public string? CaptureConditional(string description, Func<bool> condition)
    {
        if (condition())
        {
            return CaptureScreenshot(description);
        }

        TestContext.WriteLine($"Screenshot skipped (condition not met): {description}");
        return null;
    }

    /// <summary>
    /// Captures a screenshot and returns a FileInfo object
    /// </summary>
    public FileInfo CaptureScreenshotAsFileInfo(string description)
    {
        var path = CaptureScreenshot(description);
        return new FileInfo(path);
    }

    /// <summary>
    /// Gets the directory where screenshots for this test run are stored
    /// </summary>
    public string GetScreenshotDirectory()
    {
        return _screenshotDirectory;
    }

    /// <summary>
    /// Cleans up old screenshot directories (keeps last N test runs)
    /// </summary>
    public static void CleanupOldScreenshots(int keepLastNRuns = 10)
    {
        try
        {
            var screenshotBaseDir = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Screenshots"
            );

            if (!Directory.Exists(screenshotBaseDir))
            {
                return;
            }

            var directories = Directory.GetDirectories(screenshotBaseDir)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.CreationTime)
                .ToList();

            // Keep only the last N runs
            foreach (var dir in directories.Skip(keepLastNRuns))
            {
                try
                {
                    dir.Delete(recursive: true);
                    TestContext.WriteLine($"Deleted old screenshot directory: {dir.Name}");
                }
                catch (Exception ex)
                {
                    TestContext.WriteLine($"Failed to delete directory {dir.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Failed to cleanup old screenshots: {ex.Message}");
        }
    }

    /// <summary>
    /// Sanitizes a string to be safe for use as a filename
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Also replace spaces and other problematic characters
        sanitized = sanitized.Replace(" ", "_")
                             .Replace(":", "_")
                             .Replace("?", "_")
                             .Replace("!", "_");

        // Limit length
        const int maxLength = 100;
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized.Substring(0, maxLength);
        }

        return sanitized;
    }

    /// <summary>
    /// Attaches screenshot to test result for better reporting
    /// </summary>
    private void AttachToTestResult(string screenshotPath)
    {
        try
        {
            // Add to test context attachments
            TestContext.AddTestAttachment(screenshotPath, "Screenshot");
        }
        catch (Exception ex)
        {
            // Not all test runners support attachments
            TestContext.WriteLine($"Could not attach screenshot to test result: {ex.Message}");
        }
    }
}

# XAML-Based UI Test Automation Guide
## Comprehensive Testing Strategy for Uno Platform, WPF, and WinUI 3

**Project:** AlUmmahNow.VisualImporterUno
**Date:** 2025-10-12
**Target Frameworks:** Uno Platform (net9.0-windows10.0.26100)

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [UI Testing Frameworks](#ui-testing-frameworks)
3. [UI Testing Best Practices](#ui-testing-best-practices)
4. [MVVM Testing Strategy](#mvvm-testing-strategy)
5. [Uno Platform Specific](#uno-platform-specific)
6. [Common Challenges & Solutions](#common-challenges--solutions)
7. [Codebase Analysis](#codebase-analysis)
8. [Implementation Recommendations](#implementation-recommendations)
9. [Example Test Code](#example-test-code)
10. [Setup Instructions](#setup-instructions)

---

## Executive Summary

The AlUmmahNow.VisualImporterUno application is a sophisticated Uno Platform application for video file scanning and import management. The codebase demonstrates excellent MVVM architecture with CommunityToolkit.Mvvm, comprehensive error handling, and complex UI interactions including:

- File system operations (folder selection, scanning)
- Real-time progress tracking
- SignalR-based service communication
- Complex data binding scenarios
- Async UI operations with proper thread marshaling

**Key Findings:**
- **ViewModels Identified:** MainViewModel, SettingsViewModel, ServiceStatusViewModel, DatabaseMainViewModel
- **Testing Infrastructure:** Existing xUnit test suite with Moq, FluentAssertions, EF Core InMemory
- **Current Coverage:** Service layer well-tested, ViewModels not yet covered
- **Primary Challenge:** Testing Windows-specific UI components in cross-platform scenarios

---

## UI Testing Frameworks

### 1. Appium for Uno Platform Cross-Platform Apps

**Best For:** Testing deployed Uno Platform apps across multiple platforms (iOS, Android, WASM, macOS)

#### Pros:
- Cross-platform support for all Uno targets
- Industry-standard WebDriver protocol
- Large ecosystem and community support
- Can test actual deployed apps on real devices/browsers

#### Cons:
- Requires separate Appium server infrastructure
- More complex setup for each platform
- Slower than unit/integration tests
- Best suited for E2E testing, not rapid TDD cycles

#### When to Use:
- Final validation before release
- Testing platform-specific behavior
- Visual regression testing across platforms
- Real device/browser compatibility testing

#### Setup Overview:
```bash
# Install Appium
npm install -g appium

# Install platform-specific drivers
appium driver install windows    # For Windows
appium driver install xcuitest   # For iOS
appium driver install uiautomator2  # For Android

# Start Appium server
appium
```

#### Example Configuration (C#):
```csharp
var options = new AppiumOptions();
options.AddAdditionalCapability("platformName", "Windows");
options.AddAdditionalCapability("app", "AlUmmahNow.VisualImporterUno.exe");
options.AddAdditionalCapability("deviceName", "WindowsPC");

var driver = new WindowsDriver<WindowsElement>(
    new Uri("http://127.0.0.1:4723"), options);
```

---

### 2. WinAppDriver for WinUI/WPF Windows Apps

**Best For:** Automated UI testing of Windows desktop apps (WinUI 3, UWP, WPF)

#### Pros:
- Native Windows support
- Microsoft-maintained
- Works with Windows UI Automation API
- Good Visual Studio integration
- Supports element inspection tools

#### Cons:
- Windows-only (not cross-platform)
- Requires Windows 10+ with Developer Mode
- Limited to Windows desktop scenarios

#### When to Use:
- **RECOMMENDED for AlUmmahNow.VisualImporterUno** (currently Windows-only target)
- Testing Windows-specific features (file dialogs, native controls)
- CI/CD pipelines on Windows agents
- Integration testing with Windows services

#### Installation:
```bash
# Download WinAppDriver from GitHub
# https://github.com/Microsoft/WinAppDriver/releases
# Install WinAppDriver.msi

# Enable Developer Mode in Windows Settings
# Settings > Update & Security > For developers > Developer Mode
```

#### Example Test:
```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium.Appium.Windows;
using OpenQA.Selenium.Remote;

[TestClass]
public class MainWindowTests
{
    private WindowsDriver<WindowsElement> _driver;

    [TestInitialize]
    public void Setup()
    {
        var options = new AppiumOptions();
        options.AddAdditionalCapability("app",
            @"C:\Path\To\AlUmmahNow.VisualImporterUno.exe");
        options.AddAdditionalCapability("deviceName", "WindowsPC");

        _driver = new WindowsDriver<WindowsElement>(
            new Uri("http://127.0.0.1:4723"), options);
        _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
    }

    [TestCleanup]
    public void TearDown()
    {
        _driver?.Quit();
    }
}
```

---

### 3. Uno.UITest Framework Specifics

**Best For:** Uno Platform-specific UI testing across all platforms

#### Overview:
Uno.UITest is built on top of Xamarin.UITest and provides a unified API for testing Uno apps across platforms.

#### Pros:
- Designed specifically for Uno Platform
- Single API for multiple platforms
- Good integration with Uno's accessibility features
- Can test WASM, Mobile, and Desktop

#### Cons:
- Less mature than Appium/WinAppDriver
- Documentation still evolving
- Requires platform-specific test runners

#### Current Status for Project:
The current project doesn't have Uno.UITest configured. Given the Windows-only target, **WinAppDriver is recommended** for immediate needs. Uno.UITest should be considered when expanding to other platforms.

---

### 4. Selenium WebDriver for Uno WASM Apps

**Best For:** Testing Uno Platform WebAssembly deployments

#### Pros:
- Industry-standard web testing
- Extensive ecosystem
- CI/CD integration
- Cross-browser testing

#### Cons:
- WASM-specific (not applicable to desktop/mobile)
- Requires hosted WASM app
- May not detect XAML-specific issues

#### When to Use:
- Testing WASM-specific functionality
- Cross-browser compatibility
- Performance testing in browsers
- Accessibility testing with browser tools

#### Example:
```csharp
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

var driver = new ChromeDriver();
driver.Navigate().GoToUrl("https://localhost:5001"); // WASM app URL

// Find element by AutomationProperties.Name
var selectFolderButton = driver.FindElement(
    By.XPath("//button[@aria-label='Select Folder']"));
selectFolderButton.Click();
```

---

## Framework Recommendation for AlUmmahNow.VisualImporterUno

### Primary Recommendation: **Multi-Layered Testing Approach**

```
┌─────────────────────────────────────────────────────────────┐
│                        Test Pyramid                          │
├─────────────────────────────────────────────────────────────┤
│  E2E Tests (WinAppDriver)                                   │
│  - Critical user journeys                              [5%]  │
│  - Complete folder scan & import workflow                    │
├─────────────────────────────────────────────────────────────┤
│  Integration Tests                                           │
│  - ViewModel + Service integration                    [15%]  │
│  - Database operations                                       │
│  - SignalR connectivity                                      │
├─────────────────────────────────────────────────────────────┤
│  Unit Tests                                                  │
│  - ViewModel logic                                    [80%]  │
│  - Commands, properties, validation                          │
│  - Data transformations                                      │
└─────────────────────────────────────────────────────────────┘
```

**Rationale:**
1. **Unit Tests (80%):** Fast, reliable, cover ViewModels and business logic
2. **Integration Tests (15%):** Test ViewModel interactions with services
3. **E2E Tests (5%):** Critical paths only, using WinAppDriver

---

## UI Testing Best Practices

### 1. Page Object Model (POM) Pattern for XAML Apps

The Page Object Model encapsulates page/view structure and provides a clean API for tests.

#### Benefits:
- Reduced code duplication
- Easier maintenance when UI changes
- Readable test code
- Clear separation of test logic and UI structure

#### Implementation:

```csharp
// Page Object for Main View
public class MainViewPage
{
    private readonly WindowsDriver<WindowsElement> _driver;

    public MainViewPage(WindowsDriver<WindowsElement> driver)
    {
        _driver = driver;
    }

    // Elements (using AutomationProperties.AutomationId or Name)
    private WindowsElement SelectFolderButton =>
        _driver.FindElementByAccessibilityId("SelectFolderButton");

    private WindowsElement ScanFolderButton =>
        _driver.FindElementByAccessibilityId("ScanFolderButton");

    private WindowsElement FolderPathTextBox =>
        _driver.FindElementByAccessibilityId("FolderPathTextBox");

    private WindowsElement StatusMessageText =>
        _driver.FindElementByAccessibilityId("StatusMessageText");

    private WindowsElement ProgressBar =>
        _driver.FindElementByAccessibilityId("ScanProgressBar");

    // Actions
    public void ClickSelectFolder()
    {
        SelectFolderButton.Click();
    }

    public void ClickScanFolder()
    {
        ScanFolderButton.Click();
    }

    public string GetSelectedFolderPath()
    {
        return FolderPathTextBox.Text;
    }

    public string GetStatusMessage()
    {
        return StatusMessageText.Text;
    }

    public double GetProgressPercentage()
    {
        var value = ProgressBar.GetAttribute("Value.Value");
        return double.Parse(value);
    }

    // High-level workflows
    public void SelectAndScanFolder(string folderPath)
    {
        ClickSelectFolder();
        // Handle file dialog (OS-level)
        // This requires additional native automation
        WaitForScanToComplete();
    }

    public void WaitForScanToComplete(int timeoutSeconds = 30)
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));
        wait.Until(driver =>
        {
            var status = GetStatusMessage();
            return status.Contains("completed") || status.Contains("files");
        });
    }
}
```

#### Using Page Objects in Tests:

```csharp
[TestClass]
public class FolderScanTests
{
    private WindowsDriver<WindowsElement> _driver;
    private MainViewPage _mainPage;

    [TestInitialize]
    public void Setup()
    {
        // Initialize driver
        _driver = new WindowsDriver<WindowsElement>(
            new Uri("http://127.0.0.1:4723"),
            CreateAppiumOptions());

        _mainPage = new MainViewPage(_driver);
    }

    [TestMethod]
    public void ScanFolder_WithValidPath_DisplaysResults()
    {
        // Arrange
        var testFolder = @"C:\TestVideos";

        // Act
        _mainPage.SelectAndScanFolder(testFolder);

        // Assert
        var status = _mainPage.GetStatusMessage();
        Assert.IsTrue(status.Contains("Found") && status.Contains("files"));
    }

    [TestCleanup]
    public void TearDown()
    {
        _driver?.Quit();
    }
}
```

---

### 2. Handling Async UI Operations

XAML apps frequently use async operations. Proper handling is critical for reliable tests.

#### Strategies:

##### A. Explicit Waits (Preferred)
```csharp
public void WaitForElement(By locator, int timeoutSeconds = 10)
{
    var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));
    wait.Until(driver =>
    {
        try
        {
            var element = driver.FindElement(locator);
            return element.Displayed && element.Enabled;
        }
        catch (NoSuchElementException)
        {
            return false;
        }
    });
}
```

##### B. Wait for Property Changes
```csharp
public void WaitForPropertyValue(
    WindowsElement element,
    string propertyName,
    string expectedValue,
    int timeoutSeconds = 10)
{
    var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));
    wait.Until(driver =>
    {
        var actualValue = element.GetAttribute(propertyName);
        return actualValue == expectedValue;
    });
}

// Usage
WaitForPropertyValue(progressBar, "Value.Value", "100", 30);
```

##### C. Custom Wait Conditions
```csharp
public static class CustomConditions
{
    public static Func<IWebDriver, bool> ProgressBarComplete(WindowsElement progressBar)
    {
        return (driver) =>
        {
            var value = double.Parse(progressBar.GetAttribute("Value.Value"));
            return value >= 100;
        };
    }

    public static Func<IWebDriver, bool> TextContains(
        WindowsElement element,
        string expectedText)
    {
        return (driver) =>
        {
            var actualText = element.Text;
            return actualText.Contains(expectedText,
                StringComparison.OrdinalIgnoreCase);
        };
    }
}

// Usage
var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));
wait.Until(CustomConditions.ProgressBarComplete(progressBar));
```

---

### 3. Reliable Element Location Strategies

#### Priority Order (Best to Worst):

1. **AutomationProperties.AutomationId** (Best - Stable, designed for automation)
2. **AutomationProperties.Name** (Good - Descriptive, may change with localization)
3. **x:Name** (Okay - May not be exposed to automation)
4. **Class name + property values** (Fragile - Breaks with UI changes)
5. **XPath** (Last resort - Very fragile)

#### XAML Markup Guidelines:

```xml
<!-- BEST: Use AutomationProperties.AutomationId -->
<Button x:Name="SelectFolderButton"
        AutomationProperties.AutomationId="SelectFolderButton"
        AutomationProperties.Name="Select Folder"
        Command="{Binding SelectFolderCommand}"
        Content="Select Folder" />

<!-- GOOD: AutomationProperties.Name for localization -->
<TextBlock x:Name="StatusMessage"
           AutomationProperties.AutomationId="StatusMessageText"
           AutomationProperties.Name="Status Message"
           Text="{Binding StatusMessage}" />

<!-- For Lists/DataTemplates -->
<ListView AutomationProperties.AutomationId="VideoFilesList"
          AutomationProperties.Name="Video Files List"
          ItemsSource="{Binding FilteredVideoFiles}">
    <ListView.ItemTemplate>
        <DataTemplate>
            <Grid AutomationProperties.AutomationId="{Binding FilePath}"
                  AutomationProperties.Name="{Binding FileName}">
                <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}"
                          AutomationProperties.AutomationId="FileSelectionCheckbox"
                          AutomationProperties.Name="{Binding FileName, StringFormat='Select {0}'}" />
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

#### C# Locator Strategies:

```csharp
// Best: By AutomationId
var element = driver.FindElementByAccessibilityId("SelectFolderButton");

// Good: By AutomationProperties.Name
var element = driver.FindElementByName("Select Folder");

// Okay: By ClassName and properties
var elements = driver.FindElementsByClassName("Button")
    .FirstOrDefault(e => e.Text == "Select Folder");

// Avoid: Complex XPath (fragile)
var element = driver.FindElement(
    By.XPath("//Button[@Name='Select Folder' and @IsEnabled='True']"));
```

---

### 4. Testing Responsive Layouts Across Form Factors

#### Window Resizing Tests:

```csharp
[TestMethod]
[DataRow(1920, 1080, "Desktop")]
[DataRow(1366, 768, "Laptop")]
[DataRow(800, 600, "Tablet")]
public void Layout_RespondsToWindowSize(int width, int height, string formFactor)
{
    // Arrange
    _driver.Manage().Window.Size = new Size(width, height);

    // Act
    var navigationPanel = _driver.FindElementByAccessibilityId("NavigationPanel");
    var mainContent = _driver.FindElementByAccessibilityId("MainContent");

    // Assert
    Assert.IsTrue(navigationPanel.Displayed,
        $"Navigation should be visible at {formFactor} size");
    Assert.IsTrue(mainContent.Displayed,
        $"Main content should be visible at {formFactor} size");

    // Verify specific layout adjustments
    if (width < 1024)
    {
        // Verify compact mode
        var compactNavButton = _driver.FindElementByAccessibilityId("CompactNavButton");
        Assert.IsTrue(compactNavButton.Displayed,
            "Compact navigation should be used for narrow screens");
    }
}
```

#### Orientation Testing (Mobile):

```csharp
[TestMethod]
public void Layout_AdaptToOrientation_Mobile()
{
    // Portrait
    _driver.Orientation = ScreenOrientation.Portrait;
    Thread.Sleep(1000); // Allow layout to settle

    var element = _driver.FindElementByAccessibilityId("MainContent");
    var portraitHeight = element.Size.Height;

    // Landscape
    _driver.Orientation = ScreenOrientation.Landscape;
    Thread.Sleep(1000);

    var landscapeHeight = element.Size.Height;

    Assert.AreNotEqual(portraitHeight, landscapeHeight,
        "Layout should adapt to orientation change");
}
```

---

### 5. Screenshot Comparison Testing

Visual regression testing ensures UI consistency across releases.

#### Using ImageSharp for Pixel Comparison:

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public class VisualRegressionTester
{
    private readonly string _baselineFolder;
    private readonly string _currentFolder;
    private readonly double _threshold;

    public VisualRegressionTester(
        string baselineFolder,
        string currentFolder,
        double threshold = 0.05)
    {
        _baselineFolder = baselineFolder;
        _currentFolder = currentFolder;
        _threshold = threshold;
    }

    public async Task<VisualComparisonResult> CompareScreenshot(
        WindowsDriver<WindowsElement> driver,
        string testName)
    {
        // Take current screenshot
        var screenshot = driver.GetScreenshot();
        var currentPath = Path.Combine(_currentFolder, $"{testName}.png");
        screenshot.SaveAsFile(currentPath);

        // Load baseline
        var baselinePath = Path.Combine(_baselineFolder, $"{testName}.png");
        if (!File.Exists(baselinePath))
        {
            // First run - create baseline
            File.Copy(currentPath, baselinePath);
            return new VisualComparisonResult
            {
                IsMatch = true,
                Message = "Baseline created"
            };
        }

        // Compare images
        using var baselineImage = await Image.LoadAsync<Rgba32>(baselinePath);
        using var currentImage = await Image.LoadAsync<Rgba32>(currentPath);

        if (baselineImage.Width != currentImage.Width ||
            baselineImage.Height != currentImage.Height)
        {
            return new VisualComparisonResult
            {
                IsMatch = false,
                Message = "Image dimensions differ"
            };
        }

        var totalPixels = baselineImage.Width * baselineImage.Height;
        var differentPixels = 0;

        for (int y = 0; y < baselineImage.Height; y++)
        {
            for (int x = 0; x < baselineImage.Width; x++)
            {
                var baselinePixel = baselineImage[x, y];
                var currentPixel = currentImage[x, y];

                if (!PixelsMatch(baselinePixel, currentPixel))
                {
                    differentPixels++;
                }
            }
        }

        var differenceRatio = (double)differentPixels / totalPixels;
        var isMatch = differenceRatio <= _threshold;

        return new VisualComparisonResult
        {
            IsMatch = isMatch,
            DifferencePercentage = differenceRatio * 100,
            Message = isMatch
                ? "Visual match within threshold"
                : $"Visual difference: {differenceRatio:P2}"
        };
    }

    private bool PixelsMatch(Rgba32 pixel1, Rgba32 pixel2, int tolerance = 5)
    {
        return Math.Abs(pixel1.R - pixel2.R) <= tolerance &&
               Math.Abs(pixel1.G - pixel2.G) <= tolerance &&
               Math.Abs(pixel1.B - pixel2.B) <= tolerance &&
               Math.Abs(pixel1.A - pixel2.A) <= tolerance;
    }
}

public class VisualComparisonResult
{
    public bool IsMatch { get; set; }
    public double DifferencePercentage { get; set; }
    public string Message { get; set; }
}
```

#### Using in Tests:

```csharp
[TestMethod]
public async Task MainWindow_VisualAppearance_MatchesBaseline()
{
    // Arrange
    var visualTester = new VisualRegressionTester(
        @"C:\TestBaselines",
        @"C:\TestResults",
        threshold: 0.02); // 2% difference allowed

    // Act
    var mainWindow = _driver.FindElementByAccessibilityId("MainWindow");
    var result = await visualTester.CompareScreenshot(_driver, "MainWindow_Default");

    // Assert
    Assert.IsTrue(result.IsMatch, result.Message);
}
```

---

## MVVM Testing Strategy

### 1. Testing ViewModels in Isolation

ViewModels contain the core application logic and should be thoroughly unit tested.

#### Benefits of ViewModel Testing:
- Fast execution (no UI required)
- Easy to mock dependencies
- High code coverage achievable
- Supports TDD workflow
- Tests business logic directly

#### Basic ViewModel Test Structure:

```csharp
using AlUmmahNow.VisualImporterUno.ViewModels;
using AlUmmahNow.Importer.Core;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace AlUmmahNow.Tests.ViewModels;

public class MainViewModelTests : IDisposable
{
    private readonly Mock<ImportService> _mockImportService;
    private readonly Mock<ILogger<MainViewModel>> _mockLogger;
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        _mockImportService = new Mock<ImportService>();
        _mockLogger = new Mock<ILogger<MainViewModel>>();

        _viewModel = new MainViewModel(_mockImportService.Object);
    }

    [Fact]
    public void SelectedFolderPath_WhenSet_NotifiesCanExecuteChanged()
    {
        // Arrange
        var propertyChangedFired = false;
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedFolderPath))
                propertyChangedFired = true;
        };

        // Act
        _viewModel.SelectedFolderPath = @"C:\TestFolder";

        // Assert
        propertyChangedFired.Should().BeTrue();
        _viewModel.SelectedFolderPath.Should().Be(@"C:\TestFolder");
    }

    [Fact]
    public void CanStartImport_WhenNoFilesScanned_ReturnsFalse()
    {
        // Arrange
        _viewModel.HasScannedFiles = false;
        _viewModel.IsImporting = false;

        // Act
        var canExecute = _viewModel.CanStartImport;

        // Assert
        canExecute.Should().BeFalse();
    }

    [Fact]
    public void CanStartImport_WhenFilesScannedAndNotImporting_ReturnsTrue()
    {
        // Arrange
        _viewModel.HasScannedFiles = true;
        _viewModel.IsImporting = false;
        _viewModel.IsScanning = false;

        // Act
        var canExecute = _viewModel.CanStartImport;

        // Assert
        canExecute.Should().BeTrue();
    }

    [Fact]
    public async Task ScanFolderAsync_WithValidPath_SetsIsScanningTrue()
    {
        // Arrange
        _viewModel.SelectedFolderPath = @"C:\TestVideos";
        _mockImportService
            .Setup(s => s.ScanDirectoryAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<VideoFileInfo>());

        // Act
        var scanTask = _viewModel.ScanFolderCommand.ExecuteAsync(null);

        // Assert (while running)
        _viewModel.IsScanning.Should().BeTrue();

        await scanTask;

        // Assert (after completion)
        _viewModel.IsScanning.Should().BeFalse();
    }

    public void Dispose()
    {
        _viewModel?.Dispose();
    }
}
```

---

### 2. Testing Commands and ICommand Implementations

Commands are the primary way ViewModels respond to user actions.

#### Testing RelayCommand:

```csharp
[Fact]
public void SelectFolderCommand_CanExecute_ReturnsTrue()
{
    // Arrange & Act
    var canExecute = _viewModel.SelectFolderCommand.CanExecute(null);

    // Assert
    canExecute.Should().BeTrue();
}

[Fact]
public async Task SelectFolderCommand_WhenExecuted_UpdatesSelectedFolderPath()
{
    // This test requires mocking the FolderPicker, which is OS-dependent
    // For unit tests, we test the result of setting the property directly

    // Arrange
    var expectedPath = @"C:\SelectedFolder";

    // Act
    _viewModel.SelectedFolderPath = expectedPath;

    // Assert
    _viewModel.SelectedFolderPath.Should().Be(expectedPath);
    _viewModel.StatusMessage.Should().Contain("Selected folder");
}

[Fact]
public async Task StartImportCommand_WhenExecuted_CallsImportService()
{
    // Arrange
    var testFiles = new List<VideoFileInfo>
    {
        new() { FilePath = "video1.mp4", IsSelected = true, IsDuplicate = false },
        new() { FilePath = "video2.mp4", IsSelected = true, IsDuplicate = false }
    };

    _viewModel.HasScannedFiles = true;
    _viewModel.IsImporting = false;

    // Simulate scanned files (normally done by ScanFolderAsync)
    // This would require exposing a method or using reflection in real tests

    _mockImportService
        .Setup(s => s.ImportVideosAsync(
            It.IsAny<List<VideoFileInfo>>(),
            It.IsAny<string>()))
        .Returns(Task.CompletedTask);

    // Act
    await _viewModel.StartImportCommand.ExecuteAsync(null);

    // Assert
    _mockImportService.Verify(
        s => s.ImportVideosAsync(
            It.Is<List<VideoFileInfo>>(files => files.Count == 2),
            It.IsAny<string>()),
        Times.Once);
}
```

#### Testing Command CanExecute Dependencies:

```csharp
[Theory]
[InlineData(null, false, false, false)]  // No path selected
[InlineData("", false, false, false)]    // Empty path
[InlineData(@"C:\Test", true, false, false)]  // Currently scanning
[InlineData(@"C:\Test", false, true, false)]  // Currently importing
[InlineData(@"C:\Test", false, false, true)]  // Ready to scan
public void CanScanFolder_VariousStates_ReturnsExpected(
    string folderPath,
    bool isScanning,
    bool isImporting,
    bool expected)
{
    // Arrange
    _viewModel.SelectedFolderPath = folderPath;
    _viewModel.IsScanning = isScanning;
    _viewModel.IsImporting = isImporting;

    // Act
    var canExecute = _viewModel.CanScanFolder;

    // Assert
    canExecute.Should().Be(expected);
}
```

---

### 3. Testing Data Binding Scenarios

Data binding is central to MVVM. Test that properties correctly propagate changes.

#### Testing Property Change Notifications:

```csharp
[Fact]
public void StatusMessage_WhenChanged_RaisesPropertyChanged()
{
    // Arrange
    var propertyChanged = false;
    var propertyName = string.Empty;

    _viewModel.PropertyChanged += (s, e) =>
    {
        propertyChanged = true;
        propertyName = e.PropertyName;
    };

    // Act
    _viewModel.StatusMessage = "New status";

    // Assert
    propertyChanged.Should().BeTrue();
    propertyName.Should().Be(nameof(MainViewModel.StatusMessage));
}

[Fact]
public void ProgressPercentage_WhenChanged_RaisesPropertyChanged()
{
    // Arrange
    var changeCount = 0;
    _viewModel.PropertyChanged += (s, e) =>
    {
        if (e.PropertyName == nameof(MainViewModel.ProgressPercentage))
            changeCount++;
    };

    // Act
    _viewModel.ProgressPercentage = 50;
    _viewModel.ProgressPercentage = 75;
    _viewModel.ProgressPercentage = 100;

    // Assert
    changeCount.Should().Be(3);
    _viewModel.ProgressPercentage.Should().Be(100);
}
```

#### Testing Computed Properties:

```csharp
[Fact]
public void HasSelectedFiles_DependsOnSelectedFileCount()
{
    // Arrange
    _viewModel.SelectedFileCount = 0;

    // Act & Assert - No files selected
    _viewModel.HasSelectedFiles.Should().BeFalse();

    // Act & Assert - Files selected
    _viewModel.SelectedFileCount = 5;
    _viewModel.HasSelectedFiles.Should().BeTrue();
}

[Fact]
public void VideoFileInfo_StatusText_ReflectsImportStatus()
{
    // Arrange
    var videoFile = new VideoFileInfo
    {
        ImportStatus = ImportFileStatus.NotImported,
        IsSelected = true,
        IsDuplicate = false
    };

    // Act & Assert
    videoFile.StatusText.Should().Be("Ready");

    videoFile.ImportStatus = ImportFileStatus.Uploading;
    videoFile.StatusText.Should().Be("Uploading");

    videoFile.ImportStatus = ImportFileStatus.Uploaded;
    videoFile.StatusText.Should().Be("Uploaded");
}
```

#### Testing Collection Updates:

```csharp
[Fact]
public async Task ScanFolderAsync_PopulatesVideoFilesCollection()
{
    // Arrange
    var mockFiles = new List<VideoFileInfo>
    {
        new() { FileName = "video1.mp4", FilePath = @"C:\Videos\video1.mp4" },
        new() { FileName = "video2.mp4", FilePath = @"C:\Videos\video2.mp4" },
        new() { FileName = "video3.mp4", FilePath = @"C:\Videos\video3.mp4" }
    };

    _viewModel.SelectedFolderPath = @"C:\Videos";

    _mockImportService
        .Setup(s => s.ScanDirectoryAsync(@"C:\Videos", true))
        .ReturnsAsync(mockFiles);

    // Act
    await _viewModel.ScanFolderCommand.ExecuteAsync(null);

    // Assert
    _viewModel.VideoFiles.Should().HaveCount(3);
    _viewModel.VideoFiles.Should().Contain(f => f.FileName == "video1.mp4");
    _viewModel.HasScannedFiles.Should().BeTrue();
}
```

---

### 4. Mocking INotifyPropertyChanged

When testing components that depend on property changes, use helpers:

```csharp
public static class PropertyChangedTestExtensions
{
    public static void AssertPropertyChanged<TViewModel>(
        this TViewModel viewModel,
        string propertyName,
        Action<TViewModel> action)
        where TViewModel : INotifyPropertyChanged
    {
        var propertyChangedFired = false;

        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == propertyName)
                propertyChangedFired = true;
        };

        action(viewModel);

        propertyChangedFired.Should().BeTrue(
            $"PropertyChanged should be raised for {propertyName}");
    }

    public static List<string> GetChangedProperties<TViewModel>(
        this TViewModel viewModel,
        Action<TViewModel> action)
        where TViewModel : INotifyPropertyChanged
    {
        var changedProperties = new List<string>();

        viewModel.PropertyChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
                changedProperties.Add(e.PropertyName);
        };

        action(viewModel);

        return changedProperties;
    }
}

// Usage
[Fact]
public void SettingIsScanning_RaisesMultiplePropertyChanges()
{
    // Arrange & Act
    var changedProperties = _viewModel.GetChangedProperties(vm =>
    {
        vm.IsScanning = true;
    });

    // Assert
    changedProperties.Should().Contain(nameof(MainViewModel.IsScanning));
    // May also notify related command CanExecute changes
}
```

---

## Uno Platform Specific

### 1. Cross-Platform UI Testing Strategies

Uno Platform's cross-platform nature requires special considerations.

#### Platform-Specific Test Attributes:

```csharp
[TestClass]
public class CrossPlatformTests
{
    [TestMethod]
    [TestCategory("Windows")]
    public void FileDialog_Windows_UsesNativeDialog()
    {
        // Windows-specific test using WinAppDriver
    }

    [TestMethod]
    [TestCategory("WASM")]
    public void FileDialog_WASM_UsesBrowserDialog()
    {
        // WASM-specific test using Selenium
    }

    [TestMethod]
    [TestCategory("Android")]
    public void FileDialog_Android_UsesNativeDialog()
    {
        // Android-specific test using Appium
    }
}
```

#### Conditional Compilation for Platform Tests:

```csharp
#if __WASM__
    [TestMethod]
    public void WASM_SpecificFeature_WorksCorrectly()
    {
        // WASM-only test
    }
#elif __ANDROID__
    [TestMethod]
    public void Android_SpecificFeature_WorksCorrectly()
    {
        // Android-only test
    }
#elif WINDOWS
    [TestMethod]
    public void Windows_SpecificFeature_WorksCorrectly()
    {
        // Windows-only test
    }
#endif
```

---

### 2. Testing Skia, WASM, Mobile, and Windows Targets

#### Current Project Status:
The AlUmmahNow.VisualImporterUno project currently targets:
- **Windows:** `net9.0-windows10.0.26100`

#### Expansion Considerations:

When adding additional platforms, follow this testing matrix:

| Platform | Test Framework | Key Considerations |
|----------|---------------|-------------------|
| **Windows** | WinAppDriver | Native file dialogs, Windows-specific APIs |
| **WASM** | Selenium WebDriver | Browser compatibility, file upload limitations |
| **iOS** | Appium (XCUITest) | App Store guidelines, permissions |
| **Android** | Appium (UIAutomator2) | Permissions, device fragmentation |
| **macOS** | Appium (Mac2 Driver) | Native file dialogs, permissions |
| **Skia (Linux/Tizen)** | Appium or custom | Platform-specific drivers |

---

### 3. Handling Platform-Specific Code in Tests

Use conditional compilation and dependency injection:

#### ViewModel Design for Testability:

```csharp
// Interface for platform-specific operations
public interface IFilePickerService
{
    Task<string?> PickFolderAsync();
    Task<string?> PickFileAsync(params string[] extensions);
}

// Windows implementation
#if WINDOWS
public class WindowsFilePickerService : IFilePickerService
{
    public async Task<string?> PickFolderAsync()
    {
        var folderPicker = new FolderPicker();
        // Windows-specific implementation
    }
}
#endif

// Mock for testing
public class MockFilePickerService : IFilePickerService
{
    public string? FolderToReturn { get; set; }

    public Task<string?> PickFolderAsync()
    {
        return Task.FromResult(FolderToReturn);
    }

    public Task<string?> PickFileAsync(params string[] extensions)
    {
        return Task.FromResult<string?>(null);
    }
}
```

#### Testing with Platform Abstractions:

```csharp
[Fact]
public async Task SelectFolder_WithMockPicker_UpdatesPath()
{
    // Arrange
    var mockFilePicker = new MockFilePickerService
    {
        FolderToReturn = @"C:\TestFolder"
    };

    var viewModel = new MainViewModel(
        _mockImportService.Object,
        mockFilePicker);

    // Act
    await viewModel.SelectFolderCommand.ExecuteAsync(null);

    // Assert
    viewModel.SelectedFolderPath.Should().Be(@"C:\TestFolder");
}
```

---

### 4. Visual Regression Testing

Uno Platform's rendering can vary between platforms, making visual testing important.

#### Percy.io Integration (Recommended for WASM):

```bash
# Install Percy CLI
npm install --save-dev @percy/cli @percy/selenium-webdriver

# Set Percy token
export PERCY_TOKEN=your_token_here
```

```csharp
using PercyIO.Selenium;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

[TestMethod]
public void WASM_MainView_VisualSnapshot()
{
    // Arrange
    var driver = new ChromeDriver();
    driver.Navigate().GoToUrl("https://localhost:5001");

    // Act - Take Percy snapshot
    Percy.Snapshot(driver, "Main View - WASM");

    // Percy will automatically compare to baseline
    driver.Quit();
}
```

#### Playwright for Multi-Platform Visual Testing:

```csharp
using Microsoft.Playwright;

[TestMethod]
public async Task VisualRegression_MainView_AllBrowsers()
{
    using var playwright = await Playwright.CreateAsync();

    foreach (var browserType in new[] {
        playwright.Chromium,
        playwright.Firefox,
        playwright.Webkit
    })
    {
        await using var browser = await browserType.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync("https://localhost:5001");

        // Take screenshot
        await page.ScreenshotAsync(new()
        {
            Path = $"screenshots/main-view-{browserType.Name}.png",
            FullPage = true
        });
    }
}
```

---

## Common Challenges & Solutions

### 1. Testing File Pickers and Dialogs

File pickers are OS-level dialogs that are difficult to automate.

#### Challenge:
- OS native dialogs are outside application control
- Different behavior per platform (Windows, macOS, Android, WASM)
- Difficult to interact with programmatically

#### Solutions:

##### A. Abstraction for Unit Tests (Recommended)

```csharp
// Already shown above - use IFilePickerService interface

[Fact]
public async Task SelectFolder_ViaAbstraction_WorksInTests()
{
    // Arrange
    var mockPicker = new MockFilePickerService
    {
        FolderToReturn = @"C:\Videos"
    };

    var viewModel = new MainViewModel(
        _mockImportService.Object,
        mockPicker);

    // Act
    await viewModel.SelectFolderCommand.ExecuteAsync(null);

    // Assert
    viewModel.SelectedFolderPath.Should().Be(@"C:\Videos");
}
```

##### B. AutoIt for Windows Dialog Automation (E2E Tests)

```csharp
using AutoItX3Lib;

public class WindowsDialogHelper
{
    private readonly AutoItX3 _autoIt;

    public WindowsDialogHelper()
    {
        _autoIt = new AutoItX3();
    }

    public void HandleFolderDialog(string folderPath, int timeoutSeconds = 10)
    {
        // Wait for folder dialog
        _autoIt.WinWait("Select Folder", "", timeoutSeconds);
        _autoIt.WinActivate("Select Folder");

        // Type the path
        _autoIt.Send(folderPath);
        Thread.Sleep(500);

        // Press Enter or click OK
        _autoIt.Send("{ENTER}");
    }
}

// Usage in test
[TestMethod]
public void SelectFolder_E2E_HandlesDialogWithAutoIt()
{
    // Arrange
    var dialogHelper = new WindowsDialogHelper();
    var mainPage = new MainViewPage(_driver);

    // Act
    mainPage.ClickSelectFolder();

    // Handle the native dialog with AutoIt
    dialogHelper.HandleFolderDialog(@"C:\TestVideos");

    // Wait for dialog to close and path to update
    Thread.Sleep(1000);

    // Assert
    var selectedPath = mainPage.GetSelectedFolderPath();
    selectedPath.Should().Be(@"C:\TestVideos");
}
```

##### C. Pre-set Path for Integration Tests

```csharp
[TestMethod]
public async Task ScanFolder_WithPresetPath_SkipsDialog()
{
    // Arrange
    var testPath = @"C:\TestVideos";
    _viewModel.SelectedFolderPath = testPath; // Bypass dialog

    _mockImportService
        .Setup(s => s.ScanDirectoryAsync(testPath, true))
        .ReturnsAsync(new List<VideoFileInfo>
        {
            new() { FileName = "video1.mp4" }
        });

    // Act
    await _viewModel.ScanFolderCommand.ExecuteAsync(null);

    // Assert
    _viewModel.VideoFiles.Should().HaveCount(1);
}
```

---

### 2. Testing Navigation

#### Challenge:
- Frame-based navigation
- Back stack management
- Parameters passing between pages
- Async navigation completion

#### Solutions:

##### A. Abstract Navigation Service

```csharp
public interface INavigationService
{
    void Navigate<TPage>(object? parameter = null);
    void Navigate(Type pageType, object? parameter = null);
    bool CanGoBack { get; }
    void GoBack();
    void ClearBackStack();
}

public class NavigationService : INavigationService
{
    private readonly Frame _frame;

    public NavigationService(Frame frame)
    {
        _frame = frame;
    }

    public void Navigate<TPage>(object? parameter = null)
    {
        _frame.Navigate(typeof(TPage), parameter);
    }

    public void Navigate(Type pageType, object? parameter = null)
    {
        _frame.Navigate(pageType, parameter);
    }

    public bool CanGoBack => _frame.CanGoBack;

    public void GoBack()
    {
        if (_frame.CanGoBack)
            _frame.GoBack();
    }

    public void ClearBackStack()
    {
        _frame.BackStack.Clear();
    }
}
```

##### B. Testing Navigation

```csharp
public class MockNavigationService : INavigationService
{
    public List<(Type PageType, object? Parameter)> NavigationHistory { get; } = new();
    public bool CanGoBack { get; set; }

    public void Navigate<TPage>(object? parameter = null)
    {
        NavigationHistory.Add((typeof(TPage), parameter));
    }

    public void Navigate(Type pageType, object? parameter = null)
    {
        NavigationHistory.Add((pageType, parameter));
    }

    public void GoBack()
    {
        if (NavigationHistory.Count > 0)
            NavigationHistory.RemoveAt(NavigationHistory.Count - 1);
    }

    public void ClearBackStack()
    {
        NavigationHistory.Clear();
    }
}

[Fact]
public void NavigateToSettings_CallsNavigationService()
{
    // Arrange
    var mockNavigation = new MockNavigationService();
    var viewModel = new MainViewModel(
        _mockImportService.Object,
        mockNavigation);

    // Act
    viewModel.NavigateToSettingsCommand.Execute(null);

    // Assert
    mockNavigation.NavigationHistory.Should().HaveCount(1);
    mockNavigation.NavigationHistory[0].PageType.Should().Be(typeof(SettingsPage));
}
```

##### C. E2E Navigation Tests

```csharp
[TestMethod]
public void Navigation_BetweenPages_PreservesState()
{
    // Arrange
    var mainPage = new MainViewPage(_driver);
    mainPage.ClickSelectFolder();
    // ... set folder path

    // Act - Navigate to settings
    var settingsButton = _driver.FindElementByAccessibilityId("SettingsButton");
    settingsButton.Click();

    // Assert - On settings page
    var settingsPage = new SettingsViewPage(_driver);
    settingsPage.IsDisplayed().Should().BeTrue();

    // Act - Navigate back
    var backButton = _driver.FindElementByAccessibilityId("BackButton");
    backButton.Click();

    // Assert - Back on main page with state preserved
    mainPage.IsDisplayed().Should().BeTrue();
    mainPage.GetSelectedFolderPath().Should().NotBeNullOrEmpty();
}
```

---

### 3. Testing Animations and Transitions

#### Challenge:
- Animations cause timing issues
- Elements not immediately visible/clickable
- Different animation speeds in tests vs. production

#### Solutions:

##### A. Disable Animations for Tests

```xml
<!-- In App.xaml or test-specific resources -->
<Application.Resources>
    <ResourceDictionary>
        <Duration x:Key="TestAnimationDuration">0:0:0</Duration>
        <x:Double x:Key="TestAnimationSpeed">0</x:Double>
    </ResourceDictionary>
</Application.Resources>
```

```csharp
// In test setup
[TestInitialize]
public void Setup()
{
    // Set system-wide animation speed to zero for tests
    SystemNavigationManagerPreview.GetForCurrentView().BackRequested += (s, e) =>
    {
        e.Handled = true;
    };
}
```

##### B. Wait for Animations to Complete

```csharp
public static class AnimationHelpers
{
    public static async Task WaitForAnimation(
        this WindowsElement element,
        int estimatedDurationMs = 300)
    {
        await Task.Delay(estimatedDurationMs + 50); // Add buffer
    }

    public static async Task WaitUntilStable(
        this WindowsElement element,
        int checkIntervalMs = 100,
        int maxWaitMs = 5000)
    {
        var previousLocation = element.Location;
        var previousSize = element.Size;
        var startTime = DateTime.Now;

        while ((DateTime.Now - startTime).TotalMilliseconds < maxWaitMs)
        {
            await Task.Delay(checkIntervalMs);

            var currentLocation = element.Location;
            var currentSize = element.Size;

            if (currentLocation == previousLocation && currentSize == previousSize)
            {
                // Element is stable
                return;
            }

            previousLocation = currentLocation;
            previousSize = currentSize;
        }

        throw new TimeoutException(
            $"Element did not stabilize within {maxWaitMs}ms");
    }
}

// Usage
[TestMethod]
public async Task Button_AfterSlideInAnimation_BecomesClickable()
{
    // Arrange
    var mainPage = new MainViewPage(_driver);

    // Act
    var actionButton = _driver.FindElementByAccessibilityId("ActionButton");

    // Wait for slide-in animation
    await actionButton.WaitUntilStable();

    actionButton.Click();

    // Assert
    var result = mainPage.GetStatusMessage();
    result.Should().NotBeNullOrEmpty();
}
```

---

### 4. Handling Timing Issues

#### Challenge:
- Async operations (API calls, file I/O)
- UI thread marshaling
- Race conditions in tests
- Flaky tests due to timing

#### Solutions:

##### A. Structured Waiting Strategy

```csharp
public class SmartWait
{
    private readonly IWebDriver _driver;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollingInterval;

    public SmartWait(
        IWebDriver driver,
        TimeSpan timeout,
        TimeSpan? pollingInterval = null)
    {
        _driver = driver;
        _timeout = timeout;
        _pollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(500);
    }

    public T Until<T>(Func<IWebDriver, T> condition, string failureMessage = null)
    {
        var wait = new WebDriverWait(_driver, _timeout)
        {
            PollingInterval = _pollingInterval
        };

        wait.IgnoreExceptionTypes(
            typeof(NoSuchElementException),
            typeof(StaleElementReferenceException));

        try
        {
            return wait.Until(condition);
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new TimeoutException(
                failureMessage ?? "Wait condition was not met", ex);
        }
    }

    public WindowsElement WaitForElement(
        By locator,
        string elementName = null)
    {
        return Until(
            driver => driver.FindElement(locator) as WindowsElement,
            $"Element '{elementName ?? locator}' was not found");
    }

    public void WaitForElementToDisappear(
        By locator,
        string elementName = null)
    {
        Until(
            driver =>
            {
                try
                {
                    var element = driver.FindElement(locator);
                    return !element.Displayed;
                }
                catch (NoSuchElementException)
                {
                    return true;
                }
            },
            $"Element '{elementName ?? locator}' did not disappear");
    }

    public void WaitForText(
        WindowsElement element,
        string expectedText)
    {
        Until(
            driver => element.Text.Contains(expectedText),
            $"Element text did not contain '{expectedText}'. Actual: '{element.Text}'");
    }
}

// Usage
[TestMethod]
public void LongRunningOperation_CompletesSuccessfully()
{
    // Arrange
    var wait = new SmartWait(_driver, TimeSpan.FromSeconds(30));
    var mainPage = new MainViewPage(_driver);

    // Act
    mainPage.ClickScanFolder();

    // Assert
    var statusText = _driver.FindElementByAccessibilityId("StatusMessageText");
    wait.WaitForText(statusText, "completed");

    var finalStatus = statusText.Text;
    finalStatus.Should().Contain("Found").And.Contain("files");
}
```

##### B. Retry Logic for Flaky Operations

```csharp
public static class RetryHelper
{
    public static async Task<T> RetryAsync<T>(
        Func<Task<T>> operation,
        int maxAttempts = 3,
        TimeSpan? delayBetweenAttempts = null)
    {
        var delay = delayBetweenAttempts ?? TimeSpan.FromMilliseconds(500);
        Exception lastException = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (
                ex is StaleElementReferenceException ||
                ex is NoSuchElementException ||
                ex is ElementNotInteractableException)
            {
                lastException = ex;

                if (attempt < maxAttempts)
                {
                    await Task.Delay(delay);
                }
            }
        }

        throw new Exception(
            $"Operation failed after {maxAttempts} attempts",
            lastException);
    }

    public static async Task RetryAsync(
        Func<Task> operation,
        int maxAttempts = 3,
        TimeSpan? delayBetweenAttempts = null)
    {
        await RetryAsync(async () =>
        {
            await operation();
            return true;
        }, maxAttempts, delayBetweenAttempts);
    }
}

// Usage
[TestMethod]
public async Task ClickButton_WithRetry_HandlesStaleReference()
{
    // Arrange
    var mainPage = new MainViewPage(_driver);

    // Act - Retry if element becomes stale
    await RetryHelper.RetryAsync(async () =>
    {
        var button = _driver.FindElementByAccessibilityId("ScanButton");
        button.Click();
        await Task.Delay(100); // Brief pause after click
    });

    // Assert
    var status = mainPage.GetStatusMessage();
    status.Should().Contain("Scanning");
}
```

---

## Codebase Analysis

### ViewModels Analysis

#### 1. MainViewModel (`/AlUmmahNow.VisualImporterUno/ViewModels/MainViewModel.cs`)

**Complexity:** High (1820 lines)
**Responsibilities:**
- Folder selection and scanning
- Video file management and filtering
- Import orchestration
- Progress tracking
- Real-time UI updates via DispatcherQueue
- Complex error handling with COM exception recovery

**Test Coverage Needs:**
- ✅ **High Priority:**
  - Property change notifications (INotifyPropertyChanged)
  - Command CanExecute logic
  - Filtering and sorting operations
  - File selection state management
  - Status message updates

- ⚠️ **Medium Priority:**
  - Async scanning operations
  - Import progress tracking
  - Error recovery scenarios
  - Dispatcher queue marshaling

- ⏳ **Lower Priority (Integration Tests):**
  - SignalR event handling
  - File system operations
  - COM interop

**Key Testing Challenges:**
- Heavy use of DispatcherQueue for thread marshaling
- Complex COM error handling
- File system dependencies (folder picker)
- Large number of observable collections

**Testability Improvements Recommended:**
1. Extract `IDispatcherService` interface
2. Extract `IFileSystemService` for folder operations
3. Separate filtering/sorting logic into testable service
4. Reduce cyclomatic complexity in long methods

---

#### 2. SettingsViewModel (`/AlUmmahNow.VisualImporterUno/ViewModels/SettingsViewModel.cs`)

**Complexity:** Medium (391 lines)
**Responsibilities:**
- Configuration management
- Service URL testing
- Settings persistence (Windows ApplicationData + JSON fallback)
- Service path detection

**Test Coverage Needs:**
- ✅ **High Priority:**
  - Settings load/save operations
  - Property validation
  - Default value reset
  - Service path detection logic

- ⚠️ **Medium Priority:**
  - HTTP connection testing (mock HttpClient)
  - ApplicationData API (mock storage)
  - Fallback persistence logic

**Key Testing Challenges:**
- Windows ApplicationData API (platform-specific)
- File system I/O for JSON fallback
- HTTP client for connection testing

**Testability Improvements Recommended:**
1. Inject `IConfiguration` and `ILogger` (already done ✅)
2. Extract `ISettingsStorage` interface
3. Extract `IHttpClientFactory` for connection testing

---

#### 3. ServiceStatusViewModel (`/AlUmmahNow.VisualImporterUno/ViewModels/ServiceStatusViewModel.cs`)

**Complexity:** High (1016 lines)
**Responsibilities:**
- SignalR connection management
- Service lifecycle (start/stop)
- Real-time progress updates
- System metrics monitoring (CPU, memory)
- Event aggregation

**Test Coverage Needs:**
- ✅ **High Priority:**
  - SignalR event handlers
  - Service status state machine
  - Command availability logic
  - Observable collections management

- ⚠️ **Medium Priority:**
  - Service start/stop orchestration
  - Performance counter reading
  - Timer-based metrics updates

**Key Testing Challenges:**
- SignalR client dependency
- System performance counters
- Timer-based updates
- Thread synchronization

**Testability Improvements Recommended:**
1. Inject `FileScanSignalRClient` (already done ✅)
2. Extract `ISystemMetricsService` for performance monitoring
3. Use testable timer abstraction
4. Separate event handlers into testable methods

---

#### 4. DatabaseMainViewModel (`/AlUmmahNow.VisualImporterUno/ViewModels/DatabaseMainViewModel.cs`)

**Complexity:** Medium (479 lines)
**Responsibilities:**
- Database-backed scanning (vs. in-memory)
- Paginated file browsing
- Scan session management
- Channel-based file organization

**Test Coverage Needs:**
- ✅ **High Priority:**
  - Pagination logic
  - Session selection and loading
  - Channel filtering
  - Property bindings

- ⚠️ **Medium Priority:**
  - Database service integration
  - Event subscription/unsubscription
  - Async scan operations

**Testability:** This is the most testable ViewModel due to:
- Clear dependency injection
- Logger usage
- Database service abstraction

---

### UI Patterns Being Used

#### 1. Modern Material Design UI
- Card-based layouts (`ModernDashboardView.xaml`)
- Elevation with `ThemeShadow`
- Color scheme: Green primary (#16A34A), neutral grays
- Responsive grid layouts

#### 2. Data Binding Patterns
- Two-way binding for user input: `{Binding IsSelected, Mode=TwoWay}`
- Computed properties: `StatusText`, `HasSelectedFiles`
- Collection binding: `ItemsSource="{Binding FilteredVideoFiles}"`

#### 3. MVVM with CommunityToolkit
- `[RelayCommand]` for commands
- `[ObservableProperty]` for properties
- Partial classes for source generators

#### 4. Async UI Operations
- `RelayCommand` with async support: `ExecuteAsync`
- Progress reporting via `IProgress<T>`
- `DispatcherQueue` for UI thread marshaling

---

### Opportunities for UI Test Automation

#### High-Value Test Scenarios:

1. **Folder Scan Workflow** (E2E Test)
   - Select folder → Scan → Display results → Filter → Import
   - Expected: 100% critical path coverage

2. **Settings Persistence** (Integration Test)
   - Change settings → Save → Restart app → Verify persistence
   - Expected: Catch regression in settings storage

3. **Real-time Progress Updates** (E2E Test)
   - Start scan → Monitor progress bar → Verify completion
   - Expected: Detect UI threading issues

4. **Service Status Dashboard** (E2E Test)
   - View service status → Start service → Stop service
   - Expected: Catch service lifecycle issues

5. **File Filtering and Sorting** (Unit Test)
   - Apply filters → Change sort order → Verify correct display
   - Expected: High coverage of business logic

---

## Implementation Recommendations

### Recommended Testing Strategy

```
Phase 1: Unit Tests (Weeks 1-2)
├── MainViewModel unit tests
├── SettingsViewModel unit tests
├── ServiceStatusViewModel unit tests
└── DatabaseMainViewModel unit tests
Target: 80% code coverage of ViewModels

Phase 2: Integration Tests (Weeks 3-4)
├── ViewModel + Service integration
├── Database operations
└── SignalR connectivity
Target: Cover major interaction paths

Phase 3: E2E Tests (Weeks 5-6)
├── Critical user journeys (WinAppDriver)
├── Visual regression tests
└── Cross-browser WASM tests (future)
Target: 3-5 key workflows

Phase 4: CI/CD Integration (Week 7)
├── Automated test execution
├── Test reporting
└── Code coverage tracking
```

---

### Recommended Framework Selection

**For Current State (Windows-only target):**

```
Unit Tests (80% of test suite)
└── xUnit + Moq + FluentAssertions ✅ (Already in place)

Integration Tests (15% of test suite)
└── xUnit + TestHost + InMemory Database ✅ (Already in place)

E2E Tests (5% of test suite)
└── WinAppDriver + MSTest ⚠️ (New - To be added)
```

**For Future Multi-Platform Support:**

```
Add Platform-Specific E2E Tests:
├── WASM: Selenium WebDriver + Playwright
├── Android: Appium + UIAutomator2
├── iOS: Appium + XCUITest
└── macOS: Appium + Mac2 Driver
```

---

### Project Structure Recommendation

```
AlUmmahNow.Tests/
├── Unit/
│   ├── ViewModels/
│   │   ├── MainViewModelTests.cs
│   │   ├── SettingsViewModelTests.cs
│   │   ├── ServiceStatusViewModelTests.cs
│   │   └── DatabaseMainViewModelTests.cs
│   ├── Models/
│   │   └── VideoFileInfoTests.cs
│   └── Helpers/
│       └── PropertyChangedTestExtensions.cs
├── Integration/
│   ├── ViewModelServiceIntegrationTests.cs
│   ├── DatabaseOperationsTests.cs
│   └── SignalRIntegrationTests.cs
└── E2E/
    ├── PageObjects/
    │   ├── MainViewPage.cs
    │   ├── SettingsViewPage.cs
    │   └── ServiceStatusPage.cs
    ├── Helpers/
    │   ├── WinAppDriverHelper.cs
    │   ├── SmartWait.cs
    │   └── VisualRegressionTester.cs
    ├── Scenarios/
    │   ├── FolderScanWorkflowTests.cs
    │   ├── ImportWorkflowTests.cs
    │   ├── SettingsWorkflowTests.cs
    │   └── ServiceManagementWorkflowTests.cs
    └── BaseClasses/
        └── E2ETestBase.cs
```

---

## Example Test Code

### 1. MainViewModel Unit Tests

```csharp
using AlUmmahNow.VisualImporterUno.ViewModels;
using AlUmmahNow.VisualImporterUno.Models;
using AlUmmahNow.Importer.Core;
using Moq;
using Xunit;
using FluentAssertions;

namespace AlUmmahNow.Tests.Unit.ViewModels;

public class MainViewModelTests : IDisposable
{
    private readonly Mock<ImportService> _mockImportService;
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        _mockImportService = new Mock<ImportService>();
        _viewModel = new MainViewModel(_mockImportService.Object);
    }

    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        // Assert
        _viewModel.StatusMessage.Should().Be("Ready to import");
        _viewModel.ProgressPercentage.Should().Be(0);
        _viewModel.IsImporting.Should().BeFalse();
        _viewModel.IsScanning.Should().BeFalse();
        _viewModel.HasScannedFiles.Should().BeFalse();
        _viewModel.HideDuplicates.Should().BeTrue();
    }

    [Fact]
    public void SelectedFolderPath_WhenSet_RaisesPropertyChanged()
    {
        // Arrange
        var propertyChanged = false;
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedFolderPath))
                propertyChanged = true;
        };

        // Act
        _viewModel.SelectedFolderPath = @"C:\TestFolder";

        // Assert
        propertyChanged.Should().BeTrue();
        _viewModel.SelectedFolderPath.Should().Be(@"C:\TestFolder");
    }

    [Theory]
    [InlineData(null, false, false, false)]
    [InlineData("", false, false, false)]
    [InlineData(@"C:\Test", true, false, false)]
    [InlineData(@"C:\Test", false, true, false)]
    [InlineData(@"C:\Test", false, false, true)]
    public void CanScanFolder_WithVariousStates_ReturnsExpected(
        string folderPath,
        bool isScanning,
        bool isImporting,
        bool expected)
    {
        // Arrange
        _viewModel.SelectedFolderPath = folderPath;
        _viewModel.IsScanning = isScanning;
        _viewModel.IsImporting = isImporting;

        // Act
        var canScan = _viewModel.CanScanFolder;

        // Assert
        canScan.Should().Be(expected);
    }

    [Fact]
    public void CanStartImport_WhenNoFilesScanned_ReturnsFalse()
    {
        // Arrange
        _viewModel.HasScannedFiles = false;
        _viewModel.IsImporting = false;
        _viewModel.IsScanning = false;

        // Act
        var canImport = _viewModel.CanStartImport;

        // Assert
        canImport.Should().BeFalse();
    }

    [Fact]
    public void CanStartImport_WhenFilesScannedAndNotBusy_ReturnsTrue()
    {
        // Arrange
        _viewModel.HasScannedFiles = true;
        _viewModel.IsImporting = false;
        _viewModel.IsScanning = false;

        // Act
        var canImport = _viewModel.CanStartImport;

        // Assert
        canImport.Should().BeTrue();
    }

    [Fact]
    public void SelectAllFiles_SelectsAllNonDuplicateFiles()
    {
        // Arrange
        var files = new List<VideoFileInfo>
        {
            new() { FilePath = "file1.mp4", IsDuplicate = false, IsSelected = false },
            new() { FilePath = "file2.mp4", IsDuplicate = true, IsSelected = false },
            new() { FilePath = "file3.mp4", IsDuplicate = false, IsSelected = false }
        };

        // Simulate files loaded (normally done by ScanFolderAsync)
        // For real implementation, you'd need to expose a way to set this

        // Act
        _viewModel.SelectAllFilesCommand.Execute(null);

        // Assert
        // Verify non-duplicate files are selected
        // files[0].IsSelected.Should().BeTrue();
        // files[1].IsSelected.Should().BeFalse(); // Duplicate remains unselected
        // files[2].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void DeselectAllFiles_DeselectsAllFiles()
    {
        // Similar to SelectAllFiles test
    }

    [Fact]
    public void HideDuplicates_WhenToggled_UpdatesFilteredCollection()
    {
        // Test filtering logic
    }

    [Fact]
    public void CurrentSortOption_WhenChanged_UpdatesFilteredCollection()
    {
        // Test sorting logic
    }

    public void Dispose()
    {
        _viewModel?.Dispose();
    }
}
```

---

### 2. SettingsViewModel Unit Tests

```csharp
using AlUmmahNow.VisualImporterUno.ViewModels;
using AlUmmahNow.VisualImporterUno.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace AlUmmahNow.Tests.Unit.ViewModels;

public class SettingsViewModelTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<SettingsViewModel>> _mockLogger;
    private readonly Mock<FileScanServiceManager> _mockServiceManager;
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<SettingsViewModel>>();
        _mockServiceManager = new Mock<FileScanServiceManager>();

        // Setup default configuration values
        _mockConfiguration
            .Setup(c => c.GetValue<string>("FileScanService:Url"))
            .Returns("http://localhost:5000");

        _viewModel = new SettingsViewModel(
            _mockConfiguration.Object,
            _mockLogger.Object,
            _mockServiceManager.Object);
    }

    [Fact]
    public void Constructor_LoadsDefaultSettings()
    {
        // Assert
        _viewModel.FileScanServiceUrl.Should().Be("http://localhost:5000");
        _viewModel.AutoStartService.Should().BeTrue();
    }

    [Fact]
    public void FileScanServiceUrl_WhenChanged_ClearsTestResults()
    {
        // Arrange
        _viewModel.TestResults = "Previous test results";

        // Act
        _viewModel.FileScanServiceUrl = "http://localhost:5001";

        // Assert
        _viewModel.FileScanServiceUrl.Should().Be("http://localhost:5001");
        _viewModel.TestResults.Should().BeEmpty();
    }

    [Fact]
    public async Task TestConnectionAsync_WithValidUrl_ReturnsSuccess()
    {
        // This would require mocking HttpClient
        // Use IHttpClientFactory for better testability
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsToStorage()
    {
        // Arrange
        _viewModel.FileScanServiceUrl = "http://localhost:6000";
        _viewModel.AutoStartService = false;

        // Act
        var result = await _viewModel.SaveSettingsAsync();

        // Assert
        result.Should().BeTrue();
        // Verify storage calls (would need ISettingsStorage abstraction)
    }

    [Fact]
    public void ResetToDefaults_RestoresDefaultValues()
    {
        // Arrange
        _viewModel.FileScanServiceUrl = "http://custom:8080";
        _viewModel.AutoStartService = false;

        // Act
        _viewModel.ResetToDefaults();

        // Assert
        _viewModel.FileScanServiceUrl.Should().Be("http://localhost:5000");
        _viewModel.AutoStartService.Should().BeTrue();
    }

    [Fact]
    public void DetectedServicePaths_ContainsValidPaths()
    {
        // Assert
        _viewModel.DetectedServicePaths.Should().NotBeNull();
        // Would need to create test environment with actual paths
    }

    [Fact]
    public void SelectedDetectedPath_WhenSet_UpdatesServiceExecutablePath()
    {
        // Arrange
        var testPath = @"C:\Services\AlUmmahNow.FileScanService.exe";
        _viewModel.DetectedServicePaths.Add(testPath);

        // Act
        _viewModel.SelectedDetectedPath = testPath;

        // Assert
        _viewModel.ServiceExecutablePath.Should().Be(testPath);
    }
}
```

---

### 3. VideoFileInfo Model Tests

```csharp
using AlUmmahNow.VisualImporterUno.Models;
using Xunit;
using FluentAssertions;

namespace AlUmmahNow.Tests.Unit.Models;

public class VideoFileInfoTests
{
    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        // Arrange & Act
        var videoFile = new VideoFileInfo();

        // Assert
        videoFile.IsSelected.Should().BeTrue();
        videoFile.IsDuplicate.Should().BeFalse();
        videoFile.ImportStatus.Should().Be(ImportFileStatus.NotImported);
    }

    [Fact]
    public void IsSelected_WhenChanged_RaisesPropertyChanged()
    {
        // Arrange
        var videoFile = new VideoFileInfo();
        var propertyChanged = false;

        videoFile.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(VideoFileInfo.IsSelected))
                propertyChanged = true;
        };

        // Act
        videoFile.IsSelected = false;

        // Assert
        propertyChanged.Should().BeTrue();
        videoFile.IsSelected.Should().BeFalse();
    }

    [Theory]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(500, "500.0 B")]
    public void FileSizeFormatted_ReturnsCorrectFormat(long bytes, string expected)
    {
        // Arrange
        var videoFile = new VideoFileInfo { FileSize = bytes };

        // Act
        var formatted = videoFile.FileSizeFormatted;

        // Assert
        formatted.Should().Be(expected);
    }

    [Theory]
    [InlineData("video.mp4", "video")]
    [InlineData("my.video.file.mkv", "my.video.file")]
    [InlineData("noextension", "noextension")]
    public void FileNameWithoutExtension_ReturnsCorrectName(string fileName, string expected)
    {
        // Arrange
        var videoFile = new VideoFileInfo { FileName = fileName };

        // Act
        var nameWithoutExt = videoFile.FileNameWithoutExtension;

        // Assert
        nameWithoutExt.Should().Be(expected);
    }

    [Fact]
    public void ChannelName_WithoutOverride_UsesDirectoryName()
    {
        // Arrange
        var videoFile = new VideoFileInfo
        {
            Directory = @"C:\Videos\IslamicLectures"
        };

        // Act
        var channelName = videoFile.ChannelName;

        // Assert
        channelName.Should().Be("IslamicLectures");
    }

    [Fact]
    public void ChannelName_WithOverride_UsesOverrideValue()
    {
        // Arrange
        var videoFile = new VideoFileInfo
        {
            Directory = @"C:\Videos\OldName",
            ChannelNameOverride = "NewChannelName"
        };

        // Act
        var channelName = videoFile.ChannelName;

        // Assert
        channelName.Should().Be("NewChannelName");
    }

    [Theory]
    [InlineData(ImportFileStatus.NotImported, false, false, "Ready")]
    [InlineData(ImportFileStatus.Pending, false, false, "Pending")]
    [InlineData(ImportFileStatus.Uploading, false, false, "Uploading")]
    [InlineData(ImportFileStatus.Uploaded, false, false, "Uploaded")]
    [InlineData(ImportFileStatus.Failed, false, false, "Failed")]
    [InlineData(ImportFileStatus.NotImported, true, false, "Duplicate")]
    [InlineData(ImportFileStatus.NotImported, false, true, "Excluded")]
    public void StatusText_ReflectsCurrentState(
        ImportFileStatus status,
        bool isDuplicate,
        bool isSelected,
        string expectedText)
    {
        // Arrange
        var videoFile = new VideoFileInfo
        {
            ImportStatus = status,
            IsDuplicate = isDuplicate,
            IsSelected = !isSelected // Inverted because default is true
        };

        // Act
        var statusText = videoFile.StatusText;

        // Assert
        statusText.Should().Be(expectedText);
    }

    [Fact]
    public void ImportStatus_WhenChanged_RaisesMultiplePropertyChanges()
    {
        // Arrange
        var videoFile = new VideoFileInfo();
        var changedProperties = new List<string>();

        videoFile.PropertyChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
                changedProperties.Add(e.PropertyName);
        };

        // Act
        videoFile.ImportStatus = ImportFileStatus.Uploading;

        // Assert
        changedProperties.Should().Contain(nameof(VideoFileInfo.ImportStatus));
        changedProperties.Should().Contain(nameof(VideoFileInfo.StatusText));
        changedProperties.Should().Contain(nameof(VideoFileInfo.StatusColor));
        changedProperties.Should().Contain(nameof(VideoFileInfo.StatusIcon));
    }
}
```

---

### 4. E2E Test Example with Page Objects

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium.Appium.Windows;
using OpenQA.Selenium.Appium;
using System;
using System.IO;

namespace AlUmmahNow.Tests.E2E.Scenarios;

[TestClass]
public class FolderScanWorkflowTests : E2ETestBase
{
    private MainViewPage _mainPage;

    [TestInitialize]
    public void TestSetup()
    {
        base.Setup();
        _mainPage = new MainViewPage(Driver);
    }

    [TestMethod]
    [TestCategory("E2E")]
    [TestCategory("Critical")]
    public void FolderScan_WithValidFolder_DisplaysResults()
    {
        // Arrange
        var testFolder = CreateTestFolder();
        CreateTestVideoFiles(testFolder, count: 5);

        // Act
        _mainPage.SetFolderPath(testFolder);
        _mainPage.ClickScanFolder();
        _mainPage.WaitForScanToComplete();

        // Assert
        var statusMessage = _mainPage.GetStatusMessage();
        statusMessage.Should().Contain("Found 5 video files");

        var fileCount = _mainPage.GetVideoFileCount();
        fileCount.Should().Be(5);

        // Cleanup
        Directory.Delete(testFolder, recursive: true);
    }

    [TestMethod]
    [TestCategory("E2E")]
    public void FolderScan_WithEmptyFolder_DisplaysNoFiles()
    {
        // Arrange
        var emptyFolder = CreateTestFolder();

        // Act
        _mainPage.SetFolderPath(emptyFolder);
        _mainPage.ClickScanFolder();
        _mainPage.WaitForScanToComplete();

        // Assert
        var statusMessage = _mainPage.GetStatusMessage();
        statusMessage.Should().Contain("Found 0 video files");

        // Cleanup
        Directory.Delete(emptyFolder);
    }

    [TestMethod]
    [TestCategory("E2E")]
    [TestCategory("Critical")]
    public void FolderScan_ShowsProgressBar()
    {
        // Arrange
        var testFolder = CreateTestFolder();
        CreateTestVideoFiles(testFolder, count: 50); // More files for visible progress

        // Act
        _mainPage.SetFolderPath(testFolder);
        _mainPage.ClickScanFolder();

        // Assert - Check progress bar appears
        var progressBar = _mainPage.GetProgressBar();
        progressBar.Should().NotBeNull();
        progressBar.Displayed.Should().BeTrue();

        // Wait and verify progress increases
        var initialProgress = _mainPage.GetProgressPercentage();
        System.Threading.Thread.Sleep(500);
        var laterProgress = _mainPage.GetProgressPercentage();

        laterProgress.Should().BeGreaterThanOrEqualTo(initialProgress);

        // Wait for completion
        _mainPage.WaitForScanToComplete();
        var finalProgress = _mainPage.GetProgressPercentage();
        finalProgress.Should().Be(100);

        // Cleanup
        Directory.Delete(testFolder, recursive: true);
    }

    [TestMethod]
    [TestCategory("E2E")]
    public void FolderScan_CanBeCancelled()
    {
        // Arrange
        var testFolder = CreateTestFolder();
        CreateTestVideoFiles(testFolder, count: 100); // Many files for cancelable operation

        // Act
        _mainPage.SetFolderPath(testFolder);
        _mainPage.ClickScanFolder();

        System.Threading.Thread.Sleep(500); // Let scan start

        _mainPage.ClickCancelScan();

        // Assert
        var statusMessage = _mainPage.GetStatusMessage();
        statusMessage.Should().Contain("cancelled",
            "Status should indicate scan was cancelled");

        _mainPage.IsScanningInProgress().Should().BeFalse();

        // Cleanup
        Directory.Delete(testFolder, recursive: true);
    }

    private string CreateTestFolder()
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"AlUmmahNowTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        return tempPath;
    }

    private void CreateTestVideoFiles(string folder, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            var filePath = Path.Combine(folder, $"video{i}.mp4");
            File.WriteAllBytes(filePath, new byte[1024 * 100]); // 100KB dummy file
        }
    }
}
```

---

### 5. Page Object Implementation

```csharp
using OpenQA.Selenium.Appium.Windows;
using OpenQA.Selenium.Support.UI;
using System;
using FluentAssertions;

namespace AlUmmahNow.Tests.E2E.PageObjects;

public class MainViewPage
{
    private readonly WindowsDriver<WindowsElement> _driver;
    private readonly SmartWait _wait;

    public MainViewPage(WindowsDriver<WindowsElement> driver)
    {
        _driver = driver;
        _wait = new SmartWait(driver, TimeSpan.FromSeconds(30));
    }

    // Element Locators
    private WindowsElement SelectFolderButton =>
        _driver.FindElementByAccessibilityId("SelectFolderButton");

    private WindowsElement ScanFolderButton =>
        _driver.FindElementByAccessibilityId("ScanFolderButton");

    private WindowsElement CancelScanButton =>
        _driver.FindElementByAccessibilityId("CancelScanButton");

    private WindowsElement FolderPathTextBox =>
        _driver.FindElementByAccessibilityId("FolderPathTextBox");

    private WindowsElement StatusMessageText =>
        _driver.FindElementByAccessibilityId("StatusMessageText");

    private WindowsElement ScanProgressBar =>
        _driver.FindElementByAccessibilityId("ScanProgressBar");

    private WindowsElement VideoFilesList =>
        _driver.FindElementByAccessibilityId("VideoFilesList");

    // Actions
    public void ClickSelectFolder()
    {
        SelectFolderButton.Click();
    }

    public void ClickScanFolder()
    {
        ScanFolderButton.Click();
    }

    public void ClickCancelScan()
    {
        CancelScanButton.Click();
    }

    public void SetFolderPath(string path)
    {
        FolderPathTextBox.Clear();
        FolderPathTextBox.SendKeys(path);
    }

    public string GetFolderPath()
    {
        return FolderPathTextBox.Text;
    }

    public string GetStatusMessage()
    {
        return StatusMessageText.Text;
    }

    public WindowsElement GetProgressBar()
    {
        return ScanProgressBar;
    }

    public double GetProgressPercentage()
    {
        var valueStr = ScanProgressBar.GetAttribute("Value.Value");
        return double.Parse(valueStr);
    }

    public int GetVideoFileCount()
    {
        // Get count from list
        var items = VideoFilesList.FindElementsByClassName("ListViewItem");
        return items.Count;
    }

    public bool IsScanningInProgress()
    {
        try
        {
            return ScanProgressBar.Displayed &&
                   GetProgressPercentage() < 100;
        }
        catch
        {
            return false;
        }
    }

    // Wait Methods
    public void WaitForScanToComplete(int timeoutSeconds = 60)
    {
        var wait = new SmartWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));

        wait.Until(driver =>
        {
            var status = GetStatusMessage();
            return status.Contains("Found") ||
                   status.Contains("completed") ||
                   status.Contains("files");
        });
    }

    public void WaitForProgressBarToAppear()
    {
        _wait.WaitForElement(
            By.AccessibilityId("ScanProgressBar"),
            "Scan Progress Bar");
    }

    // Assertions
    public void AssertStatusContains(string expectedText)
    {
        var status = GetStatusMessage();
        status.Should().Contain(expectedText,
            $"Status message should contain '{expectedText}'");
    }

    public void AssertProgressBarVisible()
    {
        ScanProgressBar.Displayed.Should().BeTrue(
            "Progress bar should be visible during scan");
    }
}
```

---

## Setup Instructions

### 1. Unit Test Setup (Already in place)

Your project already has excellent unit test infrastructure:

```xml
<!-- AlUmmahNow.Tests.csproj - Already configured -->
<ItemGroup>
  <PackageReference Include="xunit" Version="2.9.2" />
  <PackageReference Include="Moq" Version="4.20.72" />
  <PackageReference Include="FluentAssertions" Version="6.12.1" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.9" />
</ItemGroup>
```

**Next Steps:**
1. Create `AlUmmahNow.Tests/Unit/ViewModels/` directory
2. Add ViewModel test files (examples provided above)
3. Run tests: `dotnet test --filter "Category=Unit"`

---

### 2. WinAppDriver Setup (New - E2E Tests)

#### Installation:

1. **Download WinAppDriver:**
   ```powershell
   # Download from GitHub
   Start-Process "https://github.com/Microsoft/WinAppDriver/releases/latest"
   # Install WinAppDriver.msi
   ```

2. **Enable Windows Developer Mode:**
   ```powershell
   # Open Settings
   Start-Process ms-settings:developers
   # Enable "Developer Mode"
   ```

3. **Start WinAppDriver:**
   ```powershell
   # Run WinAppDriver (will listen on http://127.0.0.1:4723)
   "C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"
   ```

#### Project Configuration:

Create new test project for E2E tests:

```bash
cd /home/meywd/AlUmmahNowTools
dotnet new mstest -n AlUmmahNow.Tests.E2E
cd AlUmmahNow.Tests.E2E
```

```xml
<!-- AlUmmahNow.Tests.E2E.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.6.0" />
    <PackageReference Include="MSTest.TestFramework" Version="3.6.0" />
    <PackageReference Include="Appium.WebDriver" Version="5.0.0-rc.1" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AlUmmahNow.VisualImporterUno\AlUmmahNow.VisualImporterUno\AlUmmahNow.VisualImporterUno.csproj" />
  </ItemGroup>
</Project>
```

#### Base Test Class:

```csharp
// E2ETestBase.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using System;
using System.IO;

namespace AlUmmahNow.Tests.E2E;

public abstract class E2ETestBase
{
    protected WindowsDriver<WindowsElement> Driver { get; private set; }
    private string _testOutputFolder;

    [TestInitialize]
    public virtual void Setup()
    {
        // Setup test output folder
        _testOutputFolder = Path.Combine(
            Path.GetTempPath(),
            $"AlUmmahNowE2E_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(_testOutputFolder);

        // Configure Appium options
        var options = new AppiumOptions();
        options.AddAdditionalCapability("app", GetApplicationPath());
        options.AddAdditionalCapability("deviceName", "WindowsPC");
        options.AddAdditionalCapability("platformName", "Windows");

        // Launch app
        Driver = new WindowsDriver<WindowsElement>(
            new Uri("http://127.0.0.1:4723"),
            options,
            TimeSpan.FromSeconds(60));

        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
    }

    [TestCleanup]
    public virtual void Cleanup()
    {
        // Take screenshot on failure
        if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed)
        {
            TakeScreenshot(TestContext.TestName);
        }

        // Close app
        Driver?.Quit();

        // Cleanup test output
        if (Directory.Exists(_testOutputFolder))
        {
            try
            {
                Directory.Delete(_testOutputFolder, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    protected string GetApplicationPath()
    {
        var projectDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        var appPath = Path.Combine(
            projectDir,
            "AlUmmahNow.VisualImporterUno",
            "AlUmmahNow.VisualImporterUno",
            "bin",
            "Debug",
            "net9.0-windows10.0.26100",
            "AlUmmahNow.VisualImporterUno.exe");

        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException(
                $"Application not found. Please build the project first. Path: {appPath}");
        }

        return appPath;
    }

    protected void TakeScreenshot(string testName)
    {
        try
        {
            var screenshot = Driver.GetScreenshot();
            var filename = $"{testName}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var path = Path.Combine(_testOutputFolder, filename);
            screenshot.SaveAsFile(path);
            TestContext.WriteLine($"Screenshot saved: {path}");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Failed to take screenshot: {ex.Message}");
        }
    }

    public TestContext TestContext { get; set; }
}
```

---

### 3. Running Tests

#### Run All Tests:
```bash
# From solution root
dotnet test

# With detailed output
dotnet test --logger "console;verbosity=detailed"
```

#### Run Specific Categories:
```bash
# Unit tests only
dotnet test --filter "Category=Unit"

# E2E tests only
dotnet test --filter "Category=E2E"

# Critical tests only
dotnet test --filter "Category=Critical"
```

#### Run with Coverage:
```bash
# Generate coverage report
dotnet test --collect:"XPlat Code Coverage"

# View coverage report
reportgenerator \
  -reports:"**/coverage.cobertura.xml" \
  -targetdir:"coveragereport" \
  -reporttypes:Html

# Open report
start coveragereport/index.html
```

---

### 4. CI/CD Integration

#### GitHub Actions Example:

```yaml
# .github/workflows/test.yml
name: Test Suite

on:
  push:
    branches: [ master, develop ]
  pull_request:
    branches: [ master ]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Run Unit Tests
        run: dotnet test --filter "Category=Unit" --logger "trx;LogFileName=unit-test-results.trx"

      - name: Publish Test Results
        uses: EnricoMi/publish-unit-test-result-action@v2
        if: always()
        with:
          files: '**/unit-test-results.trx'

  e2e-tests:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'

      - name: Install WinAppDriver
        run: |
          Invoke-WebRequest -Uri "https://github.com/microsoft/WinAppDriver/releases/download/v1.2.99/WindowsApplicationDriver.msi" -OutFile WinAppDriver.msi
          Start-Process msiexec.exe -ArgumentList '/i', 'WinAppDriver.msi', '/quiet', '/norestart' -Wait

      - name: Enable Developer Mode
        run: |
          reg add "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /f /v "AllowDevelopmentWithoutDevLicense" /d "1"

      - name: Start WinAppDriver
        run: |
          Start-Process "C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"
          Start-Sleep -Seconds 5

      - name: Build Application
        run: dotnet build --configuration Release

      - name: Run E2E Tests
        run: dotnet test --filter "Category=E2E&Category=Critical" --logger "trx;LogFileName=e2e-test-results.trx"

      - name: Upload Screenshots
        if: failure()
        uses: actions/upload-artifact@v3
        with:
          name: test-screenshots
          path: '**/screenshots/**/*.png'

      - name: Publish Test Results
        uses: EnricoMi/publish-unit-test-result-action@v2
        if: always()
        with:
          files: '**/e2e-test-results.trx'
```

---

## Summary & Next Steps

### Summary

This guide provides a comprehensive testing strategy for the AlUmmahNow.VisualImporterUno project:

1. **Multi-Layered Approach:** 80% unit tests, 15% integration tests, 5% E2E tests
2. **Framework Selection:** xUnit + Moq for unit tests, WinAppDriver for E2E tests
3. **MVVM Testing:** Comprehensive ViewModel testing examples
4. **Best Practices:** Page Object Model, async handling, reliable locators
5. **Real-World Examples:** Based on actual codebase analysis

### Recommended Next Steps

**Week 1-2: Foundation**
- [ ] Create `AlUmmahNow.Tests/Unit/ViewModels/` directory structure
- [ ] Implement MainViewModel unit tests (start with critical paths)
- [ ] Add SettingsViewModel unit tests
- [ ] Achieve 50% ViewModel code coverage

**Week 3-4: Expansion**
- [ ] Complete all ViewModel unit tests
- [ ] Add VideoFileInfo model tests
- [ ] Implement helper extensions for testing
- [ ] Achieve 80% ViewModel code coverage

**Week 5: E2E Setup**
- [ ] Install and configure WinAppDriver
- [ ] Create E2E test project
- [ ] Implement base test class and Page Objects
- [ ] Add 1-2 critical E2E test scenarios

**Week 6: CI/CD**
- [ ] Configure GitHub Actions workflow
- [ ] Add automated test execution
- [ ] Setup test reporting
- [ ] Configure code coverage tracking

**Week 7: Refinement**
- [ ] Review test results and coverage gaps
- [ ] Optimize slow tests
- [ ] Add visual regression testing
- [ ] Document testing guidelines for team

---

## Additional Resources

### Official Documentation
- [WinAppDriver GitHub](https://github.com/Microsoft/WinAppDriver)
- [Appium Documentation](http://appium.io/docs/en/about-appium/intro/)
- [Uno Platform Testing Guide](https://platform.uno/docs/articles/features/testing.html)
- [xUnit Documentation](https://xunit.net/)
- [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- [FluentAssertions](https://fluentassertions.com/)

### Community Resources
- [Uno Platform Discord](https://discord.gg/eBHZSKG)
- [Selenium C# Tutorial](https://www.selenium.dev/documentation/webdriver/)
- [Microsoft UI Testing Blog](https://devblogs.microsoft.com/dotnet/)

### Sample Projects
- [WinAppDriver Test Samples](https://github.com/Microsoft/WinAppDriver/tree/master/Samples)
- [Uno Platform Samples](https://github.com/unoplatform/Uno.Samples)

---

## Appendix: XAML Automation Properties Reference

### Essential AutomationProperties

```xml
<!-- Button with automation support -->
<Button x:Name="SaveButton"
        AutomationProperties.AutomationId="SaveButton"
        AutomationProperties.Name="Save Settings"
        AutomationProperties.HelpText="Saves the current settings"
        Content="Save" />

<!-- TextBox with automation support -->
<TextBox x:Name="UrlTextBox"
         AutomationProperties.AutomationId="ServiceUrlTextBox"
         AutomationProperties.Name="Service URL"
         AutomationProperties.LabeledBy="{Binding ElementName=UrlLabel}"
         Text="{Binding ServiceUrl, Mode=TwoWay}" />

<!-- ListView with automation support -->
<ListView x:Name="FilesList"
          AutomationProperties.AutomationId="VideoFilesList"
          AutomationProperties.Name="Video Files"
          AutomationProperties.ItemType="Video File"
          ItemsSource="{Binding FilteredVideoFiles}">
    <ListView.ItemTemplate>
        <DataTemplate>
            <Grid AutomationProperties.Name="{Binding FileName}">
                <!-- Item content -->
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>

<!-- ProgressBar with automation support -->
<ProgressBar x:Name="ScanProgress"
             AutomationProperties.AutomationId="ScanProgressBar"
             AutomationProperties.Name="Scan Progress"
             AutomationProperties.LiveSetting="Polite"
             Value="{Binding ProgressPercentage}"
             Maximum="100" />
```

### Recommended Pattern for All Interactive Controls

1. **AutomationProperties.AutomationId:** Unique, stable identifier (never localized)
2. **AutomationProperties.Name:** Human-readable name (can be localized)
3. **AutomationProperties.HelpText:** Longer description for screen readers
4. **AutomationProperties.LabeledBy:** Link to associated label element

---

**End of Guide**

For questions or contributions to this guide, please contact the AlUmmahNow development team.

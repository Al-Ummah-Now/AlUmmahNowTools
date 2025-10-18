# AlUmmahNow Visual Importer - UI Test Suite

Comprehensive end-to-end test suite for the AlUmmahNow Visual Importer application using Uno.UITest framework.

## Overview

This test suite provides automated UI testing for the Visual Importer application across multiple platforms (Windows, Browser, Android, iOS). The tests are organized following best practices with Page Object Model pattern, reusable helpers, and comprehensive test coverage.

## Project Structure

```
AlUmmahNow.VisualImporterUno.UITests/
├── AlUmmahNow.VisualImporterUno.UITests.csproj  # Project file
├── BaseTest.cs                                   # Base test class with common setup/teardown
│
├── PageObjects/                                  # Page Object Models
│   ├── WatchManagementPageObject.cs             # Watch Management page interactions
│   ├── ApprovalWorkflowPageObject.cs            # Approval Workflow page interactions
│   └── DashboardPageObject.cs                   # Dashboard/Navigation interactions
│
├── Tests/                                        # Test suites
│   ├── WatchManagementTests.cs                  # Watch Management feature tests
│   ├── ApprovalWorkflowTests.cs                 # Approval Workflow feature tests
│   ├── NavigationTests.cs                       # Navigation and app-level tests
│   └── IntegrationTests.cs                      # End-to-end integration tests
│
├── Helpers/                                      # Helper utilities
│   ├── UITestHelpers.cs                         # Common UI interaction helpers
│   ├── ScreenshotHelper.cs                      # Screenshot capture utilities
│   └── TestDataBuilder.cs                       # Test data factories
│
└── README.md                                     # This file
```

## Prerequisites

### Required Software
- .NET 9.0 SDK
- Visual Studio 2022 or JetBrains Rider
- Uno Platform templates (for running WASM tests)
- Windows SDK (for Windows tests)

### NuGet Packages
- `Uno.UITest` (v4.2.25)
- `Uno.UITest.Helpers` (v4.2.25)
- `NUnit` (v4.2.2)
- `NUnit3TestAdapter` (v4.6.0)
- `Selenium.WebDriver` (v4.25.0)

## Running Tests

### Command Line

Run all tests:
```bash
dotnet test
```

Run specific test category:
```bash
dotnet test --filter "Category=WatchManagement"
dotnet test --filter "Category=ApprovalWorkflow"
dotnet test --filter "Category=Navigation"
dotnet test --filter "Category=Integration"
```

Run tests excluding slow or destructive tests:
```bash
dotnet test --filter "Category!=Slow&Category!=Destructive"
```

### Visual Studio / Rider

1. Open Test Explorer
2. Run all tests or select specific tests/categories
3. View results and screenshots in test output

### Platform-Specific Testing

Set environment variable to target specific platform:

**Windows:**
```bash
set UNO_UITEST_PLATFORM=Windows
dotnet test
```

**Browser/WASM:**
```bash
set UNO_UITEST_PLATFORM=Browser
dotnet test
```

**Android:**
```bash
set UNO_UITEST_PLATFORM=Android
dotnet test
```

**iOS:**
```bash
set UNO_UITEST_PLATFORM=iOS
dotnet test
```

## Test Categories

### WatchManagement
Tests for Watch Management feature:
- Creating watches with validation
- Batch watch creation
- Watch list management
- Starting/stopping watches
- Pending approvals view

### ApprovalWorkflow
Tests for Approval Workflow feature:
- File filtering (channel, priority, size, date, extension)
- Filter presets (save, load, delete, set default)
- Sorting functionality
- Bulk operations (approve/reject)
- File preview with metadata
- Activity logging

### Navigation
Tests for application navigation:
- Menu navigation between pages
- Page load verification
- State preservation
- Navigation performance

### Integration
End-to-end tests covering complete workflows:
- Watch creation to file approval workflow
- Filter preset workflows
- Cross-feature interactions
- Error recovery scenarios

### Special Categories
- **Slow**: Tests that take longer to execute
- **Destructive**: Tests that modify data (approve/reject files)
- **Performance**: Tests validating performance metrics
- **E2E**: End-to-end user journey tests

## Test Patterns

### Page Object Model

Each page has a corresponding Page Object class that encapsulates:
- Element identifiers (as constants)
- Interaction methods (tap, enter text, etc.)
- Verification methods (assert element displayed, etc.)
- Navigation methods

Example:
```csharp
var watchPage = new WatchManagementPageObject(App, Helpers);
watchPage.OpenCreateWatchDialog();
watchPage.FillCreateWatchForm(watchData);
watchPage.SubmitCreateWatch();
```

### Test Data Builders

Fluent builders for creating test data:

```csharp
var watchData = TestDataBuilder.Watch()
    .WithChannelName("Test Channel")
    .WithProcessingDelay(5)
    .Complete()
    .Build();
```

### AutoRetry Attribute

Tests use `[AutoRetry]` attribute for resilience against flaky tests:
```csharp
[Test]
[AutoRetry]
public void Given_When_Then() { }
```

### Screenshot Capture

Screenshots are automatically captured:
- On test failure
- Explicitly via `ScreenshotHelper.CaptureScreenshot()`
- Step-by-step via `ScreenshotHelper.CaptureStepScreenshot()`

Screenshots are organized by:
- Test run timestamp
- Test class name
- Test name
- Step description

## Best Practices

### Test Naming
Follow Given_When_Then pattern:
```csharp
Given_ValidWatchData_When_CreateWatch_Then_WatchIsCreatedSuccessfully()
```

### Test Structure
1. **Arrange**: Set up test data and initial state
2. **Act**: Perform the action being tested
3. **Assert**: Verify expected outcomes

### Wait Strategies
- Use `WaitForElement()` for element presence
- Use `WaitForLoadingComplete()` for async operations
- Use `WaitForElementToDisappear()` for element removal
- Avoid `Thread.Sleep()` except for animations

### Element Identification
Elements should have `x:Name` or `AutomationProperties.AutomationId`:
```xml
<Button x:Name="CreateWatchButton"
        AutomationProperties.AutomationId="CreateWatchButton"
        Content="Create Watch"/>
```

### Test Independence
- Each test should be independent
- Use `CleanupTestData()` to clean up after tests
- Don't rely on test execution order

## Configuration

### App Paths
Update paths in `BaseTest.GetWindowsAppPath()` to match your build output:
```csharp
protected virtual string GetWindowsAppPath()
{
    return Path.Combine(projectDir, "bin", "Debug", ..., "App.exe");
}
```

### Timeouts
Adjust timeouts in `UITestHelpers`:
```csharp
private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
private readonly TimeSpan _shortTimeout = TimeSpan.FromSeconds(10);
private readonly TimeSpan _longTimeout = TimeSpan.FromSeconds(60);
```

## Troubleshooting

### Tests Fail to Start
- Verify application path is correct
- Ensure application builds successfully
- Check that required services are running

### Element Not Found
- Verify `x:Name` or `AutomationId` is set in XAML
- Check that element is visible and enabled
- Increase wait timeout if element loads slowly

### Flaky Tests
- Add `[AutoRetry]` attribute
- Increase wait timeouts
- Check for race conditions
- Review test for proper wait strategies

### Screenshot Issues
- Ensure Screenshots directory has write permissions
- Check disk space
- Verify `TestContext` is accessible

## Continuous Integration

### Azure DevOps Pipeline
```yaml
- task: DotNetCoreCLI@2
  displayName: 'Run UI Tests'
  inputs:
    command: 'test'
    projects: '**/*UITests.csproj'
    arguments: '--filter "Category!=Slow&Category!=Destructive"'
```

### GitHub Actions
```yaml
- name: Run UI Tests
  run: dotnet test --filter "Category!=Slow&Category!=Destructive"
  env:
    UNO_UITEST_PLATFORM: Browser
```

## Maintenance

### Adding New Tests
1. Create test method in appropriate test class
2. Use Page Object methods for interactions
3. Add appropriate attributes (`[Test]`, `[AutoRetry]`, `[Category]`)
4. Follow Given_When_Then naming
5. Add cleanup logic if needed

### Adding New Pages
1. Create Page Object class in `PageObjects/`
2. Define element identifiers as constants
3. Implement interaction methods
4. Add navigation methods
5. Create corresponding test class in `Tests/`

### Updating for UI Changes
1. Update element identifiers in Page Object
2. Update XAML to add missing `x:Name` attributes
3. Update interaction methods if needed
4. Run affected tests to verify

## Test Metrics

Recommended metrics to track:
- Test pass rate
- Test execution time
- Code coverage
- Number of flaky tests
- Screenshot analysis for visual regressions

## Support and Resources

- [Uno Platform Documentation](https://platform.uno/docs/)
- [Uno.UITest Documentation](https://platform.uno/docs/articles/ui-test.html)
- [NUnit Documentation](https://docs.nunit.org/)
- [Project Repository](https://github.com/yourusername/alummahnow)

## Contributing

When contributing tests:
1. Follow existing patterns and conventions
2. Add appropriate documentation
3. Ensure tests are independent and reliable
4. Add meaningful assertions and logging
5. Update this README if adding new patterns

## License

[Your License Here]

## Authors

Generated with Claude Code - Anthropic's official CLI for Claude

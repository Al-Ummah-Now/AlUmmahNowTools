# Testing Implementation Plan
## AlUmmahNow.VisualImporterUno - 7-Week Rollout

---

## Quick Reference

**Main Guide:** [XAML_UI_TEST_AUTOMATION_GUIDE.md](./XAML_UI_TEST_AUTOMATION_GUIDE.md)

**Current Status:**
- ✅ Service layer tests: **Good coverage**
- ⚠️ ViewModel tests: **Not implemented**
- ❌ E2E UI tests: **Not implemented**

**Target Metrics:**
- Unit Test Coverage: 80% (ViewModels + Models)
- Integration Test Coverage: 15% (ViewModel-Service integration)
- E2E Test Coverage: 5% (Critical user paths)

---

## Week 1-2: ViewModel Unit Tests Foundation

### Priority 1: MainViewModel Tests

**File:** `AlUmmahNow.Tests/Unit/ViewModels/MainViewModelTests.cs`

**Test Coverage:**
```csharp
✓ Property Change Notifications (INotifyPropertyChanged)
  - SelectedFolderPath
  - StatusMessage
  - ProgressPercentage
  - IsImporting
  - IsScanning
  - HasScannedFiles

✓ Command CanExecute Logic
  - CanScanFolder (various states)
  - CanStartImport (various states)
  - SelectAllFiles
  - DeselectAllFiles

✓ Collection Management
  - VideoFiles population
  - FilteredVideoFiles updates
  - AvailableChannels updates

✓ Filtering & Sorting
  - HideDuplicates toggle
  - ShowOnlySelected toggle
  - CurrentSortOption changes
  - CurrentSortDirection changes
```

**Estimated Effort:** 12-16 hours
**Lines of Code:** ~800-1000 test lines

### Priority 2: SettingsViewModel Tests

**File:** `AlUmmahNow.Tests/Unit/ViewModels/SettingsViewModelTests.cs`

**Test Coverage:**
```csharp
✓ Settings Loading
  - Load from configuration
  - Load from ApplicationData
  - Fallback to defaults

✓ Settings Persistence
  - Save to ApplicationData
  - Fallback to JSON
  - Error handling

✓ Service Path Detection
  - Detect valid paths
  - Handle missing paths
  - Path selection

✓ Property Validation
  - URL format validation
  - Path validation
  - Test connection logic
```

**Estimated Effort:** 6-8 hours
**Lines of Code:** ~400-500 test lines

### Priority 3: VideoFileInfo Model Tests

**File:** `AlUmmahNow.Tests/Unit/Models/VideoFileInfoTests.cs`

**Test Coverage:**
```csharp
✓ Property Calculations
  - FileSizeFormatted
  - FileNameWithoutExtension
  - ChannelName (with/without override)

✓ Status Calculations
  - StatusText
  - StatusColor
  - StatusIcon

✓ Property Change Propagation
  - ImportStatus changes
  - Dependent property updates
```

**Estimated Effort:** 4-6 hours
**Lines of Code:** ~300-400 test lines

### Deliverables Week 1-2:
- [ ] MainViewModel unit tests (50+ test methods)
- [ ] SettingsViewModel unit tests (20+ test methods)
- [ ] VideoFileInfo model tests (15+ test methods)
- [ ] Helper classes for testing (PropertyChangedTestExtensions)
- [ ] Achieve 50-60% ViewModel code coverage

---

## Week 3-4: Complete ViewModel Coverage

### Priority 1: ServiceStatusViewModel Tests

**File:** `AlUmmahNow.Tests/Unit/ViewModels/ServiceStatusViewModelTests.cs`

**Test Coverage:**
```csharp
✓ SignalR Event Handling
  - OnServiceStatusChanged
  - OnScanProgressReceived
  - OnScanStarted
  - OnScanCompleted
  - OnScanFailed

✓ Service Lifecycle
  - StartServiceCommand
  - StopServiceCommand
  - ConnectToServiceCommand
  - DisconnectFromServiceCommand

✓ Status Tracking
  - ServiceStatus property
  - ServiceHealthStatus property
  - OngoingOperations collection
  - Errors collection

✓ Command Availability
  - CanStartService
  - CanStopService
```

**Estimated Effort:** 10-12 hours
**Lines of Code:** ~600-700 test lines

### Priority 2: DatabaseMainViewModel Tests

**File:** `AlUmmahNow.Tests/Unit/ViewModels/DatabaseMainViewModelTests.cs`

**Test Coverage:**
```csharp
✓ Pagination Logic
  - NextPageCommand
  - PreviousPageCommand
  - CurrentPage updates

✓ Session Management
  - LoadScanSessionsAsync
  - RefreshScanSessionsAsync
  - SelectedScanSession changes

✓ Channel Management
  - LoadChannelsAsync
  - LoadChannelFilesAsync
  - Channel filtering

✓ Scan Operations
  - StartScanAsync
  - CancelScanAsync
  - Progress tracking
```

**Estimated Effort:** 8-10 hours
**Lines of Code:** ~500-600 test lines

### Priority 3: Integration Tests

**File:** `AlUmmahNow.Tests/Integration/ViewModelIntegrationTests.cs`

**Test Scenarios:**
```csharp
✓ MainViewModel + ImportService
  - End-to-end scan workflow
  - End-to-end import workflow

✓ ServiceStatusViewModel + SignalRClient
  - Connection lifecycle
  - Event propagation

✓ SettingsViewModel + Configuration
  - Settings persistence
  - Configuration loading
```

**Estimated Effort:** 8-10 hours
**Lines of Code:** ~400-500 test lines

### Deliverables Week 3-4:
- [ ] ServiceStatusViewModel unit tests (40+ test methods)
- [ ] DatabaseMainViewModel unit tests (30+ test methods)
- [ ] Integration tests (10+ test scenarios)
- [ ] Mock service implementations
- [ ] Achieve 80%+ ViewModel code coverage

---

## Week 5: E2E Test Infrastructure

### Task 1: WinAppDriver Setup

**Steps:**
1. Install WinAppDriver
2. Enable Windows Developer Mode
3. Configure test project
4. Create base test classes

**Files to Create:**
```
AlUmmahNow.Tests.E2E/
├── AlUmmahNow.Tests.E2E.csproj
├── BaseClasses/
│   └── E2ETestBase.cs
├── PageObjects/
│   ├── MainViewPage.cs
│   ├── SettingsViewPage.cs
│   └── ServiceStatusPage.cs
└── Helpers/
    ├── SmartWait.cs
    └── RetryHelper.cs
```

**Estimated Effort:** 6-8 hours

### Task 2: Page Object Implementation

**MainViewPage.cs:**
```csharp
✓ Element Locators (AutomationId-based)
  - SelectFolderButton
  - ScanFolderButton
  - FolderPathTextBox
  - StatusMessageText
  - ScanProgressBar
  - VideoFilesList

✓ Actions
  - ClickSelectFolder()
  - ClickScanFolder()
  - SetFolderPath(string)
  - GetStatusMessage()
  - GetProgressPercentage()

✓ Wait Methods
  - WaitForScanToComplete()
  - WaitForProgressBarToAppear()
```

**Estimated Effort:** 6-8 hours per page object

### Deliverables Week 5:
- [ ] E2E test project created
- [ ] WinAppDriver configured
- [ ] Base test class (E2ETestBase)
- [ ] 3 Page Objects (Main, Settings, ServiceStatus)
- [ ] Helper utilities (SmartWait, RetryHelper)

---

## Week 6: E2E Test Scenarios

### Critical Test Scenarios

**File:** `AlUmmahNow.Tests.E2E/Scenarios/FolderScanWorkflowTests.cs`

```csharp
[TestMethod]
[TestCategory("E2E")]
[TestCategory("Critical")]
public void FolderScan_CompleteWorkflow_Success()
{
    // Happy path: Select folder → Scan → View results
}

[TestMethod]
[TestCategory("E2E")]
[TestCategory("Critical")]
public void Import_CompleteWorkflow_Success()
{
    // Happy path: Scan → Select files → Import → Verify
}

[TestMethod]
[TestCategory("E2E")]
public void FolderScan_WithEmptyFolder_DisplaysNoFiles()
{
    // Edge case: Empty folder handling
}

[TestMethod]
[TestCategory("E2E")]
public void FolderScan_CanBeCancelled()
{
    // User cancellation scenario
}

[TestMethod]
[TestCategory("E2E")]
public void Settings_SaveAndRestore_Persists()
{
    // Settings persistence workflow
}
```

**Estimated Effort:** 12-16 hours
**Test Count:** 5-8 critical scenarios

### Visual Regression Tests (Optional)

**File:** `AlUmmahNow.Tests.E2E/Visual/VisualRegressionTests.cs`

```csharp
[TestMethod]
public void MainWindow_DefaultState_MatchesBaseline()
{
    // Screenshot comparison
}
```

**Estimated Effort:** 4-6 hours

### Deliverables Week 6:
- [ ] 5-8 critical E2E test scenarios
- [ ] Test data management
- [ ] Screenshot comparison (optional)
- [ ] Error handling and cleanup

---

## Week 7: CI/CD Integration & Documentation

### Task 1: GitHub Actions Configuration

**File:** `.github/workflows/test.yml`

```yaml
✓ Unit Test Job
  - Run on Ubuntu (fast)
  - Execute all unit tests
  - Generate coverage report
  - Publish results

✓ E2E Test Job
  - Run on Windows (WinAppDriver required)
  - Install WinAppDriver
  - Build application
  - Execute critical E2E tests
  - Upload screenshots on failure
```

**Estimated Effort:** 4-6 hours

### Task 2: Test Documentation

**Files to Create/Update:**
```
docs/
├── TESTING_GUIDE.md (Quick reference)
├── RUNNING_TESTS.md (Developer guide)
└── CONTRIBUTING_TESTS.md (How to add tests)
```

**Estimated Effort:** 4-6 hours

### Task 3: Code Coverage Dashboard

**Setup:**
1. Configure Codecov or Coveralls
2. Add coverage badges to README
3. Setup coverage thresholds
4. Configure PR coverage checks

**Estimated Effort:** 2-4 hours

### Deliverables Week 7:
- [ ] GitHub Actions workflows
- [ ] Test documentation
- [ ] Code coverage dashboard
- [ ] Coverage badges
- [ ] Team training/handoff

---

## Success Metrics

### Code Coverage Targets

```
Overall Project Coverage: 70%+
├── ViewModels: 80%+
├── Models: 80%+
├── Services: 70%+ (already good)
└── UI Code: 5% (E2E only)
```

### Test Count Targets

```
Total Tests: 150+
├── Unit Tests: 120+
│   ├── ViewModel tests: 100+
│   └── Model tests: 20+
├── Integration Tests: 20+
└── E2E Tests: 10+
```

### Performance Targets

```
Unit Tests: < 30 seconds
Integration Tests: < 2 minutes
E2E Tests: < 5 minutes
Full Suite: < 10 minutes
```

---

## Common Challenges & Solutions

### Challenge 1: Async Testing

**Problem:** ViewModels have heavy async operations
**Solution:** Use `async Task` test methods with proper awaiting

```csharp
[Fact]
public async Task ScanFolderAsync_WithValidPath_CompletesSuccessfully()
{
    // Arrange
    _viewModel.SelectedFolderPath = @"C:\TestVideos";

    // Act
    await _viewModel.ScanFolderCommand.ExecuteAsync(null);

    // Assert
    _viewModel.HasScannedFiles.Should().BeTrue();
}
```

### Challenge 2: DispatcherQueue Dependencies

**Problem:** MainViewModel heavily uses DispatcherQueue
**Solution:** Extract `IDispatcherService` abstraction

```csharp
public interface IDispatcherService
{
    bool HasThreadAccess { get; }
    void Execute(Action action);
}

// Mock for tests
public class MockDispatcherService : IDispatcherService
{
    public bool HasThreadAccess => true;
    public void Execute(Action action) => action();
}
```

### Challenge 3: File System Dependencies

**Problem:** File pickers and folder scanning depend on OS
**Solution:** Use abstractions and test doubles

```csharp
public interface IFilePickerService
{
    Task<string?> PickFolderAsync();
}

// Mock for tests
public class MockFilePickerService : IFilePickerService
{
    public string? FolderToReturn { get; set; }
    public Task<string?> PickFolderAsync() =>
        Task.FromResult(FolderToReturn);
}
```

### Challenge 4: SignalR Testing

**Problem:** Real-time SignalR events are hard to test
**Solution:** Use Moq with event raising

```csharp
[Fact]
public void ServiceStatusChanged_UpdatesUI()
{
    // Arrange
    var mockSignalRClient = new Mock<FileScanSignalRClient>();
    var viewModel = new ServiceStatusViewModel(mockSignalRClient.Object, ...);

    // Act
    mockSignalRClient.Raise(
        c => c.ServiceStatusChanged += null,
        new ServiceStatusEventArgs { Status = ServiceStatus.Connected });

    // Assert
    viewModel.ServiceStatus.Should().Be(ServiceStatus.Connected);
}
```

---

## Resource Requirements

### Development Time

```
Total Estimated Time: 60-80 hours
├── Week 1-2: 16-24 hours (ViewModel tests)
├── Week 3-4: 16-24 hours (Complete coverage)
├── Week 5: 12-16 hours (E2E infrastructure)
├── Week 6: 12-16 hours (E2E scenarios)
└── Week 7: 4-8 hours (CI/CD + docs)
```

### Infrastructure

```
Development Machine:
├── Windows 10/11 with Developer Mode
├── Visual Studio 2022 or VS Code
├── .NET 9.0 SDK
└── WinAppDriver installed

CI/CD:
├── GitHub Actions (free for public repos)
├── Windows runner (for E2E tests)
└── Ubuntu runner (for unit tests)
```

### Skills Required

```
Essential:
├── C# and .NET testing fundamentals
├── xUnit/MSTest framework knowledge
├── Moq mocking library
└── MVVM pattern understanding

Nice to Have:
├── WinAppDriver/Selenium experience
├── Page Object Model pattern
├── GitHub Actions YAML
└── Code coverage tools
```

---

## Getting Started Checklist

### Prerequisites
- [ ] Review main guide: [XAML_UI_TEST_AUTOMATION_GUIDE.md](./XAML_UI_TEST_AUTOMATION_GUIDE.md)
- [ ] Ensure .NET 9.0 SDK installed
- [ ] Verify existing test project structure
- [ ] Understand current ViewModel architecture

### Week 1 Setup
- [ ] Create `Unit/ViewModels/` directory structure
- [ ] Add necessary NuGet packages (if missing)
- [ ] Create first test file: `MainViewModelTests.cs`
- [ ] Write 5 simple property tests
- [ ] Run tests: `dotnet test --filter Category=Unit`
- [ ] Commit initial test structure

### Quick Win Tests (First Day)
```csharp
// Start with these simple tests for immediate value

[Fact]
public void Constructor_SetsDefaultProperties()
{
    var viewModel = new MainViewModel(_mockImportService.Object);
    viewModel.StatusMessage.Should().Be("Ready to import");
    viewModel.IsImporting.Should().BeFalse();
}

[Fact]
public void SelectedFolderPath_WhenSet_RaisesPropertyChanged()
{
    var changed = false;
    _viewModel.PropertyChanged += (s, e) => changed = true;
    _viewModel.SelectedFolderPath = @"C:\Test";
    changed.Should().BeTrue();
}

[Fact]
public void CanScanFolder_WithEmptyPath_ReturnsFalse()
{
    _viewModel.SelectedFolderPath = "";
    _viewModel.CanScanFolder.Should().BeFalse();
}
```

---

## Support & Resources

### Documentation
- Main Guide: `XAML_UI_TEST_AUTOMATION_GUIDE.md`
- Implementation Plan: This file
- Existing Tests: `AlUmmahNow.Tests/` directory

### External Resources
- [xUnit Documentation](https://xunit.net/)
- [Moq GitHub](https://github.com/moq/moq4)
- [FluentAssertions](https://fluentassertions.com/)
- [WinAppDriver](https://github.com/Microsoft/WinAppDriver)

### Team Communication
- Questions: Create GitHub Issues with `testing` label
- Progress: Update this plan with checkmarks
- Blockers: Tag team lead in Slack/Discord

---

## Progress Tracking

### Week 1-2: ViewModel Unit Tests Foundation
- [ ] MainViewModel tests started
- [ ] SettingsViewModel tests started
- [ ] VideoFileInfo model tests completed
- [ ] 50% ViewModel coverage achieved

### Week 3-4: Complete ViewModel Coverage
- [ ] ServiceStatusViewModel tests completed
- [ ] DatabaseMainViewModel tests completed
- [ ] Integration tests added
- [ ] 80% ViewModel coverage achieved

### Week 5: E2E Infrastructure
- [ ] WinAppDriver installed and configured
- [ ] E2E test project created
- [ ] Page Objects implemented
- [ ] Helper utilities created

### Week 6: E2E Scenarios
- [ ] Critical test scenarios implemented
- [ ] Test data management set up
- [ ] Screenshots configured
- [ ] 5+ E2E tests passing

### Week 7: CI/CD & Documentation
- [ ] GitHub Actions configured
- [ ] Tests running in CI
- [ ] Documentation completed
- [ ] Team training conducted

---

**Last Updated:** 2025-10-12
**Next Review:** End of Week 2 (Check coverage metrics)

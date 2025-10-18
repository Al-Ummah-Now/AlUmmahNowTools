# VisualImporterUno ViewModels Integration Tests

## Overview

This directory contains comprehensive integration tests for the WatchViewModel and ApprovalViewModel from the Uno Platform-based VisualImporterUno application.

## Test Files

1. **WatchViewModelTests.cs** - Tests for watch management and approval workflow
2. **ApprovalViewModelTests.cs** - Tests for approval workflow, filtering, and bulk operations

## Setup Required

These tests require additional project references and dependencies that are not currently configured in the main test project. To enable these tests, you need to:

### 1. Add Project Reference

Add a reference to the VisualImporterUno project in `AlUmmahNow.Tests.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\AlUmmahNow.VisualImporterUno\AlUmmahNow.VisualImporterUno\AlUmmahNow.VisualImporterUno.csproj" />
</ItemGroup>
```

### 2. Add Uno Platform Dependencies

Since the view models use Uno Platform-specific components (like `Microsoft.UI.Dispatching.DispatcherQueue`), you'll need to add Uno Platform packages:

```xml
<ItemGroup>
  <PackageReference Include="Uno.WinUI" Version="5.x.x" />
  <PackageReference Include="Uno.WinUI.RemoteControl" Version="5.x.x" Condition="'$(Configuration)'=='Debug'" />
</ItemGroup>
```

**Note**: The exact version should match what's used in the VisualImporterUno project.

### 3. Alternative Approach: Mock-Based Testing

If you prefer to avoid Uno Platform dependencies in the test project, you can:

1. **Create interfaces** for the view models (`IWatchViewModel`, `IApprovalViewModel`)
2. **Extract business logic** into testable service classes
3. **Keep view models thin** with minimal platform-specific code
4. **Test the services** instead of the view models directly

## Test Coverage

### WatchViewModel Tests (31 test methods)

#### Constructor and Initialization (3 tests)
- ✓ Constructor with valid SignalR client initializes successfully
- ✓ Constructor with null SignalR client throws ArgumentNullException
- ✓ Supported extensions contain common video formats

#### Create Watch Command (10 tests)
- ✓ Open/close create watch dialog functionality
- ✓ Channel name validation (empty, too short, too long, duplicate)
- ✓ Watch path validation (empty, non-existent, duplicate)
- ✓ Create watch with valid inputs
- ✓ Create watch when not connected (error handling)
- ✓ Create watch with invalid delay or no extensions (validation)

#### Watch Management Commands (6 tests)
- ✓ Stop watch command
- ✓ Delete watch command
- ✓ Refresh watches command
- ✓ Error handling for stop/delete operations
- ✓ Command enable/disable states

#### SignalR Event Handling (7 tests)
- ✓ FileDetected event updates statistics
- ✓ WatchStatusChanged event updates watch status
- ✓ WatchStopped event sets watch inactive
- ✓ WatchDeleted event removes watch from collection
- ✓ NewFileForApproval event updates status
- ✓ ApprovalStatusChanged event updates approval status

#### Approval Management (3 tests)
- ✓ Approve/reject selected approval
- ✓ Select/deselect all approvals
- ✓ Toggle approval selection

#### Filtering and Statistics (2 tests)
- ✓ Apply filters by status and channel
- ✓ Update statistics with correct calculations

#### Disposal (2 tests)
- ✓ Dispose unsubscribes from events and clears collections
- ✓ Multiple dispose calls don't throw exceptions

### ApprovalViewModel Tests (29 test methods)

#### Constructor and Initialization (2 tests)
- ✓ Constructor with valid SignalR client initializes successfully
- ✓ Constructor with null SignalR client throws ArgumentNullException

#### Refresh and Loading (3 tests)
- ✓ Refresh approvals loads data and statistics
- ✓ Refresh when not connected throws exception
- ✓ Refresh command disabled when loading

#### Approve/Reject Operations (4 tests)
- ✓ Approve single file with success
- ✓ Reject single file with success
- ✓ Handle approval operation failures
- ✓ Commands disabled with no selection

#### Bulk Approval Operations (4 tests)
- ✓ Bulk approve with channel filter
- ✓ Bulk reject with multiple filters (size, extension, channel)
- ✓ Bulk operations with date range filters
- ✓ Bulk commands disabled without active filters

#### Filtering (7 tests)
- ✓ Filter by status (show only pending)
- ✓ Filter by channel name
- ✓ Filter by priority level
- ✓ Filter by file size range
- ✓ Filter by date range
- ✓ Filter by file extension
- ✓ Clear all filters

#### Sorting (3 tests)
- ✓ Sort by file name (ascending/descending)
- ✓ Sort by file size
- ✓ Sort by priority

#### Multi-Selection (4 tests)
- ✓ Select all visible approvals
- ✓ Deselect all selections
- ✓ Toggle individual selection
- ✓ Approve multiple selected files

#### Activity Log (3 tests)
- ✓ Activity log tracks approval activities
- ✓ Activity log limits to 200 messages
- ✓ Clear activity log

#### Additional Features (3 tests)
- ✓ Preview file sets current approval
- ✓ Load approvals by specific channel
- ✓ Disposal clears all collections

## Testing Approach

### Unit Test Style with Integration Focus

These tests follow a **unit testing style** but are designed as **integration tests** because:

1. **Mock Dependencies**: All external dependencies (SignalR client, DispatcherQueue) are mocked
2. **Test Isolation**: Each test is independent and sets up its own state
3. **Integration Behavior**: Tests verify the interaction between view model, commands, and SignalR events
4. **Real-World Scenarios**: Tests cover actual user workflows and edge cases

### Mocking Strategy

- **FileScanSignalRClient**: Mocked to simulate server responses without actual network calls
- **DispatcherQueue**: Mocked to execute UI thread operations synchronously in tests
- **Test Data**: Created inline for each test to ensure isolation

### Test Patterns Used

1. **AAA Pattern**: Arrange-Act-Assert in every test
2. **FluentAssertions**: For readable assertions
3. **xUnit**: As the test framework (matching existing project patterns)
4. **IAsyncLifetime**: For setup/teardown of test resources
5. **Theory/InlineData**: For parameterized tests (where applicable)

## Running the Tests

Once the setup is complete:

```bash
# Run all VisualImporterUno tests
dotnet test --filter "FullyQualifiedName~AlUmmahNow.Tests.VisualImporterUno"

# Run specific test class
dotnet test --filter "FullyQualifiedName~WatchViewModelTests"
dotnet test --filter "FullyQualifiedName~ApprovalViewModelTests"

# Run with detailed output
dotnet test --filter "FullyQualifiedName~AlUmmahNow.Tests.VisualImporterUno" --logger "console;verbosity=detailed"
```

## Test Design Principles

1. **Fast**: All tests run in milliseconds by mocking expensive operations
2. **Isolated**: Tests don't depend on each other or external state
3. **Repeatable**: Tests produce the same result every time
4. **Self-Validating**: Clear pass/fail with descriptive assertions
5. **Comprehensive**: Cover success paths, error handling, and edge cases

## Known Limitations

1. **Platform Dependencies**: Requires Uno Platform packages or abstraction layer
2. **Async Initialization**: View models initialize asynchronously, requiring delays in some tests
3. **Dispatcher Handling**: Tests mock dispatcher but don't verify actual UI thread behavior
4. **SignalR Events**: Events are triggered manually in tests, not from actual server

## Future Improvements

1. **Interface Extraction**: Create `IWatchViewModel` and `IApprovalViewModel` interfaces
2. **Service Layer**: Extract business logic into testable service classes
3. **UI Testing**: Add Uno.UITest for actual UI interaction tests
4. **Test Helpers**: Create shared test fixtures and builders for common scenarios
5. **Performance Tests**: Add tests for large data sets and stress scenarios

## Related Documentation

- [Watch Management Feature](../../AlUmmahNow.VisualImporterUno/AlUmmahNow.VisualImporterUno/ViewModels/WatchViewModel.cs)
- [Approval Workflow Feature](../../AlUmmahNow.VisualImporterUno/AlUmmahNow.VisualImporterUno/ViewModels/ApprovalViewModel.cs)
- [SignalR Client](../../AlUmmahNow.DataImporter/Services/FileScanSignalRClient.cs)
- [File Scanning Models](../../AlUmmahNow.Data/Models/FileScanning/)

## Contributing

When adding new tests:

1. Follow the existing AAA pattern
2. Use descriptive test names that explain what is being tested
3. Mock all external dependencies
4. Test both success and failure scenarios
5. Keep tests focused on a single behavior
6. Add summary comments for complex test scenarios

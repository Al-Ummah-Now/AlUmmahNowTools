# AlUmmahNow Tools - Testing Guide

## Table of Contents
1. [Current Test Coverage](#current-test-coverage)
2. [Test Infrastructure](#test-infrastructure)
3. [Testing SignalR Communication](#testing-signalr-communication)
4. [Testing Core Functions](#testing-core-functions)
5. [Running Tests](#running-tests)
6. [Coverage Reports](#coverage-reports)
7. [AI Coding Flow Integration](#ai-coding-flow-integration)
8. [Best Practices](#best-practices)

---

## Current Test Coverage

**Overall Coverage**: ~11.9% (709/5957 lines covered)

### Current Test Status
- ✅ **78 tests passing**
- ✅ Test frameworks: xUnit, Moq, FluentAssertions
- ✅ Code coverage collection enabled (Coverlet)
- ⚠️ **Missing coverage areas**:
  - SignalR Hub methods (FileScanHub.cs)
  - SignalR Client (FileScanSignalRClient.cs)
  - FileScanEngine core logic
  - MetadataExtractor
  - FileScanWorker background service

### Existing Test Coverage
- ✅ HashCalculator: `AlUmmahNow.Tests/FileScanService/Services/HashCalculatorBasicTests.cs`
- ✅ Repository: `AlUmmahNow.Tests/Repository/RepositoryTests.cs`
- ✅ Data Models: `AlUmmahNow.Tests/Data/Models/BasicModelTests.cs`
- ✅ Storage Clients: BunnyStorageClient, R2Client tests
- ✅ Video Client: BunnyStreamClient tests

---

## Test Infrastructure

### Dependencies
```xml
<!-- Already configured in AlUmmahNow.Tests.csproj -->
<PackageReference Include="xunit" Version="2.9.2" />
<PackageReference Include="Moq" Version="4.20.72" />
<PackageReference Include="FluentAssertions" Version="6.12.1" />
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="9.0.9" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.9" />
<PackageReference Include="coverlet.collector" Version="6.0.2" />
```

### Test Project Structure
```
AlUmmahNow.Tests/
├── Data/
│   └── Models/
├── FileScanService/
│   ├── Hubs/           # ⚠️ TO CREATE
│   └── Services/
│       └── HashCalculatorBasicTests.cs ✅
├── DataImporter/
│   ├── ImporterTests.cs ✅
│   └── Services/       # ⚠️ TO CREATE
├── Repository/
│   └── RepositoryTests.cs ✅
├── Storage/
│   ├── BunnyStorageClientTests.cs ✅
│   └── R2ClientTests.cs ✅
├── Video/
│   └── BunnyStreamClientTests.cs ✅
└── Infrastructure/
    └── FileSystemTestBase.cs ✅
```

---

## Testing SignalR Communication

### 1. SignalR Hub Testing (Server-Side)

**File**: `AlUmmahNow.Tests/FileScanService/Hubs/FileScanHubTests.cs`

#### Approach: Isolated Unit Testing

```csharp
using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using AlUmmahNow.FileScanService.Hubs;
using AlUmmahNow.Data.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.Tests.FileScanService.Hubs
{
    public class FileScanHubTests : IDisposable
    {
        private readonly ImportDbContext _dbContext;
        private readonly Mock<ILogger<FileScanHub>> _mockLogger;
        private readonly Mock<IHubCallerClients> _mockClients;
        private readonly Mock<HubCallerContext> _mockContext;
        private readonly Mock<IGroupManager> _mockGroups;
        private readonly FileScanHub _hub;

        public FileScanHubTests()
        {
            // Setup in-memory database
            var options = new DbContextOptionsBuilder<ImportDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new ImportDbContext(options);

            // Setup mocks
            _mockLogger = new Mock<ILogger<FileScanHub>>();
            _mockClients = new Mock<IHubCallerClients>();
            _mockContext = new Mock<HubCallerContext>();
            _mockGroups = new Mock<IGroupManager>();

            // Create hub instance
            _hub = new FileScanHub(_mockLogger.Object, _dbContext)
            {
                Clients = _mockClients.Object,
                Context = _mockContext.Object,
                Groups = _mockGroups.Object
            };
        }

        [Fact]
        public async Task OnConnectedAsync_ShouldLogConnection()
        {
            // Arrange
            _mockContext.Setup(c => c.ConnectionId).Returns("test-connection-id");

            // Act
            await _hub.OnConnectedAsync();

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Client connected")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task JoinScanSession_ShouldAddToGroup()
        {
            // Arrange
            var sessionId = "test-session";
            _mockContext.Setup(c => c.ConnectionId).Returns("test-connection");

            // Act
            await _hub.JoinScanSession(sessionId);

            // Assert
            _mockGroups.Verify(
                g => g.AddToGroupAsync("test-connection", $"scan-{sessionId}", default),
                Times.Once);
        }

        [Fact]
        public async Task StartScan_WithValidPath_ReturnsSessionId()
        {
            // Arrange
            var testPath = Path.GetTempPath();
            _mockContext.Setup(c => c.ConnectionId).Returns("test-connection");
            var mockGroupClient = new Mock<IClientProxy>();
            _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroupClient.Object);

            // Act
            var sessionId = await _hub.StartScan(testPath, true);

            // Assert
            sessionId.Should().NotBeEmpty();
            var session = await _dbContext.ScanSessions.FindAsync(sessionId);
            session.Should().NotBeNull();
            session!.RootPath.Should().Be(testPath);
            session.Status.Should().Be(ScanStatus.Pending);
        }

        [Fact]
        public async Task StartScan_WithInvalidPath_ThrowsHubException()
        {
            // Arrange
            var invalidPath = "C:\\NonExistent\\Path";
            _mockContext.Setup(c => c.ConnectionId).Returns("test-connection");

            // Act & Assert
            await _hub.Invoking(h => h.StartScan(invalidPath, true))
                .Should().ThrowAsync<HubException>()
                .WithMessage("*Invalid or non-existent folder path*");
        }

        [Fact]
        public async Task GetScanStatus_WithValidSessionId_ReturnsStatus()
        {
            // Arrange
            var session = new ScanSession
            {
                Id = Guid.NewGuid(),
                RootPath = "C:\\Test",
                Status = ScanStatus.InProgress,
                FilesFound = 10,
                TotalSize = 1024000,
                ProgressPercentage = 50,
                CurrentOperation = "Scanning..."
            };
            _dbContext.ScanSessions.Add(session);
            await _dbContext.SaveChangesAsync();

            // Act
            var status = await _hub.GetScanStatus(session.Id);

            // Assert
            status.SessionId.Should().Be(session.Id);
            status.Status.Should().Be("InProgress");
            status.FilesScanned.Should().Be(10);
            status.ProgressPercentage.Should().Be(50);
        }

        [Fact]
        public async Task CancelScan_WithValidSession_UpdatesStatusAndNotifiesClients()
        {
            // Arrange
            var session = new ScanSession
            {
                Id = Guid.NewGuid(),
                RootPath = "C:\\Test",
                Status = ScanStatus.InProgress
            };
            _dbContext.ScanSessions.Add(session);
            await _dbContext.SaveChangesAsync();

            var mockGroupClient = new Mock<IClientProxy>();
            _mockClients.Setup(c => c.Group($"scan-{session.Id}")).Returns(mockGroupClient.Object);

            // Act
            await _hub.CancelScan(session.Id);

            // Assert
            var updatedSession = await _dbContext.ScanSessions.FindAsync(session.Id);
            updatedSession!.Status.Should().Be(ScanStatus.Cancelled);
            updatedSession.CompletedAt.Should().NotBeNull();

            mockGroupClient.Verify(
                c => c.SendCoreAsync("ScanCancelled",
                    It.Is<object[]>(o => o.Length == 1 && (Guid)o[0] == session.Id),
                    default),
                Times.Once);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }
    }
}
```

### 2. SignalR Client Testing

**File**: `AlUmmahNow.Tests/DataImporter/Services/FileScanSignalRClientTests.cs`

#### Approach: Integration Testing with Test Server

```csharp
using Xunit;
using FluentAssertions;
using AlUmmahNow.DataImporter.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System.Threading.Tasks;

namespace AlUmmahNow.Tests.DataImporter.Services
{
    public class FileScanSignalRClientTests
    {
        [Fact]
        public async Task ConnectAsync_WithInvalidUrl_ReturnsFalse()
        {
            // Arrange
            var client = new FileScanSignalRClient("http://localhost:9999");

            // Act
            var result = await client.ConnectAsync();

            // Assert
            result.Should().BeFalse();
            client.IsConnected.Should().BeFalse();
        }

        [Fact]
        public void GetServiceStatus_WhenDisconnected_ReturnsDisconnected()
        {
            // Arrange
            var client = new FileScanSignalRClient("http://localhost:5000");

            // Act
            var status = client.GetServiceStatus();

            // Assert
            status.Should().Be(ServiceStatus.Disconnected);
        }

        [Fact]
        public async Task StartScanAsync_WhenNotConnected_ThrowsInvalidOperationException()
        {
            // Arrange
            var client = new FileScanSignalRClient("http://localhost:5000");

            // Act & Assert
            await client.Invoking(c => c.StartScanAsync("C:\\Test"))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Not connected to FileScan service*");
        }

        [Fact]
        public void ServiceStatusChanged_ShouldRaiseEvent()
        {
            // Arrange
            var client = new FileScanSignalRClient("http://localhost:5000");
            var eventRaised = false;
            ServiceStatusEventArgs? capturedArgs = null;

            client.ServiceStatusChanged += (sender, args) =>
            {
                eventRaised = true;
                capturedArgs = args;
            };

            // Act - try to connect (will fail, but should raise event)
            _ = client.ConnectAsync();
            Task.Delay(500).Wait(); // Give time for event to fire

            // Assert
            eventRaised.Should().BeTrue();
            capturedArgs.Should().NotBeNull();
        }
    }
}
```

### 3. End-to-End SignalR Integration Tests

**File**: `AlUmmahNow.Tests/Integration/SignalRIntegrationTests.cs`

```csharp
using Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using AlUmmahNow.DataImporter.Services;
using System.Threading.Tasks;

namespace AlUmmahNow.Tests.Integration
{
    public class SignalRIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SignalRIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task EndToEnd_ConnectAndStartScan_ShouldWork()
        {
            // Arrange
            var serverUrl = _factory.Server.BaseAddress.ToString();
            var client = new FileScanSignalRClient(serverUrl);

            var scanStartedReceived = false;
            Guid? receivedSessionId = null;

            client.ScanStarted += (sender, sessionId) =>
            {
                scanStartedReceived = true;
                receivedSessionId = sessionId;
            };

            // Act
            var connected = await client.ConnectAsync();
            connected.Should().BeTrue();

            var testPath = Path.GetTempPath();
            var sessionId = await client.StartScanAsync(testPath);

            // Wait for event
            await Task.Delay(1000);

            // Assert
            sessionId.Should().NotBeEmpty();
            scanStartedReceived.Should().BeTrue();
            receivedSessionId.Should().Be(sessionId);

            await client.DisconnectAsync();
        }
    }
}
```

---

## Testing Core Functions

### 1. FileScanEngine Testing

**File**: `AlUmmahNow.Tests/FileScanService/Services/FileScanEngineTests.cs`

```csharp
using Xunit;
using Moq;
using FluentAssertions;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Data.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.Tests.FileScanService.Services
{
    public class FileScanEngineTests : FileSystemTestBase
    {
        private readonly ImportDbContext _dbContext;
        private readonly Mock<ILogger<FileScanEngine>> _mockLogger;
        private readonly Mock<IHashCalculator> _mockHashCalculator;
        private readonly Mock<IMetadataExtractor> _mockMetadataExtractor;
        private readonly FileScanEngine _engine;

        public FileScanEngineTests()
        {
            var options = new DbContextOptionsBuilder<ImportDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new ImportDbContext(options);

            _mockLogger = new Mock<ILogger<FileScanEngine>>();
            _mockHashCalculator = new Mock<IHashCalculator>();
            _mockMetadataExtractor = new Mock<IMetadataExtractor>();

            _engine = new FileScanEngine(
                _mockLogger.Object,
                _dbContext,
                _mockHashCalculator.Object,
                _mockMetadataExtractor.Object);
        }

        [Fact]
        public async Task ProcessScanAsync_WithNonExistentDirectory_ThrowsDirectoryNotFoundException()
        {
            // Arrange
            var session = new ScanSession
            {
                Id = Guid.NewGuid(),
                RootPath = "C:\\NonExistent"
            };

            // Act & Assert
            await _engine.Invoking(e => e.ProcessScanAsync(session, CancellationToken.None))
                .Should().ThrowAsync<DirectoryNotFoundException>();
        }

        [Fact]
        public async Task ProcessScanAsync_WithValidDirectory_UpdatesSessionProgress()
        {
            // Arrange
            var testDir = CreateTestDirectory();
            CreateTestFile(testDir, "video1.mp4", 1024);
            CreateTestFile(testDir, "video2.mp4", 2048);

            var session = new ScanSession
            {
                Id = Guid.NewGuid(),
                RootPath = testDir,
                Status = ScanStatus.Pending
            };
            _dbContext.ScanSessions.Add(session);
            await _dbContext.SaveChangesAsync();

            _mockHashCalculator
                .Setup(h => h.CalculatePathHash(It.IsAny<string>()))
                .Returns("test-hash");

            // Act
            await _engine.ProcessScanAsync(session, CancellationToken.None);

            // Assert
            var updatedSession = await _dbContext.ScanSessions.FindAsync(session.Id);
            updatedSession!.FilesFound.Should().BeGreaterThan(0);
            updatedSession.Status.Should().Be(ScanStatus.Completed);
        }

        [Fact]
        public async Task ProcessScanAsync_RaisesProgressEvents()
        {
            // Arrange
            var testDir = CreateTestDirectory();
            CreateTestFile(testDir, "video.mp4", 1024);

            var session = new ScanSession
            {
                Id = Guid.NewGuid(),
                RootPath = testDir
            };
            _dbContext.ScanSessions.Add(session);
            await _dbContext.SaveChangesAsync();

            var progressEvents = new List<ScanProgressEventArgs>();
            _engine.ProgressChanged += (sender, args) => progressEvents.Add(args);

            // Act
            await _engine.ProcessScanAsync(session, CancellationToken.None);

            // Assert
            progressEvents.Should().NotBeEmpty();
        }
    }
}
```

### 2. MetadataExtractor Testing

**File**: `AlUmmahNow.Tests/FileScanService/Services/MetadataExtractorTests.cs`

```csharp
using Xunit;
using Moq;
using FluentAssertions;
using AlUmmahNow.FileScanService.Services;
using Microsoft.Extensions.Logging;

namespace AlUmmahNow.Tests.FileScanService.Services
{
    public class MetadataExtractorTests : FileSystemTestBase
    {
        private readonly Mock<ILogger<MetadataExtractor>> _mockLogger;
        private readonly MetadataExtractor _extractor;

        public MetadataExtractorTests()
        {
            _mockLogger = new Mock<ILogger<MetadataExtractor>>();
            _extractor = new MetadataExtractor(_mockLogger.Object);
        }

        [Fact]
        public async Task ExtractMetadataAsync_WithValidVideoFile_ReturnsMetadata()
        {
            // Arrange
            var testDir = CreateTestDirectory();
            var videoPath = CreateTestFile(testDir, "test.mp4", 1024 * 1024); // 1MB

            // Act
            var metadata = await _extractor.ExtractMetadataAsync(videoPath);

            // Assert
            metadata.Should().NotBeNull();
            metadata.FilePath.Should().Be(videoPath);
            metadata.FileName.Should().Be("test.mp4");
            metadata.FileSize.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task ExtractMetadataAsync_WithNonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentPath = "C:\\NonExistent\\video.mp4";

            // Act & Assert
            await _extractor.Invoking(e => e.ExtractMetadataAsync(nonExistentPath))
                .Should().ThrowAsync<FileNotFoundException>();
        }

        [Fact]
        public async Task ExtractMetadataAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var testDir = CreateTestDirectory();
            var videoPath = CreateTestFile(testDir, "large.mp4", 10 * 1024 * 1024); // 10MB
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await _extractor.Invoking(e => e.ExtractMetadataAsync(videoPath, cts.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
```

---

## Running Tests

### Command Line

```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory:"./TestResults"

# Run specific test project
dotnet test AlUmmahNow.Tests/AlUmmahNow.Tests.csproj

# Run specific test class
dotnet test --filter "FullyQualifiedName~FileScanHubTests"

# Run specific test method
dotnet test --filter "FullyQualifiedName~FileScanHubTests.StartScan_WithValidPath_ReturnsSessionId"

# Run tests in parallel (faster)
dotnet test --parallel

# Run with detailed output
dotnet test --verbosity detailed
```

### Generate Coverage Report

```bash
# 1. Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory:"./TestResults"

# 2. Generate HTML report
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"TestResults/CoverageReport" -reporttypes:Html

# 3. View report
start TestResults/CoverageReport/index.html  # Windows
open TestResults/CoverageReport/index.html   # macOS
xdg-open TestResults/CoverageReport/index.html  # Linux
```

### Coverage Configuration

**File**: `AlUmmahNow.Tests/coverage.runsettings`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat code coverage">
        <Configuration>
          <Format>cobertura</Format>
          <Exclude>[*.Tests]*,[xunit.*]*</Exclude>
          <Include>[AlUmmahNow.*]*</Include>
          <ExcludeByFile>**/Migrations/**/*.cs</ExcludeByFile>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

---

## AI Coding Flow Integration

### 1. Pre-Commit Hook

Create `.git/hooks/pre-commit`:

```bash
#!/bin/bash

echo "Running tests before commit..."

# Run tests
dotnet test --no-build --verbosity quiet

if [ $? -ne 0 ]; then
    echo "❌ Tests failed. Commit aborted."
    echo "Run 'dotnet test' to see failures."
    exit 1
fi

echo "✅ All tests passed!"
exit 0
```

### 2. AI Coding Workflow

When using AI tools (like Claude Code), follow this workflow:

#### Step 1: Before Code Changes
```bash
# Run baseline tests
dotnet test

# Generate baseline coverage
dotnet test --collect:"XPlat Code Coverage"
```

#### Step 2: During Development
```bash
# Watch tests continuously (requires dotnet-watch)
dotnet watch test

# Or run specific tests related to your changes
dotnet test --filter "FullyQualifiedName~YourTestClass"
```

#### Step 3: After Code Changes
```bash
# Run all tests
dotnet test

# Check coverage
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"TestResults/CoverageReport" -reporttypes:Html

# Review coverage report
start TestResults/CoverageReport/index.html
```

### 3. AI Prompt Template

When asking AI to write code:

```
Please implement [feature description] with the following requirements:
1. Write the implementation in [specific file/class]
2. Create comprehensive unit tests in AlUmmahNow.Tests/[appropriate folder]
3. Use xUnit, Moq, and FluentAssertions
4. Ensure tests cover:
   - Happy path scenarios
   - Edge cases
   - Error handling
   - Async operations with cancellation
5. Follow the existing test patterns in the project
6. Aim for >80% code coverage for new code
7. After implementation, run: dotnet test
```

### 4. AI Code Review Checklist

Before committing AI-generated code:

- [ ] All tests pass: `dotnet test`
- [ ] New code has tests (>80% coverage)
- [ ] Tests follow AAA pattern (Arrange, Act, Assert)
- [ ] Mocks used appropriately for dependencies
- [ ] Edge cases covered
- [ ] Error scenarios tested
- [ ] Async/cancellation tested
- [ ] Test names are descriptive
- [ ] No flaky tests (run multiple times)

---

## Best Practices

### 1. Test Organization

```csharp
// Use AAA pattern
[Fact]
public async Task MethodName_Scenario_ExpectedBehavior()
{
    // Arrange - Setup test data and dependencies
    var dependency = new Mock<IDependency>();
    var sut = new SystemUnderTest(dependency.Object);

    // Act - Execute the method being tested
    var result = await sut.MethodAsync();

    // Assert - Verify the results
    result.Should().NotBeNull();
}
```

### 2. Naming Conventions

- Test class: `{ClassUnderTest}Tests`
- Test method: `{Method}_{Scenario}_{ExpectedBehavior}`
- Example: `StartScan_WithValidPath_ReturnsSessionId`

### 3. Mocking Best Practices

```csharp
// Mock interfaces, not concrete classes
var mockService = new Mock<IService>();

// Setup specific behaviors
mockService.Setup(s => s.GetDataAsync())
    .ReturnsAsync(new Data { Id = 1 });

// Verify interactions
mockService.Verify(s => s.SaveAsync(It.IsAny<Data>()), Times.Once);
```

### 4. Async Testing

```csharp
[Fact]
public async Task AsyncMethod_ShouldComplete()
{
    // Use async/await in tests
    var result = await _service.GetDataAsync();
    result.Should().NotBeNull();
}

[Fact]
public async Task AsyncMethod_WithCancellation_ShouldThrow()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await _service.Invoking(s => s.GetDataAsync(cts.Token))
        .Should().ThrowAsync<OperationCanceledException>();
}
```

### 5. Database Testing

```csharp
// Use in-memory database for isolated tests
var options = new DbContextOptionsBuilder<ImportDbContext>()
    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
    .Options;
var dbContext = new ImportDbContext(options);

// Clean database name per test ensures isolation
```

### 6. File System Testing

```csharp
public class FileSystemTestBase : IDisposable
{
    private readonly List<string> _tempDirectories = new();

    protected string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
```

### 7. SignalR Testing Tips

- Use `WebApplicationFactory` for integration tests
- Mock `IHubCallerClients`, `HubCallerContext`, `IGroupManager` for unit tests
- Test both client and server sides separately
- Use timeouts for event-based tests
- Clean up connections after tests

### 8. Coverage Goals

- **New code**: Aim for 80%+ coverage
- **Critical paths**: 100% coverage
- **Core business logic**: 90%+ coverage
- **UI/Infrastructure**: 60%+ coverage

---

## Quick Reference

### Common Commands
```bash
# Build
dotnet build

# Run tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate report
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"TestResults/CoverageReport" -reporttypes:Html

# Watch mode
dotnet watch test

# Filter tests
dotnet test --filter "FullyQualifiedName~SignalR"
```

### Coverage Report Location
```
TestResults/CoverageReport/index.html
```

### Test Project Location
```
AlUmmahNow.Tests/
```

---

## Next Steps

1. ✅ Current: 78 tests, ~12% coverage
2. 🎯 Target: 200+ tests, >60% coverage
3. 📋 Priority areas:
   - SignalR Hub tests (FileScanHub)
   - SignalR Client tests (FileScanSignalRClient)
   - FileScanEngine core logic
   - MetadataExtractor
   - FileScanWorker background service

---

*Last Updated: 2025-10-06*
*Coverage Report: TestResults/CoverageReport/index.html*

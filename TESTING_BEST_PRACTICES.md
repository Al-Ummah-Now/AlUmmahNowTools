# C# Test Automation Best Practices Guide
## AlUmmahNow Tools Project - Testing Strategy & Recommendations

**Generated:** 2025-10-12
**Project:** AlUmmahNowTools
**Test Framework:** xUnit 2.9.2
**Current Test Coverage:** ~1,616 lines of test code across 15 test files

---

## Table of Contents
1. [Current Testing State Analysis](#current-testing-state-analysis)
2. [Unit Testing Frameworks](#unit-testing-frameworks)
3. [Mocking Frameworks](#mocking-frameworks)
4. [Integration Testing](#integration-testing)
5. [Test Organization](#test-organization)
6. [Async Testing](#async-testing)
7. [Dependency Injection in Tests](#dependency-injection-in-tests)
8. [Code Coverage](#code-coverage)
9. [CI/CD Integration](#cicd-integration)
10. [Specific Recommendations for This Project](#specific-recommendations-for-this-project)

---

## Current Testing State Analysis

### What's Working Well ✅

1. **Strong Foundation**
   - xUnit framework properly configured with latest version (2.9.2)
   - Moq (4.20.72) for mocking dependencies
   - FluentAssertions (6.12.1) for readable assertions
   - Coverlet for code coverage collection
   - In-memory EF Core database testing setup

2. **Test Infrastructure**
   - Excellent `FileSystemTestBase` class for file system testing
   - Proper async lifecycle management with `IAsyncLifetime`
   - Test data generation helpers
   - Temporary directory cleanup

3. **Good Testing Patterns**
   - AAA (Arrange-Act-Assert) pattern consistently used
   - Descriptive test names following convention: `MethodName_Scenario_ExpectedResult`
   - Theory-based tests with `[Theory]` and `[InlineData]`
   - Proper use of FluentAssertions for readable assertions
   - Event testing with event handler subscriptions

4. **Coverage Areas**
   - FileScanService (FileWatchService, ApprovalService, MetadataExtractor)
   - SignalR Hubs (FileScanHub)
   - Data repository layer
   - Storage clients (BunnyStorage, R2)
   - Integration tests for end-to-end workflows

### Areas for Improvement 🔧

1. **Missing CI/CD Integration**
   - No GitHub Actions or Azure DevOps pipeline configuration found
   - Tests are not automatically run on commits/PRs

2. **Limited Integration Tests**
   - Integration folder exists but appears empty
   - Database integration tests only use in-memory EF Core
   - No real database testing against SQL Server/PostgreSQL

3. **Test Coverage Gaps**
   - Only 15 test files for 36+ source files (24 in Data, 12 in FileScanService)
   - Missing tests for:
     - FileScanEngine
     - ImportWorker
     - FileScanWorker
     - HashCalculator (only basic tests)
     - Video-related services (BunnyStreamClient only partially tested)

4. **No Performance/Load Testing**
   - File scanning could benefit from performance benchmarks
   - No tests for concurrent operations under load

5. **Limited API Testing**
   - Missing WebApplicationFactory tests for full API integration
   - SignalR testing could be more comprehensive

---

## Unit Testing Frameworks

### xUnit (Currently Used) ⭐ RECOMMENDED FOR THIS PROJECT

**Strengths:**
- Modern, actively maintained framework
- Clean syntax with no `[TestClass]` or `[TestInitialize]` attributes
- Built-in parallelization for faster test execution
- Excellent async/await support
- Constructor-based setup (each test gets fresh instance)
- Theory tests with `[Theory]` and `[InlineData]` for parameterized testing

**Weaknesses:**
- No built-in test ordering (but this is often considered good practice)
- Learning curve for `IClassFixture` and `ICollectionFixture`

**Current Usage Example:**
```csharp
public class FileWatchServiceTests : FileSystemTestBase
{
    private readonly Mock<ILogger<FileWatchService>> _mockLogger;
    private readonly ImportDbContext _dbContext;
    private readonly FileWatchService _fileWatchService;

    public FileWatchServiceTests()
    {
        _mockLogger = new Mock<ILogger<FileWatchService>>();

        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ImportDbContext(options);

        _fileWatchService = new FileWatchService(_mockLogger.Object, _dbContext);
    }

    [Fact]
    public async Task StartWatchAsync_WithValidRequest_ShouldCreateWatchAndReturnId()
    {
        // Arrange
        var request = new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory
        };

        // Act
        var watchId = await _fileWatchService.StartWatchAsync(request);

        // Assert
        watchId.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData(500, 1000)]  // Below minimum should be adjusted
    [InlineData(1500, 1500)] // Above minimum should remain unchanged
    public async Task StartWatchAsync_ShouldEnforceMinimumProcessingDelay(
        int requestedDelay, int expectedDelay)
    {
        // Test implementation
    }
}
```

### NUnit (Alternative)

**When to Consider:**
- Team has NUnit experience
- Need ordered test execution
- Want `[SetUp]` and `[TearDown]` instead of constructor

**Strengths:**
- Rich assertion library (Assert.That)
- Test fixtures with `[SetUp]` and `[TearDown]`
- `[TestCase]` for parameterized tests
- Test ordering support

**Weaknesses:**
- More verbose syntax
- Test instances shared across tests (can lead to state issues)

**NUnit Example:**
```csharp
[TestFixture]
public class FileWatchServiceTests
{
    private Mock<ILogger<FileWatchService>> _mockLogger;
    private ImportDbContext _dbContext;
    private FileWatchService _fileWatchService;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<FileWatchService>>();
        _dbContext = CreateInMemoryDbContext();
        _fileWatchService = new FileWatchService(_mockLogger.Object, _dbContext);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext?.Dispose();
    }

    [Test]
    public async Task StartWatchAsync_WithValidRequest_ShouldCreateWatch()
    {
        // Arrange
        var request = new CreateWatchRequest { ChannelName = "Test" };

        // Act
        var watchId = await _fileWatchService.StartWatchAsync(request);

        // Assert
        Assert.That(watchId, Is.Not.EqualTo(Guid.Empty));
    }

    [TestCase(500, 1000)]
    [TestCase(1500, 1500)]
    public async Task StartWatchAsync_EnforcesMinimumDelay(int requested, int expected)
    {
        // Test implementation
    }
}
```

### MSTest (Not Recommended for New Projects)

**When to Consider:**
- Legacy Microsoft projects
- Required for corporate environments standardized on MSTest

**Strengths:**
- Built into Visual Studio
- Familiar to developers with long Microsoft experience
- Corporate support

**Weaknesses:**
- Less modern than xUnit/NUnit
- Slower test execution
- More verbose syntax
- Limited parallelization

---

## Mocking Frameworks

### Moq (Currently Used) ⭐ RECOMMENDED

**Strengths:**
- Most popular .NET mocking framework
- Fluent, intuitive syntax
- Excellent lambda-based setup
- Good verification capabilities
- Wide community support

**Current Usage Examples:**

```csharp
// Simple mock setup
var mockR2Client = new Mock<IR2Client>();
mockR2Client.Setup(x => x.ListBucketsAsync())
    .ReturnsAsync(expectedBuckets)
    .Verifiable();

// Complex argument matching
mockStorage.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.Is<string>(s => s.Contains(".mp4"))))
    .Returns(Task.CompletedTask);

// Verification
_mockApprovalService.Verify(
    x => x.AddPendingApprovalAsync(watchId, testFilePath, It.IsAny<CancellationToken>()),
    Times.Once);

// Callback for side effects
_mockApprovalService
    .Setup(x => x.AddPendingApprovalAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .Callback<Guid, string, CancellationToken>((watchId, path, ct) => {
        // Capture arguments or trigger events
    })
    .ReturnsAsync(Guid.NewGuid());
```

**Best Practices:**
```csharp
// ✅ Good: Verify specific behavior
mockClient.Verify(x => x.SaveAsync(It.Is<Video>(v =>
    v.Title == "Test Video" && v.Duration > 0)), Times.Once);

// ❌ Bad: Verify everything with VerifyAll (brittle tests)
mockClient.VerifyAll();

// ✅ Good: Use It.IsAny<T> when exact value doesn't matter
mockLogger.Setup(x => x.Log(
    LogLevel.Error,
    It.IsAny<EventId>(),
    It.IsAny<It.IsAnyType>(),
    It.IsAny<Exception>(),
    It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

// ✅ Good: Mock returns tasks for async methods
mock.Setup(x => x.ProcessAsync()).ReturnsAsync(result);
```

### NSubstitute (Alternative)

**When to Use:**
- Prefer more natural language syntax
- Want simpler mocking setup
- Need auto-mocking of properties

**Strengths:**
- More concise syntax
- No need for Setup/Returns separation
- Better for complex return values

**Example:**
```csharp
var mockService = Substitute.For<IApprovalService>();

// Setup
mockService.GetPendingApprovalsAsync().Returns(approvals);
mockService.ProcessApprovalDecisionAsync(Arg.Any<ApprovalDecisionRequest>())
    .Returns(true);

// Verification
await mockService.Received(1).GetPendingApprovalsAsync();
await mockService.DidNotReceive().DeleteApprovalAsync(Arg.Any<Guid>());
```

### FakeItEasy (Alternative)

**When to Use:**
- Want most flexible mocking
- Need to fake static classes or sealed types
- Prefer discovery-based syntax

**Strengths:**
- Very flexible
- Great IntelliSense support
- Can fake more types than Moq

**Example:**
```csharp
var fakeService = A.Fake<IApprovalService>();

// Setup
A.CallTo(() => fakeService.GetPendingApprovalsAsync()).Returns(approvals);

// Verification
A.CallTo(() => fakeService.ProcessApprovalDecisionAsync(A<ApprovalDecisionRequest>._))
    .MustHaveHappenedOnceExactly();
```

**Recommendation for This Project:** Stick with Moq. The codebase is already using it consistently, and migrating would provide minimal benefit. The patterns are well-established and the team is familiar with it.

---

## Integration Testing

### Current State

Your project has good foundations but needs expansion in integration testing:

```csharp
// Current: Using in-memory database
var options = new DbContextOptionsBuilder<ImportDbContext>()
    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
    .Options;
```

### Best Practices for Integration Testing

#### 1. Database Testing

**In-Memory Database (Current Approach)**
- ✅ Fast
- ✅ No external dependencies
- ✅ Good for unit tests
- ❌ Doesn't test real database behavior (transactions, constraints, etc.)

**Testcontainers (Recommended for Real Integration Tests)**

Install: `Testcontainers` NuGet package

```csharp
public class DatabaseIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer;
    private ImportDbContext _dbContext;

    public DatabaseIntegrationTests()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var connectionString = _dbContainer.GetConnectionString();
        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        _dbContext = new ImportDbContext(options);
        await _dbContext.Database.MigrateAsync();
    }

    [Fact]
    public async Task RealDatabase_CanSaveAndRetrieveScanSession()
    {
        // Arrange
        var session = new ScanSession
        {
            RootPath = "/test/path",
            Status = ScanStatus.Pending
        };

        // Act
        _dbContext.ScanSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.ScanSessions.FindAsync(session.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.RootPath.Should().Be("/test/path");
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _dbContainer.StopAsync();
    }
}
```

**LocalDB (For SQL Server Projects)**

```csharp
public class SqlServerIntegrationTests : IDisposable
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=AlUmmahNowTests;Trusted_Connection=true;";
    private readonly ImportDbContext _dbContext;

    public SqlServerIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _dbContext = new ImportDbContext(options);
        _dbContext.Database.EnsureDeleted();
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task RealSqlServer_TransactionRollback_WorksCorrectly()
    {
        // Arrange
        using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var session = new ScanSession { RootPath = "/test" };
            _dbContext.ScanSessions.Add(session);
            await _dbContext.SaveChangesAsync();

            // Simulate error
            throw new Exception("Simulated error");
        }
        catch
        {
            await transaction.RollbackAsync();
        }

        // Assert - session should not exist
        var count = await _dbContext.ScanSessions.CountAsync();
        count.Should().Be(0);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}
```

#### 2. API Integration Testing

**SignalR Hub Testing (Current Approach is Good)**

```csharp
// Your current approach for SignalR is solid
public class FileScanHubTests
{
    private readonly Mock<IHubCallerClients> _mockClients;
    private readonly Mock<HubCallerContext> _mockContext;
    private readonly Mock<IGroupManager> _mockGroups;

    // Good pattern for testing hubs
    [Fact]
    public async Task JoinScanSession_ShouldAddToGroup()
    {
        // Arrange
        var sessionId = "test-session-123";
        var connectionId = "test-connection";
        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

        // Act
        await _hub.JoinScanSession(sessionId);

        // Assert
        _mockGroups.Verify(
            g => g.AddToGroupAsync(connectionId, $"scan-{sessionId}", default),
            Times.Once);
    }
}
```

**WebApplicationFactory for API Tests**

```csharp
public class FileScanApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public FileScanApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace real services with test versions
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ImportDbContext>));
                if (descriptor != null)
                    services.Remove(descriptor);

                services.AddDbContext<ImportDbContext>(options =>
                    options.UseInMemoryDatabase("TestDb"));
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetScanSessions_ReturnsSuccessAndCorrectContentType()
    {
        // Act
        var response = await _client.GetAsync("/api/scans/sessions");

        // Assert
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType.ToString()
            .Should().Be("application/json; charset=utf-8");
    }

    [Fact]
    public async Task StartScan_WithValidPath_ReturnsSessionId()
    {
        // Arrange
        var request = new { path = "/test/path", includeSubdirectories = true };
        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/scans/start", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionId = await response.Content.ReadAsStringAsync();
        Guid.Parse(sessionId).Should().NotBe(Guid.Empty);
    }
}
```

#### 3. External API Testing

**Use WireMock.Net for External API Mocking**

```csharp
public class BunnyApiIntegrationTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly BunnyStreamClient _client;

    public BunnyApiIntegrationTests()
    {
        _mockServer = WireMockServer.Start();

        // Override the base URL to point to mock server
        _client = new BunnyStreamClient("test-key", _mockServer.Url);
    }

    [Fact]
    public async Task GetVideo_WithValidId_ReturnsVideoData()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath("/library/*/videos/*")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    guid = "test-guid",
                    title = "Test Video",
                    length = 3600
                })));

        // Act
        var video = await _client.GetVideoAsync("test-guid");

        // Assert
        video.Should().NotBeNull();
        video.Title.Should().Be("Test Video");
    }

    public void Dispose()
    {
        _mockServer.Stop();
    }
}
```

#### 4. File System Integration Testing

**Your Current Approach is Excellent**

```csharp
// Your FileSystemTestBase is a great pattern
public abstract class FileSystemTestBase : IAsyncLifetime
{
    protected string TestRootDirectory { get; private set; }

    public virtual async Task InitializeAsync()
    {
        // Create temporary test directory
        TestRootDirectory = Path.Combine(
            Path.GetTempPath(),
            "AlUmmahNowTests",
            Guid.NewGuid().ToString());
        Directory.CreateDirectory(TestRootDirectory);

        await CreateTestFilesAsync();
    }

    public virtual Task DisposeAsync()
    {
        // Clean up test files
        if (Directory.Exists(TestRootDirectory))
            Directory.Delete(TestRootDirectory, recursive: true);
        return Task.CompletedTask;
    }
}
```

**Enhancement Suggestion: Add File Watcher Tests**

```csharp
public class FileWatcherIntegrationTests : FileSystemTestBase
{
    [Fact]
    public async Task FileWatcher_DetectsNewFile_WithinProcessingDelay()
    {
        // Arrange
        var watchService = new FileWatchService(_mockLogger.Object, _dbContext);
        var fileDetectedTcs = new TaskCompletionSource<FileDetectedEventArgs>();

        watchService.FileDetected += (sender, e) => fileDetectedTcs.TrySetResult(e);

        var watchId = await watchService.StartWatchAsync(new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = TestChannel1Directory,
            ProcessingDelayMs = 100
        });

        // Act
        var testFilePath = await CreateTestVideoFileAsync(
            TestChannel1Directory,
            "newfile.mp4",
            1024 * 1024);

        // Assert
        var eventArgs = await fileDetectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        eventArgs.FileName.Should().Be("newfile.mp4");
        eventArgs.FilePath.Should().Be(testFilePath);
    }
}
```

---

## Test Organization

### Current Patterns (Good!)

Your codebase follows excellent patterns:

#### 1. AAA Pattern (Arrange-Act-Assert)

```csharp
[Fact]
public async Task StartWatchAsync_WithValidRequest_ShouldCreateWatchAndReturnId()
{
    // Arrange
    var request = new CreateWatchRequest
    {
        ChannelName = "TestChannel",
        WatchPath = TestChannel1Directory,
        IncludeSubdirectories = true
    };

    // Act
    var watchId = await _fileWatchService.StartWatchAsync(request);

    // Assert
    watchId.Should().NotBe(Guid.Empty);
    var watch = await _dbContext.ChannelWatches.FindAsync(watchId);
    watch.Should().NotBeNull();
    watch.ChannelName.Should().Be("TestChannel");
}
```

#### 2. Test Naming Convention

✅ **Good naming (currently used):**
```csharp
StartWatchAsync_WithValidRequest_ShouldCreateWatchAndReturnId
GetPendingApprovalsAsync_ShouldReturnOnlyPendingApprovals
ProcessApprovalDecisionAsync_WithEmptyList_ShouldReturnFalse
```

Format: `MethodName_Scenario_ExpectedResult`

❌ **Avoid:**
```csharp
Test1()
TestStartWatch()
ValidRequestTest()
```

#### 3. Test Categories/Traits

**Add Traits for Organization:**

```csharp
public class FileWatchServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Feature", "FileWatcher")]
    public async Task StartWatchAsync_CreatesWatch()
    {
        // Test implementation
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "FileWatcher")]
    public async Task FileWatcher_DetectsRealFileChanges()
    {
        // Test implementation
    }
}
```

**Run specific categories:**
```bash
dotnet test --filter "Category=Unit"
dotnet test --filter "Feature=FileWatcher"
dotnet test --filter "Category=Integration&Feature=Database"
```

#### 4. Test Data Builders

**Enhance with Builder Pattern:**

```csharp
public class ScanSessionBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _rootPath = "/default/path";
    private ScanStatus _status = ScanStatus.Pending;
    private int _filesFound = 0;
    private DateTime _createdAt = DateTime.UtcNow;

    public ScanSessionBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public ScanSessionBuilder WithRootPath(string rootPath)
    {
        _rootPath = rootPath;
        return this;
    }

    public ScanSessionBuilder WithStatus(ScanStatus status)
    {
        _status = status;
        return this;
    }

    public ScanSessionBuilder WithFilesFound(int count)
    {
        _filesFound = count;
        return this;
    }

    public ScanSessionBuilder AsCompleted()
    {
        _status = ScanStatus.Completed;
        return this;
    }

    public ScanSession Build() => new()
    {
        Id = _id,
        RootPath = _rootPath,
        Status = _status,
        FilesFound = _filesFound,
        CreatedAt = _createdAt
    };
}

// Usage in tests
[Fact]
public async Task GetCompletedSessions_ReturnsOnlyCompleted()
{
    // Arrange
    var completedSession = new ScanSessionBuilder()
        .WithRootPath("/videos/channel1")
        .AsCompleted()
        .WithFilesFound(42)
        .Build();

    var pendingSession = new ScanSessionBuilder()
        .WithRootPath("/videos/channel2")
        .WithStatus(ScanStatus.Pending)
        .Build();

    _dbContext.ScanSessions.AddRange(completedSession, pendingSession);
    await _dbContext.SaveChangesAsync();

    // Act
    var result = await _service.GetCompletedSessionsAsync();

    // Assert
    result.Should().ContainSingle()
        .Which.Id.Should().Be(completedSession.Id);
}
```

#### 5. Custom Assertions

```csharp
public static class CustomAssertions
{
    public static ScanSessionAssertions Should(this ScanSession session)
        => new(session);
}

public class ScanSessionAssertions
{
    private readonly ScanSession _subject;

    public ScanSessionAssertions(ScanSession subject)
    {
        _subject = subject;
    }

    public AndConstraint<ScanSessionAssertions> BeCompleted(string because = "")
    {
        _subject.Status.Should().Be(ScanStatus.Completed, because);
        _subject.CompletedAt.Should().NotBeNull(because);
        _subject.ProgressPercentage.Should().Be(100, because);
        return new AndConstraint<ScanSessionAssertions>(this);
    }

    public AndConstraint<ScanSessionAssertions> BeInProgress(string because = "")
    {
        _subject.Status.Should().Be(ScanStatus.InProgress, because);
        _subject.StartedAt.Should().NotBeNull(because);
        _subject.CompletedAt.Should().BeNull(because);
        return new AndConstraint<ScanSessionAssertions>(this);
    }

    public AndConstraint<ScanSessionAssertions> HaveScanned(int fileCount, string because = "")
    {
        _subject.FilesFound.Should().Be(fileCount, because);
        return new AndConstraint<ScanSessionAssertions>(this);
    }
}

// Usage
[Fact]
public async Task CompleteScan_SetsCorrectState()
{
    // Arrange & Act
    await _service.CompleteScanAsync(sessionId);
    var session = await _dbContext.ScanSessions.FindAsync(sessionId);

    // Assert
    session.Should().BeCompleted()
        .And.HaveScanned(42);
}
```

---

## Async Testing

### Current Patterns (Good!)

Your tests properly use async/await:

```csharp
[Fact]
public async Task StartWatchAsync_WithValidRequest_ShouldCreateWatch()
{
    var watchId = await _fileWatchService.StartWatchAsync(request);
    watchId.Should().NotBe(Guid.Empty);
}
```

### Common Pitfalls & Best Practices

#### 1. ❌ DON'T: async void in Tests

```csharp
// ❌ WRONG - async void swallows exceptions
[Fact]
public async void BadTest()
{
    await _service.DoSomethingAsync();
}

// ✅ CORRECT
[Fact]
public async Task GoodTest()
{
    await _service.DoSomethingAsync();
}
```

#### 2. ❌ DON'T: Forget ConfigureAwait in Library Code

```csharp
// In production code (not tests):
public async Task ProcessFileAsync(string path)
{
    // ✅ Good for library code
    var data = await File.ReadAllTextAsync(path).ConfigureAwait(false);

    // ❌ Can cause deadlocks in some contexts
    var data = await File.ReadAllTextAsync(path);
}

// In test code:
[Fact]
public async Task TestMethod()
{
    // ConfigureAwait not needed in tests - they don't have SynchronizationContext
    var data = await File.ReadAllTextAsync(path);
}
```

#### 3. ✅ DO: Test Cancellation Properly

```csharp
[Fact]
public async Task ProcessAsync_WhenCancelled_ThrowsOperationCanceledException()
{
    // Arrange
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    // Act & Assert
    await Assert.ThrowsAsync<OperationCanceledException>(
        () => _service.ProcessLongRunningOperationAsync(cts.Token));
}

[Fact]
public async Task ProcessAsync_CancellationMidProcess_StopsGracefully()
{
    // Arrange
    using var cts = new CancellationTokenSource();
    var started = new TaskCompletionSource<bool>();

    // Start long-running operation
    var task = Task.Run(async () =>
    {
        started.SetResult(true);
        await _service.ProcessManyFilesAsync(cts.Token);
    });

    // Wait for start
    await started.Task;

    // Act - cancel after starting
    await Task.Delay(100);
    cts.Cancel();

    // Assert
    await Assert.ThrowsAsync<OperationCanceledException>(() => task);
}
```

#### 4. ✅ DO: Test Timeout Scenarios

```csharp
[Fact]
public async Task SlowOperation_CompletesWithinTimeout()
{
    // Arrange
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    // Act & Assert - should complete within 5 seconds
    var result = await _service.ProcessAsync(cts.Token);
    result.Should().NotBeNull();
}

[Fact]
public async Task VerySlowOperation_TimesOut()
{
    // Arrange
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

    // Act & Assert
    await Assert.ThrowsAsync<OperationCanceledException>(
        () => _service.ProcessVerySlowOperationAsync(cts.Token));
}
```

#### 5. ✅ DO: Use Task.WaitAsync for Safer Timeout Testing

```csharp
[Fact]
public async Task FileDetection_OccursWithinExpectedTime()
{
    // Arrange
    var fileDetectedTcs = new TaskCompletionSource<FileDetectedEventArgs>();
    _watchService.FileDetected += (s, e) => fileDetectedTcs.TrySetResult(e);

    // Act
    await CreateTestVideoFileAsync(TestChannel1Directory, "test.mp4", 1024);

    // Assert - use WaitAsync instead of Task.Delay loops
    var eventArgs = await fileDetectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
    eventArgs.FileName.Should().Be("test.mp4");
}
```

#### 6. ❌ DON'T: Use .Result or .Wait() in Tests

```csharp
// ❌ WRONG - can cause deadlocks
[Fact]
public void BadSyncTest()
{
    var result = _service.GetDataAsync().Result;
    Assert.NotNull(result);
}

// ✅ CORRECT
[Fact]
public async Task GoodAsyncTest()
{
    var result = await _service.GetDataAsync();
    result.Should().NotBeNull();
}
```

#### 7. ✅ DO: Test Concurrent Operations

```csharp
[Fact]
public async Task ConcurrentFileProcessing_HandlesMultipleFiles()
{
    // Arrange
    var files = Enumerable.Range(1, 10)
        .Select(i => $"file{i}.mp4")
        .ToList();

    // Act - process files concurrently
    var tasks = files.Select(file =>
        _service.ProcessFileAsync(Path.Combine(TestDir, file)));
    var results = await Task.WhenAll(tasks);

    // Assert
    results.Should().HaveCount(10);
    results.Should().OnlyContain(r => r.Success);
}

[Fact]
public async Task ConcurrentDatabaseWrites_MaintainConsistency()
{
    // Arrange
    var approvals = Enumerable.Range(1, 50)
        .Select(i => new PendingApproval
        {
            ChannelWatchId = _watchId,
            FileName = $"video{i}.mp4",
            FilePath = $"/path/video{i}.mp4",
            FileSize = 1024 * i,
            Extension = ".mp4"
        })
        .ToList();

    // Act - add all concurrently
    var addTasks = approvals.Select(a =>
        _dbContext.PendingApprovals.AddAsync(a).AsTask());
    await Task.WhenAll(addTasks);
    await _dbContext.SaveChangesAsync();

    // Assert
    var count = await _dbContext.PendingApprovals.CountAsync();
    count.Should().Be(50);
}
```

#### 8. ✅ DO: Test Async Disposal

```csharp
public class DisposableServiceTests : IAsyncLifetime
{
    private DisposableService _service;

    public async Task InitializeAsync()
    {
        _service = new DisposableService();
        await _service.InitializeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ReleasesResources()
    {
        // Act
        await _service.DisposeAsync();

        // Assert
        _service.IsDisposed.Should().BeTrue();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _service.ProcessAsync());
    }

    public async Task DisposeAsync()
    {
        if (_service != null)
            await _service.DisposeAsync();
    }
}
```

---

## Dependency Injection in Tests

### Current Approach

Your tests manually construct dependencies:

```csharp
public FileWatchServiceTests()
{
    _mockLogger = new Mock<ILogger<FileWatchService>>();
    _dbContext = new ImportDbContext(options);
    _mockApprovalService = new Mock<IApprovalService>();

    _fileWatchService = new FileWatchService(
        _mockLogger.Object,
        _dbContext,
        _mockApprovalService.Object);
}
```

This is fine for unit tests, but for integration tests, use real DI:

### Integration Tests with DI Container

```csharp
public class ServiceCollectionTests
{
    private readonly ServiceProvider _serviceProvider;

    public ServiceCollectionTests()
    {
        var services = new ServiceCollection();

        // Register services as they would be in production
        services.AddDbContext<ImportDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        services.AddLogging(builder => builder.AddDebug());

        // Register application services
        services.AddScoped<IFileWatchService, FileWatchService>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<IMetadataExtractor, MetadataExtractor>();
        services.AddScoped<IHashCalculator, HashCalculator>();

        // Override specific services for testing
        services.AddSingleton<IStorageClient<Video>>(sp =>
        {
            var mock = new Mock<IStorageClient<Video>>();
            mock.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            return mock.Object;
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ResolveFileWatchService_WithDependencies_Works()
    {
        // Act
        var service = _serviceProvider.GetRequiredService<IFileWatchService>();

        // Assert
        service.Should().NotBeNull();
        service.Should().BeOfType<FileWatchService>();
    }

    [Fact]
    public async Task FullWorkflow_WithRealDependencies_Succeeds()
    {
        // Arrange
        var fileWatchService = _serviceProvider.GetRequiredService<IFileWatchService>();
        var approvalService = _serviceProvider.GetRequiredService<IApprovalService>();

        // Act
        var watchId = await fileWatchService.StartWatchAsync(new CreateWatchRequest
        {
            ChannelName = "TestChannel",
            WatchPath = "/test/path"
        });

        // Assert
        watchId.Should().NotBe(Guid.Empty);

        var statistics = await approvalService.GetApprovalStatisticsAsync(watchId);
        statistics.Should().NotBeNull();
    }
}
```

### Testing Scoped Services

```csharp
[Fact]
public async Task ScopedServices_InDifferentScopes_AreDifferentInstances()
{
    // Act
    IApprovalService service1;
    IApprovalService service2;

    using (var scope1 = _serviceProvider.CreateScope())
    {
        service1 = scope1.ServiceProvider.GetRequiredService<IApprovalService>();
    }

    using (var scope2 = _serviceProvider.CreateScope())
    {
        service2 = scope2.ServiceProvider.GetRequiredService<IApprovalService>();
    }

    // Assert
    service1.Should().NotBeSameAs(service2);
}
```

### Testing Configuration

```csharp
public class ConfigurationTests
{
    [Fact]
    public void Configuration_LoadsCorrectly()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Test.json", optional: true)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Configure options
        services.Configure<FileWatchOptions>(
            configuration.GetSection("FileWatch"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<IOptions<FileWatchOptions>>();

        // Assert
        options.Value.Should().NotBeNull();
        options.Value.ProcessingDelayMs.Should().BeGreaterThan(0);
    }
}
```

---

## Code Coverage

### Current Setup ✅

You have excellent coverage tools configured:

```xml
<!-- coverage.runsettings -->
<Configuration>
  <Format>opencover,cobertura,lcov,teamcity,html</Format>
  <Exclude>[*.Tests]*,[*.Test]*,[*]*.Program,[*]*.Startup</Exclude>
  <ExcludeByAttribute>GeneratedCode,ExcludeFromCodeCoverage</ExcludeByAttribute>
  <ExcludeByFile>**/Migrations/**/*.cs</ExcludeByFile>
</Configuration>
```

```xml
<!-- AlUmmahNow.Tests.csproj -->
<PackageReference Include="coverlet.collector" Version="6.0.2" />
<PackageReference Include="coverlet.msbuild" Version="6.0.2" />
```

### Running Coverage

```bash
# Basic coverage
dotnet test --collect:"XPlat Code Coverage"

# With settings file
dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings

# Specific output format
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Multiple formats
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=\"opencover,cobertura,html\"

# With threshold (fail if below 80%)
dotnet test /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line
```

### Viewing Coverage

**ReportGenerator (Recommended)**

```bash
# Install tool
dotnet tool install -g dotnet-reportgenerator-globaltool

# Generate HTML report
reportgenerator \
  -reports:"**/coverage.cobertura.xml" \
  -targetdir:"coveragereport" \
  -reporttypes:Html

# Open report
xdg-open coveragereport/index.html  # Linux
open coveragereport/index.html      # Mac
start coveragereport/index.html     # Windows
```

### Coverage Attributes

```csharp
// Exclude entire class from coverage
[ExcludeFromCodeCoverage]
public class LegacyCodeNotTested
{
    public void OldMethod() { }
}

// Exclude specific method
public class MyService
{
    [ExcludeFromCodeCoverage]
    public void DiagnosticMethod()
    {
        // Debug-only code
    }

    public async Task ImportantMethod()
    {
        // This should be tested
    }
}

// Exclude generated code
[GeneratedCode("EntityFramework", "6.0")]
public class AutoGeneratedConfiguration
{
    // EF migrations
}
```

### Meaningful Coverage Metrics

**Don't chase 100% coverage blindly!**

#### Good Coverage Targets:

| Code Type | Target Coverage | Reasoning |
|-----------|----------------|-----------|
| Business Logic | 90-95% | Critical functionality |
| Data Access | 80-85% | Covered by integration tests |
| API Controllers | 85-90% | Important entry points |
| Models/DTOs | 60-70% | Simple property classes |
| Infrastructure | 70-80% | Mocking external dependencies |

#### Focus on:

1. **Branch Coverage** - Not just line coverage
```csharp
public async Task ProcessFileAsync(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException();  // Test this branch

    if (Path.GetExtension(path) == ".mp4")
        await ProcessVideoAsync(path);  // Test this branch
    else
        await ProcessOtherAsync(path);  // Test this branch
}

// Tests should cover:
// 1. File doesn't exist (exception)
// 2. File is .mp4 (video processing)
// 3. File is other extension (other processing)
```

2. **Critical Paths** - Core business logic
```csharp
// HIGH PRIORITY - Must be tested
public async Task<bool> ProcessApprovalDecisionAsync(ApprovalDecisionRequest request)
{
    // Critical business logic
}

// MEDIUM PRIORITY
public string FormatFileSize(long bytes)
{
    // Utility method
}

// LOW PRIORITY - Can skip
public class PendingApprovalDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; }
}
```

### Coverage in CI/CD

```yaml
# .github/workflows/test.yml
- name: Run tests with coverage
  run: dotnet test --configuration Release --collect:"XPlat Code Coverage" --settings coverage.runsettings

- name: Generate coverage report
  run: |
    dotnet tool install -g dotnet-reportgenerator-globaltool
    reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:"Html;Badges"

- name: Upload coverage to Codecov
  uses: codecov/codecov-action@v3
  with:
    files: ./coverage.cobertura.xml
    fail_ci_if_error: true

- name: Comment coverage on PR
  uses: romeovs/lcov-reporter-action@v0.3.1
  with:
    lcov-file: ./coverage.info
    github-token: ${{ secrets.GITHUB_TOKEN }}
```

---

## CI/CD Integration

### ⚠️ CRITICAL GAP: No CI/CD Configuration Found

Your project lacks automated testing in CI/CD. This should be a top priority.

### GitHub Actions (Recommended)

Create: `.github/workflows/dotnet-test.yml`

```yaml
name: .NET Tests

on:
  push:
    branches: [ master, main, develop ]
  pull_request:
    branches: [ master, main, develop ]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore --configuration Release

    - name: Run Unit Tests
      run: dotnet test --no-build --configuration Release --filter "Category=Unit" --logger "trx;LogFileName=unit-tests.trx"

    - name: Run Integration Tests
      run: dotnet test --no-build --configuration Release --filter "Category=Integration" --logger "trx;LogFileName=integration-tests.trx"

    - name: Run Tests with Coverage
      run: |
        dotnet test \
          --no-build \
          --configuration Release \
          --collect:"XPlat Code Coverage" \
          --settings AlUmmahNow.Tests/coverage.runsettings \
          --results-directory ./coverage

    - name: Generate Coverage Report
      run: |
        dotnet tool install -g dotnet-reportgenerator-globaltool
        reportgenerator \
          -reports:"./coverage/**/coverage.cobertura.xml" \
          -targetdir:"./coveragereport" \
          -reporttypes:"Html;Badges"

    - name: Upload Coverage Report
      uses: actions/upload-artifact@v4
      with:
        name: coverage-report
        path: ./coveragereport

    - name: Upload Coverage to Codecov
      uses: codecov/codecov-action@v3
      with:
        files: ./coverage/**/coverage.cobertura.xml
        fail_ci_if_error: false

    - name: Test Report
      uses: dorny/test-reporter@v1
      if: always()
      with:
        name: .NET Tests
        path: '**/*.trx'
        reporter: dotnet-trx
        fail-on-error: true

  # Run tests on multiple OS
  test-matrix:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'

    - name: Test
      run: dotnet test --configuration Release
```

### Advanced GitHub Actions with Database

```yaml
name: .NET Tests with PostgreSQL

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest

    services:
      postgres:
        image: postgres:15
        env:
          POSTGRES_USER: testuser
          POSTGRES_PASSWORD: testpass
          POSTGRES_DB: testdb
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 5432:5432

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'

    - name: Run Integration Tests with Real DB
      env:
        ConnectionStrings__Default: "Host=localhost;Port=5432;Database=testdb;Username=testuser;Password=testpass"
      run: dotnet test --filter "Category=Integration"
```

### Azure DevOps Pipeline

Create: `azure-pipelines.yml`

```yaml
trigger:
  branches:
    include:
    - master
    - develop

pool:
  vmImage: 'ubuntu-latest'

variables:
  buildConfiguration: 'Release'

steps:
- task: UseDotNet@2
  displayName: 'Install .NET 8'
  inputs:
    version: '8.0.x'

- task: DotNetCoreCLI@2
  displayName: 'Restore packages'
  inputs:
    command: 'restore'
    projects: '**/*.csproj'

- task: DotNetCoreCLI@2
  displayName: 'Build solution'
  inputs:
    command: 'build'
    projects: '**/*.csproj'
    arguments: '--configuration $(buildConfiguration) --no-restore'

- task: DotNetCoreCLI@2
  displayName: 'Run tests'
  inputs:
    command: 'test'
    projects: '**/*Tests.csproj'
    arguments: '--configuration $(buildConfiguration) --no-build --collect:"XPlat Code Coverage" --settings AlUmmahNow.Tests/coverage.runsettings'

- task: PublishCodeCoverageResults@1
  displayName: 'Publish code coverage'
  inputs:
    codeCoverageTool: 'Cobertura'
    summaryFileLocation: '$(Agent.TempDirectory)/**/coverage.cobertura.xml'

- task: PublishTestResults@2
  displayName: 'Publish test results'
  condition: always()
  inputs:
    testResultsFormat: 'VSTest'
    testResultsFiles: '**/*.trx'
    failTaskOnFailedTests: true
```

### Pre-commit Hooks (Recommended)

Install Husky.Net:
```bash
dotnet tool install --global Husky
dotnet husky install
```

Create: `.husky/pre-commit`
```bash
#!/bin/sh
. "$(dirname "$0")/_/husky.sh"

echo "Running tests before commit..."
dotnet test --configuration Release --filter "Category=Unit"

if [ $? -ne 0 ]; then
    echo "Tests failed! Commit aborted."
    exit 1
fi

echo "All tests passed! ✅"
```

Make it executable:
```bash
chmod +x .husky/pre-commit
```

---

## Specific Recommendations for This Project

### Priority 1: High Impact 🔴

#### 1. Add CI/CD Pipeline Immediately

**Action Items:**
1. Create `.github/workflows/dotnet-test.yml` (see GitHub Actions section above)
2. Configure automatic test runs on every PR
3. Set up code coverage reporting with Codecov or similar
4. Add status badges to README.md

**Estimated Impact:** Catch bugs before merging, improve code quality

**Example Badge:**
```markdown
![Tests](https://github.com/YOUR_ORG/AlUmmahNowTools/workflows/.NET%20Tests/badge.svg)
[![codecov](https://codecov.io/gh/YOUR_ORG/AlUmmahNowTools/branch/master/graph/badge.svg)](https://codecov.io/gh/YOUR_ORG/AlUmmahNowTools)
```

#### 2. Add Missing Service Tests

**Currently Missing Tests:**
- `/AlUmmahNow.FileScanService/Services/FileScanEngine.cs` - Critical scanning logic
- `/AlUmmahNow.FileScanService/Services/ImportWorker.cs` - Background worker
- `/AlUmmahNow.FileScanService/Services/FileScanWorker.cs` - Background worker
- `/AlUmmahNow.FileScanService/Services/HashCalculator.cs` - Only basic tests exist

**Example Template for Missing Tests:**

```csharp
// AlUmmahNow.Tests/FileScanService/Services/FileScanEngineTests.cs
public class FileScanEngineTests : FileSystemTestBase
{
    private readonly Mock<ILogger<FileScanEngine>> _mockLogger;
    private readonly Mock<IMetadataExtractor> _mockMetadataExtractor;
    private readonly Mock<IHashCalculator> _mockHashCalculator;
    private readonly ImportDbContext _dbContext;
    private readonly FileScanEngine _engine;

    public FileScanEngineTests()
    {
        _mockLogger = CreateMockLogger<FileScanEngine>();
        _mockMetadataExtractor = new Mock<IMetadataExtractor>();
        _mockHashCalculator = new Mock<IHashCalculator>();

        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ImportDbContext(options);

        _engine = new FileScanEngine(
            _mockLogger.Object,
            _mockMetadataExtractor.Object,
            _mockHashCalculator.Object,
            _dbContext);
    }

    [Fact]
    public async Task ScanDirectoryAsync_WithVideoFiles_ScansAllFiles()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-hash");

        // Act
        var result = await _engine.ScanDirectoryAsync(
            sessionId,
            TestChannel1Directory,
            includeSubdirectories: true);

        // Assert
        result.FilesScanned.Should().BeGreaterThan(0);
        result.TotalSize.Should().BeGreaterThan(0);

        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .ToListAsync();

        scannedFiles.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ScanDirectoryAsync_DetectsDuplicateFiles()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var duplicateHash = "same-hash-value";

        _mockHashCalculator
            .Setup(x => x.CalculateFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(duplicateHash);

        // Act
        await _engine.ScanDirectoryAsync(sessionId, TestRootDirectory, true);

        // Assert
        var files = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId)
            .ToListAsync();

        var duplicates = files.Where(f => f.IsDuplicate).ToList();
        duplicates.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(".mp4")]
    [InlineData(".mkv")]
    [InlineData(".avi")]
    [InlineData(".mov")]
    public async Task ScanDirectoryAsync_SupportsVideoFormat(string extension)
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var testFile = await CreateTestVideoFileAsync(
            TestChannel1Directory,
            $"test{extension}",
            1024);

        // Act
        await _engine.ScanDirectoryAsync(sessionId, TestChannel1Directory, false);

        // Assert
        var scannedFiles = await _dbContext.ScannedVideoFiles
            .Where(f => f.ScanSessionId == sessionId && f.Extension == extension)
            .ToListAsync();

        scannedFiles.Should().ContainSingle();
    }

    [Fact]
    public async Task ScanDirectoryAsync_WithCancellation_StopsGracefully()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        // Cancel after short delay
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _engine.ScanDirectoryAsync(
                sessionId,
                TestRootDirectory,
                true,
                cts.Token));
    }

    public override async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

#### 3. Add Integration Tests for Real Database

**Create New File:** `AlUmmahNow.Tests/Integration/DatabaseIntegrationTests.cs`

```csharp
using Testcontainers.PostgreSql;

namespace AlUmmahNow.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Feature", "Database")]
public class DatabaseIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _dbContainer;
    private ImportDbContext _dbContext;

    public async Task InitializeAsync()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithDatabase("alummah_test")
            .Build();

        await _dbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        _dbContext = new ImportDbContext(options);
        await _dbContext.Database.MigrateAsync();
    }

    [Fact]
    public async Task ScanSession_SaveAndRetrieve_WorksWithRealDatabase()
    {
        // Arrange
        var session = new ScanSession
        {
            RootPath = "/test/videos",
            Name = "Integration Test Scan",
            Status = ScanStatus.Pending,
            PathHash = "test-hash-123"
        };

        // Act
        _dbContext.ScanSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Clear tracking
        _dbContext.ChangeTracker.Clear();

        // Retrieve from database
        var retrieved = await _dbContext.ScanSessions
            .FirstOrDefaultAsync(s => s.Id == session.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.RootPath.Should().Be("/test/videos");
        retrieved.Status.Should().Be(ScanStatus.Pending);
    }

    [Fact]
    public async Task Transaction_Rollback_DoesNotPersistChanges()
    {
        // Arrange
        var session = new ScanSession { RootPath = "/test" };

        // Act
        using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            _dbContext.ScanSessions.Add(session);
            await _dbContext.SaveChangesAsync();

            // Simulate error and rollback
            await transaction.RollbackAsync();
        }

        // Assert
        var count = await _dbContext.ScanSessions.CountAsync();
        count.Should().Be(0);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _dbContainer.StopAsync();
    }
}
```

**Add NuGet Package:**
```xml
<PackageReference Include="Testcontainers.PostgreSql" Version="3.6.0" />
```

### Priority 2: Medium Impact 🟡

#### 4. Add Test Categories/Traits

**Action:** Add traits to existing tests for better organization

```csharp
// Update existing tests
[Fact]
[Trait("Category", "Unit")]
[Trait("Feature", "FileWatcher")]
public async Task StartWatchAsync_WithValidRequest_ShouldCreateWatch()
{
    // Existing test code
}

[Fact]
[Trait("Category", "Integration")]
[Trait("Feature", "FileSystem")]
public async Task RealFileSystemWatcher_ShouldDetectMultipleFiles()
{
    // Existing test code
}
```

**Run filtered tests:**
```bash
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
dotnet test --filter "Feature=FileWatcher"
```

#### 5. Improve Test Data Builders

**Create:** `AlUmmahNow.Tests/Builders/TestDataBuilders.cs`

```csharp
namespace AlUmmahNow.Tests.Builders;

public class ScanSessionBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _rootPath = "/default/path";
    private string _name = "Test Session";
    private ScanStatus _status = ScanStatus.Pending;
    private int _filesFound = 0;
    private long _totalSize = 0;
    private DateTime _createdAt = DateTime.UtcNow;

    public static ScanSessionBuilder Create() => new();

    public ScanSessionBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public ScanSessionBuilder WithRootPath(string rootPath)
    {
        _rootPath = rootPath;
        return this;
    }

    public ScanSessionBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public ScanSessionBuilder WithStatus(ScanStatus status)
    {
        _status = status;
        return this;
    }

    public ScanSessionBuilder AsCompleted()
    {
        _status = ScanStatus.Completed;
        return this;
    }

    public ScanSessionBuilder WithFilesFound(int count)
    {
        _filesFound = count;
        return this;
    }

    public ScanSessionBuilder WithTotalSize(long size)
    {
        _totalSize = size;
        return this;
    }

    public ScanSession Build() => new()
    {
        Id = _id,
        RootPath = _rootPath,
        Name = _name,
        Status = _status,
        FilesFound = _filesFound,
        TotalSize = _totalSize,
        CreatedAt = _createdAt
    };
}

public class PendingApprovalBuilder
{
    private Guid _id = Guid.NewGuid();
    private Guid _channelWatchId;
    private ApprovalStatus _status = ApprovalStatus.Pending;
    private string _fileName = "test.mp4";
    private string _filePath = "/test/test.mp4";
    private long _fileSize = 1024 * 1024;
    private string _extension = ".mp4";
    private int _priority = 3;

    public static PendingApprovalBuilder Create() => new();

    public PendingApprovalBuilder ForChannel(Guid channelWatchId)
    {
        _channelWatchId = channelWatchId;
        return this;
    }

    public PendingApprovalBuilder WithStatus(ApprovalStatus status)
    {
        _status = status;
        return this;
    }

    public PendingApprovalBuilder WithFileName(string fileName)
    {
        _fileName = fileName;
        _extension = Path.GetExtension(fileName);
        return this;
    }

    public PendingApprovalBuilder WithFileSize(long size)
    {
        _fileSize = size;
        return this;
    }

    public PendingApprovalBuilder AsApproved()
    {
        _status = ApprovalStatus.Approved;
        return this;
    }

    public PendingApprovalBuilder AsHighPriority()
    {
        _priority = 1;
        return this;
    }

    public PendingApproval Build() => new()
    {
        Id = _id,
        ChannelWatchId = _channelWatchId,
        Status = _status,
        FileName = _fileName,
        FilePath = _filePath,
        FileSize = _fileSize,
        Extension = _extension,
        Priority = _priority
    };
}

// Usage Example:
[Fact]
public async Task GetHighPriorityApprovals_ReturnsOnlyHighPriority()
{
    // Arrange
    var channel = new ChannelWatch { ChannelName = "Test" };
    _dbContext.ChannelWatches.Add(channel);
    await _dbContext.SaveChangesAsync();

    var highPriority = PendingApprovalBuilder.Create()
        .ForChannel(channel.Id)
        .AsHighPriority()
        .WithFileName("important.mp4")
        .Build();

    var normalPriority = PendingApprovalBuilder.Create()
        .ForChannel(channel.Id)
        .WithFileName("normal.mp4")
        .Build();

    _dbContext.PendingApprovals.AddRange(highPriority, normalPriority);
    await _dbContext.SaveChangesAsync();

    // Act
    var result = await _service.GetHighPriorityApprovalsAsync();

    // Assert
    result.Should().ContainSingle()
        .Which.FileName.Should().Be("important.mp4");
}
```

#### 6. Add Performance Tests

**Create:** `AlUmmahNow.Tests/Performance/PerformanceTests.cs`

```csharp
[Trait("Category", "Performance")]
public class FileScanPerformanceTests : FileSystemTestBase
{
    [Fact]
    public async Task ScanLargeDirectory_CompletesWithinTimeLimit()
    {
        // Arrange
        var fileCount = 1000;
        var tasks = Enumerable.Range(1, fileCount)
            .Select(i => CreateTestVideoFileAsync(
                TestChannel1Directory,
                $"video{i}.mp4",
                1024 * 1024));
        await Task.WhenAll(tasks);

        var stopwatch = Stopwatch.StartNew();

        // Act
        await _scanEngine.ScanDirectoryAsync(
            Guid.NewGuid(),
            TestChannel1Directory,
            includeSubdirectories: false);

        stopwatch.Stop();

        // Assert
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ConcurrentScans_HandleMultipleSessions()
    {
        // Arrange
        var sessionCount = 10;
        var sessions = Enumerable.Range(1, sessionCount)
            .Select(_ => Guid.NewGuid())
            .ToList();

        var stopwatch = Stopwatch.StartNew();

        // Act
        var scanTasks = sessions.Select(sessionId =>
            _scanEngine.ScanDirectoryAsync(
                sessionId,
                TestChannel1Directory,
                includeSubdirectories: false));

        await Task.WhenAll(scanTasks);
        stopwatch.Stop();

        // Assert
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));
    }
}
```

### Priority 3: Nice to Have 🟢

#### 7. Add Architecture Tests

Install: `NetArchTest.Rules`

```csharp
using NetArchTest.Rules;

[Trait("Category", "Architecture")]
public class ArchitectureTests
{
    private const string DomainNamespace = "AlUmmahNow.Data";
    private const string ServiceNamespace = "AlUmmahNow.FileScanService";

    [Fact]
    public void Services_ShouldNotDependOnInfrastructure()
    {
        var result = Types.InAssembly(typeof(FileWatchService).Assembly)
            .That().ResideInNamespace(ServiceNamespace)
            .ShouldNot().HaveDependencyOn("AlUmmahNow.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Controllers_ShouldHaveControllerSuffix()
    {
        var result = Types.InAssembly(typeof(FileWatchService).Assembly)
            .That().HaveNameEndingWith("Controller")
            .Should().BePublic()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Services_ShouldBeSealed_OrAbstract()
    {
        var result = Types.InAssembly(typeof(FileWatchService).Assembly)
            .That().HaveNameEndingWith("Service")
            .Should().BeSealedOrAbstract()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
```

#### 8. Add Mutation Testing

**Install:** `Stryker.NET`

```bash
dotnet tool install -g dotnet-stryker

# Run mutation testing
dotnet stryker
```

**Configuration:** `stryker-config.json`
```json
{
  "stryker-config": {
    "project": "AlUmmahNow.Tests.csproj",
    "solution": "../AlUmmahNow.sln",
    "test-projects": ["AlUmmahNow.Tests.csproj"],
    "reporters": ["html", "progress"],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 50
    }
  }
}
```

---

## Summary & Action Plan

### Quick Wins (Do This Week)

1. ✅ Create GitHub Actions workflow for automated testing
2. ✅ Add test categories/traits to existing tests
3. ✅ Add HashCalculator comprehensive tests
4. ✅ Create FileScanEngine tests

### Short Term (Next Month)

1. 🔄 Set up Testcontainers for real database testing
2. 🔄 Add missing service tests (ImportWorker, FileScanWorker)
3. 🔄 Implement test data builders
4. 🔄 Add performance benchmarks

### Long Term (Next Quarter)

1. 📋 Achieve 80%+ code coverage
2. 📋 Add mutation testing
3. 📋 Implement architecture tests
4. 📋 Create comprehensive API integration tests

### Testing Checklist for New Features

When adding new features, ensure:

- [ ] Unit tests for business logic (90%+ coverage)
- [ ] Integration tests for database operations
- [ ] Async patterns tested with cancellation
- [ ] Error cases and edge cases covered
- [ ] Performance tests for critical paths
- [ ] Tests run in CI/CD pipeline
- [ ] Code coverage meets threshold
- [ ] All tests pass before merge

---

## Additional Resources

### Books
- **"xUnit Test Patterns" by Gerard Meszaros** - Comprehensive testing patterns
- **"The Art of Unit Testing" by Roy Osherove** - .NET-specific testing
- **"Working Effectively with Legacy Code" by Michael Feathers** - Testing existing code

### Online Resources
- [xUnit Documentation](https://xunit.net/)
- [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [Microsoft Testing Best Practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)
- [Testcontainers for .NET](https://dotnet.testcontainers.org/)

### Tools
- **ReSharper** - Test runner and coverage
- **Rider** - JetBrains IDE with excellent test support
- **NCrunch** - Continuous test runner
- **dotCover** - JetBrains coverage tool
- **Fine Code Coverage** - VS Code extension

---

**Document Version:** 1.0
**Last Updated:** 2025-10-12
**Maintainer:** Development Team
**Review Schedule:** Quarterly

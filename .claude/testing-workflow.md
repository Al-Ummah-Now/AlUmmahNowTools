# Testing Workflow - Quick Reference for AI Coding

## Quick Commands

```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory:"./TestResults"

# Generate coverage report
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"TestResults/CoverageReport" -reporttypes:Html

# View coverage report
start TestResults/CoverageReport/index.html  # Windows
open TestResults/CoverageReport/index.html   # macOS

# Run specific tests
dotnet test --filter "FullyQualifiedName~TestClassName"

# Watch tests (continuous)
dotnet watch test
```

## AI Coding Workflow

### 1. Before Writing Code
- [ ] Check current test coverage
- [ ] Identify which component to work on
- [ ] Review existing test patterns in `AlUmmahNow.Tests/`

### 2. Writing Code
- [ ] Implement feature in main project
- [ ] Create corresponding test file in test project
- [ ] Follow AAA pattern: Arrange, Act, Assert
- [ ] Use existing infrastructure (FileSystemTestBase, etc.)

### 3. Test Structure
```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedBehavior()
{
    // Arrange - Setup
    var dependency = new Mock<IDependency>();
    var sut = new SystemUnderTest(dependency.Object);

    // Act - Execute
    var result = await sut.MethodAsync();

    // Assert - Verify
    result.Should().NotBeNull();
    dependency.Verify(d => d.Method(), Times.Once);
}
```

### 4. After Writing Code
- [ ] Run tests: `dotnet test`
- [ ] Check coverage: generate report
- [ ] Ensure >80% coverage for new code
- [ ] Fix failing tests
- [ ] Commit code + tests together

## Testing Patterns by Component

### SignalR Hub Testing
```csharp
// Mock setup
_mockClients.Setup(c => c.Group("group-id")).Returns(_mockClientProxy.Object);
_mockContext.Setup(c => c.ConnectionId).Returns("connection-id");

// Verify client notifications
_mockClientProxy.Verify(
    c => c.SendCoreAsync("EventName", It.IsAny<object[]>(), default),
    Times.Once);
```

### SignalR Client Testing
```csharp
// Test disconnected state
var client = new FileScanSignalRClient("http://localhost:9999");
await client.Invoking(c => c.StartScanAsync("path"))
    .Should().ThrowAsync<InvalidOperationException>();
```

### Service Testing with Dependencies
```csharp
// Mock dependencies
var mockLogger = new Mock<ILogger<Service>>();
var mockRepository = new Mock<IRepository>();

// Setup behaviors
mockRepository.Setup(r => r.GetAsync(It.IsAny<string>()))
    .ReturnsAsync(new Data());

var service = new Service(mockLogger.Object, mockRepository.Object);
```

### File System Testing
```csharp
public class MyTests : FileSystemTestBase
{
    [Fact]
    public async Task TestMethod()
    {
        // Use pre-created directories
        var videoPath = Path.Combine(TestChannel1Directory, "video1.mp4");

        // Access helper methods
        var content = await GetTestFileTextAsync(TestChannel1Directory, "video1.info.json");

        // Files are cleaned up automatically
    }
}
```

### Database Testing
```csharp
// Use in-memory database
var options = new DbContextOptionsBuilder<ImportDbContext>()
    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
    .Options;
var dbContext = new ImportDbContext(options);

// Each test gets isolated database
```

## Coverage Goals

- **New code**: 80%+ coverage
- **Critical paths**: 100% coverage
- **Core logic**: 90%+ coverage
- **UI/Infrastructure**: 60%+ coverage

## Common Test Categories

### Happy Path
```csharp
[Fact]
public async Task Method_WithValidInput_ReturnsExpectedResult()
```

### Edge Cases
```csharp
[Fact]
public async Task Method_WithEmptyInput_HandlesGracefully()

[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
public async Task Method_WithInvalidInput_HandlesCorrectly(string input)
```

### Error Handling
```csharp
[Fact]
public async Task Method_WhenDependencyThrows_HandlesException()

[Fact]
public async Task Method_WithInvalidInput_ThrowsArgumentException()
```

### Async/Cancellation
```csharp
[Fact]
public async Task Method_WithCancellation_ThrowsOperationCanceledException()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await sut.Invoking(s => s.MethodAsync(cts.Token))
        .Should().ThrowAsync<OperationCanceledException>();
}
```

## Assertion Patterns

### FluentAssertions
```csharp
// Null checks
result.Should().NotBeNull();
result.Should().BeNull();

// Value checks
result.Should().Be(expected);
result.Should().BeGreaterThan(0);

// String checks
result.Should().NotBeNullOrEmpty();
result.Should().Contain("substring");
result.Should().MatchRegex("^[a-f0-9]{64}$");

// Collection checks
list.Should().HaveCount(5);
list.Should().Contain(item => item.Id == 1);

// Exception checks
await action.Should().ThrowAsync<Exception>()
    .WithMessage("*expected message*");

// Time checks
dateTime.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
```

### Moq Verification
```csharp
// Verify method called
mock.Verify(m => m.Method(It.IsAny<string>()), Times.Once);

// Verify with specific arguments
mock.Verify(m => m.Method("exact-value"), Times.AtLeastOnce);

// Verify never called
mock.Verify(m => m.Method(), Times.Never);

// Verify log message
mockLogger.Verify(
    x => x.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("expected")),
        null,
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once);
```

## Test Organization

```
AlUmmahNow.Tests/
├── Data/
│   └── Models/              # Model tests
├── FileScanService/
│   ├── Hubs/               # SignalR Hub tests
│   └── Services/           # Service tests
├── DataImporter/
│   └── Services/           # Client tests
├── Infrastructure/
│   └── FileSystemTestBase.cs  # Test base classes
├── Repository/             # Repository tests
├── Storage/                # Storage client tests
└── Video/                  # Video client tests
```

## When to Use Which Test Type

### Unit Tests (Preferred)
- Isolated component testing
- Mock dependencies
- Fast execution
- Most tests should be unit tests

### Integration Tests
- Cross-component interaction
- Real database/network
- Slower execution
- Use sparingly

### End-to-End Tests
- Full system flow
- Real infrastructure
- Slowest execution
- Use for critical paths only

## Troubleshooting

### Tests Fail to Build
```bash
# Check project references
dotnet build AlUmmahNow.Tests/AlUmmahNow.Tests.csproj

# Restore packages
dotnet restore
```

### Tests Are Flaky
- Check for shared state between tests
- Use unique database names per test
- Verify async/await usage
- Check for timing issues

### Coverage Not Updating
```bash
# Clean old coverage files
rm -rf TestResults/

# Run with fresh coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory:"./TestResults"
```

## Resources

- **Full Guide**: `TESTING_GUIDE.md`
- **Coverage Summary**: `TEST_COVERAGE_SUMMARY.md`
- **Coverage Report**: `TestResults/CoverageReport/index.html`

---

*Quick Reference for AI-Assisted Development*
*Last Updated: 2025-10-06*

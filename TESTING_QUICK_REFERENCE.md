# Testing Quick Reference Card

## Quick Commands

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage" --settings AlUmmahNow.Tests/coverage.runsettings

# Run specific category
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
dotnet test --filter "Feature=FileWatcher"

# Run specific test
dotnet test --filter "FullyQualifiedName~FileWatchServiceTests.StartWatchAsync"

# Generate HTML coverage report
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:Html
xdg-open coveragereport/index.html  # Linux
open coveragereport/index.html      # Mac
start coveragereport/index.html     # Windows

# Watch mode (re-run on file changes)
dotnet watch test
```

## Test Structure Template

```csharp
[Fact]
[Trait("Category", "Unit")]  // or "Integration"
[Trait("Feature", "FeatureName")]
public async Task MethodName_Scenario_ExpectedResult()
{
    // Arrange - Set up test data and dependencies
    var input = new Request { /* ... */ };
    var mockService = new Mock<IService>();
    mockService.Setup(x => x.MethodAsync(It.IsAny<string>()))
        .ReturnsAsync("result");

    // Act - Call the method under test
    var result = await _sut.ProcessAsync(input);

    // Assert - Verify the outcome
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
    mockService.Verify(x => x.MethodAsync("expected"), Times.Once);
}
```

## Common FluentAssertions

```csharp
// Equality
result.Should().Be(expected);
result.Should().NotBe(unexpected);
result.Should().BeEquivalentTo(expected);

// Nullability
result.Should().BeNull();
result.Should().NotBeNull();

// Strings
name.Should().Be("expected");
name.Should().Contain("substring");
name.Should().StartWith("prefix");
name.Should().EndWith("suffix");
name.Should().NotBeNullOrEmpty();

// Numbers
count.Should().Be(10);
count.Should().BeGreaterThan(5);
count.Should().BeLessThan(20);
count.Should().BeInRange(5, 15);
count.Should().BePositive();

// Collections
list.Should().NotBeEmpty();
list.Should().HaveCount(3);
list.Should().Contain(item);
list.Should().ContainSingle();
list.Should().OnlyContain(x => x.IsValid);
list.Should().BeInAscendingOrder(x => x.Priority);

// Exceptions
await act.Should().ThrowAsync<InvalidOperationException>();
await act.Should().ThrowAsync<ArgumentNullException>()
    .WithMessage("*parameter*");

// DateTime
timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
timestamp.Should().BeAfter(DateTime.UtcNow.AddDays(-1));

// Types
obj.Should().BeOfType<SpecificType>();
obj.Should().BeAssignableTo<IInterface>();

// Chaining
result.Should().NotBeNull()
    .And.BeOfType<MyType>()
    .Which.Property.Should().Be("value");
```

## Moq Patterns

```csharp
// Setup
var mock = new Mock<IService>();

// Simple return
mock.Setup(x => x.GetName()).Returns("John");

// Async return
mock.Setup(x => x.GetDataAsync()).ReturnsAsync(data);

// Argument matching
mock.Setup(x => x.Process(It.IsAny<string>())).Returns(true);
mock.Setup(x => x.Process(It.Is<string>(s => s.Length > 5))).Returns(true);
mock.Setup(x => x.Process("specific")).Returns(true);

// Throw exceptions
mock.Setup(x => x.DoSomething()).Throws<InvalidOperationException>();
mock.Setup(x => x.DoSomethingAsync()).ThrowsAsync(new IOException());

// Callback
mock.Setup(x => x.Process(It.IsAny<string>()))
    .Callback<string>(value => capturedValue = value)
    .Returns(true);

// Verification
mock.Verify(x => x.DoSomething(), Times.Once);
mock.Verify(x => x.DoSomething(), Times.Never);
mock.Verify(x => x.DoSomething(), Times.AtLeastOnce);
mock.Verify(x => x.DoSomething(), Times.Exactly(3));

// Verify with argument
mock.Verify(x => x.Process(It.IsAny<string>()), Times.Once);
mock.Verify(x => x.Process("specific"), Times.Once);

// Properties
mock.Setup(x => x.Property).Returns(value);
mock.SetupProperty(x => x.Property, initialValue);
```

## xUnit Attributes

```csharp
[Fact]                          // Single test
[Theory]                        // Parameterized test
[InlineData(1, 2, 3)]          // Theory data
[Trait("Category", "Unit")]    // Test categorization
[Skip("Reason")]               // Skip test temporarily
```

## Theory Data Patterns

```csharp
// Inline data
[Theory]
[InlineData(1, 2, 3)]
[InlineData(5, 10, 15)]
public void Add_ReturnsSum(int a, int b, int expected)
{
    var result = a + b;
    result.Should().Be(expected);
}

// Member data
public static IEnumerable<object[]> TestData =>
    new List<object[]>
    {
        new object[] { 1, 2, 3 },
        new object[] { 5, 10, 15 }
    };

[Theory]
[MemberData(nameof(TestData))]
public void Add_ReturnsSum(int a, int b, int expected) { }

// Class data
public class TestDataGenerator : IEnumerable<object[]>
{
    public IEnumerator<object[]> GetEnumerator()
    {
        yield return new object[] { 1, 2, 3 };
        yield return new object[] { 5, 10, 15 };
    }
}

[Theory]
[ClassData(typeof(TestDataGenerator))]
public void Add_ReturnsSum(int a, int b, int expected) { }
```

## Async Testing

```csharp
// Basic async test
[Fact]
public async Task ProcessAsync_ReturnsResult()
{
    var result = await _service.ProcessAsync();
    result.Should().NotBeNull();
}

// Test cancellation
[Fact]
public async Task ProcessAsync_WhenCancelled_ThrowsException()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAsync<OperationCanceledException>(
        () => _service.ProcessAsync(cts.Token));
}

// Test timeout
[Fact]
public async Task ProcessAsync_CompletesWithinTimeout()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var result = await _service.ProcessAsync(cts.Token);
    result.Should().NotBeNull();
}

// Wait for event
[Fact]
public async Task WaitForEvent_UsingTaskCompletionSource()
{
    var tcs = new TaskCompletionSource<EventArgs>();
    _service.EventOccurred += (s, e) => tcs.TrySetResult(e);

    _service.TriggerEvent();

    var eventArgs = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
    eventArgs.Should().NotBeNull();
}

// Test concurrent operations
[Fact]
public async Task ConcurrentOperations_AllSucceed()
{
    var tasks = Enumerable.Range(1, 10)
        .Select(i => _service.ProcessAsync(i));

    var results = await Task.WhenAll(tasks);

    results.Should().HaveCount(10);
    results.Should().OnlyContain(r => r.Success);
}
```

## Database Testing

```csharp
// In-memory database
public DatabaseTests()
{
    var options = new DbContextOptionsBuilder<MyDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;
    _dbContext = new MyDbContext(options);
}

// Testcontainers (real database)
private readonly PostgreSqlContainer _container;

public async Task InitializeAsync()
{
    _container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .Build();
    await _container.StartAsync();

    var options = new DbContextOptionsBuilder<MyDbContext>()
        .UseNpgsql(_container.GetConnectionString())
        .Options;
    _dbContext = new MyDbContext(options);
    await _dbContext.Database.MigrateAsync();
}
```

## Common Test Patterns

```csharp
// Builder Pattern for Test Data
var user = UserBuilder.Create()
    .WithEmail("test@example.com")
    .WithRole("Admin")
    .Build();

// Object Mother Pattern
var user = TestUsers.CreateAdmin();
var user = TestUsers.CreateRegularUser();

// Test Fixture (shared setup)
public class DatabaseFixture : IDisposable
{
    public ImportDbContext DbContext { get; }

    public DatabaseFixture()
    {
        var options = new DbContextOptionsBuilder<ImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        DbContext = new ImportDbContext(options);
    }

    public void Dispose() => DbContext.Dispose();
}

public class MyTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public MyTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }
}
```

## Debugging Tests

```csharp
// Output to test console
private readonly ITestOutputHelper _output;

public MyTests(ITestOutputHelper output)
{
    _output = output;
}

[Fact]
public void MyTest()
{
    _output.WriteLine($"Debug info: {value}");
}

// Breakpoint in VS Code
// Add to launch.json:
{
    "name": ".NET Core Attach",
    "type": "coreclr",
    "request": "attach"
}

// Run test in debug mode
dotnet test --filter "FullyQualifiedName~MyTest" --no-build
```

## Coverage Exclusions

```csharp
// Exclude entire class
[ExcludeFromCodeCoverage]
public class LegacyCode { }

// Exclude method
public class MyClass
{
    [ExcludeFromCodeCoverage]
    public void DiagnosticMethod() { }
}

// Exclude generated code
[GeneratedCode("Tool", "Version")]
public class AutoGenerated { }
```

## CI/CD Quick Check

```bash
# Before committing, run:
dotnet build --configuration Release
dotnet test --configuration Release --filter "Category=Unit"

# Check coverage:
dotnet test --collect:"XPlat Code Coverage" /p:Threshold=60
```

## Common Mistakes to Avoid

```csharp
// ❌ DON'T: async void tests
[Fact]
public async void BadTest() { }

// ✅ DO: async Task tests
[Fact]
public async Task GoodTest() { }

// ❌ DON'T: .Result or .Wait()
var result = _service.GetDataAsync().Result;

// ✅ DO: await
var result = await _service.GetDataAsync();

// ❌ DON'T: Multiple assertions without context
Assert.True(result.IsValid);
Assert.Equal(5, result.Count);

// ✅ DO: Descriptive assertions
result.IsValid.Should().BeTrue("validation should pass for valid input");
result.Count.Should().Be(5, "all items should be returned");

// ❌ DON'T: Testing implementation details
mock.Verify(x => x.InternalHelperMethod());

// ✅ DO: Test behavior
result.Should().Be(expectedOutput);
```

## Test Smells to Watch For

- Tests that are too long (>50 lines) - split into multiple tests
- Tests that depend on execution order - use IAsyncLifetime properly
- Tests with magic numbers - use constants or named values
- Tests that test multiple things - one assertion per test (generally)
- Slow tests (>1 second) - consider mocking expensive operations
- Brittle tests that break on unrelated changes - avoid over-mocking

## Resources

- **Full Guide:** [TESTING_BEST_PRACTICES.md](./TESTING_BEST_PRACTICES.md)
- **Summary:** [TESTING_SUMMARY.md](./TESTING_SUMMARY.md)
- **Example Tests:** [AlUmmahNow.Tests/Examples/](./AlUmmahNow.Tests/Examples/)
- **xUnit Docs:** https://xunit.net/
- **FluentAssertions:** https://fluentassertions.com/
- **Moq:** https://github.com/moq/moq4

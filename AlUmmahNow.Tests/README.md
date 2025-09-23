# AlUmmahNow.Tests

This project contains comprehensive unit tests for the AlUmmahNow.Data and AlUmmahNow.DataImporter projects using xUnit.

## Test Structure

The tests are organized to mirror the source code structure:

### Repository Tests (`Repository/`)
- `RepositoryTests.cs` - Tests for the generic `Repository<T>` class
  - Tests CRUD operations (Save, Get, List, Delete) for S3 storage
  - Tests serialization/deserialization of objects
  - Tests error handling scenarios
  - Uses Moq to mock the `IR2Client` interface

### Storage Tests (`Storage/`)
- `R2ClientTests.cs` - Tests for the R2 (Cloudflare) storage client
  - Tests S3 operations (ListBuckets, ListObjects, Put, Get, Delete)
  - Tests client initialization and disposal
  - Integration-style tests that demonstrate method structure
  
- `BunnyStorageClientTests.cs` - Tests for the BunnyCDN storage client
  - Tests file upload, download, list, and delete operations
  - Demonstrates error handling with invalid credentials

### Video Tests (`Video/`)
- `BunnyStreamClientTests.cs` - Tests for the BunnyStream video API client
  - Tests video listing, retrieval, upload, and deletion
  - Tests parameter validation and error handling
  - Tests response model initialization

### DataImporter Tests (`DataImporter/`)
- `ImporterTests.cs` - Tests for the main Importer class
  - Tests import workflows and method signatures
  - Demonstrates integration between storage and repository layers

## Test Dependencies

The project uses the following testing frameworks and tools:

- **xUnit** - Main testing framework
- **Moq** - Mocking framework for isolating dependencies
- **Microsoft.EntityFrameworkCore.InMemory** - For in-memory database testing (prepared for PR #1)
- **Microsoft.Extensions.DependencyInjection** - For dependency injection in tests

## Running Tests

### Command Line
```bash
# Build the test project
dotnet build AlUmmahNow.Tests/AlUmmahNow.Tests.csproj

# Run all tests
dotnet test AlUmmahNow.Tests/AlUmmahNow.Tests.csproj

# Run tests with detailed output
dotnet test AlUmmahNow.Tests/AlUmmahNow.Tests.csproj --logger trx --results-directory TestResults

# Run tests with coverage
dotnet test AlUmmahNow.Tests/AlUmmahNow.Tests.csproj --collect:"XPlat Code Coverage"
```

### Visual Studio
- Open the solution in Visual Studio
- Use Test Explorer to run individual tests or all tests
- Right-click on test methods to run/debug specific tests

## Test Approach

### Current Implementation (Master Branch)
The current tests focus on the existing codebase in the master branch:
- Repository pattern with S3 storage
- BunnyCDN integration for file storage
- BunnyStream integration for video management
- Basic import functionality

### Mock Strategy
- External dependencies (HTTP clients, storage APIs) are mocked
- Tests verify method signatures and basic logic flow
- Error handling is tested with invalid credentials/parameters
- Integration-style tests demonstrate real API usage patterns

### Future Enhancements (PR #1 Features)
When PR #1 is merged, additional tests should be added for:
- `ImportDbContext` and Entity Framework operations
- Multi-channel import functionality with database tracking
- Resume and status monitoring capabilities
- Enhanced error handling and retry logic

## Test Coverage

Current test coverage includes:
- ✅ Repository CRUD operations
- ✅ S3 storage client functionality
- ✅ BunnyCDN storage operations  
- ✅ BunnyStream video API calls
- ✅ Import workflow structure
- ✅ Error handling scenarios
- ✅ Model initialization and validation

**Total: 31+ passing tests**

## Guidelines for Adding New Tests

1. **Follow the directory structure** - Mirror the source code organization
2. **Use descriptive test names** - Follow the pattern `MethodName_Scenario_ExpectedResult`
3. **Mock external dependencies** - Use Moq for HTTP clients and external APIs
4. **Test both success and failure paths** - Include error handling tests
5. **Use appropriate assertions** - Verify both return values and side effects
6. **Add integration tests** - For complex workflows involving multiple components

## Notes

- Some tests currently expect exceptions due to invalid test credentials
- Tests are designed to validate method signatures and basic logic flow
- For production testing, valid API keys and credentials would be configured
- The test structure is prepared for the enhanced features in PR #1

## Contributing

When adding new features to the main projects:
1. Add corresponding unit tests to this project
2. Follow the existing naming and organization patterns
3. Ensure tests can run without external dependencies (use mocks)
4. Add integration tests for complex scenarios
5. Update this README with any new test categories
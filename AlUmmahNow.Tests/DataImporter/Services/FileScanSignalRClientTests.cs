using Xunit;
using FluentAssertions;
using AlUmmahNow.DataImporter.Services;
using Microsoft.AspNetCore.SignalR.Client;

namespace AlUmmahNow.Tests.DataImporter.Services;

/// <summary>
/// Unit tests for FileScanSignalRClient
/// Tests cover connection management, event handling, and error scenarios
/// </summary>
public class FileScanSignalRClientTests
{
    #region Connection Tests

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

        await client.DisposeAsync();
    }

    [Fact]
    public async Task GetServiceStatus_WhenDisconnected_ReturnsDisconnected()
    {
        // Arrange
        await using var client = new FileScanSignalRClient("http://localhost:5000");

        // Act
        var status = client.GetServiceStatus();

        // Assert
        status.Should().Be(ServiceStatus.Disconnected);
    }

    [Fact]
    public async Task IsConnected_WhenNotConnected_ReturnsFalse()
    {
        // Arrange
        await using var client = new FileScanSignalRClient("http://localhost:5000");

        // Act
        var isConnected = client.IsConnected;

        // Assert
        isConnected.Should().BeFalse();
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task StartScanAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");

        // Act & Assert
        await client.Invoking(c => c.StartScanAsync("C:\\Test"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Not connected to FileScan service*");

        await client.DisposeAsync();
    }

    [Fact]
    public async Task GetStatusAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");
        var sessionId = Guid.NewGuid();

        // Act & Assert
        await client.Invoking(c => c.GetStatusAsync(sessionId))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Not connected to FileScan service*");

        await client.DisposeAsync();
    }

    [Fact]
    public async Task GetAllSessionsAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");

        // Act & Assert
        await client.Invoking(c => c.GetAllSessionsAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Not connected to FileScan service*");

        await client.DisposeAsync();
    }

    [Fact]
    public async Task CancelScanAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");
        var sessionId = Guid.NewGuid();

        // Act & Assert
        await client.Invoking(c => c.CancelScanAsync(sessionId))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Not connected to FileScan service*");

        await client.DisposeAsync();
    }

    #endregion

    #region Event Tests

    [Fact]
    public async Task ServiceStatusChanged_OnConnectionFailure_ShouldRaiseEvent()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:9999");
        var eventRaised = false;
        ServiceStatusEventArgs? capturedArgs = null;

        client.ServiceStatusChanged += (sender, args) =>
        {
            eventRaised = true;
            capturedArgs = args;
        };

        // Act
        await client.ConnectAsync();

        // Assert
        // Give a small delay for event to potentially fire
        await Task.Delay(100);

        // Since connection failed, we should have received a status change
        eventRaised.Should().BeTrue();
        capturedArgs.Should().NotBeNull();
        capturedArgs!.Status.Should().Be(ServiceStatus.NotAvailable);
        capturedArgs.Message.Should().Contain("not available");

        await client.DisposeAsync();
    }

    [Fact]
    public void ErrorOccurred_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");
        var eventRaised = false;

        void handler(object? sender, string error) => eventRaised = true;

        // Act
        client.ErrorOccurred += handler;
        client.ErrorOccurred -= handler;

        // Assert - no exception should be thrown
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void ScanProgressReceived_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");
        var eventRaised = false;

        void handler(object? sender, FileScanProgressEventArgs args) => eventRaised = true;

        // Act
        client.ScanProgressReceived += handler;
        client.ScanProgressReceived -= handler;

        // Assert - no exception should be thrown
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void ScanCompleted_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");
        var eventRaised = false;

        void handler(object? sender, Guid sessionId) => eventRaised = true;

        // Act
        client.ScanCompleted += handler;
        client.ScanCompleted -= handler;

        // Assert - no exception should be thrown
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void ScanFailed_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");
        var eventRaised = false;

        void handler(object? sender, (Guid sessionId, string error) data) => eventRaised = true;

        // Act
        client.ScanFailed += handler;
        client.ScanFailed -= handler;

        // Assert - no exception should be thrown
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void ScanStarted_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");
        var eventRaised = false;

        void handler(object? sender, Guid sessionId) => eventRaised = true;

        // Act
        client.ScanStarted += handler;
        client.ScanStarted -= handler;

        // Assert - no exception should be thrown
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void ScanCancelled_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");
        var eventRaised = false;

        void handler(object? sender, Guid sessionId) => eventRaised = true;

        // Act
        client.ScanCancelled += handler;
        client.ScanCancelled -= handler;

        // Assert - no exception should be thrown
        eventRaised.Should().BeFalse();
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public async Task DisposeAsync_WhenNotConnected_ShouldNotThrow()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");

        // Act & Assert
        await client.Invoking(c => c.DisposeAsync().AsTask())
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_MultipleTimesMultipleCalls_ShouldNotThrow()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");

        // Act & Assert
        await client.DisposeAsync();
        await client.Invoking(c => c.DisposeAsync().AsTask())
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_ShouldNotThrow()
    {
        // Arrange
        var client = new FileScanSignalRClient("http://localhost:5000");

        // Act & Assert
        await client.Invoking(c => c.DisconnectAsync())
            .Should().NotThrowAsync();
    }

    #endregion

    #region URL Handling Tests

    [Fact]
    public void Constructor_WithTrailingSlash_ShouldHandleCorrectly()
    {
        // Arrange & Act
        var client = new FileScanSignalRClient("http://localhost:5000/");

        // Assert - should not throw and should be created
        client.Should().NotBeNull();
        client.GetServiceStatus().Should().Be(ServiceStatus.Disconnected);
    }

    [Fact]
    public void Constructor_WithoutTrailingSlash_ShouldHandleCorrectly()
    {
        // Arrange & Act
        var client = new FileScanSignalRClient("http://localhost:5000");

        // Assert - should not throw and should be created
        client.Should().NotBeNull();
        client.GetServiceStatus().Should().Be(ServiceStatus.Disconnected);
    }

    #endregion

    #region ServiceStatus Tests

    [Theory]
    [InlineData("http://localhost:5000")]
    [InlineData("https://example.com:8080")]
    [InlineData("http://192.168.1.100:5555")]
    public async Task GetServiceStatus_InitialState_ReturnsDisconnected(string url)
    {
        // Arrange
        await using var client = new FileScanSignalRClient(url);

        // Act
        var status = client.GetServiceStatus();

        // Assert
        status.Should().Be(ServiceStatus.Disconnected);
    }

    #endregion
}

/// <summary>
/// Event arguments tests for FileScanProgressEventArgs
/// </summary>
public class FileScanProgressEventArgsTests
{
    [Fact]
    public void FileScanProgressEventArgs_CanBeConstructed()
    {
        // Arrange & Act
        var args = new FileScanProgressEventArgs
        {
            SessionId = Guid.NewGuid(),
            FilesScanned = 10,
            CurrentFile = "test.mp4",
            ProgressPercentage = 25.5,
            CurrentOperation = "Scanning..."
        };

        // Assert
        args.SessionId.Should().NotBeEmpty();
        args.FilesScanned.Should().Be(10);
        args.CurrentFile.Should().Be("test.mp4");
        args.ProgressPercentage.Should().Be(25.5);
        args.CurrentOperation.Should().Be("Scanning...");
    }

    [Fact]
    public void FileScanProgressEventArgs_DefaultValues_ShouldBeValid()
    {
        // Arrange & Act
        var args = new FileScanProgressEventArgs();

        // Assert
        args.SessionId.Should().Be(Guid.Empty);
        args.FilesScanned.Should().Be(0);
        args.CurrentFile.Should().Be(string.Empty);
        args.ProgressPercentage.Should().Be(0);
        args.CurrentOperation.Should().Be(string.Empty);
    }
}

/// <summary>
/// Event arguments tests for ServiceStatusEventArgs
/// </summary>
public class ServiceStatusEventArgsTests
{
    [Fact]
    public void ServiceStatusEventArgs_CanBeConstructed()
    {
        // Arrange & Act
        var args = new ServiceStatusEventArgs
        {
            Status = ServiceStatus.Connected,
            Message = "Connected successfully"
        };

        // Assert
        args.Status.Should().Be(ServiceStatus.Connected);
        args.Message.Should().Be("Connected successfully");
    }

    [Fact]
    public void ServiceStatusEventArgs_DefaultValues_ShouldBeValid()
    {
        // Arrange & Act
        var args = new ServiceStatusEventArgs();

        // Assert
        args.Status.Should().Be(ServiceStatus.NotAvailable);
        args.Message.Should().Be(string.Empty);
    }
}

/// <summary>
/// Tests for ServiceStatus enum
/// </summary>
public class ServiceStatusTests
{
    [Fact]
    public void ServiceStatus_HasExpectedValues()
    {
        // Assert
        Enum.IsDefined(typeof(ServiceStatus), ServiceStatus.NotAvailable).Should().BeTrue();
        Enum.IsDefined(typeof(ServiceStatus), ServiceStatus.Disconnected).Should().BeTrue();
        Enum.IsDefined(typeof(ServiceStatus), ServiceStatus.Reconnecting).Should().BeTrue();
        Enum.IsDefined(typeof(ServiceStatus), ServiceStatus.Connected).Should().BeTrue();
        Enum.IsDefined(typeof(ServiceStatus), ServiceStatus.Stopped).Should().BeTrue();
    }

    [Fact]
    public void ServiceStatus_AllValuesShouldBeDistinct()
    {
        // Arrange
        var values = Enum.GetValues<ServiceStatus>();

        // Act
        var distinctValues = values.Distinct().ToArray();

        // Assert
        distinctValues.Should().HaveCount(values.Length);
    }
}

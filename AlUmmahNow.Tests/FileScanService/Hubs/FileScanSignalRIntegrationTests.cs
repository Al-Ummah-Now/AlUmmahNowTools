using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models.FileScanning;
using AlUmmahNow.DataImporter.Services;
using AlUmmahNow.FileScanService.Services;
using AlUmmahNow.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FileScanProgram = AlUmmahNow.FileScanService.Program;

namespace AlUmmahNow.Tests.FileScanService.Hubs;

/// <summary>
/// Live in-process Kestrel integration tests for the consolidated FileScanService SignalR hub
/// (<c>/scanHub</c>), startup wiring (<see cref="FileScanProgram.ConfigureLoopbackEndpoint"/>,
/// <see cref="FileScanProgram.ConfigureSignalRCors"/>, <see cref="FileScanProgram.MapFileScanEndpoints"/>),
/// and <see cref="FileScanSignalRClient"/> without SQL Server.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FileScanSignalRIntegrationTests : IAsyncLifetime
{
    private readonly SqliteImportDb _db = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"aun-signalr-int-{Guid.NewGuid():N}");
    private IHost? _host;
    private string _baseUrl = string.Empty;
    private int _boundPort;

    private static int PickUnusedLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);

        var configuredPort = PickUnusedLoopbackPort();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:Http:Url"] = $"http://127.0.0.1:{configuredPort}"
            })
            .Build();
        _boundPort = FileScanProgram.ResolvePort(config);

        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseKestrel(options => FileScanProgram.ConfigureLoopbackEndpoint(options, _boundPort))
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureServices(services =>
                    {
                        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));
                        services.AddRouting();
                        services.AddCors(FileScanProgram.ConfigureSignalRCors);
                        services.AddSignalR();
                        services.AddScoped<ImportDbContext>(_ => _db.NewContext());
                        services.AddSingleton<IFileWatchService>(_ => new Mock<IFileWatchService>().Object);
                        services.AddScoped<IApprovalService, ApprovalService>();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseCors();
                        app.UseEndpoints(endpoints => FileScanProgram.MapFileScanEndpoints(endpoints, _boundPort));
                    });
            })
            .Build();

        await _host.StartAsync();

        var addresses = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        _baseUrl = addresses!.Addresses.First();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _host.StopAsync(cts.Token); } catch (OperationCanceledException) { }
            _host.Dispose();
        }

        _db.Dispose();

        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ConnectAndHealth_WithConsolidatedHost_BindsResolvedLoopbackPortAndSucceeds()
    {
        var boundUri = new Uri(_baseUrl);
        boundUri.Host.Should().Be("127.0.0.1");
        boundUri.Port.Should().Be(_boundPort);

        using var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.Zero };
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.ConnectionClose = true;
        var health = await http.GetAsync($"{_baseUrl}/health");
        health.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("port").GetInt32().Should().Be(_boundPort);
        payload.RootElement.GetProperty("signalRHub").GetString().Should().Be("/scanHub");

        await using var client = new FileScanSignalRClient(_baseUrl);
        var statuses = new List<ServiceStatus>();
        client.ServiceStatusChanged += (_, e) => statuses.Add(e.Status);

        var connected = await client.ConnectAsync();

        connected.Should().BeTrue();
        client.IsConnected.Should().BeTrue();
        client.GetServiceStatus().Should().Be(ServiceStatus.Connected);
        statuses.Should().Contain(ServiceStatus.Connected);
    }

    [Fact]
    public async Task StartStatusListAndCancelScan_OverLiveSignalR_PersistsAndBroadcastsEvents()
    {
        await using var client = new FileScanSignalRClient(_baseUrl);
        var startedTcs = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledTcs = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.ScanStarted += (_, id) => startedTcs.TrySetResult(id);
        client.ScanCancelled += (_, id) => cancelledTcs.TrySetResult(id);

        (await client.ConnectAsync()).Should().BeTrue();

        var sessionId = await client.StartScanAsync(_tempDir, includeSubdirectories: true);
        sessionId.Should().NotBeEmpty();

        var startedId = await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        startedId.Should().Be(sessionId);

        var status = await client.GetStatusAsync(sessionId);
        status.SessionId.Should().Be(sessionId);
        status.RootPath.Should().Be(_tempDir);
        status.Status.Should().Be(ScanStatus.Pending.ToString());

        var sessions = await client.GetAllSessionsAsync();
        sessions.Should().ContainSingle(s => s.Id == sessionId && s.RootPath == _tempDir);

        await client.CancelScanAsync(sessionId);

        var cancelledId = await cancelledTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancelledId.Should().Be(sessionId);

        var afterCancel = await client.GetStatusAsync(sessionId);
        afterCancel.Status.Should().Be(ScanStatus.Cancelled.ToString());

        // Cancelling an already-cancelled scan is rejected by the hub
        await client.Invoking(c => c.CancelScanAsync(sessionId))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartScan_WithMissingDirectory_FailsOverSignalRAndRaisesError()
    {
        await using var client = new FileScanSignalRClient(_baseUrl);
        var errors = new List<string>();
        client.ErrorOccurred += (_, msg) => errors.Add(msg);

        (await client.ConnectAsync()).Should().BeTrue();

        var missingDir = Path.Combine(_tempDir, "does-not-exist");

        await client.Invoking(c => c.StartScanAsync(missingDir))
            .Should().ThrowAsync<InvalidOperationException>();

        errors.Should().NotBeEmpty();
    }
}

[Trait("Category", "Unit")]
public sealed class FileScanServicePortTests
{
    [Theory]
    // Single-source loopback URLs
    [InlineData("http://localhost:5123", null, null, 5123)]
    [InlineData("http://127.0.0.1:5200;https://localhost:5201", null, null, 5200)]
    [InlineData("https://localhost:47922;http://localhost:47923", null, null, 47923)]
    [InlineData("https://localhost:47922", null, null, 47922)]
    [InlineData("http://[::1]:5400", null, null, 5400)]
    [InlineData(null, "http://localhost:5300", null, 5300)]
    [InlineData(null, null, "http://localhost:47923", 47923)]
    // Precedence: Kestrel:Endpoints:Http:Url > ASPNETCORE_URLS > FileScanService:Url > 5000
    [InlineData("http://localhost:5100", "http://localhost:5300", "http://localhost:5400", 5300)]
    [InlineData("http://localhost:5100", null, "http://localhost:5400", 5100)]
    // Wildcard, non-loopback, bare integer, dynamic :0, and malformed values are rejected
    [InlineData(null, "http://*:5305", null, 5000)]
    [InlineData("http://+:5305", null, null, 5000)]
    [InlineData("http://0.0.0.0:5305", null, null, 5000)]
    [InlineData(null, "http://192.168.1.50:5305", "http://localhost:5400", 5400)]
    [InlineData("5310", null, null, 5000)]
    [InlineData(null, "5310", null, 5000)]
    [InlineData("http://localhost:0", null, null, 5000)]
    [InlineData(null, "http://localhost:0", null, 5000)]
    [InlineData(null, "not-a-url", null, 5000)]
    [InlineData(null, null, null, 5000)]
    public void ResolvePort_EnforcesLoopbackPrecedenceAndFallsBackTo5000(
        string? aspnetUrls,
        string? kestrelUrl,
        string? serviceUrl,
        int expectedPort)
    {
        var values = new Dictionary<string, string?>();
        if (aspnetUrls is not null) values["ASPNETCORE_URLS"] = aspnetUrls;
        if (kestrelUrl is not null) values["Kestrel:Endpoints:Http:Url"] = kestrelUrl;
        if (serviceUrl is not null) values["FileScanService:Url"] = serviceUrl;

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        FileScanProgram.ResolvePort(config).Should().Be(expectedPort);
    }
}

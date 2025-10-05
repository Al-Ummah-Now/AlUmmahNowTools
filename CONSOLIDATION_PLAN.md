# FileScan Service Consolidation Plan

## Executive Summary

This document outlines the plan to consolidate **FileScanApi** (Web API) and **FileScanService** (Windows Service) into a single, more efficient Windows Service with SignalR for real-time communication.

## Current Architecture Problems

### Existing Setup (Overcomplicated)
```
????????????????     HTTP REST      ????????????????
?   Clients    ? ??????????????????? ? FileScanApi  ?
? (CLI/GUI)    ?                     ?  (Web API)   ?
????????????????                     ????????????????
                                            ?
                                     Polling Database
                                            ?
                                            ?
                                     ????????????????
                                     ?  Database    ?
                                     ????????????????
                                            ?
                                     Polling Database
                                            ?
                                            ?
                                     ????????????????
                                     ?FileScanService?
                                     ? (Worker)     ?
                                     ????????????????
```

### Issues with Current Design
- ? Two separate services to deploy and manage
- ? Database polling creates unnecessary overhead
- ? Extra HTTP layer adds latency
- ? More configuration complexity (two endpoints, two processes)
- ? Services need to stay synchronized
- ? No real-time updates (polling interval delays)
- ? Clients can't detect service status immediately

## Proposed Architecture (Simplified)

### New Consolidated Design
```
????????????????????                     ????????????????????????
?   DataImporter   ?                     ?   VisualImporter     ?
?     (CLI)        ?    SignalR          ?      (WinUI)         ?
?                  ???????????????????????   Real-time UI       ?
????????????????????                     ????????????????????????
         ?                                          ?
         ?           SignalR Hub                    ?
         ?      (In Windows Service)                ?
         ????????????????????????????????????????????
                        ?
                        ?
              ???????????????????????
              ?  FileScanService    ?
              ?  (Windows Service)  ?
              ?                     ?
              ?  ? SignalR Hub      ?
              ?  ? File Scanning    ?
              ?  ? Session Mgmt     ?
              ?  ? Real-time Push   ?
              ???????????????????????
                        ?
                        ?
              ???????????????????????
              ?     Database        ?
              ?  (ImportDbContext)  ?
              ???????????????????????
```

### Benefits of New Design
? **Single Service**: One Windows Service to deploy and manage  
? **Real-time Communication**: SignalR pushes updates immediately (no polling)  
? **Simpler Configuration**: One endpoint for everything  
? **Less Overhead**: No HTTP REST layer, no database polling  
? **Better Performance**: Direct push notifications vs. polling  
? **Fewer Resources**: Less CPU, memory, and database load  
? **Instant Feedback**: Clients know immediately if service is down  
? **Type Safety**: Shared DTOs between service and clients  

## Implementation Plan

### Phase 1: Prepare FileScanService for SignalR

**Estimated Time**: 4 hours

#### 1.1 Add Required NuGet Packages
```bash
cd AlUmmahNow.FileScanService
dotnet add package Microsoft.AspNetCore.SignalR
dotnet add package Microsoft.AspNetCore.SignalR.Protocols.Json
```

#### 1.2 Update Program.cs
**File**: `AlUmmahNow.FileScanService/Program.cs`

Add SignalR hosting capabilities:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Add SignalR
builder.Services.AddSignalR();

// Add Windows Service support
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AlUmmah FileScan Service";
});

// Add database context
builder.Services.AddDbContext<ImportDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add hosted service for file scanning
builder.Services.AddHostedService<FileScanWorker>();

// Add file scan engine
builder.Services.AddSingleton<IFileScanEngine, FileScanEngine>();

// Configure CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

// Map SignalR hub
app.MapHub<FileScanHub>("/scanHub");

await app.RunAsync();
```

#### 1.3 Create SignalR Hub
**File**: `AlUmmahNow.FileScanService/Hubs/FileScanHub.cs`

```csharp
using Microsoft.AspNetCore.SignalR;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace AlUmmahNow.FileScanService.Hubs;

public class FileScanHub : Hub
{
    private readonly ImportDbContext _dbContext;
    private readonly IFileScanEngine _scanEngine;

    public FileScanHub(ImportDbContext dbContext, IFileScanEngine scanEngine)
    {
        _dbContext = dbContext;
        _scanEngine = scanEngine;
    }

    /// <summary>
    /// Client requests a new scan
    /// </summary>
    public async Task<Guid> StartScan(string folderPath, bool includeSubdirectories = true)
    {
        var session = new ScanSession
        {
            RootPath = folderPath,
            Status = ScanStatus.Pending,
            IncludeSubdirectories = includeSubdirectories
        };

        _dbContext.ScanSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        await _scanEngine.QueueScanAsync(session.Id);
        await Clients.All.SendAsync("ScanQueued", session.Id, folderPath);

        return session.Id;
    }

    /// <summary>
    /// Client requests scan status
    /// </summary>
    public async Task<ScanStatusDto> GetScanStatus(Guid sessionId)
    {
        var session = await _dbContext.ScanSessions
            .Include(s => s.Files)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
            throw new HubException("Session not found");

        return new ScanStatusDto
        {
            SessionId = session.Id,
            Status = session.Status.ToString(),
            RootPath = session.RootPath,
            FilesScanned = session.Files.Count,
            TotalSize = session.Files.Sum(f => f.FileSize),
            ProgressPercentage = session.ProgressPercentage,
            CurrentOperation = session.CurrentOperation
        };
    }

    /// <summary>
    /// Client requests all sessions
    /// </summary>
    public async Task<List<ScanSessionDto>> GetAllSessions()
    {
        var sessions = await _dbContext.ScanSessions
            .OrderByDescending(s => s.CreatedAt)
            .Take(50)
            .Select(s => new ScanSessionDto
            {
                Id = s.Id,
                RootPath = s.RootPath,
                Status = s.Status.ToString(),
                CreatedAt = s.CreatedAt,
                CompletedAt = s.CompletedAt,
                FilesScanned = s.FilesScanned
            })
            .ToListAsync();

        return sessions;
    }

    /// <summary>
    /// Client requests to cancel a scan
    /// </summary>
    public async Task CancelScan(Guid sessionId)
    {
        var session = await _dbContext.ScanSessions.FindAsync(sessionId);
        if (session == null)
            throw new HubException("Session not found");

        session.Status = ScanStatus.Cancelled;
        await _dbContext.SaveChangesAsync();

        await Clients.All.SendAsync("ScanCancelled", sessionId);
    }
}
```

#### 1.4 Create DTO Classes
**File**: `AlUmmahNow.FileScanService/Models/ScanDtos.cs`

```csharp
namespace AlUmmahNow.FileScanService.Models;

public class ScanStatusDto
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public int FilesScanned { get; set; }
    public long TotalSize { get; set; }
    public double ProgressPercentage { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
}

public class ScanSessionDto
{
    public Guid Id { get; set; }
    public string RootPath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int FilesScanned { get; set; }
}

public class ScanProgressData
{
    public int FilesScanned { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
}
```

#### 1.5 Update Worker for SignalR Progress
**File**: `AlUmmahNow.FileScanService/Worker.cs`

```csharp
using Microsoft.AspNetCore.SignalR;
using AlUmmahNow.FileScanService.Hubs;

public class FileScanWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<FileScanHub> _hubContext;
    private readonly ILogger<FileScanWorker> _logger;

    public FileScanWorker(
        IServiceProvider serviceProvider,
        IHubContext<FileScanHub> hubContext,
        ILogger<FileScanWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FileScan Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
            var scanEngine = scope.ServiceProvider.GetRequiredService<IFileScanEngine>();

            var pendingScans = await dbContext.ScanSessions
                .Where(s => s.Status == ScanStatus.Pending)
                .ToListAsync(stoppingToken);

            foreach (var session in pendingScans)
            {
                try
                {
                    session.Status = ScanStatus.InProgress;
                    await dbContext.SaveChangesAsync(stoppingToken);

                    await _hubContext.Clients.All.SendAsync(
                        "ScanStarted", 
                        session.Id, 
                        session.RootPath,
                        cancellationToken: stoppingToken);

                    scanEngine.ProgressChanged += async (sender, progress) =>
                    {
                        await _hubContext.Clients.All.SendAsync(
                            "ScanProgress",
                            session.Id,
                            new
                            {
                                FilesScanned = progress.FilesScanned,
                                CurrentFile = progress.CurrentFile,
                                ProgressPercentage = progress.ProgressPercentage,
                                CurrentOperation = progress.CurrentOperation
                            },
                            stoppingToken);

                        session.FilesScanned = progress.FilesScanned;
                        session.ProgressPercentage = progress.ProgressPercentage;
                        session.CurrentOperation = progress.CurrentOperation;
                        await dbContext.SaveChangesAsync(stoppingToken);
                    };

                    await scanEngine.ScanDirectoryAsync(
                        session.RootPath,
                        session.Id,
                        stoppingToken);

                    session.Status = ScanStatus.Completed;
                    session.CompletedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(stoppingToken);

                    await _hubContext.Clients.All.SendAsync(
                        "ScanCompleted",
                        session.Id,
                        session.FilesScanned,
                        cancellationToken: stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scan failed: {SessionId}", session.Id);

                    session.Status = ScanStatus.Failed;
                    session.ErrorMessage = ex.Message;
                    await dbContext.SaveChangesAsync(stoppingToken);

                    await _hubContext.Clients.All.SendAsync(
                        "ScanFailed",
                        session.Id,
                        ex.Message,
                        cancellationToken: stoppingToken);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
```

### Phase 2: Create SignalR Client for DataImporter

**Estimated Time**: 3 hours

#### 2.1 Add SignalR Client Package
```bash
cd AlUmmahNow.DataImporter
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

#### 2.2 Create SignalR Client Class
**File**: `AlUmmahNow.DataImporter/Services/FileScanSignalRClient.cs`

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;

namespace AlUmmahNow.DataImporter.Services;

public class FileScanSignalRClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly string _hubUrl;

    public event EventHandler<ScanProgressEventArgs>? ProgressReceived;
    public event EventHandler<Guid>? ScanCompleted;
    public event EventHandler<(Guid sessionId, string error)>? ScanFailed;
    public event EventHandler<Guid>? ScanStarted;

    public FileScanSignalRClient(string serviceUrl)
    {
        _hubUrl = $"{serviceUrl}/scanHub";
        
        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<Guid, string>("ScanStarted", (sessionId, path) =>
        {
            ScanStarted?.Invoke(this, sessionId);
        });

        _connection.On<Guid, object>("ScanProgress", (sessionId, progress) =>
        {
            var progressData = JsonSerializer.Deserialize<ScanProgressData>(
                JsonSerializer.Serialize(progress));
            
            ProgressReceived?.Invoke(this, new ScanProgressEventArgs
            {
                SessionId = sessionId,
                FilesScanned = progressData?.FilesScanned ?? 0,
                CurrentFile = progressData?.CurrentFile ?? "",
                ProgressPercentage = progressData?.ProgressPercentage ?? 0
            });
        });

        _connection.On<Guid, int>("ScanCompleted", (sessionId, fileCount) =>
        {
            ScanCompleted?.Invoke(this, sessionId);
        });

        _connection.On<Guid, string>("ScanFailed", (sessionId, error) =>
        {
            ScanFailed?.Invoke(this, (sessionId, error));
        });
    }

    public async Task ConnectAsync()
    {
        await _connection.StartAsync();
        Console.WriteLine($"? Connected to FileScan service at {_hubUrl}");
    }

    public async Task<Guid> StartScanAsync(string folderPath, bool includeSubdirectories = true)
    {
        return await _connection.InvokeAsync<Guid>(
            "StartScan", 
            folderPath, 
            includeSubdirectories);
    }

    public async Task<ScanStatusDto> GetStatusAsync(Guid sessionId)
    {
        return await _connection.InvokeAsync<ScanStatusDto>("GetScanStatus", sessionId);
    }

    public async Task<List<ScanSessionDto>> GetAllSessionsAsync()
    {
        return await _connection.InvokeAsync<List<ScanSessionDto>>("GetAllSessions");
    }

    public async Task CancelScanAsync(Guid sessionId)
    {
        await _connection.InvokeAsync("CancelScan", sessionId);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}

public class ScanProgressEventArgs : EventArgs
{
    public Guid SessionId { get; set; }
    public int FilesScanned { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
}
```

#### 2.3 Update Program.cs
**File**: `AlUmmahNow.DataImporter/Program.cs`

Replace `FileScanApiClient` usage with `FileScanSignalRClient`:

```csharp
// Old way (HTTP REST)
// var apiClient = new FileScanApiClient(serviceUrl, httpClient);

// New way (SignalR)
var serviceUrl = configuration["FileScanService:Url"] ?? "http://localhost:5000";
await using var client = new FileScanSignalRClient(serviceUrl);

// Subscribe to real-time events
client.ProgressReceived += (sender, progress) =>
{
    Console.WriteLine($"Progress: {progress.ProgressPercentage:F2}% - {progress.CurrentFile}");
};

client.ScanCompleted += (sender, sessionId) =>
{
    Console.WriteLine($"? Scan completed: {sessionId}");
};

client.ScanFailed += (sender, args) =>
{
    Console.WriteLine($"? Scan failed: {args.error}");
};

await client.ConnectAsync();

if (folderPath != null)
{
    Console.WriteLine($"Starting scan of: {folderPath}");
    var sessionId = await client.StartScanAsync(folderPath);
    Console.WriteLine($"Scan session created: {sessionId}");
    Console.WriteLine("Monitoring progress... (Press Ctrl+C to exit)");
    
    await Task.Delay(Timeout.Infinite, cancellationToken);
}
```

### Phase 3: Update VisualImporter for SignalR

**Estimated Time**: 4 hours

#### 3.1 Add SignalR Client Package
```bash
cd AlUmmahNow.VisualImporter
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

#### 3.2 Create SignalR Service
**File**: `AlUmmahNow.VisualImporter/Services/FileScanSignalRService.cs`

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.UI.Dispatching;

namespace AlUmmahNow.VisualImporter.Services;

public class FileScanSignalRService : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly DispatcherQueue _dispatcher;

    public event EventHandler<ScanProgressEventArgs>? ProgressReceived;
    public event EventHandler<Guid>? ScanCompleted;
    public event EventHandler<(Guid sessionId, string error)>? ScanFailed;

    public FileScanSignalRService(string serviceUrl, DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        
        _connection = new HubConnectionBuilder()
            .WithUrl($"{serviceUrl}/scanHub")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<Guid, object>("ScanProgress", (sessionId, progress) =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                var progressData = System.Text.Json.JsonSerializer.Deserialize<ScanProgressData>(
                    System.Text.Json.JsonSerializer.Serialize(progress));
                
                ProgressReceived?.Invoke(this, new ScanProgressEventArgs
                {
                    SessionId = sessionId,
                    FilesScanned = progressData?.FilesScanned ?? 0,
                    CurrentFile = progressData?.CurrentFile ?? "",
                    ProgressPercentage = progressData?.ProgressPercentage ?? 0
                });
            });
        });

        _connection.On<Guid, int>("ScanCompleted", (sessionId, fileCount) =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                ScanCompleted?.Invoke(this, sessionId);
            });
        });

        _connection.On<Guid, string>("ScanFailed", (sessionId, error) =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                ScanFailed?.Invoke(this, (sessionId, error));
            });
        });
    }

    public async Task ConnectAsync() => await _connection.StartAsync();
    
    public async Task<Guid> StartScanAsync(string folderPath) => 
        await _connection.InvokeAsync<Guid>("StartScan", folderPath, true);
    
    public async Task<ScanStatusDto> GetStatusAsync(Guid sessionId) =>
        await _connection.InvokeAsync<ScanStatusDto>("GetScanStatus", sessionId);
    
    public async Task<List<ScanSessionDto>> GetAllSessionsAsync() =>
        await _connection.InvokeAsync<List<ScanSessionDto>>("GetAllSessions");

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
```

#### 3.3 Update ViewModel
**File**: `AlUmmahNow.VisualImporter/ViewModels/DatabaseMainViewModel.cs`

Replace API client with SignalR:

```csharp
private readonly FileScanSignalRService _signalRService;

public DatabaseMainViewModel(FileScanSignalRService signalRService, ...)
{
    _signalRService = signalRService;

    _signalRService.ProgressReceived += (sender, progress) =>
    {
        CurrentFile = progress.CurrentFile;
        ProgressPercentage = progress.ProgressPercentage;
        FilesScanned = progress.FilesScanned;
    };

    _signalRService.ScanCompleted += (sender, sessionId) =>
    {
        StatusMessage = "Scan completed!";
        IsScanning = false;
    };

    _ = _signalRService.ConnectAsync();
}

[RelayCommand]
private async Task StartScanAsync()
{
    IsScanning = true;
    StatusMessage = "Starting scan...";

    var sessionId = await _signalRService.StartScanAsync(SelectedFolderPath);
    StatusMessage = $"Scanning... {sessionId}";
}
```

### Phase 4: Remove FileScanApi

**Estimated Time**: 1 hour

#### 4.1 Remove Project from Solution
```bash
# Remove from solution file
dotnet sln remove AlUmmahNow.FileScanApi/AlUmmahNow.FileScanApi.csproj
```

#### 4.2 Delete Project Directory
```bash
# After confirming all references are removed
rm -rf AlUmmahNow.FileScanApi/
```

#### 4.3 Update Documentation
- Update README.md to remove FileScanApi references
- Update ARCHITECTURE.md with new SignalR architecture
- Update .github/copilot-instructions.md

### Phase 5: Testing and Validation

**Estimated Time**: 4 hours

#### 5.1 Unit Testing
- Test SignalR hub methods
- Test connection handling
- Test error scenarios

#### 5.2 Integration Testing
- Test DataImporter CLI with real scans
- Test VisualImporter GUI with real scans
- Test multiple concurrent clients
- Test reconnection scenarios

#### 5.3 Performance Testing
- Compare polling vs. push performance
- Measure latency improvements
- Check resource usage (CPU, memory)

## Configuration Changes

### Old Configuration (Two Services)
```json
{
  "FileScanApi": {
    "BaseUrl": "http://localhost:5001"
  }
}
```

### New Configuration (One Service)
```json
{
  "FileScanService": {
    "Url": "http://localhost:5000"
  }
}
```

## Deployment Changes

### Old Deployment
1. Deploy FileScanApi (Web API)
2. Deploy FileScanService (Windows Service)
3. Configure both services
4. Ensure both are running

### New Deployment
1. Deploy FileScanService (consolidated)
2. Configure single service
3. Install as Windows Service

### Windows Service Installation
```powershell
# Stop old services
Stop-Service "AlUmmah FileScan Service"
Stop-Service "AlUmmah FileScan API"

# Uninstall old services
sc.exe delete "AlUmmah FileScan Service"
sc.exe delete "AlUmmah FileScan API"

# Install new consolidated service
sc.exe create "AlUmmah FileScan Service" binPath="C:\Path\To\AlUmmahNow.FileScanService.exe"
sc.exe start "AlUmmah FileScan Service"
```

## Rollback Plan

If issues arise during consolidation:

1. **Keep FileScanApi project** in a `archive/` folder initially
2. **Tag current stable version** before starting
3. **Test in development** before deploying to production
4. **Staged rollout**: CLI first, then GUI
5. **Monitor logs** closely during first week

## Timeline

| Phase | Duration | Start Date | End Date |
|-------|----------|------------|----------|
| Phase 1: Prepare Service | 4 hours | TBD | TBD |
| Phase 2: Update DataImporter | 3 hours | TBD | TBD |
| Phase 3: Update VisualImporter | 4 hours | TBD | TBD |
| Phase 4: Remove FileScanApi | 1 hour | TBD | TBD |
| Phase 5: Testing | 4 hours | TBD | TBD |
| **Total** | **16 hours** | **TBD** | **TBD** |

## Success Metrics

? **Functional Requirements**
- All scan operations work via SignalR
- Real-time progress updates work
- Multiple clients can connect simultaneously
- Reconnection works automatically

? **Performance Requirements**
- Progress updates have <100ms latency
- No database polling overhead
- Memory usage reduced by 30%+
- CPU usage reduced by 20%+

? **Operational Requirements**
- Single service to manage
- Simplified configuration
- Easy to troubleshoot
- Good logging and monitoring

## Risk Assessment

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| SignalR connection issues | High | Low | Implement automatic reconnection |
| Breaking changes for clients | Medium | Medium | Staged rollout, keep API temporarily |
| Performance regressions | Medium | Low | Thorough testing before deployment |
| Windows Service installation fails | High | Low | Test installation scripts thoroughly |
| Data loss during migration | High | Very Low | Database unchanged, no data migration |

## Next Steps

1. **Review and approve this plan**
2. **Schedule development time** (16 hours estimated)
3. **Create feature branch** `feature/signalr-consolidation`
4. **Begin Phase 1** implementation
5. **Test each phase** before proceeding
6. **Document changes** as implemented
7. **Deploy to development** environment first
8. **Deploy to production** after validation

---

**Document Version**: 1.0  
**Last Updated**: 2025-01-21  
**Author**: Development Team  
**Status**: Proposed - Awaiting Approval

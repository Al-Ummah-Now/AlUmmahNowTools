# AlUmmahNowTools - Developer Instructions

AlUmmahNowTools is a collection of .NET 8 applications for managing video and image content with cloud storage services (Cloudflare R2, BunnyCDN, DeepL translation). The solution contains multiple tools: DataImporter (SignalR client), BunnyBulkUploader, VisualImporter (GUI), plus shared libraries.

**Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.**

## Working Effectively

### Prerequisites and Bootstrap
- .NET 8.0 SDK is required and already installed (version 8.0.119)
- All projects target .NET 8.0 with nullable reference types enabled
- No additional SDKs or tools need to be installed

### Build and Test Commands
- Build the entire solution: `dotnet build` - takes ~30 seconds, produces warnings but succeeds. NEVER CANCEL.
- Build release version: `dotnet build --configuration Release` - takes ~3 seconds. NEVER CANCEL.
- Run tests: `dotnet test` - completes immediately (no tests exist in the repository)
- Restore packages: `dotnet restore` - takes ~18 seconds for first run. NEVER CANCEL.

### Publishing Applications
- Publish DataImporter: `dotnet publish AlUmmahNow.DataImporter/AlUmmahNow.DataImporter.csproj --configuration Release --runtime linux-x64 --self-contained` - takes ~10 seconds. NEVER CANCEL.
- Publish BunnyBulkUploader: `dotnet publish AlUmmahNow.BunnyBulkUploader/AlUmmahNow.BunnyBulkUploader.csproj --configuration Release --runtime linux-x64 --self-contained` - takes ~10 seconds. NEVER CANCEL.
- Published executables are self-contained and located in `bin/Release/net8.0/linux-x64/publish/`

### Configuration Requirements
Applications require user secrets configuration before they can run successfully:

#### Required User Secrets for DataImporter (File Scanning via SignalR):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true"
  },
  "FileScanService": {
    "Url": "http://localhost:5000"
  }
}
```

#### Required User Secrets for BunnyBulkUploader:
```json
{
  "BunnyVideo": {
    "Zone": "your-bunny-video-zone-name",
    "Key": "your-bunny-video-api-key", 
    "Region": "your-bunny-video-region"
  }
}
```

#### Required User Secrets for VisualImporter (Full Import):
```json
{
  "R2DB": {
    "S3Api": "https://your-cloudflare-r2-endpoint",
    "S3Key": "your-r2-access-key", 
    "S3Secret": "your-r2-secret-key"
  },
  "BunnyVideo": {
    "Zone": "your-bunny-video-zone-name",
    "Key": "your-bunny-video-api-key",
    "Region": "your-bunny-video-region"
  },
  "BunnyImage": {
    "Zone": "your-bunny-image-zone-name", 
    "Key": "your-bunny-image-api-key",
    "Region": "your-bunny-image-region"
  },
  "BunnyStream": {
    "Key": "your-bunny-stream-api-key"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true"
  }
}
```

**IMPORTANT: Applications will fail to start without proper configuration. They cannot display help without valid service initialization.**

## Project Structure

### Solution Components
- **AlUmmahNow.sln** - Main solution file containing 7 projects
- **AlUmmahNow.Data** - Shared data library (.NET 8 library)
  - Storage clients for BunnyCDN and Cloudflare R2
  - Translation services using DeepL (for future content processing)
  - Video streaming integration with Bunny Stream
  - Models for videos, images, storage objects, and file scanning
- **AlUmmahNow.Importer** - Centralized import logic library (.NET 8 library)
  - VideoImporter class with session management and progress tracking
  - SimpleVideoUploader for basic bulk uploads
  - ImportCommandExecutor for CLI integration
  - Reduces code duplication across console and GUI applications
- **AlUmmahNow.DataImporter** - Console application for file scanning and session management (SignalR Client)
  - UserSecretsId: `4803ba67-724e-4477-aeb0-1d38aa664f85`
  - **Primary Purpose**: SignalR client for file scanning operations
  - Communicates with FileScanService via SignalR Hub for real-time file discovery
  - **NOT for direct video processing** - delegates to FileScanService
  - Uses FileScanSignalRClient for real-time communication
- **AlUmmahNow.BunnyBulkUploader** - Console application for bulk uploading to BunnyCDN
  - UserSecretsId: `4de61621-8c00-4020-a0b6-0ff95676f865`
  - Requires folder path parameter: `-f|--folder <path>`
  - Uses AlUmmahNow.Importer library for upload functionality
- **AlUmmahNow.VisualImporter** - WinUI 3 visual interface for importing video content
  - UserSecretsId: `4803ba67-724e-4477-aeb0-1d38aa664f85`
  - Modern WinUI application with session management and real-time progress tracking
  - Uses AlUmmahNow.Importer library for core import operations
  - **Currently uses local FileDiscoveryService** (not SignalR - planned for future)
- **AlUmmahNow.FileScanService** - Background Windows Service for file scanning (SignalR Hub Host)
  - **This service does ALL the actual file scanning work**
  - Hosts SignalR Hub (FileScanHub) for real-time communication
  - Contains FileScanEngine for actual scanning operations
  - Background worker (FileScanWorker) processes scan queues
  - All clients communicate with this service, never scan files directly

### Key Dependencies
- **AWSSDK.S3** (3.7.415.26) - Cloudflare R2 S3-compatible storage
- **BunnyCDN.Net.Storage** (1.0.4) - BunnyCDN storage client
- **DeepL.net** (1.15.0) - Translation services (for future content processing)
- **McMaster.Extensions.CommandLineUtils** (4.1.1) - CLI argument parsing
- **RestSharp** (112.1.0) - HTTP client for API calls
- **ShellProgressBar** (5.2.0) - Progress indication during operations
- **Microsoft.EntityFrameworkCore** (9.0.9) - Database operations
- **Microsoft.WindowsAppSDK** (1.8.250916003) - WinUI 3 framework
- **Microsoft.AspNetCore.SignalR.Client** (9.0.9) - SignalR client for real-time communication
- **Microsoft.AspNetCore.SignalR.Core** (1.1.0) - SignalR server components

## CLI Usage

### DataImporter Commands (File Scanning via SignalR)
```bash
# Build and run DataImporter (requires FileScanService running with SignalR hub)
cd AlUmmahNow.DataImporter
dotnet run -- --help                           # Shows usage
dotnet run -- --list-sessions                  # List scan sessions
dotnet run -- -f "/path/to/video/files"        # Start folder scan (via SignalR)
dotnet run -- --status "session-guid"          # Check scan status (via SignalR)
dotnet run -- --start-import "session-guid"    # Start import after scan
dotnet run -- --wait                           # Wait for scan completion with real-time updates
```

### BunnyBulkUploader Commands  
```bash
# Build and run BunnyBulkUploader (requires user secrets)
cd AlUmmahNow.BunnyBulkUploader
dotnet run -- --help                    # Shows usage (fails without config)
dotnet run -- -f "/path/to/video/files" # Upload videos from folder
```

### VisualImporter Usage
```bash
# Build and run VisualImporter (WinUI application)
cd AlUmmahNow.VisualImporter
dotnet build --configuration Debug /p:Platform=x64
.\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\AlUmmahNow.VisualImporter.exe
```

### FileScanService (Background Service)
```bash
# Run the FileScanService (hosts SignalR hub)
cd AlUmmahNow.FileScanService
dotnet run
# Service listens on http://localhost:5000 by default
# SignalR hub available at http://localhost:5000/scanHub
```

### Using Published Executables
```bash
# After publishing, run standalone executables
./AlUmmahNow.DataImporter/bin/Release/net8.0/linux-x64/publish/AlUmmahNow.DataImporter --list-sessions
./AlUmmahNow.BunnyBulkUploader/bin/Release/net8.0/linux-x64/publish/AlUmmahNow.BunnyBulkUploader -f "/path/to/videos"
```

## Application Purposes

### FileScanService - File Scanning Hub (The Worker)
- **THIS SERVICE DOES ALL THE ACTUAL FILE SCANNING WORK**
- Hosts SignalR Hub at `/scanHub` endpoint
- Contains FileScanEngine - the actual file scanning logic
- Background worker processes scan requests from the queue
- Stores scan results in database (ImportDbContext)
- Sends real-time progress updates to connected clients
- **All file scanning happens here - clients never scan directly**

### DataImporter - SignalR Client for File Scanning
- **NOT for video processing or direct file scanning** - this is a SignalR client
- Connects to FileScanService via SignalR Hub for file discovery
- Receives real-time progress updates during scans
- Manages scan sessions, tracks progress, monitors import status
- Uses FileScanSignalRClient for SignalR communication
- Database operations for session tracking (ImportDbContext)
- **Translation NOT used here** - this is about file operations, not content

### BunnyBulkUploader - Simple Video Upload
- Direct video upload to BunnyCDN storage
- Bulk operations on video files
- Simple progress tracking during uploads

### VisualImporter - Full Import Workflow
- GUI interface for comprehensive video import
- Session management with visual progress tracking
- Multi-channel support with thumbnail handling
- **Currently scans files locally via FileDiscoveryService**
- **Future: Will use SignalR to communicate with FileScanService**
- **This is where translation WOULD be used** for video metadata

## Development Workflow

### Making Changes
1. Always build after changes: `dotnet build` (30 seconds)
2. Test builds pass: `dotnet build --configuration Release` (3 seconds)
3. **FileScanService must be running for DataImporter to work**
4. DataImporter connects to SignalR hub at configured URL (default: http://localhost:5000)
5. BunnyBulkUploader expects video folders with `.mp4` or `.mkv` files
6. VisualImporter handles thumbnails in `.thumbs/` subfolders

### Code Navigation

#### SignalR Architecture (File Scanning)
- **SignalR Hub**: `AlUmmahNow.FileScanService/Hubs/FileScanHub.cs`
- **Scanning Engine**: `AlUmmahNow.FileScanService/Services/FileScanEngine.cs`
- **Background Worker**: `AlUmmahNow.FileScanService/Services/FileScanWorker.cs`
- **SignalR Client**: `AlUmmahNow.DataImporter/Services/FileScanSignalRClient.cs`
- **Command Execution**: `AlUmmahNow.DataImporter/Commands/CommandExecutor.cs`
- **Scan DTOs**: `AlUmmahNow.Data/Models/FileScanning/ScanDtos.cs`

#### Import Logic
- **Centralized Import Logic**: `AlUmmahNow.Importer/`
  - `VideoImporter.cs` - Main import functionality with session management
  - `SimpleVideoUploader.cs` - Basic bulk upload operations
  - `ImportCommandExecutor.cs` - CLI command handling

#### Storage & Services
- **Storage implementations**: `AlUmmahNow.Data/Storage/`
  - `BunnyStorageClient.cs` - BunnyCDN storage operations
  - `R2Client.cs` - Cloudflare R2 S3-compatible client
- **Translation services**: `AlUmmahNow.Data/Translation/DeepLClient.cs` (for future content processing)
- **Video services**: `AlUmmahNow.Data/Video/BunnyStreamClient.cs`
- **Models**: `AlUmmahNow.Data/Models/` - Video, Image, Bucket, FileScanning definitions

#### Application Entry Points
- **CLI entry points**: `Program.cs` files in each console project
- **WinUI Application**: `AlUmmahNow.VisualImporter/` - Modern Windows application

### Service Architecture (SignalR-Based)

```
???????????????????????
?  FileScanService    ? ??? The ONLY service that scans files
?  (SignalR Hub Host) ?     - FileScanEngine does actual scanning
?  Port: 5000         ?     - FileScanWorker processes queue
???????????????????????     - Stores results in database
           ?                 - Sends real-time updates via SignalR
           ? SignalR Hub
           ? /scanHub
           ?
    ??????????????????????????????????????
    ?                 ?                  ?
    ?                 ?                  ?
??????????????  ??????????????  ????????????????????
?DataImporter?  ?VisualImp.  ?  ? Future Clients   ?
?(SignalR    ?  ?(Future:    ?  ? (SignalR based)  ?
? Client)    ?  ? SignalR)   ?  ?                  ?
??????????????  ??????????????  ????????????????????
     ?               ?
     ?               ? Currently: Local FileDiscoveryService
     ?               ? Future: SignalR to FileScanService
     ?               ?
     ?               ?
[Database]      [Database]
```

**Key Points:**
- **FileScanService** = SignalR Hub Host + Actual File Scanning Worker
- **DataImporter** = SignalR Client for file scanning (never scans directly)
- **VisualImporter** = Currently local scanning, future SignalR client
- **All file scanning happens in FileScanService** - clients only communicate with it

### SignalR Communication Flow

1. **Client ? FileScanService** (via SignalR Hub):
   - `StartScan(folderPath, includeSubdirectories)` - Initiate scan
   - `GetScanStatus(sessionId)` - Query scan progress
   - `GetAllSessions()` - List all scan sessions
   - `CancelScan(sessionId)` - Cancel ongoing scan

2. **FileScanService ? Client** (SignalR Events):
   - `ScanStarted(sessionId, path)` - Scan has begun
   - `ScanProgress(sessionId, progress)` - Real-time progress updates
   - `ScanCompleted(sessionId, fileCount)` - Scan finished successfully
   - `ScanFailed(sessionId, error)` - Scan encountered error
   - `ScanCancelled(sessionId)` - Scan was cancelled

3. **Database Usage**:
   - FileScanService writes scan results to database
   - Clients read from database for session history
   - ImportDbContext shared between service and clients

## Validation Scenarios

### Build Validation
- Run `dotnet build` - should complete in ~30 seconds with warnings but no errors
- Run `dotnet build --configuration Release` - should complete in ~3 seconds
- Verify warning count is approximately 49 warnings (nullable reference types)

### Publishing Validation  
- Publish both CLI applications successfully
- Verify executable files are created in publish directories
- Check executable file size (~72KB for DataImporter)
- Verify all dependencies are included (~76MB total per published app)

### Runtime Validation

**FileScanService (must run first):**
- Should start and listen on configured port (default: 5000)
- SignalR hub available at `/scanHub` endpoint
- Background worker should be running
- Check logs for "FileScanService started" message

**DataImporter (requires FileScanService running):**
- Should connect to SignalR hub successfully
- Should list scan sessions from database
- Should receive real-time progress updates during scans
- Shows connection errors when FileScanService is down

**BunnyBulkUploader (requires BunnyCDN configuration):**
- Should accept folder parameter and upload video files
- Shows progress bars during operations
- Scans for `.mp4`, `.mkv`, `.avi`, `.mov`, and `.wmv` files

**VisualImporter (requires full configuration):**
- Provides GUI interface for import operations
- Session management with real-time progress
- Multi-channel video organization
- Currently scans locally (not via SignalR yet)

## Common Issues and Solutions

### Build Issues
- All builds produce nullable reference warnings - this is expected
- No build errors should occur with .NET 8.0 SDK
- Missing packages are auto-restored on first build

### Runtime Issues  
- **DataImporter**: "Unable to connect to FileScan service" - FileScanService not running or wrong URL configured
- **DataImporter**: SignalR connection fails - Check FileScanService:Url in configuration
- **BunnyBulkUploader**: "No RegionEndpoint or ServiceURL configured" - Missing BunnyCDN secrets
- **VisualImporter**: "Object reference not set" - Missing storage configuration
- **FileScanService**: Port already in use - Change port in configuration or stop conflicting service

### Configuration Issues
- Use `dotnet user-secrets` commands to manage secrets
- Each project has unique UserSecretsId in .csproj files
- DataImporter needs database and FileScanService URL configuration
- FileScanService needs database configuration
- BunnyBulkUploader only needs BunnyCDN configuration
- VisualImporter needs full storage and streaming configuration

### SignalR Issues
- **Connection timeout**: FileScanService may not be running on configured URL
- **Hub method not found**: Check SignalR hub method names match client calls
- **No progress updates**: Verify client subscribed to SignalR events correctly
- **Duplicate connections**: Ensure proper connection disposal in client code

## File Locations

### Important Files
- `/AlUmmahNow.sln` - Solution file
- `/README.md` - Basic project description
- `/ARCHITECTURE.md` - Comprehensive solution architecture
- `/AlUmmahNow.Data/` - Shared library source code
- `/AlUmmahNow.Importer/` - Centralized import logic library
- `/AlUmmahNow.FileScanService/` - SignalR hub host and file scanning worker
- `/AlUmmahNow.DataImporter/` - SignalR client for file scanning
- `/AlUmmahNow.BunnyBulkUploader/` - Bulk upload console application
- `/AlUmmahNow.VisualImporter/` - WinUI 3 visual import application

### Configuration Files
- `AlUmmahNow.FileScanService/appsettings.json` - Service configuration (port, database)
- `AlUmmahNow.DataImporter/appsettings.json` - Client configuration (SignalR URL)
- `AlUmmahNow.DataImporter/Properties/launchSettings.json` - Debug settings
- `AlUmmahNow.BunnyBulkUploader/Properties/launchSettings.json` - Debug settings
- `AlUmmahNow.VisualImporter/Package.appxmanifest` - WinUI app manifest

## Important Architecture Notes

### ?? REMOVED:
- ? **AlUmmahNow.FileScanApi** - This project has been REMOVED
- ? **FileScanApiClient** - REST API client has been REMOVED
- ? All REST API endpoints have been replaced with SignalR

### ? DO:
- ? All file scanning work happens in FileScanService
- ? Clients communicate via SignalR Hub at `/scanHub`
- ? Use FileScanSignalRClient for SignalR communication
- ? Subscribe to SignalR events for real-time progress
- ? FileScanService is always the source of truth for scanning operations
- ? All communication is real-time via SignalR, not polling REST APIs
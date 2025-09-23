# AlUmmahNowTools - Developer Instructions

AlUmmahNowTools is a collection of .NET 8 console applications for managing video and image content with cloud storage services (Cloudflare R2, BunnyCDN, DeepL translation). The solution contains two main CLI tools: DataImporter and BunnyBulkUploader, plus a shared data library.

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
Both applications require user secrets configuration before they can run successfully:

#### Required User Secrets for DataImporter:
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
  "DeepL": {
    "ApiKey": "your-deepl-api-key",
    "IsFreeApi": false
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

**IMPORTANT: Applications will fail to start without proper configuration. They cannot display help without valid service initialization.**

## Project Structure

### Solution Components
- **AlUmmahNow.sln** - Main solution file containing 3 projects
- **AlUmmahNow.Data** - Shared data library (.NET 8 library)
  - Storage clients for BunnyCDN and Cloudflare R2
  - Translation services using DeepL
  - Video streaming integration with Bunny Stream
  - Models for videos, images, and storage objects
- **AlUmmahNow.DataImporter** - Console application for importing video content
  - UserSecretsId: `4803ba67-724e-4477-aeb0-1d38aa664f85`
  - Requires folder path parameter: `-f|--folder <path>`
- **AlUmmahNow.BunnyBulkUploader** - Console application for bulk uploading to BunnyCDN
  - UserSecretsId: `4de61621-8c00-4020-a0b6-0ff95676f865`
  - Requires folder path parameter: `-f|--folder <path>`

### Key Dependencies
- **AWSSDK.S3** (3.7.415.26) - Cloudflare R2 S3-compatible storage
- **BunnyCDN.Net.Storage** (1.0.4) - BunnyCDN storage client
- **DeepL.net** (1.15.0) - Translation services
- **McMaster.Extensions.CommandLineUtils** (4.1.1) - CLI argument parsing
- **RestSharp** (112.1.0) - HTTP client for API calls
- **ShellProgressBar** (5.2.0) - Progress indication during operations

## CLI Usage

### DataImporter Commands
```bash
# Build and run DataImporter (requires user secrets)
cd AlUmmahNow.DataImporter
dotnet run -- --help                    # Shows usage (fails without config)
dotnet run -- -f "/path/to/video/files" # Import videos from folder
```

### BunnyBulkUploader Commands  
```bash
# Build and run BunnyBulkUploader (requires user secrets)
cd AlUmmahNow.BunnyBulkUploader
dotnet run -- --help                    # Shows usage (fails without config)
dotnet run -- -f "/path/to/video/files" # Upload videos from folder
```

### Using Published Executables
```bash
# After publishing, run standalone executables
./AlUmmahNow.DataImporter/bin/Release/net8.0/linux-x64/publish/AlUmmahNow.DataImporter -f "/path/to/videos"
./AlUmmahNow.BunnyBulkUploader/bin/Release/net8.0/linux-x64/publish/AlUmmahNow.BunnyBulkUploader -f "/path/to/videos"
```

## Development Workflow

### Making Changes
1. Always build after changes: `dotnet build` (30 seconds)
2. Test builds pass: `dotnet build --configuration Release` (3 seconds)
3. Applications expect video folders with `.mp4` or `.mkv` files
4. Applications handle thumbnails in `.thumbs/` subfolders
5. Progress bars show operation status during file processing

### Code Navigation
- **Storage implementations**: `AlUmmahNow.Data/Storage/`
  - `BunnyStorageClient.cs` - BunnyCDN storage operations
  - `R2Client.cs` - Cloudflare R2 S3-compatible client
- **Video services**: `AlUmmahNow.Data/Video/BunnyStreamClient.cs`
- **Translation**: `AlUmmahNow.Data/Translation/DeepLClient.cs`
- **Models**: `AlUmmahNow.Data/Models/` - Video, Image, Bucket definitions
- **CLI entry points**: `Program.cs` files in each console project
- **Core logic**: `Importer.cs` and `Uploader.cs` in respective projects

### Example Usage Classes
- `AlUmmahNow.DataImporter/DeepLUsageExample.cs` - Shows DeepL translation API usage patterns
- Launch settings show example folder paths for Arabic content

## Validation Scenarios

### Build Validation
- Run `dotnet build` - should complete in ~30 seconds with warnings but no errors
- Run `dotnet build --configuration Release` - should complete in ~3 seconds
- Verify warning count is exactly 49 warnings (nullable reference types)

### Publishing Validation  
- Publish both applications successfully
- Verify executable files are created in publish directories
- Check executable file size (~72KB for DataImporter)
- Verify all dependencies are included (~76MB total per published app)

### Runtime Validation (requires configuration)
With proper user secrets configured:
- DataImporter should accept folder parameter and process video files
- BunnyBulkUploader should accept folder parameter and upload to BunnyCDN
- Both should show progress bars during operations
- Applications scan for `.mp4` and `.mkv` files in specified folders

### Configuration Testing
- Applications will crash immediately without user secrets
- Error messages indicate missing configuration (Amazon.Runtime.AmazonClientException or NullReferenceException)
- User secrets must be configured before any functionality testing

## Common Issues and Solutions

### Build Issues
- All builds produce 49 nullable reference warnings - this is expected
- No build errors should occur with .NET 8.0 SDK
- Missing packages are auto-restored on first build

### Runtime Issues  
- **"No RegionEndpoint or ServiceURL configured"** - Missing R2 user secrets
- **"Object reference not set to an instance"** - Missing BunnyCDN configuration
- Applications cannot show help without valid service configuration

### Configuration Issues
- Use `dotnet user-secrets` commands to manage secrets
- Each project has unique UserSecretsId in .csproj files
- Configuration template is in `appsettings.json` (DataImporter only)

## File Locations

### Important Files
- `/AlUmmahNow.sln` - Solution file
- `/README.md` - Basic project description (minimal)
- `/AlUmmahNow.Data/` - Shared library source code
- `/AlUmmahNow.DataImporter/` - Video import application
- `/AlUmmahNow.BunnyBulkUploader/` - Bulk upload application

### Configuration Files
- `AlUmmahNow.DataImporter/appsettings.json` - Configuration template
- `AlUmmahNow.DataImporter/Properties/launchSettings.json` - Debug settings
- `AlUmmahNow.BunnyBulkUploader/Properties/launchSettings.json` - Debug settings

### Build Outputs
- `bin/Debug/net8.0/` - Debug build outputs
- `bin/Release/net8.0/` - Release build outputs  
- `bin/Release/net8.0/linux-x64/publish/` - Published standalone applications
# AlUmmahNowTools

A comprehensive .NET 8 solution for video content management, processing, and cloud storage operations with multiple interfaces (CLI, GUI) and SignalR-based real-time communication.

## ?? Quick Start

### Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code

### Build & Run
```bash
# Build entire solution
dotnet build

# Run FileScanService (required for scanning)
cd AlUmmahNow.FileScanService
dotnet run

# In another terminal, run CLI import tool
cd AlUmmahNow.DataImporter
dotnet run -- --help

# Run GUI application  
cd AlUmmahNow.VisualImporter
dotnet run
```

## ?? Key Projects

- **AlUmmahNow.Data** - Shared data models and storage services
- **AlUmmahNow.Importer** - Core import business logic
- **AlUmmahNow.DataImporter** - Command-line SignalR client for file scanning
- **AlUmmahNow.VisualImporter** - Visual import interface (WinUI 3)
- **AlUmmahNow.FileScanService** - Background file scanning service with SignalR Hub

## ??? Architecture

- **SignalR-Based Communication** - Real-time updates and progress tracking
- **Clean Architecture** with separated concerns
- **Dependency Injection** throughout
- **Async/Await** for all I/O operations
- **SOLID Principles** implementation

## ?? Documentation

- **[ARCHITECTURE.md](ARCHITECTURE.md)** - Detailed solution architecture
- **[.github/copilot-instructions.md](.github/copilot-instructions.md)** - Development guidelines

## ?? Configuration

Applications use **User Secrets** for sensitive configuration:

```bash
cd AlUmmahNow.DataImporter
dotnet user-secrets set "BunnyVideo:Key" "your-api-key"
dotnet user-secrets set "R2DB:S3Api" "your-endpoint"
dotnet user-secrets set "FileScanService:Url" "http://localhost:5000"
```

## ?? Supported Services

- **BunnyCDN** - Video and image storage
- **Cloudflare R2** - S3-compatible storage  
- **BunnyStream** - Video streaming API
- **DeepL** - Translation services
- **SignalR** - Real-time communication

## ?? Key Features

- Real-time file scanning with SignalR
- Multi-channel video organization
- Progress tracking with resume capability
- Duplicate detection and handling
- Thumbnail management
- Translation integration
- Multiple user interfaces (CLI, GUI)
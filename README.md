# AlUmmahNowTools

A comprehensive .NET 8/9 solution for video content management, processing, and cloud storage operations with cross-platform GUI (Uno Platform), CLI interfaces, and SignalR-based real-time communication.

## ?? Quick Start

### Prerequisites
- .NET 8.0 SDK (for core libraries and services)
- .NET 9.0 SDK (for Uno Platform GUI)
- Visual Studio 2022, VS Code, or Rider

### Build & Run
```bash
# Build entire solution (Note: Uno Platform project requires NET 9 SDK)
dotnet build

# Run FileScanService (required for scanning)
cd AlUmmahNow.FileScanService
dotnet run

# In another terminal, run CLI import tool
cd AlUmmahNow.DataImporter
dotnet run -- --help

# Run cross-platform GUI application (Windows, Linux, macOS)
cd AlUmmahNow.VisualImporterUno/AlUmmahNow.VisualImporterUno
dotnet run
```

## ?? Key Projects

- **AlUmmahNow.Data** - Shared data models and storage services
- **AlUmmahNow.Importer** - Core import business logic
- **AlUmmahNow.DataImporter** - Command-line SignalR client for file scanning
- **AlUmmahNow.VisualImporterUno** - Cross-platform visual import interface (Uno Platform - Windows, Linux, macOS, Web, Mobile)
- **AlUmmahNow.FileScanService** - Background file scanning service with SignalR Hub
- **AlUmmahNow.Tests** - Comprehensive test suite (258 tests)

## ??? Architecture

- **Cross-Platform GUI** - Uno Platform for Windows, Linux, macOS, Web, iOS, Android
- **SignalR-Based Communication** - Real-time updates and progress tracking
- **Clean Architecture** with separated concerns
- **Dependency Injection** throughout
- **Async/Await** for all I/O operations
- **SOLID Principles** implementation
- **Comprehensive Testing** - 258 tests with 97.7% pass rate

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

- **Cross-Platform Support** - Run on Windows, Linux, macOS (via Uno Platform)
- **Real-time file scanning** - SignalR-based file monitoring with FileSystemWatcher
- **File watch & approval workflow** - Automated monitoring with manual approval system
- **Multi-channel video organization** - Organized folder structure
- **Progress tracking** - Resume capability for interrupted operations
- **Duplicate detection** - SHA256 content hashing
- **Thumbnail management** - Automatic thumbnail extraction
- **Translation integration** - DeepL API support
- **Multiple interfaces** - CLI (cross-platform) and GUI (Uno Platform)
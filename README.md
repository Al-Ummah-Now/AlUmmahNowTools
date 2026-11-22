# AlUmmahNowTools

A comprehensive .NET 8/9 solution for video content management, processing, and cloud storage operations with cross-platform GUI (Uno Platform), CLI interfaces, and SignalR-based real-time communication.

## ?? Quick Start

### Prerequisites
- .NET 8.0 SDK (for core libraries and services)
- .NET 9.0 SDK (for Uno Platform GUI)
- Visual Studio 2022, VS Code, or Rider

### Build & Run
```bash
# Fetch the pinned AlUmmahNow.Core package once (sign-in and token storage)
scripts/fetch-core.sh

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
- **AlUmmahNow.ToolsCore** - Staff sign-in for Al Ummah Now Tools: browser sign-in through Al Ummah Now Auth (PKCE, loopback redirect), the staff check against the admin API, per-environment token storage (Windows DPAPI, macOS Keychain, Linux libsecret) and saved preferences
- **AlUmmahNow.VisualImporterUno** - Cross-platform visual import interface (Uno Platform - Windows, Linux, macOS, Web, Mobile)
- **AlUmmahNow.FileScanService** - Background file scanning service with SignalR Hub
- **AlUmmahNow.Tests** - Comprehensive test suite (258 tests)

## Al Ummah Now Tools sign-in

The desktop app opens on a sign-in screen. Staff sign in with their browser (the password never enters the app), then the app checks `GET /api/admin/import/me` on the admin API: only accounts with import access reach the workspace. Production is the default environment; Stage and QA are picked on the sign-in screen or from the account menu, and each keeps its own sign-in. The header carries the language switch (English and Arabic, right to left), light and dark (the first run follows the device), the account menu and the environment badge.

Arabic text uses the bundled IBM Plex Sans Arabic (SIL Open Font License, `Assets/Fonts`), because Uno's Skia text needs one font that carries both Arabic letters and Latin punctuation to order a line correctly.

## Speech to text on import (TA-13)

When a batch is added with "Transcribe the videos that have no captions", each video without caption files of its own is transcribed on the admin's computer after its upload, with whisper.cpp inside aun-core (Metal on macOS, Vulkan on Windows and Linux, the CPU otherwise). The model (default `large-v3-turbo`; smaller ones can be chosen) downloads once into the app's data folder and its checksum is checked. Quran recitation is matched against the server's Quran index (`GET /api/admin/import/quran-index`) and sent only as verse references, never as text. The transcript goes to `POST /api/admin/import/items/:id/transcript` as a caption draft, and, when the batch was added with "Ask the server to translate the captions", the app asks the server to translate it into the site's other language once that draft exists (`POST /items/:id/translations`). A video with its own caption files skips transcription; those files are published as they are, and the server translates from the published track (TA-20). Each translation is a machine-made draft that a person publishes. A transcription that fails here goes to the Errors page; the video is imported without a transcript.

## Social sources (super admins)

The **Social sources** page (wiki TOOLS-ADMIN TA-13) is shown only to accounts holding `import_sources:manage` (super admins). It lists a channel's social import sources, or every channel's: an RSS, Atom or podcast feed, Bluesky, Mastodon, a Telegram channel or a YouTube channel, each imported as the channel's community posts. A source is added from an account the owner connected in Studio, or with the owner's written permission recorded on the source (Telegram only from a connection). From the list a super admin runs **Sync now**, changes the schedule (only when asked, hourly, daily, weekly), turns a source off or on, removes it, reads its latest items and why one was left out, and opens its **Sync job**: state, attempts, next try, reason, log, **Try again** and Copy log.

The app only asks: admin-api `/api/admin/import/sources` (TA-9) stores nothing itself, api-videos runs one queued, idempotent `import.source-sync` admin job per source (retry with backoff, then the DLQ and the admin **Errors** page with Retry), and jobs are read and retried through `/api/content/jobs`. Screenshots: `TOOLS_DEMO=1 TOOLS_DEMO_ROLE=super_admin TOOLS_DEMO_PAGE=SocialSources`, with `TOOLS_DEMO_SOCIAL=job|items|add|remove|all|empty`.

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
dotnet user-secrets set "R2DB:S3Api" "your-endpoint"
dotnet user-secrets set "FileScanService:Url" "http://localhost:5000"
```

## ?? Supported Services

- **Cloudflare R2** - S3-compatible storage (active desktop imports upload through `admin-api` TUS to Cloudflare R2; legacy direct BunnyCDN/BunnyStream clients were removed under `TP-D4`)
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
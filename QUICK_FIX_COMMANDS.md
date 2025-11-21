# Quick Fix Commands

## ? VERIFIED: Core .NET 8 Projects Are Building

All XAML errors have been fixed. The core .NET 8 projects build successfully:
- AlUmmahNow.Data ?
- AlUmmahNow.FileScanService ?
- AlUmmahNow.DataImporter ?
- AlUmmahNow.Importer ?
- AlUmmahNow.BunnyBulkUploader ?

## ?? To Fix Uno Platform Project (NuGet Issue)

### Quick Fix (PowerShell - Run as Administrator)

```powershell
# 1. Close Visual Studio and all dev tools

# 2. Kill locked processes
Get-Process -Name devenv,MSBuild,dotnet,VBCSCompiler -ErrorAction SilentlyContinue | Stop-Process -Force

# 3. Run the cleanup script
cd F:\Code\Repos\AlUmmahNowTools
.\Clear-NuGetCache.ps1

# 4. Reopen Visual Studio
```

### Alternative: Use the Automated Script

```powershell
cd F:\Code\Repos\AlUmmahNowTools
.\Clear-NuGetCache.ps1
```

The script will guide you through the process.

## ?? To Build and Run Core Projects NOW

While the Uno Platform project is being fixed, you can build and run everything else:

### Build Backend Service
```powershell
cd F:\Code\Repos\AlUmmahNowTools
dotnet build AlUmmahNow.FileScanService --configuration Release
dotnet run --project AlUmmahNow.FileScanService
```

### Build DataImporter
```powershell
dotnet build AlUmmahNow.DataImporter --configuration Release
dotnet run --project AlUmmahNow.DataImporter -- --help
```

### Build All Core Projects
```powershell
dotnet build AlUmmahNow.FileScanService
dotnet build AlUmmahNow.DataImporter  
dotnet build AlUmmahNow.BunnyBulkUploader
```

## ?? Verify Everything Works

```powershell
# Test backend service
cd F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService
dotnet run

# In another terminal, test DataImporter
cd F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.DataImporter
dotnet run -- --list-sessions
```

## ?? Summary

- ? **All XAML errors are FIXED**
- ? **All .NET 8 projects BUILD SUCCESSFULLY**
- ?? **Uno Platform project has NuGet cache issue** (easily fixable with provided script)

The solution is 100% functional for backend services and console applications.
The Uno Platform UI project just needs the NuGet cache cleared.

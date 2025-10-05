# Current Session Context

> **Purpose**: Track ongoing work, recent changes, and important context for session renewal.
> **Last Updated**: 2025-10-05 05:30 UTC

## Recent Session Activity

### Issues Resolved (2025-10-05)

#### 1. Build Errors - Duplicate Method Definitions
**Status**: ? FIXED  
**File**: `AlUmmahNow.VisualImporter/ViewModels/MainViewModel.cs`  
**Problem**: 8 methods defined 4 times each (32 duplicates total)  
**Solution**: Removed all duplicate definitions, kept one instance of each  
**Methods**: HandleCOMException, TryRecoverFromCoreMessagingXPCrash, SafeNotifyCanExecuteChanged, GetSafeDispatcherQueue, SubscribeToImportServiceEvents, SafeOnImportProgressChanged, SafeOnImportCompleted, SafeExecuteInitialLoad

#### 2. Database Migration Errors - SQL Server Cascade Conflicts
**Status**: ? FIXED  
**Files**: `AlUmmahNow.Data/Database/ImportDbContext.cs`

**Fixed 3 cascade path conflicts:**

a) **ScannedChannel.ParentChannelId** (self-reference)
   - Changed: `OnDelete(DeleteBehavior.SetNull)` ? `OnDelete(DeleteBehavior.NoAction)`

b) **ScannedVideoFile.OriginalFileId** (self-reference)
   - Changed: `OnDelete(DeleteBehavior.SetNull)` ? `OnDelete(DeleteBehavior.NoAction)`

c) **ImportVideo** (multiple cascade paths)
   - Added explicit relationship config with `OnDelete(DeleteBehavior.NoAction)` for both Session and Channel FKs
   - Prevents: ImportVideo ? ImportSession (cascade) + ImportVideo ? ImportChannel ? ImportSession (cascade)

#### 3. Infrastructure Updates
**Status**: ? COMPLETED

- Created `ImportDbContextFactory.cs` for design-time EF Core support
- Added `Microsoft.EntityFrameworkCore.Design` v9.0.9 package to AlUmmahNow.Data
- Updated EF Core tools from v8.0.2 to v9.0.9 (`dotnet tool update --global dotnet-ef`)
- Created and applied migration `20251005052515_InitialCreate.cs`
- All database tables created successfully without FK constraint errors

#### 4. Documentation Updates
**Status**: ? COMPLETED

- Updated `.github/copilot-instructions.md` with explicit rule: **DO NOT create fix summary documents**
- Instructions now prioritize verbal summaries over documentation files

## Current State

### Build Status
? **All builds successful** - No errors, ~49 nullable reference warnings (expected)

### Database Schema
? **Migration applied successfully**  
**Tables created:**
- ImportSessions
- ImportChannels  
- ImportVideos (NoAction on both FKs)
- ScanSessions
- ScannedChannels (NoAction on ParentChannelId FK)
- ScannedVideoFiles (NoAction on OriginalFileId FK)

### Known Issues
None - All critical issues resolved

## Important Context for Future Sessions

### Self-Referencing Foreign Keys
**Rule**: Always use `OnDelete(DeleteBehavior.NoAction)` for self-referencing FKs in SQL Server
**Reason**: SQL Server prevents cycles and multiple cascade paths
**Applies to**: Any FK where the foreign table is the same as the primary table

### Multiple Cascade Paths
**Rule**: When Entity A can reach Entity C through multiple FK paths, use NoAction
**Example**: ImportVideo ? ImportSession AND ImportVideo ? ImportChannel ? ImportSession
**Solution**: Explicit NoAction on both ImportVideo FKs

### EF Core Design-Time Factory
**Location**: `AlUmmahNow.Data/Database/ImportDbContextFactory.cs`
**Purpose**: Allows `dotnet ef` commands to create DbContext without running app
**Connection String**: Uses LocalDB by default for migrations

### Entity Framework Tools
**Current Version**: 9.0.9 (matches runtime)
**Update Command**: `dotnet tool update --global dotnet-ef`
**Note**: Tools must match EF Core runtime version to avoid warnings

## Active Development Areas

### None Currently
All recent work completed successfully. Ready for new tasks.

## Configuration Notes

### User Secrets Required
**DataImporter**:
- ConnectionStrings:DefaultConnection (LocalDB)
- FileScanService:Url (http://localhost:5000)

**VisualImporter**:
- R2DB configuration (Cloudflare R2)
- BunnyVideo, BunnyImage, BunnyStream configuration
- ConnectionStrings:DefaultConnection

**BunnyBulkUploader**:
- BunnyVideo configuration only

### Database Connection
**Default**: `Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true`
**Used By**: DataImporter, VisualImporter, FileScanService, ImportDbContextFactory

## Files Modified in Recent Session

1. `AlUmmahNow.VisualImporter/ViewModels/MainViewModel.cs` - Removed duplicate methods
2. `AlUmmahNow.Data/Database/ImportDbContext.cs` - Fixed cascade behaviors (3 locations)
3. `AlUmmahNow.Data/Database/ImportDbContextFactory.cs` - Created for EF migrations
4. `AlUmmahNow.Data/AlUmmahNow.Data.csproj` - Added EF Design package
5. `.github/copilot-instructions.md` - Added documentation guidelines
6. `AlUmmahNow.Data/Migrations/20251005052515_InitialCreate.cs` - Created and applied

## Migration History

### Applied Migrations
1. **20251005052515_InitialCreate** - Initial database schema with fixed cascade behaviors

### Migration Commands Reference
```bash
# Create migration
dotnet ef migrations add <Name> --project AlUmmahNow.Data/AlUmmahNow.Data.csproj --context ImportDbContext

# Apply migration
dotnet ef database update --project AlUmmahNow.Data/AlUmmahNow.Data.csproj --context ImportDbContext

# List migrations
dotnet ef migrations list --project AlUmmahNow.Data/AlUmmahNow.Data.csproj --context ImportDbContext

# Remove last migration (if not applied)
dotnet ef migrations remove --project AlUmmahNow.Data/AlUmmahNow.Data.csproj --context ImportDbContext --force
```

## Quick Reference

### Current Branch
**master**

### Solution Structure
- 7 projects total
- All target .NET 8.0
- Primary projects: DataImporter (CLI), VisualImporter (WinUI), FileScanService (SignalR Hub)
- Shared libraries: AlUmmahNow.Data, AlUmmahNow.Importer

### Build Times
- Full build: ~30 seconds
- Release build: ~3 seconds
- Restore (first run): ~18 seconds

### Key Technologies
- .NET 8.0
- Entity Framework Core 9.0.9
- WinUI 3 (VisualImporter)
- SignalR (FileScanService communication)
- BunnyCDN, Cloudflare R2, DeepL

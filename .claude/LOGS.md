# Application Log Locations

Quick reference for accessing application logs in AlUmmahNowTools.

## 📍 Log File Locations

### FileScanService
```
F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\filescan-service-YYYYMMDD.txt
```

**Today's log**:
```
F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\filescan-service-20251008.txt
```

### VisualImporter
```
C:\Users\MEYWD\AppData\Local\AlUmmahNow\VisualImporter\Logs\visual-importer-YYYYMMDD_NNN.txt
```

**Environment variable shortcut**:
```
%LOCALAPPDATA%\AlUmmahNow\VisualImporter\Logs\
```

## 🔍 What's Logged

### FileScanService Logs Include:
- ✅ SignalR connection/disconnection events
- ✅ Hub method invocations with parameters
- ✅ **Outbound SignalR messages sent to clients** (newly added)
- ✅ **Hub method return values** (newly added)
- ✅ Scan operations and progress
- ✅ File watch operations
- ✅ Approval workflow events
- ✅ Errors and warnings

### VisualImporter Logs Include:
- Application startup/shutdown
- FileScanService connection status
- Database initialization
- Service status changes
- Settings operations
- UI events
- Errors and warnings

## 🚀 Quick Commands

### View Latest FileScanService Log (PowerShell)
```powershell
Get-Content "F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\filescan-service-$(Get-Date -Format 'yyyyMMdd').txt" -Tail 50 -Wait
```

### View Latest VisualImporter Log (PowerShell)
```powershell
Get-ChildItem "$env:LOCALAPPDATA\AlUmmahNow\VisualImporter\Logs\" |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1 |
  Get-Content -Tail 50 -Wait
```

### Search for Errors (PowerShell)
```powershell
# FileScanService errors
Select-String -Path "F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\*.txt" -Pattern "ERR"

# VisualImporter errors
Select-String -Path "$env:LOCALAPPDATA\AlUmmahNow\VisualImporter\Logs\*.txt" -Pattern "ERR"
```

### View in Claude
```
Read F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\filescan-service-20251008.txt
```

## 📊 Log Levels (Serilog)

| Level | Abbr | Usage |
|-------|------|-------|
| Trace | TRC | Very detailed diagnostic information |
| **Debug** | **DBG** | **Method parameters, return values, message details** |
| Information | INF | General application flow |
| Warning | WRN | Potentially harmful situations |
| Error | ERR | Failures and exceptions |
| Fatal | FTL | Critical errors causing termination |

## 🔧 Recent Changes

**2025-10-08**: Added comprehensive SignalR communication logging
- All outbound messages (SendAsync calls) now logged at Debug/Info/Warning levels
- All hub method return values logged at Debug level
- Enhanced connection lifecycle logging with exception details
- Hub method parameters already logged (existing)

## 💡 Common Searches

### Find SignalR Messages
```powershell
Select-String -Path "F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\*.txt" -Pattern "Sending.*message"
```

### Find Hub Method Calls
```powershell
Select-String -Path "F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\*.txt" -Pattern "Hub method.*invoked"
```

### Find Connection Events
```powershell
Select-String -Path "F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\*.txt" -Pattern "Client (connected|disconnected)"
```

### Find File Watch Operations
```powershell
Select-String -Path "F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\*.txt" -Pattern "Watch|FileDetected"
```

### Find Approval Operations
```powershell
Select-String -Path "F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\*.txt" -Pattern "Approval"
```

---

*For more details, see `.claude/README.md`*

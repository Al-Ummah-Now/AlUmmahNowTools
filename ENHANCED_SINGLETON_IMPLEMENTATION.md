# Enhanced Singleton Management Implementation

## Overview

I've successfully implemented **defense-in-depth singleton management** for the FileScanService. The service now enforces singleton behavior **internally** while the UI can still start and manage it. This provides robust protection against multiple instances.

## ? **Key Enhancements Implemented:**

### 1. **Internal Singleton Management (FileScanService)**

**New Components:**
- **`SingletonManager.cs`** - Multi-layered singleton enforcement within the service
- **Enhanced `Program.cs`** - Integrated singleton checking before service startup

**Singleton Enforcement Strategies:**
1. **Global Mutex** - Cross-session singleton enforcement (`Global\AlUmmahNow.FileScanService.Port{port}`)
2. **File-based Lock** - Backup mechanism with process information
3. **Port Binding Check** - Final verification that port is available

**Exit Codes for Integration:**
- `0` - Success (normal operation)
- `1` - Error (general failure)
- `2` - Another instance already running (**detected by UI manager**)
- `3` - Failed to acquire singleton lock

### 2. **Enhanced UI Service Manager**

**Updated `FileScanServiceManager.cs`:**
- **Handles singleton exit codes** from the service
- **Graceful handling** when another instance is detected
- **Enhanced service information** with singleton status
- **Proper process management** with singleton awareness

**Smart Instance Detection:**
```csharp
if (exitCode == 2) {
    // Another instance is running - service is available but not managed by us
    UpdateServiceStatus(ServiceHealthStatus.Running);
    _isServiceManaged = false;
    return true; // Service is available (managed by another process)
}
```

### 3. **Rich Singleton Information**

**New HTTP Endpoints:**
- `GET /singleton` - Detailed singleton lock information
- `GET /health` - Health check with singleton status
- `--singleton-info` - Console command for singleton details

**Lock Information Includes:**
- Process ID and name of lock holder
- Start time and machine name
- Mutex and file lock details
- Port and service information

### 4. **Enhanced UI Status Display**

**Updated `ServiceStatusViewModel.cs`:**
- **Singleton information display** in service status
- **Process uptime calculation** for running instances
- **Enhanced status messages** with singleton details

**Status Bar Integration:**
- Shows process ID and uptime for managed services
- Distinguishes between managed and external instances
- Real-time singleton status updates

## ?? **Singleton Management Flow:**

### Service Startup Sequence:
1. **FileScanService starts** ? Checks singleton status
2. **If another instance exists** ? Exit with code 2
3. **If singleton lock fails** ? Exit with code 3
4. **If successful** ? Acquire locks and start service

### UI Manager Response:
1. **Detects exit code 2** ? "Service available but managed externally"
2. **Detects exit code 3** ? "Failed to start service"
3. **Service starts normally** ? "Service managed by this UI"

### Singleton Lock Mechanisms:
```
???????????????????
? Global Mutex    ? ? Cross-session singleton
???????????????????
? File Lock       ? ? Process information storage
???????????????????
? Port Check      ? ? Final verification
???????????????????
```

## ?? **Singleton Status Monitoring:**

### Console Commands:
```bash
# Check singleton status
FileScanService.exe --singleton-info

# Output includes:
Service Port: 5000
Another Instance Running: YES/NO
Process ID: 12345
Process Name: AlUmmahNow.FileScanService
Start Time: 2024-01-15 14:30:22
Lock Mechanisms: [Mutex, File, Port]
```

### HTTP Endpoints:
```bash
# Detailed singleton information
GET http://localhost:5000/singleton

# Response includes lock holder details, uptime, etc.
```

### UI Display:
```
Status Bar: "Service running & connected (managed) - PID: 12345, up 2h 15m"
```

## ??? **Defense-in-Depth Benefits:**

### 1. **Multiple Protection Layers:**
- **Service-level** singleton enforcement (can't start if another exists)
- **UI-level** process management (prevents UI from starting duplicates)
- **System-level** resource locks (mutex, file, port)

### 2. **Graceful Handling:**
- UI detects existing instances and connects rather than failing
- Service provides clear exit codes for different scenarios
- Rich error messages and status information

### 3. **Robust Process Management:**
- Automatic cleanup of locks on service exit
- Process information tracking and display
- Cross-session singleton enforcement

### 4. **Enhanced User Experience:**
- Clear status messages about service state
- Ability to see which process is managing the service
- Uptime information and process details

## ?? **Implementation Details:**

### Service-Side Singleton Check:
```csharp
// In Program.cs - before starting web host
if (_singletonManager.IsAnotherInstanceRunning()) {
    Console.WriteLine("?? Another instance is already running!");
    return 2; // Exit code for "another instance running"
}

if (!_singletonManager.TryAcquireSingletonLock()) {
    Console.WriteLine("? Failed to acquire singleton lock");
    return 3; // Exit code for "lock acquisition failed"
}
```

### UI-Side Response:
```csharp
// In FileScanServiceManager.cs
if (process.HasExited) {
    var exitCode = process.ExitCode;
    if (exitCode == 2) {
        // Another instance is running - connect to it
        _isServiceManaged = false;
        return true; // Service is available
    }
}
```

### Singleton Information Display:
```csharp
// Enhanced status message with singleton details
var baseMessage = $"Service running{(IsServiceManaged ? " (managed)" : " (external)")}";
if (singletonInfo.IsLockHeld) {
    baseMessage += $" - PID: {singletonInfo.CurrentProcessId}";
    // Add uptime calculation...
}
```

## ?? **Usage Examples:**

### Scenario 1: First Instance
1. User starts VisualImporter
2. UI starts FileScanService ? Singleton lock acquired ? Service runs
3. Status: "Service running & connected (managed)"

### Scenario 2: Second Instance Attempt
1. User starts another VisualImporter
2. UI tries to start FileScanService ? Service detects existing instance ? Exit code 2
3. UI detects exit code 2 ? Connects to existing service
4. Status: "Service running & connected (external)"

### Scenario 3: Manual Service Start
1. User runs `FileScanService.exe` manually ? Singleton lock acquired
2. User starts VisualImporter ? UI detects existing service ? Connects
3. Status: "Service running & connected (external)"

## ?? **Benefits Achieved:**

1. **? Prevents Multiple Service Instances** - Robust singleton enforcement
2. **? UI Can Always Start Service** - Graceful handling of existing instances  
3. **? Clear Status Information** - Users know what's happening
4. **? Process Transparency** - Shows which process is managing the service
5. **? Automatic Recovery** - Handles various startup scenarios gracefully
6. **? Rich Diagnostics** - Console commands and HTTP endpoints for troubleshooting

The implementation provides a perfect balance between **preventing multiple instances** while maintaining **UI service management capabilities**. Users get the best of both worlds: automatic service management with robust singleton protection.
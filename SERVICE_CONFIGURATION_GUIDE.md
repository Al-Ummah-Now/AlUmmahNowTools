# FileScan Service Configuration Guide

The VisualImporter uses two different concepts for FileScan service integration:

## Service URL vs Executable Path

### Service URL (FileScanService:Url)
- **Purpose**: HTTP endpoint for communication with the running service
- **Example**: `http://localhost:5000`
- **Usage**: Used by SignalR client and FileScanServiceManager for health checks and communication
- **Configuration**: Set in appsettings.json or user secrets under `FileScanService:Url`

### Service Executable Path (ServiceInstallation:ExecutablePath)  
- **Purpose**: File system path to the service executable for starting the service
- **Example**: `C:\path\to\AlUmmahNow.FileScanService.exe`
- **Usage**: Used by FileScanServiceManager to start the service when it's not running
- **Configuration**: Set in appsettings.json or user secrets under `ServiceInstallation:ExecutablePath`

## Configuration Methods

### 1. User Secrets (Recommended for development)
```bash
dotnet user-secrets set "FileScanService:Url" "http://localhost:5000"
dotnet user-secrets set "ServiceInstallation:ExecutablePath" "C:\path\to\service.exe"
dotnet user-secrets set "ServiceInstallation:StartAutomatically" "true"
```

### 2. appsettings.json (for default values)
```json
{
  "FileScanService": {
    "Url": "http://localhost:5000"
  },
  "ServiceInstallation": {
    "ExecutablePath": "",
    "StartAutomatically": true
  }
}
```

## Auto-Detection
If no executable path is configured, the FileScanServiceManager will attempt to auto-detect the service executable in these locations:
1. Same directory as VisualImporter
2. `../AlUmmahNow.FileScanService/bin/Debug/net8.0/`
3. `../AlUmmahNow.FileScanService/bin/Release/net8.0/`
4. Solution directory relative paths
5. Fallback to `dotnet run --project` command

## Service Lifecycle
1. **Startup**: VisualImporter checks if FileScan service is running at the configured URL
2. **Auto-start**: If not running and executable path is available, starts the service
3. **Communication**: Uses SignalR to communicate with the running service
4. **Monitoring**: Continuously monitors service health via HTTP health checks
5. **Shutdown**: Gracefully stops managed service instances on application exit
# NuGet Cache Clear and Restore Script
# Run this script with Visual Studio and all dev tools closed

Write-Host "=== NuGet Cache Clear and Restore Script ===" -ForegroundColor Cyan
Write-Host ""

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "WARNING: Not running as Administrator. Some locked files may not be deletable." -ForegroundColor Yellow
    Write-Host ""
}

# Step 1: Check for running processes
Write-Host "Step 1: Checking for processes that might lock NuGet packages..." -ForegroundColor Green
$processes = @('devenv', 'MSBuild', 'dotnet', 'VBCSCompiler')
$foundProcesses = @()

foreach ($proc in $processes) {
    $running = Get-Process -Name $proc -ErrorAction SilentlyContinue
    if ($running) {
        $foundProcesses += $proc
        Write-Host "  [!] Found running process: $proc" -ForegroundColor Yellow
    }
}

if ($foundProcesses.Count -gt 0) {
    Write-Host ""
    Write-Host "WARNING: The following processes are running and may lock NuGet files:" -ForegroundColor Red
    $foundProcesses | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host ""
    $response = Read-Host "Do you want to kill these processes? (y/N)"
    if ($response -eq 'y' -or $response -eq 'Y') {
        foreach ($proc in $foundProcesses) {
            Write-Host "  Killing $proc..." -ForegroundColor Yellow
            Get-Process -Name $proc -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        }
        Write-Host "  Processes killed. Waiting 3 seconds..." -ForegroundColor Green
        Start-Sleep -Seconds 3
    } else {
        Write-Host "Skipping process termination. Some files may remain locked." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Step 2: Clearing NuGet caches..." -ForegroundColor Green

# Clear NuGet HTTP cache
Write-Host "  Clearing HTTP cache..." -ForegroundColor Cyan
try {
    dotnet nuget locals http-cache --clear
    Write-Host "  [?] HTTP cache cleared" -ForegroundColor Green
} catch {
    Write-Host "  [!] Failed to clear HTTP cache: $_" -ForegroundColor Red
}

# Clear NuGet temp
Write-Host "  Clearing temp cache..." -ForegroundColor Cyan
try {
    dotnet nuget locals temp --clear
    Write-Host "  [?] Temp cache cleared" -ForegroundColor Green
} catch {
    Write-Host "  [!] Failed to clear temp cache: $_" -ForegroundColor Red
}

# Clear global packages cache (this is the main one)
Write-Host "  Clearing global packages cache..." -ForegroundColor Cyan
try {
    dotnet nuget locals global-packages --clear
    Write-Host "  [?] Global packages cache cleared" -ForegroundColor Green
} catch {
    Write-Host "  [!] Failed to clear global packages: $_" -ForegroundColor Red
}

# Manually delete Uno packages if they exist
Write-Host ""
Write-Host "Step 3: Manually removing Uno Platform packages..." -ForegroundColor Green
$nugetFolder = "$env:USERPROFILE\.nuget\packages"
$unoPackages = @('uno.sdk', 'uno.winui', 'uno.toolkit.winui', 'uno.ui', 'uno.extensions')

foreach ($pkg in $unoPackages) {
    $pkgPath = Join-Path $nugetFolder $pkg
    if (Test-Path $pkgPath) {
        Write-Host "  Removing $pkg..." -ForegroundColor Cyan
        try {
            Remove-Item -Path $pkgPath -Recurse -Force -ErrorAction Stop
            Write-Host "  [?] Removed $pkg" -ForegroundColor Green
        } catch {
            Write-Host "  [!] Failed to remove $pkg (may be locked)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  [·] $pkg not found (already clean)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Step 4: Cleaning build outputs..." -ForegroundColor Green
$repoPath = "F:\Code\Repos\AlUmmahNowTools"
$projectPaths = @(
    "AlUmmahNow.Data",
    "AlUmmahNow.Importer",
    "AlUmmahNow.FileScanService",
    "AlUmmahNow.DataImporter",
    "AlUmmahNow.BunnyBulkUploader",
    "AlUmmahNow.Tests",
    "AlUmmahNow.VisualImporterUno\AlUmmahNow.VisualImporterUno"
)

foreach ($proj in $projectPaths) {
    $objPath = Join-Path $repoPath "$proj\obj"
    $binPath = Join-Path $repoPath "$proj\bin"
    
    if (Test-Path $objPath) {
        Write-Host "  Removing obj: $proj" -ForegroundColor Cyan
        Remove-Item -Path $objPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    if (Test-Path $binPath) {
        Write-Host "  Removing bin: $proj" -ForegroundColor Cyan
        Remove-Item -Path $binPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "  [?] Build outputs cleaned" -ForegroundColor Green

Write-Host ""
Write-Host "Step 5: Restoring NuGet packages..." -ForegroundColor Green
Set-Location $repoPath

Write-Host "  Restoring solution..." -ForegroundColor Cyan
$restoreResult = dotnet restore --force --no-cache 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [?] Restore completed successfully" -ForegroundColor Green
} else {
    Write-Host "  [!] Restore had errors. Check output above." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Restore output:" -ForegroundColor Cyan
    $restoreResult | Select-Object -Last 30
}

Write-Host ""
Write-Host "=== Cleanup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Try building the solution: dotnet build" -ForegroundColor White
Write-Host "2. If Uno project still fails, try opening Visual Studio and let it restore automatically" -ForegroundColor White
Write-Host "3. If issues persist, you may need to reinstall Uno Platform workload:" -ForegroundColor White
Write-Host "   dotnet workload install uno" -ForegroundColor Gray
Write-Host ""

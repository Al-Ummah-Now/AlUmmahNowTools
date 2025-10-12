# Script to convert logo.png to .ico and copy to all projects
# Run this from the repository root

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

Write-Host "Converting logo.png to .ico format..." -ForegroundColor Cyan

$sourceLogo = "AlUmmahNow.VisualImporter\Assets\logo.png"
$outputIcon = "app-icon.ico"

if (-not (Test-Path $sourceLogo)) {
    Write-Host "ERROR: Source logo not found at $sourceLogo" -ForegroundColor Red
    exit 1
}

# Function to convert PNG to ICO using .NET
function Convert-PngToIco {
    param(
        [string]$PngPath,
        [string]$IcoPath
    )

    Add-Type -AssemblyName System.Drawing

    # Load the PNG image
    $png = [System.Drawing.Image]::FromFile($PngPath)

    # Create icon sizes (16, 32, 48, 64, 128, 256)
    $sizes = @(16, 32, 48, 64, 128, 256)

    # Create temporary directory for icon files
    $tempDir = Join-Path $env:TEMP "icon_conversion"
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $tempDir | Out-Null

    # Resize and save each size
    $iconFiles = @()
    foreach ($size in $sizes) {
        $resizedPath = Join-Path $tempDir "$size.png"
        $bitmap = New-Object System.Drawing.Bitmap $size, $size
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($png, 0, 0, $size, $size)
        $bitmap.Save($resizedPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose()
        $bitmap.Dispose()
        $iconFiles += $resizedPath
    }

    $png.Dispose()

    # Use convert.exe if available (ImageMagick), otherwise create simple ICO
    $convertExe = Get-Command "magick.exe" -ErrorAction SilentlyContinue
    if ($null -eq $convertExe) {
        $convertExe = Get-Command "convert.exe" -ErrorAction SilentlyContinue
    }

    if ($convertExe) {
        Write-Host "Using ImageMagick to create multi-resolution ICO..." -ForegroundColor Green
        & $convertExe.Source $iconFiles $IcoPath
    }
    else {
        Write-Host "ImageMagick not found. Creating simple ICO with largest size..." -ForegroundColor Yellow
        # Create a simple ICO using the largest PNG
        $largestPng = $iconFiles[-1]
        $icon = [System.Drawing.Icon]::FromHandle(([System.Drawing.Bitmap]::FromFile($largestPng)).GetHicon())
        $fileStream = [System.IO.File]::Create($IcoPath)
        $icon.Save($fileStream)
        $fileStream.Close()
        $icon.Dispose()
    }

    # Cleanup
    Remove-Item $tempDir -Recurse -Force

    Write-Host "Icon created: $IcoPath" -ForegroundColor Green
}

try {
    # Convert PNG to ICO
    Convert-PngToIco -PngPath $sourceLogo -IcoPath $outputIcon

    # Verify icon was created
    if (-not (Test-Path $outputIcon)) {
        throw "Failed to create icon file at $outputIcon"
    }

    # Copy icon to each project directory
    $projects = @(
        "AlUmmahNow.FileScanService",
        "AlUmmahNow.BunnyBulkUploader",
        "AlUmmahNow.DataImporter"
    )

    Write-Host "`nCopying icon to project directories..." -ForegroundColor Cyan
    foreach ($project in $projects) {
        $destPath = Join-Path $project "app-icon.ico"
        Copy-Item $outputIcon $destPath -Force
        Write-Host "  Copied to $project/" -ForegroundColor Green
    }

    # Clean up root icon file after copying
    if (Test-Path $outputIcon) {
        Remove-Item $outputIcon -Force
    }

    Write-Host "`nIcon setup complete!" -ForegroundColor Green
    Write-Host "Next step: Update .csproj files to reference the icon" -ForegroundColor Yellow
}
catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

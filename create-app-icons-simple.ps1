# Simple script to convert logo.png to .ico using .NET
# Run this from the repository root

$ErrorActionPreference = "Stop"

Write-Host "Creating application icons from logo.png..." -ForegroundColor Cyan

$sourceLogo = "AlUmmahNow.VisualImporter\Assets\logo.png"

if (-not (Test-Path $sourceLogo)) {
    Write-Host "ERROR: Source logo not found at $sourceLogo" -ForegroundColor Red
    exit 1
}

# Projects to add icons to
$projects = @(
    "AlUmmahNow.FileScanService",
    "AlUmmahNow.BunnyBulkUploader",
    "AlUmmahNow.DataImporter"
)

Add-Type -AssemblyName System.Drawing

try {
    # Load the source PNG
    $image = [System.Drawing.Image]::FromFile((Resolve-Path $sourceLogo))

    foreach ($project in $projects) {
        $iconPath = Join-Path $project "app-icon.ico"

        Write-Host "Creating icon for $project..." -ForegroundColor Yellow

        # Create a 256x256 bitmap (standard icon size)
        $size = 256
        $bitmap = New-Object System.Drawing.Bitmap $size, $size
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($image, 0, 0, $size, $size)

        # Convert to icon and save
        $hIcon = $bitmap.GetHicon()
        $icon = [System.Drawing.Icon]::FromHandle($hIcon)
        $stream = [System.IO.FileStream]::new($iconPath, [System.IO.FileMode]::Create)
        $icon.Save($stream)
        $stream.Close()

        # Cleanup
        $graphics.Dispose()
        $bitmap.Dispose()
        $icon.Dispose()

        Write-Host "  Created: $iconPath" -ForegroundColor Green
    }

    $image.Dispose()

    Write-Host "`nIcon setup complete!" -ForegroundColor Green
    Write-Host "Next step: Update .csproj files to reference the icons" -ForegroundColor Yellow
}
catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

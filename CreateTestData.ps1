# Quick Crash Test Script
# Run this PowerShell script to create test data and validate the fix

Write-Host "Creating test environment for crash testing..." -ForegroundColor Green

# Create test directory structure
$testPath = "C:\TestVideos\CrashTest"
Write-Host "Creating test directory: $testPath"

if (Test-Path $testPath) {
    Remove-Item $testPath -Recurse -Force
}

New-Item -ItemType Directory -Path $testPath -Force | Out-Null

# Create multiple channel folders with dummy files
$channels = @("Channel1", "Channel2", "Channel3", "Channel4", "Channel5")
foreach ($channel in $channels) {
    $channelPath = Join-Path $testPath $channel
    New-Item -ItemType Directory -Path $channelPath -Force | Out-Null
    
    # Create dummy video files
    for ($i = 1; $i -le 50; $i++) {
        $fileName = "video$i.mp4"
        $filePath = Join-Path $channelPath $fileName
        # Create files with some content to simulate real files
        $content = "Dummy video content for testing - File $i in $channel" * 100
        $content | Out-File $filePath -Encoding UTF8
    }
    
    Write-Host "Created $channel with 50 video files"
}

Write-Host "Test environment created successfully!" -ForegroundColor Green
Write-Host "Test folder location: $testPath" -ForegroundColor Yellow
Write-Host ""
Write-Host "Testing Instructions:" -ForegroundColor Cyan
Write-Host "1. Run AlUmmahNow.VisualImporter.exe"
Write-Host "2. Select the folder: $testPath"
Write-Host "3. Click 'Scan Folder'"
Write-Host "4. While scanning, try these stress tests:"
Write-Host "   - Rapidly click Select All / Deselect All"
Write-Host "   - Change sort options multiple times"
Write-Host "   - Resize/minimize/restore window"
Write-Host "   - Switch between tabs"
Write-Host "   - Change channel filter"
Write-Host ""
Write-Host "Expected Result: No crashes, smooth operation" -ForegroundColor Green
Write-Host "Previous Issue: Would crash in CoreMessagingXP.dll" -ForegroundColor Red
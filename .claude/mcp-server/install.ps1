# Installation script for AlUmmahNow MCP Server

Write-Host "=== Installing AlUmmahNow MCP Server ===" -ForegroundColor Cyan
Write-Host ""

$mcpDir = $PSScriptRoot
$projectRoot = Split-Path -Parent (Split-Path -Parent $mcpDir)

# Check Node.js
Write-Host "Checking Node.js..." -ForegroundColor Yellow
try {
    $nodeVersion = node --version
    Write-Host "✓ Node.js version: $nodeVersion" -ForegroundColor Green
} catch {
    Write-Host "✗ Node.js not found" -ForegroundColor Red
    Write-Host "  Install from: https://nodejs.org/" -ForegroundColor Yellow
    exit 1
}

# Check npm
try {
    $npmVersion = npm --version
    Write-Host "✓ npm version: $npmVersion" -ForegroundColor Green
} catch {
    Write-Host "✗ npm not found" -ForegroundColor Red
    exit 1
}

# Install dependencies
Write-Host "`nInstalling dependencies..." -ForegroundColor Yellow
Push-Location $mcpDir
try {
    npm install
    Write-Host "✓ Dependencies installed" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to install dependencies" -ForegroundColor Red
    Pop-Location
    exit 1
}
Pop-Location

# Test server
Write-Host "`nTesting server..." -ForegroundColor Yellow
try {
    $testOutput = node "$mcpDir\index.js" 2>&1 | Select-Object -First 1
    if ($testOutput -like "*AlUmmah MCP Server*") {
        Write-Host "✓ Server test successful" -ForegroundColor Green
    } else {
        Write-Host "⚠ Server started but unexpected output" -ForegroundColor Yellow
    }
} catch {
    Write-Host "✗ Server test failed: $_" -ForegroundColor Red
}

# Add to Claude Code
Write-Host "`nAdding to Claude Code..." -ForegroundColor Yellow
$serverPath = Join-Path $mcpDir "index.js"

try {
    # Check if already added
    $mcpList = claude mcp list 2>&1
    if ($mcpList -like "*alummah*") {
        Write-Host "⚠ MCP server 'alummah' already exists" -ForegroundColor Yellow
        $response = Read-Host "Remove and re-add? (y/n)"
        if ($response -eq 'y') {
            claude mcp remove alummah
            Write-Host "Removed existing server" -ForegroundColor Yellow
        } else {
            Write-Host "Skipping registration" -ForegroundColor Yellow
            exit 0
        }
    }

    # Add server
    claude mcp add alummah node $serverPath
    Write-Host "✓ Added to Claude Code" -ForegroundColor Green
} catch {
    Write-Host "⚠ Could not add to Claude Code automatically" -ForegroundColor Yellow
    Write-Host "  Run manually:" -ForegroundColor Gray
    Write-Host "  claude mcp add alummah node $serverPath" -ForegroundColor White
}

# Verify
Write-Host "`nVerifying installation..." -ForegroundColor Yellow
try {
    $status = claude mcp status 2>&1
    Write-Host $status
} catch {
    Write-Host "Could not verify status" -ForegroundColor Yellow
}

Write-Host "`n=== Installation Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Restart Claude Code if it's running" -ForegroundColor White
Write-Host "2. Try: claude" -ForegroundColor White
Write-Host "   > 'Query the database for recent scans'" -ForegroundColor Gray
Write-Host "   > 'Run all tests'" -ForegroundColor Gray
Write-Host "   > 'Show project statistics'" -ForegroundColor Gray
Write-Host ""
Write-Host "Documentation: .claude\mcp-server\README.md" -ForegroundColor Gray
Write-Host ""

# MCP Setup Script for AlUmmahNowTools
# This script helps configure MCP for Claude Desktop

param(
    [switch]$Install,
    [switch]$Configure,
    [switch]$Verify,
    [string]$GithubToken = ""
)

Write-Host "=== MCP Setup for AlUmmahNowTools ===" -ForegroundColor Cyan
Write-Host ""

$claudeConfigDir = "$env:APPDATA\Claude"
$claudeConfigFile = "$claudeConfigDir\claude_desktop_config.json"
$projectRoot = "F:\Code\Repos\AlUmmahNowTools"

# Function to check if Node.js is installed
function Test-NodeInstalled {
    try {
        $nodeVersion = node --version 2>$null
        if ($nodeVersion) {
            Write-Host "✓ Node.js is installed: $nodeVersion" -ForegroundColor Green
            return $true
        }
    } catch {
        Write-Host "✗ Node.js is not installed" -ForegroundColor Red
        Write-Host "  Download from: https://nodejs.org/" -ForegroundColor Yellow
        return $false
    }
    return $false
}

# Function to check if Claude Desktop is installed
function Test-ClaudeInstalled {
    $claudeExe = "$env:LOCALAPPDATA\Programs\Claude\Claude.exe"
    if (Test-Path $claudeExe) {
        Write-Host "✓ Claude Desktop is installed" -ForegroundColor Green
        return $true
    } else {
        Write-Host "✗ Claude Desktop not found" -ForegroundColor Red
        Write-Host "  Expected location: $claudeExe" -ForegroundColor Yellow
        return $false
    }
}

# Function to create config directory
function Initialize-ConfigDirectory {
    if (-not (Test-Path $claudeConfigDir)) {
        Write-Host "Creating Claude config directory..." -ForegroundColor Yellow
        New-Item -ItemType Directory -Path $claudeConfigDir -Force | Out-Null
        Write-Host "✓ Config directory created" -ForegroundColor Green
    } else {
        Write-Host "✓ Config directory exists" -ForegroundColor Green
    }
}

# Function to backup existing config
function Backup-Config {
    if (Test-Path $claudeConfigFile) {
        $backupFile = "$claudeConfigFile.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item $claudeConfigFile $backupFile
        Write-Host "✓ Backed up existing config to: $backupFile" -ForegroundColor Green
    }
}

# Function to create MCP configuration
function New-MCPConfig {
    param([string]$GithubToken)

    $config = @{
        mcpServers = @{
            filesystem = @{
                command = "npx"
                args = @(
                    "-y",
                    "@modelcontextprotocol/server-filesystem",
                    $projectRoot
                )
            }
            git = @{
                command = "npx"
                args = @(
                    "-y",
                    "@modelcontextprotocol/server-git",
                    "--repository",
                    $projectRoot
                )
            }
        }
        globalShortcut = "Ctrl+Space"
    }

    # Add GitHub server if token provided
    if ($GithubToken -and $GithubToken -ne "") {
        $config.mcpServers.github = @{
            command = "npx"
            args = @("-y", "@modelcontextprotocol/server-github")
            env = @{
                GITHUB_PERSONAL_ACCESS_TOKEN = $GithubToken
            }
        }
        Write-Host "✓ GitHub server configured" -ForegroundColor Green
    }

    # Add SQLite server if database exists
    $dbPath = "$projectRoot\AlUmmahNow.Data\alummah.db"
    if (Test-Path $dbPath) {
        $config.mcpServers.sqlite = @{
            command = "npx"
            args = @(
                "-y",
                "@modelcontextprotocol/server-sqlite",
                $dbPath
            )
        }
        Write-Host "✓ SQLite server configured (database found)" -ForegroundColor Green
    }

    return $config
}

# Function to verify MCP configuration
function Test-MCPConfig {
    if (-not (Test-Path $claudeConfigFile)) {
        Write-Host "✗ No MCP configuration found" -ForegroundColor Red
        return $false
    }

    try {
        $config = Get-Content $claudeConfigFile -Raw | ConvertFrom-Json
        Write-Host "✓ Configuration file is valid JSON" -ForegroundColor Green

        if ($config.mcpServers) {
            Write-Host "`nConfigured MCP Servers:" -ForegroundColor Cyan
            $config.mcpServers.PSObject.Properties | ForEach-Object {
                Write-Host "  • $($_.Name)" -ForegroundColor White
            }
            return $true
        } else {
            Write-Host "✗ No MCP servers configured" -ForegroundColor Red
            return $false
        }
    } catch {
        Write-Host "✗ Configuration file is invalid: $_" -ForegroundColor Red
        return $false
    }
}

# Main execution
if ($Install) {
    Write-Host "`n--- Installation Check ---" -ForegroundColor Cyan
    $nodeOk = Test-NodeInstalled
    $claudeOk = Test-ClaudeInstalled

    if (-not $nodeOk) {
        Write-Host "`nPlease install Node.js first:" -ForegroundColor Yellow
        Write-Host "https://nodejs.org/" -ForegroundColor Cyan
        exit 1
    }

    if (-not $claudeOk) {
        Write-Host "`nNote: Claude Desktop not detected, but you can still configure MCP" -ForegroundColor Yellow
    }

    Write-Host "`n✓ Ready to configure MCP" -ForegroundColor Green
}

if ($Configure) {
    Write-Host "`n--- Configuring MCP ---" -ForegroundColor Cyan

    # Check Node.js
    if (-not (Test-NodeInstalled)) {
        Write-Host "`nCannot configure MCP without Node.js" -ForegroundColor Red
        exit 1
    }

    # Initialize directory
    Initialize-ConfigDirectory

    # Backup existing config
    Backup-Config

    # Prompt for GitHub token if not provided
    if (-not $GithubToken -or $GithubToken -eq "") {
        Write-Host "`nGitHub Token (optional, press Enter to skip):" -ForegroundColor Cyan
        Write-Host "Get token from: https://github.com/settings/tokens" -ForegroundColor Gray
        $GithubToken = Read-Host "Token"
    }

    # Create configuration
    $config = New-MCPConfig -GithubToken $GithubToken

    # Write configuration
    $config | ConvertTo-Json -Depth 10 | Set-Content $claudeConfigFile -Encoding UTF8
    Write-Host "`n✓ MCP configuration written to: $claudeConfigFile" -ForegroundColor Green

    # Verify
    Write-Host "`n--- Verifying Configuration ---" -ForegroundColor Cyan
    Test-MCPConfig | Out-Null

    Write-Host "`n🎉 MCP Setup Complete!" -ForegroundColor Green
    Write-Host "`nNext steps:" -ForegroundColor Cyan
    Write-Host "1. Restart Claude Desktop" -ForegroundColor White
    Write-Host "2. MCP tools will be available automatically" -ForegroundColor White
    Write-Host "3. Try asking Claude to search your codebase or check git status" -ForegroundColor White
}

if ($Verify) {
    Write-Host "`n--- Verifying MCP Setup ---" -ForegroundColor Cyan

    $nodeOk = Test-NodeInstalled
    $claudeOk = Test-ClaudeInstalled
    $configOk = Test-MCPConfig

    Write-Host "`n=== Summary ===" -ForegroundColor Cyan
    Write-Host "Node.js: $(if ($nodeOk) { '✓' } else { '✗' })" -ForegroundColor $(if ($nodeOk) { 'Green' } else { 'Red' })
    Write-Host "Claude Desktop: $(if ($claudeOk) { '✓' } else { '✗' })" -ForegroundColor $(if ($claudeOk) { 'Green' } else { 'Yellow' })
    Write-Host "MCP Config: $(if ($configOk) { '✓' } else { '✗' })" -ForegroundColor $(if ($configOk) { 'Green' } else { 'Red' })

    if ($nodeOk -and $configOk) {
        Write-Host "`n✓ MCP is ready to use!" -ForegroundColor Green
    } elseif (-not $nodeOk) {
        Write-Host "`n✗ Install Node.js first" -ForegroundColor Red
    } elseif (-not $configOk) {
        Write-Host "`n✗ Run with -Configure to set up MCP" -ForegroundColor Red
    }
}

# Show help if no parameters
if (-not ($Install -or $Configure -or $Verify)) {
    Write-Host "Usage:" -ForegroundColor Cyan
    Write-Host "  .\setup-mcp.ps1 -Install              Check prerequisites" -ForegroundColor White
    Write-Host "  .\setup-mcp.ps1 -Configure            Configure MCP for Claude Desktop" -ForegroundColor White
    Write-Host "  .\setup-mcp.ps1 -Verify               Verify MCP setup" -ForegroundColor White
    Write-Host "  .\setup-mcp.ps1 -Configure -GithubToken 'ghp_...'  Configure with GitHub token" -ForegroundColor White
    Write-Host ""
    Write-Host "Quick setup:" -ForegroundColor Cyan
    Write-Host "  .\setup-mcp.ps1 -Install -Configure -Verify" -ForegroundColor Yellow
}

Write-Host ""

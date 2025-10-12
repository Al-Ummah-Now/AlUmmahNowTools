# Claude AI Configuration for AlUmmahNowTools

This directory contains configuration and documentation for AI-assisted development with Claude.

## 📁 Files

### Configuration
- **`claude_desktop_config.template.json`** - Template MCP configuration for Claude Desktop
- **`setup-mcp.ps1`** - PowerShell script to configure MCP automatically
- **`mcp-server/`** - Custom MCP server implementation for Claude Code CLI

### Documentation
- **`QUICK_START_MCP.md`** - 30-second custom MCP server setup
- **`CUSTOM_MCP_GUIDE.md`** - Complete guide to creating custom MCP servers
- **`MCP_CONFIGURATION_GUIDE.md`** - Complete guide to MCP setup and configuration
- **`testing-workflow.md`** - Quick reference for testing in AI coding workflow
- **`LOGS.md`** - Quick reference for application log locations and commands
- **`README.md`** - This file

## 🚀 Quick Start

### Custom MCP Server (Recommended for Claude Code CLI)

```powershell
# One-command installation
cd .claude\mcp-server
.\install.ps1

# Verify
claude mcp list
```

**Provides**: Database queries, test running, project statistics, code search

👉 **Read**: `QUICK_START_MCP.md` for full details

### Claude Desktop MCP Setup

```powershell
# Check prerequisites
.\.claude\setup-mcp.ps1 -Install

# Configure MCP
.\.claude\setup-mcp.ps1 -Configure

# Verify setup
.\.claude\setup-mcp.ps1 -Verify

# Or do all at once
.\.claude\setup-mcp.ps1 -Install -Configure -Verify
```

### Option 2: Manual Setup

1. **Install Node.js** (if not already installed)
   - Download from: https://nodejs.org/
   - Verify: `node --version`

2. **Locate Claude Desktop config file**
   - Windows: `%APPDATA%\Claude\claude_desktop_config.json`
   - macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`

3. **Copy template configuration**
   ```powershell
   Copy-Item .\.claude\claude_desktop_config.template.json "$env:APPDATA\Claude\claude_desktop_config.json"
   ```

4. **Edit the configuration**
   - Replace `REPLACE_WITH_YOUR_GITHUB_TOKEN` with your GitHub token (optional)
   - Adjust paths if your project is in a different location

5. **Restart Claude Desktop**

## 🛠️ What MCP Provides

### Custom AlUmmahNow Server (Claude Code CLI)
- **Database Tools**: Query scan sessions, scanned files, view schema
- **Testing Tools**: Run tests with coverage, build projects
- **Project Tools**: Get statistics, search code, view structure
- **Project-Specific**: Tailored for this codebase

### Standard MCP Servers (Claude Desktop)

#### Filesystem Server
- Enhanced file search and operations
- Faster codebase navigation
- Better file content access

#### Git Server
- View commit history
- Check repository status
- Review changes
- Manage branches

#### GitHub Server (optional)
- Create issues and pull requests
- Search repositories
- Access repository data

#### SQLite Server (optional)
- Query your database directly
- Inspect data during development
- Test database queries

## 📚 Documentation

### For MCP Setup
Read **`MCP_CONFIGURATION_GUIDE.md`** for:
- Detailed setup instructions
- All available MCP servers
- Troubleshooting guide
- Security best practices
- Advanced configuration

### For Testing Workflow
Read **`testing-workflow.md`** for:
- Quick test commands
- Testing patterns
- AI coding workflow
- Common assertions
- Test troubleshooting

### For Log Locations
Read **`LOGS.md`** for:
- Application log file paths
- Quick access commands
- Log search examples
- What's logged in each service
- Recent logging changes

## 🔐 Security Notes

**Important**: Never commit your actual `claude_desktop_config.json` to Git if it contains tokens!

The `.gitignore` already excludes:
- `claude_desktop_config.json`
- `*.backup`

Always use:
- Template files for sharing
- Environment variables for tokens
- Minimal token scopes

## ✅ Development Workflow

**IMPORTANT**: After making any code changes, you must verify a successful build:

```powershell
# Build the entire solution
dotnet build

# Or build specific project
dotnet build AlUmmahNow.FileScanService
dotnet build AlUmmahNow.VisualImporter
```

**Required**: All changes must compile successfully before committing or considering the work complete.

## 💡 Usage Examples

Once MCP is configured, you can ask Claude:

### File Operations
```
"Search for all SignalR hub implementations"
"Find all files that use ILogger"
"Show me the FileScanEngine implementation"
```

### Git Operations
```
"Show me recent commits"
"What files have changed since last commit?"
"Show git status"
```

### Database Queries (if SQLite MCP configured)
```
"Query all scan sessions from the last 7 days"
"Show me the schema for the ScanSessions table"
"Count how many videos have been scanned"
```

### Testing
```
"Run all tests and show me the results"
"Generate a coverage report"
"Create unit tests for FileScanHub"
```

## 🆘 Troubleshooting

### MCP servers not appearing?
1. Verify Node.js is installed: `node --version`
2. Check config file location and format
3. Restart Claude Desktop
4. Check Claude Desktop logs: `%APPDATA%\Claude\logs`

### JSON syntax errors?
Validate your config:
```powershell
Get-Content "$env:APPDATA\Claude\claude_desktop_config.json" | ConvertFrom-Json
```

### Need help?
- Read `MCP_CONFIGURATION_GUIDE.md`
- Run `.\.claude\setup-mcp.ps1 -Verify`
- Check Claude Desktop logs

## 🔄 Updating MCP Configuration

To update your MCP configuration:

1. **Backup existing config**
   ```powershell
   Copy-Item "$env:APPDATA\Claude\claude_desktop_config.json" "$env:APPDATA\Claude\claude_desktop_config.json.backup"
   ```

2. **Edit configuration**
   ```powershell
   notepad "$env:APPDATA\Claude\claude_desktop_config.json"
   ```

3. **Verify changes**
   ```powershell
   .\.claude\setup-mcp.ps1 -Verify
   ```

4. **Restart Claude Desktop**

## 📝 Log Locations

Application logs are critical for debugging and monitoring. Here are the log file locations:

### FileScanService Logs
**Location**: `F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\`

**Pattern**: `filescan-service-YYYYMMDD.txt`

**Example**: `filescan-service-20251008.txt`

**Contains**:
- SignalR connection/disconnection events
- Hub method invocations with parameters
- Outbound SignalR messages sent to clients
- Hub method return values
- Scan operations and progress
- File watch operations
- Approval workflow events
- Errors and warnings

**Quick Access**:
```powershell
# View latest log
Get-Content "F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\filescan-service-$(Get-Date -Format 'yyyyMMdd').txt" -Tail 50 -Wait

# Search for errors
Select-String -Path "F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\*.txt" -Pattern "ERR"
```

### VisualImporter Logs
**Location**: `C:\Users\MEYWD\AppData\Local\AlUmmahNow\VisualImporter\Logs\`

**Pattern**: `visual-importer-YYYYMMDD_NNN.txt`

**Example**: `visual-importer-20251008_001.txt`

**Contains**:
- Application startup/shutdown events
- FileScanService connection status
- Database initialization
- Service status changes
- Settings loading
- UI operations
- Errors and warnings

**Quick Access**:
```powershell
# View latest log
Get-ChildItem "$env:LOCALAPPDATA\AlUmmahNow\VisualImporter\Logs\" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Tail 50 -Wait

# Search for errors in all logs
Select-String -Path "$env:LOCALAPPDATA\AlUmmahNow\VisualImporter\Logs\*.txt" -Pattern "ERR"
```

### Log Levels

Logs use Serilog structured logging with the following levels:

- **TRACE**: Very detailed diagnostic information
- **DEBUG**: Detailed information for debugging (method parameters, return values, message details)
- **INF** (Information): General informational messages about application flow
- **WRN** (Warning): Warning messages for potentially harmful situations
- **ERR** (Error): Error messages for failures and exceptions
- **FTL** (Fatal): Critical errors that cause application termination

### Viewing Logs in Claude

To view logs, use:
```
Read the latest FileScanService log
```

Or for specific dates:
```
Read F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.FileScanService\logs\filescan-service-20251008.txt
```

For VisualImporter:
```
Read C:\Users\MEYWD\AppData\Local\AlUmmahNow\VisualImporter\Logs\visual-importer-20251008_001.txt
```

## 📖 Additional Resources

### Official Documentation
- Claude Code Docs: https://docs.claude.com/en/docs/claude-code
- MCP Specification: https://spec.modelcontextprotocol.io/
- MCP Servers: https://github.com/modelcontextprotocol/servers

### Project Documentation
- Testing Guide: `../TESTING_GUIDE.md`
- Coverage Summary: `../TEST_COVERAGE_SUMMARY.md`

## 🎯 Next Steps

1. ✅ Set up MCP using automated script or manual steps
2. ✅ Restart Claude Desktop
3. ✅ Test MCP by asking Claude to search your codebase
4. ✅ Read testing workflow for efficient AI-assisted development
5. ✅ Explore advanced MCP servers as needed
6. ✅ **Always run `dotnet build` after making code changes to verify successful compilation**

---

*Last Updated: 2025-10-08*
*For the AlUmmahNowTools Project*

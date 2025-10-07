# Quick Start: Custom MCP Server

## Installation (30 seconds)

```powershell
# Navigate to the MCP server directory
cd .claude\mcp-server

# Run installation script (does everything for you)
.\install.ps1
```

That's it! The script will:
- ✅ Check Node.js installation
- ✅ Install dependencies
- ✅ Test the server
- ✅ Add to Claude Code
- ✅ Verify everything works

## Manual Installation

If you prefer to do it manually:

```bash
# 1. Install dependencies
cd .claude\mcp-server
npm install

# 2. Add to Claude Code
claude mcp add alummah node F:\Code\Repos\AlUmmahNowTools\.claude\mcp-server\index.js

# 3. Check status
claude mcp status

# 4. List servers
claude mcp list
```

## What You Get

### 🗃️ Database Tools
```
"Query all scan sessions from the last 7 days"
"Show me the database schema"
"Get scanned files with status Completed"
"Count total files in the database"
```

### 🧪 Testing Tools
```
"Run all tests"
"Run tests with coverage"
"Run only SignalR tests"
"Generate HTML coverage report"
"Build the solution"
```

### 📊 Project Tools
```
"Show project statistics"
"Search for all async methods"
"Find TODO comments"
"Show project directory structure"
```

## Usage

After installation, just use Claude Code normally:

```bash
claude
```

Then ask questions like:
- "Query the database for scans from yesterday"
- "Run tests for FileScanHub"
- "Show me project stats with details"
- "Search for all SignalR implementations"

## Available Tools

### Database (4 tools)
- `query_database` - Custom SQL queries
- `query_scan_sessions` - Filtered scan session queries
- `query_scanned_files` - Filtered file queries
- `get_database_schema` - View table schemas

### Testing (3 tools)
- `run_tests` - Run .NET tests
- `run_build` - Build projects
- `test_coverage_report` - Generate coverage reports

### Project (3 tools)
- `get_project_stats` - Project metrics
- `search_code` - Code pattern search
- `get_project_structure` - Directory tree

## Verify Installation

```bash
# Check MCP servers
claude mcp list

# Should show:
# alummah    running    node F:\Code\Repos\...\index.js

# Check status
claude mcp status

# Test it
claude
> "Show me project statistics"
```

## Troubleshooting

### Server not starting?
```bash
# Check Node.js version (need 18+)
node --version

# Reinstall dependencies
cd .claude\mcp-server
npm install

# Test manually
node index.js
```

### Not seeing tools in Claude Code?
```bash
# Restart Claude Code
# Then verify
claude mcp status
```

### Database errors?
Check that the database exists:
```bash
ls AlUmmahNow.Data\alummah.db
```

### Remove and reinstall
```bash
# Remove server
claude mcp remove alummah

# Reinstall
.\install.ps1
```

## Examples

### Example 1: Database Query
```
You: "Query scan sessions from the last 3 days with Completed status"

Claude: [Uses query_scan_sessions tool]
Results showing completed scans from the last 3 days...
```

### Example 2: Run Tests
```
You: "Run tests for SignalR with coverage"

Claude: [Uses run_tests tool with filter="SignalR" and coverage=true]
Test results with coverage data...
```

### Example 3: Project Stats
```
You: "Show detailed project statistics"

Claude: [Uses get_project_stats tool with detailed=true]
Comprehensive project metrics with breakdown...
```

## Configuration

The server auto-detects paths, but you can override:

```bash
# Set custom database path
$env:ALUMMAH_DB_PATH = "C:\custom\path\to\database.db"

# Set custom project root
$env:ALUMMAH_PROJECT_ROOT = "C:\custom\project\root"
```

## Debug Mode

Enable debug logging:

```bash
$env:DEBUG = "true"
node .claude\mcp-server\index.js
```

View logs:
```bash
claude mcp logs alummah
```

## Next Steps

1. ✅ Install the MCP server
2. ✅ Test with a simple query
3. ✅ Read the full guide: `CUSTOM_MCP_GUIDE.md`
4. ✅ Customize tools for your workflow

---

**Installation Time**: ~30 seconds
**Requirements**: Node.js 18+, .NET 8 SDK
**Files Created**: 6 tool implementations, 1 main server

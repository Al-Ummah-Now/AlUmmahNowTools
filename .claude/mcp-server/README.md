# AlUmmahNow MCP Server

Custom MCP server for the AlUmmahNowTools project, providing database access, testing, and project utilities.

## Installation

```bash
# 1. Install dependencies
cd .claude/mcp-server
npm install

# 2. Add to Claude Code
claude mcp add alummah node F:\Code\Repos\AlUmmahNowTools\.claude\mcp-server\index.js

# 3. Verify
claude mcp list
claude mcp status
```

## Available Tools

### Database Tools

1. **query_database** - Execute custom SQL queries
2. **query_scan_sessions** - Get scan sessions with filters
3. **query_scanned_files** - Get scanned video files
4. **get_database_schema** - View database schema

### Testing Tools

1. **run_tests** - Run .NET tests with optional filters and coverage
2. **run_build** - Build the solution or specific projects
3. **test_coverage_report** - Generate coverage reports

### Project Tools

1. **get_project_stats** - Get project statistics and metrics
2. **search_code** - Search for code patterns
3. **get_project_structure** - View directory structure

## Usage Examples

### Database Queries

```
"Query all scan sessions from the last 7 days"
"Show me the database schema for ScanSessions"
"Get all scanned files with Completed status"
```

### Testing

```
"Run all tests"
"Run tests with coverage for SignalR components"
"Generate an HTML coverage report"
"Build the FileScanService project"
```

### Project Operations

```
"Show me project statistics"
"Search for all async methods in FileScanService"
"Show the project structure"
```

## Configuration

Environment variables (optional):

```bash
# Database path
export ALUMMAH_DB_PATH="F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.Data\alummah.db"

# Project root
export ALUMMAH_PROJECT_ROOT="F:\Code\Repos\AlUmmahNowTools"
```

## Debugging

Enable debug mode:

```bash
DEBUG=true node index.js
```

View logs:

```bash
claude mcp logs alummah
```

## Requirements

- Node.js 18+
- .NET 8 SDK
- better-sqlite3 package
- @modelcontextprotocol/sdk package

## Troubleshooting

**Database not found?**
- Check that the database exists at the configured path
- Verify permissions

**Tests failing?**
- Make sure .NET SDK is installed
- Check that you're in the project root directory

**Server not starting?**
- Verify Node.js version: `node --version`
- Reinstall dependencies: `npm install`
- Check for syntax errors in the code

## License

MIT

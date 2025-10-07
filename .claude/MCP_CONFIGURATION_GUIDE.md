# MCP (Model Context Protocol) Configuration Guide

## What is MCP?

MCP (Model Context Protocol) is an open protocol that allows Claude and other AI assistants to connect to external data sources and tools. MCP servers provide:

- **Additional Tools**: Database queries, web search, file operations, etc.
- **Context Sources**: Access to external data, APIs, documentation
- **Integration**: Connect to your development environment and services

## Quick Start

### 1. Locate Claude Desktop Config File

**Windows**:
```
%APPDATA%\Claude\claude_desktop_config.json
```
Full path: `C:\Users\YourUsername\AppData\Roaming\Claude\claude_desktop_config.json`

**macOS**:
```
~/Library/Application Support/Claude/claude_desktop_config.json
```

**Linux**:
```
~/.config/Claude/claude_desktop_config.json
```

### 2. Basic Configuration Format

```json
{
  "mcpServers": {
    "server-name": {
      "command": "command-to-run",
      "args": ["arg1", "arg2"],
      "env": {
        "ENVIRONMENT_VARIABLE": "value"
      }
    }
  }
}
```

---

## Recommended MCP Servers for Development

### 1. Filesystem Server
**Purpose**: Enhanced file operations, search, and directory management

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "F:\\Code\\Repos\\AlUmmahNowTools"]
    }
  }
}
```

**Provides**:
- `read_file` - Read file contents
- `write_file` - Write to files
- `list_directory` - List directory contents
- `search_files` - Search for files by pattern
- `get_file_info` - Get file metadata

### 2. GitHub Server
**Purpose**: Interact with GitHub repositories, issues, PRs

```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "your_github_token_here"
      }
    }
  }
}
```

**Provides**:
- `create_issue` - Create GitHub issues
- `create_pull_request` - Create pull requests
- `search_repositories` - Search for repositories
- `get_file_contents` - Get file from repository
- `fork_repository` - Fork a repository

**Setup**:
1. Go to GitHub Settings → Developer settings → Personal access tokens
2. Generate new token (classic) with `repo` scope
3. Add token to config

### 3. Git Server
**Purpose**: Local Git operations

```json
{
  "mcpServers": {
    "git": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-git", "--repository", "F:\\Code\\Repos\\AlUmmahNowTools"]
    }
  }
}
```

**Provides**:
- `git_status` - Check repository status
- `git_diff` - View changes
- `git_commit` - Create commits
- `git_log` - View commit history
- `git_branch` - Manage branches

### 4. SQLite Server
**Purpose**: Query and manage SQLite databases

```json
{
  "mcpServers": {
    "sqlite": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-sqlite", "F:\\Code\\Repos\\AlUmmahNowTools\\database.db"]
    }
  }
}
```

**Provides**:
- `query` - Execute SQL queries
- `list_tables` - List all tables
- `describe_table` - Get table schema
- `execute` - Execute SQL statements

### 5. PostgreSQL Server
**Purpose**: Connect to PostgreSQL databases

```json
{
  "mcpServers": {
    "postgres": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-postgres"],
      "env": {
        "POSTGRES_CONNECTION_STRING": "postgresql://user:password@localhost:5432/database"
      }
    }
  }
}
```

### 6. Brave Search Server
**Purpose**: Web search capabilities

```json
{
  "mcpServers": {
    "brave-search": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-brave-search"],
      "env": {
        "BRAVE_API_KEY": "your_brave_api_key"
      }
    }
  }
}
```

**Setup**:
1. Go to https://brave.com/search/api/
2. Sign up for API access
3. Get your API key

### 7. Puppeteer Server
**Purpose**: Browser automation and web scraping

```json
{
  "mcpServers": {
    "puppeteer": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-puppeteer"]
    }
  }
}
```

**Provides**:
- `navigate` - Navigate to URL
- `screenshot` - Take screenshots
- `click` - Click elements
- `type` - Type into inputs
- `extract` - Extract data from pages

### 8. Slack Server
**Purpose**: Send messages to Slack

```json
{
  "mcpServers": {
    "slack": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-slack"],
      "env": {
        "SLACK_BOT_TOKEN": "xoxb-your-token",
        "SLACK_TEAM_ID": "T1234567890"
      }
    }
  }
}
```

---

## Complete Example Configuration

Here's a complete `claude_desktop_config.json` for development:

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-filesystem",
        "F:\\Code\\Repos\\AlUmmahNowTools"
      ]
    },
    "git": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-git",
        "--repository",
        "F:\\Code\\Repos\\AlUmmahNowTools"
      ]
    },
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "ghp_your_token_here"
      }
    },
    "brave-search": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-brave-search"],
      "env": {
        "BRAVE_API_KEY": "your_brave_api_key_here"
      }
    },
    "sqlite": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-sqlite",
        "F:\\Code\\Repos\\AlUmmahNowTools\\app.db"
      ]
    }
  },
  "globalShortcut": "Ctrl+Space"
}
```

---

## Configuration for Your Project

### Recommended Setup for AlUmmahNowTools

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-filesystem",
        "F:\\Code\\Repos\\AlUmmahNowTools"
      ]
    },
    "git": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-git",
        "--repository",
        "F:\\Code\\Repos\\AlUmmahNowTools"
      ]
    },
    "sqlite": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-sqlite",
        "F:\\Code\\Repos\\AlUmmahNowTools\\AlUmmahNow.Data\\alummah.db"
      ]
    }
  }
}
```

**Benefits for Your Project**:
- Enhanced file search across your codebase
- Better Git integration for commits and history
- Direct database queries for testing
- Faster file operations

---

## Installation Steps

### Step 1: Install Node.js (if not already installed)
MCP servers run via npm/npx, so you need Node.js:

Download from: https://nodejs.org/

Verify installation:
```bash
node --version
npm --version
```

### Step 2: Create/Edit Config File

1. Open the config file location for your OS
2. Create `claude_desktop_config.json` if it doesn't exist
3. Add your MCP server configuration
4. Save the file

### Step 3: Restart Claude Desktop

Close and reopen Claude Desktop for changes to take effect.

### Step 4: Verify MCP Servers

In Claude Code or Claude Desktop, you should see:
- New tools available (check with `/help` or toolbar)
- MCP server indicators
- Available MCP tools when relevant

---

## Using MCP Tools

### In Claude Code (CLI)

MCP tools are automatically available. For example:

```bash
# If filesystem MCP is configured
claude: "Search for all SignalR hub files"
# MCP filesystem server will search your codebase

# If git MCP is configured
claude: "Show me recent commits"
# MCP git server will run git log

# If database MCP is configured
claude: "Query all scan sessions from the database"
# MCP sqlite server will execute query
```

### In Claude Desktop

MCP tools appear in the tool palette and are used automatically when relevant to your request.

---

## Troubleshooting

### 1. Config File Not Found

**Windows**: Create directory first:
```powershell
mkdir "$env:APPDATA\Claude"
New-Item "$env:APPDATA\Claude\claude_desktop_config.json"
```

**macOS/Linux**:
```bash
mkdir -p ~/Library/Application\ Support/Claude
touch ~/Library/Application\ Support/Claude/claude_desktop_config.json
```

### 2. MCP Server Not Starting

Check Claude Desktop logs:

**Windows**:
```
%APPDATA%\Claude\logs
```

**macOS**:
```
~/Library/Logs/Claude
```

Common issues:
- Node.js not installed
- Invalid JSON in config file
- Missing environment variables
- Incorrect file paths

### 3. Permission Errors

Make sure the paths in your config exist and are accessible:

```bash
# Test if path exists
ls "F:\Code\Repos\AlUmmahNowTools"

# Check permissions
icacls "F:\Code\Repos\AlUmmahNowTools"
```

### 4. Testing MCP Configuration

Create a minimal config first:

```json
{
  "mcpServers": {
    "test": {
      "command": "node",
      "args": ["--version"]
    }
  }
}
```

If this works, add more servers incrementally.

---

## Security Best Practices

### 1. Never Commit Tokens

Don't commit `claude_desktop_config.json` with secrets to Git:

```gitignore
# .gitignore
claude_desktop_config.json
```

### 2. Use Environment Variables

Instead of hardcoding tokens:

```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "${GITHUB_TOKEN}"
      }
    }
  }
}
```

Then set system environment variable:
```powershell
# Windows PowerShell
[Environment]::SetEnvironmentVariable("GITHUB_TOKEN", "your_token", "User")
```

### 3. Scope Tokens Appropriately

- GitHub: Use minimal scopes needed (`repo` for private repos, `public_repo` for public)
- Database: Use read-only credentials when possible
- API keys: Restrict to specific IP addresses if supported

### 4. Restrict File Access

Only grant filesystem access to specific directories:

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-filesystem",
        "F:\\Code\\Repos\\AlUmmahNowTools",
        "--allowed-paths",
        "F:\\Code\\Repos\\AlUmmahNowTools"
      ]
    }
  }
}
```

---

## Advanced Configuration

### Custom MCP Server

You can create your own MCP server:

```json
{
  "mcpServers": {
    "custom": {
      "command": "node",
      "args": ["F:\\Code\\custom-mcp-server\\index.js"]
    }
  }
}
```

### Python MCP Server

```json
{
  "mcpServers": {
    "python-tools": {
      "command": "python",
      "args": ["-m", "custom_mcp_server"]
    }
  }
}
```

### Docker MCP Server

```json
{
  "mcpServers": {
    "docker": {
      "command": "docker",
      "args": ["run", "--rm", "-i", "mcp-server-image"]
    }
  }
}
```

---

## MCP Resources

### Official Resources
- **MCP Specification**: https://spec.modelcontextprotocol.io/
- **MCP Servers**: https://github.com/modelcontextprotocol/servers
- **Documentation**: https://modelcontextprotocol.io/

### Community Servers
- **Awesome MCP Servers**: https://github.com/punkpeye/awesome-mcp-servers
- **MCP Hub**: https://mcp-hub.com/

### Creating MCP Servers
- **TypeScript SDK**: `npm install @modelcontextprotocol/sdk`
- **Python SDK**: `pip install mcp`

---

## Quick Reference Commands

### View Current MCP Configuration
```powershell
# Windows
cat "$env:APPDATA\Claude\claude_desktop_config.json"

# PowerShell with formatting
Get-Content "$env:APPDATA\Claude\claude_desktop_config.json" | ConvertFrom-Json | ConvertTo-Json
```

### Validate JSON
```bash
# Using Node.js
node -e "JSON.parse(require('fs').readFileSync('$env:APPDATA/Claude/claude_desktop_config.json'))"
```

### Backup Config
```powershell
Copy-Item "$env:APPDATA\Claude\claude_desktop_config.json" "$env:APPDATA\Claude\claude_desktop_config.json.backup"
```

---

## Example: Testing Database MCP

After configuring SQLite MCP server, you can:

**Prompt to Claude**:
```
Query the ScanSessions table and show me all scans from the last 7 days
```

**Claude will use MCP to**:
1. Connect to your SQLite database
2. Execute: `SELECT * FROM ScanSessions WHERE CreatedAt >= datetime('now', '-7 days')`
3. Return formatted results

---

## Next Steps

1. ✅ Install Node.js
2. ✅ Create/edit `claude_desktop_config.json`
3. ✅ Add filesystem and git servers (minimum)
4. ✅ Restart Claude Desktop
5. ✅ Test with simple prompts
6. ✅ Add more servers as needed

---

*Last Updated: 2025-10-06*
*For more information: https://docs.claude.com/en/docs/model-context-protocol*

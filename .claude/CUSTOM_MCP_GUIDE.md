# Custom MCP Server Guide for Claude Code

## Overview

This guide shows you how to create and add custom MCP servers to Claude Code CLI for project-specific tools.

## Quick Start - Add Custom MCP to Claude Code

### Method 1: Using npm package
```bash
claude mcp add server-name package-name [args...]
```

### Method 2: Using local script
```bash
claude mcp add server-name node path/to/server.js [args...]
```

### Method 3: Using executable
```bash
claude mcp add server-name path/to/executable [args...]
```

---

## Example: Custom AlUmmahNow MCP Server

I've created a custom MCP server specifically for this project that provides:
- Database query tools
- Test running tools
- Project-specific utilities

### Installation

```bash
# Navigate to MCP server directory
cd F:\Code\Repos\AlUmmahNowTools\.claude\mcp-server

# Install dependencies
npm install

# Add to Claude Code
claude mcp add alummah node F:\Code\Repos\AlUmmahNowTools\.claude\mcp-server\index.js

# Verify it's added
claude mcp list

# Check status
claude mcp status
```

### Available Tools

Once installed, you'll have access to:

#### 1. `query_database`
Query the SQLite database directly
```javascript
// Usage in prompts:
"Query all scan sessions from the last 7 days"
"Show me pending approvals"
"Count total files scanned"
```

#### 2. `run_tests`
Run project tests with options
```javascript
// Usage in prompts:
"Run all tests"
"Run tests with coverage"
"Run only SignalR tests"
```

#### 3. `get_project_stats`
Get project statistics
```javascript
// Usage in prompts:
"Show me project statistics"
"How many files in the project?"
"What's the test coverage?"
```

#### 4. `search_code`
Advanced code search
```javascript
// Usage in prompts:
"Search for all SignalR hubs"
"Find all async methods in FileScanService"
"Show me all TODO comments"
```

---

## Creating Your Own Custom MCP Server

### Structure

```
.claude/mcp-server/
├── package.json          # Dependencies
├── index.js             # Main server entry point
├── tools/               # Tool implementations
│   ├── database.js      # Database tools
│   ├── testing.js       # Test tools
│   └── project.js       # Project utilities
└── README.md            # Documentation
```

### Basic Template

**package.json**:
```json
{
  "name": "alummah-mcp-server",
  "version": "1.0.0",
  "description": "Custom MCP server for AlUmmahNowTools",
  "type": "module",
  "main": "index.js",
  "dependencies": {
    "@modelcontextprotocol/sdk": "^0.5.0",
    "better-sqlite3": "^9.0.0"
  }
}
```

**index.js**:
```javascript
#!/usr/bin/env node
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';

// Import tool implementations
import { getDatabaseTools, handleDatabaseTool } from './tools/database.js';
import { getTestTools, handleTestTool } from './tools/testing.js';
import { getProjectTools, handleProjectTool } from './tools/project.js';

class AlUmmahMCPServer {
  constructor() {
    this.server = new Server(
      {
        name: 'alummah-mcp-server',
        version: '1.0.0',
      },
      {
        capabilities: {
          tools: {},
        },
      }
    );

    this.setupHandlers();
    this.setupErrorHandling();
  }

  setupHandlers() {
    // List all available tools
    this.server.setRequestHandler(ListToolsRequestSchema, async () => ({
      tools: [
        ...getDatabaseTools(),
        ...getTestTools(),
        ...getProjectTools(),
      ],
    }));

    // Handle tool calls
    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      const { name, arguments: args } = request.params;

      try {
        // Route to appropriate handler
        if (name.startsWith('query_')) {
          return await handleDatabaseTool(name, args);
        } else if (name.startsWith('run_')) {
          return await handleTestTool(name, args);
        } else if (name.startsWith('get_')) {
          return await handleProjectTool(name, args);
        }

        throw new Error(`Unknown tool: ${name}`);
      } catch (error) {
        return {
          content: [
            {
              type: 'text',
              text: `Error: ${error.message}`,
            },
          ],
        };
      }
    });
  }

  setupErrorHandling() {
    this.server.onerror = (error) => {
      console.error('[MCP Error]', error);
    };

    process.on('SIGINT', async () => {
      await this.server.close();
      process.exit(0);
    });
  }

  async run() {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    console.error('AlUmmah MCP Server running on stdio');
  }
}

// Start server
const server = new AlUmmahMCPServer();
server.run().catch(console.error);
```

### Tool Implementation Example

**tools/database.js**:
```javascript
import Database from 'better-sqlite3';
import path from 'path';

const DB_PATH = 'F:\\Code\\Repos\\AlUmmahNowTools\\AlUmmahNow.Data\\alummah.db';

export function getDatabaseTools() {
  return [
    {
      name: 'query_database',
      description: 'Execute SQL query on the project database',
      inputSchema: {
        type: 'object',
        properties: {
          query: {
            type: 'string',
            description: 'SQL query to execute',
          },
          params: {
            type: 'array',
            description: 'Query parameters',
            items: { type: 'string' },
          },
        },
        required: ['query'],
      },
    },
    {
      name: 'query_scan_sessions',
      description: 'Get scan sessions with filters',
      inputSchema: {
        type: 'object',
        properties: {
          limit: {
            type: 'number',
            description: 'Number of results to return',
            default: 10,
          },
          status: {
            type: 'string',
            description: 'Filter by status (Pending, InProgress, Completed, Failed)',
          },
          days: {
            type: 'number',
            description: 'Filter by days ago',
          },
        },
      },
    },
  ];
}

export async function handleDatabaseTool(name, args) {
  const db = new Database(DB_PATH, { readonly: true });

  try {
    if (name === 'query_database') {
      const stmt = db.prepare(args.query);
      const results = args.params
        ? stmt.all(...args.params)
        : stmt.all();

      return {
        content: [
          {
            type: 'text',
            text: JSON.stringify(results, null, 2),
          },
        ],
      };
    }

    if (name === 'query_scan_sessions') {
      let query = 'SELECT * FROM ScanSessions';
      const conditions = [];
      const params = [];

      if (args.status) {
        conditions.push('Status = ?');
        params.push(args.status);
      }

      if (args.days) {
        conditions.push("CreatedAt >= datetime('now', '-' || ? || ' days')");
        params.push(args.days);
      }

      if (conditions.length > 0) {
        query += ' WHERE ' + conditions.join(' AND ');
      }

      query += ' ORDER BY CreatedAt DESC LIMIT ?';
      params.push(args.limit || 10);

      const stmt = db.prepare(query);
      const results = stmt.all(...params);

      return {
        content: [
          {
            type: 'text',
            text: `Found ${results.length} scan sessions:\n\n${JSON.stringify(results, null, 2)}`,
          },
        ],
      };
    }
  } finally {
    db.close();
  }
}
```

**tools/testing.js**:
```javascript
import { exec } from 'child_process';
import { promisify } from 'util';

const execAsync = promisify(exec);
const PROJECT_ROOT = 'F:\\Code\\Repos\\AlUmmahNowTools';

export function getTestTools() {
  return [
    {
      name: 'run_tests',
      description: 'Run project tests with optional coverage',
      inputSchema: {
        type: 'object',
        properties: {
          filter: {
            type: 'string',
            description: 'Test filter pattern (e.g., "SignalR", "FileScan")',
          },
          coverage: {
            type: 'boolean',
            description: 'Collect code coverage',
            default: false,
          },
          verbosity: {
            type: 'string',
            enum: ['quiet', 'minimal', 'normal', 'detailed'],
            default: 'normal',
          },
        },
      },
    },
  ];
}

export async function handleTestTool(name, args) {
  if (name === 'run_tests') {
    let command = 'dotnet test';

    if (args.filter) {
      command += ` --filter "FullyQualifiedName~${args.filter}"`;
    }

    if (args.coverage) {
      command += ' --collect:"XPlat Code Coverage"';
    }

    command += ` --verbosity ${args.verbosity || 'normal'}`;

    try {
      const { stdout, stderr } = await execAsync(command, {
        cwd: PROJECT_ROOT,
        maxBuffer: 1024 * 1024 * 10, // 10MB buffer
      });

      return {
        content: [
          {
            type: 'text',
            text: `Test Results:\n\n${stdout}${stderr ? '\n\nErrors:\n' + stderr : ''}`,
          },
        ],
      };
    } catch (error) {
      return {
        content: [
          {
            type: 'text',
            text: `Test execution failed:\n${error.stdout}\n${error.stderr}`,
          },
        ],
      };
    }
  }
}
```

**tools/project.js**:
```javascript
import { readdir, stat } from 'fs/promises';
import path from 'path';

const PROJECT_ROOT = 'F:\\Code\\Repos\\AlUmmahNowTools';

export function getProjectTools() {
  return [
    {
      name: 'get_project_stats',
      description: 'Get project statistics and metrics',
      inputSchema: {
        type: 'object',
        properties: {},
      },
    },
  ];
}

async function countFiles(dir, extensions = []) {
  let count = 0;
  let totalSize = 0;

  async function scan(currentDir) {
    const entries = await readdir(currentDir, { withFileTypes: true });

    for (const entry of entries) {
      const fullPath = path.join(currentDir, entry.name);

      if (entry.isDirectory()) {
        // Skip common directories
        if (['node_modules', 'bin', 'obj', '.git'].includes(entry.name)) {
          continue;
        }
        await scan(fullPath);
      } else if (entry.isFile()) {
        const ext = path.extname(entry.name);
        if (extensions.length === 0 || extensions.includes(ext)) {
          count++;
          const stats = await stat(fullPath);
          totalSize += stats.size;
        }
      }
    }
  }

  await scan(dir);
  return { count, totalSize };
}

export async function handleProjectTool(name, args) {
  if (name === 'get_project_stats') {
    const csFiles = await countFiles(PROJECT_ROOT, ['.cs']);
    const testFiles = await countFiles(path.join(PROJECT_ROOT, 'AlUmmahNow.Tests'), ['.cs']);
    const allFiles = await countFiles(PROJECT_ROOT);

    const stats = {
      'C# Files': csFiles.count,
      'C# Size': `${(csFiles.totalSize / 1024 / 1024).toFixed(2)} MB`,
      'Test Files': testFiles.count,
      'Total Files': allFiles.count,
      'Total Size': `${(allFiles.totalSize / 1024 / 1024).toFixed(2)} MB`,
    };

    return {
      content: [
        {
          type: 'text',
          text: `Project Statistics:\n\n${JSON.stringify(stats, null, 2)}`,
        },
      ],
    };
  }
}
```

---

## Installation Steps

### 1. Create MCP Server Directory
```bash
mkdir .claude\mcp-server
cd .claude\mcp-server
```

### 2. Initialize npm Project
```bash
npm init -y
```

### 3. Install Dependencies
```bash
npm install @modelcontextprotocol/sdk better-sqlite3
```

### 4. Create Server Files
Copy the code examples above into appropriate files.

### 5. Make Entry Point Executable
```bash
# Linux/macOS
chmod +x index.js

# Windows - already executable
```

### 6. Add to Claude Code
```bash
claude mcp add alummah node F:\Code\Repos\AlUmmahNowTools\.claude\mcp-server\index.js
```

### 7. Verify Installation
```bash
claude mcp list
claude mcp status
```

---

## Testing Your MCP Server

### Test Manually
```bash
# Navigate to server directory
cd .claude\mcp-server

# Run directly
node index.js
```

### Test with Claude Code
```bash
# Check if server is running
claude mcp status

# Use in conversation
claude
> "Query the database for recent scan sessions"
> "Run tests with coverage"
> "Show me project statistics"
```

---

## Debugging

### Enable Debug Logging
```javascript
// In index.js
const DEBUG = process.env.DEBUG === 'true';

function debug(...args) {
  if (DEBUG) {
    console.error('[DEBUG]', ...args);
  }
}
```

Run with debugging:
```bash
DEBUG=true node index.js
```

### Check Server Logs
```bash
# Claude Code keeps logs
claude mcp logs alummah
```

### Common Issues

**Issue**: Server not starting
```bash
# Check Node.js version (needs 18+)
node --version

# Reinstall dependencies
npm install
```

**Issue**: Tools not appearing
```bash
# Verify server is registered
claude mcp list

# Restart Claude Code
# Then check status
claude mcp status
```

**Issue**: Database access errors
```bash
# Check database path exists
ls F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.Data\alummah.db

# Check permissions
icacls F:\Code\Repos\AlUmmahNowTools\AlUmmahNow.Data\alummah.db
```

---

## Advanced Features

### 1. Add Resources
Provide file/data access to Claude:

```javascript
this.server.setRequestHandler(ListResourcesRequestSchema, async () => ({
  resources: [
    {
      uri: 'file:///test-results',
      name: 'Latest Test Results',
      mimeType: 'application/json',
    },
  ],
}));

this.server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
  // Return resource content
});
```

### 2. Add Prompts
Provide pre-defined prompts:

```javascript
this.server.setRequestHandler(ListPromptsRequestSchema, async () => ({
  prompts: [
    {
      name: 'analyze-coverage',
      description: 'Analyze test coverage and suggest improvements',
    },
  ],
}));
```

### 3. Environment Variables
```javascript
// index.js
const CONFIG = {
  dbPath: process.env.ALUMMAH_DB_PATH || 'F:\\Code\\Repos\\AlUmmahNowTools\\AlUmmahNow.Data\\alummah.db',
  projectRoot: process.env.ALUMMAH_PROJECT_ROOT || 'F:\\Code\\Repos\\AlUmmahNowTools',
};
```

Add to Claude Code with environment variables:
```bash
claude mcp add alummah node .claude/mcp-server/index.js --env ALUMMAH_DB_PATH=path/to/db
```

---

## Example Usage in Claude Code

Once installed, you can use your custom MCP tools naturally:

```
User: "Query the database for all scan sessions from the last 7 days"
Claude: [Uses query_scan_sessions tool]

User: "Run tests for SignalR components with coverage"
Claude: [Uses run_tests tool with filter and coverage]

User: "Show me project statistics"
Claude: [Uses get_project_stats tool]

User: "Search the codebase for all async methods in FileScanService"
Claude: [Uses search_code tool]
```

---

## Next Steps

1. ✅ Create the MCP server structure
2. ✅ Implement project-specific tools
3. ✅ Test locally with `node index.js`
4. ✅ Add to Claude Code with `claude mcp add`
5. ✅ Verify with `claude mcp status`
6. ✅ Use in Claude Code conversations

---

*Last Updated: 2025-10-06*
*For AlUmmahNowTools Project*

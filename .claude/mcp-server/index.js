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

const DEBUG = process.env.DEBUG === 'true';

function debug(...args) {
  if (DEBUG) {
    console.error('[DEBUG]', ...args);
  }
}

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
    this.server.setRequestHandler(ListToolsRequestSchema, async () => {
      debug('Listing tools');
      const tools = [
        ...getDatabaseTools(),
        ...getTestTools(),
        ...getProjectTools(),
      ];
      debug('Available tools:', tools.map(t => t.name));
      return { tools };
    });

    // Handle tool calls
    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      const { name, arguments: args } = request.params;
      debug('Tool called:', name, 'with args:', args);

      try {
        let result;

        // Route to appropriate handler
        if (name.startsWith('query_') || name === 'get_database_schema') {
          result = await handleDatabaseTool(name, args);
        } else if (name.startsWith('run_') || name.startsWith('test_')) {
          result = await handleTestTool(name, args);
        } else if (name.startsWith('get_') || name.startsWith('search_')) {
          result = await handleProjectTool(name, args);
        } else {
          throw new Error(`Unknown tool: ${name}`);
        }

        debug('Tool result:', result);
        return result;
      } catch (error) {
        console.error('Tool error:', error);
        return {
          content: [
            {
              type: 'text',
              text: `Error executing ${name}: ${error.message}\n\nStack: ${error.stack}`,
            },
          ],
          isError: true,
        };
      }
    });
  }

  setupErrorHandling() {
    this.server.onerror = (error) => {
      console.error('[MCP Error]', error);
    };

    process.on('SIGINT', async () => {
      debug('Shutting down...');
      await this.server.close();
      process.exit(0);
    });

    process.on('SIGTERM', async () => {
      debug('Shutting down...');
      await this.server.close();
      process.exit(0);
    });
  }

  async run() {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    console.error('AlUmmah MCP Server v1.0.0 running on stdio');
    debug('Debug mode enabled');
  }
}

// Start server
const server = new AlUmmahMCPServer();
server.run().catch((error) => {
  console.error('Fatal error:', error);
  process.exit(1);
});

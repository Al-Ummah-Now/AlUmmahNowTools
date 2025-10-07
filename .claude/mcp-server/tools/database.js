import Database from 'better-sqlite3';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PROJECT_ROOT = process.env.ALUMMAH_PROJECT_ROOT ||
  path.resolve(__dirname, '..', '..', '..');
const DB_PATH = process.env.ALUMMAH_DB_PATH ||
  path.join(PROJECT_ROOT, 'AlUmmahNow.Data', 'alummah.db');

export function getDatabaseTools() {
  return [
    {
      name: 'query_database',
      description: 'Execute a SQL query on the AlUmmahNow project database. Use for custom queries.',
      inputSchema: {
        type: 'object',
        properties: {
          query: {
            type: 'string',
            description: 'SQL SELECT query to execute (read-only)',
          },
          limit: {
            type: 'number',
            description: 'Maximum number of results to return',
            default: 100,
          },
        },
        required: ['query'],
      },
    },
    {
      name: 'query_scan_sessions',
      description: 'Get scan sessions with optional filters',
      inputSchema: {
        type: 'object',
        properties: {
          limit: {
            type: 'number',
            description: 'Number of results to return (default: 10)',
            default: 10,
          },
          status: {
            type: 'string',
            description: 'Filter by status (Pending, InProgress, Completed, Failed, Cancelled)',
          },
          days: {
            type: 'number',
            description: 'Only return sessions from the last N days',
          },
        },
      },
    },
    {
      name: 'query_scanned_files',
      description: 'Get scanned video files with optional filters',
      inputSchema: {
        type: 'object',
        properties: {
          limit: {
            type: 'number',
            description: 'Number of results to return',
            default: 20,
          },
          channelId: {
            type: 'string',
            description: 'Filter by channel ID (GUID)',
          },
          status: {
            type: 'string',
            description: 'Filter by import status',
          },
        },
      },
    },
    {
      name: 'get_database_schema',
      description: 'Get the database schema (tables and columns)',
      inputSchema: {
        type: 'object',
        properties: {
          tableName: {
            type: 'string',
            description: 'Specific table name (optional, returns all if not specified)',
          },
        },
      },
    },
  ];
}

export async function handleDatabaseTool(name, args) {
  let db;
  try {
    db = new Database(DB_PATH, { readonly: true, fileMustExist: true });

    if (name === 'get_database_schema') {
      const tables = db.prepare(
        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
      ).all();

      const schema = {};
      for (const { name: tableName } of tables) {
        if (args.tableName && tableName !== args.tableName) continue;

        const columns = db.prepare(`PRAGMA table_info(${tableName})`).all();
        schema[tableName] = columns.map(col => ({
          name: col.name,
          type: col.type,
          notNull: Boolean(col.notnull),
          primaryKey: Boolean(col.pk),
        }));
      }

      return {
        content: [
          {
            type: 'text',
            text: `Database Schema:\n\n${JSON.stringify(schema, null, 2)}`,
          },
        ],
      };
    }

    if (name === 'query_database') {
      // Security: only allow SELECT queries
      const query = args.query.trim().toLowerCase();
      if (!query.startsWith('select')) {
        throw new Error('Only SELECT queries are allowed');
      }

      let finalQuery = args.query;
      if (args.limit && !query.includes('limit')) {
        finalQuery += ` LIMIT ${Math.min(args.limit, 1000)}`;
      }

      const stmt = db.prepare(finalQuery);
      const results = stmt.all();

      return {
        content: [
          {
            type: 'text',
            text: `Query Results (${results.length} rows):\n\n${JSON.stringify(results, null, 2)}`,
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
      params.push(Math.min(args.limit || 10, 100));

      const stmt = db.prepare(query);
      const results = stmt.all(...params);

      // Get summary stats
      const statsStmt = db.prepare(`
        SELECT
          Status,
          COUNT(*) as Count,
          SUM(FilesFound) as TotalFiles
        FROM ScanSessions
        GROUP BY Status
      `);
      const stats = statsStmt.all();

      return {
        content: [
          {
            type: 'text',
            text: `Scan Sessions (${results.length} results):\n\n${JSON.stringify(results, null, 2)}\n\nOverall Statistics:\n${JSON.stringify(stats, null, 2)}`,
          },
        ],
      };
    }

    if (name === 'query_scanned_files') {
      let query = 'SELECT * FROM ScannedVideoFiles';
      const conditions = [];
      const params = [];

      if (args.channelId) {
        conditions.push('ChannelId = ?');
        params.push(args.channelId);
      }

      if (args.status) {
        conditions.push('ImportStatus = ?');
        params.push(args.status);
      }

      if (conditions.length > 0) {
        query += ' WHERE ' + conditions.join(' AND ');
      }

      query += ' ORDER BY CreatedAt DESC LIMIT ?';
      params.push(Math.min(args.limit || 20, 100));

      const stmt = db.prepare(query);
      const results = stmt.all(...params);

      return {
        content: [
          {
            type: 'text',
            text: `Scanned Files (${results.length} results):\n\n${JSON.stringify(results, null, 2)}`,
          },
        ],
      };
    }

    throw new Error(`Unknown database tool: ${name}`);
  } catch (error) {
    if (error.code === 'SQLITE_CANTOPEN') {
      throw new Error(`Database not found at: ${DB_PATH}. Make sure the database exists.`);
    }
    throw error;
  } finally {
    if (db) {
      db.close();
    }
  }
}

import { readdir, stat, readFile } from 'fs/promises';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PROJECT_ROOT = process.env.ALUMMAH_PROJECT_ROOT ||
  path.resolve(__dirname, '..', '..', '..');

export function getProjectTools() {
  return [
    {
      name: 'get_project_stats',
      description: 'Get comprehensive project statistics and metrics',
      inputSchema: {
        type: 'object',
        properties: {
          detailed: {
            type: 'boolean',
            description: 'Include detailed breakdown by directory',
            default: false,
          },
        },
      },
    },
    {
      name: 'search_code',
      description: 'Search for code patterns in the project',
      inputSchema: {
        type: 'object',
        properties: {
          pattern: {
            type: 'string',
            description: 'Search pattern (text or regex)',
          },
          fileType: {
            type: 'string',
            description: 'File extension to search (e.g., ".cs", ".json")',
            default: '.cs',
          },
          caseSensitive: {
            type: 'boolean',
            description: 'Case-sensitive search',
            default: false,
          },
          maxResults: {
            type: 'number',
            description: 'Maximum number of results',
            default: 20,
          },
        },
        required: ['pattern'],
      },
    },
    {
      name: 'get_project_structure',
      description: 'Get the project directory structure',
      inputSchema: {
        type: 'object',
        properties: {
          maxDepth: {
            type: 'number',
            description: 'Maximum depth to traverse',
            default: 3,
          },
          excludePatterns: {
            type: 'array',
            items: { type: 'string' },
            description: 'Patterns to exclude (e.g., "bin", "obj")',
            default: ['bin', 'obj', 'node_modules', '.git'],
          },
        },
      },
    },
  ];
}

async function countFiles(dir, extensions = [], excludeDirs = ['bin', 'obj', 'node_modules', '.git', 'TestResults']) {
  let count = 0;
  let totalSize = 0;
  const filesByExt = {};

  async function scan(currentDir, depth = 0) {
    if (depth > 10) return; // Safety limit

    try {
      const entries = await readdir(currentDir, { withFileTypes: true });

      for (const entry of entries) {
        if (excludeDirs.includes(entry.name)) continue;

        const fullPath = path.join(currentDir, entry.name);

        try {
          if (entry.isDirectory()) {
            await scan(fullPath, depth + 1);
          } else if (entry.isFile()) {
            const ext = path.extname(entry.name);
            if (extensions.length === 0 || extensions.includes(ext)) {
              count++;
              const stats = await stat(fullPath);
              totalSize += stats.size;
              filesByExt[ext] = (filesByExt[ext] || 0) + 1;
            }
          }
        } catch (err) {
          // Skip files we can't access
          continue;
        }
      }
    } catch (err) {
      // Skip directories we can't read
      return;
    }
  }

  await scan(dir);
  return { count, totalSize, filesByExt };
}

async function searchInFiles(dir, pattern, fileType, caseSensitive, maxResults, excludeDirs = ['bin', 'obj', 'node_modules', '.git']) {
  const results = [];
  const regex = new RegExp(pattern, caseSensitive ? 'g' : 'gi');

  async function scan(currentDir, depth = 0) {
    if (depth > 10 || results.length >= maxResults) return;

    try {
      const entries = await readdir(currentDir, { withFileTypes: true });

      for (const entry of entries) {
        if (results.length >= maxResults) break;
        if (excludeDirs.includes(entry.name)) continue;

        const fullPath = path.join(currentDir, entry.name);

        try {
          if (entry.isDirectory()) {
            await scan(fullPath, depth + 1);
          } else if (entry.isFile() && entry.name.endsWith(fileType)) {
            const content = await readFile(fullPath, 'utf-8');
            const lines = content.split('\n');

            lines.forEach((line, lineNum) => {
              if (results.length >= maxResults) return;

              if (regex.test(line)) {
                results.push({
                  file: path.relative(PROJECT_ROOT, fullPath),
                  line: lineNum + 1,
                  content: line.trim(),
                });
              }
            });
          }
        } catch (err) {
          // Skip files we can't read
          continue;
        }
      }
    } catch (err) {
      return;
    }
  }

  await scan(dir);
  return results;
}

async function getDirectoryTree(dir, maxDepth, excludePatterns, currentDepth = 0) {
  if (currentDepth >= maxDepth) return null;

  const tree = {
    name: path.basename(dir),
    type: 'directory',
    children: [],
  };

  try {
    const entries = await readdir(dir, { withFileTypes: true });

    for (const entry of entries) {
      if (excludePatterns.some(pattern => entry.name.includes(pattern))) continue;

      const fullPath = path.join(dir, entry.name);

      if (entry.isDirectory()) {
        const subtree = await getDirectoryTree(fullPath, maxDepth, excludePatterns, currentDepth + 1);
        if (subtree) {
          tree.children.push(subtree);
        }
      } else {
        tree.children.push({
          name: entry.name,
          type: 'file',
        });
      }
    }
  } catch (err) {
    // Skip directories we can't read
  }

  return tree;
}

function formatTree(tree, prefix = '', isLast = true) {
  if (!tree) return '';

  let result = prefix + (isLast ? '└── ' : '├── ') + tree.name + '\n';

  if (tree.children) {
    tree.children.forEach((child, index) => {
      const isLastChild = index === tree.children.length - 1;
      const newPrefix = prefix + (isLast ? '    ' : '│   ');
      result += formatTree(child, newPrefix, isLastChild);
    });
  }

  return result;
}

export async function handleProjectTool(name, args) {
  if (name === 'get_project_stats') {
    const csFiles = await countFiles(PROJECT_ROOT, ['.cs']);
    const testFiles = await countFiles(path.join(PROJECT_ROOT, 'AlUmmahNow.Tests'), ['.cs']);
    const allFiles = await countFiles(PROJECT_ROOT);

    const stats = {
      'Project Root': PROJECT_ROOT,
      'Total Files': allFiles.count,
      'Total Size': `${(allFiles.totalSize / 1024 / 1024).toFixed(2)} MB`,
      'C# Files': csFiles.count,
      'C# Size': `${(csFiles.totalSize / 1024 / 1024).toFixed(2)} MB`,
      'Test Files': testFiles.count,
      'Test Size': `${(testFiles.totalSize / 1024 / 1024).toFixed(2)} MB`,
      'Files by Extension': allFiles.filesByExt,
    };

    if (args.detailed) {
      // Add per-directory breakdown
      const projects = [
        'AlUmmahNow.Data',
        'AlUmmahNow.DataImporter',
        'AlUmmahNow.FileScanService',
        'AlUmmahNow.VisualImporter',
        'AlUmmahNow.Tests',
      ];

      stats['Breakdown by Project'] = {};
      for (const proj of projects) {
        const projPath = path.join(PROJECT_ROOT, proj);
        try {
          const projStats = await countFiles(projPath, ['.cs']);
          stats['Breakdown by Project'][proj] = {
            files: projStats.count,
            size: `${(projStats.totalSize / 1024 / 1024).toFixed(2)} MB`,
          };
        } catch (err) {
          // Skip if directory doesn't exist
        }
      }
    }

    return {
      content: [
        {
          type: 'text',
          text: `Project Statistics:\n\n${JSON.stringify(stats, null, 2)}`,
        },
      ],
    };
  }

  if (name === 'search_code') {
    const results = await searchInFiles(
      PROJECT_ROOT,
      args.pattern,
      args.fileType || '.cs',
      args.caseSensitive || false,
      args.maxResults || 20
    );

    let resultText = `Code Search Results for "${args.pattern}"\n`;
    resultText += `Found ${results.length} matches${results.length === args.maxResults ? ' (limited)' : ''}:\n\n`;

    results.forEach((result, index) => {
      resultText += `${index + 1}. ${result.file}:${result.line}\n`;
      resultText += `   ${result.content}\n\n`;
    });

    return {
      content: [
        {
          type: 'text',
          text: resultText,
        },
      ],
    };
  }

  if (name === 'get_project_structure') {
    const tree = await getDirectoryTree(
      PROJECT_ROOT,
      args.maxDepth || 3,
      args.excludePatterns || ['bin', 'obj', 'node_modules', '.git']
    );

    const formattedTree = formatTree(tree);

    return {
      content: [
        {
          type: 'text',
          text: `Project Structure:\n\n${formattedTree}`,
        },
      ],
    };
  }

  throw new Error(`Unknown project tool: ${name}`);
}

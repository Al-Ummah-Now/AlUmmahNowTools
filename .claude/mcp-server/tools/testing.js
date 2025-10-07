import { exec } from 'child_process';
import { promisify } from 'util';
import path from 'path';
import { fileURLToPath } from 'url';

const execAsync = promisify(exec);
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PROJECT_ROOT = process.env.ALUMMAH_PROJECT_ROOT ||
  path.resolve(__dirname, '..', '..', '..');

export function getTestTools() {
  return [
    {
      name: 'run_tests',
      description: 'Run .NET tests with optional filters and coverage',
      inputSchema: {
        type: 'object',
        properties: {
          filter: {
            type: 'string',
            description: 'Test filter pattern (e.g., "SignalR", "FileScan", "MetadataExtractor")',
          },
          coverage: {
            type: 'boolean',
            description: 'Collect code coverage data',
            default: false,
          },
          verbosity: {
            type: 'string',
            enum: ['quiet', 'minimal', 'normal', 'detailed'],
            description: 'Test output verbosity',
            default: 'normal',
          },
        },
      },
    },
    {
      name: 'run_build',
      description: 'Build the .NET solution or specific project',
      inputSchema: {
        type: 'object',
        properties: {
          project: {
            type: 'string',
            description: 'Specific project to build (optional, builds all if not specified)',
          },
          configuration: {
            type: 'string',
            enum: ['Debug', 'Release'],
            default: 'Debug',
          },
        },
      },
    },
    {
      name: 'test_coverage_report',
      description: 'Generate test coverage report',
      inputSchema: {
        type: 'object',
        properties: {
          format: {
            type: 'string',
            enum: ['html', 'text', 'json'],
            description: 'Output format for coverage report',
            default: 'text',
          },
        },
      },
    },
  ];
}

export async function handleTestTool(name, args) {
  if (name === 'run_tests') {
    let command = 'dotnet test AlUmmahNow.Tests/AlUmmahNow.Tests.csproj';

    if (args.filter) {
      command += ` --filter "FullyQualifiedName~${args.filter}"`;
    }

    if (args.coverage) {
      command += ' --collect:"XPlat Code Coverage" --results-directory:"./TestResults"';
    }

    command += ` --verbosity ${args.verbosity || 'normal'}`;

    try {
      const { stdout, stderr } = await execAsync(command, {
        cwd: PROJECT_ROOT,
        maxBuffer: 1024 * 1024 * 10, // 10MB buffer
        timeout: 120000, // 2 minute timeout
      });

      // Parse test results
      const passedMatch = stdout.match(/Passed:\s+(\d+)/);
      const failedMatch = stdout.match(/Failed:\s+(\d+)/);
      const totalMatch = stdout.match(/Total:\s+(\d+)/);

      const summary = {
        passed: passedMatch ? parseInt(passedMatch[1]) : 0,
        failed: failedMatch ? parseInt(failedMatch[1]) : 0,
        total: totalMatch ? parseInt(totalMatch[1]) : 0,
      };

      let resultText = `Test Execution ${summary.failed === 0 ? 'PASSED ✓' : 'FAILED ✗'}\n\n`;
      resultText += `Summary:\n`;
      resultText += `  Total:  ${summary.total}\n`;
      resultText += `  Passed: ${summary.passed}\n`;
      resultText += `  Failed: ${summary.failed}\n\n`;
      resultText += `Full Output:\n${stdout}`;

      if (stderr) {
        resultText += `\n\nWarnings/Errors:\n${stderr}`;
      }

      return {
        content: [
          {
            type: 'text',
            text: resultText,
          },
        ],
      };
    } catch (error) {
      return {
        content: [
          {
            type: 'text',
            text: `Test execution failed:\n\nStdout:\n${error.stdout}\n\nStderr:\n${error.stderr}\n\nError: ${error.message}`,
          },
        ],
        isError: true,
      };
    }
  }

  if (name === 'run_build') {
    let command = 'dotnet build';

    if (args.project) {
      command += ` ${args.project}`;
    }

    command += ` --configuration ${args.configuration || 'Debug'}`;

    try {
      const { stdout, stderr } = await execAsync(command, {
        cwd: PROJECT_ROOT,
        maxBuffer: 1024 * 1024 * 10,
        timeout: 300000, // 5 minute timeout
      });

      const buildSucceeded = stdout.includes('Build succeeded');
      const warningCount = (stdout.match(/warning/gi) || []).length;
      const errorCount = (stdout.match(/error/gi) || []).length;

      let resultText = `Build ${buildSucceeded ? 'SUCCEEDED ✓' : 'FAILED ✗'}\n\n`;
      resultText += `Warnings: ${warningCount}\n`;
      resultText += `Errors: ${errorCount}\n\n`;
      resultText += `Output:\n${stdout}`;

      if (stderr) {
        resultText += `\n\nStderr:\n${stderr}`;
      }

      return {
        content: [
          {
            type: 'text',
            text: resultText,
          },
        ],
      };
    } catch (error) {
      return {
        content: [
          {
            type: 'text',
            text: `Build failed:\n\n${error.stdout}\n\n${error.stderr}`,
          },
        ],
        isError: true,
      };
    }
  }

  if (name === 'test_coverage_report') {
    // First run tests with coverage
    const testCmd = 'dotnet test --collect:"XPlat Code Coverage" --results-directory:"./TestResults"';

    try {
      await execAsync(testCmd, {
        cwd: PROJECT_ROOT,
        maxBuffer: 1024 * 1024 * 10,
      });

      // Find the latest coverage file
      const findCmd = process.platform === 'win32'
        ? 'dir /s /b TestResults\\*\\coverage.cobertura.xml'
        : 'find TestResults -name "coverage.cobertura.xml"';

      const { stdout: findOutput } = await execAsync(findCmd, { cwd: PROJECT_ROOT });
      const coverageFile = findOutput.trim().split('\n')[0];

      if (args.format === 'html') {
        // Generate HTML report
        const reportCmd = `reportgenerator -reports:"${coverageFile}" -targetdir:"TestResults/CoverageReport" -reporttypes:Html`;
        await execAsync(reportCmd, { cwd: PROJECT_ROOT });

        return {
          content: [
            {
              type: 'text',
              text: `Coverage report generated!\n\nHTML Report: ${path.join(PROJECT_ROOT, 'TestResults', 'CoverageReport', 'index.html')}\n\nOpen in browser to view detailed coverage.`,
            },
          ],
        };
      }

      // For text/json, parse the XML file
      const { readFile } = await import('fs/promises');
      const xml = await readFile(coverageFile, 'utf-8');

      // Extract coverage percentage from XML
      const lineRateMatch = xml.match(/line-rate="([\d.]+)"/);
      const branchRateMatch = xml.match(/branch-rate="([\d.]+)"/);

      const lineCoverage = lineRateMatch ? (parseFloat(lineRateMatch[1]) * 100).toFixed(2) : 'N/A';
      const branchCoverage = branchRateMatch ? (parseFloat(branchRateMatch[1]) * 100).toFixed(2) : 'N/A';

      return {
        content: [
          {
            type: 'text',
            text: `Coverage Summary:\n\nLine Coverage: ${lineCoverage}%\nBranch Coverage: ${branchCoverage}%\n\nCoverage file: ${coverageFile}`,
          },
        ],
      };
    } catch (error) {
      return {
        content: [
          {
            type: 'text',
            text: `Coverage report failed:\n${error.message}`,
          },
        ],
        isError: true,
      };
    }
  }

  throw new Error(`Unknown test tool: ${name}`);
}

import express from 'express';
import path from 'path';
import { fileURLToPath } from 'url';
import fsSync from 'fs';
import { randomUUID } from 'crypto';

// Import Assembler modules - conditional path based on environment
let EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess;
let ApiResponse, TemplateData, PreProcessTemplateMetadata;
let Logger, LogRotation, ConfigUtil;

const assemblerBasePath = fsSync.existsSync('/app/wwwroot') ? './Assembler/src' : '../Assembler/src';

const assemblerModule = await import(`${assemblerBasePath}/index.js`);
({ EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess } = assemblerModule);

const apiResponseModule = await import(`${assemblerBasePath}/api/index.js`);
({ ApiResponse, TemplateData, PreProcessTemplateMetadata } = apiResponseModule);

const loggerModule = await import(`${assemblerBasePath}/common/logger.js`);
({ Logger, LogRotation } = loggerModule);

const configModule = await import(`${assemblerBasePath}/config/index.js`);
({ ConfigUtil } = configModule);

// Import endpoint handlers

import { indexEndpoint, mergeEndpoint, getTemplatesEndpoint, scenariosEndpoint } from './assemblerEndpoint.js';
import { saveTestResultsEndpoint, savePerformanceResultsEndpoint, saveLogEndpoint, saveOutputEndpoint, testStandardEndpoint, testAdvancedEndpoint, testPerformanceEndpoint, testConsolidatePerformanceEndpoint, getReportEndpoint } from './assemblerTestEndpoint.js';

// Configure logger with context-specific log files
const logRotation = LogRotation.NONE;

// Determine paths based on environment
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const projectDirectory = __dirname;
const assemblerWebDirPath = path.join(__dirname, 'wwwroot');

// Load ConfigUtil with wwwroot path
await ConfigUtil.load(assemblerWebDirPath);
const templateAnalysisDir = path.join(projectDirectory, 'template_analysis');
const logsDir = path.join(templateAnalysisDir, 'logs');
if (!fsSync.existsSync(logsDir)) {
  fsSync.mkdirSync(logsDir, { recursive: true });
}

// Configure separate log files for each context
const contextLogFiles = {
  'LoaderNormal': path.join(logsDir, 'nodejs_loadernormal.log'),
  'LoaderPreProcess': path.join(logsDir, 'nodejs_loaderpreprocess.log'),
  'EngineNormal': path.join(logsDir, 'nodejs_enginenormal.log'),
  'EnginePreProcess': path.join(logsDir, 'nodejs_enginepreprocess.log'),
  'Index': path.join(logsDir, 'nodejs_index.log'),
  'MergeEndpoint': path.join(logsDir, 'nodejs_mergeendpoint.log'),
};

Logger.configure(0, null, false, logRotation); // 0 = DEBUG
Logger.configureContextLogFiles(contextLogFiles);
Logger.info('AssemblerWeb starting up', 'Index');

// Global constant to select template engine
const USE_PREPROCESS_ENGINE = true;

// Parse command line args
const skipIdleTracking = process.argv.includes('--skipIdleTracking');

// Parse port from --port argument
let port = process.env.PORT || 8095;
const portIndex = process.argv.indexOf('--port');
if (portIndex !== -1 && portIndex + 1 < process.argv.length) {
  port = parseInt(process.argv[portIndex + 1], 10) || port;
}

const app = express();

// Middleware
app.use(express.json());
app.use(express.static(path.join(__dirname, 'wwwroot')));

// Idle Tracking Middleware
function idleTrackingMiddleware(idleSeconds = 10) {
  let lastRequest = Date.now();
  let shutdownInitiated = false;
  const activeHolds = new Map();
  const holdTimeoutSeconds = 300; // Safety timeout for stuck holds

  console.log(`[STARTUP] Configured idleSeconds = ${idleSeconds}`);
  console.log('[STARTUP] Starting idle monitor with 10-second check interval');

  // Start idle checker interval
  setInterval(() => {
    if (shutdownInitiated) return;

    const idle = (Date.now() - lastRequest) / 1000;
    const now = Date.now();

    // Clean up expired holds and count active holds
    const expiredHolds = [];
    for (const [holdId, holdTime] of activeHolds.entries()) {
      const holdAge = (now - holdTime) / 1000;
      if (holdAge >= holdTimeoutSeconds) {
        expiredHolds.push(holdId);
      }
    }

    for (const holdId of expiredHolds) {
      activeHolds.delete(holdId);
      console.log(`[MONITOR] Removed expired hold: ${holdId} (age: ${holdTimeoutSeconds}s)`);
    }

    const activeHoldsCount = activeHolds.size;
    console.log(`[MONITOR] IdleTime: ${idle.toFixed(1)}s, Threshold: ${idleSeconds}s, ActiveHolds: ${activeHoldsCount}`);

    // Only trigger shutdown if idle time exceeded AND no active holds
    if (idle > idleSeconds && activeHoldsCount === 0) {
      shutdownInitiated = true;
      console.log('[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown');
      Logger.info(`Idle timeout reached (${idleSeconds}s) with no active requests, shutting down server...`, 'IdleTracking');
      process.exit(0);
    }
  }, 10000);

  return (req, res, next) => {
    // Generate unique hold ID for this request
    const holdId = `hold_${randomUUID().replace(/-/g, '')}`;

    // Set hold before processing to prevent shutdown during long-running requests
    activeHolds.set(holdId, Date.now());
    console.log(`[REQUEST] Request started, hold set: ${holdId}`);
    lastRequest = Date.now();

    // Remove hold after response finishes (even if error occurs)
    res.on('finish', () => {
      activeHolds.delete(holdId);
      console.log(`[REQUEST] Request completed, hold removed: ${holdId}`);
      lastRequest = Date.now();
    });

    // Also handle aborted requests
    res.on('close', () => {
      if (activeHolds.has(holdId)) {
        activeHolds.delete(holdId);
        console.log(`[REQUEST] Request aborted, hold removed: ${holdId}`);
        lastRequest = Date.now();
      }
    });

    next();
  };
}

// Check if running in debug mode - disabled idle tracking for development
const isDebug = process.env.DEBUG === 'true' ||
                process.env.VSCODE_DEBUG === 'true' ||
                process.env.IDLE_TRACKER_DISABLED === 'true' ||
                process.env.APP_ENV === 'development' ||
                typeof v8debug !== 'undefined' ||
                process.execArgv.some(arg => arg.includes('--inspect')) ||
                skipIdleTracking;

if (isDebug) {
  console.log('[DEBUG] Running in development mode - idle tracking disabled');
} else {
  const idleSeconds = process.env.IDLE_SECONDS ? parseInt(process.env.IDLE_SECONDS) : 10;
  app.use(idleTrackingMiddleware(idleSeconds));
  console.log(`[PRODUCTION] Idle tracking enabled (${idleSeconds} seconds)`);
}

// OpenAPI specification
const openApiSpec = {
  openapi: '3.0.0',
  info: {
    title: 'Arshu Api',
    version: '1.0.0',
    description: 'Template assembler API for merging templates'
  },
  servers: [
    {
      url: `http://localhost:${port}`,
      description: 'Development server'
    }
  ],
  paths: {
    '/': {
      get: {
        tags: ['Root'],
        summary: 'Get Method to Test Merging',
        description: 'Get Method to Test Merging',
        responses: {
          '200': {
            description: 'HTML page with template testing interface',
            content: {
              'text/html': {
                schema: {
                  type: 'string'
                }
              }
            }
          }
        }
      }
    },
    '/merge': {
      post: {
        tags: ['Merge'],
        summary: 'Post Method to Merge Template for AppSite, AppFile, EngineType',
        description: 'Post Method to Merge Template for AppSite, AppFile, EngineType',
        requestBody: {
          required: true,
          content: {
            'application/json': {
              schema: {
                $ref: '#/components/schemas/MergeRequest'
              }
            }
          }
        },
        responses: {
          '200': {
            description: 'Successfully merged template',
            content: {
              'application/json': {
                schema: {
                  $ref: '#/components/schemas/MergeResponse'
                }
              }
            }
          },
          '400': {
            description: 'Bad request - missing required fields',
            content: {
              'application/json': {
                schema: {
                  type: 'object',
                  properties: {
                    error: {
                      type: 'string'
                    }
                  }
                }
              }
            }
          }
        }
      }
    }
  },
  components: {
    schemas: {
      MergeRequest: {
        type: 'object',
        required: ['appSite', 'engineType'],
        properties: {
          appSite: {
            type: 'string',
            description: 'The application site name'
          },
          appView: {
            type: 'string',
            description: 'The application view name (optional)'
          },
          engineType: {
            type: 'string',
            description: 'The engine type (Normal or PreProcess)',
            enum: ['Normal', 'PreProcess']
          }
        }
      },
      MergeResponse: {
        type: 'object',
        properties: {
          html: {
            type: 'string',
            description: 'The merged HTML content'
          },
          timing: {
            type: 'object',
            properties: {
              serverTimeMs: {
                type: 'number',
                description: 'Total server processing time in milliseconds'
              },
              engineTimeMs: {
                type: 'number',
                description: 'Engine processing time in milliseconds'
              }
            }
          }
        }
      }
    }
  }
};

// Serve OpenAPI spec
app.get('/openapi.json', (req, res) => {
  res.json(openApiSpec);
});

// Routes
app.get('/', (req, res) => indexEndpoint(req, res, EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess));

app.get('/api/scenarios', (req, res) => scenariosEndpoint(req, res, ConfigUtil));

app.post('/merge', (req, res) => mergeEndpoint(req, res, EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess, ApiResponse, TemplateData, PreProcessTemplateMetadata, ConfigUtil));

app.post('/api/templates', (req, res) => getTemplatesEndpoint(req, res, LoaderNormal, LoaderPreProcess, ApiResponse, TemplateData, PreProcessTemplateMetadata));

// API endpoints

app.post('/api/test-results', (req, res) => saveTestResultsEndpoint(req, res));
app.post('/api/performance-results', (req, res) => savePerformanceResultsEndpoint(req, res));
app.post('/api/save-log', (req, res) => saveLogEndpoint(req, res));
app.post('/api/save-output', (req, res) => saveOutputEndpoint(req, res));

// Test endpoints - pass projectDirectory to endpoints
app.post('/test/standard', (req, res) => testStandardEndpoint(req, res, projectDirectory, ConfigUtil));
app.post('/test/advanced', (req, res) => testAdvancedEndpoint(req, res, projectDirectory, ConfigUtil));
app.post('/test/performance', (req, res) => testPerformanceEndpoint(req, res, projectDirectory, ConfigUtil));
app.post('/test/consolidate-performance', (req, res) => testConsolidatePerformanceEndpoint(req, res, projectDirectory));

// Report endpoint
app.post('/api/report', (req, res) => getReportEndpoint(req, res, projectDirectory));

app.listen(port, () => {
  // OS environment detection
  import('fs').then(fs => {
    fs.promises.readFile('/proc/sys/kernel/osrelease', 'utf8').then(osRelease => {
      if (osRelease.includes('microsoft')) {
        console.log('[WSL] Running in WSL environment');
      } else {
        fs.promises.readFile('/etc/os-release', 'utf8').then(osInfo => {
          const distro = (osInfo.match(/^ID=(.*)$/m) || [null, 'Unknown Linux'])[1].replace(/"/g, '');
          console.log(`[Linux] Running in ${distro} environment`);
        }).catch(() => {
          console.log('[Linux] Running in Linux environment');
        });
      }
    }).catch(() => {
      console.log('[Windows] Running in Windows environment');
    });
  });
  console.log(`AssemblerWeb Node.js server running at http://localhost:${port}`);
  console.log(`Scalar API documentation available at http://localhost:${port}/scalar`);
  console.log(`OpenAPI spec available at http://localhost:${port}/openapi.json`);
});
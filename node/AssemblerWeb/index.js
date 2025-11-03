import express from 'express';
import path from 'path';
import { fileURLToPath } from 'url';
import fsSync from 'fs';
import { randomUUID } from 'crypto';

// Add global error handlers
process.on('uncaughtException', (error) => {
  console.error('[ERROR] Uncaught Exception:', error);
  process.exit(1);
});

process.on('unhandledRejection', (reason, promise) => {
  console.error('[ERROR] Unhandled Rejection at:', promise, 'reason:', reason);
  process.exit(1);
});

// Import Assembler modules - conditional path based on environment
let EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess;
let ApiResponse, TemplateData, PreProcessTemplateMetadata;
let Logger, LogRotation, ConfigUtil;

console.log('[DEBUG] Starting imports...');
const assemblerBasePath = fsSync.existsSync('/app/wwwroot') ? './Assembler/src' : '../Assembler/src';
console.log('[DEBUG] Assembler base path:', assemblerBasePath);
console.log('[DEBUG] Assembler base path:', assemblerBasePath);

const assemblerModule = await import(`${assemblerBasePath}/index.js`);
console.log('[DEBUG] Assembler module loaded');
({ EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess } = assemblerModule);

const apiResponseModule = await import(`${assemblerBasePath}/api/index.js`);
console.log('[DEBUG] API Response module loaded');
({ ApiResponse, TemplateData, PreProcessTemplateMetadata } = apiResponseModule);

const loggerModule = await import(`${assemblerBasePath}/common/logger.js`);
console.log('[DEBUG] Logger module loaded');
({ Logger, LogRotation } = loggerModule);

const configModule = await import(`${assemblerBasePath}/config/index.js`);
console.log('[DEBUG] Config module loaded');
({ ConfigUtil } = configModule);

// Import endpoint handlers
console.log('[DEBUG] Importing endpoint handlers...');
import { mapAssemblerEndpoints } from './src/endpoint/assemblerEndpoint.js';
import { mapAssemblerTestEndpoints } from './src/endpoint/assemblerTestEndpoint.js';
console.log('[DEBUG] Endpoint handlers loaded');

// Note: Assembler dependencies are now imported inside assemblerEndpoint.js

// Import services
console.log('[DEBUG] Importing services...');
import { idleTrackingMiddleware } from './src/services/idleTrackingMiddleware.js';
console.log('[DEBUG] Services loaded');

// Configure logger with context-specific log files
const logRotation = LogRotation.HOURLY;

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
  'IdleTracking': path.join(logsDir, 'nodejs_idletracking.log'),
};

// Configure logger (no main log file - only context files)
Logger.configure(0, false, logRotation); // 0 = DEBUG

// Set logs directory for clearing
Logger.setLogsDirectory(logsDir);

// Clear logs based on debug mode
if (process.env.DEBUG || process.env.VSCODE_INSPECTOR_OPTIONS) {
  Logger.clearLogs();
} else {
  Logger.clearOldLogs(7);
}

// Configure context log files AFTER clearing (which would delete them)
Logger.configureContextLogFiles(contextLogFiles);

Logger.info('AssemblerWeb starting up', 'Index');
console.log('[DEBUG] Logger configured');

// Global constant to select template engine
const USE_PREPROCESS_ENGINE = true;

// Parse command line args
const skipIdleTracking = process.argv.includes('--skipIdleTracking');

// Parse port from --port argument
let port = process.env.PORT || 8050;
const portIndex = process.argv.indexOf('--port');
if (portIndex !== -1 && portIndex + 1 < process.argv.length) {
  port = parseInt(process.argv[portIndex + 1], 10) || port;
}

const app = express();

// Middleware
app.use(express.json());
app.use(express.static(path.join(__dirname, 'wwwroot')));

// Check if running in debug mode
console.log('[DEBUG] process.env.DEBUG:', process.env.DEBUG);
console.log('[DEBUG] process.env.VSCODE_DEBUG:', process.env.VSCODE_DEBUG);
console.log('[DEBUG] process.env.APP_ENV:', process.env.APP_ENV);
console.log('[DEBUG] process.env.VSCODE_INSPECTOR_OPTIONS:', process.env.VSCODE_INSPECTOR_OPTIONS);
console.log('[DEBUG] VSCODE_INSPECTOR_OPTIONS !== undefined:', process.env.VSCODE_INSPECTOR_OPTIONS !== undefined);
console.log('[DEBUG] process.execArgv:', process.execArgv);
console.log('[DEBUG] typeof v8debug:', typeof v8debug);

const isDebug = process.env.DEBUG === 'true' ||
                process.env.VSCODE_DEBUG === 'true' ||
                process.env.APP_ENV === 'development' ||
                process.env.VSCODE_INSPECTOR_OPTIONS !== undefined ||  // VS Code debugger attached
                typeof v8debug !== 'undefined' ||
                process.execArgv.some(arg => arg.includes('--inspect'));

console.log('[DEBUG] isDebug = ' + isDebug + ', will ' + (isDebug ? '' : 'NOT ') + 'launch browser');

// Determine if idle tracking should be enabled
// Command line args and explicit env vars take precedence
let idleTrackingEnabled;
if (skipIdleTracking) {
  idleTrackingEnabled = false; // --skipIdleTracking flag explicitly disables
} else {
  const idleTrackerDisabledEnv = process.env.IDLE_TRACKER_DISABLED;
  if (idleTrackerDisabledEnv === 'false') {
    idleTrackingEnabled = true; // Explicitly enable idle tracking
  } else if (idleTrackerDisabledEnv === 'true') {
    idleTrackingEnabled = false; // Explicitly disable idle tracking
  } else {
    idleTrackingEnabled = !isDebug; // Default: disable in debug mode
  }
}

if (idleTrackingEnabled) {
  try {
    const idleSeconds = process.env.IDLE_SECONDS ? parseInt(process.env.IDLE_SECONDS) : 10;
    console.log(`[DEBUG] Initializing idle tracking middleware with ${idleSeconds}s timeout`);
    app.use(idleTrackingMiddleware(idleSeconds));
    console.log('[IdleTracking] Idle tracking ENABLED');
  } catch (error) {
    console.error('[ERROR] Failed to initialize idle tracking:', error);
    throw error;
  }
} else {
  console.log('[IdleTracking] Idle tracking DISABLED');
}

// Setup graceful shutdown handlers
process.on('SIGINT', async () => {
  console.log('\n[SHUTDOWN] Received SIGINT');
  if (idleTrackingEnabled) {
    const { shutdown } = await import('./services/idleTrackingMiddleware.js');
    shutdown();
  }
  Logger.info('AssemblerWeb shutting down...', 'Index');
  Logger.flush();
  Logger.info('AssemblerWeb stopped', 'Index');
  Logger.flush();
  process.exit(0);
});

process.on('SIGTERM', async () => {
  console.log('\n[SHUTDOWN] Received SIGTERM');
  if (idleTrackingEnabled) {
    const { shutdown } = await import('./services/idleTrackingMiddleware.js');
    shutdown();
  }
  Logger.info('AssemblerWeb shutting down...', 'Index');
  Logger.flush();
  Logger.info('AssemblerWeb stopped', 'Index');
  Logger.flush();
  process.exit(0);
});


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

// Map assembler endpoints using the centralized functions
mapAssemblerEndpoints(app, projectDirectory);
mapAssemblerTestEndpoints(app);

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

  // Launch browser after a short delay (only in development mode)
  console.log(`[DEBUG] isDebug = ${isDebug}, will ${isDebug ? '' : 'NOT '}launch browser`);
  if (isDebug) {
    setTimeout(async () => {
      try {
        console.log('[DEBUG] Attempting to open browser...');
        const { default: open } = await import('open');
        await open(`http://localhost:${port}/`);
        console.log('[DEBUG] Browser opened successfully');
      } catch (e) {
        console.log(`Failed to open browser: ${e.message}`);
      }
    }, 500);
  }
});
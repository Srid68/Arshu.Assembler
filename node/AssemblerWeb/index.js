import express from 'express';
import path from 'path';
import { fileURLToPath } from 'url';
import fsSync from 'fs';

// Import Assembler modules - conditional path based on environment
let EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess;
let ApiResponse, TemplateData, PreProcessTemplateMetadata;
let Logger, LogRotation, CommonUtil, ConfigUtil;

const assemblerBasePath = fsSync.existsSync('/app/wwwroot') ? './Assembler/src' : '../Assembler/src';

const assemblerModule = await import(`${assemblerBasePath}/index.js`);
({ EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess } = assemblerModule);

const apiResponseModule = await import(`${assemblerBasePath}/api/index.js`);
({ ApiResponse, TemplateData, PreProcessTemplateMetadata } = apiResponseModule);

const loggerModule = await import(`${assemblerBasePath}/common/logger.js`);
({ Logger, LogRotation } = loggerModule);

const commonUtilModule = await import(`${assemblerBasePath}/common/commonUtil.js`);
({ CommonUtil } = commonUtilModule);

const configModule = await import(`${assemblerBasePath}/config/index.js`);
({ ConfigUtil } = configModule);

// Import endpoint handlers
import { indexEndpoint, mergeEndpoint, testStandardEndpoint, testAdvancedEndpoint, testPerformanceEndpoint, testConsolidatePerformanceEndpoint, scenariosEndpoint, getReportEndpoint } from './assemblerEndpoint.js';

// Configure logger with context-specific log files
const logRotation = LogRotation.NONE;
const { assemblerWebDirPath, projectDirectory } = CommonUtil.getAssemblerWebDirPath();

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

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

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

  // Start idle checker interval
  setInterval(() => {
    if (shutdownInitiated) return;
    const idle = (Date.now() - lastRequest) / 1000;
    if (idle > idleSeconds) {
      shutdownInitiated = true;
      console.log(`Idle timeout reached (${idleSeconds}s), shutting down server...`);
      process.exit(0);
    }
  }, 10000);

  return (req, res, next) => {
    lastRequest = Date.now();
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
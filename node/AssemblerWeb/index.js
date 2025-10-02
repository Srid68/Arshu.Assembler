import express from 'express';
import path from 'path';
import fs from 'fs/promises';
import { fileURLToPath } from 'url';
let EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess;
import fsSync from 'fs';
let assemblerModule;
if (fsSync.existsSync('/app/wwwroot')) {
  assemblerModule = await import('./Assembler/src/index.js');
} else {
  assemblerModule = await import('../Assembler/src/index.js');
}
({ EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess } = assemblerModule);

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Global constant to select template engine
const USE_PREPROCESS_ENGINE = true;

const app = express();
const port = process.env.PORT || 3001;

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

const idleSeconds = process.env.IDLE_SECONDS ? parseInt(process.env.IDLE_SECONDS) : 10;
const isDebug = process.env.NODE_ENV === 'development' || typeof v8debug !== 'undefined' || process.execArgv.some(arg => arg.includes('--inspect'));
if (!isDebug) {
  app.use(idleTrackingMiddleware(idleSeconds)); // Only enable in deployment
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
        required: ['appSite', 'appFile', 'engineType'],
        properties: {
          appSite: {
            type: 'string',
            description: 'The application site name'
          },
          appView: {
            type: 'string',
            description: 'The application view name (optional)'
          },
          appViewPrefix: {
            type: 'string',
            description: 'The application view prefix (optional)'
          },
          appFile: {
            type: 'string',
            description: 'The application file name'
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

// Serve Scalar API documentation

app.get('/', async (req, res) => {
  try {
    // Get all test folders in wwwroot/AppSites
    const rootDirPath = path.join(__dirname, 'wwwroot');
    const appSitesPath = path.join(rootDirPath, 'AppSites');
    
    let testDirs;
    try {
      const dirs = await fs.readdir(appSitesPath, { withFileTypes: true });
      testDirs = dirs
        .filter(dir => dir.isDirectory() && !dir.name.toLowerCase().includes('roottemplate'))
        .map(dir => dir.name)
        .sort();
    } catch (error) {
      return res.status(500).send('Error reading AppSites directory');
    }

    // Build options for select tag with uniform 4-value format
    const optionsList = [];
    
    for (const testDir of testDirs) {
      const testDirPath = path.join(appSitesPath, testDir);
      
      let htmlFiles;
      try {
        const files = await fs.readdir(testDirPath);
        htmlFiles = files
          .filter(file => file.endsWith('.html'))
          .map(file => path.parse(file).name)
          .sort();
      } catch (error) {
        continue;
      }

      // Check for Views subdirectory
      const viewsPath = path.join(testDirPath, 'Views');
      let hasViews = false;
      try {
        await fs.access(viewsPath);
        hasViews = true;
      } catch (error) {
        hasViews = false;
      }

      for (const htmlFile of htmlFiles) {
        // Dynamically set AppViewPrefix from root file name (htmlFile)
        let appViewPrefix = htmlFile;
        if (appViewPrefix && appViewPrefix.length > 6) {
          appViewPrefix = appViewPrefix.substring(0, 6);
        }
        
        // Always add the default option first (no AppView)
        const optionValue = `${testDir},${htmlFile},,${appViewPrefix}`;
        const optionText = `${testDir} - ${htmlFile}`;
        optionsList.push(`<option value="${optionValue}">${optionText}</option>`);
      }

      if (hasViews) {
        let viewFiles;
        try {
          const files = await fs.readdir(viewsPath);
          viewFiles = files
            .filter(file => file.endsWith('.html'))
            .map(file => path.parse(file).name)
            .sort();
        } catch (error) {
          continue;
        }

        // Collect all possible AppView values from viewFiles
        const appViewValues = viewFiles
          .map(vf => {
            const idx = vf.toLowerCase().indexOf('content');
            if (idx > 0) {
              const viewPart = vf.substring(0, idx);
              if (viewPart.length > 0) {
                return viewPart.charAt(0).toUpperCase() + viewPart.slice(1);
              }
            }
            return null;
          })
          .filter(av => av && av.length > 0)
          .filter((value, index, self) => self.indexOf(value) === index); // distinct

        // For each root HTML file, generate AppView test scenarios dynamically
        for (const rootFile of htmlFiles) {
          // Dynamically set AppViewPrefix from root file name (rootFile)
          let rootAppViewPrefix = rootFile;
          if (rootAppViewPrefix && rootAppViewPrefix.length > 6) {
            rootAppViewPrefix = rootAppViewPrefix.substring(0, 6);
          }
          
          // Check if this root file has corresponding view files
          const matchingViewPrefix = viewFiles
            .map(vf => {
              const idx = vf.toLowerCase().indexOf('content');
              return idx > 0 ? vf.substring(0, idx) : '';
            })
            .find(prefix => prefix && rootFile.toLowerCase().startsWith(prefix.toLowerCase()));

          if (matchingViewPrefix) {
            // Generate AppView scenarios for ALL available AppViews dynamically
            for (const appView of appViewValues) {
              if (appView) {
                const appViewPrefix = rootAppViewPrefix;
                const optionValueAppView = `${testDir},${rootFile},${appView},${appViewPrefix}`;
                const optionTextAppView = `${testDir} - ${rootFile} (AppView: ${appView})`;
                optionsList.push(`<option value="${optionValueAppView}">${optionTextAppView}</option>`);
              }
            }
          }
        }
      }
    }

    const options = optionsList.join('\n        ');

    // Read roottemplate.html and replace the options marker
    const templatePath = path.join(appSitesPath, 'roottemplate.html');
    let html = await fs.readFile(templatePath, 'utf-8');
    html = html.replace('<!--OPTIONS-->', options);
    
    res.setHeader('Content-Type', 'text/html');
    res.send(html);
  } catch (error) {
    console.error('Error in root endpoint:', error);
    res.status(500).send('Internal server error');
  }
});

app.post('/merge', async (req, res) => {
  const serverStart = Date.now();
  
  try {
    const { appSite, appView, appViewPrefix, appFile, engineType } = req.body;

    // Validate required fields
    if (!appSite || !appFile || !engineType) {
      return res.status(400).json({ 
        error: 'Missing required fields: appSite, appFile, engineType' 
      });
    }

    const rootDirPath = path.join(__dirname, 'wwwroot');
    let mergedHtml = '';
    const engineStart = Date.now();

    if (engineType.toLowerCase() === 'preprocess') {
      const templates = LoaderPreProcess.loadProcessGetTemplateFiles(rootDirPath, appSite);
      const engine = new EnginePreProcess();
      if (appViewPrefix) {
        engine.appViewPrefix = appViewPrefix;
      }
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, templates.templates);
    } else {
      const templates = LoaderNormal.loadGetTemplateFiles(rootDirPath, appSite);
      const engine = new EngineNormal();
      if (appViewPrefix) {
        engine.appViewPrefix = appViewPrefix;
      }
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, templates);
    }

    const engineTimeMs = Date.now() - engineStart;
    const serverTimeMs = Date.now() - serverStart;

    const responseObj = {
      html: mergedHtml,
      timing: {
        serverTimeMs,
        engineTimeMs
      }
    };

    res.json(responseObj);
  } catch (error) {
    console.error('Error in merge endpoint:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

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
import path from 'path';
import { fileURLToPath } from 'url';
import fsSync from 'fs';
import { getValidAppSites, isValidEngineType, isValidAppSite, isValidPathComponent } from './securityValidator.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Conditional import for Logger based on environment
const assemblerBasePath = fsSync.existsSync('/app/wwwroot') ? './Assembler/src' : '../Assembler/src';
const loggerModule = await import(`${assemblerBasePath}/common/logger.js`);
const { Logger } = loggerModule;

// Import test and performance utilities
const testingUtilsModule = await import(`${assemblerBasePath}/test/testingUtils.js`);
const { runStandardTests, runAdvancedTests, dumpPreprocessedTemplateStructures, printTestSummaryTable } = testingUtilsModule;

const performanceUtilsModule = await import(`${assemblerBasePath}/performance/performanceUtils.js`);
const { PerformanceUtils } = performanceUtilsModule;

const commonUtilModule = await import(`${assemblerBasePath}/common/commonUtil.js`);
const { CommonUtil } = commonUtilModule;

/**
 * GET / - Root endpoint using Index AppSite
 */
export async function indexEndpoint(req, res, EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess) {
  try {
    // Use Index AppSite with engine toggle parameter
    const rootDirPath = path.join(__dirname, 'wwwroot');

    // Get engine type from query parameter (default to Normal)
    const engineType = req.query.engine || 'Normal';

    // Validate EngineType against allowlist
    if (!isValidEngineType(engineType)) {
      return res.status(400).send('Invalid engine type. Use \'Normal\' or \'PreProcess\'');
    }

    // TEMPORARY: Clear cache for development
    LoaderNormal.clearCache();
    LoaderPreProcess.clearCache();

    // Load templates for Index AppSite
    const normalTemplatesRaw = LoaderNormal.loadGetTemplateFiles(rootDirPath, 'Index');
    const preprocessTemplatesRaw = LoaderPreProcess.loadProcessGetTemplateFiles(rootDirPath, 'Index');

    // Merge using selected engine (no AppView context for Index)
    let mergedHtml;
    if (engineType.toLowerCase() === 'preprocess') {
      const engine = new EnginePreProcess();
      mergedHtml = engine.mergeTemplates('Index', 'Index', null, preprocessTemplatesRaw.templates);
    } else {
      const engine = new EngineNormal();
      mergedHtml = engine.mergeTemplates('Index', 'Index', null, normalTemplatesRaw);
    }

    res.setHeader('Content-Type', 'text/html');
    res.send(mergedHtml);
  } catch (error) {
    console.error('Error in root endpoint:', error);
    res.status(500).send('Internal server error');
  }
}

/**
 * GET /api/scenarios - Get all scenarios
 */
export async function scenariosEndpoint(req, res, ConfigUtil) {
  try {
    const scenarios = ConfigUtil.getScenarios();
    const scenarioDtos = scenarios.map(s => ({
      appSite: s.appSite,
      appFile: s.appFile,
      appView: s.appView,
      displayName: s.displayName,
      description: s.description
    }));
    res.json(scenarioDtos);
  } catch (error) {
    console.error('Error getting scenarios:', error);
    res.status(500).json({ error: `Error loading scenarios: ${error.message}` });
  }
}

/**
 * POST /merge - Merge templates endpoint
 */
export async function mergeEndpoint(req, res, EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess, ApiResponse, TemplateData, PreProcessTemplateMetadata, ConfigUtil) {
  const serverStart = Date.now();

  try {
    const { appSite, appView, appViewPrefix, engineType } = req.body;

    // Validate required fields
    if (!appSite || !engineType) {
      return res.status(400).json({
        error: 'Missing required fields: appSite, engineType'
      });
    }

    // Get AppFile from scenarios
    const scenarios = ConfigUtil.getScenarios();
    const appViewValue = appView || '';
    const matchingScenario = scenarios.find(s =>
      s.appSite.toLowerCase() === appSite.toLowerCase() &&
      s.appView.toLowerCase() === appViewValue.toLowerCase()
    );

    if (!matchingScenario) {
      return res.status(400).json({
        error: `No matching scenario found for AppSite='${appSite}' and AppView='${appViewValue}'`
      });
    }

    const appFile = matchingScenario.appFile;

    const logMsg = `/merge endpoint called with: app_site=${appSite}, app_file=${appFile}, engine_type=${engineType}, app_view=${appView}, app_view_prefix=${appViewPrefix}`;
    console.log(logMsg);
    Logger.info(logMsg, 'MergeEndpoint');

    const rootDirPath = path.join(__dirname, 'wwwroot');

    // Validate EngineType against allowlist
    if (!isValidEngineType(engineType)) {
      return res.status(400).json({ error: 'Invalid EngineType value' });
    }

    // Validate AppSite against allowlist loaded from appsites.csv
    let validAppSites;
    try {
      validAppSites = getValidAppSites(rootDirPath);
    } catch (error) {
      return res.status(500).json({ error: `Failed to load AppSites: ${error.message}` });
    }

    if (!isValidAppSite(appSite, validAppSites)) {
      return res.status(400).json({ error: 'Invalid AppSite value' });
    }

    // Validate path components for path traversal attacks
    if (!isValidPathComponent(appSite)) {
      return res.status(400).json({ error: 'Invalid characters in AppSite' });
    }

    if (!isValidPathComponent(appFile)) {
      return res.status(400).json({ error: 'Invalid characters in AppFile' });
    }

    if (appView && appView !== '' && !isValidPathComponent(appView)) {
      return res.status(400).json({ error: 'Invalid characters in AppView' });
    }

    if (appViewPrefix && appViewPrefix !== '' && !isValidPathComponent(appViewPrefix)) {
      return res.status(400).json({ error: 'Invalid characters in AppViewPrefix' });
    }

    // TEMPORARY: Clear cache for development
    LoaderNormal.clearCache();
    LoaderPreProcess.clearCache();

    // Load templates based on engine type
    const templatesMap = LoaderNormal.loadGetTemplateFiles(rootDirPath, appSite);
    const preTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(rootDirPath, appSite);

    // Convert to TemplateData for ApiResponse
    const apiResponse = new ApiResponse();
    apiResponse.appSite = appSite;
    apiResponse.appFile = appFile || null;
    apiResponse.appView = appView || null;

    for (const [key, value] of Object.entries(templatesMap)) {
      const templateData = new TemplateData();
      templateData.html = value.html;
      templateData.json = value.json || null;
      apiResponse.templates.set(key, templateData);
    }

    // Convert PreprocessedTemplate to PreProcessTemplateMetadata for ApiResponse
    for (const [key, value] of Object.entries(preTemplates.templates)) {
      const metadata = new PreProcessTemplateMetadata();
      metadata.originalContent = value.originalContent;
      metadata.placeholders = value.placeholders;
      metadata.slottedTemplates = value.slottedTemplates;
      metadata.jsonData = value.jsonData;
      metadata.jsonPlaceholders = value.jsonPlaceholders;
      metadata.replacementMappings = value.replacementMappings;
      metadata.hasPlaceholders = value.hasPlaceholders;
      metadata.hasSlottedTemplates = value.hasSlottedTemplates;
      metadata.hasJsonData = value.hasJsonData;
      metadata.hasJsonPlaceholders = value.hasJsonPlaceholders;
      metadata.hasReplacementMappings = value.hasReplacementMappings;
      metadata.requiresProcessing = value.requiresProcessing;
      apiResponse.preProcessTemplates.set(key, metadata);
    }

    const engineStart = Date.now();

    let mergedHtml = '';
    if (engineType.toLowerCase() === 'preprocess') {
      const engine = new EnginePreProcess();
      if (appViewPrefix) {
        engine.appViewPrefix = appViewPrefix;
      }
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, preTemplates.templates);
      // Clear normal templates for PreProcess engine
      apiResponse.templates.clear();
    } else {
      const engine = new EngineNormal();
      if (appViewPrefix) {
        engine.appViewPrefix = appViewPrefix;
      }
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, templatesMap);
      // Clear preprocess templates for Normal engine
      apiResponse.preProcessTemplates.clear();
    }

    apiResponse.engineTimeMs = Date.now() - engineStart;
    apiResponse.serverTimeMs = Date.now() - serverStart;
    apiResponse.html = mergedHtml;

    // Use manual JSON serialization
    res.setHeader('Content-Type', 'application/json');
    res.send(apiResponse.serializeToJson());
  } catch (error) {
    console.error('Error in merge endpoint:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
}

/**
 * GET /test/standard - Run standard tests
 */
export async function testStandardEndpoint(req, res, projectDirectory, ConfigUtil) {
  const start = Date.now();
  const assemblerWebDirPath = path.join(projectDirectory, 'wwwroot');

  // Enable logging temporarily for tests
  const originalLogLevel = Logger.getLogLevel();

  // Configure logger with context-specific log files for StandardTests
  const templateAnalysisDir = path.join(projectDirectory, 'template_analysis');
  const logsDir = path.join(templateAnalysisDir, 'logs');
  await fsSync.promises.mkdir(logsDir, { recursive: true }).catch(() => {});

  const contextLogFiles = {
    'LoaderNormal': path.join(logsDir, 'nodejs_loadernormal.log'),
    'EngineNormal': path.join(logsDir, 'nodejs_enginenormal.log')
  };

  Logger.configure(0, null, false); // DEBUG level
  Logger.configureContextLogFiles(contextLogFiles);

  try {
    const scenarios = ConfigUtil.getScenarios();
    const results = await runStandardTests(assemblerWebDirPath, projectDirectory, scenarios, false, true, true);
    if (results && results.length > 0) {
      await printTestSummaryTable(assemblerWebDirPath, results, 'STANDARD TEST');
    }

    // Restore original log level
    Logger.setLogLevel(originalLogLevel);

    const elapsed = (Date.now() - start) / 1000;
    const testCount = results.length;

    // Check for failures
    const failedCount = results.filter(r =>
      r.normalPreProcess === 'FAIL' ||
      r.crossViewUnMatch === 'FAIL' ||
      (r.error && r.error !== '')
    ).length;

    let message = `Successful run of Standard Tests in ${elapsed.toFixed(2)} secs (${testCount} tests)`;
    if (failedCount > 0) {
      message += `\n⚠️ Warning: ${failedCount} test(s) failed`;
    }

    res.json({
      success: true,
      message: message,
      elapsed: elapsed,
      testCount: testCount
    });
  } catch (error) {
    // Restore original log level
    Logger.setLogLevel(originalLogLevel);
    console.error('Error in standard tests:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
}

/**
 * GET /test/advanced - Run advanced tests
 */
export async function testAdvancedEndpoint(req, res, projectDirectory, ConfigUtil) {
  const start = Date.now();
  const assemblerWebDirPath = path.join(projectDirectory, 'wwwroot');

  // Enable logging temporarily for tests
  const originalLogLevel = Logger.getLogLevel();

  // Configure logger with context-specific log files for AdvancedTests
  const templateAnalysisDir = path.join(projectDirectory, 'template_analysis');
  const logsDir = path.join(templateAnalysisDir, 'logs');
  await fsSync.promises.mkdir(logsDir, { recursive: true }).catch(() => {});

  const contextLogFiles = {
    'LoaderNormal': path.join(logsDir, 'nodejs_loadernormal.log'),
    'LoaderPreProcess': path.join(logsDir, 'nodejs_loaderpreprocess.log'),
    'EngineNormal': path.join(logsDir, 'nodejs_enginenormal.log'),
    'EnginePreProcess': path.join(logsDir, 'nodejs_enginepreprocess.log')
  };

  Logger.configure(0, null, false); // DEBUG level
  Logger.configureContextLogFiles(contextLogFiles);

  try {
    const scenarios = ConfigUtil.getScenarios();
    await dumpPreprocessedTemplateStructures(assemblerWebDirPath, projectDirectory, scenarios, true);
    const results = await runAdvancedTests(assemblerWebDirPath, projectDirectory, scenarios, false, true, true);
    if (results && results.length > 0) {
      await printTestSummaryTable(assemblerWebDirPath, results, 'ADVANCED TEST');
    }

    // Restore original log level
    Logger.setLogLevel(originalLogLevel);

    const elapsed = (Date.now() - start) / 1000;
    const testCount = results.length;

    // Check for failures
    const failedCount = results.filter(r =>
      r.normalPreProcess === 'FAIL' ||
      r.crossViewUnMatch === 'FAIL' ||
      (r.error && r.error !== '')
    ).length;

    let message = `Successful run of Advanced Tests in ${elapsed.toFixed(2)} secs (${testCount} tests)`;
    if (failedCount > 0) {
      message += `\n⚠️ Warning: ${failedCount} test(s) failed`;
    }

    res.json({
      success: true,
      message: message,
      elapsed: elapsed,
      testCount: testCount
    });
  } catch (error) {
    // Restore original log level
    Logger.setLogLevel(originalLogLevel);
    console.error('Error in advanced tests:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
}

/**
 * GET /test/performance - Run performance tests
 */
export async function testPerformanceEndpoint(req, res, projectDirectory, ConfigUtil) {
  const start = Date.now();
  const assemblerWebDirPath = path.join(projectDirectory, 'wwwroot');

  try {
    // Disable logging during performance tests
    Logger.setLogLevel(5); // NONE level

    const scenarios = ConfigUtil.getScenarios();
    const results = PerformanceUtils.runPerformanceComparison(assemblerWebDirPath, scenarios, true, true);
    if (results && results.length > 0) {
      PerformanceUtils.printPerfSummaryTable(assemblerWebDirPath, results);
    }

    // Restore original log level
    Logger.setLogLevel(0); // DEBUG level

    const elapsed = (Date.now() - start) / 1000;
    const testCount = results.length;

    // Check for performance test mismatches
    const mismatchCount = results.filter(r => r.resultsMatch !== 'YES').length;

    let message = `Successful run of Performance Tests in ${elapsed.toFixed(2)} secs (${testCount} tests)`;
    if (mismatchCount > 0) {
      message += `\n⚠️ Warning: ${mismatchCount} test(s) have output mismatch`;
    }

    res.json({
      success: true,
      message: message,
      elapsed: elapsed,
      testCount: testCount
    });
  } catch (error) {
    console.error('Error in performance tests:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
}

/**
 * GET /test/consolidate-performance - Consolidate performance data from all servers
 */
export async function testConsolidatePerformanceEndpoint(req, res, projectDirectory) {
  const start = Date.now();
  const assemblerWebDirPath = path.join(projectDirectory, 'wwwroot');

  // Configure logging for consolidate endpoint
  const templateAnalysisDir = path.join(projectDirectory, 'template_analysis');
  const logsDir = path.join(templateAnalysisDir, 'logs');
  await fsSync.promises.mkdir(logsDir, { recursive: true }).catch(() => {});
  const consolidateLogFile = path.join(logsDir, 'nodejs_consolidate_perf.log');

  // Log start
  const logMsg = `\n[${new Date().toISOString()}] Starting consolidate-performance endpoint\n`;
  await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => {});

  try {
    // Read server configuration from servers.json
    const serversConfigPath = path.join(assemblerWebDirPath, 'servers.json');
    let servers = [
      { language: 'CSharp', url: 'https://csharpassembler.fly.dev/csharp_perfsummary.json' },
      { language: 'Rust', url: 'https://rustassembler.fly.dev/rust_perfsummary.json' },
      { language: 'Node', url: 'https://nodeassembler.fly.dev/nodejs_perfsummary.json' },
      { language: 'PHP', url: 'https://phpassembler.fly.dev/php_perfsummary.json' },
      { language: 'Go', url: 'https://goassembler.fly.dev/go_perfsummary.json' }
    ];

    if (fsSync.existsSync(serversConfigPath)) {
      try {
        const configJson = fsSync.readFileSync(serversConfigPath, 'utf8');
        const config = JSON.parse(configJson);
        if (config.performanceServers && Array.isArray(config.performanceServers)) {
          servers = config.performanceServers;
        }
      } catch (err) {
        console.warn('Failed to read servers.json, using defaults:', err);
      }
    }

    const serversProcessed = [];
    const serversFailed = [];
    const performanceData = new Map(); // Map<appSite, Map<language, perfData>>

    // Fetch data from each server
    for (const server of servers) {
      // Log fetch attempt
      const logMsg = `[${new Date().toISOString()}] Fetching ${server.language} from ${server.url}\n`;
      await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => {});

      try {
        const response = await fetch(server.url, {
          signal: AbortSignal.timeout(30000)
        });

        if (response.ok) {
          const jsonText = await response.text();
          const data = JSON.parse(jsonText);

          // Process each performance entry
          if (Array.isArray(data)) {
            const itemCount = data.length;
            for (const entry of data) {
              const appSite = getStringField(entry, 'appSite', 'AppSite', 'app_site');
              const appView = getStringField(entry, 'appView', 'AppView', 'app_view');

              // Handle both milliseconds and nanoseconds
              let normalTimeMs = getFloatField(entry, 'normalTimeMs', 'NormalTimeMs', 'normal_time_ms');
              const normalTimeNanos = getFloatField(entry, 'NormalTimeNanos', 'normal_time_nanos');
              if (normalTimeNanos !== null) {
                normalTimeMs = normalTimeNanos / 1_000_000;
              }

              let preProcessTimeMs = getFloatField(entry, 'preProcessTimeMs', 'PreProcessTimeMs', 'preprocess_time_ms');
              const preProcessTimeNanos = getFloatField(entry, 'PreProcessTimeNanos', 'preprocess_time_nanos');
              if (preProcessTimeNanos !== null) {
                preProcessTimeMs = preProcessTimeNanos / 1_000_000;
              }

              const outputSize = getIntField(entry, 'outputSize', 'OutputSize', 'output_size');

              if (!performanceData.has(appSite)) {
                performanceData.set(appSite, new Map());
              }

              const langMap = performanceData.get(appSite);
              langMap.set(server.language, {
                normalTimeMs,
                preProcessTimeMs,
                outputSize,
                appView
              });
            }
            // Log success
            const logMsg = `[${new Date().toISOString()}] ✅ ${server.language}: Successfully processed ${itemCount} items\n`;
            await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => {});
          }

          serversProcessed.push(server.language);
        } else {
          const domain = server.url.replace(/^https?:\/\//, '').split('/')[0];
          const failureMsg = `${server.language}: ${domain} (HTTP ${response.status})`;
          serversFailed.push(failureMsg);
          // Log failure
          const logMsg = `[${new Date().toISOString()}] ❌ ${failureMsg}\n`;
          await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => {});
        }
      } catch (err) {
        const domain = server.url.replace(/^https?:\/\//, '').split('/')[0];
        const failureMsg = `${server.language}: ${domain} (ERROR: ${err.message})`;
        serversFailed.push(failureMsg);
        // Log failure
        const logMsg = `[${new Date().toISOString()}] ❌ ${failureMsg}\n`;
        await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => {});
      }
    }

    // Generate HTML report
    const html = [];
    html.push('<!DOCTYPE html>');
    html.push('<html>');
    html.push('<head>');
    html.push('    <meta charset="UTF-8">');
    html.push('    <meta name="viewport" content="width=device-width, initial-scale=1.0">');
    html.push('    <title>All Performance Tests</title>');
    html.push('    <style>');
    html.push('        body { font-family: Arial, sans-serif; margin: 20px; }');
    html.push('        h1, h2 { color: #333; }');
    html.push('        .table-container { overflow-x: auto; }');
    html.push('        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }');
    html.push('        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }');
    html.push('        th { background-color: #4CAF50; color: white; }');
    html.push('        tr:nth-child(even) { background-color: #f2f2f2; }');
    html.push('        @media (max-width: 768px) {');
    html.push('            body { margin: 10px; }');
    html.push('            th, td { padding: 8px; font-size: 14px; }');
    html.push('            h1, h2 { font-size: 24px; }');
    html.push('        }');
    html.push('    </style>');
    html.push('</head>');
    html.push('<body>');
    html.push('<h1>Consolidated Performance Tests</h1>');

    // Sort appSites
    const sortedAppSites = Array.from(performanceData.keys()).sort();

    // Normal Engine Table
    html.push('<h2>Normal Engine Performance (ms)</h2>');
    html.push('<div class="table-container">');
    html.push('<table><tr><th>AppSite</th>');
    for (const lang of ['CSharp', 'Rust', 'Go', 'Node', 'PHP']) {
      html.push(`<th>${lang}</th>`);
    }
    html.push('<th>Output Size</th></tr>');

    for (const appSite of sortedAppSites) {
      const langData = performanceData.get(appSite);
      html.push(`<tr><td>${appSite}</td>`);

      for (const lang of ['CSharp', 'Rust', 'Go', 'Node', 'PHP']) {
        const data = langData.get(lang);
        html.push(`<td>${formatFloat(data?.normalTimeMs)}</td>`);
      }

      // Output size from first available language
      const firstOutputSize = getFirstOutputSize(langData);
      html.push(`<td>${formatInt(firstOutputSize)}</td>`);
      html.push('</tr>');
    }
    html.push('</table>');
    html.push('</div>');

    // PreProcess Engine Table
    html.push('<h2>PreProcess Engine Performance (ms)</h2>');
    html.push('<div class="table-container">');
    html.push('<table><tr><th>AppSite</th>');
    for (const lang of ['CSharp', 'Rust', 'Go', 'Node', 'PHP']) {
      html.push(`<th>${lang}</th>`);
    }
    html.push('<th>Output Size</th></tr>');

    for (const appSite of sortedAppSites) {
      const langData = performanceData.get(appSite);
      html.push(`<tr><td>${appSite}</td>`);

      for (const lang of ['CSharp', 'Rust', 'Go', 'Node', 'PHP']) {
        const data = langData.get(lang);
        html.push(`<td>${formatFloat(data?.preProcessTimeMs)}</td>`);
      }

      const firstOutputSize = getFirstOutputSize(langData);
      html.push(`<td>${formatInt(firstOutputSize)}</td>`);
      html.push('</tr>');
    }
    html.push('</table>');
    html.push('</div>');
    html.push('</body>');
    html.push('</html>');

    // Write HTML file to Reports directory
    const reportsDir = path.join(assemblerWebDirPath, 'Reports');
    await fsSync.promises.mkdir(reportsDir, { recursive: true }).catch(() => {});
    const htmlPath = path.join(reportsDir, 'all_perf_tests.html');
    fsSync.writeFileSync(htmlPath, html.join('\n'), 'utf8');

    const elapsed = (Date.now() - start) / 1000;

    // Log completion
    const completionLogMsg = `[${new Date().toISOString()}] Consolidation complete in ${elapsed.toFixed(2)}s - ${performanceData.size} AppSites from ${serversProcessed.length}/${servers.length} servers\n`;
    await fsSync.promises.appendFile(consolidateLogFile, completionLogMsg).catch(() => {});

    let message = `Consolidated performance data from ${serversProcessed.length}/${servers.length} servers in ${elapsed.toFixed(2)} secs`;
    if (serversProcessed.length > 0) {
      message += ` | ✅ Success: ${serversProcessed.join(', ')}`;
    }
    if (serversFailed.length > 0) {
      message += `\n❌ Failed: ${serversFailed.join('; ')}`;
    }

    res.json({
      success: serversProcessed.length > 0,
      message: message,
      elapsed: elapsed,
      testCount: serversProcessed.length
    });
  } catch (error) {
    console.error('Error in consolidate performance:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
}

// Helper functions for field extraction
function getStringField(obj, ...keys) {
  for (const key of keys) {
    if (obj[key] !== undefined && obj[key] !== null) {
      return String(obj[key]);
    }
  }
  return '';
}

function getFloatField(obj, ...keys) {
  for (const key of keys) {
    if (obj[key] !== undefined && obj[key] !== null) {
      const val = parseFloat(obj[key]);
      if (!isNaN(val)) {
        return val;
      }
    }
  }
  return null;
}

function getIntField(obj, ...keys) {
  for (const key of keys) {
    if (obj[key] !== undefined && obj[key] !== null) {
      const val = parseInt(obj[key]);
      if (!isNaN(val)) {
        return val;
      }
    }
  }
  return null;
}

function formatFloat(val) {
  return val !== null && val !== undefined ? val.toFixed(2) : '-';
}

function formatInt(val) {
  return val !== null && val !== undefined ? val.toString() : '-';
}

function getFirstOutputSize(langData) {
  for (const data of langData.values()) {
    if (data.outputSize !== null && data.outputSize !== undefined) {
      return data.outputSize;
    }
  }
  return null;
}

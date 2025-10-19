
import path from 'path';
import { fileURLToPath } from 'url';
import fsSync from 'fs';
import { getValidAppSites, isValidEngineType, isValidAppSite, isValidPathComponent } from './securityValidator.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEFAULT_APP_SITE = 'Test';

// Conditional import for Logger based on environment
const assemblerBasePath = fsSync.existsSync('/app/wwwroot') ? './Assembler/src' : '../Assembler/src';
const loggerModule = await import(`${assemblerBasePath}/common/logger.js`);
const { Logger } = loggerModule;

const testingUtilsModule = await import(`${assemblerBasePath}/test/testingUtils.js`);
const { dumpPreprocessedTemplateStructures } = testingUtilsModule;

/**
 * GET / - Root endpoint using Default AppSite
 */
export async function indexEndpoint(req, res, EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess, ConfigUtil) {
  try {
    // Get appsite from query parameter or use default
    const rootDirPath = path.join(__dirname, 'wwwroot');

    let appSite = DEFAULT_APP_SITE;
    let appFile = 'index';

    // Get appsite from query parameter
    const requestedAppSite = req.query.appsite;

    // If appsite query param is provided, validate it exists in scenarios
    if (requestedAppSite) {
      // Validate AppSite against allowlist
      const validAppSites = getValidAppSites();
      if (!validAppSites.has(requestedAppSite.toLowerCase())) {
        return res.status(400).send('Invalid AppSite value');
      }

      // Validate path components for path traversal attacks
      if (!isValidPathComponent(requestedAppSite)) {
        return res.status(400).send('Invalid characters in AppSite');
      }

      // Get AppFile from scenarios
      const scenarios = ConfigUtil.getScenarios();
      const matchingScenario = scenarios.find(s =>
        s.appSite.toLowerCase() === requestedAppSite.toLowerCase() && !s.appView
      );

      if (!matchingScenario) {
        return res.status(400).send(`No matching scenario found for AppSite='${requestedAppSite}' without AppView`);
      }

      appSite = matchingScenario.appSite;
      appFile = matchingScenario.appFile;
    }

    // Get engine type from query parameter (default to Normal)
    const engineType = req.query.engine || 'Normal';

    // Validate EngineType against allowlist
    if (!isValidEngineType(engineType)) {
      return res.status(400).send('Invalid engine type. Use \'Normal\' or \'PreProcess\'');
    }

    // Load templates for requested AppSite
    const normalTemplatesRaw = LoaderNormal.loadGetTemplateFiles(rootDirPath, appSite);
    const preprocessTemplatesRaw = LoaderPreProcess.loadProcessGetTemplateFiles(rootDirPath, appSite);

    // Merge using selected engine (no AppView context)
    let mergedHtml;
    if (engineType.toLowerCase() === 'preprocess') {
      const engine = new EnginePreProcess();
      mergedHtml = engine.mergeTemplates(appSite, appFile, null, preprocessTemplatesRaw.templates);
    } else {
      const engine = new EngineNormal();
      mergedHtml = engine.mergeTemplates(appSite, appFile, null, normalTemplatesRaw);
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
export async function mergeEndpoint(req, res, EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess, ApiResponse, TemplateData, PreProcessTemplateMetadata, ConfigUtil, projectDirectory) {
  const serverStart = Date.now();

  // Enable logging for merge operations
  const originalLogLevel = Logger.getLogLevel();

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
    const { appSite, appView, engineType } = req.body;

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

    // Calculate appViewPrefix from appFile when appView is not empty
    const appViewPrefix = appView && appView !== '' ? appFile : '';

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
      validAppSites = getValidAppSites();
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

    // Save HTML output only if save query parameter is present
    const saveParam = req.query.save;
    if (saveParam && saveParam.toLowerCase() === 'true') {
      const outputDir = path.join(projectDirectory, 'template_analysis', 'output');
      await fsSync.promises.mkdir(outputDir, { recursive: true }).catch(() => {});

      const appViewSuffix = appView && appView !== '' ? `_${appView}` : '';
      const engineSuffix = engineType.toLowerCase();
      const outputFile = path.join(outputDir, `${appSite}${appViewSuffix}_${engineSuffix}.html`);
      await fsSync.promises.writeFile(outputFile, mergedHtml, 'utf8').catch(err => {
        console.error('Error saving output file:', err);
      });
    }

    // Use manual JSON serialization
    res.setHeader('Content-Type', 'application/json');
    res.send(apiResponse.serializeToJson());
  } catch (error) {
    console.error('Error in merge endpoint:', error);
    res.status(500).json({ error: 'Internal server error' });
  } finally {
    // Restore original log level
    Logger.setLogLevel(originalLogLevel);
  }
}

/**
 * POST /api/templates - Get templates for an AppSite
 */
export async function getTemplatesEndpoint(req, res, LoaderNormal, LoaderPreProcess, ApiResponse, TemplateData, PreProcessTemplateMetadata, projectDirectory) {
    try {
        const rootDirPath = path.join(__dirname, 'wwwroot')

        // Read request body
        const { appsite } = req.body
        const appSite = appsite

        if (!appSite || appSite === '') {
            return res.status(400).send('Missing appsite parameter')
        }

        // Validate AppSite against allowlist loaded from appsites.csv
        const validAppSites = getValidAppSites()
        if (!isValidAppSite(appSite, validAppSites)) {
            return res.status(400).send('Invalid AppSite value')
        }

        // Validate path components for path traversal attacks
        if (!isValidPathComponent(appSite)) {
            return res.status(400).send('Invalid characters in AppSite')
        }

        const serverStart = Date.now()

        // Load Normal templates
        const normalTemplates = LoaderNormal.loadGetTemplateFiles(rootDirPath, appSite)

        // Load PreProcess templates
        const preprocessTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(rootDirPath, appSite)

        // Convert Normal templates to TemplateData objects for proper JSON serialization
        const normalResult = new Map()
        for (const [key, value] of normalTemplates.entries()) {
            const templateData = new TemplateData()
            templateData.html = value.html
            templateData.json = value.json
            normalResult.set(key, templateData)
        }

        // Convert PreProcess templates to metadata-only objects
        const preprocessResult = new Map()
        for (const [key, value] of preprocessTemplates.templates.entries()) {
            // Use toObject() to properly convert Maps and other structures
            const plainObject = value.toObject()
            
            const metadata = new PreProcessTemplateMetadata()
            metadata.originalContent = plainObject.originalContent
            metadata.placeholders = plainObject.placeholders
            metadata.slottedTemplates = plainObject.slottedTemplates
            metadata.jsonData = plainObject.jsonData
            metadata.jsonPlaceholders = plainObject.jsonPlaceholders
            metadata.replacementMappings = plainObject.replacementMappings
            metadata.hasPlaceholders = plainObject.hasPlaceholders
            metadata.hasSlottedTemplates = plainObject.hasSlottedTemplates
            metadata.hasJsonData = plainObject.hasJsonData
            metadata.hasJsonPlaceholders = plainObject.hasJsonPlaceholders;
            metadata.hasReplacementMappings = plainObject.hasReplacementMappings;
            metadata.requiresProcessing = plainObject.requiresProcessing;
            preprocessResult.set(key, metadata)
        }

        const serverEnd = Date.now()
        const serverTimeMs = serverEnd - serverStart

        // Use proper ApiResponse structure
        const response = new ApiResponse()
        response.templates = normalResult
        response.preProcessTemplates = preprocessResult
        response.appSite = appSite
        response.appFile = null
        response.appView = null
        response.serverTimeMs = serverTimeMs

        const jsonResult = response.serializeToJson(false)

        // Check if save query parameter is present
        const saveParam = req.query.save
        if (saveParam && saveParam.toLowerCase() === 'true') {
            const templatesDir = path.join(projectDirectory, 'template_analysis', 'templates')
            await fsSync.promises.mkdir(templatesDir, { recursive: true }).catch(() => { })

            const saveFile = path.join(templatesDir, `nodejs_${appSite}_templates.json`)
            await fsSync.promises.writeFile(saveFile, jsonResult, 'utf8').catch(err => {
                console.error('Error saving templates file:', err)
            })

            // Save structure dump using TestingUtils logic
            // Build a scenario list for the requested appSite
            const scenarios = [{ appSite: appSite, appFile: '', appView: '' }]
            await dumpPreprocessedTemplateStructures(rootDirPath, projectDirectory, scenarios, true)
        }

        res.setHeader('Content-Type', 'application/json')
        res.send(jsonResult)
    } catch (error) {
        console.error('Error in /api/templates:', error)
        res.status(500).send(`Error: ${error.message}`)
    }
}

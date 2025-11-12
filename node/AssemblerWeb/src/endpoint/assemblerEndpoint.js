
import path from 'path';
import fs from 'fs';
import { fileURLToPath } from 'url';
import { EngineNormal, EnginePreProcess, EngineNormalJson, EnginePreProcessJson, LoaderNormal, LoaderPreProcess, LoaderNormalJson, LoaderPreProcessJson, ApiResponse, TemplateData, PreProcessTemplateMetadata, ConfigUtil } from '@arshu/assembler';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEFAULT_APP_SITE = 'Main';
const DEFAULT_ENGINE_TYPE = 'Normal';
const SEARCH_APP_SITE = 'Main, Language'

// Maximum parameter length to prevent DoS attacks
const PARAM_MAX_LENGTH = 256;

// Valid engine types allowlist
const VALID_ENGINE_TYPES = new Set(['normal', 'preprocess', 'normaljson', 'preprocessjson']);

// Cached ValidAppSites - loaded on first request
let cachedValidAppSites = null;

/**
 * Gets the valid AppSites from ConfigUtil. Throws if not loaded.
 */
function getValidAppSites(ConfigUtil) {
  if (cachedValidAppSites !== null) {
    return cachedValidAppSites;
  }

  cachedValidAppSites = ConfigUtil.getAppSites();
  return cachedValidAppSites;
}

/**
 * Validates if a path component is safe (no traversal, invalid chars, or excessive length)
 */
function isValidPathComponent(value) {
  if (!value || typeof value !== 'string') {
    return false;
  }

  const v = value.trim();
  if (v === '') {
    return false;
  }

  // Check parameter length to prevent DoS
  if (v.length > PARAM_MAX_LENGTH) {
    return false;
  }

  // Check for path traversal attempts
  if (v.includes('..') || v.includes('/') || v.includes('\\')) {
    return false;
  }

  // Check for other suspicious characters
  const invalidChars = ['<', '>', ':', '"', '|', '?', '*', '\0'];
  for (const char of v) {
    if (invalidChars.includes(char)) {
      return false;
    }
    // Check for control characters
    if (char.charCodeAt(0) < 32) {
      return false;
    }
  }

  return true;
}

/**
 * Validates engine type against allowlist (case-insensitive)
 */
function isValidEngineType(engineType) {
  if (!engineType || typeof engineType !== 'string') {
    return false;
  }
  return VALID_ENGINE_TYPES.has(engineType.toLowerCase());
}

/**
 * Validates app_site against allowlist (case-insensitive)
 */
function isValidAppSite(appSite, validAppSites) {
  if (!appSite || typeof appSite !== 'string') {
    return false;
  }
  return validAppSites.has(appSite.toLowerCase());
}

// Import Logger from the assembler package
import { Logger } from '@arshu/assembler';

/**
 * GET / - Index endpoint (root page)
 */
export async function indexEndpoint(req, res, EngineNormal, EnginePreProcess, EngineNormalJson, EnginePreProcessJson, LoaderNormal, LoaderPreProcess, LoaderNormalJson, LoaderPreProcessJson, ConfigUtil) {
  try {
    // Get appsite from query parameter or use default
    const projectDirectory = process.cwd();
    const rootDirPath = path.join(projectDirectory, 'wwwroot');

    let appSite = DEFAULT_APP_SITE;
    let appFile = 'index';

    // Get appsite from query parameter
    const requestedAppSite = req.query.appsite;

    // If appsite query param is provided, validate it exists in scenarios
    if (requestedAppSite) {
      // Validate AppSite against allowlist
      const validAppSites = getValidAppSites(ConfigUtil);
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

    // Get engine type from query parameter (default to DEFAULT_ENGINE_TYPE)
    const engineType = req.query.engine || DEFAULT_ENGINE_TYPE;

    // Validate EngineType against allowlist
    if (!isValidEngineType(engineType)) {
      return res.status(400).send('Invalid engine type. Use \'Normal\', \'PreProcess\', \'NormalJson\', or \'PreProcessJson\'');
    }

    // Merge using selected engine (no AppView context) using loader-based interfaces
    let mergedHtml;
    if (engineType.toLowerCase() === 'preprocessjson') {
      const loader = new LoaderPreProcessJson(rootDirPath, appSite, SEARCH_APP_SITE);
      await loader.load();
      const engine = new EnginePreProcessJson();
      mergedHtml = engine.mergeTemplates(appSite, appFile, null, loader);
    } else if (engineType.toLowerCase() === 'preprocess') {
      const loader = new LoaderPreProcess(rootDirPath, appSite, SEARCH_APP_SITE);
      await loader.load();
      const engine = new EnginePreProcess();
      mergedHtml = engine.mergeTemplates(appSite, appFile, null, loader);
    } else if (engineType.toLowerCase() === 'normaljson') {
      const loader = new LoaderNormalJson(rootDirPath, appSite, SEARCH_APP_SITE);
      await loader.load();
      const engine = new EngineNormalJson();
      mergedHtml = engine.mergeTemplates(appSite, appFile, null, loader);
    } else {
      const loader = new LoaderNormal(rootDirPath, appSite, SEARCH_APP_SITE);
      await loader.load();
      const engine = new EngineNormal();
      mergedHtml = engine.mergeTemplates(appSite, appFile, null, loader);
    }

    res.setHeader('Content-Type', 'text/html');
    res.send(mergedHtml);
  } catch (error) {
    console.error('Error in root endpoint:', error);
    res.status(500).send('Internal server error');
  }
}

/**
 * GET /:appSite/:appView? - Navigation endpoint
 */
export async function navigationEndpoint(req, res, EngineNormal, EnginePreProcess, EngineNormalJson, EnginePreProcessJson, LoaderNormal, LoaderPreProcess, LoaderNormalJson, LoaderPreProcessJson, ConfigUtil) {
  try {
    const appSite = req.params.appSite;
    const appView = req.params.appView || '';

    // Validate AppSite against allowlist
    const validAppSites = ConfigUtil.getAppSites();
    if (!validAppSites.some(s => s.toLowerCase() === appSite.toLowerCase())) {
      return res.status(400).send('Invalid AppSite value');
    }

    // Validate path components for path traversal attacks
    if (!isValidPathComponent(appSite)) {
      return res.status(400).send('Invalid characters in AppSite');
    }

    if (appView && !isValidPathComponent(appView)) {
      return res.status(400).send('Invalid characters in AppView');
    }

    // Get AppFile from scenarios
    const scenarios = ConfigUtil.getScenarios();
    const matchingScenario = scenarios.find(s =>
      s.appSite.toLowerCase() === appSite.toLowerCase() &&
      s.appView.toLowerCase() === appView.toLowerCase()
    );

    if (!matchingScenario) {
      return res.status(400).send(`No matching scenario found for AppSite='${appSite}' and AppView='${appView}'`);
    }

    const appFile = matchingScenario.appFile;

    // Get engine type from query parameter (default to DEFAULT_ENGINE_TYPE)
    const engineType = req.query.engine || DEFAULT_ENGINE_TYPE;

    // Validate EngineType against allowlist
    if (!isValidEngineType(engineType)) {
      return res.status(400).send("Invalid engine type. Use 'Normal', 'PreProcess', 'NormalJson', or 'PreProcessJson'");
    }

    // Get wwwroot path
    const wwwrootPath = getWwwrootPath();

    // Merge using selected engine
    let mergedHtml;
    if (engineType.toLowerCase() === 'preprocessjson') {
      const loader = new LoaderPreProcessJson(wwwrootPath, appSite, SEARCH_APP_SITE);
      await loader.load();
      const engine = new EnginePreProcessJson();
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, loader);
    } else if (engineType.toLowerCase() === 'preprocess') {
      const loader = new LoaderPreProcess(wwwrootPath, appSite, SEARCH_APP_SITE);
      await loader.load();
      const engine = new EnginePreProcess();
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, loader);
    } else if (engineType.toLowerCase() === 'normaljson') {
      const loader = new LoaderNormalJson(wwwrootPath, appSite, SEARCH_APP_SITE);
      await loader.load();
      const engine = new EngineNormalJson();
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, loader);
    } else {
      const loader = new LoaderNormal(wwwrootPath, appSite, SEARCH_APP_SITE);
      await loader.load();
      const engine = new EngineNormal();
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, loader);
    }

    res.setHeader('Content-Type', 'text/html');
    res.send(mergedHtml);
  } catch (error) {
    console.error('Error in navigation endpoint:', error);
    res.status(500).send('Internal server error');
  }
}

/**
 * POST /merge - Merge templates endpoint
 */
export async function mergeEndpoint(req, res, EngineNormal, EnginePreProcess, EngineNormalJson, EnginePreProcessJson, LoaderNormal, LoaderPreProcess, LoaderNormalJson, LoaderPreProcessJson, ApiResponse, TemplateData, PreProcessTemplateMetadata, ConfigUtil) {
  const serverStart = Date.now();

  // Enable logging for merge operations
  const projectDirectory = process.cwd();
  const templateAnalysisDir = path.join(projectDirectory, 'Analysis');
  const logsDir = path.join(templateAnalysisDir, 'logs');
  await fs.promises.mkdir(logsDir, { recursive: true }).catch(() => {});

  const contextLogFiles = {
    'LoaderNormal': path.join(logsDir, 'nodejs_loadernormal.log'),
    'LoaderPreProcess': path.join(logsDir, 'nodejs_loaderpreprocess.log'),
    'LoaderNormalJson': path.join(logsDir, 'nodejs_loadernormaljson.log'),
    'LoaderPreProcessJson': path.join(logsDir, 'nodejs_loaderpreprocessjson.log'),
    'EngineNormal': path.join(logsDir, 'nodejs_enginenormal.log'),
    'EnginePreProcess': path.join(logsDir, 'nodejs_enginepreprocess.log'),
    'EngineNormalJson': path.join(logsDir, 'nodejs_enginenormaljson.log'),
    'EnginePreProcessJson': path.join(logsDir, 'nodejs_enginepreprocessjson.log')
  };

  Logger.addContextLogFiles(contextLogFiles);

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

    const rootDirPath = path.join(projectDirectory, 'wwwroot');

    // Validate EngineType against allowlist
    if (!isValidEngineType(engineType)) {
      return res.status(400).json({ error: 'Invalid EngineType value' });
    }

    // Validate AppSite against allowlist loaded from appsites.csv
    let validAppSites;
    try {
      validAppSites = getValidAppSites(ConfigUtil);
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

    // Prepare loaders (interface-based)
    const normalLoader = new LoaderNormalJson(rootDirPath, appSite, SEARCH_APP_SITE);
    const preprocLoader = new LoaderPreProcessJson(rootDirPath, appSite, SEARCH_APP_SITE);
    await Promise.all([normalLoader.load(), preprocLoader.load()]);

    // Convert to TemplateData for ApiResponse
    const apiResponse = new ApiResponse();
    apiResponse.appSite = appSite;
    apiResponse.appFile = appFile || null;
    apiResponse.appView = appView || null;

    for (const [key, value] of (normalLoader._templates || new Map())) {
      const templateData = new TemplateData();
      templateData.html = value.html;
      templateData.json = value.json || null;
      apiResponse.templates.set(key, templateData);
    }

    // Convert PreprocessedTemplate to PreProcessTemplateMetadata for ApiResponse
    for (const [key, value] of (preprocLoader._templates || new Map())) {
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
      if (appViewPrefix) engine.appViewPrefix = appViewPrefix;
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, preprocLoader);
      apiResponse.templates.clear();
    } else {
      const engine = new EngineNormal();
      if (appViewPrefix) engine.appViewPrefix = appViewPrefix;
      mergedHtml = engine.mergeTemplates(appSite, appFile, appView, normalLoader);
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
 * POST /api/templates - Get templates for an AppSite
 */
export async function getTemplatesEndpoint(req, res, LoaderNormal, LoaderPreProcess, ApiResponse, TemplateData, PreProcessTemplateMetadata, ConfigUtil) {
    // Enable logging for template operations
    const projectDirectory = process.cwd();
    const templateAnalysisDir = path.join(projectDirectory, 'Analysis');
    const logsDir = path.join(templateAnalysisDir, 'logs');
    await fs.promises.mkdir(logsDir, { recursive: true }).catch(() => {});

    const contextLogFiles = {
        'LoaderNormalJson': path.join(logsDir, 'nodejs_loadernormaljson.log'),
        'LoaderPreProcessJson': path.join(logsDir, 'nodejs_loaderpreprocessjson.log')
    };

    Logger.addContextLogFiles(contextLogFiles);

    try {
        const rootDirPath = path.join(projectDirectory, 'wwwroot');

        // Read request body
        const { appsite } = req.body
        const appSite = appsite

        if (!appSite || appSite === '') {
            return res.status(400).send('Missing appsite parameter')
        }

        // Validate AppSite against allowlist loaded from appsites.csv
        const validAppSites = getValidAppSites(ConfigUtil)
        if (!isValidAppSite(appSite, validAppSites)) {
            return res.status(400).send('Invalid AppSite value')
        }

        // Validate path components for path traversal attacks
        if (!isValidPathComponent(appSite)) {
            return res.status(400).send('Invalid characters in AppSite')
        }

        const serverStart = Date.now()

        // Load Normal templates using loader instance
        const normalLoader = new LoaderNormal(rootDirPath, appSite, '')

        // Load PreProcess templates using loader instance
        const preprocessLoader = new LoaderPreProcess(rootDirPath, appSite, '')

        // Convert Normal templates to TemplateData objects for proper JSON serialization
        const normalResult = new Map()
        // Access templates from loader's internal structure
        for (const [key, value] of (normalLoader._templates || new Map()).entries()) {
            const templateData = new TemplateData()
            templateData.html = value.html
            templateData.json = value.json
            normalResult.set(key, templateData)
        }

        // Convert PreProcess templates to metadata-only objects
        const preprocessResult = new Map()
        // Access templates from loader's internal structure
        const preprocessedSiteTemplates = preprocessLoader._preprocessedSiteTemplates || { templates: new Map() }
        for (const [key, value] of preprocessedSiteTemplates.templates.entries()) {
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

        res.setHeader('Content-Type', 'application/json')
        res.send(jsonResult)
    } catch (error) {
        console.error('Error in /api/templates:', error)
        console.error('Error stack:', error.stack)
        res.status(500).send(`Error: ${error.message}`)
    }
}

/**
 * Maps all assembler endpoints to the Express app
 * Usage: mapAssemblerEndpoints(app)
 */
export function mapAssemblerEndpoints(app) {
    app.get('/', (req, res) => indexEndpoint(req, res, EngineNormal, EnginePreProcess, EngineNormalJson, EnginePreProcessJson, LoaderNormal, LoaderPreProcess, LoaderNormalJson, LoaderPreProcessJson, ConfigUtil));
    app.get('/:appSite', (req, res) => navigationEndpoint(req, res, EngineNormal, EnginePreProcess, EngineNormalJson, EnginePreProcessJson, LoaderNormal, LoaderPreProcess, LoaderNormalJson, LoaderPreProcessJson, ConfigUtil));
    app.get('/:appSite/:appView', (req, res) => navigationEndpoint(req, res, EngineNormal, EnginePreProcess, EngineNormalJson, EnginePreProcessJson, LoaderNormal, LoaderPreProcess, LoaderNormalJson, LoaderPreProcessJson, ConfigUtil));
    app.post('/merge', (req, res) => mergeEndpoint(req, res, EngineNormal, EnginePreProcess, EngineNormalJson, EnginePreProcessJson, LoaderNormal, LoaderPreProcess, LoaderNormalJson, LoaderPreProcessJson, ApiResponse, TemplateData, PreProcessTemplateMetadata, ConfigUtil));
    app.post('/api/templates', (req, res) => getTemplatesEndpoint(req, res, LoaderNormal, LoaderPreProcess, ApiResponse, TemplateData, PreProcessTemplateMetadata, ConfigUtil));
}

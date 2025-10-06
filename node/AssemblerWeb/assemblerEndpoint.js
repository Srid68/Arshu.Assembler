import path from 'path';
import { fileURLToPath } from 'url';
import fsSync from 'fs';
import { getValidAppSites, isValidEngineType, isValidAppSite, isValidPathComponent } from './securityValidator.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Conditional import for Logger based on environment
const assemblerBasePath = fsSync.existsSync('/app/wwwroot') ? './Assembler/src' : '../Assembler/src';
const loggerModule = await import(`${assemblerBasePath}/templateCommon/logger.js`);
const { Logger } = loggerModule;

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
 * POST /merge - Merge templates endpoint
 */
export async function mergeEndpoint(req, res, EngineNormal, EnginePreProcess, LoaderNormal, LoaderPreProcess, ApiResponse, TemplateData, PreProcessTemplateMetadata) {
  const serverStart = Date.now();

  try {
    const { appSite, appView, appViewPrefix, appFile, engineType } = req.body;

    const logMsg = `/merge endpoint called with: app_site=${appSite}, app_file=${appFile}, engine_type=${engineType}, app_view=${appView}, app_view_prefix=${appViewPrefix}`;
    console.log(logMsg);
    Logger.info(logMsg, 'MergeEndpoint');

    // Validate required fields
    if (!appSite || !appFile || !engineType) {
      return res.status(400).json({
        error: 'Missing required fields: appSite, appFile, engineType'
      });
    }

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

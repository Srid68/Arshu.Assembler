<?php

require_once __DIR__ . '/SecurityValidator.php';

use Psr\Http\Message\ResponseInterface as Response;
use Psr\Http\Message\ServerRequestInterface as ServerRequest;
use Assembler\Engine\EngineNormal;
use Assembler\Engine\EnginePreProcess;
use Assembler\Loader\LoaderNormal;
use Assembler\Loader\LoaderPreProcess;
use Assembler\Api\ApiResponse;
use Assembler\Api\TemplateData;
use Assembler\Api\PreProcessTemplateMetadata;
use Assembler\Common\Logger;
use Assembler\Test\TestingUtils;
use Assembler\Performance\PerformanceUtils;
use Assembler\Config\ConfigUtil;

class AssemblerEndpoint
{
    public static function indexEndpoint(ServerRequest $request, Response $response): Response
    {
        try {
            $rootDirPath = __DIR__ . DIRECTORY_SEPARATOR . 'wwwroot';

            $queryParams = $request->getQueryParams();
            $engineType = $queryParams['engine'] ?? 'Normal';

            if (!SecurityValidator::isValidEngineType($engineType)) {
                $response->getBody()->write('Invalid engine type. Use \'Normal\' or \'PreProcess\'');
                return $response->withStatus(400);
            }

            LoaderNormal::clearCache();
            LoaderPreProcess::clearCache();

            $normalTemplatesRaw = LoaderNormal::loadGetTemplateFiles($rootDirPath, 'Index');
            $preprocessTemplatesRaw = LoaderPreProcess::loadProcessGetTemplateFiles($rootDirPath, 'Index');

            $mergedHtml = '';
            if (strcasecmp($engineType, 'PreProcess') === 0) {
                $engine = new EnginePreProcess();
                $mergedHtml = $engine->mergeTemplates('Index', 'Index', null, $preprocessTemplatesRaw->templates);
            } else {
                $engine = new EngineNormal();
                $mergedHtml = $engine->mergeTemplates('Index', 'Index', null, $normalTemplatesRaw);
            }

            $response->getBody()->write($mergedHtml);
            return $response->withHeader('Content-Type', 'text/html');
        } catch (Exception $error) {
            error_log('Error in root endpoint: ' . $error->getMessage());
            $response->getBody()->write('Internal server error');
            return $response->withStatus(500);
        }
    }

	public static function scenariosEndpoint(ServerRequest $request, Response $response): Response
    {
        try {
            $scenarios = ConfigUtil::getScenarios();
            $scenarioDtos = [];

            foreach ($scenarios as $s) {
                $scenarioDtos[] = [
                    'appSite' => $s->appSite,
                    'appFile' => $s->appFile,
                    'appView' => $s->appView,
                    'displayName' => $s->displayName,
                    'description' => $s->description
                ];
            }

            $response->getBody()->write(json_encode($scenarioDtos));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error getting scenarios: ' . $error->getMessage());
            $response->getBody()->write(json_encode(['error' => 'Error loading scenarios: ' . $error->getMessage()]));
            return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
        }
    }

    public static function mergeEndpoint(ServerRequest $request, Response $response): Response
    {
        // Enable logging for merge operations
        $originalLogLevel = Logger::getLogLevel();

        $projectRootPath = dirname(__DIR__, 1);
        $templateAnalysisDir = $projectRootPath . DIRECTORY_SEPARATOR . 'template_analysis';
        $logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';
        if (!is_dir($logsDir)) {
            mkdir($logsDir, 0755, true);
        }

        $contextLogFiles = [
            'LoaderNormal' => $logsDir . DIRECTORY_SEPARATOR . 'php_loadernormal.log',
            'LoaderPreProcess' => $logsDir . DIRECTORY_SEPARATOR . 'php_loaderpreprocess.log',
            'EngineNormal' => $logsDir . DIRECTORY_SEPARATOR . 'php_enginenormal.log',
            'EnginePreProcess' => $logsDir . DIRECTORY_SEPARATOR . 'php_enginepreprocess.log'
        ];

        Logger::configure(Logger::DEBUG, null, false);
        Logger::configureContextLogFiles($contextLogFiles);

        $serverStart = microtime(true) * 1000;

        try {
            $body = $request->getBody()->getContents();
            $data = json_decode($body, true);

            if (json_last_error() !== JSON_ERROR_NONE) {
                Logger::setLogLevel($originalLogLevel);
                $response->getBody()->write(json_encode(['error' => 'Invalid JSON']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            $appSite = $data['appSite'] ?? null;
            $appView = $data['appView'] ?? null;
            $engineType = $data['engineType'] ?? null;

            if (empty($appSite) || empty($engineType)) {
                Logger::setLogLevel($originalLogLevel);
                $response->getBody()->write(json_encode(['error' => 'Missing required fields: appSite, engineType']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            // Get AppFile from scenarios
            $scenarios = ConfigUtil::getScenarios();
            $appViewValue = $appView ?? '';
            $matchingScenario = null;

            foreach ($scenarios as $s) {
                if (strcasecmp($s->appSite, $appSite) === 0 && strcasecmp($s->appView, $appViewValue) === 0) {
                    $matchingScenario = $s;
                    break;
                }
            }

            if ($matchingScenario === null) {
                Logger::setLogLevel($originalLogLevel);
                $response->getBody()->write(json_encode(['error' => "No matching scenario found for AppSite='$appSite' and AppView='$appViewValue'"]));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            $appFile = $matchingScenario->appFile;

            // Calculate appViewPrefix from appFile when appView is not empty
            $appViewPrefix = (!empty($appView)) ? $appFile : '';

            $logMsg = sprintf(
                "/merge endpoint called with: app_site=%s, app_file=%s, engine_type=%s, app_view=%s, app_view_prefix=%s",
                $appSite ?? 'null',
                $appFile ?? 'null',
                $engineType ?? 'null',
                $appView ?? 'null',
                $appViewPrefix ?? 'null'
            );
            error_log($logMsg);
            Logger::info($logMsg, 'MergeEndpoint');

            $rootDirPath = __DIR__ . DIRECTORY_SEPARATOR . 'wwwroot';

            if (!SecurityValidator::isValidEngineType($engineType)) {
                Logger::setLogLevel($originalLogLevel);
                $response->getBody()->write(json_encode(['error' => 'Invalid EngineType value']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            $validAppSites = [];
            try {
                $validAppSites = SecurityValidator::getValidAppSites($rootDirPath);
            } catch (Exception $error) {
                Logger::setLogLevel($originalLogLevel);
                $response->getBody()->write(json_encode(['error' => 'Failed to load AppSites: ' . $error->getMessage()]));
                return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
            }

            if (!SecurityValidator::isValidAppSite($appSite, $validAppSites)) {
                Logger::setLogLevel($originalLogLevel);
                $response->getBody()->write(json_encode(['error' => 'Invalid AppSite value']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if (!SecurityValidator::isValidPathComponent($appSite)) {
                Logger::setLogLevel($originalLogLevel);
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in AppSite']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if (!SecurityValidator::isValidPathComponent($appFile)) {
                Logger::setLogLevel($originalLogLevel);
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in AppFile']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if ($appView !== null && $appView !== '' && !SecurityValidator::isValidPathComponent($appView)) {
                Logger::setLogLevel($originalLogLevel);
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in AppView']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            LoaderNormal::clearCache();
            LoaderPreProcess::clearCache();

            $templatesMap = LoaderNormal::loadGetTemplateFiles($rootDirPath, $appSite);
            $preTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($rootDirPath, $appSite);

            $apiResponse = new ApiResponse();
            $apiResponse->appSite = $appSite;
            $apiResponse->appFile = $appFile;
            $apiResponse->appView = $appView;

            foreach ($templatesMap as $key => $value) {
                $templateData = new TemplateData();
                $templateData->html = $value->html ?? '';
                $templateData->json = $value->json ?? null;
                $apiResponse->templates[$key] = $templateData;
            }

            foreach ($preTemplates->templates as $key => $value) {
                $metadata = new PreProcessTemplateMetadata();
                $metadata->originalContent = $value->originalContent;
                $metadata->placeholders = $value->placeholders;
                $metadata->slottedTemplates = $value->slottedTemplates;
                $metadata->jsonData = $value->jsonData;
                $metadata->jsonPlaceholders = $value->jsonPlaceholders;
                $metadata->replacementMappings = $value->replacementMappings;
                $metadata->hasPlaceholders = $value->hasPlaceholders();
                $metadata->hasSlottedTemplates = $value->hasSlottedTemplates();
                $metadata->hasJsonData = $value->hasJsonData();
                $metadata->hasJsonPlaceholders = $value->hasJsonPlaceholders();
                $metadata->hasReplacementMappings = $value->hasReplacementMappings();
                $metadata->requiresProcessing = $value->requiresProcessing();
                $apiResponse->preProcessTemplates[$key] = $metadata;
            }

            $engineStart = microtime(true) * 1000;

            $mergedHtml = '';
            if (strcasecmp($engineType, 'PreProcess') === 0) {
                $engine = new EnginePreProcess();
                if (!empty($appViewPrefix)) {
                    $engine->setAppViewPrefix($appViewPrefix);
                }
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, $appView, $preTemplates->templates);
                $apiResponse->templates = [];
            } else {
                $engine = new EngineNormal();
                if (!empty($appViewPrefix)) {
                    $engine->setAppViewPrefix($appViewPrefix);
                }
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, $appView, $templatesMap);
                $apiResponse->preProcessTemplates = [];
            }

            $apiResponse->engineTimeMs = (microtime(true) * 1000) - $engineStart;
            $apiResponse->serverTimeMs = (microtime(true) * 1000) - $serverStart;
            $apiResponse->html = $mergedHtml;

            // Save HTML output only if save query parameter is present
            $queryParams = $request->getQueryParams();
            $saveParam = $queryParams['save'] ?? '';
            if (strcasecmp($saveParam, 'true') === 0) {
                $outputDir = $projectRootPath . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'output';
                if (!is_dir($outputDir)) {
                    mkdir($outputDir, 0755, true);
                }

                $appViewSuffix = (!empty($appView)) ? "_{$appView}" : '';
                $engineSuffix = strtolower($engineType);
                $outputFile = $outputDir . DIRECTORY_SEPARATOR . "{$appSite}{$appViewSuffix}_{$engineSuffix}.html";
                file_put_contents($outputFile, $mergedHtml);
            }

            // Restore original log level
            Logger::setLogLevel($originalLogLevel);

            $response->getBody()->write($apiResponse->serializeToJson());
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            Logger::setLogLevel($originalLogLevel);
            error_log('Error in merge endpoint: ' . $error->getMessage());
            $response->getBody()->write(json_encode(['error' => 'Internal server error']));
            return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
        }
    }

	public static function getTemplatesEndpoint(ServerRequest $request, Response $response): Response
    {
        try {
            $rootDirPath = __DIR__ . DIRECTORY_SEPARATOR . 'wwwroot';
            $projectDirectory = dirname(__DIR__, 1);

            // Read request body
            $requestBody = (string)$request->getBody();
            error_log('[/api/templates] Raw request body: ' . $requestBody);
            
            $body = json_decode($requestBody, true);
            if (json_last_error() !== JSON_ERROR_NONE) {
                error_log('[/api/templates] JSON decode error: ' . json_last_error_msg());
                $response->getBody()->write('Invalid JSON format');
                return $response->withStatus(400);
            }
            
            error_log('[/api/templates] Decoded body: ' . print_r($body, true));
            $appSite = $body['appsite'] ?? '';
            error_log('[/api/templates] Extracted appSite: ' . $appSite);

            if (empty($appSite)) {
                error_log('[/api/templates] Missing appsite parameter');
                $response->getBody()->write('Missing appsite parameter');
                return $response->withStatus(400);
            }

            // Validate AppSite against allowlist loaded from appsites.csv
            $validAppSites = SecurityValidator::getValidAppSites($rootDirPath);
            error_log('[/api/templates] Valid AppSites: ' . print_r($validAppSites, true));
            // Case-insensitive comparison to match C# behavior (using array_map to lowercase for comparison)
            $validAppSitesLower = array_map('strtolower', $validAppSites);
            if (!in_array(strtolower($appSite), $validAppSitesLower, true)) {
                error_log('[/api/templates] AppSite validation failed for: ' . $appSite);
                $response->getBody()->write('Invalid AppSite value');
                return $response->withStatus(400);
            }
            error_log('[/api/templates] AppSite validation passed');

            // Validate path components for path traversal attacks
            if (!SecurityValidator::isValidPathComponent($appSite)) {
                $response->getBody()->write('Invalid characters in AppSite');
                return $response->withStatus(400);
            }

            $serverStart = microtime(true) * 1000;

            // Load Normal templates
            $normalTemplates = LoaderNormal::loadGetTemplateFiles($rootDirPath, $appSite);

            // Load PreProcess templates
            $preprocessTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($rootDirPath, $appSite);

            // Convert Normal templates to TemplateData objects for proper JSON serialization
            $normalResult = [];
            foreach ($normalTemplates as $key => $value) {
                $templateData = new TemplateData();
                $templateData->html = $value->html ?? '';
                $templateData->json = $value->json ?? null;
                $normalResult[$key] = $templateData;
            }

            // Convert PreProcess templates to metadata-only objects
            $preprocessResult = [];
            foreach ($preprocessTemplates->templates as $key => $value) {
                $metadata = new PreProcessTemplateMetadata();
                $metadata->originalContent = $value->originalContent;
                $metadata->placeholders = $value->placeholders;
                $metadata->slottedTemplates = $value->slottedTemplates;
                $metadata->jsonData = $value->jsonData;
                $metadata->jsonPlaceholders = $value->jsonPlaceholders;
                $metadata->replacementMappings = $value->replacementMappings;
                $metadata->hasPlaceholders = $value->hasPlaceholders();
                $metadata->hasSlottedTemplates = $value->hasSlottedTemplates();
                $metadata->hasJsonData = $value->hasJsonData();
                $metadata->hasJsonPlaceholders = $value->hasJsonPlaceholders();
                $metadata->hasReplacementMappings = $value->hasReplacementMappings();
                $metadata->requiresProcessing = $value->requiresProcessing();
                $preprocessResult[$key] = $metadata;
            }

            $serverEnd = microtime(true) * 1000;
            $serverTimeMs = $serverEnd - $serverStart;

            // Use proper ApiResponse structure
            $apiResponse = new ApiResponse();
            $apiResponse->templates = $normalResult;
            $apiResponse->preProcessTemplates = $preprocessResult;
            $apiResponse->appSite = $appSite;
            $apiResponse->appFile = null;
            $apiResponse->appView = null;
            $apiResponse->serverTimeMs = $serverTimeMs;

            $jsonResult = $apiResponse->serializeToJson(false);

            // Check if save query parameter is present
            $queryParams = $request->getQueryParams();
            $saveParam = $queryParams['save'] ?? '';
            if (strcasecmp($saveParam, 'true') === 0) {
                $templatesDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'templates';
                if (!is_dir($templatesDir)) {
                    mkdir($templatesDir, 0755, true);
                }

                $saveFile = $templatesDir . DIRECTORY_SEPARATOR . "php_{$appSite}_templates.json";
                file_put_contents($saveFile, $jsonResult);

                // Save structure dump using TestingUtils logic
                // Build a scenario list for the requested appSite
                $scenarios = [(object)['appSite' => $appSite, 'appFile' => '', 'appView' => '']];
                TestingUtils::dumpPreprocessedTemplateStructures($rootDirPath, $projectDirectory, $scenarios, true);
            }

            $response->getBody()->write($jsonResult);
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in /api/templates: ' . $error->getMessage());
            $response->getBody()->write('Error: ' . $error->getMessage());
            return $response->withStatus(500);
        }
    }

}

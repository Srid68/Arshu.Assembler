<?php

use Psr\Http\Message\ResponseInterface as Response;
use Psr\Http\Message\ServerRequestInterface as ServerRequest;
use Assembler\Engine\EngineNormal;
use Assembler\Engine\EngineNormalJson;
use Assembler\Engine\EnginePreProcess;
use Assembler\Engine\EnginePreProcessJson;
use Assembler\Loader\LoaderNormal;
use Assembler\Loader\LoaderNormalJson;
use Assembler\Loader\LoaderPreProcess;
use Assembler\Loader\LoaderPreProcessJson;
use Assembler\Api\ApiResponse;
use Assembler\Api\TemplateData;
use Assembler\Api\PreProcessTemplateMetadata;
use Arshu\Common\Logger;
use Assembler\Config\ConfigUtil;

class AssemblerEndpoint
{
    const DEFAULT_APP_SITE = 'Main';
    const DEFAULT_ENGINE_TYPE = 'Normal';
    const SEARCH_APP_SITES = "Main, Language";

    private const PARAM_MAX_LENGTH = 256;
    private static array $validEngineTypes = ['normal', 'preprocess', 'normaljson', 'preprocessjson'];
    private static ?array $cachedValidAppSites = null;

    private static function getValidAppSites(): array
    {
        if (self::$cachedValidAppSites !== null) {
            return self::$cachedValidAppSites;
        }

        self::$cachedValidAppSites = ConfigUtil::getAppSites();
        return self::$cachedValidAppSites;
    }

    private static function isValidPathComponent(?string $value): bool
    {
        if ($value === null || !is_string($value)) {
            return false;
        }

        $v = trim($value);
        if ($v === '') {
            return false;
        }

        if (strlen($v) > self::PARAM_MAX_LENGTH) {
            return false;
        }

        if (str_contains($v, '..') || str_contains($v, '/') || str_contains($v, '\\')) {
            return false;
        }

        $invalidChars = ['<', '>', ':', '"', '|', '?', '*', "\0"];
        for ($i = 0; $i < strlen($v); $i++) {
            $char = $v[$i];
            if (in_array($char, $invalidChars, true)) {
                return false;
            }
            if (ord($char) < 32) {
                return false;
            }
        }

        return true;
    }

    private static function isValidEngineType(?string $engineType): bool
    {
        if ($engineType === null || !is_string($engineType)) {
            return false;
        }
        return in_array(strtolower($engineType), self::$validEngineTypes, true);
    }

    private static function isValidAppSite(?string $appSite, array $validAppSites): bool
    {
        if ($appSite === null || !is_string($appSite)) {
            return false;
        }
        // Case-insensitive comparison to match C# behavior
        $validAppSitesLower = array_map('strtolower', $validAppSites);
        return in_array(strtolower($appSite), $validAppSitesLower, true);
    }

    public static function indexEndpoint(ServerRequest $request, Response $response, string $projectDirectory): Response
    {
        try {
            // Get appsite from query parameter or use default
            $rootDirPath = $projectDirectory . DIRECTORY_SEPARATOR . 'wwwroot';

            $appSite = self::DEFAULT_APP_SITE;
            $appFile = 'index';

            $queryParams = $request->getQueryParams();
            $requestedAppSite = $queryParams['appsite'] ?? null;

            // If appsite query param is provided, validate it exists in scenarios
            if ($requestedAppSite !== null && $requestedAppSite !== '') {
                // Validate AppSite against allowlist
                $validAppSites = self::getValidAppSites();
                $isValid = false;
                foreach ($validAppSites as $valid) {
                    if (strcasecmp($valid, $requestedAppSite) === 0) {
                        $isValid = true;
                        break;
                    }
                }
                if (!$isValid) {
                    $response->getBody()->write('Invalid AppSite value');
                    return $response->withStatus(400);
                }

                // Validate path components for path traversal attacks
                if (!self::isValidPathComponent($requestedAppSite)) {
                    $response->getBody()->write('Invalid characters in AppSite');
                    return $response->withStatus(400);
                }

                // Get AppFile from scenarios
                $scenarios = ConfigUtil::getScenarios();
                $matchingScenario = null;
                foreach ($scenarios as $s) {
                    if (strcasecmp($s->appSite, $requestedAppSite) === 0 && empty($s->appView)) {
                        $matchingScenario = $s;
                        break;
                    }
                }

                if ($matchingScenario === null) {
                    $response->getBody()->write("No matching scenario found for AppSite='$requestedAppSite' without AppView");
                    return $response->withStatus(400);
                }

                $appSite = $matchingScenario->appSite;
                $appFile = $matchingScenario->appFile;
            }

            $engineType = $queryParams['engine'] ?? self::DEFAULT_ENGINE_TYPE;

            if (!self::isValidEngineType($engineType)) {
                $response->getBody()->write('Invalid engine type. Use \'Normal\', \'PreProcess\', \'NormalJson\', or \'PreProcessJson\'');
                return $response->withStatus(400);
            }

            // Merge using selected engine (no AppView context)
            $mergedHtml = '';
            if (strcasecmp($engineType, 'PreProcessJson') === 0) {
                $loader = new LoaderPreProcessJson($rootDirPath, $appSite, self::SEARCH_APP_SITES);
                $engine = new EnginePreProcessJson();
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, null, $loader);
            } elseif (strcasecmp($engineType, 'PreProcess') === 0) {
                $loader = new LoaderPreProcess($rootDirPath, $appSite, self::SEARCH_APP_SITES);
                $engine = new EnginePreProcess();
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, null, $loader);
            } elseif (strcasecmp($engineType, 'NormalJson') === 0) {
                $loader = new LoaderNormalJson($rootDirPath, $appSite, self::SEARCH_APP_SITES);
                $engine = new EngineNormalJson();
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, null, $loader);
            } else {
                $loader = new LoaderNormal($rootDirPath, $appSite, self::SEARCH_APP_SITES);
                $engine = new EngineNormal();
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, null, $loader);
            }

            $response->getBody()->write($mergedHtml);
            return $response->withHeader('Content-Type', 'text/html');
        } catch (Exception $error) {
            error_log('Error in root endpoint: ' . $error->getMessage());
            $response->getBody()->write('Internal server error');
            return $response->withStatus(500);
        }
    }

	public static function navigationEndpoint(ServerRequest $request, Response $response, string $projectRootPath, string $appSite, ?string $appView = null): Response
    {
        try {
            $appView = $appView ?? '';

            // Validate AppSite against allowlist
            $validAppSites = ConfigUtil::getAppSites();
            if (!in_array(strtolower($appSite), array_map('strtolower', $validAppSites))) {
                $response->getBody()->write('Invalid AppSite value');
                return $response->withStatus(400);
            }

            // Validate path components for path traversal attacks
            if (!SecurityValidator::isValidPathComponent($appSite)) {
                $response->getBody()->write('Invalid characters in AppSite');
                return $response->withStatus(400);
            }

            if (!empty($appView) && !SecurityValidator::isValidPathComponent($appView)) {
                $response->getBody()->write('Invalid characters in AppView');
                return $response->withStatus(400);
            }

            // Get AppFile from scenarios
            $scenarios = ConfigUtil::getScenarios();
            $matchingScenario = null;
            foreach ($scenarios as $s) {
                if (strcasecmp($s->appSite, $appSite) === 0 && strcasecmp($s->appView, $appView) === 0) {
                    $matchingScenario = $s;
                    break;
                }
            }

            if ($matchingScenario === null) {
                $response->getBody()->write("No matching scenario found for AppSite='$appSite' and AppView='$appView'");
                return $response->withStatus(400);
            }

            $appFile = $matchingScenario->appFile;

            // Get engine type from query parameter (default to DEFAULT_ENGINE_TYPE)
            $queryParams = $request->getQueryParams();
            $engineType = $queryParams['engine'] ?? self::DEFAULT_ENGINE_TYPE;

            // Validate EngineType against allowlist
            if (!self::isValidEngineType($engineType)) {
                $response->getBody()->write("Invalid engine type. Use 'Normal', 'PreProcess', 'NormalJson', or 'PreProcessJson'");
                return $response->withStatus(400);
            }

            // Get wwwroot path
            $wwwrootPath = $projectRootPath . DIRECTORY_SEPARATOR . 'wwwroot';

            // Merge using selected engine
            $mergedHtml = '';
            if (strcasecmp($engineType, 'PreProcessJson') === 0) {
                $loader = new LoaderPreProcessJson($wwwrootPath, $appSite, self::SEARCH_APP_SITES);
                $engine = new EnginePreProcessJson();
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, $appView, $loader);
            } elseif (strcasecmp($engineType, 'PreProcess') === 0) {
                $loader = new LoaderPreProcess($wwwrootPath, $appSite, self::SEARCH_APP_SITES);
                $engine = new EnginePreProcess();
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, $appView, $loader);
            } elseif (strcasecmp($engineType, 'NormalJson') === 0) {
                $loader = new LoaderNormalJson($wwwrootPath, $appSite, self::SEARCH_APP_SITES);
                $engine = new EngineNormalJson();
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, $appView, $loader);
            } else {
                $loader = new LoaderNormal($wwwrootPath, $appSite, self::SEARCH_APP_SITES);
                $engine = new EngineNormal();
                $mergedHtml = $engine->mergeTemplates($appSite, $appFile, $appView, $loader);
            }

            $response->getBody()->write($mergedHtml);
            return $response->withHeader('Content-Type', 'text/html');
        } catch (Exception $error) {
            error_log('Error in navigation endpoint: ' . $error->getMessage());
            $response->getBody()->write('Internal server error');
            return $response->withStatus(500);
        }
    }

    public static function mergeEndpoint(ServerRequest $request, Response $response, string $projectRootPath): Response
    {
        // Enable logging for merge operations
        $templateAnalysisDir = $projectRootPath . DIRECTORY_SEPARATOR . 'Analysis';
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

        Logger::addContextLogFiles($contextLogFiles);

        $serverStart = microtime(true) * 1000;

        try {
            $body = $request->getBody()->getContents();
            $data = json_decode($body, true);

            if (json_last_error() !== JSON_ERROR_NONE) {
                $response->getBody()->write(json_encode(['error' => 'Invalid JSON']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            $appSite = $data['appSite'] ?? null;
            $appView = $data['appView'] ?? null;
            $engineType = $data['engineType'] ?? null;

            if (empty($appSite) || empty($engineType)) {
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

            $rootDirPath = $projectRootPath . DIRECTORY_SEPARATOR . 'wwwroot';

            if (!self::isValidEngineType($engineType)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid EngineType value']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            $validAppSites = [];
            try {
                $validAppSites = self::getValidAppSites();
            } catch (Exception $error) {
                $response->getBody()->write(json_encode(['error' => 'Failed to load AppSites: ' . $error->getMessage()]));
                return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
            }

            if (!self::isValidAppSite($appSite, $validAppSites)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid AppSite value']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if (!self::isValidPathComponent($appSite)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in AppSite']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if (!self::isValidPathComponent($appFile)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in AppFile']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if ($appView !== null && $appView !== '' && !self::isValidPathComponent($appView)) {
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

            $response->getBody()->write($apiResponse->serializeToJson());
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in merge endpoint: ' . $error->getMessage());
            $response->getBody()->write(json_encode(['error' => 'Internal server error']));
            return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
        }
    }

	public static function getTemplatesEndpoint(ServerRequest $request, Response $response, string $projectDirectory): Response
    {
        // Enable logging for template operations
        $templateAnalysisDir = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis';
        $logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';
        if (!is_dir($logsDir)) {
            mkdir($logsDir, 0755, true);
        }

        $contextLogFiles = [
            'LoaderNormalJson' => $logsDir . DIRECTORY_SEPARATOR . 'php_loadernormaljson.log',
            'LoaderPreProcessJson' => $logsDir . DIRECTORY_SEPARATOR . 'php_loaderpreprocessjson.log'
        ];

        Logger::addContextLogFiles($contextLogFiles);

        try {
            $rootDirPath = $projectDirectory . DIRECTORY_SEPARATOR . 'wwwroot';

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
            $validAppSites = self::getValidAppSites();
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
            if (!self::isValidPathComponent($appSite)) {
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

            $response->getBody()->write($jsonResult);
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in /api/templates: ' . $error->getMessage());
            $response->getBody()->write('Error: ' . $error->getMessage());
            return $response->withStatus(500);
        }
    }

    /**
     * Maps all assembler endpoints to the Slim app
     * Usage: AssemblerEndpoint::mapAssemblerEndpoints($app, $projectDirectory)
     */
    public static function mapAssemblerEndpoints($app, string $projectDirectory)
    {
        $app->get('/', function (ServerRequest $request, Response $response) use ($projectDirectory) {
            return AssemblerEndpoint::indexEndpoint($request, $response, $projectDirectory);
        });

        $app->get('/{appSite}', function (ServerRequest $request, Response $response, array $args) use ($projectDirectory) {
            return AssemblerEndpoint::navigationEndpoint($request, $response, $projectDirectory, $args['appSite']);
        });

        $app->get('/{appSite}/{appView}', function (ServerRequest $request, Response $response, array $args) use ($projectDirectory) {
            return AssemblerEndpoint::navigationEndpoint($request, $response, $projectDirectory, $args['appSite'], $args['appView']);
        });

        $app->post('/merge', function (ServerRequest $request, Response $response) use ($projectDirectory) {
            return AssemblerEndpoint::mergeEndpoint($request, $response, $projectDirectory);
        });

        $app->post('/api/templates', function (ServerRequest $request, Response $response) use ($projectDirectory) {
            return AssemblerEndpoint::getTemplatesEndpoint($request, $response, $projectDirectory);
        });
    }

}

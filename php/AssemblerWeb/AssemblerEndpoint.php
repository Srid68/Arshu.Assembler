<?php

require_once __DIR__ . '/SecurityValidator.php';
require_once __DIR__ . '/AppSitesConfig.php';

use Psr\Http\Message\ResponseInterface as Response;
use Psr\Http\Message\ServerRequestInterface as ServerRequest;
use Assembler\TemplateCommon\TemplateUtils;
use Assembler\TemplateEngine\EngineNormal;
use Assembler\TemplateEngine\EnginePreProcess;
use Assembler\TemplateLoader\LoaderNormal;
use Assembler\TemplateLoader\LoaderPreProcess;
use Assembler\TemplateApi\ApiResponse;
use Assembler\TemplateApi\TemplateData;
use Assembler\TemplateApi\PreProcessTemplateMetadata;
use Assembler\TemplateCommon\Logger;

class AssemblerEndpoint
{
    public static function indexEndpoint(ServerRequest $request, Response $response): Response
    {
        try {
            $paths = TemplateUtils::getAssemblerWebDirPath();
            $rootDirPath = $paths['assemblerWebDirPath'];

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

    public static function mergeEndpoint(ServerRequest $request, Response $response): Response
    {
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
            $appViewPrefix = $data['appViewPrefix'] ?? null;
            $appFile = $data['appFile'] ?? null;
            $engineType = $data['engineType'] ?? null;

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

            if (empty($appSite) || empty($appFile) || empty($engineType)) {
                $response->getBody()->write(json_encode(['error' => 'Missing required fields: appSite, appFile, engineType']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            $paths = TemplateUtils::getAssemblerWebDirPath();
            $rootDirPath = $paths['assemblerWebDirPath'];

            if (!SecurityValidator::isValidEngineType($engineType)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid EngineType value']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            $validAppSites = [];
            try {
                $validAppSites = SecurityValidator::getValidAppSites($rootDirPath);
            } catch (Exception $error) {
                $response->getBody()->write(json_encode(['error' => 'Failed to load AppSites: ' . $error->getMessage()]));
                return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
            }

            if (!SecurityValidator::isValidAppSite($appSite, $validAppSites)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid AppSite value']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if (!SecurityValidator::isValidPathComponent($appSite)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in AppSite']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if (!SecurityValidator::isValidPathComponent($appFile)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in AppFile']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if ($appView !== null && $appView !== '' && !SecurityValidator::isValidPathComponent($appView)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in AppView']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            if ($appViewPrefix !== null && $appViewPrefix !== '' && !SecurityValidator::isValidPathComponent($appViewPrefix)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in AppViewPrefix']));
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
}

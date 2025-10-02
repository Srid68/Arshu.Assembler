<?php

error_reporting(E_ALL);
ini_set('display_errors', 1);

require_once __DIR__ . '/../Assembler/vendor/autoload.php';
require_once __DIR__ . '/vendor/autoload.php';
require_once __DIR__ . '/../Assembler/src/TemplateModel/ModelPreProcess.php';
require_once __DIR__ . '/MergeRequest.php';
require_once __DIR__ . '/IdleTrackingMiddleware.php';

use Psr\Http\Message\ResponseInterface as Response;
use Psr\Http\Message\ServerRequestInterface as ServerRequest;
use Slim\Factory\AppFactory;
use Assembler\TemplateCommon\TemplateUtils;
use Assembler\TemplateEngine\EngineNormal;
use Assembler\TemplateEngine\EnginePreProcess;
use Assembler\TemplateLoader\LoaderNormal;
use Assembler\TemplateLoader\LoaderPreProcess;


// ...existing code...

// Create App
$app = AppFactory::create();

// Add middleware
$app->addRoutingMiddleware();
$app->addErrorMiddleware(true, true, true);

// Configure and add idle tracking middleware
$idleSeconds = getenv('IDLE_SHUTDOWN_SECONDS') ?: 10;
$idleSeconds = is_numeric($idleSeconds) ? (int)$idleSeconds : 10;
IdleTrackingMiddleware::configure($idleSeconds);

// Only enable in non-debug mode (check if we're not in development)
$isDebug = getenv('APP_ENV') === 'development' || getenv('APP_DEBUG') === 'true';
if (!$isDebug) {
    $app->add(new IdleTrackingMiddleware());
 }

// ...existing code...

// GET / - Root endpoint
$app->get('/', function (ServerRequest $request, Response $response) {
    $paths = TemplateUtils::getAssemblerWebDirPath();
    $appSitesPath = $paths['assemblerWebDirPath'] . DIRECTORY_SEPARATOR . 'AppSites';
    // Get wwwroot/AppSites path
    $paths = TemplateUtils::getAssemblerWebDirPath();
    $appSitesPath = $paths['assemblerWebDirPath'] . DIRECTORY_SEPARATOR . 'AppSites';

    $optionsList = [];

    if (is_dir($appSitesPath)) {
        $testDirs = array_diff(scandir($appSitesPath), ['.', '..']);
        foreach ($testDirs as $testDir) {
            if ($testDir === 'roottemplate.html') continue;
            $testDirPath = $appSitesPath . DIRECTORY_SEPARATOR . $testDir;

            if (!is_dir($testDirPath)) continue;

            // Find root html files
            $htmlFiles = TemplateUtils::getHtmlFiles($testDirPath);

            // Views subdir
            $viewsPath = $testDirPath . DIRECTORY_SEPARATOR . 'Views';
            $hasViews = is_dir($viewsPath);

            foreach ($htmlFiles as $htmlFile) {
                $appViewPrefix = substr($htmlFile, 0, min(6, strlen($htmlFile)));
                $optionValue = $testDir . ',' . $htmlFile . ',,' . $appViewPrefix;
                $optionText = $testDir . ' - ' . $htmlFile;
                $optionsList[] = '<option value="' . $optionValue . '">' . $optionText . '</option>';
            }

            if ($hasViews) {
                $viewFiles = TemplateUtils::getHtmlFiles($viewsPath);

                $appViewValues = [];
                foreach ($viewFiles as $vf) {
                    $idx = stripos($vf, 'content');
                    if ($idx > 0) {
                        $viewPart = substr($vf, 0, $idx);
                        if (!empty($viewPart)) {
                            $appViewValues[] = ucfirst($viewPart);
                        }
                    }
                }
                $appViewValues = array_unique($appViewValues);

                foreach ($htmlFiles as $rootFile) {
                    $rootAppViewPrefix = substr($rootFile, 0, min(6, strlen($rootFile)));

                    $matchingViewPrefix = null;
                    foreach ($viewFiles as $vf) {
                        $idx = stripos($vf, 'content');
                        if ($idx > 0) {
                            $prefix = substr($vf, 0, $idx);
                            if (!empty($prefix) && stripos($rootFile, $prefix) === 0) {
                                $matchingViewPrefix = $prefix;
                                break;
                            }
                        }
                    }

                    if ($matchingViewPrefix !== null) {
                        foreach ($appViewValues as $appView) {
                            $optionValueAppView = $testDir . ',' . $rootFile . ',' . $appView . ',' . $rootAppViewPrefix;
                            $optionTextAppView = $testDir . ' - ' . $rootFile . ' (AppView: ' . $appView . ')';
                            $optionsList[] = '<option value="' . $optionValueAppView . '">' . $optionTextAppView . '</option>';
                        }
                    }
                }
            }
        }
    }

    $options = implode("\n        ", $optionsList);
    $templatePath = $appSitesPath . DIRECTORY_SEPARATOR . 'roottemplate.html';
        if (!file_exists($templatePath)) {
            $html = '<html><body>Template not found</body></html>';
        } else {
            $html = file_get_contents($templatePath);
        }
        $html = str_replace('<!--OPTIONS-->', $options, $html);

    $response->getBody()->write($html);
    return $response->withHeader('Content-Type', 'text/html');
})->setName('GetRootUrl');

// POST /merge - Merge templates
$app->post('/merge', function (ServerRequest $request, Response $response) {
    $serverStart = microtime(true) * 1000;
    $body = $request->getBody()->getContents();
    error_log("[DEBUG] Raw body: $body");
    $data = json_decode($body, true);
    error_log("[DEBUG] Decoded data: " . json_encode($data));
    $serverStart = microtime(true) * 1000;

    $body = $request->getBody()->getContents();
    $data = json_decode($body, true);

    if (json_last_error() !== JSON_ERROR_NONE) {
        $response->getBody()->write(json_encode(['error' => 'Invalid JSON']));
        return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
    }

    $mergeRequest = new MergeRequest(
        $data['appSite'] ?? null,
        $data['appView'] ?? null,
        $data['appViewPrefix'] ?? null,
        $data['appFile'] ?? null,
        $data['engineType'] ?? null
    );

    // Log the merge endpoint call with parameters
    error_log(sprintf(
        "/merge endpoint called with: app_site=%s, app_file=%s, engine_type=%s, app_view=%s, app_view_prefix=%s",
        $mergeRequest->appSite ?? 'null',
        $mergeRequest->appFile ?? 'null',
        $mergeRequest->engineType ?? 'null',
        $mergeRequest->appView ?? 'null',
        $mergeRequest->appViewPrefix ?? 'null'
    ));

    // Validate required fields
    if (empty($mergeRequest->appSite) || empty($mergeRequest->appFile) || empty($mergeRequest->engineType)) {
        $response->getBody()->write(json_encode(['error' => 'Missing required fields: appSite, appFile, engineType']));
        return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
    }

    $paths = TemplateUtils::getAssemblerWebDirPath();
    $rootDirPath = $paths['assemblerWebDirPath'];
    error_log("[DEBUG] rootDirPath: " . $rootDirPath);

    $engineStart = microtime(true) * 1000;

    if (strcasecmp($mergeRequest->engineType, 'PreProcess') === 0) {
        $templates = LoaderPreProcess::loadProcessGetTemplateFiles($rootDirPath, $mergeRequest->appSite);
        $engine = new EnginePreProcess();
        if (!empty($mergeRequest->appViewPrefix)) {
            $engine->setAppViewPrefix($mergeRequest->appViewPrefix);
        }
        $mergedHtml = $engine->mergeTemplates($mergeRequest->appSite, $mergeRequest->appFile, $mergeRequest->appView, $templates->templates);
    } else {
        $templates = LoaderNormal::loadGetTemplateFiles($rootDirPath, $mergeRequest->appSite);
        $engine = new EngineNormal();
        if (!empty($mergeRequest->appViewPrefix)) {
            $engine->setAppViewPrefix($mergeRequest->appViewPrefix);
        }
        $mergedHtml = $engine->mergeTemplates($mergeRequest->appSite, $mergeRequest->appFile, $mergeRequest->appView, $templates);
    }

    $engineTimeMs = (microtime(true) * 1000) - $engineStart;
    $serverTimeMs = (microtime(true) * 1000) - $serverStart;

    $responseData = [
        'html' => $mergedHtml,
        'timing' => [
            'serverTimeMs' => $serverTimeMs,
            'engineTimeMs' => $engineTimeMs
        ]
    ];

    $response->getBody()->write(json_encode($responseData));
    return $response->withHeader('Content-Type', 'application/json');
})->setName('PostMergeTemplate');

// Serve Scalar UI index.html at /scalar
// Redirect /scalar to /scalar/index.html for proper UI loading
$app->get('/scalar', function (ServerRequest $request, Response $response) {
    return $response
        ->withHeader('Location', '/scalar/index.html')
        ->withStatus(302);
});

// Serve static files from wwwroot/scalar
$app->get('/scalar/{file}', function (ServerRequest $request, Response $response, array $args) {
    $file = $args['file'];
    $filePath = __DIR__ . '/wwwroot/scalar/' . $file;
    
    if (file_exists($filePath) && is_file($filePath)) {
        $content = file_get_contents($filePath);
        $contentType = match (pathinfo($file, PATHINFO_EXTENSION)) {
            'css' => 'text/css',
            'js' => 'application/javascript',
            'html' => 'text/html',
            default => 'text/plain'
        };
        
        $response->getBody()->write($content);
        return $response->withHeader('Content-Type', $contentType);
    }
    
    return $response->withStatus(404);
});

// OpenAPI JSON route
$app->get('/openapi.json', function (ServerRequest $request, Response $response) {
    $openapiSpec = [
        'openapi' => '3.0.3',
        'info' => [
            'title' => 'Arshu Api',
            'version' => '1.0.0'
        ],
        'paths' => [
            '/' => [
                'get' => [
                    'tags' => ['Assembler'],
                    'summary' => 'Root page',
                    'description' => 'Returns the root HTML page with template options.',
                    'responses' => [
                        '200' => [
                            'description' => 'Root HTML page',
                            'content' => [
                                'text/html' => [
                                    'schema' => [
                                        'type' => 'string'
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ],
            '/merge' => [
                'post' => [
                    'tags' => ['Assembler'],
                    'summary' => 'Merge templates',
                    'description' => 'Merges templates using the specified engine type',
                    'requestBody' => [
                        'required' => true,
                        'content' => [
                            'application/json' => [
                                'schema' => [
                                    '$ref' => '#/components/schemas/MergeRequest'
                                ]
                            ]
                        ]
                    ],
                    'responses' => [
                        '200' => [
                            'description' => 'Merged template output',
                            'content' => [
                                'application/json' => [
                                    'schema' => [
                                        'type' => 'object',
                                        'properties' => [
                                            'html' => ['type' => 'string'],
                                            'timing' => [
                                                'type' => 'object',
                                                'properties' => [
                                                    'serverTimeMs' => ['type' => 'number'],
                                                    'engineTimeMs' => ['type' => 'number']
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ],
        'components' => [
            'schemas' => [
                'MergeRequest' => [
                    'type' => 'object',
                    'required' => ['appSite', 'appFile', 'engineType'],
                    'properties' => [
                        'appSite' => ['type' => 'string'],
                        'appView' => ['type' => 'string'],
                        'appViewPrefix' => ['type' => 'string'],
                        'appFile' => ['type' => 'string'],
                        'engineType' => ['type' => 'string']
                    ]
                ]
            ]
        ]
    ];

    $response->getBody()->write(json_encode($openapiSpec));
    return $response->withHeader('Content-Type', 'application/json');
});

// Run app
$app->run();
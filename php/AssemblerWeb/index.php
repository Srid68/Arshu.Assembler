<?php

error_reporting(E_ALL);
ini_set('display_errors', 0);
ini_set('log_errors', 1);

require_once __DIR__ . '/../Assembler/vendor/autoload.php';
require_once __DIR__ . '/vendor/autoload.php';
require_once __DIR__ . '/../Assembler/src/Model/ModelPreProcess.php';
require_once __DIR__ . '/../Assembler/src/Api/ApiResponse.php';
require_once __DIR__ . '/MergeRequest.php';
require_once __DIR__ . '/IdleTrackingMiddleware.php';
require_once __DIR__ . '/AssemblerEndpoint.php';

use Psr\Http\Message\ResponseInterface as Response;
use Psr\Http\Message\ServerRequestInterface as ServerRequest;
use Slim\Factory\AppFactory;
use Assembler\Common\CommonUtil;
use Assembler\Engine\EngineNormal;
use Assembler\Engine\EnginePreProcess;
use Assembler\Loader\LoaderNormal;
use Assembler\Loader\LoaderPreProcess;
use Assembler\TemplateApi\ApiResponse;
use Assembler\TemplateApi\TemplateData;
use Assembler\TemplateApi\PreProcessTemplateMetadata;
use Assembler\Common\Logger;
use Assembler\Config\ConfigUtil;

// Configure logger with context-specific log files
$logRotation = Logger::ROTATION_NONE;
$paths = CommonUtil::getAssemblerWebDirPath();
$projectDirectory = $paths['projectDirectory'];
$assemblerWebDirPath = $paths['assemblerWebDirPath'];

// Load ConfigUtil with wwwroot path
ConfigUtil::load($assemblerWebDirPath);
$templateAnalysisDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis';
$logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';
if (!is_dir($logsDir)) {
    mkdir($logsDir, 0755, true);
}

// Configure separate log files for each context
$contextLogFiles = [
    'LoaderNormal' => $logsDir . DIRECTORY_SEPARATOR . 'php_loadernormal.log',
    'LoaderPreProcess' => $logsDir . DIRECTORY_SEPARATOR . 'php_loaderpreprocess.log',
    'EngineNormal' => $logsDir . DIRECTORY_SEPARATOR . 'php_enginenormal.log',
    'EnginePreProcess' => $logsDir . DIRECTORY_SEPARATOR . 'php_enginepreprocess.log',
    'Index' => $logsDir . DIRECTORY_SEPARATOR . 'php_index.log',
    'MergeEndpoint' => $logsDir . DIRECTORY_SEPARATOR . 'php_mergeendpoint.log',
    'IdleTracking' => $logsDir . DIRECTORY_SEPARATOR . 'php_idletracking.log',
];

Logger::configure(Logger::DEBUG, null, false, $logRotation);
Logger::configureContextLogFiles($contextLogFiles);
Logger::info('AssemblerWeb starting up', 'Index');

// Create App
$app = AppFactory::create();

// Store project directory in container for use in endpoints
$container = $app->getContainer();
if ($container === null) {
    $container = new \DI\Container();
    AppFactory::setContainer($container);
    $app = AppFactory::create();
}
$container->set('projectDirectory', $projectDirectory);

// Add middleware
$app->addRoutingMiddleware();
$app->addErrorMiddleware(true, true, true);

// Configure and add idle tracking middleware
// Check if running in debug mode via environment variables or CLI
$skipIdleTracking = in_array('--skipIdleTracking', $argv ?? []);
$isDebug = getenv('DEBUG') === 'true'
    || getenv('VSCODE_DEBUG') === 'true'
    || getenv('IDLE_TRACKER_DISABLED') === 'true'
    || getenv('APP_ENV') === 'development'
    || $skipIdleTracking;

// Also check if Xdebug extension is loaded (indicates development environment)
if (extension_loaded('xdebug')) {
    $isDebug = true;
}

Logger::info('Debug mode: ' . ($isDebug ? 'enabled' : 'disabled'), 'Index');

if (!$isDebug) {
    $idleSeconds = getenv('IDLE_SHUTDOWN_SECONDS') ?: 10;
    $idleSeconds = is_numeric($idleSeconds) ? (int)$idleSeconds : 10;
    IdleTrackingMiddleware::configure($idleSeconds);
    $app->add(new IdleTrackingMiddleware());
    Logger::info("Idle tracking enabled with {$idleSeconds} seconds timeout", 'Index');
} else {
    Logger::info('Idle tracking disabled in debug mode', 'Index');
}

// ...existing code...

// GET / - Root endpoint
$app->get('/', function (ServerRequest $request, Response $response) {
    return AssemblerEndpoint::indexEndpoint($request, $response);
})->setName('GetRootUrl');

// GET /api/scenarios - Get all scenarios
$app->get('/api/scenarios', function (ServerRequest $request, Response $response) {
    return AssemblerEndpoint::scenariosEndpoint($request, $response);
})->setName('GetScenarios');

// POST /merge - Merge templates
$app->post('/merge', function (ServerRequest $request, Response $response) {
    return AssemblerEndpoint::mergeEndpoint($request, $response);
})->setName('PostMergeTemplate');

// Test endpoints
$app->post('/test/standard', function (ServerRequest $request, Response $response) use ($container) {
    $projectDirectory = $container->get('projectDirectory');
    return AssemblerEndpoint::testStandardEndpoint($request, $response, $projectDirectory);
})->setName('RunStandardTests');

$app->post('/test/advanced', function (ServerRequest $request, Response $response) use ($container) {
    $projectDirectory = $container->get('projectDirectory');
    return AssemblerEndpoint::testAdvancedEndpoint($request, $response, $projectDirectory);
})->setName('RunAdvancedTests');

$app->post('/test/performance', function (ServerRequest $request, Response $response) {
    return AssemblerEndpoint::testPerformanceEndpoint($request, $response);
})->setName('RunPerformanceTests');

$app->post('/test/consolidate-performance', function (ServerRequest $request, Response $response) use ($container) {
    $projectDirectory = $container->get('projectDirectory');
    return AssemblerEndpoint::testConsolidatePerformanceEndpoint($request, $response, $projectDirectory);
})->setName('ConsolidatePerformanceTests');

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

// Serve static files from wwwroot/Resource
$app->get('/Resource/{path:.+}', function (ServerRequest $request, Response $response, array $args) {
    $path = $args['path'];
    $filePath = __DIR__ . '/wwwroot/Resource/' . $path;

    // Security check - prevent directory traversal
    $realPath = realpath($filePath);
    $basePath = realpath(__DIR__ . '/wwwroot/Resource');

    if ($realPath && $basePath && strpos($realPath, $basePath) === 0 && is_file($realPath)) {
        $content = file_get_contents($realPath);
        $contentType = match (pathinfo($path, PATHINFO_EXTENSION)) {
            'css' => 'text/css',
            'js' => 'application/javascript',
            'html' => 'text/html',
            'json' => 'application/json',
            'png' => 'image/png',
            'jpg' => 'image/jpeg',
            'jpeg' => 'image/jpeg',
            'gif' => 'image/gif',
            'svg' => 'image/svg+xml',
            'ico' => 'image/x-icon',
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

// Serve static HTML/JSON files from wwwroot root (for test summaries, performance reports, etc.)
// This must be last to avoid shadowing other routes
$app->get('/{file:(?:php|csharp|rust|go|nodejs|node|all)_.+\.(?:html|json)}', function (ServerRequest $request, Response $response, array $args) {
    $file = $args['file'];
    $filePath = __DIR__ . '/wwwroot/' . basename($file);  // basename to prevent directory traversal

    if (file_exists($filePath) && is_file($filePath)) {
        $content = file_get_contents($filePath);
        $contentType = match (pathinfo($file, PATHINFO_EXTENSION)) {
            'html' => 'text/html',
            'json' => 'application/json',
            default => 'text/plain'
        };

        $response->getBody()->write($content);
        return $response->withHeader('Content-Type', $contentType);
    }

    return $response->withStatus(404);
});

// Run app
$app->run();
<?php

error_reporting(E_ALL);
ini_set('display_errors', 0);
ini_set('log_errors', 1);

require_once __DIR__ . '/../Assembler/vendor/autoload.php';
require_once __DIR__ . '/vendor/autoload.php';
require_once __DIR__ . '/../Assembler/src/Model/ModelPreProcess.php';
require_once __DIR__ . '/../Assembler/src/Api/ApiResponse.php';
require_once __DIR__ . '/model/MergeRequest.php';
require_once __DIR__ . '/services/IdleTrackingMiddleware.php';
require_once __DIR__ . '/endpoint/assemblerEndpoint.php';
require_once __DIR__ . '/endpoint/assemblerTestEndpoint.php';

use Psr\Http\Message\ResponseInterface as Response;
use Psr\Http\Message\ServerRequestInterface as ServerRequest;
use Slim\Factory\AppFactory;
use Assembler\Common\CommonUtil;
use Assembler\Engine\EngineNormal;
use Assembler\Engine\EnginePreProcess;
use Assembler\Loader\LoaderNormal;
use Assembler\Loader\LoaderPreProcess;
use Assembler\Api\ApiResponse;
use Assembler\Api\TemplateData;
use Assembler\Api\PreProcessTemplateMetadata;
use Assembler\Common\Logger;
use Assembler\Config\ConfigUtil;

// Configure logger with context-specific log files
$logRotation = Logger::ROTATION_HOURLY;
$projectDirectory = __DIR__;
$assemblerWebDirPath = __DIR__ . DIRECTORY_SEPARATOR . 'wwwroot';

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
    'ConfigUtil' => $logsDir . DIRECTORY_SEPARATOR . 'php_configutil.log',
];

Logger::configure(Logger::DEBUG, false, $logRotation);
Logger::setLogsDirectory($logsDir);

// Clear logs only once per server start using a marker file persisted on disk
$logInitMarker = $logsDir . DIRECTORY_SEPARATOR . '.logs_initialized';
$isDebugEnv = getenv('DEBUG') === 'true'
    || getenv('VSCODE_DEBUG') === 'true'
    || getenv('APP_ENV') === 'development'
    || extension_loaded('xdebug');

$currentPid = function_exists('getmypid') ? getmypid() : null;
$markerData = null;

if (file_exists($logInitMarker)) {
    $existingMarker = @file_get_contents($logInitMarker);
    if ($existingMarker !== false) {
        $decoded = json_decode($existingMarker, true);
        if (is_array($decoded)) {
            $markerData = $decoded;
        }
    }
}

$shouldInitialiseLogs = true;
if ($markerData !== null) {
    if ($currentPid !== null && isset($markerData['pid']) && $markerData['pid'] === $currentPid) {
        $shouldInitialiseLogs = false;
    }
}

if ($shouldInitialiseLogs) {
    if ($isDebugEnv) {
        Logger::clearLogs();
    } else {
        Logger::clearOldLogs(7);
    }

    $markerPayload = [
        'pid' => $currentPid,
        'timestamp' => time(),
        'debug' => $isDebugEnv,
    ];

    @file_put_contents($logInitMarker, json_encode($markerPayload));
}

// Ensure context log files exist every request (important when marker already present)
Logger::configureContextLogFiles($contextLogFiles);

// Load ConfigUtil with wwwroot path (after Logger is configured)
ConfigUtil::load($assemblerWebDirPath);
Logger::info('AssemblerWeb starting up', 'Index');

// Create App
$app = AppFactory::create();

// Store project directory in container for use in endpoints
$container = $app->getContainer();
if ($container === null) {
    $container = new \DI\Container();
    AppFactory::setContainer($container);
    $app = AppFactory::create();
    $container = $app->getContainer();
}
/** @var \DI\Container $container */
$container->set('projectDirectory', $projectDirectory);

// Add middleware
$app->addRoutingMiddleware();
$app->addErrorMiddleware(true, true, true);

// Configure and add idle tracking middleware
// Check if running in debug mode via environment variables or CLI
$isDebug = getenv('DEBUG') === 'true'
    || getenv('VSCODE_DEBUG') === 'true'
    || getenv('APP_ENV') === 'development';

// Also check if Xdebug extension is loaded (indicates development environment)
if (extension_loaded('xdebug')) {
    $isDebug = true;
}

// Determine if idle tracking should be enabled
// Command line args and explicit env vars take precedence
$skipIdleTrackingArg = in_array('--skipIdleTracking', $argv ?? []);
if ($skipIdleTrackingArg) {
    $idleTrackingEnabled = false; // --skipIdleTracking flag explicitly disables
} else {
    $idleTrackerDisabledEnv = getenv('IDLE_TRACKER_DISABLED');
    if ($idleTrackerDisabledEnv === 'false') {
        $idleTrackingEnabled = true; // Explicitly enable idle tracking
    } elseif ($idleTrackerDisabledEnv === 'true') {
        $idleTrackingEnabled = false; // Explicitly disable idle tracking
    } else {
        $idleTrackingEnabled = !$isDebug; // Default: disable in debug mode
    }
}

Logger::info('Debug mode: ' . ($isDebug ? 'enabled' : 'disabled'), 'Index');

if ($idleTrackingEnabled) {
    $idleSeconds = getenv('IDLE_SHUTDOWN_SECONDS') ?: 10;
    $idleSeconds = is_numeric($idleSeconds) ? (int)$idleSeconds : 10;
    
    // Configure idle tracking only once per process (logging, PID file)
    static $idleTrackingConfigured = false;
    if (!$idleTrackingConfigured) {
        IdleTrackingMiddleware::configure($idleSeconds);
        Logger::info("Idle tracking enabled with {$idleSeconds} seconds timeout", 'Index');
        
        // Write server PID file so monitor knows what process to track
        $tmpDir = $projectDirectory . DIRECTORY_SEPARATOR . 'tmp';
        $pidFile = $tmpDir . DIRECTORY_SEPARATOR . 'php_assembler_server_pid.txt';
        file_put_contents($pidFile, getmypid());
        
        $idleTrackingConfigured = true;
    }
    
    // Add middleware to app on every request (since $app is recreated)
    $app->add(new IdleTrackingMiddleware());
    
    // Note: No shutdown handler needed - the IdleTrackingMonitor process handles shutdown
} else {
    Logger::info('Idle tracking disabled (debug mode or explicitly disabled)', 'Index');
}

Logger::info('Setting up routes...', 'Index');

// GET / - Root endpoint
$app->get('/', function (ServerRequest $request, Response $response) use ($projectDirectory) {
    return AssemblerEndpoint::indexEndpoint($request, $response, $projectDirectory);
})->setName('GetRootUrl');

// GET /api/scenarios - Get all scenarios
$app->get('/api/scenarios', function (ServerRequest $request, Response $response) {
    return AssemblerEndpoint::scenariosEndpoint($request, $response);
})->setName('GetScenarios');

// POST /merge - Merge templates
$app->post('/merge', function (ServerRequest $request, Response $response) use ($projectDirectory) {
    return AssemblerEndpoint::mergeEndpoint($request, $response, $projectDirectory);
})->setName('PostMergeTemplate');

// POST /api/templates - Get templates for an AppSite
$app->post('/api/templates', function (ServerRequest $request, Response $response) use ($projectDirectory) {
    return AssemblerEndpoint::getTemplatesEndpoint($request, $response, $projectDirectory);
})->setName('GetTemplates');


// Test endpoints
$app->post('/test/standard', function (ServerRequest $request, Response $response) use ($assemblerWebDirPath, $projectDirectory) {
    return AssemblerTestEndpoint::testStandardEndpoint($request, $response, $assemblerWebDirPath, $projectDirectory);
})->setName('RunStandardTests');

$app->post('/test/advanced', function (ServerRequest $request, Response $response) use ($assemblerWebDirPath, $projectDirectory) {
    return AssemblerTestEndpoint::testAdvancedEndpoint($request, $response, $assemblerWebDirPath, $projectDirectory);
})->setName('RunAdvancedTests');

$app->post('/test/performance', function (ServerRequest $request, Response $response) use ($assemblerWebDirPath, $projectDirectory) {
    return AssemblerTestEndpoint::testPerformanceEndpoint($request, $response, $assemblerWebDirPath, $projectDirectory);
})->setName('RunPerformanceTests');

$app->post('/test/consolidate-performance', function (ServerRequest $request, Response $response) use ($assemblerWebDirPath, $projectDirectory) {
    return AssemblerTestEndpoint::testConsolidatePerformanceEndpoint($request, $response, $assemblerWebDirPath, $projectDirectory);
})->setName('ConsolidatePerformanceTests');

// Report endpoint
$app->post('/api/report', function (ServerRequest $request, Response $response) use ($projectDirectory) {
    return AssemblerTestEndpoint::getReportEndpoint($request, $response, $projectDirectory);
})->setName('GetReport');

// Register /api/test-results and /api/performance-results endpoints

$app->post('/api/test-results', function (ServerRequest $request, Response $response) use ($projectDirectory) {
    return AssemblerTestEndpoint::saveTestResultsEndpoint($request, $response, $projectDirectory);
})->setName('SaveTestResults');


$app->post('/api/performance-results', function (ServerRequest $request, Response $response) use ($projectDirectory) {
    return AssemblerTestEndpoint::savePerformanceResultsEndpoint($request, $response, $projectDirectory);
})->setName('SavePerformanceResults');

// Register /api/save-log and /api/save-output endpoints

$app->post('/api/save-log', function (ServerRequest $request, Response $response) use ($projectDirectory) {
    return AssemblerTestEndpoint::saveLogEndpoint($request, $response, $projectDirectory);
})->setName('SaveLog');


$app->post('/api/save-output', function (ServerRequest $request, Response $response) use ($projectDirectory) {
    return AssemblerTestEndpoint::saveOutputEndpoint($request, $response, $projectDirectory);
})->setName('SaveOutput');

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
                    'required' => ['appSite', 'engineType'],
                    'properties' => [
                        'appSite' => ['type' => 'string'],
                        'appView' => ['type' => 'string'],
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
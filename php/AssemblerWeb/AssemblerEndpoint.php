<?php

require_once __DIR__ . '/SecurityValidator.php';
require_once __DIR__ . '/AppSitesConfig.php';

use Psr\Http\Message\ResponseInterface as Response;
use Psr\Http\Message\ServerRequestInterface as ServerRequest;
use Assembler\Common\CommonUtil;
use Assembler\Engine\EngineNormal;
use Assembler\Engine\EnginePreProcess;
use Assembler\Loader\LoaderNormal;
use Assembler\Loader\LoaderPreProcess;
use Assembler\TemplateApi\ApiResponse;
use Assembler\TemplateApi\TemplateData;
use Assembler\TemplateApi\PreProcessTemplateMetadata;
use Assembler\Common\Logger;
use Assembler\Test\TestingUtils;
use Assembler\Performance\PerformanceUtils;
use Assembler\Config\ConfigUtil;

class AssemblerEndpoint
{
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

    public static function indexEndpoint(ServerRequest $request, Response $response): Response
    {
        try {
            $paths = CommonUtil::getAssemblerWebDirPath();
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

            $paths = CommonUtil::getAssemblerWebDirPath();
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

    public static function testStandardEndpoint(ServerRequest $request, Response $response, string $projectDirectory): Response
    {
        $start = microtime(true);
        $rootDirPath = $projectDirectory . DIRECTORY_SEPARATOR . 'wwwroot';

        // Enable logging temporarily for tests
        $originalLogLevel = Logger::getLogLevel();

        // Configure logger with context-specific log files for StandardTests
        $templateAnalysisDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis';
        $logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';
        if (!is_dir($logsDir)) {
            mkdir($logsDir, 0755, true);
        }

        $contextLogFiles = [
            'LoaderNormal' => $logsDir . DIRECTORY_SEPARATOR . 'php_loadernormal.log',
            'EngineNormal' => $logsDir . DIRECTORY_SEPARATOR . 'php_enginenormal.log'
        ];

        Logger::configure(Logger::DEBUG, null, false);
        Logger::configureContextLogFiles($contextLogFiles);

        try {
            // Capture output to prevent it from mixing with JSON response
            ob_start();
            $scenarios = ConfigUtil::getScenarios();
            $results = TestingUtils::runStandardTests($rootDirPath, $projectDirectory, $scenarios, false, true, true);
            if (!empty($results)) {
                TestingUtils::printTestSummaryTable($rootDirPath, $results, 'STANDARD TEST');
            }
            ob_end_clean();

            // Restore original log level
            Logger::setLogLevel($originalLogLevel);

            $elapsed = microtime(true) - $start;
            $testCount = count($results);

            // Check for failures
            $failedCount = 0;
            foreach ($results as $r) {
                if ($r->NormalPreProcess === 'FAIL' ||
                    $r->CrossViewUnMatch === 'FAIL' ||
                    (!empty($r->Error) && $r->Error !== '')) {
                    $failedCount++;
                }
            }

            $message = sprintf('Successful run of Standard Tests in %.2f secs (%d tests)', $elapsed, $testCount);
            if ($failedCount > 0) {
                $message .= sprintf("\n⚠️ Warning: %d test(s) failed", $failedCount);
            }

            $responseData = [
                'success' => true,
                'message' => $message,
                'elapsed' => $elapsed,
                'testCount' => $testCount
            ];

            $response->getBody()->write(json_encode($responseData));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            // Restore original log level
            Logger::setLogLevel($originalLogLevel);
            error_log('Error in standard tests: ' . $error->getMessage());
            $response->getBody()->write(json_encode(['error' => 'Internal server error']));
            return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
        }
    }

    public static function testAdvancedEndpoint(ServerRequest $request, Response $response, string $projectDirectory): Response
    {
        $start = microtime(true);
        $rootDirPath = $projectDirectory . DIRECTORY_SEPARATOR . 'wwwroot';

        // Enable logging temporarily for tests
        $originalLogLevel = Logger::getLogLevel();

        // Configure logger with context-specific log files for AdvancedTests
        $templateAnalysisDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis';
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

        try {
            // Capture output to prevent it from mixing with JSON response
            ob_start();
            $scenarios = ConfigUtil::getScenarios();
            TestingUtils::dumpPreprocessedTemplateStructures($rootDirPath, $projectDirectory, $scenarios, true);
            $results = TestingUtils::runAdvancedTests($rootDirPath, $projectDirectory, $scenarios, false, true, true);
            if (!empty($results)) {
                TestingUtils::printTestSummaryTable($rootDirPath, $results, 'ADVANCED TEST');
            }
            ob_end_clean();

            // Restore original log level
            Logger::setLogLevel($originalLogLevel);

            $elapsed = microtime(true) - $start;
            $testCount = count($results);

            // Check for failures
            $failedCount = 0;
            foreach ($results as $r) {
                if ($r->NormalPreProcess === 'FAIL' ||
                    $r->CrossViewUnMatch === 'FAIL' ||
                    (!empty($r->Error) && $r->Error !== '')) {
                    $failedCount++;
                }
            }

            $message = sprintf('Successful run of Advanced Tests in %.2f secs (%d tests)', $elapsed, $testCount);
            if ($failedCount > 0) {
                $message .= sprintf("\n⚠️ Warning: %d test(s) failed", $failedCount);
            }

            $responseData = [
                'success' => true,
                'message' => $message,
                'elapsed' => $elapsed,
                'testCount' => $testCount
            ];

            $response->getBody()->write(json_encode($responseData));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            // Restore original log level
            Logger::setLogLevel($originalLogLevel);
            error_log('Error in advanced tests: ' . $error->getMessage());
            $response->getBody()->write(json_encode(['error' => 'Internal server error']));
            return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
        }
    }

    public static function testPerformanceEndpoint(ServerRequest $request, Response $response): Response
    {
        $start = microtime(true);
        $paths = CommonUtil::getAssemblerWebDirPath();
        $rootDirPath = $paths['assemblerWebDirPath'];

        try {
            // Disable logging during performance tests for better performance
            $originalLogLevel = Logger::getLogLevel();
            Logger::setLogLevel(Logger::NONE);

            // Capture output to prevent it from mixing with JSON response
            ob_start();
            $scenarios = ConfigUtil::getScenarios();
            $results = PerformanceUtils::runPerformanceComparison($rootDirPath, $scenarios, true, true);
            if (!empty($results)) {
                PerformanceUtils::printPerfSummaryTable($rootDirPath, $results);
            }
            ob_end_clean();

            // Restore logging
            Logger::setLogLevel($originalLogLevel);

            $elapsed = microtime(true) - $start;
            $testCount = count($results);

            // Check for performance test mismatches
            $mismatchCount = 0;
            foreach ($results as $r) {
                if ($r->ResultsMatch !== 'YES') {
                    $mismatchCount++;
                }
            }

            $message = sprintf('Successful run of Performance Tests in %.2f secs (%d tests)', $elapsed, $testCount);
            if ($mismatchCount > 0) {
                $message .= sprintf("\n⚠️ Warning: %d test(s) have output mismatch", $mismatchCount);
            }

            $responseData = [
                'success' => true,
                'message' => $message,
                'elapsed' => $elapsed,
                'testCount' => $testCount
            ];

            $response->getBody()->write(json_encode($responseData));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in performance tests: ' . $error->getMessage());
            $response->getBody()->write(json_encode(['error' => 'Internal server error']));
            return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
        }
    }

    public static function testConsolidatePerformanceEndpoint(ServerRequest $request, Response $response, string $projectDirectory): Response
    {
        $start = microtime(true);
        $rootDirPath = $projectDirectory . DIRECTORY_SEPARATOR . 'wwwroot';

        // Configure logging for consolidate endpoint
        $templateAnalysisDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis';
        $logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';
        if (!is_dir($logsDir)) {
            mkdir($logsDir, 0755, true);
        }
        $consolidateLogFile = $logsDir . DIRECTORY_SEPARATOR . 'php_consolidate_perf.log';

        // Log start
        $logMsg = sprintf("\n[%s] Starting consolidate-performance endpoint\n", gmdate('Y-m-d\TH:i:s\Z'));
        file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);

        try {
            // Read server configuration from servers.json
            $serversConfigPath = $rootDirPath . DIRECTORY_SEPARATOR . 'servers.json';
            $servers = [
                ['language' => 'CSharp', 'url' => 'https://csharpassembler.fly.dev/csharp_perfsummary.json'],
                ['language' => 'Rust', 'url' => 'https://rustassembler.fly.dev/rust_perfsummary.json'],
                ['language' => 'Node', 'url' => 'https://nodeassembler.fly.dev/nodejs_perfsummary.json'],
                ['language' => 'PHP', 'url' => 'https://phpassembler.fly.dev/php_perfsummary.json'],
                ['language' => 'Go', 'url' => 'https://goassembler.fly.dev/go_perfsummary.json']
            ];

            if (file_exists($serversConfigPath)) {
                try {
                    $configJson = file_get_contents($serversConfigPath);
                    $config = json_decode($configJson, true);
                    if (isset($config['performanceServers']) && is_array($config['performanceServers'])) {
                        $servers = $config['performanceServers'];
                    }
                } catch (Exception $err) {
                    error_log('Failed to read servers.json, using defaults: ' . $err->getMessage());
                }
            }

            $serversProcessed = [];
            $serversFailed = [];
            $performanceData = []; // Map<appSite, Map<language, perfData>>

            // Fetch data from each server
            foreach ($servers as $server) {
                // Log fetch attempt
                $logMsg = sprintf("[%s] Fetching %s from %s\n", gmdate('Y-m-d\TH:i:s\Z'), $server['language'], $server['url']);
                file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);

                $ch = curl_init();
                curl_setopt($ch, CURLOPT_URL, $server['url']);
                curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
                curl_setopt($ch, CURLOPT_TIMEOUT, 30);
                curl_setopt($ch, CURLOPT_FOLLOWLOCATION, true);
                curl_setopt($ch, CURLOPT_SSL_VERIFYPEER, false);
                curl_setopt($ch, CURLOPT_SSL_VERIFYHOST, false);

                $result = curl_exec($ch);
                $httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
                $error = curl_error($ch);
                curl_close($ch);

                if ($result !== false && $httpCode == 200) {
                    $data = json_decode($result, true);

                    // Process each performance entry
                    if (is_array($data)) {
                        $itemCount = count($data);
                        foreach ($data as $entry) {
                            $appSite = $entry['AppSite'] ?? $entry['appSite'] ?? $entry['app_site'] ?? '';
                            $appView = $entry['AppView'] ?? $entry['appView'] ?? $entry['app_view'] ?? '';

                            // Handle both milliseconds and nanoseconds
                            $normalTimeMs = $entry['NormalTimeMs'] ?? $entry['normalTimeMs'] ?? $entry['normal_time_ms'] ?? null;
                            $normalTimeNanos = $entry['NormalTimeNanos'] ?? $entry['normal_time_nanos'] ?? null;
                            if ($normalTimeNanos !== null) {
                                $normalTimeMs = $normalTimeNanos / 1_000_000;
                            }

                            $preProcessTimeMs = $entry['PreProcessTimeMs'] ?? $entry['preProcessTimeMs'] ?? $entry['preprocess_time_ms'] ?? null;
                            $preProcessTimeNanos = $entry['PreProcessTimeNanos'] ?? $entry['preprocess_time_nanos'] ?? null;
                            if ($preProcessTimeNanos !== null) {
                                $preProcessTimeMs = $preProcessTimeNanos / 1_000_000;
                            }

                            $outputSize = $entry['OutputSize'] ?? $entry['outputSize'] ?? $entry['output_size'] ?? null;

                            if (!isset($performanceData[$appSite])) {
                                $performanceData[$appSite] = [];
                            }

                            $performanceData[$appSite][$server['language']] = [
                                'normalTimeMs' => $normalTimeMs,
                                'preProcessTimeMs' => $preProcessTimeMs,
                                'outputSize' => $outputSize,
                                'appView' => $appView
                            ];
                        }
                        // Log success
                        $logMsg = sprintf("[%s] ✅ %s: Successfully processed %d items\n", gmdate('Y-m-d\TH:i:s\Z'), $server['language'], $itemCount);
                        file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);
                    }

                    $serversProcessed[] = $server['language'];
                } else {
                    // Extract domain from URL
                    $domain = preg_replace('#^https?://([^/]+).*$#', '$1', $server['url']);
                    if ($error) {
                        $failureMsg = "{$server['language']}: {$domain} (ERROR: {$error})";
                        $serversFailed[] = $failureMsg;
                        // Log failure
                        $logMsg = sprintf("[%s] ❌ %s\n", gmdate('Y-m-d\TH:i:s\Z'), $failureMsg);
                        file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);
                    } else {
                        $failureMsg = "{$server['language']}: {$domain} (HTTP {$httpCode})";
                        $serversFailed[] = $failureMsg;
                        // Log failure
                        $logMsg = sprintf("[%s] ❌ %s\n", gmdate('Y-m-d\TH:i:s\Z'), $failureMsg);
                        file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);
                    }
                }
            }

            // Generate HTML report
            $html = [];
            $html[] = '<!DOCTYPE html>';
            $html[] = '<html>';
            $html[] = '<head>';
            $html[] = '    <meta charset="UTF-8">';
            $html[] = '    <meta name="viewport" content="width=device-width, initial-scale=1.0">';
            $html[] = '    <title>All Performance Tests</title>';
            $html[] = '    <style>';
            $html[] = '        body { font-family: Arial, sans-serif; margin: 20px; }';
            $html[] = '        h1, h2 { color: #333; }';
            $html[] = '        .table-container { overflow-x: auto; }';
            $html[] = '        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }';
            $html[] = '        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }';
            $html[] = '        th { background-color: #4CAF50; color: white; }';
            $html[] = '        tr:nth-child(even) { background-color: #f2f2f2; }';
            $html[] = '        @media (max-width: 768px) {';
            $html[] = '            body { margin: 10px; }';
            $html[] = '            th, td { padding: 8px; font-size: 14px; }';
            $html[] = '            h1, h2 { font-size: 24px; }';
            $html[] = '        }';
            $html[] = '    </style>';
            $html[] = '</head>';
            $html[] = '<body>';
            $html[] = '<h1>Consolidated Performance Tests</h1>';

            // Sort appSites
            $sortedAppSites = array_keys($performanceData);
            sort($sortedAppSites);
            $languages = ['CSharp', 'Rust', 'Go', 'Node', 'PHP'];

            // Normal Engine Table
            $html[] = '<h2>Normal Engine Performance (ms)</h2>';
            $html[] = '<div class="table-container">';
            $html[] = '<table><tr><th>AppSite</th>';
            foreach ($languages as $lang) {
                $html[] = "<th>{$lang}</th>";
            }
            $html[] = '<th>Output Size</th></tr>';

            foreach ($sortedAppSites as $appSite) {
                $langData = $performanceData[$appSite];
                $html[] = "<tr><td>{$appSite}</td>";

                foreach ($languages as $lang) {
                    $data = $langData[$lang] ?? null;
                    $val = $data['normalTimeMs'] ?? null;
                    $html[] = '<td>' . ($val !== null ? number_format($val, 2, '.', '') : '-') . '</td>';
                }

                // Output size from first available language
                $firstOutputSize = null;
                foreach ($languages as $lang) {
                    if (isset($langData[$lang]['outputSize'])) {
                        $firstOutputSize = $langData[$lang]['outputSize'];
                        break;
                    }
                }
                $html[] = '<td>' . ($firstOutputSize !== null ? (string)$firstOutputSize : '-') . '</td>';
                $html[] = '</tr>';
            }
            $html[] = '</table>';
            $html[] = '</div>';

            // PreProcess Engine Table
            $html[] = '<h2>PreProcess Engine Performance (ms)</h2>';
            $html[] = '<div class="table-container">';
            $html[] = '<table><tr><th>AppSite</th>';
            foreach ($languages as $lang) {
                $html[] = "<th>{$lang}</th>";
            }
            $html[] = '<th>Output Size</th></tr>';

            foreach ($sortedAppSites as $appSite) {
                $langData = $performanceData[$appSite];
                $html[] = "<tr><td>{$appSite}</td>";

                foreach ($languages as $lang) {
                    $data = $langData[$lang] ?? null;
                    $val = $data['preProcessTimeMs'] ?? null;
                    $html[] = '<td>' . ($val !== null ? number_format($val, 2, '.', '') : '-') . '</td>';
                }

                // Output size from first available language
                $firstOutputSize = null;
                foreach ($languages as $lang) {
                    if (isset($langData[$lang]['outputSize'])) {
                        $firstOutputSize = $langData[$lang]['outputSize'];
                        break;
                    }
                }
                $html[] = '<td>' . ($firstOutputSize !== null ? (string)$firstOutputSize : '-') . '</td>';
                $html[] = '</tr>';
            }
            $html[] = '</table>';
            $html[] = '</div>';
            $html[] = '</body>';
            $html[] = '</html>';

            // Write HTML file to Reports directory
            $reportsDir = $rootDirPath . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsDir)) {
                mkdir($reportsDir, 0755, true);
            }
            $htmlPath = $reportsDir . DIRECTORY_SEPARATOR . 'all_perf_tests.html';
            file_put_contents($htmlPath, implode("\n", $html));

            $elapsed = microtime(true) - $start;

            // Log completion
            $logMsg = sprintf("[%s] Consolidation complete in %.2fs - %d AppSites from %d/%d servers\n",
                gmdate('Y-m-d\TH:i:s\Z'), $elapsed, count($performanceData), count($serversProcessed), count($servers));
            file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);

            $message = sprintf("Consolidated performance data from %d/%d servers in %.2f secs", count($serversProcessed), count($servers), $elapsed);
            if (count($serversProcessed) > 0) {
                $message .= " | ✅ Success: " . implode(', ', $serversProcessed);
            }
            if (count($serversFailed) > 0) {
                $message .= "\n❌ Failed: " . implode('; ', $serversFailed);
            }

            $responseData = [
                'success' => count($serversProcessed) > 0,
                'message' => $message,
                'elapsed' => $elapsed,
                'testCount' => count($serversProcessed)
            ];

            $response->getBody()->write(json_encode($responseData));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in consolidate performance: ' . $error->getMessage());
            $response->getBody()->write(json_encode(['error' => 'Internal server error']));
            return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
        }
    }
}

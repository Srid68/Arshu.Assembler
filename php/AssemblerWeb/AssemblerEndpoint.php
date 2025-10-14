<?php

require_once __DIR__ . '/SecurityValidator.php';

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

	 public static function testStandardEndpoint(ServerRequest $request, Response $response, string $wwwrootPath, string $projectRootPath): Response
    {
        $start = microtime(true);

        // Enable logging temporarily for tests
        $originalLogLevel = Logger::getLogLevel();

        // Configure logger with context-specific log files for StandardTests
        $templateAnalysisDir = $projectRootPath . DIRECTORY_SEPARATOR . 'template_analysis';
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

        // Log start
        $startMsg = sprintf("/test/standard endpoint called at %s", gmdate('Y-m-d H:i:s'));
        Logger::info($startMsg, 'TestStandard');

        try {
            // Capture output to prevent it from mixing with JSON response
            ob_start();
            $scenarios = ConfigUtil::getScenarios();
            $results = TestingUtils::runStandardTests($wwwrootPath, $projectRootPath, $scenarios, false, true, true);
            if (!empty($results)) {
                TestingUtils::printTestSummaryTable($wwwrootPath, $projectRootPath, $results, 'STANDARD TEST');
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

            // Log completion
            $completeMsg = sprintf("/test/standard endpoint completed: elapsed=%.2fs, tests=%d, failed=%d", $elapsed, $testCount, $failedCount);
            Logger::info($completeMsg, 'TestStandard');

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

    public static function testAdvancedEndpoint(ServerRequest $request, Response $response, string $wwwrootPath, string $projectRootPath): Response
    {
        $start = microtime(true);

        // Enable logging temporarily for tests
        $originalLogLevel = Logger::getLogLevel();

        // Configure logger with context-specific log files for AdvancedTests
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

        // Log start
        $startMsg = sprintf("/test/advanced endpoint called at %s", gmdate('Y-m-d H:i:s'));
        Logger::info($startMsg, 'TestAdvanced');

        try {
            // Capture output to prevent it from mixing with JSON response
            ob_start();
            $scenarios = ConfigUtil::getScenarios();
            TestingUtils::dumpPreprocessedTemplateStructures($wwwrootPath, $projectRootPath, $scenarios, true);
            $results = TestingUtils::runAdvancedTests($wwwrootPath, $projectRootPath, $scenarios, false, true, true);
            if (!empty($results)) {
                TestingUtils::printTestSummaryTable($wwwrootPath, $projectRootPath, $results, 'ADVANCED TEST');
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

            // Log completion
            $completeMsg = sprintf("/test/advanced endpoint completed: elapsed=%.2fs, tests=%d, failed=%d", $elapsed, $testCount, $failedCount);
            Logger::info($completeMsg, 'TestAdvanced');

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

    public static function testPerformanceEndpoint(ServerRequest $request, Response $response, string $wwwrootPath, string $projectRootPath): Response
    {
        $start = microtime(true);

        try {
            // Log start before disabling logging
            $startMsg = sprintf("/test/performance endpoint called at %s", gmdate('Y-m-d H:i:s'));
            Logger::info($startMsg, 'TestPerformance');

            // Disable logging during performance tests for better performance
            $originalLogLevel = Logger::getLogLevel();
            Logger::setLogLevel(Logger::NONE);

            // Capture output to prevent it from mixing with JSON response
            ob_start();
            $scenarios = ConfigUtil::getScenarios();
            $results = PerformanceUtils::runPerformanceComparison($wwwrootPath, $scenarios, true, true);
            if (!empty($results)) {
                PerformanceUtils::printPerfSummaryTable($wwwrootPath, $projectRootPath, $results);
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

            // Log completion after restoring log level
            $completeMsg = sprintf("/test/performance endpoint completed: elapsed=%.2fs, tests=%d, mismatches=%d", $elapsed, $testCount, $mismatchCount);
            Logger::info($completeMsg, 'TestPerformance');

            $response->getBody()->write(json_encode($responseData));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in performance tests: ' . $error->getMessage());
            $response->getBody()->write(json_encode(['error' => 'Internal server error']));
            return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
        }
    }

    public static function testConsolidatePerformanceEndpoint(ServerRequest $request, Response $response, string $wwwrootPath, string $projectRootPath): Response
    {
        $start = microtime(true);

        // Configure logging for consolidate endpoint
        $templateAnalysisDir = $projectRootPath . DIRECTORY_SEPARATOR . 'template_analysis';
        $logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';
        if (!is_dir($logsDir)) {
            mkdir($logsDir, 0755, true);
        }
        $consolidateLogFile = $logsDir . DIRECTORY_SEPARATOR . 'php_consolidate_perf.log';

        // Log start
        $startMsg = sprintf("/test/consolidate-performance endpoint called at %s", gmdate('Y-m-d H:i:s'));
        Logger::info($startMsg, 'TestConsolidatePerf');
        $logMsg = sprintf("\n[%s] Starting consolidate-performance endpoint\n", gmdate('Y-m-d\TH:i:s\Z'));
        file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);

        try {
            // Read server configuration from servers.csv
            $serversConfigPath = $wwwrootPath . DIRECTORY_SEPARATOR . 'App_Data' . DIRECTORY_SEPARATOR . 'servers.csv';
            $servers = [];

            if (file_exists($serversConfigPath)) {
                $csvContent = file_get_contents($serversConfigPath);
                $lines = explode("\n", $csvContent);
                foreach ($lines as $line) {
                    $line = trim($line);
                    if ($line === '') continue;
                    $parts = explode(',', $line);
                    if (count($parts) >= 3) {
                        $language = trim($parts[0]);
                        $method = strtoupper(trim($parts[1]));
                        $url = trim($parts[2]);
                        $fileName = count($parts) >= 4 ? trim($parts[3]) : '';
                        if ($language !== '' && $method !== '' && $url !== '') {
                            $servers[] = ['language' => $language, 'method' => $method, 'url' => $url, 'fileName' => $fileName];
                        }
                    }
                }
            }

            if (empty($servers)) {
                $errorMsg = 'No server configuration found. Please configure servers in App_Data/servers.csv';
                $logMsg = sprintf("[%s] ❌ %s\n", gmdate('Y-m-d\TH:i:s\Z'), $errorMsg);
                file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);

                $responseData = [
                    'success' => false,
                    'message' => $errorMsg,
                    'elapsed' => microtime(true) - $start,
                    'testCount' => 0
                ];

                $response->getBody()->write(json_encode($responseData));
                return $response->withHeader('Content-Type', 'application/json');
            }

            $serversProcessed = [];
            $serversFailed = [];
            $performanceData = []; // Map<appSite, Map<language, perfData>>

            // Group servers by language
            $serversByLang = [];
            foreach ($servers as $server) {
                $lang = $server['language'];
                if (!isset($serversByLang[$lang])) {
                    $serversByLang[$lang] = [];
                }
                $serversByLang[$lang][] = $server;
            }

            // Fetch data from each language (trying all methods)
            foreach ($serversByLang as $lang => $langServers) {
                $langSuccess = false;
                $langErrors = [];

                foreach ($langServers as $server) {
                    // Log fetch attempt
                    if ($server['method'] === 'POST') {
                        $logMsg = sprintf("[%s] Fetching %s via POST %s (fileName: %s)\n", gmdate('Y-m-d\TH:i:s\Z'), $lang, $server['url'], $server['fileName']);
                    } else {
                        $fullUrl = $server['url'] . $server['fileName'];
                        $logMsg = sprintf("[%s] Fetching %s via GET %s\n", gmdate('Y-m-d\TH:i:s\Z'), $lang, $fullUrl);
                    }
                    file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);

                    $ch = curl_init();
                    if ($server['method'] === 'POST') {
                        $reportRequest = [
                            'fileName' => $server['fileName'],
                            'useLangPrefix' => false
                        ];
                        curl_setopt($ch, CURLOPT_URL, $server['url']);
                        curl_setopt($ch, CURLOPT_POST, true);
                        curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($reportRequest));
                        curl_setopt($ch, CURLOPT_HTTPHEADER, ['Content-Type: application/json']);
                    } else {
                        $fullUrl = $server['url'] . $server['fileName'];
                        curl_setopt($ch, CURLOPT_URL, $fullUrl);
                    }
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

                                // Create composite key: AppSite + AppView to handle scenarios with different views
                                if ($appSite !== '') {
                                    $key = ($appView !== '') ? "{$appSite} → {$appView}" : $appSite;

                                    // Use case-insensitive comparison for key matching
                                    $existingKey = null;
                                    foreach (array_keys($performanceData) as $k) {
                                        if (strcasecmp($k, $key) === 0) {
                                            $existingKey = $k;
                                            break;
                                        }
                                    }
                                    $finalKey = $existingKey ?? $key;

                                    if (!isset($performanceData[$finalKey])) {
                                        $performanceData[$finalKey] = [];
                                    }

                                    $performanceData[$finalKey][$lang] = [
                                        'normalTimeMs' => $normalTimeMs,
                                        'preProcessTimeMs' => $preProcessTimeMs,
                                        'outputSize' => $outputSize,
                                        'appView' => $appView
                                    ];
                                }
                            }
                            // Log success
                            $logMsg = sprintf("[%s] ✅ %s: Successfully processed %d items\n", gmdate('Y-m-d\TH:i:s\Z'), $lang, $itemCount);
                            file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);
                        }

                        $langSuccess = true;
                        break; // Success, no need to try other methods
                    } else {
                        // Extract domain from URL
                        $domain = preg_replace('#^https?://([^/]+).*$#', '$1', $server['url']);
                        if ($error) {
                            $errorMsg = "{$server['method']} {$domain} (ERROR: {$error})";
                            $langErrors[] = $errorMsg;
                            // Log warning
                            $logMsg = sprintf("[%s] ⚠️ %s: %s\n", gmdate('Y-m-d\TH:i:s\Z'), $lang, $errorMsg);
                            file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);
                        } else {
                            $errorMsg = "{$server['method']} {$domain} (HTTP {$httpCode})";
                            $langErrors[] = $errorMsg;
                            // Log warning
                            $logMsg = sprintf("[%s] ⚠️ %s: %s\n", gmdate('Y-m-d\TH:i:s\Z'), $lang, $errorMsg);
                            file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);
                        }
                    }
                }

                // After trying all methods for this language, determine overall result
                if ($langSuccess) {
                    $serversProcessed[] = $lang;
                } else {
                    $failureMsg = "{$lang}: All methods failed - " . implode('; ', $langErrors);
                    $serversFailed[] = $failureMsg;
                    $logMsg = sprintf("[%s] ❌ %s: All methods failed\n", gmdate('Y-m-d\TH:i:s\Z'), $lang);
                    file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);
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
            $html[] = '        .best-perf { background-color: #90EE90; font-weight: bold; }';
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

            // Get list of languages dynamically from configuration
            $languages = array_keys($serversByLang);
            sort($languages);

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

                // Find minimum time for highlighting
                $validTimes = [];
                foreach ($languages as $lang) {
                    $val = $langData[$lang]['normalTimeMs'] ?? null;
                    if ($val !== null) {
                        $validTimes[] = $val;
                    }
                }
                $minTime = !empty($validTimes) ? min($validTimes) : null;

                $html[] = "<tr><td>{$appSite}</td>";

                foreach ($languages as $lang) {
                    $data = $langData[$lang] ?? null;
                    $val = $data['normalTimeMs'] ?? null;
                    $isBest = ($minTime !== null && $val !== null && abs($val - $minTime) < 0.001);
                    $cssClass = $isBest ? ' class="best-perf"' : '';
                    $html[] = '<td' . $cssClass . '>' . ($val !== null ? number_format($val, 2, '.', '') : '-') . '</td>';
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

                // Find minimum time for highlighting
                $validTimes = [];
                foreach ($languages as $lang) {
                    $val = $langData[$lang]['preProcessTimeMs'] ?? null;
                    if ($val !== null) {
                        $validTimes[] = $val;
                    }
                }
                $minTime = !empty($validTimes) ? min($validTimes) : null;

                $html[] = "<tr><td>{$appSite}</td>";

                foreach ($languages as $lang) {
                    $data = $langData[$lang] ?? null;
                    $val = $data['preProcessTimeMs'] ?? null;
                    $isBest = ($minTime !== null && $val !== null && abs($val - $minTime) < 0.001);
                    $cssClass = $isBest ? ' class="best-perf"' : '';
                    $html[] = '<td' . $cssClass . '>' . ($val !== null ? number_format($val, 2, '.', '') : '-') . '</td>';
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
            $reportsDir = $projectRootPath . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsDir)) {
                mkdir($reportsDir, 0755, true);
            }
            $htmlPath = $reportsDir . DIRECTORY_SEPARATOR . 'all_perf_tests.html';
            file_put_contents($htmlPath, implode("\n", $html));

            $elapsed = microtime(true) - $start;

            // Log completion
            $totalLanguages = count($serversByLang);
            $completeMsg = sprintf("/test/consolidate-performance endpoint completed: elapsed=%.2fs, languages=%d/%d, appsites=%d",
                $elapsed, count($serversProcessed), $totalLanguages, count($performanceData));
            Logger::info($completeMsg, 'TestConsolidatePerf');
            $logMsg = sprintf("[%s] Consolidation complete in %.2fs - %d AppSites from %d/%d languages\n",
                gmdate('Y-m-d\TH:i:s\Z'), $elapsed, count($performanceData), count($serversProcessed), $totalLanguages);
            file_put_contents($consolidateLogFile, $logMsg, FILE_APPEND);

            $message = sprintf("Consolidated %d AppSites from %d/%d languages in %.2f secs", count($performanceData), count($serversProcessed), $totalLanguages, $elapsed);
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

    public static function getReportEndpoint(ServerRequest $request, Response $response, string $projectRootPath): Response
    {
        try {
            $body = $request->getBody()->getContents();
            $data = json_decode($body, true);

            if (json_last_error() !== JSON_ERROR_NONE) {
                $response->getBody()->write(json_encode(['error' => 'Invalid JSON']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            $fileName = $data['fileName'] ?? null;
            $useLangPrefix = $data['useLangPrefix'] ?? false;

            if (empty($fileName)) {
                $response->getBody()->write(json_encode(['error' => 'Missing required field: fileName']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            // Validate fileName for path traversal
            if (!SecurityValidator::isValidPathComponent($fileName)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in fileName']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            // Construct file path
            $prefix = $useLangPrefix ? 'php_' : '';
            $fullFileName = $prefix . $fileName;
            $reportsDir = $projectRootPath . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'Reports';
            $filePath = $reportsDir . DIRECTORY_SEPARATOR . $fullFileName;

            // Check if file exists
            if (!file_exists($filePath)) {
                $response->getBody()->write(json_encode(['error' => "Report file not found: {$fullFileName}"]));
                return $response->withStatus(404)->withHeader('Content-Type', 'application/json');
            }

            // Read and return the file content
            $content = file_get_contents($filePath);
            if ($content === false) {
                $response->getBody()->write(json_encode(['error' => 'Error reading report file']));
                return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
            }

            // Determine content type based on file extension
            $contentType = 'text/plain';
            $extension = pathinfo($fullFileName, PATHINFO_EXTENSION);
            if ($extension === 'html') {
                $contentType = 'text/html';
            } elseif ($extension === 'json') {
                $contentType = 'application/json';
            } elseif ($extension === 'md') {
                $contentType = 'text/markdown';
            }

            $response->getBody()->write($content);
            return $response->withHeader('Content-Type', $contentType);
        } catch (Exception $error) {
            error_log('Error in getReport endpoint: ' . $error->getMessage());
            $response->getBody()->write(json_encode(['error' => 'Internal server error']));
            return $response->withStatus(500)->withHeader('Content-Type', 'application/json');
        }
    }
}

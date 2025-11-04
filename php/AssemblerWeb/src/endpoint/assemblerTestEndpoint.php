<?php

require_once __DIR__ . '/SecurityValidator.php';

use Psr\Http\Message\ResponseInterface as Response;
use Psr\Http\Message\ServerRequestInterface as ServerRequest;
use Arshu\Common\Logger;
use Assembler\Test\TestingUtils;
use Assembler\Performance\PerformanceUtils;
use Assembler\Config\ConfigUtil;

class AssemblerTestEndpoint
{
	// Configurable rule groups for consolidated report grouping
	const RULE_GROUPS = [
		'HtmlRule1',
		'HtmlRule2',
		'HtmlRule3',
		'JsonRule1',
		'JsonRule2',
		'Rule1'
	];

	/**
	 * Get project directory
	 */
	private static function getProjectDirectory(): string {
		return getcwd();
	}

	/**
	 * Get wwwroot path
	 */
	private static function getWwwrootPath(): string {
		return self::getProjectDirectory() . DIRECTORY_SEPARATOR . 'wwwroot';
	}

	 public static function testStandardEndpoint(ServerRequest $request, Response $response): Response
    {
        $start = microtime(true);
        $projectRootPath = self::getProjectDirectory();
        $wwwrootPath = self::getWwwrootPath();

        // Enable logging temporarily for tests
        $originalLogLevel = Logger::getLogLevel();

        // Configure logger with context-specific log files for StandardTests
        $templateAnalysisDir = $projectRootPath . DIRECTORY_SEPARATOR . 'Analysis';
        $logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';
        if (!is_dir($logsDir)) {
            mkdir($logsDir, 0755, true);
        }

        $contextLogFiles = [
            'LoaderNormal' => $logsDir . DIRECTORY_SEPARATOR . 'php_loadernormal.log',
            'EngineNormal' => $logsDir . DIRECTORY_SEPARATOR . 'php_enginenormal.log'
        ];

        Logger::addContextLogFiles($contextLogFiles);

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

    public static function testAdvancedEndpoint(ServerRequest $request, Response $response): Response
    {
        $start = microtime(true);
        $projectRootPath = self::getProjectDirectory();
        $wwwrootPath = self::getWwwrootPath();

        // Enable logging temporarily for tests
        $originalLogLevel = Logger::getLogLevel();

        // Configure logger with context-specific log files for AdvancedTests
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

    public static function testPerformanceEndpoint(ServerRequest $request, Response $response): Response
    {
        $start = microtime(true);
        $projectRootPath = self::getProjectDirectory();
        $wwwrootPath = self::getWwwrootPath();

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

	public static function testConsolidatePerformanceEndpoint(ServerRequest $request, Response $response): Response
    {
        $start = microtime(true);
        $projectRootPath = self::getProjectDirectory();
        $wwwrootPath = self::getWwwrootPath();

        // Configure logging for consolidate endpoint
        $templateAnalysisDir = $projectRootPath . DIRECTORY_SEPARATOR . 'Analysis';
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
            $html[] = '    <title>Consolidated Performance Summary</title>';
            $html[] = '    <style>';
            $html[] = '        body { font-family: Arial, sans-serif; margin: 20px; }';
            $html[] = '        h1 { color: #333; }';
            $html[] = '        h2 { color: #333; margin-top: 40px; }';
            $html[] = '        .meta { color: #666; font-style: italic; margin-bottom: 10px; }';
            $html[] = '        .table-container { overflow-x: auto; }';
            $html[] = '        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 700px; }';
            $html[] = '        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }';
            $html[] = '        th { background-color: #4CAF50; color: white; }';
            $html[] = '        tr:nth-child(even) { background-color: #f2f2f2; }';
            $html[] = '        td:nth-child(2), td:nth-child(3), td:nth-child(4), td:nth-child(5), td:nth-child(6), td:nth-child(7) { text-align: right; }';
            $html[] = '        .best-perf { background-color: #90EE90; font-weight: bold; }';
            $html[] = '        .worst-perf { background-color: #FFB6C6; font-weight: bold; }';
            $html[] = '        .avg-perf { background-color: #FFD700; font-weight: bold; }';
            $html[] = '        .legend { display: flex; gap: 20px; margin: 20px 0; flex-wrap: wrap; }';
            $html[] = '        .legend-item { display: flex; align-items: center; gap: 8px; }';
            $html[] = '        .legend-box { width: 24px; height: 24px; border: 1px solid #999; }';
            $html[] = '        .view-toggle { margin: 20px 0; }';
            $html[] = '        .view-btn { padding: 10px 20px; margin-right: 10px; cursor: pointer; border: 2px solid #4CAF50; background: white; color: #4CAF50; font-size: 14px; border-radius: 5px; }';
            $html[] = '        .view-btn.active { background: #4CAF50; color: white; }';
            $html[] = '        .view-content { display: none; }';
            $html[] = '        .view-content.active { display: block; }';
            $html[] = '        .chart-container { margin: 20px 0; }';
            $html[] = '        .chart-row { margin-bottom: 25px; }';
            $html[] = '        .chart-label { font-weight: bold; margin-bottom: 8px; font-size: 14px; color: #333; }';
            $html[] = '        .chart-bars-container { display: flex; flex-direction: column; gap: 8px; }';
            $html[] = '        .chart-bar-wrapper { display: flex; align-items: center; gap: 10px; }';
            $html[] = '        .chart-bar-label { min-width: 80px; font-weight: 600; color: #555; font-size: 13px; }';
            $html[] = '        .chart-bar { height: 30px; border-radius: 5px; display: flex; align-items: center; justify-content: flex-end; padding-right: 10px; color: white; font-weight: bold; font-size: 12px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); transition: transform 0.2s; min-width: 40px; }';
            $html[] = '        .chart-bar:hover { transform: translateX(5px); box-shadow: 0 4px 8px rgba(0,0,0,0.15); }';
            $html[] = '        .chart-bar-value { margin-left: 10px; font-weight: 600; color: #333; font-size: 13px; min-width: 60px; }';
            $html[] = '        .grouped-chart-section { margin-bottom: 40px; padding: 20px; background: #f9f9f9; border-radius: 8px; }';
            $html[] = '        .grouped-chart-title { font-size: 1.3em; font-weight: bold; color: #667eea; margin-bottom: 15px; border-bottom: 2px solid #667eea; padding-bottom: 8px; }';
            $html[] = '        .grouped-bar-group { display: flex; align-items: center; margin-bottom: 20px; }';
            $html[] = '        .grouped-bar-label { min-width: 100px; font-weight: 600; color: #333; font-size: 13px; }';
            $html[] = '        .grouped-bars { flex: 1; display: flex; flex-direction: column; gap: 4px; }';
            $html[] = '        .grouped-bar-item { display: flex; align-items: center; gap: 8px; }';
            $html[] = '        .grouped-bar { height: 24px; border-radius: 4px; display: flex; align-items: center; justify-content: flex-end; padding-right: 8px; color: white; font-weight: bold; font-size: 11px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); min-width: 30px; }';
            $html[] = '        .grouped-lang-label { min-width: 60px; font-size: 12px; color: #666; }';
            $html[] = '    </style>';
            $html[] = '</head>';
            $html[] = '<body>';
            $html[] = '    <h1>Consolidated Performance Summary</h1>';
            $html[] = '    <div class="meta">Generated: ' . gmdate('Y-m-d H:i:s') . ' UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>';
            $html[] = '    <div class="legend">';
            $html[] = '        <div class="legend-item"><div class="legend-box" style="background-color: #4CAF50; opacity: 0.8;"></div><span>Normal Engine (N)</span></div>';
            $html[] = '        <div class="legend-item"><div class="legend-box" style="background-color: #2196F3; opacity: 0.8;"></div><span>PreProcess Engine (P)</span></div>';
            $html[] = '        <div class="legend-item"><div class="legend-box" style="background-color: #90EE90;"></div><span>Best (Lowest Time - Table View)</span></div>';
            $html[] = '        <div class="legend-item"><div class="legend-box" style="background-color: #FFD700;"></div><span>Nearest to Average (Table View)</span></div>';
            $html[] = '        <div class="legend-item"><div class="legend-box" style="background-color: #FFB6C6;"></div><span>Worst (Highest Time - Table View)</span></div>';
            $html[] = '    </div>';
            $html[] = '    <div class="view-toggle">';
            $html[] = '        <button class="view-btn active" data-view="grouped">Grouped View</button>';
            $html[] = '        <button class="view-btn" data-view="chart">Bar Chart View</button>';
            $html[] = '        <button class="view-btn" data-view="table">Table View</button>';
            $html[] = '    </div>';

            // Get list of languages dynamically from configuration
            $languages = array_keys($serversByLang);
            sort($languages);

            // Combined Bar Chart View (Normal + PreProcess)
            $html[] = '    <div id="combined-chart" class="view-content">';
            $html[] = '        <div class="chart-container">';

            // Filter by rule groups
            $filteredApps = array_filter(array_keys($performanceData), function($app) {
                foreach (self::RULE_GROUPS as $rule) {
                    if (strpos($app, $rule) === 0) {
                        return true;
                    }
                }
                return false;
            });
            sort($filteredApps);

            foreach ($filteredApps as $app) {
                $html[] = '            <div class="chart-row">';
                $html[] = '                <div class="chart-label">' . htmlspecialchars($app) . '</div>';
                $html[] = '                <div class="chart-bars-container">';

                // Calculate max time across BOTH engines for consistent scaling
                $allTimes = [];
                foreach ($languages as $lang) {
                    if (isset($performanceData[$app][$lang])) {
                        $normalTime = $performanceData[$app][$lang]['normalTimeMs'];
                        $preprocessTime = $performanceData[$app][$lang]['preProcessTimeMs'];
                        if ($normalTime !== null && $normalTime > 0) $allTimes[] = $normalTime;
                        if ($preprocessTime !== null && $preprocessTime > 0) $allTimes[] = $preprocessTime;
                    }
                }
                $maxTimeForScale = !empty($allTimes) ? max($allTimes) : 1.0;

                // Calculate highlighting for Normal Engine
                $normalValidTimes = [];
                foreach ($languages as $lang) {
                    if (isset($performanceData[$app][$lang])) {
                        $normalTime = $performanceData[$app][$lang]['normalTimeMs'];
                        if ($normalTime !== null && $normalTime > 0) {
                            $normalValidTimes[] = $normalTime;
                        }
                    }
                }
                $normalMinTime = !empty($normalValidTimes) ? min($normalValidTimes) : null;
                $normalMaxTime = !empty($normalValidTimes) ? max($normalValidTimes) : null;
                $normalAvgTime = !empty($normalValidTimes) ? array_sum($normalValidTimes) / count($normalValidTimes) : null;

                // Calculate highlighting for PreProcess Engine
                $preprocessValidTimes = [];
                foreach ($languages as $lang) {
                    if (isset($performanceData[$app][$lang])) {
                        $preprocessTime = $performanceData[$app][$lang]['preProcessTimeMs'];
                        if ($preprocessTime !== null && $preprocessTime > 0) {
                            $preprocessValidTimes[] = $preprocessTime;
                        }
                    }
                }
                $preprocessMinTime = !empty($preprocessValidTimes) ? min($preprocessValidTimes) : null;
                $preprocessMaxTime = !empty($preprocessValidTimes) ? max($preprocessValidTimes) : null;
                $preprocessAvgTime = !empty($preprocessValidTimes) ? array_sum($preprocessValidTimes) / count($preprocessValidTimes) : null;

                foreach ($languages as $lang) {
                    if (isset($performanceData[$app][$lang])) {
                        $normalTime = $performanceData[$app][$lang]['normalTimeMs'];
                        $preprocessTime = $performanceData[$app][$lang]['preProcessTimeMs'];

                        if (($normalTime !== null && $normalTime > 0) || ($preprocessTime !== null && $preprocessTime > 0)) {
                            $html[] = '                    <div class="chart-bar-wrapper">';
                            $html[] = '                        <div class="chart-bar-label">' . htmlspecialchars($lang) . '</div>';
                            $html[] = '                        <div style="position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;">';

                            // Normal Engine Bar (bottom layer)
                            if ($normalTime !== null && $normalTime > 0) {
                                $widthPercent = ($normalTime / $maxTimeForScale) * 100;

                                // Determine highlight color
                                $normalBgColor = '#4CAF50';
                                if ($normalMinTime !== null && abs($normalTime - $normalMinTime) < 0.01) {
                                    $normalBgColor = '#90EE90';
                                } else if ($normalMaxTime !== null && abs($normalTime - $normalMaxTime) < 0.01) {
                                    $normalBgColor = '#FFB6C6';
                                } else if ($normalAvgTime !== null && count($normalValidTimes) > 2) {
                                    usort($normalValidTimes, function($a, $b) use ($normalAvgTime) {
                                        return abs($a - $normalAvgTime) <=> abs($b - $normalAvgTime);
                                    });
                                    if (abs($normalTime - $normalValidTimes[0]) < 0.01) {
                                        $normalBgColor = '#FFD700';
                                    }
                                }

                                $normalLabelStyle = $widthPercent > 85
                                    ? 'position: absolute; right: calc(100% - ' . $widthPercent . '% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;'
                                    : 'position: absolute; left: calc(' . $widthPercent . '% + 5px); top: 0; font-size: 11px; color: ' . $normalBgColor . '; font-weight: 600; white-space: nowrap;';

                                $html[] = '                            <div style="position: absolute; left: 0; top: 0; width: ' . $widthPercent . '%; height: 15px; background-color: ' . $normalBgColor . '; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="' . htmlspecialchars($lang) . ' Normal: ' . number_format($normalTime, 2) . 'ms"></div>';
                                $html[] = '                            <span style="' . $normalLabelStyle . '">N: ' . number_format($normalTime, 2) . 'ms</span>';
                            }

                            // PreProcess Engine Bar (top layer, slightly offset)
                            if ($preprocessTime !== null && $preprocessTime > 0) {
                                $widthPercent = ($preprocessTime / $maxTimeForScale) * 100;

                                // Determine highlight color
                                $preprocessBgColor = '#2196F3';
                                if ($preprocessMinTime !== null && abs($preprocessTime - $preprocessMinTime) < 0.01) {
                                    $preprocessBgColor = '#90EE90';
                                } else if ($preprocessMaxTime !== null && abs($preprocessTime - $preprocessMaxTime) < 0.01) {
                                    $preprocessBgColor = '#FFB6C6';
                                } else if ($preprocessAvgTime !== null && count($preprocessValidTimes) > 2) {
                                    usort($preprocessValidTimes, function($a, $b) use ($preprocessAvgTime) {
                                        return abs($a - $preprocessAvgTime) <=> abs($b - $preprocessAvgTime);
                                    });
                                    if (abs($preprocessTime - $preprocessValidTimes[0]) < 0.01) {
                                        $preprocessBgColor = '#FFD700';
                                    }
                                }

                                $preprocessLabelStyle = $widthPercent > 85
                                    ? 'position: absolute; right: calc(100% - ' . $widthPercent . '% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;'
                                    : 'position: absolute; left: calc(' . $widthPercent . '% + 5px); top: 15px; font-size: 11px; color: ' . $preprocessBgColor . '; font-weight: 600; white-space: nowrap;';

                                $html[] = '                            <div style="position: absolute; left: 0; top: 15px; width: ' . $widthPercent . '%; height: 15px; background-color: ' . $preprocessBgColor . '; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="' . htmlspecialchars($lang) . ' PreProcess: ' . number_format($preprocessTime, 2) . 'ms"></div>';
                                $html[] = '                            <span style="' . $preprocessLabelStyle . '">P: ' . number_format($preprocessTime, 2) . 'ms</span>';
                            }

                            $html[] = '                        </div>';
                            $html[] = '                    </div>';
                        }
                    }
                }

                $html[] = '                </div>';
                $html[] = '            </div>';
            }

            $html[] = '        </div>';
            $html[] = '    </div>';

            // Grouped Chart View (Group by configured rule groups)
            $html[] = '    <div id="combined-grouped" class="view-content active">';
            $html[] = '        <div class="chart-container">';

            foreach (self::RULE_GROUPS as $rulePattern) {
                // Find all apps matching this rule pattern
                $matchingApps = array_filter(array_keys($performanceData), function($app) use ($rulePattern) {
                    return strpos($app, $rulePattern) === 0 && strpos($app, 'Test') === false;
                });
                sort($matchingApps);

                if (empty($matchingApps)) continue;

                $html[] = '            <div class="grouped-chart-section">';
                $html[] = '                <div class="grouped-chart-title">' . htmlspecialchars($rulePattern) . '</div>';
                $html[] = '                <div class="chart-bars-container">';

                // Calculate max time across ALL languages in this rule group for consistent scaling
                $allMaxValues = [];
                foreach ($languages as $lang) {
                    $normalTimes = [];
                    foreach ($matchingApps as $app) {
                        if (isset($performanceData[$app][$lang])) {
                            $normalTime = $performanceData[$app][$lang]['normalTimeMs'];
                            if ($normalTime !== null && $normalTime > 0) {
                                $normalTimes[] = $normalTime;
                            }
                        }
                    }
                    $preprocessTimes = [];
                    foreach ($matchingApps as $app) {
                        if (isset($performanceData[$app][$lang])) {
                            $preprocessTime = $performanceData[$app][$lang]['preProcessTimeMs'];
                            if ($preprocessTime !== null && $preprocessTime > 0) {
                                $preprocessTimes[] = $preprocessTime;
                            }
                        }
                    }

                    if (!empty($normalTimes)) $allMaxValues[] = max($normalTimes);
                    if (!empty($preprocessTimes)) $allMaxValues[] = max($preprocessTimes);
                }
                $maxTimeForScale = !empty($allMaxValues) ? max($allMaxValues) : 1.0;

                // For each language, calculate min/avg/max across all apps in this rule group
                foreach ($languages as $lang) {
                    // Collect Normal Engine times
                    $normalTimes = [];
                    foreach ($matchingApps as $app) {
                        if (isset($performanceData[$app][$lang])) {
                            $normalTime = $performanceData[$app][$lang]['normalTimeMs'];
                            if ($normalTime !== null && $normalTime > 0) {
                                $normalTimes[] = $normalTime;
                            }
                        }
                    }

                    // Collect PreProcess Engine times
                    $preprocessTimes = [];
                    foreach ($matchingApps as $app) {
                        if (isset($performanceData[$app][$lang])) {
                            $preprocessTime = $performanceData[$app][$lang]['preProcessTimeMs'];
                            if ($preprocessTime !== null && $preprocessTime > 0) {
                                $preprocessTimes[] = $preprocessTime;
                            }
                        }
                    }

                    if (empty($normalTimes) && empty($preprocessTimes)) continue;

                    // Calculate aggregates
                    $normalMin = !empty($normalTimes) ? min($normalTimes) : null;
                    $normalAvg = !empty($normalTimes) ? array_sum($normalTimes) / count($normalTimes) : null;
                    $normalMax = !empty($normalTimes) ? max($normalTimes) : null;

                    $preprocessMin = !empty($preprocessTimes) ? min($preprocessTimes) : null;
                    $preprocessAvg = !empty($preprocessTimes) ? array_sum($preprocessTimes) / count($preprocessTimes) : null;
                    $preprocessMax = !empty($preprocessTimes) ? max($preprocessTimes) : null;

                    $html[] = '                    <div class="chart-bar-wrapper">';
                    $html[] = '                        <div class="chart-bar-label">' . htmlspecialchars($lang) . '</div>';
                    $html[] = '                        <div style="position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;">';

                    // Normal Engine Bar (showing min, avg, max as segments)
                    if ($normalMin !== null && $normalAvg !== null && $normalMax !== null) {
                        $minWidth = ($normalMin / $maxTimeForScale) * 100;
                        $avgWidth = ($normalAvg / $maxTimeForScale) * 100;
                        $maxWidth = ($normalMax / $maxTimeForScale) * 100;

                        $html[] = '                            <div style="position: absolute; left: 0; top: 0; width: ' . $maxWidth . '%; height: 15px; background-color: #90EE90; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="' . htmlspecialchars($lang) . ' Normal Max: ' . number_format($normalMax, 2) . 'ms"></div>';
                        $html[] = '                            <div style="position: absolute; left: 0; top: 0; width: ' . $avgWidth . '%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="' . htmlspecialchars($lang) . ' Normal Avg: ' . number_format($normalAvg, 2) . 'ms"></div>';
                        $html[] = '                            <div style="position: absolute; left: 0; top: 0; width: ' . $minWidth . '%; height: 15px; background-color: #4CAF50; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="' . htmlspecialchars($lang) . ' Normal Min: ' . number_format($normalMin, 2) . 'ms"></div>';

                        $labelStyle = $maxWidth > 85
                            ? 'position: absolute; right: calc(100% - ' . $maxWidth . '% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;'
                            : 'position: absolute; left: calc(' . $maxWidth . '% + 5px); top: 0; font-size: 11px; color: #4CAF50; font-weight: 600; white-space: nowrap;';
                        $html[] = '                            <span style="' . $labelStyle . '">N: ' . number_format($normalMin, 2) . '/' . number_format($normalAvg, 2) . '/' . number_format($normalMax, 2) . '</span>';
                    }

                    // PreProcess Engine Bar (showing min, avg, max as segments)
                    if ($preprocessMin !== null && $preprocessAvg !== null && $preprocessMax !== null) {
                        $minWidth = ($preprocessMin / $maxTimeForScale) * 100;
                        $avgWidth = ($preprocessAvg / $maxTimeForScale) * 100;
                        $maxWidth = ($preprocessMax / $maxTimeForScale) * 100;

                        $html[] = '                            <div style="position: absolute; left: 0; top: 15px; width: ' . $maxWidth . '%; height: 15px; background-color: #FFB6C6; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="' . htmlspecialchars($lang) . ' PreProcess Max: ' . number_format($preprocessMax, 2) . 'ms"></div>';
                        $html[] = '                            <div style="position: absolute; left: 0; top: 15px; width: ' . $avgWidth . '%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="' . htmlspecialchars($lang) . ' PreProcess Avg: ' . number_format($preprocessAvg, 2) . 'ms"></div>';
                        $html[] = '                            <div style="position: absolute; left: 0; top: 15px; width: ' . $minWidth . '%; height: 15px; background-color: #2196F3; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="' . htmlspecialchars($lang) . ' PreProcess Min: ' . number_format($preprocessMin, 2) . 'ms"></div>';

                        $labelStyle = $maxWidth > 85
                            ? 'position: absolute; right: calc(100% - ' . $maxWidth . '% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;'
                            : 'position: absolute; left: calc(' . $maxWidth . '% + 5px); top: 15px; font-size: 11px; color: #2196F3; font-weight: 600; white-space: nowrap;';
                        $html[] = '                            <span style="' . $labelStyle . '">P: ' . number_format($preprocessMin, 2) . '/' . number_format($preprocessAvg, 2) . '/' . number_format($preprocessMax, 2) . '</span>';
                    }

                    $html[] = '                        </div>';
                    $html[] = '                    </div>';
                }

                $html[] = '                </div>';
                $html[] = '            </div>';
            }

            $html[] = '        </div>';
            $html[] = '    </div>';

            // Table View - Normal Engine
            $html[] = '    <div id="normal-table" class="view-content">';
            $html[] = '    <h2>Normal Engine</h2>';
            $html[] = '    <div class="table-container">';
            $html[] = '    <table>';
            $html[] = '        <tr>';
            $html[] = '            <th>AppSite/AppView</th>';
            foreach ($languages as $lang) {
                $html[] = '            <th>' . htmlspecialchars($lang) . '</th>';
            }
            $html[] = '            <th>OutputSize</th>';
            $html[] = '        </tr>';

            $sortedAppSites = array_filter(array_keys($performanceData), function($app) {
                foreach (self::RULE_GROUPS as $rule) {
                    if (strpos($app, $rule) === 0) return true;
                }
                return false;
            });
            sort($sortedAppSites);

            foreach ($sortedAppSites as $app) {
                // Find min, max, and avg time for highlighting (excluding zero values)
                $validTimes = [];
                foreach ($languages as $lang) {
                    if (isset($performanceData[$app][$lang])) {
                        $normalTime = $performanceData[$app][$lang]['normalTimeMs'];
                        if ($normalTime !== null && $normalTime > 0) {
                            $validTimes[] = $normalTime;
                        }
                    }
                }
                $minTime = !empty($validTimes) ? min($validTimes) : null;
                $maxTime = !empty($validTimes) ? max($validTimes) : null;
                $avgTime = !empty($validTimes) ? array_sum($validTimes) / count($validTimes) : null;

                $html[] = '        <tr>';
                $html[] = '            <td>' . htmlspecialchars($app) . '</td>';

                foreach ($languages as $lang) {
                    $timeValue = '-';
                    $cssClass = '';

                    if (isset($performanceData[$app][$lang])) {
                        $normalTime = $performanceData[$app][$lang]['normalTimeMs'];
                        if ($normalTime !== null) {
                            $timeValue = number_format($normalTime, 2);

                            if ($normalTime > 0) {
                                if ($minTime !== null && abs($normalTime - $minTime) < 0.001) {
                                    $cssClass = ' class="best-perf"';
                                } else if ($maxTime !== null && abs($normalTime - $maxTime) < 0.001) {
                                    $cssClass = ' class="worst-perf"';
                                } else if ($avgTime !== null && count($validTimes) > 2) {
                                    usort($validTimes, function($a, $b) use ($avgTime) {
                                        return abs($a - $avgTime) <=> abs($b - $avgTime);
                                    });
                                    if (abs($normalTime - $validTimes[0]) < 0.001) {
                                        $cssClass = ' class="avg-perf"';
                                    }
                                }
                            }
                        }
                    }

                    $html[] = '            <td' . $cssClass . '>' . $timeValue . '</td>';
                }

                // Output size from first available language
                $firstOutputSize = null;
                foreach ($languages as $lang) {
                    if (isset($performanceData[$app][$lang]['outputSize'])) {
                        $firstOutputSize = $performanceData[$app][$lang]['outputSize'];
                        break;
                    }
                }
                $html[] = '            <td>' . ($firstOutputSize !== null ? (string)$firstOutputSize : '-') . '</td>';
                $html[] = '        </tr>';
            }

            $html[] = '    </table>';
            $html[] = '    </div>';
            $html[] = '    </div>';

            // Table View - PreProcess Engine
            $html[] = '    <div id="preprocess-table" class="view-content">';
            $html[] = '    <h2>PreProcess Engine</h2>';
            $html[] = '    <div class="table-container">';
            $html[] = '    <table>';
            $html[] = '        <tr>';
            $html[] = '            <th>AppSite/AppView</th>';
            foreach ($languages as $lang) {
                $html[] = '            <th>' . htmlspecialchars($lang) . '</th>';
            }
            $html[] = '            <th>OutputSize</th>';
            $html[] = '        </tr>';

            foreach ($sortedAppSites as $app) {
                // Find min, max, and avg time for highlighting (excluding zero values)
                $validTimes = [];
                foreach ($languages as $lang) {
                    if (isset($performanceData[$app][$lang])) {
                        $preprocessTime = $performanceData[$app][$lang]['preProcessTimeMs'];
                        if ($preprocessTime !== null && $preprocessTime > 0) {
                            $validTimes[] = $preprocessTime;
                        }
                    }
                }
                $minTime = !empty($validTimes) ? min($validTimes) : null;
                $maxTime = !empty($validTimes) ? max($validTimes) : null;
                $avgTime = !empty($validTimes) ? array_sum($validTimes) / count($validTimes) : null;

                $html[] = '        <tr>';
                $html[] = '            <td>' . htmlspecialchars($app) . '</td>';

                foreach ($languages as $lang) {
                    $timeValue = '-';
                    $cssClass = '';

                    if (isset($performanceData[$app][$lang])) {
                        $preprocessTime = $performanceData[$app][$lang]['preProcessTimeMs'];
                        if ($preprocessTime !== null) {
                            $timeValue = number_format($preprocessTime, 2);

                            if ($preprocessTime > 0) {
                                if ($minTime !== null && abs($preprocessTime - $minTime) < 0.001) {
                                    $cssClass = ' class="best-perf"';
                                } else if ($maxTime !== null && abs($preprocessTime - $maxTime) < 0.001) {
                                    $cssClass = ' class="worst-perf"';
                                } else if ($avgTime !== null && count($validTimes) > 2) {
                                    usort($validTimes, function($a, $b) use ($avgTime) {
                                        return abs($a - $avgTime) <=> abs($b - $avgTime);
                                    });
                                    if (abs($preprocessTime - $validTimes[0]) < 0.001) {
                                        $cssClass = ' class="avg-perf"';
                                    }
                                }
                            }
                        }
                    }

                    $html[] = '            <td' . $cssClass . '>' . $timeValue . '</td>';
                }

                // Output size from first available language
                $firstOutputSize = null;
                foreach ($languages as $lang) {
                    if (isset($performanceData[$app][$lang]['outputSize'])) {
                        $firstOutputSize = $performanceData[$app][$lang]['outputSize'];
                        break;
                    }
                }
                $html[] = '            <td>' . ($firstOutputSize !== null ? (string)$firstOutputSize : '-') . '</td>';
                $html[] = '        </tr>';
            }

            $html[] = '    </table>';
            $html[] = '    </div>';
            $html[] = '    </div>';

            // Add JavaScript for view switching
            $html[] = '    <script>';
            $html[] = '        document.addEventListener("DOMContentLoaded", function() {';
            $html[] = '            const buttons = document.querySelectorAll(".view-btn");';
            $html[] = '            buttons.forEach(btn => {';
            $html[] = '                btn.addEventListener("click", function() {';
            $html[] = '                    const view = this.getAttribute("data-view");';
            $html[] = '                    // Remove active class from all buttons';
            $html[] = '                    buttons.forEach(b => b.classList.remove("active"));';
            $html[] = '                    // Add active class to clicked button';
            $html[] = '                    this.classList.add("active");';
            $html[] = '                    // Hide all view contents';
            $html[] = '                    document.querySelectorAll(".view-content").forEach(vc => vc.classList.remove("active"));';
            $html[] = '                    // Show selected views';
            $html[] = '                    if (view === "grouped") {';
            $html[] = '                        document.getElementById("combined-grouped").classList.add("active");';
            $html[] = '                    } else if (view === "chart") {';
            $html[] = '                        document.getElementById("combined-chart").classList.add("active");';
            $html[] = '                    } else if (view === "table") {';
            $html[] = '                        document.getElementById("normal-table").classList.add("active");';
            $html[] = '                        document.getElementById("preprocess-table").classList.add("active");';
            $html[] = '                    }';
            $html[] = '                });';
            $html[] = '            });';
            $html[] = '        });';
            $html[] = '    </script>';
            $html[] = '</body>';
            $html[] = '</html>';

            // Write HTML file to Reports directory
            $reportsDir = $projectRootPath . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'Reports';
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

    public static function getReportEndpoint(ServerRequest $request, Response $response): Response
    {
        $projectRootPath = self::getProjectDirectory();
        try {
            $body = $request->getBody()->getContents();
            $data = json_decode($body, true);

            if (json_last_error() !== JSON_ERROR_NONE) {
                $response->getBody()->write(json_encode(['error' => 'Invalid JSON']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            $fileName = $data['fileName'] ?? null;
            $useLangPrefix = $data['useLangPrefix'] ?? false;
            $langPrefix = $data['langPrefix'] ?? null;

            if (empty($fileName)) {
                $response->getBody()->write(json_encode(['error' => 'Missing required field: fileName']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            // Validate fileName for path traversal
            if (!SecurityValidator::isValidPathComponent($fileName)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in fileName']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            // Validate langPrefix for path traversal if provided
            if ($langPrefix && !SecurityValidator::isValidPathComponent($langPrefix)) {
                $response->getBody()->write(json_encode(['error' => 'Invalid characters in langPrefix']));
                return $response->withStatus(400)->withHeader('Content-Type', 'application/json');
            }

            // Construct file path
            $prefix = ($useLangPrefix && $langPrefix) ? $langPrefix . '_' : '';
            $fullFileName = $prefix . $fileName;
            $reportsDir = $projectRootPath . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'Reports';
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

    /**
     * POST /api/save-log - Save a log file (browser-callable)
     * Mirrors C# behavior: expects JSON { context, content }, validates and writes to Analysis/logs/javascript_{context}.log
     */
    public static function saveLogEndpoint(ServerRequest $request, Response $response): Response
    {
        $projectDirectory = self::getProjectDirectory();
        try {
            error_log("[/api/save-log] Endpoint called");
            $body = json_decode((string)$request->getBody(), true);
            $contextName = $body['context'] ?? '';
            $logContent = trim($body['content'] ?? '');

            if (empty($contextName)) {
                $response->getBody()->write('Missing context parameter');
                return $response->withStatus(400);
            }

            // If content is empty after trimming, return success without saving
            if (empty($logContent)) {
                $testResponse = ['success' => true, 'message' => 'No content to save', 'elapsed' => 0, 'testCount' => 0];
                $response->getBody()->write(json_encode($testResponse));
                return $response->withHeader('Content-Type', 'application/json');
            }

            // Validate context name (path component)
            if (!SecurityValidator::isValidPathComponent($contextName)) {
                $response->getBody()->write('Invalid context parameter');
                return $response->withStatus(400)->withHeader('Content-Type', 'text/plain');
            }

            // Validate log content (size and format)
            $logErrorMessage = null;
            if (!SecurityValidator::isValidLogContent($logContent, $logErrorMessage)) {
                $response->getBody()->write($logErrorMessage ?? 'Invalid log content');
                return $response->withStatus(400);
            }

            $logsDir = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'logs';
            if (!is_dir($logsDir)) {
                mkdir($logsDir, 0755, true);
            }

            $logFile = $logsDir . DIRECTORY_SEPARATOR . 'javascript_' . strtolower($contextName) . '.log';
            file_put_contents($logFile, $logContent);

            $testResponse = ['success' => true, 'message' => 'Log saved successfully', 'elapsed' => 0, 'testCount' => 0];
            $response->getBody()->write(json_encode($testResponse));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in /api/save-log: ' . $error->getMessage());
            $response->getBody()->write('Error: ' . $error->getMessage());
            return $response->withStatus(500);
        }
    }

    /**
     * POST /api/save-output - Save an output file (browser-callable)
     * Mirrors C# behavior: expects JSON { appSite, appView?, engineType, html }
     */
    public static function saveOutputEndpoint(ServerRequest $request, Response $response): Response
    {
        $projectDirectory = self::getProjectDirectory();
        try {
            error_log("[/api/save-output] Endpoint called");
            $requestBody = (string)$request->getBody();
            error_log(sprintf("[/api/save-output] Request body length: %d", strlen($requestBody)));

            $body = json_decode($requestBody, true);
            $appSite = $body['appSite'] ?? '';
            $appView = $body['appView'] ?? '';
            $engineType = $body['engineType'] ?? '';
            $htmlContent = $body['html'] ?? '';

            error_log(sprintf("[/api/save-output] Parsed: appSite=%s, appView=%s, engineType=%s, htmlLength=%d", $appSite, $appView, $engineType, strlen($htmlContent)));

            if (empty($appSite) || empty($engineType) || empty($htmlContent)) {
                error_log("[/api/save-output] Missing required parameters");
                $response->getBody()->write('Missing required parameters');
                return $response->withStatus(400);
            }

            $rootDirPath = __DIR__ . DIRECTORY_SEPARATOR . 'wwwroot';

            // Validate AppSite against allowlist
            $validAppSites = SecurityValidator::getValidAppSites($rootDirPath);
            $validAppSitesLower = array_map('strtolower', $validAppSites);
            if (!in_array(strtolower($appSite), $validAppSitesLower, true)) {
                error_log("[/api/save-output] Invalid AppSite: {$appSite}");
                $response->getBody()->write('Invalid AppSite value');
                return $response->withStatus(400);
            }

            // Validate engine type against allowlist
            if (!SecurityValidator::isValidEngineType($engineType)) {
                error_log("[/api/save-output] Invalid engineType: {$engineType}");
                $response->getBody()->write('Invalid engine type');
                return $response->withStatus(400);
            }

            // Validate parameters (path components)
            if (!SecurityValidator::isValidPathComponent($appSite)) {
                error_log("[/api/save-output] Invalid AppSite path component: {$appSite}");
                $response->getBody()->write('Invalid AppSite parameter');
                return $response->withStatus(400);
            }
            if (!empty($appView) && !SecurityValidator::isValidPathComponent($appView)) {
                error_log("[/api/save-output] Invalid AppView path component: {$appView}");
                $response->getBody()->write('Invalid AppView parameter');
                return $response->withStatus(400);
            }
            if (!SecurityValidator::isValidPathComponent($engineType)) {
                error_log("[/api/save-output] Invalid engineType path component: {$engineType}");
                $response->getBody()->write('Invalid engineType parameter');
                return $response->withStatus(400);
            }

            // Validate output size against template size + buffer
            $templateTotalSize = SecurityValidator::getTemplateTotalSize($appSite, $appView ?? '');
            $outputSize = strlen($htmlContent);
            $maxAllowedSize = $templateTotalSize + SecurityValidator::OUTPUT_SIZE_BUFFER;
            error_log(sprintf("[/api/save-output] Size validation: output=%d, template=%d, buffer=%d, max=%d", $outputSize, $templateTotalSize, SecurityValidator::OUTPUT_SIZE_BUFFER, $maxAllowedSize));

            if (!SecurityValidator::isValidOutputSizeWithBuffer($htmlContent, $templateTotalSize)) {
                $errorMsg = sprintf('Save output failed: output size (%d bytes) exceeds max size allowed (%d bytes = template %d + buffer %d)', $outputSize, $maxAllowedSize, $templateTotalSize, SecurityValidator::OUTPUT_SIZE_BUFFER);
                error_log("[/api/save-output] {$errorMsg}");
                $response->getBody()->write($errorMsg);
                return $response->withStatus(400);
            }

            $outputDir = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'output';
            if (!is_dir($outputDir)) {
                mkdir($outputDir, 0755, true);
            }

            $appViewSuffix = empty($appView) ? '' : '_' . $appView;
            $engineSuffix = strtolower($engineType);
            $outputFile = $outputDir . DIRECTORY_SEPARATOR . sprintf('javascript_%s%s_%s.html', $appSite, $appViewSuffix, $engineSuffix);
            file_put_contents($outputFile, $htmlContent);
            error_log('[/api/save-output] Success! Output saved to: ' . $outputFile);

            $testResponse = ['success' => true, 'message' => 'Output saved successfully', 'elapsed' => 0, 'testCount' => 0];
            $response->getBody()->write(json_encode($testResponse));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in /api/save-output: ' . $error->getMessage());
            $response->getBody()->write('Error: ' . $error->getMessage());
            return $response->withStatus(500);
        }
    }

    /**
     * POST /api/test-results - Save test results and generate HTML/JSON reports
     */
    public static function saveTestResultsEndpoint(ServerRequest $request, Response $response): Response
    {
        $projectDirectory = self::getProjectDirectory();
        try {
            $summaryRows = json_decode((string)$request->getBody(), true);
            if (!is_array($summaryRows)) {
                $response->getBody()->write('Invalid test results format');
                return $response->withStatus(400);
            }
            $rootDirPath = __DIR__ . DIRECTORY_SEPARATOR . 'wwwroot';
            $reportsPath = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsPath)) {
                mkdir($reportsPath, 0755, true);
            }

            // Get test type from query parameter
            $queryParams = $request->getQueryParams();
            $testType = $queryParams['testType'] ?? 'standardtest';
            $testTypeFile = strtolower(str_replace([' ', '-'], '', $testType));

            // Generate HTML table matching Rust format
            $formattedTestType = strtoupper(str_replace('test', ' TEST', $testType));
            $htmlParts = [];
            $htmlParts[] = '<!DOCTYPE html>';
            $htmlParts[] = '<html>';
            $htmlParts[] = '<head>';
            $htmlParts[] = '    <meta charset="UTF-8">';
            $htmlParts[] = '    <meta name="viewport" content="width=device-width, initial-scale=1.0">';
            $htmlParts[] = "    <title>JavaScript {$formattedTestType}</title>";
            $htmlParts[] = '    <style>';
            $htmlParts[] = '        body { font-family: Arial, sans-serif; margin: 20px; }';
            $htmlParts[] = '        h1 { color: #333; }';
            $htmlParts[] = '        .table-container { overflow-x: auto; }';
            $htmlParts[] = '        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }';
            $htmlParts[] = '        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }';
            $htmlParts[] = '        th { background-color: #4CAF50; color: white; }';
            $htmlParts[] = '        tr:nth-child(even) { background-color: #f2f2f2; }';
            $htmlParts[] = '        .pass { color: green; font-weight: bold; }';
            $htmlParts[] = '        .fail { color: red; font-weight: bold; }';
            $htmlParts[] = '        @media (max-width: 768px) {';
            $htmlParts[] = '            body { margin: 10px; }';
            $htmlParts[] = '            th, td { padding: 8px; font-size: 14px; }';
            $htmlParts[] = '            h1 { font-size: 24px; }';
            $htmlParts[] = '        }';
            $htmlParts[] = '    </style>';
            $htmlParts[] = '</head>';
            $htmlParts[] = '<body>';
            $htmlParts[] = "    <h1>JavaScript {$formattedTestType}</h1>";
            $htmlParts[] = '    <div class="meta" style="color: #666; font-style: italic; margin-bottom: 10px;">Generated: ' . gmdate('Y-m-d H:i:s') . ' UTC</div>';
            $htmlParts[] = '    <div class="table-container">';
            $htmlParts[] = '    <table>';
            $htmlParts[] = '        <tr>';
            $htmlParts[] = '            <th>AppSite</th>';
            $htmlParts[] = '            <th>AppFile</th>';
            $htmlParts[] = '            <th>AppView</th>';
            $htmlParts[] = '            <th>OutputMatch</th>';
            $htmlParts[] = '            <th>ViewUnMatch</th>';
            $htmlParts[] = '            <th>Error</th>';
            $htmlParts[] = '        </tr>';

            foreach ($summaryRows as $row) {
                $appSite = $row['AppSite'] ?? $row['app_site'] ?? $row['appSite'] ?? '';
                $appFile = $row['AppFile'] ?? $row['app_file'] ?? $row['appFile'] ?? '';
                $appView = $row['AppView'] ?? $row['app_view'] ?? $row['appView'] ?? '';
                $normalPreProcess = $row['NormalPreProcess'] ?? $row['normal_pre_process'] ?? $row['normalPreProcess'] ?? '';
                $crossViewUnMatch = $row['CrossViewUnMatch'] ?? $row['cross_view_un_match'] ?? $row['crossViewUnMatch'] ?? '';
                $errorMsg = $row['Error'] ?? $row['error'] ?? '';

                $outputMatchClass = $normalPreProcess === 'PASS' ? 'pass' : ($normalPreProcess === 'FAIL' ? 'fail' : '');
                $viewUnmatchClass = $crossViewUnMatch === 'PASS' ? 'pass' : ($crossViewUnMatch === 'FAIL' ? 'fail' : '');

                $htmlParts[] = '        <tr>';
                $htmlParts[] = "            <td>{$appSite}</td>";
                $htmlParts[] = "            <td>{$appFile}</td>";
                $htmlParts[] = "            <td>{$appView}</td>";
                $htmlParts[] = "            <td class=\"{$outputMatchClass}\">{$normalPreProcess}</td>";
                $htmlParts[] = "            <td class=\"{$viewUnmatchClass}\">{$crossViewUnMatch}</td>";
                $htmlParts[] = "            <td>{$errorMsg}</td>";
                $htmlParts[] = '        </tr>';
            }

            $htmlParts[] = '    </table>';
            $htmlParts[] = '    </div>';
            $htmlParts[] = '</body>';
            $htmlParts[] = '</html>';

            // Save HTML
            $htmlFile = $reportsPath . DIRECTORY_SEPARATOR . "javascript_{$testTypeFile}_Summary.html";
            file_put_contents($htmlFile, implode("\n", $htmlParts));

            // Save JSON
            $jsonFile = $reportsPath . DIRECTORY_SEPARATOR . "javascript_{$testTypeFile}_Summary.json";
            file_put_contents($jsonFile, json_encode($summaryRows, JSON_PRETTY_PRINT));
            $response->getBody()->write(json_encode(['message' => 'Test results saved successfully']));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in /api/test-results: ' . $error->getMessage());
            $response->getBody()->write('Error: ' . $error->getMessage());
            return $response->withStatus(500);
        }
    }

    /**
     * POST /api/performance-results - Save performance results and generate HTML/JSON reports
     */
    public static function savePerformanceResultsEndpoint(ServerRequest $request, Response $response): Response
    {
        $projectDirectory = self::getProjectDirectory();
        try {
            $summaryRows = json_decode((string)$request->getBody(), true);
            if (!is_array($summaryRows)) {
                $response->getBody()->write('Invalid performance results format');
                return $response->withStatus(400);
            }

            error_log(sprintf('POST /api/performance-results called with %d rows', count($summaryRows)));

            // Validate each row
            $rootDirPath = __DIR__ . DIRECTORY_SEPARATOR . 'wwwroot';
            $validAppSites = SecurityValidator::getValidAppSites($rootDirPath);
            foreach ($summaryRows as $row) {
                // Validate AppSite is in allowlist
                if (!empty($row['AppSite'] ?? $row['appSite'] ?? null)) {
                    $appSite = $row['AppSite'] ?? $row['appSite'];
                    $validAppSitesLower = array_map('strtolower', $validAppSites);
                    if (!in_array(strtolower($appSite), $validAppSitesLower, true)) {
                        $response->getBody()->write("Invalid AppSite: {$appSite}");
                        return $response->withStatus(400);
                    }
                }
                // Validate parameter lengths (256 char limit)
                if (!empty($row['AppSite'] ?? $row['appSite'] ?? null) && !SecurityValidator::isValidPathComponent($row['AppSite'] ?? $row['appSite'])) {
                    $response->getBody()->write('Invalid AppSite parameter');
                    return $response->withStatus(400);
                }
                if (!empty($row['AppFile'] ?? $row['appFile'] ?? null) && !SecurityValidator::isValidPathComponent($row['AppFile'] ?? $row['appFile'])) {
                    $response->getBody()->write('Invalid AppFile parameter');
                    return $response->withStatus(400);
                }
                if (!empty($row['AppView'] ?? $row['appView'] ?? null) && !SecurityValidator::isValidPathComponent($row['AppView'] ?? $row['appView'])) {
                    $response->getBody()->write('Invalid AppView parameter');
                    return $response->withStatus(400);
                }
            }

            $reportsPath = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsPath)) {
                mkdir($reportsPath, 0755, true);
            }

            // Generate HTML table
            $htmlParts = [];
            $htmlParts[] = '<html><head><title>Client-Side Performance Summary Table</title>';
            $htmlParts[] = '<style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}.meta{color:#666;font-style:italic;margin-bottom:10px;}</style></head><body>';
            $htmlParts[] = '<h2>Client-Side JavaScript PERFORMANCE SUMMARY TABLE</h2>';
            $htmlParts[] = '<div class="meta">Generated: ' . gmdate('Y-m-d H:i:s') . ' UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>';
            $htmlParts[] = '<table>';
            $htmlParts[] = '<tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th><th>ScnTime(ms)</th><th>Elapsed(ms)</th></tr>';

            foreach ($summaryRows as $row) {
                $appSite = $row['AppSite'] ?? $row['appSite'] ?? '';
                $appView = $row['AppView'] ?? $row['appView'] ?? '';
                $normalTimeMs = (float)($row['NormalTimeMs'] ?? $row['normalTimeMs'] ?? 0);
                $preProcessTimeMs = (float)($row['PreProcessTimeMs'] ?? $row['preProcessTimeMs'] ?? 0);
                $resultsMatch = $row['ResultsMatch'] ?? $row['resultsMatch'] ?? '';
                $perfDifference = $row['PerfDifference'] ?? $row['perfDifference'] ?? '';
                $scenarioTotalTimeMs = (int)($row['ScenarioTotalTimeMs'] ?? $row['scenarioTotalTimeMs'] ?? 0);
                $elapsedTimeMs = (int)($row['ElapsedTimeMs'] ?? $row['elapsedTimeMs'] ?? 0);

                $htmlParts[] = '<tr>';
                $htmlParts[] = '<td>' . htmlspecialchars($appSite) . '</td>';
                $htmlParts[] = '<td>' . htmlspecialchars($appView) . '</td>';
                $htmlParts[] = '<td>' . number_format($normalTimeMs, 2) . '</td>';
                $htmlParts[] = '<td>' . number_format($preProcessTimeMs, 2) . '</td>';
                $htmlParts[] = '<td>' . htmlspecialchars($resultsMatch) . '</td>';
                $htmlParts[] = '<td>' . htmlspecialchars($perfDifference) . '</td>';
                $htmlParts[] = '<td>' . $scenarioTotalTimeMs . '</td>';
                $htmlParts[] = '<td>' . $elapsedTimeMs . '</td>';
                $htmlParts[] = '</tr>';
            }

            $htmlParts[] = '</table></body></html>';

            // Save HTML
            $htmlFile = $reportsPath . DIRECTORY_SEPARATOR . "javascript_perfsummary.html";
            file_put_contents($htmlFile, implode("\n", $htmlParts));
            error_log("Performance summary HTML saved to: {$htmlFile}");

            // Save JSON
            $jsonFile = $reportsPath . DIRECTORY_SEPARATOR . "javascript_perfsummary.json";
            file_put_contents($jsonFile, json_encode($summaryRows, JSON_PRETTY_PRINT));
            error_log("Performance summary JSON saved to: {$jsonFile}");

            $response->getBody()->write(json_encode(['message' => 'Performance results saved successfully']));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in /api/performance-results: ' . $error->getMessage());
            $response->getBody()->write('Error: ' . $error->getMessage());
            return $response->withStatus(500);
        }
    }

    /**
     * Maps all assembler test endpoints to the Slim app
     * @param \Slim\App $app - Slim application instance
     */
    public static function mapAssemblerTestEndpoints($app) {
        $app->post('/test/standard', function ($request, $response) {
            return AssemblerTestEndpoint::testStandardEndpoint($request, $response);
        })->setName('RunStandardTests');

        $app->post('/test/advanced', function ($request, $response) {
            return AssemblerTestEndpoint::testAdvancedEndpoint($request, $response);
        })->setName('RunAdvancedTests');

        $app->post('/test/performance', function ($request, $response) {
            return AssemblerTestEndpoint::testPerformanceEndpoint($request, $response);
        })->setName('RunPerformanceTests');

        $app->post('/test/consolidate-performance', function ($request, $response) {
            return AssemblerTestEndpoint::testConsolidatePerformanceEndpoint($request, $response);
        })->setName('ConsolidatePerformanceTests');

        $app->post('/api/report', function ($request, $response) {
            return AssemblerTestEndpoint::getReportEndpoint($request, $response);
        })->setName('GetReport');

        $app->post('/api/test-results', function ($request, $response) {
            return AssemblerTestEndpoint::saveTestResultsEndpoint($request, $response);
        })->setName('SaveTestResults');

        $app->post('/api/performance-results', function ($request, $response) {
            return AssemblerTestEndpoint::savePerformanceResultsEndpoint($request, $response);
        })->setName('SavePerformanceResults');

        $app->post('/api/save-log', function ($request, $response) {
            return AssemblerTestEndpoint::saveLogEndpoint($request, $response);
        })->setName('SaveLog');

        $app->post('/api/save-output', function ($request, $response) {
            return AssemblerTestEndpoint::saveOutputEndpoint($request, $response);
        })->setName('SaveOutput');
    }

}

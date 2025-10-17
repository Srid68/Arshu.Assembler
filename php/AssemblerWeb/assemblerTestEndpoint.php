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

class AssemblerTestEndpoint
{
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
            $html[] = '<div class="meta" style="color:#666;font-style:italic;margin-bottom:10px;">Generated: ' . gmdate('Y-m-d H:i:s') . ' UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>';

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

    /**
     * POST /api/save-log - Save a log file (browser-callable)
     * Mirrors C# behavior: expects JSON { context, content }, validates and writes to template_analysis/logs/javascript_{context}.log
     */
    public static function saveLogEndpoint(ServerRequest $request, Response $response): Response
    {
        try {
            error_log("[/api/save-log] Endpoint called");
            $body = json_decode((string)$request->getBody(), true);
            $contextName = $body['context'] ?? '';
            $logContent = $body['content'] ?? '';

            if (empty($contextName) || empty($logContent)) {
                $response->getBody()->write('Missing context or content parameter');
                return $response->withStatus(400);
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

            $projectDirectory = dirname(__DIR__, 1);
            $logsDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'logs';
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
        try {
            error_log("[/api/save-output] Endpoint called");
            $body = json_decode((string)$request->getBody(), true);
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

            $projectDirectory = dirname(__DIR__, 1);
            $outputDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'output';
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
        try {
            $summaryRows = json_decode((string)$request->getBody(), true);
            if (!is_array($summaryRows)) {
                $response->getBody()->write('Invalid test results format');
                return $response->withStatus(400);
            }
            $rootDirPath = __DIR__ . DIRECTORY_SEPARATOR . 'wwwroot';
            $projectDirectory = dirname(__DIR__, 1);
            $reportsPath = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsPath)) {
                mkdir($reportsPath, 0755, true);
            }
            // Save HTML
            $now = date('Ymd_His');
            $htmlFile = $reportsPath . DIRECTORY_SEPARATOR . "php_test_summary_{$now}.html";
            $html = '<html><body><pre>' . json_encode($summaryRows, JSON_PRETTY_PRINT) . '</pre></body></html>';
            file_put_contents($htmlFile, $html);
            // Save JSON
            $jsonFile = $reportsPath . DIRECTORY_SEPARATOR . "php_test_summary_{$now}.json";
            file_put_contents($jsonFile, json_encode($summaryRows, JSON_PRETTY_PRINT));
            // Save log
            $logFile = $reportsPath . DIRECTORY_SEPARATOR . "php_test_results_{$now}.log";
            file_put_contents($logFile, "Saved test results at {$now}\n");
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
        try {
            $summaryRows = json_decode((string)$request->getBody(), true);
            if (!is_array($summaryRows)) {
                $response->getBody()->write('Invalid performance results format');
                return $response->withStatus(400);
            }
            $rootDirPath = __DIR__ . DIRECTORY_SEPARATOR . 'wwwroot';
            $projectDirectory = dirname(__DIR__, 1);
            $reportsPath = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsPath)) {
                mkdir($reportsPath, 0755, true);
            }
            // Save HTML
            $now = date('Ymd_His');
            $htmlFile = $reportsPath . DIRECTORY_SEPARATOR . "php_perf_summary_{$now}.html";
            $html = '<html><body><pre>' . json_encode($summaryRows, JSON_PRETTY_PRINT) . '</pre></body></html>';
            file_put_contents($htmlFile, $html);
            // Save JSON
            $jsonFile = $reportsPath . DIRECTORY_SEPARATOR . "php_perf_summary_{$now}.json";
            file_put_contents($jsonFile, json_encode($summaryRows, JSON_PRETTY_PRINT));
            // Save log
            $logFile = $reportsPath . DIRECTORY_SEPARATOR . "php_perf_results_{$now}.log";
            file_put_contents($logFile, "Saved performance results at {$now}\n");
            $response->getBody()->write(json_encode(['message' => 'Performance results saved successfully']));
            return $response->withHeader('Content-Type', 'application/json');
        } catch (Exception $error) {
            error_log('Error in /api/performance-results: ' . $error->getMessage());
            $response->getBody()->write('Error: ' . $error->getMessage());
            return $response->withStatus(500);
        }
    }

}

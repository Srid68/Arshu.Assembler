<?php

namespace Assembler\Performance;

use Assembler\Loader\LoaderNormal;
use Assembler\Loader\LoaderPreProcess;
use Assembler\Engine\EngineNormal;
use Assembler\Engine\EnginePreProcess;
use Assembler\Common\CommonUtil;

class PerfSummaryRow
{
    public ?string $AppSite;
    public ?string $AppFile;
    public ?string $AppView;
    public int $Iterations;
    public float $NormalTimeMs;
    public float $PreProcessTimeMs;
    public int $OutputSize;
    public ?string $ResultsMatch;
    public ?string $PerfDifference;
    public int $ScenarioTotalTimeMs;
    public int $ElapsedTimeMs;

    public function __construct(?string $appSite = null, ?string $appFile = null, ?string $appView = null, int $iterations = 0, float $normalTimeMs = 0.0, float $preProcessTimeMs = 0.0, int $outputSize = 0, ?string $resultsMatch = null, ?string $perfDifference = null, int $scenarioTotalTimeMs = 0, int $elapsedTimeMs = 0)
    {
        $this->AppSite = $appSite;
        $this->AppFile = $appFile;
        $this->AppView = $appView;
        $this->Iterations = $iterations;
        $this->NormalTimeMs = $normalTimeMs;
        $this->PreProcessTimeMs = $preProcessTimeMs;
        $this->OutputSize = $outputSize;
        $this->ResultsMatch = $resultsMatch;
        $this->PerfDifference = $perfDifference;
        $this->ScenarioTotalTimeMs = $scenarioTotalTimeMs;
        $this->ElapsedTimeMs = $elapsedTimeMs;
    }
}

class PerformanceUtils
{
    /**
     * @param string $assemblerWebDirPath
     * @param array $scenarios
     * @param bool $skipDetails
     * @param bool $enableJsonProcessing
     * @return PerfSummaryRow[]
     */
    public static function runPerformanceComparison(string $assemblerWebDirPath, array $scenarios, bool $skipDetails = false, bool $enableJsonProcessing = true): array
    {
        $startTime = microtime(true);

        if (empty($assemblerWebDirPath)) {
            echo "❌ No assemblerWebDirPath passed\n";
            return [];
        }

        if (empty($scenarios)) {
            echo "❌ No scenarios passed\n";
            return [];
        }

        $iterations = 1000;
        $summaryRows = [];

        foreach ($scenarios as $scenario) {
            $scenarioStartTime = microtime(true);
            $testAppSite = $scenario->appSite;
            $appFileName = $scenario->appFile;
            $appView = $scenario->appView;
            $appViewPrefix = !empty($appView) ? substr($appView, 0, min(strlen($appView), 6)) : '';

            if (!$skipDetails) {
                echo str_repeat('-', 60) . "\n";
                echo "[PHP] Testing: AppSite={$testAppSite}, AppFile={$appFileName}, AppView={$appView}\n";
                echo "[PHP] Iterations: " . number_format($iterations) . "\n";
            }

            LoaderNormal::clearCache();
            LoaderPreProcess::clearCache();
            $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $testAppSite);
            $siteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $testAppSite);

            if ($templates === null || count($templates) === 0) {
                continue;
            }

            $mainTemplateKey = strtolower($testAppSite . '_' . $appFileName);
            if (!isset($templates[$mainTemplateKey])) {
                continue;
            }

            $normalEngine = new EngineNormal();
            $normalEngine->setAppViewPrefix($appViewPrefix);

            // Warmup - run a few iterations first to ensure consistent performance
            for ($warmup = 0; $warmup < 100; $warmup++) {
                $normalEngine->mergeTemplates($testAppSite, $appFileName, $appView, $templates, $enableJsonProcessing);
            }

            $start = microtime(true);
            $resultNormal = '';
            for ($i = 0; $i < $iterations; $i++) {
                $resultNormal = $normalEngine->mergeTemplates($testAppSite, $appFileName, $appView, $templates, $enableJsonProcessing);
            }
            $normalTime = round((microtime(true) - $start) * 1000, 2);

            if (!$skipDetails) {
                $avg = $normalTime / $iterations;
                echo "[PHP] Normal Engine:     {$normalTime}ms | Avg: " . number_format($avg, 3) . "ms/op | Size: " . CommonUtil::utf16Len($resultNormal) . " chars\n";
            }

            LoaderNormal::clearCache();
            LoaderPreProcess::clearCache();
            $preProcessEngine = new EnginePreProcess();
            $preProcessEngine->setAppViewPrefix($appViewPrefix);

            // Warmup for PreProcess engine
            for ($warmup = 0; $warmup < 100; $warmup++) {
                $preProcessEngine->mergeTemplates($testAppSite, $appFileName, $appView, $siteTemplates->templates, $enableJsonProcessing);
            }

            $start = microtime(true);
            $resultPreProcess = '';
            for ($i = 0; $i < $iterations; $i++) {
                $resultPreProcess = $preProcessEngine->mergeTemplates($testAppSite, $appFileName, $appView, $siteTemplates->templates, $enableJsonProcessing);
            }
            $preProcessTime = round((microtime(true) - $start) * 1000, 2);

            if (!$skipDetails) {
                $avg = $preProcessTime / $iterations;
                echo "[PHP] PreProcess Engine: {$preProcessTime}ms | Avg: " . number_format($avg, 3) . "ms/op | Size: " . CommonUtil::utf16Len($resultPreProcess) . " chars\n";

                $difference = round($preProcessTime - $normalTime, 2);
                $differencePercent = $normalTime > 0 ? ($difference / $normalTime) * 100 : 0;
                $signMs = $difference >= 0 ? '+' : '';
                $signPct = $differencePercent >= 0 ? '+' : '';
                $matchStr = $resultNormal === $resultPreProcess ? 'YES' : 'NO';
                echo "[PHP] Performance: {$signMs}{$difference}ms ({$signPct}" . number_format($differencePercent, 1) . "%) | Match: {$matchStr}\n";
            }

            $scenarioTotalTime = round((microtime(true) - $scenarioStartTime) * 1000);
            $elapsedTime = round((microtime(true) - $startTime) * 1000);

            if (!$skipDetails) {
                echo "[PHP] Scenario Total Time: {$scenarioTotalTime}ms | Elapsed: {$elapsedTime}ms\n";
            }

            $summaryRows[] = new PerfSummaryRow(
                $testAppSite,
                $appFileName,
                $appView,
                $iterations,
                $normalTime,
                $preProcessTime,
                CommonUtil::utf16Len($resultNormal),
                ($resultNormal === $resultPreProcess ? "YES" : "NO"),
                $normalTime > 0 ? number_format(($preProcessTime - $normalTime) / $normalTime * 100, 1) . "%" : "0%",
                $scenarioTotalTime,
                $elapsedTime
            );
        }

        if (!$skipDetails) {
            $elapsed = (microtime(true) - $startTime) * 1000;
            echo sprintf("\n========== Performance Testing Completed in %.0fms ==========\n\n", $elapsed);
        }
        return $summaryRows;
    }

    /**
     * @param string $assemblerWebDirPath
     * @param string $projectDirectory
     * @param PerfSummaryRow[] $summaryRows
     */
    public static function printPerfSummaryTable(string $assemblerWebDirPath, string $projectDirectory, array $summaryRows): void
    {
        if (empty($summaryRows)) {
            return;
        }
        echo "\n==================== PHP PERFORMANCE SUMMARY ====================\n\n";

        $headers = ['AppSite', 'AppView', 'Normal(ms)', 'PreProc(ms)', 'Match', 'PerfDiff', 'ScnTime(ms)', 'Elapsed(ms)'];
        $colCount = count($headers);
        $widths = array_fill(0, $colCount, 0);
        for ($i = 0; $i < $colCount; $i++) {
            $widths[$i] = strlen($headers[$i]);
        }
        foreach ($summaryRows as $row) {
            $widths[0] = max($widths[0], strlen($row->AppSite ?? ''));
            $widths[1] = max($widths[1], strlen($row->AppView ?? ''));
            $widths[2] = max($widths[2], strlen(number_format($row->NormalTimeMs, 2)));
            $widths[3] = max($widths[3], strlen(number_format($row->PreProcessTimeMs, 2)));
            $widths[4] = max($widths[4], strlen($row->ResultsMatch ?? ''));
            $widths[5] = max($widths[5], strlen($row->PerfDifference ?? ''));
            $widths[6] = max($widths[6], strlen((string)$row->ScenarioTotalTimeMs));
            $widths[7] = max($widths[7], strlen((string)$row->ElapsedTimeMs));
        }
        // Print header
        echo '| ';
        for ($i = 0; $i < $colCount; $i++) {
            echo str_pad($headers[$i], $widths[$i]);
            if ($i < $colCount - 1) echo ' | ';
        }
        echo " |\n";
        // Print divider
        echo '|';
        for ($i = 0; $i < $colCount; $i++) {
            echo ' ' . str_repeat('-', $widths[$i]) . ' ';
            if ($i < $colCount - 1) echo '|';
        }
        echo "|\n";
        // Print rows
        foreach ($summaryRows as $row) {
            echo '| ';
            echo str_pad($row->AppSite ?? '', $widths[0]);
            echo ' | ';
            echo str_pad($row->AppView ?? '', $widths[1]);
            echo ' | ';
            echo str_pad(number_format($row->NormalTimeMs, 2), $widths[2]);
            echo ' | ';
            echo str_pad(number_format($row->PreProcessTimeMs, 2), $widths[3]);
            echo ' | ';
            echo str_pad($row->ResultsMatch ?? '', $widths[4]);
            echo ' | ';
            echo str_pad($row->PerfDifference ?? '', $widths[5]);
            echo ' | ';
            echo str_pad((string)$row->ScenarioTotalTimeMs, $widths[6]);
            echo ' | ';
            echo str_pad((string)$row->ElapsedTimeMs, $widths[7]);
            echo " |\n";
        }
        // Print bottom divider
        echo '|';
        for ($i = 0; $i < $colCount; $i++) {
            echo ' ' . str_repeat('-', $widths[$i]) . ' ';
            if ($i < $colCount - 1) echo '|';
        }
        echo "|\n";

        // Save HTML file
        try {
            $reportsDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsDir)) {
                mkdir($reportsDir, 0755, true);
            }

            $html = '<html><head><title>PHP Performance Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}.meta{color:#666;font-style:italic;margin-bottom:10px;}</style></head><body>';
            $html .= '<h2>PHP Performance Summary Table</h2>';
            $html .= '<div class="meta">Generated: ' . gmdate('Y-m-d H:i:s') . ' UTC | All times in milliseconds (ms)</div>';
            $html .= '<table>';
            $html .= '<tr>';
            foreach ($headers as $h) {
                $html .= "<th>{$h}</th>";
            }
            $html .= '</tr>';
            foreach ($summaryRows as $row) {
                $html .= '<tr>';
                $html .= "<td>{$row->AppSite}</td>";
                $html .= "<td>{$row->AppView}</td>";
                $html .= "<td>" . number_format($row->NormalTimeMs, 2) . "</td>";
                $html .= "<td>" . number_format($row->PreProcessTimeMs, 2) . "</td>";
                $html .= "<td>{$row->ResultsMatch}</td>";
                $html .= "<td>{$row->PerfDifference}</td>";
                $html .= "<td>{$row->ScenarioTotalTimeMs}</td>";
                $html .= "<td>{$row->ElapsedTimeMs}</td>";
                $html .= '</tr>';
            }
            $html .= '</table></body></html>';

            $outFile = $reportsDir . DIRECTORY_SEPARATOR . 'php_perfsummary.html';
            file_put_contents($outFile, $html);
            echo "Performance summary HTML saved to: {$outFile}\n";
        } catch (\Exception $ex) {
            echo "Error saving performance summary HTML: {$ex->getMessage()}\n";
        }

        // Save JSON file
        try {
            $reportsDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsDir)) {
                mkdir($reportsDir, 0755, true);
            }

            $jsonFile = $reportsDir . DIRECTORY_SEPARATOR . 'php_perfsummary.json';
            $jsonData = array_map(function($row) {
                return [
                    'AppSite' => $row->AppSite,
                    'AppFile' => $row->AppFile,
                    'AppView' => $row->AppView,
                    'Iterations' => $row->Iterations,
                    'NormalTimeMs' => round($row->NormalTimeMs, 2),
                    'PreProcessTimeMs' => round($row->PreProcessTimeMs, 2),
                    'OutputSize' => $row->OutputSize,
                    'ResultsMatch' => $row->ResultsMatch,
                    'PerfDifference' => $row->PerfDifference,
                    'ScenarioTotalTimeMs' => $row->ScenarioTotalTimeMs,
                    'ElapsedTimeMs' => $row->ElapsedTimeMs
                ];
            }, $summaryRows);
            $json = json_encode($jsonData, JSON_PRETTY_PRINT);
            file_put_contents($jsonFile, $json);
            echo "Performance summary JSON saved to: {$jsonFile}\n";
        } catch (\Exception $ex) {
            echo "Error saving performance summary JSON: {$ex->getMessage()}\n";
        }
    }
}

?>
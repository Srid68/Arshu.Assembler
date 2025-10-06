<?php

namespace Assembler\TemplatePerformance;

use Assembler\TemplateLoader\LoaderNormal;
use Assembler\TemplateLoader\LoaderPreProcess;
use Assembler\TemplateEngine\EngineNormal;
use Assembler\TemplateEngine\EnginePreProcess;

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

    public function __construct(?string $appSite = null, ?string $appFile = null, ?string $appView = null, int $iterations = 0, float $normalTimeMs = 0.0, float $preProcessTimeMs = 0.0, int $outputSize = 0, ?string $resultsMatch = null, ?string $perfDifference = null)
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
    }
}

class PerformanceUtils
{
    /**
     * @param string $assemblerWebDirPath
     * @param bool $enableJsonProcessing
     * @param string|null $appSiteFilter
     * @param bool $skipDetails
     * @return PerfSummaryRow[]
     */
    public static function runPerformanceComparison(string $assemblerWebDirPath, bool $enableJsonProcessing, ?string $appSiteFilter = null, bool $skipDetails = false): array
    {
        $startTime = microtime(true);
        echo "\n========== RunPerformanceComparison ==========\n";
        $iterations = 1000;
        $appSitesPath = $assemblerWebDirPath . DIRECTORY_SEPARATOR . 'AppSites';
        $summaryRows = [];
        if (!is_dir($appSitesPath)) {
            return $summaryRows;
        }
        $allAppSiteDirs = array_filter(scandir($appSitesPath), function($dir) use ($appSitesPath) {
            return $dir !== '.' && $dir !== '..' && is_dir($appSitesPath . DIRECTORY_SEPARATOR . $dir);
        });
        $appSites = array_map('basename', $allAppSiteDirs);
        foreach ($appSites as $testAppSite) {
            if ($appSiteFilter !== null && strcasecmp($testAppSite, $appSiteFilter) !== 0) {
                continue;
            }
            $appSiteDir = $appSitesPath . DIRECTORY_SEPARATOR . $testAppSite;
            $htmlFiles = glob($appSiteDir . DIRECTORY_SEPARATOR . '*.html');
            foreach ($htmlFiles as $htmlFilePath) {
                $appFileName = basename($htmlFilePath, '.html');
                try {
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
                    $appViewScenarios = [['AppView' => '', 'AppViewPrefix' => '']];
                    $viewsPath = $appSiteDir . DIRECTORY_SEPARATOR . 'Views';
                    if (is_dir($viewsPath)) {
                        $viewFiles = glob($viewsPath . DIRECTORY_SEPARATOR . '*.html');
                        foreach ($viewFiles as $viewFile) {
                            $viewName = basename($viewFile, '.html');
                            $appView = '';
                            $appViewPrefix = '';
                            $contentIndex = stripos($viewName, 'content');
                            if ($contentIndex !== false && $contentIndex > 0) {
                                $viewPart = substr($viewName, 0, $contentIndex);
                                if (strlen($viewPart) > 0) {
                                    $appView = ucfirst($viewPart);
                                    $appViewPrefix = substr($appView, 0, min(strlen($appView), 6));
                                }
                            }
                            if (!empty($appView)) {
                                $appViewScenarios[] = ['AppView' => $appView, 'AppViewPrefix' => $appViewPrefix];
                            }
                        }
                    }
                    foreach ($appViewScenarios as $scenario) {
                        if (!$skipDetails) {
                            echo str_repeat('-', 60) . "\n";
                            echo "[PHP] Testing: AppSite={$testAppSite}, AppFile={$appFileName}, AppView={$scenario['AppView']}\n";
                            echo "[PHP] Iterations: " . number_format($iterations) . "\n";
                        }
                        LoaderNormal::clearCache();
                        LoaderPreProcess::clearCache();
                        $start = microtime(true);
                        $resultNormal = '';
                        $normalEngine = new EngineNormal();
                        $normalEngine->setAppViewPrefix($scenario['AppViewPrefix']);
                        for ($i = 0; $i < $iterations; $i++) {
                            $resultNormal = $normalEngine->mergeTemplates($testAppSite, $appFileName, $scenario['AppView'], $templates, $enableJsonProcessing);
                        }
                        $normalTime = round((microtime(true) - $start) * 1000, 2);
                        if (!$skipDetails) {
                            $avg = $normalTime / $iterations;
                            echo "[PHP] Normal Engine:     {$normalTime}ms | Avg: " . number_format($avg, 3) . "ms/op | Size: " . strlen($resultNormal) . " chars\n";
                        }
                        LoaderNormal::clearCache();
                        LoaderPreProcess::clearCache();
                        $preProcessEngine = new EnginePreProcess();
                        $preProcessEngine->setAppViewPrefix($scenario['AppViewPrefix']);
                        $start = microtime(true);
                        $resultPreProcess = '';
                        for ($i = 0; $i < $iterations; $i++) {
                            $resultPreProcess = $preProcessEngine->mergeTemplates($testAppSite, $appFileName, $scenario['AppView'], $siteTemplates->templates, $enableJsonProcessing);
                        }
                        $preProcessTime = round((microtime(true) - $start) * 1000, 2);
                        if (!$skipDetails) {
                            $avg = $preProcessTime / $iterations;
                            echo "[PHP] PreProcess Engine: {$preProcessTime}ms | Avg: " . number_format($avg, 3) . "ms/op | Size: " . strlen($resultPreProcess) . " chars\n";

                            $difference = round($preProcessTime - $normalTime, 2);
                            $differencePercent = $normalTime > 0 ? ($difference / $normalTime) * 100 : 0;
                            $signMs = $difference >= 0 ? '+' : '';
                            $signPct = $differencePercent >= 0 ? '+' : '';
                            $matchStr = $resultNormal === $resultPreProcess ? 'YES' : 'NO';
                            echo "[PHP] Performance: {$signMs}{$difference}ms ({$signPct}" . number_format($differencePercent, 1) . "%) | Match: {$matchStr}\n";
                        }
                        $summaryRows[] = new PerfSummaryRow(
                            $testAppSite,
                            $appFileName,
                            $scenario['AppView'],
                            $iterations,
                            $normalTime,
                            $preProcessTime,
                            strlen($resultNormal),
                            ($resultNormal === $resultPreProcess ? "YES" : "NO"),
                            $normalTime > 0 ? number_format(($preProcessTime - $normalTime) / $normalTime * 100, 1) . "%" : "0%"
                        );
                    }
                } catch (\Exception $ex) {
                    if (!$skipDetails) echo "❌ Error in performance testing for {$testAppSite}: {$ex->getMessage()}\n";
                }
            }
        }
        $elapsed = (microtime(true) - $startTime) * 1000;
        echo sprintf("\n========== Performance Testing Completed in %.0fms ==========\n\n", $elapsed);
        return $summaryRows;
    }

    /**
     * @param string $assemblerWebDirPath
     * @param PerfSummaryRow[] $summaryRows
     */
    public static function printPerfSummaryTable(string $assemblerWebDirPath, array $summaryRows): void
    {
        if (empty($summaryRows)) {
            return;
        }
        echo "\n==================== PHP PERFORMANCE SUMMARY ====================\n\n";

        $headers = ['AppSite', 'AppView', 'Normal(ms)', 'PreProc(ms)', 'Match', 'PerfDiff'];
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
            $html = '<html><head><title>PHP Performance Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}</style></head><body>';
            $html .= '<h2>PHP Performance Summary Table</h2>';
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
                $html .= '</tr>';
            }
            $html .= '</table></body></html>';

            $outFile = $assemblerWebDirPath . DIRECTORY_SEPARATOR . 'php_perfsummary.html';
            file_put_contents($outFile, $html);
            echo "Performance summary HTML saved to: {$outFile}\n";
        } catch (\Exception $ex) {
            echo "Error saving performance summary HTML: {$ex->getMessage()}\n";
        }

        // Save JSON file
        try {
            $jsonFile = $assemblerWebDirPath . DIRECTORY_SEPARATOR . 'php_perfsummary.json';
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
                    'PerfDifference' => $row->PerfDifference
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
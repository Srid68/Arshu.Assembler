<?php

namespace Assembler\Test;

use Assembler\Loader\LoaderNormal;
use Assembler\Loader\LoaderPreProcess;
use Assembler\Loader\LoaderNormalJson;
use Assembler\Loader\LoaderPreProcessJson;
use Assembler\Engine\EngineNormal;
use Assembler\Engine\EnginePreProcess;
use Assembler\Engine\EngineNormalJson;
use Assembler\Engine\EnginePreProcessJson;
use Assembler\Common\CommonUtil;
use Assembler\Api\ApiResponse;

class TestSummaryRow
{
    public string $AppSite;
    public string $AppFile;
    public string $AppView;
    public string $NormalPreProcess;
    public string $CrossViewUnMatch;
    public string $Error;
    public int $NormalSize;
    public int $PreProcessSize;
    public int $NormalJsonSize;
    public int $PreProcessJsonSize;
    public string $AllMatch;

    public function __construct(
        string $appSite = "",
        string $appFile = "",
        string $appView = "",
        string $normalPreProcess = "",
        string $crossViewUnMatch = "",
        string $error = "",
        int $normalSize = 0,
        int $preProcessSize = 0,
        int $normalJsonSize = 0,
        int $preProcessJsonSize = 0,
        string $allMatch = ""
    ) {
        $this->AppSite = $appSite;
        $this->AppFile = $appFile;
        $this->AppView = $appView;
        $this->NormalPreProcess = $normalPreProcess;
        $this->CrossViewUnMatch = $crossViewUnMatch;
        $this->Error = $error;
        $this->NormalSize = $normalSize;
        $this->PreProcessSize = $preProcessSize;
        $this->NormalJsonSize = $normalJsonSize;
        $this->PreProcessJsonSize = $preProcessJsonSize;
        $this->AllMatch = $allMatch;
    }
}

class TestingUtils
{
    public static function runStandardTests(string $assemblerWebDirPath, string $projectDirectory, array $scenarios, string $searchAppSites, bool $printHtmlOutput, bool $skipDetails, bool $enableJsonProcessing): array
    {
        $globalTestSummaryRows = [];

        if (!$skipDetails) {
            echo "\n========== ENTER RunStandardTests ==========\n";
        }

        if (empty($scenarios)) {
            echo "❌ No scenarios passed\n";
            return $globalTestSummaryRows;
        }

        $outputDir = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'output';
        if (!is_dir($outputDir)) {
            mkdir($outputDir, 0755, true);
        }

        $groupedScenarios = [];
        foreach ($scenarios as $scenario) {
            $key = $scenario->appSite . '|' . $scenario->appFile;
            if (!isset($groupedScenarios[$key])) {
                $groupedScenarios[$key] = [];
            }
            $groupedScenarios[$key][] = $scenario;
        }

        foreach ($groupedScenarios as $key => $group) {
            $testSite = $group[0]->appSite;
            $appFileName = $group[0]->appFile;

            if (!$skipDetails) {
                echo "$testSite: 🔍 STANDARD TEST : appsite: $testSite appfile: $appFileName\n";
                echo "$testSite: " . str_repeat('=', 50) . "\n";
            }

            try {
                $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $testSite, $searchAppSites);
                $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $testSite, $searchAppSites);
                $loaderNormalJson = new LoaderNormalJson($assemblerWebDirPath, $testSite, $searchAppSites);
                $loaderPreProcessJson = new LoaderPreProcessJson($assemblerWebDirPath, $testSite, $searchAppSites);

                foreach ($group as $scenario) {
                    $appView = $scenario->appView;

                    $normalEngine = new EngineNormal();
                    $normalEngine->setAppViewPrefix($appFileName);
                    $resultNormal = $normalEngine->mergeTemplates($testSite, $appFileName, $appView, $templates, $searchAppSites, $enableJsonProcessing);

                    $preProcessEngine = new EnginePreProcess();
                    $preProcessEngine->setAppViewPrefix($appFileName);
                    $resultPreProcess = $preProcessEngine->mergeTemplates($testSite, $appFileName, $appView, $preprocessedSiteTemplates->templates, $searchAppSites, $enableJsonProcessing);

                    $normalJsonEngine = new EngineNormalJson();
                    $normalJsonEngine->setAppViewPrefix($appFileName);
                    $resultNormalJson = $normalJsonEngine->mergeTemplates($testSite, $appFileName, $appView, $loaderNormalJson, $enableJsonProcessing);

                    $preProcessJsonEngine = new EnginePreProcessJson();
                    $preProcessJsonEngine->setAppViewPrefix($appFileName);
                    $resultPreProcessJson = $preProcessJsonEngine->mergeTemplates($testSite, $appFileName, $appView, $loaderPreProcessJson, $enableJsonProcessing);

                    $appViewSuffix = empty($appView) ? "" : "_{$appView}";
                    file_put_contents($outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_normal.html", $resultNormal ?? '');
                    file_put_contents($outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_preprocess.html", $resultPreProcess ?? '');
                    file_put_contents($outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_normalJson.html", $resultNormalJson ?? '');
                    file_put_contents($outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_preProcessJson.html", $resultPreProcessJson ?? '');

                    $outputsMatch = ($resultNormal === $resultPreProcess) && ($resultNormal === $resultNormalJson) && ($resultNormal === $resultPreProcessJson);
                    $matchStatus = $outputsMatch ? "PASS" : "FAIL";

                    if (!$skipDetails) {
                        echo "$testSite: 🧪 STANDARD TEST : scenario: AppView='$appView' - $matchStatus\n";
                        echo "  Normal: " . strlen($resultNormal ?? '') . ", PreProcess: " . strlen($resultPreProcess ?? '') . ", NormalJson: " . strlen($resultNormalJson ?? '') . ", PreProcessJson: " . strlen($resultPreProcessJson ?? '') . "\n";
                    }

                    if ($printHtmlOutput) {
                        echo "\nFULL HTML OUTPUT for AppView '$appView':\n" . ($resultNormal ?? '') . "\n";
                    }

                    $globalTestSummaryRows[] = new TestSummaryRow(
                        $testSite,
                        $appFileName,
                        $scenario->appView,
                        "",
                        "",
                        "",
                        strlen($resultNormal ?? ''),
                        strlen($resultPreProcess ?? ''),
                        strlen($resultNormalJson ?? ''),
                        strlen($resultPreProcessJson ?? ''),
                        $matchStatus
                    );
                }
            } catch (
Exception $e) {
                echo "❌ Error in $testSite/$appFileName: " . $e->getMessage() . "\n";
                $globalTestSummaryRows[] = new TestSummaryRow($testSite, $appFileName, "", "", "", $e->getMessage());
            }
        }

        return $globalTestSummaryRows;
    }

    public static function runAdvancedTests(string $assemblerWebDirPath, string $projectDirectory, array $scenarios, string $searchAppSites, bool $printHtmlOutput, bool $skipDetails, bool $enableJsonProcessing): array
    {
        $globalTestSummaryRows = [];

        if (!$skipDetails) {
            echo "🧪 Starting Advanced Tests\n\n";
            echo "========== ENTER RunAdvancedTests ==========\n";
        }

        if (empty($scenarios)) {
            echo "❌ No scenarios passed\n";
            return $globalTestSummaryRows;
        }

        $outputDir = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'output';
        if (!is_dir($outputDir)) {
            mkdir($outputDir, 0755, true);
        }

        $groupedScenarios = [];
        foreach ($scenarios as $scenario) {
            $key = $scenario->appSite . '|' . $scenario->appFile;
            if (!isset($groupedScenarios[$key])) {
                $groupedScenarios[$key] = [];
            }
            $groupedScenarios[$key][] = $scenario;
        }

        foreach ($groupedScenarios as $key => $group) {
            $testSite = $group[0]->appSite;
            $appFileName = $group[0]->appFile;

            if (!$skipDetails) {
                echo "🔍 ADVANCED TEST : appsite: $testSite appfile: $appFileName\n";
            }

            try {
                LoaderNormal::clearCache();
                LoaderPreProcess::clearCache();
                LoaderNormalJson::clearCache();
                LoaderPreProcessJson::clearCache();

                $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $testSite, $searchAppSites);
                $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $testSite, $searchAppSites);
                $loaderNormalJson = new LoaderNormalJson($assemblerWebDirPath, $testSite, $searchAppSites);
                $loaderPreProcessJson = new LoaderPreProcessJson($assemblerWebDirPath, $testSite, $searchAppSites);

                $scenarioResults = [];

                foreach ($group as $scenario) {
                    $appView = $scenario->appView;

                    $normalEngine = new EngineNormal();
                    $normalEngine->setAppViewPrefix($appFileName);
                    $preProcessEngine = new EnginePreProcess();
                    $preProcessEngine->setAppViewPrefix($appFileName);
                    $normalJsonEngine = new EngineNormalJson();
                    $normalJsonEngine->setAppViewPrefix($appFileName);
                    $preProcessJsonEngine = new EnginePreProcessJson();
                    $preProcessJsonEngine->setAppViewPrefix($appFileName);

                    $resultNormal = $normalEngine->mergeTemplates($testSite, $appFileName, $appView, $templates, $searchAppSites, $enableJsonProcessing);
                    $resultPreProcess = $preProcessEngine->mergeTemplates($testSite, $appFileName, $appView, $preprocessedSiteTemplates->templates, $searchAppSites, $enableJsonProcessing);
                    $resultNormalJson = $normalJsonEngine->mergeTemplates($testSite, $appFileName, $appView, $loaderNormalJson, $enableJsonProcessing);
                    $resultPreProcessJson = $preProcessJsonEngine->mergeTemplates($testSite, $appFileName, $appView, $loaderPreProcessJson, $enableJsonProcessing);

                    $outputsMatch = ($resultNormal === $resultPreProcess) && ($resultNormal === $resultNormalJson) && ($resultNormal === $resultPreProcessJson);
                    $matchStatus = $outputsMatch ? "PASS" : "FAIL";

                    $scenarioResults[] = [
                        'appView' => $appView,
                        'normalOutput' => $resultNormal ?? "",
                        'preProcessOutput' => $resultPreProcess ?? "",
                        'normalJsonOutput' => $resultNormalJson ?? "",
                        'preProcessJsonOutput' => $resultPreProcessJson ?? "",
                        'matchStatus' => $matchStatus
                    ];

                    $appViewSuffix = empty($appView) ? "" : "_{$appView}";
                    file_put_contents($outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_normal.html", $resultNormal ?? '');
                    file_put_contents($outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_preprocess.html", $resultPreProcess ?? '');
                    file_put_contents($outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_normalJson.html", $resultNormalJson ?? '');
                    file_put_contents($outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_preProcessJson.html", $resultPreProcessJson ?? '');

                    if (!$skipDetails || !$outputsMatch) {
                        echo "$testSite: scenario: AppView='$appView' - $matchStatus\n";
                    }

                    if ($printHtmlOutput) {
                        echo "\nNORMAL OUTPUT for AppView '$appView':\n" . ($resultNormal ?? '') . "\n";
                        echo "\nPREPROCESS OUTPUT for AppView '$appView':\n" . ($resultPreProcess ?? '') . "\n";
                        echo "\nNORMAL JSON OUTPUT for AppView '$appView':\n" . ($resultNormalJson ?? '') . "\n";
                        echo "\nPREPROCESS JSON OUTPUT for AppView '$appView':\n" . ($resultPreProcessJson ?? '') . "\n";
                    }
                }

                if (!$skipDetails && count($scenarioResults) > 0) {
                    $firstResult = $scenarioResults[0];
                    echo "\n$testSite: 📊 DETAILED OUTPUT ANALYSIS:\n";
                    echo "   Normal length: " . CommonUtil::utf16Len($firstResult['normalOutput']) . " chars\n";
                    echo "   PreProcess length: " . CommonUtil::utf16Len($firstResult['preProcessOutput']) . " chars\n";
                    echo "   NormalJson length: " . CommonUtil::utf16Len($firstResult['normalJsonOutput']) . " chars\n";
                    echo "   PreProcessJson length: " . CommonUtil::utf16Len($firstResult['preProcessJsonOutput']) . " chars\n";
                }

                $crossViewResult = "";
                if (count($scenarioResults) > 1) {
                    $allDiffer = true;
                    for ($i = 1; $i < count($scenarioResults); $i++) {
                        for ($j = $i + 1; $j < count($scenarioResults); $j++) {
                            if ($scenarioResults[$i]['normalOutput'] === $scenarioResults[$j]['normalOutput']) {
                                $allDiffer = false;
                                break 2;
                            }
                        }
                    }
                    $crossViewResult = $allDiffer ? "PASS" : "FAIL";
                }

                foreach ($scenarioResults as $i => $result) {
                    $crossView = ($i > 0 && count($scenarioResults) > 1) ? $crossViewResult : "";
                    $globalTestSummaryRows[] = new TestSummaryRow(
                        $testSite,
                        $appFileName,
                        $result['appView'],
                        $result['matchStatus'],
                        $crossView,
                        "",
                        CommonUtil::utf16Len($result['normalOutput']),
                        CommonUtil::utf16Len($result['preProcessOutput']),
                        CommonUtil::utf16Len($result['normalJsonOutput']),
                        CommonUtil::utf16Len($result['preProcessJsonOutput'])
                    );
                }
            } catch (
Exception $e) {
                echo "❌ Error testing $testSite $appFileName: " . $e->getMessage() . "\n";
                $globalTestSummaryRows[] = new TestSummaryRow($testSite, $appFileName, "", "ERROR", "", $e->getMessage());
            }
        }
        if (!$skipDetails) {
            echo "========== EXIT RunAdvancedTests ==========\n\n";
        }

        return $globalTestSummaryRows;
    }

    public static function dumpPreprocessedTemplateStructures(string $assemblerWebDirPath, string $projectDirectory, array $scenarios, string $searchAppSites, bool $skipDetails = false): void
    {
        if (empty($assemblerWebDirPath)) {
            if (!$skipDetails) echo "❌ No assemblerWebDirPath passed for DumpPreprocessedTemplateStructures\n";
            return;
        }

        if (empty($projectDirectory)) {
            if (!$skipDetails) echo "❌ No projectDirectory passed for DumpPreprocessedTemplateStructures\n";
            return;
        }

        if (empty($scenarios)) {
            if (!$skipDetails) echo "❌ No scenarios passed for DumpPreprocessedTemplateStructures\n";
            return;
        }

        $sites = array_unique(array_map(fn($s) => $s->appSite, $scenarios));

        foreach ($sites as $site) {
            try {
                LoaderNormal::clearCache();
                LoaderPreProcess::clearCache();

                $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $site, $searchAppSites);
                $fullJson = ApiResponse::serializePreprocessedSiteTemplates($preprocessedSiteTemplates, true);

                $outputDir = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'dump';
                if (!is_dir($outputDir)) {
                    mkdir($outputDir, 0755, true);
                }

                $summaryFile = $outputDir . DIRECTORY_SEPARATOR . $site . '_summary.json';
                $fullFile = $outputDir . DIRECTORY_SEPARATOR . $site . '_full.json';

                if (file_exists($summaryFile)) unlink($summaryFile);
                if (file_exists($fullFile)) unlink($fullFile);

                $summary = ApiResponse::createPreprocessedSummary($preprocessedSiteTemplates);
                file_put_contents($summaryFile, ApiResponse::serializePreprocessedSummary($summary, true));
                file_put_contents($fullFile, $fullJson);

                if (!$skipDetails) {
                    echo "✅ Dumped structure for $site\n";
                    echo "   Summary: $summaryFile\n";
                    echo "   Full: $fullFile\n";
                }
            } catch (
Exception $ex) {
                if (!$skipDetails) {
                    echo "❌ Error dumping structure for $site: " . $ex->getMessage() . "\n";
                }
            }
        }
    }

    public static function printTestSummaryTable(string $assemblerWebDirPath, string $projectDirectory, array $summaryRows, string $testType): void
    {
        if (empty($summaryRows)) return;
        if (empty($testType)) $testType = "TEST";

        echo "\n==================== PHP " . strtoupper($testType) . " SUMMARY ====================\n\n";

        $isAdvancedTest = stripos($testType, "ADVANCED") !== false;
        $isStandardTest = stripos($testType, "STANDARD") !== false;

        if ($isAdvancedTest) {
            echo "| " . str_pad("AppSite", 15) . " | " . str_pad("AppFile", 10) . " | " . str_pad("AppView", 10) . " | " . str_pad("Normal", 8) . " | " . str_pad("PreProc", 8) . " | " . str_pad("NormJson", 8) . " | " . str_pad("PreProcJson", 11) . " | " . str_pad("Match All", 9) . " | " . str_pad("ViewUnMatch", 11) . " | " . str_pad("Error", 10) . " |\n";
            echo "| " . str_repeat("-", 15) . " | " . str_repeat("-", 10) . " | " . str_repeat("-", 10) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 11) . " | " . str_repeat("-", 9) . " | " . str_repeat("-", 11) . " | " . str_repeat("-", 10) . " |\n";

            foreach ($summaryRows as $row) {
                $allMatch = ($row->NormalSize === $row->PreProcessSize) && ($row->NormalSize === $row->NormalJsonSize) && ($row->NormalSize === $row->PreProcessJsonSize);
                $matchIndicator = $allMatch ? "✓" : "✗";
                echo "| " . str_pad($row->AppSite ?? "", 15) . " | " . str_pad($row->AppFile ?? "", 10) . " | " . str_pad($row->AppView ?? "", 10) . " | " . str_pad(strval($row->NormalSize ?? 0), 8) . " | " . str_pad(strval($row->PreProcessSize ?? 0), 8) . " | " . str_pad(strval($row->NormalJsonSize ?? 0), 8) . " | " . str_pad(strval($row->PreProcessJsonSize ?? 0), 11) . " | " . str_pad($matchIndicator, 9) . " | " . str_pad($row->CrossViewUnMatch ?? "", 11) . " | " . str_pad($row->Error ?? "", 10) . " |\n";
            }
            echo "| " . str_repeat("-", 15) . " | " . str_repeat("-", 10) . " | " . str_repeat("-", 10) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 11) . " | " . str_repeat("-", 9) . " | " . str_repeat("-", 11) . " | " . str_repeat("-", 10) . " |\n";
        } else if ($isStandardTest) {
            echo "| " . str_pad("AppSite", 15) . " | " . str_pad("AppFile", 10) . " | " . str_pad("AppView", 10) . " | " . str_pad("Normal", 8) . " | " . str_pad("PreProc", 8) . " | " . str_pad("NormJson", 8) . " | " . str_pad("PreProcJson", 11) . " | " . str_pad("All Match", 9) . " | " . str_pad("Error", 10) . " |\n";
            echo "| " . str_repeat("-", 15) . " | " . str_repeat("-", 10) . " | " . str_repeat("-", 10) . " | ". str_repeat("-", 8) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 11) . " | " . str_repeat("-", 9) . " | " . str_repeat("-", 10) . " |\n";

            foreach ($summaryRows as $row) {
                echo "| " . str_pad($row->AppSite ?? "", 15) . " | " . str_pad($row->AppFile ?? "", 10) . " | " . str_pad($row->AppView ?? "", 10) . " | " . str_pad(strval($row->NormalSize ?? 0), 8) . " | " . str_pad(strval($row->PreProcessSize ?? 0), 8) . " | " . str_pad(strval($row->NormalJsonSize ?? 0), 8) . " | " . str_pad(strval($row->PreProcessJsonSize ?? 0), 11) . " | " . str_pad($row->AllMatch ?? "", 9) . " | " . str_pad($row->Error ?? "", 10) . " |\n";
            }
            echo "| " . str_repeat("-", 15) . " | " . str_repeat("-", 10) . " | " . str_repeat("-", 10) . " | ". str_repeat("-", 8) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 8) . " | " . str_repeat("-", 11) . " | " . str_repeat("-", 9) . " | " . str_repeat("-", 10) . " |\n";
        }

        try {
            $reportsDir = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis' . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsDir)) {
                mkdir($reportsDir, 0755, true);
            }

            $testTypeFile = strtolower(str_replace([" ", "-"], "", $testType));
            $outFile = $reportsDir . DIRECTORY_SEPARATOR . "php_{$testTypeFile}_Summary.html";
            $jsonFile = $reportsDir . DIRECTORY_SEPARATOR . "php_{$testTypeFile}_Summary.json";

            $html = self::generateSummaryHtml($summaryRows, $testType, $isAdvancedTest, $isStandardTest);
            file_put_contents($outFile, $html);
            echo "Test summary HTML saved to: $outFile\n";

            file_put_contents($jsonFile, json_encode($summaryRows, JSON_PRETTY_PRINT));
            echo "Test summary JSON saved to: $jsonFile\n";
        } catch (
Exception $ex) {
            echo "Error saving test summary files: " . $ex->getMessage() . "\n";
        }

        echo "\n======================================================\n";
    }

    private static function generateSummaryHtml(array $summaryRows, string $testType, bool $isAdvancedTest, bool $isStandardTest): string
    {
        $html = "<!DOCTYPE html>\n<html>\n<head>\n";
        $html .= "    <meta charset=\"UTF-8\">
";
        $html .= "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">
";
        $html .= "    <title>PHP " . strtoupper($testType) . " Summary</title>
";
        $html .= "    <style>\n";
        $html .= "        body { font-family: Arial, sans-serif; margin: 20px; }\n";
        $html .= "        h1 { color: #333; }\n";
        $html .= "        .table-container { overflow-x: auto; }\n";
        $html .= "        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }\n";
        $html .= "        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n";
        $html .= "        th { background-color: #4CAF50; color: white; }\n";
        $html .= "        tr:nth-child(even) { background-color: #f2f2f2; }\n";
        $html .= "        .pass { color: green; font-weight: bold; }\n";
        $html .= "        .fail { color: red; font-weight: bold; }\n";
        $html .= "    </style>\n";
        $html .= "</head>\n<body>\n";
        $html .= "    <h1>PHP " . strtoupper($testType) . " Summary</h1>\n";
        $html .= "    <div class=\"meta\" style=\"color: #666; font-style: italic; margin-bottom: 10px;\">Generated: " . gmdate('Y-m-d H:i:s') . " UTC</div>\n";
        $html .= "    <div class=\"table-container\">\n    <table>\n";

        if ($isAdvancedTest) {
            $html .= "<tr><th>AppSite</th><th>AppFile</th><th>AppView</th><th>Normal</th><th>PreProc</th><th>NormJson</th><th>PreProcJson</th><th>Match All</th><th>ViewUnMatch</th><th>Error</th></tr>\n";
            foreach ($summaryRows as $row) {
                $allMatch = ($row->NormalSize === $row->PreProcessSize) && ($row->NormalSize === $row->NormalJsonSize) && ($row->NormalSize === $row->PreProcessJsonSize);
                $matchIndicator = $allMatch ? "✓" : "✗";
                $matchClass = $allMatch ? "pass" : "fail";
                $viewUnMatchClass = ($row->CrossViewUnMatch === "PASS") ? "pass" : (($row->CrossViewUnMatch === "FAIL") ? "fail" : "");
                $html .= "<tr><td>{$row->AppSite}</td><td>{$row->AppFile}</td><td>{$row->AppView}</td><td>{$row->NormalSize}</td><td>{$row->PreProcessSize}</td><td>{$row->NormalJsonSize}</td><td>{$row->PreProcessJsonSize}</td><td class=\"{$matchClass}\">{$matchIndicator}</td><td class=\"{$viewUnMatchClass}\">{$row->CrossViewUnMatch}</td><td>{$row->Error}</td></tr>\n";
            }
        } elseif ($isStandardTest) {
            $html .= "<tr><th>AppSite</th><th>AppFile</th><th>AppView</th><th>Normal</th><th>PreProc</th><th>NormJson</th><th>PreProcJson</th><th>All Match</th><th>Error</th></tr>\n";
            foreach ($summaryRows as $row) {
                $allMatchClass = ($row->AllMatch === "PASS") ? "pass" : "fail";
                $html .= "<tr><td>{$row->AppSite}</td><td>{$row->AppFile}</td><td>{$row->AppView}</td><td>{$row->NormalSize}</td><td>{$row->PreProcessSize}</td><td>{$row->NormalJsonSize}</td><td>{$row->PreProcessJsonSize}</td><td class=\"{$allMatchClass}\">{$row->AllMatch}</td><td>{$row->Error}</td></tr>\n";
            }
        }

        $html .= "    </table>\n    </div>\n</body>\n</html>\n";
        return $html;
    }
}
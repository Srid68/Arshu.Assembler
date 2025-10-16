<?php

namespace Assembler\Test;

use Assembler\Loader\LoaderNormal;
use Assembler\Loader\LoaderPreProcess;
use Assembler\Engine\EngineNormal;
use Assembler\Engine\EnginePreProcess;
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

    public function __construct(string $appSite = "", string $appFile = "", string $appView = "", string $normalPreProcess = "", string $crossViewUnMatch = "", string $error = "")
    {
        $this->AppSite = $appSite;
        $this->AppFile = $appFile;
        $this->AppView = $appView;
        $this->NormalPreProcess = $normalPreProcess;
        $this->CrossViewUnMatch = $crossViewUnMatch;
        $this->Error = $error;
    }
}

class TestingUtils
{
    public static function runStandardTests(string $assemblerWebDirPath, string $projectDirectory, array $scenarios, bool $printHtmlOutput, bool $skipDetails, bool $enableJsonProcessing): array
    {
        $globalTestSummaryRows = [];

        if (!$skipDetails) {
            echo "\n========== ENTER RunStandardTests ==========\n";
        }

        if (empty($scenarios)) {
            echo "Ã¢ÂÅ’ No scenarios passed\n";
            return $globalTestSummaryRows;
        }

        // Create output directory for saving HTML outputs
        $outputDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'output';
        if (!is_dir($outputDir)) {
            mkdir($outputDir, 0755, true);
        }

        // Group scenarios by AppSite and AppFile
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
                echo "$testSite: Ã°Å¸â€Â STANDARD TEST : appsite: $testSite appfile: $appFileName\n";
                echo "$testSite: " . str_repeat('=', 50) . "\n";
            }

            try {
                // Load templates for this appsite
                $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $testSite);

                $scenarioOutputs = [];
                foreach ($group as $scenario) {
                    $appView = $scenario->appView;
                    $engine = new EngineNormal();
                    $engine->setAppViewPrefix($appFileName);
                    $output = $engine->mergeTemplates($testSite, $appFileName, $appView, $templates, $enableJsonProcessing);
                    $scenarioOutputs[] = $output ?? '';

                    // Save HTML output to template_analysis/output folder
                    $appViewSuffix = empty($appView) ? "" : "_{$appView}";
                    $outputFile = $outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_normal.html";
                    file_put_contents($outputFile, $output ?? '');

                    if (!$skipDetails) {
                        echo "$testSite: Ã°Å¸Â§Âª STANDARD TEST : scenario: AppView='$appView'\n";
                        echo "Output length = " . strlen($output ?? '') . "\n";
                    }

                    if ($printHtmlOutput) {
                        echo "\nFULL HTML OUTPUT for AppView '$appView':\n";
                        echo $output . "\n";
                    }
                }

                // Validate for unresolved placeholders
                $scenarioUnresolved = [];
                foreach ($scenarioOutputs as $output) {
                    $hasUnresolved = false;
                    $isEmpty = empty(trim($output));

                    // Scan for any {{...}} patterns which indicate unresolved placeholders
                    $startIndex = 0;
                    while (($startIndex = strpos($output, '{{', $startIndex)) !== false) {
                        $endIndex = strpos($output, '}}', $startIndex);
                        if ($endIndex !== false) {
                            // Any {{...}} pattern in final output is unresolved
                            $hasUnresolved = true;
                            if (!$skipDetails) {
                                $content = substr($output, $startIndex, $endIndex - $startIndex + 2);
                                echo "$testSite: Ã¢ÂÅ’ Found unresolved placeholder: $content\n";
                            }
                            break;
                        } else {
                            break;
                        }
                    }
                    $scenarioUnresolved[] = $hasUnresolved || $isEmpty;
                }

                // Cross-view validation logic (similar to C#)
                $matchResult = "";
                if (count($group) > 2) { // default + at least two AppViews
                    $allDiffer = true;
                    $firstAppViewOutput = $scenarioOutputs[1]; // first AppView scenario
                    for ($i = 2; $i < count($scenarioOutputs); $i++) {
                        if ($scenarioOutputs[$i] == $firstAppViewOutput) {
                            $allDiffer = false;
                            break;
                        }
                    }
                    $matchResult = $allDiffer ? "PASS" : "FAIL";
                    if (!$skipDetails) {
                        if ($allDiffer) {
                            echo "Ã¢Å“â€¦ SUCCESS: Outputs for different AppViews DO NOT MATCH in $testSite as expected.\n";
                        } else {
                            echo "Ã¢ÂÅ’ FAILURE: Some outputs for AppViews MATCH in $testSite. Expected them to differ.\n";
                        }
                    }
                }

                // Add summary rows for each scenario (matching C# logic)
                for ($i = 0; $i < count($group); $i++) {
                    $scenario = $group[$i];
                    $crossView = ($i > 0 && count($group) > 2) ? $matchResult : "";
                    $hasUnresolved = $scenarioUnresolved[$i];
                    $normalPreProcess = ($i == 0) ? ($hasUnresolved ? "FAIL" : "PASS") : "";

                    $globalTestSummaryRows[] = new TestSummaryRow(
                        $testSite,
                        $appFileName,
                        $scenario->appView,
                        $normalPreProcess,
                        $crossView,
                        ""
                    );
                }

            } catch (\Exception $e) {
                echo "Ã¢ÂÅ’ Error in $testSite/$appFileName: " . $e->getMessage() . "\n";
                $globalTestSummaryRows[] = new TestSummaryRow(
                    $testSite,
                    $appFileName,
                    "",
                    "",
                    "",
                    $e->getMessage()
                );
            }
        }

        return $globalTestSummaryRows;
    }

    public static function runAdvancedTests(string $assemblerWebDirPath, string $projectDirectory, array $scenarios, bool $printHtmlOutput, bool $skipDetails, bool $enableJsonProcessing): array
    {
        $globalTestSummaryRows = [];

        if (!$skipDetails) {
            echo "Ã°Å¸Â§Âª Starting Advanced Tests\n\n";
            echo "========== ENTER RunAdvancedTests ==========\n";
        }

        if (empty($scenarios)) {
            echo "Ã¢ÂÅ’ No scenarios passed\n";
            return $globalTestSummaryRows;
        }

        // Create output directory for saving HTML outputs
        $outputDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'output';
        if (!is_dir($outputDir)) {
            mkdir($outputDir, 0755, true);
        }

        // Group scenarios by AppSite and AppFile
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
                echo "Ã°Å¸â€Â ADVANCED TEST : appsite: $testSite appfile: $appFileName\n";
            }

            try {
                LoaderNormal::clearCache();
                LoaderPreProcess::clearCache();

                $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $testSite);
                $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $testSite);

                $scenarioResults = [];

                foreach ($group as $scenario) {
                    $appView = $scenario->appView;

                    $normalEngine = new EngineNormal();
                    $normalEngine->setAppViewPrefix($appFileName);
                    $preProcessEngine = new EnginePreProcess();
                    $preProcessEngine->setAppViewPrefix($appFileName);

                    $resultNormal = $normalEngine->mergeTemplates($testSite, $appFileName, $appView, $templates, $enableJsonProcessing);
                    $resultPreProcess = $preProcessEngine->mergeTemplates($testSite, $appFileName, $appView, $preprocessedSiteTemplates->templates, $enableJsonProcessing);

                    $outputsMatch = $resultNormal === $resultPreProcess;
                    $matchStatus = $outputsMatch ? "PASS" : "FAIL";

                    $scenarioResults[] = [$appView, $resultNormal ?? "", $resultPreProcess ?? "", $matchStatus];

                    // Save HTML outputs to template_analysis/output folder
                    $appViewSuffix = empty($appView) ? "" : "_{$appView}";
                    $normalOutputFile = $outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_normal.html";
                    $preprocessOutputFile = $outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_preprocess.html";
                    file_put_contents($normalOutputFile, $resultNormal ?? '');
                    file_put_contents($preprocessOutputFile, $resultPreProcess ?? '');

                    if (!$skipDetails || !$outputsMatch) {
                        echo "$testSite: scenario: AppView='$appView' - $matchStatus\n";
                    }

                    if ($printHtmlOutput) {
                        echo "\nNORMAL OUTPUT for AppView '$appView':\n$resultNormal\n";
                        echo "\nPREPROCESS OUTPUT for AppView '$appView':\n$resultPreProcess\n";
                    }
                }

                // Print detailed output analysis after processing all scenarios
                if (count($scenarioResults) > 0) {
                    $firstResult = $scenarioResults[0];
                    $normalLen = CommonUtil::utf16Len($firstResult[1]);
                    $preprocessLen = CommonUtil::utf16Len($firstResult[2]);
                    echo "\n$testSite: 📊 DETAILED OUTPUT ANALYSIS:\n";
                    echo "   Normal length: $normalLen chars\n";
                    echo "   PreProcess length: $preprocessLen chars\n";
                    $diff = abs($normalLen - $preprocessLen);
                    echo "   Difference: $diff chars\n";
                }

                // Cross-view comparison
                $crossViewResult = "";
                if (count($scenarioResults) > 1) {
                    $allDiffer = true;
                    for ($i = 1; $i < count($scenarioResults); $i++) {
                        for ($j = $i + 1; $j < count($scenarioResults); $j++) {
                            if ($scenarioResults[$i][1] === $scenarioResults[$j][1] ||
                                $scenarioResults[$i][2] === $scenarioResults[$j][2]) {
                                $allDiffer = false;
                                break 2;
                            }
                        }
                    }
                    $crossViewResult = $allDiffer ? "PASS" : "FAIL";
                }

                // Add summary rows
                for ($i = 0; $i < count($scenarioResults); $i++) {
                    $result = $scenarioResults[$i];
                    $crossView = ($i > 0 && count($scenarioResults) > 1) ? $crossViewResult : "";

                    $globalTestSummaryRows[] = new TestSummaryRow(
                        $testSite,
                        $appFileName,
                        $result[0],
                        $result[3],
                        $crossView,
                        ""
                    );
                }
            } catch (\Exception $e) {
                echo "Ã¢ÂÅ’ Error testing $testSite $appFileName: " . $e->getMessage() . "\n";
                $globalTestSummaryRows[] = new TestSummaryRow(
                    $testSite,
                    $appFileName,
                    "",
                    "ERROR",
                    "",
                    $e->getMessage()
                );
            }
        }
        if (!$skipDetails) {
            echo "========== EXIT RunAdvancedTests ==========\n\n";
        }

        return $globalTestSummaryRows;
    }

    public static function dumpPreprocessedTemplateStructures(string $assemblerWebDirPath, string $projectDirectory, array $scenarios, bool $skipDetails = false): void
    {
        if (empty($assemblerWebDirPath)) {
            if (!$skipDetails)
                echo "âŒ No assemblerWebDirPath passed for DumpPreprocessedTemplateStructures\n";
            return;
        }

        if (empty($projectDirectory)) {
            if (!$skipDetails)
                echo "âŒ No projectDirectory passed for DumpPreprocessedTemplateStructures\n";
            return;
        }

        if (empty($scenarios)) {
            if (!$skipDetails)
                echo "âŒ No scenarios passed for DumpPreprocessedTemplateStructures\n";
            return;
        }

        // Get unique AppSites from scenarios
        $sites = array_unique(array_map(function($scenario) { return $scenario->appSite; }, $scenarios));

        foreach ($sites as $site) {

            try {
                LoaderNormal::clearCache();
                LoaderPreProcess::clearCache();

                $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $site);
                $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $site);

                $fullJson = ApiResponse::serializePreprocessedSiteTemplates($preprocessedSiteTemplates, true);

                // Save to file for easier analysis
                $outputDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis';
                if (!is_dir($outputDir)) {
                    mkdir($outputDir, 0755, true);
                }

                $summaryFile = $outputDir . DIRECTORY_SEPARATOR . $site . '_summary.json';
                $fullFile = $outputDir . DIRECTORY_SEPARATOR . $site . '_full.json';

                if (file_exists($summaryFile)) {
                    unlink($summaryFile);
                }
                if (file_exists($fullFile)) {
                    unlink($fullFile);
                }

                $summary = ApiResponse::createPreprocessedSummary($preprocessedSiteTemplates);
                file_put_contents($summaryFile, ApiResponse::serializePreprocessedSummary($summary, true));
                file_put_contents($fullFile, $fullJson);

                if (!$skipDetails) {
                    echo "âœ… Dumped structure for $site\n";
                    echo "   Summary: $summaryFile\n";
                    echo "   Full: $fullFile\n";
                }
            } catch (\Exception $ex) {
                if (!$skipDetails) {
                    echo "âŒ Error dumping structure for $site: " . $ex->getMessage() . "\n";
                }
            }
        }
    }
    public static function printTestSummaryTable(string $assemblerWebDirPath, string $projectDirectory, array $summaryRows, string $testType): void
    {
        if (empty($summaryRows)) return;
        if (empty($testType)) $testType = "TEST";

        echo "\n==================== PHP " . strtoupper($testType) . " SUMMARY ====================\n\n";

        $headers = ["AppSite", "AppFile", "AppView", "OutputMatch", "ViewUnMatch", "Error"];
        $colCount = count($headers);
        $widths = [];

        // Calculate column widths
        for ($i = 0; $i < $colCount; $i++) {
            $maxLen = strlen($headers[$i]);
            foreach ($summaryRows as $row) {
                $value = self::getValue($row, $i);
                if (strlen($value) > $maxLen) $maxLen = strlen($value);
            }
            $widths[$i] = $maxLen < 10 ? 10 : $maxLen;
        }

        // Print header
        echo "| ";
        for ($i = 0; $i < $colCount; $i++) {
            echo str_pad($headers[$i], $widths[$i]);
            if ($i < $colCount - 1) echo " | ";
        }
        echo " |\n";

        // Print divider
        echo "|";
        for ($i = 0; $i < $colCount; $i++) {
            echo " " . str_repeat('-', $widths[$i]) . " ";
            if ($i < $colCount - 1) echo "|";
        }
        echo "|\n";

        // Print rows
        foreach ($summaryRows as $row) {
            echo "| ";
            echo str_pad($row->AppSite ?? "", $widths[0]);
            echo " | ";
            echo str_pad($row->AppFile ?? "", $widths[1]);
            echo " | ";
            echo str_pad($row->AppView ?? "", $widths[2]);
            echo " | ";
            echo str_pad($row->NormalPreProcess ?? "", $widths[3]);
            echo " | ";
            echo str_pad($row->CrossViewUnMatch ?? "", $widths[4]);
            echo " | ";
            echo str_pad($row->Error ?? "", $widths[5]);
            echo " |\n";
        }

        // Print bottom divider
        echo "|";
        for ($i = 0; $i < $colCount; $i++) {
            echo " " . str_repeat('-', $widths[$i]) . " ";
            if ($i < $colCount - 1) echo "|";
        }
        echo "|\n";

        // Save HTML file
        try {
            $reportsDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis' . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsDir)) {
                mkdir($reportsDir, 0755, true);
            }

            $html = "<!DOCTYPE html>\n<html>\n<head>\n";
            $html .= "    <meta charset=\"UTF-8\">\n";
            $html .= "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n";
            $html .= "    <title>PHP " . strtoupper($testType) . " Summary</title>\n";
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
            $html .= "        @media (max-width: 768px) {\n";
            $html .= "            body { margin: 10px; }\n";
            $html .= "            th, td { padding: 8px; font-size: 14px; }\n";
            $html .= "            h1 { font-size: 24px; }\n";
            $html .= "        }\n";
            $html .= "    </style>\n</head>\n<body>\n";
            $html .= "    <h1>PHP " . strtoupper($testType) . " Summary</h1>\n";
            $html .= "    <div class=\"meta\" style=\"color: #666; font-style: italic; margin-bottom: 10px;\">Generated: " . gmdate('Y-m-d H:i:s') . " UTC</div>\n";
            $html .= "    <div class=\"table-container\">\n    <table>\n";
            $html .= "        <tr>\n";
            $html .= "            <th>AppSite</th>\n";
            $html .= "            <th>AppFile</th>\n";
            $html .= "            <th>AppView</th>\n";
            $html .= "            <th>OutputMatch</th>\n";
            $html .= "            <th>ViewUnMatch</th>\n";
            $html .= "            <th>Error</th>\n";
            $html .= "        </tr>\n";
            foreach ($summaryRows as $row) {
                $outputMatchClass = ($row->NormalPreProcess === "PASS") ? "pass" : (($row->NormalPreProcess === "FAIL") ? "fail" : "");
                $viewUnMatchClass = ($row->CrossViewUnMatch === "PASS") ? "pass" : (($row->CrossViewUnMatch === "FAIL") ? "fail" : "");

                $html .= "        <tr>\n";
                $html .= "            <td>{$row->AppSite}</td>\n";
                $html .= "            <td>{$row->AppFile}</td>\n";
                $html .= "            <td>{$row->AppView}</td>\n";
                $html .= "            <td class=\"{$outputMatchClass}\">{$row->NormalPreProcess}</td>\n";
                $html .= "            <td class=\"{$viewUnMatchClass}\">{$row->CrossViewUnMatch}</td>\n";
                $html .= "            <td>{$row->Error}</td>\n";
                $html .= "        </tr>\n";
            }
            $html .= "    </table>\n    </div>\n</body>\n</html>\n";

            // Sanitize testType for filename
            $testTypeFile = strtolower(str_replace([" ", "-"], "", $testType));
            $outFile = $reportsDir . DIRECTORY_SEPARATOR . "php_{$testTypeFile}_Summary.html";
            file_put_contents($outFile, $html);
            echo "Test summary HTML saved to: $outFile\n";

            // Save JSON summary file
            $jsonFile = $reportsDir . DIRECTORY_SEPARATOR . "php_{$testTypeFile}_Summary.json";
            $json = json_encode($summaryRows, JSON_PRETTY_PRINT);
            file_put_contents($jsonFile, $json);
            echo "Test summary JSON saved to: $jsonFile\n";

        } catch (\Exception $ex) {
            echo "Error saving test summary HTML: " . $ex->getMessage() . "\n";
        }

        echo "\n======================================================\n";
    }

    public static function analyzeOutputDifferences(string $output1, string $output2): void
    {
        // Split both outputs into lines for comparison
        $lines1 = explode("\n", $output1);
        $lines2 = explode("\n", $output2);

        echo "   Lines: " . count($lines1) . " vs " . count($lines2) . "\n";

        // Compare line by line
        $commonLength = min(count($lines1), count($lines2));
        for ($i = 0; $i < $commonLength; $i++) {
            if ($lines1[$i] !== $lines2[$i]) {
                echo "\n   Difference at line " . ($i + 1) . ":\n";
                echo "   Normal:    " . CommonUtil::utf16Len($lines1[$i]) . " chars\n";
                echo "   PreProcess:" . CommonUtil::utf16Len($lines2[$i]) . " chars\n";

                // Show first position where they differ
                $minLength = min(strlen($lines1[$i]), strlen($lines2[$i]));
                for ($j = 0; $j < $minLength; $j++) {
                    if ($lines1[$i][$j] !== $lines2[$i][$j]) {
                        echo "   First difference at character " . ($j + 1) . ": '{$lines1[$i][$j]}' vs '{$lines2[$i][$j]}'\n";
                        break;
                    }
                }
            }
        }
    }

    public static function compareJsonProcessing(string $assemblerWebDirPath, string $appSite, string $appFile, bool $enableJsonProcessing = true): void
    {
        echo "\nÃ°Å¸â€œÅ  Testing JSON Processing Impact for $appSite : $appFile\n";
        echo str_repeat('-', 50) . "\n";

        $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $appSite);
        $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $appSite);
        $preprocessedTemplates = $preprocessedSiteTemplates->templates;

        if (empty($templates)) {
            echo "Ã¢ÂÅ’ No templates found for $appSite\n";
            return;
        }

        // Build AppView scenarios
        $appViewScenarios = [["", ""]]; // No AppView
        $appSitesPath = $assemblerWebDirPath . DIRECTORY_SEPARATOR . "AppSites" . DIRECTORY_SEPARATOR . $appSite;
        $viewsPath = $appSitesPath . DIRECTORY_SEPARATOR . "Views";
        if (is_dir($viewsPath)) {
            $viewFiles = glob($viewsPath . DIRECTORY_SEPARATOR . "*.html");
            foreach ($viewFiles as $viewFile) {
                $viewName = pathinfo($viewFile, PATHINFO_FILENAME);
                $appView = "";
                $appViewPrefix = "";
                if (stripos($viewName, "content") !== false) {
                    $contentIndex = stripos($viewName, "content");
                    if ($contentIndex > 0) {
                        $viewPart = substr($viewName, 0, $contentIndex);
                        if (strlen($viewPart) > 0) {
                            $appView = ucfirst($viewPart);
                            $appViewPrefix = substr($appView, 0, min(strlen($appView), 6));
                        }
                    }
                }
                if (!empty($appView)) {
                    $appViewScenarios[] = [$appView, $appViewPrefix];
                }
            }
        }

        foreach ($appViewScenarios as $scenario) {
            [$appView, $appViewPrefix] = $scenario;
            echo "\nÃ°Å¸â€Â Testing scenario: AppView='$appView', AppViewPrefix='$appViewPrefix'\n";

            $normalEngine = new EngineNormal();
            $preProcessEngine = new EnginePreProcess();
            $normalEngine->setAppViewPrefix($appViewPrefix);
            $preProcessEngine->setAppViewPrefix($appViewPrefix);

            // Find the main template key for both engines
            $mainTemplateKey = null;
            $mainPreprocessedKey = null;
            foreach (array_keys($templates) as $key) {
                if (stripos($key, strtolower($appSite) . "_") === 0) {
                    $mainTemplateKey = $key;
                    break;
                }
            }
            foreach (array_keys($preprocessedTemplates) as $key) {
                if (stripos($key, strtolower($appSite) . "_") === 0) {
                    $mainPreprocessedKey = $key;
                    break;
                }
            }

            $resultNormal = "";
            $resultPreProcess = "";
            if ($mainTemplateKey) {
                $appFileName = substr($mainTemplateKey, strpos($mainTemplateKey, '_') + 1);
                $resultNormal = $normalEngine->mergeTemplates($appSite, $appFileName, $appView, $templates, $enableJsonProcessing);
            }
            if ($mainPreprocessedKey) {
                $appFileName = substr($mainPreprocessedKey, strpos($mainPreprocessedKey, '_') + 1);
                $resultPreProcess = $preProcessEngine->mergeTemplates($appSite, $appFileName, $appView, $preprocessedTemplates, $enableJsonProcessing);
            }

            echo "   Ã°Å¸â€œÂ Normal Engine Output: " . strlen($resultNormal) . " chars\n";
            echo "   Ã°Å¸â€œÂ PreProcess Engine Output: " . strlen($resultPreProcess) . " chars\n";

            // Compare results between engines
            $outputsMatch = $resultNormal === $resultPreProcess;
            echo "\nÃ¢Å“â€¦ Outputs " . ($outputsMatch ? "Match! Ã¢Å“Â¨" : "Differ Ã¢ÂÅ’") . "\n";

            if (!$outputsMatch) {
                // Save outputs for comparison if they differ
                $testOutputDir = getcwd() . DIRECTORY_SEPARATOR . "test_output";
                if (!is_dir($testOutputDir)) {
                    mkdir($testOutputDir, 0755, true);
                }

                $jsonSuffix = $enableJsonProcessing ? "with" : "no";
                file_put_contents($testOutputDir . DIRECTORY_SEPARATOR . "{$appSite}_normal_{$appView}_{$jsonSuffix}_json.html", $resultNormal);
                file_put_contents($testOutputDir . DIRECTORY_SEPARATOR . "{$appSite}_preprocess_{$appView}_{$jsonSuffix}_json.html", $resultPreProcess);

                echo "\nÃ°Å¸â€œâ€ž Outputs saved to: $testOutputDir\n";

                // Show a diff of lengths by section to help identify where they differ
                echo "\nÃ°Å¸â€Â Output Analysis:\n";
                self::analyzeOutputDifferences($resultNormal, $resultPreProcess);
            }
        }
    }

    private static function getValue(TestSummaryRow $row, int $index): string
    {
        switch ($index) {
            case 0: return $row->AppSite ?? "";
            case 1: return $row->AppFile ?? "";
            case 2: return $row->AppView ?? "";
            case 3: return $row->NormalPreProcess ?? "";
            case 4: return $row->CrossViewUnMatch ?? "";
            case 5: return $row->Error ?? "";
            default: return "";
        }
    }

    private static function testWithTiming($methodName, $method, $skipDetails = false)
    {
        $start = microtime(true);
        try {
            $result = $method();
            $elapsed = microtime(true) - $start;
            $elapsedMs = round($elapsed * 1000, 2);
            if (!$skipDetails) {
                echo "Ã¢Å“â€¦ $methodName: $elapsedMs ms\n";
            }
            return $result;
        } catch (\Exception $ex) {
            $elapsed = microtime(true) - $start;
            $elapsedMs = round($elapsed * 1000, 2);
            echo "Ã¢ÂÅ’ $methodName: FAILED - " . $ex->getMessage() . "\n";
            return null;
        }
    }
}

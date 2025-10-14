<?php

namespace Assembler\Test;

use Assembler\TemplateLoader\LoaderNormal;
use Assembler\TemplateLoader\LoaderPreProcess;
use Assembler\TemplateEngine\EngineNormal;
use Assembler\TemplateEngine\EnginePreProcess;

use Assembler\TemplateApi\ApiResponse;

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
    public static function runStandardTests(string $assemblerWebDirPath, string $projectDirectory, array $scenarios, bool $enableJsonProcessing, bool $printHtmlOutput, bool $skipDetails = false): array
    {
        $globalTestSummaryRows = [];

        if (!$skipDetails) {
            echo "\n========== ENTER RunStandardTests ==========\n";
        }

        if (empty($scenarios)) {
            echo "❌ No scenarios passed\n";
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
                echo "$testSite: 🔍 STANDARD TEST : appsite: $testSite appfile: $appFileName\n";
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
                        echo "$testSite: 🧪 STANDARD TEST : scenario: AppView='$appView'\n";
                        echo "Output length = " . strlen($output ?? '') . "\n";
                    }

                    if ($printHtmlOutput) {
                        echo "\nFULL HTML OUTPUT for AppView '$appView':\n";
                        echo $output . "\n";
                    }
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
                            echo "✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in $testSite as expected.\n";
                        } else {
                            echo "❌ FAILURE: Some outputs for AppViews MATCH in $testSite. Expected them to differ.\n";
                        }
                    }
                }

                // Add summary rows for each scenario (matching C# logic)
                for ($i = 0; $i < count($group); $i++) {
                    $scenario = $group[$i];
                    $crossView = ($i > 0 && count($group) > 2) ? $matchResult : "";
                    $normalPreProcess = ($i == 0) ? "PASS" : "";

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
                echo "❌ Error in $testSite/$appFileName: " . $e->getMessage() . "\n";
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

    public static function runAdvancedTests(string $assemblerWebDirPath, string $projectDirectory, array $scenarios, bool $enableJsonProcessing, bool $printHtmlOutput, bool $skipDetails = false): array
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
                echo "🔍 ADVANCED TEST : appsite: $testSite appfile: $appFileName\n";
            }

            try {
                LoaderNormal::clearCache();
                LoaderPreProcess::clearCache();

                $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $testSite);
                $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $testSite);

                $scenarioResults = [];

                foreach ($group as $scenario) {
                    $appView = $scenario->appView;
                    // Load templates with timing
                    $templates = self::testWithTiming("LoadGetTemplateFiles", function() use ($assemblerWebDirPath, $testSite) {
                        return LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $testSite);
                    }, $skipDetails);
                    $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $testSite);

                    if (!$skipDetails) {
                        echo "📂 Loaded " . count($templates) . " templates:\n";
                        ksort($templates);
                        foreach ($templates as $key => $template) {
                            $htmlLength = $template->html ? strlen($template->html) : 0;
                            $jsonLength = $template->json ? strlen($template->json) : 0;
                            $jsonInfo = $template->json ? " + $jsonLength chars JSON" : "";
                            echo "   • $key: $htmlLength chars HTML$jsonInfo\n";
                        }
                        echo "\n";

                        echo "🔧 JSON Processing: " . ($enableJsonProcessing ? "ENABLED" : "DISABLED") . "\n";
                    }

                    // Try all possible scenarios: with and without AppView/AppViewPrefix
                    $appViewScenarios = [["", ""]]; // No AppView

                    // Add AppView scenarios if Views folder exists
                    $appSitePathInner = $assemblerWebDirPath . DIRECTORY_SEPARATOR . "AppSites" . DIRECTORY_SEPARATOR . $testSite;
                    $viewsPath = $appSitePathInner . DIRECTORY_SEPARATOR . "Views";
                    if (is_dir($viewsPath)) {
                        $viewFiles = glob($viewsPath . DIRECTORY_SEPARATOR . '*.html');
                        foreach ($viewFiles as $viewFile) {
                            $viewName = pathinfo($viewFile, PATHINFO_FILENAME);
                            $appView = "";
                            $appViewPrefix = "";
                            if (stripos(strtolower($viewName), "content") !== false) {
                                $contentIndex = stripos(strtolower($viewName), "content");
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

                    // Store outputs for each AppView scenario for cross-comparison
                    $scenarioResults = [];
                    foreach ($appViewScenarios as $scenario) {
                        [$appView, $appViewPrefix] = $scenario;
                        if (!$skipDetails) {
                            echo "$testSite: 🧪 ADVANCED TEST : scenario: AppView='$appView', AppViewPrefix='$appViewPrefix'\n";
                        }
                        $results = [];

                        // Standard implementation
                        $normalEngine = new EngineNormal();
                        $normalEngine->setAppViewPrefix($appFileName);
                        $resultNormal = self::testWithTiming("Normal - MergeTemplates", function() use ($normalEngine, $testSite, $appFileName, $appView, $templates, $enableJsonProcessing) {
                            return $normalEngine->mergeTemplates($testSite, $appFileName, $appView, $templates, $enableJsonProcessing);
                        }, $skipDetails);
                        $results["Normal"] = $resultNormal ?? "";

                        // Preprocessing implementation
                        $preProcessEngine = new EnginePreProcess();
                        $preProcessEngine->setAppViewPrefix($appFileName);
                        $resultPreProcess = self::testWithTiming("PreProcess - MergeTemplates", function() use ($preProcessEngine, $testSite, $appFileName, $appView, $preprocessedSiteTemplates, $enableJsonProcessing) {
                            return $preProcessEngine->mergeTemplates($testSite, $appFileName, $appView, $preprocessedSiteTemplates->templates, $enableJsonProcessing);
                        }, $skipDetails);
                        $results["PreProcess"] = $resultPreProcess ?? "";
                        if (!$skipDetails) {
                            echo "\n";
                        }

                        // Store for cross-AppView comparison
                        $scenarioResults[] = [$appView, $resultNormal ?? "", $resultPreProcess ?? ""];

                        // Save HTML outputs to template_analysis/output folder
                        $appViewSuffix = empty($appView) ? "" : "_{$appView}";
                        $normalOutputFile = $outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_normal.html";
                        $preprocessOutputFile = $outputDir . DIRECTORY_SEPARATOR . "{$testSite}{$appViewSuffix}_preprocess.html";
                        file_put_contents($normalOutputFile, $resultNormal ?? '');
                        file_put_contents($preprocessOutputFile, $resultPreProcess ?? '');

                        if ($printHtmlOutput) {
                            echo "\n📋 FULL HTML OUTPUT (Normal):\n" . $resultNormal . "\n";
                            echo "\n📋 FULL HTML OUTPUT (PreProcess):\n" . $resultPreProcess . "\n";
                        }

                        // Compare results
                        $match = ($resultNormal === $resultPreProcess) ? "✅ MATCH" : "❌ MISMATCH";
                        $matchResult = ($resultNormal === $resultPreProcess) ? "PASS" : "FAIL";

                        if (!$skipDetails) {
                            echo "$testSite: 📊 RESULTS COMPARISON:\n";
                            echo "$testSite: " . str_repeat('-', 45) . "\n";

                            // Always show the detailed format like C#
                            echo "$testSite: 🔹 All Two Methods:\n";
                            echo "$testSite:   Normal: " . utf16Len($resultNormal) . " chars\n";
                            echo "$testSite:   PreProcess: " . utf16Len($resultPreProcess) . " chars\n";
                            echo "$testSite:   $match\n";
                        }

                        if (!$skipDetails) {
                            if ($resultNormal === $resultPreProcess) {
                                echo "\n$testSite: 🎉 ALL METHODS PRODUCE IDENTICAL RESULTS! ✅\n";
                            }
                        }

                        // Always analyze differences when outputs don't match (outside skipDetails)
                        if ($resultNormal !== $resultPreProcess) {
                            if (!$skipDetails) {
                                echo "\n$testSite: 🔍 Output Analysis:\n";
                            }
                            self::analyzeOutputDifferences($resultNormal, $resultPreProcess);
                        }

                        // Add summary row for this scenario
                        $globalTestSummaryRows[] = new TestSummaryRow(
                            $testSite,
                            $appFileName,
                            $appView,
                            $matchResult,
                            "",
                            ""
                        );

                        echo "\n";
                    }

                    // Cross-AppView comparison
                    if (count($scenarioResults) > 1) {
                        if (!$skipDetails) {
                            echo "$testSite: 🔄 CROSS-APPVIEW COMPARISON:\n";
                            echo "$testSite: " . str_repeat('-', 45) . "\n";
                        }

                        $baseResult = $scenarioResults[0];
                        $crossViewPass = true; // Assume cross-view logic works

                        // Check if all AppViews produce different outputs (similar to standard tests)
                        // Compare each AppView output against others
                        for ($i = 1; $i < count($scenarioResults); $i++) {
                            for ($j = $i + 1; $j < count($scenarioResults); $j++) {
                                $appView1 = $scenarioResults[$i];
                                $appView2 = $scenarioResults[$j];

                                $normalMatch = ($appView1[1] === $appView2[1]) ? "✅ MATCH" : "❌ MISMATCH";
                                $preProcessMatch = ($appView1[2] === $appView2[2]) ? "✅ MATCH" : "❌ MISMATCH";

                                if (!$skipDetails) {
                                    echo "$testSite: AppView='{$appView1[0]}' vs AppView='{$appView2[0]}':\n";
                                    echo "$testSite:   Normal: $normalMatch\n";
                                    echo "$testSite:   PreProcess: $preProcessMatch\n";
                                }

                                // Different AppViews should produce different outputs
                                // If they match, it's a failure
                                if ($appView1[1] === $appView2[1] || $appView1[2] === $appView2[2]) {
                                    $crossViewPass = false;
                                }
                            }
                        }

                        // Update summary rows for AppView scenarios with cross-view result
                        if ($crossViewPass) {
                            if (!$skipDetails) {
                                echo "$testSite: ✅ CROSS-VIEW LOGIC: PASS (Different AppViews produce different outputs as expected)\n";
                            }
                            // Update the CrossViewUnMatch field for AppView rows
                            for ($i = count($globalTestSummaryRows) - count($scenarioResults); $i < count($globalTestSummaryRows); $i++) {
                                if (!empty($globalTestSummaryRows[$i]->AppView)) {
                                    $globalTestSummaryRows[$i]->CrossViewUnMatch = "PASS";
                                }
                            }
                        } else {
                            if (!$skipDetails) {
                                echo "$testSite: ❌ CROSS-VIEW LOGIC: FAIL (Different AppViews should produce different outputs)\n";
                            }
                            // Update the CrossViewUnMatch field for AppView rows
                            for ($i = count($globalTestSummaryRows) - count($scenarioResults); $i < count($globalTestSummaryRows); $i++) {
                                if (!empty($globalTestSummaryRows[$i]->AppView)) {
                                    $globalTestSummaryRows[$i]->CrossViewUnMatch = "FAIL";
                                }
                            }
                        }
                        if (!$skipDetails) {
                            echo "\n";
                        }
                    }
                }
            } catch (\Exception $e) {
                echo "❌ Error processing $testSite/$appFileName: " . $e->getMessage() . "\n";
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
        if (!$skipDetails) {
            echo "========== EXIT RunAdvancedTests ==========\n\n";
        }

        return $globalTestSummaryRows;
    }

    public static function dumpPreprocessedTemplateStructures(string $assemblerWebDirPath, string $projectDirectory, array $scenarios, bool $skipDetails = false): void
    {
        if (!$skipDetails) {
            echo "🔍 Analyzing preprocessed template structures...\n\n";
        }

        if (empty($scenarios)) {
            echo "❌ No scenarios passed\n";
            return;
        }

        // Get unique AppSites from scenarios
        $sites = array_unique(array_map(function($scenario) { return $scenario->appSite; }, $scenarios));

        foreach ($sites as $site) {

            echo "🔍 Analyzing site: $site\n";
            echo str_repeat('=', 60) . "\n";

            try {
                // Clear cache to ensure fresh load
                LoaderNormal::clearCache();
                LoaderPreProcess::clearCache();

                // Test the path resolution first
                echo "Current Directory: " . getcwd() . "\n";
                echo "AssemblerWebDirPath: $assemblerWebDirPath\n";

                $sitePath = $appSitesPath . DIRECTORY_SEPARATOR . $site;
                echo "AppSites path: $sitePath\n";
                echo "AppSites exists: " . (is_dir($sitePath) ? "true" : "false") . "\n";

                if (is_dir($sitePath)) {
                    echo "Site directory found and accessible\n";
                }

                // Load templates using both methods
                $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $site);
                echo "LoadGetTemplateFiles found " . count($templates) . " templates\n";

                $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $site);
                echo "LoadProcessGetTemplateFiles found " . count($preprocessedSiteTemplates->templates) . " templates\n";

                // Main template key logic: ensure main template key is robust
                $mainTemplateKey = strtolower($site . "_" . $site);
                if (!array_key_exists($mainTemplateKey, $templates)) {
                    // Fallback: use first template key
                    $mainTemplateKey = !empty($templates) ? array_keys($templates)[0] : null;
                    echo "⚠️ Main template key not found, using fallback: $mainTemplateKey\n";
                }

                if (count($preprocessedSiteTemplates->templates) == 0) {
                    echo "⚠️  No templates found - check path resolution\n";
                    continue;
                }

                // Create summary using ApiResponse
                $summary = ApiResponse::createPreprocessedSummary($preprocessedSiteTemplates);
                $summaryJson = ApiResponse::serializePreprocessedSummary($summary, true);

                echo "\n📋 Summary for $site:\n";
                echo $summaryJson . "\n";

                echo "\n📄 Full Structure for $site:\n";
                $fullJson = ApiResponse::serializePreprocessedSiteTemplates($preprocessedSiteTemplates, true);
                echo $fullJson . "\n";

                // Save to file for easier analysis
                $outputDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis';
                if (!is_dir($outputDir)) {
                    mkdir($outputDir, 0755, true);
                }

                $summaryFile = $outputDir . DIRECTORY_SEPARATOR . $site . '_summary.json';
                $fullFile = $outputDir . DIRECTORY_SEPARATOR . $site . '_full.json';

                // Delete existing files to ensure clean generation
                if (file_exists($summaryFile)) {
                    unlink($summaryFile);
                }
                if (file_exists($fullFile)) {
                    unlink($fullFile);
                }

                file_put_contents($summaryFile, $summaryJson);
                file_put_contents($fullFile, $fullJson);

                echo "💾 Analysis saved to:\n";
                echo "   Summary: $summaryFile\n";
                echo "   Full:    $fullFile\n";
            } catch (\Exception $ex) {
                echo "❌ Error analyzing $site: " . $ex->getMessage() . "\n";
                echo "   Stack trace: " . $ex->getTraceAsString() . "\n";
            }

            echo "\n"; // Empty line between sites
        }

        echo "✅ Template structure analysis complete!\n\n";
    }

    public static function printTestSummaryTable(string $assemblerWebDirPath, array $summaryRows, string $testType): void
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
            $reportsDir = $assemblerWebDirPath . DIRECTORY_SEPARATOR . 'Reports';
            if (!is_dir($reportsDir)) {
                mkdir($reportsDir, 0755, true);
            }

            $html = "<!DOCTYPE html>\n<html>\n<head>\n";
            $html .= "    <meta charset=\"UTF-8\">\n";
            $html .= "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n";
            $html .= "    <title>Test Summary Table</title>\n";
            $html .= "    <style>\n";
            $html .= "        body { font-family: Arial, sans-serif; margin: 20px; }\n";
            $html .= "        h1, h2 { color: #333; }\n";
            $html .= "        .table-container { overflow-x: auto; }\n";
            $html .= "        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }\n";
            $html .= "        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n";
            $html .= "        th { background-color: #4CAF50; color: white; }\n";
            $html .= "        tr:nth-child(even) { background-color: #f2f2f2; }\n";
            $html .= "        @media (max-width: 768px) {\n";
            $html .= "            body { margin: 10px; }\n";
            $html .= "            th, td { padding: 8px; font-size: 14px; }\n";
            $html .= "            h1, h2 { font-size: 20px; }\n";
            $html .= "        }\n";
            $html .= "    </style>\n</head>\n<body>\n";
            $html .= "<h2>PHP " . strtoupper($testType) . " SUMMARY TABLE</h2>\n";
            $html .= "<div class=\"table-container\">\n<table>\n";
            $html .= "<tr>";
            foreach ($headers as $h) $html .= "<th>$h</th>";
            $html .= "</tr>\n";
            foreach ($summaryRows as $row) {
                $html .= "<tr>";
                $html .= "<td>{$row->AppSite}</td>";
                $html .= "<td>{$row->AppFile}</td>";
                $html .= "<td>{$row->AppView}</td>";
                $html .= "<td>{$row->NormalPreProcess}</td>";
                $html .= "<td>{$row->CrossViewUnMatch}</td>";
                $html .= "<td>{$row->Error}</td>";
                $html .= "</tr>\n";
            }
            $html .= "</table>\n</div>\n</body>\n</html>\n";

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
                echo "   Normal:    " . utf16Len($lines1[$i]) . " chars\n";
                echo "   PreProcess:" . utf16Len($lines2[$i]) . " chars\n";

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
        echo "\n📊 Testing JSON Processing Impact for $appSite : $appFile\n";
        echo str_repeat('-', 50) . "\n";

        $templates = LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $appSite);
        $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $appSite);
        $preprocessedTemplates = $preprocessedSiteTemplates->templates;

        if (empty($templates)) {
            echo "❌ No templates found for $appSite\n";
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
            echo "\n🔍 Testing scenario: AppView='$appView', AppViewPrefix='$appViewPrefix'\n";

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

            echo "   📏 Normal Engine Output: " . strlen($resultNormal) . " chars\n";
            echo "   📏 PreProcess Engine Output: " . strlen($resultPreProcess) . " chars\n";

            // Compare results between engines
            $outputsMatch = $resultNormal === $resultPreProcess;
            echo "\n✅ Outputs " . ($outputsMatch ? "Match! ✨" : "Differ ❌") . "\n";

            if (!$outputsMatch) {
                // Save outputs for comparison if they differ
                $testOutputDir = getcwd() . DIRECTORY_SEPARATOR . "test_output";
                if (!is_dir($testOutputDir)) {
                    mkdir($testOutputDir, 0755, true);
                }

                $jsonSuffix = $enableJsonProcessing ? "with" : "no";
                file_put_contents($testOutputDir . DIRECTORY_SEPARATOR . "{$appSite}_normal_{$appView}_{$jsonSuffix}_json.html", $resultNormal);
                file_put_contents($testOutputDir . DIRECTORY_SEPARATOR . "{$appSite}_preprocess_{$appView}_{$jsonSuffix}_json.html", $resultPreProcess);

                echo "\n📄 Outputs saved to: $testOutputDir\n";

                // Show a diff of lengths by section to help identify where they differ
                echo "\n🔍 Output Analysis:\n";
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
                echo "✅ $methodName: $elapsedMs ms\n";
            }
            return $result;
        } catch (\Exception $ex) {
            $elapsed = microtime(true) - $start;
            $elapsedMs = round($elapsed * 1000, 2);
            echo "❌ $methodName: FAILED - " . $ex->getMessage() . "\n";
            return null;
        }
    }
}

// utf16Len helper function for PHP (defined globally since it's used in index.php as well)
if (!function_exists('utf16Len')) {
    function utf16Len($str) {
        $count = 0;
        $len = mb_strlen($str, 'UTF-8');
        for ($i = 0; $i < $len; $i++) {
            $char = mb_substr($str, $i, 1, 'UTF-8');
            $codePoint = mb_ord($char, 'UTF-8');
            if ($codePoint <= 0xFFFF) {
                $count++; // BMP character = 1 UTF-16 code unit
            } else {
                $count += 2; // Supplementary character = 2 UTF-16 code units (surrogate pair)
            }
        }
        return $count;
    }
}

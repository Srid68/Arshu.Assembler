<?php

require_once __DIR__ . "/../Assembler/src/App/Json/JsonArray.php";
require_once __DIR__ . "/../Assembler/src/App/Json/JsonObject.php";
require_once __DIR__ . "/../Assembler/src/TemplateLoader/LoaderNormal.php";
require_once __DIR__ . "/../Assembler/src/TemplateLoader/LoaderPreProcess.php";
require_once __DIR__ . "/../Assembler/src/TemplateEngine/EngineNormal.php";
require_once __DIR__ . "/../Assembler/src/TemplateEngine/EnginePreProcess.php";
require_once __DIR__ . "/../Assembler/src/TemplateCommon/TemplateUtils.php";
require_once __DIR__ . "/../Assembler/src/TemplateModel/ModelPreProcess.php";
require_once __DIR__ . "/../Assembler/src/App/JsonConverter.php";
require_once __DIR__ . "/PreprocessExtensions.php";
require_once __DIR__ . "/../Assembler/src/TemplatePerformance/PerformanceUtils.php";

use Assembler\TemplateLoader\LoaderNormal;
use Assembler\TemplateLoader\LoaderPreProcess;
use Assembler\TemplateEngine\EngineNormal;
use Assembler\TemplateEngine\EnginePreProcess;
use Assembler\TemplateCommon\TemplateUtils;
use Assembler\TemplateModel\PreprocessedTemplate;
use Assembler\TemplatePerformance\PerformanceUtils;

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

// Global summary rows for all tests
$globalTestSummaryRows = [];

function main(array $args): void
{
    global $globalTestSummaryRows;
    
    echo "\n========== ENTER Main ==========\n";
    echo "[DEBUG] Received args: [" . implode(", ", $args) . "]\n";
    
    $templateUtils = new TemplateUtils();
    $result = $templateUtils->getAssemblerWebDirPath();
    $assemblerWebDirPath = $result['assemblerWebDirPath'];
    $projectDirectory = $result['projectDirectory'];
    $appSiteFilter = null;
    
    if (!empty($projectDirectory)) {
        echo "Assembler Web Directory: $assemblerWebDirPath\n\n";
        
        if (is_dir($assemblerWebDirPath)) {
            echo "💡 Use --appsite to filter appsite in both engines\n\n";
            echo "💡 Use --standardtests to run standard tests in both engines\n\n";
            echo "💡 Use --investigate or --analyze for performance analysis\n";
            echo "\n";
            echo "💡 Use --printhtml to print output html in both engines\n\n";
            echo "�� Use --nojson to disable JSON processing in both engines\n\n";
            
            $disableJsonProcessing = in_array("--nojson", $args);
            $runInvestigation = in_array("--investigate", $args) || in_array("--analyze", $args);
            
            // Parse arguments
            $printHtmlOutput = false;
            $runStandardTestsOption = false;
            
            for ($i = 0; $i < count($args); $i++) {
                $arg = $args[$i];
                if (strpos($arg, "--appsite=") === 0) {
                    $appSiteFilter = substr($arg, strlen("--appsite="));
                } elseif (strcasecmp($arg, "--appsite") === 0 && $i + 1 < count($args)) {
                    $appSiteFilter = $args[$i + 1];
                } elseif (strcasecmp($arg, "--printhtml") === 0) {
                    $printHtmlOutput = true;
                } elseif (strcasecmp($arg, "--standardtests") === 0) {
                    $runStandardTestsOption = true;
                }
            }
            
            if ($runInvestigation) {
                echo "RunPerformanceInvestigation\n";
                $summaryRows = PerformanceUtils::runPerformanceComparison($assemblerWebDirPath, !$disableJsonProcessing, $appSiteFilter);
                PerformanceUtils::printPerfSummaryTable($assemblerWebDirPath, $summaryRows);
            } elseif ($runStandardTestsOption) {
                echo "RunStandardTests\n";
                runStandardTests($assemblerWebDirPath, $appSiteFilter, !$disableJsonProcessing, $printHtmlOutput);
                if (!empty($globalTestSummaryRows)) {
                    printTestSummaryTable($assemblerWebDirPath, $globalTestSummaryRows, "STANDARD TEST");
                }
            } else {
                // Run dump analysis first, then advanced tests
                dumpPreprocessedTemplateStructures($assemblerWebDirPath, $projectDirectory, $appSiteFilter);
                
                echo "\n🔬 Now running advanced template tests...\n\n";
                runAdvancedTests($assemblerWebDirPath, $appSiteFilter, !$disableJsonProcessing, $printHtmlOutput);
                if (!empty($globalTestSummaryRows)) {
                    printTestSummaryTable($assemblerWebDirPath, $globalTestSummaryRows, "ADVANCED TEST");
                }

                // Also run performance investigation after advanced tests
                echo "\n📊 Running performance investigation...\n";
                $summaryRows = PerformanceUtils::runPerformanceComparison($assemblerWebDirPath, !$disableJsonProcessing, $appSiteFilter);
                PerformanceUtils::printPerfSummaryTable($assemblerWebDirPath, $summaryRows);
            }
        } else {
            echo "❌ Assembler Web Directory not found: $assemblerWebDirPath\n";
        }
    } else {
        echo "❌ Project directory could not be determined.\n";
    }
    
    echo "\n========== EXIT Main ==========\n";
}

function runStandardTests(string $assemblerWebDirPath, ?string $appSiteFilter, bool $enableJsonProcessing, bool $printHtmlOutput): void
{
    global $globalTestSummaryRows;
    
    echo "\n========== ENTER RunStandardTests ==========\n";
    
    // Find all appsite directories in AssemblerWeb/wwwroot/AppSites
    $appSitesDir = $assemblerWebDirPath . DIRECTORY_SEPARATOR . 'AppSites';
    if (!is_dir($appSitesDir)) {
        echo "❌ AppSites directory not found: $appSitesDir\n";
        return;
    }

    $allAppSiteDirs = array_filter(scandir($appSitesDir), function($d) use ($appSitesDir) {
        return $d !== '.' && $d !== '..' && is_dir($appSitesDir . DIRECTORY_SEPARATOR . $d);
    });
    $allTestSiteNames = array_values($allAppSiteDirs);
    
    echo "[DEBUG] All available testSites: [" . implode(", ", $allTestSiteNames) . "]\n";
    echo "[DEBUG] appSiteFilter value: '$appSiteFilter'\n";
    
    $testSites = $allTestSiteNames;
    if (!empty($appSiteFilter)) {
        $filterTrimmed = trim($appSiteFilter);
        $testSites = array_filter($allTestSiteNames, function($s) use ($filterTrimmed) {
            return strcasecmp($s, $filterTrimmed) === 0;
        });
        echo "[DEBUG] Filtered testSites: [" . implode(", ", $testSites) . "] for appSiteFilter='$filterTrimmed'\n";
        if (empty($testSites)) {
            echo "[WARNING] appSiteFilter '$filterTrimmed' did not match any test site. Available sites: [" . implode(", ", $allTestSiteNames) . "]\n";
        }
    }

    foreach ($testSites as $testSite) {
        $appSiteDir = $appSitesDir . DIRECTORY_SEPARATOR . $testSite;
        if (!is_dir($appSiteDir)) {
            echo "❌ $testSite appsite not found: $appSiteDir\n";
            continue;
        }

        // Find HTML files in the appsite directory
        $htmlFiles = glob($appSiteDir . DIRECTORY_SEPARATOR . '*.html');
        foreach ($htmlFiles as $htmlFilePath) {
            $appFileName = pathinfo($htmlFilePath, PATHINFO_FILENAME);
            echo "$testSite: 🔍 STANDARD TEST : appsite: $testSite appfile: $appFileName\n";
            echo "$testSite: AppSite: $testSite, AppViewPrefix: Html3A\n";
            echo "$testSite: " . str_repeat('=', 50) . "\n";

            try {
                // Load templates for this appsite
                $templates = \Assembler\TemplateLoader\LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $testSite);
                if (empty($templates)) {
                    echo "⚠️ No templates found for $testSite\n";
                    continue;
                }

                // Dynamically generate AppView scenarios
                $appViewScenarios = [['', '']]; // No AppView

                // Add AppView scenarios if Views folder exists
                $appSitePath = $assemblerWebDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $testSite;
                $viewsPath = $appSitePath . DIRECTORY_SEPARATOR . 'Views';
                if (is_dir($viewsPath)) {
                    $viewFiles = glob($viewsPath . DIRECTORY_SEPARATOR . '*.html');
                    foreach ($viewFiles as $viewFile) {
                        $viewName = pathinfo($viewFile, PATHINFO_FILENAME);
                        $appView = '';
                        $appViewPrefix = '';
                        if (stripos($viewName, 'content') !== false) {
                            $contentIndex = stripos($viewName, 'content');
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

                $scenarioOutputs = [];
                foreach ($appViewScenarios as $scenario) {
                    $engine = new \Assembler\TemplateEngine\EngineNormal();
                    $engine->setAppViewPrefix($appFileName);
                    $output = $engine->mergeTemplates($testSite, $appFileName, $scenario[0], $templates, $enableJsonProcessing);
                    $scenarioOutputs[] = $output ?? '';
                    
                    echo "$testSite: 🧪 STANDARD TEST : scenario: AppView='{$scenario[0]}', AppViewPrefix='{$scenario[1]}'\n";
                    echo "Output length = " . strlen($output) . " Output sample: " . substr($output, 0, min(200, strlen($output))) . "\n";

                    if ($printHtmlOutput) {
                        echo "\nFULL HTML OUTPUT for AppView '{$scenario[0]}':\n";
                        echo $output . "\n";
                    }
                }

                // Cross-view validation logic (similar to C#)
                $matchResult = "";
                if (count($appViewScenarios) > 2) { // default + at least two AppViews
                    $allDiffer = true;
                    $firstAppViewOutput = $scenarioOutputs[1]; // first AppView scenario
                    for ($i = 2; $i < count($scenarioOutputs); $i++) {
                        if ($scenarioOutputs[$i] == $firstAppViewOutput) {
                            $allDiffer = false;
                            break;
                        }
                    }
                    if ($allDiffer) {
                        echo "✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in $testSite as expected.\n";
                        $matchResult = "PASS";
                    } else {
                        echo "❌ FAILURE: Some outputs for AppViews MATCH in $testSite. Expected them to differ.\n";
                        $matchResult = "FAIL";
                    }
                }

                // Scan for unresolved placeholders
                $scenarioUnresolved = [];
                foreach ($scenarioOutputs as $output) {
                    $hasUnresolved = false;
                    $isEmpty = empty(trim($output));
                    $startIndex = 0;
                    while (($startIndex = strpos($output, '{{', $startIndex)) !== false) {
                        $endIndex = strpos($output, '}}', $startIndex);
                        if ($endIndex !== false) {
                            $content = substr($output, $startIndex + 2, $endIndex - $startIndex - 2);
                            // Only flag as unresolved if it doesn't start with $ (which are normal template placeholders)
                            if (substr($content, 0, 1) !== '$') {
                                $hasUnresolved = true;
                                break;
                            }
                            $startIndex = $endIndex + 2;
                        } else {
                            break;
                        }
                    }
                    $scenarioUnresolved[] = $hasUnresolved || $isEmpty;
                }

                // Add summary rows for each scenario
                for ($i = 0; $i < count($appViewScenarios); $i++) {
                    $scenario = $appViewScenarios[$i];
                    $crossView = "";
                    if ($i > 0 && count($appViewScenarios) > 2) {
                        $crossView = $matchResult;
                    }
                    $hasUnresolved = $scenarioUnresolved[$i];
                    $globalTestSummaryRows[] = new TestSummaryRow(
                        $testSite,
                        $appFileName,
                        $scenario[0],
                        ($i == 0) ? ($hasUnresolved ? "FAIL" : "PASS") : "",
                        $crossView,
                        ""
                    );
                }

            } catch (Exception $e) {
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
    }
}

function dumpPreprocessedTemplateStructures(string $assemblerWebDirPath, string $projectDirectory, ?string $appSiteFilter): void
{
    echo "🔍 Analyzing preprocessed template structures...\n\n";
    
    $appSitesPath = $assemblerWebDirPath . DIRECTORY_SEPARATOR . 'AppSites';
    if (!is_dir($appSitesPath)) {
        echo "❌ AppSites directory not found: $appSitesPath\n";
        return;
    }
    
    $allAppSiteDirs = glob($appSitesPath . DIRECTORY_SEPARATOR . '*', GLOB_ONLYDIR);
    $appSites = array_map(function($dir) { return basename($dir); }, $allAppSiteDirs);

    foreach ($appSites as $site) {
        if (!empty($appSiteFilter) && strcasecmp($site, $appSiteFilter) !== 0) {
            continue;
        }

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

            echo "\n📋 Summary for $site:\n";
            echo PreprocessExtensions::toSummaryJson($preprocessedSiteTemplates, true) . "\n";

            echo "\n📄 Full Structure for $site:\n";
            $fullJson = PreprocessExtensions::toJson($preprocessedSiteTemplates, true);
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

            file_put_contents($summaryFile, PreprocessExtensions::toSummaryJson($preprocessedSiteTemplates, true));
            file_put_contents($fullFile, $fullJson);

            echo "💾 Analysis saved to:\n";
            echo "   Summary: $summaryFile\n";
            echo "   Full:    $fullFile\n";
        } catch (Exception $ex) {
            echo "❌ Error analyzing $site: " . $ex->getMessage() . "\n";
            echo "   Stack trace: " . $ex->getTraceAsString() . "\n";
        }

        echo "\n"; // Empty line between sites
    }

    echo "✅ Template structure analysis complete!\n\n";
}

function runAdvancedTests(string $assemblerWebDirPath, ?string $appSiteFilter, bool $enableJsonProcessing, bool $printHtmlOutput): void
{
    global $globalTestSummaryRows;
    echo "🧪 Starting Advanced Tests\n\n";
    echo "========== ENTER RunAdvancedTests ==========\n";
    
    $appSitesPath = $assemblerWebDirPath . DIRECTORY_SEPARATOR . 'AppSites';
    if (!is_dir($appSitesPath)) {
        echo "❌ AppSites directory not found: $appSitesPath\n";
        return;
    }
    
    $allAppSiteDirs = glob($appSitesPath . DIRECTORY_SEPARATOR . '*', GLOB_ONLYDIR);
    $allTestSiteNames = array_map(function($dir) { return basename($dir); }, $allAppSiteDirs);
    echo "[DEBUG] All available testSites: [" . implode(", ", $allTestSiteNames) . "]\n";
    echo "[DEBUG] appSiteFilter value: '$appSiteFilter'\n";
    
    $testSites = $allTestSiteNames;
    if (!empty($appSiteFilter)) {
        $filterTrimmed = trim($appSiteFilter);
        $testSites = array_filter($allTestSiteNames, function($s) use ($filterTrimmed) {
            return strcasecmp($s, $filterTrimmed) === 0;
        });
        echo "[DEBUG] Filtered testSites: [" . implode(", ", $testSites) . "] for appSiteFilter='$filterTrimmed'\n";
        if (empty($testSites)) {
            echo "[WARNING] appSiteFilter '$filterTrimmed' did not match any test site. Available sites: [" . implode(", ", $allTestSiteNames) . "]\n";
        }
    }

    foreach ($testSites as $testSite) {
        $appSiteDir = $appSitesPath . DIRECTORY_SEPARATOR . $testSite;
        if (!is_dir($appSiteDir)) {
            echo "❌ $testSite appsite not found: $appSiteDir\n";
            continue;
        }
        
        $htmlFiles = glob($appSiteDir . DIRECTORY_SEPARATOR . '*.html');
        foreach ($htmlFiles as $htmlFilePath) {
            $appFileName = pathinfo($htmlFilePath, PATHINFO_FILENAME);
            echo "🔍 ADVANCED TEST : appsite: $testSite appfile: $appFileName\n";
            
            try {
                // Load templates with timing
                $templates = testWithTiming("LoadGetTemplateFiles", function() use ($assemblerWebDirPath, $testSite) {
                    return LoaderNormal::loadGetTemplateFiles($assemblerWebDirPath, $testSite);
                });
                $preprocessedSiteTemplates = LoaderPreProcess::loadProcessGetTemplateFiles($assemblerWebDirPath, $testSite);

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
                echo "$testSite: 🧪 ADVANCED TEST : scenario: AppView='$appView', AppViewPrefix='$appViewPrefix'\n";
                $results = [];

                // Standard implementation
                $normalEngine = new EngineNormal();
                $normalEngine->setAppViewPrefix($appFileName);
                $resultNormal = testWithTiming("Normal - MergeTemplates", function() use ($normalEngine, $testSite, $appFileName, $appView, $templates, $enableJsonProcessing) {
                    return $normalEngine->mergeTemplates($testSite, $appFileName, $appView, $templates, $enableJsonProcessing);
                });
                $results["Normal"] = $resultNormal ?? "";

                // Preprocessing implementation
                $preProcessEngine = new EnginePreProcess();
                $preProcessEngine->setAppViewPrefix($appFileName);
                $resultPreProcess = testWithTiming("PreProcess - MergeTemplates", function() use ($preProcessEngine, $testSite, $appFileName, $appView, $preprocessedSiteTemplates, $enableJsonProcessing) {
                    return $preProcessEngine->mergeTemplates($testSite, $appFileName, $appView, $preprocessedSiteTemplates->templates, $enableJsonProcessing);
                });
                $results["PreProcess"] = $resultPreProcess ?? "";
                echo "\n";

                // Store for cross-AppView comparison
                $scenarioResults[] = [$appView, $resultNormal ?? "", $resultPreProcess ?? ""];

                if ($printHtmlOutput) {
                    echo "\n📋 FULL HTML OUTPUT (Normal):\n" . $resultNormal . "\n";
                    echo "\n📋 FULL HTML OUTPUT (PreProcess):\n" . $resultPreProcess . "\n";
                }

                // Compare results
                echo "$testSite: 📊 RESULTS COMPARISON:\n";
                echo "$testSite: " . str_repeat('-', 45) . "\n";
                
                $match = ($resultNormal === $resultPreProcess) ? "✅ MATCH" : "❌ MISMATCH";
                $matchResult = ($resultNormal === $resultPreProcess) ? "PASS" : "FAIL";
                
                // Always show the detailed format like C#
                echo "$testSite: 🔹 All Two Methods:\n";
                echo "$testSite:   Normal: " . strlen($resultNormal) . " chars\n";
                echo "$testSite:   PreProcess: " . strlen($resultPreProcess) . " chars\n";
                echo "$testSite:   $match\n";
                
                if ($resultNormal === $resultPreProcess) {
                    echo "\n$testSite: 🎉 ALL METHODS PRODUCE IDENTICAL RESULTS! ✅\n";
                } else {
                    // Analyze differences if they don't match
                    echo "\n$testSite: 🔍 Output Analysis:\n";
                    analyzeOutputDifferences($resultNormal, $resultPreProcess);
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
                echo "$testSite: 🔄 CROSS-APPVIEW COMPARISON:\n";
                echo "$testSite: " . str_repeat('-', 45) . "\n";
                
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
                        
                        echo "$testSite: AppView='{$appView1[0]}' vs AppView='{$appView2[0]}':\n";
                        echo "$testSite:   Normal: $normalMatch\n";
                        echo "$testSite:   PreProcess: $preProcessMatch\n";
                        
                        // Different AppViews should produce different outputs
                        // If they match, it's a failure
                        if ($appView1[1] === $appView2[1] || $appView1[2] === $appView2[2]) {
                            $crossViewPass = false;
                        }
                    }
                }
                
                // Update summary rows for AppView scenarios with cross-view result
                if ($crossViewPass) {
                    echo "$testSite: ✅ CROSS-VIEW LOGIC: PASS (Different AppViews produce different outputs as expected)\n";
                    // Update the CrossViewUnMatch field for AppView rows
                    for ($i = count($globalTestSummaryRows) - count($scenarioResults); $i < count($globalTestSummaryRows); $i++) {
                        if (!empty($globalTestSummaryRows[$i]->AppView)) {
                            $globalTestSummaryRows[$i]->CrossViewUnMatch = "PASS";
                        }
                    }
                } else {
                    echo "$testSite: ❌ CROSS-VIEW LOGIC: FAIL (Different AppViews should produce different outputs)\n";
                    // Update the CrossViewUnMatch field for AppView rows
                    for ($i = count($globalTestSummaryRows) - count($scenarioResults); $i < count($globalTestSummaryRows); $i++) {
                        if (!empty($globalTestSummaryRows[$i]->AppView)) {
                            $globalTestSummaryRows[$i]->CrossViewUnMatch = "FAIL";
                        }
                    }
                }
                echo "\n";
            }
            } catch (Exception $e) {
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
    }
    echo "========== EXIT RunAdvancedTests ==========\n\n";
}

/**
 * Analyzes output differences between two strings line by line
 * @param string $output1 First output to compare
 * @param string $output2 Second output to compare
 */
function analyzeOutputDifferences(string $output1, string $output2): void
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
            echo "   Normal:    " . strlen($lines1[$i]) . " chars\n";
            echo "   PreProcess:" . strlen($lines2[$i]) . " chars\n";

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

/**
 * Compare template output with and without JSON processing enabled
 * @param string $assemblerWebDirPath Path to assembler web directory
 * @param string $appSite Application site name
 * @param string $appFile Application file name
 * @param bool $enableJsonProcessing Whether to enable JSON processing
 */
function compareJsonProcessing(string $assemblerWebDirPath, string $appSite, string $appFile, bool $enableJsonProcessing = true): void
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
            analyzeOutputDifferences($resultNormal, $resultPreProcess);
        }
    }
}

function printTestSummaryTable(string $assemblerWebDirPath, array $summaryRows, string $testType): void
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
            $value = getValue($row, $i);
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
        $html = "<html><head><title>Test Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}</style></head><body>\n";
        $html .= "<h2>PHP " . strtoupper($testType) . " SUMMARY TABLE</h2>\n";
        $html .= "<table>\n";
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
        $html .= "</table></body></html>\n";

        // Sanitize testType for filename
        $testTypeFile = strtolower(str_replace([" ", "-"], "", $testType));
        $outFile = $assemblerWebDirPath . DIRECTORY_SEPARATOR . "php_{$testTypeFile}_Summary.html";
        file_put_contents($outFile, $html);
        echo "Test summary HTML saved to: $outFile\n";
        
        // Save JSON summary file
        $jsonFile = $assemblerWebDirPath . DIRECTORY_SEPARATOR . "php_{$testTypeFile}_Summary.json";
        $json = json_encode($summaryRows, JSON_PRETTY_PRINT);
        file_put_contents($jsonFile, $json);
        echo "Test summary JSON saved to: $jsonFile\n";
        
    } catch (Exception $ex) {
        echo "Error saving test summary HTML: " . $ex->getMessage() . "\n";
    }
    
    echo "\n======================================================\n";
}

function getValue(TestSummaryRow $row, int $index): string
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

// Get command line arguments (skip script name)
$args = array_slice($argv, 1);

function testWithTiming($methodName, $method) 
{
    $start = microtime(true);
    try {
        $result = $method();
        $elapsed = microtime(true) - $start;
        $elapsedMs = round($elapsed * 1000, 2);
        echo "✅ $methodName: $elapsedMs ms\n";
        return $result;
    } catch (Exception $ex) {
        $elapsed = microtime(true) - $start;
        $elapsedMs = round($elapsed * 1000, 2);
        echo "❌ $methodName: FAILED - " . $ex->getMessage() . "\n";
        return null;
    }
}

// Run the main function
main($args);

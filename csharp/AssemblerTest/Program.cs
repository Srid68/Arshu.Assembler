using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Assembler.TemplateCommon;
using Assembler.TemplateEngine;
using Assembler.TemplateLoader;
using Assembler.TemplateModel;

namespace AssemblerTest
{
    public class TestSummaryRow
    {
        public string AppSite { get; set; }
        public string AppFile { get; set; }
        public string AppView { get; set; }
        public string NormalPreProcess { get; set; }
        public string CrossViewUnMatch { get; set; }
        public string Error { get; set; }
    }

    class PerfSummaryRow
    {
        public string AppSite { get; set; }
        public string AppFile { get; set; }
        public string AppView { get; set; }
        public int Iterations { get; set; }
        public long NormalTimeMs { get; set; }
        public long PreProcessTimeMs { get; set; }
        public double NormalAvgMs { get; set; }
        public double PreProcessAvgMs { get; set; }
        public int OutputSize { get; set; }
        public string ResultsMatch { get; set; }
        public string PerfDifference { get; set; }
    }

    partial class Program
    {
        // Global summary rows for all tests
        static List<TestSummaryRow> GlobalTestSummaryRows = new List<TestSummaryRow>();
        static List<PerfSummaryRow> PerfSummaryRows = new List<PerfSummaryRow>();

        static void Main(string[] args)
        {
            Console.WriteLine("\n========== ENTER Main ==========");
            Console.WriteLine($"[DEBUG] Received args: [{string.Join(", ", args)}]");
            var (assemblerWebDirPath, projectDirectory) = TemplateUtils.GetAssemblerWebDirPath();
            string appSiteFilter = null;

            if (string.IsNullOrEmpty(projectDirectory) == false)
            {
                Console.WriteLine($"Assembler Web Directory: {assemblerWebDirPath}");
                Console.WriteLine();

                if (Directory.Exists(assemblerWebDirPath))
                {
                    Console.WriteLine("💡 Use --appsite to filter appsite in both engines\n");
                    Console.WriteLine("💡 Use --standardtests to run standard tests in both engines\n");
                    Console.WriteLine("💡 Use --investigate or --analyze for performance analysis");
                    Console.WriteLine("");
                    Console.WriteLine("💡 Use --printhtml to print output html in both engines\n");
                    Console.WriteLine("💡 Use --nojson to disable JSON processing in both engines\n");

                    bool disableJsonProcessing = args.Contains("--nojson");
                    bool runInvestigation = args.Contains("--investigate") || args.Contains("--analyze");

                    // Option to run only a specific test
                    bool printHtmlOutput = false;
                    bool runStandardTestsOption = false;
                    for (int i = 0; i < args.Length; i++)
                    {
                        var arg = args[i];
                        if (arg.StartsWith("--appsite="))
                        {
                            appSiteFilter = arg.Substring("--appsite=".Length);
                        }
                        else if (arg.Equals("--appsite", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        {
                            appSiteFilter = args[i + 1];
                        }
                        if (arg.Equals("--printhtml", StringComparison.OrdinalIgnoreCase))
                        {
                            printHtmlOutput = true;
                        }
                        if (arg.Equals("--standardtests", StringComparison.OrdinalIgnoreCase))
                        {
                            runStandardTestsOption = true;
                        }
                    }

                    if (runInvestigation)
                    {
                        Console.WriteLine("RunPerformanceInvestigation");
                        RunPerformanceInvestigation(assemblerWebDirPath, appSiteFilter);
                    }
                    else if (runStandardTestsOption)
                    {
                        Console.WriteLine("RunStandardTests");
                        RunStandardTests(assemblerWebDirPath, appSiteFilter, !disableJsonProcessing, printHtmlOutput);
                        if (GlobalTestSummaryRows != null && GlobalTestSummaryRows.Count > 0)
                        {
                            PrintTestSummaryTable(assemblerWebDirPath, GlobalTestSummaryRows, "STANDARD TEST");
                        }
                    }
                    else
                    {
                        // Run dump analysis first, then advanced tests
                        DumpPreprocessedTemplateStructures(assemblerWebDirPath, projectDirectory, appSiteFilter);

                        Console.WriteLine("\n🔬 Now running advanced template tests...\n");
                        RunAdvancedTests(assemblerWebDirPath, appSiteFilter, !disableJsonProcessing, printHtmlOutput);
                        if (GlobalTestSummaryRows != null && GlobalTestSummaryRows.Count > 0)
                        {
                            PrintTestSummaryTable(assemblerWebDirPath, GlobalTestSummaryRows, "ADVANCED TEST");
                        }

                        Console.WriteLine("RunPerformanceComparison");
                        RunPerformanceComparison(assemblerWebDirPath, true, appSiteFilter);
                    }
                }
            }
        }

        #region Test Methods

        static void RunStandardTests(string assemblerWebDirPath, string appSiteFilter = null, bool enableJsonProcessing = true, bool printHtmlOutput = false)
        {
            Console.WriteLine("\n========== ENTER RunStandardTests ==========");
            string appSitesPath = Path.Combine(assemblerWebDirPath, "AppSites");
            if (!Directory.Exists(appSitesPath))
            {
                Console.WriteLine($"❌ AppSites directory not found: {appSitesPath}");
                return;
            }

            var allAppSiteDirs = Directory.GetDirectories(appSitesPath, "*", SearchOption.TopDirectoryOnly);
            var allTestSiteNames = allAppSiteDirs.Select(dir => Path.GetFileName(dir)).ToArray();
            Console.WriteLine($"[DEBUG] All available testSites: [{string.Join(", ", allTestSiteNames)}]");
            Console.WriteLine($"[DEBUG] appSiteFilter value: '{appSiteFilter}'");
            var testSites = allTestSiteNames;
            if (!string.IsNullOrEmpty(appSiteFilter))
            {
                var filterTrimmed = appSiteFilter.Trim();
                testSites = allTestSiteNames.Where(s => s.Equals(filterTrimmed, StringComparison.OrdinalIgnoreCase)).ToArray();
                Console.WriteLine($"[DEBUG] Filtered testSites: [{string.Join(", ", testSites)}] for appSiteFilter='{filterTrimmed}'");
                if (testSites.Length == 0)
                {
                    Console.WriteLine($"[WARNING] appSiteFilter '{filterTrimmed}' did not match any test site. Available sites: [{string.Join(", ", allTestSiteNames)}]");
                }
            }

            foreach (var testSite in testSites)
            {
                var appSiteDir = Path.Combine(appSitesPath, testSite);
                if (!Directory.Exists(appSiteDir))
                {
                    Console.WriteLine($"❌ {testSite} appsite not found: {appSiteDir}");
                    continue;
                }
                var htmlFiles = Directory.GetFiles(appSiteDir, "*.html", SearchOption.TopDirectoryOnly);
                foreach (var htmlFilePath in htmlFiles)
                {
                    var appFileName = Path.GetFileNameWithoutExtension(htmlFilePath);
                    Console.WriteLine($"{testSite}: 🔍 STANDARD TEST : appsite: {testSite} appfile: {appFileName}");
                    Console.WriteLine($"{testSite}: AppSite: {testSite}, AppViewPrefix: Html3A");
                    Console.WriteLine($"{testSite}: {new string('=', 50)}");
                    try
                    {
                        // Dynamically generate AppView scenarios
                        var appViewScenarios = new List<(string AppView, string AppViewPrefix)>
                        {
                            ("", ""), // No AppView
                        };

                        // Add AppView scenarios if Views folder exists
                        var appSitePath = Path.Combine(assemblerWebDirPath, "AppSites", testSite);
                        var viewsPath = Path.Combine(appSitePath, "Views");
                        if (Directory.Exists(viewsPath))
                        {
                            var viewFiles = Directory.GetFiles(viewsPath, "*.html");
                            foreach (var viewFile in viewFiles)
                            {
                                var viewName = Path.GetFileNameWithoutExtension(viewFile);
                                var appView = "";
                                var appViewPrefix = "";
                                if (viewName.ToLowerInvariant().Contains("content"))
                                {
                                    var contentIndex = viewName.ToLowerInvariant().IndexOf("content");
                                    if (contentIndex > 0)
                                    {
                                        var viewPart = viewName.Substring(0, contentIndex);
                                        if (viewPart.Length > 0)
                                        {
                                            appView = char.ToUpper(viewPart[0]) + viewPart.Substring(1);
                                            appViewPrefix = appView.Substring(0, Math.Min(appView.Length, 6));
                                        }
                                    }
                                }
                                if (!string.IsNullOrEmpty(appView))
                                {
                                    appViewScenarios.Add((appView, appViewPrefix));
                                }
                            }
                        }

                        var scenarioOutputs = new List<string>();
                        var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, testSite);

                        foreach (var scenario in appViewScenarios)
                        {
                            var normalEngine = new EngineNormal();
                            normalEngine.AppViewPrefix = appFileName;
                            var preProcessEngine = new EnginePreProcess();
                            preProcessEngine.AppViewPrefix = appFileName;
                            var resultNormal = normalEngine.MergeTemplates(testSite, appFileName, scenario.AppView, templates, enableJsonProcessing);
                            scenarioOutputs.Add(resultNormal ?? "");
                            Console.WriteLine($"{testSite}: 🧪 STANDARD TEST : scenario: AppView='{scenario.AppView}', AppViewPrefix='{scenario.AppViewPrefix}'");
                            Console.WriteLine($"Output length = {resultNormal?.Length ?? 0} Output sample: {resultNormal?.Substring(0, Math.Min(200, resultNormal.Length))}");
                            if (printHtmlOutput)
                            {
                                Console.WriteLine($"\nFULL HTML OUTPUT for AppView '{scenario.AppView}':\n{resultNormal}\n");
                            }
                        }

                        // Compare outputs for cross-view
                        string matchResult = "";
                        // Only compare outputs between AppView scenarios (exclude default scenario without AppView)
                        if (appViewScenarios.Count > 2) // default + at least two AppViews
                        {
                            bool allDiffer = true;
                            var firstAppViewOutput = scenarioOutputs[1]; // first AppView scenario
                            for (int i = 2; i < scenarioOutputs.Count; i++)
                            {
                                if (scenarioOutputs[i] == firstAppViewOutput)
                                {
                                    allDiffer = false;
                                    break;
                                }
                            }
                            if (allDiffer)
                            {
                                Console.WriteLine($"✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in {testSite} as expected.");
                                matchResult = "PASS";
                            }
                            else
                            {
                                Console.WriteLine($"❌ FAILURE: Some outputs for AppViews MATCH in {testSite}. Expected them to differ.");
                                matchResult = "FAIL";
                            }
                        }

                        // Scan for unresolved placeholders in each scenario output
                        var scenarioUnresolved = new List<bool>();
                        for (int i = 0; i < scenarioOutputs.Count; i++)
                        {
                            var output = scenarioOutputs[i] ?? "";
                            // Check for unresolved mustache templates {{field}} but not {{$field}} which are normal
                            bool hasUnresolved = false;
                            bool isEmpty = string.IsNullOrWhiteSpace(output);
                            int startIndex = 0;
                            while ((startIndex = output.IndexOf("{{", startIndex)) != -1)
                            {
                                int endIndex = output.IndexOf("}}", startIndex);
                                if (endIndex != -1)
                                {
                                    string content = output.Substring(startIndex + 2, endIndex - startIndex - 2);
                                    // Only flag as unresolved if it doesn't start with $ (which are normal template placeholders)
                                    if (!content.StartsWith("$"))
                                    {
                                        hasUnresolved = true;
                                        break;
                                    }
                                    startIndex = endIndex + 2;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            scenarioUnresolved.Add(hasUnresolved || isEmpty);
                        }
                        // Add summary rows for each scenario
                        for (int i = 0; i < appViewScenarios.Count; i++)
                        {
                            var scenario = appViewScenarios[i];
                            string crossView = "";
                            if (i > 0 && appViewScenarios.Count > 2)
                            {
                                crossView = matchResult;
                            }
                            bool hasUnresolved = scenarioUnresolved[i];
                            GlobalTestSummaryRows.Add(new TestSummaryRow
                            {
                                AppSite = testSite,
                                AppFile = appFileName,
                                AppView = scenario.AppView,
                                NormalPreProcess = (i == 0) ? (hasUnresolved ? "FAIL" : "PASS") : "",
                                CrossViewUnMatch = crossView,
                                Error = hasUnresolved ? (string.IsNullOrWhiteSpace(scenarioOutputs[i]) ? "Empty" : "Unresolv") : ""
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error testing {testSite} {appFileName}: {ex.Message}");
                        // Add error row for the first scenario or general
                        GlobalTestSummaryRows.Add(new TestSummaryRow
                        {
                            AppSite = testSite,
                            AppFile = appFileName,
                            AppView = "",
                            NormalPreProcess = "ERROR",
                            CrossViewUnMatch = "",
                            Error = ex.Message
                        });
                    }
                    Console.WriteLine();
                }
            }
            // Do not print summary table here; will print globally after all tests
        }

        static void RunAdvancedTests(string assemblerWebDirPath, string appSiteFilter = null, bool enableJsonProcessing = true, bool printHtmlOutput = false)
        {
            Console.WriteLine("\n========== ENTER RunAdvancedTests ==========");
            string appSitesPath = Path.Combine(assemblerWebDirPath, "AppSites");
            if (!Directory.Exists(appSitesPath))
            {
                Console.WriteLine($"❌ AppSites directory not found: {appSitesPath}");
                return;
            }
            var allAppSiteDirs = Directory.GetDirectories(appSitesPath, "*", SearchOption.TopDirectoryOnly);
            var allTestSiteNames = allAppSiteDirs.Select(dir => Path.GetFileName(dir)).ToArray();
            Console.WriteLine($"[DEBUG] All available testSites: [{string.Join(", ", allTestSiteNames)}]");
            Console.WriteLine($"[DEBUG] appSiteFilter value: '{appSiteFilter}'");
            var testSites = allTestSiteNames;
            if (!string.IsNullOrEmpty(appSiteFilter))
            {
                var filterTrimmed = appSiteFilter.Trim();
                testSites = allTestSiteNames.Where(s => s.Equals(filterTrimmed, StringComparison.OrdinalIgnoreCase)).ToArray();
                Console.WriteLine($"[DEBUG] Filtered testSites: [{string.Join(", ", testSites)}] for appSiteFilter='{filterTrimmed}'");
                if (testSites.Length == 0)
                {
                    Console.WriteLine($"[WARNING] appSiteFilter '{filterTrimmed}' did not match any test site. Available sites: [{string.Join(", ", allTestSiteNames)}]");
                }
            }

            foreach (var testSite in testSites)
            {
                var appSiteDir = Path.Combine(appSitesPath, testSite);
                if (!Directory.Exists(appSiteDir))
                {
                    Console.WriteLine($"❌ {testSite} appsite not found: {appSiteDir}");
                    continue;
                }
                var htmlFiles = Directory.GetFiles(appSiteDir, "*.html", SearchOption.TopDirectoryOnly);
                foreach (var htmlFilePath in htmlFiles)
                {
                    var appFileName = Path.GetFileNameWithoutExtension(htmlFilePath);
                    Console.WriteLine($"🔍 ADVANCED TEST : appsite: {testSite} appfile: {appFileName}");
                    // ...existing code for running scenarios and output...
                    // Load templates with timing
                    var templates = TestWithTiming($"LoadGetTemplateFiles", () => LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, testSite));
                    var preprocessedSiteTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, testSite);

                    Console.WriteLine($"📂 Loaded {templates.Count} templates:");
                    foreach (var template in templates.OrderBy(x => x.Key))
                    {
                        var htmlLength = template.Value.html?.Length ?? 0;
                        var jsonLength = template.Value.json?.Length ?? 0;
                        var jsonInfo = template.Value.json != null ? $" + {jsonLength} chars JSON" : "";
                        Console.WriteLine($"   • {template.Key}: {htmlLength} chars HTML{jsonInfo}");
                    }
                    Console.WriteLine();

                    Console.WriteLine($"🔧 JSON Processing: {(enableJsonProcessing ? "ENABLED" : "DISABLED")}");

                    // Try all possible scenarios: with and without AppView/AppViewPrefix
                    var appViewScenarios = new List<(string AppView, string AppViewPrefix)>
                    {
                        ("", ""), // No AppView
                    };

                    // Add AppView scenarios if Views folder exists
                    var appSitePathInner = Path.Combine(assemblerWebDirPath, "AppSites", testSite);
                    var viewsPath = Path.Combine(appSitePathInner, "Views");
                    if (Directory.Exists(viewsPath))
                    {
                        var viewFiles = Directory.GetFiles(viewsPath, "*.html");
                        foreach (var viewFile in viewFiles)
                        {
                            var viewName = Path.GetFileNameWithoutExtension(viewFile);
                            var appView = "";
                            var appViewPrefix = "";
                            if (viewName.ToLowerInvariant().Contains("content"))
                            {
                                var contentIndex = viewName.ToLowerInvariant().IndexOf("content");
                                if (contentIndex > 0)
                                {
                                    var viewPart = viewName.Substring(0, contentIndex);
                                    if (viewPart.Length > 0)
                                    {
                                        appView = char.ToUpper(viewPart[0]) + viewPart.Substring(1);
                                        appViewPrefix = appView.Substring(0, Math.Min(appView.Length, 6));
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(appView))
                            {
                                appViewScenarios.Add((appView, appViewPrefix));
                            }
                        }
                    }

                    // Store outputs for each AppView scenario for cross-comparison
                    var scenarioResults = new List<(string AppView, string NormalOutput, string PreProcessOutput)>();
                    foreach (var scenario in appViewScenarios)
                    {
                        Console.WriteLine($"{testSite}: 🧪 ADVANCED TEST : scenario: AppView='{scenario.AppView}', AppViewPrefix='{scenario.AppViewPrefix}'");
                        var results = new Dictionary<string, string>();

                        // Standard implementation
                        var normalEngine = new EngineNormal();
                        normalEngine.AppViewPrefix = appFileName;
                        var resultNormal = TestWithTiming($"Normal - MergeTemplates",
                            () => normalEngine.MergeTemplates(testSite, appFileName, scenario.AppView, templates, enableJsonProcessing));
                        results["Normal"] = resultNormal ?? "";

                        // Preprocessing implementation
                        var preProcessEngine = new EnginePreProcess();
                        preProcessEngine.AppViewPrefix = appFileName;
                        var resultPreProcess = TestWithTiming($"PreProcess - MergeTemplates",
                            () => preProcessEngine.MergeTemplates(testSite, appFileName, scenario.AppView, preprocessedSiteTemplates.Templates, enableJsonProcessing));
                        results["PreProcess"] = resultPreProcess ?? "";
                        Console.WriteLine();

                        // Store for cross-AppView comparison
                        scenarioResults.Add((scenario.AppView, resultNormal ?? "", resultPreProcess ?? ""));

                        if (printHtmlOutput)
                        {
                            Console.WriteLine("\n📋 FULL HTML OUTPUT (Normal):\n" + resultNormal);
                            Console.WriteLine("\n📋 FULL HTML OUTPUT (PreProcess):\n" + resultPreProcess);
                        }

                        // Compare results
                        Console.WriteLine($"{testSite}: 📊 RESULTS COMPARISON:");
                        Console.WriteLine($"{testSite}: {new string('-', 45)}");

                        Console.WriteLine($"{testSite}: 🔹 All Two Methods:");
                        foreach (var result in results)
                        {
                            Console.WriteLine($"{testSite}:   {result.Key}: {(result.Value?.Length ?? 0)} chars");
                        }

                        // Check if all results match
                        bool allMatch = true;
                        var baseResult = results.Values.FirstOrDefault();
                        var baseKey = results.Keys.FirstOrDefault();

                        foreach (var kvp in results.Skip(1))
                        {
                            if (kvp.Value != baseResult)
                            {
                                Console.WriteLine($"{testSite}:   ❌ {baseKey} vs {kvp.Key}: NO MATCH");
                                allMatch = false;
                            }
                            else
                            {
                                Console.WriteLine($"{testSite}:   ✅ {baseKey} vs {kvp.Key}: MATCH");
                            }
                        }

                        string matchResult = allMatch ? "PASS" : "FAIL";
                        if (GlobalTestSummaryRows.Count == 0)
                        {
                            // Header handled in printing
                        }
                        GlobalTestSummaryRows.Add(new TestSummaryRow
                        {
                            AppSite = testSite,
                            AppFile = appFileName,
                            AppView = scenario.AppView,
                            NormalPreProcess = matchResult,
                            CrossViewUnMatch = "",
                            Error = ""
                        });

                        if (allMatch)
                        {
                            Console.WriteLine($"\n{testSite}: 🎉 ALL METHODS PRODUCE IDENTICAL RESULTS! ✅");
                        }
                        else
                        {
                            Console.WriteLine($"\n{testSite}: ⚠️  METHODS PRODUCE DIFFERENT RESULTS! ❌");
                        }

                        // Show final processed outputs
                        if (!string.IsNullOrEmpty(baseResult))
                        {
                            Console.WriteLine($"\n{testSite}: 📋 FINAL OUTPUT SAMPLE (full HTML):");
                            Console.WriteLine($"{testSite}: {baseResult}");
                        }

                        // Show detailed differences if any method differs
                        if (!allMatch)
                        {
                            Console.WriteLine($"\n{testSite}: ❗ DETAILED DIFFERENCES:");

                            foreach (var kvp in results.Skip(1))
                            {
                                if (kvp.Value != baseResult)
                                {
                                    Console.WriteLine($"{testSite}: 🔸 {baseKey} vs {kvp.Key}:");
                                    Console.WriteLine($"{testSite}:   {baseKey} Result:\n{baseResult}");
                                    Console.WriteLine($"{testSite}:   {kvp.Key} Result:\n{kvp.Value}");
                                    Console.WriteLine();
                                }
                            }
                        }
                        // Check for unmerged template fields in all outputs
                        Console.WriteLine($"\n{testSite}: 🔎 Checking for unmerged template fields in outputs...");
                        bool foundUnmerged = false;
                        foreach (var kvp in results)
                        {
                            var content = kvp.Value ?? string.Empty;
                            var unmergedFields = new List<string>();
                            
                            // Find all ${field} patterns using indexOf
                            int startIndex = 0;
                            while ((startIndex = content.IndexOf("${", startIndex)) != -1)
                            {
                                int endIndex = content.IndexOf("}", startIndex);
                                if (endIndex != -1)
                                {
                                    string field = content.Substring(startIndex, endIndex - startIndex + 1);
                                    unmergedFields.Add(field);
                                    startIndex = endIndex + 1;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            
                            if (unmergedFields.Count > 0)
                            {
                                // If JSON processing is disabled, skip reporting unmerged JSON fields (those starting with ${Json or ${$Json)
                                var filteredFields = enableJsonProcessing 
                                    ? unmergedFields 
                                    : unmergedFields.Where(f => !f.StartsWith("${Json") && !f.StartsWith("${$Json")).ToList();
                                    
                                if (filteredFields.Count() > 0)
                                {
                                    Console.WriteLine($"{testSite}:   ❌ {kvp.Key} output contains {filteredFields.Count()} unmerged non-JSON template fields!");
                                    foreach (var field in filteredFields)
                                    {
                                        Console.WriteLine($"{testSite}:      Unmerged field: {field}");
                                    }
                                    foundUnmerged = true;
                                }
                                else
                                {
                                    Console.WriteLine($"{testSite}:   ✅ {kvp.Key} output contains no unmerged non-JSON template fields.");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"{testSite}:   ✅ {kvp.Key} output contains no unmerged template fields.");
                            }
                        }
                        if (foundUnmerged)
                        {
                            Console.WriteLine($"\n{testSite}: ⚠️  TEST FAILURE: Unmerged non-JSON template fields found in output!");
                        }
                        else
                        {
                            Console.WriteLine($"\n{testSite}: 🎉 TEST SUCCESS: No unmerged non-JSON template fields found in any output.");
                        }
                    }

                    // Compare outputs from different AppViews (cross-scenario)
                    // Only compare AppView scenarios (exclude empty AppView scenario)
                    var appViewResults = scenarioResults.Where(r => !string.IsNullOrEmpty(r.AppView)).ToList();
                    if (appViewResults.Count > 1)
                    {
                        Console.WriteLine("\n🔬 Cross-AppView Output Comparison:");
                        bool allAppViewsDiffer = true;
                        var firstAppViewNormal = appViewResults[0].NormalOutput;
                        var firstAppViewPreProcess = appViewResults[0].PreProcessOutput;

                        for (int i = 1; i < appViewResults.Count; i++)
                        {
                            string crossViewMatch = "";
                            if (appViewResults[i].NormalOutput == firstAppViewNormal && appViewResults[i].PreProcessOutput == firstAppViewPreProcess)
                            {
                                Console.WriteLine($"❌ FAILURE: Outputs for AppView '{appViewResults[0].AppView}' and AppView '{appViewResults[i].AppView}' MATCH. Expected them to differ.");
                                allAppViewsDiffer = false;
                                crossViewMatch = "FAIL";
                            }
                            else
                            {
                                Console.WriteLine($"✅ SUCCESS: Outputs for AppView '{appViewResults[0].AppView}' and AppView '{appViewResults[i].AppView}' DO NOT MATCH as expected.");
                                crossViewMatch = "PASS";
                            }

                            // Find and update the corresponding row in GlobalTestSummaryRows
                            var targetAppView = appViewResults[i].AppView;
                            var rowToUpdate = GlobalTestSummaryRows.LastOrDefault(r => r.AppSite == testSite && r.AppFile == appFileName && r.AppView == targetAppView);
                            if (rowToUpdate != null)
                            {
                                rowToUpdate.CrossViewUnMatch = crossViewMatch;
                            }
                        }

                        // Also set the first AppView result
                        var firstTargetAppView = appViewResults[0].AppView;
                        var firstRowToUpdate = GlobalTestSummaryRows.LastOrDefault(r => r.AppSite == testSite && r.AppFile == appFileName && r.AppView == firstTargetAppView);
                        if (firstRowToUpdate != null)
                        {
                            firstRowToUpdate.CrossViewUnMatch = allAppViewsDiffer ? "PASS" : "FAIL";
                        }

                        if (allAppViewsDiffer)
                        {
                            Console.WriteLine("🎉 All AppView outputs are different as expected.");
                        }
                        else
                        {
                            Console.WriteLine("❌ Some AppView outputs match when they should differ.");
                        }
                    }
                    // Do not print summary table here; will print globally after all tests
                }
            }
        }

        #endregion

        #region Structure Dump Method

        static void DumpPreprocessedTemplateStructures(string assemblerWebDirPath, string projectDirectory, string appSiteFilter = null)
        {
            string appSitesPath = Path.Combine(assemblerWebDirPath, "AppSites");
            if (!Directory.Exists(appSitesPath))
            {
                Console.WriteLine($"❌ AppSites directory not found: {appSitesPath}");
                return;
            }
            var allAppSiteDirs = Directory.GetDirectories(appSitesPath, "*", SearchOption.TopDirectoryOnly);
            var appSites = allAppSiteDirs.Select(dir => Path.GetFileName(dir)).ToArray();

            // appSiteFilter is now the parameter

            foreach (string site in appSites)
            {
                if (!string.IsNullOrEmpty(appSiteFilter) && !site.Equals(appSiteFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                Console.WriteLine($"🔍 Analyzing site: {site}");
                Console.WriteLine(new string('=', 60));

                try
                {
                    // Clear cache to ensure fresh load
                    LoaderNormal.ClearCache();
                    LoaderPreProcess.ClearCache();

                    // Test the path resolution first
                    Console.WriteLine($"Current Directory: {Directory.GetCurrentDirectory()}");
                    Console.WriteLine($"AssemblerWebDirPath: {assemblerWebDirPath}");

                    var sitePath = Path.Combine(appSitesPath, site);
                    Console.WriteLine($"AppSites path: {sitePath}");
                    Console.WriteLine($"AppSites exists: {Directory.Exists(sitePath)}");

                    if (Directory.Exists(sitePath))
                    {
                        Console.WriteLine($"Site directory found and accessible");
                    }

                    // Load templates using both methods
                    var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, site);
                    Console.WriteLine($"LoadGetTemplateFiles found {templates.Count} templates");

                    var preprocessedSiteTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, site);
                    Console.WriteLine($"LoadProcessGetTemplateFiles found {preprocessedSiteTemplates.Templates.Count} templates");

                    // Main template key logic: ensure main template key is robust
                    string mainTemplateKey = (site + "_" + site).ToLowerInvariant();
                    if (!templates.ContainsKey(mainTemplateKey))
                    {
                        // Fallback: use first template key
                        mainTemplateKey = templates.Keys.FirstOrDefault();
                        Console.WriteLine($"⚠️ Main template key not found, using fallback: {mainTemplateKey}");
                    }

                    if (preprocessedSiteTemplates.Templates.Count == 0)
                    {
                        Console.WriteLine("⚠️  No templates found - check path resolution");
                        continue;
                    }

                    Console.WriteLine($"📋 Summary for {site}:");
                    Console.WriteLine(preprocessedSiteTemplates.ToSummaryJson(true));

                    Console.WriteLine($"\n📄 Full Structure for {site}:");
                    var fullJson = preprocessedSiteTemplates.ToJson(true);
                    Console.WriteLine(fullJson);

                    // Save to file for easier analysis
                    var outputDir = Path.Combine(projectDirectory, "template_analysis");
                    Directory.CreateDirectory(outputDir);

                    var summaryFile = Path.Combine(outputDir, $"{site}_summary.json");
                    var fullFile = Path.Combine(outputDir, $"{site}_full.json");

                    // Delete existing files to ensure clean generation
                    if (File.Exists(summaryFile))
                        File.Delete(summaryFile);
                    if (File.Exists(fullFile))
                        File.Delete(fullFile);

                    File.WriteAllText(summaryFile, preprocessedSiteTemplates.ToSummaryJson(true));
                    File.WriteAllText(fullFile, fullJson);

                    Console.WriteLine($"💾 Analysis saved to:");
                    Console.WriteLine($"   Summary: {summaryFile}");
                    Console.WriteLine($"   Full:    {fullFile}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error analyzing {site}: {ex.Message}");
                    Console.WriteLine($"   Stack trace: {ex.StackTrace}");
                }

                Console.WriteLine(); // Empty line between sites
            }

            Console.WriteLine("✅ Template structure analysis complete!");
        }

        #endregion

        #region Performance Methods

        static void RunPerformanceComparison(string assemblerWebDirPath, bool enableJsonProcessing = true, string appSiteFilter = null)
        {
            var summaryRows = Assembler.TemplatePerformance.PerformanceUtils.RunPerformanceComparison(assemblerWebDirPath, enableJsonProcessing, appSiteFilter);
            Assembler.TemplatePerformance.PerformanceUtils.PrintPerfSummaryTable(assemblerWebDirPath, summaryRows);
        }

        static void RunPerformanceInvestigation(string assemblerWebDirPath, string appSiteFilter = null)
        {
            int iterations = 1000;
            string appSitesPath = Path.Combine(assemblerWebDirPath, "AppSites");
            if (!Directory.Exists(appSitesPath))
            {
                Console.WriteLine($"❌ AppSites directory not found: {appSitesPath}");
                return;
            }
            var allAppSiteDirs = Directory.GetDirectories(appSitesPath, "*", SearchOption.TopDirectoryOnly);
            var appSites = allAppSiteDirs.Select(dir => Path.GetFileName(dir)).ToArray();
            foreach (var testAppSite in appSites)
            {
                if (!string.IsNullOrEmpty(appSiteFilter) && !testAppSite.Equals(appSiteFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                string appSiteDir = Path.Combine(appSitesPath, testAppSite);
                if (!Directory.Exists(appSiteDir))
                {
                    Console.WriteLine($"❌ {testAppSite} appsite not found: {appSiteDir}");
                    continue;
                }
                var htmlFiles = Directory.GetFiles(appSiteDir, "*.html", SearchOption.TopDirectoryOnly);
                foreach (var htmlFilePath in htmlFiles)
                {
                    var appFileName = Path.GetFileNameWithoutExtension(htmlFilePath);

                    // Build AppView scenarios (same logic as StandardTests/AdvancedTests)
                    var appViewScenarios = new List<(string AppView, string AppViewPrefix)>
                    {
                        ("", "") // No AppView
                    };
                    var appSitePathInner = Path.Combine(assemblerWebDirPath, "AppSites", testAppSite);
                    var viewsPath = Path.Combine(appSitePathInner, "Views");
                    if (Directory.Exists(viewsPath))
                    {
                        var viewFiles = Directory.GetFiles(viewsPath, "*.html");
                        foreach (var viewFile in viewFiles)
                        {
                            var viewName = Path.GetFileNameWithoutExtension(viewFile);
                            var appView = "";
                            var appViewPrefix = "";
                            if (viewName.ToLowerInvariant().Contains("content"))
                            {
                                var contentIndex = viewName.ToLowerInvariant().IndexOf("content");
                                if (contentIndex > 0)
                                {
                                    var viewPart = viewName.Substring(0, contentIndex);
                                    if (viewPart.Length > 0)
                                    {
                                        appView = char.ToUpper(viewPart[0]) + viewPart.Substring(1);
                                        appViewPrefix = appView.Substring(0, Math.Min(appView.Length, 6));
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(appView))
                            {
                                appViewScenarios.Add((appView, appViewPrefix));
                            }
                        }
                    }

                    var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, testAppSite);
                    var preprocessedTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, testAppSite).Templates;

                    string mainTemplateKey = (testAppSite + "_" + appFileName).ToLowerInvariant();
                    if (!templates.TryGetValue(mainTemplateKey, out var mainTemplate))
                    {
                        Console.WriteLine($"❌ No main template found for key: {mainTemplateKey}");
                        continue;
                    }
                    if (!preprocessedTemplates.TryGetValue(mainTemplateKey, out var mainPreTemplate))
                    {
                        Console.WriteLine($"❌ No preprocessed main template found for key: {mainTemplateKey}");
                        continue;
                    }

                    foreach (var scenario in appViewScenarios)
                    {
                        Console.WriteLine($"\n📊 C# PERFORMANCE ANALYSIS: {testAppSite}, {appFileName}, AppView='{scenario.AppView}'"); //, AppViewPrefix='{scenario.AppViewPrefix}'
                        Console.WriteLine(new string('=', 60));

                        // Normal Engine
                        LoaderNormal.ClearCache();
                        var normalEngine = new EngineNormal();
                        normalEngine.AppViewPrefix = scenario.AppViewPrefix;
                        var sw = Stopwatch.StartNew();
                        string normalResult = "";
                        for (int i = 0; i < iterations; i++)
                        {
                            normalResult = normalEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, templates, false);
                        }
                        sw.Stop();
                        var normalTime = sw.ElapsedMilliseconds;

                        // PreProcess Engine
                        LoaderPreProcess.ClearCache();
                        var preProcessEngine = new EnginePreProcess();
                        preProcessEngine.AppViewPrefix = scenario.AppViewPrefix;
                        sw.Restart();
                        string preProcessResult = "";
                        for (int i = 0; i < iterations; i++)
                        {
                            preProcessResult = preProcessEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, preprocessedTemplates, false);
                        }
                        sw.Stop();
                        var preProcessTime = sw.ElapsedMilliseconds;

                        // Analysis
                        Console.WriteLine($"Normal Engine:     {normalTime}ms");
                        Console.WriteLine($"PreProcess Engine: {preProcessTime}ms");
                        Console.WriteLine($"Results match:     {(normalResult == preProcessResult ? "✅ YES" : "❌ NO")}");
                        Console.WriteLine($"Normal size:       {normalResult?.Length ?? 0} chars");
                        Console.WriteLine($"PreProcess size:   {preProcessResult?.Length ?? 0} chars");

                        if (string.IsNullOrEmpty(normalResult) || string.IsNullOrEmpty(preProcessResult))
                        {
                            Console.WriteLine("❌ Output is empty. Check template keys and input files for this appsite.");
                        }
                        else if (preProcessTime < normalTime)
                        {
                            var diffMs = normalTime - preProcessTime;
                            var diffPct = normalTime > 0 ? ((double)diffMs / normalTime) * 100 : 0;
                            Console.WriteLine($"✅ PreProcess Engine is faster by {diffMs}ms ({diffPct:F1}%)");
                        }
                        else if (preProcessTime > normalTime)
                        {
                            var diffMs = preProcessTime - normalTime;
                            var diffPct = preProcessTime > 0 ? ((double)diffMs / preProcessTime) * 100 : 0;
                            Console.WriteLine($"❌ Normal Engine is faster by {diffMs}ms ({diffPct:F1}%)");
                        }
                        else
                        {
                            Console.WriteLine($"⚖️  Both engines have equal performance.");
                        }
                    }
                }
            }
        }

        #endregion

        #region Compare Methods

        /// <summary>
        /// Analyzes differences between two template outputs by comparing sections
        /// </summary>
        private static void AnalyzeOutputDifferences(string output1, string output2)
        {
            // Split both outputs into lines for comparison
            var lines1 = output1.Split('\n');
            var lines2 = output2.Split('\n');

            Console.WriteLine($"   Lines: {lines1.Length} vs {lines2.Length}");

            // Compare line by line
            int commonLength = Math.Min(lines1.Length, lines2.Length);
            for (int i = 0; i < commonLength; i++)
            {
                if (lines1[i] != lines2[i])
                {
                    Console.WriteLine($"\n   Difference at line {i + 1}:");
                    Console.WriteLine($"   Normal:    {lines1[i].Length} chars");
                    Console.WriteLine($"   PreProcess:{lines2[i].Length} chars");

                    // Show first position where they differ
                    var minLength = Math.Min(lines1[i].Length, lines2[i].Length);
                    for (int j = 0; j < minLength; j++)
                    {
                        if (lines1[i][j] != lines2[i][j])
                        {
                            Console.WriteLine($"   First difference at character {j + 1}: '{lines1[i][j]}' vs '{lines2[i][j]}'");
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Compare template output with and without JSON processing enabled
        /// </summary>
        private void CompareJsonProcessing(string assemblerWebDirPath, string appSite, string appFile, bool enableJsonProcessing = true)
        {
            Console.WriteLine($"\n📊 Testing JSON Processing Impact for {appSite} : {appFile}");
            Console.WriteLine(new string('-', 50));

            var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, appSite);
            var preprocessedTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, appSite).Templates;

            if (templates == null || templates.Count == 0)
            {
                Console.WriteLine($"❌ No templates found for {appSite}");
                return;
            }

            // Build AppView scenarios
            var appViewScenarios = new System.Collections.Generic.List<(string AppView, string AppViewPrefix)>
            {
                ("", ""), // No AppView
            };
            var appSitesPath = Path.Combine(assemblerWebDirPath, "AppSites", appSite);
            var viewsPath = Path.Combine(appSitesPath, "Views");
            if (Directory.Exists(viewsPath))
            {
                var viewFiles = Directory.GetFiles(viewsPath, "*.html");
                foreach (var viewFile in viewFiles)
                {
                    var viewName = Path.GetFileNameWithoutExtension(viewFile);
                    var appView = "";
                    var appViewPrefix = "";
                    if (viewName.ToLowerInvariant().Contains("content"))
                    {
                        var contentIndex = viewName.ToLowerInvariant().IndexOf("content");
                        if (contentIndex > 0)
                        {
                            var viewPart = viewName.Substring(0, contentIndex);
                            if (viewPart.Length > 0)
                            {
                                appView = char.ToUpper(viewPart[0]) + viewPart.Substring(1);
                                appViewPrefix = appView.Substring(0, Math.Min(appView.Length, 6));
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(appView))
                    {
                        appViewScenarios.Add((appView, appViewPrefix));
                    }
                }
            }

            foreach (var scenario in appViewScenarios)
            {
                Console.WriteLine($"\n🔍 Testing scenario: AppView='{scenario.AppView}', AppViewPrefix='{scenario.AppViewPrefix}'");
                var normalEngine = new EngineNormal();
                var preProcessEngine = new EnginePreProcess();
                normalEngine.AppViewPrefix = scenario.AppViewPrefix;
                preProcessEngine.AppViewPrefix = scenario.AppViewPrefix;

                // Find the main template key for both engines
                var mainTemplateKey = templates.Keys.FirstOrDefault(k => k.StartsWith(appSite.ToLowerInvariant() + "_", StringComparison.OrdinalIgnoreCase));
                var mainPreprocessedKey = preprocessedTemplates.Keys.FirstOrDefault(k => k.StartsWith(appSite.ToLowerInvariant() + "_", StringComparison.OrdinalIgnoreCase));

                string resultNormal = "";
                string resultPreProcess = "";
                if (mainTemplateKey != null)
                    resultNormal = normalEngine.MergeTemplates(appSite, mainTemplateKey.Substring(mainTemplateKey.IndexOf('_') + 1), scenario.AppView, templates, enableJsonProcessing);
                if (mainPreprocessedKey != null)
                    resultPreProcess = preProcessEngine.MergeTemplates(appSite, mainPreprocessedKey.Substring(mainPreprocessedKey.IndexOf('_') + 1), scenario.AppView, preprocessedTemplates, enableJsonProcessing);

                Console.WriteLine($"   📏 Normal Engine Output: {resultNormal.Length} chars");
                Console.WriteLine($"   📏 PreProcess Engine Output: {resultPreProcess.Length} chars");

                // Compare results between engines
                bool outputsMatch = resultNormal == resultPreProcess;
                Console.WriteLine($"\n✅ Outputs {(outputsMatch ? "Match! ✨" : "Differ ❌")}");

                if (!outputsMatch)
                {
                    // Save outputs for comparison if they differ
                    var testOutputDir = Path.Combine(AppContext.BaseDirectory, "test_output");
                    Directory.CreateDirectory(testOutputDir);

                    File.WriteAllText(Path.Combine(testOutputDir, $"{appSite}_normal_{scenario.AppView}_{(enableJsonProcessing ? "with" : "no")}_json.html"), resultNormal);
                    File.WriteAllText(Path.Combine(testOutputDir, $"{appSite}_preprocess_{scenario.AppView}_{(enableJsonProcessing ? "with" : "no")}_json.html"), resultPreProcess);

                    Console.WriteLine($"\n📄 Outputs saved to: {testOutputDir}");

                    // Show a diff of lengths by section to help identify where they differ
                    Console.WriteLine("\n� Output Analysis:");
                    AnalyzeOutputDifferences(resultNormal, resultPreProcess);
                }
            }
        }

        #endregion

        #region Print Utilities

        static void PrintTestSummaryTable(string assemblerWebDirPath, List<TestSummaryRow> summaryRows, string testType)
        {
            if (summaryRows == null || summaryRows.Count == 0)
                return;
            if (string.IsNullOrEmpty(testType)) testType = "TEST";
            Console.WriteLine($"\n==================== C# {testType.ToUpperInvariant()} SUMMARY ====================\n");

            var headers = new[] { "AppSite", "AppFile", "AppView", "OutputMatch", "ViewUnMatch", "Error" };
            int colCount = headers.Length;
            int[] widths = new int[colCount];
            for (int i = 0; i < colCount; i++)
            {
                int maxLen = headers[i].Length;
                foreach (var row in summaryRows)
                {
                    string value = GetValue(row, i);
                    if (value.Length > maxLen) maxLen = value.Length;
                }
                widths[i] = maxLen < 10 ? 10 : maxLen;
            }

            // Print header
            Console.Write("| ");
            for (int i = 0; i < colCount; i++)
            {
                Console.Write(headers[i].PadRight(widths[i]));
                if (i < colCount - 1) Console.Write(" | ");
            }
            Console.WriteLine(" |");

            // Print divider
            Console.Write("|");
            for (int i = 0; i < colCount; i++)
            {
                Console.Write(" " + new string('-', widths[i]) + " ");
                if (i < colCount - 1) Console.Write("|");
            }
            Console.WriteLine("|");

            // Print rows
            foreach (var row in summaryRows)
            {
                Console.Write("| ");
                Console.Write((row.AppSite ?? "").PadRight(widths[0]));
                Console.Write(" | ");
                Console.Write((row.AppFile ?? "").PadRight(widths[1]));
                Console.Write(" | ");
                Console.Write((row.AppView ?? "").PadRight(widths[2]));
                Console.Write(" | ");
                Console.Write((row.NormalPreProcess ?? "").PadRight(widths[3]));
                Console.Write(" | ");
                Console.Write((row.CrossViewUnMatch ?? "").PadRight(widths[4]));
                Console.Write(" | ");
                Console.Write((row.Error ?? "").PadRight(widths[5]));
                Console.WriteLine(" |");
            }
            // Print bottom divider
            Console.Write("|");
            for (int i = 0; i < colCount; i++)
            {
                Console.Write(" " + new string('-', widths[i]) + " ");
                if (i < colCount - 1) Console.Write("|");
            }
            Console.WriteLine("|");

            // Save HTML file
            try
            {
                var html = new System.Text.StringBuilder();
                html.AppendLine("<html><head><title>Test Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}</style></head><body>");
                html.AppendLine($"<h2>C# {testType.ToUpperInvariant()} SUMMARY TABLE</h2>");
                html.AppendLine("<table>");
                html.Append("<tr>");
                foreach (var h in headers) html.Append($"<th>{h}</th>");
                html.AppendLine("</tr>");
                foreach (var row in summaryRows)
                {
                    html.Append("<tr>");
                    html.Append($"<td>{row.AppSite}</td>");
                    html.Append($"<td>{row.AppFile}</td>");
                    html.Append($"<td>{row.AppView}</td>");
                    html.Append($"<td>{row.NormalPreProcess}</td>");
                    html.Append($"<td>{row.CrossViewUnMatch}</td>");
                    html.Append($"<td>{row.Error}</td>");
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</table></body></html>");

                // Sanitize testType for filename
                string testTypeFile = testType.Replace(" ", "").Replace("-", "").ToLowerInvariant();
                var outFile = Path.Combine(assemblerWebDirPath, $"csharp_{testTypeFile}_Summary.html");
                System.IO.File.WriteAllText(outFile, html.ToString());
                Console.WriteLine($"Test summary HTML saved to: {outFile}");
                // Save JSON summary file (new, matches Rust)
                var jsonFile = Path.Combine(assemblerWebDirPath, $"csharp_{testTypeFile}_Summary.json");
                var json = Arshu.App.Json.JsonConverter.SerializeObject(summaryRows, true);
                System.IO.File.WriteAllText(jsonFile, json);
                Console.WriteLine($"Test summary JSON saved to: {jsonFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving test summary HTML: {ex.Message}");
            }
            Console.WriteLine("\n======================================================\n");
        }


        static string GetValue(TestSummaryRow row, int index)
        {
            switch (index)
            {
                case 0: return row.AppSite ?? "";
                case 1: return row.AppFile ?? "";
                case 2: return row.AppView ?? "";
                case 3: return row.NormalPreProcess ?? "";
                case 4: return row.CrossViewUnMatch ?? "";
                case 5: return row.Error ?? "";
                default: return "";
            }
        }

        #endregion

        #region Utilities

        static T TestWithTiming<T>(string methodName, Func<T> method)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = method();
                sw.Stop();
                Console.WriteLine($"✅ {methodName}: {sw.ElapsedTicks} ticks ({sw.ElapsedMilliseconds}ms)");
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"❌ {methodName}: FAILED - {ex.Message}");
                return default(T);
            }
        }

        #endregion

    }
}

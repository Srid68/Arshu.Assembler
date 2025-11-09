using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Arshu.App.Json;
using Assembler.Engine;
using Assembler.Loader;
using Assembler.Config;
using Assembler.Model;
using Assembler.Api;

namespace Assembler.Test;

public class TestSummaryRow
{
    public string? AppSite { get; set; }
    public string? AppFile { get; set; }
    public string? AppView { get; set; }
    public string? NormalPreProcess { get; set; }
    public string? CrossViewUnMatch { get; set; }
    public string? Error { get; set; }

    // Advanced test fields - output sizes for all 4 engines
    public int NormalSize { get; set; }
    public int NormalJsonSize { get; set; }
    public int PreProcessSize { get; set; }
    public int PreProcessJsonSize { get; set; }
    public bool AllEnginesMatch { get; set; }
}

public static class TestingUtils
{
    /// <summary>
    /// Checks if output contains unreplaced placeholders ({{...}})
    /// Returns error message if found, null if clean
    /// Supports negative testing: AppSites ending with "-Fail" should have unreplaced placeholders
    /// </summary>
    private static string? CheckForUnreplacedPlaceholders(string output, string appSite, bool skipDetails)
    {
        // Negative testing disabled - first verify all engines fail consistently
        bool hasUnresolved = false;
        string? unresolvedPlaceholder = null;

        if (string.IsNullOrWhiteSpace(output))
        {
            return "Empty output";
        }

        // Scan for any {{...}} patterns which indicate unresolved placeholders
        int startIndex = 0;
        while ((startIndex = output.IndexOf("{{", startIndex)) >= 0)
        {
            int endIndex = output.IndexOf("}}", startIndex);
            if (endIndex >= 0)
            {
                hasUnresolved = true;
                unresolvedPlaceholder = output.Substring(startIndex, endIndex - startIndex + 2);
                if (!skipDetails)
                {
                    Console.WriteLine($"{appSite}: ❌ Found unreplaced placeholder: {unresolvedPlaceholder}");
                }
                break;
            }
            startIndex = endIndex + 2;
        }

        // Always fail on unreplaced placeholders (negative testing will be added later)
        return hasUnresolved ? $"Unreplaced placeholder found: {unresolvedPlaceholder}" : null;
    }

    public static List<TestSummaryRow> RunStandardTests(string assemblerWebDirPath, string projectDirectory, List<Scenario> scenarios, string searchAppSites, bool printHtmlOutput = false, bool skipDetails = false, bool enableJsonProcessing = true)
    {
        var summaryRows = new List<TestSummaryRow>();

        if (string.IsNullOrEmpty(assemblerWebDirPath))
        {
            Console.WriteLine("❌ No assemblerWebDirPath passed");
            return summaryRows;
        }

        if (string.IsNullOrEmpty(projectDirectory))
        {
            Console.WriteLine("❌ No projectDirectory passed");
            return summaryRows;
        }

        if (scenarios == null || scenarios.Count == 0)
        {
            Console.WriteLine("❌ No scenarios passed");
            return summaryRows;
        }

        // Group scenarios by AppSite and AppFile
        var groupedScenarios = new Dictionary<(string AppSite, string AppFile), List<Scenario>>();
        for (int i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var key = (scenario.AppSite, scenario.AppFile);
            if (!groupedScenarios.ContainsKey(key))
            {
                groupedScenarios[key] = new List<Scenario>();
            }
            groupedScenarios[key].Add(scenario);
        }

        var outputDir = Path.Combine(projectDirectory ?? "", "Analysis", "output");
        Directory.CreateDirectory(outputDir);

        foreach (var kvp in groupedScenarios)
        {
            var testSite = kvp.Key.AppSite;
            var appFileName = kvp.Key.AppFile;
            var group = kvp.Value;

            if (!skipDetails)
            {
                Console.WriteLine($"{testSite}: 🔍 STANDARD TEST : appsite: {testSite} appfile: {appFileName}");
                Console.WriteLine($"{testSite}: {new string('=', 50)}");
            }

            try
            {
                var scenarioOutputs = new List<string>();
                var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites);

                foreach (var scenario in group)
                {
                    var appView = scenario.AppView;
                    var normalEngine = new EngineNormal();
                    normalEngine.AppViewPrefix = appFileName;
                    var resultNormal = normalEngine.MergeTemplates(testSite, appFileName, appView, templates, searchAppSites, enableJsonProcessing);
                    scenarioOutputs.Add(resultNormal ?? "");

                    // Save HTML output to Analysis folder
                    var appViewSuffix = string.IsNullOrEmpty(appView) ? "" : $"_{appView}";
                    var outputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_normal.html");
                    File.WriteAllText(outputFile, resultNormal ?? "");

                    if (!skipDetails)
                    {
                        Console.WriteLine($"{testSite}: 🧪 STANDARD TEST : scenario: AppView='{appView}'");
                        Console.WriteLine($"Output length = {resultNormal?.Length ?? 0}");
                    }
                    if (printHtmlOutput)
                    {
                        Console.WriteLine($"\nFULL HTML OUTPUT for AppView '{appView}':\n{resultNormal}\n");
                    }
                }

                // Validate for unresolved placeholders using new helper
                var scenarioErrors = new List<string?>();
                foreach (var output in scenarioOutputs)
                {
                    var error = CheckForUnreplacedPlaceholders(output, testSite, skipDetails);
                    scenarioErrors.Add(error);
                }

                // Compare outputs for cross-view
                string matchResult = "";
                if (group.Count > 1) // at least two AppViews to compare
                {
                    bool allDiffer = true;
                    var firstAppViewOutput = scenarioOutputs[0];
                    for (int i = 1; i < scenarioOutputs.Count; i++)
                    {
                        if (scenarioOutputs[i] == firstAppViewOutput)
                        {
                            allDiffer = false;
                            break;
                        }
                    }
                    matchResult = allDiffer ? "PASS" : "FAIL";
                    if (!skipDetails)
                    {
                        if (allDiffer)
                            Console.WriteLine($"✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in {testSite} as expected.");
                        else
                            Console.WriteLine($"❌ FAILURE: Some outputs for AppViews MATCH in {testSite}. Expected them to differ.");
                    }
                }

                // Add summary rows for each scenario (validate all AppViews, not just the first)
                for (int i = 0; i < group.Count; i++)
                {
                    var scenario = group[i];
                    var crossView = (i > 0 && group.Count > 1) ? matchResult : "";
                    var error = scenarioErrors[i];
                    // Validate all AppViews, not just the first one
                    var normalPreProcess = (error != null ? "FAIL" : "PASS");

                    summaryRows.Add(new TestSummaryRow
                    {
                        AppSite = testSite,
                        AppFile = appFileName,
                        AppView = scenario.AppView,
                        NormalPreProcess = normalPreProcess,
                        CrossViewUnMatch = crossView,
                        Error = error ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in {testSite}/{appFileName}: {ex.Message}");
                summaryRows.Add(new TestSummaryRow
                {
                    AppSite = testSite,
                    AppFile = appFileName,
                    AppView = "",
                    NormalPreProcess = "",
                    CrossViewUnMatch = "",
                    Error = ex.Message
                });
            }
        }
        return summaryRows;
    }

    public static List<TestSummaryRow> RunStandardJsonTests(string assemblerWebDirPath, string projectDirectory, List<Scenario> scenarios, string searchAppSites, bool printHtmlOutput = false, bool skipDetails = false, bool enableJsonProcessing = true)
    {
        var summaryRows = new List<TestSummaryRow>();

        if (string.IsNullOrEmpty(assemblerWebDirPath))
        {
            Console.WriteLine("❌ No assemblerWebDirPath passed");
            return summaryRows;
        }

        if (string.IsNullOrEmpty(projectDirectory))
        {
            Console.WriteLine("❌ No projectDirectory passed");
            return summaryRows;
        }

        if (scenarios == null || scenarios.Count == 0)
        {
            Console.WriteLine("❌ No scenarios passed");
            return summaryRows;
        }

        // Group scenarios by AppSite and AppFile
        var groupedScenarios = new Dictionary<(string AppSite, string AppFile), List<Scenario>>();
        for (int i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var key = (scenario.AppSite, scenario.AppFile);
            if (!groupedScenarios.ContainsKey(key))
            {
                groupedScenarios[key] = new List<Scenario>();
            }
            groupedScenarios[key].Add(scenario);
        }

        var outputDir = Path.Combine(projectDirectory ?? "", "Analysis", "output");
        Directory.CreateDirectory(outputDir);

        foreach (var kvp in groupedScenarios)
        {
            var testSite = kvp.Key.AppSite;
            var appFileName = kvp.Key.AppFile;
            var group = kvp.Value;

            if (!skipDetails)
            {
                Console.WriteLine($"{testSite}: 🔍 STANDARD JSON TEST : appsite: {testSite} appfile: {appFileName}");
                Console.WriteLine($"{testSite}: {new string('=', 50)}");
            }

            try
            {
                var scenarioOutputs = new List<string>();
                var loader = new LoaderNormalJson(assemblerWebDirPath, testSite, searchAppSites);

                foreach (var scenario in group)
                {
                    var appView = scenario.AppView;
                    var normalJsonEngine = new EngineNormalJson();
                    normalJsonEngine.AppViewPrefix = appFileName;
                    var resultNormalJson = normalJsonEngine.MergeTemplates(testSite, appFileName, appView, loader, enableJsonProcessing);
                    scenarioOutputs.Add(resultNormalJson ?? "");

                    // Save HTML output to Analysis folder
                    var appViewSuffix = string.IsNullOrEmpty(appView) ? "" : $"_{appView}";
                    var outputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_normaljson.html");
                    File.WriteAllText(outputFile, resultNormalJson ?? "");

                    if (!skipDetails)
                    {
                        Console.WriteLine($"{testSite}: 🧪 STANDARD JSON TEST : scenario: AppView='{appView}'");
                        Console.WriteLine($"Output length = {resultNormalJson?.Length ?? 0}");
                    }
                    if (printHtmlOutput)
                    {
                        Console.WriteLine($"\nFULL HTML OUTPUT for AppView '{appView}':\n{resultNormalJson}\n");
                    }
                }

                // Validate for unresolved placeholders using new helper
                var scenarioErrors = new List<string?>();
                foreach (var output in scenarioOutputs)
                {
                    var error = CheckForUnreplacedPlaceholders(output, testSite, skipDetails);
                    scenarioErrors.Add(error);
                }

                // Compare outputs for cross-view
                string matchResult = "";
                if (group.Count > 1) // at least two AppViews to compare
                {
                    bool allDiffer = true;
                    var firstAppViewOutput = scenarioOutputs[0];
                    for (int i = 1; i < scenarioOutputs.Count; i++)
                    {
                        if (scenarioOutputs[i] == firstAppViewOutput)
                        {
                            allDiffer = false;
                            break;
                        }
                    }
                    matchResult = allDiffer ? "PASS" : "FAIL";
                    if (!skipDetails)
                    {
                        if (allDiffer)
                            Console.WriteLine($"✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in {testSite} as expected.");
                        else
                            Console.WriteLine($"❌ FAILURE: Some outputs for AppViews MATCH in {testSite}. Expected them to differ.");
                    }
                }

                // Add summary rows for each scenario (validate all AppViews, not just the first)
                for (int i = 0; i < group.Count; i++)
                {
                    var scenario = group[i];
                    var crossView = (i > 0 && group.Count > 1) ? matchResult : "";
                    var error = scenarioErrors[i];
                    // Validate all AppViews, not just the first one
                    var normalPreProcess = (error != null ? "FAIL" : "PASS");

                    summaryRows.Add(new TestSummaryRow
                    {
                        AppSite = testSite,
                        AppFile = appFileName,
                        AppView = scenario.AppView,
                        NormalPreProcess = normalPreProcess,
                        CrossViewUnMatch = crossView,
                        Error = error ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in {testSite}/{appFileName}: {ex.Message}");
                summaryRows.Add(new TestSummaryRow
                {
                    AppSite = testSite,
                    AppFile = appFileName,
                    AppView = "",
                    NormalPreProcess = "",
                    CrossViewUnMatch = "",
                    Error = ex.Message
                });
            }
        }
        return summaryRows;
    }

    public static List<TestSummaryRow> RunStandardPreProcessTests(string assemblerWebDirPath, string projectDirectory, List<Scenario> scenarios, string searchAppSites, bool printHtmlOutput = false, bool skipDetails = false, bool enableJsonProcessing = true)
    {
        var summaryRows = new List<TestSummaryRow>();

        if (string.IsNullOrEmpty(assemblerWebDirPath))
        {
            Console.WriteLine("❌ No assemblerWebDirPath passed");
            return summaryRows;
        }

        if (string.IsNullOrEmpty(projectDirectory))
        {
            Console.WriteLine("❌ No projectDirectory passed");
            return summaryRows;
        }

        if (scenarios == null || scenarios.Count == 0)
        {
            Console.WriteLine("❌ No scenarios passed");
            return summaryRows;
        }

        // Group scenarios by AppSite and AppFile
        var groupedScenarios = new Dictionary<(string AppSite, string AppFile), List<Scenario>>();
        for (int i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var key = (scenario.AppSite, scenario.AppFile);
            if (!groupedScenarios.ContainsKey(key))
            {
                groupedScenarios[key] = new List<Scenario>();
            }
            groupedScenarios[key].Add(scenario);
        }

        var outputDir = Path.Combine(projectDirectory ?? "", "Analysis", "output");
        Directory.CreateDirectory(outputDir);

        foreach (var kvp in groupedScenarios)
        {
            var testSite = kvp.Key.AppSite;
            var appFileName = kvp.Key.AppFile;
            var group = kvp.Value;

            if (!skipDetails)
            {
                Console.WriteLine($"{testSite}: 🔍 STANDARD PREPROCESS TEST : appsite: {testSite} appfile: {appFileName}");
                Console.WriteLine($"{testSite}: {new string('=', 50)}");
            }

            try
            {
                var scenarioOutputs = new List<string>();
                var preprocessedTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites).Templates;

                foreach (var scenario in group)
                {
                    var appView = scenario.AppView;
                    var preProcessEngine = new EnginePreProcess();
                    preProcessEngine.AppViewPrefix = appFileName;
                    var resultPreProcess = preProcessEngine.MergeTemplates(testSite, appFileName, appView, preprocessedTemplates, searchAppSites, enableJsonProcessing);
                    scenarioOutputs.Add(resultPreProcess ?? "");

                    // Save HTML output to Analysis folder
                    var appViewSuffix = string.IsNullOrEmpty(appView) ? "" : $"_{appView}";
                    var outputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_preprocess.html");
                    File.WriteAllText(outputFile, resultPreProcess ?? "");

                    if (!skipDetails)
                    {
                        Console.WriteLine($"{testSite}: 🧪 STANDARD PREPROCESS TEST : scenario: AppView='{appView}'");
                        Console.WriteLine($"Output length = {resultPreProcess?.Length ?? 0}");
                    }
                    if (printHtmlOutput)
                    {
                        Console.WriteLine($"\nFULL HTML OUTPUT for AppView '{appView}':\n{resultPreProcess}\n");
                    }
                }

                // Validate for unresolved placeholders using new helper
                var scenarioErrors = new List<string?>();
                foreach (var output in scenarioOutputs)
                {
                    var error = CheckForUnreplacedPlaceholders(output, testSite, skipDetails);
                    scenarioErrors.Add(error);
                }

                // Compare outputs for cross-view
                string matchResult = "";
                if (group.Count > 1) // at least two AppViews to compare
                {
                    bool allDiffer = true;
                    var firstAppViewOutput = scenarioOutputs[0];
                    for (int i = 1; i < scenarioOutputs.Count; i++)
                    {
                        if (scenarioOutputs[i] == firstAppViewOutput)
                        {
                            allDiffer = false;
                            break;
                        }
                    }
                    matchResult = allDiffer ? "PASS" : "FAIL";
                    if (!skipDetails)
                    {
                        if (allDiffer)
                            Console.WriteLine($"✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in {testSite} as expected.");
                        else
                            Console.WriteLine($"❌ FAILURE: Some outputs for AppViews MATCH in {testSite}. Expected them to differ.");
                    }
                }

                // Add summary rows for each scenario (validate all AppViews, not just the first)
                for (int i = 0; i < group.Count; i++)
                {
                    var scenario = group[i];
                    var crossView = (i > 0 && group.Count > 1) ? matchResult : "";
                    var error = scenarioErrors[i];
                    // Validate all AppViews, not just the first one
                    var normalPreProcess = (error != null ? "FAIL" : "PASS");

                    summaryRows.Add(new TestSummaryRow
                    {
                        AppSite = testSite,
                        AppFile = appFileName,
                        AppView = scenario.AppView,
                        NormalPreProcess = normalPreProcess,
                        CrossViewUnMatch = crossView,
                        Error = error ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in {testSite}/{appFileName}: {ex.Message}");
                summaryRows.Add(new TestSummaryRow
                {
                    AppSite = testSite,
                    AppFile = appFileName,
                    AppView = "",
                    NormalPreProcess = "",
                    CrossViewUnMatch = "",
                    Error = ex.Message
                });
            }
        }
        return summaryRows;
    }

    public static List<TestSummaryRow> RunStandardPreProcessJsonTests(string assemblerWebDirPath, string projectDirectory, List<Scenario> scenarios, string searchAppSites, bool printHtmlOutput = false, bool skipDetails = false, bool enableJsonProcessing = true)
    {
        var summaryRows = new List<TestSummaryRow>();

        if (string.IsNullOrEmpty(assemblerWebDirPath))
        {
            Console.WriteLine("❌ No assemblerWebDirPath passed");
            return summaryRows;
        }

        if (string.IsNullOrEmpty(projectDirectory))
        {
            Console.WriteLine("❌ No projectDirectory passed");
            return summaryRows;
        }

        if (scenarios == null || scenarios.Count == 0)
        {
            Console.WriteLine("❌ No scenarios passed");
            return summaryRows;
        }

        // Group scenarios by AppSite and AppFile
        var groupedScenarios = new Dictionary<(string AppSite, string AppFile), List<Scenario>>();
        for (int i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var key = (scenario.AppSite, scenario.AppFile);
            if (!groupedScenarios.ContainsKey(key))
            {
                groupedScenarios[key] = new List<Scenario>();
            }
            groupedScenarios[key].Add(scenario);
        }

        var outputDir = Path.Combine(projectDirectory ?? "", "Analysis", "output");
        Directory.CreateDirectory(outputDir);

        foreach (var kvp in groupedScenarios)
        {
            var testSite = kvp.Key.AppSite;
            var appFileName = kvp.Key.AppFile;
            var group = kvp.Value;

            if (!skipDetails)
            {
                Console.WriteLine($"{testSite}: 🔍 STANDARD PREPROCESS JSON TEST : appsite: {testSite} appfile: {appFileName}");
                Console.WriteLine($"{testSite}: {new string('=', 50)}");
            }

            try
            {
                var scenarioOutputs = new List<string>();
                var loaderPreProcessJson = new LoaderPreProcessJson(assemblerWebDirPath, testSite, searchAppSites);

                foreach (var scenario in group)
                {
                    var appView = scenario.AppView;
                    var preprocessJsonEngine = new EnginePreProcessJson();
                    preprocessJsonEngine.AppViewPrefix = appFileName;
                    var resultPreProcessJson = preprocessJsonEngine.MergeTemplates(testSite, appFileName, appView, loaderPreProcessJson, enableJsonProcessing);
                    scenarioOutputs.Add(resultPreProcessJson ?? "");

                    // Save HTML output to Analysis folder
                    var appViewSuffix = string.IsNullOrEmpty(appView) ? "" : $"_{appView}";
                    var outputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_preprocessjson.html");
                    File.WriteAllText(outputFile, resultPreProcessJson ?? "");

                    if (!skipDetails)
                    {
                        Console.WriteLine($"{testSite}: 🧪 STANDARD PREPROCESS JSON TEST : scenario: AppView='{appView}'");
                        Console.WriteLine($"Output length = {resultPreProcessJson?.Length ?? 0}");
                    }
                    if (printHtmlOutput)
                    {
                        Console.WriteLine($"\nFULL HTML OUTPUT for AppView '{appView}':\n{resultPreProcessJson}\n");
                    }
                }

                // Validate for unresolved placeholders using new helper
                var scenarioErrors = new List<string?>();
                foreach (var output in scenarioOutputs)
                {
                    var error = CheckForUnreplacedPlaceholders(output, testSite, skipDetails);
                    scenarioErrors.Add(error);
                }

                // Compare outputs for cross-view
                string matchResult = "";
                if (group.Count > 1) // at least two AppViews to compare
                {
                    bool allDiffer = true;
                    var firstAppViewOutput = scenarioOutputs[0];
                    for (int i = 1; i < scenarioOutputs.Count; i++)
                    {
                        if (scenarioOutputs[i] == firstAppViewOutput)
                        {
                            allDiffer = false;
                            break;
                        }
                    }
                    matchResult = allDiffer ? "PASS" : "FAIL";
                    if (!skipDetails)
                    {
                        if (allDiffer)
                            Console.WriteLine($"✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in {testSite} as expected.");
                        else
                            Console.WriteLine($"❌ FAILURE: Some outputs for AppViews MATCH in {testSite}. Expected them to differ.");
                    }
                }

                // Add summary rows for each scenario (validate all AppViews, not just the first)
                for (int i = 0; i < group.Count; i++)
                {
                    var scenario = group[i];
                    var crossView = (i > 0 && group.Count > 1) ? matchResult : "";
                    var error = scenarioErrors[i];
                    // Validate all AppViews, not just the first one
                    var normalPreProcess = (error != null ? "FAIL" : "PASS");

                    summaryRows.Add(new TestSummaryRow
                    {
                        AppSite = testSite,
                        AppFile = appFileName,
                        AppView = scenario.AppView,
                        NormalPreProcess = normalPreProcess,
                        CrossViewUnMatch = crossView,
                        Error = error ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in {testSite}/{appFileName}: {ex.Message}");
                summaryRows.Add(new TestSummaryRow
                {
                    AppSite = testSite,
                    AppFile = appFileName,
                    AppView = "",
                    NormalPreProcess = "",
                    CrossViewUnMatch = "",
                    Error = ex.Message
                });
            }
        }
        return summaryRows;
    }

    public static List<TestSummaryRow> RunAdvancedTests(string assemblerWebDirPath, string projectDirectory, List<Scenario> scenarios, string searchAppSites, bool printHtmlOutput = false, bool skipDetails = false, bool enableJsonProcessing = true)
    {
        var summaryRows = new List<TestSummaryRow>();

        if (string.IsNullOrEmpty(assemblerWebDirPath))
        {
            Console.WriteLine("❌ No assemblerWebDirPath passed");
            return summaryRows;
        }

        if (string.IsNullOrEmpty(projectDirectory))
        {
            Console.WriteLine("❌ No projectDirectory passed");
            return summaryRows;
        }

        if (scenarios == null || scenarios.Count == 0)
        {
            Console.WriteLine("❌ No scenarios passed");
            return summaryRows;
        }

        // Group scenarios by AppSite and AppFile
        var groupedScenarios = new Dictionary<(string AppSite, string AppFile), List<Scenario>>();
        for (int i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var key = (scenario.AppSite, scenario.AppFile);
            if (!groupedScenarios.ContainsKey(key))
            {
                groupedScenarios[key] = new List<Scenario>();
            }
            groupedScenarios[key].Add(scenario);
        }

        var outputDir = Path.Combine(projectDirectory ?? "", "Analysis", "output");
        Directory.CreateDirectory(outputDir);

        foreach (var kvp in groupedScenarios)
        {
            var testSite = kvp.Key.AppSite;
            var appFileName = kvp.Key.AppFile;
            var group = kvp.Value;

            if (!skipDetails)
            {
                Console.WriteLine($"🔍 ADVANCED TEST : appsite: {testSite} appfile: {appFileName}");
            }

            try
            {
                LoaderNormal.ClearCache();
                LoaderPreProcess.ClearCache();

                var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites);
                var loaderNormalJson = new LoaderNormalJson(assemblerWebDirPath, testSite, searchAppSites);
                var preprocessedTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites).Templates;
                var loaderPreProcessJson = new LoaderPreProcessJson(assemblerWebDirPath, testSite, searchAppSites);

                var scenarioResults = new List<(string AppView, string NormalOutput, string NormalJsonOutput, string PreProcessOutput, string PreProcessJsonOutput, string MatchStatus)>();

                foreach (var scenario in group)
                {
                    var appView = scenario.AppView;

                    var normalEngine = new EngineNormal();
                    normalEngine.AppViewPrefix = appFileName;

                    var normalJsonEngine = new EngineNormalJson();
                    normalJsonEngine.AppViewPrefix = appFileName;

                    var preProcessEngine = new EnginePreProcess();
                    preProcessEngine.AppViewPrefix = appFileName;

                    var preProcessJsonEngine = new EnginePreProcessJson();
                    preProcessJsonEngine.AppViewPrefix = appFileName;

                    var resultNormal = normalEngine.MergeTemplates(testSite, appFileName, appView, templates, searchAppSites, enableJsonProcessing);
                    var resultNormalJson = normalJsonEngine.MergeTemplates(testSite, appFileName, appView, loaderNormalJson, enableJsonProcessing);
                    var resultPreProcess = preProcessEngine.MergeTemplates(testSite, appFileName, appView, preprocessedTemplates, searchAppSites, enableJsonProcessing);
                    var resultPreProcessJson = preProcessJsonEngine.MergeTemplates(testSite, appFileName, appView, loaderPreProcessJson, enableJsonProcessing);

                    bool allMatch = resultNormal == resultNormalJson &&
                                   resultNormal == resultPreProcess &&
                                   resultNormal == resultPreProcessJson;
                    string matchStatus = allMatch ? "PASS" : "FAIL";

                    scenarioResults.Add((appView, resultNormal ?? "", resultNormalJson ?? "", resultPreProcess ?? "", resultPreProcessJson ?? "", matchStatus));

                    // Save HTML outputs to Analysis folder
                    var appViewSuffix = string.IsNullOrEmpty(appView) ? "" : $"_{appView}";
                    var normalOutputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_normal.html");
                    var normalJsonOutputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_normaljson.html");
                    var preprocessOutputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_preprocess.html");
                    var preprocessJsonOutputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_preprocessjson.html");
                    File.WriteAllText(normalOutputFile, resultNormal ?? "");
                    File.WriteAllText(normalJsonOutputFile, resultNormalJson ?? "");
                    File.WriteAllText(preprocessOutputFile, resultPreProcess ?? "");
                    File.WriteAllText(preprocessJsonOutputFile, resultPreProcessJson ?? "");

                    if (!skipDetails || !allMatch)
                    {
                        Console.WriteLine($"{testSite}: scenario: AppView='{appView}' - {matchStatus}");
                        if (!allMatch)
                        {
                            Console.WriteLine($"  Normal: {resultNormal?.Length ?? 0} chars");
                            Console.WriteLine($"  NormalJson: {resultNormalJson?.Length ?? 0} chars");
                            Console.WriteLine($"  PreProcess: {resultPreProcess?.Length ?? 0} chars");
                            Console.WriteLine($"  PreProcessJson: {resultPreProcessJson?.Length ?? 0} chars");
                        }
                    }

                    if (printHtmlOutput)
                    {
                        Console.WriteLine($"\nNORMAL OUTPUT for AppView '{appView}':\n{resultNormal}\n");
                        Console.WriteLine($"\nNORMAL JSON OUTPUT for AppView '{appView}':\n{resultNormalJson}\n");
                        Console.WriteLine($"\nPREPROCESS OUTPUT for AppView '{appView}':\n{resultPreProcess}\n");
                        Console.WriteLine($"\nPREPROCESS JSON OUTPUT for AppView '{appView}':\n{resultPreProcessJson}\n");
                    }
                }

                // Print detailed output analysis after processing all scenarios
                if (!skipDetails && scenarioResults.Count > 0)
                {
                    var firstResult = scenarioResults[0];
                    Console.WriteLine($"\n{testSite}: 📊 DETAILED OUTPUT ANALYSIS:");
                    Console.WriteLine($"   Normal length: {firstResult.NormalOutput.Length} chars");
                    Console.WriteLine($"   NormalJson length: {firstResult.NormalJsonOutput.Length} chars");
                    Console.WriteLine($"   PreProcess length: {firstResult.PreProcessOutput.Length} chars");
                    Console.WriteLine($"   PreProcessJson length: {firstResult.PreProcessJsonOutput.Length} chars");

                    bool allSame = firstResult.NormalOutput.Length == firstResult.NormalJsonOutput.Length &&
                                  firstResult.NormalOutput.Length == firstResult.PreProcessOutput.Length &&
                                  firstResult.NormalOutput.Length == firstResult.PreProcessJsonOutput.Length;
                    Console.WriteLine($"   All engines same length: {(allSame ? "YES" : "NO")}");
                }

                // Cross-view comparison
                string crossViewResult = "";
                if (scenarioResults.Count > 1)
                {
                    bool allDiffer = true;
                    for (int i = 1; i < scenarioResults.Count; i++)
                    {
                        for (int j = i + 1; j < scenarioResults.Count; j++)
                        {
                            if (scenarioResults[i].NormalOutput == scenarioResults[j].NormalOutput ||
                                scenarioResults[i].NormalJsonOutput == scenarioResults[j].NormalJsonOutput ||
                                scenarioResults[i].PreProcessOutput == scenarioResults[j].PreProcessOutput ||
                                scenarioResults[i].PreProcessJsonOutput == scenarioResults[j].PreProcessJsonOutput)
                            {
                                allDiffer = false;
                                break;
                            }
                        }
                        if (!allDiffer) break;
                    }
                    crossViewResult = allDiffer ? "PASS" : "FAIL";
                }

                // Add summary rows
                for (int i = 0; i < scenarioResults.Count; i++)
                {
                    var result = scenarioResults[i];
                    var crossView = (i > 0 && scenarioResults.Count > 1) ? crossViewResult : "";
                    bool allMatch = result.NormalOutput == result.NormalJsonOutput &&
                                   result.NormalOutput == result.PreProcessOutput &&
                                   result.NormalOutput == result.PreProcessJsonOutput;

                    summaryRows.Add(new TestSummaryRow
                    {
                        AppSite = testSite,
                        AppFile = appFileName,
                        AppView = result.AppView,
                        NormalPreProcess = result.MatchStatus,
                        CrossViewUnMatch = crossView,
                        Error = "",
                        NormalSize = result.NormalOutput.Length,
                        NormalJsonSize = result.NormalJsonOutput.Length,
                        PreProcessSize = result.PreProcessOutput.Length,
                        PreProcessJsonSize = result.PreProcessJsonOutput.Length,
                        AllEnginesMatch = allMatch
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing {testSite} {appFileName}: {ex.Message}");
                summaryRows.Add(new TestSummaryRow
                {
                    AppSite = testSite,
                    AppFile = appFileName,
                    AppView = "",
                    NormalPreProcess = "ERROR",
                    CrossViewUnMatch = "",
                    Error = ex.Message
                });
            }
        }
        return summaryRows;
    }

    public static void PrintTestSummaryTable(string assemblerWebDirPath, string projectDirectory, List<TestSummaryRow> summaryRows, string testType)
    {
        Console.WriteLine($"\n==================== C# {testType} SUMMARY ====================\n");

        // Check if this is an advanced test (has engine size data)
        bool isAdvancedTest = testType.Contains("ADVANCED", StringComparison.OrdinalIgnoreCase) ||
                             (summaryRows.Count > 0 && summaryRows[0].NormalSize > 0);

        if (isAdvancedTest)
        {
            // Advanced test table with engine output sizes
            Console.WriteLine($"| {"AppSite",-15} | {"AppFile",-10} | {"AppView",-10} | {"Normal",-8} | {"NormalJ",-8} | {"PreProc",-8} | {"PreProcJ",-8} | {"Match",-6} | {"ViewUnMatch",-11} | {"Error",-10} |");
            Console.WriteLine($"| {new string('-', 15)} | {new string('-', 10)} | {new string('-', 10)} | {new string('-', 8)} | {new string('-', 8)} | {new string('-', 8)} | {new string('-', 8)} | {new string('-', 6)} | {new string('-', 11)} | {new string('-', 10)} |");

            foreach (var row in summaryRows)
            {
                string matchIndicator = row.AllEnginesMatch ? "✓" : "✗";
                Console.WriteLine($"| {row.AppSite,-15} | {row.AppFile,-10} | {row.AppView,-10} | {row.NormalSize,-8} | {row.NormalJsonSize,-8} | {row.PreProcessSize,-8} | {row.PreProcessJsonSize,-8} | {matchIndicator,-6} | {row.CrossViewUnMatch,-11} | {row.Error,-10} |");
            }

            Console.WriteLine($"| {new string('-', 15)} | {new string('-', 10)} | {new string('-', 10)} | {new string('-', 8)} | {new string('-', 8)} | {new string('-', 8)} | {new string('-', 8)} | {new string('-', 6)} | {new string('-', 11)} | {new string('-', 10)} |");
        }
        else
        {
            // Standard test table (original format)
            Console.WriteLine($"| {"AppSite",-15} | {"AppFile",-10} | {"AppView",-10} | {"OutputMatch",-11} | {"ViewUnMatch",-11} | {"Error",-10} |");
            Console.WriteLine($"| {new string('-', 15)} | {new string('-', 10)} | {new string('-', 10)} | {new string('-', 11)} | {new string('-', 11)} | {new string('-', 10)} |");

            foreach (var row in summaryRows)
            {
                Console.WriteLine($"| {row.AppSite,-15} | {row.AppFile,-10} | {row.AppView,-10} | {row.NormalPreProcess,-11} | {row.CrossViewUnMatch,-11} | {row.Error,-10} |");
            }

            Console.WriteLine($"| {new string('-', 15)} | {new string('-', 10)} | {new string('-', 10)} | {new string('-', 11)} | {new string('-', 11)} | {new string('-', 10)} |");
        }

        string projectName = "csharp";
        string reportsDir = Path.Combine(projectDirectory, "Analysis", "Reports");
        Directory.CreateDirectory(reportsDir);
        string summaryHtmlFile = Path.Combine(reportsDir, $"{projectName}_{testType.ToLower().Replace(" ", "")}_Summary.html");
        string summaryJsonFile = Path.Combine(reportsDir, $"{projectName}_{testType.ToLower().Replace(" ", "")}_Summary.json");

        var htmlContent = GenerateSummaryHtml(summaryRows, testType);
        File.WriteAllText(summaryHtmlFile, htmlContent);
        Console.WriteLine($"Test summary HTML saved to: {summaryHtmlFile}");

        try
        {
            var jsonContent = JsonConverter.SerializeObject(summaryRows, true);
            File.WriteAllText(summaryJsonFile, jsonContent);
            Console.WriteLine($"Test summary JSON saved to: {summaryJsonFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Could not save JSON summary: {ex.Message}");
        }

        Console.WriteLine("\n======================================================\n");
    }

    private static string GenerateSummaryHtml(List<TestSummaryRow> summaryRows, string testType)
    {
        // Check if this is an advanced test
        bool isAdvancedTest = testType.Contains("ADVANCED", StringComparison.OrdinalIgnoreCase) ||
                             (summaryRows.Count > 0 && summaryRows[0].NormalSize > 0);

        var html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>C# {testType} Summary</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; }}
        h1 {{ color: #333; }}
        .table-container {{ overflow-x: auto; }}
        table {{ border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }}
        th, td {{ border: 1px solid #ddd; padding: 12px; text-align: left; }}
        th {{ background-color: #4CAF50; color: white; }}
        tr:nth-child(even) {{ background-color: #f2f2f2; }}
        .pass {{ color: green; font-weight: bold; }}
        .fail {{ color: red; font-weight: bold; }}
        .match {{ background-color: #d4edda; color: #155724; font-weight: bold; }}
        .mismatch {{ background-color: #f8d7da; color: #721c24; font-weight: bold; }}
        .size-match {{ background-color: #d4edda; }}
        .size-mismatch {{ background-color: #f8d7da; }}
        @media (max-width: 768px) {{
            body {{ margin: 10px; }}
            th, td {{ padding: 8px; font-size: 14px; }}
            h1 {{ font-size: 24px; }}
        }}
    </style>
</head>
<body>
    <h1>C# {testType} Summary</h1>
    <div class=""meta"" style=""color: #666; font-style: italic; margin-bottom: 10px;"">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>
    <div class=""table-container"">
    <table>
        <tr>
            <th>AppSite</th>
            <th>AppFile</th>
            <th>AppView</th>";

        if (isAdvancedTest)
        {
            html += @"
            <th>Normal</th>
            <th>NormalJson</th>
            <th>PreProcess</th>
            <th>PreProcessJson</th>
            <th>Match</th>";
        }
        else
        {
            html += @"
            <th>OutputMatch</th>";
        }

        html += @"
            <th>ViewUnMatch</th>
            <th>Error</th>
        </tr>";

        foreach (var row in summaryRows)
        {
            if (isAdvancedTest)
            {
                // Advanced test row with size data and color coding
                string sizeClass = row.AllEnginesMatch ? "size-match" : "size-mismatch";
                string matchClass = row.AllEnginesMatch ? "match" : "mismatch";
                string matchText = row.AllEnginesMatch ? "✓ PASS" : "✗ FAIL";
                var viewUnMatchClass = row.CrossViewUnMatch == "PASS" ? "pass" : (row.CrossViewUnMatch == "FAIL" ? "fail" : "");

                html += $@"
        <tr>
            <td>{row.AppSite}</td>
            <td>{row.AppFile}</td>
            <td>{row.AppView}</td>
            <td class=""{sizeClass}"">{row.NormalSize}</td>
            <td class=""{sizeClass}"">{row.NormalJsonSize}</td>
            <td class=""{sizeClass}"">{row.PreProcessSize}</td>
            <td class=""{sizeClass}"">{row.PreProcessJsonSize}</td>
            <td class=""{matchClass}"">{matchText}</td>
            <td class=""{viewUnMatchClass}"">{row.CrossViewUnMatch}</td>
            <td>{row.Error}</td>
        </tr>";
            }
            else
            {
                // Standard test row
                var outputMatchClass = row.NormalPreProcess == "PASS" ? "pass" : (row.NormalPreProcess == "FAIL" ? "fail" : "");
                var viewUnMatchClass = row.CrossViewUnMatch == "PASS" ? "pass" : (row.CrossViewUnMatch == "FAIL" ? "fail" : "");

                html += $@"
        <tr>
            <td>{row.AppSite}</td>
            <td>{row.AppFile}</td>
            <td>{row.AppView}</td>
            <td class=""{outputMatchClass}"">{row.NormalPreProcess}</td>
            <td class=""{viewUnMatchClass}"">{row.CrossViewUnMatch}</td>
            <td>{row.Error}</td>
        </tr>";
            }
        }

        html += @"
    </table>
    </div>
</body>
</html>";

        return html;
    }

    public static void DumpPreprocessedTemplateStructures(string assemblerWebDirPath, string projectDirectory, List<Scenario> scenarios, string searchAppSites, bool skipDetails = false)
    {
        if (string.IsNullOrEmpty(assemblerWebDirPath))
        {
            if (!skipDetails)
                Console.WriteLine("❌ No assemblerWebDirPath passed for DumpPreprocessedTemplateStructures");
            return;
        }

        if (string.IsNullOrEmpty(projectDirectory))
        {
            if (!skipDetails)
                Console.WriteLine("❌ No projectDirectory passed for DumpPreprocessedTemplateStructures");
            return;
        }

        if (scenarios == null || scenarios.Count == 0)
        {
            if (!skipDetails)
                Console.WriteLine("❌ No scenarios passed for DumpPreprocessedTemplateStructures");
            return;
        }

        // Get unique AppSites from scenarios
        var appSitesSet = new HashSet<string>();
        for (int i = 0; i < scenarios.Count; i++)
        {
            appSitesSet.Add(scenarios[i].AppSite);
        }
        var appSites = new List<string>(appSitesSet);

        foreach (string site in appSites)
        {

            try
            {
                LoaderNormal.ClearCache();
                LoaderPreProcess.ClearCache();

                var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, site, searchAppSites);
                var preprocessedSiteTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, site, searchAppSites);

                var fullJson = ApiResponse.SerializePreprocessedSiteTemplates(preprocessedSiteTemplates, true);

                // Save to file for easier analysis in 'dump' folder inside Analysis
                var outputDir = Path.Combine(projectDirectory, "Analysis", "dump");
                Directory.CreateDirectory(outputDir);

                var summaryFile = Path.Combine(outputDir, $"{site}_summary.json");
                var fullFile = Path.Combine(outputDir, $"{site}_full.json");

                if (File.Exists(summaryFile))
                    File.Delete(summaryFile);
                if (File.Exists(fullFile))
                    File.Delete(fullFile);

                var summary = ApiResponse.CreatePreprocessedSummary(preprocessedSiteTemplates);
                File.WriteAllText(summaryFile, ApiResponse.SerializePreprocessedSummary(summary, true));
                File.WriteAllText(fullFile, fullJson);

                if (!skipDetails)
                {
                    Console.WriteLine($"✅ Dumped structure for {site}");
                    Console.WriteLine($"   Summary: {summaryFile}");
                    Console.WriteLine($"   Full: {fullFile}");
                }
            }
            catch (Exception ex)
            {
                if (!skipDetails)
                {
                    Console.WriteLine($"❌ Error dumping structure for {site}: {ex.Message}");
                }
            }
        }
    }
}

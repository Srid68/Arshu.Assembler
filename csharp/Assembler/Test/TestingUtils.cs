using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
}

public static class TestingUtils
{
    public static List<TestSummaryRow> RunStandardTests(string assemblerWebDirPath, string projectDirectory, List<Scenario> scenarios, bool printHtmlOutput = false, bool skipDetails = false, bool enableJsonProcessing = true)
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
        var groupedScenarios = scenarios
            .GroupBy(s => new { s.AppSite, s.AppFile })
            .ToList();

        var outputDir = Path.Combine(projectDirectory ?? "", "Analysis", "output");
        Directory.CreateDirectory(outputDir);

        foreach (var group in groupedScenarios)
        {
            var testSite = group.Key.AppSite;
            var appFileName = group.Key.AppFile;

            if (!skipDetails)
            {
                Console.WriteLine($"{testSite}: 🔍 STANDARD TEST : appsite: {testSite} appfile: {appFileName}");
                Console.WriteLine($"{testSite}: {new string('=', 50)}");
            }

            try
            {
                var scenarioOutputs = new List<string>();
                var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, testSite);

                foreach (var scenario in group)
                {
                    var appView = scenario.AppView;
                    var normalEngine = new EngineNormal();
                    normalEngine.AppViewPrefix = appFileName;
                    var resultNormal = normalEngine.MergeTemplates(testSite, appFileName, appView, templates, enableJsonProcessing);
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

                // Validate for unresolved placeholders
                var scenarioUnresolved = new List<bool>();
                foreach (var output in scenarioOutputs)
                {
                    bool hasUnresolved = false;
                    bool isEmpty = string.IsNullOrWhiteSpace(output);

                    // Scan for any {{...}} patterns which indicate unresolved placeholders
                    int startIndex = 0;
                    while ((startIndex = output.IndexOf("{{", startIndex)) >= 0)
                    {
                        int endIndex = output.IndexOf("}}", startIndex);
                        if (endIndex >= 0)
                        {
                            // Any {{...}} pattern in final output is unresolved
                            hasUnresolved = true;
                            if (!skipDetails)
                            {
                                string content = output.Substring(startIndex, endIndex - startIndex + 2);
                                Console.WriteLine($"{testSite}: ❌ Found unresolved placeholder: {content}");
                            }
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }
                    scenarioUnresolved.Add(hasUnresolved || isEmpty);
                }

                // Compare outputs for cross-view
                string matchResult = "";
                if (group.Count() > 2) // default + at least two AppViews
                {
                    bool allDiffer = true;
                    var firstAppViewOutput = scenarioOutputs[1];
                    for (int i = 2; i < scenarioOutputs.Count; i++)
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

                // Add summary rows for each scenario (matching Rust logic)
                for (int i = 0; i < group.Count(); i++)
                {
                    var scenario = group.ElementAt(i);
                    var crossView = (i > 0 && group.Count() > 2) ? matchResult : "";
                    var hasUnresolved = scenarioUnresolved[i];
                    var normalPreProcess = (i == 0) ? (hasUnresolved ? "FAIL" : "PASS") : "";

                    summaryRows.Add(new TestSummaryRow
                    {
                        AppSite = testSite,
                        AppFile = appFileName,
                        AppView = scenario.AppView,
                        NormalPreProcess = normalPreProcess,
                        CrossViewUnMatch = crossView,
                        Error = ""
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

    public static List<TestSummaryRow> RunAdvancedTests(string assemblerWebDirPath, string projectDirectory, List<Scenario> scenarios, bool printHtmlOutput = false, bool skipDetails = false, bool enableJsonProcessing = true)
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
        var groupedScenarios = scenarios
            .GroupBy(s => new { s.AppSite, s.AppFile })
            .ToList();

        var outputDir = Path.Combine(projectDirectory ?? "", "Analysis", "output");
        Directory.CreateDirectory(outputDir);

        foreach (var group in groupedScenarios)
        {
            var testSite = group.Key.AppSite;
            var appFileName = group.Key.AppFile;

            if (!skipDetails)
            {
                Console.WriteLine($"🔍 ADVANCED TEST : appsite: {testSite} appfile: {appFileName}");
            }

            try
            {
                LoaderNormal.ClearCache();
                LoaderPreProcess.ClearCache();

                var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, testSite);
                var preprocessedTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, testSite).Templates;

                var scenarioResults = new List<(string AppView, string NormalOutput, string PreProcessOutput, string MatchStatus)>();

                foreach (var scenario in group)
                {
                    var appView = scenario.AppView;
                    var normalEngine = new EngineNormal();
                    normalEngine.AppViewPrefix = appFileName;
                    var preProcessEngine = new EnginePreProcess();
                    preProcessEngine.AppViewPrefix = appFileName;

                    var resultNormal = normalEngine.MergeTemplates(testSite, appFileName, appView, templates, enableJsonProcessing);
                    var resultPreProcess = preProcessEngine.MergeTemplates(testSite, appFileName, appView, preprocessedTemplates, enableJsonProcessing);

                    bool outputsMatch = resultNormal == resultPreProcess;
                    string matchStatus = outputsMatch ? "PASS" : "FAIL";

                    scenarioResults.Add((appView, resultNormal ?? "", resultPreProcess ?? "", matchStatus));

                    // Save HTML outputs to Analysis folder
                    var appViewSuffix = string.IsNullOrEmpty(appView) ? "" : $"_{appView}";
                    var normalOutputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_normal.html");
                    var preprocessOutputFile = Path.Combine(outputDir, $"{testSite}{appViewSuffix}_preprocess.html");
                    File.WriteAllText(normalOutputFile, resultNormal ?? "");
                    File.WriteAllText(preprocessOutputFile, resultPreProcess ?? "");

                    if (!skipDetails || !outputsMatch)
                    {
                        Console.WriteLine($"{testSite}: scenario: AppView='{appView}' - {matchStatus}");
                    }

                    if (printHtmlOutput)
                    {
                        Console.WriteLine($"\nNORMAL OUTPUT for AppView '{appView}':\n{resultNormal}\n");
                        Console.WriteLine($"\nPREPROCESS OUTPUT for AppView '{appView}':\n{resultPreProcess}\n");
                    }
                }

                // Print detailed output analysis after processing all scenarios
                if (scenarioResults.Count > 0)
                {
                    var firstResult = scenarioResults[0];
                    Console.WriteLine($"\n{testSite}: 📊 DETAILED OUTPUT ANALYSIS:");
                    Console.WriteLine($"   Normal length: {firstResult.NormalOutput.Length} chars");
                    Console.WriteLine($"   PreProcess length: {firstResult.PreProcessOutput.Length} chars");
                    Console.WriteLine($"   Difference: {Math.Abs(firstResult.NormalOutput.Length - firstResult.PreProcessOutput.Length)} chars");
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
                                scenarioResults[i].PreProcessOutput == scenarioResults[j].PreProcessOutput)
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

                    summaryRows.Add(new TestSummaryRow
                    {
                        AppSite = testSite,
                        AppFile = appFileName,
                        AppView = result.AppView,
                        NormalPreProcess = result.MatchStatus,
                        CrossViewUnMatch = crossView,
                        Error = ""
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
        Console.WriteLine($"| {"AppSite",-10} | {"AppFile",-10} | {"AppView",-10} | {"OutputMatch",-11} | {"ViewUnMatch",-11} | {"Error",-10} |");
        Console.WriteLine($"| {new string('-', 10)} | {new string('-', 10)} | {new string('-', 10)} | {new string('-', 11)} | {new string('-', 11)} | {new string('-', 10)} |");

        foreach (var row in summaryRows)
        {
            Console.WriteLine($"| {row.AppSite,-10} | {row.AppFile,-10} | {row.AppView,-10} | {row.NormalPreProcess,-11} | {row.CrossViewUnMatch,-11} | {row.Error,-10} |");
        }

        Console.WriteLine($"| {new string('-', 10)} | {new string('-', 10)} | {new string('-', 10)} | {new string('-', 11)} | {new string('-', 11)} | {new string('-', 10)} |");

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
            <th>AppView</th>
            <th>OutputMatch</th>
            <th>ViewUnMatch</th>
            <th>Error</th>
        </tr>";

        foreach (var row in summaryRows)
        {
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

        html += @"
    </table>
    </div>
</body>
</html>";

        return html;
    }

    public static void DumpPreprocessedTemplateStructures(string assemblerWebDirPath, string projectDirectory, List<Scenario> scenarios, bool skipDetails = false)
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
        var appSites = scenarios.Select(s => s.AppSite).Distinct().ToList();

        foreach (string site in appSites)
        {

            try
            {
                LoaderNormal.ClearCache();
                LoaderPreProcess.ClearCache();

                var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, site);
                var preprocessedSiteTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, site);

                var fullJson = ApiResponse.SerializePreprocessedSiteTemplates(preprocessedSiteTemplates, true);

                // Save to file for easier analysis
                var outputDir = Path.Combine(projectDirectory, "Analysis");
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

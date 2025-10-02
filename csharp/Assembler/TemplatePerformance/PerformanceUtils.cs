using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Assembler.TemplateEngine;
using Assembler.TemplateLoader;

namespace Assembler.TemplatePerformance;

public static class PerformanceUtils
{
    public class PerfSummaryRow
    {
        public string? AppSite { get; set; }
        public string? AppFile { get; set; }
        public string? AppView { get; set; }
        public int Iterations { get; set; }
        public long NormalTimeTicks { get; set; }
        public long PreProcessTimeTicks { get; set; }
        public int OutputSize { get; set; }
        public string? ResultsMatch { get; set; }
        public string? PerfDifference { get; set; }
        
        // Helper properties for display
        public double NormalTimeMs => (double)NormalTimeTicks / Stopwatch.Frequency * 1000;
        public double PreProcessTimeMs => (double)PreProcessTimeTicks / Stopwatch.Frequency * 1000;
    }

    public static List<PerfSummaryRow> RunPerformanceComparison(string assemblerWebDirPath, bool enableJsonProcessing, string? appSiteFilter = null, bool skipDetails = false)
    {
        int iterations = 1000;
        string appSitesPath = Path.Combine(assemblerWebDirPath, "AppSites");
        var summaryRows = new List<PerfSummaryRow>();
        if (!Directory.Exists(appSitesPath))
            return summaryRows;
        var allAppSiteDirs = Directory.GetDirectories(appSitesPath, "*", SearchOption.TopDirectoryOnly);
        var appSites = allAppSiteDirs.Select(dir => Path.GetFileName(dir)).ToArray();
        foreach (var testAppSite in appSites)
        {
            if (!string.IsNullOrEmpty(appSiteFilter) && !testAppSite.Equals(appSiteFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            string appSiteDir = Path.Combine(appSitesPath, testAppSite);
            var htmlFiles = Directory.GetFiles(appSiteDir, "*.html", SearchOption.TopDirectoryOnly);
            foreach (var htmlFilePath in htmlFiles)
            {
                var appFileName = Path.GetFileNameWithoutExtension(htmlFilePath);
                try
                {
                    LoaderNormal.ClearCache();
                    LoaderPreProcess.ClearCache();
                    var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, testAppSite);
                    var siteTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, testAppSite);
                    if (templates == null || templates.Count == 0)
                    {
                        if (!skipDetails) Console.WriteLine($"❌ No templates found for {testAppSite}");
                        continue;
                    }
                    var mainTemplateKey = (testAppSite + "_" + appFileName).ToLowerInvariant();
                    if (!templates.TryGetValue(mainTemplateKey, out var mainTemplate))
                    {
                        if (!skipDetails) Console.WriteLine($"❌ No main template found for {mainTemplateKey}");
                        continue;
                    }
                    if (!skipDetails)
                    {
                        Console.WriteLine($"Template Key: {mainTemplateKey}");
                        Console.WriteLine($"Templates available: {templates.Count}");
                    }
                    var appViewScenarios = new List<(string AppView, string AppViewPrefix)> { ("", "") };
                    var viewsPath = Path.Combine(appSiteDir, "Views");
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
                                appViewScenarios.Add((appView, appViewPrefix));
                        }
                    }
                    foreach (var scenario in appViewScenarios)
                    {
                        if (!skipDetails)
                        {
                            Console.WriteLine(new string('-', 60));
                            Console.WriteLine($">>> C# SCENARIO : '{testAppSite}', '{appFileName}', '{scenario.AppView}', '{scenario.AppViewPrefix}'");
                            Console.WriteLine($"Iterations per test: {iterations:N0}");
                        }
                        var normalEngine = new EngineNormal();
                        normalEngine.AppViewPrefix = scenario.AppViewPrefix;
                        
                        // JIT Warmup - run a few iterations first to warm up the JIT
                        for (int warmup = 0; warmup < 100; warmup++)
                        {
                            normalEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, templates, enableJsonProcessing);
                        }
                        
                        var sw = Stopwatch.StartNew();
                        string resultNormal = "";
                        for (int i = 0; i < iterations; i++)
                        {
                            resultNormal = normalEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, templates, enableJsonProcessing);
                        }
                        sw.Stop();
                        var normalTime = sw.ElapsedMilliseconds;
                        var normalTicks = sw.ElapsedTicks;
                        if (!skipDetails)
                        {
                            Console.WriteLine($"[Normal Engine] {iterations:N0} iterations: {normalTime}ms ({normalTicks:N0} ticks)");
                            Console.WriteLine($"[Normal Engine] Avg: {(double)normalTime / iterations:F3}ms per op, Output size: {resultNormal.Length} chars");
                        }
                        LoaderNormal.ClearCache();
                        LoaderPreProcess.ClearCache();
                        var preProcessEngine = new EnginePreProcess();
                        preProcessEngine.AppViewPrefix = scenario.AppViewPrefix;
                        
                        // JIT Warmup for PreProcess engine
                        for (int warmup = 0; warmup < 100; warmup++)
                        {
                            preProcessEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, siteTemplates.Templates, enableJsonProcessing);
                        }
                        
                        sw.Restart();
                        string resultPreProcess = "";
                        for (int i = 0; i < iterations; i++)
                        {
                            resultPreProcess = preProcessEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, siteTemplates.Templates, enableJsonProcessing);
                        }
                        sw.Stop();
                        var preProcessTime = sw.ElapsedMilliseconds;
                        var preProcessTicks = sw.ElapsedTicks;
                        if (!skipDetails)
                        {
                            Console.WriteLine($"[PreProcess Engine] {iterations:N0} iterations: {preProcessTime}ms ({preProcessTicks:N0} ticks)");
                            Console.WriteLine($"[PreProcess Engine] Avg: {(double)preProcessTime / iterations:F3}ms per op, Output size: {resultPreProcess.Length} chars");
                            Console.WriteLine($">>> C# PERFORMANCE COMPARISON:");
                            Console.WriteLine(new string('-', 50));
                            var difference = preProcessTime - normalTime;
                            var differencePercent = normalTime > 0 ? ((double)difference / normalTime) * 100 : 0;
                            var tickDifference = preProcessTicks - normalTicks;
                            var tickDifferencePercent = normalTicks > 0 ? ((double)tickDifference / normalTicks) * 100 : 0;
                            Console.WriteLine($"Time difference: {difference}ms ({differencePercent:F1}%)");
                            Console.WriteLine($"Tick difference: {tickDifference:N0} ticks ({tickDifferencePercent:F1}%)");
                            Console.WriteLine($"Results match: {(resultNormal == resultPreProcess ? "✅ YES" : "❌ NO")}");
                        }
                        summaryRows.Add(new PerfSummaryRow
                        {
                            AppSite = testAppSite,
                            AppFile = appFileName,
                            AppView = scenario.AppView,
                            Iterations = iterations,
                            NormalTimeTicks = normalTicks,
                            PreProcessTimeTicks = preProcessTicks,
                            OutputSize = resultNormal.Length,
                            ResultsMatch = (resultNormal == resultPreProcess ? "YES" : "NO"),
                            PerfDifference = normalTicks > 0 ? $"{((double)(preProcessTicks - normalTicks) / normalTicks * 100):F1}%" : "0%"
                        });
                    }
                }
                catch (Exception ex)
                {
                    if (!skipDetails) Console.WriteLine($"❌ Error in performance testing for {testAppSite}: {ex.Message}");
                }
            }
        }
        return summaryRows;
    }

    public static void PrintPerfSummaryTable(string assemblerWebDirPath, List<PerfSummaryRow> summaryRows)
    {
        if (summaryRows == null || summaryRows.Count == 0)
            return;
        Console.WriteLine("\n==================== C# PERFORMANCE SUMMARY ====================\n");

        var headers = new[] { "AppSite", "AppView", "Normal(ms)", "PreProc(ms)", "Match", "PerfDiff" };
        int colCount = headers.Length;
        int[] widths = new int[colCount];
        for (int i = 0; i < colCount; i++)
        {
            widths[i] = headers[i].Length;
        }
        foreach (var row in summaryRows)
        {
            widths[0] = Math.Max(widths[0], row.AppSite?.Length ?? 0);
            widths[1] = Math.Max(widths[1], row.AppView?.Length ?? 0);
            widths[2] = Math.Max(widths[2], row.NormalTimeMs.ToString("F2").Length);
            widths[3] = Math.Max(widths[3], row.PreProcessTimeMs.ToString("F2").Length);
            widths[4] = Math.Max(widths[4], row.ResultsMatch?.Length ?? 0);
            widths[5] = Math.Max(widths[5], row.PerfDifference?.Length ?? 0);
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
            Console.Write((row.AppView ?? "").PadRight(widths[1]));
            Console.Write(" | ");
            Console.Write(row.NormalTimeMs.ToString("F2").PadRight(widths[2]));
            Console.Write(" | ");
            Console.Write(row.PreProcessTimeMs.ToString("F2").PadRight(widths[3]));
            Console.Write(" | ");
            Console.Write((row.ResultsMatch ?? "").PadRight(widths[4]));
            Console.Write(" | ");
            Console.Write((row.PerfDifference ?? "").PadRight(widths[5]));
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
            var html = new StringBuilder();
            html.AppendLine("<html><head><title>C# Performance Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}</style></head><body>");
            html.AppendLine("<h2>C# Performance Summary Table</h2>");
            html.AppendLine("<table>");
            html.Append("<tr>");
            foreach (var h in headers) html.Append($"<th>{h}</th>");
            html.AppendLine("</tr>");
            foreach (var row in summaryRows)
            {
                html.Append("<tr>");
                html.Append($"<td>{row.AppSite}</td>");
                html.Append($"<td>{row.AppView}</td>");
                html.Append($"<td>{row.NormalTimeMs:F2}</td>");
                html.Append($"<td>{row.PreProcessTimeMs:F2}</td>");
                html.Append($"<td>{row.ResultsMatch}</td>");
                html.Append($"<td>{row.PerfDifference}</td>");
                html.AppendLine("</tr>");
            }
            html.AppendLine("</table></body></html>");

            var outFile = Path.Combine(assemblerWebDirPath, "csharp_perfsummary.html");
            File.WriteAllText(outFile, html.ToString());
            Console.WriteLine($"Performance summary HTML saved to: {outFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving performance summary HTML: {ex.Message}");
        }

        // Save JSON file
        try
        {
            var jsonFile = Path.Combine(assemblerWebDirPath, "csharp_perfsummary.json");
            var json = Arshu.App.Json.JsonConverter.SerializeObject(summaryRows, true);
            File.WriteAllText(jsonFile, json);
            Console.WriteLine($"Performance summary JSON saved to: {jsonFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving performance summary JSON: {ex.Message}");
        }
    }
}

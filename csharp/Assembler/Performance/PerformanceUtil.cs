using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Arshu.App.Json;
using Assembler.Engine;
using Assembler.Loader;
using Assembler.Config;

namespace Assembler.Performance;

public static class PerformanceUtil
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
        public long ScenarioTotalTimeMs { get; set; }
        public long ElapsedTimeMs { get; set; }

        // Helper properties for display
        public double NormalTimeMs => (double)NormalTimeTicks / Stopwatch.Frequency * 1000;
        public double PreProcessTimeMs => (double)PreProcessTimeTicks / Stopwatch.Frequency * 1000;
    }

    public static List<PerfSummaryRow> RunPerformanceComparison(string assemblerWebDirPath, List<Scenario> scenarios, bool skipDetails = false, bool enableJsonProcessing = true)
    {
        var startTime = Stopwatch.StartNew();

        if (string.IsNullOrEmpty(assemblerWebDirPath))
        {
            Console.WriteLine("❌ No assemblerWebDirPath passed");
            return new List<PerfSummaryRow>();
        }

        if (scenarios == null || scenarios.Count == 0)
        {
            Console.WriteLine("❌ No scenarios passed");
            return new List<PerfSummaryRow>();
        }

        int iterations = 1000;
        var summaryRows = new List<PerfSummaryRow>();

        foreach (var scenario in scenarios)
        {
            var scenarioStartTime = Stopwatch.StartNew();
            var testAppSite = scenario.AppSite;
            var appFileName = scenario.AppFile;
            var appView = scenario.AppView;
            var appViewPrefix = string.IsNullOrEmpty(appView) ? "" : appView.Substring(0, Math.Min(appView.Length, 6));

            try
            {
                LoaderNormal.ClearCache();
                LoaderPreProcess.ClearCache();
                var templates = LoaderNormal.LoadGetTemplateFiles(assemblerWebDirPath, testAppSite);
                var siteTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(assemblerWebDirPath, testAppSite);
                if (templates == null || templates.Count == 0)
                    continue;
                var mainTemplateKey = (testAppSite + "_" + appFileName).ToLowerInvariant();
                if (!templates.TryGetValue(mainTemplateKey, out var mainTemplate))
                    continue;

                if (!skipDetails)
                {
                    Console.WriteLine(new string('-', 60));
                    Console.WriteLine($"[C#] Testing: AppSite={testAppSite}, AppFile={appFileName}, AppView={appView}");
                    Console.WriteLine($"[C#] Iterations: {iterations:N0}");
                }
                var normalEngine = new EngineNormal();
                normalEngine.AppViewPrefix = appViewPrefix;

                // JIT Warmup - run a few iterations first to warm up the JIT
                for (int warmup = 0; warmup < 100; warmup++)
                {
                    normalEngine.MergeTemplates(testAppSite, appFileName, appView, templates, enableJsonProcessing);
                }

                var sw = Stopwatch.StartNew();
                string resultNormal = "";
                for (int i = 0; i < iterations; i++)
                {
                    resultNormal = normalEngine.MergeTemplates(testAppSite, appFileName, appView, templates, enableJsonProcessing);
                }
                sw.Stop();
                var normalTime = sw.ElapsedMilliseconds;
                var normalTicks = sw.ElapsedTicks;
                if (!skipDetails)
                {
                    Console.WriteLine($"[C#] Normal Engine:     {normalTime}ms | Avg: {(double)normalTime / iterations:F3}ms/op | Size: {resultNormal.Length} chars");
                }
                LoaderNormal.ClearCache();
                LoaderPreProcess.ClearCache();
                var preProcessEngine = new EnginePreProcess();
                preProcessEngine.AppViewPrefix = appViewPrefix;

                // JIT Warmup for PreProcess engine
                for (int warmup = 0; warmup < 100; warmup++)
                {
                    preProcessEngine.MergeTemplates(testAppSite, appFileName, appView, siteTemplates.Templates, enableJsonProcessing);
                }

                sw.Restart();
                string resultPreProcess = "";
                for (int i = 0; i < iterations; i++)
                {
                    resultPreProcess = preProcessEngine.MergeTemplates(testAppSite, appFileName, appView, siteTemplates.Templates, enableJsonProcessing);
                }
                sw.Stop();
                var preProcessTime = sw.ElapsedMilliseconds;
                var preProcessTicks = sw.ElapsedTicks;
                if (!skipDetails)
                {
                    Console.WriteLine($"[C#] PreProcess Engine: {preProcessTime}ms | Avg: {(double)preProcessTime / iterations:F3}ms/op | Size: {resultPreProcess.Length} chars");
                    var difference = preProcessTime - normalTime;
                    var differencePercent = normalTime > 0 ? ((double)difference / normalTime) * 100 : 0;
                    Console.WriteLine($"[C#] Performance: {(difference >= 0 ? "+" : "")}{difference}ms ({(differencePercent >= 0 ? "+" : "")}{differencePercent:F1}%) | Match: {(resultNormal == resultPreProcess ? "YES" : "NO")}");
                }

                scenarioStartTime.Stop();
                var scenarioTotalTime = scenarioStartTime.ElapsedMilliseconds;
                var elapsedTime = startTime.ElapsedMilliseconds;

                if (!skipDetails)
                {
                    Console.WriteLine($"[C#] Scenario Total Time: {scenarioTotalTime}ms | Elapsed: {elapsedTime}ms");
                }

                summaryRows.Add(new PerfSummaryRow
                {
                    AppSite = testAppSite,
                    AppFile = appFileName,
                    AppView = appView,
                    Iterations = iterations,
                    NormalTimeTicks = normalTicks,
                    PreProcessTimeTicks = preProcessTicks,
                    OutputSize = resultNormal.Length,
                    ResultsMatch = (resultNormal == resultPreProcess ? "YES" : "NO"),
                    PerfDifference = normalTicks > 0 ? $"{((double)(preProcessTicks - normalTicks) / normalTicks * 100):F1}%" : "0%",
                    ScenarioTotalTimeMs = scenarioTotalTime,
                    ElapsedTimeMs = elapsedTime
                });
            }
            catch (Exception)
            {
                // Silent error handling
            }
        }
        startTime.Stop();

        Console.WriteLine($"\n========== Performance Testing Completed in {startTime.ElapsedMilliseconds}ms ==========\n");

        return summaryRows;
    }

    public static void PrintPerfSummaryTable(string assemblerWebDirPath, List<PerfSummaryRow> summaryRows)
    {
        if (summaryRows == null || summaryRows.Count == 0)
            return;
        Console.WriteLine("\n==================== C# PERFORMANCE SUMMARY ====================\n");

        var headers = new[] { "AppSite", "AppView", "Normal(ms)", "PreProc(ms)", "Match", "PerfDiff", "ScnTime(ms)", "Elapsed(ms)" };
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
            widths[6] = Math.Max(widths[6], row.ScenarioTotalTimeMs.ToString().Length);
            widths[7] = Math.Max(widths[7], row.ElapsedTimeMs.ToString().Length);
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
            Console.Write(" | ");
            Console.Write(row.ScenarioTotalTimeMs.ToString().PadRight(widths[6]));
            Console.Write(" | ");
            Console.Write(row.ElapsedTimeMs.ToString().PadRight(widths[7]));
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
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine("    <title>C# Performance Summary Table</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
            html.AppendLine("        h2 { color: #333; }");
            html.AppendLine("        .table-container { overflow-x: auto; }");
            html.AppendLine("        table { border-collapse: collapse; width: 100%; min-width: 600px; }");
            html.AppendLine("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }");
            html.AppendLine("        th { background-color: #4CAF50; color: white; }");
            html.AppendLine("        tr:nth-child(even) { background-color: #f2f2f2; }");
            html.AppendLine("        @media (max-width: 768px) {");
            html.AppendLine("            body { margin: 10px; }");
            html.AppendLine("            th, td { padding: 8px; font-size: 14px; }");
            html.AppendLine("            h2 { font-size: 20px; }");
            html.AppendLine("        }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <h2>C# Performance Summary Table</h2>");
            html.AppendLine("    <div class=\"table-container\">");
            html.AppendLine("    <table>");
            html.Append("        <tr>");
            foreach (var h in headers) html.Append($"<th>{h}</th>");
            html.AppendLine("</tr>");
            foreach (var row in summaryRows)
            {
                html.Append("        <tr>");
                html.Append($"<td>{row.AppSite}</td>");
                html.Append($"<td>{row.AppView}</td>");
                html.Append($"<td>{row.NormalTimeMs:F2}</td>");
                html.Append($"<td>{row.PreProcessTimeMs:F2}</td>");
                html.Append($"<td>{row.ResultsMatch}</td>");
                html.Append($"<td>{row.PerfDifference}</td>");
                html.Append($"<td>{row.ScenarioTotalTimeMs}</td>");
                html.Append($"<td>{row.ElapsedTimeMs}</td>");
                html.AppendLine("</tr>");
            }
            html.AppendLine("    </table>");
            html.AppendLine("    </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            var reportsDir = Path.Combine(assemblerWebDirPath, "Reports");
            Directory.CreateDirectory(reportsDir);
            var outFile = Path.Combine(reportsDir, "csharp_perfsummary.html");
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
            var reportsDir = Path.Combine(assemblerWebDirPath, "Reports");
            Directory.CreateDirectory(reportsDir);
            var jsonFile = Path.Combine(reportsDir, "csharp_perfsummary.json");
            var json = JsonConverter.SerializeObject(summaryRows, true);
            File.WriteAllText(jsonFile, json);
            Console.WriteLine($"Performance summary JSON saved to: {jsonFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving performance summary JSON: {ex.Message}");
        }
    }
}

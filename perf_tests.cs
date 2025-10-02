using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

class RunAllPerfTest
{
    static void Main(string[] args)
    {
        // Define paths for perfsummary files for each language and engine
        var workspaceDir = AppDomain.CurrentDomain.BaseDirectory;
        var perfFiles = new List<(string Language, string Path)>
        {
            ("CSharp", "csharp/AssemblerWeb/wwwroot/csharp_perfsummary.json"),
            ("Rust", "rust/AssemblerWeb/wwwroot/rust_perfsummary.json"),
            ("Go", "go/AssemblerWeb/wwwroot/go_perfsummary.json"),
            ("Node", "node/AssemblerWeb/wwwroot/nodejs_perfsummary.json"),
            ("PHP", "php/AssemblerWeb/wwwroot/php_perfsummary.json"),
        };

        var appPerf = new Dictionary<string, Dictionary<string, (double? NormalTimeMs, double? PreProcessTimeMs, int? OutputSize, string? AppView)>>();
        foreach (var (lang, path) in perfFiles)
        {
            if (File.Exists(path))
            {
                var content = File.ReadAllText(path);
                try
                {
                    var arr = System.Text.Json.JsonDocument.Parse(content).RootElement;
                    if (arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            string appSite = item.TryGetProperty("AppSite", out var v1) ? v1.GetString() ?? "" : (item.TryGetProperty("app_site", out var v2) ? v2.GetString() ?? "" : item.TryGetProperty("appSite", out var v3) ? v3.GetString() ?? "" : "");
                            string appView = item.TryGetProperty("AppView", out var av1) ? av1.GetString() ?? "" : (item.TryGetProperty("app_view", out var av2) ? av2.GetString() ?? "" : item.TryGetProperty("appView", out var av3) ? av3.GetString() ?? "" : "");
                            
                            // Try millisecond fields first, then nanosecond fields (convert to ms)
                            double? normalTime = null;
                            if (item.TryGetProperty("NormalTimeMs", out var nt1)) normalTime = nt1.GetDouble();
                            else if (item.TryGetProperty("normal_time_ms", out var nt2)) normalTime = nt2.GetDouble();
                            else if (item.TryGetProperty("normalTimeMs", out var nt3)) normalTime = nt3.GetDouble();
                            else if (item.TryGetProperty("NormalTimeNanos", out var ntn1)) normalTime = ntn1.GetDouble() / 1_000_000.0;
                            else if (item.TryGetProperty("normal_time_nanos", out var ntn2)) normalTime = ntn2.GetDouble() / 1_000_000.0;
                            
                            double? preprocessTime = null;
                            if (item.TryGetProperty("PreProcessTimeMs", out var pt1)) preprocessTime = pt1.GetDouble();
                            else if (item.TryGetProperty("preprocess_time_ms", out var pt2)) preprocessTime = pt2.GetDouble();
                            else if (item.TryGetProperty("preProcessTimeMs", out var pt3)) preprocessTime = pt3.GetDouble();
                            else if (item.TryGetProperty("PreProcessTimeNanos", out var ptn1)) preprocessTime = ptn1.GetDouble() / 1_000_000.0;
                            else if (item.TryGetProperty("preprocess_time_nanos", out var ptn2)) preprocessTime = ptn2.GetDouble() / 1_000_000.0;
                            
                            int? outputSize = item.TryGetProperty("OutputSize", out var os1) ? os1.GetInt32() : (item.TryGetProperty("output_size", out var os2) ? os2.GetInt32() : (int?)null);
                            string key = string.IsNullOrEmpty(appView) ? appSite : appSite + " / " + appView;
                            if (!appPerf.ContainsKey(key)) appPerf[key] = new Dictionary<string, (double?, double?, int?, string?)>();
                            appPerf[key][lang] = (normalTime, preprocessTime, outputSize, appView);
                        }
                    }
                }
                catch
                {
                    // skip on error
                }
            }
        }

        // Build markdown report
        var sb = new StringBuilder();
        sb.AppendLine("# Consolidated Performance Summary\n");

        // Normal Engine Table
        sb.AppendLine("## Normal Engine\n");
        sb.Append("| AppSite/AppView | CSharp | Rust | Go | Node | PHP | OutputSize |\n");
        sb.Append("|----------------|--------|------|----|------|-----|------------|\n");
        foreach (var app in appPerf.Keys)
        {
            var csharp = appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].NormalTimeMs.HasValue ? appPerf[app]["CSharp"].NormalTimeMs!.Value.ToString("F2") : "-";
            var rust = appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].NormalTimeMs.HasValue ? appPerf[app]["Rust"].NormalTimeMs!.Value.ToString("F2") : "-";
            var go = appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].NormalTimeMs.HasValue ? appPerf[app]["Go"].NormalTimeMs!.Value.ToString("F2") : "-";
            var node = appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].NormalTimeMs.HasValue ? appPerf[app]["Node"].NormalTimeMs!.Value.ToString("F2") : "-";
            var php = appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].NormalTimeMs.HasValue ? appPerf[app]["PHP"].NormalTimeMs!.Value.ToString("F2") : "-";
            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            sb.AppendLine($"| {app} | {csharp} | {rust} | {go} | {node} | {php} | {outputSize} |");
        }
        sb.AppendLine();

        // PreProcess Engine Table
        sb.AppendLine("## PreProcess Engine\n");
        sb.Append("| AppSite/AppView | CSharp | Rust | Go | Node | PHP | OutputSize |\n");
        sb.Append("|----------------|--------|------|----|------|-----|------------|\n");
        foreach (var app in appPerf.Keys)
        {
            var csharp = appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].PreProcessTimeMs.HasValue ? appPerf[app]["CSharp"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var rust = appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].PreProcessTimeMs.HasValue ? appPerf[app]["Rust"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var go = appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].PreProcessTimeMs.HasValue ? appPerf[app]["Go"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var node = appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].PreProcessTimeMs.HasValue ? appPerf[app]["Node"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var php = appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].PreProcessTimeMs.HasValue ? appPerf[app]["PHP"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            sb.AppendLine($"| {app} | {csharp} | {rust} | {go} | {node} | {php} | {outputSize} |");
        }
        sb.AppendLine();
        File.WriteAllText("perf_tests.md", sb.ToString());
        Console.WriteLine("Consolidated summary written to perf_tests.md");
    }
}

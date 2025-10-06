using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

class RunAllPerfTest
{
    static string FindWorkspaceRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();

        // Search upward for workspace root marker (perf_tests.cs or .git)
        while (currentDir != null)
        {
            if (File.Exists(Path.Combine(currentDir, "perf_tests.cs")) ||
                Directory.Exists(Path.Combine(currentDir, ".git")))
            {
                return currentDir;
            }

            var parent = Directory.GetParent(currentDir);
            if (parent == null) break;
            currentDir = parent.FullName;
        }

        // Fallback to current directory
        return Directory.GetCurrentDirectory();
    }

    static void Main(string[] args)
    {
        // Find workspace root directory
        var workspaceRoot = FindWorkspaceRoot();
        Console.WriteLine($"Workspace root: {workspaceRoot}\n");

        // Define paths for perfsummary files for each language and engine
        var perfFiles = new List<(string Language, string Path)>
        {
            ("CSharp", "csharp/AssemblerWeb/wwwroot/csharp_perfsummary.json"),
            ("Rust", "rust/AssemblerWeb/wwwroot/rust_perfsummary.json"),
            ("Go", "go/AssemblerWeb/wwwroot/go_perfsummary.json"),
            ("Node", "node/AssemblerWeb/wwwroot/nodejs_perfsummary.json"),
            ("PHP", "php/AssemblerWeb/wwwroot/php_perfsummary.json"),
        };

        var appPerf = new Dictionary<string, Dictionary<string, (double? NormalTimeMs, double? PreProcessTimeMs, int? OutputSize, string? AppView)>>();
        var filesProcessed = new List<string>();
        var filesMissing = new List<string>();

        foreach (var (lang, path) in perfFiles)
        {
            var fullPath = Path.Combine(workspaceRoot, path);
            if (File.Exists(fullPath))
            {
                var content = File.ReadAllText(fullPath);
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
                            string key = string.IsNullOrEmpty(appView) ? appSite : appSite + " → " + appView;
                            if (!appPerf.ContainsKey(key)) appPerf[key] = new Dictionary<string, (double?, double?, int?, string?)>();
                            appPerf[key][lang] = (normalTime, preprocessTime, outputSize, appView);
                        }
                    }
                    filesProcessed.Add($"{lang}: {path}");
                }
                catch (Exception ex)
                {
                    filesMissing.Add($"{lang}: {path} (ERROR: {ex.Message})");
                }
            }
            else
            {
                filesMissing.Add($"{lang}: {path} (File not found)");
            }
        }

        // Print processing summary
        Console.WriteLine("Files processed:");
        foreach (var file in filesProcessed)
        {
            Console.WriteLine($"  ✓ {file}");
        }

        if (filesMissing.Count > 0)
        {
            Console.WriteLine("\nFiles missing or failed:");
            foreach (var file in filesMissing)
            {
                Console.WriteLine($"  ✗ {file}");
            }
        }
        Console.WriteLine();

        // Build Markdown report for workspace root
        var mdSb = new StringBuilder();
        mdSb.AppendLine("# Consolidated Performance Summary");
        mdSb.AppendLine();
        mdSb.AppendLine($"*Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        mdSb.AppendLine();
        mdSb.AppendLine($"*All times in milliseconds (ms)*");
        mdSb.AppendLine();

        // Normal Engine Table (Markdown)
        mdSb.AppendLine("## Normal Engine");
        mdSb.AppendLine();
        mdSb.AppendLine("| AppSite/AppView | CSharp | Rust | Go | Node | PHP | OutputSize |");
        mdSb.AppendLine("|----------------|--------|------|----|------|-----|------------|");
        foreach (var app in appPerf.Keys.OrderBy(k => k))
        {
            var csharp = appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].NormalTimeMs.HasValue ? appPerf[app]["CSharp"].NormalTimeMs!.Value.ToString("F2") : "-";
            var rust = appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].NormalTimeMs.HasValue ? appPerf[app]["Rust"].NormalTimeMs!.Value.ToString("F2") : "-";
            var go = appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].NormalTimeMs.HasValue ? appPerf[app]["Go"].NormalTimeMs!.Value.ToString("F2") : "-";
            var node = appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].NormalTimeMs.HasValue ? appPerf[app]["Node"].NormalTimeMs!.Value.ToString("F2") : "-";
            var php = appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].NormalTimeMs.HasValue ? appPerf[app]["PHP"].NormalTimeMs!.Value.ToString("F2") : "-";
            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            mdSb.AppendLine($"| {app} | {csharp} | {rust} | {go} | {node} | {php} | {outputSize} |");
        }
        mdSb.AppendLine();

        // PreProcess Engine Table (Markdown)
        mdSb.AppendLine("## PreProcess Engine");
        mdSb.AppendLine();
        mdSb.AppendLine("| AppSite/AppView | CSharp | Rust | Go | Node | PHP | OutputSize |");
        mdSb.AppendLine("|----------------|--------|------|----|------|-----|------------|");
        foreach (var app in appPerf.Keys.OrderBy(k => k))
        {
            var csharp = appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].PreProcessTimeMs.HasValue ? appPerf[app]["CSharp"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var rust = appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].PreProcessTimeMs.HasValue ? appPerf[app]["Rust"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var go = appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].PreProcessTimeMs.HasValue ? appPerf[app]["Go"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var node = appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].PreProcessTimeMs.HasValue ? appPerf[app]["Node"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var php = appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].PreProcessTimeMs.HasValue ? appPerf[app]["PHP"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            mdSb.AppendLine($"| {app} | {csharp} | {rust} | {go} | {node} | {php} | {outputSize} |");
        }
        mdSb.AppendLine();

        // Build HTML report for web
        var htmlSb = new StringBuilder();
        htmlSb.AppendLine("<html><head><title>Consolidated Performance Summary</title>");
        htmlSb.AppendLine("<style>");
        htmlSb.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; background: #f5f7fa; }");
        htmlSb.AppendLine("h1 { color: #667eea; text-align: center; }");
        htmlSb.AppendLine("h2 { color: #764ba2; margin-top: 40px; }");
        htmlSb.AppendLine(".meta { text-align: center; color: #666; font-style: italic; margin-bottom: 30px; }");
        htmlSb.AppendLine("table { border-collapse: collapse; width: 100%; max-width: 1200px; margin: 20px auto; background: white; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }");
        htmlSb.AppendLine("th, td { border: 1px solid #ddd; padding: 12px 8px; text-align: left; }");
        htmlSb.AppendLine("th { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; font-weight: bold; position: sticky; top: 0; }");
        htmlSb.AppendLine("tr:nth-child(even) { background-color: #f9f9f9; }");
        htmlSb.AppendLine("tr:hover { background-color: #f0f0f0; }");
        htmlSb.AppendLine("td:nth-child(2), td:nth-child(3), td:nth-child(4), td:nth-child(5), td:nth-child(6), td:nth-child(7) { text-align: right; }");
        htmlSb.AppendLine("</style></head><body>");
        htmlSb.AppendLine("<h1>Consolidated Performance Summary</h1>");
        htmlSb.AppendLine($"<div class=\"meta\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>");
        htmlSb.AppendLine($"<div class=\"meta\">All times in milliseconds (ms)</div>");

        // Normal Engine Table (HTML)
        htmlSb.AppendLine("<h2>Normal Engine</h2>");
        htmlSb.AppendLine("<table>");
        htmlSb.AppendLine("<tr><th>AppSite/AppView</th><th>CSharp</th><th>Rust</th><th>Go</th><th>Node</th><th>PHP</th><th>OutputSize</th></tr>");
        foreach (var app in appPerf.Keys.OrderBy(k => k))
        {
            var csharp = appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].NormalTimeMs.HasValue ? appPerf[app]["CSharp"].NormalTimeMs!.Value.ToString("F2") : "-";
            var rust = appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].NormalTimeMs.HasValue ? appPerf[app]["Rust"].NormalTimeMs!.Value.ToString("F2") : "-";
            var go = appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].NormalTimeMs.HasValue ? appPerf[app]["Go"].NormalTimeMs!.Value.ToString("F2") : "-";
            var node = appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].NormalTimeMs.HasValue ? appPerf[app]["Node"].NormalTimeMs!.Value.ToString("F2") : "-";
            var php = appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].NormalTimeMs.HasValue ? appPerf[app]["PHP"].NormalTimeMs!.Value.ToString("F2") : "-";
            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            htmlSb.AppendLine($"<tr><td>{app}</td><td>{csharp}</td><td>{rust}</td><td>{go}</td><td>{node}</td><td>{php}</td><td>{outputSize}</td></tr>");
        }
        htmlSb.AppendLine("</table>");

        // PreProcess Engine Table (HTML)
        htmlSb.AppendLine("<h2>PreProcess Engine</h2>");
        htmlSb.AppendLine("<table>");
        htmlSb.AppendLine("<tr><th>AppSite/AppView</th><th>CSharp</th><th>Rust</th><th>Go</th><th>Node</th><th>PHP</th><th>OutputSize</th></tr>");
        foreach (var app in appPerf.Keys.OrderBy(k => k))
        {
            var csharp = appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].PreProcessTimeMs.HasValue ? appPerf[app]["CSharp"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var rust = appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].PreProcessTimeMs.HasValue ? appPerf[app]["Rust"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var go = appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].PreProcessTimeMs.HasValue ? appPerf[app]["Go"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var node = appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].PreProcessTimeMs.HasValue ? appPerf[app]["Node"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var php = appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].PreProcessTimeMs.HasValue ? appPerf[app]["PHP"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            htmlSb.AppendLine($"<tr><td>{app}</td><td>{csharp}</td><td>{rust}</td><td>{go}</td><td>{node}</td><td>{php}</td><td>{outputSize}</td></tr>");
        }
        htmlSb.AppendLine("</table>");
        htmlSb.AppendLine("</body></html>");

        var markdownContent = mdSb.ToString();
        var htmlContent = htmlSb.ToString();

        // Write Markdown to workspace root
        var markdownPath = Path.Combine(workspaceRoot, "perf_tests.md");
        File.WriteAllText(markdownPath, markdownContent);
        Console.WriteLine($"Markdown summary written to: {markdownPath}");

        // Write HTML to wwwroot for all language directories
        var htmlPaths = new List<string>
        {
            Path.Combine(workspaceRoot, "csharp", "AssemblerWeb", "wwwroot", "all_perf_tests.html"),
            Path.Combine(workspaceRoot, "rust", "AssemblerWeb", "wwwroot", "all_perf_tests.html"),
            Path.Combine(workspaceRoot, "go", "AssemblerWeb", "wwwroot", "all_perf_tests.html"),
            Path.Combine(workspaceRoot, "node", "AssemblerWeb", "wwwroot", "all_perf_tests.html"),
            Path.Combine(workspaceRoot, "php", "AssemblerWeb", "wwwroot", "all_perf_tests.html")
        };

        foreach (var htmlPath in htmlPaths)
        {
            try
            {
                File.WriteAllText(htmlPath, htmlContent);
                Console.WriteLine($"HTML summary written to: {htmlPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not write to {htmlPath}: {ex.Message}");
            }
        }
    }
}

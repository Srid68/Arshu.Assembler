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
            ("CSharp", "csharp/AssemblerTest/template_analysis/Reports/csharp_perfsummary.json"),
            ("Rust", "rust/AssemblerTest/template_analysis/Reports/rust_perfsummary.json"),
            ("Go", "go/AssemblerTest/template_analysis/Reports/go_perfsummary.json"),
            ("Node", "node/AssemblerTest/template_analysis/Reports/nodejs_perfsummary.json"),
            ("PHP", "php/AssemblerTest/template_analysis/Reports/php_perfsummary.json"),
            ("Javascript", "csharp/AssemblerWebJs/template_analysis/Reports/javascript_perfsummary.json"),
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

                            // Normalize AppView casing for consistency across languages
                            if (appView == "Html3a") appView = "Html3A";
                            else if (appView == "Html3b") appView = "Html3B";

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
        var languages = new[] { "CSharp", "Rust", "Go", "Node", "PHP", "Javascript" };
        mdSb.AppendLine("## Normal Engine");
        mdSb.AppendLine();
        mdSb.AppendLine("| AppSite/AppView | CSharp | Rust | Go | Node | PHP | Javascript | OutputSize |");
        mdSb.AppendLine("|----------------|--------|------|----|------|-----|------------|------------|");
        foreach (var app in appPerf.Keys.OrderBy(k => k))
        {
            // Find minimum time for highlighting
            var validTimes = languages
                .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue)
                .Select(lang => appPerf[app][lang].NormalTimeMs!.Value)
                .ToList();
            var minTime = validTimes.Any() ? validTimes.Min() : (double?)null;

            var csharp = appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].NormalTimeMs.HasValue ? appPerf[app]["CSharp"].NormalTimeMs!.Value.ToString("F2") : "-";
            var rust = appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].NormalTimeMs.HasValue ? appPerf[app]["Rust"].NormalTimeMs!.Value.ToString("F2") : "-";
            var go = appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].NormalTimeMs.HasValue ? appPerf[app]["Go"].NormalTimeMs!.Value.ToString("F2") : "-";
            var node = appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].NormalTimeMs.HasValue ? appPerf[app]["Node"].NormalTimeMs!.Value.ToString("F2") : "-";
            var php = appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].NormalTimeMs.HasValue ? appPerf[app]["PHP"].NormalTimeMs!.Value.ToString("F2") : "-";
            var javascript = appPerf[app].ContainsKey("Javascript") && appPerf[app]["Javascript"].NormalTimeMs.HasValue ? appPerf[app]["Javascript"].NormalTimeMs!.Value.ToString("F2") : "-";

            // Bold the best (minimum) values
            if (minTime.HasValue)
            {
                if (appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].NormalTimeMs.HasValue && Math.Abs(appPerf[app]["CSharp"].NormalTimeMs!.Value - minTime.Value) < 0.001) csharp = $"**{csharp}**";
                if (appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].NormalTimeMs.HasValue && Math.Abs(appPerf[app]["Rust"].NormalTimeMs!.Value - minTime.Value) < 0.001) rust = $"**{rust}**";
                if (appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].NormalTimeMs.HasValue && Math.Abs(appPerf[app]["Go"].NormalTimeMs!.Value - minTime.Value) < 0.001) go = $"**{go}**";
                if (appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].NormalTimeMs.HasValue && Math.Abs(appPerf[app]["Node"].NormalTimeMs!.Value - minTime.Value) < 0.001) node = $"**{node}**";
                if (appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].NormalTimeMs.HasValue && Math.Abs(appPerf[app]["PHP"].NormalTimeMs!.Value - minTime.Value) < 0.001) php = $"**{php}**";
                if (appPerf[app].ContainsKey("Javascript") && appPerf[app]["Javascript"].NormalTimeMs.HasValue && Math.Abs(appPerf[app]["Javascript"].NormalTimeMs!.Value - minTime.Value) < 0.001) javascript = $"**{javascript}**";
            }

            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            mdSb.AppendLine($"| {app} | {csharp} | {rust} | {go} | {node} | {php} | {javascript} | {outputSize} |");
        }
        mdSb.AppendLine();

        // PreProcess Engine Table (Markdown)
        mdSb.AppendLine("## PreProcess Engine");
        mdSb.AppendLine();
        mdSb.AppendLine("| AppSite/AppView | CSharp | Rust | Go | Node | PHP | Javascript | OutputSize |");
        mdSb.AppendLine("|----------------|--------|------|----|------|-----|------------|------------|");
        foreach (var app in appPerf.Keys.OrderBy(k => k))
        {
            // Find minimum time for highlighting
            var validTimes = languages
                .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue)
                .Select(lang => appPerf[app][lang].PreProcessTimeMs!.Value)
                .ToList();
            var minTime = validTimes.Any() ? validTimes.Min() : (double?)null;

            var csharp = appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].PreProcessTimeMs.HasValue ? appPerf[app]["CSharp"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var rust = appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].PreProcessTimeMs.HasValue ? appPerf[app]["Rust"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var go = appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].PreProcessTimeMs.HasValue ? appPerf[app]["Go"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var node = appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].PreProcessTimeMs.HasValue ? appPerf[app]["Node"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var php = appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].PreProcessTimeMs.HasValue ? appPerf[app]["PHP"].PreProcessTimeMs!.Value.ToString("F2") : "-";
            var javascript = appPerf[app].ContainsKey("Javascript") && appPerf[app]["Javascript"].PreProcessTimeMs.HasValue ? appPerf[app]["Javascript"].PreProcessTimeMs!.Value.ToString("F2") : "-";

            // Bold the best (minimum) values
            if (minTime.HasValue)
            {
                if (appPerf[app].ContainsKey("CSharp") && appPerf[app]["CSharp"].PreProcessTimeMs.HasValue && Math.Abs(appPerf[app]["CSharp"].PreProcessTimeMs!.Value - minTime.Value) < 0.001) csharp = $"**{csharp}**";
                if (appPerf[app].ContainsKey("Rust") && appPerf[app]["Rust"].PreProcessTimeMs.HasValue && Math.Abs(appPerf[app]["Rust"].PreProcessTimeMs!.Value - minTime.Value) < 0.001) rust = $"**{rust}**";
                if (appPerf[app].ContainsKey("Go") && appPerf[app]["Go"].PreProcessTimeMs.HasValue && Math.Abs(appPerf[app]["Go"].PreProcessTimeMs!.Value - minTime.Value) < 0.001) go = $"**{go}**";
                if (appPerf[app].ContainsKey("Node") && appPerf[app]["Node"].PreProcessTimeMs.HasValue && Math.Abs(appPerf[app]["Node"].PreProcessTimeMs!.Value - minTime.Value) < 0.001) node = $"**{node}**";
                if (appPerf[app].ContainsKey("PHP") && appPerf[app]["PHP"].PreProcessTimeMs.HasValue && Math.Abs(appPerf[app]["PHP"].PreProcessTimeMs!.Value - minTime.Value) < 0.001) php = $"**{php}**";
                if (appPerf[app].ContainsKey("Javascript") && appPerf[app]["Javascript"].PreProcessTimeMs.HasValue && Math.Abs(appPerf[app]["Javascript"].PreProcessTimeMs!.Value - minTime.Value) < 0.001) javascript = $"**{javascript}**";
            }

            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            mdSb.AppendLine($"| {app} | {csharp} | {rust} | {go} | {node} | {php} | {javascript} | {outputSize} |");
        }
        mdSb.AppendLine();

        // Build HTML report for web (matching AssemblerWeb endpoint styling)
        var htmlSb = new StringBuilder();
        htmlSb.AppendLine("<!DOCTYPE html>");
        htmlSb.AppendLine("<html>");
        htmlSb.AppendLine("<head>");
        htmlSb.AppendLine("    <meta charset=\"UTF-8\">");
        htmlSb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        htmlSb.AppendLine("    <title>Consolidated Performance Summary</title>");
        htmlSb.AppendLine("    <style>");
        htmlSb.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
        htmlSb.AppendLine("        h1 { color: #333; }");
        htmlSb.AppendLine("        h2 { color: #333; margin-top: 40px; }");
        htmlSb.AppendLine("        .meta { color: #666; font-style: italic; margin-bottom: 10px; }");
        htmlSb.AppendLine("        .table-container { overflow-x: auto; }");
        htmlSb.AppendLine("        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 700px; }");
        htmlSb.AppendLine("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }");
        htmlSb.AppendLine("        th { background-color: #4CAF50; color: white; }");
        htmlSb.AppendLine("        tr:nth-child(even) { background-color: #f2f2f2; }");
        htmlSb.AppendLine("        td:nth-child(2), td:nth-child(3), td:nth-child(4), td:nth-child(5), td:nth-child(6), td:nth-child(7), td:nth-child(8) { text-align: right; }");
        htmlSb.AppendLine("        .best-perf { background-color: #90EE90; font-weight: bold; }");
        htmlSb.AppendLine("        @media (max-width: 768px) {");
        htmlSb.AppendLine("            body { margin: 10px; }");
        htmlSb.AppendLine("            th, td { padding: 8px; font-size: 14px; }");
        htmlSb.AppendLine("            h1 { font-size: 24px; }");
        htmlSb.AppendLine("            h2 { font-size: 20px; }");
        htmlSb.AppendLine("            .meta { font-size: 12px; }");
        htmlSb.AppendLine("        }");
        htmlSb.AppendLine("    </style>");
        htmlSb.AppendLine("</head>");
        htmlSb.AppendLine("<body>");
        htmlSb.AppendLine("    <h1>Consolidated Performance Summary</h1>");
        htmlSb.AppendLine($"    <div class=\"meta\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | All times in milliseconds (ms)</div>");

        // Normal Engine Table (HTML)
        htmlSb.AppendLine("    <h2>Normal Engine</h2>");
        htmlSb.AppendLine("    <div class=\"table-container\">");
        htmlSb.AppendLine("    <table>");
        htmlSb.AppendLine("        <tr>");
        htmlSb.AppendLine("            <th>AppSite/AppView</th>");
        foreach (var lang in languages)
        {
            htmlSb.AppendLine($"            <th>{lang}</th>");
        }
        htmlSb.AppendLine("            <th>OutputSize</th>");
        htmlSb.AppendLine("        </tr>");
        foreach (var app in appPerf.Keys.OrderBy(k => k))
        {
            // Find minimum time for highlighting
            var validTimes = languages
                .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue)
                .Select(lang => appPerf[app][lang].NormalTimeMs!.Value)
                .ToList();
            var minTime = validTimes.Any() ? validTimes.Min() : (double?)null;

            htmlSb.AppendLine("        <tr>");
            htmlSb.AppendLine($"            <td>{app}</td>");
            foreach (var lang in languages)
            {
                var timeValue = appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue
                    ? appPerf[app][lang].NormalTimeMs!.Value.ToString("F2")
                    : "-";
                var isBest = minTime.HasValue && appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue
                    && Math.Abs(appPerf[app][lang].NormalTimeMs!.Value - minTime.Value) < 0.001;
                var cssClass = isBest ? " class=\"best-perf\"" : "";
                htmlSb.AppendLine($"            <td{cssClass}>{timeValue}</td>");
            }
            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            htmlSb.AppendLine($"            <td>{outputSize}</td>");
            htmlSb.AppendLine("        </tr>");
        }
        htmlSb.AppendLine("    </table>");
        htmlSb.AppendLine("    </div>");

        // PreProcess Engine Table (HTML)
        htmlSb.AppendLine("    <h2>PreProcess Engine</h2>");
        htmlSb.AppendLine("    <div class=\"table-container\">");
        htmlSb.AppendLine("    <table>");
        htmlSb.AppendLine("        <tr>");
        htmlSb.AppendLine("            <th>AppSite/AppView</th>");
        foreach (var lang in languages)
        {
            htmlSb.AppendLine($"            <th>{lang}</th>");
        }
        htmlSb.AppendLine("            <th>OutputSize</th>");
        htmlSb.AppendLine("        </tr>");
        foreach (var app in appPerf.Keys.OrderBy(k => k))
        {
            // Find minimum time for highlighting
            var validTimes = languages
                .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue)
                .Select(lang => appPerf[app][lang].PreProcessTimeMs!.Value)
                .ToList();
            var minTime = validTimes.Any() ? validTimes.Min() : (double?)null;

            htmlSb.AppendLine("        <tr>");
            htmlSb.AppendLine($"            <td>{app}</td>");
            foreach (var lang in languages)
            {
                var timeValue = appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue
                    ? appPerf[app][lang].PreProcessTimeMs!.Value.ToString("F2")
                    : "-";
                var isBest = minTime.HasValue && appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue
                    && Math.Abs(appPerf[app][lang].PreProcessTimeMs!.Value - minTime.Value) < 0.001;
                var cssClass = isBest ? " class=\"best-perf\"" : "";
                htmlSb.AppendLine($"            <td{cssClass}>{timeValue}</td>");
            }
            var outputSizeTuple = appPerf[app].Values.FirstOrDefault(v => v.OutputSize.HasValue);
            var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
            htmlSb.AppendLine($"            <td>{outputSize}</td>");
            htmlSb.AppendLine("        </tr>");
        }
        htmlSb.AppendLine("    </table>");
        htmlSb.AppendLine("    </div>");
        htmlSb.AppendLine("</body>");
        htmlSb.AppendLine("</html>");

        var markdownContent = mdSb.ToString();
        var htmlContent = htmlSb.ToString();

        // Write Markdown to workspace root
        var markdownPath = Path.Combine(workspaceRoot, "perf_tests.md");
        File.WriteAllText(markdownPath, markdownContent);
        Console.WriteLine($"Markdown summary written to: {markdownPath}");

        // Write HTML to template_analysis/Reports for all language directories
        var htmlPaths = new List<string>
        {
            Path.Combine(workspaceRoot, "csharp", "AssemblerTest", "template_analysis", "Reports", "all_perf_tests.html"),
            Path.Combine(workspaceRoot, "rust", "AssemblerTest", "template_analysis", "Reports", "all_perf_tests.html"),
            Path.Combine(workspaceRoot, "go", "AssemblerTest", "template_analysis", "Reports", "all_perf_tests.html"),
            Path.Combine(workspaceRoot, "node", "AssemblerTest", "template_analysis", "Reports", "all_perf_tests.html"),
            Path.Combine(workspaceRoot, "php", "AssemblerTest", "template_analysis", "Reports", "all_perf_tests.html")
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

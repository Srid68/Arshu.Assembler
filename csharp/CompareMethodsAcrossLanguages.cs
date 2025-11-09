using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Assembler.Tools;

/// <summary>
/// Automated tool to compare class structure and logging consistency across multiple programming languages
/// </summary>
public class CompareMethodsAcrossLanguages
{
    public static void Main(string[] args)
    {
        string configPath = args.Length > 0 ? args[0] : "CompareMethodsConfig.json";

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Configuration file not found: {configPath}");
            Console.WriteLine("Usage: CompareMethodsAcrossLanguages [configPath]");
            return;
        }

        try
        {
            var config = LoadConfiguration(configPath);
            var comparison = AnalyzeAllLanguages(config);
            var markdownReport = GenerateMarkdownReport(config, comparison);

            string outputFile = Path.Combine(config.OutputPath, $"{config.ClassName}_Comparison.md");
            File.WriteAllText(outputFile, markdownReport);

            Console.WriteLine($"✓ Comparison report generated: {outputFile}");
            Console.WriteLine($"✓ Analyzed {comparison.Count} languages");
            Console.WriteLine($"✓ Found {comparison.Values.FirstOrDefault()?.Methods.Count ?? 0} unique methods");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static ComparisonConfig LoadConfiguration(string configPath)
    {
        string json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<ComparisonConfig>(json, options)
            ?? throw new Exception("Failed to deserialize configuration");
    }

    private static Dictionary<string, LanguageAnalysis> AnalyzeAllLanguages(ComparisonConfig config)
    {
        var results = new Dictionary<string, LanguageAnalysis>();

        foreach (var lang in config.Languages)
        {
            Console.WriteLine($"Analyzing {lang.Name}...");

            if (!File.Exists(lang.FilePath))
            {
                Console.WriteLine($"  Warning: File not found: {lang.FilePath}");
                continue;
            }

            string content = File.ReadAllText(lang.FilePath);
            var analysis = AnalyzeLanguageFile(lang, content);
            results[lang.Name] = analysis;

            Console.WriteLine($"  Found {analysis.Methods.Count} methods");
        }

        return results;
    }

    private static LanguageAnalysis AnalyzeLanguageFile(LanguageConfig lang, string content)
    {
        var analysis = new LanguageAnalysis { LanguageName = lang.Name };

        // Split content into lines for easier processing
        var lines = content.Split('\n');

        // Find all methods
        var methodMatches = Regex.Matches(content, lang.MethodPattern, RegexOptions.Multiline);

        foreach (Match match in methodMatches)
        {
            string methodName = ExtractMethodName(match, lang.Name);
            if (string.IsNullOrEmpty(methodName) || methodName.StartsWith("_"))
                continue;

            // Find method body boundaries
            int methodStart = match.Index;
            int methodEnd = FindMethodEnd(content, methodStart, lang.Name);

            if (methodEnd > methodStart)
            {
                string methodBody = content.Substring(methodStart, methodEnd - methodStart);
                var logCounts = CountLogStatements(methodBody, lang.LogPatterns);

                analysis.Methods[methodName] = new MethodInfo
                {
                    Name = methodName,
                    StartIndex = methodStart,
                    EndIndex = methodEnd,
                    DebugLogs = logCounts["Debug"],
                    InfoLogs = logCounts["Info"],
                    WarnLogs = logCounts["Warn"],
                    ErrorLogs = logCounts["Error"],
                    TotalLogs = logCounts.Values.Sum()
                };
            }
        }

        return analysis;
    }

    private static string ExtractMethodName(Match match, string languageName)
    {
        return languageName switch
        {
            "C#" => match.Groups.Count >= 4 ? match.Groups[3].Value : "",
            "Rust" => match.Groups.Count >= 4 ? match.Groups[3].Value : "",
            "Go" => match.Groups.Count >= 3 ? match.Groups[2].Value : "",
            "Node" => match.Groups.Count >= 4 ? match.Groups[3].Value : "",
            "PHP" => match.Groups.Count >= 4 ? match.Groups[3].Value : "",
            _ => ""
        };
    }

    private static int FindMethodEnd(string content, int startIndex, string languageName)
    {
        // Simple brace matching for C#, Rust, Go, Node, PHP
        int braceDepth = 0;
        bool inMethod = false;

        for (int i = startIndex; i < content.Length; i++)
        {
            char c = content[i];

            if (c == '{')
            {
                braceDepth++;
                inMethod = true;
            }
            else if (c == '}')
            {
                braceDepth--;
                if (inMethod && braceDepth == 0)
                {
                    return i + 1;
                }
            }
        }

        return content.Length;
    }

    private static Dictionary<string, int> CountLogStatements(string methodBody, List<string> logPatterns)
    {
        var counts = new Dictionary<string, int>
        {
            ["Debug"] = 0,
            ["Info"] = 0,
            ["Warn"] = 0,
            ["Error"] = 0
        };

        if (logPatterns.Count >= 1)
            counts["Debug"] = Regex.Matches(methodBody, logPatterns[0]).Count;
        if (logPatterns.Count >= 2)
            counts["Info"] = Regex.Matches(methodBody, logPatterns[1]).Count;
        if (logPatterns.Count >= 3)
            counts["Warn"] = Regex.Matches(methodBody, logPatterns[2]).Count;
        if (logPatterns.Count >= 4)
            counts["Error"] = Regex.Matches(methodBody, logPatterns[3]).Count;

        return counts;
    }

    private static string GenerateMarkdownReport(ComparisonConfig config, Dictionary<string, LanguageAnalysis> comparison)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"# {config.ClassName} Class Comparison Across Languages");
        sb.AppendLine();
        sb.AppendLine("## Overview");
        sb.AppendLine($"This document compares the {config.ClassName} class structure and logging consistency across multiple language implementations.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Get all unique method names
        var allMethods = new HashSet<string>();
        foreach (var analysis in comparison.Values)
        {
            foreach (var method in analysis.Methods.Keys)
            {
                allMethods.Add(method);
            }
        }
        var sortedMethods = allMethods.OrderBy(m => m).ToList();

        // Method Structure Comparison Table
        sb.AppendLine("## Method Structure Comparison");
        sb.AppendLine();
        sb.Append("| Method Name |");
        foreach (var lang in config.Languages)
        {
            sb.Append($" {lang.Name} |");
        }
        sb.AppendLine(" Notes |");

        sb.Append("|-------------|");
        foreach (var lang in config.Languages)
        {
            sb.Append("-------|");
        }
        sb.AppendLine("-------|");

        foreach (var method in sortedMethods)
        {
            sb.Append($"| **{method}** |");
            foreach (var lang in config.Languages)
            {
                if (comparison.ContainsKey(lang.Name) && comparison[lang.Name].Methods.ContainsKey(method))
                {
                    sb.Append(" ✓ |");
                }
                else
                {
                    sb.Append(" ✗ |");
                }
            }
            sb.AppendLine(" |");
        }
        sb.AppendLine();

        // Logger Statement Count per Method
        sb.AppendLine("## Logger Statement Count per Method");
        sb.AppendLine();

        foreach (var method in sortedMethods)
        {
            sb.AppendLine($"### {method}");
            sb.AppendLine("| Language | Debug Logs | Info Logs | Warn Logs | Error Logs | Total |");
            sb.AppendLine("|----------|-----------|-----------|-----------|-----------|-------|");

            bool isConsistent = true;
            int? expectedTotal = null;

            foreach (var lang in config.Languages)
            {
                if (comparison.ContainsKey(lang.Name) && comparison[lang.Name].Methods.ContainsKey(method))
                {
                    var methodInfo = comparison[lang.Name].Methods[method];
                    sb.AppendLine($"| {lang.Name} | {methodInfo.DebugLogs} | {methodInfo.InfoLogs} | {methodInfo.WarnLogs} | {methodInfo.ErrorLogs} | **{methodInfo.TotalLogs}** |");

                    if (expectedTotal == null)
                        expectedTotal = methodInfo.TotalLogs;
                    else if (expectedTotal != methodInfo.TotalLogs)
                        isConsistent = false;
                }
            }

            sb.AppendLine();
            sb.AppendLine(isConsistent ? "**Status:** ✅ Consistent across all languages" : "**Status:** ⚠️ **INCONSISTENT**");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        // Summary Statistics
        sb.AppendLine("## Summary Statistics");
        sb.AppendLine();
        sb.AppendLine("### Total Logger Statements per Language");
        sb.AppendLine("| Language | Debug | Info | Warn | Error | **Total** |");
        sb.AppendLine("|----------|-------|------|------|-------|-----------|");

        foreach (var lang in config.Languages)
        {
            if (comparison.ContainsKey(lang.Name))
            {
                var analysis = comparison[lang.Name];
                int totalDebug = analysis.Methods.Values.Sum(m => m.DebugLogs);
                int totalInfo = analysis.Methods.Values.Sum(m => m.InfoLogs);
                int totalWarn = analysis.Methods.Values.Sum(m => m.WarnLogs);
                int totalError = analysis.Methods.Values.Sum(m => m.ErrorLogs);
                int total = totalDebug + totalInfo + totalWarn + totalError;

                sb.AppendLine($"| {lang.Name} | {totalDebug} | {totalInfo} | {totalWarn} | {totalError} | **{total}** |");
            }
        }
        sb.AppendLine();

        // Consistency Report
        var inconsistentMethods = new List<string>();
        var consistentMethods = new List<string>();

        foreach (var method in sortedMethods)
        {
            bool isConsistent = true;
            int? expectedTotal = null;

            foreach (var lang in config.Languages)
            {
                if (comparison.ContainsKey(lang.Name) && comparison[lang.Name].Methods.ContainsKey(method))
                {
                    var methodInfo = comparison[lang.Name].Methods[method];

                    if (expectedTotal == null)
                        expectedTotal = methodInfo.TotalLogs;
                    else if (expectedTotal != methodInfo.TotalLogs)
                        isConsistent = false;
                }
            }

            if (isConsistent)
                consistentMethods.Add(method);
            else
                inconsistentMethods.Add(method);
        }

        sb.AppendLine($"### Methods with Perfect Consistency ({consistentMethods.Count} methods)");
        foreach (var method in consistentMethods)
        {
            sb.AppendLine($"✅ {method}");
        }
        sb.AppendLine();

        sb.AppendLine($"### Methods Needing Alignment ({inconsistentMethods.Count} methods)");
        foreach (var method in inconsistentMethods)
        {
            sb.AppendLine($"⚠️ {method}");
        }
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return sb.ToString();
    }
}

// Configuration Classes
public class ComparisonConfig
{
    public string ClassName { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public List<LanguageConfig> Languages { get; set; } = new();
}

public class LanguageConfig
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string MethodPattern { get; set; } = "";
    public List<string> LogPatterns { get; set; } = new();
}

// Analysis Results
public class LanguageAnalysis
{
    public string LanguageName { get; set; } = "";
    public Dictionary<string, MethodInfo> Methods { get; set; } = new();
}

public class MethodInfo
{
    public string Name { get; set; } = "";
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public int DebugLogs { get; set; }
    public int InfoLogs { get; set; }
    public int WarnLogs { get; set; }
    public int ErrorLogs { get; set; }
    public int TotalLogs { get; set; }
}

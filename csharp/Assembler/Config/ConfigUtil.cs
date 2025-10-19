using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Assembler.Config;

public class Scenario
{
    public string AppSite { get; set; }
    public string AppFile { get; set; }
    public string AppView { get; set; }
    public int TotalSize { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }

    public Scenario(string appSite, string appFile, string appView, int totalSize = 0, string displayName = "", string description = "")
    {
        AppSite = appSite;
        AppFile = appFile;
        AppView = appView;
        TotalSize = totalSize;
        DisplayName = displayName;
        Description = description;
    }

    public override string ToString()
    {
        return $"{AppSite}:{AppFile}:{AppView}:{TotalSize}";
    }

    public string ToCsvLine()
    {
        // CSV format: AppSite,AppFile,AppView,TotalSize,DisplayName,Description
        return $"{AppSite},{AppFile},{AppView},{TotalSize},\"{DisplayName}\",\"{Description}\"";
    }
}

public static class ConfigUtil
{
    private static HashSet<string>? _cachedAppSites = null;
    private static List<Scenario>? _cachedScenarios = null;
    private static readonly object _cacheLock = new object();
    private static string? _wwwrootPath = null;

    /// <summary>
    /// Extracts unique AppSites from scenarios
    /// </summary>
    private static HashSet<string> ExtractAppSitesFromScenarios(List<Scenario> scenarios)
    {
        var appSites = scenarios
            .Select(s => s.AppSite)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"[ConfigUtil] Extracted {appSites.Count} AppSites from scenarios.csv");

        return new HashSet<string>(appSites, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Calculates the total size of all template files for a given AppSite
    /// </summary>
    private static int CalculateTotalTemplateSize(string appSitesPath, string appSite)
    {
        int totalSize = 0;
        var appSiteDir = Path.Combine(appSitesPath, appSite);

        try
        {
            // Get all HTML and JSON files in the appSite directory and subdirectories
            var htmlFiles = Directory.GetFiles(appSiteDir, "*.html", SearchOption.AllDirectories);
            var jsonFiles = Directory.GetFiles(appSiteDir, "*.json", SearchOption.AllDirectories);

            foreach (var file in htmlFiles.Concat(jsonFiles))
            {
                var fileInfo = new FileInfo(file);
                totalSize += (int)fileInfo.Length;
            }
        }
        catch
        {
            // Return 0 if there's any error calculating size
            return 0;
        }

        return totalSize;
    }

    /// <summary>
    /// Generates display name for a scenario
    /// </summary>
    private static string GenerateDisplayName(string appSite, string appView)
    {
        // Extract rule type from appSite (e.g., "HtmlRule1A" -> "Rule 1A", "JsonRule2B" -> "Rule 2B")
        var rulePart = appSite.Replace("Html", "").Replace("Json", "");
        var displayName = "";

        if (appSite.StartsWith("Html"))
        {
            displayName = rulePart + " (HTML)";
        }
        else if (appSite.StartsWith("Json"))
        {
            displayName = rulePart + " (JSON)";
        }
        else
        {
            displayName = appSite;
        }

        if (!string.IsNullOrEmpty(appView))
        {
            displayName += $" - AppView: {appView}";
        }

        return displayName;
    }

    /// <summary>
    /// Generates description for a scenario based on rule patterns
    /// </summary>
    private static string GenerateDescription(string appSite, string appView)
    {
        var description = "";

        // Generate description based on appSite pattern
        if (appSite.Contains("Rule1"))
        {
            description = "Simple placeholder replacement";
        }
        else if (appSite.Contains("Rule2"))
        {
            description = "Slotted markup patterns";
        }
        else if (appSite.Contains("Rule3"))
        {
            description = "Context-based placeholders";
        }

        if (appSite.Contains("Html") && appSite.Contains("Json"))
        {
            description += " with HTML and JSON";
        }
        else if (appSite.Contains("Json"))
        {
            description += " with JSON data";
        }

        if (!string.IsNullOrEmpty(appView))
        {
            description += $" ({appView} view)";
        }

        return description;
    }

    /// <summary>
    /// Discovers scenarios by scanning AppSites folders and generates scenarios.csv
    /// </summary>
    private static void GenerateScenariosCsv(string wwwrootPath)
    {
        var appSitesPath = Path.Combine(wwwrootPath, "AppSites");
        var appDataPath = Path.Combine(wwwrootPath, "App_Data");
        var csvFilePath = Path.Combine(appDataPath, "scenarios.csv");

        if (!Directory.Exists(appSitesPath))
        {
            throw new DirectoryNotFoundException($"AppSites directory not found: {appSitesPath}");
        }

        // Ensure App_Data directory exists
        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }

        var scenarios = new List<Scenario>();

        // Get all directories in AppSites folder
        var appSiteDirs = Directory.GetDirectories(appSitesPath)
            .Select(dir => Path.GetFileName(dir))
            .Where(dirName => !string.IsNullOrEmpty(dirName))
            .OrderBy(name => name)
            .ToList();

        foreach (var appSite in appSiteDirs)
        {
            // Get all HTML files in the appSite directory
            var appSiteDir = Path.Combine(appSitesPath, appSite);
            var htmlFiles = Directory.GetFiles(appSiteDir, "*.html", SearchOption.TopDirectoryOnly);

            foreach (var htmlFile in htmlFiles)
            {
                var appFile = Path.GetFileNameWithoutExtension(htmlFile);

                // Calculate total size of all template files for this appSite
                int totalSize = CalculateTotalTemplateSize(appSitesPath, appSite);

                // Generate display name and description
                var displayName = GenerateDisplayName(appSite, "");
                var description = GenerateDescription(appSite, "");

                // Add default scenario (no AppView)
                scenarios.Add(new Scenario(appSite, appFile, "", totalSize, displayName, description));

                // Check for Views folder
                var viewsPath = Path.Combine(appSitesPath, appSite, "Views");
                if (Directory.Exists(viewsPath))
                {
                    var viewFiles = Directory.GetFiles(viewsPath, "*.html");
                    foreach (var viewFile in viewFiles)
                    {
                        var viewName = Path.GetFileNameWithoutExtension(viewFile);
                        var appView = "";

                        // Extract AppView from view filename (e.g., "Html3AContent.html" -> "Html3A")
                        if (viewName.ToLowerInvariant().Contains("content"))
                        {
                            var contentIndex = viewName.ToLowerInvariant().IndexOf("content");
                            if (contentIndex > 0)
                            {
                                var viewPart = viewName.Substring(0, contentIndex);
                                if (viewPart.Length > 0)
                                {
                                    appView = char.ToUpper(viewPart[0]) + viewPart.Substring(1);
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(appView))
                        {
                            var viewDisplayName = GenerateDisplayName(appSite, appView);
                            var viewDescription = GenerateDescription(appSite, appView);
                            scenarios.Add(new Scenario(appSite, appFile, appView, totalSize, viewDisplayName, viewDescription));
                        }
                    }
                }
            }
        }

        // Write as multi-line CSV with header
        var csvLines = new List<string>
        {
            "AppSite,AppFile,AppView,TotalSize,DisplayName,Description"
        };
        csvLines.AddRange(scenarios.Select(s => s.ToCsvLine()));

        File.WriteAllLines(csvFilePath, csvLines);

        Console.WriteLine($"[ConfigUtil] Generated scenarios.csv with {scenarios.Count} scenarios");
    }

    /// <summary>
    /// Loads scenarios from scenarios.csv, generates it if it doesn't exist (internal method)
    /// </summary>
    private static List<Scenario> LoadScenariosInternal(string wwwrootPath)
    {
        var appDataPath = Path.Combine(wwwrootPath, "App_Data");
        var csvFilePath = Path.Combine(appDataPath, "scenarios.csv");

        // Generate scenarios.csv if it doesn't exist
        if (!File.Exists(csvFilePath))
        {
            Console.WriteLine($"[ConfigUtil] scenarios.csv not found, generating...");
            GenerateScenariosCsv(wwwrootPath);
        }

        // Read CSV lines
        var csvLines = File.ReadAllLines(csvFilePath);

        if (csvLines.Length == 0)
        {
            throw new InvalidOperationException("scenarios.csv is empty");
        }

        var scenarios = new List<Scenario>();

        // Check if first line is a header (contains "AppSite,AppFile")
        bool hasHeader = csvLines[0].Contains("AppSite") && csvLines[0].Contains("AppFile");
        int startLine = hasHeader ? 1 : 0;

        for (int i = startLine; i < csvLines.Length; i++)
        {
            var line = csvLines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Parse CSV line with quoted values
            var parts = ParseCsvLine(line);

            if (parts.Length >= 2)
            {
                var appSite = parts[0].Trim();
                var appFile = parts[1].Trim();
                var appView = parts.Length > 2 ? parts[2].Trim() : "";
                var totalSize = 0;
                if (parts.Length > 3 && int.TryParse(parts[3].Trim(), out var parsedSize))
                {
                    totalSize = parsedSize;
                }
                var displayName = parts.Length > 4 ? parts[4].Trim().Trim('"') : "";
                var description = parts.Length > 5 ? parts[5].Trim().Trim('"') : "";

                scenarios.Add(new Scenario(appSite, appFile, appView, totalSize, displayName, description));
            }
        }

        if (scenarios.Count == 0)
        {
            throw new InvalidOperationException("No scenarios found in scenarios.csv");
        }

        Console.WriteLine($"[ConfigUtil] Loaded {scenarios.Count} scenarios from scenarios.csv");

        return scenarios;
    }

    /// <summary>
    /// Parses a CSV line handling quoted values
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = "";
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current);
                current = "";
            }
            else
            {
                current += c;
            }
        }

        result.Add(current);
        return result.ToArray();
    }

    /// <summary>
    /// Loads AppSites from wwwroot path and caches them. Call this during startup.
    /// </summary>
    public static void Load(string wwwrootPath)
    {
        lock (_cacheLock)
        {
            _wwwrootPath = wwwrootPath;
            _cachedScenarios = LoadScenariosInternal(wwwrootPath);
            _cachedAppSites = ExtractAppSitesFromScenarios(_cachedScenarios);
        }
    }

    /// <summary>
    /// Reloads AppSites and Scenarios from the stored wwwroot path. Throws if not loaded.
    /// </summary>
    public static void Reload()
    {
        lock (_cacheLock)
        {
            if (_wwwrootPath == null)
                throw new InvalidOperationException("ConfigUtil not loaded. Call Load(wwwrootPath) first.");

            _cachedScenarios = LoadScenariosInternal(_wwwrootPath);
            _cachedAppSites = ExtractAppSitesFromScenarios(_cachedScenarios);
        }
    }

    /// <summary>
    /// Gets the cached AppSites. Throws if not loaded.
    /// </summary>
    public static HashSet<string> GetAppSites()
    {
        if (_cachedAppSites == null)
            throw new InvalidOperationException("AppSitesConfig not loaded. Call Load(wwwrootPath) first.");

        return _cachedAppSites;
    }

    /// <summary>
    /// Gets the cached Scenarios. Throws if not loaded.
    /// </summary>
    public static List<Scenario> GetScenarios()
    {
        if (_cachedScenarios == null)
            throw new InvalidOperationException("AppSitesConfig not loaded. Call Load(wwwrootPath) first.");

        return _cachedScenarios;
    }

    /// <summary>
    /// Filters scenarios by appSite
    /// </summary>
    public static List<Scenario> FilterByAppSite(List<Scenario> scenarios, string appSiteFilter)
    {
        if (string.IsNullOrEmpty(appSiteFilter))
        {
            return scenarios;
        }

        return scenarios
            .Where(s => s.AppSite.Equals(appSiteFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

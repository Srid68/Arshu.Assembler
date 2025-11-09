using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Standalone script to generate/update scenarios.json by scanning AppSites folder
/// Usage:
///   First run (no scenarios.json exists): dotnet run GenerateScenarios.cs
///   - Creates default scenarios.json template
///   - Edit the file and fill in RelativeRootPath
///   - Run again to generate scenarios
///
///   Subsequent runs: dotnet run GenerateScenarios.cs
///   - Reads RelativeRootPath from scenarios.json
///   - Scans AppSites and updates scenarios
/// </summary>
class GenerateScenarios
{

    class ScenariosConfig
    {
        public string RelativeRootPath { get; set; } = "";
        public bool CopyJson { get; set; } = false;
        public List<string> UpdateJsonLists { get; set; } = new List<string>();
        public List<Scenario> AppSites { get; set; } = new List<Scenario>();
    }

    class Scenario
    {
        public string AppSite { get; set; } = "";
        public string AppFile { get; set; } = "";
        public string AppView { get; set; } = "";
        public int TotalSize { get; set; } = 0;
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
    }

    static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("========================================");
            Console.WriteLine("Scenarios.json Generator");
            Console.WriteLine("========================================");
            Console.WriteLine();

            // Get the current directory (where script runs)
            var currentDir = Directory.GetCurrentDirectory();
            Console.WriteLine($"Current directory: {currentDir}");

            // JSON file will be saved in current directory
            var jsonFilePath = Path.Combine(currentDir, "scenarios.json");

            // Check if scenarios.json exists
            if (!File.Exists(jsonFilePath))
            {
                Console.WriteLine("scenarios.json not found. Creating default template...");
                CreateDefaultScenariosJson(jsonFilePath);
                Console.WriteLine();
                Console.WriteLine("✓ Created default scenarios.json template");
                Console.WriteLine($"✓ File location: {jsonFilePath}");
                Console.WriteLine();
                Console.WriteLine("NEXT STEPS:");
                Console.WriteLine("1. Open scenarios.json in a text editor");
                Console.WriteLine("2. Fill in the RelativeRootPath (e.g., \"AssemblerWeb/wwwroot\")");
                Console.WriteLine("3. Run this script again to generate scenarios");
                return;
            }

            // Read existing config from scenarios.json
            ScenariosConfig? existingConfig = null;
            var existingScenarios = new Dictionary<string, Scenario>();

            Console.WriteLine("Reading existing scenarios.json...");
            try
            {
                var jsonContent = File.ReadAllText(jsonFilePath);
                existingConfig = ParseScenariosConfigJson(jsonContent);

                foreach (var scenario in existingConfig.AppSites)
                {
                    var key = $"{scenario.AppSite}:{scenario.AppView}";
                    existingScenarios[key] = scenario;
                }

                Console.WriteLine($"Loaded {existingScenarios.Count} existing scenarios");
                Console.WriteLine($"Config: RelativeRootPath={existingConfig.RelativeRootPath}, CopyJson={existingConfig.CopyJson}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not parse existing scenarios.json: {ex.Message}");
                Console.WriteLine("Will generate fresh scenario data");
                Console.WriteLine();
            }

            // Check if RelativeRootPath is filled in
            string? relativeRootPath = existingConfig?.RelativeRootPath;
            if (string.IsNullOrWhiteSpace(relativeRootPath))
            {
                Console.WriteLine("ERROR: RelativeRootPath is empty in scenarios.json!");
                Console.WriteLine();
                Console.WriteLine("Please edit scenarios.json and fill in the RelativeRootPath");
                Console.WriteLine("Example: \"RelativeRootPath\": \"AssemblerWeb/wwwroot\"");
                return;
            }

            Console.WriteLine($"Using RelativeRootPath: {relativeRootPath}");

            // Find wwwroot path
            string wwwrootPath = Path.Combine(currentDir, relativeRootPath);
            wwwrootPath = Path.GetFullPath(wwwrootPath); // Normalize path

            if (!Directory.Exists(wwwrootPath))
            {
                Console.WriteLine($"ERROR: Could not find wwwroot directory at: {wwwrootPath}");
                Console.WriteLine($"Hint: Provide correct RelativeRootPath as command-line argument");
                return;
            }

            Console.WriteLine($"Using wwwroot path: {wwwrootPath}");
            Console.WriteLine($"Output file: {jsonFilePath}");
            Console.WriteLine();

            // Paths
            var appSitesPath = Path.Combine(wwwrootPath, "AppSites");

            if (!Directory.Exists(appSitesPath))
            {
                Console.WriteLine($"ERROR: AppSites directory not found: {appSitesPath}");
                return;
            }

            // Discover AppSites from filesystem
            Console.WriteLine("Scanning AppSites folder...");
            var scenarios = new List<Scenario>();

            var appSiteDirs = Directory.GetDirectories(appSitesPath)
                .Select(dir => Path.GetFileName(dir))
                .Where(dirName => !string.IsNullOrEmpty(dirName))
                .OrderBy(name => name)
                .ToList();

            Console.WriteLine($"Found {appSiteDirs.Count} AppSites");
            Console.WriteLine();

            foreach (var appSite in appSiteDirs)
            {
                var appSiteDir = Path.Combine(appSitesPath, appSite);

                // Get all HTML files in the appSite directory (top level only)
                var htmlFiles = Directory.GetFiles(appSiteDir, "*.html", SearchOption.TopDirectoryOnly);

                foreach (var htmlFile in htmlFiles)
                {
                    var appFile = Path.GetFileNameWithoutExtension(htmlFile);

                    // Calculate total size
                    int totalSize = CalculateTotalTemplateSize(appSiteDir);

                    // Check for Views folder
                    var viewsPath = Path.Combine(appSiteDir, "Views");
                    var viewDirs = new List<string>();

                    if (Directory.Exists(viewsPath))
                    {
                        // Get all subdirectories in Views folder
                        viewDirs = Directory.GetDirectories(viewsPath)
                            .Select(dir => Path.GetFileName(dir))
                            .Where(dirName => !string.IsNullOrEmpty(dirName))
                            .ToList();
                    }

                    // Only add empty AppView scenario if no specific Views exist
                    if (viewDirs.Count == 0)
                    {
                        // Check if we have existing scenario data
                        var key = $"{appSite}:";
                        var existingScenario = existingScenarios.GetValueOrDefault(key);

                        scenarios.Add(new Scenario
                        {
                            AppSite = appSite,
                            AppFile = appFile,
                            AppView = "",
                            TotalSize = totalSize,
                            DisplayName = existingScenario?.DisplayName ?? GenerateDisplayName(appSite, ""),
                            Description = existingScenario?.Description ?? GenerateDescription(appSite, "")
                        });
                    }
                    else
                    {
                        // Add specific view scenarios
                        foreach (var viewDir in viewDirs)
                        {
                            var viewKey = $"{appSite}:{viewDir}";
                            var existingViewScenario = existingScenarios.GetValueOrDefault(viewKey);

                            scenarios.Add(new Scenario
                            {
                                AppSite = appSite,
                                AppFile = appFile,
                                AppView = viewDir,
                                TotalSize = totalSize,
                                DisplayName = existingViewScenario?.DisplayName ?? GenerateDisplayName(appSite, viewDir),
                                Description = existingViewScenario?.Description ?? GenerateDescription(appSite, viewDir)
                            });
                        }
                    }
                }
            }

            // Write JSON file
            Console.WriteLine("Writing scenarios.json...");

            // Build config with preserved settings and updated scenarios
            var config = new ScenariosConfig
            {
                RelativeRootPath = relativeRootPath,
                CopyJson = existingConfig?.CopyJson ?? false,
                UpdateJsonLists = existingConfig?.UpdateJsonLists ?? new List<string>(),
                AppSites = scenarios
            };

            var json = BuildScenariosConfigJson(config);
            File.WriteAllText(jsonFilePath, json);

            Console.WriteLine($"✓ Generated scenarios.json with {scenarios.Count} scenarios");
            Console.WriteLine($"✓ File location: {jsonFilePath}");
            Console.WriteLine();

            // Copy scenarios to UpdateJsonLists if CopyJson is true
            if (config.CopyJson && config.UpdateJsonLists.Count > 0)
            {
                Console.WriteLine("CopyJson is enabled. Updating target JSON files...");
                Console.WriteLine();

                foreach (var targetFile in config.UpdateJsonLists)
                {
                    try
                    {
                        var targetPath = Path.Combine(wwwrootPath, targetFile);
                        Console.WriteLine($"Updating: {targetFile}");

                        if (!File.Exists(targetPath))
                        {
                            Console.WriteLine($"  ⚠ File not found: {targetPath}");
                            continue;
                        }

                        // Read existing JSON file
                        var existingJson = File.ReadAllText(targetPath);

                        // Update with scenarios
                        var updatedJson = AddOrUpdateScenariosInJson(existingJson, scenarios);

                        // Write back
                        File.WriteAllText(targetPath, updatedJson);

                        Console.WriteLine($"  ✓ Updated with {scenarios.Count} scenarios");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ✗ Error: {ex.Message}");
                    }
                }

                Console.WriteLine();
            }

            Console.WriteLine("Done!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    static int CalculateTotalTemplateSize(string appSiteDir)
    {
        int totalSize = 0;

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

    static string GenerateDisplayName(string appSite, string appView)
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
            displayName += $" → {appView}";
        }

        return displayName;
    }

    static string GenerateDescription(string appSite, string appView)
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

    static string BuildScenariosConfigJson(ScenariosConfig config)
    {
        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine($"  \"RelativeRootPath\": \"{EscapeJson(config.RelativeRootPath)}\",");
        json.AppendLine($"  \"CopyJson\": {config.CopyJson.ToString().ToLower()},");

        // Write UpdateJsonLists array
        json.Append("  \"UpdateJsonLists\": [");
        if (config.UpdateJsonLists.Count > 0)
        {
            json.AppendLine();
            for (int i = 0; i < config.UpdateJsonLists.Count; i++)
            {
                json.Append($"    \"{EscapeJson(config.UpdateJsonLists[i])}\"");
                if (i < config.UpdateJsonLists.Count - 1)
                    json.AppendLine(",");
                else
                    json.AppendLine();
            }
            json.AppendLine("  ],");
        }
        else
        {
            json.AppendLine("],");
        }

        // Write AppSites array
        json.AppendLine("  \"AppSites\": [");
        for (int i = 0; i < config.AppSites.Count; i++)
        {
            var s = config.AppSites[i];
            json.AppendLine("    {");
            json.AppendLine($"      \"appSite\": \"{EscapeJson(s.AppSite)}\",");
            json.AppendLine($"      \"appFile\": \"{EscapeJson(s.AppFile)}\",");
            json.AppendLine($"      \"appView\": \"{EscapeJson(s.AppView)}\",");
            json.AppendLine($"      \"displayName\": \"{EscapeJson(s.DisplayName)}\",");
            json.AppendLine($"      \"description\": \"{EscapeJson(s.Description)}\",");
            json.AppendLine($"      \"totalSize\": {s.TotalSize}");
            json.Append("    }");

            if (i < config.AppSites.Count - 1)
                json.AppendLine(",");
            else
                json.AppendLine();
        }
        json.AppendLine("  ]");
        json.AppendLine("}");
        return json.ToString();
    }

    static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    static ScenariosConfig ParseScenariosConfigJson(string jsonContent)
    {
        var config = new ScenariosConfig();

        // Simple JSON parser for scenarios config
        // This is a basic implementation that works for well-formed JSON

        var lines = jsonContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Scenario? currentScenario = null;
        bool inAppSitesArray = false;
        bool inUpdateJsonListsArray = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Check for arrays
            if (trimmed.StartsWith("\"AppSites\""))
            {
                inAppSitesArray = true;
                inUpdateJsonListsArray = false;
                continue;
            }
            else if (trimmed.StartsWith("\"UpdateJsonLists\""))
            {
                inUpdateJsonListsArray = true;
                inAppSitesArray = false;
                continue;
            }

            // Parse UpdateJsonLists array items
            if (inUpdateJsonListsArray && trimmed.StartsWith("\"") && !trimmed.StartsWith("\"UpdateJsonLists\""))
            {
                var value = trimmed.TrimEnd(',').Trim().Trim('"');
                if (!string.IsNullOrEmpty(value) && value != "[" && value != "]")
                {
                    config.UpdateJsonLists.Add(value);
                }
            }

            // Parse AppSites array
            if (inAppSitesArray)
            {
                if (trimmed == "{")
                {
                    currentScenario = new Scenario();
                }
                else if (trimmed == "}" || trimmed == "},")
                {
                    if (currentScenario != null)
                    {
                        config.AppSites.Add(currentScenario);
                        currentScenario = null;
                    }
                }
                else if (currentScenario != null && trimmed.Contains(":"))
                {
                    var colonIndex = trimmed.IndexOf(':');
                    var key = trimmed.Substring(0, colonIndex).Trim().Trim('"');
                    var value = trimmed.Substring(colonIndex + 1).Trim().TrimEnd(',').Trim();

                    // Remove quotes from string values
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }

                    switch (key)
                    {
                        case "appSite":
                            currentScenario.AppSite = value;
                            break;
                        case "appFile":
                            currentScenario.AppFile = value;
                            break;
                        case "appView":
                            currentScenario.AppView = value;
                            break;
                        case "displayName":
                            currentScenario.DisplayName = value;
                            break;
                        case "description":
                            currentScenario.Description = value;
                            break;
                        case "totalSize":
                            if (int.TryParse(value, out var size))
                                currentScenario.TotalSize = size;
                            break;
                    }
                }
            }
            // Parse root level properties
            else if (!inAppSitesArray && !inUpdateJsonListsArray && trimmed.Contains(":"))
            {
                var colonIndex = trimmed.IndexOf(':');
                var key = trimmed.Substring(0, colonIndex).Trim().Trim('"');
                var value = trimmed.Substring(colonIndex + 1).Trim().TrimEnd(',').Trim();

                // Remove quotes from string values
                if (value.StartsWith("\"") && value.EndsWith("\""))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                switch (key)
                {
                    case "RelativeRootPath":
                        config.RelativeRootPath = value;
                        break;
                    case "CopyJson":
                        if (bool.TryParse(value, out var copyJson))
                            config.CopyJson = copyJson;
                        break;
                }
            }
        }

        return config;
    }

    static void CreateDefaultScenariosJson(string jsonFilePath)
    {
        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  \"RelativeRootPath\": \"\",");
        json.AppendLine("  \"CopyJson\": false,");
        json.AppendLine("  \"UpdateJsonLists\": [");
        json.AppendLine("    \"Test/Component/Toolbar/toolbar.json\"");
        json.AppendLine("  ],");
        json.AppendLine("  \"AppSites\": []");
        json.AppendLine("}");

        File.WriteAllText(jsonFilePath, json.ToString());
    }

    static string AddOrUpdateScenariosInJson(string existingJson, List<Scenario> scenarios)
    {
        var lines = existingJson.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new StringBuilder();
        bool scenariosFound = false;
        int braceDepth = 0;
        int detectedIndent = 4; // Default to 4 spaces
        int lastPropertyLineIndex = -1;

        // First pass: detect indentation and find last property line
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed == "{")
            {
                braceDepth++;
            }
            else if (trimmed == "}" || trimmed.StartsWith("}"))
            {
                braceDepth--;
            }
            else if (braceDepth == 1 && trimmed.Contains(":") && !trimmed.StartsWith("//"))
            {
                // Detect indentation from first property
                if (lastPropertyLineIndex == -1)
                {
                    int leadingSpaces = lines[i].Length - lines[i].TrimStart().Length;
                    if (leadingSpaces > 0)
                        detectedIndent = leadingSpaces;
                }
                lastPropertyLineIndex = i;
            }
        }

        // Second pass: build result
        braceDepth = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            // Track brace depth
            if (trimmed == "{")
            {
                braceDepth++;
                result.AppendLine(lines[i]);
                continue;
            }
            else if (trimmed == "}" || trimmed.StartsWith("}"))
            {
                braceDepth--;

                // If we're closing the root object and haven't added scenarios yet, add them now
                if (braceDepth == 0 && !scenariosFound)
                {
                    var indentStr = new string(' ', detectedIndent);
                    result.AppendLine(indentStr + "\"Scenarios\": " + BuildScenariosArrayJson(scenarios, detectedIndent));
                }

                result.AppendLine(lines[i]);
                continue;
            }

            // Check if this line starts the Scenarios property
            if (trimmed.StartsWith("\"Scenarios\"") && braceDepth == 1)
            {
                scenariosFound = true;

                // Add the Scenarios property with updated data
                var indentStr = new string(' ', detectedIndent);
                result.AppendLine(indentStr + "\"Scenarios\": " + BuildScenariosArrayJson(scenarios, detectedIndent));

                // Skip lines until we close the scenarios array
                int arrayDepth = 0;
                bool started = false;
                for (int j = i; j < lines.Length; j++)
                {
                    var line = lines[j].Trim();
                    if (line.Contains("["))
                    {
                        arrayDepth++;
                        started = true;
                    }
                    if (line.Contains("]"))
                    {
                        arrayDepth--;
                        if (started && arrayDepth == 0)
                        {
                            i = j; // Skip to after the array closes
                            break;
                        }
                    }
                }
                continue;
            }

            // Add comma to last property line if we're about to add Scenarios
            if (i == lastPropertyLineIndex && !scenariosFound)
            {
                var line = lines[i];
                // Only add comma if it doesn't already have one
                if (!line.TrimEnd().EndsWith(","))
                {
                    result.AppendLine(line + ",");
                }
                else
                {
                    result.AppendLine(line);
                }
            }
            else
            {
                result.AppendLine(lines[i]);
            }
        }

        return result.ToString();
    }

    static string BuildScenariosArrayJson(List<Scenario> scenarios, int indent)
    {
        var json = new StringBuilder();
        var indentStr = new string(' ', indent);
        var itemIndentStr = new string(' ', indent + 2);

        json.AppendLine("[");

        for (int i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            json.AppendLine(itemIndentStr + "{");
            json.AppendLine(itemIndentStr + $"  \"appSite\": \"{EscapeJson(s.AppSite)}\",");
            json.AppendLine(itemIndentStr + $"  \"appFile\": \"{EscapeJson(s.AppFile)}\",");
            json.AppendLine(itemIndentStr + $"  \"appView\": \"{EscapeJson(s.AppView)}\",");
            json.AppendLine(itemIndentStr + $"  \"displayName\": \"{EscapeJson(s.DisplayName)}\",");
            json.AppendLine(itemIndentStr + $"  \"description\": \"{EscapeJson(s.Description)}\",");
            json.AppendLine(itemIndentStr + $"  \"totalSize\": {s.TotalSize}");
            json.Append(itemIndentStr + "}");

            if (i < scenarios.Count - 1)
                json.AppendLine(",");
            else
                json.AppendLine();
        }

        json.Append(indentStr + "]");
        return json.ToString();
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Assembler.TemplateLoader;
using Assembler.TemplateModel;
using Arshu.App.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Web;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

//wsl --unregister Ubuntu-22.04
//wsl --install Ubuntu-22.04
//wsl -s Ubuntu-22.04
//wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
//sudo dpkg -i packages-microsoft-prod.deb
//rm packages-microsoft-prod.deb
//sudo apt update
//sudo apt install -y dotnet-sdk-9.0
//wsl bash -c "sudo apt-get remove --purge dotnet-sdk-9.0 dotnet-sdk-10.0 -y; rm -rf ~/.dotnet; sudo apt-get autoremove -y"
//

namespace AssemblerWebJs;

#region Models/Serialization Config

public class TemplateData
{
    public string Html { get; set; } = string.Empty;
    public string? Json { get; set; }
}

public class ScenarioDto
{
    public string AppSite { get; set; } = string.Empty;
    public string AppFile { get; set; } = string.Empty;
    public string AppView { get; set; } = string.Empty;
    public string AppViewPrefix { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
}

public class PreProcessTemplateMetadata
{
    public string OriginalContent { get; set; } = string.Empty;
    public List<TemplatePlaceholder> Placeholders { get; set; } = new();
    public List<SlottedTemplate> SlottedTemplates { get; set; } = new();
    public object? JsonData { get; set; }
    public List<JsonPlaceholder> JsonPlaceholders { get; set; } = new();
    public List<ReplacementMapping> ReplacementMappings { get; set; } = new();
    public bool HasPlaceholders { get; set; }
    public bool HasSlottedTemplates { get; set; }
    public bool HasJsonData { get; set; }
    public bool HasJsonPlaceholders { get; set; }
    public bool HasReplacementMappings { get; set; }
    public bool RequiresProcessing { get; set; }
}

[JsonSerializable(typeof(ScenarioDto))]
[JsonSerializable(typeof(List<ScenarioDto>))]
[JsonSerializable(typeof(ScenarioDto[]))]
[JsonSerializable(typeof(TemplateData))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(object))]
public partial class SimpleJsonContext : JsonSerializerContext
{
}

#endregion

public class Program
{
    #region Test Code

    /*
    // Add this helper method to Program class
    private static bool JsonElementsEqual(System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var aProps = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                if (aProps.Count != bProps.Count) return false;
                foreach (var key in aProps.Keys)
                {
                    if (!bProps.ContainsKey(key)) return false;
                    if (!JsonElementsEqual(aProps[key], bProps[key])) return false;
                }
                return true;
            case System.Text.Json.JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength()) return false;
                for (int i = 0; i < a.GetArrayLength(); i++)
                {
                    if (!JsonElementsEqual(a[i], b[i])) return false;
                }
                return true;
            default:
                return a.ToString() == b.ToString();
        }
    }

    // Add this helper function to Program class
    private static void CompareJsonStructureRecursive(string path, System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            Console.WriteLine($"DIFF at {path}: ValueKind differs - Old: {a.ValueKind}, New: {b.ValueKind}");
            return;
        }
        switch (a.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var aProps = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                foreach (var key in aProps.Keys)
                {
                    if (!bProps.ContainsKey(key))
                        Console.WriteLine($"DIFF at {path}: Key '{key}' missing in New JSON");
                }
                foreach (var key in bProps.Keys)
                {
                    if (!aProps.ContainsKey(key))
                        Console.WriteLine($"DIFF at {path}: Key '{key}' missing in Old JSON");
                }
                foreach (var key in aProps.Keys.Intersect(bProps.Keys))
                {
                    CompareJsonStructureRecursive($"{path}.{key}", aProps[key], bProps[key]);
                }
                break;
            case System.Text.Json.JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength())
                    Console.WriteLine($"DIFF at {path}: Array length differs - Old: {a.GetArrayLength()}, New: {b.GetArrayLength()}");
                int minLen = Math.Min(a.GetArrayLength(), b.GetArrayLength());
                for (int i = 0; i < minLen; i++)
                {
                    CompareJsonStructureRecursive($"{path}[{i}]", a[i], b[i]);
                }
                break;
            default:
                if (a.ToString() != b.ToString())
                    Console.WriteLine($"DIFF at {path}: Value differs - Old: '{a}', New: '{b}'");
                break;
        }
    }

    static void CompareJsonOutputs(object response)
    {
        var jsonConverterResult = JsonConverter.SerializeObjectForWeb(response);
        var systemTextJsonResult = JsonSerializer.Serialize(response);

        Console.WriteLine("=== JSON COMPARISON ANALYSIS ===");
        Console.WriteLine($"JsonConverter Length: {jsonConverterResult.Length}");
        Console.WriteLine($"System.Text.Json Length: {systemTextJsonResult.Length}");

        // Parse both JSON strings to compare structures
        try
        {
            var jsonConverterParsed = JsonDocument.Parse(jsonConverterResult);
            var systemTextJsonParsed = JsonDocument.Parse(systemTextJsonResult);

            Console.WriteLine("\n--- STRUCTURE ANALYSIS ---");
            CompareJsonElements("ROOT", jsonConverterParsed.RootElement, systemTextJsonParsed.RootElement);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing JSON for comparison: {ex.Message}");
        }

        // Detailed character-by-character comparison for HTML escaping differences
        Console.WriteLine("\n--- CHARACTER ESCAPING ANALYSIS ---");
        var jsonConverterLines = jsonConverterResult.Split('\n').Take(3).ToArray();
        var systemTextJsonLines = systemTextJsonResult.Split('\n').Take(3).ToArray();
        
        for (int i = 0; i < Math.Min(jsonConverterLines.Length, systemTextJsonLines.Length); i++)
        {
            var line1 = jsonConverterLines[i].Length > 200 ? jsonConverterLines[i].Substring(0, 200) + "..." : jsonConverterLines[i];
            var line2 = systemTextJsonLines[i].Length > 200 ? systemTextJsonLines[i].Substring(0, 200) + "..." : systemTextJsonLines[i];
            
            if (line1 != line2)
            {
                Console.WriteLine($"Line {i+1} DIFF:");
                Console.WriteLine($"JsonConverter: {line1}");
                Console.WriteLine($"System.Text.Json: {line2}");
                
                // Show character differences
                var minLen = Math.Min(line1.Length, line2.Length);
                for (int j = 0; j < minLen; j++)
                {
                    if (line1[j] != line2[j])
                    {
                        Console.WriteLine($"First diff at position {j}: JsonConverter='{line1[j]}' (0x{(int)line1[j]:X2}) vs System.Text.Json='{line2[j]}' (0x{(int)line2[j]:X2})");
                        Console.WriteLine($"Context: ...{line1.Substring(Math.Max(0, j-10), Math.Min(20, line1.Length - Math.Max(0, j-10)))}...");
                        Console.WriteLine($"Context: ...{line2.Substring(Math.Max(0, j-10), Math.Min(20, line2.Length - Math.Max(0, j-10)))}...");
                        break;
                    }
                }
            }
        }

        Console.WriteLine("\n--- FIRST 200 CHARS OF EACH ---");
        Console.WriteLine("JsonConverter:");
        Console.WriteLine(jsonConverterResult.Substring(0, Math.Min(200, jsonConverterResult.Length)));
        Console.WriteLine("\nSystem.Text.Json:");
        Console.WriteLine(systemTextJsonResult.Substring(0, Math.Min(200, systemTextJsonResult.Length)));

        // If lengths differ, find the exact difference
        if (jsonConverterResult.Length != systemTextJsonResult.Length)
        {
                Console.WriteLine("\n--- FULL STRING COMPARISON (due to length difference) ---");
            var minLen = Math.Min(jsonConverterResult.Length, systemTextJsonResult.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (jsonConverterResult[i] != systemTextJsonResult[i])
                {
                    Console.WriteLine($"First diff at position {i}: JsonConverter='{jsonConverterResult[i]}' (0x{(int)jsonConverterResult[i]:X2}) vs System.Text.Json='{systemTextJsonResult[i]}' (0x{(int)systemTextJsonResult[i]:X2})");
                    Console.WriteLine($"JsonConverter context: ...{jsonConverterResult.Substring(Math.Max(0, i-10), Math.Min(20, jsonConverterResult.Length - Math.Max(0, i-10)))}...");
                    Console.WriteLine($"System.Text.Json context: ...{systemTextJsonResult.Substring(Math.Max(0, i-10), Math.Min(20, systemTextJsonResult.Length - Math.Max(0, i-10)))}...");
                    break;
                }
            }
            if (jsonConverterResult.Length > systemTextJsonResult.Length)
            {
                Console.WriteLine($"JsonConverter has extra chars at end: '{jsonConverterResult.Substring(minLen)}'");
            }
            else
            {
                Console.WriteLine($"System.Text.Json has extra chars at end: '{systemTextJsonResult.Substring(minLen)}'");
            }
        }

        Console.WriteLine("=== END COMPARISON ===\n");
    }

    static void CompareJsonElements(string path, JsonElement jsonConverter, JsonElement systemTextJson)
    {
        if (jsonConverter.ValueKind != systemTextJson.ValueKind)
        {
            Console.WriteLine($"DIFF at {path}: ValueKind differs - JsonConverter: {jsonConverter.ValueKind}, System.Text.Json: {systemTextJson.ValueKind}");
            return;
        }

        switch (jsonConverter.ValueKind)
        {
            case JsonValueKind.Object:
                var jsonConverterProps = jsonConverter.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var systemTextJsonProps = systemTextJson.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

                // Check for missing properties
                foreach (var prop in jsonConverterProps.Keys.Except(systemTextJsonProps.Keys))
                {
                    Console.WriteLine($"DIFF at {path}: Property '{prop}' exists in JsonConverter but not in System.Text.Json");
                }
                foreach (var prop in systemTextJsonProps.Keys.Except(jsonConverterProps.Keys))
                {
                    Console.WriteLine($"DIFF at {path}: Property '{prop}' exists in System.Text.Json but not in JsonConverter");
                }

                // Compare common properties (limit depth to avoid too much output)
                if (path.Split('.').Length < 4) // Limit depth
                {
                    foreach (var prop in jsonConverterProps.Keys.Intersect(systemTextJsonProps.Keys).Take(5)) // Limit properties
                    {
                        CompareJsonElements($"{path}.{prop}", jsonConverterProps[prop], systemTextJsonProps[prop]);
                    }
                }
                break;

            case JsonValueKind.Array:
                if (jsonConverter.GetArrayLength() != systemTextJson.GetArrayLength())
                {
                    Console.WriteLine($"DIFF at {path}: Array length differs - JsonConverter: {jsonConverter.GetArrayLength()}, System.Text.Json: {systemTextJson.GetArrayLength()}");
                }
                break;

            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                if (jsonConverter.ToString() != systemTextJson.ToString())
                {
                    Console.WriteLine($"DIFF at {path}: Value differs - JsonConverter: '{jsonConverter}', System.Text.Json: '{systemTextJson}'");
                }
                break;
        }
    }
    */

    #endregion

    public static void Main(string[] args)
    {
        #region Print Environment Info

        // Print environment info
        if (System.IO.Directory.Exists("/proc"))
        {
            // Check for WSL
            if (System.IO.File.Exists("/proc/sys/kernel/osrelease"))
            {
                var osRelease = System.IO.File.ReadAllText("/proc/sys/kernel/osrelease");
                if (osRelease.Contains("microsoft"))
                {
                    Console.WriteLine("[WSL] Running in WSL environment");
                }
                else
                {
                    // Try to detect Linux distro
                    string distro = "Unknown Linux";
                    if (System.IO.File.Exists("/etc/os-release"))
                    {
                        var lines = System.IO.File.ReadAllLines("/etc/os-release");
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("ID="))
                            {
                                distro = line.Substring(3).Trim('"');
                                break;
                            }
                        }
                    }
                    Console.WriteLine($"[Linux] Running in {distro} environment");
                }
            }
            else
            {
                Console.WriteLine("[Linux] Running in Linux environment");
            }
        }
        else
        {
            Console.WriteLine("[Windows] Running in Windows environment");
        }

        #endregion

        #region Builder Configuration

        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, SimpleJsonContext.Default);
        });

        var app = builder.Build();

        #endregion

        #region Idle Tracking Middleware

        // Idle Tracking Middleware
        if (!app.Environment.IsDevelopment())
        {
            var idleSecondsEnv = Environment.GetEnvironmentVariable("IDLE_SECONDS");
            var idleSeconds = 10;
            if (!string.IsNullOrEmpty(idleSecondsEnv) && int.TryParse(idleSecondsEnv, out var envIdleSeconds))
                idleSeconds = envIdleSeconds;
            IdleTrackingMiddleware.Configure(idleSeconds);
            var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("IdleTrackingMiddleware");
            IdleTrackingMiddleware.SetLogger(logger);
            IdleTrackingMiddleware.StartTimer(appLifetime);
            app.Use((context, next) => IdleTrackingMiddleware.InvokeAsync(context, next, appLifetime));
        }

        #endregion

        #region Use Static Files

        app.UseStaticFiles();

        #endregion

        #region Root Endpoint

        // GET endpoint for root page with prepopulated scenarios
        app.MapGet("/", async (HttpContext context) =>
        {
            try
            {
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
                string appSitesPath = Path.Combine(rootDirPath, "AppSites");

                // Get all test directories
                var testDirs = Directory.GetDirectories(appSitesPath)
                    .Select(dir => Path.GetFileName(dir))
                    .Where(dirName => !dirName.Equals("roottemplate.html", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name)
                    .ToList();

                // Build options for select tag with uniform format
                var optionsList = new List<string>();
                foreach (var testDir in testDirs)
                {
                    var testDirPath = Path.Combine(appSitesPath, testDir);
                    var htmlFiles = Directory.GetFiles(testDirPath, "*.html")
                        .Select(file => Path.GetFileNameWithoutExtension(file))
                        .OrderBy(name => name)
                        .ToList();

                    // Check for Views subdirectory
                    var viewsPath = Path.Combine(testDirPath, "Views");
                    bool hasViews = Directory.Exists(viewsPath);

                    foreach (var htmlFile in htmlFiles)
                    {
                        // Dynamically set AppViewPrefix from root file name
                        string appViewPrefix = htmlFile;
                        if (!string.IsNullOrEmpty(appViewPrefix))
                        {
                            appViewPrefix = appViewPrefix.Substring(0, Math.Min(appViewPrefix.Length, 6));
                        }

                        // Always add the default option first (no AppView)
                        var optionValue = $"{{\"appSite\":\"{testDir}\",\"appFile\":\"{htmlFile}\",\"appView\":\"\",\"appViewPrefix\":\"{appViewPrefix}\"}}";
                        var optionText = $"{testDir} → {htmlFile}";
                        optionsList.Add($"<option value=\"{HttpUtility.HtmlAttributeEncode(optionValue)}\">{optionText}</option>");
                    }

                    if (hasViews)
                    {
                        var viewFiles = Directory.GetFiles(viewsPath, "*.html")
                            .Select(file => Path.GetFileNameWithoutExtension(file))
                            .OrderBy(name => name)
                            .ToList();

                        // Collect all possible AppView values from viewFiles
                        var appViewValues = viewFiles
                            .Select(vf =>
                            {
                                var idx = vf.ToLowerInvariant().IndexOf("content");
                                if (idx > 0)
                                {
                                    var viewPart = vf.Substring(0, idx);
                                    if (viewPart.Length > 0)
                                        return char.ToUpper(viewPart[0]) + viewPart.Substring(1);
                                }
                                return null;
                            })
                            .Where(av => !string.IsNullOrEmpty(av))
                            .Distinct()
                            .ToList();

                        // For each root HTML file, generate AppView test scenarios
                        foreach (var rootFile in htmlFiles)
                        {
                            string rootAppViewPrefix = rootFile;
                            if (!string.IsNullOrEmpty(rootAppViewPrefix))
                            {
                                rootAppViewPrefix = rootAppViewPrefix.Substring(0, Math.Min(rootAppViewPrefix.Length, 6));
                            }

                            // Generate AppView scenarios for ALL available AppViews
                            foreach (var appView in appViewValues)
                            {
                                if (!string.IsNullOrEmpty(appView))
                                {
                                    var optionValueAppView = $"{{\"appSite\":\"{testDir}\",\"appFile\":\"{rootFile}\",\"appView\":\"{appView}\",\"appViewPrefix\":\"{rootAppViewPrefix}\"}}";
                                    var optionTextAppView = $"{testDir} → {rootFile} (View: {appView})";
                                    optionsList.Add($"<option value=\"{HttpUtility.HtmlAttributeEncode(optionValueAppView)}\">{optionTextAppView}</option>");
                                }
                            }
                        }
                    }
                }

                string options = string.Join("\n        ", optionsList);

                // Read index.html and replace the options marker
                var templatePath = Path.Combine(rootDirPath, "index.html");
                string html = await File.ReadAllTextAsync(templatePath);
                html = html.Replace("<!--OPTIONS-->", options);
                return Results.Content(html, "text/html");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        #endregion

        #region Get Scenarios Endpoint

        app.MapGet("/api/scenarios", (HttpContext context) =>
        {
            try
            {
                Console.WriteLine("GET /api/scenarios called");
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
                var appSitesPath = Path.Combine(rootDirPath, "AppSites");
                Console.WriteLine($"AppSites path: {appSitesPath}");

                if (!Directory.Exists(appSitesPath))
                {
                    //Console.WriteLine("AppSites directory does not exist");
                    return Results.Ok(new ScenarioDto[0]);
                }

                var scenarios = new List<ScenarioDto>();

                var testDirs = Directory.GetDirectories(appSitesPath)
                    .Select(dir => Path.GetFileName(dir))
                    .Where(dirName => !dirName.Equals("roottemplate.html", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name)
                    .ToList();

                foreach (var testDir in testDirs)
                {
                    var testDirPath = Path.Combine(appSitesPath, testDir);
                    if (!Directory.Exists(testDirPath)) continue;

                    var htmlFiles = Directory.GetFiles(testDirPath, "*.html", SearchOption.TopDirectoryOnly);
                    foreach (var htmlFilePath in htmlFiles)
                    {
                        var appFileName = Path.GetFileNameWithoutExtension(htmlFilePath);

                        // Generate AppView scenarios based on Views folder (same logic as AssemblerTest)
                        var appViewScenarios = new List<(string AppView, string AppViewPrefix)>
                        {
                            ("", "") // No AppView (default scenario)
                        };

                        // Check for Views folder and add AppView scenarios
                        var viewsPath = Path.Combine(testDirPath, "Views");
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
                                {
                                    appViewScenarios.Add((appView, appViewPrefix));
                                }
                            }
                        }

                        // Create scenarios for each AppView combination
                        foreach (var scenario in appViewScenarios)
                        {
                            var displayText = string.IsNullOrEmpty(scenario.AppView)
                                ? $"{testDir} → {appFileName}"
                                : $"{testDir} → {appFileName} (View: {scenario.AppView})";

                            scenarios.Add(new ScenarioDto
                            {
                                AppSite = testDir,
                                AppFile = appFileName,
                                AppView = scenario.AppView,
                                AppViewPrefix = appFileName, // Use appFileName as AppViewPrefix like in AssemblerTest
                                DisplayText = displayText
                            });
                        }
                    }
                }

                Console.WriteLine($"Found {scenarios.Count} scenarios");
                return Results.Ok(scenarios);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in /api/scenarios: {ex.Message}");
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        #endregion

        #region Get Templates Endpoint

        app.MapGet("/api/templates/{appSite}", (HttpContext context, string appSite, string? appFile = null, string? appView = null) =>
        {
            try
            {
                var serverStart = DateTime.UtcNow;
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                // Load Normal templates
                var normalTemplates = LoaderNormal.LoadGetTemplateFiles(rootDirPath, appSite);

                // Load PreProcess templates
                var preprocessTemplates = LoaderPreProcess.LoadProcessGetTemplateFiles(rootDirPath, appSite);

                // Convert Normal templates to TemplateData objects for proper JSON serialization
                var normalResult = normalTemplates.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new TemplateData { Html = kvp.Value.html, Json = kvp.Value.json }
                );

                // Convert PreProcess templates to metadata-only objects
                var preprocessResult = preprocessTemplates.Templates.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new PreProcessTemplateMetadata
                    {
                        OriginalContent = kvp.Value.OriginalContent,
                        Placeholders = kvp.Value.Placeholders,
                        SlottedTemplates = kvp.Value.SlottedTemplates,
                        JsonData = kvp.Value.JsonData,
                        JsonPlaceholders = kvp.Value.JsonPlaceholders,
                        ReplacementMappings = kvp.Value.ReplacementMappings,
                        HasPlaceholders = kvp.Value.HasPlaceholders,
                        HasSlottedTemplates = kvp.Value.HasSlottedTemplates,
                        HasJsonData = kvp.Value.HasJsonData,
                        HasJsonPlaceholders = kvp.Value.HasJsonPlaceholders,
                        HasReplacementMappings = kvp.Value.HasReplacementMappings,
                        RequiresProcessing = kvp.Value.RequiresProcessing
                    }
                );

                var serverEnd = DateTime.UtcNow;
                var serverTimeMs = (serverEnd - serverStart).TotalMilliseconds;

                // Use named response class for NativeAOT compatibility
                var response = new TemplateApiResponse
                {
                    Templates = normalResult,
                    PreProcessTemplates = preprocessResult,
                    AppSite = appSite,
                    AppFile = appFile,
                    AppView = appView,
                    ServerTimeMs = serverTimeMs
                };

                var jsonResult = response.SerializeToJson();

                /*              
                try
                {
                    //var jsonResultOld = Arshu.App.Json.JsonConverter.SerializeObjectForWeb(response);
                    var oldDoc = System.Text.Json.JsonDocument.Parse(jsonResultOld);
                    var newDoc = System.Text.Json.JsonDocument.Parse(jsonResult);
                    CompareJsonStructureRecursive("ROOT", oldDoc.RootElement, newDoc.RootElement);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // Log JSON parsing errors for debugging
                    Console.WriteLine($"[JSON Parse Error] {ex.Message}");
                    Console.WriteLine($"JsonConverter result length: {jsonResultOld.Length}");
                    Console.WriteLine($"Custom serialization result length: {jsonResult.Length}");
                    Console.WriteLine($"First 500 chars of custom serialization: {jsonResult.Substring(0, Math.Min(2000, jsonResult.Length))}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Other Error] {ex.Message}");
                }
                 */

                return Results.Content(jsonResult, "application/json");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        #endregion

        app.Run();
    }
}

#region Idle Tracking Middleware

public class IdleTrackingMiddleware
{
    private static DateTime _lastRequest = DateTime.UtcNow;
    private static Timer? _timer;
    private static bool _shutdownInitiated = false;
    private static int _idleSeconds = 10; 
    private static object _lock = new object();
    private static ILogger? _logger;
    public static void SetLogger(ILogger logger)
    {
        _logger = logger;
    }
    private static void Log(string message)
    {
        _logger?.LogInformation($"[IdleTrackingMiddleware] {DateTime.UtcNow:O} {message}");
    }

    public static void Configure(int idleSeconds)
    {
    _idleSeconds = idleSeconds;
    Log($"Configured idleSeconds = {_idleSeconds}");
    }

    public static void StartTimer(IHostApplicationLifetime appLifetime)
    {
    Log("Starting idle timer");
    _timer = new Timer(_ => CheckIdle(appLifetime), null, 10000, 10000);
    }

    public static void UpdateLastRequest()
    {
        lock (_lock)
        {
            _lastRequest = DateTime.UtcNow;
            Log("Last request time updated");
        }
    }

    private static void CheckIdle(IHostApplicationLifetime appLifetime)
    {
        if (_shutdownInitiated) return;
        var idleTime = DateTime.UtcNow - _lastRequest;
        Log($"Idle check: idleTime={idleTime.TotalSeconds}s, idleSeconds={_idleSeconds}");
        if (idleTime.TotalSeconds > _idleSeconds)
        {
            Log("Idle period exceeded, shutting down application");
            _shutdownInitiated = true;
            appLifetime.StopApplication();
        }
    }

    public static async Task InvokeAsync(HttpContext context, RequestDelegate next, IHostApplicationLifetime appLifetime)
    {
        UpdateLastRequest();
        if (_timer == null)
        {
            StartTimer(appLifetime);
        }
        await next(context);
    }
}

#endregion
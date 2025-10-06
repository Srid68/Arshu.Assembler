using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Assembler.TemplateApi;
using Assembler.TemplateLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace AssemblerWebJs
{
    #region Models/Serialization Config

    public class ScenarioDto
    {
        public string AppSite { get; set; } = string.Empty;
        public string AppFile { get; set; } = string.Empty;
        public string AppView { get; set; } = string.Empty;
        public string AppViewPrefix { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
    }

    [JsonSerializable(typeof(ScenarioDto))]
    [JsonSerializable(typeof(List<ScenarioDto>))]
    [JsonSerializable(typeof(ScenarioDto[]))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(object))]
    public partial class SimpleJsonContext : JsonSerializerContext
    {
    }

    #endregion

    public static class AssemblerEndpoint
    {
        public static void MapAssemblerEndpoints(this WebApplication app)
        {
            #region Root Endpoint

            // GET endpoint for Index AppSite
            app.MapGet("/", (HttpContext context) =>
            {
                try
                {
                    string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                    // Get engine type from query parameter (default to Normal)
                    var engineType = context.Request.Query["engine"].ToString();
                    if (string.IsNullOrEmpty(engineType))
                    {
                        engineType = "Normal";
                    }

                    // Validate EngineType against allowlist
                    if (!SecurityValidator.ValidEngineTypes.Contains(engineType))
                        return Results.Text("Invalid engine type. Use 'Normal' or 'PreProcess'", statusCode: 400);

                    // Load templates for Index AppSite
                    var normalTemplatesRaw = LoaderNormal.LoadGetTemplateFiles(rootDirPath, "Index");
                    var preprocessTemplatesRaw = LoaderPreProcess.LoadProcessGetTemplateFiles(rootDirPath, "Index");

                    // Convert Normal templates to TemplateData objects
                    var normalResult = normalTemplatesRaw.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new TemplateData { Html = kvp.Value.html, Json = kvp.Value.json }
                    );

                    // Convert PreProcess templates to metadata objects
                    var preprocessResult = preprocessTemplatesRaw.Templates.ToDictionary(
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

                    // Prepare response for client-side rendering
                    var response = new ApiResponse
                    {
                        Templates = normalResult,
                        PreProcessTemplates = preprocessResult,
                        AppSite = "Index",
                        AppFile = "Index",
                        AppView = null,
                        ServerTimeMs = 0
                    };

                    var jsonResult = response.SerializeToJson();

                    // Merge using selected engine (no AppView context for Index)
                    string html = "";
                    if (engineType.Equals("PreProcess", StringComparison.OrdinalIgnoreCase))
                    {
                        var engine = new Assembler.TemplateEngine.EnginePreProcess();
                        html = engine.MergeTemplates("Index", "Index", null, preprocessTemplatesRaw.Templates);
                    }
                    else
                    {
                        var engine = new Assembler.TemplateEngine.EngineNormal();
                        html = engine.MergeTemplates("Index", "Index", null, normalTemplatesRaw);
                    }

                    // Note: No need to inject __INITIAL_TEMPLATES__ for Index page
                    // The client-side JavaScript will fetch templates via /api/templates endpoint

                    return Results.Content(html, "text/html");
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
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
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Get Templates Endpoint

            app.MapGet("/api/templates/{appSite}", (HttpContext context, string appSite, string? appFile = null, string? appView = null) =>
            {
                try
                {
                    string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                    // Validate AppSite against allowlist loaded from appsites.csv
                    var validAppSites = SecurityValidator.GetValidAppSites(rootDirPath);
                    if (!validAppSites.Contains(appSite))
                        return Results.Text("Invalid AppSite value", statusCode: 400);

                    // Validate path components for path traversal attacks
                    if (!SecurityValidator.IsValidPathComponent(appSite))
                        return Results.Text("Invalid characters in AppSite", statusCode: 400);

                    if (!string.IsNullOrEmpty(appFile) && !SecurityValidator.IsValidPathComponent(appFile))
                        return Results.Text("Invalid characters in AppFile", statusCode: 400);

                    if (!string.IsNullOrEmpty(appView) && !SecurityValidator.IsValidPathComponent(appView))
                        return Results.Text("Invalid characters in AppView", statusCode: 400);

                    var serverStart = DateTime.UtcNow;

                    // TEMPORARY: Clear cache for development
                    LoaderNormal.ClearCache();
                    LoaderPreProcess.ClearCache();

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
                    var response = new ApiResponse
                    {
                        Templates = normalResult,
                        PreProcessTemplates = preprocessResult,
                        AppSite = appSite,
                        AppFile = appFile,
                        AppView = appView,
                        ServerTimeMs = serverTimeMs
                    };

                    var jsonResult = response.SerializeToJson();

                    return Results.Content(jsonResult, "application/json");
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion
        }
    }
}

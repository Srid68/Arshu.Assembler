using Assembler.Api;
using Assembler.Common;
using Assembler.Config;
using Assembler.Engine;
using Assembler.Loader;
using Assembler.Performance;
using Assembler.Test;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssemblerWebJs
{
    #region Model and Serialization

    // Model for scenario response
    public class ScenarioDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("appSite")]
        public string AppSite { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("appFile")]
        public string AppFile { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("appView")]
        public string AppView { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    // Model for merge request
    public class MergeRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("appSite")]
        public string? AppSite { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("appView")]
        public string? AppView { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("engineType")]
        public string? EngineType { get; set; }
    }
    
    [JsonSerializable(typeof(ScenarioDto[]))]
    [JsonSerializable(typeof(MergeRequest))]
    public partial class AssemblerJsonContext : JsonSerializerContext { }

    #endregion

    public static class AssemblerEndpoint
    {
        public static void MapAssemblerEndpoints(this WebApplication app)
        // POST endpoint for merging templates
        {
            var assemblerGroup = app.MapGroup("")
                .WithTags("Assembler");

            #region Root Endpoint

            assemblerGroup.MapGet("/", (HttpContext context) =>
            {
                // Use Index AppSite with engine toggle parameter
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                // Get engine type from query parameter (default to Normal)
                var engineType = context.Request.Query["engine"].ToString();
                if (string.IsNullOrEmpty(engineType))
                {
                    engineType = "Normal";
                }

                // Validate EngineType against allowlist
                if (!SecurityValidator.ValidEngineTypes.Contains(engineType))
                    return Results.BadRequest("Invalid engine type. Use 'Normal' or 'PreProcess'");

                // Load templates for Index AppSite
                var normalTemplatesRaw = LoaderNormal.LoadGetTemplateFiles(rootDirPath, "Index");
                var preprocessTemplatesRaw = LoaderPreProcess.LoadProcessGetTemplateFiles(rootDirPath, "Index");

                // Merge using selected engine (no AppView context for Index)
                string mergedHtml = "";
                if (engineType.Equals("PreProcess", StringComparison.OrdinalIgnoreCase))
                {
                    var engine = new EnginePreProcess();
                    mergedHtml = engine.MergeTemplates("Index", "Index", null, preprocessTemplatesRaw.Templates);
                }
                else
                {
                    var engine = new EngineNormal();
                    mergedHtml = engine.MergeTemplates("Index", "Index", null, normalTemplatesRaw);
                }

                return Results.Content(mergedHtml, "text/html");
            })
            .WithName("GetRootUrl")
            .WithDisplayName("Get Method to Test Merging with Index AppSite")
            .WithDescription("Get Method to Test Merging with Index AppSite - use ?engine=Normal or ?engine=PreProcess")
            .WithTags("Root");

            #endregion

            #region Get Scenarios Endpoint

            assemblerGroup.MapGet("/api/scenarios", (HttpContext context) =>
            {
                try
                {
                    var scenarios = ConfigUtil.GetScenarios();
                    var scenarioDtos = scenarios.Select(s => new ScenarioDto
                    {
                        AppSite = s.AppSite,
                        AppFile = s.AppFile,
                        AppView = s.AppView,
                        DisplayName = s.DisplayName,
                        Description = s.Description
                    }).ToArray();

                    return Results.Json(scenarioDtos, AssemblerJsonContext.Default.ScenarioDtoArray);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error loading scenarios: {ex.Message}");
                }
            })
            .WithName("GetScenarios")
            .WithDisplayName("Get Scenarios")
            .WithDescription("Returns all available scenarios from scenarios.csv")
            .WithTags("Scenarios");

            #endregion

            #region Merge Endpoint

            assemblerGroup.MapPost("/merge", async (HttpContext context) =>
            {
                var serverStart = DateTime.UtcNow;

                // Enable logging for merge operations
                var originalLogLevel = Logger.GetLogLevel();
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                var templateAnalysisDir = Path.Combine(projectDirectory, "template_analysis");
                var logsDir = Path.Combine(templateAnalysisDir, "logs");
                Directory.CreateDirectory(logsDir);

                var contextLogFiles = new Dictionary<string, string>
                {
                    { "LoaderNormal", Path.Combine(logsDir, "csharp_loadernormal.log") },
                    { "LoaderPreProcess", Path.Combine(logsDir, "csharp_loaderpreprocess.log") },
                    { "EngineNormal", Path.Combine(logsDir, "csharp_enginenormal.log") },
                    { "EnginePreProcess", Path.Combine(logsDir, "csharp_enginepreprocess.log") }
                };

                Logger.Configure(Logger.LogLevel.DEBUG, null, false);
                Logger.ConfigureContextLogFiles(contextLogFiles);

                try
                {
                    using var reader = new StreamReader(context.Request.Body);
                    var body = await reader.ReadToEndAsync();
                    if (string.IsNullOrWhiteSpace(body))
                        return Results.BadRequest("Empty request body");

                    var input = System.Text.Json.JsonSerializer.Deserialize<MergeRequest>(body, AssemblerJsonContext.Default.MergeRequest);
                    if (input == null || string.IsNullOrWhiteSpace(input.AppSite) || string.IsNullOrWhiteSpace(input.EngineType))
                        return Results.BadRequest("Missing required fields: AppSite, EngineType");

                    string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                    // Validate AppSite against allowlist loaded from appsites.csv
                    var validAppSites = SecurityValidator.GetValidAppSites();
                    if (!validAppSites.Contains(input.AppSite))
                        return Results.BadRequest("Invalid AppSite value");

                    // Validate EngineType against allowlist
                    if (!SecurityValidator.ValidEngineTypes.Contains(input.EngineType))
                        return Results.BadRequest("Invalid EngineType value");

                    // Validate path components for path traversal attacks
                    if (!SecurityValidator.IsValidPathComponent(input.AppSite))
                        return Results.BadRequest("Invalid characters in AppSite");

                    if (!string.IsNullOrEmpty(input.AppView) && !SecurityValidator.IsValidPathComponent(input.AppView))
                        return Results.BadRequest("Invalid characters in AppView");

                    // Get AppFile from scenarios
                    var scenarios = ConfigUtil.GetScenarios();
                    var appView = input.AppView ?? "";
                    var matchingScenario = scenarios.FirstOrDefault(s =>
                        s.AppSite.Equals(input.AppSite, StringComparison.OrdinalIgnoreCase) &&
                        s.AppView.Equals(appView, StringComparison.OrdinalIgnoreCase));

                    if (matchingScenario == null)
                        return Results.BadRequest($"No matching scenario found for AppSite='{input.AppSite}' and AppView='{appView}'");

                    var appFile = matchingScenario.AppFile;
                    var appViewPrefix = string.IsNullOrEmpty(appView) ? "" : appFile;

                    var normalTemplatesRaw = LoaderNormal.LoadGetTemplateFiles(rootDirPath, input.AppSite);
                    var preprocessTemplatesRaw = LoaderPreProcess.LoadProcessGetTemplateFiles(rootDirPath, input.AppSite);

                    var normalResult = normalTemplatesRaw.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new TemplateData { Html = kvp.Value.html, Json = kvp.Value.json }
                    );

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

                    var engineStart = DateTime.UtcNow;
                    string mergedHtml = "";
                    if (input.EngineType.Equals("PreProcess", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var engine = new EnginePreProcess();
                        if (!string.IsNullOrEmpty(appViewPrefix))
                            engine.AppViewPrefix = appViewPrefix;
                        mergedHtml = engine.MergeTemplates(input.AppSite, appFile, appView, preprocessTemplatesRaw.Templates);
                    }
                    else
                    {
                        var engine = new EngineNormal();
                        if (!string.IsNullOrEmpty(appViewPrefix))
                            engine.AppViewPrefix = appViewPrefix;
                        mergedHtml = engine.MergeTemplates(input.AppSite, appFile, appView, normalTemplatesRaw);
                    }
                    var engineTimeMs = (DateTime.UtcNow - engineStart).TotalMilliseconds;
                    var serverEnd = DateTime.UtcNow;
                    var serverTimeMs = (serverEnd - serverStart).TotalMilliseconds;

                    // Save HTML output only if save query parameter is present
                    var saveParam = context.Request.Query["save"].ToString();
                    if (!string.IsNullOrEmpty(saveParam) && saveParam.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        var contentRoot = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                        var outputDir = Path.Combine(contentRoot, "template_analysis", "output");
                        Directory.CreateDirectory(outputDir);

                        var appViewSuffix = string.IsNullOrEmpty(input.AppView) ? "" : $"_{input.AppView}";
                        var engineSuffix = input.EngineType.ToLower();
                        var outputFile = Path.Combine(outputDir, $"{input.AppSite}{appViewSuffix}_{engineSuffix}.html");
                        File.WriteAllText(outputFile, mergedHtml);

                        // Also generate the structure dump file, filtered by AppSite
                        var allScenarios = ConfigUtil.GetScenarios();
                        var filteredScenarios = ConfigUtil.FilterByAppSite(allScenarios, input.AppSite);
                        TestingUtils.DumpPreprocessedTemplateStructures(rootDirPath, contentRoot, filteredScenarios, true);
                    }

                    var responseObj = new ApiResponse
                    {
                        Templates = normalResult,
                        PreProcessTemplates = preprocessResult,
                        AppSite = input.AppSite ?? string.Empty,
                        AppFile = appFile,
                        AppView = appView,
                        ServerTimeMs = serverTimeMs,
                        Html = mergedHtml,
                        EngineTimeMs = engineTimeMs
                    };
                    var responseJson = responseObj.SerializeToJson();

                    return Results.Content(responseJson, "application/json");
                }
                finally
                {
                    // Restore original log level
                    Logger.SetLogLevel(originalLogLevel);
                }
            })
            .Accepts<MergeRequest>("application/json")
            .WithName("PostMergeTemplate")
            .WithDisplayName("Post Method to Merge Template for AppSite, AppView, EngineType")
            .WithDescription("Post Method to Merge Template for AppSite, AppView, EngineType. AppFile is retrieved from scenarios.")
            .WithTags("Merge");

            #endregion

            #region Get Templates Endpoint

            assemblerGroup.MapPost("/api/templates", async (HttpContext context) =>
            {
                var serverStart = DateTime.UtcNow;

                // Enable logging for merge operations
                var originalLogLevel = Logger.GetLogLevel();
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                var templateAnalysisDir = Path.Combine(projectDirectory, "template_analysis");
                var logsDir = Path.Combine(templateAnalysisDir, "logs");
                Directory.CreateDirectory(logsDir);

                var contextLogFiles = new Dictionary<string, string>
                    {
                        { "LoaderNormal", Path.Combine(logsDir, "csharp_loadernormal.log") },
                        { "LoaderPreProcess", Path.Combine(logsDir, "csharp_loaderpreprocess.log") }
                    };

                Logger.Configure(Logger.LogLevel.DEBUG, null, false);
                Logger.ConfigureContextLogFiles(contextLogFiles);

                try
                {
                    string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                    // Read the request body
                    using var reader = new StreamReader(context.Request.Body);
                    var requestBody = await reader.ReadToEndAsync();

                    // Parse JSON manually for NativeAOT compatibility
                    var appSite = "";
                    try
                    {
                        var jsonDoc = System.Text.Json.JsonDocument.Parse(requestBody);
                        if (jsonDoc.RootElement.TryGetProperty("appsite", out var appSiteElement))
                        {
                            appSite = appSiteElement.GetString() ?? "";
                        }
                    }
                    catch
                    {
                        return Results.Text("Invalid JSON format", statusCode: 400);
                    }

                    if (string.IsNullOrEmpty(appSite))
                        return Results.Text("Missing appsite parameter", statusCode: 400);

                    // Validate AppSite against allowlist loaded from appsites.csv
                    var validAppSites = SecurityValidator.GetValidAppSites();
                    if (!validAppSites.Contains(appSite))
                        return Results.Text("Invalid AppSite value", statusCode: 400);

                    // Validate path components for path traversal attacks
                    if (!SecurityValidator.IsValidPathComponent(appSite))
                        return Results.Text("Invalid characters in AppSite", statusCode: 400);
                  
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
                        AppFile = null,
                        AppView = null,
                        ServerTimeMs = serverTimeMs
                    };

                    var jsonResult = response.SerializeToJson();

                    // Check if save query parameter is present
                    var saveParam = context.Request.Query["save"].ToString();
                    if (!string.IsNullOrEmpty(saveParam) && saveParam.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        var templatesDir = Path.Combine(projectDirectory, "template_analysis", "templates");
                        Directory.CreateDirectory(templatesDir);

                        var saveFile = Path.Combine(templatesDir, $"csharp_{appSite}_templates.json");
                        await File.WriteAllTextAsync(saveFile, jsonResult);

                        // Save structure dump using TestingUtils logic
                        // Build a scenario list for the requested appSite
                        var scenarios = new List<Assembler.Config.Scenario> {
                            new Assembler.Config.Scenario(appSite, "", "")
                        };
                        Assembler.Test.TestingUtils.DumpPreprocessedTemplateStructures(rootDirPath, projectDirectory, scenarios, true);
                    }

                    return Results.Content(jsonResult, "application/json");
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
                finally
                {
                    // Restore original log level
                    Logger.SetLogLevel(originalLogLevel);
                }
            });

            #endregion
        }
    }
}
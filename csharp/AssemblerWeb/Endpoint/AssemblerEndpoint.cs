using Assembler.Api;
using Assembler.Common;
using Assembler.Config;
using Assembler.Engine;
using Assembler.Loader;
using Assembler.Test;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace AssemblerWeb
{
    
    #region Model and Serialization

    // Model for scenario response
    public class ScenarioDto
    {
        [JsonPropertyName("appSite")]
        public string AppSite { get; set; } = string.Empty;
        [JsonPropertyName("appFile")]
        public string AppFile { get; set; } = string.Empty;
        [JsonPropertyName("appView")]
        public string AppView { get; set; } = string.Empty;
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    // Model for merge request
    public class MergeRequest
    {
        [JsonPropertyName("appSite")]
        public string? AppSite { get; set; }
        [JsonPropertyName("appView")]
        public string? AppView { get; set; }
        [JsonPropertyName("engineType")]
        public string? EngineType { get; set; }
    }
    
    [JsonSerializable(typeof(ScenarioDto[]))]
    [JsonSerializable(typeof(MergeRequest))]
    public partial class AssemblerJsonContext : JsonSerializerContext { }

    #endregion

    public static class AssemblerEndpoint
    {
        #region Constants

        public const string DefaultAppSite = "Test";

        // Maximum parameter length to prevent DoS attacks
        private const int ParamMaxLength = 256;

        #endregion

        #region Validations

        // Valid engine types allowlist
        private static readonly HashSet<string> ValidEngineTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Normal", "PreProcess"
        };

        /// <summary>
        /// Gets the valid AppSites from TemplateConfig. Throws if not loaded.
        /// </summary>
        private static HashSet<string> GetValidAppSites()
        {
            return ConfigUtil.GetAppSites();
        }

        /// <summary>
        /// Validates if a path component is safe (no traversal, invalid chars, or excessive length)
        /// </summary>
        private static bool IsValidPathComponent(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Check parameter length to prevent DoS
            if (value.Length > ParamMaxLength)
                return false;

            // Check for path traversal attempts
            if (value.Contains("..") || value.Contains("/") || value.Contains("\\"))
                return false;

            // Check for other suspicious characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (value.Any(c => invalidChars.Contains(c)))
                return false;

            return true;
        }

        #endregion

        public static void MapAssemblerEndpoints(this WebApplication app)
        {
            var assemblerGroup = app.MapGroup("")
                .WithTags("Assembler");

            #region Root Endpoint

            assemblerGroup.MapGet("/", (HttpContext context) =>
            {
                // Get appsite from query parameter or use default
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                var requestedAppSite = context.Request.Query["appsite"].ToString();
                string appSite = DefaultAppSite;
                string appFile = "index";

                // If appsite query param is provided, validate it exists in scenarios
                if (!string.IsNullOrEmpty(requestedAppSite))
                {
                    // Validate AppSite against allowlist
                    var validAppSites = GetValidAppSites();
                    if (!validAppSites.Contains(requestedAppSite))
                        return Results.BadRequest("Invalid AppSite value");

                    // Validate path components for path traversal attacks
                    if (!IsValidPathComponent(requestedAppSite))
                        return Results.BadRequest("Invalid characters in AppSite");

                    // Get AppFile from scenarios
                    var scenarios = ConfigUtil.GetScenarios();
                    var matchingScenario = scenarios.FirstOrDefault(s =>
                        s.AppSite.Equals(requestedAppSite, StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrEmpty(s.AppView));

                    if (matchingScenario == null)
                        return Results.BadRequest($"No matching scenario found for AppSite='{requestedAppSite}' without AppView");

                    appSite = requestedAppSite;
                    appFile = matchingScenario.AppFile;
                }

                // Get engine type from query parameter (default to Normal)
                var engineType = context.Request.Query["engine"].ToString();
                if (string.IsNullOrEmpty(engineType))
                {
                    engineType = "Normal";
                }

                // Validate EngineType against allowlist
                if (!ValidEngineTypes.Contains(engineType))
                    return Results.BadRequest("Invalid engine type. Use 'Normal' or 'PreProcess'");

                // Load templates for selected AppSite
                var normalTemplatesRaw = LoaderNormal.LoadGetTemplateFiles(rootDirPath, appSite);
                var preprocessTemplatesRaw = LoaderPreProcess.LoadProcessGetTemplateFiles(rootDirPath, appSite);

                // Merge using selected engine (no AppView context)
                string mergedHtml = "";
                if (engineType.Equals("PreProcess", StringComparison.OrdinalIgnoreCase))
                {
                    var engine = new EnginePreProcess();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, null, preprocessTemplatesRaw.Templates);
                }
                else
                {
                    var engine = new EngineNormal();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, null, normalTemplatesRaw);
                }

                return Results.Content(mergedHtml, "text/html");
            })
            .WithName("GetRootUrl")
            .WithDisplayName("Get Method to Test Merging with AppSite")
            .WithDescription("Get Method to Test Merging - use ?appsite=AppSiteName&engine=Normal or ?engine=PreProcess")
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

                Logger.Configure(Logger.LogLevel.DEBUG, consoleOutput: false, Logger.LogRotation.HOURLY);
                Logger.AddContextLogFiles(contextLogFiles);

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
                    var validAppSites = GetValidAppSites();
                    if (!validAppSites.Contains(input.AppSite))
                        return Results.BadRequest("Invalid AppSite value");

                    // Validate EngineType against allowlist
                    if (!ValidEngineTypes.Contains(input.EngineType))
                        return Results.BadRequest("Invalid EngineType value");

                    // Validate path components for path traversal attacks
                    if (!IsValidPathComponent(input.AppSite))
                        return Results.BadRequest("Invalid characters in AppSite");

                    if (!string.IsNullOrEmpty(input.AppView) && !IsValidPathComponent(input.AppView))
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

                Logger.Configure(Logger.LogLevel.DEBUG, consoleOutput: false, Logger.LogRotation.HOURLY);
                Logger.AddContextLogFiles(contextLogFiles);

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
                    var validAppSites = GetValidAppSites();
                    if (!validAppSites.Contains(appSite))
                        return Results.Text("Invalid AppSite value", statusCode: 400);

                    // Validate path components for path traversal attacks
                    if (!IsValidPathComponent(appSite))
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
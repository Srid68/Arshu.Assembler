using Assembler.Api;
using Assembler.Common;
using Arshu.Common;
using Assembler.Config;
using Assembler.Engine;
using Assembler.Loader;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace AssemblerWebJs
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

        public const string DefaultEngineType = "Normal";
        public const string DefaultAppSite = "Main";
        public const string SearchAppSites = "Common, Language";

        // Maximum parameter length to prevent DoS attacks
        private const int ParamMaxLength = 256;

        #endregion

        #region Validations

        // Valid engine types allowlist
        private static readonly HashSet<string> ValidEngineTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Normal", "PreProcess", "NormalJson", "PreProcessJson"
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
                // Root endpoint loads the default AppSite (Home) using DefaultEngineType
                // For other AppSites, use /{appSite} or /{appSite}/{appView} endpoints
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                string appSite = DefaultAppSite;
                string appView = "";
                string engineType = DefaultEngineType;

                // Get AppFile from scenarios using ConfigUtil (same as merge endpoint)
                var appFile = ConfigUtil.GetAppFile(appSite, appView);

                // Get engine type from query parameter (default to DefaultEngineType)
                var engineQuery = context.Request.Query["engine"].ToString();
                if (!string.IsNullOrEmpty(engineQuery))
                {
                    engineType = engineQuery;
                }

                // Validate EngineType against allowlist
                if (!ValidEngineTypes.Contains(engineType))
                    return Results.BadRequest("Invalid engine type. Use 'Normal', 'PreProcess', 'NormalJson', or 'PreProcessJson'");

                // Merge using selected engine
                string mergedHtml = "";
                if (engineType.Equals("PreProcess", StringComparison.OrdinalIgnoreCase) || engineType.Equals("PreProcessJson", StringComparison.OrdinalIgnoreCase))
                {
                    var loader = new LoaderPreProcessJson(rootDirPath, appSite, SearchAppSites);
                    var engine = new EnginePreProcessJson();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, null, loader);
                }
                else // Normal or NormalJson
                {
                    var loader = new LoaderNormalJson(rootDirPath, appSite, SearchAppSites);
                    var engine = new EngineNormalJson();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, null, loader);
                }

                return Results.Content(mergedHtml, "text/html");
            })
            .WithName("GetRootUrl")
            .WithDisplayName("Get Method for Default Home Page")
            .WithDescription("Get Method for Default Home Page - loads the default AppSite using DefaultEngineType")
            .WithTags("Root");

            #endregion

            #region AppSite Navigation Endpoint

            assemblerGroup.MapGet("/{appSite}/{appView?}", (HttpContext context, string appSite, string? appView) =>
            {
                // Get root directory path
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                // Validate AppSite against allowlist
                var validAppSites = GetValidAppSites();
                if (!validAppSites.Contains(appSite))
                    return Results.BadRequest("Invalid AppSite value");

                // Validate path components for path traversal attacks
                if (!IsValidPathComponent(appSite))
                    return Results.BadRequest("Invalid characters in AppSite");

                if (!string.IsNullOrEmpty(appView) && !IsValidPathComponent(appView))
                    return Results.BadRequest("Invalid characters in AppView");

                // Get AppFile from scenarios using ConfigUtil
                var appViewValue = appView ?? "";
                var appFile = ConfigUtil.GetAppFile(appSite, appViewValue);

                // Get engine type from query parameter (default to Normal)
                var engineType = context.Request.Query["engine"].ToString();
                if (string.IsNullOrEmpty(engineType))
                {
                    engineType = DefaultEngineType;
                }

                // Validate EngineType against allowlist
                if (!ValidEngineTypes.Contains(engineType))
                    return Results.BadRequest("Invalid engine type. Use 'Normal', 'PreProcess', 'NormalJson', or 'PreProcessJson'");

                // Merge using selected engine with AppView context
                string mergedHtml = "";
                if (engineType.Equals("PreProcess", StringComparison.OrdinalIgnoreCase) || engineType.Equals("PreProcessJson", StringComparison.OrdinalIgnoreCase))
                {
                    var loader = new LoaderPreProcessJson(rootDirPath, appSite, SearchAppSites);
                    var engine = new EnginePreProcessJson();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, appViewValue, loader);
                }
                else // Normal or NormalJson
                {
                    var loader = new LoaderNormalJson(rootDirPath, appSite, SearchAppSites);
                    var engine = new EngineNormalJson();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, appViewValue, loader);
                }

                return Results.Content(mergedHtml, "text/html");
            })
            .WithName("GetAppSiteNavigation")
            .WithDisplayName("Get Method to Navigate to AppSite with Optional AppView")
            .WithDescription("Get Method to Navigate - use /{appSite} or /{appSite}/{appView} with optional ?engine=Normal or ?engine=PreProcess")
            .WithTags("Navigation");

            #endregion

            #region Merge Endpoint

            assemblerGroup.MapPost("/merge", async (HttpContext context) =>
            {
                var serverStart = DateTime.UtcNow;

                // Enable logging for merge operations
                var originalLogLevel = Logger.GetLogLevel();
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                var templateAnalysisDir = Path.Combine(projectDirectory, "Analysis");
                var logsDir = Path.Combine(templateAnalysisDir, "logs");
                Directory.CreateDirectory(logsDir);

                var contextLogFiles = new Dictionary<string, string>
                {
                    { "LoaderNormal", Path.Combine(logsDir, "csharp_loadernormal.log") },
                    { "LoaderPreProcess", Path.Combine(logsDir, "csharp_loaderpreprocess.log") },
                    { "EngineNormal", Path.Combine(logsDir, "csharp_enginenormal.log") },
                    { "EnginePreProcess", Path.Combine(logsDir, "csharp_enginepreprocess.log") },
                    { "LoaderNormalJson", Path.Combine(logsDir, "csharp_loadernormaljson.log") },
                    { "EngineNormalJson", Path.Combine(logsDir, "csharp_enginenormaljson.log") }
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

                    // Get AppFile from scenarios using ConfigUtil
                    var appView = input.AppView ?? "";
                    var appFile = ConfigUtil.GetAppFile(input.AppSite, appView);
                    var appViewPrefix = string.IsNullOrEmpty(appView) ? "" : appFile;

                    // Load templates using new JSON loaders for API response
                    var normalLoader = new LoaderNormalJson(rootDirPath, input.AppSite, SearchAppSites);
                    var preprocessLoader = new LoaderPreProcessJson(rootDirPath, input.AppSite, SearchAppSites);

                    var normalResult = new Dictionary<string, TemplateData>();
                    var normalTemplates = normalLoader.GetAllTemplatesForSerialization();
                    foreach (var kvp in normalTemplates)
                    {
                        normalResult[kvp.Key] = new TemplateData
                        {
                            Html = kvp.Value.html,
                            Json = kvp.Value.jsonString
                        };
                    }

                    var preprocessResult = new Dictionary<string, PreProcessTemplateMetadata>();
                    var preprocessTemplates = preprocessLoader.GetAllTemplatesForSerialization();
                    foreach (var kvp in preprocessTemplates)
                    {
                        var template = kvp.Value;
                        preprocessResult[kvp.Key] = new PreProcessTemplateMetadata
                        {
                            OriginalContent = template.OriginalContent,
                            Placeholders = template.Placeholders,
                            SlottedTemplates = template.SlottedTemplates,
                            JsonData = template.JsonData,
                            JsonPlaceholders = template.JsonPlaceholders,
                            ReplacementMappings = template.ReplacementMappings,
                            HasPlaceholders = template.HasPlaceholders,
                            HasSlottedTemplates = template.HasSlottedTemplates,
                            HasJsonData = template.HasJsonData,
                            HasJsonPlaceholders = template.HasJsonPlaceholders,
                            HasReplacementMappings = template.HasReplacementMappings,
                            RequiresProcessing = template.RequiresProcessing
                        };
                    }

                    var engineStart = DateTime.UtcNow;
                    string mergedHtml = "";
                    if (input.EngineType.Equals("PreProcess", System.StringComparison.OrdinalIgnoreCase) || input.EngineType.Equals("PreProcessJson", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var loader = new LoaderPreProcessJson(rootDirPath, input.AppSite, SearchAppSites);
                        var engine = new EnginePreProcessJson();
                        if (!string.IsNullOrEmpty(appViewPrefix))
                            engine.AppViewPrefix = appViewPrefix;
                        mergedHtml = engine.MergeTemplates(input.AppSite, appFile, appView, loader);
                    }
                    else // Normal or NormalJson
                    {
                        var loader = new LoaderNormalJson(rootDirPath, input.AppSite, SearchAppSites);
                        var engine = new EngineNormalJson();
                        if (!string.IsNullOrEmpty(appViewPrefix))
                            engine.AppViewPrefix = appViewPrefix;
                        mergedHtml = engine.MergeTemplates(input.AppSite, appFile, appView, loader);
                    }
                    var engineTimeMs = (DateTime.UtcNow - engineStart).TotalMilliseconds;
                    var serverEnd = DateTime.UtcNow;
                    var serverTimeMs = (serverEnd - serverStart).TotalMilliseconds;

                    // Save HTML output only if save query parameter is present
                    var saveParam = context.Request.Query["save"].ToString();
                    if (!string.IsNullOrEmpty(saveParam) && saveParam.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        var contentRoot = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                        var outputDir = Path.Combine(contentRoot, "Analysis", "output");
                        Directory.CreateDirectory(outputDir);

                        var appViewSuffix = string.IsNullOrEmpty(input.AppView) ? "" : $"_{input.AppView}";
                        var engineSuffix = input.EngineType.ToLower();
                        var outputFile = Path.Combine(outputDir, $"{input.AppSite}{appViewSuffix}_{engineSuffix}.html");
                        File.WriteAllText(outputFile, mergedHtml);
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

                var templateAnalysisDir = Path.Combine(projectDirectory, "Analysis");
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

                    // Load Normal templates using new JSON loader
                    var normalLoader = new LoaderNormalJson(rootDirPath, appSite, SearchAppSites);

                    // Load PreProcess templates using new JSON loader
                    var preprocessLoader = new LoaderPreProcessJson(rootDirPath, appSite, SearchAppSites);

                    // Convert Normal templates to TemplateData objects for proper JSON serialization
                    var normalResult = new Dictionary<string, TemplateData>();
                    var normalTemplates = normalLoader.GetAllTemplatesForSerialization();
                    foreach (var kvp in normalTemplates)
                    {
                        normalResult[kvp.Key] = new TemplateData
                        {
                            Html = kvp.Value.html,
                            Json = kvp.Value.jsonString
                        };
                    }

                    // Convert PreProcess templates to metadata-only objects
                    var preprocessResult = new Dictionary<string, PreProcessTemplateMetadata>();
                    var preprocessTemplates = preprocessLoader.GetAllTemplatesForSerialization();
                    foreach (var kvp in preprocessTemplates)
                    {
                        var template = kvp.Value;
                        preprocessResult[kvp.Key] = new PreProcessTemplateMetadata
                        {
                            OriginalContent = template.OriginalContent,
                            Placeholders = template.Placeholders,
                            SlottedTemplates = template.SlottedTemplates,
                            JsonData = template.JsonData,
                            JsonPlaceholders = template.JsonPlaceholders,
                            ReplacementMappings = template.ReplacementMappings,
                            HasPlaceholders = template.HasPlaceholders,
                            HasSlottedTemplates = template.HasSlottedTemplates,
                            HasJsonData = template.HasJsonData,
                            HasJsonPlaceholders = template.HasJsonPlaceholders,
                            HasReplacementMappings = template.HasReplacementMappings,
                            RequiresProcessing = template.RequiresProcessing
                        };
                    }

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
                        var templatesDir = Path.Combine(projectDirectory, "Analysis", "templates");
                        Directory.CreateDirectory(templatesDir);

                        var saveFile = Path.Combine(templatesDir, $"csharp_{appSite}_templates.json");
                        await File.WriteAllTextAsync(saveFile, jsonResult);
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
using Arshu.Common;
using Assembler.Api;
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
        public const string DefaultAppSite = "Main";
        public const string DefaultEngineType = "Normal";
        public const string SearchAppSites = "Main, Language";

        // Maximum parameter length to prevent DoS attacks
        private const int ParamMaxLength = 256;

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
            for (int i = 0; i < value.Length; i++)
            {
                for (int j = 0; j < invalidChars.Length; j++)
                {
                    if (value[i] == invalidChars[j])
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds the JSON response for /api/templates endpoint using pre-serialized template JSON
        /// </summary>
        private static string BuildTemplatesApiResponse(string normalTemplatesJson, string preprocessTemplatesJson, string appSite, double serverTimeMs)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            sb.Append("\"Templates\":");
            sb.Append(normalTemplatesJson);
            sb.Append(",\"PreProcessTemplates\":");
            sb.Append(preprocessTemplatesJson);
            sb.Append(",\"AppSite\":\"");
            sb.Append(EscapeJsonString(appSite));
            sb.Append("\",\"AppFile\":null,\"AppView\":null,\"ServerTimeMs\":");
            sb.Append(serverTimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Escapes a string for safe inclusion in JSON
        /// </summary>
        private static string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t")
                .Replace("<", "\\u003C")
                .Replace(">", "\\u003E")
                .Replace("&", "\\u0026")
                .Replace("'", "\\u0027")
                .Replace("+", "\\u002B");
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

                string engineType = DefaultEngineType;
                string appSite = DefaultAppSite;
                string appView = "";

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
                if (engineType.Equals("PreProcessJson", StringComparison.OrdinalIgnoreCase))
                {
                    var loader = new LoaderPreProcessJson(rootDirPath, appSite, SearchAppSites);
                    var engine = new EnginePreProcessJson();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, null, loader);
                }
                else if (engineType.Equals("PreProcess", StringComparison.OrdinalIgnoreCase))
                {
                    var loader = new LoaderPreProcess(rootDirPath, appSite, SearchAppSites);
                    var engine = new EnginePreProcess();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, null, loader);
                }
                else if (engineType.Equals("NormalJson", StringComparison.OrdinalIgnoreCase))
                {
                    var loader = new LoaderNormalJson(rootDirPath, appSite, SearchAppSites);
                    var engine = new EngineNormalJson();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, null, loader);
                }
                else // Normal
                {
                    var loader = new LoaderNormal(rootDirPath, appSite, SearchAppSites);
                    var engine = new EngineNormal();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, null, loader);
                }

                return Results.Content(mergedHtml, "text/html");
            })
            .WithName("GetRootUrl")
            .WithDisplayName("Get Method for Default Home Page")
            .WithDescription("Get Method for Default Home Page - loads the default AppSite using DefaultEngineType")
            .WithTags("Root");

            #endregion

            #region Navigation Endpoint

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
                if (engineType.Equals("PreProcessJson", StringComparison.OrdinalIgnoreCase))
                {
                    var loader = new LoaderPreProcessJson(rootDirPath, appSite, SearchAppSites);
                    var engine = new EnginePreProcessJson();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, appViewValue, loader);
                }
                else if (engineType.Equals("PreProcess", StringComparison.OrdinalIgnoreCase))
                {
                    var loader = new LoaderPreProcess(rootDirPath, appSite, SearchAppSites);
                    var engine = new EnginePreProcess();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, appViewValue, loader);
                }
                else if (engineType.Equals("NormalJson", StringComparison.OrdinalIgnoreCase))
                {
                    var loader = new LoaderNormalJson(rootDirPath, appSite, SearchAppSites);
                    var engine = new EngineNormalJson();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, appViewValue, loader);
                }
                else // Normal
                {
                    var loader = new LoaderNormal(rootDirPath, appSite, SearchAppSites);
                    var engine = new EngineNormal();
                    mergedHtml = engine.MergeTemplates(appSite, appFile, appViewValue, loader);
                }

                return Results.Content(mergedHtml, "text/html");
            })
            .WithName("GetAppSiteNavigation")
            .WithDisplayName("Get Method to Navigate to AppSite with Optional AppView")
            .WithDescription("Get Method to Navigate - use /{appSite} or /{appSite}/{appView} with optional ?engine=Normal, ?engine=NormalJson, ?engine=PreProcess, or ?engine=PreProcessJson")
            .WithTags("Navigation");

            #endregion

            #region Merge Endpoint

            assemblerGroup.MapPost("/merge", async (HttpContext context) =>
            {
                var serverStart = DateTime.UtcNow;

                // Enable logging for merge operations
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                var templateAnalysisDir = Path.Combine(projectDirectory, "Analysis");
                var logsDir = Path.Combine(templateAnalysisDir, "logs");
                Directory.CreateDirectory(logsDir);

                var contextLogFiles = new Dictionary<string, string>
                {
                    { "LoaderNormal", Path.Combine(logsDir, "csharp_loadernormal.log") },
                    { "LoaderPreProcess", Path.Combine(logsDir, "csharp_loaderpreprocess.log") },
                    { "LoaderNormalJson", Path.Combine(logsDir, "csharp_loadernormaljson.log") },
                    { "LoaderPreProcessJson", Path.Combine(logsDir, "csharp_loaderpreprocessjson.log") },
                    { "EngineNormal", Path.Combine(logsDir, "csharp_enginenormal.log") },
                    { "EnginePreProcess", Path.Combine(logsDir, "csharp_enginepreprocess.log") },
                    { "EngineNormalJson", Path.Combine(logsDir, "csharp_enginenormaljson.log") },
                    { "EnginePreProcessJson", Path.Combine(logsDir, "csharp_enginepreprocessjson.log") }
                };

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

                    var engineStart = DateTime.UtcNow;
                    string mergedHtml = "";
                    if (input.EngineType.Equals("PreProcessJson", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var loader = new LoaderPreProcessJson(rootDirPath, input.AppSite, SearchAppSites);
                        var engine = new EnginePreProcessJson();
                        if (!string.IsNullOrEmpty(appViewPrefix))
                            engine.AppViewPrefix = appViewPrefix;
                        mergedHtml = engine.MergeTemplates(input.AppSite, appFile, appView, loader);
                    }
                    else if (input.EngineType.Equals("PreProcess", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var loader = new LoaderPreProcess(rootDirPath, input.AppSite, SearchAppSites);
                        var engine = new EnginePreProcess();
                        if (!string.IsNullOrEmpty(appViewPrefix))
                            engine.AppViewPrefix = appViewPrefix;
                        mergedHtml = engine.MergeTemplates(input.AppSite, appFile, appView, loader);
                    }
                    else if (input.EngineType.Equals("NormalJson", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var loader = new LoaderNormalJson(rootDirPath, input.AppSite, SearchAppSites);
                        var engine = new EngineNormalJson();
                        if (!string.IsNullOrEmpty(appViewPrefix))
                            engine.AppViewPrefix = appViewPrefix;
                        mergedHtml = engine.MergeTemplates(input.AppSite, appFile, appView, loader);
                    }
                    else // Normal
                    {
                        var loader = new LoaderNormal(rootDirPath, input.AppSite, SearchAppSites);
                        var engine = new EngineNormal();
                        if (!string.IsNullOrEmpty(appViewPrefix))
                            engine.AppViewPrefix = appViewPrefix;
                        mergedHtml = engine.MergeTemplates(input.AppSite, appFile, appView, loader);
                    }
                    var engineTimeMs = (DateTime.UtcNow - engineStart).TotalMilliseconds;
                    var serverEnd = DateTime.UtcNow;
                    var serverTimeMs = (serverEnd - serverStart).TotalMilliseconds;

                    var responseObj = new ApiResponse
                    {
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
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
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

                // Enable logging for template operations
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                var templateAnalysisDir = Path.Combine(projectDirectory, "Analysis");
                var logsDir = Path.Combine(templateAnalysisDir, "logs");
                Directory.CreateDirectory(logsDir);

                var contextLogFiles = new Dictionary<string, string>
                {
                    { "LoaderNormalJson", Path.Combine(logsDir, "csharp_loadernormaljson.log") },
                    { "LoaderPreProcessJson", Path.Combine(logsDir, "csharp_loaderpreprocessjson.log") }
                };

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

                    // Get pre-serialized JSON from loaders
                    var normalTemplatesJson = normalLoader.GetAllTemplatesJson();
                    var preprocessTemplatesJson = preprocessLoader.GetAllTemplatesJson();

                    var serverEnd = DateTime.UtcNow;
                    var serverTimeMs = (serverEnd - serverStart).TotalMilliseconds;

                    // Build JSON response manually using pre-serialized template JSON
                    var jsonResult = BuildTemplatesApiResponse(normalTemplatesJson, preprocessTemplatesJson, appSite, serverTimeMs);

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
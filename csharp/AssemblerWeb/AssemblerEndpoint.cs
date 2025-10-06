using Assembler.TemplateApi;
using Assembler.TemplateEngine;
using Assembler.TemplateLoader;
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

    // Model for merge request
    public class MergeRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("appSite")]
        public string? AppSite { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("appView")]
        public string? AppView { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("appViewPrefix")]
        public string? AppViewPrefix { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("appFile")]
        public string? AppFile { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("engineType")]
        public string? EngineType { get; set; }
    }

   
    [JsonSerializable(typeof(MergeRequest))]
    public partial class ResponseJsonContext : JsonSerializerContext { }

    [JsonSerializable(typeof(MergeRequest))]
    public partial class MergeRequestJsonContext : JsonSerializerContext { }

    #endregion

    public static class AssemblerEndpoint
    {
        public static void MapAssemblerEndpoints(this WebApplication app)
        // POST endpoint for merging templates
        {
            var assemblerGroup = app.MapGroup("")
                .WithTags("Assembler");

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

                // TEMPORARY: Clear cache for development
                LoaderNormal.ClearCache();
                LoaderPreProcess.ClearCache();

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

            assemblerGroup.MapPost("/merge", async (HttpContext context) =>
            {
                var serverStart = DateTime.UtcNow;

                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body))
                    return Results.BadRequest("Empty request body");

                var input = System.Text.Json.JsonSerializer.Deserialize<MergeRequest>(body, MergeRequestJsonContext.Default.MergeRequest);
                if (input == null || string.IsNullOrWhiteSpace(input.AppSite) || string.IsNullOrWhiteSpace(input.AppFile) || string.IsNullOrWhiteSpace(input.EngineType))
                    return Results.BadRequest("Missing required fields: AppSite, AppFile, EngineType");

                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");

                // Validate AppSite against allowlist loaded from appsites.csv
                var validAppSites = SecurityValidator.GetValidAppSites(rootDirPath);
                if (!validAppSites.Contains(input.AppSite))
                    return Results.BadRequest("Invalid AppSite value");

                // Validate EngineType against allowlist
                if (!SecurityValidator.ValidEngineTypes.Contains(input.EngineType))
                    return Results.BadRequest("Invalid EngineType value");

                // Validate path components for path traversal attacks
                if (!SecurityValidator.IsValidPathComponent(input.AppSite))
                    return Results.BadRequest("Invalid characters in AppSite");

                if (!SecurityValidator.IsValidPathComponent(input.AppFile))
                    return Results.BadRequest("Invalid characters in AppFile");

                if (!string.IsNullOrEmpty(input.AppView) && !SecurityValidator.IsValidPathComponent(input.AppView))
                    return Results.BadRequest("Invalid characters in AppView");

                if (!string.IsNullOrEmpty(input.AppViewPrefix) && !SecurityValidator.IsValidPathComponent(input.AppViewPrefix))
                    return Results.BadRequest("Invalid characters in AppViewPrefix");

                // TEMPORARY: Clear cache for development - remove this for production
                LoaderNormal.ClearCache();
                LoaderPreProcess.ClearCache();

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
                    if (!string.IsNullOrEmpty(input.AppViewPrefix))
                        engine.AppViewPrefix = input.AppViewPrefix;
                    mergedHtml = engine.MergeTemplates(input.AppSite, input.AppFile, input.AppView, preprocessTemplatesRaw.Templates);
                }
                else
                {
                    var engine = new EngineNormal();
                    if (!string.IsNullOrEmpty(input.AppViewPrefix))
                        engine.AppViewPrefix = input.AppViewPrefix;
                    mergedHtml = engine.MergeTemplates(input.AppSite, input.AppFile, input.AppView, normalTemplatesRaw);
                }
                var engineTimeMs = (DateTime.UtcNow - engineStart).TotalMilliseconds;
                var serverEnd = DateTime.UtcNow;
                var serverTimeMs = (serverEnd - serverStart).TotalMilliseconds;

                var responseObj = new ApiResponse
                {
                    Templates = normalResult,
                    PreProcessTemplates = preprocessResult,
                    AppSite = input.AppSite ?? string.Empty,
                    AppFile = input.AppFile,
                    AppView = input.AppView,
                    ServerTimeMs = serverTimeMs,
                    Html = mergedHtml,
                    EngineTimeMs = engineTimeMs
                };
                var responseJson = responseObj.SerializeToJson();

                return Results.Content(responseJson, "application/json");
            })
            .Accepts<MergeRequest>("application/json")
            .WithName("PostMergeTemplate")
            .WithDisplayName("Post Method to Merge Template for AppSite, AppFile, EngineType")
            .WithDescription("Post Method to Merge Template for AppSite, AppFile, EngineType")
            .WithTags("Merge");

        }
    }
}
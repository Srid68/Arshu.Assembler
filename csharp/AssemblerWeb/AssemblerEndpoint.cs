using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using Assembler.TemplateCommon;
using Assembler.TemplateEngine;
using Assembler.TemplateLoader;
using Assembler.TemplateModel;
using System.Text.Json.Serialization;

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

public class MergeResponse
{
    public string html { get; set; } = string.Empty;
    public Timing timing { get; set; } = new Timing();
}

public class Timing
{
    public double serverTimeMs { get; set; }
    public double engineTimeMs { get; set; }
}

[JsonSerializable(typeof(MergeResponse))]
[JsonSerializable(typeof(Timing))]
[JsonSerializable(typeof(MergeRequest))]
public partial class ResponseJsonContext : JsonSerializerContext { }

[JsonSerializable(typeof(MergeRequest))]
[JsonSerializable(typeof(MergeResponse))]
[JsonSerializable(typeof(Timing))]
public partial class MergeRequestJsonContext : JsonSerializerContext { }

#endregion

public static class AssemblerEndpoint
{
    public static void MapAssemblerEndpoints(this WebApplication app)
        // POST endpoint for merging templates
    {
        var assemblerGroup = app.MapGroup("")
            .WithTags("Assembler");

        assemblerGroup.MapGet("/", async (HttpContext context) =>
        {
            // Get all test folders in wwwroot/AppSites
            string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
            string appSitesPath = Path.Combine(rootDirPath, "AppSites");

            var testDirs = Directory.GetDirectories(appSitesPath)
                .Select(dir => Path.GetFileName(dir))
                .Where(dirName => !dirName.Equals("roottemplate.html", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name)
                .ToList();

            // Build options for select tag with uniform 4-value format
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
                    // Dynamically set AppViewPrefix from root file name (htmlFile)
                    string appViewPrefix = htmlFile;
                    if (!string.IsNullOrEmpty(appViewPrefix))
                    {
                        appViewPrefix = appViewPrefix.Substring(0, Math.Min(appViewPrefix.Length, 6));
                    }
                    // Always add the default option first (no AppView)
                    var optionValue = $"{testDir},{htmlFile},,{appViewPrefix}";
                    var optionText = $"{testDir} - {htmlFile}";
                    optionsList.Add($"<option value=\"{optionValue}\">{optionText}</option>");
                }

                if (hasViews)
                {
                    var viewFiles = Directory.GetFiles(viewsPath, "*.html")
                        .Select(file => Path.GetFileNameWithoutExtension(file))
                        .OrderBy(name => name)
                        .ToList();

                    // Collect all possible AppView values from viewFiles
                    var appViewValues = viewFiles
                        .Select(vf => {
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

                    // For each root HTML file, generate AppView test scenarios dynamically
                    foreach (var rootFile in htmlFiles)
                    {
                        // Dynamically set AppViewPrefix from root file name (rootFile)
                        string rootAppViewPrefix = rootFile;
                        if (!string.IsNullOrEmpty(rootAppViewPrefix))
                        {
                            rootAppViewPrefix = rootAppViewPrefix.Substring(0, Math.Min(rootAppViewPrefix.Length, 6));
                        }
                        // Check if this root file has corresponding view files
                        var matchingViewPrefix = viewFiles
                            .Select(vf => {
                                var idx = vf.ToLowerInvariant().IndexOf("content");
                                return idx > 0 ? vf.Substring(0, idx) : "";
                            })
                            .FirstOrDefault(prefix => !string.IsNullOrEmpty(prefix) && 
                                rootFile.ToLowerInvariant().StartsWith(prefix.ToLowerInvariant()));

                        if (!string.IsNullOrEmpty(matchingViewPrefix))
                        {
                            // Generate AppView scenarios for ALL available AppViews dynamically
                            foreach (var appView in appViewValues)
                            {
                                if (!string.IsNullOrEmpty(appView))
                                {
                                    var appViewPrefix = rootAppViewPrefix;
                                    var optionValueAppView = $"{testDir},{rootFile},{appView},{appViewPrefix}";
                                    var optionTextAppView = $"{testDir} - {rootFile} (AppView: {appView})";
                                    optionsList.Add($"<option value=\"{optionValueAppView}\">{optionTextAppView}</option>");
                                }
                            }
                        }
                    }
                }
            }
            
            string options = string.Join("\n        ", optionsList);

            // Read roottemplate.html and replace the options marker
            var templatePath = Path.Combine(appSitesPath, "roottemplate.html");
            string html = await File.ReadAllTextAsync(templatePath);
            html = html.Replace("<!--OPTIONS-->", options);
            return Results.Content(html, "text/html");
        })
        .WithName("GetRootUrl")
        .WithDisplayName("Get Method to Test Merging")
        .WithDescription("Get Method to Test Merging")
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
            string mergedHtml = "";
            double engineTimeMs = 0;
            var engineStart = DateTime.UtcNow;
            if (input.EngineType.Equals("PreProcess", System.StringComparison.OrdinalIgnoreCase))
            {
                var templates = LoaderPreProcess.LoadProcessGetTemplateFiles(rootDirPath, input.AppSite);
                var engine = new EnginePreProcess();
                if (!string.IsNullOrEmpty(input.AppViewPrefix))
                    engine.AppViewPrefix = input.AppViewPrefix;
                mergedHtml = engine.MergeTemplates(input.AppSite, input.AppFile, input.AppView, templates.Templates);
            }
            else
            {
                var templates = LoaderNormal.LoadGetTemplateFiles(rootDirPath, input.AppSite);
                var engine = new EngineNormal();
                if (!string.IsNullOrEmpty(input.AppViewPrefix))
                    engine.AppViewPrefix = input.AppViewPrefix;
                mergedHtml = engine.MergeTemplates(input.AppSite, input.AppFile, input.AppView, templates);
            }
            engineTimeMs = (DateTime.UtcNow - engineStart).TotalMilliseconds;
            var serverEnd = DateTime.UtcNow;
            var serverTimeMs = (serverEnd - serverStart).TotalMilliseconds;
            var responseObj = new MergeResponse {
                html = mergedHtml,
                timing = new Timing {
                    serverTimeMs = serverTimeMs,
                    engineTimeMs = engineTimeMs
                }
            };
            return Results.Json(responseObj, ResponseJsonContext.Default.MergeResponse);
        })
        .Accepts<MergeRequest>("application/json")
        .WithName("PostMergeTemplate")
        .WithDisplayName("Post Method to Merge Template for AppSite, AppFile, EngineType")
        .WithDescription("Post Method to Merge Template for AppSite, AppFile, EngineType")
        .WithTags("Merge");
    }
}

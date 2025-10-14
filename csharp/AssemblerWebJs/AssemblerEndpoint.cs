using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Assembler.Api;
using Assembler.Config;
using Assembler.Loader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using Assembler.Engine;

namespace AssemblerWebJs
{
    #region Models/Serialization Config

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

    public class TestSummaryRowDto
    {
        public string AppSite { get; set; } = string.Empty;
        public string AppFile { get; set; } = string.Empty;
        public string AppView { get; set; } = string.Empty;
        public string NormalPreProcess { get; set; } = string.Empty;
        public string CrossViewUnMatch { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public class PerfSummaryRowDto
    {
        public string AppSite { get; set; } = string.Empty;
        public string AppFile { get; set; } = string.Empty;
        public string AppView { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public double NormalTimeMs { get; set; }
        public double PreProcessTimeMs { get; set; }
        public int OutputSize { get; set; }
        public string ResultsMatch { get; set; } = string.Empty;
        public string PerfDifference { get; set; } = string.Empty;
    }

    public class TestResponse
    {
        public string Message { get; set; } = string.Empty;
    }

    public class ReportRequestDto
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;
        [JsonPropertyName("useLangPrefix")]
        public bool UseLangPrefix { get; set; }
    }

    [JsonSerializable(typeof(ScenarioDto))]
    [JsonSerializable(typeof(List<ScenarioDto>))]
    [JsonSerializable(typeof(ScenarioDto[]))]
    [JsonSerializable(typeof(TestSummaryRowDto))]
    [JsonSerializable(typeof(List<TestSummaryRowDto>))]
    [JsonSerializable(typeof(TestSummaryRowDto[]))]
    [JsonSerializable(typeof(PerfSummaryRowDto))]
    [JsonSerializable(typeof(List<PerfSummaryRowDto>))]
    [JsonSerializable(typeof(PerfSummaryRowDto[]))]
    [JsonSerializable(typeof(TestResponse))]
    [JsonSerializable(typeof(ReportRequestDto))]
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
                        var engine = new EnginePreProcess();
                        html = engine.MergeTemplates("Index", "Index", null, preprocessTemplatesRaw.Templates);
                    }
                    else
                    {
                        var engine = new EngineNormal();
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
                    var scenarios = ConfigUtil.GetScenarios();
                    var scenarioDtos = scenarios.Select(s => new ScenarioDto
                    {
                        AppSite = s.AppSite,
                        AppFile = s.AppFile,
                        AppView = s.AppView,
                        DisplayName = s.DisplayName,
                        Description = s.Description
                    }).ToArray();

                    return Results.Json(scenarioDtos, SimpleJsonContext.Default.ScenarioDtoArray);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in /api/scenarios: {ex.Message}");
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Get Templates Endpoint

            app.MapPost("/api/templates", async (HttpContext context) =>
            {
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

                    var serverStart = DateTime.UtcNow;

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

                    return Results.Content(jsonResult, "application/json");
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Save Test Results Endpoint

            app.MapPost("/api/test-results", async (HttpContext context, TestSummaryRowDto[] summaryRows) =>
            {
                try
                {
                    Console.WriteLine($"POST /api/test-results called with {summaryRows.Length} rows");

                    // Validate each row
                    var validAppSites = SecurityValidator.GetValidAppSites();
                    foreach (var row in summaryRows)
                    {
                        // Validate AppSite is in allowlist
                        if (!string.IsNullOrEmpty(row.AppSite) && !validAppSites.Contains(row.AppSite))
                            return Results.Text($"Invalid AppSite: {row.AppSite}", statusCode: 400);

                        // Validate parameter lengths (256 char limit)
                        if (!SecurityValidator.IsValidPathComponent(row.AppSite))
                            return Results.Text("Invalid AppSite parameter", statusCode: 400);
                        if (!SecurityValidator.IsValidPathComponent(row.AppFile))
                            return Results.Text("Invalid AppFile parameter", statusCode: 400);
                        if (!string.IsNullOrEmpty(row.AppView) && !SecurityValidator.IsValidPathComponent(row.AppView))
                            return Results.Text("Invalid AppView parameter", statusCode: 400);
                    }

                    string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                    string reportsPath = Path.Combine(projectDirectory, "template_analysis", "Reports");

                    // Create Reports directory if it doesn't exist
                    Directory.CreateDirectory(reportsPath);

                    var testType = context.Request.Query["testType"].ToString();
                    if (string.IsNullOrEmpty(testType))
                        testType = "standardtest";

                    var testTypeFile = testType.ToLower().Replace(" ", "").Replace("-", "");

                    // Generate HTML table matching C# standard format
                    var html = new System.Text.StringBuilder();
                    // Format testType to match C# style (e.g., "advancedtest" -> "ADVANCED TEST")
                    var formattedTestType = testType.Replace("test", " TEST").ToUpper();

                    html.AppendLine("<!DOCTYPE html>");
                    html.AppendLine("<html>");
                    html.AppendLine("<head>");
                    html.AppendLine("    <meta charset=\"UTF-8\">");
                    html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
                    html.AppendLine($"    <title>JavaScript {formattedTestType} Summary</title>");
                    html.AppendLine("    <style>");
                    html.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
                    html.AppendLine("        h1 { color: #333; }");
                    html.AppendLine("        .table-container { overflow-x: auto; }");
                    html.AppendLine("        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }");
                    html.AppendLine("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }");
                    html.AppendLine("        th { background-color: #4CAF50; color: white; }");
                    html.AppendLine("        tr:nth-child(even) { background-color: #f2f2f2; }");
                    html.AppendLine("        .pass { color: green; font-weight: bold; }");
                    html.AppendLine("        .fail { color: red; font-weight: bold; }");
                    html.AppendLine("        @media (max-width: 768px) {");
                    html.AppendLine("            body { margin: 10px; }");
                    html.AppendLine("            th, td { padding: 8px; font-size: 14px; }");
                    html.AppendLine("            h1 { font-size: 24px; }");
                    html.AppendLine("        }");
                    html.AppendLine("    </style>");
                    html.AppendLine("</head>");
                    html.AppendLine("<body>");
                    html.AppendLine($"    <h1>JavaScript {formattedTestType} Summary</h1>");
                    html.AppendLine($"    <div class=\"meta\" style=\"color: #666; font-style: italic; margin-bottom: 10px;\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>");
                    html.AppendLine("    <div class=\"table-container\">");
                    html.AppendLine("    <table>");
                    html.AppendLine("        <tr>");
                    html.AppendLine("            <th>AppSite</th>");
                    html.AppendLine("            <th>AppFile</th>");
                    html.AppendLine("            <th>AppView</th>");
                    html.AppendLine("            <th>OutputMatch</th>");
                    html.AppendLine("            <th>ViewUnMatch</th>");
                    html.AppendLine("            <th>Error</th>");
                    html.AppendLine("        </tr>");

                    foreach (var row in summaryRows)
                    {
                        var outputMatchClass = row.NormalPreProcess == "PASS" ? "pass" : (row.NormalPreProcess == "FAIL" ? "fail" : "");
                        var viewUnMatchClass = row.CrossViewUnMatch == "PASS" ? "pass" : (row.CrossViewUnMatch == "FAIL" ? "fail" : "");

                        html.AppendLine("        <tr>");
                        html.AppendLine($"            <td>{row.AppSite}</td>");
                        html.AppendLine($"            <td>{row.AppFile}</td>");
                        html.AppendLine($"            <td>{row.AppView}</td>");
                        html.AppendLine($"            <td class=\"{outputMatchClass}\">{row.NormalPreProcess}</td>");
                        html.AppendLine($"            <td class=\"{viewUnMatchClass}\">{row.CrossViewUnMatch}</td>");
                        html.AppendLine($"            <td>{row.Error}</td>");
                        html.AppendLine("        </tr>");
                    }

                    html.AppendLine("    </table>");
                    html.AppendLine("    </div>");
                    html.AppendLine("</body>");
                    html.AppendLine("</html>");

                    var htmlFile = Path.Combine(reportsPath, $"javascript_{testTypeFile}_Summary.html");
                    await File.WriteAllTextAsync(htmlFile, html.ToString());
                    Console.WriteLine($"Test summary HTML saved to: {htmlFile}");

                    // Save JSON summary file
                    var jsonFile = Path.Combine(reportsPath, $"javascript_{testTypeFile}_Summary.json");
                    var jsonData = SerializeTestSummaryRowsToJson(summaryRows, true);
                    await File.WriteAllTextAsync(jsonFile, jsonData);
                    Console.WriteLine($"Test summary JSON saved to: {jsonFile}");

                    var response = new TestResponse
                    {
                        Message = "Test results saved successfully"
                    };
                    return Results.Json(response, SimpleJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in /api/test-results: {ex.Message}");
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Save Performance Results Endpoint

            app.MapPost("/api/performance-results", async (HttpContext context, PerfSummaryRowDto[] summaryRows) =>
            {
                try
                {
                    Console.WriteLine($"POST /api/performance-results called with {summaryRows.Length} rows");

                    // Validate each row
                    var validAppSites = SecurityValidator.GetValidAppSites();
                    foreach (var row in summaryRows)
                    {
                        // Validate AppSite is in allowlist
                        if (!string.IsNullOrEmpty(row.AppSite) && !validAppSites.Contains(row.AppSite))
                            return Results.Text($"Invalid AppSite: {row.AppSite}", statusCode: 400);

                        // Validate parameter lengths (256 char limit)
                        if (!SecurityValidator.IsValidPathComponent(row.AppSite))
                            return Results.Text("Invalid AppSite parameter", statusCode: 400);
                        if (!SecurityValidator.IsValidPathComponent(row.AppFile))
                            return Results.Text("Invalid AppFile parameter", statusCode: 400);
                        if (!string.IsNullOrEmpty(row.AppView) && !SecurityValidator.IsValidPathComponent(row.AppView))
                            return Results.Text("Invalid AppView parameter", statusCode: 400);
                    }

                    string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                    string reportsPath = Path.Combine(projectDirectory, "template_analysis", "Reports");

                    // Create Reports directory if it doesn't exist
                    Directory.CreateDirectory(reportsPath);

                    // Generate HTML table
                    var html = new System.Text.StringBuilder();
                    html.AppendLine("<html><head><title>Client-Side Performance Summary Table</title>");
                    html.AppendLine("<style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}.meta{color:#666;font-style:italic;margin-bottom:10px;}</style></head><body>");
                    html.AppendLine("<h2>Client-Side JavaScript PERFORMANCE SUMMARY TABLE</h2>");
                    html.AppendLine($"<div class=\"meta\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | All times in milliseconds (ms)</div>");
                    html.AppendLine("<table>");
                    html.AppendLine("<tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th></tr>");

                    foreach (var row in summaryRows)
                    {
                        html.AppendLine("<tr>");
                        html.AppendLine($"<td>{row.AppSite}</td>");
                        html.AppendLine($"<td>{row.AppView}</td>");
                        html.AppendLine($"<td>{row.NormalTimeMs:F2}</td>");
                        html.AppendLine($"<td>{row.PreProcessTimeMs:F2}</td>");
                        html.AppendLine($"<td>{row.ResultsMatch}</td>");
                        html.AppendLine($"<td>{row.PerfDifference}</td>");
                        html.AppendLine("</tr>");
                    }

                    html.AppendLine("</table></body></html>");

                    var htmlFile = Path.Combine(reportsPath, "javascript_perfsummary.html");
                    await File.WriteAllTextAsync(htmlFile, html.ToString());
                    Console.WriteLine($"Performance summary HTML saved to: {htmlFile}");

                    // Save JSON summary file
                    var jsonFile = Path.Combine(reportsPath, "javascript_perfsummary.json");
                    var jsonData = SerializePerfSummaryRowsToJson(summaryRows, true);
                    await File.WriteAllTextAsync(jsonFile, jsonData);
                    Console.WriteLine($"Performance summary JSON saved to: {jsonFile}");

                    var response = new TestResponse
                    {
                        Message = "Performance results saved successfully"
                    };
                    return Results.Json(response, SimpleJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in /api/performance-results: {ex.Message}");
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Save Log Endpoint

            app.MapPost("/api/save-log", async (HttpContext context) =>
            {
                try
                {
                    using var reader = new StreamReader(context.Request.Body);
                    var requestBody = await reader.ReadToEndAsync();

                    var contextName = "";
                    var logContent = "";

                    try
                    {
                        var jsonDoc = System.Text.Json.JsonDocument.Parse(requestBody);
                        if (jsonDoc.RootElement.TryGetProperty("context", out var contextElement))
                        {
                            contextName = contextElement.GetString() ?? "";
                        }
                        if (jsonDoc.RootElement.TryGetProperty("content", out var contentElement))
                        {
                            logContent = contentElement.GetString() ?? "";
                        }
                    }
                    catch
                    {
                        return Results.Text("Invalid JSON format", statusCode: 400);
                    }

                    if (string.IsNullOrEmpty(contextName) || string.IsNullOrEmpty(logContent))
                        return Results.Text("Missing context or content parameter", statusCode: 400);

                    // Validate context name parameter (256 char limit, no path traversal)
                    if (!SecurityValidator.IsValidPathComponent(contextName))
                        return Results.Text("Invalid context parameter", statusCode: 400);

                    // Validate log content (size and format)
                    if (!SecurityValidator.IsValidLogContent(logContent, out var logErrorMessage))
                        return Results.Text(logErrorMessage ?? "Invalid log content", statusCode: 400);

                    string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                    var logsDir = Path.Combine(projectDirectory, "template_analysis", "logs");
                    Directory.CreateDirectory(logsDir);

                    var logFile = Path.Combine(logsDir, $"javascript_{contextName.ToLower()}.log");
                    await File.WriteAllTextAsync(logFile, logContent);

                    var response = new TestResponse
                    {
                        Message = "Log saved successfully"
                    };
                    return Results.Json(response, SimpleJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Save Output Endpoint

            app.MapPost("/api/save-output", async (HttpContext context) =>
            {
                try
                {
                    using var reader = new StreamReader(context.Request.Body);
                    var requestBody = await reader.ReadToEndAsync();

                    var appSite = "";
                    var appView = "";
                    var engineType = "";
                    var htmlContent = "";

                    try
                    {
                        var jsonDoc = System.Text.Json.JsonDocument.Parse(requestBody);
                        if (jsonDoc.RootElement.TryGetProperty("appSite", out var appSiteElement))
                        {
                            appSite = appSiteElement.GetString() ?? "";
                        }
                        if (jsonDoc.RootElement.TryGetProperty("appView", out var appViewElement))
                        {
                            appView = appViewElement.GetString() ?? "";
                        }
                        if (jsonDoc.RootElement.TryGetProperty("engineType", out var engineTypeElement))
                        {
                            engineType = engineTypeElement.GetString() ?? "";
                        }
                        if (jsonDoc.RootElement.TryGetProperty("html", out var htmlElement))
                        {
                            htmlContent = htmlElement.GetString() ?? "";
                        }
                    }
                    catch
                    {
                        return Results.Text("Invalid JSON format", statusCode: 400);
                    }

                    if (string.IsNullOrEmpty(appSite) || string.IsNullOrEmpty(engineType) || string.IsNullOrEmpty(htmlContent))
                        return Results.Text("Missing required parameters", statusCode: 400);

                    // Validate AppSite against allowlist
                    var validAppSites = SecurityValidator.GetValidAppSites();
                    if (!validAppSites.Contains(appSite))
                        return Results.Text("Invalid AppSite value", statusCode: 400);

                    // Validate engine type against allowlist
                    if (!SecurityValidator.ValidEngineTypes.Contains(engineType))
                        return Results.Text("Invalid engine type", statusCode: 400);

                    // Validate parameters (256 char limit, no path traversal)
                    if (!SecurityValidator.IsValidPathComponent(appSite))
                        return Results.Text("Invalid AppSite parameter", statusCode: 400);
                    if (!string.IsNullOrEmpty(appView) && !SecurityValidator.IsValidPathComponent(appView))
                        return Results.Text("Invalid AppView parameter", statusCode: 400);
                    if (!SecurityValidator.IsValidPathComponent(engineType))
                        return Results.Text("Invalid engineType parameter", statusCode: 400);

                    // Validate output size against template size + 10KB buffer
                    var templateTotalSize = SecurityValidator.GetTemplateTotalSize(appSite, appView ?? "");
                    if (!SecurityValidator.IsValidOutputSizeWithBuffer(htmlContent, templateTotalSize))
                        return Results.Text("Output content exceeds maximum size limit (template size + 10KB buffer)", statusCode: 400);

                    string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                    var outputDir = Path.Combine(projectDirectory, "template_analysis", "output");
                    Directory.CreateDirectory(outputDir);

                    var appViewSuffix = string.IsNullOrEmpty(appView) ? "" : $"_{appView}";
                    var engineSuffix = engineType.ToLower();
                    var outputFile = Path.Combine(outputDir, $"{appSite}{appViewSuffix}_{engineSuffix}.html");
                    await File.WriteAllTextAsync(outputFile, htmlContent);

                    var response = new TestResponse
                    {
                        Message = "Output saved successfully"
                    };
                    return Results.Json(response, SimpleJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Get Report Endpoint

            app.MapPost("/api/report", async (HttpContext context) =>
            {
                try
                {
                    using var reader = new StreamReader(context.Request.Body);
                    var requestBody = await reader.ReadToEndAsync();

                    var fileName = "";
                    var useLangPrefix = false;

                    try
                    {
                        var jsonDoc = System.Text.Json.JsonDocument.Parse(requestBody);
                        if (jsonDoc.RootElement.TryGetProperty("fileName", out var fileNameElement))
                        {
                            fileName = fileNameElement.GetString() ?? "";
                        }
                        if (jsonDoc.RootElement.TryGetProperty("useLangPrefix", out var useLangPrefixElement))
                        {
                            useLangPrefix = useLangPrefixElement.GetBoolean();
                        }
                    }
                    catch
                    {
                        return Results.Text("Invalid JSON format", statusCode: 400);
                    }

                    if (string.IsNullOrEmpty(fileName))
                        return Results.Text("Missing required field: fileName", statusCode: 400);

                    // Validate fileName for path traversal
                    if (!SecurityValidator.IsValidPathComponent(fileName))
                        return Results.Text("Invalid characters in fileName", statusCode: 400);

                    // Construct file path
                    string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                    var prefix = useLangPrefix ? "javascript_" : "";
                    var fullFileName = prefix + fileName;
                    var reportsDir = Path.Combine(projectDirectory, "template_analysis", "Reports");
                    var filePath = Path.Combine(reportsDir, fullFileName);

                    // Check if file exists
                    if (!File.Exists(filePath))
                        return Results.Text($"Report file not found: {fullFileName}", statusCode: 404);

                    // Read and return the file content
                    var content = await File.ReadAllTextAsync(filePath);

                    // Determine content type based on file extension
                    var contentType = "text/plain";
                    var extension = Path.GetExtension(fullFileName).ToLower();
                    if (extension == ".html")
                        contentType = "text/html";
                    else if (extension == ".json")
                        contentType = "application/json";
                    else if (extension == ".md")
                        contentType = "text/markdown";

                    return Results.Content(content, contentType);
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Consolidate Performance Endpoint

            app.MapPost("/api/consolidate-performance", async (HttpContext context) =>
            {
                try
                {
                    string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
                    string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                    // Configure logging for consolidate endpoint
                    var templateAnalysisDir = Path.Combine(projectDirectory, "template_analysis");
                    var logsDir = Path.Combine(templateAnalysisDir, "logs");
                    Directory.CreateDirectory(logsDir);
                    var consolidateLogFile = Path.Combine(logsDir, "javascript_consolidate_perf.log");

                    // Read server configuration from servers.csv
                    var serversConfigPath = Path.Combine(rootDirPath, "App_Data", "servers.csv");
                    List<(string Language, string Url, string Method, string FileName)> perfServers = new List<(string Language, string Url, string Method, string FileName)>();

                    if (File.Exists(serversConfigPath))
                    {
                        File.AppendAllText(consolidateLogFile, $"\n[{DateTime.UtcNow:O}] Starting consolidate-performance endpoint\n");
                        var lines = await File.ReadAllLinesAsync(serversConfigPath);
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(',');
                            if (parts.Length >= 3)
                            {
                                var language = parts[0].Trim();
                                var method = parts[1].Trim().ToUpper();
                                var url = parts[2].Trim();
                                var fileName = parts.Length >= 4 ? parts[3].Trim() : "";
                                if (!string.IsNullOrEmpty(language) && !string.IsNullOrEmpty(method) && !string.IsNullOrEmpty(url))
                                {
                                    perfServers.Add((language, url, method, fileName));
                                }
                            }
                        }
                    }

                    if (perfServers.Count == 0)
                    {
                        var errorMsg = "No server configuration found. Please configure servers in App_Data/servers.csv";
                        File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] ❌ {errorMsg}\n");

                        var errorResponse = new TestResponse
                        {
                            Message = errorMsg
                        };

                        return Results.Json(errorResponse, SimpleJsonContext.Default.TestResponse);
                    }

                    var appPerf = new Dictionary<string, Dictionary<string, (double? NormalTimeMs, double? PreProcessTimeMs, int? OutputSize, string? AppView)>>();
                    var serversProcessed = new List<string>();
                    var serversFailed = new List<string>();

                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

                    // Group servers by language
                    var serversByLang = perfServers.GroupBy(s => s.Language).ToList();

                    foreach (var langGroup in serversByLang)
                    {
                        var lang = langGroup.Key;
                        bool langSuccess = false;
                        var langErrors = new List<string>();

                        foreach (var (_, url, method, fileName) in langGroup)
                        {
                            try
                            {
                                HttpResponseMessage httpResponse;

                                if (method == "POST")
                                {
                                    File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] Fetching {lang} via POST {url} (fileName: {fileName})\n");
                                    var reportRequest = new ReportRequestDto { FileName = fileName, UseLangPrefix = false };
                                    var requestBody = System.Text.Json.JsonSerializer.Serialize(reportRequest, SimpleJsonContext.Default.ReportRequestDto);
                                    var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                                    httpResponse = await httpClient.PostAsync(url, content);
                                }
                                else
                                {
                                    var fullUrl = url + fileName;
                                    File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] Fetching {lang} via GET {fullUrl}\n");
                                    httpResponse = await httpClient.GetAsync(fullUrl);
                                }

                                if (httpResponse.IsSuccessStatusCode)
                                {
                                    var responseContent = await httpResponse.Content.ReadAsStringAsync();
                                    var arr = System.Text.Json.JsonDocument.Parse(responseContent).RootElement;

                                    if (arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                                    {
                                        int itemCount = 0;
                                        foreach (var item in arr.EnumerateArray())
                                        {
                                            string appSite = item.TryGetProperty("AppSite", out var v1) ? v1.GetString() ?? "" :
                                                            (item.TryGetProperty("app_site", out var v2) ? v2.GetString() ?? "" :
                                                            (item.TryGetProperty("appSite", out var v3) ? v3.GetString() ?? "" : ""));

                                            string appView = item.TryGetProperty("AppView", out var av1) ? av1.GetString() ?? "" :
                                                            (item.TryGetProperty("app_view", out var av2) ? av2.GetString() ?? "" :
                                                            (item.TryGetProperty("appView", out var av3) ? av3.GetString() ?? "" : ""));

                                            double? normalTime = null;
                                            if (item.TryGetProperty("NormalTimeMs", out var nt1)) normalTime = nt1.GetDouble();
                                            else if (item.TryGetProperty("normal_time_ms", out var nt2)) normalTime = nt2.GetDouble();
                                            else if (item.TryGetProperty("normalTimeMs", out var nt3)) normalTime = nt3.GetDouble();
                                            else if (item.TryGetProperty("NormalTimeNanos", out var ntn1)) normalTime = ntn1.GetDouble() / 1_000_000.0;
                                            else if (item.TryGetProperty("normal_time_nanos", out var ntn2)) normalTime = ntn2.GetDouble() / 1_000_000.0;

                                            double? preprocessTime = null;
                                            if (item.TryGetProperty("PreProcessTimeMs", out var pt1)) preprocessTime = pt1.GetDouble();
                                            else if (item.TryGetProperty("preprocess_time_ms", out var pt2)) preprocessTime = pt2.GetDouble();
                                            else if (item.TryGetProperty("preProcessTimeMs", out var pt3)) preprocessTime = pt3.GetDouble();
                                            else if (item.TryGetProperty("PreProcessTimeNanos", out var ptn1)) preprocessTime = ptn1.GetDouble() / 1_000_000.0;
                                            else if (item.TryGetProperty("preprocess_time_nanos", out var ptn2)) preprocessTime = ptn2.GetDouble() / 1_000_000.0;

                                            int? outputSize = item.TryGetProperty("OutputSize", out var os1) ? os1.GetInt32() :
                                                             (item.TryGetProperty("output_size", out var os2) ? os2.GetInt32() : null);

                                            if (!string.IsNullOrEmpty(appSite))
                                            {
                                                string key = string.IsNullOrEmpty(appView) ? appSite : appSite + " → " + appView;

                                                // Use case-insensitive comparison for key matching
                                                var existingKey = appPerf.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
                                                if (existingKey != null)
                                                {
                                                    key = existingKey; // Use existing key to maintain consistent casing
                                                }
                                                else
                                                {
                                                    appPerf[key] = new Dictionary<string, (double?, double?, int?, string?)>();
                                                }
                                                appPerf[key][lang] = (normalTime, preprocessTime, outputSize, appView);
                                                itemCount++;
                                            }
                                        }
                                        File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] ✅ {lang} ({method}): Successfully processed {itemCount} items\n");
                                        langSuccess = true;
                                        break; // Success, no need to try other methods
                                    }
                                }
                                else
                                {
                                    var errorMsg = $"{method} {url} (HTTP {httpResponse.StatusCode})";
                                    langErrors.Add(errorMsg);
                                    File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] ⚠️ {lang}: {errorMsg}\n");
                                }
                            }
                            catch (Exception ex)
                            {
                                var errorMsg = $"{method} {url} (ERROR: {ex.Message})";
                                langErrors.Add(errorMsg);
                                File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] ⚠️ {lang}: {errorMsg}\n");
                            }
                        }

                        // After trying all methods for this language, determine overall result
                        if (langSuccess)
                        {
                            serversProcessed.Add(lang);
                        }
                        else
                        {
                            var failureMsg = $"{lang}: All methods failed - {string.Join("; ", langErrors)}";
                            serversFailed.Add(failureMsg);
                            File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] ❌ {lang}: All methods failed\n");
                        }
                    }

                    // Build HTML report
                    var htmlSb = new System.Text.StringBuilder();
                    htmlSb.AppendLine("<!DOCTYPE html>");
                    htmlSb.AppendLine("<html>");
                    htmlSb.AppendLine("<head>");
                    htmlSb.AppendLine("    <meta charset=\"UTF-8\">");
                    htmlSb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
                    htmlSb.AppendLine("    <title>Consolidated Performance Summary</title>");
                    htmlSb.AppendLine("    <style>");
                    htmlSb.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
                    htmlSb.AppendLine("        h1 { color: #333; }");
                    htmlSb.AppendLine("        h2 { color: #333; margin-top: 40px; }");
                    htmlSb.AppendLine("        .meta { color: #666; font-style: italic; margin-bottom: 10px; }");
                    htmlSb.AppendLine("        .table-container { overflow-x: auto; }");
                    htmlSb.AppendLine("        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 700px; }");
                    htmlSb.AppendLine("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }");
                    htmlSb.AppendLine("        th { background-color: #4CAF50; color: white; }");
                    htmlSb.AppendLine("        tr:nth-child(even) { background-color: #f2f2f2; }");
                    htmlSb.AppendLine("        td:nth-child(2), td:nth-child(3), td:nth-child(4), td:nth-child(5), td:nth-child(6), td:nth-child(7) { text-align: right; }");
                    htmlSb.AppendLine("        .best-perf { background-color: #90EE90; font-weight: bold; }");
                    htmlSb.AppendLine("    </style>");
                    htmlSb.AppendLine("</head>");
                    htmlSb.AppendLine("<body>");
                    htmlSb.AppendLine("    <h1>Consolidated Performance Summary</h1>");
                    htmlSb.AppendLine($"    <div class=\"meta\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | All times in milliseconds (ms)</div>");

                    // Get list of languages dynamically from configuration
                    var languages = serversByLang.Select(g => g.Key).OrderBy(l => l).ToList();

                    // Normal Engine Table
                    htmlSb.AppendLine("    <h2>Normal Engine</h2>");
                    htmlSb.AppendLine("    <div class=\"table-container\">");
                    htmlSb.AppendLine("    <table>");
                    htmlSb.AppendLine("        <tr>");
                    htmlSb.AppendLine("            <th>AppSite/AppView</th>");
                    foreach (var lang in languages)
                    {
                        htmlSb.AppendLine($"            <th>{lang}</th>");
                    }
                    htmlSb.AppendLine("            <th>OutputSize</th>");
                    htmlSb.AppendLine("        </tr>");
                    foreach (var app in appPerf.Keys.OrderBy(k => k))
                    {
                        // Find minimum time for highlighting
                        var validTimes = languages
                            .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue)
                            .Select(lang => appPerf[app][lang].NormalTimeMs!.Value)
                            .ToList();
                        var minTime = validTimes.Any() ? validTimes.Min() : (double?)null;

                        htmlSb.AppendLine("        <tr>");
                        htmlSb.AppendLine($"            <td>{app}</td>");
                        foreach (var lang in languages)
                        {
                            var timeValue = appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue
                                ? appPerf[app][lang].NormalTimeMs!.Value.ToString("F2")
                                : "-";
                            var isBest = minTime.HasValue && appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue
                                && Math.Abs(appPerf[app][lang].NormalTimeMs!.Value - minTime.Value) < 0.001;
                            var cssClass = isBest ? " class=\"best-perf\"" : "";
                            htmlSb.AppendLine($"            <td{cssClass}>{timeValue}</td>");
                        }
                        var outputSizeTuple = appPerf[app].Values.Where(v => v.OutputSize.HasValue).FirstOrDefault();
                        var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
                        htmlSb.AppendLine($"            <td>{outputSize}</td>");
                        htmlSb.AppendLine("        </tr>");
                    }
                    htmlSb.AppendLine("    </table>");
                    htmlSb.AppendLine("    </div>");

                    // PreProcess Engine Table
                    htmlSb.AppendLine("    <h2>PreProcess Engine</h2>");
                    htmlSb.AppendLine("    <div class=\"table-container\">");
                    htmlSb.AppendLine("    <table>");
                    htmlSb.AppendLine("        <tr>");
                    htmlSb.AppendLine("            <th>AppSite/AppView</th>");
                    foreach (var lang in languages)
                    {
                        htmlSb.AppendLine($"            <th>{lang}</th>");
                    }
                    htmlSb.AppendLine("            <th>OutputSize</th>");
                    htmlSb.AppendLine("        </tr>");
                    foreach (var app in appPerf.Keys.OrderBy(k => k))
                    {
                        // Find minimum time for highlighting
                        var validTimes = languages
                            .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue)
                            .Select(lang => appPerf[app][lang].PreProcessTimeMs!.Value)
                            .ToList();
                        var minTime = validTimes.Any() ? validTimes.Min() : (double?)null;

                        htmlSb.AppendLine("        <tr>");
                        htmlSb.AppendLine($"            <td>{app}</td>");
                        foreach (var lang in languages)
                        {
                            var timeValue = appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue
                                ? appPerf[app][lang].PreProcessTimeMs!.Value.ToString("F2")
                                : "-";
                            var isBest = minTime.HasValue && appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue
                                && Math.Abs(appPerf[app][lang].PreProcessTimeMs!.Value - minTime.Value) < 0.001;
                            var cssClass = isBest ? " class=\"best-perf\"" : "";
                            htmlSb.AppendLine($"            <td{cssClass}>{timeValue}</td>");
                        }
                        var outputSizeTuple = appPerf[app].Values.Where(v => v.OutputSize.HasValue).FirstOrDefault();
                        var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
                        htmlSb.AppendLine($"            <td>{outputSize}</td>");
                        htmlSb.AppendLine("        </tr>");
                    }
                    htmlSb.AppendLine("    </table>");
                    htmlSb.AppendLine("    </div>");
                    htmlSb.AppendLine("</body>");
                    htmlSb.AppendLine("</html>");

                    var htmlContent = htmlSb.ToString();

                    // Write HTML to Reports directory
                    var reportsDir = Path.Combine(projectDirectory, "template_analysis", "Reports");
                    Directory.CreateDirectory(reportsDir);
                    var htmlPath = Path.Combine(reportsDir, "all_perf_tests.html");
                    await File.WriteAllTextAsync(htmlPath, htmlContent);

                    File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] Consolidation complete - {appPerf.Count} AppSites from {serversProcessed.Count}/{serversByLang.Count} servers\n");

                    // Count unique languages
                    var totalLanguages = serversByLang.Count;

                    // Build detailed message
                    var messageBuilder = new System.Text.StringBuilder();
                    messageBuilder.Append($"Consolidated {appPerf.Count} AppSites from {serversProcessed.Count}/{totalLanguages} languages");

                    if (serversProcessed.Count > 0)
                    {
                        messageBuilder.Append(" | ✅ Success: ");
                        messageBuilder.Append(string.Join(", ", serversProcessed));
                    }

                    if (serversFailed.Count > 0)
                    {
                        messageBuilder.Append("\n❌ Failed: ");
                        messageBuilder.Append(string.Join("; ", serversFailed));
                    }

                    var response = new TestResponse
                    {
                        Message = messageBuilder.ToString()
                    };

                    return Results.Json(response, SimpleJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion
        }

        #region Custom JSON Serialization for NativeAOT

        private static string SerializeTestSummaryRowsToJson(TestSummaryRowDto[] rows, bool indented)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[");

            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) sb.Append(",");
                if (indented) sb.AppendLine();

                var row = rows[i];
                if (indented) sb.Append("  ");
                sb.Append("{");
                if (indented) sb.AppendLine();

                AppendJsonProperty(sb, "AppSite", row.AppSite, indented, false);
                AppendJsonProperty(sb, "AppFile", row.AppFile, indented, false);
                AppendJsonProperty(sb, "AppView", row.AppView, indented, false);
                AppendJsonProperty(sb, "NormalPreProcess", row.NormalPreProcess, indented, false);
                AppendJsonProperty(sb, "CrossViewUnMatch", row.CrossViewUnMatch, indented, false);
                AppendJsonProperty(sb, "Error", row.Error, indented, true);

                if (indented) sb.Append("  ");
                sb.Append("}");
            }

            if (indented) sb.AppendLine();
            sb.Append("]");

            return sb.ToString();
        }

        private static string SerializePerfSummaryRowsToJson(PerfSummaryRowDto[] rows, bool indented)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[");

            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) sb.Append(",");
                if (indented) sb.AppendLine();

                var row = rows[i];
                if (indented) sb.Append("  ");
                sb.Append("{");
                if (indented) sb.AppendLine();

                AppendJsonProperty(sb, "AppSite", row.AppSite, indented, false);
                AppendJsonProperty(sb, "AppFile", row.AppFile, indented, false);
                AppendJsonProperty(sb, "AppView", row.AppView, indented, false);
                AppendJsonProperty(sb, "Iterations", row.Iterations.ToString(), indented, false, false);
                AppendJsonProperty(sb, "NormalTimeMs", row.NormalTimeMs.ToString("F2"), indented, false, false);
                AppendJsonProperty(sb, "PreProcessTimeMs", row.PreProcessTimeMs.ToString("F2"), indented, false, false);
                AppendJsonProperty(sb, "OutputSize", row.OutputSize.ToString(), indented, false, false);
                AppendJsonProperty(sb, "ResultsMatch", row.ResultsMatch, indented, false);
                AppendJsonProperty(sb, "PerfDifference", row.PerfDifference, indented, true);

                if (indented) sb.Append("  ");
                sb.Append("}");
            }

            if (indented) sb.AppendLine();
            sb.Append("]");

            return sb.ToString();
        }

        private static void AppendJsonProperty(System.Text.StringBuilder sb, string propertyName, string? value, bool indented, bool isLast, bool isString = true)
        {
            if (indented) sb.Append("    ");
            sb.Append("\"");
            sb.Append(propertyName);
            sb.Append("\":");
            if (indented) sb.Append(" ");

            if (isString)
            {
                sb.Append("\"");
                sb.Append(value ?? "");
                sb.Append("\"");
            }
            else
            {
                sb.Append(value ?? "0");
            }

            if (!isLast) sb.Append(",");
            if (indented) sb.AppendLine();
        }

        #endregion  
    }
}

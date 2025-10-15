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
        [System.Text.Json.Serialization.JsonPropertyName("engineType")]
        public string? EngineType { get; set; }
    }

    // Model for test response
    public class TestResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("elapsed")]
        public double Elapsed { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("testCount")]
        public int TestCount { get; set; }
    }

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

    // Model for report request
    public class ReportRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("fileName")]
        public string? FileName { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("useLangPrefix")]
        public bool UseLangPrefix { get; set; }
    }


    [JsonSerializable(typeof(MergeRequest))]
    [JsonSerializable(typeof(TestResponse))]
    [JsonSerializable(typeof(ScenarioDto[]))]
    [JsonSerializable(typeof(ReportRequest))]
    public partial class ResponseJsonContext : JsonSerializerContext { }

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

                    return Results.Json(scenarioDtos, ResponseJsonContext.Default.ScenarioDtoArray);
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

            assemblerGroup.MapPost("/merge", async (HttpContext context) =>
            {
                var serverStart = DateTime.UtcNow;

                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body))
                    return Results.BadRequest("Empty request body");

                var input = System.Text.Json.JsonSerializer.Deserialize<MergeRequest>(body, ResponseJsonContext.Default.MergeRequest);
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

                // Save HTML output to template_analysis/output folder (parent of wwwroot)
                var contentRoot = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                var outputDir = Path.Combine(contentRoot, "template_analysis", "output");
                Directory.CreateDirectory(outputDir);

                var appViewSuffix = string.IsNullOrEmpty(input.AppView) ? "" : $"_{input.AppView}";
                var engineSuffix = input.EngineType.ToLower();
                var outputFile = Path.Combine(outputDir, $"{input.AppSite}{appViewSuffix}_{engineSuffix}.html");
                File.WriteAllText(outputFile, mergedHtml);

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
            })
            .Accepts<MergeRequest>("application/json")
            .WithName("PostMergeTemplate")
            .WithDisplayName("Post Method to Merge Template for AppSite, AppView, EngineType")
            .WithDescription("Post Method to Merge Template for AppSite, AppView, EngineType. AppFile is retrieved from scenarios.")
            .WithTags("Merge");

            // Test endpoints
            assemblerGroup.MapPost("/test/standard", (HttpContext context) =>
            {
                var sw = Stopwatch.StartNew();
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                // Enable logging temporarily for tests
                var originalLogLevel = Logger.GetLogLevel();

                // Configure logger with context-specific log files for StandardTests
                var templateAnalysisDir = Path.Combine(projectDirectory, "template_analysis");
                var logsDir = Path.Combine(templateAnalysisDir, "logs");
                Directory.CreateDirectory(logsDir);

                var contextLogFiles = new Dictionary<string, string>
                {
                    { "LoaderNormal", Path.Combine(logsDir, "csharp_loadernormal.log") },
                    { "EngineNormal", Path.Combine(logsDir, "csharp_enginenormal.log") }
                };

                Logger.Configure(Logger.LogLevel.DEBUG, null, false);
                Logger.ConfigureContextLogFiles(contextLogFiles);

                try
                {
                    var scenarios = ConfigUtil.GetScenarios();
                    var summaryRows = TestingUtils.RunStandardTests(rootDirPath, projectDirectory, scenarios, false, true, true);
                    if (summaryRows != null && summaryRows.Count > 0)
                    {
                        TestingUtils.PrintTestSummaryTable(rootDirPath, projectDirectory,summaryRows, "STANDARD TEST");
                    }

                    sw.Stop();
                    var elapsedSeconds = sw.Elapsed.TotalSeconds;
                    var testCount = summaryRows?.Count ?? 0;

                    // Check for failures
                    var failedTests = summaryRows?.Where(r =>
                        r.NormalPreProcess == "FAIL" ||
                        r.CrossViewUnMatch == "FAIL" ||
                        !string.IsNullOrEmpty(r.Error)
                    ).ToList() ?? new List<TestSummaryRow>();

                    var message = $"Successful run of Standard Tests in {elapsedSeconds:F2} secs ({testCount} tests)";
                    if (failedTests.Count > 0)
                    {
                        message += $"\n⚠️ Warning: {failedTests.Count} test(s) failed";
                    }

                    var response = new TestResponse
                    {
                        Success = true,
                        Message = message,
                        Elapsed = elapsedSeconds,
                        TestCount = testCount
                    };

                    return Results.Json(response, ResponseJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return Results.Problem($"Error running standard tests: {ex.Message}");
                }
                finally
                {
                    // Restore original log level
                    Logger.SetLogLevel(originalLogLevel);
                }
            })
            .WithName("RunStandardTests")
            .WithDisplayName("Run Standard Tests")
            .WithDescription("Runs standard template tests and saves results to wwwroot")
            .WithTags("Test");

            assemblerGroup.MapPost("/test/advanced", (HttpContext context) =>
            {
                var sw = Stopwatch.StartNew();
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                // Enable logging temporarily for tests
                var originalLogLevel = Logger.GetLogLevel();

                // Configure logger with context-specific log files for AdvancedTests
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
                    var scenarios = ConfigUtil.GetScenarios();

                    // Dump preprocessed template structures before running advanced tests
                    TestingUtils.DumpPreprocessedTemplateStructures(rootDirPath, projectDirectory, scenarios, true);

                    var summaryRows = TestingUtils.RunAdvancedTests(rootDirPath, projectDirectory, scenarios, false, true, true);
                    if (summaryRows != null && summaryRows.Count > 0)
                    {
                        TestingUtils.PrintTestSummaryTable(rootDirPath, projectDirectory, summaryRows, "ADVANCED TEST");
                    }

                    sw.Stop();
                    var elapsedSeconds = sw.Elapsed.TotalSeconds;
                    var testCount = summaryRows?.Count ?? 0;

                    // Check for failures
                    var failedTests = summaryRows?.Where(r =>
                        r.NormalPreProcess == "FAIL" ||
                        r.CrossViewUnMatch == "FAIL" ||
                        !string.IsNullOrEmpty(r.Error)
                    ).ToList() ?? new List<TestSummaryRow>();

                    var message = $"Successful run of Advanced Tests in {elapsedSeconds:F2} secs ({testCount} tests)";
                    if (failedTests.Count > 0)
                    {
                        message += $"\n⚠️ Warning: {failedTests.Count} test(s) failed";
                    }

                    var response = new TestResponse
                    {
                        Success = true,
                        Message = message,
                        Elapsed = elapsedSeconds,
                        TestCount = testCount
                    };

                    return Results.Json(response, ResponseJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return Results.Problem($"Error running advanced tests: {ex.Message}");
                }
                finally
                {
                    // Restore original log level
                    Logger.SetLogLevel(originalLogLevel);
                }
            })
            .WithName("RunAdvancedTests")
            .WithDisplayName("Run Advanced Tests")
            .WithDescription("Runs advanced template tests and saves results to wwwroot")
            .WithTags("Test");

            assemblerGroup.MapPost("/test/performance", (HttpContext context) =>
            {
                var sw = Stopwatch.StartNew();
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                // Disable logging during performance tests
                var originalLogLevel = Logger.GetLogLevel();
                Logger.SetLogLevel(Logger.LogLevel.NONE);

                try
                {
                    var scenarios = ConfigUtil.GetScenarios();
                    var summaryRows = PerformanceUtils.RunPerformanceComparison(rootDirPath, projectDirectory, scenarios, true, true);
                    if (summaryRows != null && summaryRows.Count > 0)
                    {
                        PerformanceUtils.PrintPerfSummaryTable(rootDirPath, projectDirectory, summaryRows);
                    }

                    sw.Stop();
                    var elapsedSeconds = sw.Elapsed.TotalSeconds;
                    var testCount = summaryRows?.Count ?? 0;

                    // Check for performance test mismatches
                    var mismatchTests = summaryRows?.Where(r => r.ResultsMatch != "YES").ToList() ?? new List<PerformanceUtils.PerfSummaryRow>();

                    var message = $"Successful run of Performance Tests in {elapsedSeconds:F2} secs ({testCount} tests)";
                    if (mismatchTests.Count > 0)
                    {
                        message += $"\n⚠️ Warning: {mismatchTests.Count} test(s) have output mismatch";
                    }

                    var response = new TestResponse
                    {
                        Success = true,
                        Message = message,
                        Elapsed = elapsedSeconds,
                        TestCount = testCount
                    };

                    return Results.Json(response, ResponseJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return Results.Problem($"Error running performance tests: {ex.Message}");
                }
                finally
                {
                    // Restore original log level
                    Logger.SetLogLevel(originalLogLevel);
                }
            })
            .WithName("RunPerformanceTests")
            .WithDisplayName("Run Performance Tests")
            .WithDescription("Runs performance comparison tests and saves results to wwwroot")
            .WithTags("Test");

            assemblerGroup.MapPost("/test/consolidate-performance", async (HttpContext context) =>
            {
                var sw = Stopwatch.StartNew();
                string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                // Configure logging for consolidate endpoint
                var templateAnalysisDir = Path.Combine(projectDirectory, "template_analysis");
                var logsDir = Path.Combine(templateAnalysisDir, "logs");
                Directory.CreateDirectory(logsDir);
                var consolidateLogFile = Path.Combine(logsDir, "csharp_consolidate_perf.log");

                try
                {
                    // Log start
                    File.AppendAllText(consolidateLogFile, $"\n[{DateTime.UtcNow:O}] Starting consolidate-performance endpoint\n");

                    // Read server configuration from servers.csv
                    var serversConfigPath = Path.Combine(rootDirPath, "App_Data", "servers.csv");
                    List<(string Language, string Url, string Method, string FileName)> perfServers = new List<(string Language, string Url, string Method, string FileName)>();

                    if (File.Exists(serversConfigPath))
                    {
                        var lines = File.ReadAllLines(serversConfigPath);
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
                            Success = false,
                            Message = errorMsg,
                            Elapsed = sw.Elapsed.TotalSeconds,
                            TestCount = 0
                        };

                        return Results.Json(errorResponse, ResponseJsonContext.Default.TestResponse);
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
                                    var reportRequest = new ReportRequest { FileName = fileName, UseLangPrefix = false };
                                    var requestBody = JsonSerializer.Serialize(reportRequest, ResponseJsonContext.Default.ReportRequest);
                                    var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
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
                                    var arr = JsonDocument.Parse(responseContent).RootElement;

                                    if (arr.ValueKind == JsonValueKind.Array)
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
                                            if (item.TryGetProperty("NormalTimeMs", out var nt1) && nt1.ValueKind == JsonValueKind.Number) normalTime = nt1.GetDouble();
                                            else if (item.TryGetProperty("normal_time_ms", out var nt2) && nt2.ValueKind == JsonValueKind.Number) normalTime = nt2.GetDouble();
                                            else if (item.TryGetProperty("normalTimeMs", out var nt3) && nt3.ValueKind == JsonValueKind.Number) normalTime = nt3.GetDouble();
                                            else if (item.TryGetProperty("NormalTimeTicks", out var ntticks) && ntticks.ValueKind == JsonValueKind.Number) normalTime = ntticks.GetDouble() / 10000.0;
                                            else if (item.TryGetProperty("NormalTimeNanos", out var ntn1) && ntn1.ValueKind == JsonValueKind.Number) normalTime = ntn1.GetDouble() / 1_000_000.0;
                                            else if (item.TryGetProperty("normal_time_nanos", out var ntn2) && ntn2.ValueKind == JsonValueKind.Number) normalTime = ntn2.GetDouble() / 1_000_000.0;

                                            double? preprocessTime = null;
                                            if (item.TryGetProperty("PreProcessTimeMs", out var pt1) && pt1.ValueKind == JsonValueKind.Number) preprocessTime = pt1.GetDouble();
                                            else if (item.TryGetProperty("preprocess_time_ms", out var pt2) && pt2.ValueKind == JsonValueKind.Number) preprocessTime = pt2.GetDouble();
                                            else if (item.TryGetProperty("preProcessTimeMs", out var pt3) && pt3.ValueKind == JsonValueKind.Number) preprocessTime = pt3.GetDouble();
                                            else if (item.TryGetProperty("PreProcessTimeTicks", out var ptticks) && ptticks.ValueKind == JsonValueKind.Number) preprocessTime = ptticks.GetDouble() / 10000.0;
                                            else if (item.TryGetProperty("PreProcessTimeNanos", out var ptn1) && ptn1.ValueKind == JsonValueKind.Number) preprocessTime = ptn1.GetDouble() / 1_000_000.0;
                                            else if (item.TryGetProperty("preprocess_time_nanos", out var ptn2) && ptn2.ValueKind == JsonValueKind.Number) preprocessTime = ptn2.GetDouble() / 1_000_000.0;

                                            int? outputSize = null;
                                            if (item.TryGetProperty("OutputSize", out var os1) && os1.ValueKind == JsonValueKind.Number) outputSize = os1.GetInt32();
                                            else if (item.TryGetProperty("output_size", out var os2) && os2.ValueKind == JsonValueKind.Number) outputSize = os2.GetInt32();
                                            else if (item.TryGetProperty("outputSize", out var os3) && os3.ValueKind == JsonValueKind.Number) outputSize = os3.GetInt32();

                                            if (!string.IsNullOrEmpty(appSite))
                                            {
                                                // Normalize AppView to ensure case-insensitive matching across languages
                                                string normalizedAppView = string.IsNullOrEmpty(appView) ? "" : appView;
                                                string key = string.IsNullOrEmpty(normalizedAppView) ? appSite : appSite + " → " + normalizedAppView;

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
                    var htmlSb = new StringBuilder();
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
                    htmlSb.AppendLine("        @media (max-width: 768px) {");
                    htmlSb.AppendLine("            body { margin: 10px; }");
                    htmlSb.AppendLine("            th, td { padding: 8px; font-size: 14px; }");
                    htmlSb.AppendLine("            h1 { font-size: 24px; }");
                    htmlSb.AppendLine("            h2 { font-size: 20px; }");
                    htmlSb.AppendLine("            .meta { font-size: 12px; }");
                    htmlSb.AppendLine("        }");
                    htmlSb.AppendLine("    </style>");
                    htmlSb.AppendLine("</head>");
                    htmlSb.AppendLine("<body>");
                    htmlSb.AppendLine("    <h1>Consolidated Performance Summary</h1>");
                    htmlSb.AppendLine($"    <div class=\"meta\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>");

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

                    // Write HTML to template_analysis/Reports directory
                    var reportsDir = Path.Combine(projectDirectory, "template_analysis", "Reports");
                    Directory.CreateDirectory(reportsDir);
                    var htmlPath = Path.Combine(reportsDir, "all_perf_tests.html");
                    File.WriteAllText(htmlPath, htmlContent);

                    sw.Stop();
                    var elapsedSeconds = sw.Elapsed.TotalSeconds;

                    File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] Consolidation complete in {elapsedSeconds:F2}s - {appPerf.Count} AppSites from {serversProcessed.Count}/{perfServers.Count} servers\n");

                    // Count unique languages
                    var totalLanguages = serversByLang.Count;

                    // Build detailed message
                    var messageBuilder = new StringBuilder();
                    messageBuilder.Append($"Consolidated {appPerf.Count} AppSites from {serversProcessed.Count}/{totalLanguages} languages in {elapsedSeconds:F2} secs");

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
                        Success = true,
                        Message = messageBuilder.ToString(),
                        Elapsed = elapsedSeconds,
                        TestCount = appPerf.Count
                    };

                    return Results.Json(response, ResponseJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] ❌ FATAL ERROR: {ex.Message}\n");
                    File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] Stack trace: {ex.StackTrace}\n");
                    return Results.Problem($"Error consolidating performance data: {ex.Message}");
                }
            })
            .WithName("ConsolidatePerformanceTests")
            .WithDisplayName("Consolidate All Performance Tests")
            .WithDescription("Fetches performance data from all language servers and consolidates into a single report")
            .WithTags("Test");

            assemblerGroup.MapPost("/api/report", async (HttpContext context) =>
            {
                string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body))
                    return Results.BadRequest("Empty request body");

                var input = System.Text.Json.JsonSerializer.Deserialize<ReportRequest>(body, ResponseJsonContext.Default.ReportRequest);
                if (input == null || string.IsNullOrWhiteSpace(input.FileName))
                    return Results.BadRequest("Missing required field: fileName");

                // Validate fileName for path traversal
                if (!SecurityValidator.IsValidPathComponent(input.FileName))
                    return Results.BadRequest("Invalid characters in fileName");

                // Construct file path
                string prefix = input.UseLangPrefix ? "csharp_" : "";
                string fileName = prefix + input.FileName;
                string reportsDir = Path.Combine(projectDirectory, "template_analysis", "Reports");
                string filePath = Path.Combine(reportsDir, fileName);

                // Check if file exists
                if (!File.Exists(filePath))
                    return Results.NotFound($"Report file not found: {fileName}");

                // Read and return the file content
                try
                {
                    string content = File.ReadAllText(filePath);
                    return Results.Content(content, "text/html");
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error reading report file: {ex.Message}");
                }
            })
            .Accepts<ReportRequest>("application/json")
            .WithName("GetReport")
            .WithDisplayName("Get Report")
            .WithDescription("Retrieves a report file from template_analysis/Reports directory")
            .WithTags("Report");

        }
    }
}
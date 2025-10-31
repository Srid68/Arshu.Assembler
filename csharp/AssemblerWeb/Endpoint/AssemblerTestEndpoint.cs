using Assembler.Common;
using Assembler.Config;
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
using System.Text.Json.Serialization;

namespace AssemblerWeb
{
    #region Model and Serialization
    
    // Model for report request
    public class ReportRequest
    {
        [JsonPropertyName("fileName")]
        public string? FileName { get; set; }
        [JsonPropertyName("useLangPrefix")]
        public bool UseLangPrefix { get; set; }
        [JsonPropertyName("langPrefix")]
        public string? LangPrefix { get; set; }
    }

    // Model for test response
    public class TestResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        [JsonPropertyName("elapsed")]
        public double Elapsed { get; set; }
        [JsonPropertyName("testCount")]
        public int TestCount { get; set; }
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
        public int ScenarioTotalTimeMs { get; set; }
        public int ElapsedTimeMs { get; set; }
    }

    [JsonSerializable(typeof(ReportRequest))]
    [JsonSerializable(typeof(TestResponse))]
    [JsonSerializable(typeof(TestSummaryRowDto[]))]
    [JsonSerializable(typeof(PerfSummaryRowDto[]))]
    public partial class AssemblerTestJsonContext : JsonSerializerContext { }

    #endregion

    public static class AssemblerTestEndpoint
    {
        // Configurable rule groups for consolidated report grouping
        private static readonly string[] RULE_GROUPS = new[]
        {
            "HtmlRule1",
            "HtmlRule2",
            "HtmlRule3",
            "JsonRule1",
            "JsonRule2",
            "Rule1"
        };

        public static void MapAssemblerTestEndpoints(this WebApplication app)
        // POST endpoint for merging templates
        {
            var assemblerTestGroup = app.MapGroup("")
                .WithTags("AssemblerTest");

            #region Test Standard Endpoint

            // Test endpoints
            assemblerTestGroup.MapPost("/test/standard", (HttpContext context) =>
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

                Logger.Configure(Logger.LogLevel.DEBUG, consoleOutput: false, Logger.LogRotation.HOURLY);
                Logger.AddContextLogFiles(contextLogFiles);

                try
                {
                    var scenarios = ConfigUtil.GetScenarios();
                    var summaryRows = TestingUtils.RunStandardTests(rootDirPath, projectDirectory, scenarios, false, true, true);
                    if (summaryRows != null && summaryRows.Count > 0)
                    {
                        TestingUtils.PrintTestSummaryTable(rootDirPath, projectDirectory, summaryRows, "STANDARD TEST");
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

                    return Results.Json(response, AssemblerTestJsonContext.Default.TestResponse);
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

            #endregion

            #region Test Advanced Endpoint

            assemblerTestGroup.MapPost("/test/advanced", (HttpContext context) =>
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

                Logger.Configure(Logger.LogLevel.DEBUG, consoleOutput: false, Logger.LogRotation.HOURLY);
                Logger.AddContextLogFiles(contextLogFiles);

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

                    return Results.Json(response, AssemblerTestJsonContext.Default.TestResponse);
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

            #endregion

            #region Test Performance Endpoint

            assemblerTestGroup.MapPost("/test/performance", (HttpContext context) =>
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

                    return Results.Json(response, AssemblerTestJsonContext.Default.TestResponse);
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

            #endregion

            #region Consolidate Performance Endpoint

            assemblerTestGroup.MapPost("/test/consolidate-performance", async (HttpContext context) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    string rootDirPath = Path.Combine(context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
                    string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;

                    // Configure logging for consolidate endpoint
                    var templateAnalysisDir = Path.Combine(projectDirectory, "template_analysis");
                    var logsDir = Path.Combine(templateAnalysisDir, "logs");
                    Directory.CreateDirectory(logsDir);
                    var consolidateLogFile = Path.Combine(logsDir, "csharp_consolidate_perf.log");

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

                        return Results.Json(errorResponse, AssemblerTestJsonContext.Default.TestResponse);
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
                                    var requestBody = System.Text.Json.JsonSerializer.Serialize(reportRequest, AssemblerTestJsonContext.Default.ReportRequest);
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
                    htmlSb.AppendLine("        .worst-perf { background-color: #FFB6C6; font-weight: bold; }");
                    htmlSb.AppendLine("        .avg-perf { background-color: #FFD700; font-weight: bold; }");
                    htmlSb.AppendLine("        .legend { display: flex; gap: 20px; margin: 20px 0; flex-wrap: wrap; }");
                    htmlSb.AppendLine("        .legend-item { display: flex; align-items: center; gap: 8px; }");
                    htmlSb.AppendLine("        .legend-box { width: 24px; height: 24px; border: 1px solid #999; }");
                    htmlSb.AppendLine("        .view-toggle { margin: 20px 0; }");
                    htmlSb.AppendLine("        .view-btn { padding: 10px 20px; margin-right: 10px; cursor: pointer; border: 2px solid #4CAF50; background: white; color: #4CAF50; font-size: 14px; border-radius: 5px; }");
                    htmlSb.AppendLine("        .view-btn.active { background: #4CAF50; color: white; }");
                    htmlSb.AppendLine("        .view-content { display: none; }");
                    htmlSb.AppendLine("        .view-content.active { display: block; }");
                    htmlSb.AppendLine("        .chart-container { margin: 20px 0; }");
                    htmlSb.AppendLine("        .chart-row { margin-bottom: 25px; }");
                    htmlSb.AppendLine("        .chart-label { font-weight: bold; margin-bottom: 8px; font-size: 14px; color: #333; }");
                    htmlSb.AppendLine("        .chart-bars-container { display: flex; flex-direction: column; gap: 8px; }");
                    htmlSb.AppendLine("        .chart-bar-wrapper { display: flex; align-items: center; gap: 10px; }");
                    htmlSb.AppendLine("        .chart-bar-label { min-width: 80px; font-weight: 600; color: #555; font-size: 13px; }");
                    htmlSb.AppendLine("        .chart-bar { height: 30px; border-radius: 5px; display: flex; align-items: center; justify-content: flex-end; padding-right: 10px; color: white; font-weight: bold; font-size: 12px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); transition: transform 0.2s; min-width: 40px; }");
                    htmlSb.AppendLine("        .chart-bar:hover { transform: translateX(5px); box-shadow: 0 4px 8px rgba(0,0,0,0.15); }");
                    htmlSb.AppendLine("        .chart-bar-value { margin-left: 10px; font-weight: 600; color: #333; font-size: 13px; min-width: 60px; }");
                    htmlSb.AppendLine("        .grouped-chart-section { margin-bottom: 40px; padding: 20px; background: #f9f9f9; border-radius: 8px; }");
                    htmlSb.AppendLine("        .grouped-chart-title { font-size: 1.3em; font-weight: bold; color: #667eea; margin-bottom: 15px; border-bottom: 2px solid #667eea; padding-bottom: 8px; }");
                    htmlSb.AppendLine("        .grouped-bar-group { display: flex; align-items: center; margin-bottom: 20px; }");
                    htmlSb.AppendLine("        .grouped-bar-label { min-width: 100px; font-weight: 600; color: #333; font-size: 13px; }");
                    htmlSb.AppendLine("        .grouped-bars { flex: 1; display: flex; flex-direction: column; gap: 4px; }");
                    htmlSb.AppendLine("        .grouped-bar-item { display: flex; align-items: center; gap: 8px; }");
                    htmlSb.AppendLine("        .grouped-bar { height: 24px; border-radius: 4px; display: flex; align-items: center; justify-content: flex-end; padding-right: 8px; color: white; font-weight: bold; font-size: 11px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); min-width: 30px; }");
                    htmlSb.AppendLine("        .grouped-lang-label { min-width: 60px; font-size: 12px; color: #666; }");
                    htmlSb.AppendLine("    </style>");
                    htmlSb.AppendLine("</head>");
                    htmlSb.AppendLine("<body>");
                    htmlSb.AppendLine("    <h1>Consolidated Performance Summary</h1>");
                    htmlSb.AppendLine($"    <div class=\"meta\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>");
                    htmlSb.AppendLine("    <div class=\"legend\">");
                    htmlSb.AppendLine("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #4CAF50; opacity: 0.8;\"></div><span>Normal Engine (N)</span></div>");
                    htmlSb.AppendLine("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #2196F3; opacity: 0.8;\"></div><span>PreProcess Engine (P)</span></div>");
                    htmlSb.AppendLine("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #90EE90;\"></div><span>Best (Lowest Time - Table View)</span></div>");
                    htmlSb.AppendLine("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #FFD700;\"></div><span>Nearest to Average (Table View)</span></div>");
                    htmlSb.AppendLine("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #FFB6C6;\"></div><span>Worst (Highest Time - Table View)</span></div>");
                    htmlSb.AppendLine("    </div>");
                    htmlSb.AppendLine("    <div class=\"view-toggle\">");
                    htmlSb.AppendLine("        <button class=\"view-btn active\" data-view=\"grouped\">Grouped View</button>");
                    htmlSb.AppendLine("        <button class=\"view-btn\" data-view=\"chart\">Bar Chart View</button>");
                    htmlSb.AppendLine("        <button class=\"view-btn\" data-view=\"table\">Table View</button>");
                    htmlSb.AppendLine("    </div>");

                    // Get list of languages dynamically from configuration
                    var languages = serversByLang.Select(g => g.Key).OrderBy(l => l).ToList();

                    #region Combined Bar Chart View

                    // Combined Chart View (Normal + PreProcess)
                    htmlSb.AppendLine("    <div id=\"combined-chart\" class=\"view-content\">");
                    htmlSb.AppendLine("        <div class=\"chart-container\">");

                    // Generate combined chart data showing both engines (filter by rule groups)
                    var filteredApps = appPerf.Keys
                        .Where(app => RULE_GROUPS.Any(rule => app.StartsWith(rule)))
                        .OrderBy(k => k);

                    foreach (var app in filteredApps)
                    {
                        htmlSb.AppendLine("            <div class=\"chart-row\">");
                        htmlSb.AppendLine($"                <div class=\"chart-label\">{app}</div>");
                        htmlSb.AppendLine("                <div class=\"chart-bars-container\">");

                        // Calculate max time across BOTH engines for consistent scaling
                        var allTimes = new List<double>();
                        foreach (var lang in languages)
                        {
                            if (appPerf[app].ContainsKey(lang))
                            {
                                if (appPerf[app][lang].NormalTimeMs.HasValue && appPerf[app][lang].NormalTimeMs!.Value > 0)
                                    allTimes.Add(appPerf[app][lang].NormalTimeMs!.Value);
                                if (appPerf[app][lang].PreProcessTimeMs.HasValue && appPerf[app][lang].PreProcessTimeMs!.Value > 0)
                                    allTimes.Add(appPerf[app][lang].PreProcessTimeMs!.Value);
                            }
                        }
                        var maxTimeForScale = allTimes.Any() ? allTimes.Max() : 1.0;

                        // Calculate highlighting for Normal Engine
                        var normalValidTimes = languages
                            .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue && appPerf[app][lang].NormalTimeMs!.Value > 0)
                            .Select(lang => appPerf[app][lang].NormalTimeMs!.Value)
                            .ToList();
                        var normalMinTime = normalValidTimes.Any() ? normalValidTimes.Min() : (double?)null;
                        var normalMaxTime = normalValidTimes.Any() ? normalValidTimes.Max() : (double?)null;
                        var normalAvgTime = normalValidTimes.Any() ? normalValidTimes.Average() : (double?)null;

                        // Calculate highlighting for PreProcess Engine
                        var preprocessValidTimes = languages
                            .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue && appPerf[app][lang].PreProcessTimeMs!.Value > 0)
                            .Select(lang => appPerf[app][lang].PreProcessTimeMs!.Value)
                            .ToList();
                        var preprocessMinTime = preprocessValidTimes.Any() ? preprocessValidTimes.Min() : (double?)null;
                        var preprocessMaxTime = preprocessValidTimes.Any() ? preprocessValidTimes.Max() : (double?)null;
                        var preprocessAvgTime = preprocessValidTimes.Any() ? preprocessValidTimes.Average() : (double?)null;

                        foreach (var lang in languages)
                        {
                            if (appPerf[app].ContainsKey(lang))
                            {
                                var normalTime = appPerf[app][lang].NormalTimeMs;
                                var preprocessTime = appPerf[app][lang].PreProcessTimeMs;

                                if ((normalTime.HasValue && normalTime.Value > 0) || (preprocessTime.HasValue && preprocessTime.Value > 0))
                                {
                                    htmlSb.AppendLine("                    <div class=\"chart-bar-wrapper\">");
                                    htmlSb.AppendLine($"                        <div class=\"chart-bar-label\">{lang}</div>");

                                    // Container for overlapping bars (both start from 0) - with overflow visible for labels
                                    htmlSb.AppendLine("                        <div style=\"position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;\">");

                                    // Normal Engine Bar (bottom layer) with label at the end
                                    if (normalTime.HasValue && normalTime.Value > 0)
                                    {
                                        var widthPercent = (normalTime.Value / maxTimeForScale) * 100;

                                        // Determine highlight color
                                        var normalBgColor = "#4CAF50"; // default green
                                        if (normalMinTime.HasValue && Math.Abs(normalTime.Value - normalMinTime.Value) < 0.01)
                                            normalBgColor = "#90EE90"; // best (light green)
                                        else if (normalMaxTime.HasValue && Math.Abs(normalTime.Value - normalMaxTime.Value) < 0.01)
                                            normalBgColor = "#FFB6C6"; // worst (light red)
                                        else if (normalAvgTime.HasValue && normalValidTimes.Count > 2)
                                        {
                                            var nearestToAvg = normalValidTimes.OrderBy(t => Math.Abs(t - normalAvgTime.Value)).First();
                                            if (Math.Abs(normalTime.Value - nearestToAvg) < 0.01)
                                                normalBgColor = "#FFD700"; // avg (gold)
                                        }

                                        // Position label: inside bar if very wide (>85%), otherwise outside at end
                                        var normalLabelStyle = widthPercent > 85
                                            ? $"position: absolute; right: calc(100% - {widthPercent}% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;"
                                            : $"position: absolute; left: calc({widthPercent}% + 5px); top: 0; font-size: 11px; color: {normalBgColor}; font-weight: 600; white-space: nowrap;";
                                        htmlSb.AppendLine($"                            <div style=\"position: absolute; left: 0; top: 0; width: {widthPercent}%; height: 15px; background-color: {normalBgColor}; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{lang} Normal: {normalTime.Value:F2}ms\"></div>");
                                        htmlSb.AppendLine($"                            <span style=\"{normalLabelStyle}\">N: {normalTime.Value:F2}ms</span>");
                                    }

                                    // PreProcess Engine Bar (top layer, slightly offset) with label at the end
                                    if (preprocessTime.HasValue && preprocessTime.Value > 0)
                                    {
                                        var widthPercent = (preprocessTime.Value / maxTimeForScale) * 100;

                                        // Determine highlight color
                                        var preprocessBgColor = "#2196F3"; // default blue
                                        if (preprocessMinTime.HasValue && Math.Abs(preprocessTime.Value - preprocessMinTime.Value) < 0.01)
                                            preprocessBgColor = "#90EE90"; // best (light green)
                                        else if (preprocessMaxTime.HasValue && Math.Abs(preprocessTime.Value - preprocessMaxTime.Value) < 0.01)
                                            preprocessBgColor = "#FFB6C6"; // worst (light red)
                                        else if (preprocessAvgTime.HasValue && preprocessValidTimes.Count > 2)
                                        {
                                            var nearestToAvg = preprocessValidTimes.OrderBy(t => Math.Abs(t - preprocessAvgTime.Value)).First();
                                            if (Math.Abs(preprocessTime.Value - nearestToAvg) < 0.01)
                                                preprocessBgColor = "#FFD700"; // avg (gold)
                                        }

                                        // Position label: inside bar if very wide (>85%), otherwise outside at end
                                        var preprocessLabelStyle = widthPercent > 85
                                            ? $"position: absolute; right: calc(100% - {widthPercent}% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;"
                                            : $"position: absolute; left: calc({widthPercent}% + 5px); top: 15px; font-size: 11px; color: {preprocessBgColor}; font-weight: 600; white-space: nowrap;";
                                        htmlSb.AppendLine($"                            <div style=\"position: absolute; left: 0; top: 15px; width: {widthPercent}%; height: 15px; background-color: {preprocessBgColor}; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{lang} PreProcess: {preprocessTime.Value:F2}ms\"></div>");
                                        htmlSb.AppendLine($"                            <span style=\"{preprocessLabelStyle}\">P: {preprocessTime.Value:F2}ms</span>");
                                    }

                                    htmlSb.AppendLine("                        </div>");
                                    htmlSb.AppendLine("                    </div>");
                                }
                            }
                        }

                        htmlSb.AppendLine("                </div>");
                        htmlSb.AppendLine("            </div>");
                    }

                    htmlSb.AppendLine("        </div>");
                    htmlSb.AppendLine("    </div>");

                    #endregion

                    #region Grouped Bar Chart View

                    // Grouped Chart View (Group by configured rule groups)
                    htmlSb.AppendLine("    <div id=\"combined-grouped\" class=\"view-content active\">");
                    htmlSb.AppendLine("        <div class=\"chart-container\">");

                    foreach (var rulePattern in RULE_GROUPS)
                    {
                        // Find all apps matching this rule pattern (excluding Test AppSite for now)
                        var matchingApps = appPerf.Keys
                            .Where(app => app.StartsWith(rulePattern) && !app.Contains("Test"))
                            .OrderBy(app => app)
                            .ToList();

                        if (!matchingApps.Any()) continue;

                        htmlSb.AppendLine("            <div class=\"grouped-chart-section\">");
                        htmlSb.AppendLine($"                <div class=\"grouped-chart-title\">{rulePattern}</div>");
                        htmlSb.AppendLine("                <div class=\"chart-bars-container\">");

                        // Calculate max time across ALL languages in this rule group for consistent scaling
                        var allMaxValues = new List<double>();
                        foreach (var lang in languages)
                        {
                            var normalTimes = matchingApps
                                .Where(app => appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue && appPerf[app][lang].NormalTimeMs!.Value > 0)
                                .Select(app => appPerf[app][lang].NormalTimeMs!.Value)
                                .ToList();
                            var preprocessTimes = matchingApps
                                .Where(app => appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue && appPerf[app][lang].PreProcessTimeMs!.Value > 0)
                                .Select(app => appPerf[app][lang].PreProcessTimeMs!.Value)
                                .ToList();

                            if (normalTimes.Any()) allMaxValues.Add(normalTimes.Max());
                            if (preprocessTimes.Any()) allMaxValues.Add(preprocessTimes.Max());
                        }
                        var maxTimeForScale = allMaxValues.Any() ? allMaxValues.Max() : 1.0;

                        // For each language, calculate min/avg/max across all apps in this rule group
                        foreach (var lang in languages)
                        {
                            // Collect Normal Engine times for this language across all apps in the group
                            var normalTimes = matchingApps
                                .Where(app => appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue && appPerf[app][lang].NormalTimeMs!.Value > 0)
                                .Select(app => appPerf[app][lang].NormalTimeMs!.Value)
                                .ToList();

                            // Collect PreProcess Engine times for this language across all apps in the group
                            var preprocessTimes = matchingApps
                                .Where(app => appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue && appPerf[app][lang].PreProcessTimeMs!.Value > 0)
                                .Select(app => appPerf[app][lang].PreProcessTimeMs!.Value)
                                .ToList();

                            if (!normalTimes.Any() && !preprocessTimes.Any()) continue;

                            // Calculate aggregates
                            var normalMin = normalTimes.Any() ? normalTimes.Min() : (double?)null;
                            var normalAvg = normalTimes.Any() ? normalTimes.Average() : (double?)null;
                            var normalMax = normalTimes.Any() ? normalTimes.Max() : (double?)null;

                            var preprocessMin = preprocessTimes.Any() ? preprocessTimes.Min() : (double?)null;
                            var preprocessAvg = preprocessTimes.Any() ? preprocessTimes.Average() : (double?)null;
                            var preprocessMax = preprocessTimes.Any() ? preprocessTimes.Max() : (double?)null;

                            htmlSb.AppendLine("                    <div class=\"chart-bar-wrapper\">");
                            htmlSb.AppendLine($"                        <div class=\"chart-bar-label\">{lang}</div>");

                            // Container for overlapping bars
                            htmlSb.AppendLine("                        <div style=\"position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;\">");

                            // Normal Engine Bar (showing min, avg, max as segments)
                            if (normalMin.HasValue && normalAvg.HasValue && normalMax.HasValue)
                            {
                                var minWidth = (normalMin.Value / maxTimeForScale) * 100;
                                var avgWidth = (normalAvg.Value / maxTimeForScale) * 100;
                                var maxWidth = (normalMax.Value / maxTimeForScale) * 100;

                                // Draw max bar (light green background)
                                htmlSb.AppendLine($"                            <div style=\"position: absolute; left: 0; top: 0; width: {maxWidth}%; height: 15px; background-color: #90EE90; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{lang} Normal Max: {normalMax.Value:F2}ms\"></div>");

                                // Draw avg bar (gold - middle layer)
                                htmlSb.AppendLine($"                            <div style=\"position: absolute; left: 0; top: 0; width: {avgWidth}%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{lang} Normal Avg: {normalAvg.Value:F2}ms\"></div>");

                                // Draw min bar (dark green - top layer)
                                htmlSb.AppendLine($"                            <div style=\"position: absolute; left: 0; top: 0; width: {minWidth}%; height: 15px; background-color: #4CAF50; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{lang} Normal Min: {normalMin.Value:F2}ms\"></div>");

                                // Label at end of max bar
                                var labelStyle = maxWidth > 85
                                    ? $"position: absolute; right: calc(100% - {maxWidth}% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;"
                                    : $"position: absolute; left: calc({maxWidth}% + 5px); top: 0; font-size: 11px; color: #4CAF50; font-weight: 600; white-space: nowrap;";
                                htmlSb.AppendLine($"                            <span style=\"{labelStyle}\">N: {normalMin.Value:F2}/{normalAvg.Value:F2}/{normalMax.Value:F2}</span>");
                            }

                            // PreProcess Engine Bar (showing min, avg, max as segments)
                            if (preprocessMin.HasValue && preprocessAvg.HasValue && preprocessMax.HasValue)
                            {
                                var minWidth = (preprocessMin.Value / maxTimeForScale) * 100;
                                var avgWidth = (preprocessAvg.Value / maxTimeForScale) * 100;
                                var maxWidth = (preprocessMax.Value / maxTimeForScale) * 100;

                                // Draw max bar (light pink background)
                                htmlSb.AppendLine($"                            <div style=\"position: absolute; left: 0; top: 15px; width: {maxWidth}%; height: 15px; background-color: #FFB6C6; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{lang} PreProcess Max: {preprocessMax.Value:F2}ms\"></div>");

                                // Draw avg bar (gold - middle layer)
                                htmlSb.AppendLine($"                            <div style=\"position: absolute; left: 0; top: 15px; width: {avgWidth}%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{lang} PreProcess Avg: {preprocessAvg.Value:F2}ms\"></div>");

                                // Draw min bar (dark blue - top layer)
                                htmlSb.AppendLine($"                            <div style=\"position: absolute; left: 0; top: 15px; width: {minWidth}%; height: 15px; background-color: #2196F3; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{lang} PreProcess Min: {preprocessMin.Value:F2}ms\"></div>");

                                // Label at end of max bar
                                var labelStyle = maxWidth > 85
                                    ? $"position: absolute; right: calc(100% - {maxWidth}% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;"
                                    : $"position: absolute; left: calc({maxWidth}% + 5px); top: 15px; font-size: 11px; color: #2196F3; font-weight: 600; white-space: nowrap;";
                                htmlSb.AppendLine($"                            <span style=\"{labelStyle}\">P: {preprocessMin.Value:F2}/{preprocessAvg.Value:F2}/{preprocessMax.Value:F2}</span>");
                            }

                            htmlSb.AppendLine("                        </div>");
                            htmlSb.AppendLine("                    </div>");
                        }

                        htmlSb.AppendLine("                </div>");
                        htmlSb.AppendLine("            </div>");
                    }

                    htmlSb.AppendLine("        </div>");
                    htmlSb.AppendLine("    </div>");

                    #endregion

                    #region Normal Engine Table View

                    // Table View - Normal Engine
                    htmlSb.AppendLine("    <div id=\"normal-table\" class=\"view-content\">");
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
                    foreach (var app in appPerf.Keys.Where(app => RULE_GROUPS.Any(rule => app.StartsWith(rule))).OrderBy(k => k))
                    {
                        // Find min, max, and avg time for highlighting (excluding zero values)
                        var validTimes = languages
                            .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue && appPerf[app][lang].NormalTimeMs!.Value > 0)
                            .Select(lang => appPerf[app][lang].NormalTimeMs!.Value)
                            .ToList();
                        var minTime = validTimes.Any() ? validTimes.Min() : (double?)null;
                        var maxTime = validTimes.Any() ? validTimes.Max() : (double?)null;
                        var avgTime = validTimes.Any() ? validTimes.Average() : (double?)null;

                        htmlSb.AppendLine("        <tr>");
                        htmlSb.AppendLine($"            <td>{app}</td>");
                        foreach (var lang in languages)
                        {
                            var timeValue = appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue
                                ? appPerf[app][lang].NormalTimeMs!.Value.ToString("F2")
                                : "-";

                            var cssClass = "";
                            if (appPerf[app].ContainsKey(lang) && appPerf[app][lang].NormalTimeMs.HasValue && appPerf[app][lang].NormalTimeMs!.Value > 0)
                            {
                                var currentTime = appPerf[app][lang].NormalTimeMs!.Value;
                                if (minTime.HasValue && Math.Abs(currentTime - minTime.Value) < 0.001)
                                {
                                    cssClass = " class=\"best-perf\"";
                                }
                                else if (maxTime.HasValue && Math.Abs(currentTime - maxTime.Value) < 0.001)
                                {
                                    cssClass = " class=\"worst-perf\"";
                                }
                                else if (avgTime.HasValue && validTimes.Count > 2)
                                {
                                    // Find the value nearest to average
                                    var nearestToAvg = validTimes.OrderBy(t => Math.Abs(t - avgTime.Value)).First();
                                    if (Math.Abs(currentTime - nearestToAvg) < 0.001)
                                    {
                                        cssClass = " class=\"avg-perf\"";
                                    }
                                }
                            }

                            htmlSb.AppendLine($"            <td{cssClass}>{timeValue}</td>");
                        }
                        var outputSizeTuple = appPerf[app].Values.Where(v => v.OutputSize.HasValue).FirstOrDefault();
                        var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
                        htmlSb.AppendLine($"            <td>{outputSize}</td>");
                        htmlSb.AppendLine("        </tr>");
                    }
                    htmlSb.AppendLine("    </table>");
                    htmlSb.AppendLine("    </div>");
                    htmlSb.AppendLine("    </div>");

                    #endregion

                    #region PreProcess Engine Table View

                    // PreProcess Engine Table
                    htmlSb.AppendLine("    <div id=\"preprocess-table\" class=\"view-content\">");
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
                    foreach (var app in appPerf.Keys.Where(app => RULE_GROUPS.Any(rule => app.StartsWith(rule))).OrderBy(k => k))
                    {
                        // Find min, max, and avg time for highlighting (excluding zero values)
                        var validTimes = languages
                            .Where(lang => appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue && appPerf[app][lang].PreProcessTimeMs!.Value > 0)
                            .Select(lang => appPerf[app][lang].PreProcessTimeMs!.Value)
                            .ToList();
                        var minTime = validTimes.Any() ? validTimes.Min() : (double?)null;
                        var maxTime = validTimes.Any() ? validTimes.Max() : (double?)null;
                        var avgTime = validTimes.Any() ? validTimes.Average() : (double?)null;

                        htmlSb.AppendLine("        <tr>");
                        htmlSb.AppendLine($"            <td>{app}</td>");

                        foreach (var lang in languages)
                        {
                            var timeValue = appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue
                                ? appPerf[app][lang].PreProcessTimeMs!.Value.ToString("F2")
                                : "-";

                            var cssClass = "";
                            if (appPerf[app].ContainsKey(lang) && appPerf[app][lang].PreProcessTimeMs.HasValue && appPerf[app][lang].PreProcessTimeMs!.Value > 0)
                            {
                                var currentTime = appPerf[app][lang].PreProcessTimeMs!.Value;
                                if (minTime.HasValue && Math.Abs(currentTime - minTime.Value) < 0.001)
                                {
                                    cssClass = " class=\"best-perf\"";
                                }
                                else if (maxTime.HasValue && Math.Abs(currentTime - maxTime.Value) < 0.001)
                                {
                                    cssClass = " class=\"worst-perf\"";
                                }
                                else if (avgTime.HasValue && validTimes.Count > 2)
                                {
                                    var nearestToAvg = validTimes.OrderBy(t => Math.Abs(t - avgTime.Value)).First();
                                    if (Math.Abs(currentTime - nearestToAvg) < 0.001)
                                    {
                                        cssClass = " class=\"avg-perf\"";
                                    }
                                }
                            }

                            htmlSb.AppendLine($"            <td{cssClass}>{timeValue}</td>");
                        }
                        var outputSizeTuple = appPerf[app].Values.Where(v => v.OutputSize.HasValue).FirstOrDefault();
                        var outputSize = outputSizeTuple.OutputSize.HasValue ? outputSizeTuple.OutputSize.Value.ToString() : "-";
                        htmlSb.AppendLine($"            <td>{outputSize}</td>");
                        htmlSb.AppendLine("        </tr>");
                    }
                    htmlSb.AppendLine("    </table>");
                    htmlSb.AppendLine("    </div>");
                    htmlSb.AppendLine("    </div>");

                    #endregion

                    htmlSb.AppendLine("</body>");
                    htmlSb.AppendLine("</html>");

                    var htmlContent = htmlSb.ToString();

                    // Write HTML to Reports directory
                    var reportsDir = Path.Combine(projectDirectory, "template_analysis", "Reports");
                    Directory.CreateDirectory(reportsDir);
                    var htmlPath = Path.Combine(reportsDir, "all_perf_tests.html");
                    await File.WriteAllTextAsync(htmlPath, htmlContent);

                    sw.Stop();
                    var elapsedSeconds = sw.Elapsed.TotalSeconds;

                    // Log completion
                    var totalLanguages = serversByLang.Count;
                    File.AppendAllText(consolidateLogFile, $"[{DateTime.UtcNow:O}] Consolidation complete in {elapsedSeconds:F2}s - {appPerf.Count} AppSites from {serversProcessed.Count}/{totalLanguages} languages\n");

                    // Build detailed message
                    var messageBuilder = new System.Text.StringBuilder();
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
                        Success = serversProcessed.Count > 0,
                        Message = messageBuilder.ToString(),
                        Elapsed = elapsedSeconds,
                        TestCount = serversProcessed.Count
                    };

                    return Results.Json(response, AssemblerTestJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Get Report Endpoint

            assemblerTestGroup.MapPost("/api/report", async (HttpContext context) =>
            {
                try
                {
                    using var reader = new StreamReader(context.Request.Body);
                    var requestBody = await reader.ReadToEndAsync();

                    var fileName = "";
                    var useLangPrefix = false;
                    var langPrefix = "";

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
                        if (jsonDoc.RootElement.TryGetProperty("langPrefix", out var langPrefixElement))
                        {
                            langPrefix = langPrefixElement.GetString() ?? "";
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

                    // Validate langPrefix for path traversal if provided
                    if (!string.IsNullOrEmpty(langPrefix) && !SecurityValidator.IsValidPathComponent(langPrefix))
                        return Results.Text("Invalid characters in langPrefix", statusCode: 400);

                    // Construct file path
                    string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                    var prefix = useLangPrefix && !string.IsNullOrEmpty(langPrefix) ? langPrefix + "_" : "";
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



            #region Save Log Endpoint

            assemblerTestGroup.MapPost("/api/save-log", async (HttpContext context) =>
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
                            logContent = (contentElement.GetString() ?? "").Trim();
                        }
                    }
                    catch
                    {
                        return Results.Text("Invalid JSON format", statusCode: 400);
                    }

                    if (string.IsNullOrEmpty(contextName))
                        return Results.Text("Missing context parameter", statusCode: 400);

                    // If content is empty after trimming, return success without saving
                    if (string.IsNullOrEmpty(logContent))
                    {
                        var emptyResponse = new TestResponse
                        {
                            Message = "No content to save"
                        };
                        return Results.Json(emptyResponse, AssemblerTestJsonContext.Default.TestResponse);
                    }

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
                    return Results.Json(response, AssemblerTestJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Save Output Endpoint

            assemblerTestGroup.MapPost("/api/save-output", async (HttpContext context) =>
            {
                try
                {
                    Console.WriteLine($"[/api/save-output] Endpoint called");
                    using var reader = new StreamReader(context.Request.Body);
                    var requestBody = await reader.ReadToEndAsync();
                    Console.WriteLine($"[/api/save-output] Request body length: {requestBody.Length}");

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
                        Console.WriteLine($"[/api/save-output] Parsed: appSite={appSite}, appView={appView}, engineType={engineType}, htmlLength={htmlContent.Length}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[/api/save-output] JSON parse error: {ex.Message}");
                        return Results.Text("Invalid JSON format", statusCode: 400);
                    }

                    if (string.IsNullOrEmpty(appSite) || string.IsNullOrEmpty(engineType) || string.IsNullOrEmpty(htmlContent))
                    {
                        Console.WriteLine($"[/api/save-output] Missing parameters: appSite={appSite}, engineType={engineType}, htmlLength={htmlContent.Length}");
                        return Results.Text("Missing required parameters", statusCode: 400);
                    }

                    // Validate AppSite against allowlist
                    var validAppSites = SecurityValidator.GetValidAppSites();
                    if (!validAppSites.Contains(appSite))
                    {
                        Console.WriteLine($"[/api/save-output] Invalid AppSite: {appSite}");
                        return Results.Text("Invalid AppSite value", statusCode: 400);
                    }

                    // Validate engine type against allowlist
                    if (!SecurityValidator.ValidEngineTypes.Contains(engineType))
                    {
                        Console.WriteLine($"[/api/save-output] Invalid engineType: {engineType}");
                        return Results.Text("Invalid engine type", statusCode: 400);
                    }

                    // Validate parameters (256 char limit, no path traversal)
                    if (!SecurityValidator.IsValidPathComponent(appSite))
                    {
                        Console.WriteLine($"[/api/save-output] Invalid AppSite path component: {appSite}");
                        return Results.Text("Invalid AppSite parameter", statusCode: 400);
                    }
                    if (!string.IsNullOrEmpty(appView) && !SecurityValidator.IsValidPathComponent(appView))
                    {
                        Console.WriteLine($"[/api/save-output] Invalid AppView path component: {appView}");
                        return Results.Text("Invalid AppView parameter", statusCode: 400);
                    }
                    if (!SecurityValidator.IsValidPathComponent(engineType))
                    {
                        Console.WriteLine($"[/api/save-output] Invalid engineType path component: {engineType}");
                        return Results.Text("Invalid engineType parameter", statusCode: 400);
                    }

                    // Validate output size against template size + buffer
                    var templateTotalSize = SecurityValidator.GetTemplateTotalSize(appSite, appView ?? "");
                    var outputSize = System.Text.Encoding.UTF8.GetByteCount(htmlContent);
                    var maxAllowedSize = templateTotalSize + SecurityValidator.OutputSizeBuffer;
                    Console.WriteLine($"[/api/save-output] Size validation: output={outputSize:N0}, template={templateTotalSize:N0}, buffer={SecurityValidator.OutputSizeBuffer:N0}, max={maxAllowedSize:N0}");
                    if (!SecurityValidator.IsValidOutputSizeWithBuffer(htmlContent, templateTotalSize))
                    {
                        var errorMsg = $"Save output failed: output size ({outputSize:N0} bytes) exceeds max size allowed ({maxAllowedSize:N0} bytes = template {templateTotalSize:N0} + buffer {SecurityValidator.OutputSizeBuffer:N0})";
                        Console.WriteLine($"[/api/save-output] {errorMsg}");
                        return Results.Text(errorMsg, statusCode: 400);
                    }

                    string projectDirectory = context.RequestServices.GetRequiredService<IHostEnvironment>().ContentRootPath;
                    var outputDir = Path.Combine(projectDirectory, "template_analysis", "output");
                    Directory.CreateDirectory(outputDir);

                    var appViewSuffix = string.IsNullOrEmpty(appView) ? "" : $"_{appView}";
                    var engineSuffix = engineType.ToLower();
                    var outputFile = Path.Combine(outputDir, $"javascript_{appSite}{appViewSuffix}_{engineSuffix}.html");
                    await File.WriteAllTextAsync(outputFile, htmlContent);
                    Console.WriteLine($"[/api/save-output] Success! Output saved to: {outputFile}");

                    var response = new TestResponse
                    {
                        Message = "Output saved successfully"
                    };
                    return Results.Json(response, AssemblerTestJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Save Test Results Endpoint

            assemblerTestGroup.MapPost("/api/test-results", async (HttpContext context, TestSummaryRowDto[] summaryRows) =>
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
                    return Results.Json(response, AssemblerTestJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in /api/test-results: {ex.Message}");
                    return Results.Text($"Error: {ex.Message}", statusCode: 500);
                }
            });

            #endregion

            #region Save Performance Results Endpoint

            assemblerTestGroup.MapPost("/api/performance-results", async (HttpContext context, PerfSummaryRowDto[] summaryRows) =>
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
                    html.AppendLine($"<div class=\"meta\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>");
                    html.AppendLine("<table>");
                    html.AppendLine("<tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th><th>ScnTime(ms)</th><th>Elapsed(ms)</th></tr>");

                    foreach (var row in summaryRows)
                    {
                        html.AppendLine("<tr>");
                        html.AppendLine($"<td>{row.AppSite}</td>");
                        html.AppendLine($"<td>{row.AppView}</td>");
                        html.AppendLine($"<td>{row.NormalTimeMs:F2}</td>");
                        html.AppendLine($"<td>{row.PreProcessTimeMs:F2}</td>");
                        html.AppendLine($"<td>{row.ResultsMatch}</td>");
                        html.AppendLine($"<td>{row.PerfDifference}</td>");
                        html.AppendLine($"<td>{row.ScenarioTotalTimeMs}</td>");
                        html.AppendLine($"<td>{row.ElapsedTimeMs}</td>");
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
                    return Results.Json(response, AssemblerTestJsonContext.Default.TestResponse);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in /api/performance-results: {ex.Message}");
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
                AppendJsonProperty(sb, "PerfDifference", row.PerfDifference, indented, false);
                AppendJsonProperty(sb, "ScenarioTotalTimeMs", row.ScenarioTotalTimeMs.ToString(), indented, false, false);
                AppendJsonProperty(sb, "ElapsedTimeMs", row.ElapsedTimeMs.ToString(), indented, true, false);

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
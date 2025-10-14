package main

import (
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"

	"github.com/gin-gonic/gin"

	"assembler/common"
	"assembler/config"
	"assembler/engine"
	"assembler/loader"
	"assembler/performance"
	"assembler/test"
)

// scenarios handles the GET /api/scenarios endpoint
func scenarios(c *gin.Context) {
	allScenarios, err := config.GetScenarios()
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Error loading scenarios: " + err.Error()})
		return
	}

	type ScenarioDto struct {
		AppSite     string `json:"appSite"`
		AppFile     string `json:"appFile"`
		AppView     string `json:"appView"`
		DisplayName string `json:"displayName"`
		Description string `json:"description"`
	}

	var scenarioDtos []ScenarioDto
	for _, s := range allScenarios {
		scenarioDtos = append(scenarioDtos, ScenarioDto{
			AppSite:     s.AppSite,
			AppFile:     s.AppFile,
			AppView:     s.AppView,
			DisplayName: s.DisplayName,
			Description: s.Description,
		})
	}

	c.JSON(http.StatusOK, scenarioDtos)
}

// PerfData represents performance data for consolidation
type PerfData struct {
	NormalTimeMs     *float64
	PreProcessTimeMs *float64
	OutputSize       *int
	AppView          string
}

// MergeRequest represents the request structure for template merging
type MergeRequest struct {
	AppSite       *string `json:"appSite" binding:"required"`
	AppView       *string `json:"appView"`
	AppViewPrefix *string `json:"appViewPrefix"`
	EngineType    *string `json:"engineType" binding:"required"`
}

// index handles the GET / endpoint
func index(c *gin.Context) {
	// Use Index AppSite with engine toggle parameter
	assemblerWebDirPath, _ := common.GetAssemblerWebDirPath()
	rootDirPath := assemblerWebDirPath

	// Get engine type from query parameter (default to Normal)
	engineType := c.DefaultQuery("engine", "Normal")

	// Validate EngineType against allowlist
	if !IsValidEngineType(engineType) {
		c.String(http.StatusBadRequest, "Invalid engine type. Use 'Normal' or 'PreProcess'")
		return
	}

	// TEMPORARY: Clear cache for development
	loader.ClearCache()
	loader.ClearPreProcessCache()

	// Load templates for Index AppSite
	normalTemplatesRaw := loader.LoadGetTemplateFiles(rootDirPath, "Index")
	preprocessTemplatesRaw := loader.LoadProcessGetTemplateFiles(rootDirPath, "Index")

	// Merge using selected engine (no AppView context for Index)
	var mergedHtml string
	if strings.EqualFold(engineType, "PreProcess") {
		engine := engine.NewEnginePreProcess("")
		mergedHtml = engine.MergeTemplates("Index", "Index", "", preprocessTemplatesRaw.Templates, true)
	} else {
		engine := engine.NewEngineNormal("")
		mergedHtml = engine.MergeTemplates("Index", "Index", "", normalTemplatesRaw, true)
	}

	c.Header("Content-Type", "text/html")
	c.String(http.StatusOK, mergedHtml)
}

// mergeTemplates handles the POST /merge endpoint
func mergeTemplates(c *gin.Context) {
	var req MergeRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	// Validate required fields
	if req.AppSite == nil || *req.AppSite == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: appSite"})
		return
	}
	if req.EngineType == nil || *req.EngineType == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: engineType"})
		return
	}

	// Get AppFile from scenarios
	allScenarios, err := config.GetScenarios()
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to load scenarios: " + err.Error()})
		return
	}

	appViewValue := safeString(req.AppView)
	var matchingScenario *config.Scenario
	for i := range allScenarios {
		if strings.EqualFold(allScenarios[i].AppSite, *req.AppSite) &&
		   strings.EqualFold(allScenarios[i].AppView, appViewValue) {
			matchingScenario = &allScenarios[i]
			break
		}
	}

	if matchingScenario == nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": fmt.Sprintf("No matching scenario found for AppSite='%s' and AppView='%s'", *req.AppSite, appViewValue)})
		return
	}

	appFile := matchingScenario.AppFile

	logMsg := fmt.Sprintf("/merge endpoint called with: app_site=%v, app_file=%v, engine_type=%v, app_view=%v, app_view_prefix=%v",
		safeString(req.AppSite), appFile, safeString(req.EngineType), safeString(req.AppView), safeString(req.AppViewPrefix))
	fmt.Println(logMsg)
	common.Info(logMsg, "MergeEndpoint")

	// Get wwwroot directory
	assemblerWebDirPath, _ := common.GetAssemblerWebDirPath()
	rootDirPath := assemblerWebDirPath

	// Validate EngineType against allowlist
	if !IsValidEngineType(*req.EngineType) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid EngineType value"})
		return
	}

	// Validate AppSite against allowlist from ConfigUtil
	validAppSites, err := GetValidAppSites()
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to get AppSites: " + err.Error()})
		return
	}

	if !IsValidAppSite(*req.AppSite, validAppSites) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid AppSite value"})
		return
	}

	// Validate path components for path traversal attacks
	if !IsValidPathComponent(req.AppSite) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppSite"})
		return
	}

	// Validate appFile from scenario
	if !IsValidPathComponent(&appFile) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppFile"})
		return
	}

	if req.AppView != nil && *req.AppView != "" && !IsValidPathComponent(req.AppView) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppView"})
		return
	}

	if req.AppViewPrefix != nil && *req.AppViewPrefix != "" && !IsValidPathComponent(req.AppViewPrefix) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppViewPrefix"})
		return
	}

	// TEMPORARY: Clear cache for development
	loader.ClearCache()
	loader.ClearPreProcessCache()

	serverStart := time.Now()
	engineStart := time.Now()
	var mergedHTML string
	if strings.EqualFold(*req.EngineType, "PreProcess") {
		templates := loader.LoadProcessGetTemplateFiles(rootDirPath, *req.AppSite)
		engine := engine.NewEnginePreProcess(safeString(req.AppViewPrefix))
		mergedHTML = engine.MergeTemplates(*req.AppSite, appFile, safeString(req.AppView), templates.Templates, true)
	} else {
		templates := loader.LoadGetTemplateFiles(rootDirPath, *req.AppSite)
		engine := engine.NewEngineNormal(safeString(req.AppViewPrefix))
		mergedHTML = engine.MergeTemplates(*req.AppSite, appFile, safeString(req.AppView), templates, true)
	}
	engineTimeMs := float64(time.Since(engineStart).Microseconds()) / 1000.0
	serverTimeMs := float64(time.Since(serverStart).Microseconds()) / 1000.0
	responseObj := map[string]interface{}{
		"Html":         mergedHTML,
		"ServerTimeMs": serverTimeMs,
		"EngineTimeMs": engineTimeMs,
		"Templates":    make(map[string]interface{}),
		"PreProcessTemplates": make(map[string]interface{}),
	}
	c.Header("Content-Type", "application/json")
	c.JSON(http.StatusOK, responseObj)
}

// Helper function
func safeString(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}

// TestResponse represents the response structure for test endpoints
type TestResponse struct {
	Success   bool    `json:"success"`
	Message   string  `json:"message"`
	Elapsed   float64 `json:"elapsed"`
	TestCount int     `json:"testCount"`
}

// testStandard handles the POST /test/standard endpoint
func testStandard(c *gin.Context) {
	start := time.Now()
	rootDirPath := filepath.Join(projectDirectory, "wwwroot")

	// Enable logging temporarily for tests
	originalLogLevel := common.GetLogLevel()

	// Configure logger with context-specific log files for StandardTests
	templateAnalysisDir := filepath.Join(projectDirectory, "template_analysis")
	logsDir := filepath.Join(templateAnalysisDir, "logs")
	os.MkdirAll(logsDir, 0755)

	contextLogFiles := map[string]string{
		"LoaderNormal": filepath.Join(logsDir, "go_loadernormal.log"),
		"EngineNormal": filepath.Join(logsDir, "go_enginenormal.log"),
	}

	common.Configure(common.DEBUG, "", false, common.ROTATION_NONE)
	common.ConfigureContextLogFiles(contextLogFiles)

	// Load scenarios from ConfigUtil
	scenarios, err := config.GetScenarios()
	if err != nil {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to load scenarios: " + err.Error()})
		return
	}

	results := test.RunStandardTests(rootDirPath, projectDirectory, scenarios, false, true, true)
	if len(results) > 0 {
		test.PrintTestSummaryTable(rootDirPath, results, "STANDARD TEST")
	}

	// Restore original log level
	common.SetLogLevel(originalLogLevel)

	elapsed := time.Since(start).Seconds()
	testCount := len(results)

	// Check for failures
	failedCount := 0
	for _, r := range results {
		if r.NormalPreProcess == "FAIL" || r.CrossViewUnMatch == "FAIL" || r.Error != "" {
			failedCount++
		}
	}

	message := fmt.Sprintf("Successful run of Standard Tests in %.2f secs (%d tests)", elapsed, testCount)
	if failedCount > 0 {
		message += fmt.Sprintf("\n⚠️ Warning: %d test(s) failed", failedCount)
	}

	response := TestResponse{
		Success:   true,
		Message:   message,
		Elapsed:   elapsed,
		TestCount: testCount,
	}

	c.JSON(http.StatusOK, response)
}

// testAdvanced handles the POST /test/advanced endpoint
func testAdvanced(c *gin.Context) {
	start := time.Now()
	rootDirPath := filepath.Join(projectDirectory, "wwwroot")

	// Enable logging temporarily for tests
	originalLogLevel := common.GetLogLevel()

	// Configure logger with context-specific log files for AdvancedTests
	templateAnalysisDir := filepath.Join(projectDirectory, "template_analysis")
	logsDir := filepath.Join(templateAnalysisDir, "logs")
	os.MkdirAll(logsDir, 0755)

	contextLogFiles := map[string]string{
		"LoaderNormal":      filepath.Join(logsDir, "go_loadernormal.log"),
		"LoaderPreProcess":  filepath.Join(logsDir, "go_loaderpreprocess.log"),
		"EngineNormal":      filepath.Join(logsDir, "go_enginenormal.log"),
		"EnginePreProcess":  filepath.Join(logsDir, "go_enginepreprocess.log"),
	}

	common.Configure(common.DEBUG, "", false, common.ROTATION_NONE)
	common.ConfigureContextLogFiles(contextLogFiles)

	// Load scenarios from ConfigUtil
	scenarios, err := config.GetScenarios()
	if err != nil {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to load scenarios: " + err.Error()})
		return
	}

	// Dump preprocessed template structures before running advanced tests
	test.DumpPreprocessedTemplateStructures(rootDirPath, projectDirectory, scenarios, true)

	results := test.RunAdvancedTests(rootDirPath, projectDirectory, scenarios, false, true, true)
	if len(results) > 0 {
		test.PrintTestSummaryTable(rootDirPath, results, "ADVANCED TEST")
	}

	// Restore original log level
	common.SetLogLevel(originalLogLevel)

	elapsed := time.Since(start).Seconds()
	testCount := len(results)

	// Check for failures
	failedCount := 0
	for _, r := range results {
		if r.NormalPreProcess == "FAIL" || r.CrossViewUnMatch == "FAIL" || r.Error != "" {
			failedCount++
		}
	}

	message := fmt.Sprintf("Successful run of Advanced Tests in %.2f secs (%d tests)", elapsed, testCount)
	if failedCount > 0 {
		message += fmt.Sprintf("\n⚠️ Warning: %d test(s) failed", failedCount)
	}

	response := TestResponse{
		Success:   true,
		Message:   message,
		Elapsed:   elapsed,
		TestCount: testCount,
	}

	c.JSON(http.StatusOK, response)
}

// testPerformance handles the POST /test/performance endpoint
func testPerformance(c *gin.Context) {
	start := time.Now()
	assemblerWebDirPath, _ := common.GetAssemblerWebDirPath()
	rootDirPath := assemblerWebDirPath

	// Disable logging during performance tests
	originalLogLevel := common.GetLogLevel()
	common.SetLogLevel(common.NONE)

	// Load scenarios from ConfigUtil
	scenarios, err := config.GetScenarios()
	if err != nil {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to load scenarios: " + err.Error()})
		return
	}

	results := performance.RunPerformanceComparison(rootDirPath, scenarios, true, true)
	if len(results) > 0 {
		performance.PrintPerfSummaryTable(rootDirPath, results)
	}

	// Restore original log level
	common.SetLogLevel(originalLogLevel)
	elapsed := time.Since(start).Seconds()
	testCount := len(results)

	// Check for performance test mismatches
	mismatchCount := 0
	for _, r := range results {
		if r.ResultsMatch != "YES" {
			mismatchCount++
		}
	}

	message := fmt.Sprintf("Successful run of Performance Tests in %.2f secs (%d tests)", elapsed, testCount)
	if mismatchCount > 0 {
		message += fmt.Sprintf("\n⚠️ Warning: %d test(s) have output mismatch", mismatchCount)
	}

	response := TestResponse{
		Success:   true,
		Message:   message,
		Elapsed:   elapsed,
		TestCount: testCount,
	}

	c.JSON(http.StatusOK, response)
}

// testConsolidatePerformance handles the POST /test/consolidate-performance endpoint
func testConsolidatePerformance(c *gin.Context) {
	start := time.Now()
	rootDirPath := filepath.Join(projectDirectory, "wwwroot")

	// Configure logging for consolidate endpoint
	templateAnalysisDir := filepath.Join(projectDirectory, "template_analysis")
	logsDir := filepath.Join(templateAnalysisDir, "logs")
	os.MkdirAll(logsDir, 0755)
	consolidateLogFile := filepath.Join(logsDir, "go_consolidate_perf.log")

	// Log start
	logMsg := fmt.Sprintf("\n[%s] Starting consolidate-performance endpoint\n", time.Now().UTC().Format(time.RFC3339))
	f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
	if f != nil {
		f.WriteString(logMsg)
		f.Close()
	}

	// Read server configuration from servers.json
	serversConfigPath := filepath.Join(rootDirPath, "servers.json")
	type ServerConfig struct {
		Language string `json:"language"`
		URL      string `json:"url"`
	}
	type ServersConfig struct {
		PerformanceServers []ServerConfig `json:"performanceServers"`
	}

	servers := []ServerConfig{
		{Language: "CSharp", URL: "https://csharpassembler.fly.dev/csharp_perfsummary.json"},
		{Language: "Rust", URL: "https://rustassembler.fly.dev/rust_perfsummary.json"},
		{Language: "Node", URL: "https://nodeassembler.fly.dev/nodejs_perfsummary.json"},
		{Language: "PHP", URL: "https://phpassembler.fly.dev/php_perfsummary.json"},
		{Language: "Go", URL: "https://goassembler.fly.dev/go_perfsummary.json"},
	}

	if configData, err := os.ReadFile(serversConfigPath); err == nil {
		var config ServersConfig
		if err := json.Unmarshal(configData, &config); err == nil {
			servers = config.PerformanceServers
		}
	}

	client := &http.Client{Timeout: 30 * time.Second}
	var serversProcessed []string
	var serversFailed []string

	// Map to store performance data: appKey -> language -> (normalMs, preprocessMs, outputSize, appView)
	appPerf := make(map[string]map[string]PerfData)

	for _, server := range servers {
		// Log fetch attempt
		logMsg := fmt.Sprintf("[%s] Fetching %s from %s\n", time.Now().UTC().Format(time.RFC3339), server.Language, server.URL)
		f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
		if f != nil {
			f.WriteString(logMsg)
			f.Close()
		}

		resp, err := client.Get(server.URL)
		if err != nil {
			domain := strings.Split(strings.TrimPrefix(strings.TrimPrefix(server.URL, "https://"), "http://"), "/")[0]
			failureMsg := fmt.Sprintf("%s: %s (ERROR: %v)", server.Language, domain, err)
			serversFailed = append(serversFailed, failureMsg)
			// Log failure
			logMsg := fmt.Sprintf("[%s] ❌ %s\n", time.Now().UTC().Format(time.RFC3339), failureMsg)
			f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
			if f != nil {
				f.WriteString(logMsg)
				f.Close()
			}
			continue
		}
		defer resp.Body.Close()

		if resp.StatusCode == http.StatusOK {
			body, err := io.ReadAll(resp.Body)
			if err != nil {
				domain := strings.Split(strings.TrimPrefix(strings.TrimPrefix(server.URL, "https://"), "http://"), "/")[0]
				failureMsg := fmt.Sprintf("%s: %s (ERROR: %v)", server.Language, domain, err)
				serversFailed = append(serversFailed, failureMsg)
				// Log failure
				logMsg := fmt.Sprintf("[%s] ❌ %s\n", time.Now().UTC().Format(time.RFC3339), failureMsg)
				f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
				if f != nil {
					f.WriteString(logMsg)
					f.Close()
				}
				continue
			}

			// Parse JSON array
			var perfRows []map[string]interface{}
			if err := json.Unmarshal(body, &perfRows); err == nil {
				itemCount := len(perfRows)
				for _, item := range perfRows {
					// Extract fields with case-insensitive property names
					appSite := getStringField(item, "AppSite", "app_site", "appSite")
					appView := getStringField(item, "AppView", "app_view", "appView")

					normalTime := getFloatField(item, "NormalTimeMs", "normal_time_ms", "normalTimeMs", "NormalTimeNanos", "normal_time_nanos")
					if _, hasNanos := item["NormalTimeNanos"]; hasNanos {
						normalTime = normalTime / 1_000_000.0
					} else if _, hasNanos := item["normal_time_nanos"]; hasNanos {
						normalTime = normalTime / 1_000_000.0
					}

					preprocessTime := getFloatField(item, "PreProcessTimeMs", "preprocess_time_ms", "preProcessTimeMs", "PreProcessTimeNanos", "preprocess_time_nanos")
					if _, hasNanos := item["PreProcessTimeNanos"]; hasNanos {
						preprocessTime = preprocessTime / 1_000_000.0
					} else if _, hasNanos := item["preprocess_time_nanos"]; hasNanos {
						preprocessTime = preprocessTime / 1_000_000.0
					}

					outputSize := getIntField(item, "OutputSize", "output_size", "outputSize")

					key := appSite
					if appView != "" {
						key = appSite + " → " + appView
					}

					if appPerf[key] == nil {
						appPerf[key] = make(map[string]PerfData)
					}

					normPtr := &normalTime
					prepPtr := &preprocessTime
					outPtr := &outputSize
					appPerf[key][server.Language] = PerfData{
						NormalTimeMs:     normPtr,
						PreProcessTimeMs: prepPtr,
						OutputSize:       outPtr,
						AppView:          appView,
					}
				}
				// Log success
				logMsg := fmt.Sprintf("[%s] ✅ %s: Successfully processed %d items\n", time.Now().UTC().Format(time.RFC3339), server.Language, itemCount)
				f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
				if f != nil {
					f.WriteString(logMsg)
					f.Close()
				}
			}
			serversProcessed = append(serversProcessed, server.Language)
		} else {
			domain := strings.Split(strings.TrimPrefix(strings.TrimPrefix(server.URL, "https://"), "http://"), "/")[0]
			failureMsg := fmt.Sprintf("%s: %s (HTTP %d)", server.Language, domain, resp.StatusCode)
			serversFailed = append(serversFailed, failureMsg)
			// Log failure
			logMsg := fmt.Sprintf("[%s] ❌ %s\n", time.Now().UTC().Format(time.RFC3339), failureMsg)
			f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
			if f != nil {
				f.WriteString(logMsg)
				f.Close()
			}
		}
	}

	// Generate HTML report
	var htmlBuilder strings.Builder
	htmlBuilder.WriteString("<!DOCTYPE html>\n<html>\n<head>\n")
	htmlBuilder.WriteString("    <meta charset=\"UTF-8\">\n")
	htmlBuilder.WriteString("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n")
	htmlBuilder.WriteString("    <title>Consolidated Performance Summary</title>\n")
	htmlBuilder.WriteString("    <style>\n")
	htmlBuilder.WriteString("        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f7fa; }\n")
	htmlBuilder.WriteString("        h1 { color: #667eea; text-align: center; }\n")
	htmlBuilder.WriteString("        h2 { color: #764ba2; margin-top: 40px; }\n")
	htmlBuilder.WriteString("        .meta { text-align: center; color: #666; font-style: italic; margin-bottom: 30px; }\n")
	htmlBuilder.WriteString("        .table-container { overflow-x: auto; }\n")
	htmlBuilder.WriteString("        table { border-collapse: collapse; width: 100%; max-width: 1200px; margin: 20px auto; background: white; box-shadow: 0 2px 8px rgba(0,0,0,0.1); min-width: 600px; }\n")
	htmlBuilder.WriteString("        th, td { border: 1px solid #ddd; padding: 12px 8px; text-align: left; }\n")
	htmlBuilder.WriteString("        th { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; font-weight: bold; position: sticky; top: 0; }\n")
	htmlBuilder.WriteString("        tr:nth-child(even) { background-color: #f9f9f9; }\n")
	htmlBuilder.WriteString("        tr:hover { background-color: #f0f0f0; }\n")
	htmlBuilder.WriteString("        td:nth-child(2), td:nth-child(3), td:nth-child(4), td:nth-child(5), td:nth-child(6), td:nth-child(7) { text-align: right; }\n")
	htmlBuilder.WriteString("        @media (max-width: 768px) {\n")
	htmlBuilder.WriteString("            body { margin: 10px; }\n")
	htmlBuilder.WriteString("            th, td { padding: 8px; font-size: 14px; }\n")
	htmlBuilder.WriteString("            h1 { font-size: 24px; }\n")
	htmlBuilder.WriteString("            h2 { font-size: 20px; }\n")
	htmlBuilder.WriteString("        }\n")
	htmlBuilder.WriteString("    </style>\n</head>\n<body>\n")
	htmlBuilder.WriteString("<h1>Consolidated Performance Summary</h1>\n")
	htmlBuilder.WriteString(fmt.Sprintf("<div class=\"meta\">Generated: %s UTC</div>\n", time.Now().UTC().Format("2006-01-02 15:04:05")))
	htmlBuilder.WriteString("<div class=\"meta\">All times in milliseconds (ms)</div>\n")

	// Normal Engine Table
	htmlBuilder.WriteString("<h2>Normal Engine</h2>\n")
	htmlBuilder.WriteString("<div class=\"table-container\">\n<table>\n")
	htmlBuilder.WriteString("<tr><th>AppSite/AppView</th><th>CSharp</th><th>Rust</th><th>Go</th><th>Node</th><th>PHP</th><th>OutputSize</th></tr>\n")

	// Sort app keys
	var appKeys []string
	for k := range appPerf {
		appKeys = append(appKeys, k)
	}
	sort.Strings(appKeys)

	for _, app := range appKeys {
		csharp := formatFloat(appPerf[app]["CSharp"].NormalTimeMs)
		rust := formatFloat(appPerf[app]["Rust"].NormalTimeMs)
		goPerfData := formatFloat(appPerf[app]["Go"].NormalTimeMs)
		node := formatFloat(appPerf[app]["Node"].NormalTimeMs)
		php := formatFloat(appPerf[app]["PHP"].NormalTimeMs)
		outputSize := formatInt(getFirstOutputSize(appPerf[app]))

		htmlBuilder.WriteString(fmt.Sprintf("<tr><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>\n",
			app, csharp, rust, goPerfData, node, php, outputSize))
	}
	htmlBuilder.WriteString("</table>\n</div>\n")

	// PreProcess Engine Table
	htmlBuilder.WriteString("<h2>PreProcess Engine</h2>\n")
	htmlBuilder.WriteString("<div class=\"table-container\">\n<table>\n")
	htmlBuilder.WriteString("<tr><th>AppSite/AppView</th><th>CSharp</th><th>Rust</th><th>Go</th><th>Node</th><th>PHP</th><th>OutputSize</th></tr>\n")

	for _, app := range appKeys {
		csharp := formatFloat(appPerf[app]["CSharp"].PreProcessTimeMs)
		rust := formatFloat(appPerf[app]["Rust"].PreProcessTimeMs)
		goPerfData := formatFloat(appPerf[app]["Go"].PreProcessTimeMs)
		node := formatFloat(appPerf[app]["Node"].PreProcessTimeMs)
		php := formatFloat(appPerf[app]["PHP"].PreProcessTimeMs)
		outputSize := formatInt(getFirstOutputSize(appPerf[app]))

		htmlBuilder.WriteString(fmt.Sprintf("<tr><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>\n",
			app, csharp, rust, goPerfData, node, php, outputSize))
	}
	htmlBuilder.WriteString("</table>\n</div>\n")
	htmlBuilder.WriteString("</body>\n</html>\n")

	// Write HTML to Reports directory
	reportsDir := filepath.Join(rootDirPath, "Reports")
	if err := os.MkdirAll(reportsDir, 0755); err != nil {
		log.Printf("Error creating Reports directory: %v", err)
	}
	htmlPath := filepath.Join(reportsDir, "all_perf_tests.html")
	if err := os.WriteFile(htmlPath, []byte(htmlBuilder.String()), 0644); err != nil {
		log.Printf("Error writing HTML file: %v", err)
	}

	elapsed := time.Since(start).Seconds()

	// Log completion
	logMsg = fmt.Sprintf("[%s] Consolidation complete in %.2fs - %d AppSites from %d/%d servers\n",
		time.Now().UTC().Format(time.RFC3339), elapsed, len(appPerf), len(serversProcessed), len(servers))
	f, _ = os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
	if f != nil {
		f.WriteString(logMsg)
		f.Close()
	}

	message := fmt.Sprintf("Consolidated %d AppSites from %d/%d servers in %.2f secs", len(appPerf), len(serversProcessed), len(servers), elapsed)
	if len(serversProcessed) > 0 {
		message += fmt.Sprintf(" | ✅ Success: %s", strings.Join(serversProcessed, ", "))
	}
	if len(serversFailed) > 0 {
		message += fmt.Sprintf("\n❌ Failed: %s", strings.Join(serversFailed, "; "))
	}

	response := TestResponse{
		Success:   len(serversProcessed) > 0,
		Message:   message,
		Elapsed:   elapsed,
		TestCount: len(serversProcessed),
	}

	c.JSON(http.StatusOK, response)
}

// Helper functions for consolidate performance
func getStringField(m map[string]interface{}, keys ...string) string {
	for _, key := range keys {
		if val, ok := m[key]; ok {
			if str, ok := val.(string); ok {
				return str
			}
		}
	}
	return ""
}

func getFloatField(m map[string]interface{}, keys ...string) float64 {
	for _, key := range keys {
		if val, ok := m[key]; ok {
			switch v := val.(type) {
			case float64:
				return v
			case int:
				return float64(v)
			}
		}
	}
	return 0
}

func getIntField(m map[string]interface{}, keys ...string) int {
	for _, key := range keys {
		if val, ok := m[key]; ok {
			switch v := val.(type) {
			case int:
				return v
			case float64:
				return int(v)
			}
		}
	}
	return 0
}

func formatFloat(f *float64) string {
	if f == nil {
		return "-"
	}
	return fmt.Sprintf("%.2f", *f)
}

func formatInt(i *int) string {
	if i == nil {
		return "-"
	}
	return fmt.Sprintf("%d", *i)
}

func getFirstOutputSize(langData map[string]PerfData) *int {
	for _, data := range langData {
		if data.OutputSize != nil {
			return data.OutputSize
		}
	}
	return nil
}

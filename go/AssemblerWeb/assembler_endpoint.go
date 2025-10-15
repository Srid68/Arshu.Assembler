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

// PerfData represents performance data for consolidation
type PerfData struct {
	NormalTimeMs     *float64
	PreProcessTimeMs *float64
	OutputSize       *int
	AppView          string
}

// MergeRequest represents the request structure for template merging
type MergeRequest struct {
	AppSite    *string `json:"appSite" binding:"required"`
	AppView    *string `json:"appView"`
	EngineType *string `json:"engineType" binding:"required"`
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

	// Calculate appViewPrefix from appFile when appView is not empty
	appViewPrefix := ""
	if appViewValue != "" {
		appViewPrefix = appFile
	}

	logMsg := fmt.Sprintf("/merge endpoint called with: app_site=%v, app_file=%v, engine_type=%v, app_view=%v, app_view_prefix=%v",
		safeString(req.AppSite), appFile, safeString(req.EngineType), appViewValue, appViewPrefix)
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

	serverStart := time.Now()
	engineStart := time.Now()
	var mergedHTML string
	if strings.EqualFold(*req.EngineType, "PreProcess") {
		templates := loader.LoadProcessGetTemplateFiles(rootDirPath, *req.AppSite)
		engine := engine.NewEnginePreProcess(appViewPrefix)
		mergedHTML = engine.MergeTemplates(*req.AppSite, appFile, appViewValue, templates.Templates, true)
	} else {
		templates := loader.LoadGetTemplateFiles(rootDirPath, *req.AppSite)
		engine := engine.NewEngineNormal(appViewPrefix)
		mergedHTML = engine.MergeTemplates(*req.AppSite, appFile, appViewValue, templates, true)
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
		test.PrintTestSummaryTable(rootDirPath, projectDirectory, results, "STANDARD TEST")
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
		test.PrintTestSummaryTable(rootDirPath, projectDirectory, results, "ADVANCED TEST")
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
	assemblerWebDirPath, projectDir := common.GetAssemblerWebDirPath()
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

	results := performance.RunPerformanceComparison(rootDirPath, projectDir, scenarios, true, true)
	if len(results) > 0 {
		performance.PrintPerfSummaryTable(rootDirPath, projectDir, results)
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

	// Read server configuration from servers.csv
	serversConfigPath := filepath.Join(rootDirPath, "App_Data", "servers.csv")
	type ServerConfig struct {
		Language string
		Method   string
		URL      string
		FileName string
	}

	var servers []ServerConfig

	if configData, err := os.ReadFile(serversConfigPath); err == nil {
		lines := strings.Split(string(configData), "\n")
		for _, line := range lines {
			line = strings.TrimSpace(line)
			if line == "" {
				continue
			}
			parts := strings.Split(line, ",")
			if len(parts) >= 3 {
				language := strings.TrimSpace(parts[0])
				method := strings.ToUpper(strings.TrimSpace(parts[1]))
				url := strings.TrimSpace(parts[2])
				fileName := ""
				if len(parts) >= 4 {
					fileName = strings.TrimSpace(parts[3])
				}
				if language != "" && method != "" && url != "" {
					servers = append(servers, ServerConfig{Language: language, Method: method, URL: url, FileName: fileName})
				}
			}
		}
	}

	if len(servers) == 0 {
		errorMsg := "No server configuration found. Please configure servers in App_Data/servers.csv"
		logMsg := fmt.Sprintf("[%s] ❌ %s\n", time.Now().UTC().Format(time.RFC3339), errorMsg)
		f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
		if f != nil {
			f.WriteString(logMsg)
			f.Close()
		}

		response := TestResponse{
			Success:   false,
			Message:   errorMsg,
			Elapsed:   time.Since(start).Seconds(),
			TestCount: 0,
		}

		c.JSON(http.StatusOK, response)
		return
	}

	client := &http.Client{Timeout: 120 * time.Second}
	var serversProcessed []string
	var serversFailed []string

	// Map to store performance data: appKey -> language -> (normalMs, preprocessMs, outputSize, appView)
	appPerf := make(map[string]map[string]PerfData)

	// Group servers by language
	serversByLang := make(map[string][]ServerConfig)
	for _, server := range servers {
		serversByLang[server.Language] = append(serversByLang[server.Language], server)
	}

	for lang, langServers := range serversByLang {
		langSuccess := false
		var langErrors []string

		for _, server := range langServers {
			// Log fetch attempt
			var logMsg string
			if server.Method == "POST" {
				logMsg = fmt.Sprintf("[%s] Fetching %s via POST %s (fileName: %s)\n", time.Now().UTC().Format(time.RFC3339), lang, server.URL, server.FileName)
			} else {
				fullURL := server.URL + server.FileName
				logMsg = fmt.Sprintf("[%s] Fetching %s via GET %s\n", time.Now().UTC().Format(time.RFC3339), lang, fullURL)
			}
			f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
			if f != nil {
				f.WriteString(logMsg)
				f.Close()
			}

			var resp *http.Response
			var err error

			if server.Method == "POST" {
				reportRequest := map[string]interface{}{
					"fileName":      server.FileName,
					"useLangPrefix": false,
				}
				jsonData, _ := json.Marshal(reportRequest)
				resp, err = client.Post(server.URL, "application/json", strings.NewReader(string(jsonData)))
			} else {
				fullURL := server.URL + server.FileName
				resp, err = client.Get(fullURL)
			}

			if err != nil {
				domain := strings.Split(strings.TrimPrefix(strings.TrimPrefix(server.URL, "https://"), "http://"), "/")[0]
				errorMsg := fmt.Sprintf("%s %s (ERROR: %v)", server.Method, domain, err)
				langErrors = append(langErrors, errorMsg)
				// Log warning
				logMsg := fmt.Sprintf("[%s] ⚠️ %s: %s\n", time.Now().UTC().Format(time.RFC3339), lang, errorMsg)
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
					errorMsg := fmt.Sprintf("%s %s (ERROR: %v)", server.Method, domain, err)
					langErrors = append(langErrors, errorMsg)
					// Log warning
					logMsg := fmt.Sprintf("[%s] ⚠️ %s: %s\n", time.Now().UTC().Format(time.RFC3339), lang, errorMsg)
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

						if appSite != "" {
							key := appSite
							if appView != "" {
								key = appSite + " → " + appView
							}

							// Use case-insensitive comparison for key matching
							var existingKey string
							for k := range appPerf {
								if strings.EqualFold(k, key) {
									existingKey = k
									break
								}
							}
							if existingKey != "" {
								key = existingKey
							}

							if appPerf[key] == nil {
								appPerf[key] = make(map[string]PerfData)
							}

							normPtr := &normalTime
							prepPtr := &preprocessTime
							outPtr := &outputSize
							appPerf[key][lang] = PerfData{
								NormalTimeMs:     normPtr,
								PreProcessTimeMs: prepPtr,
								OutputSize:       outPtr,
								AppView:          appView,
							}
						}
					}
					// Log success
					logMsg := fmt.Sprintf("[%s] ✅ %s: Successfully processed %d items\n", time.Now().UTC().Format(time.RFC3339), lang, itemCount)
					f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
					if f != nil {
						f.WriteString(logMsg)
						f.Close()
					}
				}
				langSuccess = true
				break // Success, no need to try other methods
			} else {
				domain := strings.Split(strings.TrimPrefix(strings.TrimPrefix(server.URL, "https://"), "http://"), "/")[0]
				errorMsg := fmt.Sprintf("%s %s (HTTP %d)", server.Method, domain, resp.StatusCode)
				langErrors = append(langErrors, errorMsg)
				// Log warning
				logMsg := fmt.Sprintf("[%s] ⚠️ %s: %s\n", time.Now().UTC().Format(time.RFC3339), lang, errorMsg)
				f, _ := os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
				if f != nil {
					f.WriteString(logMsg)
					f.Close()
				}
			}
		}

		// After trying all methods for this language, determine overall result
		if langSuccess {
			serversProcessed = append(serversProcessed, lang)
		} else {
			failureMsg := fmt.Sprintf("%s: All methods failed - %s", lang, strings.Join(langErrors, "; "))
			serversFailed = append(serversFailed, failureMsg)
			logMsg := fmt.Sprintf("[%s] ❌ %s: All methods failed\n", time.Now().UTC().Format(time.RFC3339), lang)
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
	htmlBuilder.WriteString("        body { font-family: Arial, sans-serif; margin: 20px; }\n")
	htmlBuilder.WriteString("        h1 { color: #333; }\n")
	htmlBuilder.WriteString("        h2 { color: #333; margin-top: 40px; }\n")
	htmlBuilder.WriteString("        .meta { color: #666; font-style: italic; margin-bottom: 10px; }\n")
	htmlBuilder.WriteString("        .table-container { overflow-x: auto; }\n")
	htmlBuilder.WriteString("        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 700px; }\n")
	htmlBuilder.WriteString("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n")
	htmlBuilder.WriteString("        th { background-color: #4CAF50; color: white; }\n")
	htmlBuilder.WriteString("        tr:nth-child(even) { background-color: #f2f2f2; }\n")
	htmlBuilder.WriteString("        td:nth-child(2), td:nth-child(3), td:nth-child(4), td:nth-child(5), td:nth-child(6), td:nth-child(7) { text-align: right; }\n")
	htmlBuilder.WriteString("        .best-perf { background-color: #90EE90; font-weight: bold; }\n")
	htmlBuilder.WriteString("        @media (max-width: 768px) {\n")
	htmlBuilder.WriteString("            body { margin: 10px; }\n")
	htmlBuilder.WriteString("            th, td { padding: 8px; font-size: 14px; }\n")
	htmlBuilder.WriteString("            h1 { font-size: 24px; }\n")
	htmlBuilder.WriteString("            h2 { font-size: 20px; }\n")
	htmlBuilder.WriteString("            .meta { font-size: 12px; }\n")
	htmlBuilder.WriteString("        }\n")
	htmlBuilder.WriteString("    </style>\n</head>\n<body>\n")
	htmlBuilder.WriteString("    <h1>Consolidated Performance Summary</h1>\n")
	htmlBuilder.WriteString(fmt.Sprintf("    <div class=\"meta\">Generated: %s UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>\n", time.Now().UTC().Format("2006-01-02 15:04:05")))

	// Get list of languages dynamically from configuration
	var languages []string
	for lang := range serversByLang {
		languages = append(languages, lang)
	}
	sort.Strings(languages)

	// Normal Engine Table
	htmlBuilder.WriteString("    <h2>Normal Engine</h2>\n")
	htmlBuilder.WriteString("    <div class=\"table-container\">\n")
	htmlBuilder.WriteString("    <table>\n")
	htmlBuilder.WriteString("        <tr>\n")
	htmlBuilder.WriteString("            <th>AppSite/AppView</th>\n")
	for _, lang := range languages {
		htmlBuilder.WriteString(fmt.Sprintf("            <th>%s</th>\n", lang))
	}
	htmlBuilder.WriteString("            <th>OutputSize</th>\n")
	htmlBuilder.WriteString("        </tr>\n")

	// Sort app keys
	var appKeys []string
	for k := range appPerf {
		appKeys = append(appKeys, k)
	}
	sort.Strings(appKeys)

	// Cache OutputSize per app to ensure consistency across both tables
	appOutputSizes := make(map[string]*int)
	for _, app := range appKeys {
		appOutputSizes[app] = getFirstOutputSize(appPerf[app])
	}

	for _, app := range appKeys {
		// Find minimum time for highlighting
		var validTimes []float64
		for _, lang := range languages {
			if appPerf[app][lang].NormalTimeMs != nil {
				validTimes = append(validTimes, *appPerf[app][lang].NormalTimeMs)
			}
		}
		var minTime *float64
		if len(validTimes) > 0 {
			min := validTimes[0]
			for _, t := range validTimes {
				if t < min {
					min = t
				}
			}
			minTime = &min
		}

		htmlBuilder.WriteString("        <tr>\n")
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", app))
		for _, lang := range languages {
			timeValue := formatFloat(appPerf[app][lang].NormalTimeMs)
			isBest := minTime != nil && appPerf[app][lang].NormalTimeMs != nil && (*appPerf[app][lang].NormalTimeMs - *minTime) < 0.001 && (*appPerf[app][lang].NormalTimeMs - *minTime) > -0.001
			cssClass := ""
			if isBest {
				cssClass = " class=\"best-perf\""
			}
			htmlBuilder.WriteString(fmt.Sprintf("            <td%s>%s</td>\n", cssClass, timeValue))
		}
		outputSize := formatInt(appOutputSizes[app])
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", outputSize))
		htmlBuilder.WriteString("        </tr>\n")
	}
	htmlBuilder.WriteString("    </table>\n")
	htmlBuilder.WriteString("    </div>\n")

	// PreProcess Engine Table
	htmlBuilder.WriteString("    <h2>PreProcess Engine</h2>\n")
	htmlBuilder.WriteString("    <div class=\"table-container\">\n")
	htmlBuilder.WriteString("    <table>\n")
	htmlBuilder.WriteString("        <tr>\n")
	htmlBuilder.WriteString("            <th>AppSite/AppView</th>\n")
	for _, lang := range languages {
		htmlBuilder.WriteString(fmt.Sprintf("            <th>%s</th>\n", lang))
	}
	htmlBuilder.WriteString("            <th>OutputSize</th>\n")
	htmlBuilder.WriteString("        </tr>\n")

	for _, app := range appKeys {
		// Find minimum time for highlighting
		var validTimes []float64
		for _, lang := range languages {
			if appPerf[app][lang].PreProcessTimeMs != nil {
				validTimes = append(validTimes, *appPerf[app][lang].PreProcessTimeMs)
			}
		}
		var minTime *float64
		if len(validTimes) > 0 {
			min := validTimes[0]
			for _, t := range validTimes {
				if t < min {
					min = t
				}
			}
			minTime = &min
		}

		htmlBuilder.WriteString("        <tr>\n")
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", app))
		for _, lang := range languages {
			timeValue := formatFloat(appPerf[app][lang].PreProcessTimeMs)
			isBest := minTime != nil && appPerf[app][lang].PreProcessTimeMs != nil && (*appPerf[app][lang].PreProcessTimeMs - *minTime) < 0.001 && (*appPerf[app][lang].PreProcessTimeMs - *minTime) > -0.001
			cssClass := ""
			if isBest {
				cssClass = " class=\"best-perf\""
			}
			htmlBuilder.WriteString(fmt.Sprintf("            <td%s>%s</td>\n", cssClass, timeValue))
		}
		outputSize := formatInt(appOutputSizes[app])
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", outputSize))
		htmlBuilder.WriteString("        </tr>\n")
	}
	htmlBuilder.WriteString("    </table>\n")
	htmlBuilder.WriteString("    </div>\n")
	htmlBuilder.WriteString("</body>\n</html>\n")

	// Write HTML to template_analysis/Reports directory
	reportsDir := filepath.Join(projectDirectory, "template_analysis", "Reports")
	if err := os.MkdirAll(reportsDir, 0755); err != nil {
		log.Printf("Error creating Reports directory: %v", err)
	}
	htmlPath := filepath.Join(reportsDir, "all_perf_tests.html")
	if err := os.WriteFile(htmlPath, []byte(htmlBuilder.String()), 0644); err != nil {
		log.Printf("Error writing HTML file: %v", err)
	}

	elapsed := time.Since(start).Seconds()

	// Log completion
	totalLanguages := len(serversByLang)
	logMsg = fmt.Sprintf("[%s] Consolidation complete in %.2fs - %d AppSites from %d/%d languages\n",
		time.Now().UTC().Format(time.RFC3339), elapsed, len(appPerf), len(serversProcessed), totalLanguages)
	f, _ = os.OpenFile(consolidateLogFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
	if f != nil {
		f.WriteString(logMsg)
		f.Close()
	}

	message := fmt.Sprintf("Consolidated %d AppSites from %d/%d languages in %.2f secs", len(appPerf), len(serversProcessed), totalLanguages, elapsed)
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

// ReportRequest represents the request structure for report retrieval
type ReportRequest struct {
	FileName      *string `json:"fileName" binding:"required"`
	UseLangPrefix *bool   `json:"useLangPrefix"`
}

// getReport handles the POST /api/report endpoint
func getReport(c *gin.Context) {
	var req ReportRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: fileName"})
		return
	}

	// Validate required fields
	if req.FileName == nil || *req.FileName == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: fileName"})
		return
	}

	// Validate fileName for path traversal
	if !IsValidPathComponent(req.FileName) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in fileName"})
		return
	}

	// Construct file path
	prefix := ""
	if req.UseLangPrefix != nil && *req.UseLangPrefix {
		prefix = "go_"
	}
	fileName := prefix + *req.FileName
	reportsDir := filepath.Join(projectDirectory, "template_analysis", "Reports")
	filePath := filepath.Join(reportsDir, fileName)

	// Check if file exists
	if _, err := os.Stat(filePath); os.IsNotExist(err) {
		c.JSON(http.StatusNotFound, gin.H{"error": fmt.Sprintf("Report file not found: %s", fileName)})
		return
	}

	// Read and return the file content
	content, err := os.ReadFile(filePath)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": fmt.Sprintf("Error reading report file: %v", err)})
		return
	}

	// Determine content type based on file extension
	contentType := "text/plain"
	if strings.HasSuffix(fileName, ".html") {
		contentType = "text/html"
	} else if strings.HasSuffix(fileName, ".json") {
		contentType = "application/json"
	} else if strings.HasSuffix(fileName, ".md") {
		contentType = "text/markdown"
	}

	c.Data(http.StatusOK, contentType, content)
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

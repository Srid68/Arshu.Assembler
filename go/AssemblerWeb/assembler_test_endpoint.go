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
		"LoaderNormal":     filepath.Join(logsDir, "go_loadernormal.log"),
		"LoaderPreProcess": filepath.Join(logsDir, "go_loaderpreprocess.log"),
		"EngineNormal":     filepath.Join(logsDir, "go_enginenormal.log"),
		"EnginePreProcess": filepath.Join(logsDir, "go_enginepreprocess.log"),
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
			isBest := minTime != nil && appPerf[app][lang].NormalTimeMs != nil && (*appPerf[app][lang].NormalTimeMs-*minTime) < 0.001 && (*appPerf[app][lang].NormalTimeMs-*minTime) > -0.001
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
			isBest := minTime != nil && appPerf[app][lang].PreProcessTimeMs != nil && (*appPerf[app][lang].PreProcessTimeMs-*minTime) < 0.001 && (*appPerf[app][lang].PreProcessTimeMs-*minTime) > -0.001
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
	LangPrefix    *string `json:"langPrefix"`
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

	// Validate langPrefix for path traversal if provided
	if req.LangPrefix != nil && !IsValidPathComponent(req.LangPrefix) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in langPrefix"})
		return
	}

	// Construct file path
	prefix := ""
	if req.UseLangPrefix != nil && *req.UseLangPrefix && req.LangPrefix != nil {
		prefix = *req.LangPrefix + "_"
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

// saveLog handles the POST /api/save-log endpoint
func saveLog(c *gin.Context) {
	var req struct {
		FileName string `json:"fileName"`
		Content  string `json:"content"`
	}
	if err := c.ShouldBindJSON(&req); err != nil || req.FileName == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required fields: fileName, content"})
		return
	}
	if !IsValidPathComponent(&req.FileName) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in fileName"})
		return
	}
	logsDir := filepath.Join(projectDirectory, "template_analysis", "logs")
	os.MkdirAll(logsDir, 0755)
	logFile := filepath.Join(logsDir, req.FileName)
	if err := os.WriteFile(logFile, []byte(req.Content), 0644); err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to save log file"})
		return
	}
	c.JSON(http.StatusOK, gin.H{"message": "Log saved successfully"})
}

// saveOutput handles the POST /api/save-output endpoint
func saveOutput(c *gin.Context) {
	var req struct {
		AppSite    string `json:"appSite"`
		AppView    string `json:"appView"`
		EngineType string `json:"engineType"`
		Html       string `json:"html"`
	}
	if err := c.ShouldBindJSON(&req); err != nil || req.AppSite == "" || req.EngineType == "" || req.Html == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required fields: appSite, engineType, html"})
		return
	}
	if !IsValidPathComponent(&req.AppSite) || !IsValidPathComponent(&req.EngineType) || (req.AppView != "" && !IsValidPathComponent(&req.AppView)) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid path component in parameters"})
		return
	}
	outputDir := filepath.Join(projectDirectory, "template_analysis", "output")
	os.MkdirAll(outputDir, 0755)
	appViewSuffix := ""
	if req.AppView != "" {
		appViewSuffix = "_" + req.AppView
	}
	engineSuffix := strings.ToLower(req.EngineType)
	outputFile := filepath.Join(outputDir, fmt.Sprintf("javascript_%s%s_%s.html", req.AppSite, appViewSuffix, engineSuffix))
	if err := os.WriteFile(outputFile, []byte(req.Html), 0644); err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to save output file"})
		return
	}
	c.JSON(http.StatusOK, gin.H{"message": "Output saved successfully"})
}

// saveTestResults handles the POST /api/test-results endpoint
func saveTestResults(c *gin.Context) {
	var summaryRows []map[string]interface{}
	if err := c.ShouldBindJSON(&summaryRows); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid test results format"})
		return
	}
	outputDir := filepath.Join(projectDirectory, "template_analysis", "Reports")
	os.MkdirAll(outputDir, 0755)
	now := time.Now().UTC().Format("20060102_150405")
	htmlFile := filepath.Join(outputDir, fmt.Sprintf("go_test_summary_%s.html", now))
	jsonFile := filepath.Join(outputDir, fmt.Sprintf("go_test_summary_%s.json", now))
	jsonBytes, _ := json.MarshalIndent(summaryRows, "", "  ")
	os.WriteFile(jsonFile, jsonBytes, 0644)
	// For HTML, just dump JSON as <pre> for now
	htmlContent := fmt.Sprintf("<html><body><pre>%s</pre></body></html>", string(jsonBytes))
	os.WriteFile(htmlFile, []byte(htmlContent), 0644)
	c.JSON(http.StatusOK, gin.H{"message": "Test results saved successfully"})
}

// savePerformanceResults handles the POST /api/performance-results endpoint
func savePerformanceResults(c *gin.Context) {
	var summaryRows []map[string]interface{}
	if err := c.ShouldBindJSON(&summaryRows); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid performance results format"})
		return
	}
	outputDir := filepath.Join(projectDirectory, "template_analysis", "Reports")
	os.MkdirAll(outputDir, 0755)
	now := time.Now().UTC().Format("20060102_150405")
	htmlFile := filepath.Join(outputDir, fmt.Sprintf("go_perf_summary_%s.html", now))
	jsonFile := filepath.Join(outputDir, fmt.Sprintf("go_perf_summary_%s.json", now))
	jsonBytes, _ := json.MarshalIndent(summaryRows, "", "  ")
	os.WriteFile(jsonFile, jsonBytes, 0644)
	htmlContent := fmt.Sprintf("<html><body><pre>%s</pre></body></html>", string(jsonBytes))
	os.WriteFile(htmlFile, []byte(htmlContent), 0644)
	c.JSON(http.StatusOK, gin.H{"message": "Performance results saved successfully"})
}

// Helper function
func safeString(s *string) string {
	if s == nil {
		return ""
	}
	return *s
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

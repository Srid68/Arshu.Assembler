package endpoint

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

// RULE_GROUPS defines the configurable rule groups for consolidated report grouping
var RULE_GROUPS = []string{
	"HtmlRule1",
	"HtmlRule2",
	"HtmlRule3",
	"JsonRule1",
	"JsonRule2",
	"Rule1",
}

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

// TestStandard handles the POST /test/standard endpoint
func TestStandard(c *gin.Context) {
	start := time.Now()
	projectDirectory := getProjectDirectory()
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

	common.Configure(common.DEBUG, false, common.ROTATION_NONE)
	common.AddContextLogFiles(contextLogFiles)

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

// TestAdvanced handles the POST /test/advanced endpoint
func TestAdvanced(c *gin.Context) {
	start := time.Now()
	projectDirectory := getProjectDirectory()
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

	common.Configure(common.DEBUG, false, common.ROTATION_NONE)
	common.AddContextLogFiles(contextLogFiles)

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

// TestPerformance handles the POST /test/performance endpoint
func TestPerformance(c *gin.Context) {
	start := time.Now()
	projectDirectory := getProjectDirectory()
	rootDirPath := getWwwrootPath()

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

	results := performance.RunPerformanceComparison(rootDirPath, projectDirectory, scenarios, true, true)
	if len(results) > 0 {
		performance.PrintPerfSummaryTable(rootDirPath, projectDirectory, results)
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

// TestConsolidatePerformance handles the POST /test/consolidate-performance endpoint
func TestConsolidatePerformance(c *gin.Context) {
	start := time.Now()
	projectDirectory := getProjectDirectory()
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

						// Get normal time - check for both ms and nanos versions
						var normalTimePtr *float64
						if normalTime := getFloatFieldPtr(item, "NormalTimeMs", "normal_time_ms", "normalTimeMs"); normalTime != nil {
							normalTimePtr = normalTime
						} else if normalTimeNanos := getFloatFieldPtr(item, "NormalTimeNanos", "normal_time_nanos"); normalTimeNanos != nil {
							converted := *normalTimeNanos / 1_000_000.0
							normalTimePtr = &converted
						}

						// Get preprocess time - check for both ms and nanos versions
						var preprocessTimePtr *float64
						if preprocessTime := getFloatFieldPtr(item, "PreProcessTimeMs", "preprocess_time_ms", "preProcessTimeMs"); preprocessTime != nil {
							preprocessTimePtr = preprocessTime
						} else if preprocessTimeNanos := getFloatFieldPtr(item, "PreProcessTimeNanos", "preprocess_time_nanos"); preprocessTimeNanos != nil {
							converted := *preprocessTimeNanos / 1_000_000.0
							preprocessTimePtr = &converted
						}

						outputSizePtr := getIntFieldPtr(item, "OutputSize", "output_size", "outputSize")

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

							appPerf[key][lang] = PerfData{
								NormalTimeMs:     normalTimePtr,
								PreProcessTimeMs: preprocessTimePtr,
								OutputSize:       outputSizePtr,
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
	htmlBuilder.WriteString("        .worst-perf { background-color: #FFB6C6; font-weight: bold; }\n")
	htmlBuilder.WriteString("        .avg-perf { background-color: #FFD700; font-weight: bold; }\n")
	htmlBuilder.WriteString("        .legend { display: flex; gap: 20px; margin: 20px 0; flex-wrap: wrap; }\n")
	htmlBuilder.WriteString("        .legend-item { display: flex; align-items: center; gap: 8px; }\n")
	htmlBuilder.WriteString("        .legend-box { width: 24px; height: 24px; border: 1px solid #999; }\n")
	htmlBuilder.WriteString("        .view-toggle { margin: 20px 0; }\n")
	htmlBuilder.WriteString("        .view-btn { padding: 10px 20px; margin-right: 10px; cursor: pointer; border: 2px solid #4CAF50; background: white; color: #4CAF50; font-size: 14px; border-radius: 5px; }\n")
	htmlBuilder.WriteString("        .view-btn.active { background: #4CAF50; color: white; }\n")
	htmlBuilder.WriteString("        .view-content { display: none; }\n")
	htmlBuilder.WriteString("        .view-content.active { display: block; }\n")
	htmlBuilder.WriteString("        .chart-container { margin: 20px 0; }\n")
	htmlBuilder.WriteString("        .chart-row { margin-bottom: 25px; }\n")
	htmlBuilder.WriteString("        .chart-label { font-weight: bold; margin-bottom: 8px; font-size: 14px; color: #333; }\n")
	htmlBuilder.WriteString("        .chart-bars-container { display: flex; flex-direction: column; gap: 8px; }\n")
	htmlBuilder.WriteString("        .chart-bar-wrapper { display: flex; align-items: center; gap: 10px; }\n")
	htmlBuilder.WriteString("        .chart-bar-label { min-width: 80px; font-weight: 600; color: #555; font-size: 13px; }\n")
	htmlBuilder.WriteString("        .chart-bar { height: 30px; border-radius: 5px; display: flex; align-items: center; justify-content: flex-end; padding-right: 10px; color: white; font-weight: bold; font-size: 12px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); transition: transform 0.2s; min-width: 40px; }\n")
	htmlBuilder.WriteString("        .chart-bar:hover { transform: translateX(5px); box-shadow: 0 4px 8px rgba(0,0,0,0.15); }\n")
	htmlBuilder.WriteString("        .chart-bar-value { margin-left: 10px; font-weight: 600; color: #333; font-size: 13px; min-width: 60px; }\n")
	htmlBuilder.WriteString("        .grouped-chart-section { margin-bottom: 40px; padding: 20px; background: #f9f9f9; border-radius: 8px; }\n")
	htmlBuilder.WriteString("        .grouped-chart-title { font-size: 1.3em; font-weight: bold; color: #667eea; margin-bottom: 15px; border-bottom: 2px solid #667eea; padding-bottom: 8px; }\n")
	htmlBuilder.WriteString("        .grouped-bar-group { display: flex; align-items: center; margin-bottom: 20px; }\n")
	htmlBuilder.WriteString("        .grouped-bar-label { min-width: 100px; font-weight: 600; color: #333; font-size: 13px; }\n")
	htmlBuilder.WriteString("        .grouped-bars { flex: 1; display: flex; flex-direction: column; gap: 4px; }\n")
	htmlBuilder.WriteString("        .grouped-bar-item { display: flex; align-items: center; gap: 8px; }\n")
	htmlBuilder.WriteString("        .grouped-bar { height: 24px; border-radius: 4px; display: flex; align-items: center; justify-content: flex-end; padding-right: 8px; color: white; font-weight: bold; font-size: 11px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); min-width: 30px; }\n")
	htmlBuilder.WriteString("        .grouped-lang-label { min-width: 60px; font-size: 12px; color: #666; }\n")
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
	htmlBuilder.WriteString("    <div class=\"legend\">\n")
	htmlBuilder.WriteString("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #4CAF50; opacity: 0.8;\"></div><span>Normal Engine (N)</span></div>\n")
	htmlBuilder.WriteString("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #2196F3; opacity: 0.8;\"></div><span>PreProcess Engine (P)</span></div>\n")
	htmlBuilder.WriteString("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #90EE90;\"></div><span>Best (Lowest Time - Table View)</span></div>\n")
	htmlBuilder.WriteString("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #FFD700;\"></div><span>Nearest to Average (Table View)</span></div>\n")
	htmlBuilder.WriteString("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #FFB6C6;\"></div><span>Worst (Highest Time - Table View)</span></div>\n")
	htmlBuilder.WriteString("    </div>\n")
	htmlBuilder.WriteString("    <div class=\"view-toggle\">\n")
	htmlBuilder.WriteString("        <button class=\"view-btn active\" data-view=\"grouped\">Grouped View</button>\n")
	htmlBuilder.WriteString("        <button class=\"view-btn\" data-view=\"chart\">Bar Chart View</button>\n")
	htmlBuilder.WriteString("        <button class=\"view-btn\" data-view=\"table\">Table View</button>\n")
	htmlBuilder.WriteString("    </div>\n")

	// Get list of languages dynamically from configuration
	var languages []string
	for lang := range serversByLang {
		languages = append(languages, lang)
	}
	sort.Strings(languages)

	// Sort app keys and filter by RULE_GROUPS
	var appKeys []string
	for k := range appPerf {
		matchesRuleGroup := false
		for _, rule := range RULE_GROUPS {
			if strings.HasPrefix(k, rule) {
				matchesRuleGroup = true
				break
			}
		}
		if matchesRuleGroup {
			appKeys = append(appKeys, k)
		}
	}
	sort.Strings(appKeys)

	// Cache OutputSize per app to ensure consistency across all views
	appOutputSizes := make(map[string]*int)
	for _, app := range appKeys {
		appOutputSizes[app] = getFirstOutputSize(appPerf[app])
	}

	// Combined Bar Chart View
	htmlBuilder.WriteString("    <div id=\"combined-chart\" class=\"view-content\">\n")
	htmlBuilder.WriteString("        <div class=\"chart-container\">\n")

	for _, app := range appKeys {
		htmlBuilder.WriteString("            <div class=\"chart-row\">\n")
		htmlBuilder.WriteString(fmt.Sprintf("                <div class=\"chart-label\">%s</div>\n", app))
		htmlBuilder.WriteString("                <div class=\"chart-bars-container\">\n")

		// Calculate max time across BOTH engines for consistent scaling
		var allTimes []float64
		for _, lang := range languages {
			if appPerf[app][lang].NormalTimeMs != nil && *appPerf[app][lang].NormalTimeMs > 0 {
				allTimes = append(allTimes, *appPerf[app][lang].NormalTimeMs)
			}
			if appPerf[app][lang].PreProcessTimeMs != nil && *appPerf[app][lang].PreProcessTimeMs > 0 {
				allTimes = append(allTimes, *appPerf[app][lang].PreProcessTimeMs)
			}
		}
		maxTimeForScale := 1.0
		if len(allTimes) > 0 {
			maxTimeForScale = *maxFloat(allTimes)
		}

		// Calculate highlighting for Normal Engine
		var normalValidTimes []float64
		for _, lang := range languages {
			if appPerf[app][lang].NormalTimeMs != nil && *appPerf[app][lang].NormalTimeMs > 0 {
				normalValidTimes = append(normalValidTimes, *appPerf[app][lang].NormalTimeMs)
			}
		}
		normalMinTime := minFloat(normalValidTimes)
		normalMaxTime := maxFloat(normalValidTimes)
		normalAvgTime := avgFloat(normalValidTimes)

		// Calculate highlighting for PreProcess Engine
		var preprocessValidTimes []float64
		for _, lang := range languages {
			if appPerf[app][lang].PreProcessTimeMs != nil && *appPerf[app][lang].PreProcessTimeMs > 0 {
				preprocessValidTimes = append(preprocessValidTimes, *appPerf[app][lang].PreProcessTimeMs)
			}
		}
		preprocessMinTime := minFloat(preprocessValidTimes)
		preprocessMaxTime := maxFloat(preprocessValidTimes)
		preprocessAvgTime := avgFloat(preprocessValidTimes)

		for _, lang := range languages {
			if appPerf[app][lang].NormalTimeMs == nil && appPerf[app][lang].PreProcessTimeMs == nil {
				continue
			}
			normalTime := appPerf[app][lang].NormalTimeMs
			preprocessTime := appPerf[app][lang].PreProcessTimeMs

			if (normalTime != nil && *normalTime > 0) || (preprocessTime != nil && *preprocessTime > 0) {
				htmlBuilder.WriteString("                    <div class=\"chart-bar-wrapper\">\n")
				htmlBuilder.WriteString(fmt.Sprintf("                        <div class=\"chart-bar-label\">%s</div>\n", lang))

				// Container for overlapping bars
				htmlBuilder.WriteString("                        <div style=\"position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;\">\n")

				// Normal Engine Bar (bottom layer)
				if normalTime != nil && *normalTime > 0 {
					widthPercent := (*normalTime / maxTimeForScale) * 100
					normalBgColor := "#4CAF50" // default green
					if normalMinTime != nil && isNearValue(*normalTime, *normalMinTime, 0.01) {
						normalBgColor = "#90EE90" // best
					} else if normalMaxTime != nil && isNearValue(*normalTime, *normalMaxTime, 0.01) {
						normalBgColor = "#FFB6C6" // worst
					} else if normalAvgTime != nil && len(normalValidTimes) > 2 {
						nearestToAvg := *normalAvgTime
						for _, t := range normalValidTimes {
							if isNearValue(t, *normalAvgTime, 0.01) {
								nearestToAvg = t
								break
							}
						}
						if isNearValue(*normalTime, nearestToAvg, 0.01) {
							normalBgColor = "#FFD700" // avg
						}
					}

					htmlBuilder.WriteString(fmt.Sprintf("                            <div style=\"position: absolute; left: 0; top: 0; width: %.2f%%; height: 15px; background-color: %s; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"%s Normal: %.2fms\"></div>\n", widthPercent, normalBgColor, lang, *normalTime))

					var normalLabelStyle string
					if widthPercent > 85 {
						normalLabelStyle = fmt.Sprintf("position: absolute; right: calc(100%% - %.2f%% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;", widthPercent)
					} else {
						normalLabelStyle = fmt.Sprintf("position: absolute; left: calc(%.2f%% + 5px); top: 0; font-size: 11px; color: %s; font-weight: 600; white-space: nowrap;", widthPercent, normalBgColor)
					}
					htmlBuilder.WriteString(fmt.Sprintf("                            <span style=\"%s\">N: %.2fms</span>\n", normalLabelStyle, *normalTime))
				}

				// PreProcess Engine Bar (top layer, slightly offset)
				if preprocessTime != nil && *preprocessTime > 0 {
					widthPercent := (*preprocessTime / maxTimeForScale) * 100
					preprocessBgColor := "#2196F3" // default blue
					if preprocessMinTime != nil && isNearValue(*preprocessTime, *preprocessMinTime, 0.01) {
						preprocessBgColor = "#90EE90" // best
					} else if preprocessMaxTime != nil && isNearValue(*preprocessTime, *preprocessMaxTime, 0.01) {
						preprocessBgColor = "#FFB6C6" // worst
					} else if preprocessAvgTime != nil && len(preprocessValidTimes) > 2 {
						nearestToAvg := *preprocessAvgTime
						for _, t := range preprocessValidTimes {
							if isNearValue(t, *preprocessAvgTime, 0.01) {
								nearestToAvg = t
								break
							}
						}
						if isNearValue(*preprocessTime, nearestToAvg, 0.01) {
							preprocessBgColor = "#FFD700" // avg
						}
					}

					htmlBuilder.WriteString(fmt.Sprintf("                            <div style=\"position: absolute; left: 0; top: 15px; width: %.2f%%; height: 15px; background-color: %s; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"%s PreProcess: %.2fms\"></div>\n", widthPercent, preprocessBgColor, lang, *preprocessTime))

					var preprocessLabelStyle string
					if widthPercent > 85 {
						preprocessLabelStyle = fmt.Sprintf("position: absolute; right: calc(100%% - %.2f%% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;", widthPercent)
					} else {
						preprocessLabelStyle = fmt.Sprintf("position: absolute; left: calc(%.2f%% + 5px); top: 15px; font-size: 11px; color: %s; font-weight: 600; white-space: nowrap;", widthPercent, preprocessBgColor)
					}
					htmlBuilder.WriteString(fmt.Sprintf("                            <span style=\"%s\">P: %.2fms</span>\n", preprocessLabelStyle, *preprocessTime))
				}

				htmlBuilder.WriteString("                        </div>\n")
				htmlBuilder.WriteString("                    </div>\n")
			}
		}

		htmlBuilder.WriteString("                </div>\n")
		htmlBuilder.WriteString("            </div>\n")
	}

	htmlBuilder.WriteString("        </div>\n")
	htmlBuilder.WriteString("    </div>\n")

	// Grouped Bar Chart View (default active)
	htmlBuilder.WriteString("    <div id=\"combined-grouped\" class=\"view-content active\">\n")
	htmlBuilder.WriteString("        <div class=\"chart-container\">\n")

	for _, rulePattern := range RULE_GROUPS {
		// Find all apps matching this rule pattern
		var matchingApps []string
		for _, app := range appKeys {
			if strings.HasPrefix(app, rulePattern) && !strings.Contains(app, "Test") {
				matchingApps = append(matchingApps, app)
			}
		}

		if len(matchingApps) == 0 {
			continue
		}

		htmlBuilder.WriteString("            <div class=\"grouped-chart-section\">\n")
		htmlBuilder.WriteString(fmt.Sprintf("                <div class=\"grouped-chart-title\">%s</div>\n", rulePattern))
		htmlBuilder.WriteString("                <div class=\"chart-bars-container\">\n")

		// Calculate max time across ALL languages in this rule group for consistent scaling
		var allMaxValues []float64
		for _, lang := range languages {
			normalTimes := []float64{}
			for _, app := range matchingApps {
				if appPerf[app][lang].NormalTimeMs != nil && *appPerf[app][lang].NormalTimeMs > 0 {
					normalTimes = append(normalTimes, *appPerf[app][lang].NormalTimeMs)
				}
			}
			preprocessTimes := []float64{}
			for _, app := range matchingApps {
				if appPerf[app][lang].PreProcessTimeMs != nil && *appPerf[app][lang].PreProcessTimeMs > 0 {
					preprocessTimes = append(preprocessTimes, *appPerf[app][lang].PreProcessTimeMs)
				}
			}

			if len(normalTimes) > 0 {
				if maxVal := maxFloat(normalTimes); maxVal != nil {
					allMaxValues = append(allMaxValues, *maxVal)
				}
			}
			if len(preprocessTimes) > 0 {
				if maxVal := maxFloat(preprocessTimes); maxVal != nil {
					allMaxValues = append(allMaxValues, *maxVal)
				}
			}
		}
		maxTimeForScale := 1.0
		if len(allMaxValues) > 0 {
			if maxVal := maxFloat(allMaxValues); maxVal != nil {
				maxTimeForScale = *maxVal
			}
		}

		// For each language, calculate min/avg/max across all apps in this rule group
		for _, lang := range languages {
			// Collect Normal Engine times for this language across all apps in the group
			var normalTimes []float64
			for _, app := range matchingApps {
				if appPerf[app][lang].NormalTimeMs != nil && *appPerf[app][lang].NormalTimeMs > 0 {
					normalTimes = append(normalTimes, *appPerf[app][lang].NormalTimeMs)
				}
			}

			// Collect PreProcess Engine times for this language across all apps in the group
			var preprocessTimes []float64
			for _, app := range matchingApps {
				if appPerf[app][lang].PreProcessTimeMs != nil && *appPerf[app][lang].PreProcessTimeMs > 0 {
					preprocessTimes = append(preprocessTimes, *appPerf[app][lang].PreProcessTimeMs)
				}
			}

			if len(normalTimes) == 0 && len(preprocessTimes) == 0 {
				continue
			}

			// Calculate aggregates
			normalMin := minFloat(normalTimes)
			normalAvg := avgFloat(normalTimes)
			normalMax := maxFloat(normalTimes)

			preprocessMin := minFloat(preprocessTimes)
			preprocessAvg := avgFloat(preprocessTimes)
			preprocessMax := maxFloat(preprocessTimes)

			htmlBuilder.WriteString("                    <div class=\"chart-bar-wrapper\">\n")
			htmlBuilder.WriteString(fmt.Sprintf("                        <div class=\"chart-bar-label\">%s</div>\n", lang))

			// Container for overlapping bars
			htmlBuilder.WriteString("                        <div style=\"position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;\">\n")

			// Normal Engine Bar (showing min, avg, max as segments)
			if normalMin != nil && normalAvg != nil && normalMax != nil {
				minWidth := (*normalMin / maxTimeForScale) * 100
				avgWidth := (*normalAvg / maxTimeForScale) * 100
				maxWidth := (*normalMax / maxTimeForScale) * 100

				// Draw max bar (light green background)
				htmlBuilder.WriteString(fmt.Sprintf("                            <div style=\"position: absolute; left: 0; top: 0; width: %.2f%%; height: 15px; background-color: #90EE90; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"%s Normal Max: %.2fms\"></div>\n", maxWidth, lang, *normalMax))

				// Draw avg bar (gold - middle layer)
				htmlBuilder.WriteString(fmt.Sprintf("                            <div style=\"position: absolute; left: 0; top: 0; width: %.2f%%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"%s Normal Avg: %.2fms\"></div>\n", avgWidth, lang, *normalAvg))

				// Draw min bar (dark green - top layer)
				htmlBuilder.WriteString(fmt.Sprintf("                            <div style=\"position: absolute; left: 0; top: 0; width: %.2f%%; height: 15px; background-color: #4CAF50; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"%s Normal Min: %.2fms\"></div>\n", minWidth, lang, *normalMin))

				// Label at end of max bar
				var labelStyle string
				if maxWidth > 85 {
					labelStyle = fmt.Sprintf("position: absolute; right: calc(100%% - %.2f%% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;", maxWidth)
				} else {
					labelStyle = fmt.Sprintf("position: absolute; left: calc(%.2f%% + 5px); top: 0; font-size: 11px; color: #4CAF50; font-weight: 600; white-space: nowrap;", maxWidth)
				}
				htmlBuilder.WriteString(fmt.Sprintf("                            <span style=\"%s\">N: %.2f/%.2f/%.2f</span>\n", labelStyle, *normalMin, *normalAvg, *normalMax))
			}

			// PreProcess Engine Bar (showing min, avg, max as segments)
			if preprocessMin != nil && preprocessAvg != nil && preprocessMax != nil {
				minWidth := (*preprocessMin / maxTimeForScale) * 100
				avgWidth := (*preprocessAvg / maxTimeForScale) * 100
				maxWidth := (*preprocessMax / maxTimeForScale) * 100

				// Draw max bar (light pink background)
				htmlBuilder.WriteString(fmt.Sprintf("                            <div style=\"position: absolute; left: 0; top: 15px; width: %.2f%%; height: 15px; background-color: #FFB6C6; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"%s PreProcess Max: %.2fms\"></div>\n", maxWidth, lang, *preprocessMax))

				// Draw avg bar (gold - middle layer)
				htmlBuilder.WriteString(fmt.Sprintf("                            <div style=\"position: absolute; left: 0; top: 15px; width: %.2f%%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"%s PreProcess Avg: %.2fms\"></div>\n", avgWidth, lang, *preprocessAvg))

				// Draw min bar (dark blue - top layer)
				htmlBuilder.WriteString(fmt.Sprintf("                            <div style=\"position: absolute; left: 0; top: 15px; width: %.2f%%; height: 15px; background-color: #2196F3; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"%s PreProcess Min: %.2fms\"></div>\n", minWidth, lang, *preprocessMin))

				// Label at end of max bar
				var labelStyle string
				if maxWidth > 85 {
					labelStyle = fmt.Sprintf("position: absolute; right: calc(100%% - %.2f%% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;", maxWidth)
				} else {
					labelStyle = fmt.Sprintf("position: absolute; left: calc(%.2f%% + 5px); top: 15px; font-size: 11px; color: #2196F3; font-weight: 600; white-space: nowrap;", maxWidth)
				}
				htmlBuilder.WriteString(fmt.Sprintf("                            <span style=\"%s\">P: %.2f/%.2f/%.2f</span>\n", labelStyle, *preprocessMin, *preprocessAvg, *preprocessMax))
			}

			htmlBuilder.WriteString("                        </div>\n")
			htmlBuilder.WriteString("                    </div>\n")
		}

		htmlBuilder.WriteString("                </div>\n")
		htmlBuilder.WriteString("            </div>\n")
	}

	htmlBuilder.WriteString("        </div>\n")
	htmlBuilder.WriteString("    </div>\n")

	// Table View - Normal Engine
	htmlBuilder.WriteString("    <div id=\"normal-table\" class=\"view-content\">\n")
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

	for _, app := range appKeys {
		// Find min, max, and avg time for highlighting
		var validTimes []float64
		for _, lang := range languages {
			if appPerf[app][lang].NormalTimeMs != nil && *appPerf[app][lang].NormalTimeMs > 0 {
				validTimes = append(validTimes, *appPerf[app][lang].NormalTimeMs)
			}
		}
		minTime := minFloat(validTimes)
		maxTime := maxFloat(validTimes)
		avgTime := avgFloat(validTimes)

		htmlBuilder.WriteString("        <tr>\n")
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", app))
		for _, lang := range languages {
			timeValue := formatFloat(appPerf[app][lang].NormalTimeMs)
			cssClass := ""
			if appPerf[app][lang].NormalTimeMs != nil && *appPerf[app][lang].NormalTimeMs > 0 {
				currentTime := *appPerf[app][lang].NormalTimeMs
				if minTime != nil && isNearValue(currentTime, *minTime, 0.001) {
					cssClass = " class=\"best-perf\""
				} else if maxTime != nil && isNearValue(currentTime, *maxTime, 0.001) {
					cssClass = " class=\"worst-perf\""
				} else if avgTime != nil && len(validTimes) > 2 {
					// Find the value nearest to average
					nearestToAvg := *avgTime
					for _, t := range validTimes {
						if isNearValue(t, *avgTime, 0.001) {
							nearestToAvg = t
							break
						}
					}
					if isNearValue(currentTime, nearestToAvg, 0.001) {
						cssClass = " class=\"avg-perf\""
					}
				}
			}
			htmlBuilder.WriteString(fmt.Sprintf("            <td%s>%s</td>\n", cssClass, timeValue))
		}
		outputSize := formatInt(appOutputSizes[app])
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", outputSize))
		htmlBuilder.WriteString("        </tr>\n")
	}
	htmlBuilder.WriteString("    </table>\n")
	htmlBuilder.WriteString("    </div>\n")
	htmlBuilder.WriteString("    </div>\n")

	// Table View - PreProcess Engine
	htmlBuilder.WriteString("    <div id=\"preprocess-table\" class=\"view-content\">\n")
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
		// Find min, max, and avg time for highlighting
		var validTimes []float64
		for _, lang := range languages {
			if appPerf[app][lang].PreProcessTimeMs != nil && *appPerf[app][lang].PreProcessTimeMs > 0 {
				validTimes = append(validTimes, *appPerf[app][lang].PreProcessTimeMs)
			}
		}
		minTime := minFloat(validTimes)
		maxTime := maxFloat(validTimes)
		avgTime := avgFloat(validTimes)

		htmlBuilder.WriteString("        <tr>\n")
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", app))
		for _, lang := range languages {
			timeValue := formatFloat(appPerf[app][lang].PreProcessTimeMs)
			cssClass := ""
			if appPerf[app][lang].PreProcessTimeMs != nil && *appPerf[app][lang].PreProcessTimeMs > 0 {
				currentTime := *appPerf[app][lang].PreProcessTimeMs
				if minTime != nil && isNearValue(currentTime, *minTime, 0.001) {
					cssClass = " class=\"best-perf\""
				} else if maxTime != nil && isNearValue(currentTime, *maxTime, 0.001) {
					cssClass = " class=\"worst-perf\""
				} else if avgTime != nil && len(validTimes) > 2 {
					nearestToAvg := *avgTime
					for _, t := range validTimes {
						if isNearValue(t, *avgTime, 0.001) {
							nearestToAvg = t
							break
						}
					}
					if isNearValue(currentTime, nearestToAvg, 0.001) {
						cssClass = " class=\"avg-perf\""
					}
				}
			}
			htmlBuilder.WriteString(fmt.Sprintf("            <td%s>%s</td>\n", cssClass, timeValue))
		}
		outputSize := formatInt(appOutputSizes[app])
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", outputSize))
		htmlBuilder.WriteString("        </tr>\n")
	}
	htmlBuilder.WriteString("    </table>\n")
	htmlBuilder.WriteString("    </div>\n")
	htmlBuilder.WriteString("    </div>\n")

	// Add JavaScript for view switching
	htmlBuilder.WriteString("    <script>\n")
	htmlBuilder.WriteString("        document.querySelectorAll('.view-btn').forEach(btn => {\n")
	htmlBuilder.WriteString("            btn.addEventListener('click', function() {\n")
	htmlBuilder.WriteString("                const view = this.getAttribute('data-view');\n")
	htmlBuilder.WriteString("                document.querySelectorAll('.view-btn').forEach(b => b.classList.remove('active'));\n")
	htmlBuilder.WriteString("                this.classList.add('active');\n")
	htmlBuilder.WriteString("                document.querySelectorAll('.view-content').forEach(v => v.classList.remove('active'));\n")
	htmlBuilder.WriteString("                if (view === 'chart') {\n")
	htmlBuilder.WriteString("                    document.getElementById('combined-chart').classList.add('active');\n")
	htmlBuilder.WriteString("                } else if (view === 'grouped') {\n")
	htmlBuilder.WriteString("                    document.getElementById('combined-grouped').classList.add('active');\n")
	htmlBuilder.WriteString("                } else if (view === 'table') {\n")
	htmlBuilder.WriteString("                    document.getElementById('normal-table').classList.add('active');\n")
	htmlBuilder.WriteString("                    document.getElementById('preprocess-table').classList.add('active');\n")
	htmlBuilder.WriteString("                }\n")
	htmlBuilder.WriteString("            });\n")
	htmlBuilder.WriteString("        });\n")
	htmlBuilder.WriteString("    </script>\n")
	htmlBuilder.WriteString("</body>\n</html>\n")

	// Write HTML to template_analysis/Reports directory
	reportsDir := filepath.Join(getProjectDirectory(), "template_analysis", "Reports")
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

// GetReport handles the POST /api/report endpoint
func GetReport(c *gin.Context) {
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
	reportsDir := filepath.Join(getProjectDirectory(), "template_analysis", "Reports")
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

// SaveLog handles the POST /api/save-log endpoint
func SaveLog(c *gin.Context) {
	var req struct {
		Context string `json:"context"`
		Content string `json:"content"`
	}
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid JSON format"})
		return
	}

	// Trim whitespace from content
	req.Content = strings.TrimSpace(req.Content)

	// Check for missing context
	if req.Context == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: context"})
		return
	}

	// If content is empty after trimming, return success without saving
	if req.Content == "" {
		c.JSON(http.StatusOK, gin.H{"message": "No content to save"})
		return
	}

	if !IsValidPathComponent(&req.Context) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in context"})
		return
	}

	// Validate log content (size and format)
	isValid, errorMessage := IsValidLogContent(&req.Content)
	if !isValid {
		c.JSON(http.StatusBadRequest, gin.H{"error": errorMessage})
		return
	}

	logsDir := filepath.Join(getProjectDirectory(), "template_analysis", "logs")
	os.MkdirAll(logsDir, 0755)
	logFile := filepath.Join(logsDir, fmt.Sprintf("javascript_%s.log", strings.ToLower(req.Context)))
	if err := os.WriteFile(logFile, []byte(req.Content), 0644); err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to save log file"})
		return
	}
	c.JSON(http.StatusOK, gin.H{"message": "Log saved successfully"})
}

// SaveOutput handles the POST /api/save-output endpoint
func SaveOutput(c *gin.Context) {
	log.Println("[/api/save-output] Endpoint called")

	var req struct {
		AppSite    string `json:"appSite"`
		AppView    string `json:"appView"`
		EngineType string `json:"engineType"`
		Html       string `json:"html"`
	}
	if err := c.ShouldBindJSON(&req); err != nil {
		log.Printf("[/api/save-output] JSON parse error: %v", err)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid JSON format"})
		return
	}

	log.Printf("[/api/save-output] Parsed: appSite=%s, appView=%s, engineType=%s, htmlLength=%d", req.AppSite, req.AppView, req.EngineType, len(req.Html))

	if req.AppSite == "" || req.EngineType == "" || req.Html == "" {
		log.Printf("[/api/save-output] Missing parameters: appSite=%s, engineType=%s, htmlLength=%d", req.AppSite, req.EngineType, len(req.Html))
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required parameters"})
		return
	}

	// Validate AppSite against allowlist
	validAppSites, err := GetValidAppSites()
	if err != nil {
		log.Printf("[/api/save-output] Failed to load AppSites: %v", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": fmt.Sprintf("Failed to load AppSites: %v", err)})
		return
	}
	appSiteLower := strings.ToLower(req.AppSite)
	isValid := false
	for validAppSite := range validAppSites {
		if strings.ToLower(validAppSite) == appSiteLower {
			isValid = true
			break
		}
	}
	if !isValid {
		log.Printf("[/api/save-output] Invalid AppSite: %s", req.AppSite)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid AppSite value"})
		return
	}

	// Validate engine type against allowlist
	if !IsValidEngineType(req.EngineType) {
		log.Printf("[/api/save-output] Invalid engineType: %s", req.EngineType)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid engine type"})
		return
	}

	// Validate path components
	if !IsValidPathComponent(&req.AppSite) {
		log.Printf("[/api/save-output] Invalid AppSite path component: %s", req.AppSite)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid AppSite parameter"})
		return
	}
	if req.AppView != "" && !IsValidPathComponent(&req.AppView) {
		log.Printf("[/api/save-output] Invalid AppView path component: %s", req.AppView)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid AppView parameter"})
		return
	}
	if !IsValidPathComponent(&req.EngineType) {
		log.Printf("[/api/save-output] Invalid engineType path component: %s", req.EngineType)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid engineType parameter"})
		return
	}

	// Validate output size against template size + buffer
	templateTotalSize := GetTemplateTotalSize(req.AppSite, req.AppView)
	outputSize := len(req.Html)
	maxAllowedSize := templateTotalSize + OutputSizeBuffer
	log.Printf("[/api/save-output] Size validation: output=%d, template=%d, buffer=%d, max=%d", outputSize, templateTotalSize, OutputSizeBuffer, maxAllowedSize)

	if !IsValidOutputSizeWithBuffer(&req.Html, templateTotalSize) {
		errorMsg := fmt.Sprintf("Save output failed: output size (%d bytes) exceeds max size allowed (%d bytes = template %d + buffer %d)", outputSize, maxAllowedSize, templateTotalSize, OutputSizeBuffer)
		log.Printf("[/api/save-output] %s", errorMsg)
		c.JSON(http.StatusBadRequest, gin.H{"error": errorMsg})
		return
	}

	outputDir := filepath.Join(getProjectDirectory(), "template_analysis", "output")
	os.MkdirAll(outputDir, 0755)
	appViewSuffix := ""
	if req.AppView != "" {
		appViewSuffix = "_" + req.AppView
	}
	engineSuffix := strings.ToLower(req.EngineType)
	outputFile := filepath.Join(outputDir, fmt.Sprintf("javascript_%s%s_%s.html", req.AppSite, appViewSuffix, engineSuffix))
	if err := os.WriteFile(outputFile, []byte(req.Html), 0644); err != nil {
		log.Printf("[/api/save-output] Failed to write file: %v", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to save output file"})
		return
	}
	log.Printf("[/api/save-output] Success! Output saved to: %s", outputFile)
	c.JSON(http.StatusOK, gin.H{"message": "Output saved successfully"})
}

// SaveTestResults handles the POST /api/test-results endpoint
func SaveTestResults(c *gin.Context) {
	var summaryRows []map[string]interface{}
	if err := c.ShouldBindJSON(&summaryRows); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid test results format"})
		return
	}
	outputDir := filepath.Join(getProjectDirectory(), "template_analysis", "Reports")
	os.MkdirAll(outputDir, 0755)

	// Get test type from query parameter
	testType := c.Query("testType")
	if testType == "" {
		testType = "standardtest"
	}
	testTypeFile := strings.ToLower(strings.ReplaceAll(strings.ReplaceAll(testType, " ", ""), "-", ""))

	htmlFile := filepath.Join(outputDir, fmt.Sprintf("javascript_%s_Summary.html", testTypeFile))
	jsonFile := filepath.Join(outputDir, fmt.Sprintf("javascript_%s_Summary.json", testTypeFile))

	// Generate HTML table matching Rust format
	formattedTestType := strings.ToUpper(strings.ReplaceAll(testType, "test", " TEST"))
	var htmlBuilder strings.Builder
	htmlBuilder.WriteString("<!DOCTYPE html>\n<html>\n<head>\n")
	htmlBuilder.WriteString("    <meta charset=\"UTF-8\">\n")
	htmlBuilder.WriteString("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n")
	htmlBuilder.WriteString(fmt.Sprintf("    <title>JavaScript %s</title>\n", formattedTestType))
	htmlBuilder.WriteString("    <style>\n")
	htmlBuilder.WriteString("        body { font-family: Arial, sans-serif; margin: 20px; }\n")
	htmlBuilder.WriteString("        h1 { color: #333; }\n")
	htmlBuilder.WriteString("        .table-container { overflow-x: auto; }\n")
	htmlBuilder.WriteString("        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }\n")
	htmlBuilder.WriteString("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n")
	htmlBuilder.WriteString("        th { background-color: #4CAF50; color: white; }\n")
	htmlBuilder.WriteString("        tr:nth-child(even) { background-color: #f2f2f2; }\n")
	htmlBuilder.WriteString("        .pass { color: green; font-weight: bold; }\n")
	htmlBuilder.WriteString("        .fail { color: red; font-weight: bold; }\n")
	htmlBuilder.WriteString("        @media (max-width: 768px) {\n")
	htmlBuilder.WriteString("            body { margin: 10px; }\n")
	htmlBuilder.WriteString("            th, td { padding: 8px; font-size: 14px; }\n")
	htmlBuilder.WriteString("            h1 { font-size: 24px; }\n")
	htmlBuilder.WriteString("        }\n")
	htmlBuilder.WriteString("    </style>\n</head>\n<body>\n")
	htmlBuilder.WriteString(fmt.Sprintf("    <h1>JavaScript %s</h1>\n", formattedTestType))
	htmlBuilder.WriteString(fmt.Sprintf("    <div class=\"meta\" style=\"color: #666; font-style: italic; margin-bottom: 10px;\">Generated: %s UTC</div>\n", time.Now().UTC().Format("2006-01-02 15:04:05")))
	htmlBuilder.WriteString("    <div class=\"table-container\">\n    <table>\n        <tr>\n")
	htmlBuilder.WriteString("            <th>AppSite</th>\n            <th>AppFile</th>\n            <th>AppView</th>\n")
	htmlBuilder.WriteString("            <th>OutputMatch</th>\n            <th>ViewUnMatch</th>\n            <th>Error</th>\n        </tr>\n")

	for _, row := range summaryRows {
		appSite := getStringField(row, "AppSite", "app_site", "appSite")
		appFile := getStringField(row, "AppFile", "app_file", "appFile")
		appView := getStringField(row, "AppView", "app_view", "appView")
		normalPreProcess := getStringField(row, "NormalPreProcess", "normal_pre_process", "normalPreProcess")
		crossViewUnMatch := getStringField(row, "CrossViewUnMatch", "cross_view_un_match", "crossViewUnMatch")
		errorMsg := getStringField(row, "Error", "error")

		outputMatchClass := ""
		if normalPreProcess == "PASS" {
			outputMatchClass = "pass"
		} else if normalPreProcess == "FAIL" {
			outputMatchClass = "fail"
		}

		viewUnmatchClass := ""
		if crossViewUnMatch == "PASS" {
			viewUnmatchClass = "pass"
		} else if crossViewUnMatch == "FAIL" {
			viewUnmatchClass = "fail"
		}

		htmlBuilder.WriteString("        <tr>\n")
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", appSite))
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", appFile))
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", appView))
		htmlBuilder.WriteString(fmt.Sprintf("            <td class=\"%s\">%s</td>\n", outputMatchClass, normalPreProcess))
		htmlBuilder.WriteString(fmt.Sprintf("            <td class=\"%s\">%s</td>\n", viewUnmatchClass, crossViewUnMatch))
		htmlBuilder.WriteString(fmt.Sprintf("            <td>%s</td>\n", errorMsg))
		htmlBuilder.WriteString("        </tr>\n")
	}

	htmlBuilder.WriteString("    </table>\n    </div>\n</body>\n</html>")

	os.WriteFile(htmlFile, []byte(htmlBuilder.String()), 0644)

	// Save JSON summary file
	jsonBytes, _ := json.MarshalIndent(summaryRows, "", "  ")
	os.WriteFile(jsonFile, jsonBytes, 0644)
	c.JSON(http.StatusOK, gin.H{"message": "Test results saved successfully"})
}

// SavePerformanceResults handles the POST /api/performance-results endpoint
func SavePerformanceResults(c *gin.Context) {
	var summaryRows []map[string]interface{}
	if err := c.ShouldBindJSON(&summaryRows); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid performance results format"})
		return
	}
	outputDir := filepath.Join(getProjectDirectory(), "template_analysis", "Reports")
	os.MkdirAll(outputDir, 0755)

	// Generate HTML table for performance results
	var htmlBuilder strings.Builder
	htmlBuilder.WriteString("<html><head><title>Client-Side Performance Summary Table</title>\n")
	htmlBuilder.WriteString("<style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}.meta{color:#666;font-style:italic;margin-bottom:10px;}</style></head><body>\n")
	htmlBuilder.WriteString("<h2>Client-Side JavaScript PERFORMANCE SUMMARY TABLE</h2>\n")
	htmlBuilder.WriteString(fmt.Sprintf("<div class=\"meta\">Generated: %s UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>\n", time.Now().UTC().Format("2006-01-02 15:04:05")))
	htmlBuilder.WriteString("<table>\n<tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th><th>ScnTime(ms)</th><th>Elapsed(ms)</th></tr>\n")

	for _, row := range summaryRows {
		appSite := getStringField(row, "AppSite", "app_site", "appSite")
		appView := getStringField(row, "AppView", "app_view", "appView")
		normalTimeMs := getFloatField(row, "NormalTimeMs", "normal_time_ms", "normalTimeMs")
		preProcessTimeMs := getFloatField(row, "PreProcessTimeMs", "preprocess_time_ms", "preProcessTimeMs")
		resultsMatch := getStringField(row, "ResultsMatch", "results_match", "resultsMatch")
		perfDifference := getStringField(row, "PerfDifference", "perf_difference", "perfDifference")
		scenarioTotalTimeMs := getIntField(row, "ScenarioTotalTimeMs", "scenario_total_time_ms", "scenarioTotalTimeMs")
		elapsedTimeMs := getIntField(row, "ElapsedTimeMs", "elapsed_time_ms", "elapsedTimeMs")

		htmlBuilder.WriteString("<tr>\n")
		htmlBuilder.WriteString(fmt.Sprintf("<td>%s</td>\n", appSite))
		htmlBuilder.WriteString(fmt.Sprintf("<td>%s</td>\n", appView))
		htmlBuilder.WriteString(fmt.Sprintf("<td>%.2f</td>\n", normalTimeMs))
		htmlBuilder.WriteString(fmt.Sprintf("<td>%.2f</td>\n", preProcessTimeMs))
		htmlBuilder.WriteString(fmt.Sprintf("<td>%s</td>\n", resultsMatch))
		htmlBuilder.WriteString(fmt.Sprintf("<td>%s</td>\n", perfDifference))
		htmlBuilder.WriteString(fmt.Sprintf("<td>%d</td>\n", scenarioTotalTimeMs))
		htmlBuilder.WriteString(fmt.Sprintf("<td>%d</td>\n", elapsedTimeMs))
		htmlBuilder.WriteString("</tr>\n")
	}

	htmlBuilder.WriteString("</table></body></html>")

	htmlFile := filepath.Join(outputDir, "javascript_perfsummary.html")
	os.WriteFile(htmlFile, []byte(htmlBuilder.String()), 0644)

	// Save JSON summary file
	jsonFile := filepath.Join(outputDir, "javascript_perfsummary.json")
	jsonBytes, _ := json.MarshalIndent(summaryRows, "", "  ")
	os.WriteFile(jsonFile, jsonBytes, 0644)

	c.JSON(http.StatusOK, gin.H{"message": "Performance results saved successfully"})
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

func getFloatFieldPtr(m map[string]interface{}, keys ...string) *float64 {
	for _, key := range keys {
		if val, ok := m[key]; ok {
			switch v := val.(type) {
			case float64:
				return &v
			case int:
				f := float64(v)
				return &f
			}
		}
	}
	return nil
}

func getIntFieldPtr(m map[string]interface{}, keys ...string) *int {
	for _, key := range keys {
		if val, ok := m[key]; ok {
			switch v := val.(type) {
			case int:
				return &v
			case float64:
				i := int(v)
				return &i
			}
		}
	}
	return nil
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

// Helper functions for statistical calculations
func minFloat(vals []float64) *float64 {
	if len(vals) == 0 {
		return nil
	}
	min := vals[0]
	for _, v := range vals {
		if v < min {
			min = v
		}
	}
	return &min
}

func maxFloat(vals []float64) *float64 {
	if len(vals) == 0 {
		return nil
	}
	max := vals[0]
	for _, v := range vals {
		if v > max {
			max = v
		}
	}
	return &max
}

func avgFloat(vals []float64) *float64 {
	if len(vals) == 0 {
		return nil
	}
	sum := 0.0
	for _, v := range vals {
		sum += v
	}
	avg := sum / float64(len(vals))
	return &avg
}

func isNearValue(a, b float64, epsilon float64) bool {
	diff := a - b
	return diff < epsilon && diff > -epsilon
}

// MapAssemblerTestEndpoints maps all assembler test endpoints to the router
func MapAssemblerTestEndpoints(r *gin.Engine) {
	r.POST("/test/standard", TestStandard)
	r.POST("/test/advanced", TestAdvanced)
	r.POST("/test/performance", TestPerformance)
	r.POST("/test/consolidate-performance", TestConsolidatePerformance)
	r.POST("/api/report", GetReport)
	r.POST("/api/save-log", SaveLog)
	r.POST("/api/save-output", SaveOutput)
	r.POST("/api/test-results", SaveTestResults)
	r.POST("/api/performance-results", SavePerformanceResults)
}

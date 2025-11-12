// PerformanceUtils implementation for Go
// This module matches the C#/Rust structure

package performance

import (
	"assembler/common"
	"assembler/config"
	enginenormal "assembler/engine/normal"
	enginepreprocess "assembler/engine/preprocess"
	loadernormal "assembler/loader/normal"
	loaderpreprocess "assembler/loader/preprocess"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

type PerfSummaryRow struct {
	AppSite               string  `json:"AppSite"`
	AppFile               string  `json:"AppFile"`
	AppView               string  `json:"AppView"`
	Iterations            int     `json:"Iterations"`
	NormalTimeNanos       int64   `json:"NormalTimeNanos"`
	PreProcessTimeNanos   int64   `json:"PreProcessTimeNanos"`
	OutputSize            int     `json:"OutputSize"`
	ResultsMatch          string  `json:"ResultsMatch"`
	PerfDifference        string  `json:"PerfDifference"`
	ScenarioTotalTimeMs   int64   `json:"ScenarioTotalTimeMs"`
	ElapsedTimeMs         int64   `json:"ElapsedTimeMs"`
	NormalTimeMsValue     float64 `json:"NormalTimeMs"`
	PreProcessTimeMsValue float64 `json:"PreProcessTimeMs"`
}

// Helper methods for display
func (p *PerfSummaryRow) NormalTimeMs() float64 {
	return float64(p.NormalTimeNanos) / 1_000_000.0
}

func (p *PerfSummaryRow) PreProcessTimeMs() float64 {
	return float64(p.PreProcessTimeNanos) / 1_000_000.0
}

func RunPerformanceComparison(assemblerWebDir, projectDirectory string, scenarios []config.Scenario, searchAppSites string, skipDetails bool, enableJsonProcessing bool) []PerfSummaryRow {
	startTime := time.Now()

	if assemblerWebDir == "" {
		fmt.Println("❌ No assemblerWebDirPath passed")
		return []PerfSummaryRow{}
	}

	if len(scenarios) == 0 {
		fmt.Println("❌ No scenarios passed")
		return []PerfSummaryRow{}
	}

	iterations := 1000
	var perfSummaryRows []PerfSummaryRow

	for _, scenario := range scenarios {
		scenarioStartTime := time.Now()
		testAppSite := scenario.AppSite
		appFileName := scenario.AppFile
		appView := scenario.AppView

		// Calculate appViewPrefix
		appViewPrefix := ""
		if appView != "" {
			if len(appView) > 6 {
				appViewPrefix = appView[:6]
			} else {
				appViewPrefix = appView
			}
		}

		if !skipDetails {
			fmt.Println(strings.Repeat("-", 60))
			fmt.Printf("[Go] Testing: AppSite=%s, AppFile=%s, AppView=%s\n", testAppSite, appFileName, appView)
			fmt.Printf("[Go] Iterations: %d\n", iterations)
		}

		loaderNormal := loadernormal.NewLoaderNormal(assemblerWebDir, testAppSite, searchAppSites)
		loaderNormal.ClearCache()
		loaderPreProcess := loaderpreprocess.NewLoaderPreProcess(assemblerWebDir, testAppSite, searchAppSites)

		if !loaderNormal.HasTemplate(testAppSite, appFileName) {
			continue
		}

		normalEngine := enginenormal.NewEngineNormal(appFileName)
		normalEngine.SetAppViewPrefix(appViewPrefix)

		// Warmup - run a few iterations first to ensure consistent performance
		for warmup := 0; warmup < 100; warmup++ {
			normalEngine.MergeTemplates(testAppSite, appFileName, appView, loaderNormal, enableJsonProcessing)
		}

		start := time.Now()
		var resultNormal string
		for i := 0; i < iterations; i++ {
			resultNormal = normalEngine.MergeTemplates(testAppSite, appFileName, appView, loaderNormal, enableJsonProcessing)
		}
		normalTimeNanos := time.Since(start).Nanoseconds()
		if !skipDetails {
			normalTimeMs := float64(normalTimeNanos) / 1_000_000.0
			avg := normalTimeMs / float64(iterations)
			fmt.Printf("[Go] Normal Engine:     %.0fms | Avg: %.3fms/op | Size: %d chars\n", normalTimeMs, avg, common.Utf16Len(resultNormal))
		}

		loaderNormal.ClearCache()
		loaderPreProcess.ClearCache()
		preprocessEngine := enginepreprocess.NewEnginePreProcess(appFileName)
		preprocessEngine.SetAppViewPrefix(appViewPrefix)

		// Warmup for PreProcess engine
		for warmup := 0; warmup < 100; warmup++ {
			preprocessEngine.MergeTemplates(testAppSite, appFileName, appView, loaderPreProcess, enableJsonProcessing)
		}

		start = time.Now()
		var resultPreprocess string
		for i := 0; i < iterations; i++ {
			resultPreprocess = preprocessEngine.MergeTemplates(testAppSite, appFileName, appView, loaderPreProcess, enableJsonProcessing)
		}
		preprocessTimeNanos := time.Since(start).Nanoseconds()
		resultsMatch := resultNormal == resultPreprocess

		if !skipDetails {
			preprocessTimeMs := float64(preprocessTimeNanos) / 1_000_000.0
			avg := preprocessTimeMs / float64(iterations)
			fmt.Printf("[Go] PreProcess Engine: %.0fms | Avg: %.3fms/op | Size: %d chars\n", preprocessTimeMs, avg, common.Utf16Len(resultPreprocess))

			differenceMs := preprocessTimeMs - float64(normalTimeNanos)/1_000_000.0
			differencePercent := 0.0
			if normalTimeNanos > 0 {
				differencePercent = (float64(preprocessTimeNanos-normalTimeNanos) / float64(normalTimeNanos)) * 100.0
			}
			signMs := ""
			if differenceMs >= 0 {
				signMs = "+"
			}
			signPct := ""
			if differencePercent >= 0 {
				signPct = "+"
			}
			matchStr := "NO"
			if resultsMatch {
				matchStr = "YES"
			}
			fmt.Printf("[Go] Performance: %s%.0fms (%s%.1f%%) | Match: %s\n", signMs, differenceMs, signPct, differencePercent, matchStr)
		}

		scenarioTotalTime := time.Since(scenarioStartTime).Milliseconds()
		elapsedTime := time.Since(startTime).Milliseconds()

		if !skipDetails {
			fmt.Printf("[Go] Scenario Total Time: %dms | Elapsed: %dms\n", scenarioTotalTime, elapsedTime)
		}

		perfDiff := "0%"
		if normalTimeNanos > 0 {
			perfDiff = fmt.Sprintf("%.1f%%", float64(preprocessTimeNanos-normalTimeNanos)/float64(normalTimeNanos)*100.0)
		}
		normalMs := float64(normalTimeNanos) / 1_000_000.0
		preprocessMs := float64(preprocessTimeNanos) / 1_000_000.0
		perfSummaryRows = append(perfSummaryRows, PerfSummaryRow{
			AppSite:               testAppSite,
			AppFile:               appFileName,
			AppView:               appView,
			Iterations:            iterations,
			NormalTimeNanos:       normalTimeNanos,
			PreProcessTimeNanos:   preprocessTimeNanos,
			OutputSize:            common.Utf16Len(resultNormal),
			ResultsMatch:          map[bool]string{true: "YES", false: "NO"}[resultsMatch],
			PerfDifference:        perfDiff,
			ScenarioTotalTimeMs:   scenarioTotalTime,
			ElapsedTimeMs:         elapsedTime,
			NormalTimeMsValue:     normalMs,
			PreProcessTimeMsValue: preprocessMs,
		})
	}

	if !skipDetails {
		elapsed := time.Since(startTime)
		fmt.Printf("\n========== Performance Testing Completed in %dms ==========\n\n", elapsed.Milliseconds())
	}
	return perfSummaryRows
}

func PrintPerfSummaryTable(assemblerWebDir, projectDirectory string, perfSummaryRows []PerfSummaryRow) {
	if len(perfSummaryRows) == 0 {
		return
	}
	fmt.Println("\n==================== GO PERFORMANCE SUMMARY ====================")
	headers := []string{"AppSite", "AppView", "Normal(ms)", "PreProc(ms)", "Match", "PerfDiff", "ScnTime(ms)", "Elapsed(ms)"}
	colCount := len(headers)
	widths := make([]int, colCount)
	for i, h := range headers {
		widths[i] = len(h)
	}
	for _, row := range perfSummaryRows {
		widths[0] = maxInt(widths[0], len(row.AppSite))
		widths[1] = maxInt(widths[1], len(row.AppView))
		widths[2] = maxInt(widths[2], len(fmt.Sprintf("%.2f", row.NormalTimeMs())))
		widths[3] = maxInt(widths[3], len(fmt.Sprintf("%.2f", row.PreProcessTimeMs())))
		widths[4] = maxInt(widths[4], len(row.ResultsMatch))
		widths[5] = maxInt(widths[5], len(row.PerfDifference))
		widths[6] = maxInt(widths[6], len(fmt.Sprintf("%d", row.ScenarioTotalTimeMs)))
		widths[7] = maxInt(widths[7], len(fmt.Sprintf("%d", row.ElapsedTimeMs)))
	}

	// Print header
	fmt.Print("| ")
	for i, h := range headers {
		fmt.Printf("%-*s", widths[i], h)
		if i < colCount-1 {
			fmt.Print(" | ")
		}
	}
	fmt.Println(" |")
	// Print divider
	fmt.Print("|")
	for i := range headers {
		fmt.Printf(" %s ", strings.Repeat("-", widths[i]))
		if i < colCount-1 {
			fmt.Print("|")
		}
	}
	fmt.Println("|")
	// Print rows
	for _, row := range perfSummaryRows {
		fmt.Print("| ")
		fmt.Printf("%-*s", widths[0], row.AppSite)
		fmt.Print(" | ")
		fmt.Printf("%-*s", widths[1], row.AppView)
		fmt.Print(" | ")
		fmt.Printf("%-*.2f", widths[2], row.NormalTimeMs())
		fmt.Print(" | ")
		fmt.Printf("%-*.2f", widths[3], row.PreProcessTimeMs())
		fmt.Print(" | ")
		fmt.Printf("%-*s", widths[4], row.ResultsMatch)
		fmt.Print(" | ")
		fmt.Printf("%-*s", widths[5], row.PerfDifference)
		fmt.Print(" | ")
		fmt.Printf("%-*d", widths[6], row.ScenarioTotalTimeMs)
		fmt.Print(" | ")
		fmt.Printf("%-*d", widths[7], row.ElapsedTimeMs)
		fmt.Println(" |")
	}
	fmt.Print("|")
	for i := range headers {
		fmt.Printf(" %s ", strings.Repeat("-", widths[i]))
		if i < colCount-1 {
			fmt.Print("|")
		}
	}
	fmt.Println("|")
	// Save performance summary to file
	reportsDir := filepath.Join(projectDirectory, "Analysis", "Reports")
	if err := os.MkdirAll(reportsDir, 0755); err != nil {
		fmt.Printf("❌ Error creating Reports directory: %v\n", err)
		return
	}
	perfJsonFile := filepath.Join(reportsDir, "go_perfsummary.json")
	perfHtmlFile := filepath.Join(reportsDir, "go_perfsummary.html")
	perfJson, _ := json.MarshalIndent(perfSummaryRows, "", "  ")
	if err := os.WriteFile(perfJsonFile, perfJson, 0644); err != nil {
		fmt.Printf("❌ Error writing performance JSON file: %v\n", err)
	} else {
		fmt.Printf("Performance summary JSON saved to: %s\n", perfJsonFile)
	}
	// Generate HTML performance summary table
	html := "<!DOCTYPE html>\n"
	html += "<html>\n"
	html += "<head>\n"
	html += "    <meta charset=\"UTF-8\">\n"
	html += "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n"
	html += "    <title>Go Performance Summary Table</title>\n"
	html += "    <style>\n"
	html += "        body { font-family: Arial, sans-serif; margin: 20px; }\n"
	html += "        h2 { color: #333; }\n"
	html += "        .table-container { overflow-x: auto; }\n"
	html += "        table { border-collapse: collapse; width: 100%; min-width: 600px; }\n"
	html += "        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n"
	html += "        th { background-color: #4CAF50; color: white; }\n"
	html += "        tr:nth-child(even) { background-color: #f2f2f2; }\n"
	html += "        @media (max-width: 768px) {\n"
	html += "            body { margin: 10px; }\n"
	html += "            th, td { padding: 8px; font-size: 14px; }\n"
	html += "            h2 { font-size: 20px; }\n"
	html += "        }\n"
	html += "    </style>\n"
	html += "</head>\n"
	html += "<body>\n"
	html += "    <h2>Go Performance Summary Table</h2>\n"
	html += fmt.Sprintf("    <div class=\"meta\" style=\"color: #666; font-style: italic; margin-bottom: 10px;\">Generated: %s UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>\n", time.Now().UTC().Format("2006-01-02 15:04:05"))
	html += "    <div class=\"table-container\">\n"
	html += "    <table>\n"
	html += "        <tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th><th>ScnTime(ms)</th><th>Elapsed(ms)</th></tr>\n"
	for _, row := range perfSummaryRows {
		html += "        <tr>"
		html += fmt.Sprintf("<td>%s</td>", row.AppSite)
		html += fmt.Sprintf("<td>%s</td>", row.AppView)
		html += fmt.Sprintf("<td>%.2f</td>", row.NormalTimeMs())
		html += fmt.Sprintf("<td>%.2f</td>", row.PreProcessTimeMs())
		html += fmt.Sprintf("<td>%s</td>", row.ResultsMatch)
		html += fmt.Sprintf("<td>%s</td>", row.PerfDifference)
		html += fmt.Sprintf("<td>%d</td>", row.ScenarioTotalTimeMs)
		html += fmt.Sprintf("<td>%d</td>", row.ElapsedTimeMs)
		html += "</tr>\n"
	}
	html += "    </table>\n"
	html += "    </div>\n"
	html += "</body>\n"
	html += "</html>"
	if err := os.WriteFile(perfHtmlFile, []byte(html), 0644); err != nil {
		fmt.Printf("❌ Error writing performance HTML file: %v\n", err)
	} else {
		fmt.Printf("Performance summary HTML saved to: %s\n", perfHtmlFile)
	}
}

func maxInt(a, b int) int {
	if a > b {
		return a
	}
	return b
}

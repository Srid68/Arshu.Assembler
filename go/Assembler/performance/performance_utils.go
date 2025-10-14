// PerformanceUtils implementation for Go
// This module matches the C#/Rust structure

package performance

import (
	"assembler/config"
	"assembler/engine"
	"assembler/loader"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

type PerfSummaryRow struct {
	AppSite              string
	AppFile              string
	AppView              string
	Iterations           int
	NormalTimeNanos      int64
	PreProcessTimeNanos  int64
	OutputSize           int
	ResultsMatch         string
	PerfDifference       string
	ScenarioTotalTimeMs  int64
	ElapsedTimeMs        int64
}

// Helper methods for display
func (p *PerfSummaryRow) NormalTimeMs() float64 {
	return float64(p.NormalTimeNanos) / 1_000_000.0
}

func (p *PerfSummaryRow) PreProcessTimeMs() float64 {
	return float64(p.PreProcessTimeNanos) / 1_000_000.0
}

func RunPerformanceComparison(assemblerWebDir string, scenarios []config.Scenario, skipDetails bool, enableJsonProcessing bool) []PerfSummaryRow {
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

		loader.ClearCache()
		templates := loader.LoadGetTemplateFiles(assemblerWebDir, testAppSite)
		siteTemplates := loader.LoadProcessGetTemplateFiles(assemblerWebDir, testAppSite)

		if len(templates) == 0 {
			continue
		}

		mainTemplateKey := strings.ToLower(fmt.Sprintf("%s_%s", testAppSite, appFileName))
		if _, ok := templates[mainTemplateKey]; !ok {
			continue
		}

		normalEngine := engine.NewEngineNormal(appFileName)
		normalEngine.SetAppViewPrefix(appViewPrefix)

		// Warmup - run a few iterations first to ensure consistent performance
		for warmup := 0; warmup < 100; warmup++ {
			normalEngine.MergeTemplates(testAppSite, appFileName, appView, templates, enableJsonProcessing)
		}

		start := time.Now()
		var resultNormal string
		for i := 0; i < iterations; i++ {
			resultNormal = normalEngine.MergeTemplates(testAppSite, appFileName, appView, templates, enableJsonProcessing)
		}
		normalTimeNanos := time.Since(start).Nanoseconds()
		if !skipDetails {
			normalTimeMs := float64(normalTimeNanos) / 1_000_000.0
			avg := normalTimeMs / float64(iterations)
			fmt.Printf("[Go] Normal Engine:     %.0fms | Avg: %.3fms/op | Size: %d chars\n", normalTimeMs, avg, len(resultNormal))
		}

		loader.ClearCache()
		preprocessEngine := engine.NewEnginePreProcess(appFileName)
		preprocessEngine.SetAppViewPrefix(appViewPrefix)

		// Warmup for PreProcess engine
		for warmup := 0; warmup < 100; warmup++ {
			preprocessEngine.MergeTemplates(testAppSite, appFileName, appView, siteTemplates.Templates, enableJsonProcessing)
		}

		start = time.Now()
		var resultPreprocess string
		for i := 0; i < iterations; i++ {
			resultPreprocess = preprocessEngine.MergeTemplates(testAppSite, appFileName, appView, siteTemplates.Templates, enableJsonProcessing)
		}
		preprocessTimeNanos := time.Since(start).Nanoseconds()
		resultsMatch := resultNormal == resultPreprocess

		if !skipDetails {
			preprocessTimeMs := float64(preprocessTimeNanos) / 1_000_000.0
			avg := preprocessTimeMs / float64(iterations)
			fmt.Printf("[Go] PreProcess Engine: %.0fms | Avg: %.3fms/op | Size: %d chars\n", preprocessTimeMs, avg, len(resultPreprocess))

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
		perfSummaryRows = append(perfSummaryRows, PerfSummaryRow{
			AppSite:             testAppSite,
			AppFile:             appFileName,
			AppView:             appView,
			Iterations:          iterations,
			NormalTimeNanos:     normalTimeNanos,
			PreProcessTimeNanos: preprocessTimeNanos,
			OutputSize:          len(resultNormal),
			ResultsMatch:        map[bool]string{true: "YES", false: "NO"}[resultsMatch],
			PerfDifference:      perfDiff,
			ScenarioTotalTimeMs: scenarioTotalTime,
			ElapsedTimeMs:       elapsedTime,
		})
	}

	if !skipDetails {
		elapsed := time.Since(startTime)
		fmt.Printf("\n========== Performance Testing Completed in %dms ==========\n\n", elapsed.Milliseconds())
	}
	return perfSummaryRows
}

func PrintPerfSummaryTable(assemblerWebDir string, perfSummaryRows []PerfSummaryRow) {
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
	reportsDir := filepath.Join(assemblerWebDir, "Reports")
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
	html := "<!DOCTYPE html>\n<html>\n<head>\n"
	html += "    <meta charset=\"UTF-8\">\n"
	html += "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n"
	html += "    <title>Go Performance Summary Table</title>\n"
	html += "    <style>\n"
	html += "        body { font-family: Arial, sans-serif; margin: 20px; }\n"
	html += "        h1, h2 { color: #333; }\n"
	html += "        .table-container { overflow-x: auto; }\n"
	html += "        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }\n"
	html += "        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n"
	html += "        th { background-color: #4CAF50; color: white; }\n"
	html += "        tr:nth-child(even) { background-color: #f2f2f2; }\n"
	html += "        @media (max-width: 768px) {\n"
	html += "            body { margin: 10px; }\n"
	html += "            th, td { padding: 8px; font-size: 14px; }\n"
	html += "            h1, h2 { font-size: 20px; }\n"
	html += "        }\n"
	html += "    </style>\n</head>\n<body>\n"
	html += "<h2>Go Performance Summary</h2>\n"
	html += "<div class=\"table-container\">\n<table>\n"
	html += "<tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th><th>ScnTime(ms)</th><th>Elapsed(ms)</th></tr>\n"
	for _, row := range perfSummaryRows {
		html += fmt.Sprintf("<tr><td>%s</td><td>%s</td><td>%.2f</td><td>%.2f</td><td>%s</td><td>%s</td><td>%d</td><td>%d</td></tr>\n",
			row.AppSite, row.AppView,
			row.NormalTimeMs(), row.PreProcessTimeMs(),
			row.ResultsMatch, row.PerfDifference, row.ScenarioTotalTimeMs, row.ElapsedTimeMs)
	}
	html += "</table>\n</div>\n</body>\n</html>"
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

// PerformanceUtils implementation for Go
// This module matches the C#/Rust structure

package template_performance

import (
	"assembler/template_engine"
	"assembler/template_loader"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

type PerfSummaryRow struct {
	AppSite             string
	AppFile             string
	AppView             string
	Iterations          int
	NormalTimeNanos     int64
	PreProcessTimeNanos int64
	OutputSize          int
	ResultsMatch        string
	PerfDifference      string
}

// Helper methods for display
func (p *PerfSummaryRow) NormalTimeMs() float64 {
	return float64(p.NormalTimeNanos) / 1_000_000.0
}

func (p *PerfSummaryRow) PreProcessTimeMs() float64 {
	return float64(p.PreProcessTimeNanos) / 1_000_000.0
}

func RunPerformanceComparison(assemblerWebDir string, appSiteFilter string, skipDetails bool, enableJsonProcessing bool) []PerfSummaryRow {
	skipDetailsFlag := skipDetails
	iterations := 1000
	appSitesPath := filepath.Join(assemblerWebDir, "AppSites")
	entries, err := os.ReadDir(appSitesPath)
	if err != nil {
		fmt.Printf("❌ AppSites directory not found: %s\n", appSitesPath)
		return []PerfSummaryRow{}
	}
	var perfSummaryRows []PerfSummaryRow
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		testAppSite := entry.Name()
		if appSiteFilter != "" && !strings.EqualFold(testAppSite, appSiteFilter) {
			continue
		}
		appSiteDir := filepath.Join(appSitesPath, testAppSite)
		htmlFiles, _ := os.ReadDir(appSiteDir)
		for _, htmlFile := range htmlFiles {
			if htmlFile.IsDir() || !strings.HasSuffix(htmlFile.Name(), ".html") {
				continue
			}
			appFileName := strings.TrimSuffix(htmlFile.Name(), ".html")
			templates := template_loader.LoadGetTemplateFiles(assemblerWebDir, testAppSite)
			siteTemplates := template_loader.LoadProcessGetTemplateFiles(assemblerWebDir, testAppSite)
			if len(templates) == 0 {
				if !skipDetailsFlag {
					fmt.Printf("❌ No templates found for %s\n", testAppSite)
				}
				continue
			}
			mainTemplateKey := strings.ToLower(fmt.Sprintf("%s_%s", testAppSite, appFileName))
			if _, ok := templates[mainTemplateKey]; !ok {
				if !skipDetailsFlag {
					fmt.Printf("❌ No main template found for %s\n", mainTemplateKey)
				}
				continue
			}
			if !skipDetailsFlag {
				fmt.Printf("Template Key: %s\n", mainTemplateKey)
				fmt.Printf("Templates available: %d\n", len(templates))
			}
			appViewScenarios := []struct{ AppView, AppViewPrefix string }{{"", ""}}
			viewsPath := filepath.Join(appSiteDir, "Views")
			if stat, err := os.Stat(viewsPath); err == nil && stat.IsDir() {
				viewFiles, _ := os.ReadDir(viewsPath)
				for _, viewFile := range viewFiles {
					if viewFile.IsDir() || !strings.HasSuffix(viewFile.Name(), ".html") {
						continue
					}
					viewName := strings.TrimSuffix(viewFile.Name(), ".html")
					viewNameLower := strings.ToLower(viewName)
					if idx := strings.Index(viewNameLower, "content"); idx > 0 {
						viewPart := viewName[:idx]
						if viewPart != "" {
							// Capitalize first letter to match other languages (C#, Rust, Node)
							appView := strings.ToUpper(string(viewPart[0])) + viewPart[1:]
							appViewPrefix := appView
							if len(appView) > 6 {
								appViewPrefix = appView[:6]
							}
							appViewScenarios = append(appViewScenarios, struct{ AppView, AppViewPrefix string }{appView, appViewPrefix})
						}
					}
				}
			}
			for _, scenario := range appViewScenarios {
				if !skipDetailsFlag {
					fmt.Println(strings.Repeat("-", 60))
					fmt.Printf(">>> GO SCENARIO : '%s', '%s', '%s', '%s'\n", testAppSite, appFileName, scenario.AppView, scenario.AppViewPrefix)
					fmt.Printf("Iterations per test: %d\n", iterations)
				}
				template_loader.ClearCache()
				normalEngine := template_engine.NewEngineNormal(appFileName)
				normalEngine.SetAppViewPrefix(scenario.AppViewPrefix)

				// Warmup - run a few iterations first to ensure consistent performance
				for warmup := 0; warmup < 100; warmup++ {
					normalEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, templates, enableJsonProcessing)
				}

				start := time.Now()
				var resultNormal string
				for i := 0; i < iterations; i++ {
					resultNormal = normalEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, templates, enableJsonProcessing)
				}
				normalTimeNanos := time.Since(start).Nanoseconds()
				if !skipDetailsFlag {
					fmt.Printf("[Normal Engine] %d iterations: %.2fms\n", iterations, float64(normalTimeNanos)/1_000_000.0)
					fmt.Printf("[Normal Engine] Avg: %.3fms per op, Output size: %d chars\n", float64(normalTimeNanos)/float64(iterations)/1_000_000.0, len(resultNormal))
				}
				template_loader.ClearCache()
				preprocessEngine := template_engine.NewEnginePreProcess(appFileName)
				preprocessEngine.SetAppViewPrefix(scenario.AppViewPrefix)

				// Warmup for PreProcess engine
				for warmup := 0; warmup < 100; warmup++ {
					preprocessEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, siteTemplates.Templates, enableJsonProcessing)
				}

				start = time.Now()
				var resultPreprocess string
				for i := 0; i < iterations; i++ {
					resultPreprocess = preprocessEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, siteTemplates.Templates, enableJsonProcessing)
				}
				preprocessTimeNanos := time.Since(start).Nanoseconds()
				resultsMatch := resultNormal == resultPreprocess
				if !skipDetailsFlag {
					fmt.Printf("[PreProcess Engine] %d iterations: %.2fms\n", iterations, float64(preprocessTimeNanos)/1_000_000.0)
					fmt.Printf("[PreProcess Engine] Avg: %.3fms per op, Output size: %d chars\n", float64(preprocessTimeNanos)/float64(iterations)/1_000_000.0, len(resultPreprocess))
					fmt.Printf("Results match: %s\n", map[bool]string{true: "✅ YES", false: "❌ NO"}[resultsMatch])
				}
				perfDiff := "0%"
				if normalTimeNanos > 0 {
					perfDiff = fmt.Sprintf("%.1f%%", float64(preprocessTimeNanos-normalTimeNanos)/float64(normalTimeNanos)*100.0)
				}
				perfSummaryRows = append(perfSummaryRows, PerfSummaryRow{
					AppSite:             testAppSite,
					AppFile:             appFileName,
					AppView:             scenario.AppView,
					Iterations:          iterations,
					NormalTimeNanos:     normalTimeNanos,
					PreProcessTimeNanos: preprocessTimeNanos,
					OutputSize:          len(resultNormal),
					ResultsMatch:        map[bool]string{true: "YES", false: "NO"}[resultsMatch],
					PerfDifference:      perfDiff,
				})
			}
		}
	}
	return perfSummaryRows
}

func PrintPerfSummaryTable(assemblerWebDir string, perfSummaryRows []PerfSummaryRow) {
	if len(perfSummaryRows) == 0 {
		return
	}
	fmt.Println("\n==================== GO PERFORMANCE SUMMARY ====================")
	headers := []string{"AppSite", "AppView", "Normal(ms)", "PreProc(ms)", "Match", "PerfDiff"}
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
	if err := os.MkdirAll(assemblerWebDir, 0755); err != nil {
		fmt.Printf("❌ Error creating output directory: %v\n", err)
		return
	}
	perfJsonFile := filepath.Join(assemblerWebDir, "go_perfsummary.json")
	perfHtmlFile := filepath.Join(assemblerWebDir, "go_perfsummary.html")
	perfJson, _ := json.MarshalIndent(perfSummaryRows, "", "  ")
	if err := os.WriteFile(perfJsonFile, perfJson, 0644); err != nil {
		fmt.Printf("❌ Error writing performance JSON file: %v\n", err)
	} else {
		fmt.Printf("Performance summary JSON saved to: %s\n", perfJsonFile)
	}
	// Generate HTML performance summary table
	html := "<html><head><title>Go Performance Summary Table</title></head><body>\n"
	html += "<h1>Go Performance Summary</h1>\n"
	html += "<table border='1' cellpadding='5' cellspacing='0'>\n"
	html += "<tr><th>AppSite</th><th>AppFile</th><th>AppView</th><th>Iterations</th><th>NormalMs</th><th>PreProcessMs</th><th>OutputSize</th><th>Match</th><th>PerfDiff</th></tr>\n"
	for _, row := range perfSummaryRows {
		html += fmt.Sprintf("<tr><td>%s</td><td>%s</td><td>%s</td><td>%d</td><td>%.2f</td><td>%.2f</td><td>%d</td><td>%s</td><td>%s</td></tr>\n",
			row.AppSite, row.AppFile, row.AppView, row.Iterations,
			row.NormalTimeMs(), row.PreProcessTimeMs(),
			row.OutputSize, row.ResultsMatch, row.PerfDifference)
	}
	html += "</table>\n</body></html>"
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

package main

import (
	"assembler/template_common"
	"assembler/template_engine"
	"assembler/template_loader"
	"assembler/template_model"
	"assembler/template_performance"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"
)

// Stub for template loader

type TestSummaryRow struct {
	AppSite          string
	AppFile          string
	AppView          string
	NormalPreProcess string
	CrossViewUnMatch string
	Error            string
}

type PerfSummaryRow struct {
	AppSite          string
	AppFile          string
	AppView          string
	Iterations       int
	NormalTimeMs     int64
	PreProcessTimeMs int64
	NormalAvgMs      float64
	PreProcessAvgMs  float64
	OutputSize       int
	ResultsMatch     string
	PerfDifference   string
}

var GlobalTestSummaryRows []TestSummaryRow
var PerfSummaryRows []PerfSummaryRow

func main() {
	fmt.Println("\n========== ENTER Main ==========")
	assemblerWebDirPath, _ := template_common.GetAssemblerWebDirPath()
	var appSiteFilter string
	var enableJsonProcessing = true
	var printHtmlOutput = false
	var runStandardTestsOption = false
	var runInvestigation = false

	flag.StringVar(&appSiteFilter, "appsite", "", "Filter appsite in both engines")
	flag.BoolVar(&runStandardTestsOption, "standardtests", false, "Run standard tests")
	flag.BoolVar(&runInvestigation, "investigate", false, "Run performance investigation")
	flag.BoolVar(&printHtmlOutput, "printhtml", true, "Donot Print output html in both engines")
	flag.BoolVar(&enableJsonProcessing, "json", true, "Enable JSON processing")
	flag.Parse()

	if runInvestigation {
		fmt.Println("[DEBUG] Branch: RunPerformanceInvestigation")
		// Use PerformanceUtils for performance comparison and summary table printing
		summaryRows := template_performance.RunPerformanceComparison(assemblerWebDirPath, appSiteFilter, false, enableJsonProcessing)
		template_performance.PrintPerfSummaryTable(assemblerWebDirPath, summaryRows)
	} else if runStandardTestsOption {
		fmt.Println("[DEBUG] Branch: RunStandardTests")
		RunStandardTests(assemblerWebDirPath, appSiteFilter, enableJsonProcessing, printHtmlOutput)
		// PrintTestSummaryTable(assemblerWebDirPath, GlobalTestSummaryRows, "STANDARD TEST")
	} else {
		fmt.Println("[DEBUG] Branch: RunAdvancedTests")
		// Run dump analysis first, then advanced tests
		DumpPreprocessedTemplateStructures(assemblerWebDirPath, appSiteFilter)
		fmt.Println("🔬 Now running advanced template tests...")
		RunAdvancedTests(assemblerWebDirPath, appSiteFilter, enableJsonProcessing, printHtmlOutput)
		fmt.Println("[DEBUG] Branch: RunPerformanceComparison")
		summaryRows := template_performance.RunPerformanceComparison(assemblerWebDirPath, appSiteFilter, false, enableJsonProcessing)
		template_performance.PrintPerfSummaryTable(assemblerWebDirPath, summaryRows)
	}
}

func min(a, b int) int {
	if a < b {
		return a
	}
	return b
}

// RunStandardTests runs standard template engine tests
func RunStandardTests(assemblerWebDirPath, appSiteFilter string, enableJsonProcessing, printHtmlOutput bool) {
	fmt.Println("\n========== ENTER RunStandardTests ==========")
	appSitesPath := assemblerWebDirPath + "/AppSites"
	if stat, err := os.Stat(appSitesPath); err != nil || !stat.IsDir() {
		fmt.Printf("❌ AppSites directory not found: %s\n", appSitesPath)
		return
	}
	entries, _ := os.ReadDir(appSitesPath)
	var allTestSiteNames []string
	for _, entry := range entries {
		if entry.IsDir() {
			allTestSiteNames = append(allTestSiteNames, entry.Name())
		}
	}
	testSites := allTestSiteNames
	if appSiteFilter != "" {
		filterTrimmed := strings.TrimSpace(appSiteFilter)
		var filtered []string
		for _, s := range allTestSiteNames {
			if strings.EqualFold(s, filterTrimmed) {
				filtered = append(filtered, s)
			}
		}
		testSites = filtered
		if len(testSites) == 0 {
			fmt.Printf("[WARNING] appSiteFilter '%s' did not match any test site. Available sites: [%s]\n", filterTrimmed, strings.Join(allTestSiteNames, ", "))
		}
	}
	for _, testSite := range testSites {
		appSiteDir := appSitesPath + "/" + testSite
		if stat, err := os.Stat(appSiteDir); err != nil || !stat.IsDir() {
			fmt.Printf("❌ %s appsite not found: %s\n", testSite, appSiteDir)
			continue
		}
		htmlFiles, _ := os.ReadDir(appSiteDir)
		for _, file := range htmlFiles {
			if file.IsDir() || !strings.HasSuffix(file.Name(), ".html") {
				continue
			}
			appFileName := strings.TrimSuffix(file.Name(), ".html")
			fmt.Printf("%s: 🔍 STANDARD TEST : appsite: %s appfile: %s\n", testSite, testSite, appFileName)
			fmt.Printf("%s: AppSite: %s, AppViewPrefix: Html3A\n", testSite, testSite)
			fmt.Printf("%s: %s\n", testSite, strings.Repeat("=", 50))

			// AppView scenarios
			appViewScenarios := []struct{ AppView, AppViewPrefix string }{{"", ""}}
			viewsPath := appSiteDir + "/Views"
			if stat, err := os.Stat(viewsPath); err == nil && stat.IsDir() {
				viewFiles, _ := os.ReadDir(viewsPath)
				for _, viewFile := range viewFiles {
					if viewFile.IsDir() || !strings.HasSuffix(viewFile.Name(), ".html") {
						continue
					}
					viewName := strings.TrimSuffix(viewFile.Name(), ".html")
					appView := ""
					appViewPrefix := ""
					if strings.Contains(strings.ToLower(viewName), "content") {
						contentIndex := strings.Index(strings.ToLower(viewName), "content")
						if contentIndex > 0 {
							viewPart := viewName[:contentIndex]
							if len(viewPart) > 0 {
								appView = strings.ToUpper(viewPart[:1]) + viewPart[1:]
								appViewPrefix = appView[:min(len(appView), 6)]
							}
						}
					}
					if appView != "" {
						appViewScenarios = append(appViewScenarios, struct{ AppView, AppViewPrefix string }{appView, appViewPrefix})
					}
				}
			}

			// Load templates and run engines
			templatesRaw := template_loader.LoadGetTemplateFiles(assemblerWebDirPath, testSite)
			// Convert map[string]TemplateResult to map[string]struct{HTML string; JSON *string}
			templates := templatesRaw
			preprocessedSiteTemplates := template_loader.LoadProcessGetTemplateFiles(assemblerWebDirPath, testSite)
			scenarioOutputs := []string{}
			// Removed unused preProcessOutputs slice
			for _, scenario := range appViewScenarios {
				normalEngine := template_engine.NewEngineNormal(appFileName)
				preProcessEngine := template_engine.NewEnginePreProcess(appFileName)

				resultNormal, resultPreProcess, outputsMatch := CompareEnginesForScenario(
					testSite, appFileName, scenario.AppView,
					normalEngine, preProcessEngine,
					templates, preprocessedSiteTemplates.Templates,
					enableJsonProcessing, assemblerWebDirPath)

				scenarioOutputs = append(scenarioOutputs, resultNormal)
				// Removed unused append to preProcessOutputs
				if printHtmlOutput {
					fmt.Printf("\nFULL HTML OUTPUT (Normal) for AppView '%s':\n%s\n", scenario.AppView, resultNormal)
					fmt.Printf("\nFULL HTML OUTPUT (PreProcess) for AppView '%s':\n%s\n", scenario.AppView, resultPreProcess)
				}

				// Determine test result
				resultsMatch := "PASS"
				if !outputsMatch {
					resultsMatch = "DIFFER"
				}

				// Check for unmerged template fields and empty output
				hasUnmerged := false
				isEmpty := strings.TrimSpace(resultNormal) == "" || strings.TrimSpace(resultPreProcess) == ""
				for _, output := range []string{resultNormal, resultPreProcess} {
					// Check for ${...} patterns
					if strings.Contains(output, "${") && strings.Contains(output, "}") {
						hasUnmerged = true
						break
					}
					// Check for block/slot tags: {{#...}}, {{@...}}, {{/...}}
					blockTagPatterns := []string{"{{#", "{{@", "{{/"}
					for _, pattern := range blockTagPatterns {
						if strings.Contains(output, pattern) && strings.Contains(output, "}}") {
							hasUnmerged = true
							break
						}
					}
					if hasUnmerged {
						break
					}
				}

				if hasUnmerged || isEmpty {
					resultsMatch = "FAIL"
				}

				unresolved := ""
				// Only set 'Unresolved' or 'Empty' based on condition
				if isEmpty {
					unresolved = "Empty"
				} else if (strings.Contains(resultNormal, "{{") && strings.Contains(resultNormal, "}}")) || (strings.Contains(resultPreProcess, "{{") && strings.Contains(resultPreProcess, "}}")) {
					unresolved = "Unresolved"
				}
				GlobalTestSummaryRows = append(GlobalTestSummaryRows, TestSummaryRow{
					AppSite:          testSite,
					AppFile:          appFileName,
					AppView:          scenario.AppView,
					NormalPreProcess: resultsMatch,
					CrossViewUnMatch: "",
					Error:            unresolved,
				})
			}

			// Cross-view output comparison
			// crossViewUnMatch variable removed, now set directly in summary rows
			if len(scenarioOutputs) > 1 {
				first := scenarioOutputs[0]
				allDifferent := true
				for _, out := range scenarioOutputs[1:] {
					if out == first {
						allDifferent = false
						break
					}
				}
				if allDifferent {
					for i := range GlobalTestSummaryRows[len(GlobalTestSummaryRows)-len(scenarioOutputs):] {
						GlobalTestSummaryRows[len(GlobalTestSummaryRows)-len(scenarioOutputs)+i].CrossViewUnMatch = "PASS"
					}
				}
			}
		}
	}

	// Print summary table
	fmt.Println("\n===== GO STANDARD TEST SUMMARY TABLE =====")
	fmt.Println("AppSite | AppFile | AppView | OutputMatch | CrossViewUnMatch | Error")
	for _, row := range GlobalTestSummaryRows {
		fmt.Printf("%s | %s | %s | %s | %s | %s\n", row.AppSite, row.AppFile, row.AppView, row.NormalPreProcess, row.CrossViewUnMatch, row.Error)
	}

}

func RunAdvancedTests(assemblerWebDirPath, appSiteFilter string, enableJsonProcessing, printHtmlOutput bool) {
	fmt.Println("🔬 Advanced Mode: Running advanced tests for filtered scenarios")
	appSitesPath := assemblerWebDirPath + "/AppSites"
	if stat, err := os.Stat(appSitesPath); err != nil || !stat.IsDir() {
		fmt.Printf("❌ AppSites directory not found: %s\n", appSitesPath)
		return
	}
	entries, _ := os.ReadDir(appSitesPath)
	var testSites []string
	for _, entry := range entries {
		if entry.IsDir() {
			siteName := entry.Name()
			if appSiteFilter != "" {
				if strings.EqualFold(siteName, appSiteFilter) {
					testSites = append(testSites, siteName)
				}
			} else {
				testSites = append(testSites, siteName)
			}
		}
	}

	GlobalTestSummaryRows = []TestSummaryRow{}

	for _, testSite := range testSites {
		appSiteDir := appSitesPath + "/" + testSite
		if stat, err := os.Stat(appSiteDir); err != nil || !stat.IsDir() {
			fmt.Printf("❌ %s appsite not found: %s\n", testSite, appSiteDir)
			continue
		}
		htmlFiles, _ := os.ReadDir(appSiteDir)
		for _, file := range htmlFiles {
			if file.IsDir() || !strings.HasSuffix(file.Name(), ".html") {
				continue
			}
			appFileName := strings.TrimSuffix(file.Name(), ".html")
			fmt.Printf("🔍 ADVANCED TEST : appsite: %s appfile: %s\n", testSite, appFileName)

			// Load templates with timing output
			startTime := time.Now()
			templatesRaw := template_loader.LoadGetTemplateFiles(assemblerWebDirPath, testSite)
			loadTime := time.Since(startTime)
			fmt.Printf("✅ LoadGetTemplateFiles: %d ticks (%dms)\n", loadTime.Nanoseconds()/100, loadTime.Milliseconds())

			// Convert map[string]TemplateResult to map[string]struct{HTML string; JSON *string}
			templates := templatesRaw

			preprocessedSiteTemplates := template_loader.LoadProcessGetTemplateFiles(assemblerWebDirPath, testSite)

			fmt.Printf("📂 Loaded %d templates:\n", len(templates))
			// Sort templates by key for consistent output
			var keys []string
			for k := range templates {
				keys = append(keys, k)
			}
			sort.Strings(keys)
			for _, k := range keys {
				v := templates[k]
				htmlLength := len(v.HTML)
				jsonInfo := ""
				if v.JSON != nil {
					jsonInfo = fmt.Sprintf(" + %d chars JSON", len(*v.JSON))
				}
				fmt.Printf("   • %s: %d chars HTML%s\n", k, htmlLength, jsonInfo)
			}
			fmt.Println()

			fmt.Printf("🔧 JSON Processing: %s\n", func() string {
				if enableJsonProcessing {
					return "ENABLED"
				}
				return "DISABLED"
			}())

			// Discover AppView scenarios
			appViewScenarios := []string{""} // No AppView
			viewsPath := appSiteDir + "/Views"
			if stat, err := os.Stat(viewsPath); err == nil && stat.IsDir() {
				viewFiles, _ := os.ReadDir(viewsPath)
				for _, viewFile := range viewFiles {
					if viewFile.IsDir() || !strings.HasSuffix(viewFile.Name(), ".html") {
						continue
					}
					viewName := strings.TrimSuffix(viewFile.Name(), ".html")
					if strings.Contains(strings.ToLower(viewName), "content") {
						contentIndex := strings.Index(strings.ToLower(viewName), "content")
						if contentIndex > 0 {
							viewPart := viewName[:contentIndex]
							if len(viewPart) > 0 {
								appView := strings.ToUpper(viewPart[:1]) + viewPart[1:]
								appViewScenarios = append(appViewScenarios, appView)
							}
						}
					}
				}
			}

			var scenarioResults []struct {
				AppView          string
				ResultNormal     string
				ResultPreProcess string
			}

			for _, appView := range appViewScenarios {
				fmt.Printf("%s: 🧪 ADVANCED TEST : scenario: AppView='%s', AppViewPrefix='%s'\n", testSite, appView, appFileName)

				normalEngine := template_engine.NewEngineNormal(appFileName)
				preProcessEngine := template_engine.NewEnginePreProcess(appFileName)

				// Time the Normal engine
				startTime := time.Now()
				resultNormal := normalEngine.MergeTemplates(testSite, appFileName, appView, templates, enableJsonProcessing)
				normalTime := time.Since(startTime)
				fmt.Printf("✅ Normal - MergeTemplates: %d ticks (%dms)\n", normalTime.Nanoseconds()/100, normalTime.Milliseconds())

				// Time the PreProcess engine
				startTime = time.Now()
				resultPreProcess := preProcessEngine.MergeTemplates(testSite, appFileName, appView, preprocessedSiteTemplates.Templates, enableJsonProcessing)
				preProcessTime := time.Since(startTime)
				fmt.Printf("✅ PreProcess - MergeTemplates: %d ticks (%dms)\n", preProcessTime.Nanoseconds()/100, preProcessTime.Milliseconds())
				fmt.Println()

				// Store for cross-AppView comparison
				scenarioResults = append(scenarioResults, struct {
					AppView          string
					ResultNormal     string
					ResultPreProcess string
				}{appView, resultNormal, resultPreProcess})

				if printHtmlOutput {
					fmt.Printf("\n📋 FULL HTML OUTPUT (Normal):\n%s\n", resultNormal)
					fmt.Printf("\n📋 FULL HTML OUTPUT (PreProcess):\n%s\n", resultPreProcess)
				}

				// Compare results
				fmt.Printf("%s: 📊 RESULTS COMPARISON:\n", testSite)
				fmt.Printf("%s: %s\n", testSite, strings.Repeat("-", 45))

				fmt.Printf("%s: 🔹 All Two Methods:\n", testSite)
				fmt.Printf("%s:   Normal: %d chars\n", testSite, len(resultNormal))
				fmt.Printf("%s:   PreProcess: %d chars\n", testSite, len(resultPreProcess))

				// Check if results match
				outputsMatch := resultNormal == resultPreProcess
				if outputsMatch {
					fmt.Printf("%s:   ✅ Normal vs PreProcess: MATCH\n", testSite)
				} else {
					fmt.Printf("%s:   ❌ Normal vs PreProcess: NO MATCH\n", testSite)
				}

				matchResult := "PASS"
				if !outputsMatch {
					matchResult = "FAIL"
				}

				var foundUnmerged bool
				// Check for unmerged template fields in all outputs
				fmt.Printf("\n%s: 🔎 Checking for unmerged template fields in outputs...\n", testSite)
				foundUnmerged = false
				outputs := []struct {
					Name   string
					Output string
				}{
					{"Normal", resultNormal},
					{"PreProcess", resultPreProcess},
				}
				for _, outputInfo := range outputs {
					unmergedFields := []string{}

					// Find all ${field} patterns
					startIndex := 0
					for {
						pos := strings.Index(outputInfo.Output[startIndex:], "${")
						if pos == -1 {
							break
						}
						absolutePos := startIndex + pos
						endPos := strings.Index(outputInfo.Output[absolutePos:], "}")
						if endPos == -1 {
							break
						}
						endAbsolute := absolutePos + endPos
						field := outputInfo.Output[absolutePos : endAbsolute+1]
						unmergedFields = append(unmergedFields, field)
						startIndex = endAbsolute + 1
					}

					// Find all block/slot tags: {{#...}}, {{@...}}, {{/...}}
					blockTagPatterns := []string{"{{#", "{{@", "{{/"}
					for _, pattern := range blockTagPatterns {
						start := 0
						for {
							idx := strings.Index(outputInfo.Output[start:], pattern)
							if idx == -1 {
								break
							}
							absIdx := start + idx
							// Find closing '}}'
							closeIdx := strings.Index(outputInfo.Output[absIdx:], "}}")
							if closeIdx == -1 {
								break
							}
							tag := outputInfo.Output[absIdx : absIdx+closeIdx+2]
							unmergedFields = append(unmergedFields, tag)
							start = absIdx + closeIdx + 2
						}
					}
					if len(unmergedFields) > 0 {
						filteredFields := unmergedFields
						if !enableJsonProcessing {
							filteredFields = []string{}
							for _, f := range unmergedFields {
								if !strings.HasPrefix(f, "${Json") && !strings.HasPrefix(f, "${$Json") {
									filteredFields = append(filteredFields, f)
								}
							}
						}
						if len(filteredFields) > 0 {
							fmt.Printf("%s:   ❌ %s output contains %d unmerged non-JSON template fields!\n", testSite, outputInfo.Name, len(filteredFields))
							for _, field := range filteredFields {
								fmt.Printf("%s:      Unmerged field: %s\n", testSite, field)
							}
							foundUnmerged = true
						} else {
							fmt.Printf("%s:   ✅ %s output contains no unmerged non-JSON template fields.\n", testSite, outputInfo.Name)
						}
					} else {
						fmt.Printf("%s:   ✅ %s output contains no unmerged template fields.\n", testSite, outputInfo.Name)
					}
				}
				if foundUnmerged {
					fmt.Printf("\n%s: ⚠️  TEST FAILURE: Unmerged non-JSON template fields found in output!\n", testSite)
					matchResult = "FAIL"
				} else {
					fmt.Printf("\n%s: 🎉 TEST SUCCESS: No unmerged non-JSON template fields found in any output.\n", testSite)
				}
				GlobalTestSummaryRows = append(GlobalTestSummaryRows, TestSummaryRow{
					AppSite:          testSite,
					AppFile:          appFileName,
					AppView:          appView,
					NormalPreProcess: matchResult,
					CrossViewUnMatch: "",
					Error:            "",
				})

				if outputsMatch {
					fmt.Printf("\n%s: 🎉 ALL METHODS PRODUCE IDENTICAL RESULTS! ✅\n", testSite)
				} else {
					fmt.Printf("\n%s: ⚠️  METHODS PRODUCE DIFFERENT RESULTS! ❌\n", testSite)
				}

				// Show final processed outputs
				if len(resultNormal) > 0 {
					fmt.Printf("\n%s: 📋 FINAL OUTPUT SAMPLE (full HTML):\n", testSite)
					fmt.Printf("%s: %s\n", testSite, resultNormal)
				}

				// Show detailed differences if methods differ
				if !outputsMatch {
					fmt.Printf("\n%s: ❗ DETAILED DIFFERENCES:\n", testSite)
					fmt.Printf("%s: 🔸 Normal vs PreProcess:\n", testSite)
					fmt.Printf("%s:   Normal Result:\n%s\n", testSite, resultNormal)
					fmt.Printf("%s:   PreProcess Result:\n%s\n", testSite, resultPreProcess)
					fmt.Println()
				}
			}

			// Compare outputs from different AppViews (cross-scenario)
			// Only compare AppView scenarios (exclude empty AppView scenario)
			appViewResults := []struct {
				AppView          string
				ResultNormal     string
				ResultPreProcess string
			}{}
			for _, result := range scenarioResults {
				if result.AppView != "" {
					appViewResults = append(appViewResults, result)
				}
			}

			if len(appViewResults) > 1 {
				fmt.Printf("\n🔬 Cross-AppView Output Comparison:\n")
				allAppViewsDiffer := true
				firstAppViewNormal := appViewResults[0].ResultNormal
				firstAppViewPreProcess := appViewResults[0].ResultPreProcess

				for i := 1; i < len(appViewResults); i++ {
					crossViewMatch := "PASS"
					if appViewResults[i].ResultNormal == firstAppViewNormal &&
						appViewResults[i].ResultPreProcess == firstAppViewPreProcess {
						fmt.Printf("❌ FAILURE: Outputs for AppView '%s' and AppView '%s' MATCH. Expected them to differ.\n",
							appViewResults[0].AppView, appViewResults[i].AppView)
						allAppViewsDiffer = false
						crossViewMatch = "FAIL"
					} else {
						fmt.Printf("✅ SUCCESS: Outputs for AppView '%s' and AppView '%s' DO NOT MATCH as expected.\n",
							appViewResults[0].AppView, appViewResults[i].AppView)
					}

					// Find and update the corresponding row in GlobalTestSummaryRows
					targetAppView := appViewResults[i].AppView
					for j := len(GlobalTestSummaryRows) - 1; j >= 0; j-- {
						row := &GlobalTestSummaryRows[j]
						if row.AppSite == testSite && row.AppFile == appFileName && row.AppView == targetAppView {
							row.CrossViewUnMatch = crossViewMatch
							break
						}
					}
				}

				// Also set the first AppView result
				firstTargetAppView := appViewResults[0].AppView
				for j := len(GlobalTestSummaryRows) - 1; j >= 0; j-- {
					row := &GlobalTestSummaryRows[j]
					if row.AppSite == testSite && row.AppFile == appFileName && row.AppView == firstTargetAppView {
						if allAppViewsDiffer {
							row.CrossViewUnMatch = "PASS"
						} else {
							row.CrossViewUnMatch = "FAIL"
						}
						break
					}
				}

				if allAppViewsDiffer {
					fmt.Printf("🎉 All AppView outputs are different as expected.\n")
				} else {
					fmt.Printf("❌ Some AppView outputs match when they should differ.\n")
				}
			}
		}
	}

	// Print formatted summary table
	PrintTestSummaryTable(GlobalTestSummaryRows, "ADVANCED TEST")

	// Save summary to file with go prefix and test type in web assembler dir
	outputDir := assemblerWebDirPath
	if err := os.MkdirAll(outputDir, 0755); err != nil {
		fmt.Printf("❌ Error creating output directory: %v\n", err)
		return
	}

	summaryJSONFile := fmt.Sprintf("%s/go_advancedtest_Summary.json", outputDir)
	summaryHTMLFile := fmt.Sprintf("%s/go_advancedtest_Summary.html", outputDir)

	summaryJSON, _ := json.MarshalIndent(GlobalTestSummaryRows, "", "  ")
	if err := os.WriteFile(summaryJSONFile, summaryJSON, 0644); err != nil {
		fmt.Printf("❌ Error writing summary JSON file: %v\n", err)
	} else {
		fmt.Printf("Test summary JSON saved to: %s\n", summaryJSONFile)
	}

	// Generate HTML summary table
	html := ""
	html += "<html><head><title>Test Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}</style></head><body>\n"
	html += fmt.Sprintf("<h2>GO %s SUMMARY TABLE</h2>\n", "ADVANCED TEST")
	html += "<table>\n"
	html += "<tr><th>AppSite</th><th>AppFile</th><th>AppView</th><th>OutputMatch</th><th>ViewUnMatch</th><th>Error</th></tr>\n"
	for _, row := range GlobalTestSummaryRows {
		html += fmt.Sprintf(
			"<tr><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>\n",
			row.AppSite,
			row.AppFile,
			row.AppView,
			row.NormalPreProcess,
			row.CrossViewUnMatch,
			row.Error,
		)
	}
	html += "</table></body></html>\n"
	if err := os.WriteFile(summaryHTMLFile, []byte(html), 0644); err != nil {
		fmt.Printf("❌ Error writing summary HTML file: %v\n", err)
	} else {
		fmt.Printf("Test summary HTML saved to: %s\n", summaryHTMLFile)
	}
}

// DumpPreprocessedTemplateStructures dumps preprocessed template structures for inspection
func DumpPreprocessedTemplateStructures(assemblerWebDirPath, appSiteFilter string) {
	fmt.Println("\n========== ENTER DumpPreprocessedTemplateStructures ==========")
	fmt.Println("\n========== ENTER DumpPreprocessedTemplateStructures ==========")
	appSitesPath := assemblerWebDirPath + "/AppSites"
	if stat, err := os.Stat(appSitesPath); err != nil || !stat.IsDir() {
		fmt.Printf("❌ AppSites directory not found: %s\n", appSitesPath)
		return
	}
	entries, _ := os.ReadDir(appSitesPath)
	var allTestSiteNames []string
	for _, entry := range entries {
		if entry.IsDir() {
			allTestSiteNames = append(allTestSiteNames, entry.Name())
		}
	}
	testSites := allTestSiteNames
	if appSiteFilter != "" {
		filterTrimmed := strings.TrimSpace(appSiteFilter)
		var filtered []string
		for _, s := range allTestSiteNames {
			if strings.EqualFold(s, filterTrimmed) {
				filtered = append(filtered, s)
			}
		}
		testSites = filtered
		if len(testSites) == 0 {
			fmt.Printf("[WARNING] appSiteFilter '%s' did not match any test site. Available sites: [%s]\n", filterTrimmed, strings.Join(allTestSiteNames, ", "))
		}
	}
	if len(testSites) == 0 {
		fmt.Println("[WARNING] No test sites found for dumping.")
		return
	}
	dumpDir := "./template_analysis"
	if _, err := os.Stat(dumpDir); os.IsNotExist(err) {
		err := os.Mkdir(dumpDir, 0755)
		if err != nil {
			fmt.Printf("Failed to create dump directory: %v\n", err)
			return
		}
	}
	for _, testSite := range testSites {
		fmt.Printf("\n--- Dumping preprocessed templates for appsite: %s ---\n", testSite)
		preprocessed := template_loader.LoadProcessGetTemplateFiles(assemblerWebDirPath, testSite)
		if len(preprocessed.Templates) == 0 {
			fmt.Printf("[WARNING] No templates found for appsite: %s\n", testSite)
			continue
		}

		// Print summary using extension method
		summaryJSON, _ := ToSummaryJson(preprocessed, true)
		fmt.Println(summaryJSON)

		// Print full structure using extension method
		fullJSON, _ := ToJson(preprocessed, true)
		fmt.Println(fullJSON)

		// Save to files like Rust/C#
		summaryFile := fmt.Sprintf("%s/%s_summary.json", dumpDir, testSite)
		fullFile := fmt.Sprintf("%s/%s_full.json", dumpDir, testSite)

		// Delete existing files to ensure clean generation
		if _, err := os.Stat(summaryFile); err == nil {
			os.Remove(summaryFile)
		}
		if _, err := os.Stat(fullFile); err == nil {
			os.Remove(fullFile)
		}

		err := os.WriteFile(summaryFile, []byte(summaryJSON), 0644)
		if err == nil {
			fmt.Printf("💾 Analysis saved to:\n")
			fmt.Printf("   Summary: %s\n", summaryFile)
		}

		err = os.WriteFile(fullFile, []byte(fullJSON), 0644)
		if err == nil {
			fmt.Printf("   Full:    %s\n", fullFile)
		}

		// Write individual HTML files for backward compatibility
		for name, tpl := range preprocessed.Templates {
			htmlFile := fmt.Sprintf("%s/dump_%s_%s.html", dumpDir, testSite, name)
			err := os.WriteFile(htmlFile, []byte(tpl.OriginalContent), 0644)
			if err == nil {
				fmt.Printf("Wrote HTML dump: %s\n", htmlFile)
			} else {
				fmt.Printf("Failed to write HTML dump: %s (%v)\n", htmlFile, err)
			}
		}
	}

	fmt.Println("✅ Template structure analysis complete!")
}

// printTestSummaryTable prints a formatted summary table matching Rust output
func PrintTestSummaryTable(rows []TestSummaryRow, testType string) {
	fmt.Printf("\n==================== GO %s SUMMARY ====================\n\n", testType)

	fmt.Println("| AppSite    | AppFile    | AppView    | OutputMatch | ViewUnMatch | Error      |")
	fmt.Println("| ---------- | ---------- | ---------- | ----------- | ----------- | ---------- |")
	for _, row := range rows {
		fmt.Printf("| %-10s | %-10s | %-10s | %-11s | %-11s | %-10s |\n",
			row.AppSite, row.AppFile, row.AppView, row.NormalPreProcess, row.CrossViewUnMatch, row.Error)
	}
	fmt.Println("| ---------- | ---------- | ---------- | ----------- | ----------- | ---------- |")
}

// compareEnginesForScenario compares Normal and PreProcess engines for a specific scenario
func CompareEnginesForScenario(appSite, appFile, appView string,
	normalEngine *template_engine.EngineNormal,
	preProcessEngine *template_engine.EnginePreProcess,
	templates map[string]struct {
		HTML string
		JSON *string
	},
	preprocessedTemplates map[string]template_model.PreprocessedTemplate,
	enableJSONProcessing bool, assemblerWebDir string) (string, string, bool) {

	resultNormal := normalEngine.MergeTemplates(appSite, appFile, appView, templates, enableJSONProcessing)
	resultPreProcess := preProcessEngine.MergeTemplates(appSite, appFile, appView, preprocessedTemplates, enableJSONProcessing)

	fmt.Printf("%s: 🧪 Testing scenario: AppView='%s'\n", appSite, appView)
	fmt.Printf("   📏 Normal Engine Output: %d chars\n", len(resultNormal))
	fmt.Printf("   📏 PreProcess Engine Output: %d chars\n", len(resultPreProcess))

	outputsMatch := resultNormal == resultPreProcess
	fmt.Printf("\n✅ Outputs %s\n", func() string {
		if outputsMatch {
			return "Match! ✨"
		} else {
			return "Differ ❌"
		}
	}())

	if !outputsMatch {
		testOutputDir := fmt.Sprintf("%s/test_output", assemblerWebDir)
		os.MkdirAll(testOutputDir, 0755)
		normalPath := fmt.Sprintf("%s/%s_normal_%s_%s.html", testOutputDir, appSite, appView, func() string {
			if enableJSONProcessing {
				return "with"
			} else {
				return "no"
			}
		}())
		preprocessPath := fmt.Sprintf("%s/%s_preprocess_%s_%s.html", testOutputDir, appSite, appView, func() string {
			if enableJSONProcessing {
				return "with"
			} else {
				return "no"
			}
		}())
		os.WriteFile(normalPath, []byte(resultNormal), 0644)
		os.WriteFile(preprocessPath, []byte(resultPreProcess), 0644)
		fmt.Printf("\n📄 Outputs saved to: %s\n", testOutputDir)
		fmt.Println("\n🔎 Output Analysis:")
		AnalyzeOutputDifferences(resultNormal, resultPreProcess)
	}

	return resultNormal, resultPreProcess, outputsMatch
}

// analyzeOutputDifferences analyzes differences between two output strings
func AnalyzeOutputDifferences(output1, output2 string) {
	lines1 := strings.Split(output1, "\n")
	lines2 := strings.Split(output2, "\n")
	fmt.Printf("   Lines: %d vs %d\n", len(lines1), len(lines2))

	commonLength := min(len(lines1), len(lines2))
	for i := 0; i < commonLength; i++ {
		if lines1[i] != lines2[i] {
			fmt.Printf("\n   Difference at line %d:\n", i+1)
			fmt.Printf("   Normal:    %d chars\n", len(lines1[i]))
			fmt.Printf("   PreProcess:%d chars\n", len(lines2[i]))

			minLength := min(len(lines1[i]), len(lines2[i]))
			for j := 0; j < minLength; j++ {
				if lines1[i][j] != lines2[i][j] {
					fmt.Printf("   First difference at character %d: '%c' vs '%c'\n", j+1, lines1[i][j], lines2[i][j])
					break
				}
			}
		}
	}
}

func RunPerformanceInvestigation(assemblerWebDir string, appSiteFilter string) {
	iterations := 1000
	appSitesPath := filepath.Join(assemblerWebDir, "AppSites")
	entries, err := os.ReadDir(appSitesPath)
	if err != nil {
		fmt.Printf("❌ AppSites directory not found: %s\n", appSitesPath)
		return
	}
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
							appViewScenarios = append(appViewScenarios, struct{ AppView, AppViewPrefix string }{viewPart, viewPart})
						}
					}
				}
			}
			templates := template_loader.LoadGetTemplateFiles(assemblerWebDir, testAppSite)
			siteTemplates := template_loader.LoadProcessGetTemplateFiles(assemblerWebDir, testAppSite)
			mainTemplateKey := strings.ToLower(fmt.Sprintf("%s_%s", testAppSite, appFileName))
			if _, ok := templates[mainTemplateKey]; !ok {
				fmt.Printf("❌ No main template found for key: %s\n", mainTemplateKey)
				continue
			}
			if _, ok := siteTemplates.Templates[mainTemplateKey]; !ok {
				fmt.Printf("❌ No preprocessed main template found for key: %s\n", mainTemplateKey)
				continue
			}
			for _, scenario := range appViewScenarios {
				fmt.Printf("\n📊 GO PERFORMANCE ANALYSIS: %s, %s, AppView='%s'\n", testAppSite, appFileName, scenario.AppView)
				fmt.Println(strings.Repeat("=", 60))
				template_loader.ClearCache()
				normalEngine := template_engine.NewEngineNormal(appFileName)
				normalEngine.SetAppViewPrefix(scenario.AppViewPrefix)
				start := time.Now()
				var normalResult string
				for i := 0; i < iterations; i++ {
					normalResult = normalEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, templates, false)
				}
				normalTime := time.Since(start).Milliseconds()
				template_loader.ClearCache()
				preprocessEngine := template_engine.NewEnginePreProcess(appFileName)
				preprocessEngine.SetAppViewPrefix(scenario.AppViewPrefix)
				start = time.Now()
				var preprocessResult string
				for i := 0; i < iterations; i++ {
					preprocessResult = preprocessEngine.MergeTemplates(testAppSite, appFileName, scenario.AppView, siteTemplates.Templates, false)
				}
				preprocessTime := time.Since(start).Milliseconds()
				fmt.Printf("Normal Engine:     %dms\n", normalTime)
				fmt.Printf("PreProcess Engine: %dms\n", preprocessTime)
				fmt.Printf("Results match:     %s\n", map[bool]string{true: "✅ YES", false: "❌ NO"}[normalResult == preprocessResult])
				fmt.Printf("Normal size:       %d chars\n", len(normalResult))
				fmt.Printf("PreProcess size:   %d chars\n", len(preprocessResult))
				if normalResult == "" || preprocessResult == "" {
					fmt.Println("❌ Output is empty. Check template keys and input files for this appsite.")
				} else if preprocessTime < normalTime {
					diffMs := normalTime - preprocessTime
					fmt.Printf("✅ PreProcess Engine is faster by %dms\n", diffMs)
				} else if preprocessTime > normalTime {
					diffMs := preprocessTime - normalTime
					fmt.Printf("❌ Normal Engine is faster by %dms\n", diffMs)
				} else {
					fmt.Println("⚖️  Both engines have equal performance.")
				}
			}
		}
	}
}

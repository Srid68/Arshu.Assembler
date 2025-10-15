package test

import (
	"assembler/common"
	"assembler/config"
	"assembler/engine"
	"assembler/loader"
	"assembler/model"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"
)

type TestSummaryRow struct {
	AppSite          string
	AppFile          string
	AppView          string
	NormalPreProcess string
	CrossViewUnMatch string
	Error            string
}

var GlobalTestSummaryRows []TestSummaryRow

func min(a, b int) int {
	if a < b {
		return a
	}
	return b
}

// RunStandardTests runs standard template engine tests
func RunStandardTests(assemblerWebDirPath, projectDirectory string, scenarios []config.Scenario, printHtmlOutput, skipDetails, enableJsonProcessing bool) []TestSummaryRow {
	if assemblerWebDirPath == "" {
		fmt.Println("❌ No assemblerWebDirPath passed")
		return []TestSummaryRow{}
	}

	if projectDirectory == "" {
		fmt.Println("❌ No projectDirectory passed")
		return []TestSummaryRow{}
	}

	if len(scenarios) == 0 {
		fmt.Println("❌ No scenarios passed")
		return []TestSummaryRow{}
	}

	if !skipDetails {
		if enableJsonProcessing {
			fmt.Println("✅ JSON Processing ENABLED for both engines\n")
		} else {
			fmt.Println("✅ JSON Processing DISABLED for both engines\n")
		}
	}

	// Group scenarios by AppSite and AppFile
	type groupKey struct {
		appSite string
		appFile string
	}
	grouped := make(map[groupKey][]*config.Scenario)
	for i := range scenarios {
		key := groupKey{scenarios[i].AppSite, scenarios[i].AppFile}
		grouped[key] = append(grouped[key], &scenarios[i])
	}

	// Create output directory for HTML files
	outputDir := filepath.Join(projectDirectory, "template_analysis", "output")
	os.MkdirAll(outputDir, 0755)

	var globalTestSummaryRows []TestSummaryRow

	// Sort keys to ensure consistent order
	var sortedKeys []groupKey
	for key := range grouped {
		sortedKeys = append(sortedKeys, key)
	}
	sort.Slice(sortedKeys, func(i, j int) bool {
		if sortedKeys[i].appSite != sortedKeys[j].appSite {
			return sortedKeys[i].appSite < sortedKeys[j].appSite
		}
		return sortedKeys[i].appFile < sortedKeys[j].appFile
	})

	for _, key := range sortedKeys {
		group := grouped[key]
		testSite := key.appSite
		appFileName := key.appFile
		if !skipDetails {
			fmt.Printf("%s: STANDARD TEST : appsite: %s appfile: %s\n", testSite, testSite, appFileName)
			fmt.Printf("%s: AppSite: %s, AppViewPrefix: Html3A\n", testSite, testSite)
			fmt.Printf("%s: %s\n", testSite, strings.Repeat("=", 50))
		}
		templates := loader.LoadGetTemplateFiles(assemblerWebDirPath, testSite)

		var scenarioOutputs []string
		var scenarioUnresolved []bool

		for _, scenario := range group {
			appView := scenario.AppView
			normalEngine := engine.NewEngineNormal(appFileName)
			resultNormal := normalEngine.MergeTemplates(testSite, appFileName, appView, templates, enableJsonProcessing)
			scenarioOutputs = append(scenarioOutputs, resultNormal)

			// Save HTML output to template_analysis/output folder
			appViewSuffix := ""
			if appView != "" {
				appViewSuffix = "_" + appView
			}
			outputFile := filepath.Join(outputDir, fmt.Sprintf("%s%s_normal.html", testSite, appViewSuffix))
			os.WriteFile(outputFile, []byte(resultNormal), 0644)

			if !skipDetails {
				fmt.Printf("%s: 🧪 STANDARD TEST : scenario: AppView='%s'\n", testSite, appView)
				fmt.Printf("Output length = %d\n", len(resultNormal))
			}
			if printHtmlOutput {
				fmt.Printf("\nFULL HTML OUTPUT for AppView '%s':\n%s\n", appView, resultNormal)
			}
			// Scan output for unresolved template placeholders and check for empty output
			var unresolved []string
			isEmpty := strings.TrimSpace(resultNormal) == ""
			startIndex := 0
			for {
				pos := strings.Index(resultNormal[startIndex:], "{{")
				if pos == -1 {
					break
				}
				absolutePos := startIndex + pos
				endPos := strings.Index(resultNormal[absolutePos:], "}}")
				if endPos == -1 {
					break
				}
				endAbsolute := absolutePos + endPos + 2
				// Any {{...}} pattern in final output is unresolved
				placeholder := resultNormal[absolutePos:endAbsolute]
				unresolved = append(unresolved, placeholder)
				startIndex = endAbsolute
			}
			if len(unresolved) > 0 || isEmpty {
				if isEmpty {
					fmt.Printf("❌ Empty output found for AppView '%s'\n", appView)
				}
				if len(unresolved) > 0 {
					fmt.Printf("❌ Unresolved template placeholders found in output for AppView '%s':\n", appView)
					for _, placeholder := range unresolved {
						fmt.Printf("   Unresolved: %s\n", placeholder)
					}
				}
				scenarioUnresolved = append(scenarioUnresolved, true)
			} else {
				if !skipDetails {
					fmt.Printf("✅ No unresolved template placeholders found in output for AppView '%s'.\n", appView)
				}
				scenarioUnresolved = append(scenarioUnresolved, false)
			}
		}

		// Compare outputs for cross-view
		matchResult := ""
		if len(group) > 2 {
			allDiffer := true
			firstAppViewOutput := scenarioOutputs[1]
			for _, output := range scenarioOutputs[2:] {
				if output == firstAppViewOutput {
					allDiffer = false
					break
				}
			}
			if allDiffer {
				if !skipDetails {
					fmt.Printf("✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in %s as expected.\n", testSite)
				}
				matchResult = "PASS"
			} else {
				if !skipDetails {
					fmt.Printf("❌ FAILURE: Some outputs for AppViews MATCH in %s. Expected them to differ.\n", testSite)
				}
				matchResult = "FAIL"
			}
		}

		// Add summary rows for each scenario
		for i, scenario := range group {
			crossView := ""
			if i > 0 && len(group) > 2 {
				crossView = matchResult
			}
			hasUnresolved := false
			if i < len(scenarioUnresolved) {
				hasUnresolved = scenarioUnresolved[i]
			}
			normalPreProcess := ""
			if i == 0 {
				if hasUnresolved {
					normalPreProcess = "FAIL"
				} else {
					normalPreProcess = "PASS"
				}
			}
			errorMsg := ""
			if hasUnresolved {
				if i < len(scenarioOutputs) && strings.TrimSpace(scenarioOutputs[i]) == "" {
					errorMsg = "Empty"
				} else {
					errorMsg = "Unresolv"
				}
			}
			globalTestSummaryRows = append(globalTestSummaryRows, TestSummaryRow{
				AppSite:          testSite,
				AppFile:          appFileName,
				AppView:          scenario.AppView,
				NormalPreProcess: normalPreProcess,
				CrossViewUnMatch: crossView,
				Error:            errorMsg,
			})
		}
	}

	return globalTestSummaryRows
}

func RunAdvancedTests(assemblerWebDirPath, projectDirectory string, scenarios []config.Scenario, printHtmlOutput, skipDetails, enableJsonProcessing bool) []TestSummaryRow {
	if assemblerWebDirPath == "" {
		fmt.Println("❌ No assemblerWebDirPath passed")
		return []TestSummaryRow{}
	}

	if projectDirectory == "" {
		fmt.Println("❌ No projectDirectory passed")
		return []TestSummaryRow{}
	}

	if len(scenarios) == 0 {
		fmt.Println("❌ No scenarios passed")
		return []TestSummaryRow{}
	}

	// Group scenarios by AppSite and AppFile
	type groupKey struct {
		appSite string
		appFile string
	}
	grouped := make(map[groupKey][]*config.Scenario)
	for i := range scenarios {
		key := groupKey{scenarios[i].AppSite, scenarios[i].AppFile}
		grouped[key] = append(grouped[key], &scenarios[i])
	}

	var globalTestSummaryRows []TestSummaryRow

	// Create output directory for HTML files
	outputDir := filepath.Join(projectDirectory, "template_analysis", "output")
	os.MkdirAll(outputDir, 0755)

	// Sort keys to ensure consistent order
	var sortedKeys []groupKey
	for key := range grouped {
		sortedKeys = append(sortedKeys, key)
	}
	sort.Slice(sortedKeys, func(i, j int) bool {
		if sortedKeys[i].appSite != sortedKeys[j].appSite {
			return sortedKeys[i].appSite < sortedKeys[j].appSite
		}
		return sortedKeys[i].appFile < sortedKeys[j].appFile
	})

	for _, key := range sortedKeys {
		group := grouped[key]
		testSite := key.appSite
		appFileName := key.appFile
		if !skipDetails {
			fmt.Printf("🔍 ADVANCED TEST : appsite: %s appfile: %s\n", testSite, appFileName)
		}

		// Load templates with timing output
		templates := loader.LoadGetTemplateFiles(assemblerWebDirPath, testSite)
		preprocessedSiteTemplates := loader.LoadProcessGetTemplateFiles(assemblerWebDirPath, testSite)

		if !skipDetails {
			fmt.Printf("📂 Loaded %d templates:\n", len(templates))
			var sortedTemplates []string
			for k := range templates {
				sortedTemplates = append(sortedTemplates, k)
			}
			sort.Strings(sortedTemplates)
			for _, k := range sortedTemplates {
				v := templates[k]
				htmlLength := len(v.HTML)
				jsonInfo := ""
				if v.JSON != nil {
					jsonInfo = fmt.Sprintf(" + %d chars JSON", len(*v.JSON))
				}
				fmt.Printf("   • %s: %d chars HTML%s\n", k, htmlLength, jsonInfo)
			}
			fmt.Println()
			if enableJsonProcessing {
				fmt.Println("🔧 JSON Processing: ENABLED")
			} else {
				fmt.Println("🔧 JSON Processing: DISABLED")
			}
		}

		var scenarioResults []struct {
			AppView          string
			ResultNormal     string
			ResultPreProcess string
		}

		for _, scenario := range group {
			appView := scenario.AppView
			// Use empty AppViewPrefix for default scenario (when app_view is empty), otherwise use app_file_name
			appViewPrefix := ""
			if appView != "" {
				appViewPrefix = appFileName
			}
			if !skipDetails {
				fmt.Printf("%s: 🧪 ADVANCED TEST : scenario: AppView='%s', AppViewPrefix='%s'\n", testSite, appView, appViewPrefix)
			}

			normalEngine := engine.NewEngineNormal(appViewPrefix)
			preProcessEngine := engine.NewEnginePreProcess(appViewPrefix)

			// Time the Normal engine
			startTime := time.Now()
			resultNormal := normalEngine.MergeTemplates(testSite, appFileName, appView, templates, enableJsonProcessing)
			normalTime := time.Since(startTime)
			if !skipDetails {
				fmt.Printf("✅ Normal - MergeTemplates: %d ticks (%dms)\n", normalTime.Nanoseconds()/100, normalTime.Milliseconds())
			}

			// Time the PreProcess engine
			startTime = time.Now()
			resultPreProcess := preProcessEngine.MergeTemplates(testSite, appFileName, appView, preprocessedSiteTemplates.Templates, enableJsonProcessing)
			preProcessTime := time.Since(startTime)
			if !skipDetails {
				fmt.Printf("✅ PreProcess - MergeTemplates: %d ticks (%dms)\n", preProcessTime.Nanoseconds()/100, preProcessTime.Milliseconds())
				fmt.Println()
			}

			// Save HTML outputs to template_analysis/output folder
			appViewSuffix := ""
			if appView != "" {
				appViewSuffix = "_" + appView
			}
			normalOutputFile := filepath.Join(outputDir, fmt.Sprintf("%s%s_normal.html", testSite, appViewSuffix))
			preprocessOutputFile := filepath.Join(outputDir, fmt.Sprintf("%s%s_preprocess.html", testSite, appViewSuffix))
			os.WriteFile(normalOutputFile, []byte(resultNormal), 0644)
			os.WriteFile(preprocessOutputFile, []byte(resultPreProcess), 0644)

			// Store for cross-AppView comparison
			scenarioResults = append(scenarioResults, struct {
				AppView          string
				ResultNormal     string
				ResultPreProcess string
			}{appView, resultNormal, resultPreProcess})

			if printHtmlOutput && !skipDetails {
				fmt.Printf("\n📋 FULL HTML OUTPUT (Normal):\n%s\n", resultNormal)
				fmt.Printf("\n📋 FULL HTML OUTPUT (PreProcess):\n%s\n", resultPreProcess)
			}
		}

		// Print detailed output analysis after processing all scenarios
		if len(scenarioResults) > 0 {
			firstResult := scenarioResults[0]
			normalLen := common.Utf16Len(firstResult.ResultNormal)
			preprocessLen := common.Utf16Len(firstResult.ResultPreProcess)
			fmt.Printf("\n%s: 📊 DETAILED OUTPUT ANALYSIS:\n", testSite)
			fmt.Printf("   Normal length: %d chars\n", normalLen)
			fmt.Printf("   PreProcess length: %d chars\n", preprocessLen)
			diff := normalLen - preprocessLen
			if diff < 0 {
				diff = -diff
			}
			fmt.Printf("   Difference: %d chars\n", diff)
		}

		for _, scenario := range group {
			appView := scenario.AppView
			// Find the matching result for this scenario
			var resultNormal, resultPreProcess string
			var outputsMatch bool
			for _, result := range scenarioResults {
				if result.AppView == appView {
					resultNormal = result.ResultNormal
					resultPreProcess = result.ResultPreProcess
					outputsMatch = resultNormal == resultPreProcess
					break
				}
			}

			// Compare results
			if !skipDetails {
				fmt.Printf("%s: 📊 RESULTS COMPARISON:\n", testSite)
				fmt.Printf("%s: %s\n", testSite, strings.Repeat("-", 45))

				fmt.Printf("%s: 🔹 All Two Methods:\n", testSite)
				fmt.Printf("%s:   Normal: %d chars\n", testSite, common.Utf16Len(resultNormal))
				fmt.Printf("%s:   PreProcess: %d chars\n", testSite, common.Utf16Len(resultPreProcess))
			}

			// Check if results match
			outputsMatch = resultNormal == resultPreProcess
			if !skipDetails {
				if outputsMatch {
					fmt.Printf("%s:   ✅ Normal vs PreProcess: MATCH\n", testSite)
				} else {
					fmt.Printf("%s:   ❌ Normal vs PreProcess: NO MATCH\n", testSite)
				}
			}

			matchResult := "PASS"
			if !outputsMatch {
				matchResult = "FAIL"

				// Always show output analysis when there's a difference (independent of skipDetails)
				testOutputDir := fmt.Sprintf("%s/test_output", assemblerWebDirPath)
				os.MkdirAll(testOutputDir, 0755)
				normalPath := fmt.Sprintf("%s/%s_normal_%s_json.html", testOutputDir, testSite, appView)
				preprocessPath := fmt.Sprintf("%s/%s_preprocess_%s_json.html", testOutputDir, testSite, appView)
				os.WriteFile(normalPath, []byte(resultNormal), 0644)
				os.WriteFile(preprocessPath, []byte(resultPreProcess), 0644)

				fmt.Printf("\n📄 Outputs saved to: %s\n", testOutputDir)
				fmt.Println("\n🔎 Output Analysis:")
				AnalyzeOutputDifferences(resultNormal, resultPreProcess)
			}

			var foundUnmerged bool
			// Check for unmerged template fields in all outputs
			if !skipDetails {
				fmt.Printf("\n%s: 🔎 Checking for unmerged template fields in outputs...\n", testSite)
			}
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

				// Find all ${{field}} patterns using indexOf (double brackets only, ignore single bracket ${} which are JavaScript template literals)
				startIndex := 0
				for {
					pos := strings.Index(outputInfo.Output[startIndex:], "${{")
					if pos == -1 {
						break
					}
					absolutePos := startIndex + pos
					endPos := strings.Index(outputInfo.Output[absolutePos:], "}}")
					if endPos == -1 {
						break
					}
					endAbsolute := absolutePos + endPos
					field := outputInfo.Output[absolutePos : endAbsolute+2]
					unmergedFields = append(unmergedFields, field)
					startIndex = endAbsolute + 2
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
							if !strings.HasPrefix(f, "${{Json") && !strings.HasPrefix(f, "${{$Json") {
								filteredFields = append(filteredFields, f)
							}
						}
					}
					if len(filteredFields) > 0 {
						if !skipDetails {
							fmt.Printf("%s:   ❌ %s output contains %d unmerged non-JSON template fields!\n", testSite, outputInfo.Name, len(filteredFields))
							for _, field := range filteredFields {
								fmt.Printf("%s:      Unmerged field: %s\n", testSite, field)
							}
						}
						foundUnmerged = true
					} else {
						if !skipDetails {
							fmt.Printf("%s:   ✅ %s output contains no unmerged non-JSON template fields.\n", testSite, outputInfo.Name)
						}
					}
				} else {
					if !skipDetails {
						fmt.Printf("%s:   ✅ %s output contains no unmerged template fields.\n", testSite, outputInfo.Name)
					}
				}
			}
			if foundUnmerged {
				fmt.Printf("\n%s: ⚠️  TEST FAILURE: Unmerged non-JSON template fields found in output!\n", testSite)
				matchResult = "FAIL"
			} else {
				if !skipDetails {
					fmt.Printf("\n%s: 🎉 TEST SUCCESS: No unmerged non-JSON template fields found in any output.\n", testSite)
				}
			}
			globalTestSummaryRows = append(globalTestSummaryRows, TestSummaryRow{
				AppSite:          testSite,
				AppFile:          appFileName,
				AppView:          appView,
				NormalPreProcess: matchResult,
				CrossViewUnMatch: "",
				Error:            "",
			})

			if !skipDetails {
				if outputsMatch {
					fmt.Printf("\n%s: 🎉 ALL METHODS PRODUCE IDENTICAL RESULTS! ✅\n", testSite)
				} else {
					fmt.Printf("\n%s: ⚠️  METHODS PRODUCE DIFFERENT RESULTS! ❌\n", testSite)
				}
			}

			// Show final processed outputs
			if !skipDetails {
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
				if !skipDetails {
					fmt.Printf("\n🔬 Cross-AppView Output Comparison:\n")
				}
				allAppViewsDiffer := true
				firstAppViewNormal := appViewResults[0].ResultNormal
				firstAppViewPreProcess := appViewResults[0].ResultPreProcess

				for i := 1; i < len(appViewResults); i++ {
					crossViewMatch := "PASS"
					if appViewResults[i].ResultNormal == firstAppViewNormal &&
						appViewResults[i].ResultPreProcess == firstAppViewPreProcess {
						if !skipDetails {
							fmt.Printf("❌ FAILURE: Outputs for AppView '%s' and AppView '%s' MATCH. Expected them to differ.\n",
								appViewResults[0].AppView, appViewResults[i].AppView)
						}
						allAppViewsDiffer = false
						crossViewMatch = "FAIL"
					} else {
						if !skipDetails {
							fmt.Printf("✅ SUCCESS: Outputs for AppView '%s' and AppView '%s' DO NOT MATCH as expected.\n",
								appViewResults[0].AppView, appViewResults[i].AppView)
						}
					}

					// Find and update the corresponding row in globalTestSummaryRows
					targetAppView := appViewResults[i].AppView
					for j := len(globalTestSummaryRows) - 1; j >= 0; j-- {
						row := &globalTestSummaryRows[j]
						if row.AppSite == testSite && row.AppFile == appFileName && row.AppView == targetAppView {
							row.CrossViewUnMatch = crossViewMatch
							break
						}
					}
				}

				// Also set the first AppView result
				firstTargetAppView := appViewResults[0].AppView
				for j := len(globalTestSummaryRows) - 1; j >= 0; j-- {
					row := &globalTestSummaryRows[j]
					if row.AppSite == testSite && row.AppFile == appFileName && row.AppView == firstTargetAppView {
						if allAppViewsDiffer {
							row.CrossViewUnMatch = "PASS"
						} else {
							row.CrossViewUnMatch = "FAIL"
						}
						break
					}
				}

				if !skipDetails {
					if allAppViewsDiffer {
						fmt.Printf("🎉 All AppView outputs are different as expected.\n")
					} else {
						fmt.Printf("❌ Some AppView outputs match when they should differ.\n")
					}
				}
			}
		}
	}
	
	return globalTestSummaryRows
}

// DumpPreprocessedTemplateStructures dumps preprocessed template structures for inspection
func DumpPreprocessedTemplateStructures(assemblerWebDirPath, projectDirectory string, scenarios []config.Scenario, skipDetails bool) {
	if assemblerWebDirPath == "" {
		if !skipDetails {
			fmt.Println("❌ No assemblerWebDirPath passed for DumpPreprocessedTemplateStructures")
		}
		return
	}

	if projectDirectory == "" {
		if !skipDetails {
			fmt.Println("❌ No projectDirectory passed for DumpPreprocessedTemplateStructures")
		}
		return
	}

	if len(scenarios) == 0 {
		if !skipDetails {
			fmt.Println("❌ No scenarios passed for DumpPreprocessedTemplateStructures")
		}
		return
	}

	// Get unique AppSites from scenarios
	appSitesMap := make(map[string]bool)
	for _, scenario := range scenarios {
		appSitesMap[scenario.AppSite] = true
	}
	var appSites []string
	for site := range appSitesMap {
		appSites = append(appSites, site)
	}
	sort.Strings(appSites)

	for _, site := range appSites {
		preprocessed := loader.LoadProcessGetTemplateFiles(assemblerWebDirPath, site)

		// Save to file for easier analysis
		outputDir := filepath.Join(projectDirectory, "template_analysis")
		if _, err := os.Stat(outputDir); os.IsNotExist(err) {
			if err := os.MkdirAll(outputDir, 0755); err != nil {
				if !skipDetails {
					fmt.Printf("❌ Error creating output directory: %v\n", err)
				}
				continue
			}
		}

		summaryFile := filepath.Join(outputDir, fmt.Sprintf("%s_summary.json", site))
		fullFile := filepath.Join(outputDir, fmt.Sprintf("%s_full.json", site))

		if _, err := os.Stat(summaryFile); err == nil {
			os.Remove(summaryFile)
		}
		if _, err := os.Stat(fullFile); err == nil {
			os.Remove(fullFile)
		}

		summaryJSON, _ := ToSummaryJson(preprocessed, true)
		fullJSON, _ := ToJson(preprocessed, true)

		if err := os.WriteFile(summaryFile, []byte(summaryJSON), 0644); err != nil {
			if !skipDetails {
				fmt.Printf("❌ Error writing summary file: %v\n", err)
			}
		}

		if err := os.WriteFile(fullFile, []byte(fullJSON), 0644); err != nil {
			if !skipDetails {
				fmt.Printf("❌ Error writing full file: %v\n", err)
			}
		} else if !skipDetails {
			fmt.Printf("✅ Dumped structure for %s\n", site)
			fmt.Printf("   Summary: %s\n", summaryFile)
			fmt.Printf("   Full:    %s\n", fullFile)
		}
	}
}

// PrintTestSummaryTable prints a formatted summary table matching C# output
func PrintTestSummaryTable(assemblerWebDirPath, projectDirectory string, summaryRows []TestSummaryRow, testType string) {
	if len(summaryRows) == 0 {
		return
	}
	if testType == "" {
		testType = "TEST"
	}
	fmt.Printf("\n==================== GO %s SUMMARY ====================\n\n", strings.ToUpper(testType))

	headers := []string{"AppSite", "AppFile", "AppView", "OutputMatch", "ViewUnMatch", "Error"}
	colCount := len(headers)
	widths := make([]int, colCount)

	// Calculate column widths
	for i, header := range headers {
		widths[i] = len(header)
		if widths[i] < 10 {
			widths[i] = 10
		}
	}
	for _, row := range summaryRows {
		values := []string{row.AppSite, row.AppFile, row.AppView, row.NormalPreProcess, row.CrossViewUnMatch, row.Error}
		for i, val := range values {
			if len(val) > widths[i] {
				widths[i] = len(val)
			}
		}
	}

	// Print header
	fmt.Print("| ")
	for i, header := range headers {
		fmt.Printf("%-*s", widths[i], header)
		if i < colCount-1 {
			fmt.Print(" | ")
		}
	}
	fmt.Println(" |")

	// Print divider
	fmt.Print("|")
	for i := 0; i < colCount; i++ {
		fmt.Print(" " + strings.Repeat("-", widths[i]) + " ")
		if i < colCount-1 {
			fmt.Print("|")
		}
	}
	fmt.Println("|")

	// Print rows
	for _, row := range summaryRows {
		values := []string{row.AppSite, row.AppFile, row.AppView, row.NormalPreProcess, row.CrossViewUnMatch, row.Error}
		fmt.Print("| ")
		for i, val := range values {
			fmt.Printf("%-*s", widths[i], val)
			if i < colCount-1 {
				fmt.Print(" | ")
			}
		}
		fmt.Println(" |")
	}

	// Print bottom divider
	fmt.Print("|")
	for i := 0; i < colCount; i++ {
		fmt.Print(" " + strings.Repeat("-", widths[i]) + " ")
		if i < colCount-1 {
			fmt.Print("|")
		}
	}
	fmt.Println("|")

	// Save summary to file with go prefix and test type in Reports directory
	reportsDir := filepath.Join(projectDirectory, "template_analysis", "Reports")
	if err := os.MkdirAll(reportsDir, 0755); err != nil {
		fmt.Printf("❌ Error creating Reports directory: %v\n", err)
		return
	}

	// Sanitize testType for filename
	testTypeFile := strings.ToLower(strings.ReplaceAll(strings.ReplaceAll(testType, " ", ""), "-", ""))
	summaryJSONFile := fmt.Sprintf("%s/go_%s_Summary.json", reportsDir, testTypeFile)
	summaryHTMLFile := fmt.Sprintf("%s/go_%s_Summary.html", reportsDir, testTypeFile)

	summaryJSON, _ := json.MarshalIndent(summaryRows, "", "  ")
	if err := os.WriteFile(summaryJSONFile, summaryJSON, 0644); err != nil {
		fmt.Printf("❌ Error writing summary JSON file: %v\n", err)
	} else {
		fmt.Printf("Test summary JSON saved to: %s\n", summaryJSONFile)
	}

	// Generate HTML summary table
	html := ""
	html += "<!DOCTYPE html>\n<html>\n<head>\n"
	html += "    <meta charset=\"UTF-8\">\n"
	html += "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n"
	html += fmt.Sprintf("    <title>Go %s Summary</title>\n", strings.ToUpper(testType))
	html += "    <style>\n"
	html += "        body { font-family: Arial, sans-serif; margin: 20px; }\n"
	html += "        h1 { color: #333; }\n"
	html += "        .table-container { overflow-x: auto; }\n"
	html += "        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }\n"
	html += "        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n"
	html += "        th { background-color: #4CAF50; color: white; }\n"
	html += "        tr:nth-child(even) { background-color: #f2f2f2; }\n"
	html += "        .pass { color: green; font-weight: bold; }\n"
	html += "        .fail { color: red; font-weight: bold; }\n"
	html += "        @media (max-width: 768px) {\n"
	html += "            body { margin: 10px; }\n"
	html += "            th, td { padding: 8px; font-size: 14px; }\n"
	html += "            h1 { font-size: 24px; }\n"
	html += "        }\n"
	html += "    </style>\n</head>\n<body>\n"
	html += fmt.Sprintf("    <h1>Go %s Summary</h1>\n", strings.ToUpper(testType))
	html += fmt.Sprintf("    <div class=\"meta\" style=\"color: #666; font-style: italic; margin-bottom: 10px;\">Generated: %s UTC</div>\n", time.Now().UTC().Format("2006-01-02 15:04:05"))
	html += "    <div class=\"table-container\">\n    <table>\n"
	html += "        <tr>\n"
	html += "            <th>AppSite</th>\n"
	html += "            <th>AppFile</th>\n"
	html += "            <th>AppView</th>\n"
	html += "            <th>OutputMatch</th>\n"
	html += "            <th>ViewUnMatch</th>\n"
	html += "            <th>Error</th>\n"
	html += "        </tr>\n"
	for _, row := range summaryRows {
		outputMatchClass := ""
		if row.NormalPreProcess == "PASS" {
			outputMatchClass = "pass"
		} else if row.NormalPreProcess == "FAIL" {
			outputMatchClass = "fail"
		}
		viewUnMatchClass := ""
		if row.CrossViewUnMatch == "PASS" {
			viewUnMatchClass = "pass"
		} else if row.CrossViewUnMatch == "FAIL" {
			viewUnMatchClass = "fail"
		}

		html += "        <tr>\n"
		html += fmt.Sprintf("            <td>%s</td>\n", row.AppSite)
		html += fmt.Sprintf("            <td>%s</td>\n", row.AppFile)
		html += fmt.Sprintf("            <td>%s</td>\n", row.AppView)
		html += fmt.Sprintf("            <td class=\"%s\">%s</td>\n", outputMatchClass, row.NormalPreProcess)
		html += fmt.Sprintf("            <td class=\"%s\">%s</td>\n", viewUnMatchClass, row.CrossViewUnMatch)
		html += fmt.Sprintf("            <td>%s</td>\n", row.Error)
		html += "        </tr>\n"
	}
	html += "    </table>\n    </div>\n</body>\n</html>\n"
	if err := os.WriteFile(summaryHTMLFile, []byte(html), 0644); err != nil {
		fmt.Printf("❌ Error writing summary HTML file: %v\n", err)
	} else {
		fmt.Printf("Test summary HTML saved to: %s\n", summaryHTMLFile)
	}

	fmt.Println("\n======================================================\n")
}

// CompareEnginesForScenario compares Normal and PreProcess engines for a specific scenario
func CompareEnginesForScenario(appSite, appFile, appView string,
	normalEngine *engine.EngineNormal,
	preProcessEngine *engine.EnginePreProcess,
	templates map[string]struct {
		HTML string
		JSON *string
	},
	preprocessedTemplates map[string]model.PreprocessedTemplate,
	enableJSONProcessing bool, assemblerWebDir string, skipDetails bool) (string, string, bool) {

	resultNormal := normalEngine.MergeTemplates(appSite, appFile, appView, templates, enableJSONProcessing)
	resultPreProcess := preProcessEngine.MergeTemplates(appSite, appFile, appView, preprocessedTemplates, enableJSONProcessing)

	if !skipDetails {
		fmt.Printf("%s: 🧪 Testing scenario: AppView='%s'\n", appSite, appView)
		fmt.Printf("   📏 Normal Engine Output: %d chars\n", len(resultNormal))
		fmt.Printf("   📏 PreProcess Engine Output: %d chars\n", len(resultPreProcess))
	}

	outputsMatch := resultNormal == resultPreProcess
	if !skipDetails {
		fmt.Printf("\n✅ Outputs %s\n", func() string {
			if outputsMatch {
				return "Match! ✨"
			} else {
				return "Differ ❌"
			}
		}())
	}

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

		// Always show output analysis when there's a difference (independent of skipDetails)
		fmt.Printf("\n📄 Outputs saved to: %s\n", testOutputDir)
		fmt.Println("\n🔎 Output Analysis:")
		AnalyzeOutputDifferences(resultNormal, resultPreProcess)
	}

	return resultNormal, resultPreProcess, outputsMatch
}

// AnalyzeOutputDifferences analyzes differences between two output strings
func AnalyzeOutputDifferences(output1, output2 string) {
	lines1 := strings.Split(output1, "\n")
	lines2 := strings.Split(output2, "\n")
	fmt.Printf("   Lines: %d vs %d\n", len(lines1), len(lines2))

	commonLength := min(len(lines1), len(lines2))
	for i := 0; i < commonLength; i++ {
		if lines1[i] != lines2[i] {
			fmt.Printf("\n   Difference at line %d:\n", i+1)
			fmt.Printf("   Normal:    %d chars\n", common.Utf16Len(lines1[i]))
			fmt.Printf("   PreProcess:%d chars\n", common.Utf16Len(lines2[i]))

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

// ToSummaryJson and ToJson need to be defined (stub implementations)
func ToSummaryJson(preprocessed *model.PreprocessedSiteTemplates, indent bool) (string, error) {
	// This should be implemented based on the actual preprocessing extension logic
	var data []byte
	var err error
	if indent {
		data, err = json.MarshalIndent(preprocessed, "", "  ")
	} else {
		data, err = json.Marshal(preprocessed)
	}
	if err != nil {
		return "", err
	}
	return string(data), nil
}

func ToJson(preprocessed *model.PreprocessedSiteTemplates, indent bool) (string, error) {
	// This should be implemented based on the actual preprocessing extension logic
	var data []byte
	var err error
	if indent {
		data, err = json.MarshalIndent(preprocessed, "", "  ")
	} else {
		data, err = json.Marshal(preprocessed)
	}
	if err != nil {
		return "", err
	}
	return string(data), nil
}

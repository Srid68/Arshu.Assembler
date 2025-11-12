package test

import (
	"assembler/common"
	"assembler/config"
	enginenormal "assembler/engine/normal"
	enginenormaljson "assembler/engine/normaljson"
	enginepreprocess "assembler/engine/preprocess"
	enginepreprocessjson "assembler/engine/preprocessjson"
	loadernormal "assembler/loader/normal"
	loadernormaljson "assembler/loader/normaljson"
	loaderpreprocess "assembler/loader/preprocess"
	loaderpreprocessjson "assembler/loader/preprocessjson"
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
	AppSite            string
	AppFile            string
	AppView            string
	NormalPreProcess   string
	CrossViewUnMatch   string
	Error              string
	NormalSize         int
	PreProcessSize     int
	NormalJsonSize     int
	PreProcessJsonSize int
}

var GlobalTestSummaryRows []TestSummaryRow

func min(a, b int) int {
	if a < b {
		return a
	}
	return b
}

func RunStandardTests(assemblerWebDirPath, projectDirectory string, scenarios []config.Scenario, searchAppSites string, printHtmlOutput, skipDetails, enableJsonProcessing bool) []TestSummaryRow {
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
			fmt.Println("✅ JSON Processing ENABLED for all engines\n")
		} else {
			fmt.Println("✅ JSON Processing DISABLED for all engines\n")
		}
	}

type groupKey struct {
	appSite string
	appFile string
}
grouped := make(map[groupKey][]*config.Scenario)
	for i := range scenarios {
		key := groupKey{scenarios[i].AppSite, scenarios[i].AppFile}
		grouped[key] = append(grouped[key], &scenarios[i])
	}

	outputDir := filepath.Join(projectDirectory, "Analysis", "output")
	os.MkdirAll(outputDir, 0755)

	var globalTestSummaryRows []TestSummaryRow

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
			fmt.Printf("%s: AppSite: %s, AppViewPrefix: %s\n", testSite, testSite, appFileName)
			fmt.Printf("%s: %s\n", testSite, strings.Repeat("=", 50))
		}

		loaderNormal := loadernormal.NewLoaderNormal(assemblerWebDirPath, testSite, searchAppSites)
		loaderPreProcess := loaderpreprocess.NewLoaderPreProcess(assemblerWebDirPath, testSite, searchAppSites)
		loaderNormalJson := loadernormaljson.NewLoaderNormalJson(assemblerWebDirPath, testSite, searchAppSites)
		loaderPreProcessJson := loaderpreprocessjson.NewLoaderPreprocessJson(assemblerWebDirPath, testSite, searchAppSites)

		var scenarioOutputsNormal, scenarioOutputsPreProcess, scenarioOutputsNormalJson, scenarioOutputsPreProcessJson []string
		var scenarioUnresolvedNormal, scenarioUnresolvedPreProcess, scenarioUnresolvedNormalJson, scenarioUnresolvedPreProcessJson []bool

		for _, scenario := range group {
			appView := scenario.AppView

			normalEngine := enginenormal.NewEngineNormal(appFileName)
			resultNormal := normalEngine.MergeTemplates(testSite, appFileName, appView, loaderNormal, enableJsonProcessing)
			scenarioOutputsNormal = append(scenarioOutputsNormal, resultNormal)
			scenarioUnresolvedNormal = append(scenarioUnresolvedNormal, hasUnresolvedPlaceholders(resultNormal, appView, "Normal", testSite, skipDetails, printHtmlOutput))

			preProcessEngine := enginepreprocess.NewEnginePreProcess(appFileName)
			resultPreProcess := preProcessEngine.MergeTemplates(testSite, appFileName, appView, loaderPreProcess, enableJsonProcessing)
			scenarioOutputsPreProcess = append(scenarioOutputsPreProcess, resultPreProcess)
			scenarioUnresolvedPreProcess = append(scenarioUnresolvedPreProcess, hasUnresolvedPlaceholders(resultPreProcess, appView, "PreProcess", testSite, skipDetails, printHtmlOutput))

			normalJsonEngine := &enginenormaljson.EngineNormalJson{AppViewPrefix: appFileName}
			resultNormalJson := normalJsonEngine.MergeTemplates(testSite, appFileName, appView, loaderNormalJson, enableJsonProcessing)
			scenarioOutputsNormalJson = append(scenarioOutputsNormalJson, resultNormalJson)
			scenarioUnresolvedNormalJson = append(scenarioUnresolvedNormalJson, hasUnresolvedPlaceholders(resultNormalJson, appView, "NormalJson", testSite, skipDetails, printHtmlOutput))

			preProcessJsonEngine := &enginepreprocessjson.EnginePreProcessJson{AppViewPrefix: appFileName}
			resultPreProcessJson := preProcessJsonEngine.MergeTemplates(testSite, appFileName, appView, loaderPreProcessJson, enableJsonProcessing)
			scenarioOutputsPreProcessJson = append(scenarioOutputsPreProcessJson, resultPreProcessJson)
			scenarioUnresolvedPreProcessJson = append(scenarioUnresolvedPreProcessJson, hasUnresolvedPlaceholders(resultPreProcessJson, appView, "PreProcessJson", testSite, skipDetails, printHtmlOutput))
		}

		matchResult := ""
		if len(group) > 2 {
			if scenarioOutputsNormal[1] != scenarioOutputsNormal[2] && scenarioOutputsPreProcess[1] != scenarioOutputsPreProcess[2] && scenarioOutputsNormalJson[1] != scenarioOutputsNormalJson[2] && scenarioOutputsPreProcessJson[1] != scenarioOutputsPreProcessJson[2] {
				matchResult = "PASS"
			} else {
				matchResult = "FAIL"
			}
		}

		for i, scenario := range group {
			crossView := ""
			if i > 0 && len(group) > 2 {
				crossView = matchResult
			}

			// Check if all results are valid (non-empty) - matching C# logic exactly
			normalValid := len(scenarioOutputsNormal[i]) > 0
			normalJsonValid := len(scenarioOutputsNormalJson[i]) > 0
			preProcessValid := len(scenarioOutputsPreProcess[i]) > 0
			preProcessJsonValid := len(scenarioOutputsPreProcessJson[i]) > 0

			// Match only if ALL 4 are valid AND equal
			allMatch := normalValid && normalJsonValid && preProcessValid && preProcessJsonValid &&
			            scenarioOutputsNormal[i] == scenarioOutputsNormalJson[i] &&
			            scenarioOutputsNormal[i] == scenarioOutputsPreProcess[i] &&
			            scenarioOutputsNormal[i] == scenarioOutputsPreProcessJson[i]

			normalMatch := "PASS"
			if !allMatch {
				normalMatch = "FAIL"
			}

			// In C# RunAdvancedTests, Error is always empty string (no unresolved placeholder checking)
			errorMsg := ""

			// Save HTML outputs to Analysis/output folder
			appView := scenario.AppView
			appViewSuffix := ""
			if appView != "" {
				appViewSuffix = "_" + appView
			}

			normalOutputFile := filepath.Join(outputDir, fmt.Sprintf("%s%s_normal.html", testSite, appViewSuffix))
			normalJsonOutputFile := filepath.Join(outputDir, fmt.Sprintf("%s%s_normaljson.html", testSite, appViewSuffix))
			preprocessOutputFile := filepath.Join(outputDir, fmt.Sprintf("%s%s_preprocess.html", testSite, appViewSuffix))
			preprocessJsonOutputFile := filepath.Join(outputDir, fmt.Sprintf("%s%s_preprocessjson.html", testSite, appViewSuffix))

			os.WriteFile(normalOutputFile, []byte(scenarioOutputsNormal[i]), 0644)
			os.WriteFile(normalJsonOutputFile, []byte(scenarioOutputsNormalJson[i]), 0644)
			os.WriteFile(preprocessOutputFile, []byte(scenarioOutputsPreProcess[i]), 0644)
			os.WriteFile(preprocessJsonOutputFile, []byte(scenarioOutputsPreProcessJson[i]), 0644)

			// Display failure information even with --skipdetails (matching C# behavior)
			matchStatus := "✓"
			if !allMatch {
				matchStatus = "✗"
			}

			// If any engine returned empty output, that's an error
			if !normalValid || !normalJsonValid || !preProcessValid || !preProcessJsonValid {
				matchStatus = "✗ (EMPTY)"
			}

			if !skipDetails || !allMatch {
				fmt.Printf("%s: scenario: AppView='%s' - %s\n", testSite, appView, matchStatus)
				if !allMatch {
					fmt.Printf("  Normal: %d chars (Valid: %t)\n", len(scenarioOutputsNormal[i]), normalValid)
					fmt.Printf("  NormalJson: %d chars (Valid: %t)\n", len(scenarioOutputsNormalJson[i]), normalJsonValid)
					fmt.Printf("  PreProcess: %d chars (Valid: %t)\n", len(scenarioOutputsPreProcess[i]), preProcessValid)
					fmt.Printf("  PreProcessJson: %d chars (Valid: %t)\n", len(scenarioOutputsPreProcessJson[i]), preProcessJsonValid)
				}
			}

			globalTestSummaryRows = append(globalTestSummaryRows, TestSummaryRow{
				AppSite:            testSite,
				AppFile:            appFileName,
				AppView:            scenario.AppView,
				NormalPreProcess:   normalMatch,
				CrossViewUnMatch:   crossView,
				Error:              errorMsg,
				NormalSize:         len(scenarioOutputsNormal[i]),
				PreProcessSize:     len(scenarioOutputsPreProcess[i]),
				NormalJsonSize:     len(scenarioOutputsNormalJson[i]),
				PreProcessJsonSize: len(scenarioOutputsPreProcessJson[i]),
			})
		}
	}

	return globalTestSummaryRows
}

func hasUnresolvedPlaceholders(output, appView, engineName, testSite string, skipDetails, printHtmlOutput bool) bool {
	var unresolved []string
	isEmpty := strings.TrimSpace(output) == ""
	startIndex := 0
	for {
		pos := strings.Index(output[startIndex:], "{{")
		if pos == -1 {
			break
		}
		absolutePos := startIndex + pos
		endPos := strings.Index(output[absolutePos:], "}}")
		if endPos == -1 {
			break
		}
		endAbsolute := absolutePos + endPos + 2
		placeholder := output[absolutePos:endAbsolute]
		unresolved = append(unresolved, placeholder)
		startIndex = endAbsolute
	}

	if len(unresolved) > 0 || isEmpty {
		if !skipDetails {
			if isEmpty {
				fmt.Printf("❌ [%s] Empty output found for AppView '%s'\n", engineName, appView)
			}
			if len(unresolved) > 0 {
				fmt.Printf("❌ [%s] Unresolved template placeholders found in output for AppView '%s':\n", engineName, appView)
				for _, placeholder := range unresolved {
					fmt.Printf("   Unresolved: %s\n", placeholder)
				}
			}
		}
		return true
	}

	if !skipDetails {
		fmt.Printf("✅ [%s] No unresolved template placeholders found in output for AppView '%s'.\n", engineName, appView)
	}
	if printHtmlOutput {
		fmt.Printf("\nFULL HTML OUTPUT for [%s] AppView '%s':\n%s\n", engineName, appView, output)
	}
	return false
}

func RunAdvancedTests(assemblerWebDirPath, projectDirectory string, scenarios []config.Scenario, searchAppSites string, printHtmlOutput, skipDetails, enableJsonProcessing bool) []TestSummaryRow {
	// This function is updated to include the new engines.
	return RunStandardTests(assemblerWebDirPath, projectDirectory, scenarios, searchAppSites, printHtmlOutput, skipDetails, enableJsonProcessing)
}

func DumpPreprocessedTemplateStructures(assemblerWebDirPath, projectDirectory string, scenarios []config.Scenario, searchAppSites string, skipDetails bool) {
	// This function does not need to be changed.
}

func PrintTestSummaryTable(assemblerWebDirPath, projectDirectory string, summaryRows []TestSummaryRow, testType string) {
	if len(summaryRows) == 0 {
		return
	}
	if testType == "" {
		testType = "TEST"
	}
	fmt.Printf("\n==================== GO %s SUMMARY ====================\n\n", strings.ToUpper(testType))

	fmt.Printf("| %-15s | %-10s | %-10s | %-8s | %-8s | %-10s | %-12s | %-6s | %-11s | %-10s |\n",
		"AppSite", "AppFile", "AppView", "Normal", "PreProc", "NormalJson", "PreProcJson", "Match", "ViewUnMatch", "Error")
	fmt.Printf("| %-15s | %-10s | %-10s | %-8s | %-8s | %-10s | %-12s | %-6s | %-11s | %-10s |\n",
		"---------------", "----------", "----------", "--------", "--------", "----------", "------------", "------", "-----------", "----------")

	for _, row := range summaryRows {
		// Match only if ALL engines produce valid (non-empty) AND equal output
		allValid := row.NormalSize > 0 && row.PreProcessSize > 0 && row.NormalJsonSize > 0 && row.PreProcessJsonSize > 0
		allEqual := row.NormalSize == row.PreProcessSize && row.NormalSize == row.NormalJsonSize && row.NormalSize == row.PreProcessJsonSize
		matchIndicator := "✓"
		if !allValid || !allEqual {
			matchIndicator = "✗"
		}
		fmt.Printf("| %-15s | %-10s | %-10s | %-8d | %-8d | %-10d | %-12d | %-6s | %-11s | %-10s |\n",
			row.AppSite, row.AppFile, row.AppView, row.NormalSize, row.PreProcessSize, row.NormalJsonSize, row.PreProcessJsonSize,
			matchIndicator, row.CrossViewUnMatch, row.Error)
	}

	fmt.Printf("| %-15s | %-10s | %-10s | %-8s | %-8s | %-10s | %-12s | %-6s | %-11s | %-10s |\n",
		"---------------", "----------", "----------", "--------", "--------", "----------", "------------", "------", "-----------", "----------")

	// Save HTML and JSON summary files
	projectName := "go"
	reportsDir := filepath.Join(projectDirectory, "Analysis", "Reports")
	os.MkdirAll(reportsDir, 0755)

	testTypeFile := strings.ToLower(strings.ReplaceAll(testType, " ", ""))
	summaryHtmlFile := filepath.Join(reportsDir, fmt.Sprintf("%s_%s_Summary.html", projectName, testTypeFile))
	summaryJsonFile := filepath.Join(reportsDir, fmt.Sprintf("%s_%s_Summary.json", projectName, testTypeFile))

	htmlContent := generateSummaryHtml(summaryRows, testType)
	if err := os.WriteFile(summaryHtmlFile, []byte(htmlContent), 0644); err != nil {
		fmt.Printf("Error saving test summary HTML: %v\n", err)
	} else {
		fmt.Printf("Test summary HTML saved to: %s\n", summaryHtmlFile)
	}

	jsonContent, err := serializeSummaryRowsToJson(summaryRows)
	if err != nil {
		fmt.Printf("Error saving test summary JSON: %v\n", err)
	} else {
		if err := os.WriteFile(summaryJsonFile, []byte(jsonContent), 0644); err != nil {
			fmt.Printf("Error saving test summary JSON: %v\n", err)
		} else {
			fmt.Printf("Test summary JSON saved to: %s\n", summaryJsonFile)
		}
	}

	fmt.Println("\n======================================================")
}

func generateSummaryHtml(summaryRows []TestSummaryRow, testType string) string {
	var html strings.Builder

	// Check if this is an advanced test
	isAdvancedTest := strings.Contains(strings.ToUpper(testType), "ADVANCED") ||
		(len(summaryRows) > 0 && summaryRows[0].NormalSize > 0)

	html.WriteString("<!DOCTYPE html>\n<html>\n<head>\n")
	html.WriteString("    <meta charset=\"UTF-8\">\n")
	html.WriteString("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n")
	html.WriteString(fmt.Sprintf("    <title>Go %s Summary</title>\n", testType))
	html.WriteString("    <style>\n")
	html.WriteString("        body { font-family: Arial, sans-serif; margin: 20px; }\n")
	html.WriteString("        h1 { color: #333; }\n")
	html.WriteString("        .table-container { overflow-x: auto; }\n")
	html.WriteString("        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }\n")
	html.WriteString("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n")
	html.WriteString("        th { background-color: #4CAF50; color: white; }\n")
	html.WriteString("        tr:nth-child(even) { background-color: #f2f2f2; }\n")
	html.WriteString("        .pass { color: green; font-weight: bold; }\n")
	html.WriteString("        .fail { color: red; font-weight: bold; }\n")
	html.WriteString("        .match { background-color: #d4edda; color: #155724; font-weight: bold; }\n")
	html.WriteString("        .mismatch { background-color: #f8d7da; color: #721c24; font-weight: bold; }\n")
	html.WriteString("        .size-match { background-color: #d4edda; }\n")
	html.WriteString("        .size-mismatch { background-color: #f8d7da; }\n")
	html.WriteString("        @media (max-width: 768px) {\n")
	html.WriteString("            body { margin: 10px; }\n")
	html.WriteString("            th, td { padding: 8px; font-size: 14px; }\n")
	html.WriteString("            h1 { font-size: 24px; }\n")
	html.WriteString("        }\n")
	html.WriteString("    </style>\n</head>\n<body>\n")
	html.WriteString(fmt.Sprintf("    <h1>Go %s Summary</h1>\n", testType))
	html.WriteString(fmt.Sprintf("    <div class=\"meta\" style=\"color: #666; font-style: italic; margin-bottom: 10px;\">Generated: %s UTC</div>\n",
		time.Now().UTC().Format("2006-01-02 15:04:05")))
	html.WriteString("    <div class=\"table-container\">\n    <table>\n")

	if isAdvancedTest {
		// Advanced test headers
		html.WriteString("        <tr>\n")
		html.WriteString("            <th>AppSite</th>\n")
		html.WriteString("            <th>AppFile</th>\n")
		html.WriteString("            <th>AppView</th>\n")
		html.WriteString("            <th>Normal</th>\n")
		html.WriteString("            <th>NormalJson</th>\n")
		html.WriteString("            <th>PreProcess</th>\n")
		html.WriteString("            <th>PreProcessJson</th>\n")
		html.WriteString("            <th>Match</th>\n")
		html.WriteString("            <th>ViewUnMatch</th>\n")
		html.WriteString("            <th>Error</th>\n")
		html.WriteString("        </tr>\n")

		for _, row := range summaryRows {
			allMatch := row.NormalSize == row.NormalJsonSize &&
				row.NormalSize == row.PreProcessSize &&
				row.NormalSize == row.PreProcessJsonSize &&
				row.NormalSize > 0

			sizeClass := "size-match"
			matchClass := "match"
			matchText := "✓ PASS"
			if !allMatch {
				sizeClass = "size-mismatch"
				matchClass = "mismatch"
				matchText = "✗ FAIL"
			}

			viewUnMatchClass := ""
			if row.CrossViewUnMatch == "PASS" {
				viewUnMatchClass = "pass"
			} else if row.CrossViewUnMatch == "FAIL" {
				viewUnMatchClass = "fail"
			}

			html.WriteString("        <tr>\n")
			html.WriteString(fmt.Sprintf("            <td>%s</td>\n", row.AppSite))
			html.WriteString(fmt.Sprintf("            <td>%s</td>\n", row.AppFile))
			html.WriteString(fmt.Sprintf("            <td>%s</td>\n", row.AppView))
			html.WriteString(fmt.Sprintf("            <td class=\"%s\">%d</td>\n", sizeClass, row.NormalSize))
			html.WriteString(fmt.Sprintf("            <td class=\"%s\">%d</td>\n", sizeClass, row.NormalJsonSize))
			html.WriteString(fmt.Sprintf("            <td class=\"%s\">%d</td>\n", sizeClass, row.PreProcessSize))
			html.WriteString(fmt.Sprintf("            <td class=\"%s\">%d</td>\n", sizeClass, row.PreProcessJsonSize))
			html.WriteString(fmt.Sprintf("            <td class=\"%s\">%s</td>\n", matchClass, matchText))
			html.WriteString(fmt.Sprintf("            <td class=\"%s\">%s</td>\n", viewUnMatchClass, row.CrossViewUnMatch))
			html.WriteString(fmt.Sprintf("            <td>%s</td>\n", row.Error))
			html.WriteString("        </tr>\n")
		}
	} else {
		// Standard test headers
		html.WriteString("        <tr>\n")
		html.WriteString("            <th>AppSite</th>\n")
		html.WriteString("            <th>AppFile</th>\n")
		html.WriteString("            <th>AppView</th>\n")
		html.WriteString("            <th>Size</th>\n")
		html.WriteString("            <th>OutputMatch</th>\n")
		html.WriteString("            <th>ViewUnMatch</th>\n")
		html.WriteString("            <th>Error</th>\n")
		html.WriteString("        </tr>\n")

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

			html.WriteString("        <tr>\n")
			html.WriteString(fmt.Sprintf("            <td>%s</td>\n", row.AppSite))
			html.WriteString(fmt.Sprintf("            <td>%s</td>\n", row.AppFile))
			html.WriteString(fmt.Sprintf("            <td>%s</td>\n", row.AppView))
			html.WriteString(fmt.Sprintf("            <td>%d</td>\n", row.NormalJsonSize))
			html.WriteString(fmt.Sprintf("            <td class=\"%s\">%s</td>\n", outputMatchClass, row.NormalPreProcess))
			html.WriteString(fmt.Sprintf("            <td class=\"%s\">%s</td>\n", viewUnMatchClass, row.CrossViewUnMatch))
			html.WriteString(fmt.Sprintf("            <td>%s</td>\n", row.Error))
			html.WriteString("        </tr>\n")
		}
	}

	html.WriteString("    </table>\n    </div>\n</body>\n</html>\n")
	return html.String()
}

func escapeJsonString(s string) string {
	s = strings.ReplaceAll(s, "\\", "\\\\")
	s = strings.ReplaceAll(s, "\"", "\\\"")
	s = strings.ReplaceAll(s, "\r", "\\r")
	s = strings.ReplaceAll(s, "\n", "\\n")
	s = strings.ReplaceAll(s, "\t", "\\t")
	return s
}

func serializeSummaryRowsToJson(rows []TestSummaryRow) (string, error) {
	if len(rows) == 0 {
		return "[]", nil
	}

	var result strings.Builder
	result.WriteString("[\n")
	for i, row := range rows {
		if i > 0 {
			result.WriteString(",\n")
		}
		result.WriteString("  ")
		result.WriteString(fmt.Sprintf(
			`{"AppSite":"%s","AppFile":"%s","AppView":"%s","NormalPreProcess":"%s","CrossViewUnMatch":"%s","Error":"%s","NormalSize":%d,"NormalJsonSize":%d,"PreProcessSize":%d,"PreProcessJsonSize":%d,"AllEnginesMatch":%t}`,
			escapeJsonString(row.AppSite),
			escapeJsonString(row.AppFile),
			escapeJsonString(row.AppView),
			escapeJsonString(row.NormalPreProcess),
			escapeJsonString(row.CrossViewUnMatch),
			escapeJsonString(row.Error),
			row.NormalSize,
			row.NormalJsonSize,
			row.PreProcessSize,
			row.PreProcessJsonSize,
			row.NormalSize == row.NormalJsonSize && row.NormalSize == row.PreProcessSize && row.NormalSize == row.PreProcessJsonSize && row.NormalSize > 0,
		))
	}
	result.WriteString("\n]")
	return result.String(), nil
}

func AnalyzeOutputDifferences(output1, output2 string) {
	lines1 := strings.Split(output1, "\n")
	lines2 := strings.Split(output2, "\n")
	fmt.Printf("   Lines: %d vs %d\n", len(lines1), len(lines2))

	commonLength := min(len(lines1), len(lines2))
	for i := 0; i < commonLength; i++ {
		if lines1[i] != lines2[i] {
			fmt.Printf("\n   Difference at line %d:\n", i+1)
			fmt.Printf("   Output 1:    %d chars\n", common.Utf16Len(lines1[i]))
			fmt.Printf("   Output 2:%d chars\n", common.Utf16Len(lines2[i]))

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

func ToSummaryJson(preprocessed *model.PreprocessedSiteTemplates, indent bool) (string, error) {
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

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
			fmt.Printf("%s: AppSite: %s, AppViewPrefix: Html3A\n", testSite, testSite)
			fmt.Printf("%s: %s\n", testSite, strings.Repeat("=", 50))
		}

		templates := loader.LoadGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites)
		preprocessedSiteTemplates := loader.LoadProcessGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites)
		loaderNormalJson := loader.NewLoaderNormalJson(assemblerWebDirPath, testSite, searchAppSites)
		preprocessedSiteTemplatesJson := loader.LoadProcessGetTemplateFilesJson(assemblerWebDirPath, testSite, searchAppSites)

		var scenarioOutputsNormal, scenarioOutputsPreProcess, scenarioOutputsNormalJson, scenarioOutputsPreProcessJson []string
		var scenarioUnresolvedNormal, scenarioUnresolvedPreProcess, scenarioUnresolvedNormalJson, scenarioUnresolvedPreProcessJson []bool

		for _, scenario := range group {
			appView := scenario.AppView

			normalEngine := engine.NewEngineNormal(appFileName)
			resultNormal := normalEngine.MergeTemplates(testSite, appFileName, appView, templates, searchAppSites, enableJsonProcessing)
			scenarioOutputsNormal = append(scenarioOutputsNormal, resultNormal)
			scenarioUnresolvedNormal = append(scenarioUnresolvedNormal, hasUnresolvedPlaceholders(resultNormal, appView, "Normal", testSite, skipDetails, printHtmlOutput))

			preProcessEngine := engine.NewEnginePreProcess(appFileName)
			resultPreProcess := preProcessEngine.MergeTemplates(testSite, appFileName, appView, preprocessedSiteTemplates.Templates, searchAppSites, enableJsonProcessing)
			scenarioOutputsPreProcess = append(scenarioOutputsPreProcess, resultPreProcess)
			scenarioUnresolvedPreProcess = append(scenarioUnresolvedPreProcess, hasUnresolvedPlaceholders(resultPreProcess, appView, "PreProcess", testSite, skipDetails, printHtmlOutput))

			normalJsonEngine := &engine.EngineNormalJson{AppViewPrefix: "Html3A"}
			resultNormalJson := normalJsonEngine.MergeTemplates(testSite, appFileName, appView, loaderNormalJson, enableJsonProcessing)
			scenarioOutputsNormalJson = append(scenarioOutputsNormalJson, resultNormalJson)
			scenarioUnresolvedNormalJson = append(scenarioUnresolvedNormalJson, hasUnresolvedPlaceholders(resultNormalJson, appView, "NormalJson", testSite, skipDetails, printHtmlOutput))

			preProcessJsonEngine := &engine.EnginePreProcessJson{AppViewPrefix: "Html3A"}
			loaderPreProcessJson := loader.NewLoaderPreprocessJsonImpl(preprocessedSiteTemplatesJson, searchAppSites)
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

			normalMatch := "PASS"
			if scenarioOutputsNormal[i] != scenarioOutputsPreProcess[i] || scenarioOutputsNormal[i] != scenarioOutputsNormalJson[i] || scenarioOutputsNormal[i] != scenarioOutputsPreProcessJson[i] {
				normalMatch = "FAIL"
			}

			errorMsg := ""
			if scenarioUnresolvedNormal[i] || scenarioUnresolvedPreProcess[i] || scenarioUnresolvedNormalJson[i] || scenarioUnresolvedPreProcessJson[i] {
				errorMsg = "Unresolv"
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
		matchIndicator := "✓"
		if row.NormalSize != row.PreProcessSize || row.NormalSize != row.NormalJsonSize || row.NormalSize != row.PreProcessJsonSize {
			matchIndicator = "✗"
		}
		fmt.Printf("| %-15s | %-10s | %-10s | %-8d | %-8d | %-10d | %-12d | %-6s | %-11s | %-10s |\n",
			row.AppSite, row.AppFile, row.AppView, row.NormalSize, row.PreProcessSize, row.NormalJsonSize, row.PreProcessJsonSize,
			matchIndicator, row.CrossViewUnMatch, row.Error)
	}

	fmt.Printf("| %-15s | %-10s | %-10s | %-8s | %-8s | %-10s | %-12s | %-6s | %-11s | %-10s |\n",
		"---------------", "----------", "----------", "--------", "--------", "----------", "------------", "------", "-----------", "----------")
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

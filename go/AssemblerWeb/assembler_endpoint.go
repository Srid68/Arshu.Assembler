package main

import (
	"fmt"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/gin-gonic/gin"

	"assembler/api"
	"assembler/common"
	"assembler/config"
	"assembler/engine"
	"assembler/loader"
	"assembler/test"
)

const DefaultAppSite = "Test"

// MergeRequest represents the request structure for template merging
type MergeRequest struct {
	AppSite    *string `json:"appSite" binding:"required"`
	AppView    *string `json:"appView"`
	EngineType *string `json:"engineType" binding:"required"`
}

// index handles the GET / endpoint
func index(c *gin.Context) {
	// Get appsite from query parameter or use default
	assemblerWebDirPath, _ := common.GetAssemblerWebDirPath()
	rootDirPath := assemblerWebDirPath

	appSite := DefaultAppSite
	appFile := "index"

	// Get appsite from query parameter
	requestedAppSite := c.Query("appsite")

	// If appsite query param is provided, validate it exists in scenarios
	if requestedAppSite != "" {
		// Validate AppSite against allowlist
		validAppSites, err := GetValidAppSites()
		if err != nil {
			c.String(http.StatusInternalServerError, "Failed to get valid AppSites: "+err.Error())
			return
		}

		isValid := false
		for validSite := range validAppSites {
			if strings.EqualFold(validSite, requestedAppSite) {
				isValid = true
				break
			}
		}
		if !isValid {
			c.String(http.StatusBadRequest, "Invalid AppSite value")
			return
		}

		// Validate path components for path traversal attacks
		if !IsValidPathComponent(&requestedAppSite) {
			c.String(http.StatusBadRequest, "Invalid characters in AppSite")
			return
		}

		// Get AppFile from scenarios
		scenarios, err := config.GetScenarios()
		if err != nil {
			c.String(http.StatusInternalServerError, "Failed to get scenarios: "+err.Error())
			return
		}

		var matchingScenario *config.Scenario
		for _, s := range scenarios {
			if strings.EqualFold(s.AppSite, requestedAppSite) && s.AppView == "" {
				matchingScenario = &s
				break
			}
		}

		if matchingScenario == nil {
			c.String(http.StatusBadRequest, fmt.Sprintf("No matching scenario found for AppSite='%s' without AppView", requestedAppSite))
			return
		}

		appSite = matchingScenario.AppSite
		appFile = matchingScenario.AppFile
	}

	// Get engine type from query parameter (default to Normal)
	engineType := c.DefaultQuery("engine", "Normal")

	// Validate EngineType against allowlist
	if !IsValidEngineType(engineType) {
		c.String(http.StatusBadRequest, "Invalid engine type. Use 'Normal' or 'PreProcess'")
		return
	}

	// Load templates for requested AppSite
	normalTemplatesRaw := loader.LoadGetTemplateFiles(rootDirPath, appSite)
	preprocessTemplatesRaw := loader.LoadProcessGetTemplateFiles(rootDirPath, appSite)

	// Merge using selected engine (no AppView context)
	var mergedHtml string
	if strings.EqualFold(engineType, "PreProcess") {
		engine := engine.NewEnginePreProcess("")
		mergedHtml = engine.MergeTemplates(appSite, appFile, "", preprocessTemplatesRaw.Templates, true)
	} else {
		engine := engine.NewEngineNormal("")
		mergedHtml = engine.MergeTemplates(appSite, appFile, "", normalTemplatesRaw, true)
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
	// Enable logging for merge operations
	originalLogLevel := common.GetLogLevel()

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

	var req MergeRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	// Validate required fields
	if req.AppSite == nil || *req.AppSite == "" {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: appSite"})
		return
	}
	if req.EngineType == nil || *req.EngineType == "" {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: engineType"})
		return
	}

	// Get AppFile from scenarios
	allScenarios, err := config.GetScenarios()
	if err != nil {
		common.SetLogLevel(originalLogLevel)
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
		common.SetLogLevel(originalLogLevel)
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
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid EngineType value"})
		return
	}

	// Validate AppSite against allowlist from ConfigUtil
	validAppSites, err := GetValidAppSites()
	if err != nil {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to get AppSites: " + err.Error()})
		return
	}

	if !IsValidAppSite(*req.AppSite, validAppSites) {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid AppSite value"})
		return
	}

	// Validate path components for path traversal attacks
	if !IsValidPathComponent(req.AppSite) {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppSite"})
		return
	}

	// Validate appFile from scenario
	if !IsValidPathComponent(&appFile) {
		common.SetLogLevel(originalLogLevel)
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppFile"})
		return
	}

	if req.AppView != nil && *req.AppView != "" && !IsValidPathComponent(req.AppView) {
		common.SetLogLevel(originalLogLevel)
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

	// Save HTML output only if save query parameter is present
	saveParam := c.Query("save")
	if strings.EqualFold(saveParam, "true") {
		outputDir := filepath.Join(projectDirectory, "template_analysis", "output")
		os.MkdirAll(outputDir, 0755)

		appViewSuffix := ""
		if appViewValue != "" {
			appViewSuffix = "_" + appViewValue
		}
		engineSuffix := strings.ToLower(*req.EngineType)
		outputFile := filepath.Join(outputDir, fmt.Sprintf("%s%s_%s.html", *req.AppSite, appViewSuffix, engineSuffix))
		os.WriteFile(outputFile, []byte(mergedHTML), 0644)
	}

	responseObj := map[string]interface{}{
		"Html":                mergedHTML,
		"ServerTimeMs":        serverTimeMs,
		"EngineTimeMs":        engineTimeMs,
		"Templates":           make(map[string]interface{}),
		"PreProcessTemplates": make(map[string]interface{}),
	}

	// Restore original log level
	common.SetLogLevel(originalLogLevel)

	c.Header("Content-Type", "application/json")
	c.JSON(http.StatusOK, responseObj)
}

// getTemplates handles the POST /api/templates endpoint
func getTemplates(c *gin.Context) {
	var req struct {
		AppSite string `json:"appSite"`
	}
	if err := c.ShouldBindJSON(&req); err != nil || req.AppSite == "" {
		c.String(http.StatusBadRequest, "Missing appsite parameter")
		return
	}

	// Validate AppSite against allowlist loaded from appsites.csv
	validAppSites, err := GetValidAppSites()
	if err != nil {
		c.String(http.StatusInternalServerError, "Error loading valid AppSites")
		return
	}
	if !validAppSites[req.AppSite] {
		c.String(http.StatusBadRequest, "Invalid AppSite value")
		return
	}

	// Validate path components for path traversal attacks
	if !IsValidPathComponent(&req.AppSite) {
		c.String(http.StatusBadRequest, "Invalid characters in AppSite")
		return
	}

	serverStart := time.Now()

	assemblerWebDirPath, _ := common.GetAssemblerWebDirPath()
	rootDirPath := assemblerWebDirPath

	// Load Normal templates
	normalTemplates := loader.LoadGetTemplateFiles(rootDirPath, req.AppSite)

	// Load PreProcess templates
	preprocessTemplates := loader.LoadProcessGetTemplateFiles(rootDirPath, req.AppSite)

	// Convert Normal templates to TemplateData objects for proper JSON serialization
	normalResult := make(map[string]api.TemplateData)
	for key, value := range normalTemplates {
		jsonValue := ""
		if value.JSON != nil {
			jsonValue = *value.JSON
		}
		normalResult[key] = api.TemplateData{
			Html: value.HTML,
			Json: jsonValue,
		}
	}

	// Convert PreProcess templates to metadata-only objects
	preprocessResult := make(map[string]api.PreProcessTemplateMetadata)
	for key, value := range preprocessTemplates.Templates {
		preprocessResult[key] = api.PreProcessTemplateMetadata{
			OriginalContent:        value.OriginalContent,
			Placeholders:           value.Placeholders,
			SlottedTemplates:       value.SlottedTemplates,
			JsonData:               value.JsonData,
			JsonPlaceholders:       value.JsonPlaceholders,
			ReplacementMappings:    value.ReplacementMappings,
			HasPlaceholders:        value.HasPlaceholders,
			HasSlottedTemplates:    value.HasSlottedTemplates,
			HasJsonData:            value.HasJsonData,
			HasJsonPlaceholders:    value.HasJsonPlaceholders,
			HasReplacementMappings: value.HasReplacementMappings,
			RequiresProcessing:     value.RequiresProcessing,
		}
	}

	serverEnd := time.Now()
	serverTimeMs := float64(serverEnd.Sub(serverStart).Milliseconds())

	// Use proper ApiResponse structure
	response := api.ApiResponse{
		Templates:           normalResult,
		PreProcessTemplates: preprocessResult,
		AppSite:             req.AppSite,
		AppFile:             "",
		AppView:             "",
		ServerTimeMs:        serverTimeMs,
	}

	jsonResult := response.SerializeToJson(false)

	// Check if save query parameter is present
	saveParam := c.Query("save")
	if strings.EqualFold(saveParam, "true") {
		templatesDir := filepath.Join(projectDirectory, "template_analysis", "templates")
		os.MkdirAll(templatesDir, 0755)

		saveFile := filepath.Join(templatesDir, fmt.Sprintf("go_%s_templates.json", req.AppSite))
		os.WriteFile(saveFile, []byte(jsonResult), 0644)

		// Save structure dump using TestingUtils logic
		// Build a scenario list for the requested appSite
		scenarios := []config.Scenario{
			{AppSite: req.AppSite, AppFile: "", AppView: ""},
		}
		test.DumpPreprocessedTemplateStructures(rootDirPath, projectDirectory, scenarios, true)
	}

	c.Header("Content-Type", "application/json")
	c.String(http.StatusOK, jsonResult)
}

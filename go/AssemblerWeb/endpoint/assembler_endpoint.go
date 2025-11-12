package endpoint

import (
	"fmt"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/gin-gonic/gin"

	"assembler/api"
	"assembler/config"
	enginenormal "assembler/engine/normal"
	enginenormaljson "assembler/engine/normaljson"
	enginepreprocess "assembler/engine/preprocess"
	enginepreprocessjson "assembler/engine/preprocessjson"
	loadernormal "assembler/loader/normal"
	loadernormaljson "assembler/loader/normaljson"
	loaderpreprocess "assembler/loader/preprocess"
	loaderpreprocessjson "assembler/loader/preprocessjson"

	Logger "arshu/common"
)

const DefaultAppSite = "Main"
const DefaultEngineType = "Normal"
const SearchAppSites = "Main, Language"

// Maximum parameter length to prevent DoS attacks
const paramMaxLength = 256

// ValidEngineTypes is the allowlist of valid engine types
var validEngineTypes = map[string]bool{
	"Normal":         true,
	"PreProcess":     true,
	"NormalJson":     true,
	"PreProcessJson": true,
}

// GetProjectDirectory returns the current project directory
func getProjectDirectory() string {
	dir, err := os.Getwd()
	if err != nil {
		return "."
	}
	return dir
}

// GetWwwrootPath returns the wwwroot path
func getWwwrootPath() string {
	return filepath.Join(getProjectDirectory(), "wwwroot")
}

// GetValidAppSites gets the valid AppSites from ConfigUtil
func getValidAppSites() (map[string]bool, error) {
	return config.GetAppSites()
}

// IsValidPathComponent validates if a path component is safe
func isValidPathComponent(value *string) bool {
	if value == nil {
		return false
	}

	v := strings.TrimSpace(*value)
	if v == "" {
		return false
	}

	// Check parameter length to prevent DoS
	if len(v) > paramMaxLength {
		return false
	}

	// Check for path traversal attempts
	if strings.Contains(v, "..") || strings.Contains(v, "/") || strings.Contains(v, "\\") {
		return false
	}

	// Check for suspicious characters
	invalidChars := []rune{'<', '>', ':', '"', '|', '?', '*', '\x00'}
	for _, char := range v {
		for _, invalid := range invalidChars {
			if char == invalid {
				return false
			}
		}
		// Check for control characters
		if char < 32 {
			return false
		}
	}

	return true
}

// buildTemplatesApiResponse builds the JSON response for /api/templates endpoint using pre-serialized template JSON
func buildTemplatesApiResponse(normalTemplatesJson, preprocessTemplatesJson, appSite string, serverTimeMs float64) string {
	return fmt.Sprintf(`{"Templates":%s,"PreProcessTemplates":%s,"AppSite":"%s","AppFile":null,"AppView":null,"ServerTimeMs":%f}`,
		normalTemplatesJson,
		preprocessTemplatesJson,
		escapeJsonString(appSite),
		serverTimeMs)
}

// escapeJsonString escapes a string for safe inclusion in JSON
func escapeJsonString(input string) string {
	replacer := strings.NewReplacer(
		"\\", "\\\\",
		"\"", "\\\"",
		"\r", "\\r",
		"\n", "\\n",
		"\t", "\\t",
		"<", "\\u003C",
		">", "\\u003E",
		"&", "\\u0026",
		"'", "\\u0027",
		"+", "\\u002B",
	)
	return replacer.Replace(input)
}

// IsValidEngineType validates engine type against allowlist (case-insensitive)
func isValidEngineType(engineType string) bool {
	for validType := range validEngineTypes {
		if strings.EqualFold(validType, engineType) {
			return true
		}
	}
	return false
}

// IsValidAppSite validates app_site against allowlist (case-insensitive)
func isValidAppSite(appSite string, validAppSites map[string]bool) bool {
	for validSite := range validAppSites {
		if strings.EqualFold(validSite, appSite) {
			return true
		}
	}
	return false
}

// MergeRequest represents the request structure for template merging
type MergeRequest struct {
	AppSite    *string `json:"appSite" binding:"required"`
	AppView    *string `json:"appView"`
	EngineType *string `json:"engineType" binding:"required"`
}

// Index handles the GET / endpoint
func Index(c *gin.Context) {
	// Get appsite from query parameter or use default
	rootDirPath := getWwwrootPath()

	appSite := DefaultAppSite
	appFile := "index"

	// Get appsite from query parameter
	requestedAppSite := c.Query("appsite")

	// If appsite query param is provided, validate it exists in scenarios
	if requestedAppSite != "" {
		// Validate AppSite against allowlist
		validAppSites, err := getValidAppSites()
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
		if !isValidPathComponent(&requestedAppSite) {
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

	// Get engine type from query parameter (default to DefaultEngineType)
	engineType := c.DefaultQuery("engine", DefaultEngineType)

	// Validate EngineType against allowlist
	if !isValidEngineType(engineType) {
		c.String(http.StatusBadRequest, "Invalid engine type. Use 'Normal', 'PreProcess', 'NormalJson', or 'PreProcessJson'")
		return
	}

	// Merge using selected engine (no AppView context)
	var mergedHtml string
	if strings.EqualFold(engineType, "PreProcessJson") {
		loaderPreprocessJson := loader.NewLoaderPreprocessJson(rootDirPath, appSite, SearchAppSites)
		engine := engine.NewEnginePreProcessJson("")
		mergedHtml = engine.MergeTemplates(appSite, appFile, "", loaderPreprocessJson, true)
	} else if strings.EqualFold(engineType, "PreProcess") {
		loaderPreProcess := loader.NewLoaderPreProcess(rootDirPath, appSite, SearchAppSites)
		engine := engine.NewEnginePreProcess("")
		mergedHtml = engine.MergeTemplates(appSite, appFile, "", loaderPreProcess, true)
	} else if strings.EqualFold(engineType, "NormalJson") {
		loaderNormalJson := loader.NewLoaderNormalJson(rootDirPath, appSite, SearchAppSites)
		engine := engine.NewEngineNormalJson("")
		mergedHtml = engine.MergeTemplates(appSite, appFile, "", loaderNormalJson, true)
	} else {
		loaderNormal := loader.NewLoaderNormal(rootDirPath, appSite, SearchAppSites)
		engine := engine.NewEngineNormal("")
		mergedHtml = engine.MergeTemplates(appSite, appFile, "", loaderNormal, true)
	}

	c.Header("Content-Type", "text/html")
	c.String(http.StatusOK, mergedHtml)
}

// NavigationEndpoint handles GET /:appSite/:appView navigation
func NavigationEndpoint(c *gin.Context) {
	appSite := c.Param("appSite")
	appView := c.Param("appView") // Optional, may be empty

	// Validate AppSite against allowlist
	validAppSites, err := getValidAppSites()
	if err != nil {
		c.String(http.StatusInternalServerError, "Error loading valid AppSites: "+err.Error())
		return
	}

	appSiteLower := strings.ToLower(appSite)
	if !validAppSites[appSiteLower] {
		c.String(http.StatusBadRequest, "Invalid AppSite value")
		return
	}

	// Validate path components for path traversal attacks
	if !isValidPathComponent(&appSite) {
		c.String(http.StatusBadRequest, "Invalid characters in AppSite")
		return
	}

	if appView != "" && !isValidPathComponent(&appView) {
		c.String(http.StatusBadRequest, "Invalid characters in AppView")
		return
	}

	// Get AppFile from scenarios
	scenarios, err := config.GetScenarios()
	if err != nil {
		c.String(http.StatusInternalServerError, "Failed to get scenarios: "+err.Error())
		return
	}

	appViewValue := appView
	var matchingScenario *config.Scenario
	for _, s := range scenarios {
		if strings.EqualFold(s.AppSite, appSite) && strings.EqualFold(s.AppView, appViewValue) {
			matchingScenario = &s
			break
		}
	}

	if matchingScenario == nil {
		c.String(http.StatusBadRequest, fmt.Sprintf("No matching scenario found for AppSite='%s' and AppView='%s'", appSite, appViewValue))
		return
	}

	appFile := matchingScenario.AppFile

	// Get engine type from query parameter (default to DefaultEngineType)
	engineType := c.Query("engine")
	if engineType == "" {
		engineType = DefaultEngineType
	}

	// Validate EngineType against allowlist
	if !isValidEngineType(engineType) {
		c.String(http.StatusBadRequest, "Invalid engine type. Use 'Normal', 'PreProcess', 'NormalJson', or 'PreProcessJson'")
		return
	}

	// Get wwwroot path
	wwwrootPath := getWwwrootPath()

	// Merge using selected engine
	var mergedHtml string
	if strings.EqualFold(engineType, "PreProcessJson") {
		loaderPreprocessJson := loader.NewLoaderPreprocessJson(wwwrootPath, appSite, SearchAppSites)
		eng := engine.NewEnginePreProcessJson("")
		mergedHtml = eng.MergeTemplates(appSite, appFile, appViewValue, loaderPreprocessJson, false)
	} else if strings.EqualFold(engineType, "PreProcess") {
		loaderPreProcess := loader.NewLoaderPreProcess(wwwrootPath, appSite, SearchAppSites)
		eng := engine.NewEnginePreProcess("")
		mergedHtml = eng.MergeTemplates(appSite, appFile, appViewValue, loaderPreProcess, false)
	} else if strings.EqualFold(engineType, "NormalJson") {
		loaderNormalJson := loader.NewLoaderNormalJson(wwwrootPath, appSite, SearchAppSites)
		eng := engine.NewEngineNormalJson("")
		mergedHtml = eng.MergeTemplates(appSite, appFile, appViewValue, loaderNormalJson, false)
	} else {
		loaderNormal := loader.NewLoaderNormal(wwwrootPath, appSite, SearchAppSites)
		eng := engine.NewEngineNormal("")
		mergedHtml = eng.MergeTemplates(appSite, appFile, appViewValue, loaderNormal, false)
	}

	c.Header("Content-Type", "text/html")
	c.String(http.StatusOK, mergedHtml)
}

// MergeTemplates handles the POST /merge endpoint
func MergeTemplates(c *gin.Context) {
	// Enable logging for merge operations
	projectDirectory := getProjectDirectory()
	templateAnalysisDir := filepath.Join(projectDirectory, "Analysis")
	logsDir := filepath.Join(templateAnalysisDir, "logs")
	os.MkdirAll(logsDir, 0755)

	contextLogFiles := map[string]string{
		"LoaderNormal":         filepath.Join(logsDir, "go_loadernormal.log"),
		"LoaderPreProcess":     filepath.Join(logsDir, "go_loaderpreprocess.log"),
		"LoaderNormalJson":     filepath.Join(logsDir, "go_loadernormaljson.log"),
		"LoaderPreProcessJson": filepath.Join(logsDir, "go_loaderpreprocessjson.log"),
		"EngineNormal":         filepath.Join(logsDir, "go_enginenormal.log"),
		"EnginePreProcess":     filepath.Join(logsDir, "go_enginepreprocess.log"),
		"EngineNormalJson":     filepath.Join(logsDir, "go_enginenormaljson.log"),
		"EnginePreProcessJson": filepath.Join(logsDir, "go_enginepreprocessjson.log"),
	}

	Logger.AddContextLogFiles(contextLogFiles)

	var req MergeRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	// Validate required fields
	if req.AppSite == nil || *req.AppSite == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: appSite"})
		return
	}
	if req.EngineType == nil || *req.EngineType == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: engineType"})
		return
	}

	// Get AppFile from scenarios
	allScenarios, err := config.GetScenarios()
	if err != nil {
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
	Logger.Info(logMsg, "MergeEndpoint")

	// Get wwwroot directory
	rootDirPath := getWwwrootPath()

	// Validate EngineType against allowlist
	if !isValidEngineType(*req.EngineType) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid EngineType value"})
		return
	}

	// Validate AppSite against allowlist from ConfigUtil
	validAppSites, err := getValidAppSites()
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to get AppSites: " + err.Error()})
		return
	}

	if !isValidAppSite(*req.AppSite, validAppSites) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid AppSite value"})
		return
	}

	// Validate path components for path traversal attacks
	if !isValidPathComponent(req.AppSite) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppSite"})
		return
	}

	// Validate appFile from scenario
	if !isValidPathComponent(&appFile) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppFile"})
		return
	}

	if req.AppView != nil && *req.AppView != "" && !isValidPathComponent(req.AppView) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppView"})
		return
	}

	serverStart := time.Now()
	engineStart := time.Now()
	var mergedHTML string
	if strings.EqualFold(*req.EngineType, "PreProcessJson") {
		loaderPreprocessJson := loader.NewLoaderPreprocessJson(rootDirPath, *req.AppSite, SearchAppSites)
		engine := engine.NewEnginePreProcessJson(appViewPrefix)
		mergedHTML = engine.MergeTemplates(*req.AppSite, appFile, appViewValue, loaderPreprocessJson, true)
	} else if strings.EqualFold(*req.EngineType, "PreProcess") {
		loaderPreProcess := loader.NewLoaderPreProcess(rootDirPath, *req.AppSite, SearchAppSites)
		engine := engine.NewEnginePreProcess(appViewPrefix)
		mergedHTML = engine.MergeTemplates(*req.AppSite, appFile, appViewValue, loaderPreProcess, true)
	} else if strings.EqualFold(*req.EngineType, "NormalJson") {
		loaderNormalJson := loader.NewLoaderNormalJson(rootDirPath, *req.AppSite, SearchAppSites)
		engine := engine.NewEngineNormalJson(appViewPrefix)
		mergedHTML = engine.MergeTemplates(*req.AppSite, appFile, appViewValue, loaderNormalJson, true)
	} else {
		loaderNormal := loader.NewLoaderNormal(rootDirPath, *req.AppSite, SearchAppSites)
		engine := engine.NewEngineNormal(appViewPrefix)
		mergedHTML = engine.MergeTemplates(*req.AppSite, appFile, appViewValue, loaderNormal, true)
	}
	engineTimeMs := float64(time.Since(engineStart).Microseconds()) / 1000.0
	serverTimeMs := float64(time.Since(serverStart).Microseconds()) / 1000.0

	// Save HTML output only if save query parameter is present
	saveParam := c.Query("save")
	if strings.EqualFold(saveParam, "true") {
		outputDir := filepath.Join(projectDirectory, "Analysis", "output")
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
		"AppSite":      *req.AppSite,
		"AppFile":      appFile,
		"AppView":      appViewValue,
		"Html":         mergedHTML,
		"ServerTimeMs": serverTimeMs,
		"EngineTimeMs": engineTimeMs,
	}

	// Restore original log level
	Logger.SetLogLevel(originalLogLevel)

	c.Header("Content-Type", "application/json")
	c.JSON(http.StatusOK, responseObj)
}

// GetTemplates handles the POST /api/templates endpoint
func GetTemplates(c *gin.Context) {
	// Enable logging for template operations
	projectDirectory := getProjectDirectory()

	templateAnalysisDir := filepath.Join(projectDirectory, "Analysis")
	logsDir := filepath.Join(templateAnalysisDir, "logs")
	os.MkdirAll(logsDir, 0755)

	contextLogFiles := map[string]string{
		"LoaderNormalJson":     filepath.Join(logsDir, "go_loadernormaljson.log"),
		"LoaderPreProcessJson": filepath.Join(logsDir, "go_loaderpreprocessjson.log"),
	}

	Logger.AddContextLogFiles(contextLogFiles)

	var req struct {
		AppSite string `json:"appSite"`
	}
	if err := c.ShouldBindJSON(&req); err != nil || req.AppSite == "" {
		c.String(http.StatusBadRequest, "Missing appsite parameter")
		return
	}

	// Validate AppSite against allowlist loaded from appsites.csv
	validAppSites, err := getValidAppSites()
	if err != nil {
		c.String(http.StatusInternalServerError, "Error loading valid AppSites")
		return
	}
	if !validAppSites[req.AppSite] {
		c.String(http.StatusBadRequest, "Invalid AppSite value")
		return
	}

	// Validate path components for path traversal attacks
	if !isValidPathComponent(&req.AppSite) {
		c.String(http.StatusBadRequest, "Invalid characters in AppSite")
		return
	}

	serverStart := time.Now()

	rootDirPath := getWwwrootPath()

	// Load Normal templates using JSON loader
	normalLoader := loader.NewLoaderNormalJson(rootDirPath, req.AppSite, SearchAppSites)

	// Load PreProcess templates using JSON loader
	preprocessLoader := loader.NewLoaderPreprocessJson(rootDirPath, req.AppSite, SearchAppSites)

	// Get pre-serialized JSON from loaders
	normalTemplatesJson := normalLoader.GetAllTemplatesJson()
	preprocessTemplatesJson := preprocessLoader.GetAllTemplatesJson()

	serverEnd := time.Now()
	serverTimeMs := float64(serverEnd.Sub(serverStart).Milliseconds())

	// Build JSON response manually using pre-serialized template JSON
	jsonResult := buildTemplatesApiResponse(normalTemplatesJson, preprocessTemplatesJson, req.AppSite, serverTimeMs)

	c.Header("Content-Type", "application/json")
	c.String(http.StatusOK, jsonResult)
}

// Helper function to safely dereference string pointers
func safeString(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}

// MapAssemblerEndpoints maps all assembler endpoints to the router
// Usage: endpoint.MapAssemblerEndpoints(router)
func MapAssemblerEndpoints(router *gin.Engine) {
	// Map assembler endpoints
	router.GET("/", Index)
	router.GET("/:appSite", NavigationEndpoint)
	router.GET("/:appSite/:appView", NavigationEndpoint)
	router.POST("/merge", MergeTemplates)
	router.POST("/api/templates", GetTemplates)
}

package main

import (
	"fmt"
	"net/http"
	"strings"
	"time"

	"github.com/gin-gonic/gin"

	"assembler/template_common"
	"assembler/template_engine"
	"assembler/template_loader"
)

// MergeRequest represents the request structure for template merging
type MergeRequest struct {
	AppSite       *string `json:"appSite" binding:"required"`
	AppView       *string `json:"appView"`
	AppViewPrefix *string `json:"appViewPrefix"`
	AppFile       *string `json:"appFile" binding:"required"`
	EngineType    *string `json:"engineType" binding:"required"`
}

// index handles the GET / endpoint
func index(c *gin.Context) {
	// Use Index AppSite with engine toggle parameter
	assemblerWebDirPath, _ := template_common.GetAssemblerWebDirPath()
	rootDirPath := assemblerWebDirPath

	// Get engine type from query parameter (default to Normal)
	engineType := c.DefaultQuery("engine", "Normal")

	// Validate EngineType against allowlist
	if !IsValidEngineType(engineType) {
		c.String(http.StatusBadRequest, "Invalid engine type. Use 'Normal' or 'PreProcess'")
		return
	}

	// TEMPORARY: Clear cache for development
	template_loader.ClearCache()
	template_loader.ClearPreProcessCache()

	// Load templates for Index AppSite
	normalTemplatesRaw := template_loader.LoadGetTemplateFiles(rootDirPath, "Index")
	preprocessTemplatesRaw := template_loader.LoadProcessGetTemplateFiles(rootDirPath, "Index")

	// Merge using selected engine (no AppView context for Index)
	var mergedHtml string
	if strings.EqualFold(engineType, "PreProcess") {
		engine := template_engine.NewEnginePreProcess("")
		mergedHtml = engine.MergeTemplates("Index", "Index", "", preprocessTemplatesRaw.Templates, true)
	} else {
		engine := template_engine.NewEngineNormal("")
		mergedHtml = engine.MergeTemplates("Index", "Index", "", normalTemplatesRaw, true)
	}

	c.Header("Content-Type", "text/html")
	c.String(http.StatusOK, mergedHtml)
}

// mergeTemplates handles the POST /merge endpoint
func mergeTemplates(c *gin.Context) {
	var req MergeRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	logMsg := fmt.Sprintf("/merge endpoint called with: app_site=%v, app_file=%v, engine_type=%v, app_view=%v, app_view_prefix=%v",
		safeString(req.AppSite), safeString(req.AppFile), safeString(req.EngineType), safeString(req.AppView), safeString(req.AppViewPrefix))
	fmt.Println(logMsg)
	template_common.Info(logMsg, "MergeEndpoint")

	// Validate required fields
	if req.AppSite == nil || *req.AppSite == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: appSite"})
		return
	}
	if req.AppFile == nil || *req.AppFile == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: appFile"})
		return
	}
	if req.EngineType == nil || *req.EngineType == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: engineType"})
		return
	}

	// Get wwwroot directory
	assemblerWebDirPath, _ := template_common.GetAssemblerWebDirPath()
	rootDirPath := assemblerWebDirPath

	// Validate EngineType against allowlist
	if !IsValidEngineType(*req.EngineType) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid EngineType value"})
		return
	}

	// Validate AppSite against allowlist loaded from appsites.csv
	validAppSites, err := GetValidAppSites(rootDirPath)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "Failed to load AppSites: " + err.Error()})
		return
	}

	if !IsValidAppSite(*req.AppSite, validAppSites) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid AppSite value"})
		return
	}

	// Validate path components for path traversal attacks
	if !IsValidPathComponent(req.AppSite) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppSite"})
		return
	}

	if !IsValidPathComponent(req.AppFile) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppFile"})
		return
	}

	if req.AppView != nil && *req.AppView != "" && !IsValidPathComponent(req.AppView) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppView"})
		return
	}

	if req.AppViewPrefix != nil && *req.AppViewPrefix != "" && !IsValidPathComponent(req.AppViewPrefix) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Invalid characters in AppViewPrefix"})
		return
	}

	// TEMPORARY: Clear cache for development
	template_loader.ClearCache()
	template_loader.ClearPreProcessCache()

	serverStart := time.Now()
	engineStart := time.Now()
	var mergedHTML string
	if strings.EqualFold(*req.EngineType, "PreProcess") {
		templates := template_loader.LoadProcessGetTemplateFiles(rootDirPath, *req.AppSite)
		engine := template_engine.NewEnginePreProcess(safeString(req.AppViewPrefix))
		mergedHTML = engine.MergeTemplates(*req.AppSite, *req.AppFile, safeString(req.AppView), templates.Templates, true)
	} else {
		templates := template_loader.LoadGetTemplateFiles(rootDirPath, *req.AppSite)
		engine := template_engine.NewEngineNormal(safeString(req.AppViewPrefix))
		mergedHTML = engine.MergeTemplates(*req.AppSite, *req.AppFile, safeString(req.AppView), templates, true)
	}
	engineTimeMs := float64(time.Since(engineStart).Microseconds()) / 1000.0
	serverTimeMs := float64(time.Since(serverStart).Microseconds()) / 1000.0
	responseObj := map[string]interface{}{
		"Html":         mergedHTML,
		"ServerTimeMs": serverTimeMs,
		"EngineTimeMs": engineTimeMs,
		"Templates":    make(map[string]interface{}),
		"PreProcessTemplates": make(map[string]interface{}),
	}
	c.Header("Content-Type", "application/json")
	c.JSON(http.StatusOK, responseObj)
}

// Helper function
func safeString(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}

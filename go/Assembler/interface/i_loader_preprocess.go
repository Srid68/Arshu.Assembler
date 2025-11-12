package interfaces

import "assembler/model"

// ILoaderPreProcess is the loader interface specifically for EnginePreProcess
// Provides preprocessed template access with pre-calculated replacement mappings
// Independent interface to ensure no coupling with other engine implementations
type ILoaderPreProcess interface {
	// Loading Related Methods

	// GetSearchAppSites gets the search AppSites for template fallback resolution
	// Returns comma-delimited string of AppSite names
	GetSearchAppSites() string

	// HasTemplate checks if a template exists
	HasTemplate(appSite, templateName string) bool

	// ClearCache clears the template cache (for testing/hot reload)
	ClearCache()

	// Merging Related Methods

	// GetTemplateHtml gets a preprocessed template by appSite and name with optional AppView fallback
	// Returns PreprocessedTemplate with all replacement mappings pre-calculated
	// Searches in SearchAppSites if not found in primary appSite
	GetTemplateHtml(appSite, templateName, appView, appViewPrefix string) *model.PreprocessedTemplate

	// MergeHtmlWithJson merges HTML string with JSON data for the specified template
	// This centralizes JSON merging logic in the loader for clean architecture
	// The loader internally retrieves JSON (with inheritance resolution) and merges it with HTML
	// Engines cannot access raw JSON - all JSON retrieval and merging happens inside the loader
	MergeHtmlWithJson(html, appSite, templateName string) string

	// ApplyAllReplacementMappings applies all replacement mappings from all templates to the given content
	// This is the core PreProcess engine logic - loader applies all mappings internally
	// Engines call this method without needing direct access to template dictionary
	ApplyAllReplacementMappings(content, appSite string, mainTemplate *model.PreprocessedTemplate, appView, appViewPrefix string, enableJsonProcessing bool) string
}

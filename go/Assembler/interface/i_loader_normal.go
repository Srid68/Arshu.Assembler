package interfaces

// ILoaderNormal is the loader interface specifically for EngineNormal
// Provides raw HTML/JSON template access for Normal engine's runtime merging approach
// Independent interface to ensure no coupling with other engine implementations
type ILoaderNormal interface {
	// Loading Related Methods

	// GetSearchAppSites gets the search AppSites for template fallback resolution
	// Returns comma-delimited string of AppSite names
	GetSearchAppSites() string

	// HasTemplate checks if a template exists
	HasTemplate(appSite, templateName string) bool

	// ClearCache clears the template cache (for testing/hot reload)
	ClearCache()

	// Merging Related Methods

	// GetTemplateHtml gets a template's HTML content by appSite and name with optional AppView fallback
	// Returns raw HTML only (no JSON merged)
	// Searches in SearchAppSites if not found in primary appSite
	GetTemplateHtml(appSite, templateName, appView, appViewPrefix string) string

	// MergeHtmlWithJson merges HTML string with JSON data for the specified template
	// This centralizes JSON merging logic in the loader for clean architecture
	// The loader internally retrieves JSON (with inheritance resolution) and merges it with HTML
	// Engines cannot access raw JSON - all JSON retrieval and merging happens inside the loader
	MergeHtmlWithJson(html, appSite, templateName string) string
}

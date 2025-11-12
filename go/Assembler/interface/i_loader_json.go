package interfaces

// ILoaderJson is the generic loader interface equivalent to C# ILoaderJson<TTemplate>
// In Go, GetTemplateHtml returns interface{} which can be either string or *model.PreprocessedTemplate
// depending on the concrete implementation (LoaderNormalJson or LoaderPreProcessJson)
type ILoaderJson interface {
	// Loading Related Methods

	// GetSearchAppSites gets the search AppSites for template fallback resolution
	// Returns comma-delimited string of AppSite names
	GetSearchAppSites() string

	// HasTemplate checks if a template exists
	HasTemplate(appSite, templateName string) bool

	// GetAllTemplatesJson gets all templates as a serialized JSON string for client-side template engine
	// This is specifically for /api/templates endpoint
	GetAllTemplatesJson() string

	// ClearCache clears the template cache (for testing/hot reload)
	ClearCache()

	// Merging Related Methods

	// GetTemplateHtml gets a template by appSite and name with optional AppView fallback
	// Returns interface{} which is either string (for LoaderNormalJson) or *PreprocessedTemplate (for LoaderPreProcessJson)
	// Searches in SearchAppSites if not found in primary appSite
	GetTemplateHtml(appSite, templateName, appView, appViewPrefix string) interface{}

	// MergeHtmlWithJson merges HTML string with JSON data using inheritance-aware JSON retrieval
	// This centralizes JSON merging logic in the loader for clean architecture
	MergeHtmlWithJson(html, appSite, templateName string) string

	// ApplyAllReplacementMappings applies all replacement mappings from all templates to the given content
	// This is the core PreProcess engine logic - loader applies all mappings internally
	// Only applicable for PreprocessedTemplate type implementations
	ApplyAllReplacementMappings(content, appSite string, mainTemplate interface{}, appView, appViewPrefix string, enableJsonProcessing bool) string
}

/// Loader interface specifically for EngineNormal
/// Provides simple string-based template access without JSON support
/// Independent interface to ensure no coupling with other engine implementations
pub trait ILoaderNormal {
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    fn search_app_sites(&self) -> &str;

    /// Gets a template's HTML content by appSite and name with optional AppView fallback
    /// Returns raw HTML string only (no JSON merged)
    /// Searches in SearchAppSites if not found in primary appSite
    /// Templates stay immutable inside the loader
    ///
    /// # Arguments
    /// * `app_site` - The application site name
    /// * `template_name` - The template name (e.g., "Header", "Index")
    /// * `app_view` - Optional AppView for fallback logic
    /// * `app_view_prefix` - Optional AppView prefix for fallback logic
    ///
    /// # Returns
    /// Template HTML content or None if not found
    fn get_template_html(
        &self,
        app_site: &str,
        template_name: &str,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
    ) -> Option<String>;

    /// Gets parsed JSON data for a template
    /// Returns None if no JSON file exists for the template
    /// Searches in SearchAppSites if not found in primary appSite
    ///
    /// # Arguments
    /// * `app_site` - The application site name
    /// * `template_name` - The template name
    ///
    /// # Returns
    /// Parsed JsonObject or None if no JSON file exists
    fn get_template_json(&self, app_site: &str, template_name: &str) -> Option<crate::app::json::JsonObject>;

    /// Merges HTML string with JSON data using loader-controlled inheritance logic
    /// Centralizes JSON merging so engines do not need to understand inheritance rules
    ///
    /// # Arguments
    /// * `html` - Raw HTML to merge
    /// * `app_site` - Application site for JSON lookup
    /// * `template_name` - Template name for JSON lookup
    ///
    /// # Returns
    /// HTML merged with JSON (or original HTML if no JSON is available)
    fn merge_html_with_json(&self, html: &str, app_site: &str, template_name: &str) -> String;

    /// Checks if a template exists
    ///
    /// # Arguments
    /// * `app_site` - The application site name
    /// * `template_name` - The template name
    ///
    /// # Returns
    /// True if template exists, false otherwise
    fn has_template(&self, app_site: &str, template_name: &str) -> bool;

    /// Clears the template cache (for testing/hot reload)
    fn clear_cache(&self);
}

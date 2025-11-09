use crate::app::JsonObject;

/// Generic loader interface that provides template extraction (HTML and JSON)
/// This interface is responsible ONLY for retrieval - engines handle all merging logic
pub trait ILoader<TTemplate> {
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    fn search_app_sites(&self) -> &str;

    /// Gets a template's HTML content by appSite and name with optional AppView fallback
    /// Returns raw HTML only (no JSON merged)
    /// Searches in SearchAppSites if not found in primary appSite
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
    ) -> Option<TTemplate>;

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
    fn get_template_json(&self, app_site: &str, template_name: &str) -> Option<JsonObject>;

    /// Checks if a template exists
    ///
    /// # Arguments
    /// * `app_site` - The application site name
    /// * `template_name` - The template name
    ///
    /// # Returns
    /// true if template exists, false otherwise
    fn has_template(&self, app_site: &str, template_name: &str) -> bool;

    /// Clears the template cache (for testing/hot reload)
    fn clear_cache(&self);
}

use crate::model::model_preprocess::PreprocessedTemplate;

/// Generic loader interface that provides template extraction (HTML and JSON)
/// and JSON merging with inheritance support for clean architecture
/// TTemplate can be String (for NormalJson) or PreprocessedTemplate (for PreProcessJson)
pub trait ILoaderJson<TTemplate> {
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    fn search_app_sites(&self) -> &str;

    /// Gets a template's HTML content by appSite and name with optional AppView fallback
    /// Returns raw HTML only (no JSON merged)
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
    ) -> Option<TTemplate>;

    /// Merges HTML string with JSON data using inheritance-aware JSON retrieval
    /// This centralizes JSON merging logic in the loader for clean architecture
    /// The loader internally retrieves JSON (with inheritance resolution) and merges it with HTML
    /// Engines cannot access raw JSON - all JSON retrieval and merging happens inside the loader
    /// Note: This method always works with string HTML, independent of TTemplate type
    ///
    /// # Arguments
    /// * `html` - The HTML string content to merge
    /// * `app_site` - The application site name
    /// * `template_name` - The template name (for retrieving JSON data)
    ///
    /// # Returns
    /// HTML string with JSON data merged, or original HTML if no JSON exists
    fn merge_html_with_json(&self, html: &str, app_site: &str, template_name: &str) -> String;

    /// Applies all replacement mappings from all templates to the given content
    /// This is the core PreProcess engine logic - loader applies all mappings internally
    /// Engines call this method without needing direct access to template dictionary
    /// Only applicable for PreprocessedTemplate type (TTemplate = PreprocessedTemplate)
    ///
    /// # Arguments
    /// * `content` - The content HTML to apply replacements to
    /// * `app_site` - The application site name
    /// * `main_template` - The main template (for first-pass JSON placeholder logic)
    /// * `app_view` - Optional AppView for fallback logic
    /// * `app_view_prefix` - Optional AppView prefix for fallback logic
    /// * `enable_json_processing` - Whether to enable JSON data processing
    ///
    /// # Returns
    /// Content with all replacement mappings applied
    fn apply_all_replacement_mappings(
        &self,
        content: &str,
        app_site: &str,
        main_template: Option<&PreprocessedTemplate>,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
        enable_json_processing: bool,
    ) -> String;

    /// Checks if a template exists
    ///
    /// # Arguments
    /// * `app_site` - The application site name
    /// * `template_name` - The template name
    ///
    /// # Returns
    /// True if template exists, false otherwise
    fn has_template(&self, app_site: &str, template_name: &str) -> bool;

    /// Gets all templates as a serialized JSON string for client-side template engine
    /// This is specifically for /api/templates endpoint
    /// Returns a JSON string - templates remain immutable (no references to internal state)
    ///
    /// # Returns
    /// Serialized JSON string containing all templates
    fn get_all_templates_json(&self) -> String;

    /// Clears the template cache (for testing/hot reload)
    fn clear_cache(&self);
}

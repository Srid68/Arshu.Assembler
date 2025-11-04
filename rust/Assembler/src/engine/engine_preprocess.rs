use crate::model::model_preprocess::{PreprocessedTemplate, ReplacementType};
use crate::common::common_util::CommonUtil;
use crate::common::logger::Logger;
use std::collections::HashMap;

/// <summary>
/// PreProcess template engine implementation that only does merging using preprocessed data structures
/// All parsing is done by TemplateLoader, this engine only handles merging
/// </summary>
pub struct EnginePreProcess {
    app_view_prefix: String,
}

impl EnginePreProcess {
    pub fn new(prefix: String) -> Self {
        Self { app_view_prefix: prefix }
    }

    pub fn app_view_prefix(&self) -> &str {
        &self.app_view_prefix
    }
    
    pub fn set_app_view_prefix(&mut self, prefix: String) {
        self.app_view_prefix = prefix;
    }
    
    /// <summary>
    /// Merges templates using preprocessed data structures
    /// This method only does merging using preprocessed data structures - no loading or parsing
    /// </summary>
    /// <param name="app_site">The application site name for template key generation</param>
    /// <param name="app_file">The application file name</param>
    /// <param name="app_view">The application view name (optional)</param>
    /// <param name="preprocessed_templates">Dictionary of preprocessed templates for this specific appSite</param>
    /// <param name="enable_json_processing">Whether to enable JSON data processing</param>
    /// <returns>HTML with placeholders replaced using preprocessed structures</returns>
    pub fn merge_templates(
        &self,
        app_site: &str,
        app_file: &str,
        app_view: Option<&str>,
        preprocessed_templates: &HashMap<String, PreprocessedTemplate>,
        enable_json_processing: bool,
    ) -> String {
        let app_view_str = app_view.unwrap_or("null");
        Logger::debug(&format!("MergeTemplates called: appSite={}, appFile={}, appView={}, enableJson={}",
            app_site, app_file, app_view_str, enable_json_processing), Some("EnginePreProcess"));

        if preprocessed_templates.is_empty() {
            Logger::warn("No preprocessed templates available", Some("EnginePreProcess"));
            return String::new();
        }

        Logger::debug(&format!("Using {} preprocessed templates", preprocessed_templates.len()), Some("EnginePreProcess"));

        // Use the get_template method to retrieve the main template
        let main_preprocessed = self.get_template(
            app_site,
            app_file,
            preprocessed_templates,
            app_view,
            Some(&self.app_view_prefix),
            true,
        );

        if main_preprocessed.is_none() {
            Logger::warn(&format!("Main template not found for appSite={}, appFile={}", app_site, app_file), Some("EnginePreProcess"));
            return String::new();
        }

        let main_preprocessed = main_preprocessed.unwrap();

        Logger::debug(&format!("Main template found, original size: {}", main_preprocessed.original_content.len()), Some("EnginePreProcess"));

        // Start with original content
        let content_html = main_preprocessed.original_content.clone();

        // Apply ALL replacement mappings from ALL templates (TemplateLoader did all the processing)
        let result = self.apply_template_replacements(&content_html, preprocessed_templates, enable_json_processing, app_view, main_preprocessed);

        Logger::debug(&format!("MergeTemplates complete: output size={}", result.len()), Some("EnginePreProcess"));
        result
    }

    /// <summary>
    /// Retrieves a template from the preprocessed templates dictionary based on various scenarios including AppView fallback logic
    /// </summary>
    /// <param name="app_site">The application site name</param>
    /// <param name="template_name">The template name (can be appFile or placeholderName)</param>
    /// <param name="preprocessed_templates">Dictionary of preprocessed templates</param>
    /// <param name="app_view">The application view name (optional)</param>
    /// <param name="app_view_prefix">The application view prefix (optional, uses instance property if not provided)</param>
    /// <param name="use_app_view_fallback">Whether to apply AppView fallback logic</param>
    /// <returns>The template's original content if found, None otherwise</returns>
    fn get_template< 'a>(
        &self,
        app_site: &str,
        template_name: &str,
        preprocessed_templates: &'a HashMap<String, PreprocessedTemplate>,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
        use_app_view_fallback: bool,
    ) -> Option<&'a PreprocessedTemplate> {
        if preprocessed_templates.is_empty() {
            return None;
        }

        let view_prefix = app_view_prefix.unwrap_or(&self.app_view_prefix);
        
        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if use_app_view_fallback {
            if let (Some(app_view), Some(view_prefix)) = (app_view, Some(view_prefix)) {
                if !view_prefix.is_empty() && template_name.to_lowercase().contains(&view_prefix.to_lowercase()) {
                    let app_key = CommonUtil::replace_case_insensitive(template_name, view_prefix, app_view);
                    let fallback_template_key = format!("{}_{}", app_site.to_lowercase(), app_key.to_lowercase());
                    if let Some(fallback_template) = preprocessed_templates.get(&fallback_template_key) {
                        return Some(fallback_template);
                    }
                }
            }
        }

        // SECOND: If no AppView-specific template found, try primary template
        let primary_template_key = format!("{}_{}", app_site.to_lowercase(), template_name.to_lowercase());
        if let Some(primary_template) = preprocessed_templates.get(&primary_template_key) {
            return Some(primary_template);
        }

        None
    }

    /// <summary>
    /// Applies all replacement mappings from all templates - NO processing logic, only direct replacements
    /// </summary>
    fn apply_template_replacements(
        &self,
        content: &str,
        preprocessed_templates: &HashMap<String, PreprocessedTemplate>,
        enable_json_processing: bool,
        app_view: Option<&str>,
        main_template: &PreprocessedTemplate,
    ) -> String {
        Logger::debug(&format!("Starting ApplyTemplateReplacements, initial size: {}", content.len()), Some("EnginePreProcess"));

        let mut result = content.to_string();

        let mut previous;
        let max_passes = 10;
        let mut current_pass = 0;

        loop {
            previous = result.clone();
            current_pass += 1;

            Logger::debug(&format!("Replacement pass {}, current size: {}", current_pass, result.len()), Some("EnginePreProcess"));

            let mut json_placeholder_count = 0;
            let mut slotted_count = 0;
            let mut simple_count = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template (to avoid overwriting component content)
            if current_pass == 1 && enable_json_processing {
                for mapping in main_template.replacement_mappings.iter().filter(|m| matches!(m.r#type, ReplacementType::JsonPlaceholder)) {
                    if result.contains(&mapping.original_text) {
                        Logger::debug(&format!("Applying main template JSON placeholder: {} -> {}", mapping.original_text, mapping.replacement_text), Some("EnginePreProcess"));
                        result = result.replace(&mapping.original_text, &mapping.replacement_text);
                        json_placeholder_count += 1;
                    }
                }
            }

            for template in preprocessed_templates.values() {
                for mapping in template.replacement_mappings.iter().filter(|m| matches!(m.r#type, ReplacementType::SlottedTemplate)) {
                    if result.contains(&mapping.original_text) {
                        let preview_len = std::cmp::min(50, mapping.original_text.len());
                        let preview = &mapping.original_text[..preview_len];
                        Logger::debug(&format!("Applying slotted template: {}... -> {} chars", preview, mapping.replacement_text.len()), Some("EnginePreProcess"));
                        result = result.replace(&mapping.original_text, &mapping.replacement_text);
                        slotted_count += 1;
                    }
                }

                for mapping in template.replacement_mappings.iter().filter(|m| matches!(m.r#type, ReplacementType::SimpleTemplate)) {
                    if result.contains(&mapping.original_text) {
                        let replacement_text = self.apply_app_view_logic_to_replacement(&mapping.original_text, &mapping.replacement_text, preprocessed_templates, app_view);
                        let preview_len = std::cmp::min(200, replacement_text.len());
                        let preview = &replacement_text[..preview_len];
                        Logger::debug(&format!("Applying simple template: {} -> replacement text (first 200 chars): {}", mapping.original_text, preview), Some("EnginePreProcess"));
                        result = result.replace(&mapping.original_text, &replacement_text);
                        simple_count += 1;
                    }
                }
            }

            Logger::debug(&format!("Pass {} applied: {} main JSON placeholders, {} slotted, {} simple", current_pass, json_placeholder_count, slotted_count, simple_count), Some("EnginePreProcess"));

            if result == previous || current_pass >= max_passes {
                break;
            }
        }

        Logger::debug(&format!("Replacement complete after {} passes, final size: {}", current_pass, result.len()), Some("EnginePreProcess"));
        result
    }

    /// <summary>
    /// Applies AppView fallback logic to template replacement text using the centralized get_template method
    /// </summary>
    fn apply_app_view_logic_to_replacement(
        &self,
        original_text: &str,
        replacement_text: &str,
        preprocessed_templates: &HashMap<String, PreprocessedTemplate>,
        app_view: Option<&str>,
    ) -> String {
        // If no appView context, use the default replacement text (which already has JSON values baked in)
        if app_view.is_none() || app_view == Some("") {
            return replacement_text.to_string();
        }

        // Extract placeholder name from {{PlaceholderName}} format
        let placeholder_name = Self::extract_placeholder_name(original_text);
        if placeholder_name.is_empty() {
            return replacement_text.to_string();
        }

        // First get the appSite from the template key pattern
        let sample_key = match preprocessed_templates.keys().next() {
            Some(key) => key,
            None => return replacement_text.to_string(),
        };

        let parts: Vec<&str> = sample_key.split('_').collect();
        if parts.len() < 2 {
            return replacement_text.to_string();
        }

        let app_site = parts[0]; // Extract appSite from the key pattern

        // Use GetTemplate to find the AppView-specific template variant
        let app_view_template = self.get_template(app_site, &placeholder_name, preprocessed_templates, app_view, Some(&self.app_view_prefix), true);

        // If no AppView-specific template found, use the default replacement text
        if app_view_template.is_none() {
            return replacement_text.to_string();
        }

        // Return the AppView-specific template's original content (which already has JSON baked in by LoaderPreProcess)
        app_view_template.unwrap().original_content.clone()
    }

    /// <summary>
    /// Extracts placeholder name from {{PlaceholderName}} format
    /// </summary>
    fn extract_placeholder_name(original_text: &str) -> String {
        if !original_text.starts_with("{{") || !original_text.ends_with("}}") {
            return String::new();
        }
        
        original_text[2..original_text.len() - 2].trim().to_string()
    }
}

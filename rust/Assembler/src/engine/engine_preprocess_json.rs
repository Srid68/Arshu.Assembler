use crate::common::common_util::CommonUtil;
use crate::interface::ILoaderJson;
use crate::loader::loader_preprocess_json::LoaderPreProcessJson;
use crate::model::model_preprocess::{PreprocessedTemplate, ReplacementType};
use arshu::common::Logger;
use std::collections::HashMap;

/// PreProcess JSON template engine implementation that only does merging using preprocessed data structures with JsonObject
/// All parsing and JSON processing is done by LoaderPreProcessJson, this engine only handles merging
/// Uses ILoader<PreprocessedTemplate> for consistency with NormalJson architecture
pub struct EnginePreProcessJson {
    app_view_prefix: String,
}

impl EnginePreProcessJson {
    pub fn new(prefix: String) -> Self {
        Self {
            app_view_prefix: prefix,
        }
    }

    pub fn app_view_prefix(&self) -> &str {
        &self.app_view_prefix
    }

    pub fn set_app_view_prefix(&mut self, prefix: String) {
        self.app_view_prefix = prefix;
    }

    /// Merges templates using preprocessed data structures with JsonObject via ILoader
    /// This method only does merging using preprocessed data structures - no loading or parsing
    ///
    /// # Arguments
    /// * `app_site` - The application site name for template key generation
    /// * `app_file` - The application file name
    /// * `app_view` - The application view name (optional)
    /// * `loader` - ILoader providing preprocessed templates
    /// * `enable_json_processing` - Whether to enable JSON data processing
    ///
    /// # Returns
    /// HTML with placeholders replaced using preprocessed structures
    pub fn merge_templates(
        &self,
        app_site: &str,
        app_file: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderJson<PreprocessedTemplate>,
        enable_json_processing: bool,
    ) -> String {
        let app_view_str = app_view.unwrap_or("null");
        Logger::debug(
            &format!(
                "MergeTemplates called: appSite={}, appFile={}, appView={}, enableJson={}",
                app_site, app_file, app_view_str, enable_json_processing
            ),
            Some("EnginePreProcessJson"),
        );

        // Get all templates (needed for PreProcess engine to apply replacement mappings from all templates)
        let loader_preprocess = match Self::downcast_loader(loader) {
            Some(l) => l,
            None => {
                Logger::warn(
                    "Loader is not LoaderPreProcessJson",
                    Some("EnginePreProcessJson"),
                );
                return String::new();
            }
        };

        let preprocessed_templates = loader_preprocess.all_templates();

        if preprocessed_templates.is_empty() {
            Logger::warn(
                "No preprocessed templates available",
                Some("EnginePreProcessJson"),
            );
            return String::new();
        }

        Logger::debug(
            &format!(
                "Using {} preprocessed templates",
                preprocessed_templates.len()
            ),
            Some("EnginePreProcessJson"),
        );

        // Use ILoader to retrieve the main template
        let main_preprocessed =
            loader.get_template_html(app_site, app_file, app_view, Some(&self.app_view_prefix));
        let main_preprocessed = match main_preprocessed {
            Some(t) => t,
            None => {
                Logger::warn(
                    &format!(
                        "Main template not found for appSite={}, appFile={}",
                        app_site, app_file
                    ),
                    Some("EnginePreProcessJson"),
                );
                return String::new();
            }
        };

        Logger::debug(
            &format!(
                "Main template found, original size: {}",
                main_preprocessed.original_content.len()
            ),
            Some("EnginePreProcessJson"),
        );

        // Start with original content
        let mut content_html = main_preprocessed.original_content.clone();

        // Merge JSON into main template first using loader's centralized method
        if enable_json_processing {
            content_html = loader.merge_html_with_json(&content_html, app_site, app_file);
            Logger::debug(
                &format!(
                    "After main template JSON merge: {} chars",
                    content_html.len()
                ),
                Some("EnginePreProcessJson"),
            );
        }

        // Apply ALL replacement mappings from ALL templates
        content_html = self.apply_template_replacements(
            &content_html,
            preprocessed_templates,
            enable_json_processing,
            app_view,
            Some(&main_preprocessed),
            loader,
            app_site,
        );

        Logger::debug(
            &format!(
                "MergeTemplates complete: output size={}",
                content_html.len()
            ),
            Some("EnginePreProcessJson"),
        );

        content_html
    }

    /// Retrieves a template from the preprocessed templates dictionary based on various scenarios including AppView fallback logic
    #[allow(dead_code)]
    fn get_template(
        &self,
        app_site: &str,
        template_name: &str,
        preprocessed_templates: &HashMap<String, PreprocessedTemplate>,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
        use_app_view_fallback: bool,
    ) -> Option<PreprocessedTemplate> {
        if preprocessed_templates.is_empty() {
            return None;
        }

        let view_prefix = app_view_prefix.unwrap_or(&self.app_view_prefix);

        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if use_app_view_fallback {
            if let Some(view) = app_view {
                if !view_prefix.is_empty() {
                    if template_name
                        .to_lowercase()
                        .contains(&view_prefix.to_lowercase())
                    {
                        let app_key =
                            CommonUtil::replace_case_insensitive(template_name, view_prefix, view);
                        let fallback_template_key =
                            format!("{}_{}", app_site.to_lowercase(), app_key.to_lowercase());
                        if let Some(fallback_template) =
                            preprocessed_templates.get(&fallback_template_key)
                        {
                            return Some(fallback_template.clone());
                        }
                    }
                }
            }
        }

        // SECOND: If no AppView-specific template found, try primary template
        let primary_template_key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );
        if let Some(primary_template) = preprocessed_templates.get(&primary_template_key) {
            return Some(primary_template.clone());
        }

        None
    }

    // Apply PreProcess Structure

    /// Applies all replacement mappings from all templates with JSON merging done by engine
    fn apply_template_replacements(
        &self,
        content: &str,
        preprocessed_templates: &HashMap<String, PreprocessedTemplate>,
        enable_json_processing: bool,
        app_view: Option<&str>,
        main_template: Option<&PreprocessedTemplate>,
        loader: &dyn ILoaderJson<PreprocessedTemplate>,
        app_site: &str,
    ) -> String {
        let mut result = content.to_string();

        Logger::debug(
            &format!(
                "Starting ApplyTemplateReplacements, initial size: {}",
                content.len()
            ),
            Some("EnginePreProcessJson"),
        );

        // Apply replacement mappings from all templates in multiple passes until no more changes
        let max_passes = 10;
        let mut current_pass = 0;

        loop {
            let previous = result.clone();
            current_pass += 1;

            Logger::debug(
                &format!(
                    "Replacement pass {}, current size: {}",
                    current_pass,
                    result.len()
                ),
                Some("EnginePreProcessJson"),
            );

            let mut slotted_count = 0;
            let mut simple_count = 0;
            let mut json_placeholder_count = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template
            if let Some(main) = main_template {
                if current_pass == 1 && enable_json_processing {
                    for mapping in &main.replacement_mappings {
                        if mapping.r#type != ReplacementType::JsonPlaceholder {
                            continue;
                        }

                        if result.contains(&mapping.original_text) {
                            Logger::debug(
                                &format!(
                                    "Applying main template JSON placeholder: {} -> {}",
                                    mapping.original_text, mapping.replacement_text
                                ),
                                Some("EnginePreProcessJson"),
                            );
                            result =
                                result.replace(&mapping.original_text, &mapping.replacement_text);
                            json_placeholder_count += 1;
                        }
                    }
                }
            }

            // Apply replacement mappings from all templates
            for template in preprocessed_templates.values() {
                // Apply slotted template mappings - engine retrieves and merges JSON
                for mapping in &template.replacement_mappings {
                    if mapping.r#type != ReplacementType::SlottedTemplate {
                        continue;
                    }

                    if result.contains(&mapping.original_text) {
                        // Get replacement text and merge JSON using TargetTemplateName
                        let mut replacement_text = mapping.replacement_text.clone();

                        // Merge JSON using TargetTemplateName if available
                        if enable_json_processing {
                            if let Some(ref target_template_name) = mapping.target_template_name {
                                replacement_text = loader.merge_html_with_json(
                                    &replacement_text,
                                    app_site,
                                    target_template_name,
                                );
                                Logger::debug(
                                    &format!(
                                        "After merging JSON for slotted template {}: {} chars",
                                        target_template_name,
                                        replacement_text.len()
                                    ),
                                    Some("EnginePreProcessJson"),
                                );
                            }
                        }

                        Logger::debug(
                            &format!(
                                "Applying slotted template: {}... -> {} chars",
                                &mapping.original_text
                                    [..std::cmp::min(50, mapping.original_text.len())],
                                replacement_text.len()
                            ),
                            Some("EnginePreProcessJson"),
                        );
                        result = result.replace(&mapping.original_text, &replacement_text);

                        slotted_count += 1;
                    }
                }

                // Apply simple template mappings (components) - engine retrieves and merges JSON
                for mapping in &template.replacement_mappings {
                    if mapping.r#type != ReplacementType::SimpleTemplate {
                        continue;
                    }

                    if result.contains(&mapping.original_text) {
                        // Get replacement text and merge JSON using TargetTemplateName
                        let mut replacement_text = mapping.replacement_text.clone();

                        // Handle AppView logic if needed
                        if app_view.is_some() {
                            if let Some(ref target_template_name) = mapping.target_template_name {
                                let app_view_template = self.get_template(
                                    app_site,
                                    target_template_name,
                                    preprocessed_templates,
                                    app_view,
                                    Some(&self.app_view_prefix),
                                    true,
                                );
                                if let Some(app_view_tmpl) = app_view_template {
                                    replacement_text = app_view_tmpl.original_content.clone();
                                }
                            }
                        }

                        // Merge JSON using TargetTemplateName
                        if enable_json_processing {
                            if let Some(ref target_template_name) = mapping.target_template_name {
                                replacement_text = loader.merge_html_with_json(
                                    &replacement_text,
                                    app_site,
                                    target_template_name,
                                );
                                Logger::debug(
                                    &format!(
                                        "After merging JSON for simple template {}: {} chars",
                                        target_template_name,
                                        replacement_text.len()
                                    ),
                                    Some("EnginePreProcessJson"),
                                );
                            }
                        }

                        Logger::debug(
                            &format!(
                                "Applying simple template: {} -> {} chars",
                                mapping.original_text,
                                replacement_text.len()
                            ),
                            Some("EnginePreProcessJson"),
                        );
                        result = result.replace(&mapping.original_text, &replacement_text);
                        simple_count += 1;
                    }
                }
            }

            Logger::debug(
                &format!(
                    "Pass {} applied: {} main JSON placeholders, {} slotted, {} simple",
                    current_pass, json_placeholder_count, slotted_count, simple_count
                ),
                Some("EnginePreProcessJson"),
            );

            if result == previous || current_pass >= max_passes {
                break;
            }
        }

        Logger::debug(
            &format!(
                "Replacement complete after {} passes, final size: {}",
                current_pass,
                result.len()
            ),
            Some("EnginePreProcessJson"),
        );

        result
    }

    /// Helper function to downcast ILoader to LoaderPreProcessJson
    fn downcast_loader(
        loader: &dyn ILoaderJson<PreprocessedTemplate>,
    ) -> Option<&LoaderPreProcessJson> {
        // In Rust, we can't directly downcast trait objects without unsafe code
        // This is a limitation - we'll need to use a workaround
        // For now, we'll use an unsafe approach (this should be improved in production code)
        unsafe {
            let loader_ptr = loader as *const dyn ILoaderJson<PreprocessedTemplate>;
            let concrete_ptr = loader_ptr as *const LoaderPreProcessJson;
            Some(&*concrete_ptr)
        }
    }
}

use crate::interface::i_loader_preprocess::ILoaderPreProcess;
use super::json_merge_util::JsonMergeUtil;
use crate::app::json::{JsonObject, JsonValue};
use crate::common::common_util::CommonUtil;
use crate::model::model_preprocess::{
    JsonPlaceholder, PreprocessedSiteTemplates, PreprocessedTemplate, ReplacementMapping,
    ReplacementType, SlotPlaceholder, SlottedTemplate, TemplatePlaceholder,
};
use arshu::common::Logger;
use lazy_static::lazy_static;
use std::collections::{HashMap, HashSet};
use std::fs;
use std::path::Path;
use std::sync::Mutex;
use walkdir;

/// <summary>
/// Handles loading and caching of HTML templates from the file system for PreProcess engine
/// </summary>
pub struct LoaderPreProcess {
    preprocessed_templates: PreprocessedSiteTemplates,
    search_app_sites: String,
    _app_site: String,
}

lazy_static! {
    static ref PREPROCESSED_TEMPLATES_CACHE: Mutex<HashMap<String, PreprocessedSiteTemplates>> =
        Mutex::new(HashMap::new());
}

impl LoaderPreProcess {
    /// Creates a new LoaderPreProcess instance
    pub fn new(root_dir_path: &str, app_site: &str, search_app_sites: &str) -> Self {
        Logger::debug(
            &format!("LoaderPreProcess::new called for appSite: {}, searchAppSites: {}", app_site, search_app_sites),
            Some("LoaderPreProcess"),
        );

        let preprocessed_templates = Self::load_process_get_template_files(root_dir_path, app_site, search_app_sites);

        Logger::debug(
            &format!("Loaded {} preprocessed templates for {}", preprocessed_templates.templates.len(), app_site),
            Some("LoaderPreProcess"),
        );

        Self {
            preprocessed_templates,
            search_app_sites: search_app_sites.to_string(),
            _app_site: app_site.to_string(),
        }
    }

    pub fn load_process_get_template_files(
        root_dir_path: &str,
        app_site: &str,
        search_app_sites: &str,
    ) -> PreprocessedSiteTemplates {
        Logger::debug(
            &format!(
                "LoadProcessGetTemplateFiles called for appSite: {}, searchAppSites: {}",
                app_site, search_app_sites
            ),
            Some("LoaderPreProcess"),
        );

        let cache_key = format!(
            "{}|{}|{}",
            Path::new(root_dir_path)
                .parent()
                .unwrap_or(Path::new(""))
                .display(),
            app_site,
            search_app_sites
        );

        {
            let cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
            if let Some(cached) = cache.get(&cache_key) {
                Logger::debug(
                    &format!(
                        "Returning cached templates for {} ({} templates)",
                        app_site,
                        cached.templates.len()
                    ),
                    Some("LoaderPreProcess"),
                );
                return cached.clone();
            }
        }

        // Load templates from primary appSite
        let mut result = Self::load_templates_from_single_app_site(root_dir_path, app_site);

        // Load templates from searchAppSites for fallback
        if !search_app_sites.is_empty() {
            let search_app_sites_array: Vec<&str> = search_app_sites.split(',').collect();
            for search_app_site in search_app_sites_array {
                let search_app_site = search_app_site.trim();
                if search_app_site.is_empty() {
                    continue;
                }

                let search_result =
                    Self::load_templates_from_single_app_site(root_dir_path, search_app_site);

                // Merge templates (primary appSite takes precedence)
                for (key, value) in search_result.templates {
                    if !result.templates.contains_key(&key) {
                        result.templates.insert(key.clone(), value);
                        if let Some(raw) = search_result.raw_templates.get(&key) {
                            result.raw_templates.insert(key.clone(), raw.clone());
                        }
                        result.template_keys.insert(key.clone());
                        Logger::debug(
                            &format!(
                                "Added fallback template '{}' from '{}'",
                                key, search_app_site
                            ),
                            Some("LoaderPreProcess"),
                        );
                    }
                }
            }
        }

        // CRITICAL: Create ALL replacement mappings after all templates are loaded
        // This ensures PreProcess engine does ONLY merging, no processing logic
        Self::create_all_replacement_mappings_for_site(&mut result, app_site);

        Logger::debug(
            &format!("Created all replacement mappings for {}", app_site),
            Some("LoaderPreProcess"),
        );

        // Update convenience flags for all templates after processing
        for template in result.templates.values_mut() {
            template.update_flags();
        }

        let mut cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
        cache.insert(cache_key, result.clone());
        result
    }

    /// <summary>
    /// Loads templates from a single AppSite without caching or fallback logic
    /// </summary>
    fn load_templates_from_single_app_site(
        root_dir_path: &str,
        app_site: &str,
    ) -> PreprocessedSiteTemplates {
        let mut result = PreprocessedSiteTemplates {
            site_name: app_site.to_string(),
            templates: HashMap::new(),
            raw_templates: HashMap::new(),
            template_keys: HashSet::new(),
        };

        let app_sites_path = format!("{}/AppSites/{}", root_dir_path, app_site);

        if !Path::new(&app_sites_path).exists() {
            Logger::warn(
                &format!("AppSites directory not found: {}", app_sites_path),
                Some("LoaderPreProcess"),
            );
            return result;
        }

        Logger::debug(
            &format!("Loading templates from: {}", app_sites_path),
            Some("LoaderPreProcess"),
        );

        for entry in walkdir::WalkDir::new(&app_sites_path)
            .into_iter()
            .filter_map(|e| e.ok())
        {
            let path = entry.path();
            if path.extension().map(|ext| ext == "html").unwrap_or(false) {
                let file_name = path.file_stem().unwrap().to_string_lossy().to_string();
                let key = format!("{}_{}", app_site.to_lowercase(), file_name.to_lowercase());
                let content = CommonUtil::normalize_file_content(
                    &fs::read_to_string(path).unwrap_or_default(),
                );

                Logger::debug(
                    &format!("Loading template: {} (size: {})", key, content.len()),
                    Some("LoaderPreProcess"),
                );

                // Find JSON file case-insensitively
                let json_file = path.with_extension("json");
                let json_content = if json_file.exists() {
                    let json_str = CommonUtil::normalize_file_content(
                        &fs::read_to_string(json_file).unwrap_or_default(),
                    );
                    Logger::debug(
                        &format!("Found JSON file for {} (size: {})", key, json_str.len()),
                        Some("LoaderPreProcess"),
                    );
                    Some(json_str)
                } else {
                    // Try case-insensitive search in the same directory
                    if let Some(parent_dir) = path.parent() {
                        let base_name = path.file_stem().unwrap().to_string_lossy();
                        if let Ok(entries) = fs::read_dir(parent_dir) {
                            let matching_json = entries.filter_map(|e| e.ok()).find(|entry| {
                                let file_path = entry.path();
                                file_path
                                    .extension()
                                    .map(|ext| ext == "json")
                                    .unwrap_or(false)
                                    && file_path
                                        .file_stem()
                                        .and_then(|stem| {
                                            Some(
                                                stem.to_string_lossy()
                                                    .eq_ignore_ascii_case(&base_name),
                                            )
                                        })
                                        .unwrap_or(false)
                            });
                            if let Some(json_entry) = matching_json {
                                if let Ok(json_str) = fs::read_to_string(json_entry.path()) {
                                    let json_str = CommonUtil::normalize_file_content(&json_str);
                                    Logger::debug(
                                        &format!(
                                            "Found JSON file (case-insensitive) for {} (size: {})",
                                            key,
                                            json_str.len()
                                        ),
                                        Some("LoaderPreProcess"),
                                    );
                                    Some(json_str)
                                } else {
                                    None
                                }
                            } else {
                                None
                            }
                        } else {
                            None
                        }
                    } else {
                        None
                    }
                };

                result.raw_templates.insert(key.clone(), content.clone());
                result.template_keys.insert(key.clone());

                let preprocessed =
                    Self::preprocess_template(&content, json_content.as_deref(), &key);
                Logger::debug(
                    &format!(
                        "Preprocessed {}: {} replacements, {} slotted, {} placeholders",
                        key,
                        preprocessed.replacement_mappings.len(),
                        preprocessed.slotted_templates.len(),
                        preprocessed.placeholders.len()
                    ),
                    Some("LoaderPreProcess"),
                );
                result.templates.insert(key, preprocessed);
            }
        }

        Logger::debug(
            &format!(
                "Loaded {} templates for {}",
                result.templates.len(),
                app_site
            ),
            Some("LoaderPreProcess"),
        );
        result
    }

    pub fn preprocess_json_data(json_content: &str) -> Option<crate::app::JsonObject> {
        use crate::app::JsonConverter;
        Some(JsonConverter::parse_json_string(json_content))
    }

    pub fn clear_cache() {
        let mut preprocessed_cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
        preprocessed_cache.clear();
    }

}

impl ILoaderPreProcess for LoaderPreProcess {
    fn search_app_sites(&self) -> &str {
        &self.search_app_sites
    }

    fn get_template_html(
        &self,
        app_site: &str,
        template_name: &str,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
    ) -> Option<PreprocessedTemplate> {
        // Use stored preprocessed templates directly
        // Try AppView fallback first if provided
        if let (Some(view), Some(prefix)) = (app_view, app_view_prefix) {
            if template_name
                .to_lowercase()
                .contains(&prefix.to_lowercase())
            {
                let app_key = CommonUtil::replace_case_insensitive(template_name, prefix, view);
                let fallback_key =
                    format!("{}_{}", app_site.to_lowercase(), app_key.to_lowercase());

                if let Some(template) = self.preprocessed_templates.templates.get(&fallback_key) {
                    return Some(template.clone());
                }
            }
        }

        // Try primary template key
        let primary_key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );

        if let Some(template) = self.preprocessed_templates.templates.get(&primary_key) {
            return Some(template.clone());
        }

        // FALLBACK: Search in searchAppSites
        if !self.search_app_sites.is_empty() {
            let search_app_sites_array: Vec<&str> = self.search_app_sites.split(',').collect();
            for search_app_site in search_app_sites_array {
                let search_app_site = search_app_site.trim();
                if search_app_site.is_empty() {
                    continue;
                }

                let search_key = format!(
                    "{}_{}",
                    search_app_site.to_lowercase(),
                    template_name.to_lowercase()
                );

                if let Some(template) = self.preprocessed_templates.templates.get(&search_key) {
                    return Some(template.clone());
                }
            }
        }

        None
    }

    fn has_template(&self, app_site: &str, template_name: &str) -> bool {
        // Use stored preprocessed templates directly
        let key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );

        if self.preprocessed_templates.templates.contains_key(&key) {
            return true;
        }

        // FALLBACK: Search in searchAppSites
        if !self.search_app_sites.is_empty() {
            let search_app_sites_array: Vec<&str> = self.search_app_sites.split(',').collect();
            for search_app_site in search_app_sites_array {
                let search_app_site = search_app_site.trim();
                if search_app_site.is_empty() {
                    continue;
                }

                let search_key = format!(
                    "{}_{}",
                    search_app_site.to_lowercase(),
                    template_name.to_lowercase()
                );

                if self.preprocessed_templates.templates.contains_key(&search_key) {
                    return true;
                }
            }
        }

        false
    }

    fn merge_html_with_json(&self, html: &str, app_site: &str, template_name: &str) -> String {
        if html.is_empty() {
            return html.to_string();
        }

        // Get the preprocessed template which has JSON with inheritance already resolved
        let template = self.get_template_html(app_site, template_name, None, None);

        if template.is_none() || template.as_ref().unwrap().json_data.is_none() {
            Logger::debug(
                &format!("No JSON data found for {}, returning original HTML", template_name),
                Some("LoaderPreProcess"),
            );
            return html.to_string();
        }

        Logger::debug(
            &format!("Merging HTML with JSON for {}", template_name),
            Some("LoaderPreProcess"),
        );

        let json_data = template.unwrap().json_data.unwrap();
        JsonMergeUtil::merge_template_with_json(html, &json_data)
    }

    fn apply_all_replacement_mappings(
        &self,
        content: &str,
        app_site: &str,
        main_template: Option<&PreprocessedTemplate>,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
        enable_json_processing: bool,
    ) -> String {
        Logger::debug(
            &format!(
                "Starting ApplyAllReplacementMappings, initial size: {}",
                content.len()
            ),
            Some("LoaderPreProcess"),
        );

        let mut result = content.to_string();
        let mut previous;
        let max_passes = 10;
        let mut current_pass = 0;

        loop {
            previous = result.clone();
            current_pass += 1;

            Logger::debug(
                &format!(
                    "Replacement pass {}, current size: {}",
                    current_pass,
                    result.len()
                ),
                Some("LoaderPreProcess"),
            );

            let mut json_placeholder_count = 0;
            let mut slotted_count = 0;
            let mut simple_count = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template (to avoid overwriting component content)
            if current_pass == 1 && enable_json_processing {
                if let Some(main) = main_template {
                    for mapping in main
                        .replacement_mappings
                        .iter()
                        .filter(|m| matches!(m.r#type, ReplacementType::JsonPlaceholder))
                    {
                        if result.contains(&mapping.original_text) {
                            Logger::debug(
                                &format!(
                                    "Applying main template JSON placeholder (original size: {}, replacement size: {})",
                                    mapping.original_text.len(),
                                    mapping.replacement_text.len()
                                ),
                                Some("LoaderPreProcess"),
                            );
                            result = result.replace(&mapping.original_text, &mapping.replacement_text);
                            json_placeholder_count += 1;
                        }
                    }
                }
            }

            // SECOND: Apply slotted template replacements from all templates
            for template in self.preprocessed_templates.templates.values() {
                for mapping in template
                    .replacement_mappings
                    .iter()
                    .filter(|m| matches!(m.r#type, ReplacementType::SlottedTemplate))
                {
                    if result.contains(&mapping.original_text) {
                        Logger::debug(
                            &format!(
                                "Applying slotted template (original size: {}, replacement size: {})",
                                mapping.original_text.len(),
                                mapping.replacement_text.len()
                            ),
                            Some("LoaderPreProcess"),
                        );
                        result = result.replace(&mapping.original_text, &mapping.replacement_text);
                        slotted_count += 1;
                    }
                }

                // THIRD: Apply simple template replacements with AppView logic
                for mapping in template
                    .replacement_mappings
                    .iter()
                    .filter(|m| matches!(m.r#type, ReplacementType::SimpleTemplate))
                {
                    if result.contains(&mapping.original_text) {
                        let replacement_text = self.apply_app_view_logic_to_replacement(
                            &mapping.original_text,
                            &mapping.replacement_text,
                            app_site,
                            app_view,
                            app_view_prefix,
                        );
                        Logger::debug(
                            &format!(
                                "Applying simple template: {} (replacement size: {})",
                                mapping.original_text,
                                replacement_text.len()
                            ),
                            Some("LoaderPreProcess"),
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
                Some("LoaderPreProcess"),
            );

            if result == previous || current_pass >= max_passes {
                Logger::debug(
                    &format!("Completed after {} passes, final size: {}", current_pass, result.len()),
                    Some("LoaderPreProcess"),
                );
                break;
            }
        }

        result
    }

    fn clear_cache(&self) {
        Self::clear_cache();
    }
}

impl LoaderPreProcess {
    fn preprocess_template(
        content: &str,
        json_content: Option<&str>,
        _template_key: &str,
    ) -> PreprocessedTemplate {
        let mut template = PreprocessedTemplate {
            original_content: content.to_string(),
            placeholders: Vec::new(),
            slotted_templates: Vec::new(),
            json_data: None,
            json_placeholders: Vec::new(),
            replacement_mappings: Vec::new(),
            has_placeholders_flag: false,
            has_slotted_templates_flag: false,
            has_json_data_flag: false,
            has_json_placeholders_flag: false,
            has_replacement_mappings_flag: false,
            requires_processing_flag: false,
        };

        if content.is_empty() {
            return template;
        }

        if let Some(json) = json_content {
            template.json_data = Self::preprocess_json_data(json);
        }

        Self::parse_slotted_templates(content, &mut template);
        Self::parse_placeholder_templates(content, &mut template);

        if template.has_json_data() {
            Self::preprocess_json_templates(&mut template);
        }

        template.update_flags();
        template
    }

    fn create_all_replacement_mappings_for_site(
        site_templates: &mut PreprocessedSiteTemplates,
        app_site: &str,
    ) {
        let template_keys: Vec<String> = site_templates.templates.keys().cloned().collect();

        Logger::debug(
            &format!(
                "Creating replacement mappings for {} - Phase 0: JSON inheritance",
                app_site
            ),
            Some("LoaderPreProcess"),
        );
        let parent_map = Self::build_parent_map_for_preprocess(site_templates, app_site);
        Self::resolve_json_inheritance_for_all_templates(site_templates, &parent_map);
        Self::recreate_json_placeholder_mappings_after_inheritance(site_templates);

        Logger::debug(
            &format!(
                "Creating replacement mappings for {} - Phase 1: JSON arrays",
                app_site
            ),
            Some("LoaderPreProcess"),
        );
        for key in &template_keys {
            if let Some(template) = site_templates.templates.get_mut(key) {
                let content = template.original_content.clone();
                Self::create_json_array_replacement_mappings(template, &content);
            }
        }

        Logger::debug(
            &format!(
                "Creating replacement mappings for {} - Phase 2: Simple placeholders",
                app_site
            ),
            Some("LoaderPreProcess"),
        );
        let all_templates_snapshot = site_templates.templates.clone();
        for key in &template_keys {
            if let Some(template) = site_templates.templates.get_mut(key) {
                Self::create_placeholder_replacement_mappings(
                    template,
                    &all_templates_snapshot,
                    app_site,
                );
            }
        }

        Logger::debug(
            &format!(
                "Creating replacement mappings for {} - Phase 3: Slotted templates",
                app_site
            ),
            Some("LoaderPreProcess"),
        );
        let all_templates_snapshot = site_templates.templates.clone();
        for key in &template_keys {
            if let Some(template) = site_templates.templates.get_mut(key) {
                Self::create_slotted_template_replacement_mappings(
                    template,
                    &all_templates_snapshot,
                    app_site,
                );
            }
        }

        let total_mappings: usize = site_templates
            .templates
            .values()
            .map(|t| t.replacement_mappings.len())
            .sum();
        Logger::info(
            &format!(
                "Total replacement mappings created for {}: {}",
                app_site, total_mappings
            ),
            Some("LoaderPreProcess"),
        );
    }

    /// Apply AppView logic to replacement text
    fn apply_app_view_logic_to_replacement(
        &self,
        original_text: &str,
        replacement_text: &str,
        app_site: &str,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
    ) -> String {
        // Extract placeholder name from {{PlaceholderName}}
        let placeholder_name = original_text.trim_start_matches("{{").trim_end_matches("}}").trim();

        // Try to get AppView-specific version of the template
        if let Some(view_prefix) = app_view_prefix {
            if let Some(view) = app_view {
                if !view_prefix.is_empty() && placeholder_name.to_lowercase().contains(&view_prefix.to_lowercase()) {
                    // Try to get the AppView-specific template
                    let app_view_key = crate::common::common_util::CommonUtil::replace_case_insensitive(
                        placeholder_name,
                        view_prefix,
                        view,
                    );
                    let template_key = format!("{}_{}", app_site.to_lowercase(), app_view_key.to_lowercase());

                    if let Some(view_template) = self.preprocessed_templates.templates.get(&template_key) {
                        Logger::debug(
                            &format!("Using AppView-specific template: {}", template_key),
                            Some("LoaderPreProcess"),
                        );
                        return view_template.original_content.clone();
                    }
                }
            }
        }

        replacement_text.to_string()
    }

    fn create_placeholder_replacement_mappings(
        template: &mut PreprocessedTemplate,
        all_templates: &HashMap<String, PreprocessedTemplate>,
        app_site: &str,
    ) {
        if !template.has_placeholders() {
            return;
        }

        for placeholder in &template.placeholders {
            // FIRST: Try current appSite
            let target_template_key =
                format!("{}_{}", app_site.to_lowercase(), placeholder.template_key);
            let mut target_template: Option<&PreprocessedTemplate> = None;

            if let Some(template_ref) = all_templates.get(&target_template_key) {
                target_template = Some(template_ref);
            } else {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                let search_key = format!("_{}", placeholder.template_key);
                for (key, value) in all_templates {
                    if key.to_lowercase().ends_with(&search_key.to_lowercase()) {
                        target_template = Some(value);
                        Logger::debug(
                            &format!(
                                "Template '{}' not found as '{}', using fallback from '{}'",
                                placeholder.template_key, target_template_key, key
                            ),
                            Some("LoaderPreProcess"),
                        );
                        break;
                    }
                }
            }

            if let Some(target_template) = target_template {
                // Start with the target template's original content
                let mut processed_template = target_template.original_content.clone();

                // CRITICAL FIX: Apply the target template's JSON placeholder replacements
                // This ensures nested components use their own JSON context (e.g., header.json for Header component)
                let json_mappings: Vec<&ReplacementMapping> = target_template
                    .replacement_mappings
                    .iter()
                    .filter(|m| matches!(m.r#type, ReplacementType::JsonPlaceholder))
                    .collect();
                Logger::debug(
                    &format!(
                        "Applying {} JSON mappings to {}",
                        json_mappings.len(),
                        target_template_key
                    ),
                    Some("LoaderPreProcess"),
                );
                Logger::debug(
                    &format!(
                        "Before replacements, template size: {}",
                        processed_template.len()
                    ),
                    Some("LoaderPreProcess"),
                );
                for json_mapping in json_mappings {
                    let before = processed_template.len();
                    Logger::debug(
                        &format!(
                            "  Replacing placeholder (original size: {}, replacement size: {})",
                            json_mapping.original_text.len(),
                            json_mapping.replacement_text.len()
                        ),
                        Some("LoaderPreProcess"),
                    );
                    processed_template = processed_template
                        .replace(&json_mapping.original_text, &json_mapping.replacement_text);
                    let after = processed_template.len();
                    Logger::debug(
                        &format!(
                            "    Size changed from {} to {} (diff: {})",
                            before,
                            after,
                            after as i32 - before as i32
                        ),
                        Some("LoaderPreProcess"),
                    );
                }
                Logger::debug(
                    &format!(
                        "After replacements, template size: {}",
                        processed_template.len()
                    ),
                    Some("LoaderPreProcess"),
                );

                template.replacement_mappings.push(ReplacementMapping {
                    start_index: placeholder.start_index,
                    end_index: placeholder.end_index,
                    original_text: placeholder.full_match.clone(),
                    replacement_text: processed_template,
                    r#type: ReplacementType::SimpleTemplate,
                    target_template_name: Some(placeholder.template_key.clone()),
                });
            }
        }
    }

    fn create_slotted_template_replacement_mappings(
        template: &mut PreprocessedTemplate,
        all_templates: &HashMap<String, PreprocessedTemplate>,
        app_site: &str,
    ) {
        if !template.has_slotted_templates() {
            return;
        }

        for slotted_template in &template.slotted_templates {
            let full_match = &slotted_template.full_match;
            // FIRST: Try current appSite
            let target_template_key = format!(
                "{}_{}",
                app_site.to_lowercase(),
                slotted_template.template_key
            );
            let mut target_template: Option<&PreprocessedTemplate> = None;

            if let Some(template_ref) = all_templates.get(&target_template_key) {
                target_template = Some(template_ref);
            } else {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                let search_key = format!("_{}", slotted_template.template_key);
                for (key, value) in all_templates {
                    if key.to_lowercase().ends_with(&search_key.to_lowercase()) {
                        target_template = Some(value);
                        Logger::debug(
                            &format!(
                                "Slotted template '{}' not found as '{}', using fallback from '{}'",
                                slotted_template.template_key, target_template_key, key
                            ),
                            Some("LoaderPreProcess"),
                        );
                        break;
                    }
                }
            }

            if let Some(target_template) = target_template {
                let mut processed_template = target_template.original_content.clone();

                for slot in &slotted_template.slots {
                    let processed_slot_content = Self::process_slot_content_for_replacement_mapping(
                        slot,
                        all_templates,
                        app_site,
                    );
                    processed_template =
                        processed_template.replace(&slot.slot_key, &processed_slot_content);
                }

                if slotted_template.slots.is_empty() {
                    let actual_inner_content = &slotted_template.inner_content;
                    if !actual_inner_content.trim().is_empty() {
                        let default_slot_key = "{{$HTMLPLACEHOLDER}}";
                        if processed_template.contains(default_slot_key) {
                            processed_template = processed_template
                                .replace(default_slot_key, actual_inner_content.trim());
                        }
                    }
                }

                processed_template =
                    CommonUtil::remove_remaining_slot_placeholders(&processed_template);

                template.replacement_mappings.push(ReplacementMapping {
                    start_index: slotted_template.start_index,
                    end_index: slotted_template.end_index,
                    original_text: full_match.clone(),
                    replacement_text: processed_template,
                    r#type: ReplacementType::SlottedTemplate,
                    target_template_name: Some(slotted_template.template_key.clone()),
                });
            }
        }
    }

    fn process_slot_content_for_replacement_mapping(
        slot: &SlotPlaceholder,
        all_templates: &HashMap<String, PreprocessedTemplate>,
        app_site: &str,
    ) -> String {
        let mut result = slot.content.clone();

        for nested_slotted_template in &slot.nested_slotted_templates {
            // FIRST: Try current appSite
            let target_template_key = format!(
                "{}_{}",
                app_site.to_lowercase(),
                nested_slotted_template.template_key
            );
            let mut target_template = all_templates.get(&target_template_key);

            if target_template.is_none() {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                let search_key = format!("_{}", nested_slotted_template.template_key);
                for (key, value) in all_templates.iter() {
                    if key.to_lowercase().ends_with(&search_key.to_lowercase()) {
                        target_template = Some(value);
                        Logger::debug(&format!("Nested slotted template '{}' not found as '{}', using fallback from '{}'",
                            nested_slotted_template.template_key, target_template_key, key), Some("LoaderPreProcess"));
                        break;
                    }
                }
            }

            if let Some(target_template) = target_template {
                let mut processed_template = target_template.original_content.clone();

                for nested_slot in &nested_slotted_template.slots {
                    let processed_nested_slot_content =
                        Self::process_slot_content_for_replacement_mapping(
                            nested_slot,
                            all_templates,
                            app_site,
                        );
                    processed_template = processed_template
                        .replace(&nested_slot.slot_key, &processed_nested_slot_content);
                }

                processed_template =
                    CommonUtil::remove_remaining_slot_placeholders(&processed_template);
                result = result.replace(&nested_slotted_template.full_match, &processed_template);
            }
        }

        for nested_placeholder in &slot.nested_placeholders {
            // FIRST: Try current appSite
            let target_template_key = format!(
                "{}_{}",
                app_site.to_lowercase(),
                nested_placeholder.template_key
            );
            let mut target_template = all_templates.get(&target_template_key);

            if target_template.is_none() {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                let search_key = format!("_{}", nested_placeholder.template_key);
                for (key, value) in all_templates.iter() {
                    if key.to_lowercase().ends_with(&search_key.to_lowercase()) {
                        target_template = Some(value);
                        Logger::debug(&format!("Nested placeholder '{}' not found as '{}', using fallback from '{}'",
                            nested_placeholder.template_key, target_template_key, key), Some("LoaderPreProcess"));
                        break;
                    }
                }
            }

            if let Some(target_template) = target_template {
                // Start with the target template's original content
                let mut processed_template = target_template.original_content.clone();

                // CRITICAL FIX: Apply the target template's JSON placeholder replacements
                // This ensures nested components use their own JSON context
                for json_mapping in target_template
                    .replacement_mappings
                    .iter()
                    .filter(|m| matches!(m.r#type, ReplacementType::JsonPlaceholder))
                {
                    processed_template = processed_template
                        .replace(&json_mapping.original_text, &json_mapping.replacement_text);
                }

                result = result.replace(&nested_placeholder.full_match, &processed_template);
            }
        }

        result
    }

    fn parse_slotted_templates(content: &str, template: &mut PreprocessedTemplate) {
        let mut search_pos = 0;
        while search_pos < content.len() {
            let open_start = match content[search_pos..].find("{{#") {
                Some(pos) => search_pos + pos,
                None => break,
            };
            let open_end = match content[open_start + 3..].find("}}") {
                Some(pos) => open_start + 3 + pos,
                None => break,
            };

            let template_name = content[open_start + 3..open_end].trim();
            if template_name.is_empty() || !CommonUtil::is_alphanumeric(template_name) {
                search_pos = open_start + 1;
                continue;
            }

            let close_tag = format!("{{{{/{}}}}}", template_name);
            let open_tag = format!("{{{{#{}}}}}", template_name);

            let close_start = match CommonUtil::find_matching_close_tag(
                content,
                open_end + 2,
                &open_tag,
                &close_tag,
            ) {
                Some(pos) => pos,
                None => {
                    search_pos = open_start + 1;
                    continue;
                }
            };

            let full_match = content[open_start..close_start + close_tag.len()].to_string();
            let inner_content = content[open_end + 2..close_start].to_string();

            let mut slots = Vec::new();
            Self::parse_slots(&inner_content, &mut slots);

            let slotted_template = SlottedTemplate {
                name: template_name.to_string(),
                start_index: open_start,
                end_index: close_start + close_tag.len(),
                full_match,
                inner_content,
                slots,
                template_key: template_name.to_lowercase(),
                json_data: None,
            };

            // Add ALL slotted template instances (no deduplication) - multiple instances of the same template are allowed
            template.slotted_templates.push(slotted_template);

            search_pos = close_start + close_tag.len();
        }
    }

    fn parse_slots(inner_content: &str, slots: &mut Vec<SlotPlaceholder>) {
        let mut search_pos = 0;
        while search_pos < inner_content.len() {
            let slot_start = match inner_content[search_pos..].find("{{@HTMLPLACEHOLDER") {
                Some(pos) => search_pos + pos,
                None => break,
            };

            let after_placeholder = slot_start + 18;
            let mut slot_num = String::new();
            let mut pos = after_placeholder;

            while pos < inner_content.len() {
                let remaining = &inner_content[pos..];
                if let Some(ch) = remaining.chars().next() {
                    if ch.is_ascii_digit() {
                        slot_num.push(ch);
                        pos += ch.len_utf8();
                    } else {
                        break;
                    }
                } else {
                    break;
                }
            }

            if pos + 1 >= inner_content.len() || &inner_content[pos..pos + 2] != "}}" {
                search_pos = slot_start + 1;
                continue;
            }

            let slot_open_end = pos + 2;

            let close_tag = if slot_num.is_empty() {
                "{{/HTMLPLACEHOLDER}}".to_string()
            } else {
                format!("{{{{/HTMLPLACEHOLDER{}}}}}", slot_num)
            };

            let open_tag = if slot_num.is_empty() {
                "{{@HTMLPLACEHOLDER}}".to_string()
            } else {
                format!("{{{{@HTMLPLACEHOLDER{}}}}}", slot_num)
            };

            let close_start = match CommonUtil::find_matching_close_tag(
                inner_content,
                slot_open_end,
                &open_tag,
                &close_tag,
            ) {
                Some(pos) => pos,
                None => {
                    search_pos = slot_start + 1;
                    continue;
                }
            };

            let slot_content = inner_content[slot_open_end..close_start].to_string();

            let slot_key = if slot_num.is_empty() {
                "{{$HTMLPLACEHOLDER}}".to_string()
            } else {
                format!("{{{{$HTMLPLACEHOLDER{}}}}}", slot_num)
            };

            let mut temp_template = PreprocessedTemplate {
                original_content: slot_content.clone(),
                placeholders: Vec::new(),
                slotted_templates: Vec::new(),
                json_data: None,
                json_placeholders: Vec::new(),
                replacement_mappings: Vec::new(),
                has_placeholders_flag: false,
                has_slotted_templates_flag: false,
                has_json_data_flag: false,
                has_json_placeholders_flag: false,
                has_replacement_mappings_flag: false,
                requires_processing_flag: false,
            };

            Self::parse_slotted_templates(&slot_content, &mut temp_template);
            let nested_slotted_templates = temp_template.slotted_templates.clone();

            Self::parse_placeholder_templates(&slot_content, &mut temp_template);
            let nested_placeholders = temp_template.placeholders.clone();

            let has_nested_placeholders = !nested_placeholders.is_empty();
            let has_nested_slotted_templates = !nested_slotted_templates.is_empty();
            let requires_nested_processing =
                has_nested_placeholders || has_nested_slotted_templates;

            let slot = SlotPlaceholder {
                number: slot_num,
                start_index: slot_start,
                end_index: close_start + close_tag.len(),
                content: slot_content,
                slot_key,
                open_tag,
                close_tag: close_tag.clone(),
                nested_slots: Vec::new(),
                nested_placeholders,
                nested_slotted_templates,
                has_nested_placeholders,
                has_nested_slotted_templates,
                requires_nested_processing,
            };

            slots.push(slot);
            search_pos = close_start + close_tag.len();
        }
    }

    fn parse_placeholder_templates(content: &str, template: &mut PreprocessedTemplate) {
        let mut search_pos = 0;
        while search_pos < content.len() {
            let open_start = match content[search_pos..].find("{{") {
                Some(pos) => search_pos + pos,
                None => break,
            };

            if open_start + 2 < content.len() {
                let next_char = content.chars().nth(open_start + 2).unwrap_or('\0');
                if next_char == '#' || next_char == '@' || next_char == '$' || next_char == '/' {
                    search_pos = open_start + 2;
                    continue;
                }
            }

            let close_start = match content[open_start + 2..].find("}}") {
                Some(pos) => open_start + 2 + pos,
                None => break,
            };

            let placeholder_name = content[open_start + 2..close_start].trim();
            if placeholder_name.is_empty() || !CommonUtil::is_alphanumeric(placeholder_name) {
                search_pos = open_start + 2;
                continue;
            }

            let placeholder = TemplatePlaceholder {
                name: placeholder_name.to_string(),
                start_index: open_start,
                end_index: close_start + 2,
                full_match: content[open_start..close_start + 2].to_string(),
                template_key: placeholder_name.to_lowercase(),
                json_data: None,
                nested_placeholders: Vec::new(),
                nested_slots: Vec::new(),
            };

            if !template
                .placeholders
                .iter()
                .any(|p| p.name == placeholder_name)
            {
                template.placeholders.push(placeholder);
            }

            search_pos = close_start + 2;
        }
    }

    fn preprocess_json_templates(template: &mut PreprocessedTemplate) {
        if template.json_data.is_none() {
            return;
        }

        let content = template.original_content.clone();
        Self::create_json_array_replacement_mappings(template, &content);
        Self::create_json_placeholder_replacement_mappings(template, &content);
    }

    fn create_json_array_replacement_mappings(template: &mut PreprocessedTemplate, content: &str) {
        if template.json_data.is_none() {
            return;
        }
        let json_data = template.json_data.as_ref().unwrap();

        if let Some(object) = json_data.as_object() {
            for (json_key, json_value) in object {
                if let Some(data_list) = json_value.as_array() {
                    let key_norm = json_key.to_lowercase();
                    let possible_tags = vec![
                        json_key.clone(),
                        key_norm.clone(),
                        key_norm.trim_end_matches('s').to_string(),
                        format!("{}s", key_norm),
                    ];

                    for tag in possible_tags {
                        let block_start_tag = format!("{{{{@{}}}}}", tag);
                        let block_end_tag = format!("{{{{/{}}}}}", tag);

                        if let Some(start_idx) =
                            content.to_lowercase().find(&block_start_tag.to_lowercase())
                        {
                            let search_from = start_idx + block_start_tag.len();
                            if let Some(end_idx) = content[search_from..]
                                .to_lowercase()
                                .find(&block_end_tag.to_lowercase())
                            {
                                let end_idx = search_from + end_idx;

                                if end_idx > start_idx {
                                    let block_content =
                                        &content[start_idx + block_start_tag.len()..end_idx];
                                    let full_block =
                                        &content[start_idx..end_idx + block_end_tag.len()];

                                    let processed_array_content =
                                        Self::process_array_block_content_safely(
                                            block_content,
                                            data_list,
                                        );

                                    template.replacement_mappings.push(ReplacementMapping {
                                        start_index: start_idx,
                                        end_index: end_idx + block_end_tag.len(),
                                        original_text: full_block.to_string(),
                                        replacement_text: processed_array_content,
                                        r#type: ReplacementType::JsonPlaceholder,
                                        target_template_name: None,
                                    });

                                    let empty_block_start = format!("{{{{^{}}}}}", tag);
                                    let empty_block_end = format!("{{{{/{}}}}}", tag);
                                    if let Some(empty_start_idx) = content
                                        .to_lowercase()
                                        .find(&empty_block_start.to_lowercase())
                                    {
                                        let empty_search_from =
                                            empty_start_idx + empty_block_start.len();
                                        if let Some(empty_end_idx) = content[empty_search_from..]
                                            .to_lowercase()
                                            .find(&empty_block_end.to_lowercase())
                                        {
                                            let empty_end_idx = empty_search_from + empty_end_idx;
                                            if empty_end_idx
                                                > empty_start_idx + empty_block_start.len()
                                            {
                                                let empty_content_start =
                                                    empty_start_idx + empty_block_start.len();
                                                let empty_block_content =
                                                    &content[empty_content_start..empty_end_idx];
                                                let full_empty_block = &content[empty_start_idx
                                                    ..empty_end_idx + empty_block_end.len()];
                                                let empty_replacement = if data_list.is_empty() {
                                                    empty_block_content
                                                } else {
                                                    ""
                                                };

                                                template.replacement_mappings.push(
                                                    ReplacementMapping {
                                                        start_index: empty_start_idx,
                                                        end_index: empty_end_idx
                                                            + empty_block_end.len(),
                                                        original_text: full_empty_block.to_string(),
                                                        replacement_text: empty_replacement
                                                            .to_string(),
                                                        r#type: ReplacementType::JsonPlaceholder,
                                                        target_template_name: None,
                                                    },
                                                );
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    fn process_array_block_content_safely(
        block_content: &str,
        array_data: &crate::app::JsonArray,
    ) -> String {
        use crate::app::JsonValue;
        let mut merged_block = String::new();

        for item in array_data.iter() {
            if let JsonValue::Object(json_item) = item {
                let mut item_block = block_content.to_string();

                for (key, value) in json_item.iter() {
                    let placeholder = format!("{{{{${}}}}}", key);
                    let value_str = match value {
                        JsonValue::String(s) => s.clone(),
                        JsonValue::Number(n) => n.to_string(),
                        JsonValue::Integer(i) => i.to_string(),
                        JsonValue::Bool(b) => b.to_string(),
                        JsonValue::Null => "null".to_string(),
                        _ => value.to_string(),
                    };
                    item_block =
                        Self::replace_all_case_insensitive(&item_block, &placeholder, &value_str);
                }

                item_block = Self::process_conditional_blocks_safely(&item_block, json_item);
                merged_block.push_str(&item_block);
            }
        }
        merged_block
    }

    fn replace_all_case_insensitive(input: &str, search: &str, replacement: &str) -> String {
        let mut result = String::new();
        let mut last_end = 0;
        let lower_search = search.to_lowercase();
        let lower_input = input.to_lowercase();

        while let Some(start) = lower_input[last_end..].find(&lower_search) {
            let start = last_end + start;
            result.push_str(&input[last_end..start]);
            result.push_str(replacement);
            last_end = start + search.len();
        }
        result.push_str(&input[last_end..]);
        result
    }

    fn process_conditional_blocks_safely(
        content: &str,
        json_item: &crate::app::JsonObject,
    ) -> String {
        let mut result = content.to_string();
        let conditional_keys = Self::find_conditional_keys_in_content(&result);

        for cond_key in conditional_keys {
            let cond_value = Self::get_condition_value(json_item, &cond_key);
            result = Self::process_conditional_block_safely(&result, &cond_key, cond_value);
        }
        result
    }

    fn find_conditional_keys_in_content(content: &str) -> HashSet<String> {
        let mut conditional_keys = HashSet::new();
        let mut cond_idx = 0;

        while let Some(cond_start) = content[cond_idx..].to_lowercase().find("{{@") {
            let cond_start = cond_idx + cond_start;
            if let Some(cond_end) = content[cond_start..].find("}}") {
                let cond_end = cond_start + cond_end;
                let cond_key = content[cond_start + 3..cond_end].trim().to_string();
                conditional_keys.insert(cond_key);
                cond_idx = cond_end + 2;
            } else {
                break;
            }
        }
        conditional_keys
    }

    fn get_condition_value(item: &crate::app::JsonObject, cond_key: &str) -> bool {
        use crate::app::JsonValue;
        if let Some(value) = item.get(cond_key) {
            return match value {
                JsonValue::Bool(b) => *b,
                JsonValue::String(s) => s.parse::<bool>().unwrap_or(false),
                JsonValue::Integer(i) => *i != 0,
                JsonValue::Number(n) => *n != 0.0,
                _ => false,
            };
        }
        for (key, value) in item.iter() {
            if key.eq_ignore_ascii_case(cond_key) {
                return match value {
                    JsonValue::Bool(b) => *b,
                    JsonValue::String(s) => s.parse::<bool>().unwrap_or(false),
                    JsonValue::Integer(i) => *i != 0,
                    JsonValue::Number(n) => *n != 0.0,
                    _ => false,
                };
            }
        }
        false
    }

    fn process_conditional_block_safely(input: &str, key: &str, condition: bool) -> String {
        let mut result = input.to_string();
        let condition_tags = vec![
            (format!("{{{{@{}}}}}", key), format!("{{{{ /{}}}}}", key)),
            (format!("{{{{@{}}}}}", key), format!("{{{{/{}}}}}", key)),
        ];

        for (cond_start_tag, cond_end_tag) in condition_tags {
            while let Some(start_idx) = result.to_lowercase().find(&cond_start_tag.to_lowercase()) {
                let content_start = start_idx + cond_start_tag.len();
                if let Some(end_idx) = result[content_start..]
                    .to_lowercase()
                    .find(&cond_end_tag.to_lowercase())
                {
                    let end_idx = content_start + end_idx;
                    let content = &result[content_start..end_idx];
                    let replacement = if condition {
                        content.to_string()
                    } else {
                        String::new()
                    };
                    result.replace_range(start_idx..end_idx + cond_end_tag.len(), &replacement);
                } else {
                    break;
                }
            }
        }
        result
    }

    fn create_json_placeholder_replacement_mappings(
        template: &mut PreprocessedTemplate,
        content: &str,
    ) {
        if template.json_data.is_none() {
            return;
        }
        let json_data = template.json_data.as_ref().unwrap();

        if let Some(object) = json_data.as_object() {
            for (key, value) in object {
                if let Some(string_value) = value.as_str() {
                    let placeholder = format!("{{{{${}}}}}", key);

                    if content.to_lowercase().contains(&placeholder.to_lowercase()) {
                        if !template.replacement_mappings.iter().any(|m| {
                            matches!(m.r#type, ReplacementType::JsonPlaceholder)
                                && m.original_text.eq_ignore_ascii_case(&placeholder)
                        }) {
                            template.replacement_mappings.push(ReplacementMapping {
                                start_index: 0,
                                end_index: 0,
                                original_text: placeholder.clone(),
                                replacement_text: string_value.to_string(),
                                r#type: ReplacementType::JsonPlaceholder,
                                target_template_name: None,
                            });
                        }

                        if !template
                            .json_placeholders
                            .iter()
                            .any(|p| p.placeholder.eq_ignore_ascii_case(&placeholder))
                        {
                            template.json_placeholders.push(JsonPlaceholder {
                                key: key.clone(),
                                placeholder: placeholder.clone(),
                                value: string_value.to_string(),
                            });
                        }
                    }
                }
            }
        }
    }

    fn build_parent_map_for_preprocess(
        site_templates: &PreprocessedSiteTemplates,
        app_site: &str,
    ) -> HashMap<String, String> {
        let mut parent_map = HashMap::new();

        Logger::debug(
            &format!("Building parent map for appSite: {}", app_site),
            Some("LoaderPreProcess"),
        );

        let primary_prefix = format!("{}_", app_site.to_lowercase());

        for (template_key, template) in &site_templates.templates {
            let is_primary_template = template_key.starts_with(&primary_prefix);
            for placeholder in &template.placeholders {
                let template_name = &placeholder.template_key;
                let child_template_key = format!(
                    "{}_{}",
                    app_site.to_lowercase(),
                    template_name.to_lowercase()
                );

                if is_primary_template {
                    Logger::debug(
                        &format!(
                            "Parent relationship: {} -> parent: {}",
                            child_template_key, template_key
                        ),
                        Some("LoaderPreProcess"),
                    );
                    parent_map.insert(child_template_key.clone(), template_key.clone());
                } else {
                    parent_map
                        .entry(child_template_key.clone())
                        .or_insert_with(|| {
                            Logger::debug(
                                &format!(
                                    "Parent relationship: {} -> parent: {}",
                                    child_template_key, template_key
                                ),
                                Some("LoaderPreProcess"),
                            );
                            template_key.clone()
                        });
                }
            }

            for slotted_template in &template.slotted_templates {
                let template_name = &slotted_template.name;
                let child_template_key = format!(
                    "{}_{}",
                    app_site.to_lowercase(),
                    template_name.to_lowercase()
                );

                if is_primary_template {
                    Logger::debug(
                        &format!(
                            "Parent relationship (slotted): {} -> parent: {}",
                            child_template_key, template_key
                        ),
                        Some("LoaderPreProcess"),
                    );
                    parent_map.insert(child_template_key.clone(), template_key.clone());
                } else {
                    parent_map
                        .entry(child_template_key.clone())
                        .or_insert_with(|| {
                            Logger::debug(
                                &format!(
                                    "Parent relationship (slotted): {} -> parent: {}",
                                    child_template_key, template_key
                                ),
                                Some("LoaderPreProcess"),
                            );
                            template_key.clone()
                        });
                }
            }
        }

        Logger::debug(
            &format!("Built parent map with {} relationships", parent_map.len()),
            Some("LoaderPreProcess"),
        );
        parent_map
    }

    fn resolve_json_inheritance_for_all_templates(
        site_templates: &mut PreprocessedSiteTemplates,
        parent_map: &HashMap<String, String>,
    ) {
        let template_keys: Vec<String> = site_templates.templates.keys().cloned().collect();

        for template_key in template_keys {
            let json_data_clone = site_templates
                .templates
                .get(&template_key)
                .and_then(|t| t.json_data.clone());

            if json_data_clone.is_none() {
                continue;
            }

            let json_data = json_data_clone.as_ref().unwrap();
            let mut resolved_json = JsonObject::new();
            let mut has_inheritance = false;

            for (key, value) in json_data.iter() {
                if key.ends_with('#') {
                    if let Some(str_value) = value.as_str() {
                        has_inheritance = true;
                        let actual_key = &key[..key.len() - 1];
                        if let Some(resolved_value) = Self::search_parent_tree_for_key_preprocess(
                            actual_key,
                            &template_key,
                            &site_templates.templates,
                            parent_map,
                        ) {
                            resolved_json.insert(
                                actual_key.to_string(),
                                JsonValue::String(resolved_value.clone()),
                            );
                            Logger::debug(
                                &format!(
                                    "Resolved inherited key {} -> {} = {} for template {}",
                                    key, actual_key, resolved_value, template_key
                                ),
                                Some("LoaderPreProcess"),
                            );
                        } else {
                            resolved_json.insert(
                                actual_key.to_string(),
                                JsonValue::String(str_value.to_string()),
                            );
                            Logger::debug(
                                &format!(
                                    "No inherited value found for {}, using default: {}",
                                    actual_key, str_value
                                ),
                                Some("LoaderPreProcess"),
                            );
                        }
                    }
                } else {
                    resolved_json.insert(key.clone(), value.clone());
                }
            }

            if has_inheritance {
                if let Some(template) = site_templates.templates.get_mut(&template_key) {
                    template.json_data = Some(resolved_json);
                    Logger::debug(
                        &format!(
                            "Updated JsonData for template {} with resolved inheritance",
                            template_key
                        ),
                        Some("LoaderPreProcess"),
                    );
                }
            }
        }
    }

    fn search_parent_tree_for_key_preprocess(
        key: &str,
        current_template_key: &str,
        all_templates: &HashMap<String, PreprocessedTemplate>,
        parent_map: &HashMap<String, String>,
    ) -> Option<String> {
        let parent_key = parent_map.get(current_template_key)?;

        Logger::debug(
            &format!("Checking parent {} for key {}", parent_key, key),
            Some("LoaderPreProcess"),
        );

        let parent_template = all_templates.get(parent_key)?;

        if parent_template.json_data.is_none() {
            Logger::debug(
                &format!(
                    "Parent template {} has no JSON data, searching further up",
                    parent_key
                ),
                Some("LoaderPreProcess"),
            );
            return Self::search_parent_tree_for_key_preprocess(
                key,
                parent_key,
                all_templates,
                parent_map,
            );
        }

        let parent_json = parent_template.json_data.as_ref().unwrap();
        for (k, v) in parent_json.iter() {
            if k.eq_ignore_ascii_case(key) {
                if let Some(str_value) = v.as_str() {
                    Logger::debug(
                        &format!("Found key {} in parent {}: {}", key, parent_key, str_value),
                        Some("LoaderPreProcess"),
                    );
                    return Some(str_value.to_string());
                }
            }
        }

        Logger::debug(
            &format!(
                "Key {} not found in parent {}, searching further up",
                key, parent_key
            ),
            Some("LoaderPreProcess"),
        );
        Self::search_parent_tree_for_key_preprocess(key, parent_key, all_templates, parent_map)
    }

    fn recreate_json_placeholder_mappings_after_inheritance(
        site_templates: &mut PreprocessedSiteTemplates,
    ) {
        for (template_key, template) in site_templates.templates.iter_mut() {
            if template.json_data.is_none() {
                continue;
            }

            template
                .replacement_mappings
                .retain(|mapping| mapping.r#type != ReplacementType::JsonPlaceholder);

            let content_clone = template.original_content.clone();
            Self::create_json_array_replacement_mappings(template, &content_clone);
            let placeholder_clone = template.original_content.clone();
            Self::create_json_placeholder_replacement_mappings(template, &placeholder_clone);

            if template_key.eq_ignore_ascii_case("jsonruleflow1a_links") {
                if let Some(json_data) = &template.json_data {
                    if let Some(language_value) = json_data.get("Language").and_then(|v| v.as_str())
                    {
                        Logger::debug(
                            &format!(
                                "[Debug] jsonruleflow1a_links inherited Language={}",
                                language_value
                            ),
                            Some("LoaderPreProcess"),
                        );
                    }
                }
            }

            Logger::debug(
                &format!(
                    "Recreated JSON placeholder and array mappings for template {} after inheritance resolution",
                    template_key
                ),
                Some("LoaderPreProcess"),
            );
        }
    }
}

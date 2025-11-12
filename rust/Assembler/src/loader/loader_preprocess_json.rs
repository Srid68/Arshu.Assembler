use crate::interface::i_loader_json::ILoaderJson;
use crate::app::json::{JsonObject, JsonValue};
use crate::app::json_convertor::JsonConverter;
use crate::common::common_util::CommonUtil;
use crate::loader::json_merge_util::JsonMergeUtil;
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

lazy_static! {
    static ref PREPROCESSED_TEMPLATES_CACHE: Mutex<HashMap<String, PreprocessedSiteTemplates>> =
        Mutex::new(HashMap::new());
}

/// Loader that implements ILoader<PreprocessedTemplate> for PreProcess engine
/// Loads and preprocesses templates with JsonObject for type safety
pub struct LoaderPreProcessJson {
    templates: HashMap<String, PreprocessedTemplate>,
    search_app_sites: String,
}

impl LoaderPreProcessJson {
    /// Creates a new loader instance by loading and preprocessing templates from the specified root directory
    ///
    /// # Arguments
    /// * `root_dir_path` - Root directory path containing AppSites folder
    /// * `app_site` - Primary AppSite name to load
    /// * `search_app_sites` - Comma-delimited string of AppSite names to search for fallback templates (can be empty string)
    pub fn new(root_dir_path: &str, app_site: &str, search_app_sites: &str) -> Self {
        // Load templates from primary appSite
        let site_templates = Self::load_process_get_template_files(root_dir_path, app_site);
        let mut templates = site_templates.templates.clone();

        // Load templates from searchAppSites for fallback
        if !search_app_sites.is_empty() {
            let search_app_sites_array: Vec<&str> = search_app_sites.split(',').collect();
            for search_app_site in search_app_sites_array {
                let search_app_site = search_app_site.trim();
                if search_app_site.is_empty() {
                    continue;
                }

                let search_site_templates =
                    Self::load_process_get_template_files(root_dir_path, search_app_site);
                for (key, value) in search_site_templates.templates {
                    // Only add if not already present (primary appSite takes precedence)
                    if !templates.contains_key(&key) {
                        templates.insert(key, value);
                    }
                }
            }
        }

        Self {
            templates,
            search_app_sites: search_app_sites.to_string(),
        }
    }

    /// Gets all preprocessed templates (needed for PreProcess engine to apply replacement mappings from all templates)
    pub fn all_templates(&self) -> &HashMap<String, PreprocessedTemplate> {
        &self.templates
    }

    fn merge_with_json(&self, html: &str, app_site: &str, template_name: &str) -> String {
        if html.is_empty() {
            return html.to_string();
        }

        if let Some(template) = self.get_template_internal(app_site, template_name, None, None) {
            if let Some(json_data) = template.json_data {
                Logger::debug(
                    &format!("Merging HTML with JSON for {}", template_name),
                    Some("LoaderPreProcessJson"),
                );
                return JsonMergeUtil::merge_template_with_json(html, &json_data);
            }
        }

        Logger::debug(
            &format!(
                "No JSON data found for {}, returning original HTML",
                template_name
            ),
            Some("LoaderPreProcessJson"),
        );
        html.to_string()
    }

    /// Internal helper method with AppView fallback logic and SearchAppSites support
    fn get_template_internal(
        &self,
        app_site: &str,
        template_name: &str,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
    ) -> Option<PreprocessedTemplate> {
        // Try AppView fallback first if provided
        if let (Some(view), Some(prefix)) = (app_view, app_view_prefix) {
            if template_name
                .to_lowercase()
                .contains(&prefix.to_lowercase())
            {
                let app_key = CommonUtil::replace_case_insensitive(template_name, prefix, view);
                let fallback_key =
                    format!("{}_{}", app_site.to_lowercase(), app_key.to_lowercase());

                if let Some(template) = self.templates.get(&fallback_key) {
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
        if let Some(template) = self.templates.get(&primary_key) {
            return Some(template.clone());
        }

        // Search in SearchAppSites as fallback
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
                if let Some(template) = self.templates.get(&search_key) {
                    Logger::debug(
                        &format!(
                            "Template '{}' not found in '{}', using fallback from '{}'",
                            template_name, app_site, search_app_site
                        ),
                        Some("LoaderPreProcessJson"),
                    );
                    return Some(template.clone());
                }
            }
        }

        None
    }

    // Loading Templates

    /// Loads and preprocesses HTML files from the specified application site directory into structured templates
    fn load_process_get_template_files(
        root_dir_path: &str,
        app_site: &str,
    ) -> PreprocessedSiteTemplates {
        Logger::debug(
            &format!(
                "LoadProcessGetTemplateFiles called for appSite: {}",
                app_site
            ),
            Some("LoaderPreProcessJson"),
        );

        let cache_key = format!(
            "{}|{}",
            Path::new(root_dir_path)
                .parent()
                .unwrap_or(Path::new(""))
                .display(),
            app_site
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
                    Some("LoaderPreProcessJson"),
                );
                return cached.clone();
            }
        }

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
                Some("LoaderPreProcessJson"),
            );
            let mut cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
            cache.insert(cache_key, result.clone());
            return result;
        }

        Logger::debug(
            &format!("Loading templates from: {}", app_sites_path),
            Some("LoaderPreProcessJson"),
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
                    Some("LoaderPreProcessJson"),
                );

                // Find JSON file case-insensitively
                let json_file = path.with_extension("json");
                let mut json_data: Option<JsonObject> = None;

                // Try exact match first
                if json_file.exists() {
                    let json_content = CommonUtil::normalize_file_content(
                        &fs::read_to_string(&json_file).unwrap_or_default(),
                    );
                    json_data = Some(JsonConverter::parse_json_string(&json_content));
                    Logger::debug(
                        &format!("Found JSON file for {}, parsed to JsonObject", key),
                        Some("LoaderPreProcessJson"),
                    );
                } else {
                    // Try case-insensitive search
                    if let Some(directory) = path.parent() {
                        let base_file_name =
                            path.file_stem().unwrap().to_string_lossy().to_string();
                        if let Ok(entries) = fs::read_dir(directory) {
                            let mut matching_json: Option<std::path::PathBuf> = None;
                            for entry in entries.filter_map(|e| e.ok()) {
                                let entry_path = entry.path();
                                if entry_path
                                    .extension()
                                    .map(|ext| ext == "json")
                                    .unwrap_or(false)
                                {
                                    if let Some(entry_stem) = entry_path.file_stem() {
                                        if entry_stem
                                            .to_string_lossy()
                                            .eq_ignore_ascii_case(&base_file_name)
                                        {
                                            matching_json = Some(entry_path);
                                            break;
                                        }
                                    }
                                }
                            }

                            if let Some(matching_json) = matching_json {
                                let json_content = CommonUtil::normalize_file_content(
                                    &fs::read_to_string(&matching_json).unwrap_or_default(),
                                );
                                json_data = Some(JsonConverter::parse_json_string(&json_content));
                                Logger::debug(&format!("Found JSON file (case-insensitive) for {}, parsed to JsonObject", key), Some("LoaderPreProcessJson"));
                            }
                        }
                    }
                }

                // Store raw template
                result.raw_templates.insert(key.clone(), content.clone());
                result.template_keys.insert(key.clone());

                // Preprocess the template with JSON data
                let preprocessed =
                    Self::preprocess_template(&content, json_data.as_ref(), app_site, &key);
                result.templates.insert(key.clone(), preprocessed);

                Logger::debug(
                    &format!(
                        "Preprocessed {}: {} replacements, {} slotted, {} placeholders",
                        key,
                        result.templates[&key].replacement_mappings.len(),
                        result.templates[&key].slotted_templates.len(),
                        result.templates[&key].placeholders.len()
                    ),
                    Some("LoaderPreProcessJson"),
                );
            }
        }

        Logger::debug(
            &format!(
                "Loaded {} templates for {}",
                result.templates.len(),
                app_site
            ),
            Some("LoaderPreProcessJson"),
        );

        // CRITICAL: Create ALL replacement mappings after all templates are loaded
        Self::create_all_replacement_mappings_for_site(&mut result, app_site);

        Logger::debug(
            &format!("Created all replacement mappings for {}", app_site),
            Some("LoaderPreProcessJson"),
        );

        let mut cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
        cache.insert(cache_key, result.clone());
        result
    }

    // PreProcess and Mapping

    /// Creates a preprocessed template by parsing its structure and using JsonObject for data
    fn preprocess_template(
        content: &str,
        json_data: Option<&JsonObject>,
        app_site: &str,
        _template_key: &str,
    ) -> PreprocessedTemplate {
        let mut template = PreprocessedTemplate {
            original_content: content.to_string(),
            placeholders: Vec::new(),
            slotted_templates: Vec::new(),
            json_data: json_data.cloned(),
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
            template.update_flags();
            return template;
        }

        // Parse template structure
        Self::parse_slotted_templates(content, app_site, &mut template);
        Self::parse_placeholder_templates(content, app_site, &mut template);

        // Preprocess JSON templates
        if json_data.is_some() && !json_data.unwrap().is_empty() {
            Self::preprocess_json_templates(&mut template);
        }

        template.update_flags();
        template
    }

    /// Creates ALL replacement mappings for all templates after they are loaded
    fn create_all_replacement_mappings_for_site(
        site_templates: &mut PreprocessedSiteTemplates,
        app_site: &str,
    ) {
        // Phase 0: Build parent map and resolve JSON inheritance BEFORE creating any mappings
        Logger::debug(
            &format!(
                "Creating replacement mappings for {} - Phase 0: JSON inheritance",
                app_site
            ),
            Some("LoaderPreProcessJson"),
        );
        let parent_map = Self::build_parent_map_for_preprocess_json(site_templates, app_site);
        Self::resolve_json_inheritance_for_all_templates_json(site_templates, &parent_map);
        Self::recreate_json_placeholder_mappings_after_inheritance_json(site_templates);

        Logger::debug(
            &format!(
                "Creating replacement mappings for {} - Phase 1: JSON arrays",
                app_site
            ),
            Some("LoaderPreProcessJson"),
        );

        // Phase 1: Create JSON replacement mappings for all templates first
        let keys: Vec<String> = site_templates.templates.keys().cloned().collect();
        for key in keys {
            let template = site_templates.templates.get(&key).unwrap();
            let content = template.original_content.clone();
            let json_data = template.json_data.clone();

            let mut new_mappings = Vec::new();
            if json_data.is_some() {
                Self::create_json_array_replacement_mappings_for_template(
                    &content,
                    json_data.as_ref(),
                    &mut new_mappings,
                );
            }

            if !new_mappings.is_empty() {
                let template_mut = site_templates.templates.get_mut(&key).unwrap();
                template_mut.replacement_mappings.extend(new_mappings);
            }
        }

        Logger::debug(
            &format!(
                "Creating replacement mappings for {} - Phase 2: Simple placeholders",
                app_site
            ),
            Some("LoaderPreProcessJson"),
        );

        // Phase 2: Create simple template replacement mappings
        let keys: Vec<String> = site_templates.templates.keys().cloned().collect();
        for key in keys.clone() {
            let template = site_templates.templates.get(&key).unwrap().clone();
            let mut new_mappings = Vec::new();
            Self::create_placeholder_replacement_mappings(
                &template,
                &site_templates.templates,
                app_site,
                &mut new_mappings,
            );

            if !new_mappings.is_empty() {
                let template_mut = site_templates.templates.get_mut(&key).unwrap();
                template_mut.replacement_mappings.extend(new_mappings);
            }
        }

        Logger::debug(
            &format!(
                "Creating replacement mappings for {} - Phase 3: Slotted templates",
                app_site
            ),
            Some("LoaderPreProcessJson"),
        );

        // Phase 3: Create slotted template replacement mappings
        for key in keys {
            let template = site_templates.templates.get(&key).unwrap().clone();
            let mut new_mappings = Vec::new();
            Self::create_slotted_template_replacement_mappings(
                &template,
                &site_templates.templates,
                app_site,
                &mut new_mappings,
            );

            if !new_mappings.is_empty() {
                let template_mut = site_templates.templates.get_mut(&key).unwrap();
                template_mut.replacement_mappings.extend(new_mappings);
            }
        }

        // Log summary
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
            Some("LoaderPreProcessJson"),
        );
    }

    /// Creates replacement mappings for simple placeholders
    fn create_placeholder_replacement_mappings(
        template: &PreprocessedTemplate,
        all_templates: &HashMap<String, PreprocessedTemplate>,
        app_site: &str,
        new_mappings: &mut Vec<ReplacementMapping>,
    ) {
        if template.placeholders.is_empty() {
            return;
        }

        for placeholder in &template.placeholders {
            let target_template_key =
                format!("{}_{}", app_site.to_lowercase(), placeholder.template_key);
            if let Some(target_template) = all_templates.get(&target_template_key) {
                let processed_template = target_template.original_content.clone();

                Logger::debug(
                    &format!(
                        "Creating replacement mapping: {} -> {}",
                        placeholder.full_match, placeholder.template_key
                    ),
                    Some("LoaderPreProcessJson"),
                );
                new_mappings.push(ReplacementMapping {
                    start_index: 0,
                    end_index: 0,
                    original_text: placeholder.full_match.clone(),
                    replacement_text: processed_template,
                    r#type: ReplacementType::SimpleTemplate,
                    target_template_name: Some(placeholder.template_key.clone()), // Store for engine to retrieve JSON
                });
            }
        }
    }

    /// Creates replacement mappings for slotted templates
    fn create_slotted_template_replacement_mappings(
        template: &PreprocessedTemplate,
        all_templates: &HashMap<String, PreprocessedTemplate>,
        app_site: &str,
        new_mappings: &mut Vec<ReplacementMapping>,
    ) {
        if template.slotted_templates.is_empty() {
            return;
        }

        for slotted_template in &template.slotted_templates {
            let full_match = &slotted_template.full_match;
            let target_template_key = format!(
                "{}_{}",
                app_site.to_lowercase(),
                slotted_template.template_key
            );

            if let Some(target_template) = all_templates.get(&target_template_key) {
                let mut processed_template = target_template.original_content.clone();

                // Process slots
                for slot in &slotted_template.slots {
                    let processed_slot_content = Self::process_slot_content_for_replacement_mapping(
                        slot,
                        all_templates,
                        app_site,
                    );
                    processed_template =
                        processed_template.replace(&slot.slot_key, &processed_slot_content);
                }

                // Handle default slot
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

                // Remove remaining slot placeholders
                processed_template =
                    CommonUtil::remove_remaining_slot_placeholders(&processed_template);

                Logger::debug(
                    &format!(
                        "Creating slotted replacement mapping: {} -> {}",
                        slotted_template.name, slotted_template.template_key
                    ),
                    Some("LoaderPreProcessJson"),
                );
                new_mappings.push(ReplacementMapping {
                    start_index: 0,
                    end_index: 0,
                    original_text: full_match.clone(),
                    replacement_text: processed_template,
                    r#type: ReplacementType::SlottedTemplate,
                    target_template_name: Some(slotted_template.template_key.clone()), // Store for engine to retrieve JSON
                });
            }
        }
    }

    /// Processes slot content for creating replacement mappings
    fn process_slot_content_for_replacement_mapping(
        slot: &SlotPlaceholder,
        all_templates: &HashMap<String, PreprocessedTemplate>,
        app_site: &str,
    ) -> String {
        let mut result = slot.content.clone();

        // Process nested slotted templates
        for nested_slotted_template in &slot.nested_slotted_templates {
            let target_template_key = format!(
                "{}_{}",
                app_site.to_lowercase(),
                nested_slotted_template.template_key
            );
            if let Some(target_template) = all_templates.get(&target_template_key) {
                let mut processed_template = target_template.original_content.clone();

                // Process nested slots
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

                // Remove remaining slot placeholders
                processed_template =
                    CommonUtil::remove_remaining_slot_placeholders(&processed_template);

                // Replace in result
                result = result.replace(&nested_slotted_template.full_match, &processed_template);
            }
        }

        result
    }

    // Slot Processing

    /// Parses slotted templates in the content
    fn parse_slotted_templates(content: &str, app_site: &str, template: &mut PreprocessedTemplate) {
        let mut search_pos = 0;

        while search_pos < content.len() {
            // Look for opening tag {{#
            let open_start = match content[search_pos..].find("{{#") {
                Some(pos) => search_pos + pos,
                None => break,
            };

            // Find the end of the template name
            let open_end = match content[open_start + 3..].find("}}") {
                Some(pos) => open_start + 3 + pos,
                None => break,
            };

            // Extract template name
            let template_name = content[open_start + 3..open_end].trim();
            if template_name.is_empty() || !CommonUtil::is_alphanumeric(template_name) {
                search_pos = open_start + 1;
                continue;
            }

            // Look for corresponding closing tag
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

            // Extract inner content
            let inner_start = open_end + 2;
            let inner_content = &content[inner_start..close_start];
            let full_match = &content[open_start..close_start + close_tag.len()];

            // Create slotted template structure
            let mut slotted_template = SlottedTemplate {
                name: template_name.to_string(),
                start_index: open_start,
                end_index: close_start + close_tag.len(),
                full_match: full_match.to_string(),
                inner_content: inner_content.to_string(),
                slots: Vec::new(),
                template_key: template_name.to_lowercase(),
                json_data: None,
            };

            // Parse slots within this slotted template
            Self::parse_slots(inner_content, &mut slotted_template, app_site);

            template.slotted_templates.push(slotted_template);
            search_pos = close_start + close_tag.len();
        }
    }

    /// Parses slots within a slotted template
    fn parse_slots(inner_content: &str, slotted_template: &mut SlottedTemplate, app_site: &str) {
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

            let close_tag_len = close_tag.len();

            let mut slot = SlotPlaceholder {
                nested_slots: Vec::new(),
                number: slot_num,
                start_index: slot_start,
                end_index: close_start + close_tag_len,
                content: slot_content,
                slot_key,
                open_tag,
                close_tag,
                nested_placeholders: Vec::new(),
                nested_slotted_templates: Vec::new(),
                has_nested_placeholders: false,
                has_nested_slotted_templates: false,
                requires_nested_processing: false,
            };

            Self::parse_nested_templates_in_slot(
                &mut slot,
                slotted_template.json_data.as_ref(),
                app_site,
            );

            slotted_template.slots.push(slot);
            search_pos = close_start + close_tag_len;
        }
    }

    /// Parses nested templates within slot content
    fn parse_nested_templates_in_slot(
        slot: &mut SlotPlaceholder,
        json_data: Option<&JsonObject>,
        app_site: &str,
    ) {
        if slot.content.is_empty() {
            return;
        }

        let content = &slot.content;

        // Parse simple placeholders like {{ComponentName}}
        let mut search_pos = 0;
        while search_pos < content.len() {
            let open_start = match content[search_pos..].find("{{") {
                Some(pos) => search_pos + pos,
                None => break,
            };

            // Skip if it's a special placeholder
            if open_start + 2 < content.len() {
                let next_char = content.chars().nth(open_start + 2).unwrap();
                if next_char == '#' || next_char == '@' || next_char == '$' || next_char == '/' {
                    search_pos = open_start + 2;
                    continue;
                }
            }

            let close_start = match content[open_start + 2..].find("}}") {
                Some(pos) => open_start + 2 + pos,
                None => break,
            };

            let template_name = content[open_start + 2..close_start].trim();
            if !template_name.is_empty() && CommonUtil::is_alphanumeric(template_name) {
                let template_key = template_name.to_lowercase();

                slot.nested_placeholders.push(TemplatePlaceholder {
                    name: template_name.to_string(),
                    start_index: open_start,
                    end_index: close_start + 2,
                    full_match: content[open_start..close_start + 2].to_string(),
                    template_key,
                    json_data: json_data.cloned(),
                    nested_placeholders: Vec::new(),
                    nested_slots: Vec::new(),
                });
            }

            search_pos = close_start + 2;
        }

        // Parse slotted templates like {{#TemplateName}} ... {{/TemplateName}}
        search_pos = 0;
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

            let inner_content = &content[open_end + 2..close_start];
            let template_key = template_name.to_lowercase();

            let mut nested_slotted_template = SlottedTemplate {
                name: template_name.to_string(),
                start_index: open_start,
                end_index: close_start + close_tag.len(),
                full_match: content[open_start..close_start + close_tag.len()].to_string(),
                inner_content: inner_content.to_string(),
                slots: Vec::new(),
                template_key,
                json_data: json_data.cloned(),
            };

            // Parse slots within this nested slotted template
            Self::parse_slots(inner_content, &mut nested_slotted_template, app_site);

            slot.nested_slotted_templates.push(nested_slotted_template);
            search_pos = close_start + close_tag.len();
        }

        slot.has_nested_placeholders = !slot.nested_placeholders.is_empty();
        slot.has_nested_slotted_templates = !slot.nested_slotted_templates.is_empty();
        slot.requires_nested_processing =
            slot.has_nested_placeholders || slot.has_nested_slotted_templates;
    }

    // PlaceHolder Processing

    /// Parses simple placeholders in the content
    fn parse_placeholder_templates(
        content: &str,
        _app_site: &str,
        template: &mut PreprocessedTemplate,
    ) {
        let mut search_pos = 0;

        while search_pos < content.len() {
            // Look for opening placeholder {{
            let open_start = match content[search_pos..].find("{{") {
                Some(pos) => search_pos + pos,
                None => break,
            };

            // Make sure it's not a slotted template or special placeholder
            if open_start + 2 < content.len() {
                let next_char = content.chars().nth(open_start + 2).unwrap();
                if next_char == '#' || next_char == '@' || next_char == '$' || next_char == '/' {
                    search_pos = open_start + 2;
                    continue;
                }
            }

            // Find closing }}
            let close_start = match content[open_start + 2..].find("}}") {
                Some(pos) => open_start + 2 + pos,
                None => break,
            };

            // Extract placeholder name
            let placeholder_name = content[open_start + 2..close_start].trim();
            if placeholder_name.is_empty() || !CommonUtil::is_alphanumeric(placeholder_name) {
                search_pos = open_start + 2;
                continue;
            }

            // Create placeholder structure
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

            template.placeholders.push(placeholder);
            search_pos = close_start + 2;
        }
    }

    // Json Processing

    /// Preprocesses JSON templates by creating complete replacement mappings
    fn preprocess_json_templates(template: &mut PreprocessedTemplate) {
        if template.json_data.is_none() {
            return;
        }

        let content = template.original_content.clone();

        // Step 1: Create replacement mappings for JSON array blocks (handled in create_all_replacement_mappings_for_site)
        // Step 2: Create replacement mappings for JSON placeholders
        Self::create_json_placeholder_replacement_mappings(template, &content);
    }

    /// Creates replacement mappings for JSON array blocks
    fn create_json_array_replacement_mappings_for_template(
        content: &str,
        json_data: Option<&JsonObject>,
        new_mappings: &mut Vec<ReplacementMapping>,
    ) {
        if json_data.is_none() {
            return;
        }

        let json_data = json_data.unwrap();

        for (json_key, json_value) in json_data.iter() {
            if let JsonValue::Array(data_list) = json_value {
                let key_norm = json_key.to_lowercase();
                let possible_tags = vec![
                    json_key.clone(),
                    key_norm.clone(),
                    key_norm.trim_end_matches('s').to_string(),
                    format!("{}s", &key_norm),
                ];

                for tag in possible_tags {
                    let block_start_tag = format!("{{{{@{}}}}}", tag);
                    let block_end_tag = format!("{{{{/{}}}}}", tag);

                    if let Some(start_idx) =
                        Self::index_of_case_insensitive(content, &block_start_tag)
                    {
                        let search_from = start_idx + block_start_tag.len();
                        if let Some(end_idx) = Self::index_of_case_insensitive_from(
                            content,
                            &block_end_tag,
                            search_from,
                        ) {
                            if end_idx > start_idx {
                                let content_start = start_idx + block_start_tag.len();
                                if content_start <= end_idx {
                                    let block_content = &content[content_start..end_idx];
                                    let full_block =
                                        &content[start_idx..end_idx + block_end_tag.len()];

                                    // Process the array content completely
                                    let processed_array_content =
                                        Self::process_array_block_content_safely(
                                            block_content,
                                            data_list,
                                        );

                                    new_mappings.push(ReplacementMapping {
                                        start_index: start_idx,
                                        end_index: end_idx + block_end_tag.len(),
                                        original_text: full_block.to_string(),
                                        replacement_text: processed_array_content,
                                        r#type: ReplacementType::JsonPlaceholder,
                                        target_template_name: None, // JSON placeholders use own template's JSON
                                    });

                                    // Handle empty array blocks
                                    let empty_block_start = format!("{{{{^{}}}}}", tag);
                                    let empty_block_end = format!("{{{{/{}}}}}", tag);

                                    if let Some(empty_start_idx) =
                                        Self::index_of_case_insensitive(content, &empty_block_start)
                                    {
                                        let empty_search_from =
                                            empty_start_idx + empty_block_start.len();
                                        if let Some(empty_end_idx) =
                                            Self::index_of_case_insensitive_from(
                                                content,
                                                &empty_block_end,
                                                empty_search_from,
                                            )
                                        {
                                            if empty_end_idx
                                                > empty_start_idx + empty_block_start.len()
                                            {
                                                let content_start =
                                                    empty_start_idx + empty_block_start.len();
                                                let content_length = empty_end_idx - content_start;

                                                if content_start + content_length <= content.len() {
                                                    let empty_block_content =
                                                        &content[content_start..empty_end_idx];
                                                    let full_empty_block = &content[empty_start_idx
                                                        ..empty_end_idx + empty_block_end.len()];
                                                    let empty_replacement = if data_list.is_empty()
                                                    {
                                                        empty_block_content.to_string()
                                                    } else {
                                                        String::new()
                                                    };

                                                    new_mappings.push(ReplacementMapping {
                                                        start_index: empty_start_idx,
                                                        end_index: empty_end_idx
                                                            + empty_block_end.len(),
                                                        original_text: full_empty_block.to_string(),
                                                        replacement_text: empty_replacement,
                                                        r#type: ReplacementType::JsonPlaceholder,
                                                        target_template_name: None, // JSON placeholders use own template's JSON
                                                    });
                                                }
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

    /// Creates replacement mappings for JSON placeholders
    fn create_json_placeholder_replacement_mappings(
        template: &mut PreprocessedTemplate,
        content: &str,
    ) {
        if template.json_data.is_none() {
            return;
        }

        let json_data = template.json_data.as_ref().unwrap();

        for (key, value) in json_data.iter() {
            if let JsonValue::String(string_value) = value {
                let placeholder = format!("{{{{${}}}}}", key);

                if Self::index_of_case_insensitive(content, &placeholder).is_some() {
                    // Create replacement mapping
                    template.replacement_mappings.push(ReplacementMapping {
                        start_index: 0,
                        end_index: 0,
                        original_text: placeholder.clone(),
                        replacement_text: string_value.clone(),
                        r#type: ReplacementType::JsonPlaceholder,
                        target_template_name: None, // JSON placeholders use own template's JSON
                    });

                    // Also create JsonPlaceholder (avoid duplicates)
                    let placeholder_exists = template
                        .json_placeholders
                        .iter()
                        .any(|p| p.placeholder == placeholder);
                    if !placeholder_exists {
                        template.json_placeholders.push(JsonPlaceholder {
                            key: key.clone(),
                            placeholder: placeholder.clone(),
                            value: string_value.clone(),
                        });
                    }
                }
            }
        }
    }

    /// Safely processes array block content
    fn process_array_block_content_safely(
        block_content: &str,
        array_data: &crate::app::json::JsonArray,
    ) -> String {
        let mut merged_block = String::new();

        for item in array_data.iter() {
            if let JsonValue::Object(json_item) = item {
                let mut item_block = block_content.to_string();

                // Replace all placeholders for this item
                for (key, value) in json_item.iter() {
                    let placeholder = format!("{{{{${}}}}}", key);
                    let value_str = match value {
                        JsonValue::Bool(b) => if *b { "true" } else { "false" }.to_string(),
                        JsonValue::Null => String::new(),
                        JsonValue::String(s) => s.clone(),
                        JsonValue::Integer(i) => i.to_string(),
                        JsonValue::Number(n) => n.to_string(),
                        _ => value.to_string(),
                    };
                    item_block =
                        Self::replace_all_case_insensitive(&item_block, &placeholder, &value_str);
                }

                // Handle conditional blocks for this item safely
                item_block = Self::process_conditional_blocks_safely(&item_block, json_item);

                merged_block.push_str(&item_block);
            }
        }

        merged_block
    }

    /// Safely processes conditional blocks
    fn process_conditional_blocks_safely(content: &str, json_item: &JsonObject) -> String {
        let mut result = content.to_string();

        // Find all conditional keys in the content
        let conditional_keys = Self::find_conditional_keys_in_content(&result);

        for cond_key in conditional_keys {
            let cond_value = Self::get_condition_value(json_item, &cond_key);
            result = Self::process_conditional_block_safely(&result, &cond_key, cond_value);
        }

        result
    }

    /// Finds conditional keys in content
    fn find_conditional_keys_in_content(content: &str) -> HashSet<String> {
        let mut conditional_keys = HashSet::new();
        let mut cond_idx = 0;

        loop {
            if let Some(cond_start) = Self::index_of_case_insensitive_from(content, "{{@", cond_idx)
            {
                if let Some(cond_end) =
                    Self::index_of_case_insensitive_from(content, "}}", cond_start)
                {
                    let start = cond_start + 3;
                    if start < cond_end {
                        let cond_key = content[start..cond_end].trim().to_string();
                        conditional_keys.insert(cond_key);
                        cond_idx = cond_end + 2;
                    } else {
                        break;
                    }
                } else {
                    break;
                }
            } else {
                break;
            }
        }

        conditional_keys
    }

    /// Gets condition value from item data
    fn get_condition_value(item: &JsonObject, cond_key: &str) -> bool {
        for (key, value) in item.iter() {
            if key.eq_ignore_ascii_case(cond_key) {
                return match value {
                    JsonValue::Bool(b) => *b,
                    JsonValue::String(s) => s.parse::<bool>().unwrap_or(false),
                    JsonValue::Integer(i) => *i != 0,
                    JsonValue::Number(d) => *d != 0.0,
                    _ => false,
                };
            }
        }

        false
    }

    /// Processes a single conditional block safely
    fn process_conditional_block_safely(input: &str, key: &str, condition: bool) -> String {
        let mut result = input.to_string();

        let condition_tags = vec![
            (format!("{{{{@{}}}}}", key), format!("{{{{ /{}}}}}", key)),
            (format!("{{{{@{}}}}}", key), format!("{{{{/{}}}}}", key)),
        ];

        for (cond_start, cond_end) in condition_tags {
            loop {
                if let Some(start_idx) = Self::index_of_case_insensitive(&result, &cond_start) {
                    if let Some(end_idx) = Self::index_of_case_insensitive(&result, &cond_end) {
                        let content_start = start_idx + cond_start.len();
                        if end_idx > content_start {
                            let content = result[content_start..end_idx].to_string();
                            let before = &result[..start_idx];
                            let after = &result[end_idx + cond_end.len()..];

                            result = if condition {
                                format!("{}{}{}", before, content, after)
                            } else {
                                format!("{}{}", before, after)
                            };
                        } else {
                            break;
                        }
                    } else {
                        break;
                    }
                } else {
                    break;
                }
            }
        }

        result
    }

    // Helper methods

    fn replace_all_case_insensitive(input: &str, search: &str, replacement: &str) -> String {
        let mut result = input.to_string();
        let mut idx = 0;

        while let Some(found) = Self::index_of_case_insensitive_from(&result, search, idx) {
            let before = &result[..found];
            let after = &result[found + search.len()..];
            result = format!("{}{}{}", before, replacement, after);
            idx = found + replacement.len();
        }

        result
    }

    fn index_of_case_insensitive(haystack: &str, needle: &str) -> Option<usize> {
        Self::index_of_case_insensitive_from(haystack, needle, 0)
    }

    fn index_of_case_insensitive_from(haystack: &str, needle: &str, from: usize) -> Option<usize> {
        let haystack_lower = haystack.to_lowercase();
        let needle_lower = needle.to_lowercase();

        haystack_lower[from..]
            .find(&needle_lower)
            .map(|pos| from + pos)
    }

    /// Helper methods for JSON inheritance
    /// NOTE: The following methods are INTENTIONAL COPIES used across all loaders for architectural separation.
    /// Each loader/engine pair is independent to allow individual evolution without shared dependencies.
    /// DO NOT extract these to shared utilities - that would create tight coupling.

    /// Builds a parent-child relationship map by analyzing template placeholders
    /// Tracks which template is the parent of another based on {{TemplateName}} references
    fn build_parent_map_for_preprocess_json(
        site_templates: &PreprocessedSiteTemplates,
        app_site: &str,
    ) -> std::collections::HashMap<String, String> {
        let mut parent_map = std::collections::HashMap::new();

        Logger::debug(
            &format!("Building parent map for appSite: {}", app_site),
            Some("LoaderPreProcessJson"),
        );

        let primary_prefix = format!("{}_", app_site.to_lowercase());

        for (template_key, template) in &site_templates.templates {
            let is_primary_template = template_key.starts_with(&primary_prefix);
            // Find all {{TemplateName}} placeholders in this template
            for placeholder in &template.placeholders {
                let placeholder_name = &placeholder.name;

                // This template (template_key) is the parent of the placeholder template
                let child_template_key = format!(
                    "{}_{}",
                    app_site.to_lowercase(),
                    placeholder_name.to_lowercase()
                );

                if is_primary_template {
                    Logger::debug(
                        &format!(
                            "Parent relationship: {} -> parent: {}",
                            child_template_key, template_key
                        ),
                        Some("LoaderPreProcessJson"),
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
                                Some("LoaderPreProcessJson"),
                            );
                            template_key.clone()
                        });
                }
            }

            // Also check slotted templates
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
                        Some("LoaderPreProcessJson"),
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
                                Some("LoaderPreProcessJson"),
                            );
                            template_key.clone()
                        });
                }
            }
        }

        Logger::debug(
            &format!("Built parent map with {} relationships", parent_map.len()),
            Some("LoaderPreProcessJson"),
        );
        parent_map
    }

    /// Resolves JSON inheritance for all templates by modifying their JsonData in place
    fn resolve_json_inheritance_for_all_templates_json(
        site_templates: &mut PreprocessedSiteTemplates,
        parent_map: &std::collections::HashMap<String, String>,
    ) {
        // Collect template keys to avoid borrow checker issues
        let template_keys: Vec<String> = site_templates.templates.keys().cloned().collect();

        for template_key in template_keys {
            // First, clone the json_data to avoid borrow checker issues
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
                // Check if this is an inheritable key (ends with #)
                if key.ends_with('#') {
                    if let Some(str_value) = value.as_str() {
                        has_inheritance = true;
                        let actual_key = &key[..key.len() - 1];
                        let resolved_value = Self::search_parent_tree_for_key_preprocess_json(
                            actual_key,
                            &template_key,
                            &site_templates.templates,
                            parent_map,
                        );

                        if let Some(resolved) = resolved_value {
                            resolved_json.insert(
                                actual_key.to_string(),
                                JsonValue::String(resolved.clone()),
                            );
                            Logger::debug(
                                &format!(
                                    "Resolved inherited key {} -> {} = {} for template {}",
                                    key, actual_key, resolved, template_key
                                ),
                                Some("LoaderPreProcessJson"),
                            );
                        } else {
                            // Use default value if not found in parents
                            resolved_json.insert(
                                actual_key.to_string(),
                                JsonValue::String(str_value.to_string()),
                            );
                            Logger::debug(
                                &format!(
                                    "No inherited value found for {}, using default: {}",
                                    actual_key, str_value
                                ),
                                Some("LoaderPreProcessJson"),
                            );
                        }
                    }
                } else {
                    // Normal key - keep as is
                    resolved_json.insert(key.clone(), value.clone());
                }
            }

            // Replace JsonData with resolved version if any inheritance was found
            if has_inheritance {
                let template = site_templates.templates.get_mut(&template_key).unwrap();
                template.json_data = Some(resolved_json);
                Logger::debug(
                    &format!(
                        "Updated JsonData for template {} with resolved inheritance",
                        template_key
                    ),
                    Some("LoaderPreProcessJson"),
                );
            }
        }
    }

    /// Searches up the parent tree to find a JSON key value
    fn search_parent_tree_for_key_preprocess_json(
        key: &str,
        current_template_key: &str,
        all_templates: &std::collections::HashMap<String, PreprocessedTemplate>,
        parent_map: &std::collections::HashMap<String, String>,
    ) -> Option<String> {
        // Get parent template key
        let parent_key = parent_map.get(current_template_key)?;

        Logger::debug(
            &format!("Checking parent {} for key {}", parent_key, key),
            Some("LoaderPreProcessJson"),
        );

        // Get parent's template
        let parent_template = all_templates.get(parent_key)?;

        if parent_template.json_data.is_none() {
            Logger::debug(
                &format!(
                    "Parent template {} has no JSON data, searching further up",
                    parent_key
                ),
                Some("LoaderPreProcessJson"),
            );
            // Parent has no JSON, search further up the tree
            return Self::search_parent_tree_for_key_preprocess_json(
                key,
                parent_key,
                all_templates,
                parent_map,
            );
        }

        // Look for the key (case-insensitive)
        let parent_json = parent_template.json_data.as_ref().unwrap();
        for (k, v) in parent_json.iter() {
            if k.eq_ignore_ascii_case(key) {
                if let Some(str_value) = v.as_str() {
                    Logger::debug(
                        &format!("Found key {} in parent {}: {}", key, parent_key, str_value),
                        Some("LoaderPreProcessJson"),
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
            Some("LoaderPreProcessJson"),
        );
        // Not found in this parent, search further up the tree
        Self::search_parent_tree_for_key_preprocess_json(key, parent_key, all_templates, parent_map)
    }

    fn recreate_json_placeholder_mappings_after_inheritance_json(
        site_templates: &mut PreprocessedSiteTemplates,
    ) {
        for (template_key, template) in site_templates.templates.iter_mut() {
            if template.json_data.is_none() {
                continue;
            }

            // Remove old JSON placeholder mappings (both simple placeholders AND array blocks use JsonPlaceholder type)
            template
                .replacement_mappings
                .retain(|mapping| mapping.r#type != ReplacementType::JsonPlaceholder);

            // Recreate JSON array block mappings FIRST (they may contain simple placeholders)
            Self::create_json_array_replacement_mappings_for_template(
                &template.original_content,
                template.json_data.as_ref(),
                &mut template.replacement_mappings,
            );

            // Then recreate simple JSON placeholder mappings from the resolved JsonData
            let original_content_clone = template.original_content.clone(); // Clone to fix borrow error
            Self::create_json_placeholder_replacement_mappings(template, &original_content_clone);

            Logger::debug(
                &format!(
                    "Recreated JSON placeholder and array mappings for template {} after inheritance resolution",
                    template_key
                ),
                Some("LoaderPreProcessJson"),
            );
        }
    }
}

impl ILoaderJson<PreprocessedTemplate> for LoaderPreProcessJson {
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
        self.get_template_internal(app_site, template_name, app_view, app_view_prefix)
    }

    fn merge_html_with_json(&self, html: &str, app_site: &str, template_name: &str) -> String {
        self.merge_with_json(html, app_site, template_name)
    }

    fn apply_all_replacement_mappings(
        &self,
        content: &str,
        _app_site: &str,
        main_template: Option<&PreprocessedTemplate>,
        _app_view: Option<&str>,
        _app_view_prefix: Option<&str>,
        enable_json_processing: bool,
    ) -> String {
        let mut result = content.to_string();

        // Apply JSON placeholder replacements first if enabled
        if enable_json_processing {
            if let Some(main) = main_template {
                for mapping in &main.replacement_mappings {
                    if matches!(mapping.r#type, ReplacementType::JsonPlaceholder) {
                        result = result.replace(&mapping.original_text, &mapping.replacement_text);
                    }
                }
            }
        }

        // Apply all replacement mappings from all templates
        for template in self.templates.values() {
            for mapping in &template.replacement_mappings {
                if !matches!(mapping.r#type, ReplacementType::JsonPlaceholder) {
                    result = result.replace(&mapping.original_text, &mapping.replacement_text);
                }
            }
        }

        result
    }

    fn has_template(&self, app_site: &str, template_name: &str) -> bool {
        let key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );
        self.templates.contains_key(&key)
    }

    fn get_all_templates_json(&self) -> String {
        // Serialize all templates to JSON string
        let mut json_str = String::from("{");
        let mut first = true;

        for (key, template) in &self.templates {
            if !first {
                json_str.push(',');
            }
            first = false;

            json_str.push_str(&format!("\"{}\":{{", key));
            json_str.push_str(&format!("\"html\":\"{}\"", Self::escape_json_string(&template.original_content)));

            if let Some(json_data) = &template.json_data {
                json_str.push_str(",\"json\":");
                json_str.push_str(&JsonConverter::serialize_object(json_data));
            }

            json_str.push('}');
        }

        json_str.push('}');
        json_str
    }

    fn clear_cache(&self) {
        let mut cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
        cache.clear();
    }
}

impl LoaderPreProcessJson {
    /// Helper method to escape JSON strings
    fn escape_json_string(s: &str) -> String {
        s.replace('\\', "\\\\")
            .replace('"', "\\\"")
            .replace('\n', "\\n")
            .replace('\r', "\\r")
            .replace('\t', "\\t")
    }
}

use crate::interface::i_loader_json::ILoaderJson;
use crate::app::json::{JsonObject, JsonValue};
use crate::app::json_convertor::JsonConverter;
use crate::common::common_util::CommonUtil;
use crate::loader::json_merge_util::JsonMergeUtil;
use crate::model::model_preprocess::PreprocessedTemplate;
use arshu::common::Logger;
use lazy_static::lazy_static;
use std::collections::HashMap;
use std::fs;
use std::path::Path;
use std::sync::Mutex;
use walkdir;

lazy_static! {
    static ref HTML_TEMPLATES_CACHE: Mutex<HashMap<String, HashMap<String, (String, Option<JsonObject>)>>> =
        Mutex::new(HashMap::new());
}

/// Loader that implements ILoader<String> for Normal engine
/// Loads templates with JsonObject for type safety
pub struct LoaderNormalJson {
    templates: HashMap<String, (String, Option<JsonObject>)>,
    search_app_sites: String,
    parent_map: HashMap<String, String>,
}

impl LoaderNormalJson {
    /// Creates a new loader instance by loading templates from the specified root directory
    ///
    /// # Arguments
    /// * `root_dir_path` - Root directory path containing AppSites folder
    /// * `app_sites` - Primary AppSite name to load
    /// * `search_app_sites` - Comma-delimited string of AppSite names to search for fallback templates (can be empty string)
    pub fn new(root_dir_path: &str, app_sites: &str, search_app_sites: &str) -> Self {
        // Load templates from primary appSite
        let mut templates = Self::load_get_template_files(root_dir_path, app_sites);

        // Load templates from searchAppSites for fallback
        if !search_app_sites.is_empty() {
            let search_app_sites_array: Vec<&str> = search_app_sites.split(',').collect();
            for search_app_site in search_app_sites_array {
                let search_app_site = search_app_site.trim();
                if search_app_site.is_empty() {
                    continue;
                }

                let search_templates =
                    Self::load_get_template_files(root_dir_path, search_app_site);
                for (key, value) in search_templates {
                    // Only add if not already present (primary appSite takes precedence)
                    if !templates.contains_key(&key) {
                        templates.insert(key, value);
                    }
                }
            }
        }

        let parent_map = Self::build_parent_map(app_sites, &templates);
        Logger::debug(
            &format!(
                "Built parent map with {} relationships for JSON inheritance",
                parent_map.len()
            ),
            Some("LoaderNormalJson"),
        );

        Self {
            templates,
            search_app_sites: search_app_sites.to_string(),
            parent_map,
        }
    }

    /// Internal helper method with AppView fallback logic and SearchAppSites support
    /// This implements the template resolution strategy used by the engines
    fn get_template_internal(
        &self,
        app_site: &str,
        template_name: &str,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
    ) -> Option<(String, Option<JsonObject>)> {
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
                        Some("LoaderNormalJson"),
                    );
                    return Some(template.clone());
                }
            }
        }

        None
    }

    /// Loads HTML files and corresponding JSON files from a single application site
    /// JSON is parsed to JsonObject immediately for type safety
    fn load_get_template_files(
        root_dir_path: &str,
        app_site: &str,
    ) -> HashMap<String, (String, Option<JsonObject>)> {
        Logger::debug(
            &format!("LoadGetTemplateFiles called for appSite: {}", app_site),
            Some("LoaderNormalJson"),
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
            let cache = HTML_TEMPLATES_CACHE.lock().unwrap();
            if let Some(cached) = cache.get(&cache_key) {
                Logger::debug(
                    &format!(
                        "Returning cached templates for {} ({} templates)",
                        app_site,
                        cached.len()
                    ),
                    Some("LoaderNormalJson"),
                );
                return cached.clone();
            }
        }

        let mut result = HashMap::new();
        let app_sites_path = format!("{}/AppSites/{}", root_dir_path, app_site);

        if !Path::new(&app_sites_path).exists() {
            Logger::warn(
                &format!("AppSites directory not found: {}", app_sites_path),
                Some("LoaderNormalJson"),
            );
            let mut cache = HTML_TEMPLATES_CACHE.lock().unwrap();
            cache.insert(cache_key, result.clone());
            return result;
        }

        Logger::debug(
            &format!("Loading templates from: {}", app_sites_path),
            Some("LoaderNormalJson"),
        );

        for entry in walkdir::WalkDir::new(&app_sites_path)
            .into_iter()
            .filter_map(|e| e.ok())
        {
            let path = entry.path();
            if path.extension().map(|ext| ext == "html").unwrap_or(false) {
                let file_name = path.file_stem().unwrap().to_string_lossy().to_string();
                let key = format!("{}_{}", app_site.to_lowercase(), file_name.to_lowercase());
                let html_content = CommonUtil::normalize_file_content(
                    &fs::read_to_string(path).unwrap_or_default(),
                );

                Logger::debug(
                    &format!(
                        "Loading template: {} (html size: {})",
                        key,
                        html_content.len()
                    ),
                    Some("LoaderNormalJson"),
                );

                // Find and parse JSON file to JsonObject
                let json_file = path.with_extension("json");
                let mut json_object: Option<JsonObject> = None;

                // Try exact match first
                if json_file.exists() {
                    let json_content = CommonUtil::normalize_file_content(
                        &fs::read_to_string(&json_file).unwrap_or_default(),
                    );
                    if !json_content.is_empty() {
                        match JsonConverter::parse_json_string(&json_content) {
                            obj if !obj.is_empty() => {
                                json_object = Some(obj);
                                Logger::debug(
                                    &format!(
                                        "Found and parsed JSON file for {} (size: {})",
                                        key,
                                        json_content.len()
                                    ),
                                    Some("LoaderNormalJson"),
                                );
                            }
                            _ => {
                                Logger::error(
                                    &format!("Failed to parse JSON for {}", key),
                                    Some("LoaderNormalJson"),
                                );
                            }
                        }
                    }
                } else {
                    // Try case-insensitive search in the same directory
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
                                if !json_content.is_empty() {
                                    match JsonConverter::parse_json_string(&json_content) {
                                        obj if !obj.is_empty() => {
                                            json_object = Some(obj);
                                            Logger::debug(&format!("Found and parsed JSON file (case-insensitive) for {} (size: {})", key, json_content.len()), Some("LoaderNormalJson"));
                                        }
                                        _ => {
                                            Logger::error(
                                                &format!("Failed to parse JSON for {}", key),
                                                Some("LoaderNormalJson"),
                                            );
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                result.insert(key, (html_content, json_object));
            }
        }

        Logger::debug(
            &format!("Loaded {} templates for {}", result.len(), app_site),
            Some("LoaderNormalJson"),
        );

        let mut cache = HTML_TEMPLATES_CACHE.lock().unwrap();
        cache.insert(cache_key, result.clone());
        result
    }

    fn merge_with_json(&self, html: &str, app_site: &str, template_name: &str) -> String {
        Logger::debug(
            &format!("MergeHtmlWithJson called: appSite={}, templateName={}", app_site, template_name),
            Some("LoaderNormalJson"),
        );

        if html.is_empty() {
            return html.to_string();
        }

        if let Some(json_data) = self.get_template_json_with_inheritance(app_site, template_name) {
            let json_keys: Vec<&str> = json_data.keys().map(|k| k.as_str()).collect();
            Logger::debug(
                &format!("Merging HTML with JSON for {} (keys: {})", template_name, json_keys.join(", ")),
                Some("LoaderNormalJson"),
            );
            return JsonMergeUtil::merge_template_with_json(html, &json_data);
        }

        Logger::debug(
            &format!(
                "No JSON data found for {}, returning original HTML",
                template_name
            ),
            Some("LoaderNormalJson"),
        );
        html.to_string()
    }

    fn get_template_json_with_inheritance(
        &self,
        app_site: &str,
        template_name: &str,
    ) -> Option<JsonObject> {
        let template_key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );
        Logger::debug(
            &format!("GetTemplateJsonWithInheritance: templateKey={}", template_key),
            Some("LoaderNormalJson"),
        );

        let template = self.get_template_internal(app_site, template_name, None, None)?;
        let json_obj = match template.1 {
            Some(json) => json,
            None => {
                Logger::debug(
                    &format!("No JSON found for templateKey={}", template_key),
                    Some("LoaderNormalJson"),
                );
                return None;
            }
        };

        let raw_keys: Vec<&str> = json_obj.keys().map(|k| k.as_str()).collect();
        Logger::debug(
            &format!("Raw JSON keys for {}: {}", template_key, raw_keys.join(", ")),
            Some("LoaderNormalJson"),
        );

        let mut resolved_json = JsonObject::new();

        for (key, value) in json_obj.iter() {
            if key.ends_with('#') {
                if let Some(str_value) = value.as_str() {
                    let actual_key = key.trim_end_matches('#');
                    Logger::debug(
                        &format!("Found inheritance key: {}, defaultValue={}, resolving for actualKey={}", key, str_value, actual_key),
                        Some("LoaderNormalJson"),
                    );
                    if let Some(resolved_value) =
                        self.resolve_json_key_with_inheritance(actual_key, str_value, &template_key)
                    {
                        resolved_json.insert(
                            actual_key.to_string(),
                            JsonValue::String(resolved_value.clone()),
                        );
                        Logger::debug(
                            &format!(
                                "Resolved inherited key {} -> {} = {}",
                                key, actual_key, resolved_value
                            ),
                            Some("LoaderNormalJson"),
                        );
                        continue;
                    }
                }
            }

            resolved_json.insert(key.clone(), value.clone());
        }

        Some(resolved_json)
    }

    fn resolve_json_key_with_inheritance(
        &self,
        actual_key: &str,
        default_value: &str,
        current_template_key: &str,
    ) -> Option<String> {
        Logger::debug(
            &format!(
                "Resolving inherited key: {} for template {}",
                actual_key, current_template_key
            ),
            Some("LoaderNormalJson"),
        );

        if let Some(inherited_value) =
            self.search_parent_tree_for_key(actual_key, current_template_key)
        {
            Logger::debug(
                &format!(
                    "Found inherited value for {}: {}",
                    actual_key, inherited_value
                ),
                Some("LoaderNormalJson"),
            );
            return Some(inherited_value);
        }

        Logger::debug(
            &format!(
                "No inherited value found for {}, using default: {}",
                actual_key, default_value
            ),
            Some("LoaderNormalJson"),
        );
        Some(default_value.to_string())
    }

    fn search_parent_tree_for_key(&self, key: &str, current_template_key: &str) -> Option<String> {
        let parent_key = match self.parent_map.get(current_template_key) {
            Some(parent) => parent.clone(),
            None => {
                Logger::debug(
                    &format!("No parent found for {}", current_template_key),
                    Some("LoaderNormalJson"),
                );
                return None;
            }
        };

        Logger::debug(
            &format!("Checking parent {} for key {}", parent_key, key),
            Some("LoaderNormalJson"),
        );

        let parent_entry = match self.templates.get(&parent_key) {
            Some(entry) => entry,
            None => {
                Logger::debug(
                    &format!("Parent template {} not found in templates", parent_key),
                    Some("LoaderNormalJson"),
                );
                return None;
            }
        };

        if let Some(parent_json) = &parent_entry.1 {
            for (json_key, json_value) in parent_json.iter() {
                if json_key.eq_ignore_ascii_case(key) {
                    if let Some(str_value) = json_value.as_str() {
                        Logger::debug(
                            &format!("Found key {} in parent {}: {}", key, parent_key, str_value),
                            Some("LoaderNormalJson"),
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
                Some("LoaderNormalJson"),
            );
            return self.search_parent_tree_for_key(key, &parent_key);
        }

        Logger::debug(
            &format!(
                "Parent template {} has no JSON data, searching further up",
                parent_key
            ),
            Some("LoaderNormalJson"),
        );
        self.search_parent_tree_for_key(key, &parent_key)
    }

    fn build_parent_map(
        app_site: &str,
        templates: &HashMap<String, (String, Option<JsonObject>)>,
    ) -> HashMap<String, String> {
        Logger::debug(
            &format!("Building parent map for appSite: {}", app_site),
            Some("LoaderNormalJson"),
        );

        let mut parent_map = HashMap::new();

        for (template_key, (html, _)) in templates {
            // Extract the actual appSite from the template_key (format: "appsite_templatename")
            let parent_app_site = if let Some(underscore_pos) = template_key.find('_') {
                &template_key[..underscore_pos]
            } else {
                app_site  // Fallback to primary app_site if no underscore found
            };

            let mut search_pos = 0;

            while search_pos < html.len() {
                let open_start = match html[search_pos..].find("{{") {
                    Some(pos) => search_pos + pos,
                    None => break,
                };

                if open_start + 2 >= html.len() {
                    break;
                }

                let next_char = html.as_bytes()[open_start + 2];
                if next_char == b'#' || next_char == b'@' || next_char == b'$' || next_char == b'/'
                {
                    search_pos = open_start + 2;
                    continue;
                }

                let close_start = match html[open_start + 2..].find("}}") {
                    Some(pos) => open_start + 2 + pos,
                    None => break,
                };

                let placeholder_name = html[open_start + 2..close_start].trim();

                if !placeholder_name.is_empty() && Self::is_alphanumeric(placeholder_name) {
                    // Use the parent's appSite, not the primary app_site
                    let child_template_key = format!(
                        "{}_{}",
                        parent_app_site.to_lowercase(),
                        placeholder_name.to_lowercase()
                    );

                    if !parent_map.contains_key(&child_template_key) {
                        parent_map.insert(child_template_key.clone(), template_key.clone());
                        Logger::debug(
                            &format!(
                                "Parent relationship: {} -> parent: {}",
                                child_template_key, template_key
                            ),
                            Some("LoaderNormalJson"),
                        );
                    }
                }

                search_pos = close_start + 2;
            }
        }

        Logger::debug(
            &format!("Built parent map with {} relationships", parent_map.len()),
            Some("LoaderNormalJson"),
        );
        parent_map
    }

    fn is_alphanumeric(value: &str) -> bool {
        value.chars().all(|c| c.is_ascii_alphanumeric())
    }
}

impl ILoaderJson<String> for LoaderNormalJson {
    fn search_app_sites(&self) -> &str {
        &self.search_app_sites
    }

    fn get_template_html(
        &self,
        app_site: &str,
        template_name: &str,
        app_view: Option<&str>,
        app_view_prefix: Option<&str>,
    ) -> Option<String> {
        self.get_template_internal(app_site, template_name, app_view, app_view_prefix)
            .map(|(html, _)| html)
    }

    fn merge_html_with_json(&self, html: &str, app_site: &str, template_name: &str) -> String {
        self.merge_with_json(html, app_site, template_name)
    }

    fn apply_all_replacement_mappings(
        &self,
        _content: &str,
        _app_site: &str,
        _main_template: Option<&PreprocessedTemplate>,
        _app_view: Option<&str>,
        _app_view_prefix: Option<&str>,
        _enable_json_processing: bool,
    ) -> String {
        panic!("ApplyAllReplacementMappings is not supported by LoaderNormalJson - use LoaderPreProcessJson instead");
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

        for (key, (html, json_obj)) in &self.templates {
            if !first {
                json_str.push(',');
            }
            first = false;

            json_str.push_str(&format!("\"{}\":{{", key));
            json_str.push_str(&format!("\"html\":\"{}\"", Self::escape_json_string(html)));

            if let Some(json) = json_obj {
                json_str.push_str(",\"json\":");
                json_str.push_str(&JsonConverter::serialize_object(json));
            }

            json_str.push('}');
        }

        json_str.push('}');
        json_str
    }

    fn clear_cache(&self) {
        let mut cache = HTML_TEMPLATES_CACHE.lock().unwrap();
        cache.clear();
    }
}

impl LoaderNormalJson {
    /// Helper method to escape JSON strings
    fn escape_json_string(s: &str) -> String {
        s.replace('\\', "\\\\")
            .replace('"', "\\\"")
            .replace('\n', "\\n")
            .replace('\r', "\\r")
            .replace('\t', "\\t")
    }
}

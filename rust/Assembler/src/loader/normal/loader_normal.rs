use crate::interface::i_loader_normal::ILoaderNormal;
use crate::app::json::{JsonObject, JsonValue};
use crate::app::json_convertor::JsonConverter;
use crate::common::common_util::CommonUtil;
use crate::loader::json_merge_util::JsonMergeUtil;
use arshu::common::Logger;
use indexmap::IndexMap;
use lazy_static::lazy_static;
use std::collections::HashMap;
use std::fs;
use std::path::Path;
use std::sync::Mutex;
use walkdir;

/// <summary>
/// Handles loading and caching of HTML templates from the file system for Normal engine
/// </summary>
pub struct LoaderNormal {
    templates: TemplateMap,
    search_app_sites: String,
    app_site: String,
    parent_map: HashMap<String, String>,
}

type TemplateMap = IndexMap<String, (String, Option<String>)>;

lazy_static! {
    static ref HTML_TEMPLATES_CACHE: Mutex<IndexMap<String, TemplateMap>> =
        Mutex::new(IndexMap::new());
}

impl LoaderNormal {
    /// Creates a new LoaderNormal instance
    pub fn new(root_dir_path: &str, app_site: &str, search_app_sites: &str) -> Self {
        Logger::debug(
            &format!("LoaderNormal::new called for appSite: {}, searchAppSites: {}", app_site, search_app_sites),
            Some("LoaderNormal"),
        );

        let templates = Self::load_get_template_files(root_dir_path, app_site, search_app_sites);

        Logger::debug(
            &format!("Loaded {} templates for {}", templates.len(), app_site),
            Some("LoaderNormal"),
        );

        let mut loader = Self {
            templates,
            search_app_sites: search_app_sites.to_string(),
            app_site: app_site.to_string(),
            parent_map: HashMap::new(),
        };

        // Build parent-child relationship map for JSON inheritance
        loader.parent_map = loader.build_parent_map();
        Logger::debug(
            &format!("Built parent map with {} relationships for JSON inheritance", loader.parent_map.len()),
            Some("LoaderNormal"),
        );

        loader
    }

    // Loading Templates

    /// <summary>
    /// Loads HTML files and corresponding JSON files from the specified application site directory, caching the output per appSite
    /// </summary>
    pub fn load_get_template_files(
        root_dir_path: &str,
        app_site: &str,
        search_app_sites: &str,
    ) -> TemplateMap {
        Logger::debug(
            &format!(
                "LoadGetTemplateFiles called for appSite: {}, searchAppSites: {}",
                app_site, search_app_sites
            ),
            Some("LoaderNormal"),
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
            let cache = HTML_TEMPLATES_CACHE.lock().unwrap();
            if let Some(cached) = cache.get(&cache_key) {
                Logger::debug(
                    &format!(
                        "Returning cached templates for {} ({} templates)",
                        app_site,
                        cached.len()
                    ),
                    Some("LoaderNormal"),
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

                let search_templates =
                    Self::load_templates_from_single_app_site(root_dir_path, search_app_site);
                for (key, value) in search_templates {
                    // Only add if not already present (primary appSite takes precedence)
                    if !result.contains_key(&key) {
                        result.insert(key.clone(), value);
                        Logger::debug(
                            &format!(
                                "Added fallback template '{}' from '{}'",
                                key, search_app_site
                            ),
                            Some("LoaderNormal"),
                        );
                    }
                }
            }
        }

        let mut cache = HTML_TEMPLATES_CACHE.lock().unwrap();
        cache.insert(cache_key, result.clone());
        result
    }

    /// <summary>
    /// Loads templates from a single AppSite without caching or fallback logic
    /// </summary>
    fn load_templates_from_single_app_site(root_dir_path: &str, app_site: &str) -> TemplateMap {
        let mut result = TemplateMap::new();
        let app_sites_path = format!("{}/AppSites/{}", root_dir_path, app_site);

        if !Path::new(&app_sites_path).exists() {
            Logger::warn(
                &format!("AppSites directory not found: {}", app_sites_path),
                Some("LoaderNormal"),
            );
            return result;
        }

        Logger::debug(
            &format!("Loading templates from: {}", app_sites_path),
            Some("LoaderNormal"),
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
                    Some("LoaderNormal"),
                );

                // Find JSON file case-insensitively
                let json_file = path.with_extension("json");
                let json_content = if json_file.exists() {
                    let json_str = CommonUtil::normalize_file_content(
                        &fs::read_to_string(&json_file).unwrap_or_default(),
                    );
                    Logger::debug(
                        &format!("Found JSON file for {} (size: {})", key, json_str.len()),
                        Some("LoaderNormal"),
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
                                        Some("LoaderNormal"),
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
                result.insert(key, (html_content, json_content));
            }
        }

        Logger::debug(
            &format!("Loaded {} templates for {}", result.len(), app_site),
            Some("LoaderNormal"),
        );
        result
    }

    /// <summary>
    /// Clear all cached templates (useful for testing or when templates change)
    /// </summary>
    pub fn clear_cache() {
        let mut html_cache = HTML_TEMPLATES_CACHE.lock().unwrap();
        html_cache.clear();
    }

    // JSON Inheritance Support (Private)

    /// Builds a parent-child relationship map by analyzing template placeholders
    /// Tracks which template is the parent of another based on {{TemplateName}} references
    fn build_parent_map(&self) -> HashMap<String, String> {
        let mut parent_map = HashMap::new();

        Logger::debug(
            &format!("Building parent map for appSite: {}", self.app_site),
            Some("LoaderNormal"),
        );

        for (template_key, (html, _)) in &self.templates {
            // Find all {{TemplateName}} placeholders in this template
            let mut search_pos = 0;
            while search_pos < html.len() {
                let open_start = match html[search_pos..].find("{{") {
                    Some(pos) => search_pos + pos,
                    None => break,
                };

                // Skip special placeholders (#, @, $, /)
                if open_start + 2 < html.len() {
                    let next_char = html.chars().nth(open_start + 2).unwrap_or('\0');
                    if next_char == '#' || next_char == '@' || next_char == '$' || next_char == '/' {
                        search_pos = open_start + 2;
                        continue;
                    }
                }

                let close_start = match html[open_start + 2..].find("}}") {
                    Some(pos) => open_start + 2 + pos,
                    None => break,
                };

                let placeholder_name = html[open_start + 2..close_start].trim();

                // Check if this is a valid alphanumeric template name
                if !placeholder_name.is_empty() && CommonUtil::is_alphanumeric(placeholder_name) {
                    // This template (template_key) is the parent of the placeholder template
                    let child_template_key = format!(
                        "{}_{}",
                        self.app_site.to_lowercase(),
                        placeholder_name.to_lowercase()
                    );

                    if !parent_map.contains_key(&child_template_key) {
                        parent_map.insert(child_template_key.clone(), template_key.clone());
                        Logger::debug(
                            &format!("Parent relationship: {} -> parent: {}", child_template_key, template_key),
                            Some("LoaderNormal"),
                        );
                    }
                }

                search_pos = close_start + 2;
            }
        }

        Logger::debug(
            &format!("Built parent map with {} relationships", parent_map.len()),
            Some("LoaderNormal"),
        );
        parent_map
    }

    /// Gets parsed JSON with inheritance resolution
    /// Resolves keys ending with # by searching up the parent tree
    fn get_template_json_with_inheritance(&self, app_site: &str, template_name: &str) -> Option<JsonObject> {
        let mut template_key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );

        Logger::debug(
            &format!("GetTemplateJsonWithInheritance: templateKey={}", template_key),
            Some("LoaderNormal"),
        );

        // Try to get JSON from primary appSite template
        let mut json_content: Option<String> = None;
        if let Some((_, json)) = self.templates.get(&template_key) {
            json_content = json.clone();
        } else {
            // Try searchAppSites fallback
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

                    if let Some((_, json)) = self.templates.get(&search_key) {
                        json_content = json.clone();
                        template_key = search_key; // Update key for inheritance resolution
                        break;
                    }
                }
            }
        }

        let json_content = match json_content {
            Some(content) if !content.is_empty() => content,
            _ => {
                Logger::debug(
                    &format!("No JSON found for templateKey={}", template_key),
                    Some("LoaderNormal"),
                );
                return None;
            }
        };

        // Parse JSON string
        let json_obj = JsonConverter::parse_json_string(&json_content);

        let raw_keys: Vec<String> = json_obj.keys().cloned().collect();
        Logger::debug(
            &format!("Raw JSON keys for {}: {}", template_key, raw_keys.join(", ")),
            Some("LoaderNormal"),
        );

        let mut resolved_json = JsonObject::new();

        // Process each JSON key and resolve inheritance
        for (key, value) in json_obj.iter() {
            // Check if this is an inheritable key (ends with #)
            if key.ends_with("#") {
                if let JsonValue::String(str_value) = value {
                    // Resolve inherited value
                    let actual_key = &key[..key.len() - 1];
                    Logger::debug(
                        &format!("Found inheritance key: {}, defaultValue={}, resolving for actualKey={}", key, str_value, actual_key),
                        Some("LoaderNormal"),
                    );
                    let resolved_value = self.resolve_json_key_with_inheritance(actual_key, str_value, &template_key);
                    resolved_json.insert(actual_key.to_string(), JsonValue::String(resolved_value.clone()));
                    Logger::debug(
                        &format!("Resolved inherited key {} -> {} = {}", key, actual_key, resolved_value),
                        Some("LoaderNormal"),
                    );
                    continue;
                }
            }

            // Normal key - keep as is
            resolved_json.insert(key.clone(), value.clone());
        }

        Some(resolved_json)
    }

    /// Resolves a JSON key by searching up the parent tree
    fn resolve_json_key_with_inheritance(&self, actual_key: &str, default_value: &str, current_template_key: &str) -> String {
        Logger::debug(
            &format!("Resolving inherited key: {} for template {}", actual_key, current_template_key),
            Some("LoaderNormal"),
        );

        // Search up the parent tree for the key
        if let Some(inherited_value) = self.search_parent_tree_for_key(actual_key, current_template_key) {
            Logger::debug(
                &format!("Found inherited value for {}: {}", actual_key, inherited_value),
                Some("LoaderNormal"),
            );
            return inherited_value;
        }

        // If not found in parents, use the default value
        Logger::debug(
            &format!("No inherited value found for {}, using default: {}", actual_key, default_value),
            Some("LoaderNormal"),
        );
        default_value.to_string()
    }

    /// Searches up the parent tree to find a JSON key value
    fn search_parent_tree_for_key(&self, key: &str, current_template_key: &str) -> Option<String> {
        // Get parent template key
        let parent_key = match self.parent_map.get(current_template_key) {
            Some(pk) => pk,
            None => {
                Logger::debug(
                    &format!("No parent found for {}", current_template_key),
                    Some("LoaderNormal"),
                );
                return None;
            }
        };

        Logger::debug(
            &format!("Checking parent {} for key {}", parent_key, key),
            Some("LoaderNormal"),
        );

        // Get parent's template
        let parent_template = match self.templates.get(parent_key) {
            Some(pt) => pt,
            None => {
                Logger::debug(
                    &format!("Parent template {} not found in templates", parent_key),
                    Some("LoaderNormal"),
                );
                return None;
            }
        };

        if parent_template.1.is_none() || parent_template.1.as_ref().unwrap().is_empty() {
            Logger::debug(
                &format!("Parent template {} has no JSON data, searching further up", parent_key),
                Some("LoaderNormal"),
            );
            // Parent has no JSON, search further up the tree
            return self.search_parent_tree_for_key(key, parent_key);
        }

        // Parse parent's JSON
        let parent_json_obj = JsonConverter::parse_json_string(parent_template.1.as_ref().unwrap());

        // Look for the key (case-insensitive)
        for (k, v) in parent_json_obj.iter() {
            if k.eq_ignore_ascii_case(key) {
                if let JsonValue::String(str_value) = v {
                    Logger::debug(
                        &format!("Found key {} in parent {}: {}", key, parent_key, str_value),
                        Some("LoaderNormal"),
                    );
                    return Some(str_value.clone());
                }
            }
        }

        Logger::debug(
            &format!("Key {} not found in parent {}, searching further up", key, parent_key),
            Some("LoaderNormal"),
        );
        // Not found in this parent, search further up the tree
        self.search_parent_tree_for_key(key, parent_key)
    }

}

impl ILoaderNormal for LoaderNormal {
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
        // Use stored templates directly
        // Try AppView fallback first if provided
        if let (Some(view), Some(prefix)) = (app_view, app_view_prefix) {
            if template_name
                .to_lowercase()
                .contains(&prefix.to_lowercase())
            {
                let app_key = CommonUtil::replace_case_insensitive(template_name, prefix, view);
                let fallback_key =
                    format!("{}_{}", app_site.to_lowercase(), app_key.to_lowercase());

                if let Some((html, _)) = self.templates.get(&fallback_key) {
                    return Some(html.clone());
                }
            }
        }

        // Try primary template key
        let primary_key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );

        if let Some((html, _)) = self.templates.get(&primary_key) {
            return Some(html.clone());
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

                if let Some((html, _)) = self.templates.get(&search_key) {
                    return Some(html.clone());
                }
            }
        }

        None
    }

    fn get_template_json(&self, app_site: &str, template_name: &str) -> Option<JsonObject> {
        // Use stored templates directly to get JSON content
        let key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );

        if let Some((_, json_str)) = self.templates.get(&key) {
            if let Some(json) = json_str {
                return Some(JsonConverter::parse_json_string(json));
            }
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

                if let Some((_, json_str)) = self.templates.get(&search_key) {
                    if let Some(json) = json_str {
                        return Some(JsonConverter::parse_json_string(json));
                    }
                }
            }
        }

        None
    }

    fn merge_html_with_json(&self, html: &str, app_site: &str, template_name: &str) -> String {
        Logger::debug(
            &format!("MergeHtmlWithJson called: appSite={}, templateName={}", app_site, template_name),
            Some("LoaderNormal"),
        );

        if html.is_empty() {
            return html.to_string();
        }

        // Get JSON with inheritance resolution
        if let Some(json_data) = self.get_template_json_with_inheritance(app_site, template_name) {
            let json_keys: Vec<String> = json_data.keys().cloned().collect();
            Logger::debug(
                &format!("Merging HTML with JSON for {} (keys: {})", template_name, json_keys.join(", ")),
                Some("LoaderNormal"),
            );
            return JsonMergeUtil::merge_template_with_json(html, &json_data);
        }

        Logger::debug(
            &format!(
                "No JSON data found for {}, returning original HTML",
                template_name
            ),
            Some("LoaderNormal"),
        );
        html.to_string()
    }

    fn has_template(&self, app_site: &str, template_name: &str) -> bool {
        // Use stored templates directly
        let key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );

        if self.templates.contains_key(&key) {
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

                if self.templates.contains_key(&search_key) {
                    return true;
                }
            }
        }

        false
    }

    fn clear_cache(&self) {
        Self::clear_cache();
    }
}

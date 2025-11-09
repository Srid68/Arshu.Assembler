use super::i_loader::ILoader;
use crate::app::json::JsonObject;
use crate::app::json_convertor::JsonConverter;
use crate::common::common_util::CommonUtil;
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

        Self {
            templates,
            search_app_sites: search_app_sites.to_string(),
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
}

impl ILoader<String> for LoaderNormalJson {
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

    fn get_template_json(&self, app_site: &str, template_name: &str) -> Option<JsonObject> {
        self.get_template_internal(app_site, template_name, None, None)
            .and_then(|(_, json)| json)
    }

    fn has_template(&self, app_site: &str, template_name: &str) -> bool {
        let key = format!(
            "{}_{}",
            app_site.to_lowercase(),
            template_name.to_lowercase()
        );
        self.templates.contains_key(&key)
    }

    fn clear_cache(&self) {
        let mut cache = HTML_TEMPLATES_CACHE.lock().unwrap();
        cache.clear();
    }
}

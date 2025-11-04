use std::collections::HashMap;
use std::fs;
use std::path::Path;
use walkdir;
use lazy_static::lazy_static;
use std::sync::Mutex;
use crate::common::logger::Logger;
use crate::common::common_util::CommonUtil;

/// <summary>
/// Handles loading and caching of HTML templates from the file system for Normal engine
/// </summary>
pub struct LoaderNormal;

lazy_static! {
    static ref HTML_TEMPLATES_CACHE: Mutex<HashMap<String, HashMap<String, (String, Option<String>)>>> = 
        Mutex::new(HashMap::new());
}

impl LoaderNormal {
    // Loading Templates

    /// <summary>
    /// Loads HTML files and corresponding JSON files from the specified application site directory, caching the output per appSite
    /// </summary>
    pub fn load_get_template_files(root_dir_path: &str, app_site: &str) -> HashMap<String, (String, Option<String>)> {
        Logger::debug(&format!("LoadGetTemplateFiles called for appSite: {}", app_site), Some("LoaderNormal"));

        let cache_key = format!("{}|{}",
            Path::new(root_dir_path).parent().unwrap_or(Path::new("")).display(),
            app_site);

        {
            let cache = HTML_TEMPLATES_CACHE.lock().unwrap();
            if let Some(cached) = cache.get(&cache_key) {
                Logger::debug(&format!("Returning cached templates for {} ({} templates)", app_site, cached.len()), Some("LoaderNormal"));
                return cached.clone();
            }
        }

        let mut result = HashMap::new();
        let app_sites_path = format!("{}/AppSites/{}", root_dir_path, app_site);

        if !Path::new(&app_sites_path).exists() {
            Logger::warn(&format!("AppSites directory not found: {}", app_sites_path), Some("LoaderNormal"));
            let mut cache = HTML_TEMPLATES_CACHE.lock().unwrap();
            cache.insert(cache_key, result.clone());
            return result;
        }

        Logger::debug(&format!("Loading templates from: {}", app_sites_path), Some("LoaderNormal"));

        for entry in walkdir::WalkDir::new(&app_sites_path)
            .into_iter()
            .filter_map(|e| e.ok())
        {
            let path = entry.path();
            if path.extension().map(|ext| ext == "html").unwrap_or(false) {
                let file_name = path.file_stem().unwrap().to_string_lossy().to_string();
                let key = format!("{}_{}", app_site.to_lowercase(), file_name.to_lowercase());
                let html_content = CommonUtil::normalize_file_content(&fs::read_to_string(path).unwrap_or_default());

                Logger::debug(&format!("Loading template: {} (html size: {})", key, html_content.len()), Some("LoaderNormal"));

                // Find JSON file case-insensitively
                let json_file = path.with_extension("json");
                let json_content = if json_file.exists() {
                    let json_str = CommonUtil::normalize_file_content(&fs::read_to_string(&json_file).unwrap_or_default());
                    Logger::debug(&format!("Found JSON file for {} (size: {})", key, json_str.len()), Some("LoaderNormal"));
                    Some(json_str)
                } else {
                    // Try case-insensitive search in the same directory
                    if let Some(parent_dir) = path.parent() {
                        let base_name = path.file_stem().unwrap().to_string_lossy();
                        if let Ok(entries) = fs::read_dir(parent_dir) {
                            let matching_json = entries
                                .filter_map(|e| e.ok())
                                .find(|entry| {
                                    let file_path = entry.path();
                                    file_path.extension().map(|ext| ext == "json").unwrap_or(false) &&
                                    file_path.file_stem()
                                        .and_then(|stem| Some(stem.to_string_lossy().eq_ignore_ascii_case(&base_name)))
                                        .unwrap_or(false)
                                });
                            if let Some(json_entry) = matching_json {
                                if let Ok(json_str) = fs::read_to_string(json_entry.path()) {
                                    let json_str = CommonUtil::normalize_file_content(&json_str);
                                    Logger::debug(&format!("Found JSON file (case-insensitive) for {} (size: {})", key, json_str.len()), Some("LoaderNormal"));
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

        Logger::debug(&format!("Loaded {} templates for {}", result.len(), app_site), Some("LoaderNormal"));

        let mut cache = HTML_TEMPLATES_CACHE.lock().unwrap();
        cache.insert(cache_key, result.clone());
        result
    }

    /// <summary>
    /// Clear all cached templates (useful for testing or when templates change)
    /// </summary>
    pub fn clear_cache() {
        let mut html_cache = HTML_TEMPLATES_CACHE.lock().unwrap();
        html_cache.clear();
    }
}
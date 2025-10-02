use std::collections::HashMap;
use std::fs;
use std::path::Path;
use walkdir;
use lazy_static::lazy_static;
use std::sync::Mutex;

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
        let cache_key = format!("{}|{}", 
            Path::new(root_dir_path).parent().unwrap_or(Path::new("")).display(), 
            app_site);
        
        {
            let cache = HTML_TEMPLATES_CACHE.lock().unwrap();
            if let Some(cached) = cache.get(&cache_key) {
                return cached.clone();
            }
        }

        let mut result = HashMap::new();
        let app_sites_path = format!("{}/AppSites/{}", root_dir_path, app_site);
        
        if !Path::new(&app_sites_path).exists() {
            let mut cache = HTML_TEMPLATES_CACHE.lock().unwrap();
            cache.insert(cache_key, result.clone());
            return result;
        }

        for entry in walkdir::WalkDir::new(&app_sites_path)
            .into_iter()
            .filter_map(|e| e.ok()) 
        {
            let path = entry.path();
            if path.extension().map(|ext| ext == "html").unwrap_or(false) {
                let file_name = path.file_stem().unwrap().to_string_lossy().to_string();
                let key = format!("{}_{}", app_site.to_lowercase(), file_name.to_lowercase());
                let html_content = fs::read_to_string(path).unwrap_or_default();
                let json_file = path.with_extension("json");
                let json_content = if json_file.exists() {
                    Some(fs::read_to_string(json_file).unwrap_or_default())
                } else {
                    None
                };
                result.insert(key, (html_content, json_content));
            }
        }
        
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
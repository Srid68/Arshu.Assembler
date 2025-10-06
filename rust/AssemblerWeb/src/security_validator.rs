use std::collections::HashSet;
use std::sync::Mutex;
use std::sync::OnceLock;

// Maximum parameter length to prevent DoS attacks
const PARAM_MAX_LENGTH: usize = 256;

/// Valid engine types allowlist
static VALID_ENGINE_TYPES: OnceLock<HashSet<String>> = OnceLock::new();

fn get_valid_engine_types() -> &'static HashSet<String> {
    VALID_ENGINE_TYPES.get_or_init(|| {
        let mut set = HashSet::new();
        set.insert("Normal".to_string());
        set.insert("PreProcess".to_string());
        set
    })
}

/// Cached ValidAppSites - loaded on first request
static CACHED_VALID_APP_SITES: OnceLock<Mutex<Option<HashSet<String>>>> = OnceLock::new();

fn get_app_sites_cache() -> &'static Mutex<Option<HashSet<String>>> {
    CACHED_VALID_APP_SITES.get_or_init(|| Mutex::new(None))
}

/// Gets the valid AppSites from cache, or loads from appsites.csv (generating if needed)
pub fn get_valid_app_sites(wwwroot_path: &str) -> Result<HashSet<String>, String> {
    let cache = get_app_sites_cache();
    let mut cache_guard = cache.lock()
        .map_err(|e| format!("Failed to acquire lock: {}", e))?;

    if cache_guard.is_some() {
        return Ok(cache_guard.clone().unwrap());
    }

    // Load AppSites
    let app_sites = crate::app_sites_config::load_app_sites(wwwroot_path)?;
    *cache_guard = Some(app_sites.clone());
    Ok(app_sites)
}

/// Clears the AppSites cache - useful for development/testing
#[allow(dead_code)]
pub fn clear_app_sites_cache() {
    let cache = get_app_sites_cache();
    if let Ok(mut cache_guard) = cache.lock() {
        *cache_guard = None;
    }
}

/// Validates if a path component is safe (no traversal, invalid chars, or excessive length)
pub fn is_valid_path_component(value: Option<&str>) -> bool {
    match value {
        None => false,
        Some(v) if v.trim().is_empty() => false,
        Some(v) => {
            // Check parameter length to prevent DoS
            if v.len() > PARAM_MAX_LENGTH {
                return false;
            }

            // Check for path traversal attempts
            if v.contains("..") || v.contains('/') || v.contains('\\') {
                return false;
            }

            // Check for other suspicious characters
            let invalid_chars = ['<', '>', ':', '"', '|', '?', '*', '\0'];
            if v.chars().any(|c| invalid_chars.contains(&c) || c.is_control()) {
                return false;
            }

            true
        }
    }
}

/// Validates engine type against allowlist (case-insensitive)
pub fn is_valid_engine_type(engine_type: &str) -> bool {
    get_valid_engine_types().iter().any(|valid| valid.eq_ignore_ascii_case(engine_type))
}

/// Validates app_site against allowlist (case-insensitive)
pub fn is_valid_app_site(app_site: &str, valid_app_sites: &HashSet<String>) -> bool {
    valid_app_sites.iter().any(|valid| valid.eq_ignore_ascii_case(app_site))
}

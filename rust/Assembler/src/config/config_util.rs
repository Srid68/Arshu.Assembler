use once_cell::sync::Lazy;
use std::collections::HashSet;
use std::fs;
use std::path::Path;
use std::sync::Mutex;

#[derive(Debug, Clone)]
pub struct Scenario {
    pub app_site: String,
    pub app_file: String,
    pub app_view: String,
}

impl Scenario {
    pub fn new(app_site: String, app_file: String, app_view: String) -> Self {
        Scenario {
            app_site,
            app_file,
            app_view,
        }
    }

    pub fn to_string(&self) -> String {
        format!("{}:{}:{}", self.app_site, self.app_file, self.app_view)
    }
}

static CONFIG_CACHE: Lazy<Mutex<ConfigCache>> = Lazy::new(|| {
    Mutex::new(ConfigCache {
        wwwroot_path: None,
        app_sites: None,
        scenarios: None,
    })
});

struct ConfigCache {
    wwwroot_path: Option<String>,
    app_sites: Option<HashSet<String>>,
    scenarios: Option<Vec<Scenario>>,
}

pub struct ConfigUtil;

pub const DEFAULT_APP_FILE: &str = "Index";

impl ConfigUtil {
    /// Extracts unique AppSites from scenarios
    fn extract_app_sites_from_scenarios(scenarios: &[Scenario]) -> HashSet<String> {
        let mut app_sites = HashSet::new();

        for scenario in scenarios {
            if !scenario.app_site.is_empty() {
                app_sites.insert(scenario.app_site.to_lowercase());
            }
        }

        println!(
            "[ConfigUtil] Extracted {} AppSites from folder scan",
            app_sites.len()
        );
        app_sites
    }

    /// Discovers scenarios by scanning AppSites folder structure
    fn load_scenarios_internal(wwwroot_path: &str) -> Result<Vec<Scenario>, String> {
        let app_sites_path = Path::new(wwwroot_path).join("AppSites");

        if !app_sites_path.exists() {
            return Err(format!(
                "AppSites directory not found: {:?}",
                app_sites_path
            ));
        }

        let mut scenarios = Vec::new();

        // Get all directories in AppSites folder
        let entries = fs::read_dir(&app_sites_path)
            .map_err(|e| format!("Failed to read AppSites directory: {}", e))?;

        let mut app_site_dirs: Vec<String> = entries
            .filter_map(|e| e.ok())
            .filter(|e| e.path().is_dir())
            .filter_map(|e| e.file_name().into_string().ok())
            .collect();

        app_site_dirs.sort();

        for app_site in app_site_dirs {
            // Get all HTML files in the appSite directory (top level only)
            let app_site_dir = app_sites_path.join(&app_site);

            let html_files = fs::read_dir(&app_site_dir)
                .map_err(|e| format!("Failed to read AppSite directory {}: {}", app_site, e))?
                .filter_map(|e| e.ok())
                .filter(|e| {
                    e.path().is_file()
                        && e.path()
                            .extension()
                            .map(|ext| ext == "html")
                            .unwrap_or(false)
                })
                .collect::<Vec<_>>();

            // If no HTML files found, use DEFAULT_APP_FILE
            if html_files.is_empty() {
                // Create a dummy entry for DEFAULT_APP_FILE
                let app_file = DEFAULT_APP_FILE.to_string();

                // Check for Views folder
                let views_path = app_site_dir.join("Views");
                let mut view_dirs = Vec::new();

                if views_path.exists() && views_path.is_dir() {
                    // Get all subdirectories in Views folder
                    if let Ok(view_entries) = fs::read_dir(&views_path) {
                        view_dirs = view_entries
                            .filter_map(|e| e.ok())
                            .filter(|e| e.path().is_dir())
                            .filter_map(|e| e.file_name().into_string().ok())
                            .collect();
                    }
                }

                // Only add empty AppView scenario if no specific Views exist
                if view_dirs.is_empty() {
                    scenarios.push(Scenario::new(
                        app_site.clone(),
                        app_file.clone(),
                        String::new(),
                    ));
                } else {
                    // Add specific view scenarios
                    for view_dir in view_dirs {
                        scenarios.push(Scenario::new(app_site.clone(), app_file.clone(), view_dir));
                    }
                }
            } else {
                for html_file in html_files {
                    let app_file = html_file
                        .path()
                        .file_stem()
                        .and_then(|s| s.to_str())
                        .unwrap_or("")
                        .to_string();

                    // Check for Views folder
                    let views_path = app_site_dir.join("Views");
                    let mut view_dirs = Vec::new();

                    if views_path.exists() && views_path.is_dir() {
                        // Get all subdirectories in Views folder
                        if let Ok(view_entries) = fs::read_dir(&views_path) {
                            view_dirs = view_entries
                                .filter_map(|e| e.ok())
                                .filter(|e| e.path().is_dir())
                                .filter_map(|e| e.file_name().into_string().ok())
                                .collect();
                        }
                    }

                    // Only add empty AppView scenario if no specific Views exist
                    if view_dirs.is_empty() {
                        scenarios.push(Scenario::new(
                            app_site.clone(),
                            app_file.clone(),
                            String::new(),
                        ));
                    } else {
                        // Add specific view scenarios
                        for view_dir in view_dirs {
                            scenarios.push(Scenario::new(
                                app_site.clone(),
                                app_file.clone(),
                                view_dir,
                            ));
                        }
                    }
                }
            }
        }

        if scenarios.is_empty() {
            return Err("No scenarios found in AppSites folder".to_string());
        }

        println!(
            "[ConfigUtil] Loaded {} scenarios from AppSites folder",
            scenarios.len()
        );

        Ok(scenarios)
    }

    /// Loads AppSites from wwwroot path and caches them. Call this during startup.
    pub fn load(wwwroot_path: &str) -> Result<(), String> {
        let mut cache = CONFIG_CACHE.lock().unwrap();

        let scenarios = Self::load_scenarios_internal(wwwroot_path)?;
        let app_sites = Self::extract_app_sites_from_scenarios(&scenarios);

        cache.wwwroot_path = Some(wwwroot_path.to_string());
        cache.scenarios = Some(scenarios);
        cache.app_sites = Some(app_sites);

        Ok(())
    }

    /// Reloads AppSites and Scenarios from the stored wwwroot path. Throws if not loaded.
    pub fn reload() -> Result<(), String> {
        let mut cache = CONFIG_CACHE.lock().unwrap();

        let wwwroot_path = cache
            .wwwroot_path
            .clone()
            .ok_or_else(|| "ConfigUtil not loaded. Call load(wwwroot_path) first.".to_string())?;

        let scenarios = Self::load_scenarios_internal(&wwwroot_path)?;
        let app_sites = Self::extract_app_sites_from_scenarios(&scenarios);

        cache.scenarios = Some(scenarios);
        cache.app_sites = Some(app_sites);

        Ok(())
    }

    /// Gets the cached AppSites. Throws if not loaded.
    pub fn get_app_sites() -> Result<HashSet<String>, String> {
        let cache = CONFIG_CACHE.lock().unwrap();
        cache
            .app_sites
            .clone()
            .ok_or_else(|| "AppSitesConfig not loaded. Call load(wwwroot_path) first.".to_string())
    }

    /// Gets the cached Scenarios. Throws if not loaded.
    pub fn get_scenarios() -> Result<Vec<Scenario>, String> {
        let cache = CONFIG_CACHE.lock().unwrap();
        cache
            .scenarios
            .clone()
            .ok_or_else(|| "AppSitesConfig not loaded. Call load(wwwroot_path) first.".to_string())
    }

    /// Filters scenarios by appSite
    pub fn filter_by_app_site(scenarios: &[Scenario], app_site_filter: &str) -> Vec<Scenario> {
        if app_site_filter.is_empty() {
            return scenarios.to_vec();
        }

        scenarios
            .iter()
            .filter(|s| s.app_site.eq_ignore_ascii_case(app_site_filter))
            .cloned()
            .collect()
    }
}

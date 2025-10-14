use std::path::Path;
use std::fs;
use std::sync::Mutex;
use once_cell::sync::Lazy;

#[derive(Debug, Clone)]
pub struct Scenario {
    pub app_site: String,
    pub app_file: String,
    pub app_view: String,
    pub total_size: i32,
    pub display_name: String,
    pub description: String,
}

impl Scenario {
    pub fn new(app_site: String, app_file: String, app_view: String, total_size: i32, display_name: String, description: String) -> Self {
        Scenario {
            app_site,
            app_file,
            app_view,
            total_size,
            display_name,
            description,
        }
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
    app_sites: Option<std::collections::HashSet<String>>,
    scenarios: Option<Vec<Scenario>>,
}

pub struct ConfigUtil;

impl ConfigUtil {
    /// Loads AppSites and scenarios from wwwroot path and caches them
    pub fn load(wwwroot_path: &str) -> Result<(), String> {
        let app_sites = Self::load_app_sites_internal(wwwroot_path)?;
        let scenarios = Self::load_scenarios_internal(wwwroot_path)?;

        let mut cache = CONFIG_CACHE.lock().unwrap();
        cache.wwwroot_path = Some(wwwroot_path.to_string());
        cache.app_sites = Some(app_sites);
        cache.scenarios = Some(scenarios);
        Ok(())
    }

    /// Gets the cached AppSites
    pub fn get_app_sites() -> Result<std::collections::HashSet<String>, String> {
        let cache = CONFIG_CACHE.lock().unwrap();
        cache.app_sites.clone().ok_or_else(|| "ConfigUtil not loaded. Call load(wwwroot_path) first.".to_string())
    }

    /// Gets the cached scenarios
    pub fn get_scenarios() -> Result<Vec<Scenario>, String> {
        let cache = CONFIG_CACHE.lock().unwrap();
        cache.scenarios.clone().ok_or_else(|| "ConfigUtil not loaded. Call load(wwwroot_path) first.".to_string())
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

    fn load_app_sites_internal(wwwroot_path: &str) -> Result<std::collections::HashSet<String>, String> {
        let app_data_path = Path::new(wwwroot_path).join("App_Data");
        let csv_file_path = app_data_path.join("appsites.csv");

        // Generate if doesn't exist
        if !csv_file_path.exists() {
            println!("[ConfigUtil] appsites.csv not found, generating...");
            Self::generate_app_sites_csv(wwwroot_path)?;
        }

        // Read CSV
        let csv_content = fs::read_to_string(&csv_file_path)
            .map_err(|e| format!("Failed to read appsites.csv: {}", e))?;

        let csv_content = csv_content.trim();
        if csv_content.is_empty() {
            return Err("appsites.csv is empty".to_string());
        }

        let app_sites: std::collections::HashSet<String> = csv_content
            .split(',')
            .map(|s| s.trim().to_string())
            .filter(|s| !s.is_empty())
            .collect();

        if app_sites.is_empty() {
            return Err("No AppSites found in appsites.csv".to_string());
        }

        println!("[ConfigUtil] Loaded {} AppSites from appsites.csv", app_sites.len());

        Ok(app_sites)
    }

    fn load_scenarios_internal(wwwroot_path: &str) -> Result<Vec<Scenario>, String> {
        let app_data_path = Path::new(wwwroot_path).join("App_Data");
        let csv_file_path = app_data_path.join("scenarios.csv");

        // Generate if doesn't exist
        if !csv_file_path.exists() {
            println!("[ConfigUtil] scenarios.csv not found, generating...");
            Self::generate_scenarios_csv(wwwroot_path)?;
        }

        // Read CSV
        let csv_content = fs::read_to_string(&csv_file_path)
            .map_err(|e| format!("Failed to read scenarios.csv: {}", e))?;

        let lines: Vec<&str> = csv_content.lines().collect();
        if lines.is_empty() {
            return Err("scenarios.csv is empty".to_string());
        }

        let mut scenarios = Vec::new();

        // Check if first line is header
        let has_header = lines[0].contains("AppSite") && lines[0].contains("AppFile");
        let start_line = if has_header { 1 } else { 0 };

        for line in lines.iter().skip(start_line) {
            let line = line.trim();
            if line.is_empty() {
                continue;
            }

            let parts = Self::parse_csv_line(line);
            if parts.len() >= 2 {
                let app_site = parts[0].trim().to_string();
                let app_file = parts[1].trim().to_string();
                let app_view = if parts.len() > 2 { parts[2].trim().to_string() } else { String::new() };
                let total_size = if parts.len() > 3 { parts[3].trim().parse().unwrap_or(0) } else { 0 };
                let display_name = if parts.len() > 4 { parts[4].trim().trim_matches('"').to_string() } else { String::new() };
                let description = if parts.len() > 5 { parts[5].trim().trim_matches('"').to_string() } else { String::new() };

                scenarios.push(Scenario::new(app_site, app_file, app_view, total_size, display_name, description));
            }
        }

        if scenarios.is_empty() {
            return Err("No scenarios found in scenarios.csv".to_string());
        }

        println!("[ConfigUtil] Loaded {} scenarios from scenarios.csv", scenarios.len());

        Ok(scenarios)
    }

    fn parse_csv_line(line: &str) -> Vec<String> {
        let mut result = Vec::new();
        let mut current = String::new();
        let mut in_quotes = false;

        for c in line.chars() {
            match c {
                '"' => in_quotes = !in_quotes,
                ',' if !in_quotes => {
                    result.push(current.clone());
                    current.clear();
                }
                _ => current.push(c),
            }
        }

        result.push(current);
        result
    }

    fn generate_scenarios_csv(wwwroot_path: &str) -> Result<(), String> {
        let app_sites_path = Path::new(wwwroot_path).join("AppSites");
        let app_data_path = Path::new(wwwroot_path).join("App_Data");
        let csv_file_path = app_data_path.join("scenarios.csv");

        if !app_sites_path.exists() {
            return Err(format!("AppSites directory not found: {:?}", app_sites_path));
        }

        // Ensure App_Data exists
        fs::create_dir_all(&app_data_path)
            .map_err(|e| format!("Failed to create App_Data directory: {}", e))?;

        let mut scenarios = Vec::new();

        // Read all AppSite directories
        if let Ok(entries) = fs::read_dir(&app_sites_path) {
            for entry in entries.filter_map(|e| e.ok()) {
                if !entry.path().is_dir() {
                    continue;
                }

                let app_site = entry.file_name().to_string_lossy().to_string();

                // Get all HTML files
                if let Ok(files) = fs::read_dir(entry.path()) {
                    for file_entry in files.filter_map(|e| e.ok()) {
                        if let Some(extension) = file_entry.path().extension() {
                            if extension == "html" {
                                let app_file = file_entry.path()
                                    .file_stem()
                                    .unwrap()
                                    .to_string_lossy()
                                    .to_string();

                                // Add default scenario
                                scenarios.push(Scenario::new(
                                    app_site.clone(),
                                    app_file.clone(),
                                    String::new(),
                                    0,
                                    String::new(),
                                    String::new()
                                ));

                                // Check for Views
                                let views_path = entry.path().join("Views");
                                if views_path.exists() {
                                    if let Ok(view_files) = fs::read_dir(&views_path) {
                                        for view_file in view_files.filter_map(|e| e.ok()) {
                                            if let Some(ext) = view_file.path().extension() {
                                                if ext == "html" {
                                                    let view_name = view_file.path()
                                                        .file_stem()
                                                        .unwrap()
                                                        .to_string_lossy()
                                                        .to_lowercase();

                                                    if view_name.contains("content") {
                                                        if let Some(content_index) = view_name.find("content") {
                                                            if content_index > 0 {
                                                                let view_part = &view_name[..content_index];
                                                                if !view_part.is_empty() {
                                                                    let mut app_view = String::new();
                                                                    app_view.push(view_part.chars().next().unwrap().to_uppercase().next().unwrap());
                                                                    app_view.push_str(&view_part[1..]);

                                                                    scenarios.push(Scenario::new(
                                                                        app_site.clone(),
                                                                        app_file.clone(),
                                                                        app_view,
                                                                        0,
                                                                        String::new(),
                                                                        String::new()
                                                                    ));
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Write CSV
        let mut csv_lines = vec!["AppSite,AppFile,AppView,TotalSize,DisplayName,Description".to_string()];
        for scenario in &scenarios {
            csv_lines.push(format!(
                "{},{},{},{},\"{}\",\"{}\"",
                scenario.app_site,
                scenario.app_file,
                scenario.app_view,
                scenario.total_size,
                scenario.display_name,
                scenario.description
            ));
        }

        fs::write(&csv_file_path, csv_lines.join("\n"))
            .map_err(|e| format!("Failed to write scenarios.csv: {}", e))?;

        println!("[ConfigUtil] Generated scenarios.csv with {} scenarios", scenarios.len());
        Ok(())
    }

    fn generate_app_sites_csv(wwwroot_path: &str) -> Result<(), String> {
        let app_sites_path = Path::new(wwwroot_path).join("AppSites");
        let app_data_path = Path::new(wwwroot_path).join("App_Data");
        let csv_file_path = app_data_path.join("appsites.csv");

        if !app_sites_path.exists() {
            return Err(format!("AppSites directory not found: {:?}", app_sites_path));
        }

        // Ensure App_Data exists
        fs::create_dir_all(&app_data_path)
            .map_err(|e| format!("Failed to create App_Data directory: {}", e))?;

        // Get all directories in AppSites folder
        let mut app_sites = Vec::new();
        if let Ok(entries) = fs::read_dir(&app_sites_path) {
            for entry in entries.filter_map(|e| e.ok()) {
                if entry.path().is_dir() {
                    if let Some(dir_name) = entry.file_name().to_str() {
                        app_sites.push(dir_name.to_string());
                    }
                }
            }
        }

        // Add Index if not present
        if !app_sites.iter().any(|s| s.eq_ignore_ascii_case("Index")) {
            app_sites.push("Index".to_string());
        }

        // Sort app sites
        app_sites.sort();

        // Write as CSV (comma-delimited)
        let csv = app_sites.join(",");
        fs::write(&csv_file_path, csv)
            .map_err(|e| format!("Failed to write appsites.csv: {}", e))?;

        println!("[ConfigUtil] Generated appsites.csv with {} AppSites", app_sites.len());
        Ok(())
    }
}

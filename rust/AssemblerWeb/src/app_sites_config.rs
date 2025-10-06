use std::collections::HashSet;
use std::fs;
use std::path::Path;

/// Discovers AppSites by scanning the AppSites folder and generates appsites.csv
fn generate_app_sites_csv(wwwroot_path: &str) -> Result<(), String> {
    let app_sites_path = Path::new(wwwroot_path).join("AppSites");
    let app_data_path = Path::new(wwwroot_path).join("App_Data");
    let csv_file_path = app_data_path.join("appsites.csv");

    if !app_sites_path.exists() {
        return Err(format!("AppSites directory not found: {:?}", app_sites_path));
    }

    // Ensure App_Data directory exists
    if !app_data_path.exists() {
        fs::create_dir_all(&app_data_path)
            .map_err(|e| format!("Failed to create App_Data directory: {}", e))?;
    }

    // Get all directories in AppSites folder
    let mut app_sites: Vec<String> = fs::read_dir(&app_sites_path)
        .map_err(|e| format!("Failed to read AppSites directory: {}", e))?
        .filter_map(|entry| entry.ok())
        .filter(|entry| entry.path().is_dir())
        .filter_map(|entry| {
            entry.file_name().to_str().map(|s| s.to_string())
        })
        .collect();

    // Add Index as it's a valid AppSite
    if !app_sites.iter().any(|s| s.eq_ignore_ascii_case("Index")) {
        app_sites.push("Index".to_string());
    }

    // Sort for consistency
    app_sites.sort();

    // Write as CSV (comma-delimited)
    let csv = app_sites.join(",");
    fs::write(&csv_file_path, csv)
        .map_err(|e| format!("Failed to write appsites.csv: {}", e))?;

    println!("[AppSitesConfig] Generated appsites.csv with {} AppSites", app_sites.len());
    Ok(())
}

/// Loads AppSites from appsites.csv, generates it if it doesn't exist
pub fn load_app_sites(wwwroot_path: &str) -> Result<HashSet<String>, String> {
    let app_data_path = Path::new(wwwroot_path).join("App_Data");
    let csv_file_path = app_data_path.join("appsites.csv");

    // Generate appsites.csv if it doesn't exist
    if !csv_file_path.exists() {
        println!("[AppSitesConfig] appsites.csv not found, generating...");
        generate_app_sites_csv(wwwroot_path)?;
    }

    // Read and parse CSV
    let csv = fs::read_to_string(&csv_file_path)
        .map_err(|e| format!("Failed to read appsites.csv: {}", e))?;

    let csv = csv.trim();
    if csv.is_empty() {
        return Err("appsites.csv is empty".to_string());
    }

    let app_sites: Vec<String> = csv.split(',')
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
        .collect();

    if app_sites.is_empty() {
        return Err("No AppSites found in appsites.csv".to_string());
    }

    println!("[AppSitesConfig] Loaded {} AppSites from appsites.csv", app_sites.len());

    // Use case-insensitive comparison
    Ok(app_sites.into_iter().collect())
}

/// Reloads AppSites by regenerating appsites.csv from the file system
#[allow(dead_code)]
pub fn reload_app_sites(wwwroot_path: &str) -> Result<HashSet<String>, String> {
    println!("[AppSitesConfig] Reloading AppSites...");
    generate_app_sites_csv(wwwroot_path)?;
    load_app_sites(wwwroot_path)
}

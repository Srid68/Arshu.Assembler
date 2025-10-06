package main

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// GenerateAppSitesCsv discovers AppSites by scanning the AppSites folder and generates appsites.csv
func GenerateAppSitesCsv(wwwrootPath string) error {
	appSitesPath := filepath.Join(wwwrootPath, "AppSites")
	appDataPath := filepath.Join(wwwrootPath, "App_Data")
	csvFilePath := filepath.Join(appDataPath, "appsites.csv")

	if _, err := os.Stat(appSitesPath); os.IsNotExist(err) {
		return fmt.Errorf("AppSites directory not found: %s", appSitesPath)
	}

	// Ensure App_Data directory exists
	if err := os.MkdirAll(appDataPath, 0755); err != nil {
		return fmt.Errorf("failed to create App_Data directory: %w", err)
	}

	// Get all directories in AppSites folder
	entries, err := os.ReadDir(appSitesPath)
	if err != nil {
		return fmt.Errorf("failed to read AppSites directory: %w", err)
	}

	var appSites []string
	for _, entry := range entries {
		if entry.IsDir() {
			appSites = append(appSites, entry.Name())
		}
	}

	// Add Index as it's a valid AppSite
	hasIndex := false
	for _, site := range appSites {
		if strings.EqualFold(site, "Index") {
			hasIndex = true
			break
		}
	}
	if !hasIndex {
		appSites = append(appSites, "Index")
	}

	// Sort for consistency
	sort.Strings(appSites)

	// Write as CSV (comma-delimited)
	csv := strings.Join(appSites, ",")
	if err := os.WriteFile(csvFilePath, []byte(csv), 0644); err != nil {
		return fmt.Errorf("failed to write appsites.csv: %w", err)
	}

	fmt.Printf("[AppSitesConfig] Generated appsites.csv with %d AppSites\n", len(appSites))
	return nil
}

// LoadAppSites loads AppSites from appsites.csv, generates it if it doesn't exist
func LoadAppSites(wwwrootPath string) (map[string]bool, error) {
	appDataPath := filepath.Join(wwwrootPath, "App_Data")
	csvFilePath := filepath.Join(appDataPath, "appsites.csv")

	// Generate appsites.csv if it doesn't exist
	if _, err := os.Stat(csvFilePath); os.IsNotExist(err) {
		fmt.Println("[AppSitesConfig] appsites.csv not found, generating...")
		if err := GenerateAppSitesCsv(wwwrootPath); err != nil {
			return nil, err
		}
	}

	// Read and parse CSV
	data, err := os.ReadFile(csvFilePath)
	if err != nil {
		return nil, fmt.Errorf("failed to read appsites.csv: %w", err)
	}

	csv := strings.TrimSpace(string(data))
	if csv == "" {
		return nil, fmt.Errorf("appsites.csv is empty")
	}

	parts := strings.Split(csv, ",")
	appSites := make(map[string]bool)
	for _, part := range parts {
		trimmed := strings.TrimSpace(part)
		if trimmed != "" {
			appSites[trimmed] = true
		}
	}

	if len(appSites) == 0 {
		return nil, fmt.Errorf("no AppSites found in appsites.csv")
	}

	fmt.Printf("[AppSitesConfig] Loaded %d AppSites from appsites.csv\n", len(appSites))
	return appSites, nil
}

// ReloadAppSites reloads AppSites by regenerating appsites.csv from the file system
func ReloadAppSites(wwwrootPath string) (map[string]bool, error) {
	fmt.Println("[AppSitesConfig] Reloading AppSites...")
	if err := GenerateAppSitesCsv(wwwrootPath); err != nil {
		return nil, err
	}
	return LoadAppSites(wwwrootPath)
}

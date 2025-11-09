package config

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
)

type Scenario struct {
	AppSite string
	AppFile string
	AppView string
}

func NewScenario(appSite, appFile, appView string) Scenario {
	return Scenario{
		AppSite: appSite,
		AppFile: appFile,
		AppView: appView,
	}
}

func (s *Scenario) ToString() string {
	return fmt.Sprintf("%s:%s:%s", s.AppSite, s.AppFile, s.AppView)
}

type configCache struct {
	wwwrootPath string
	appSites    map[string]bool
	scenarios   []Scenario
}

var (
	cache      *configCache
	cacheMutex sync.Mutex
)

type ConfigUtil struct{}

const DefaultAppFile = "Index"

// extractAppSitesFromScenarios extracts unique AppSites from scenarios
func extractAppSitesFromScenarios(scenarios []Scenario) map[string]bool {
	appSitesMap := make(map[string]bool)
	for _, scenario := range scenarios {
		if scenario.AppSite != "" {
			appSitesMap[strings.ToLower(scenario.AppSite)] = true
		}
	}

	fmt.Printf("[ConfigUtil] Extracted %d AppSites from folder scan\n", len(appSitesMap))
	return appSitesMap
}

// loadScenariosInternal discovers scenarios by scanning AppSites folder structure
func loadScenariosInternal(wwwrootPath string) ([]Scenario, error) {
	appSitesPath := filepath.Join(wwwrootPath, "AppSites")

	if _, err := os.Stat(appSitesPath); os.IsNotExist(err) {
		return nil, fmt.Errorf("AppSites directory not found: %s", appSitesPath)
	}

	scenarios := []Scenario{}

	// Get all directories in AppSites folder
	entries, err := os.ReadDir(appSitesPath)
	if err != nil {
		return nil, fmt.Errorf("Failed to read AppSites directory: %v", err)
	}

	var appSiteDirs []string
	for _, entry := range entries {
		if entry.IsDir() {
			appSiteDirs = append(appSiteDirs, entry.Name())
		}
	}
	sort.Strings(appSiteDirs)

	for _, appSite := range appSiteDirs {
		// Get all HTML files in the appSite directory (top level only)
		appSiteDir := filepath.Join(appSitesPath, appSite)

		files, err := os.ReadDir(appSiteDir)
		if err != nil {
			continue
		}

		var htmlFiles []string
		for _, file := range files {
			if file.IsDir() || !strings.HasSuffix(file.Name(), ".html") {
				continue
			}
			htmlFiles = append(htmlFiles, file.Name())
		}

		// If no HTML files found, use DefaultAppFile
		if len(htmlFiles) == 0 {
			htmlFiles = []string{DefaultAppFile}
		}

		for _, fileName := range htmlFiles {
			var appFile string
			if fileName == DefaultAppFile {
				appFile = DefaultAppFile
			} else {
				appFile = strings.TrimSuffix(fileName, ".html")
			}

			// Check for Views folder
			viewsPath := filepath.Join(appSiteDir, "Views")
			var viewDirs []string

			if viewsInfo, err := os.Stat(viewsPath); err == nil && viewsInfo.IsDir() {
				// Get all subdirectories in Views folder
				if viewEntries, err := os.ReadDir(viewsPath); err == nil {
					for _, viewEntry := range viewEntries {
						if viewEntry.IsDir() {
							viewDirs = append(viewDirs, viewEntry.Name())
						}
					}
				}
			}

			// Only add empty AppView scenario if no specific Views exist
			if len(viewDirs) == 0 {
				scenarios = append(scenarios, NewScenario(appSite, appFile, ""))
			} else {
				// Add specific view scenarios
				for _, viewDir := range viewDirs {
					scenarios = append(scenarios, NewScenario(appSite, appFile, viewDir))
				}
			}
		}
	}

	if len(scenarios) == 0 {
		return nil, fmt.Errorf("No scenarios found in AppSites folder")
	}

	fmt.Printf("[ConfigUtil] Loaded %d scenarios from AppSites folder\n", len(scenarios))

	return scenarios, nil
}

// Load loads AppSites from wwwroot path and caches them. Call this during startup.
func Load(wwwrootPath string) error {
	cacheMutex.Lock()
	defer cacheMutex.Unlock()

	scenarios, err := loadScenariosInternal(wwwrootPath)
	if err != nil {
		return err
	}

	appSites := extractAppSitesFromScenarios(scenarios)

	cache = &configCache{
		wwwrootPath: wwwrootPath,
		scenarios:   scenarios,
		appSites:    appSites,
	}

	return nil
}

// Reload reloads AppSites and Scenarios from the stored wwwroot path. Throws if not loaded.
func Reload() error {
	cacheMutex.Lock()
	defer cacheMutex.Unlock()

	if cache == nil {
		return fmt.Errorf("ConfigUtil not loaded. Call Load(wwwrootPath) first.")
	}

	scenarios, err := loadScenariosInternal(cache.wwwrootPath)
	if err != nil {
		return err
	}

	appSites := extractAppSitesFromScenarios(scenarios)

	cache.scenarios = scenarios
	cache.appSites = appSites

	return nil
}

// GetAppSites gets the cached AppSites. Throws if not loaded.
func GetAppSites() (map[string]bool, error) {
	cacheMutex.Lock()
	defer cacheMutex.Unlock()

	if cache == nil {
		return nil, fmt.Errorf("AppSitesConfig not loaded. Call Load(wwwrootPath) first.")
	}

	return cache.appSites, nil
}

// GetScenarios gets the cached Scenarios. Throws if not loaded.
func GetScenarios() ([]Scenario, error) {
	cacheMutex.Lock()
	defer cacheMutex.Unlock()

	if cache == nil {
		return nil, fmt.Errorf("AppSitesConfig not loaded. Call Load(wwwrootPath) first.")
	}

	return cache.scenarios, nil
}

// FilterByAppSite filters scenarios by appSite
func FilterByAppSite(scenarios []Scenario, appSiteFilter string) []Scenario {
	if appSiteFilter == "" {
		return scenarios
	}

	filtered := []Scenario{}
	for _, s := range scenarios {
		if strings.EqualFold(s.AppSite, appSiteFilter) {
			filtered = append(filtered, s)
		}
	}
	return filtered
}

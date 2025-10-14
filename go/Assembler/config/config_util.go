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
	AppSite     string
	AppFile     string
	AppView     string
	TotalSize   int
	DisplayName string
	Description string
}

func NewScenario(appSite, appFile, appView string, totalSize int, displayName, description string) Scenario {
	return Scenario{
		AppSite:     appSite,
		AppFile:     appFile,
		AppView:     appView,
		TotalSize:   totalSize,
		DisplayName: displayName,
		Description: description,
	}
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

// Load loads AppSites and scenarios from wwwroot path and caches them
func Load(wwwrootPath string) error {
	appSites, err := loadAppSitesInternal(wwwrootPath)
	if err != nil {
		return err
	}

	scenarios, err := loadScenariosInternal(wwwrootPath)
	if err != nil {
		return err
	}

	cacheMutex.Lock()
	defer cacheMutex.Unlock()

	cache = &configCache{
		wwwrootPath: wwwrootPath,
		appSites:    appSites,
		scenarios:   scenarios,
	}

	return nil
}

// GetAppSites gets the cached AppSites
func GetAppSites() (map[string]bool, error) {
	cacheMutex.Lock()
	defer cacheMutex.Unlock()

	if cache == nil {
		return nil, fmt.Errorf("ConfigUtil not loaded. Call Load(wwwrootPath) first.")
	}

	return cache.appSites, nil
}

// GetScenarios gets the cached scenarios
func GetScenarios() ([]Scenario, error) {
	cacheMutex.Lock()
	defer cacheMutex.Unlock()

	if cache == nil {
		return nil, fmt.Errorf("ConfigUtil not loaded. Call Load(wwwrootPath) first.")
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

func loadAppSitesInternal(wwwrootPath string) (map[string]bool, error) {
	appDataPath := filepath.Join(wwwrootPath, "App_Data")
	csvFilePath := filepath.Join(appDataPath, "appsites.csv")

	// Generate if doesn't exist
	if _, err := os.Stat(csvFilePath); os.IsNotExist(err) {
		fmt.Println("[ConfigUtil] appsites.csv not found, generating...")
		if err := generateAppSitesCsv(wwwrootPath); err != nil {
			return nil, err
		}
	}

	// Read CSV
	csvContent, err := os.ReadFile(csvFilePath)
	if err != nil {
		return nil, fmt.Errorf("Failed to read appsites.csv: %v", err)
	}

	csvStr := strings.TrimSpace(string(csvContent))
	if csvStr == "" {
		return nil, fmt.Errorf("appsites.csv is empty")
	}

	appSitesMap := make(map[string]bool)
	parts := strings.Split(csvStr, ",")
	for _, part := range parts {
		appSite := strings.TrimSpace(part)
		if appSite != "" {
			appSitesMap[appSite] = true
		}
	}

	if len(appSitesMap) == 0 {
		return nil, fmt.Errorf("No AppSites found in appsites.csv")
	}

	fmt.Printf("[ConfigUtil] Loaded %d AppSites from appsites.csv\n", len(appSitesMap))

	return appSitesMap, nil
}

func loadScenariosInternal(wwwrootPath string) ([]Scenario, error) {
	appDataPath := filepath.Join(wwwrootPath, "App_Data")
	csvFilePath := filepath.Join(appDataPath, "scenarios.csv")

	// Generate if doesn't exist
	if _, err := os.Stat(csvFilePath); os.IsNotExist(err) {
		fmt.Println("[ConfigUtil] scenarios.csv not found, generating...")
		if err := generateScenariosCsv(wwwrootPath); err != nil {
			return nil, err
		}
	}

	// Read CSV
	csvContent, err := os.ReadFile(csvFilePath)
	if err != nil {
		return nil, fmt.Errorf("Failed to read scenarios.csv: %v", err)
	}

	lines := strings.Split(string(csvContent), "\n")
	if len(lines) == 0 {
		return nil, fmt.Errorf("scenarios.csv is empty")
	}

	scenarios := []Scenario{}

	// Check if first line is header
	hasHeader := strings.Contains(lines[0], "AppSite") && strings.Contains(lines[0], "AppFile")
	startLine := 0
	if hasHeader {
		startLine = 1
	}

	for i := startLine; i < len(lines); i++ {
		line := strings.TrimSpace(lines[i])
		if line == "" {
			continue
		}

		parts := parseCsvLine(line)
		if len(parts) >= 2 {
			appSite := strings.TrimSpace(parts[0])
			appFile := strings.TrimSpace(parts[1])
			appView := ""
			if len(parts) > 2 {
				appView = strings.TrimSpace(parts[2])
			}
			totalSize := 0
			if len(parts) > 3 {
				fmt.Sscanf(parts[3], "%d", &totalSize)
			}
			displayName := ""
			if len(parts) > 4 {
				displayName = strings.Trim(strings.TrimSpace(parts[4]), "\"")
			}
			description := ""
			if len(parts) > 5 {
				description = strings.Trim(strings.TrimSpace(parts[5]), "\"")
			}

			scenarios = append(scenarios, NewScenario(appSite, appFile, appView, totalSize, displayName, description))
		}
	}

	if len(scenarios) == 0 {
		return nil, fmt.Errorf("No scenarios found in scenarios.csv")
	}

	fmt.Printf("[ConfigUtil] Loaded %d scenarios from scenarios.csv\n", len(scenarios))

	return scenarios, nil
}

func parseCsvLine(line string) []string {
	result := []string{}
	current := ""
	inQuotes := false

	for _, c := range line {
		switch c {
		case '"':
			inQuotes = !inQuotes
		case ',':
			if !inQuotes {
				result = append(result, current)
				current = ""
			} else {
				current += string(c)
			}
		default:
			current += string(c)
		}
	}

	result = append(result, current)
	return result
}

func generateScenariosCsv(wwwrootPath string) error {
	appSitesPath := filepath.Join(wwwrootPath, "AppSites")
	appDataPath := filepath.Join(wwwrootPath, "App_Data")
	csvFilePath := filepath.Join(appDataPath, "scenarios.csv")

	if _, err := os.Stat(appSitesPath); os.IsNotExist(err) {
		return fmt.Errorf("AppSites directory not found: %s", appSitesPath)
	}

	// Ensure App_Data exists
	if err := os.MkdirAll(appDataPath, 0755); err != nil {
		return fmt.Errorf("Failed to create App_Data directory: %v", err)
	}

	scenarios := []Scenario{}

	// Read all AppSite directories
	entries, err := os.ReadDir(appSitesPath)
	if err != nil {
		return fmt.Errorf("Failed to read AppSites directory: %v", err)
	}

	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}

		appSite := entry.Name()

		// Get all HTML files
		appSiteDir := filepath.Join(appSitesPath, appSite)
		files, err := os.ReadDir(appSiteDir)
		if err != nil {
			continue
		}

		for _, file := range files {
			if file.IsDir() || !strings.HasSuffix(file.Name(), ".html") {
				continue
			}

			appFile := strings.TrimSuffix(file.Name(), ".html")

			// Add default scenario
			scenarios = append(scenarios, NewScenario(appSite, appFile, "", 0, "", ""))

			// Check for Views
			viewsPath := filepath.Join(appSiteDir, "Views")
			if viewsInfo, err := os.Stat(viewsPath); err == nil && viewsInfo.IsDir() {
				viewFiles, err := os.ReadDir(viewsPath)
				if err == nil {
					for _, viewFile := range viewFiles {
						if viewFile.IsDir() || !strings.HasSuffix(viewFile.Name(), ".html") {
							continue
						}

						viewName := strings.ToLower(strings.TrimSuffix(viewFile.Name(), ".html"))
						if strings.Contains(viewName, "content") {
							contentIndex := strings.Index(viewName, "content")
							if contentIndex > 0 {
								viewPart := viewName[:contentIndex]
								if len(viewPart) > 0 {
									appView := strings.ToUpper(string(viewPart[0])) + viewPart[1:]
									scenarios = append(scenarios, NewScenario(appSite, appFile, appView, 0, "", ""))
								}
							}
						}
					}
				}
			}
		}
	}

	// Write CSV
	csvLines := []string{"AppSite,AppFile,AppView,TotalSize,DisplayName,Description"}
	for _, scenario := range scenarios {
		csvLines = append(csvLines, fmt.Sprintf(
			"%s,%s,%s,%d,\"%s\",\"%s\"",
			scenario.AppSite,
			scenario.AppFile,
			scenario.AppView,
			scenario.TotalSize,
			scenario.DisplayName,
			scenario.Description,
		))
	}

	if err := os.WriteFile(csvFilePath, []byte(strings.Join(csvLines, "\n")), 0644); err != nil {
		return fmt.Errorf("Failed to write scenarios.csv: %v", err)
	}

	fmt.Printf("[ConfigUtil] Generated scenarios.csv with %d scenarios\n", len(scenarios))
	return nil
}

func generateAppSitesCsv(wwwrootPath string) error {
	appSitesPath := filepath.Join(wwwrootPath, "AppSites")
	appDataPath := filepath.Join(wwwrootPath, "App_Data")
	csvFilePath := filepath.Join(appDataPath, "appsites.csv")

	if _, err := os.Stat(appSitesPath); os.IsNotExist(err) {
		return fmt.Errorf("AppSites directory not found: %s", appSitesPath)
	}

	// Ensure App_Data exists
	if err := os.MkdirAll(appDataPath, 0755); err != nil {
		return fmt.Errorf("Failed to create App_Data directory: %v", err)
	}

	// Get all directories in AppSites folder
	entries, err := os.ReadDir(appSitesPath)
	if err != nil {
		return fmt.Errorf("Failed to read AppSites directory: %v", err)
	}

	appSites := []string{}
	for _, entry := range entries {
		if entry.IsDir() {
			appSites = append(appSites, entry.Name())
		}
	}

	// Add Index if not present
	hasIndex := false
	for _, name := range appSites {
		if strings.EqualFold(name, "Index") {
			hasIndex = true
			break
		}
	}
	if !hasIndex {
		appSites = append(appSites, "Index")
	}

	// Sort app sites
	sort.Strings(appSites)

	// Write as CSV (comma-delimited)
	csv := strings.Join(appSites, ",")
	if err := os.WriteFile(csvFilePath, []byte(csv), 0644); err != nil {
		return fmt.Errorf("Failed to write appsites.csv: %v", err)
	}

	fmt.Printf("[ConfigUtil] Generated appsites.csv with %d AppSites\n", len(appSites))
	return nil
}

package loader

import (
	"assembler/common"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

var htmlTemplatesCache = struct {
	sync.RWMutex
	cache map[string]map[string]struct {
		HTML string
		JSON *string
	}
}{cache: make(map[string]map[string]struct {
	HTML string
	JSON *string
})}

// LoadGetTemplateFiles loads HTML and corresponding JSON files from the specified appSite directory, caching the output per appSite
// Returns map[string]struct{HTML string; JSON *string} equivalent to C#'s (string, string?) and Rust's (String, Option<String>)
func LoadGetTemplateFiles(rootDirPath, appSite string) map[string]struct {
	HTML string
	JSON *string
} {
	common.Debug(fmt.Sprintf("LoadGetTemplateFiles called for appSite: %s", appSite), "LoaderNormal")

	cacheKey := fmt.Sprintf("%s|%s", filepath.Dir(rootDirPath), appSite)

	htmlTemplatesCache.RLock()
	if cached, ok := htmlTemplatesCache.cache[cacheKey]; ok {
		htmlTemplatesCache.RUnlock()
		common.Debug(fmt.Sprintf("Returning cached templates for %s (%d templates)", appSite, len(cached)), "LoaderNormal")
		return cached
	}
	htmlTemplatesCache.RUnlock()

	result := make(map[string]struct {
		HTML string
		JSON *string
	})
	appSitesPath := filepath.Join(rootDirPath, "AppSites", appSite)
	if stat, err := os.Stat(appSitesPath); err != nil || !stat.IsDir() {
		common.Warn(fmt.Sprintf("AppSites directory not found: %s", appSitesPath), "LoaderNormal")
		htmlTemplatesCache.Lock()
		htmlTemplatesCache.cache[cacheKey] = result
		htmlTemplatesCache.Unlock()
		return result
	}

	common.Debug(fmt.Sprintf("Loading templates from: %s", appSitesPath), "LoaderNormal")

	_ = filepath.Walk(appSitesPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(info.Name(), ".html") {
			return nil
		}
		fileName := strings.TrimSuffix(info.Name(), ".html")
		key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(fileName))
		f, _ := os.Open(path)
		htmlBytes, _ := io.ReadAll(f)
		f.Close()
		htmlContent := common.NormalizeFileContent(string(htmlBytes))

		common.Debug(fmt.Sprintf("Loading template: %s (html size: %d)", key, len(htmlContent)), "LoaderNormal")

		// Find JSON file case-insensitively
		jsonFile := strings.TrimSuffix(path, ".html") + ".json"
		var jsonContent *string
		if _, err := os.Stat(jsonFile); err == nil {
			jf, _ := os.Open(jsonFile)
			jsonBytes, _ := io.ReadAll(jf)
			jf.Close()
			jsonStr := common.NormalizeFileContent(string(jsonBytes))
			jsonContent = &jsonStr
			common.Debug(fmt.Sprintf("Found JSON file for %s (size: %d)", key, len(jsonStr)), "LoaderNormal")
		} else {
			// Try case-insensitive search in the same directory
			dir := filepath.Dir(path)
			baseName := strings.ToLower(strings.TrimSuffix(filepath.Base(path), ".html"))
			entries, err := os.ReadDir(dir)
			if err == nil {
				for _, entry := range entries {
					if !entry.IsDir() && strings.HasSuffix(strings.ToLower(entry.Name()), ".json") {
						entryBase := strings.ToLower(strings.TrimSuffix(entry.Name(), ".json"))
						if entryBase == baseName {
							matchedJsonPath := filepath.Join(dir, entry.Name())
							jf, _ := os.Open(matchedJsonPath)
							jsonBytes, _ := io.ReadAll(jf)
							jf.Close()
							jsonStr := common.NormalizeFileContent(string(jsonBytes))
							jsonContent = &jsonStr
							common.Debug(fmt.Sprintf("Found JSON file (case-insensitive) for %s (size: %d)", key, len(jsonStr)), "LoaderNormal")
							break
						}
					}
				}
			}
		}
		result[key] = struct {
			HTML string
			JSON *string
		}{HTML: htmlContent, JSON: jsonContent}
		return nil
	})

	common.Info(fmt.Sprintf("Loaded %d templates for %s", len(result), appSite), "LoaderNormal")

	htmlTemplatesCache.Lock()
	htmlTemplatesCache.cache[cacheKey] = result
	htmlTemplatesCache.Unlock()
	return result
}

// ClearCache clears all cached normal templates
func ClearCache() {
	htmlTemplatesCache.Lock()
	htmlTemplatesCache.cache = make(map[string]map[string]struct {
		HTML string
		JSON *string
	})
	htmlTemplatesCache.Unlock()
}

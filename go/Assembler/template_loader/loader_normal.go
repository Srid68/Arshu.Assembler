package template_loader

import (
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
	cacheKey := fmt.Sprintf("%s|%s", filepath.Dir(rootDirPath), appSite)

	htmlTemplatesCache.RLock()
	if cached, ok := htmlTemplatesCache.cache[cacheKey]; ok {
		htmlTemplatesCache.RUnlock()
		return cached
	}
	htmlTemplatesCache.RUnlock()

	result := make(map[string]struct {
		HTML string
		JSON *string
	})
	appSitesPath := filepath.Join(rootDirPath, "AppSites", appSite)
	if stat, err := os.Stat(appSitesPath); err != nil || !stat.IsDir() {
		htmlTemplatesCache.Lock()
		htmlTemplatesCache.cache[cacheKey] = result
		htmlTemplatesCache.Unlock()
		return result
	}

	_ = filepath.Walk(appSitesPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(info.Name(), ".html") {
			return nil
		}
		fileName := strings.TrimSuffix(info.Name(), ".html")
		key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(fileName))
		f, _ := os.Open(path)
		htmlBytes, _ := io.ReadAll(f)
		f.Close()
		htmlContent := htmlBytes
		jsonFile := strings.TrimSuffix(path, ".html") + ".json"
		var jsonContent *string
		if _, err := os.Stat(jsonFile); err == nil {
			jf, _ := os.Open(jsonFile)
			jsonBytes, _ := io.ReadAll(jf)
			jf.Close()
			jsonStr := string(jsonBytes)
			jsonContent = &jsonStr
		}
		result[key] = struct {
			HTML string
			JSON *string
		}{HTML: string(htmlContent), JSON: jsonContent}
		return nil
	})

	htmlTemplatesCache.Lock()
	htmlTemplatesCache.cache[cacheKey] = result
	htmlTemplatesCache.Unlock()
	return result
}

// ClearCache clears all cached templates
func ClearCache() {
	htmlTemplatesCache.Lock()
	htmlTemplatesCache.cache = make(map[string]map[string]struct {
		HTML string
		JSON *string
	})
	htmlTemplatesCache.Unlock()
}

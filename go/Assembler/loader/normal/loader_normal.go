package loader_normal

import (
	Logger "arshu/common"
	"assembler/app"
	appjson "assembler/app/json"
	"assembler/common"
	"assembler/loader"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

var normalTemplatesCache = struct {
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
func LoadGetTemplateFiles(rootDirPath, appSite, searchAppSites string) map[string]struct {
	HTML string
	JSON *string
} {
	Logger.Debug(fmt.Sprintf("LoadGetTemplateFiles called for appSite: %s, searchAppSites: %s", appSite, searchAppSites), "LoaderNormal")

	cacheKey := fmt.Sprintf("%s|%s|%s", filepath.Dir(rootDirPath), appSite, searchAppSites)

	normalTemplatesCache.RLock()
	cached, ok := normalTemplatesCache.cache[cacheKey]
	normalTemplatesCache.RUnlock()
	if ok {
		Logger.Debug(fmt.Sprintf("Returning cached templates for %s (%d templates)", appSite, len(cached)), "LoaderNormal")
		return cached
	}

	// Load templates from primary appSite
	result := LoadTemplatesFromSingleAppSiteNormal(rootDirPath, appSite)

	// Load templates from searchAppSites for fallback
	if searchAppSites != "" {
		searchAppSitesArray := strings.Split(searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}
			searchTemplates := LoadTemplatesFromSingleAppSiteNormal(rootDirPath, searchAppSite)
			for k, v := range searchTemplates {
				if _, exists := result[k]; !exists {
					result[k] = v
					Logger.Debug(fmt.Sprintf("Added fallback template '%s' from '%s'", k, searchAppSite), "LoaderNormal")
				}
			}
		}
	}

	normalTemplatesCache.Lock()
	normalTemplatesCache.cache[cacheKey] = result
	normalTemplatesCache.Unlock()
	return result
}

// LoadTemplatesFromSingleAppSiteNormal loads templates from a single AppSite without caching or fallback logic
func LoadTemplatesFromSingleAppSiteNormal(rootDirPath, appSite string) map[string]struct {
	HTML string
	JSON *string
} {
	result := make(map[string]struct {
		HTML string
		JSON *string
	})
	appSitesPath := filepath.Join(rootDirPath, "AppSites", appSite)
	if stat, err := os.Stat(appSitesPath); err != nil || !stat.IsDir() {
		Logger.Warn(fmt.Sprintf("AppSites directory not found: %s", appSitesPath), "LoaderNormal")
		return result
	}

	Logger.Debug(fmt.Sprintf("Loading templates from: %s", appSitesPath), "LoaderNormal")

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

		Logger.Debug(fmt.Sprintf("Loading template: %s (html size: %d)", key, len(htmlContent)), "LoaderNormal")

		// Find JSON file case-insensitively
		jsonFile := strings.TrimSuffix(path, ".html") + ".json"
		var jsonContent *string
		// Try exact match first
		if _, err := os.Stat(jsonFile); err == nil {
			jf, _ := os.Open(jsonFile)
			jsonBytes, _ := io.ReadAll(jf)
			jf.Close()
			jsonStr := common.NormalizeFileContent(string(jsonBytes))
			jsonContent = &jsonStr
			Logger.Debug(fmt.Sprintf("Found JSON file for %s (size: %d)", key, len(jsonStr)), "LoaderNormal")
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
							Logger.Debug(fmt.Sprintf("Found JSON file (case-insensitive) for %s (size: %d)", key, len(jsonStr)), "LoaderNormal")
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

	Logger.Debug(fmt.Sprintf("Loaded %d templates for %s", len(result), appSite), "LoaderNormal")
	return result
}

// ClearNormalCache clears all cached normal templates (useful for testing or when templates change)
func ClearNormalCache() {
	normalTemplatesCache.Lock()
	normalTemplatesCache.cache = make(map[string]map[string]struct {
		HTML string
		JSON *string
	})
	normalTemplatesCache.Unlock()
}

// LoaderNormal implements ILoaderNormal interface for EngineNormal
type LoaderNormal struct {
	templates map[string]struct {
		HTML string
		JSON *string
	}
	searchAppSites string
	appSite        string
	parentMap      map[string]string
}

// NewLoaderNormal creates a new LoaderNormal instance
func NewLoaderNormal(rootDirPath, appSite, searchAppSites string) *LoaderNormal {
	l := &LoaderNormal{
		templates:      LoadGetTemplateFiles(rootDirPath, appSite, searchAppSites),
		searchAppSites: searchAppSites,
		appSite:        appSite,
		parentMap:      make(map[string]string),
	}
	// Build parent map for JSON inheritance by scanning HTML for placeholders
	l.parentMap = l.buildParentMap()
	Logger.Debug(fmt.Sprintf("Built parent map with %d relationships for JSON inheritance", len(l.parentMap)), "LoaderNormal")
	return l
}

// buildParentMap builds a parent-child relationship map by analyzing template placeholders
// Tracks which template is the parent of another based on {{TemplateName}} references
func (l *LoaderNormal) buildParentMap() map[string]string {
	parentMap := make(map[string]string)
	Logger.Debug(fmt.Sprintf("Building parent map for appSite: %s", l.appSite), "LoaderNormal")

	for templateKey, template := range l.templates {
		html := template.HTML

		// Find all {{TemplateName}} placeholders in this template
		searchPos := 0
		for searchPos < len(html) {
			openStart := strings.Index(html[searchPos:], "{{")
			if openStart == -1 {
				break
			}
			openStart += searchPos

			// Skip special placeholders (#, @, $, /)
			if openStart+2 < len(html) && strings.ContainsAny(string(html[openStart+2]), "#@$/") {
				searchPos = openStart + 2
				continue
			}

			closeStart := strings.Index(html[openStart+2:], "}}")
			if closeStart == -1 {
				break
			}
			closeStart += openStart + 2

			placeholderName := strings.TrimSpace(html[openStart+2 : closeStart])

			// Check if this is a valid alphanumeric template name
			if placeholderName != "" && isAlphaNumeric(placeholderName) {
				// This template (templateKey) is the parent of the placeholder template
				childTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(l.appSite), strings.ToLower(placeholderName))

				if _, exists := parentMap[childTemplateKey]; !exists {
					parentMap[childTemplateKey] = templateKey
					Logger.Debug(fmt.Sprintf("Parent relationship: %s -> parent: %s", childTemplateKey, templateKey), "LoaderNormal")
				}
			}

			searchPos = closeStart + 2
		}
	}

	Logger.Debug(fmt.Sprintf("Built parent map with %d relationships", len(parentMap)), "LoaderNormal")
	return parentMap
}

// GetSearchAppSites returns the search AppSites for template fallback resolution
func (l *LoaderNormal) GetSearchAppSites() string {
	return l.searchAppSites
}

// HasTemplate checks if a template exists
func (l *LoaderNormal) HasTemplate(appSite, templateName string) bool {
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
	_, exists := l.templates[key]
	return exists
}

// ClearCache clears the template cache
func (l *LoaderNormal) ClearCache() {
	ClearNormalCache()
}

// GetTemplateHtml gets a template's HTML content by appSite and name with optional AppView fallback
func (l *LoaderNormal) GetTemplateHtml(appSite, templateName, appView, appViewPrefix string) string {
	// Try AppView fallback first if provided
	if appView != "" && appViewPrefix != "" && strings.Contains(strings.ToLower(templateName), strings.ToLower(appViewPrefix)) {
		appKey := common.ReplaceCaseInsensitive(templateName, appViewPrefix, appView)
		key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(appKey))
		if t, ok := l.templates[key]; ok {
			return t.HTML
		}
	}

	// Try primary template key
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
	if t, ok := l.templates[key]; ok {
		return t.HTML
	}

	// FALLBACK: Search in searchAppSites
	if l.searchAppSites != "" {
		searchAppSitesArray := strings.Split(l.searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}
			searchKey := fmt.Sprintf("%s_%s", strings.ToLower(searchAppSite), strings.ToLower(templateName))
			if t, ok := l.templates[searchKey]; ok {
				return t.HTML
			}
		}
	}

	return ""
}

// MergeHtmlWithJson merges HTML string with JSON data for the specified template
func (l *LoaderNormal) MergeHtmlWithJson(html, appSite, templateName string) string {
	if html == "" {
		return html
	}

	// Get JSON with inheritance resolution
	jsonData := l.GetTemplateJsonWithInheritance(appSite, templateName)
	if jsonData == nil {
		Logger.Debug(fmt.Sprintf("No JSON data found for %s, returning original HTML", templateName), "LoaderNormal")
		return html
	}

	Logger.Debug(fmt.Sprintf("Merging HTML with JSON for %s", templateName), "LoaderNormal")
	return loader.MergeTemplateWithJson(html, jsonData)
}

// GetTemplateJsonWithInheritance retrieves JSON with inheritance resolved (matches C# naming)
func (l *LoaderNormal) GetTemplateJsonWithInheritance(appSite, templateName string) *appjson.JsonObject {
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
	Logger.Debug(fmt.Sprintf("GetTemplateJsonWithInheritance: templateKey=%s", key), "LoaderNormal")

	// Try to get JSON from primary appSite template
	var rawJSON *string
	if t, ok := l.templates[key]; ok && t.JSON != nil {
		rawJSON = t.JSON
	} else if l.searchAppSites != "" {
		searchAppSitesArray := strings.Split(l.searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}
			searchKey := fmt.Sprintf("%s_%s", strings.ToLower(searchAppSite), strings.ToLower(templateName))
			if t, ok := l.templates[searchKey]; ok && t.JSON != nil {
				Logger.Debug(fmt.Sprintf("JSON for '%s' not found in '%s', using fallback from '%s'", templateName, appSite, searchAppSite), "LoaderNormal")
				rawJSON = t.JSON
				key = searchKey // Update key for inheritance resolution (matching C# line 386)
				break
			}
		}
	}

	if rawJSON == nil {
		Logger.Debug(fmt.Sprintf("No JSON found for templateKey=%s", key), "LoaderNormal")
		return nil
	}

	// Parse JSON using manual parsing (matching C#)
	jsonObject := app.ParseJsonString(*rawJSON)
	if jsonObject == nil {
		Logger.Debug(fmt.Sprintf("Error parsing JSON for template %s", key), "LoaderNormal")
		return nil
	}

	rawKeys := []string{}
	for k := range jsonObject.Iter() {
		rawKeys = append(rawKeys, k)
	}
	Logger.Debug(fmt.Sprintf("Raw JSON keys for %s: %s", key, strings.Join(rawKeys, ", ")), "LoaderNormal")

	// Resolve inheritance for keys ending with "#"
	resolved := appjson.NewJsonObject()
	for k, v := range jsonObject.Iter() {
		if strings.HasSuffix(k, "#") {
			actualKey := k[:len(k)-1]
			if v.Kind == appjson.JsonString {
				defStr := v.StrVal
				Logger.Debug(fmt.Sprintf("Found inheritance key: %s, defaultValue=%s, resolving for actualKey=%s", k, defStr, actualKey), "LoaderNormal")
				if val := l.ResolveJsonKeyWithInheritance(actualKey, defStr, key); val != "" {
					resolved.Set(actualKey, appjson.JsonValue{Kind: appjson.JsonString, StrVal: val})
					Logger.Debug(fmt.Sprintf("Resolved inherited key %s -> %s = %s", k, actualKey, val), "LoaderNormal")
					continue
				}
				Logger.Debug(fmt.Sprintf("No inherited value found for %s, using default: %s", actualKey, defStr), "LoaderNormal")
				resolved.Set(actualKey, appjson.JsonValue{Kind: appjson.JsonString, StrVal: defStr})
				continue
			}
		}
		resolved.Set(k, v)
	}

	return resolved
}

// ResolveJsonKeyWithInheritance resolves a JSON key by searching up the parent tree (matches C# naming)
func (l *LoaderNormal) ResolveJsonKeyWithInheritance(actualKey, defaultValue, currentTemplateKey string) string {
	if inherited := l.SearchParentTreeForKey(actualKey, currentTemplateKey); inherited != "" {
		return inherited
	}
	return defaultValue
}

// SearchParentTreeForKey searches up the parent tree to find a JSON key value (matches C# naming)
func (l *LoaderNormal) SearchParentTreeForKey(key, currentTemplateKey string) string {
	parentKey, ok := l.parentMap[currentTemplateKey]
	if !ok {
		Logger.Debug(fmt.Sprintf("No parent found for %s", currentTemplateKey), "LoaderNormal")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Checking parent %s for key %s", parentKey, key), "LoaderNormal")

	if t, ok := l.templates[parentKey]; ok && t.JSON != nil {
		// Parse JSON using manual parsing (matching C#)
		parentJsonObject := app.ParseJsonString(*t.JSON)
		if parentJsonObject != nil {
			for pk, pv := range parentJsonObject.Iter() {
				if strings.EqualFold(pk, key) {
					if pv.Kind == appjson.JsonString {
						Logger.Debug(fmt.Sprintf("Found key %s in parent %s: %s", key, parentKey, pv.StrVal), "LoaderNormal")
						return pv.StrVal
					}
				}
			}
		}
	}

	return l.SearchParentTreeForKey(key, parentKey)
}

// isAlphaNumeric checks if a string contains only alphanumeric characters
func isAlphaNumeric(s string) bool {
	for i := 0; i < len(s); i++ {
		c := s[i]
		if !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) {
			return false
		}
	}
	return true
}

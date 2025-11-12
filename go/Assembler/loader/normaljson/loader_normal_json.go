package loader_normaljson

import (
	Logger "arshu/common"
	"assembler/app"
	appjson "assembler/app/json"
	"assembler/common"
	"assembler/loader"
	loadernormal "assembler/loader/normal"
	"fmt"
	"path/filepath"
	"strings"
	"sync"
)

// Separate cache for NormalJson to avoid corruption with Normal loader
var normalJsonTemplatesCache = struct {
	sync.RWMutex
	cache map[string]map[string]struct {
		HTML string
		JSON *string
	}
}{cache: make(map[string]map[string]struct {
	HTML string
	JSON *string
})}

type LoaderNormalJson struct{
	templates      map[string]struct {
		HTML string
		JSON *string
	}
	searchAppSites string
	appSite        string
	parentMap      map[string]string
}

func NewLoaderNormalJson(rootDirPath, appSite, searchAppSites string) *LoaderNormalJson {
	l := &LoaderNormalJson{
		templates:      LoadGetTemplateFilesJson(rootDirPath, appSite, searchAppSites),
		searchAppSites: searchAppSites,
		appSite:        appSite,
		parentMap:      make(map[string]string),
	}
	l.parentMap = l.buildParentMap()
	Logger.Debug(fmt.Sprintf("Built parent map with %d relationships for JSON inheritance", len(l.parentMap)), "LoaderNormalJson")
	return l
}

// GetSearchAppSites returns the search AppSites for template fallback resolution
func (l *LoaderNormalJson) GetSearchAppSites() string {
	return l.searchAppSites
}

// HasTemplate checks if a template exists
func (l *LoaderNormalJson) HasTemplate(appSite, templateName string) bool {
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
	_, exists := l.templates[key]
	return exists
}

// GetAllTemplatesJson returns all templates as a serialized JSON string
// For LoaderNormalJson, this is not typically used, but required by interface
func (l *LoaderNormalJson) GetAllTemplatesJson() string {
	// Not implemented for normal loader
	return "{}"
}

// ClearCache clears the template cache
func (l *LoaderNormalJson) ClearCache() {
	normalJsonTemplatesCache.Lock()
	defer normalJsonTemplatesCache.Unlock()
	normalJsonTemplatesCache.cache = make(map[string]map[string]struct {
		HTML string
		JSON *string
	})
	Logger.Debug("Template cache cleared", "LoaderNormalJson")
}

// LoadGetTemplateFilesJson loads HTML and JSON files with separate cache for NormalJson
func LoadGetTemplateFilesJson(rootDirPath, appSite, searchAppSites string) map[string]struct {
	HTML string
	JSON *string
} {
	Logger.Debug(fmt.Sprintf("LoadGetTemplateFilesJson called for appSite: %s, searchAppSites: %s", appSite, searchAppSites), "LoaderNormalJson")

	cacheKey := fmt.Sprintf("%s|%s|%s", filepath.Dir(rootDirPath), appSite, searchAppSites)

	normalJsonTemplatesCache.RLock()
	cached, ok := normalJsonTemplatesCache.cache[cacheKey]
	normalJsonTemplatesCache.RUnlock()
	if ok {
		Logger.Debug(fmt.Sprintf("Returning cached templates for %s (%d templates)", appSite, len(cached)), "LoaderNormalJson")
		return cached
	}

	// Load templates from primary appSite (reuse Normal's function as it's stateless)
	result := loadernormal.LoadTemplatesFromSingleAppSiteNormal(rootDirPath, appSite)

	// Load templates from searchAppSites for fallback
	if searchAppSites != "" {
		searchAppSitesArray := strings.Split(searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}
			searchTemplates := loadernormal.LoadTemplatesFromSingleAppSiteNormal(rootDirPath, searchAppSite)
			for k, v := range searchTemplates {
				if _, exists := result[k]; !exists {
					result[k] = v
					Logger.Debug(fmt.Sprintf("Added fallback template '%s' from '%s'", k, searchAppSite), "LoaderNormalJson")
				}
			}
		}
	}

	normalJsonTemplatesCache.Lock()
	normalJsonTemplatesCache.cache[cacheKey] = result
	normalJsonTemplatesCache.Unlock()
	return result
}

// ApplyAllReplacementMappings for LoaderNormalJson just returns content as-is
// This method is only relevant for PreProcess loaders
func (l *LoaderNormalJson) ApplyAllReplacementMappings(content, appSite string, mainTemplate interface{}, appView, appViewPrefix string, enableJsonProcessing bool) string {
	// Not applicable for normal loader - just return content unchanged
	return content
}

// GetTemplateHtml returns interface{} for compatibility with ILoaderJson
// The actual return type is string for LoaderNormalJson
func (l *LoaderNormalJson) GetTemplateHtml(appSite, appFile, appView, appViewPrefix string) interface{} {
	// Try AppView fallback first if provided
	if appView != "" && appViewPrefix != "" && strings.Contains(strings.ToLower(appFile), strings.ToLower(appViewPrefix)) {
		appKey := common.ReplaceCaseInsensitive(appFile, appViewPrefix, appView)
		key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(appKey))
		if t, ok := l.templates[key]; ok {
			return t.HTML
		}
	}

	// Try primary template key
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(appFile))
	if t, ok := l.templates[key]; ok {
		return t.HTML
	}

	if l.searchAppSites != "" {
		searchAppSitesArray := strings.Split(l.searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}

			searchKey := fmt.Sprintf("%s_%s", strings.ToLower(searchAppSite), strings.ToLower(appFile))
			if t, ok := l.templates[searchKey]; ok {
				Logger.Debug(fmt.Sprintf("Template '%s' not found in '%s', using fallback from '%s'", appFile, appSite, searchAppSite), "LoaderNormalJson")
				return t.HTML
			}
		}
	}

	return ""
}

func (l *LoaderNormalJson) MergeHtmlWithJson(html, appSite, appFile string) string {
	Logger.Debug(fmt.Sprintf("MergeHtmlWithJson called: appSite=%s, templateName=%s", appSite, appFile), "LoaderNormalJson")

	jsonData := l.GetTemplateJsonWithInheritance(appSite, appFile)
	if jsonData == nil {
		Logger.Debug(fmt.Sprintf("No JSON data found for %s, returning original HTML", appFile), "LoaderNormalJson")
		return html
	}

	jsonKeys := []string{}
	for k := range jsonData {
		jsonKeys = append(jsonKeys, k)
	}
	Logger.Debug(fmt.Sprintf("Merging HTML with JSON for %s (keys: %s)", appFile, strings.Join(jsonKeys, ", ")), "LoaderNormalJson")
	return common.MergeTemplateWithJson(html, jsonData)
}

func (l *LoaderNormalJson) buildParentMap() map[string]string {
	parentMap := make(map[string]string)
	Logger.Debug(fmt.Sprintf("Building parent map for appSite: %s", l.appSite), "LoaderNormalJson")

	// Process templates in deterministic order to ensure consistent parent relationships
	// Sort keys: SearchAppSites first, then main AppSite (so main AppSite wins in case of conflicts)
	mainAppSitePrefix := strings.ToLower(l.appSite) + "_"
	var searchTemplateKeys []string
	var mainTemplateKeys []string

	for templateKey := range l.templates {
		if strings.HasPrefix(templateKey, mainAppSitePrefix) {
			mainTemplateKeys = append(mainTemplateKeys, templateKey)
		} else {
			searchTemplateKeys = append(searchTemplateKeys, templateKey)
		}
	}

	// Process SearchAppSites templates first, then main AppSite (last wins)
	allKeys := append(searchTemplateKeys, mainTemplateKeys...)

	for _, templateKey := range allKeys {
		t := l.templates[templateKey]
		html := t.HTML
		searchPos := 0
		for searchPos < len(html) {
			openStart := strings.Index(html[searchPos:], "{{")
			if openStart == -1 {
				break
			}
			openStart += searchPos

			if openStart+2 < len(html) {
				ch := html[openStart+2]
				if ch == '#' || ch == '@' || ch == '$' || ch == '/' {
					searchPos = openStart + 2
					continue
				}
			}

			closeStart := strings.Index(html[openStart+2:], "}}")
			if closeStart == -1 {
				break
			}
			closeStart += openStart + 2

			placeholderName := strings.TrimSpace(html[openStart+2 : closeStart])
			if placeholderName == "" || !isAlphaNumeric(placeholderName) {
				searchPos = openStart + 1
				continue
			}

			childTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(l.appSite), strings.ToLower(placeholderName))
			// Use "last wins" strategy - later templates (main AppSite) override earlier ones (SearchAppSites)
			if existingParent, exists := parentMap[childTemplateKey]; exists && existingParent != templateKey {
				Logger.Debug(fmt.Sprintf("Overwriting parent relationship: %s -> parent: %s (was: %s)", childTemplateKey, templateKey, existingParent), "LoaderNormalJson")
			} else if !exists {
				Logger.Debug(fmt.Sprintf("Parent relationship: %s -> parent: %s", childTemplateKey, templateKey), "LoaderNormalJson")
			}
			parentMap[childTemplateKey] = templateKey

			searchPos = closeStart + 2
		}
	}
	return parentMap
}

// GetTemplateJsonWithInheritance retrieves JSON with inheritance resolved (matches C# naming)
func (l *LoaderNormalJson) GetTemplateJsonWithInheritance(appSite, templateName string) map[string]interface{} {
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
	Logger.Debug(fmt.Sprintf("GetTemplateJsonWithInheritance: templateKey=%s", key), "LoaderNormalJson")

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
				Logger.Debug(fmt.Sprintf("JSON for '%s' not found in '%s', using fallback from '%s'", templateName, appSite, searchAppSite), "LoaderNormalJson")
				rawJSON = t.JSON
				key = searchKey // Update key for inheritance resolution (matching C# line 386)
				break
			}
		}
	}

	if rawJSON == nil {
		Logger.Debug(fmt.Sprintf("No JSON found for templateKey=%s", key), "LoaderNormalJson")
		return nil
	}

	// Parse JSON using manual parsing (matching C#)
	jsonObject := app.ParseJsonString(*rawJSON)
	if jsonObject == nil {
		Logger.Debug(fmt.Sprintf("Error parsing JSON for template %s", key), "LoaderNormalJson")
		return nil
	}

	// Convert JsonObject to map[string]interface{} for processing
	jsonObj := make(map[string]interface{})
	for k, v := range jsonObject.Iter() {
		jsonObj[k] = loader.ConvertJsonValueToInterface(v)
	}

	rawKeys := []string{}
	for k := range jsonObj {
		rawKeys = append(rawKeys, k)
	}
	Logger.Debug(fmt.Sprintf("Raw JSON keys for %s: %s", key, strings.Join(rawKeys, ", ")), "LoaderNormalJson")

	resolved := make(map[string]interface{})
	for k, v := range jsonObj {
		if strings.HasSuffix(k, "#") {
			actualKey := k[:len(k)-1]
			if defStr, ok := v.(string); ok {
				Logger.Debug(fmt.Sprintf("Found inheritance key: %s, defaultValue=%s, resolving for actualKey=%s", k, defStr, actualKey), "LoaderNormalJson")
				if val := l.ResolveJsonKeyWithInheritance(actualKey, defStr, key); val != "" {
					resolved[actualKey] = val
					Logger.Debug(fmt.Sprintf("Resolved inherited key %s -> %s = %s", k, actualKey, val), "LoaderNormalJson")
					continue
				}
				Logger.Debug(fmt.Sprintf("No inherited value found for %s, using default: %s", actualKey, defStr), "LoaderNormalJson")
				resolved[actualKey] = defStr
				continue
			}
		}
		resolved[k] = v
	}

	return resolved
}

// ResolveJsonKeyWithInheritance resolves a JSON key by searching up the parent tree (matches C# naming)
func (l *LoaderNormalJson) ResolveJsonKeyWithInheritance(actualKey, defaultValue, currentTemplateKey string) string {
	if inherited := l.SearchParentTreeForKey(actualKey, currentTemplateKey); inherited != "" {
		return inherited
	}
	return defaultValue
}

// SearchParentTreeForKey searches up the parent tree to find a JSON key value (matches C# naming)
func (l *LoaderNormalJson) SearchParentTreeForKey(key, currentTemplateKey string) string {
	parentKey, ok := l.parentMap[currentTemplateKey]
	if !ok {
		Logger.Debug(fmt.Sprintf("No parent found for %s", currentTemplateKey), "LoaderNormalJson")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Checking parent %s for key %s", parentKey, key), "LoaderNormalJson")

	if t, ok := l.templates[parentKey]; ok && t.JSON != nil {
		// Parse JSON using manual parsing (matching C#)
		parentJsonObject := app.ParseJsonString(*t.JSON)
		if parentJsonObject != nil {
			for pk, pv := range parentJsonObject.Iter() {
				if strings.EqualFold(pk, key) {
					if pv.Kind == appjson.JsonString {
						Logger.Debug(fmt.Sprintf("Found key %s in parent %s: %s", key, parentKey, pv.StrVal), "LoaderNormalJson")
						return pv.StrVal
					}
				}
			}
		}
	}

	return l.SearchParentTreeForKey(key, parentKey)
}

// GetAllTemplatesForSerialization returns all templates for API serialization
// Returns a copy of template data - does not expose internal state
// This is for API endpoints that need to build complex responses
func (l *LoaderNormalJson) GetAllTemplatesForSerialization() map[string]struct {
	HTML string
	JSON string
} {
	result := make(map[string]struct {
		HTML string
		JSON string
	})

	for key, value := range l.templates {
		jsonStr := ""
		if value.JSON != nil {
			jsonStr = *value.JSON
		}
		result[key] = struct {
			HTML string
			JSON string
		}{
			HTML: value.HTML,
			JSON: jsonStr,
		}
	}

	return result
}

func isAlphaNumeric(s string) bool {
	for i := 0; i < len(s); i++ {
		c := s[i]
		if !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) {
			return false
		}
	}
	return true
}
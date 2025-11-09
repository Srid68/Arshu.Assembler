package loader

import (
	Logger "arshu/common"
	"encoding/json"
	"fmt"
	"strings"
)

type LoaderNormalJson struct {
    templates       map[string]struct {
        HTML string
        JSON *string
    }
    searchAppSites string
    appSite        string
    parentMap      map[string]string
}

func NewLoaderNormalJson(rootDirPath, appSite, searchAppSites string) *LoaderNormalJson {
    l := &LoaderNormalJson{
        templates:       LoadGetTemplateFiles(rootDirPath, appSite, searchAppSites),
        searchAppSites:  searchAppSites,
        appSite:         appSite,
        parentMap:       make(map[string]string),
    }
    // Build parent-child relationship map for JSON inheritance
    l.parentMap = l.buildParentMap()
    Logger.Debug(fmt.Sprintf("Built parent map with %d relationships for JSON inheritance", len(l.parentMap)), "LoaderNormalJson")
    return l
}

func (l *LoaderNormalJson) GetTemplateHtml(appSite, appFile, appView, appViewPrefix string) string {
	if appView != "" && appViewPrefix != "" {
		// Try AppView specific template first
		appKey := strings.Replace(appFile, appViewPrefix, appView, 1)
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

	// Search in SearchAppSites as fallback
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

func (l *LoaderNormalJson) GetTemplateJson(appSite, appFile string) map[string]interface{} {
    // Resolve JSON with inheritance support
    return l.getTemplateJsonWithInheritance(appSite, appFile)
}

// buildParentMap analyzes template HTML to identify parent-child relationships based on {{TemplateName}} usage
func (l *LoaderNormalJson) buildParentMap() map[string]string {
    parentMap := make(map[string]string)

    for templateKey, t := range l.templates {
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
            if _, exists := parentMap[childTemplateKey]; !exists {
                parentMap[childTemplateKey] = templateKey
                Logger.Debug(fmt.Sprintf("Parent relationship: %s -> parent: %s", childTemplateKey, templateKey), "LoaderNormalJson")
            }

            searchPos = closeStart + 2
        }
    }
    return parentMap
}

// getTemplateJsonWithInheritance parses JSON and resolves keys ending with '#' by searching up the parent tree
func (l *LoaderNormalJson) getTemplateJsonWithInheritance(appSite, templateName string) map[string]interface{} {
    key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))

    // Get raw JSON for the template (with fallback as in original logic)
    var rawJSON *string
    if t, ok := l.templates[key]; ok && t.JSON != nil {
        rawJSON = t.JSON
    } else if l.searchAppSites != "" { // fallback search
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
                break
            }
        }
    }

    if rawJSON == nil {
        return nil
    }

    var jsonObj map[string]interface{}
    if err := json.Unmarshal([]byte(*rawJSON), &jsonObj); err != nil {
        Logger.Debug(fmt.Sprintf("Error parsing JSON for template %s: %v", key, err), "LoaderNormalJson")
        return nil
    }

    resolved := make(map[string]interface{})
    // Resolve each key, handling inheritance for keys ending with '#'
    for k, v := range jsonObj {
        if strings.HasSuffix(k, "#") {
            actualKey := k[:len(k)-1]
            if defStr, ok := v.(string); ok {
                if val := l.resolveJsonKeyWithInheritance(actualKey, defStr, key); val != "" {
                    resolved[actualKey] = val
                    Logger.Debug(fmt.Sprintf("Resolved inherited key %s -> %s = %s", k, actualKey, val), "LoaderNormalJson")
                    continue
                }
                // If not found in parents, use default value
                Logger.Debug(fmt.Sprintf("No inherited value found for %s, using default: %s", actualKey, defStr), "LoaderNormalJson")
                resolved[actualKey] = defStr
                continue
            }
        }
        // Normal key - keep as is
        resolved[k] = v
    }

    return resolved
}

// resolveJsonKeyWithInheritance walks up the parent chain to find a value for actualKey
func (l *LoaderNormalJson) resolveJsonKeyWithInheritance(actualKey, defaultValue, currentTemplateKey string) string {
    if inherited := l.searchParentTreeForKey(actualKey, currentTemplateKey); inherited != "" {
        return inherited
    }
    return defaultValue
}

// searchParentTreeForKey searches parents recursively for the given key (case-insensitive)
func (l *LoaderNormalJson) searchParentTreeForKey(key, currentTemplateKey string) string {
    parentKey, ok := l.parentMap[currentTemplateKey]
    if !ok {
        Logger.Debug(fmt.Sprintf("No parent found for %s", currentTemplateKey), "LoaderNormalJson")
        return ""
    }

    Logger.Debug(fmt.Sprintf("Checking parent %s for key %s", parentKey, key), "LoaderNormalJson")

    // Get parent's JSON
    if t, ok := l.templates[parentKey]; ok && t.JSON != nil {
        var parentJSON map[string]interface{}
        if err := json.Unmarshal([]byte(*t.JSON), &parentJSON); err == nil {
            for pk, pv := range parentJSON {
                if strings.EqualFold(pk, key) {
                    if str, ok := pv.(string); ok {
                        Logger.Debug(fmt.Sprintf("Found key %s in parent %s: %s", key, parentKey, str), "LoaderNormalJson")
                        return str
                    }
                }
            }
        }
    }

    // Not found here; continue up the tree
    return l.searchParentTreeForKey(key, parentKey)
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

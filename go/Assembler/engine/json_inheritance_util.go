package engine

import (
	Logger "arshu/common"
	"encoding/json"
	"fmt"
	"strings"
	"unicode"
)

// ResolveJsonKeyWithInheritance resolves a JSON key with inheritance support
// If the key ends with #, searches up the parent tree for the key without #
func ResolveJsonKeyWithInheritance(
	jsonKey string,
	currentValue string,
	currentTemplateKey string,
	allTemplates map[string]struct {
		HTML string
		JSON *string
	},
	parentMap map[string]string) string {

	// If key doesn't end with #, no inheritance - return current value
	if !strings.HasSuffix(jsonKey, "#") {
		return currentValue
	}

	// Extract the actual key name without the # suffix
	actualKey := jsonKey[:len(jsonKey)-1]

	Logger.Debug(fmt.Sprintf("Resolving inherited key: %s -> %s for template %s", jsonKey, actualKey, currentTemplateKey), "JsonInheritance")

	// Search up the parent tree for the key
	inheritedValue := searchParentTreeForKey(actualKey, currentTemplateKey, allTemplates, parentMap)

	if inheritedValue != "" {
		Logger.Debug(fmt.Sprintf("Found inherited value for %s: %s", actualKey, inheritedValue), "JsonInheritance")
		return inheritedValue
	}

	// If not found in parents, use the current value as default
	Logger.Debug(fmt.Sprintf("No inherited value found for %s, using default: %s", actualKey, currentValue), "JsonInheritance")
	return currentValue
}

// searchParentTreeForKey searches up the parent tree to find a JSON key value
func searchParentTreeForKey(
	key string,
	currentTemplateKey string,
	allTemplates map[string]struct {
		HTML string
		JSON *string
	},
	parentMap map[string]string) string {

	// Get parent template key
	parentKey, exists := parentMap[currentTemplateKey]
	if !exists {
		Logger.Debug(fmt.Sprintf("No parent found for %s", currentTemplateKey), "JsonInheritance")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Checking parent %s for key %s", parentKey, key), "JsonInheritance")

	// Get parent's JSON data
	parentTemplate, exists := allTemplates[parentKey]
	if !exists {
		Logger.Debug(fmt.Sprintf("Parent template %s not found in allTemplates", parentKey), "JsonInheritance")
		return ""
	}

	if parentTemplate.JSON == nil || *parentTemplate.JSON == "" {
		Logger.Debug(fmt.Sprintf("Parent template %s has no JSON data, searching further up", parentKey), "JsonInheritance")
		// Parent has no JSON, search further up the tree
		return searchParentTreeForKey(key, parentKey, allTemplates, parentMap)
	}

	// Parse parent's JSON
	var parentJsonObj map[string]interface{}
	if err := json.Unmarshal([]byte(*parentTemplate.JSON), &parentJsonObj); err != nil {
		Logger.Error(fmt.Sprintf("Error parsing JSON for parent %s: %v", parentKey, err), "JsonInheritance")
		return ""
	}

	// Look for the key (case-insensitive)
	for k, v := range parentJsonObj {
		if strings.EqualFold(k, key) {
			if strValue, ok := v.(string); ok {
				Logger.Debug(fmt.Sprintf("Found key %s in parent %s: %s", key, parentKey, strValue), "JsonInheritance")
				return strValue
			}
		}
	}

	Logger.Debug(fmt.Sprintf("Key %s not found in parent %s, searching further up", key, parentKey), "JsonInheritance")
	// Not found in this parent, search further up the tree
	return searchParentTreeForKey(key, parentKey, allTemplates, parentMap)
}

// BuildParentMap builds a parent map from template structure by analyzing placeholders
// This tracks which template is the parent of another based on {{TemplateName}} references
func BuildParentMap(
	appSite string,
	allTemplates map[string]struct {
		HTML string
		JSON *string
	}) map[string]string {

	parentMap := make(map[string]string)

	Logger.Debug(fmt.Sprintf("Building parent map for appSite: %s", appSite), "JsonInheritance")

	for templateKey, template := range allTemplates {
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
			if openStart+2 < len(html) &&
				(html[openStart+2] == '#' || html[openStart+2] == '@' ||
					html[openStart+2] == '$' || html[openStart+2] == '/') {
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
				childTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(placeholderName)

				if _, exists := parentMap[childTemplateKey]; !exists {
					parentMap[childTemplateKey] = templateKey
					Logger.Debug(fmt.Sprintf("Parent relationship: %s -> parent: %s", childTemplateKey, templateKey), "JsonInheritance")
				}
			}

			searchPos = closeStart + 2
		}
	}

	Logger.Debug(fmt.Sprintf("Built parent map with %d relationships", len(parentMap)), "JsonInheritance")
	return parentMap
}

func isAlphaNumeric(str string) bool {
	for _, r := range str {
		if !unicode.IsLetter(r) && !unicode.IsDigit(r) {
			return false
		}
	}
	return true
}

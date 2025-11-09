package engine

import (
	"assembler/common"
	"encoding/json"
	"fmt"
	"strings"

	Logger "arshu/common"
)

type EngineNormal struct {
	AppViewPrefix string
}

func NewEngineNormal(prefix string) *EngineNormal {
	return &EngineNormal{AppViewPrefix: prefix}
}

func (e *EngineNormal) SetAppViewPrefix(prefix string) {
	e.AppViewPrefix = prefix
}

func (e *EngineNormal) GetAppViewPrefix() string {
	return e.AppViewPrefix
}

func (e *EngineNormal) MergeTemplates(appSite, appFile, appView string, templates map[string]struct {
	HTML string
	JSON *string
}, searchAppSites string, enableJsonProcessing bool) string {
	Logger.Debug(fmt.Sprintf("MergeTemplates called: appSite=%s, appFile=%s, appView=%s, searchAppSites=%s, enableJson=%t", appSite, appFile, appView, searchAppSites, enableJsonProcessing), "EngineNormal")

	if len(templates) == 0 {
		Logger.Warn("No templates available", "EngineNormal")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Using %d templates", len(templates)), "EngineNormal")

	// Build parent-child relationship map for JSON inheritance
	parentMap := BuildParentMap(appSite, templates)
	Logger.Debug(fmt.Sprintf("Built parent map with %d relationships for JSON inheritance", len(parentMap)), "EngineNormal")

	mainHtml, mainJson := e.GetTemplate(appSite, appFile, templates, searchAppSites, appView, e.AppViewPrefix, true)
	if mainHtml == "" {
		Logger.Warn(fmt.Sprintf("Main template not found for appSite=%s, appFile=%s", appSite, appFile), "EngineNormal")
		return ""
	}

	jsonLen := 0
	if mainJson != nil {
		jsonLen = len(*mainJson)
	}
	Logger.Debug(fmt.Sprintf("Main template found, html size: %d, json: %d", len(mainHtml), jsonLen), "EngineNormal")

	mainTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(appFile)
	contentHtml := mainHtml
	if enableJsonProcessing && mainJson != nil {
		Logger.Debug(fmt.Sprintf("Merging main template with JSON (size: %d)", len(*mainJson)), "EngineNormal")
		contentHtml = e.mergeTemplateWithJson(contentHtml, *mainJson, mainTemplateKey, templates, parentMap)
		Logger.Debug(fmt.Sprintf("After main JSON merge: %d chars", len(contentHtml)), "EngineNormal")
	}
	mergedTemplates := make(map[string]string)
	allJsonValues := make(map[string]string)

	if enableJsonProcessing && mainJson != nil {
		var jsonData map[string]interface{}
		if err := json.Unmarshal([]byte(*mainJson), &jsonData); err == nil {
			for k, v := range jsonData {
				if s, ok := v.(string); ok {
					allJsonValues[k] = s
				}
			}
		}
	}

	jsonMergeCount := 0
	for k, v := range templates {
		htmlContent := v.HTML
		jsonContent := v.JSON

		Logger.Debug(fmt.Sprintf("Processing template: %s, has JSON: %t", k, jsonContent != nil && *jsonContent != ""), "EngineNormal")

		if enableJsonProcessing && jsonContent != nil && *jsonContent != "" {
			// Pre-merge component HTML with JSON so {{$Key}} placeholders are resolved
			htmlContent = e.mergeTemplateWithJson(htmlContent, *jsonContent, k, templates, parentMap)
			Logger.Debug(fmt.Sprintf("Template %s pre-merged with JSON", k), "EngineNormal")
			jsonMergeCount++

			var jsonData map[string]interface{}
			if err := json.Unmarshal([]byte(*jsonContent), &jsonData); err == nil {
				for jk, jv := range jsonData {
					if s, ok := jv.(string); ok {
						// Skip keys ending with #  - they are inheritance markers
						if !strings.HasSuffix(jk, "#") {
							allJsonValues[jk] = s
						}
					}
				}
			}
		}
		mergedTemplates[k] = htmlContent
	}

	Logger.Debug(fmt.Sprintf("Pre-merged %d templates with JSON, collected %d JSON values", jsonMergeCount, len(allJsonValues)), "EngineNormal")

	previous := ""
	actualPasses := 0
	maxPasses := 10
	for pass := 0; pass < maxPasses; pass++ {
		previous = contentHtml
		actualPasses = pass + 1

		Logger.Debug(fmt.Sprintf("Pass %d, current size: %d", actualPasses, len(contentHtml)), "EngineNormal")

		contentHtml = e.MergeTemplateSlots(contentHtml, appSite, appView, mergedTemplates)
		Logger.Debug(fmt.Sprintf("After slot merge: %d chars", len(contentHtml)), "EngineNormal")

		contentHtml = e.ReplaceTemplatePlaceholdersWithJson(contentHtml, appSite, mergedTemplates, allJsonValues, searchAppSites, appView)
		Logger.Debug(fmt.Sprintf("After placeholder replacement: %d chars", len(contentHtml)), "EngineNormal")

		if contentHtml == previous {
			Logger.Debug(fmt.Sprintf("No changes in pass %d, stopping", actualPasses), "EngineNormal")
			break
		}
	}

	Logger.Debug(fmt.Sprintf("MergeTemplates complete after %d passes: output size=%d", actualPasses, len(contentHtml)), "EngineNormal")
	return contentHtml
}

func (e *EngineNormal) GetTemplate(appSite, templateName string, templates map[string]struct {
	HTML string
	JSON *string
}, searchAppSites, appView, appViewPrefix string, useAppViewFallback bool) (string, *string) {
	if len(templates) == 0 {
		return "", nil
	}
	viewPrefix := appViewPrefix

	if useAppViewFallback && appView != "" && viewPrefix != "" {
		templateNameLower := strings.ToLower(templateName)
		viewPrefixLower := strings.ToLower(viewPrefix)

		if strings.Contains(templateNameLower, viewPrefixLower) {
			appKey := common.ReplaceCaseInsensitive(templateName, viewPrefix, appView)
			fallbackTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(appKey)
			for k := range templates {
				if strings.EqualFold(k, fallbackTemplateKey) {
					v := templates[k]
					return v.HTML, v.JSON
				}
			}
		}
	}

	primaryTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(templateName)
	for k := range templates {
		if strings.EqualFold(k, primaryTemplateKey) {
			v := templates[k]
			return v.HTML, v.JSON
		}
	}

	if searchAppSites != "" {
		Logger.Debug(fmt.Sprintf("Attempting to find fallback template for '%s' in searchAppSites: %s", templateName, searchAppSites), "EngineNormal")
		searchAppSitesArray := strings.Split(searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}

			searchKey := strings.ToLower(searchAppSite) + "_" + strings.ToLower(templateName)
			for k := range templates {
				if strings.EqualFold(k, searchKey) {
					v := templates[k]
					Logger.Debug(fmt.Sprintf("Component '%s' not found in '%s', using fallback from '%s'", templateName, appSite, searchAppSite), "EngineNormal")
					return v.HTML, v.JSON
				}
			}
		}
		Logger.Debug(fmt.Sprintf("No fallback template found for '%s' in any of the searchAppSites: %s", templateName, searchAppSites), "EngineNormal")
	}

	return "", nil
}

func (e *EngineNormal) getTemplateFromMerged(appSite, templateName string, mergedTemplates map[string]string, searchAppSites, appView string) string {
	if len(mergedTemplates) == 0 {
		return ""
	}

	if appView != "" && e.AppViewPrefix != "" {
		templateNameLower := strings.ToLower(templateName)
		viewPrefixLower := strings.ToLower(e.AppViewPrefix)

		if strings.Contains(templateNameLower, viewPrefixLower) {
			appKey := common.ReplaceCaseInsensitive(templateName, e.AppViewPrefix, appView)
			fallbackTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(appKey)
			if fallbackTemplate, exists := mergedTemplates[fallbackTemplateKey]; exists {
				return fallbackTemplate
			}
		}
	}

	primaryTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(templateName)
	if primaryTemplate, exists := mergedTemplates[primaryTemplateKey]; exists {
		return primaryTemplate
	}

	if searchAppSites != "" {
		searchAppSitesArray := strings.Split(searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}

			searchKey := strings.ToLower(searchAppSite) + "_" + strings.ToLower(templateName)
			if searchTemplate, exists := mergedTemplates[searchKey]; exists {
				Logger.Debug(fmt.Sprintf("Component '%s' not found in '%s', using fallback from '%s'", templateName, appSite, searchAppSite), "EngineNormal")
				return searchTemplate
			}
		}
	}

	return ""
}

func (e *EngineNormal) ReplaceTemplatePlaceholdersWithJson(html, appSite string, htmlFiles, jsonValues map[string]string, searchAppSites, appView string) string {
	result := html
	searchPos := 0
	for searchPos < len(result) {
		openStart := strings.Index(result[searchPos:], "{{")
		if openStart == -1 {
			break
		}
		openStart += searchPos
		if openStart+2 < len(result) && strings.ContainsAny(string(result[openStart+2]), "#@/") {
			searchPos = openStart + 2
			continue
		}
		closeStart := strings.Index(result[openStart+2:], "}}")
		if closeStart == -1 {
			break
		}
		closeStart += openStart + 2
		placeholderName := strings.TrimSpace(result[openStart+2 : closeStart])
		if placeholderName == "" {
			searchPos = openStart + 2
			continue
		}

		var processedReplacement string

		if strings.HasPrefix(placeholderName, "$") {
			key := placeholderName[1:]
			if value, exists := jsonValues[key]; exists {
				processedReplacement = value
			}
		} else if common.IsAlphaNumeric(placeholderName) {
			templateContent := e.getTemplateFromMerged(appSite, placeholderName, htmlFiles, searchAppSites, appView)

			if templateContent != "" {
				Logger.Debug(fmt.Sprintf("Found template for placeholder {{%s}}", placeholderName), "EngineNormal")
				processedReplacement = e.ReplaceTemplatePlaceholdersWithJson(templateContent, appSite, htmlFiles, jsonValues, searchAppSites, appView)
			} else {
				Logger.Debug(fmt.Sprintf("No template found for {{%s}} - use {{$%s}} for JSON values", placeholderName, placeholderName), "EngineNormal")
			}
		}

		if processedReplacement != "" {
			placeholder := result[openStart : closeStart+2]
			result = strings.Replace(result, placeholder, processedReplacement, 1)
			searchPos = openStart + len(processedReplacement)
		} else {
			searchPos = closeStart + 2
		}
	}
	return result
}

func (e *EngineNormal) MergeTemplateSlots(contentHtml, appSite, appView string, templates map[string]string) string {
	if contentHtml == "" || len(templates) == 0 {
		return contentHtml
	}
	previous := ""
	for {
		previous = contentHtml
		contentHtml = e.ProcessTemplateSlots(contentHtml, appSite, appView, templates)
		if contentHtml == previous {
			break
		}
	}
	return contentHtml
}

func (e *EngineNormal) ProcessTemplateSlots(contentHtml, appSite, appView string, templates map[string]string) string {
	result := contentHtml
	searchPos := 0
	for searchPos < len(result) {
		openStart := strings.Index(result[searchPos:], "{{#")
		if openStart == -1 {
			break
		}
		openStart += searchPos
		openEnd := strings.Index(result[openStart+3:], "}}")
		if openEnd == -1 {
			break
		}
		openEnd += openStart + 3
		templateName := strings.TrimSpace(result[openStart+3 : openEnd])
		if templateName == "" || !common.IsAlphaNumeric(templateName) {
			searchPos = openStart + 1
			continue
		}
		closeTag := "{{/" + templateName + "}}"
		closeStart, found := common.FindMatchingCloseTag(result, openEnd+2, "{{#"+templateName+"}}", closeTag)
		if !found {
			searchPos = openStart + 1
			continue
		}
		innerStart := openEnd + 2
		innerContent := result[innerStart:closeStart]

		templatesForGetTemplate := make(map[string]struct {
			HTML string
			JSON *string
		})
		for k, v := range templates {
			templatesForGetTemplate[k] = struct {
				HTML string
				JSON *string
			}{HTML: v, JSON: nil}
		}

		templateHtml, _ := e.GetTemplate(appSite, templateName, templatesForGetTemplate, "", appView, e.AppViewPrefix, true)
		if templateHtml != "" {
			slotContents := e.ExtractSlotContents(innerContent, appSite, appView, templates)
			processedTemplate := templateHtml
			for k, v := range slotContents {
				processedTemplate = strings.ReplaceAll(processedTemplate, k, v)
			}
			fullMatch := result[openStart : closeStart+len(closeTag)]
			result = strings.Replace(result, fullMatch, processedTemplate, 1)
			searchPos = openStart + len(processedTemplate)
		} else {
			searchPos = openStart + 1
		}
	}
	return result
}

func (e *EngineNormal) ExtractSlotContents(innerContent, appSite, appView string, templates map[string]string) map[string]string {
	slotContents := make(map[string]string)
	searchPos := 0
	for searchPos < len(innerContent) {
		slotStart := strings.Index(innerContent[searchPos:], "{{@HTMLPLACEHOLDER")
		if slotStart == -1 {
			break
		}
		slotStart += searchPos
		afterPlaceholder := slotStart + 18
		slotNum := ""
		pos := afterPlaceholder
		for pos < len(innerContent) && innerContent[pos] >= '0' && innerContent[pos] <= '9' {
			slotNum += string(innerContent[pos])
			pos++
		}
		if pos+1 >= len(innerContent) || innerContent[pos:pos+2] != "}}" {
			searchPos = slotStart + 1
			continue
		}
		slotOpenEnd := pos + 2
		var closeTag, openTag string
		if slotNum == "" {
			closeTag = "{{/HTMLPLACEHOLDER}}"
			openTag = "{{@HTMLPLACEHOLDER}}"
		} else {
			closeTag = "{{/HTMLPLACEHOLDER" + slotNum + "}}"
			openTag = "{{@HTMLPLACEHOLDER" + slotNum + "}}"
		}
		closeStart, found := common.FindMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag)
		if !found {
			searchPos = slotStart + 1
			continue
		}
		slotContent := innerContent[slotOpenEnd:closeStart]
		var slotKey string
		if slotNum == "" {
			slotKey = "{{$HTMLPLACEHOLDER}}"
		} else {
			slotKey = "{{$HTMLPLACEHOLDER" + slotNum + "}}"
		}
		recursiveResult := e.MergeTemplateSlots(slotContent, appSite, appView, templates)
		recursiveResult = e.ReplaceTemplatePlaceholders(recursiveResult, appSite, appView, templates)
		slotContents[slotKey] = recursiveResult
		searchPos = closeStart + len(closeTag)
	}
	return slotContents
}

func (e *EngineNormal) ReplaceTemplatePlaceholders(html, appSite, appView string, htmlFiles map[string]string) string {
	result := html
	searchPos := 0
	for searchPos < len(result) {
		openStart := strings.Index(result[searchPos:], "{{")
		if openStart == -1 {
			break
		}
		openStart += searchPos
		if openStart+2 < len(result) && strings.ContainsAny(string(result[openStart+2]), "#@$/") {
			searchPos = openStart + 2
			continue
		}
		closeStart := strings.Index(result[openStart+2:], "}}")
		if closeStart == -1 {
			break
		}
		closeStart += openStart + 2
		placeholderName := strings.TrimSpace(result[openStart+2 : closeStart])
		if placeholderName == "" || !common.IsAlphaNumeric(placeholderName) {
			searchPos = openStart + 2
			continue
		}
		templateContent := htmlFiles[placeholderName]
		var processedReplacement string
		if templateContent != "" {
			processedReplacement = e.ReplaceTemplatePlaceholders(templateContent, appSite, appView, htmlFiles)
		}
		if processedReplacement != "" {
			placeholder := result[openStart : closeStart+2]
			result = strings.Replace(result, placeholder, processedReplacement, 1)
			searchPos = openStart + len(processedReplacement)
		} else {
			searchPos = closeStart + 2
		}
	}
	return result
}

// mergeTemplateWithJson merges HTML template with JSON data using placeholder replacement
// Supports JSON key inheritance: keys ending with # will inherit values from parent templates
func (e *EngineNormal) mergeTemplateWithJson(
	template string,
	jsonText string,
	templateKey string,
	allTemplates map[string]struct {
		HTML string
		JSON *string
	},
	parentMap map[string]string) string {

	// Parse JSON
	var jsonObject map[string]interface{}
	if err := json.Unmarshal([]byte(jsonText), &jsonObject); err != nil {
		Logger.Error(fmt.Sprintf("Error parsing JSON for template %s: %v", templateKey, err), "EngineNormal")
		return template
	}

	// Convert and resolve inherited keys
	resolvedData := make(map[string]interface{})
	for key, value := range jsonObject {
		// Check if this is an inheritable key (ends with #)
		if strings.HasSuffix(key, "#") {
			if strValue, ok := value.(string); ok {
				// Resolve inherited value
				resolvedValue := ResolveJsonKeyWithInheritance(key, strValue, templateKey, allTemplates, parentMap)
				if resolvedValue != "" {
					// Store with the actual key name (without #)
					actualKey := key[:len(key)-1]
					resolvedData[actualKey] = resolvedValue
					Logger.Debug(fmt.Sprintf("Resolved inherited key %s -> %s = %s", key, actualKey, resolvedValue), "EngineNormal")
					continue
				}
			}
		}

		// Normal key processing (non-inheritable keys)
		resolvedData[key] = value
	}

	// Use common merge function for the actual merging
	return common.MergeTemplateWithJson(template, resolvedData)
}
package template_loader

import (
	"assembler/template_common"
	"assembler/template_model"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

var preprocessedTemplatesCache = struct {
	sync.RWMutex
	cache map[string]*template_model.PreprocessedSiteTemplates
}{cache: make(map[string]*template_model.PreprocessedSiteTemplates)}

func LoadProcessGetTemplateFiles(rootDirPath, appSite string) *template_model.PreprocessedSiteTemplates {
	cacheKey := filepath.Dir(rootDirPath) + "|" + appSite
	preprocessedTemplatesCache.RLock()
	if cached, ok := preprocessedTemplatesCache.cache[cacheKey]; ok {
		preprocessedTemplatesCache.RUnlock()
		return cached
	}
	preprocessedTemplatesCache.RUnlock()

	result := &template_model.PreprocessedSiteTemplates{
		SiteName:     appSite,
		Templates:    make(map[string]template_model.PreprocessedTemplate),
		RawTemplates: make(map[string]string),
		TemplateKeys: make(map[string]struct{}),
	}

	appSitesPath := filepath.Join(rootDirPath, "AppSites", appSite)
	if _, err := os.Stat(appSitesPath); os.IsNotExist(err) {
		preprocessedTemplatesCache.Lock()
		preprocessedTemplatesCache.cache[cacheKey] = result
		preprocessedTemplatesCache.Unlock()
		return result
	}

	err := filepath.Walk(appSitesPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(info.Name(), ".html") {
			return nil
		}
		contentBytes, _ := os.ReadFile(path)
		content := string(contentBytes)
		fileName := strings.TrimSuffix(info.Name(), ".html")
		key := strings.ToLower(appSite) + "_" + strings.ToLower(fileName)
		result.RawTemplates[key] = content
		result.TemplateKeys[key] = struct{}{}

		// Look for corresponding JSON file
		jsonPath := strings.TrimSuffix(path, ".html") + ".json"
		var jsonContent *string
		if jsonBytes, err := os.ReadFile(jsonPath); err == nil {
			jsonStr := string(jsonBytes)
			jsonContent = &jsonStr
		}

		// Preprocess the template
		preprocessed := preprocessTemplate(content, jsonContent, appSite, key)
		result.Templates[key] = preprocessed
		return nil
	})
	if err != nil {
		// handle error if needed
	}

	// After loading all templates, create replacement mappings for simple template placeholders
	createReplacementMappings(result)

	// Update convenience flags for all templates after processing
	for _, template := range result.Templates {
		template.UpdateFlags()
	}

	preprocessedTemplatesCache.Lock()
	preprocessedTemplatesCache.cache[cacheKey] = result
	preprocessedTemplatesCache.Unlock()
	return result
}

// createReplacementMappings analyzes all templates and creates replacement mappings for template placeholders
func createReplacementMappings(result *template_model.PreprocessedSiteTemplates) {
	// CRITICAL: Create ALL replacement mappings after all templates are loaded
	// This ensures PreProcess engine does ONLY merging, no processing logic
	createAllReplacementMappingsForSite(result)
}

// createAllReplacementMappingsForSite creates all replacement mappings for a site with AppView support
func createAllReplacementMappingsForSite(siteTemplates *template_model.PreprocessedSiteTemplates) {
	// Phase 1: Create JSON replacement mappings for all templates first (no dependencies)
	templateKeys := make([]string, 0, len(siteTemplates.Templates))
	for key := range siteTemplates.Templates {
		templateKeys = append(templateKeys, key)
	}

	for _, key := range templateKeys {
		if template, exists := siteTemplates.Templates[key]; exists {
			// Create replacement mappings for JSON array blocks (including negative blocks)
			createJsonArrayReplacementMappings(&template, template.OriginalContent)
			siteTemplates.Templates[key] = template
		}
	}

	// Phase 2: Create simple template replacement mappings (may depend on JSON but not on slotted templates)
	allTemplatesSnapshot := make(map[string]template_model.PreprocessedTemplate)
	for key, template := range siteTemplates.Templates {
		allTemplatesSnapshot[key] = template
	}

	for _, key := range templateKeys {
		if template, exists := siteTemplates.Templates[key]; exists {
			createPlaceholderReplacementMappings(&template, allTemplatesSnapshot, siteTemplates.SiteName)
			siteTemplates.Templates[key] = template
		}
	}

	// Phase 3: Create slotted template replacement mappings (may depend on simple templates)
	for _, key := range templateKeys {
		if template, exists := siteTemplates.Templates[key]; exists {
			createSlottedTemplateReplacementMappings(&template, allTemplatesSnapshot, siteTemplates.SiteName)
			siteTemplates.Templates[key] = template
		}
	}
}

// createPlaceholderReplacementMappings creates replacement mappings for simple template placeholders that reference other templates
// This method processes {{PlaceholderName}} patterns and creates direct replacement mappings with AppView support
func createPlaceholderReplacementMappings(template *template_model.PreprocessedTemplate, allTemplates map[string]template_model.PreprocessedTemplate, appSite string) {
	content := template.OriginalContent
	searchPos := 0

	for searchPos < len(content) {
		openStart := strings.Index(content[searchPos:], "{{")
		if openStart == -1 {
			break
		}
		openStart += searchPos

		// Skip if it's a special placeholder (starts with #, @, $, /)
		if openStart+2 < len(content) && strings.ContainsAny(string(content[openStart+2]), "#@$/") {
			searchPos = openStart + 2
			continue
		}

		closeStart := strings.Index(content[openStart+2:], "}}")
		if closeStart == -1 {
			break
		}
		closeStart += openStart + 2

		placeholderName := strings.TrimSpace(content[openStart+2 : closeStart])
		if placeholderName == "" || !template_common.IsAlphaNumeric(placeholderName) {
			searchPos = openStart + 2
			continue
		}

		placeholder := content[openStart : closeStart+2]

		// Try to find the referenced template with AppView support
		templateKey := strings.ToLower(appSite) + "_" + strings.ToLower(placeholderName)

		// First try exact case-insensitive match
		var foundKey string
		for key := range allTemplates {
			if strings.EqualFold(key, templateKey) {
				foundKey = key
				break
			}
		}

		if foundKey != "" {
			if referencedTemplate, exists := allTemplates[foundKey]; exists {
				// Create replacement mapping for direct replacement
				mapping := template_model.ReplacementMapping{
					OriginalText:    placeholder,
					ReplacementText: referencedTemplate.OriginalContent,
					Type:            template_model.SimpleTemplateType,
				}
				template.ReplacementMappings = append(template.ReplacementMappings, mapping)
			}
		}

		searchPos = closeStart + 2
	}
}

// createSlottedTemplateReplacementMappings creates replacement mappings for slotted templates
func createSlottedTemplateReplacementMappings(template *template_model.PreprocessedTemplate, allTemplates map[string]template_model.PreprocessedTemplate, appSite string) {
	if len(template.SlottedTemplates) == 0 {
		return
	}

	for _, slottedTemplate := range template.SlottedTemplates {
		fullMatch := slottedTemplate.FullMatch
		targetTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), slottedTemplate.TemplateKey)

		if targetTemplate, exists := allTemplates[targetTemplateKey]; exists {
			processedTemplate := targetTemplate.OriginalContent

			for _, slot := range slottedTemplate.Slots {
				processedSlotContent := processSlotContentForReplacementMappingRecursive(slot, allTemplates, appSite)
				processedTemplate = strings.ReplaceAll(processedTemplate, slot.SlotKey, processedSlotContent)
			}

			if len(slottedTemplate.Slots) == 0 {
				actualInnerContent := slottedTemplate.InnerContent
				if strings.TrimSpace(actualInnerContent) != "" {
					defaultSlotKey := "{{$HTMLPLACEHOLDER}}"
					if strings.Contains(processedTemplate, defaultSlotKey) {
						processedTemplate = strings.ReplaceAll(processedTemplate, defaultSlotKey, strings.TrimSpace(actualInnerContent))
					}
				}
			}

			processedTemplate = template_common.RemoveRemainingSlotPlaceholders(processedTemplate)

			// Recursively resolve nested slots and placeholders in the merged template
			processedTemplate = recursivelyResolveSlotsAndPlaceholders(processedTemplate, allTemplates, appSite)

			mapping := template_model.ReplacementMapping{
				OriginalText:    fullMatch,
				ReplacementText: processedTemplate,
				Type:            template_model.SlottedTemplateType,
			}
			template.ReplacementMappings = append(template.ReplacementMappings, mapping)
		}
	}
}

// processSlotContentForReplacementMappingRecursive processes slot content recursively for replacement mapping
func processSlotContentForReplacementMappingRecursive(slot template_model.SlotPlaceholder, allTemplates map[string]template_model.PreprocessedTemplate, appSite string) string {
	result := slot.Content

	// Recursively resolve nested slotted templates
	for _, nestedSlottedTemplate := range slot.NestedSlottedTemplates {
		targetTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), nestedSlottedTemplate.TemplateKey)
		if targetTemplate, exists := allTemplates[targetTemplateKey]; exists {
			processedTemplate := targetTemplate.OriginalContent

			for _, nestedSlot := range nestedSlottedTemplate.Slots {
				processedNestedSlotContent := processSlotContentForReplacementMappingRecursive(nestedSlot, allTemplates, appSite)
				processedTemplate = strings.ReplaceAll(processedTemplate, nestedSlot.SlotKey, processedNestedSlotContent)
			}

			processedTemplate = template_common.RemoveRemainingSlotPlaceholders(processedTemplate)
			// Recursively resolve further slots and placeholders
			processedTemplate = recursivelyResolveSlotsAndPlaceholders(processedTemplate, allTemplates, appSite)
			result = strings.ReplaceAll(result, nestedSlottedTemplate.FullMatch, processedTemplate)
		}
	}

	// Recursively resolve placeholders in slot content
	result = recursivelyResolveSlotsAndPlaceholders(result, allTemplates, appSite)
	return result
}

// recursivelyResolveSlotsAndPlaceholders resolves all slots and placeholders recursively in the content
func recursivelyResolveSlotsAndPlaceholders(content string, allTemplates map[string]template_model.PreprocessedTemplate, appSite string) string {
	previous := ""
	result := content
	for result != previous {
		previous = result
		// Resolve simple placeholders
		result = resolveSimplePlaceholders(result, allTemplates, appSite)
		// Resolve slotted templates
		result = resolveSlottedTemplates(result, allTemplates, appSite)
	}
	return result
}

// resolveSimplePlaceholders replaces simple template placeholders with their content
func resolveSimplePlaceholders(content string, allTemplates map[string]template_model.PreprocessedTemplate, appSite string) string {
	searchPos := 0
	result := content
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
		if placeholderName == "" || !template_common.IsAlphaNumeric(placeholderName) {
			searchPos = openStart + 2
			continue
		}
		templateKey := strings.ToLower(appSite) + "_" + strings.ToLower(placeholderName)
		if referencedTemplate, exists := allTemplates[templateKey]; exists {
			result = strings.Replace(result, result[openStart:closeStart+2], referencedTemplate.OriginalContent, 1)
			searchPos = openStart + len(referencedTemplate.OriginalContent)
		} else {
			searchPos = closeStart + 2
		}
	}
	return result
}

// resolveSlottedTemplates replaces slotted templates recursively
func resolveSlottedTemplates(content string, allTemplates map[string]template_model.PreprocessedTemplate, appSite string) string {
	searchPos := 0
	result := content
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
		if templateName == "" || !template_common.IsAlphaNumeric(templateName) {
			searchPos = openStart + 1
			continue
		}
		closeTag := "{{/" + templateName + "}}"
		closeStart := findMatchingCloseTag(result, openEnd+2, "{{#"+templateName+"}}", closeTag)
		if closeStart == -1 {
			searchPos = openStart + 1
			continue
		}
		// innerStart := openEnd + 2
		fullMatch := result[openStart : closeStart+len(closeTag)]
		targetTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
		if targetTemplate, exists := allTemplates[targetTemplateKey]; exists {
			processedTemplate := targetTemplate.OriginalContent
			// Recursively resolve slots in the inner content
			for _, slot := range targetTemplate.SlottedTemplates {
				for _, slotPlaceholder := range slot.Slots {
					processedSlotContent := processSlotContentForReplacementMappingRecursive(slotPlaceholder, allTemplates, appSite)
					processedTemplate = strings.ReplaceAll(processedTemplate, slotPlaceholder.SlotKey, processedSlotContent)
				}
			}
			processedTemplate = template_common.RemoveRemainingSlotPlaceholders(processedTemplate)
			processedTemplate = recursivelyResolveSlotsAndPlaceholders(processedTemplate, allTemplates, appSite)
			result = strings.Replace(result, fullMatch, processedTemplate, 1)
			searchPos = openStart + len(processedTemplate)
		} else {
			searchPos = closeStart + len(closeTag)
		}
	}
	return result
}

// preprocessTemplate creates a preprocessed template by parsing its structure and any associated JSON data
func preprocessTemplate(content string, jsonContent *string, appSite, templateKey string) template_model.PreprocessedTemplate {
	template := template_model.PreprocessedTemplate{
		OriginalContent:     content,
		HTML:                content,
		JSON:                jsonContent,
		ReplacementMappings: []template_model.ReplacementMapping{},
		JsonPlaceholders:    []template_model.JsonPlaceholder{},
	}

	if content == "" {
		return template
	}

	// Parse JSON data into a structure
	if jsonContent != nil && *jsonContent != "" {
		template.JsonData = preprocessJsonData(*jsonContent)
	}

	// Parse template structure
	parseSlottedTemplates(content, appSite, &template)
	parsePlaceholderTemplates(content, appSite, &template)

	// Preprocess JSON templates if JSON data exists
	if template.JsonData != nil {
		preprocessJsonTemplates(&template)
	}

	template.UpdateFlags()
	return template
}

// preprocessJsonData parses JSON content into a map structure
func preprocessJsonData(jsonContent string) *map[string]interface{} {
	var result map[string]interface{}
	if err := json.Unmarshal([]byte(jsonContent), &result); err != nil {
		return nil
	}
	return &result
}

// parseSlottedTemplates parses slotted templates in the content
func parseSlottedTemplates(content, appSite string, template *template_model.PreprocessedTemplate) {
	searchPos := 0

	for searchPos < len(content) {
		// Look for opening tag {{#
		openStart := strings.Index(content[searchPos:], "{{#")
		if openStart == -1 {
			break
		}
		openStart += searchPos

		// Find the end of the template name
		openEnd := strings.Index(content[openStart+3:], "}}")
		if openEnd == -1 {
			break
		}
		openEnd += openStart + 3

		// Extract template name
		templateName := strings.TrimSpace(content[openStart+3 : openEnd])
		if templateName == "" || !template_common.IsAlphaNumeric(templateName) {
			searchPos = openStart + 1
			continue
		}

		// Look for corresponding closing tag
		closeTag := "{{/" + templateName + "}}"
		closeStart := findMatchingCloseTag(content, openEnd+2, "{{#"+templateName+"}}", closeTag)
		if closeStart == -1 {
			searchPos = openStart + 1
			continue
		}

		// Extract inner content
		innerStart := openEnd + 2
		innerContent := content[innerStart:closeStart]
		fullMatch := content[openStart : closeStart+len(closeTag)]

		// Create slotted template structure
		slottedTemplate := template_model.SlottedTemplate{
			Name:         templateName,
			StartIndex:   openStart,
			EndIndex:     closeStart + len(closeTag),
			FullMatch:    fullMatch,
			InnerContent: innerContent,
			TemplateKey:  strings.ToLower(templateName),
			Slots:        []template_model.SlotPlaceholder{},
		}

		// Parse slots within this slotted template
		parseSlots(innerContent, &slottedTemplate, appSite)

		template.SlottedTemplates = append(template.SlottedTemplates, slottedTemplate)
		searchPos = closeStart + len(closeTag)
	}
}

// parsePlaceholderTemplates parses simple placeholders in the content
func parsePlaceholderTemplates(content, appSite string, template *template_model.PreprocessedTemplate) {
	searchPos := 0

	for searchPos < len(content) {
		// Look for opening placeholder {{
		openStart := strings.Index(content[searchPos:], "{{")
		if openStart == -1 {
			break
		}
		openStart += searchPos

		// Make sure it's not a slotted template or special placeholder
		if openStart+2 < len(content) && strings.ContainsAny(string(content[openStart+2]), "#@$/") {
			searchPos = openStart + 2
			continue
		}

		// Find closing }}
		closeStart := strings.Index(content[openStart+2:], "}}")
		if closeStart == -1 {
			break
		}
		closeStart += openStart + 2

		// Extract placeholder name
		placeholderName := strings.TrimSpace(content[openStart+2 : closeStart])
		if placeholderName == "" || !template_common.IsAlphaNumeric(placeholderName) {
			searchPos = openStart + 2
			continue
		}

		// Create placeholder structure
		placeholder := template_model.TemplatePlaceholder{
			Name:        placeholderName,
			StartIndex:  openStart,
			EndIndex:    closeStart + 2,
			FullMatch:   content[openStart : closeStart+2],
			TemplateKey: strings.ToLower(placeholderName),
		}

		template.Placeholders = append(template.Placeholders, placeholder)
		searchPos = closeStart + 2
	}
}

// parseSlots parses slots within a slotted template
func parseSlots(innerContent string, slottedTemplate *template_model.SlottedTemplate, appSite string) {
	searchPos := 0

	for searchPos < len(innerContent) {
		// Look for slot start {{@HTMLPLACEHOLDER
		slotStart := strings.Index(innerContent[searchPos:], "{{@HTMLPLACEHOLDER")
		if slotStart == -1 {
			break
		}
		slotStart += searchPos

		// Find the number (if any) and closing }}
		afterPlaceholder := slotStart + 18 // Length of "{{@HTMLPLACEHOLDER"
		slotNum := ""
		pos := afterPlaceholder

		// Extract slot number
		for pos < len(innerContent) && innerContent[pos] >= '0' && innerContent[pos] <= '9' {
			slotNum += string(innerContent[pos])
			pos++
		}

		// Check for closing }}
		if pos+1 >= len(innerContent) || innerContent[pos:pos+2] != "}}" {
			searchPos = slotStart + 1
			continue
		}

		slotOpenEnd := pos + 2

		// Find matching closing tag
		var closeTag, openTag string
		if slotNum == "" {
			closeTag = "{{/HTMLPLACEHOLDER}}"
			openTag = "{{@HTMLPLACEHOLDER}}"
		} else {
			closeTag = "{{/HTMLPLACEHOLDER" + slotNum + "}}"
			openTag = "{{@HTMLPLACEHOLDER" + slotNum + "}}"
		}

		closeStart := findMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag)
		if closeStart == -1 {
			searchPos = slotStart + 1
			continue
		}

		// Extract slot content
		slotContent := innerContent[slotOpenEnd:closeStart]

		// Generate slot key
		var slotKey string
		if slotNum == "" {
			slotKey = "{{$HTMLPLACEHOLDER}}"
		} else {
			slotKey = "{{$HTMLPLACEHOLDER" + slotNum + "}}"
		}

		// Create slot structure
		slot := template_model.SlotPlaceholder{
			Number:                 slotNum,
			StartIndex:             slotStart,
			EndIndex:               closeStart + len(closeTag),
			Content:                slotContent,
			SlotKey:                slotKey,
			OpenTag:                openTag,
			CloseTag:               closeTag,
			NestedPlaceholders:     []template_model.TemplatePlaceholder{},
			NestedSlottedTemplates: []template_model.SlottedTemplate{},
		}

		// Parse nested templates within the slot content
		parseNestedTemplatesInSlot(&slot, slottedTemplate.JsonData, appSite)

		slottedTemplate.Slots = append(slottedTemplate.Slots, slot)
		searchPos = closeStart + len(closeTag)
	}
}

// parseNestedTemplatesInSlot parses nested templates within slot content
func parseNestedTemplatesInSlot(slot *template_model.SlotPlaceholder, jsonData *map[string]interface{}, appSite string) {
	if slot.Content == "" {
		return
	}

	// Parse nested slotted templates first
	searchPos := 0
	for searchPos < len(slot.Content) {
		openStart := strings.Index(slot.Content[searchPos:], "{{#")
		if openStart == -1 {
			break
		}
		openStart += searchPos

		openEnd := strings.Index(slot.Content[openStart+3:], "}}")
		if openEnd == -1 {
			break
		}
		openEnd += openStart + 3

		templateName := strings.TrimSpace(slot.Content[openStart+3 : openEnd])
		if templateName == "" || !template_common.IsAlphaNumeric(templateName) {
			searchPos = openStart + 1
			continue
		}

		closeTag := "{{/" + templateName + "}}"
		closeStart := findMatchingCloseTag(slot.Content, openEnd+2, "{{#"+templateName+"}}", closeTag)
		if closeStart == -1 {
			searchPos = openStart + 1
			continue
		}

		innerStart := openEnd + 2
		innerContent := slot.Content[innerStart:closeStart]
		fullMatch := slot.Content[openStart : closeStart+len(closeTag)]

		slottedTemplate := template_model.SlottedTemplate{
			Name:         templateName,
			StartIndex:   openStart,
			EndIndex:     closeStart + len(closeTag),
			FullMatch:    fullMatch,
			InnerContent: innerContent,
			TemplateKey:  strings.ToLower(templateName),
			JsonData:     jsonData,
		}

		// Parse slots within this nested slotted template
		parseSlots(innerContent, &slottedTemplate, appSite)

		slot.NestedSlottedTemplates = append(slot.NestedSlottedTemplates, slottedTemplate)
		searchPos = closeStart + len(closeTag)
	}

	// Parse simple placeholders
	searchPos = 0
	for searchPos < len(slot.Content) {
		openStart := strings.Index(slot.Content[searchPos:], "{{")
		if openStart == -1 {
			break
		}
		openStart += searchPos

		if openStart+2 < len(slot.Content) && strings.ContainsAny(string(slot.Content[openStart+2]), "#/@$") {
			searchPos = openStart + 2
			continue
		}

		closeStart := strings.Index(slot.Content[openStart+2:], "}}")
		if closeStart == -1 {
			break
		}
		closeStart += openStart + 2

		templateName := strings.TrimSpace(slot.Content[openStart+2 : closeStart])
		if templateName == "" || !template_common.IsAlphaNumeric(templateName) {
			searchPos = openStart + 2
			continue
		}

		placeholder := template_model.TemplatePlaceholder{
			Name:        templateName,
			StartIndex:  openStart,
			EndIndex:    closeStart + 2,
			FullMatch:   slot.Content[openStart : closeStart+2],
			TemplateKey: strings.ToLower(templateName),
			JsonData:    jsonData,
		}

		slot.NestedPlaceholders = append(slot.NestedPlaceholders, placeholder)
		searchPos = closeStart + 2
	}
}

// preprocessJsonTemplates preprocesses JSON templates by creating replacement mappings
func preprocessJsonTemplates(template *template_model.PreprocessedTemplate) {
	if template.JsonData == nil {
		return
	}

	content := template.OriginalContent

	// Create replacement mappings for JSON array blocks
	createJsonArrayReplacementMappings(template, content)

	// Create replacement mappings for JSON placeholders
	createJsonPlaceholderReplacementMappings(template, content)
}

// createJsonArrayReplacementMappings creates replacement mappings for JSON array blocks
func createJsonArrayReplacementMappings(template *template_model.PreprocessedTemplate, content string) {
	if template.JsonData == nil {
		return
	}

	for key, value := range *template.JsonData {
		if dataList, ok := value.([]interface{}); ok {
			// Try to find a matching template block for this JSON array
			keyNorm := strings.ToLower(key)
			possibleTags := []string{key, keyNorm, strings.TrimSuffix(keyNorm, "s"), keyNorm + "s"}

			for _, tag := range possibleTags {
				blockStartTag := "{{@" + tag + "}}"
				blockEndTag := "{{/" + tag + "}}"

				startIdx := findCaseInsensitive(content, blockStartTag)
				if startIdx != -1 {
					searchFrom := startIdx + len(blockStartTag)
					endIdx := findCaseInsensitive(content[searchFrom:], blockEndTag)
					if endIdx != -1 {
						endIdx = searchFrom + endIdx

						if startIdx < endIdx {
							// Found a valid block - extract content and process it
							blockContent := content[startIdx+len(blockStartTag) : endIdx]
							fullBlock := content[startIdx : endIdx+len(blockEndTag)]

							// Process the array content
							processedArrayContent := processArrayBlockContent(blockContent, dataList)

							// Create direct replacement mapping
							template.ReplacementMappings = append(template.ReplacementMappings, template_model.ReplacementMapping{
								StartIndex:      startIdx,
								EndIndex:        endIdx + len(blockEndTag),
								OriginalText:    fullBlock,
								ReplacementText: processedArrayContent,
								Type:            template_model.JsonPlaceholderType,
							})

							// Handle empty array blocks
							emptyBlockStart := "{{^" + tag + "}}"
							emptyBlockEnd := "{{/" + tag + "}}"
							emptyStartIdx := findCaseInsensitive(content, emptyBlockStart)
							if emptyStartIdx != -1 {
								emptySearchFrom := emptyStartIdx + len(emptyBlockStart)
								emptyEndIdx := findCaseInsensitive(content[emptySearchFrom:], emptyBlockEnd)
								if emptyEndIdx != -1 {
									emptyEndIdx = emptySearchFrom + emptyEndIdx

									if emptyEndIdx > emptyStartIdx+len(emptyBlockStart) {
										emptyBlockContent := content[emptyStartIdx+len(emptyBlockStart) : emptyEndIdx]
										fullEmptyBlock := content[emptyStartIdx : emptyEndIdx+len(emptyBlockEnd)]
										var emptyReplacement string
										if len(dataList) == 0 {
											emptyReplacement = emptyBlockContent
										}

										template.ReplacementMappings = append(template.ReplacementMappings, template_model.ReplacementMapping{
											StartIndex:      emptyStartIdx,
											EndIndex:        emptyEndIdx + len(emptyBlockEnd),
											OriginalText:    fullEmptyBlock,
											ReplacementText: emptyReplacement,
											Type:            template_model.JsonPlaceholderType,
										})
									}
								}
							}

							break // Process only the first matching template for this JSON key
						}
					}
				}
			}
		}
	}
}

// processArrayBlockContent processes array block content by iterating through JSON array data
func processArrayBlockContent(blockContent string, arrayData []interface{}) string {
	var mergedBlock strings.Builder

	// Process each item in the array data
	for _, item := range arrayData {
		if jsonItem, ok := item.(map[string]interface{}); ok {
			itemBlock := blockContent

			// Replace all placeholders for this item
			for k, v := range jsonItem {
				placeholder := "{{$" + k + "}}"
				valueStr := ""
				if v != nil {
					if bytes, err := json.Marshal(v); err == nil {
						valueStr = strings.ReplaceAll(string(bytes), "\"", "")
						if valueStr == "null" {
							valueStr = ""
						}
					}
				}
				itemBlock = replaceAllCaseInsensitive(itemBlock, placeholder, valueStr)
			}

			// Handle conditional blocks for this item
			itemBlock = processConditionalBlocks(itemBlock, jsonItem)

			mergedBlock.WriteString(itemBlock)
		}
	}

	return mergedBlock.String()
}

// processConditionalBlocks processes conditional blocks in content
func processConditionalBlocks(content string, jsonItem map[string]interface{}) string {
	result := content

	// Find all conditional keys in the content
	conditionalKeys := findConditionalKeysInContent(result)

	for condKey := range conditionalKeys {
		condValue := getConditionValue(jsonItem, condKey)
		result = processConditionalBlock(result, condKey, condValue)
	}

	return result
}

// findConditionalKeysInContent finds conditional keys in content
func findConditionalKeysInContent(content string) map[string]bool {
	conditionalKeys := make(map[string]bool)
	condIdx := 0

	for condIdx < len(content) {
		condStart := strings.Index(content[condIdx:], "{{@")
		if condStart == -1 {
			break
		}
		condStart += condIdx
		condEnd := strings.Index(content[condStart:], "}}")
		if condEnd == -1 {
			break
		}
		condEnd += condStart
		condKey := strings.TrimSpace(content[condStart+3 : condEnd])
		conditionalKeys[condKey] = true
		condIdx = condEnd + 2
	}

	return conditionalKeys
}

// getConditionValue gets condition value from item data
func getConditionValue(item map[string]interface{}, condKey string) bool {
	// First try exact match
	if condObj, exists := item[condKey]; exists && condObj != nil {
		if boolValue, ok := condObj.(bool); ok {
			return boolValue
		}
		if strValue, ok := condObj.(string); ok {
			return strValue != "" && strings.ToLower(strValue) != "false"
		}
		if numValue, ok := condObj.(float64); ok {
			return numValue != 0
		}
		if intValue, ok := condObj.(int); ok {
			return intValue != 0
		}
	}

	// Try case-insensitive match
	for k, v := range item {
		if strings.EqualFold(k, condKey) && v != nil {
			if boolValue, ok := v.(bool); ok {
				return boolValue
			}
			if strValue, ok := v.(string); ok {
				return strValue != "" && strings.ToLower(strValue) != "false"
			}
			if numValue, ok := v.(float64); ok {
				return numValue != 0
			}
			if intValue, ok := v.(int); ok {
				return intValue != 0
			}
		}
	}

	return false
}

// processConditionalBlock processes a single conditional block
func processConditionalBlock(input, key string, condition bool) string {
	// Support both space variants: {{ /Key}} and {{/Key}}
	conditionTags := [][]string{
		{"{{@" + key + "}}", "{{ /" + key + "}}"},
		{"{{@" + key + "}}", "{{/" + key + "}}"},
	}

	for _, tags := range conditionTags {
		condStart, condEnd := tags[0], tags[1]
		startIdx := findCaseInsensitive(input, condStart)
		endIdx := findCaseInsensitive(input, condEnd)

		for startIdx != -1 && endIdx != -1 {
			contentStart := startIdx + len(condStart)
			if endIdx > contentStart {
				content := input[contentStart:endIdx]
				if condition {
					input = input[:startIdx] + content + input[endIdx+len(condEnd):]
				} else {
					input = input[:startIdx] + input[endIdx+len(condEnd):]
				}
			} else {
				break
			}

			startIdx = findCaseInsensitive(input, condStart)
			endIdx = findCaseInsensitive(input, condEnd)
		}
	}

	return input
}

// createJsonPlaceholderReplacementMappings creates replacement mappings for JSON placeholders
func createJsonPlaceholderReplacementMappings(template *template_model.PreprocessedTemplate, content string) {
	if template.JsonData == nil {
		return
	}

	for k, v := range *template.JsonData {
		if stringValue, ok := v.(string); ok {
			// Handle both {{$key}} and {{key}} patterns
			placeholders := []string{
				"{{$" + k + "}}",
				"{{" + k + "}}",
			}

			for _, placeholder := range placeholders {
				if findCaseInsensitive(content, placeholder) != -1 {
					// Create replacement mapping for direct replacement
					template.ReplacementMappings = append(template.ReplacementMappings, template_model.ReplacementMapping{
						OriginalText:    placeholder,
						ReplacementText: stringValue,
						Type:            template_model.JsonPlaceholderType,
					})

					// Also create JsonPlaceholder for backward compatibility
					template.JsonPlaceholders = append(template.JsonPlaceholders, template_model.JsonPlaceholder{
						Key:         k,
						Placeholder: placeholder,
						Value:       stringValue,
					})
				}
			}
		}
	}
}

// Helper functions

// findCaseInsensitive finds a substring case-insensitively
func findCaseInsensitive(s, substr string) int {
	return strings.Index(strings.ToLower(s), strings.ToLower(substr))
}

// replaceAllCaseInsensitive replaces all case-insensitive occurrences
func replaceAllCaseInsensitive(input, search, replacement string) string {
	lowerInput := strings.ToLower(input)
	lowerSearch := strings.ToLower(search)

	idx := 0
	for {
		found := strings.Index(lowerInput[idx:], lowerSearch)
		if found == -1 {
			break
		}
		found += idx
		input = input[:found] + replacement + input[found+len(search):]
		lowerInput = lowerInput[:found] + strings.ToLower(replacement) + lowerInput[found+len(search):]
		idx = found + len(replacement)
	}
	return input
}

// findMatchingCloseTag finds matching close tag with proper nesting
func findMatchingCloseTag(content string, searchFrom int, openTag, closeTag string) int {
	if searchFrom >= len(content) {
		return -1
	}

	depth := 1
	pos := searchFrom

	for pos < len(content) && depth > 0 {
		// Look for next occurrence of either open or close tag
		nextOpen := strings.Index(content[pos:], openTag)
		nextClose := strings.Index(content[pos:], closeTag)

		if nextClose == -1 {
			return -1 // No closing tag found
		}

		if nextOpen != -1 && nextOpen < nextClose {
			// Found another opening tag first
			depth++
			pos += nextOpen + len(openTag)
		} else {
			// Found closing tag
			depth--
			if depth == 0 {
				return pos + nextClose
			}
			pos += nextClose + len(closeTag)
		}
	}

	return -1
}

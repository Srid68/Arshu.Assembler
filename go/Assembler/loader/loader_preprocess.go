package loader

import (
	Logger "arshu/common"
	"assembler/common"
	"assembler/model"
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

var preprocessedTemplatesCache = struct {
	sync.RWMutex
	cache map[string]*model.PreprocessedSiteTemplates
}{cache: make(map[string]*model.PreprocessedSiteTemplates)}

func LoadProcessGetTemplateFiles(rootDirPath, appSite, searchAppSites string) *model.PreprocessedSiteTemplates {
	Logger.Debug(fmt.Sprintf("LoadProcessGetTemplateFiles called for appSite: %s, searchAppSites: %s", appSite, searchAppSites), "LoaderPreProcess")

	// ...existing code...
	// Strict C#-style logic and logging

	cacheKey := filepath.Dir(rootDirPath) + "|" + appSite + "|" + searchAppSites
	preprocessedTemplatesCache.RLock()
	cached, ok := preprocessedTemplatesCache.cache[cacheKey]
	preprocessedTemplatesCache.RUnlock()
	if ok {
		Logger.Debug(fmt.Sprintf("Returning cached templates for %s (%d templates)", appSite, len(cached.Templates)), "LoaderPreProcess")
		return cached
	}

	// Load templates from primary appSite
	result := loadTemplatesFromSingleAppSite(rootDirPath, appSite)

	// Load templates from searchAppSites for fallback
	if searchAppSites != "" {
		searchAppSitesArray := strings.Split(searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}
			searchResult := loadTemplatesFromSingleAppSite(rootDirPath, searchAppSite)
			for k, v := range searchResult.Templates {
				if _, exists := result.Templates[k]; !exists {
					result.Templates[k] = v
					result.RawTemplates[k] = searchResult.RawTemplates[k]
					result.TemplateKeys[k] = struct{}{}
					Logger.Debug(fmt.Sprintf("Added fallback template '%s' from '%s'", k, searchAppSite), "LoaderPreProcess")
				}
			}
		}
	}

	// CRITICAL: Create ALL replacement mappings after all templates are loaded
	// This ensures PreProcess engine does ONLY merging, no processing logic
	createAllReplacementMappingsForSite(result, appSite)
	Logger.Debug(fmt.Sprintf("Created all replacement mappings for %s", appSite), "LoaderPreProcess")

	preprocessedTemplatesCache.Lock()
	preprocessedTemplatesCache.cache[cacheKey] = result
	preprocessedTemplatesCache.Unlock()
	return result
}

// loadTemplatesFromSingleAppSite loads templates from a single AppSite without caching or fallback logic
func loadTemplatesFromSingleAppSite(rootDirPath, appSite string) *model.PreprocessedSiteTemplates {
	result := &model.PreprocessedSiteTemplates{
		SiteName:     appSite,
		Templates:    make(map[string]model.PreprocessedTemplate),
		RawTemplates: make(map[string]string),
		TemplateKeys: make(map[string]struct{}),
	}

	appSitesPath := filepath.Join(rootDirPath, "AppSites", appSite)
	if stat, err := os.Stat(appSitesPath); err != nil || !stat.IsDir() {
		Logger.Warn(fmt.Sprintf("AppSites directory not found: %s", appSitesPath), "LoaderPreProcess")
		return result
	}

	Logger.Debug(fmt.Sprintf("Loading templates from: %s", appSitesPath), "LoaderPreProcess")

	_ = filepath.Walk(appSitesPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(info.Name(), ".html") {
			return nil
		}
		fileName := strings.TrimSuffix(info.Name(), ".html")
		key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(fileName))

		htmlBytes, err := os.ReadFile(path)
		if err != nil {
			return nil
		}
		content := common.NormalizeFileContent(string(htmlBytes))

		Logger.Debug(fmt.Sprintf("Loading template: %s (size: %d)", key, len(content)), "LoaderPreProcess")

		// Find JSON file case-insensitively
		jsonFile := strings.TrimSuffix(path, ".html") + ".json"
		var jsonContent *string

		// Try exact match first
		if _, err := os.Stat(jsonFile); err == nil {
			jsonBytes, err := os.ReadFile(jsonFile)
			if err == nil {
				jsonStr := common.NormalizeFileContent(string(jsonBytes))
				jsonContent = &jsonStr
				Logger.Debug(fmt.Sprintf("Found JSON file for %s (size: %d)", key, len(jsonStr)), "LoaderPreProcess")
			}
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
							jsonBytes, err := os.ReadFile(matchedJsonPath)
							if err == nil {
								jsonStr := common.NormalizeFileContent(string(jsonBytes))
								jsonContent = &jsonStr
								Logger.Debug(fmt.Sprintf("Found JSON file (case-insensitive) for %s (size: %d)", key, len(jsonStr)), "LoaderPreProcess")
							}
							break
						}
					}
				}
			}
		}

		// Store raw template for backward compatibility
		result.RawTemplates[key] = content
		result.TemplateKeys[key] = struct{}{}

		// Preprocess the template with JSON data
		preprocessed := preprocessTemplate(content, jsonContent, appSite, key)
		result.Templates[key] = preprocessed

		Logger.Debug(fmt.Sprintf("Preprocessed %s: %d replacements, %d slotted, %d placeholders", key, len(preprocessed.ReplacementMappings), len(preprocessed.SlottedTemplates), len(preprocessed.Placeholders)), "LoaderPreProcess")
		return nil
	})

	Logger.Debug(fmt.Sprintf("Loaded %d templates for %s", len(result.Templates), appSite), "LoaderPreProcess")
	return result
}

// ClearPreProcessCache clears all cached preprocessed templates (useful for testing or when templates change)
func ClearPreProcessCache() {
	preprocessedTemplatesCache.Lock()
	preprocessedTemplatesCache.cache = make(map[string]*model.PreprocessedSiteTemplates)
	preprocessedTemplatesCache.Unlock()
}

// createAllReplacementMappingsForSite creates all replacement mappings for a site with AppView support
// Critical architectural method - moves ALL processing from engine to loader
func createAllReplacementMappingsForSite(siteTemplates *model.PreprocessedSiteTemplates, appSite string) {
	// Phase 0: Build parent map and resolve JSON inheritance BEFORE creating any mappings
	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 0: JSON inheritance", appSite), "LoaderPreProcess")
	parentMap := buildParentMapForPreProcess(siteTemplates, appSite)
	resolveJsonInheritanceForAllTemplates(siteTemplates, parentMap)

	// After resolving inheritance, recreate JSON placeholder mappings with the resolved values
	recreateJsonPlaceholderMappingsAfterInheritance(siteTemplates)

	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 1: JSON arrays", appSite), "LoaderPreProcess")

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

	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 2: Simple placeholders", appSite), "LoaderPreProcess")

	// Phase 2: Create simple template replacement mappings (may depend on JSON but not on slotted templates)
	allTemplatesSnapshot := make(map[string]model.PreprocessedTemplate)
	for key, template := range siteTemplates.Templates {
		allTemplatesSnapshot[key] = template
	}

	for _, key := range templateKeys {
		if template, exists := siteTemplates.Templates[key]; exists {
			createPlaceholderReplacementMappings(&template, allTemplatesSnapshot, appSite)
			siteTemplates.Templates[key] = template
		}
	}

	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 3: Slotted templates", appSite), "LoaderPreProcess")

	// Phase 3: Create slotted template replacement mappings (may depend on other templates)
	for _, key := range templateKeys {
		if template, exists := siteTemplates.Templates[key]; exists {
			createSlottedTemplateReplacementMappings(&template, allTemplatesSnapshot, appSite)
			siteTemplates.Templates[key] = template
		}
	}

	// Log summary of all replacement mappings
	totalMappings := 0
	for _, template := range siteTemplates.Templates {
		totalMappings += len(template.ReplacementMappings)
	}
	Logger.Info(fmt.Sprintf("Total replacement mappings created for %s: %d", appSite, totalMappings), "LoaderPreProcess")
}

// createPlaceholderReplacementMappings creates replacement mappings for simple placeholders ({{templatename}})
// This moves ALL placeholder processing logic from PreProcess engine to TemplateLoader
func createPlaceholderReplacementMappings(template *model.PreprocessedTemplate, allTemplates map[string]model.PreprocessedTemplate, appSite string) {
	if len(template.Placeholders) == 0 {
		return
	}

	for _, placeholder := range template.Placeholders {
		// FIRST: Try current appSite
		targetTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), placeholder.TemplateKey)
		var targetTemplate model.PreprocessedTemplate
		var found bool

		if targetTemplate, found = allTemplates[targetTemplateKey]; !found {
			// SECOND: Search in all loaded templates (includes searchAppSites)
			searchKey := "_" + placeholder.TemplateKey
			for key, tmpl := range allTemplates {
				if strings.HasSuffix(strings.ToLower(key), searchKey) {
					targetTemplate = tmpl
					found = true
					Logger.Debug(fmt.Sprintf("Template '%s' not found as '%s', using fallback from '%s'", placeholder.TemplateKey, targetTemplateKey, key), "LoaderPreProcess")
					break
				}
			}
		}

		if found {
			// Start with the target template's original content
			processedTemplate := targetTemplate.OriginalContent

			// CRITICAL FIX: Apply the target template's JSON placeholder replacements
			// This ensures nested components use their own JSON context (e.g., header.json for Header component)
			var jsonMappings []model.ReplacementMapping
			for _, m := range targetTemplate.ReplacementMappings {
				if m.Type == model.JsonPlaceholderType {
					jsonMappings = append(jsonMappings, m)
				}
			}
			Logger.Debug(fmt.Sprintf("Applying %d JSON mappings to %s", len(jsonMappings), targetTemplateKey), "LoaderPreProcess")
			Logger.Debug(fmt.Sprintf("Before replacements, template size: %d", len(processedTemplate)), "LoaderPreProcess")
			for _, jsonMapping := range jsonMappings {
				before := len(processedTemplate)
				Logger.Debug(fmt.Sprintf("  Replacing placeholder (original size: %d, replacement size: %d)", len(jsonMapping.OriginalText), len(jsonMapping.ReplacementText)), "LoaderPreProcess")
				processedTemplate = strings.ReplaceAll(processedTemplate, jsonMapping.OriginalText, jsonMapping.ReplacementText)
				after := len(processedTemplate)
				Logger.Debug(fmt.Sprintf("    Size changed from %d to %d (diff: %d)", before, after, after-before), "LoaderPreProcess")
			}
			Logger.Debug(fmt.Sprintf("After replacements, template size: %d", len(processedTemplate)), "LoaderPreProcess")

			// Create the replacement mapping
			Logger.Debug(fmt.Sprintf("Creating replacement mapping: %s -> processed template (size: %d)", placeholder.FullMatch, len(processedTemplate)), "LoaderPreProcess")
			mapping := model.ReplacementMapping{
				OriginalText:    placeholder.FullMatch,
				ReplacementText: processedTemplate,
				Type:            model.SimpleTemplateType,
			}
			template.ReplacementMappings = append(template.ReplacementMappings, mapping)
		}
	}
}

// createSlottedTemplateReplacementMappings creates replacement mappings for slotted templates
func createSlottedTemplateReplacementMappings(template *model.PreprocessedTemplate, allTemplates map[string]model.PreprocessedTemplate, appSite string) {
	if len(template.SlottedTemplates) == 0 {
		return
	}

	for _, slottedTemplate := range template.SlottedTemplates {
		fullMatch := slottedTemplate.FullMatch
		targetTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), slottedTemplate.TemplateKey)

		// FIRST: Try current appSite
		var targetTemplate model.PreprocessedTemplate
		var found bool
		if targetTemplate, found = allTemplates[targetTemplateKey]; !found {
			// SECOND: Search in all loaded templates (includes searchAppSites)
			searchKey := "_" + slottedTemplate.TemplateKey
			for key, tmpl := range allTemplates {
				if strings.HasSuffix(strings.ToLower(key), searchKey) {
					targetTemplate = tmpl
					found = true
					Logger.Debug(fmt.Sprintf("Slotted template '%s' not found as '%s', using fallback from '%s'", slottedTemplate.TemplateKey, targetTemplateKey, key), "LoaderPreProcess")
					break
				}
			}
		}

		if found {
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

			processedTemplate = common.RemoveRemainingSlotPlaceholders(processedTemplate)

			mapping := model.ReplacementMapping{
				OriginalText:    fullMatch,
				ReplacementText: processedTemplate,
				Type:            model.SlottedTemplateType,
			}
			template.ReplacementMappings = append(template.ReplacementMappings, mapping)
		}
	}
}

// processSlotContentForReplacementMappingRecursive processes slot content recursively for replacement mapping
func processSlotContentForReplacementMappingRecursive(slot model.SlotPlaceholder, allTemplates map[string]model.PreprocessedTemplate, appSite string) string {
	result := slot.Content

	// Recursively resolve nested slotted templates
	for _, nestedSlottedTemplate := range slot.NestedSlottedTemplates {
		targetTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), nestedSlottedTemplate.TemplateKey)
		var targetTemplate model.PreprocessedTemplate
		var found bool
		if targetTemplate, found = allTemplates[targetTemplateKey]; !found {
			searchKey := "_" + nestedSlottedTemplate.TemplateKey
			for key, tmpl := range allTemplates {
				if strings.HasSuffix(strings.ToLower(key), strings.ToLower(searchKey)) {
					targetTemplate = tmpl
					found = true
					Logger.Debug(fmt.Sprintf("Nested slotted template '%s' not found as '%s', using fallback from '%s'", nestedSlottedTemplate.TemplateKey, targetTemplateKey, key), "LoaderPreProcess")
					break
				}
			}
		}

		if found {
			processedTemplate := targetTemplate.OriginalContent

			for _, nestedSlot := range nestedSlottedTemplate.Slots {
				processedNestedSlotContent := processSlotContentForReplacementMappingRecursive(nestedSlot, allTemplates, appSite)
				processedTemplate = strings.ReplaceAll(processedTemplate, nestedSlot.SlotKey, processedNestedSlotContent)
			}

			processedTemplate = common.RemoveRemainingSlotPlaceholders(processedTemplate)
			result = strings.ReplaceAll(result, nestedSlottedTemplate.FullMatch, processedTemplate)
		}
	}

	// Process nested simple placeholders
	for _, nestedPlaceholder := range slot.NestedPlaceholders {
		targetTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), nestedPlaceholder.TemplateKey)
		var targetTemplate model.PreprocessedTemplate
		var found bool
		if targetTemplate, found = allTemplates[targetTemplateKey]; !found {
			searchKey := "_" + nestedPlaceholder.TemplateKey
			for key, tmpl := range allTemplates {
				if strings.HasSuffix(strings.ToLower(key), strings.ToLower(searchKey)) {
					targetTemplate = tmpl
					found = true
					Logger.Debug(fmt.Sprintf("Nested placeholder '%s' not found as '%s', using fallback from '%s'", nestedPlaceholder.TemplateKey, targetTemplateKey, key), "LoaderPreProcess")
					break
				}
			}
		}

		if found {
			// Start with the target template's original content
			processedTemplate := targetTemplate.OriginalContent

			// CRITICAL FIX: Apply the target template's JSON placeholder replacements
			// This ensures nested components use their own JSON context
			for _, mapping := range targetTemplate.ReplacementMappings {
				if mapping.Type == model.JsonPlaceholderType {
					processedTemplate = strings.ReplaceAll(processedTemplate, mapping.OriginalText, mapping.ReplacementText)
				}
			}

			// Replace in result
			result = strings.ReplaceAll(result, nestedPlaceholder.FullMatch, processedTemplate)
		}
	}

	return result
}

// preprocessTemplate creates a preprocessed template by parsing its structure and any associated JSON data
func preprocessTemplate(content string, jsonContent *string, appSite, templateKey string) model.PreprocessedTemplate {
	template := model.PreprocessedTemplate{
		OriginalContent:     content,
		HTML:                content,
		JSON:                jsonContent,
		ReplacementMappings: []model.ReplacementMapping{},
		JsonPlaceholders:    []model.JsonPlaceholder{},
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
func parseSlottedTemplates(content, appSite string, template *model.PreprocessedTemplate) {
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
		if templateName == "" || !common.IsAlphaNumeric(templateName) {
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
		slottedTemplate := model.SlottedTemplate{
			Name:         templateName,
			StartIndex:   openStart,
			EndIndex:     closeStart + len(closeTag),
			FullMatch:    fullMatch,
			InnerContent: innerContent,
			TemplateKey:  strings.ToLower(templateName),
			Slots:        []model.SlotPlaceholder{},
		}

		// Parse slots within this slotted template
		parseSlots(innerContent, &slottedTemplate, appSite)

		template.SlottedTemplates = append(template.SlottedTemplates, slottedTemplate)
		searchPos = closeStart + len(closeTag)
	}
}

// parsePlaceholderTemplates parses simple placeholders in the content
func parsePlaceholderTemplates(content, appSite string, template *model.PreprocessedTemplate) {
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
		if placeholderName == "" || !common.IsAlphaNumeric(placeholderName) {
			searchPos = openStart + 2
			continue
		}

		// Create placeholder structure
		placeholder := model.TemplatePlaceholder{
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
func parseSlots(innerContent string, slottedTemplate *model.SlottedTemplate, appSite string) {
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
		slot := model.SlotPlaceholder{
			Number:                    slotNum,
			StartIndex:                slotStart,
			EndIndex:                  closeStart + len(closeTag),
			Content:                   slotContent,
			SlotKey:                   slotKey,
			OpenTag:                   openTag,
			CloseTag:                  closeTag,
			NestedPlaceholders:        []model.TemplatePlaceholder{},
			NestedSlottedTemplates:    []model.SlottedTemplate{},
			HasNestedPlaceholders:     false, // Will be updated after parsing
			HasNestedSlottedTemplates: false, // Will be updated after parsing
			RequiresNestedProcessing:  false, // Will be updated after parsing
		}

		// Parse nested templates within the slot content
		parseNestedTemplatesInSlot(&slot, slottedTemplate.JsonData, appSite)

		// Update boolean flags after parsing nested content
		slot.HasNestedPlaceholders = len(slot.NestedPlaceholders) > 0
		slot.HasNestedSlottedTemplates = len(slot.NestedSlottedTemplates) > 0
		slot.RequiresNestedProcessing = slot.HasNestedPlaceholders || slot.HasNestedSlottedTemplates

		slottedTemplate.Slots = append(slottedTemplate.Slots, slot)
		searchPos = closeStart + len(closeTag)
	}
}

// parseNestedTemplatesInSlot parses nested templates within slot content
func parseNestedTemplatesInSlot(slot *model.SlotPlaceholder, jsonData *map[string]interface{}, appSite string) {
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
		if templateName == "" || !common.IsAlphaNumeric(templateName) {
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

		slottedTemplate := model.SlottedTemplate{
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
		if templateName == "" || !common.IsAlphaNumeric(templateName) {
			searchPos = openStart + 2
			continue
		}

		placeholder := model.TemplatePlaceholder{
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
func preprocessJsonTemplates(template *model.PreprocessedTemplate) {
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
func createJsonArrayReplacementMappings(template *model.PreprocessedTemplate, content string) {
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
							template.ReplacementMappings = append(template.ReplacementMappings, model.ReplacementMapping{
								StartIndex:      startIdx,
								EndIndex:        endIdx + len(blockEndTag),
								OriginalText:    fullBlock,
								ReplacementText: processedArrayContent,
								Type:            model.JsonPlaceholderType,
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

										template.ReplacementMappings = append(template.ReplacementMappings, model.ReplacementMapping{
											StartIndex:      emptyStartIdx,
											EndIndex:        emptyEndIdx + len(emptyBlockEnd),
											OriginalText:    fullEmptyBlock,
											ReplacementText: emptyReplacement,
											Type:            model.JsonPlaceholderType,
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
					var buf bytes.Buffer
					encoder := json.NewEncoder(&buf)
					encoder.SetEscapeHTML(false) // Disable HTML escaping to preserve &, <, >, etc.
					if err := encoder.Encode(v); err == nil {
						valueStr = strings.TrimSpace(buf.String()) // Encoder adds a newline, so trim it
						valueStr = strings.ReplaceAll(valueStr, "\"", "")
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
func createJsonPlaceholderReplacementMappings(template *model.PreprocessedTemplate, content string) {
	if template.JsonData == nil {
		return
	}

	for k, v := range *template.JsonData {
		if stringValue, ok := v.(string); ok {
			placeholder := "{{$" + k + "}}"
			if findCaseInsensitive(content, placeholder) != -1 {
				if !mappingExists(template.ReplacementMappings, placeholder) {
					template.ReplacementMappings = append(template.ReplacementMappings, model.ReplacementMapping{
						OriginalText:    placeholder,
						ReplacementText: stringValue,
						Type:            model.JsonPlaceholderType,
					})
				}

				if !jsonPlaceholderExists(template.JsonPlaceholders, placeholder) {
					template.JsonPlaceholders = append(template.JsonPlaceholders, model.JsonPlaceholder{
						Key:         k,
						Placeholder: placeholder,
						Value:       stringValue,
					})
				}
			}
		}
	}
}

func mappingExists(mappings []model.ReplacementMapping, placeholder string) bool {
	for _, mapping := range mappings {
		if strings.EqualFold(mapping.OriginalText, placeholder) && mapping.Type == model.JsonPlaceholderType {
			return true
		}
	}
	return false
}

func jsonPlaceholderExists(placeholders []model.JsonPlaceholder, placeholder string) bool {
	for _, p := range placeholders {
		if strings.EqualFold(p.Placeholder, placeholder) {
			return true
		}
	}
	return false
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

// #region JSON Inheritance Support
// NOTE: The following methods are INTENTIONAL COPIES used across all loaders for architectural separation.
// Each loader/engine pair is independent to allow individual evolution without shared dependencies.
// DO NOT extract these to shared utilities - that would create tight coupling.

// buildParentMapForPreProcess builds a parent-child relationship map by analyzing template placeholders
// Tracks which template is the parent of another based on {{TemplateName}} references
func buildParentMapForPreProcess(siteTemplates *model.PreprocessedSiteTemplates, appSite string) map[string]string {
	parentMap := make(map[string]string)

	Logger.Debug(fmt.Sprintf("Building parent map for appSite: %s", appSite), "LoaderPreProcess")

	for templateKey, template := range siteTemplates.Templates {
		// Find all {{TemplateName}} placeholders in this template
		for _, placeholder := range template.Placeholders {
			placeholderName := placeholder.Name

			// This template (templateKey) is the parent of the placeholder template
			childTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(placeholderName))

			if _, exists := parentMap[childTemplateKey]; !exists {
				parentMap[childTemplateKey] = templateKey
				Logger.Debug(fmt.Sprintf("Parent relationship: %s -> parent: %s", childTemplateKey, templateKey), "LoaderPreProcess")
			}
		}

		// Also check slotted templates
		for _, slottedTemplate := range template.SlottedTemplates {
			templateName := slottedTemplate.Name
			childTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))

			if _, exists := parentMap[childTemplateKey]; !exists {
				parentMap[childTemplateKey] = templateKey
				Logger.Debug(fmt.Sprintf("Parent relationship (slotted): %s -> parent: %s", childTemplateKey, templateKey), "LoaderPreProcess")
			}
		}
	}

	Logger.Debug(fmt.Sprintf("Built parent map with %d relationships", len(parentMap)), "LoaderPreProcess")
	return parentMap
}

// resolveJsonInheritanceForAllTemplates resolves JSON inheritance for all templates by modifying their JsonData in place
func resolveJsonInheritanceForAllTemplates(siteTemplates *model.PreprocessedSiteTemplates, parentMap map[string]string) {
	for templateKey, template := range siteTemplates.Templates {
		if template.JsonData == nil || len(*template.JsonData) == 0 {
			continue
		}

		// Resolve inheritance for this template
		resolvedJson := make(map[string]interface{})
		hasInheritance := false

		for key, value := range *template.JsonData {
			// Check if this is an inheritable key (ends with #)
			if strings.HasSuffix(key, "#") {
				if strValue, ok := value.(string); ok {
					hasInheritance = true
					actualKey := key[:len(key)-1]
					resolvedValue := searchParentTreeForKeyPreProcess(actualKey, templateKey, siteTemplates.Templates, parentMap)

					if resolvedValue != "" {
						resolvedJson[actualKey] = resolvedValue
						Logger.Debug(fmt.Sprintf("Resolved inherited key %s -> %s = %s for template %s", key, actualKey, resolvedValue, templateKey), "LoaderPreProcess")
					} else {
						// Use default value if not found in parents
						resolvedJson[actualKey] = strValue
						Logger.Debug(fmt.Sprintf("No inherited value found for %s, using default: %s", actualKey, strValue), "LoaderPreProcess")
					}
				}
			} else {
				// Normal key - keep as is
				resolvedJson[key] = value
			}
		}

		// Replace JsonData with resolved version if any inheritance was found
		if hasInheritance {
			template.JsonData = &resolvedJson
			siteTemplates.Templates[templateKey] = template
			Logger.Debug(fmt.Sprintf("Updated JsonData for template %s with resolved inheritance", templateKey), "LoaderPreProcess")
		}
	}
}

// recreateJsonPlaceholderMappingsAfterInheritance recreates JSON placeholder replacement mappings after inheritance resolution
// This is needed because mappings were created during preprocessing before inheritance was resolved
func recreateJsonPlaceholderMappingsAfterInheritance(siteTemplates *model.PreprocessedSiteTemplates) {
	for templateKey, template := range siteTemplates.Templates {
		if template.JsonData == nil || len(*template.JsonData) == 0 {
			continue
		}

		// Remove old JSON placeholder mappings (both simple placeholders AND array blocks use JsonPlaceholderType)
		newMappings := make([]model.ReplacementMapping, 0)
		for _, mapping := range template.ReplacementMappings {
			if mapping.Type != model.JsonPlaceholderType {
				newMappings = append(newMappings, mapping)
			}
		}

		template.ReplacementMappings = newMappings

		// Recreate JSON array block mappings FIRST (they may contain simple placeholders)
		createJsonArrayReplacementMappings(&template, template.OriginalContent)

		// Then recreate simple JSON placeholder mappings from the resolved JsonData
		createJsonPlaceholderReplacementMappings(&template, template.OriginalContent)

		siteTemplates.Templates[templateKey] = template
		Logger.Debug(fmt.Sprintf("Recreated JSON placeholder and array mappings for template %s after inheritance resolution", templateKey), "LoaderPreProcess")
	}
}

// searchParentTreeForKeyPreProcess searches up the parent tree to find a JSON key value
func searchParentTreeForKeyPreProcess(key string, currentTemplateKey string, allTemplates map[string]model.PreprocessedTemplate, parentMap map[string]string) string {
	// Get parent template key
	parentKey, exists := parentMap[currentTemplateKey]
	if !exists {
		Logger.Debug(fmt.Sprintf("No parent found for %s", currentTemplateKey), "LoaderPreProcess")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Checking parent %s for key %s", parentKey, key), "LoaderPreProcess")

	// Get parent's template
	parentTemplate, exists := allTemplates[parentKey]
	if !exists {
		Logger.Debug(fmt.Sprintf("Parent template %s not found in templates", parentKey), "LoaderPreProcess")
		return ""
	}

	if parentTemplate.JsonData == nil || len(*parentTemplate.JsonData) == 0 {
		Logger.Debug(fmt.Sprintf("Parent template %s has no JSON data, searching further up", parentKey), "LoaderPreProcess")
		// Parent has no JSON, search further up the tree
		return searchParentTreeForKeyPreProcess(key, parentKey, allTemplates, parentMap)
	}

	// Look for the key (case-insensitive)
	for k, v := range *parentTemplate.JsonData {
		if strings.EqualFold(k, key) {
			if strValue, ok := v.(string); ok {
				Logger.Debug(fmt.Sprintf("Found key %s in parent %s: %s", key, parentKey, strValue), "LoaderPreProcess")
				return strValue
			}
		}
	}

	Logger.Debug(fmt.Sprintf("Key %s not found in parent %s, searching further up", key, parentKey), "LoaderPreProcess")
	// Not found in this parent, search further up the tree
	return searchParentTreeForKeyPreProcess(key, parentKey, allTemplates, parentMap)
}

// #endregion

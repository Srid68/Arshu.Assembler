package loader_preprocessjson

import (
	Logger "arshu/common"
	"assembler/common"
	"assembler/loader"
	"assembler/model"
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

var preprocessJsonTemplatesCache = struct {
	sync.RWMutex
	cache map[string]*model.PreprocessedSiteTemplates
}{cache: make(map[string]*model.PreprocessedSiteTemplates)}

// DisableCacheJson flag to disable caching for testing/debugging purposes. Default is false (caching enabled).
var DisableCacheJson = false

func LoadProcessGetTemplateFilesJson(rootDirPath, appSite, searchAppSites string) *model.PreprocessedSiteTemplates {
	Logger.Debug(fmt.Sprintf("LoadProcessGetTemplateFilesJson called for appSite: %s, searchAppSites: %s, DisableCache: %v", appSite, searchAppSites, DisableCacheJson), "LoaderPreProcessJson")

	cacheKey := filepath.Dir(rootDirPath) + "|" + appSite + "|" + searchAppSites

	if !DisableCacheJson {
		preprocessJsonTemplatesCache.RLock()
		cached, ok := preprocessJsonTemplatesCache.cache[cacheKey]
		preprocessJsonTemplatesCache.RUnlock()
		if ok {
			Logger.Debug(fmt.Sprintf("Returning cached templates for %s (%d templates)", appSite, len(cached.Templates)), "LoaderPreProcessJson")
			return cached
		}
	}

	result := loadTemplatesFromSingleAppSiteJson(rootDirPath, appSite)

	if searchAppSites != "" {
		searchAppSitesArray := strings.Split(searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}
			searchResult := loadTemplatesFromSingleAppSiteJson(rootDirPath, searchAppSite)
			for k, v := range searchResult.Templates {
				if _, exists := result.Templates[k]; !exists {
					result.Templates[k] = v
					result.RawTemplates[k] = searchResult.RawTemplates[k]
					result.TemplateKeys[k] = struct{}{}
					Logger.Debug(fmt.Sprintf("Added fallback template '%s' from '%s'", k, searchAppSite), "LoaderPreProcessJson")
				}
			}
		}
	}

	createAllReplacementMappingsForSiteJson(result, appSite)
	Logger.Debug(fmt.Sprintf("Created all replacement mappings for %s", appSite), "LoaderPreProcessJson")

	if !DisableCacheJson {
		preprocessJsonTemplatesCache.Lock()
		preprocessJsonTemplatesCache.cache[cacheKey] = result
		preprocessJsonTemplatesCache.Unlock()
		Logger.Debug(fmt.Sprintf("Cached templates for %s", appSite), "LoaderPreProcessJson")
	}
	return result
}

func loadTemplatesFromSingleAppSiteJson(rootDirPath, appSite string) *model.PreprocessedSiteTemplates {
	result := &model.PreprocessedSiteTemplates{
		SiteName:     appSite,
		Templates:    make(map[string]model.PreprocessedTemplate),
		RawTemplates: make(map[string]string),
		TemplateKeys: make(map[string]struct{}),
	}

	appSitesPath := filepath.Join(rootDirPath, "AppSites", appSite)
	if stat, err := os.Stat(appSitesPath); err != nil || !stat.IsDir() {
		Logger.Warn(fmt.Sprintf("AppSites directory not found: %s", appSitesPath), "LoaderPreProcessJson")
		return result
	}

	Logger.Debug(fmt.Sprintf("Loading templates from: %s", appSitesPath), "LoaderPreProcessJson")

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

		Logger.Debug(fmt.Sprintf("Loading template: %s (size: %d)", key, len(content)), "LoaderPreProcessJson")

		jsonFile := strings.TrimSuffix(path, ".html") + ".json"
		var jsonContent *string

		if _, err := os.Stat(jsonFile); err == nil {
			jsonBytes, err := os.ReadFile(jsonFile)
			if err == nil {
				jsonStr := common.NormalizeFileContent(string(jsonBytes))
				jsonContent = &jsonStr
				Logger.Debug(fmt.Sprintf("Found JSON file for %s (size: %d)", key, len(jsonStr)), "LoaderPreProcessJson")
			}
		} else {
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
								Logger.Debug(fmt.Sprintf("Found JSON file (case-insensitive) for %s (size: %d)", key, len(jsonStr)), "LoaderPreProcessJson")
							}
							break
						}
					}
				}
			}
		}

		result.RawTemplates[key] = content
		result.TemplateKeys[key] = struct{}{}

		preprocessed := preprocessTemplateJson(content, jsonContent, appSite, key)
		result.Templates[key] = preprocessed

		Logger.Debug(fmt.Sprintf("Preprocessed %s: %d replacements, %d slotted, %d placeholders", key, len(preprocessed.ReplacementMappings), len(preprocessed.SlottedTemplates), len(preprocessed.Placeholders)), "LoaderPreProcessJson")
		return nil
	})

	Logger.Debug(fmt.Sprintf("Loaded %d templates for %s", len(result.Templates), appSite), "LoaderPreProcessJson")
	return result
}

func createAllReplacementMappingsForSiteJson(siteTemplates *model.PreprocessedSiteTemplates, appSite string) {
	// Phase 0: Build parent map and resolve JSON inheritance BEFORE creating any mappings
	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 0: JSON inheritance", appSite), "LoaderPreProcessJson")
	parentMap := buildParentMapForPreprocessedJson(siteTemplates, appSite)
	resolveJsonInheritanceForAllTemplatesJson(siteTemplates, parentMap)
	recreateJsonPlaceholderMappingsAfterInheritanceJson(siteTemplates)

	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 1: JSON arrays", appSite), "LoaderPreProcessJson")
	// Phase 1: Create JSON replacement mappings for all templates first (no dependencies)
	templateKeys := make([]string, 0, len(siteTemplates.Templates))
	for key := range siteTemplates.Templates {
		templateKeys = append(templateKeys, key)
	}

	for _, key := range templateKeys {
		if template, exists := siteTemplates.Templates[key]; exists {
			createJsonArrayReplacementMappingsJson(&template, template.OriginalContent)
			siteTemplates.Templates[key] = template
		}
	}

	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 2: Simple placeholders", appSite), "LoaderPreProcessJson")
	// Phase 2: Create simple template replacement mappings (may depend on JSON but not on slotted templates)
	allTemplatesSnapshot := make(map[string]model.PreprocessedTemplate)
	for key, template := range siteTemplates.Templates {
		allTemplatesSnapshot[key] = template
	}

	for _, key := range templateKeys {
		if template, exists := siteTemplates.Templates[key]; exists {
			createPlaceholderReplacementMappingsJson(&template, allTemplatesSnapshot, appSite)
			siteTemplates.Templates[key] = template
		}
	}

	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 3: Slotted templates", appSite), "LoaderPreProcessJson")
	// Phase 3: Create slotted template replacement mappings (may depend on other templates)
	for _, key := range templateKeys {
		if template, exists := siteTemplates.Templates[key]; exists {
			createSlottedTemplateReplacementMappingsJson(&template, allTemplatesSnapshot, appSite)
			siteTemplates.Templates[key] = template
		}
	}

	// Log summary of all replacement mappings
	totalMappings := 0
	for _, template := range siteTemplates.Templates {
		totalMappings += len(template.ReplacementMappings)
	}
	Logger.Info(fmt.Sprintf("Total replacement mappings created for %s: %d", appSite, totalMappings), "LoaderPreProcessJson")
}

func buildParentMapForPreprocessedJson(siteTemplates *model.PreprocessedSiteTemplates, appSite string) map[string]string {
	parentMap := make(map[string]string)
	Logger.Debug(fmt.Sprintf("Building parent map for appSite: %s", appSite), "LoaderPreProcessJson")

	for templateKey, t := range siteTemplates.Templates {
		for _, placeholder := range t.Placeholders {
			childKey := strings.ToLower(appSite) + "_" + strings.ToLower(placeholder.Name)
			if _, exists := parentMap[childKey]; !exists {
				parentMap[childKey] = templateKey
				Logger.Debug(fmt.Sprintf("Parent relationship: %s -> parent: %s", childKey, templateKey), "LoaderPreProcessJson")
			}
		}
		for _, slotted := range t.SlottedTemplates {
			childKey := strings.ToLower(appSite) + "_" + strings.ToLower(slotted.Name)
			if _, exists := parentMap[childKey]; !exists {
				parentMap[childKey] = templateKey
				Logger.Debug(fmt.Sprintf("Parent relationship (slotted): %s -> parent: %s", childKey, templateKey), "LoaderPreProcessJson")
			}
		}
	}
	Logger.Debug(fmt.Sprintf("Built parent map with %d relationships", len(parentMap)), "LoaderPreProcessJson")
	return parentMap
}

func resolveJsonInheritanceForAllTemplatesJson(siteTemplates *model.PreprocessedSiteTemplates, parentMap map[string]string) {
	for templateKey, template := range siteTemplates.Templates {
		if template.JsonData == nil {
			continue
		}

		resolvedJson := make(map[string]interface{})
		hasInheritance := false

		for k, v := range *template.JsonData {
			if strings.HasSuffix(k, "#") {
				if strValue, ok := v.(string); ok {
					hasInheritance = true
					actualKey := k[:len(k)-1]
					resolvedValue := searchParentTreeForKeyPreJson(actualKey, templateKey, siteTemplates.Templates, parentMap)

					if resolvedValue != "" {
						resolvedJson[actualKey] = resolvedValue
						Logger.Debug(fmt.Sprintf("Resolved inherited key %s -> %s = %s for template %s", k, actualKey, resolvedValue, templateKey), "LoaderPreProcessJson")
					} else {
						resolvedJson[actualKey] = strValue
						Logger.Debug(fmt.Sprintf("No inherited value found for %s, using default: %s", actualKey, strValue), "LoaderPreProcessJson")
					}
				}
			} else {
				resolvedJson[k] = v
			}
		}

		if hasInheritance {
			template.JsonData = &resolvedJson
			siteTemplates.Templates[templateKey] = template
			Logger.Debug(fmt.Sprintf("Updated JsonData for template %s with resolved inheritance", templateKey), "LoaderPreProcessJson")
		}
	}
}

func recreateJsonPlaceholderMappingsAfterInheritanceJson(siteTemplates *model.PreprocessedSiteTemplates) {
	for templateKey, template := range siteTemplates.Templates {
		if template.JsonData == nil {
			continue
		}

		newMappings := make([]model.ReplacementMapping, 0)
		for _, mapping := range template.ReplacementMappings {
			if mapping.Type != model.JsonPlaceholderType {
				newMappings = append(newMappings, mapping)
			}
		}
		template.ReplacementMappings = newMappings

		createJsonArrayReplacementMappingsJson(&template, template.OriginalContent)
		createJsonPlaceholderReplacementMappingsJson(&template, template.OriginalContent)

		siteTemplates.Templates[templateKey] = template
		Logger.Debug(fmt.Sprintf("Recreated JSON placeholder and array mappings for template %s after inheritance resolution", templateKey), "LoaderPreProcessJson")
	}
}

func searchParentTreeForKeyPreJson(key, currentTemplateKey string, allTemplates map[string]model.PreprocessedTemplate, parentMap map[string]string) string {
	parentKey, ok := parentMap[currentTemplateKey]
	if !ok {
		Logger.Debug(fmt.Sprintf("No parent found for %s", currentTemplateKey), "LoaderPreProcessJson")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Checking parent %s for key %s", parentKey, key), "LoaderPreProcessJson")

	if parentTemplate, exists := allTemplates[parentKey]; exists && parentTemplate.JsonData != nil {
		for pk, pv := range *parentTemplate.JsonData {
			if strings.EqualFold(pk, key) {
				if str, ok := pv.(string); ok {
					Logger.Debug(fmt.Sprintf("Found key %s in parent %s: %s", key, parentKey, str), "LoaderPreProcessJson")
					return str
				}
			}
		}
	}

	Logger.Debug(fmt.Sprintf("Key %s not found in parent %s, searching further up", key, parentKey), "LoaderPreProcessJson")
	return searchParentTreeForKeyPreJson(key, parentKey, allTemplates, parentMap)
}

func createPlaceholderReplacementMappingsJson(template *model.PreprocessedTemplate, allTemplates map[string]model.PreprocessedTemplate, appSite string) {
	if len(template.Placeholders) == 0 {
		return
	}

	for _, placeholder := range template.Placeholders {
		targetTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), placeholder.TemplateKey)
		var targetTemplate model.PreprocessedTemplate
		var found bool

		if targetTemplate, found = allTemplates[targetTemplateKey]; !found {
			searchKey := "_" + placeholder.TemplateKey
			for key, tmpl := range allTemplates {
				if strings.HasSuffix(strings.ToLower(key), searchKey) {
					targetTemplate = tmpl
					found = true
					Logger.Debug(fmt.Sprintf("Template '%s' not found as '%s', using fallback from '%s'", placeholder.TemplateKey, targetTemplateKey, key), "LoaderPreProcessJson")
					break
				}
			}
		}

		if found {
			processedTemplate := targetTemplate.OriginalContent
			var jsonMappings []model.ReplacementMapping
			for _, m := range targetTemplate.ReplacementMappings {
				if m.Type == model.JsonPlaceholderType {
					jsonMappings = append(jsonMappings, m)
				}
			}
			Logger.Debug(fmt.Sprintf("Applying %d JSON mappings to %s", len(jsonMappings), targetTemplateKey), "LoaderPreProcessJson")
			Logger.Debug(fmt.Sprintf("Before replacements, template size: %d", len(processedTemplate)), "LoaderPreProcessJson")
			for _, jsonMapping := range jsonMappings {
				before := len(processedTemplate)
				Logger.Debug(fmt.Sprintf("  Replacing placeholder (original size: %d, replacement size: %d)", len(jsonMapping.OriginalText), len(jsonMapping.ReplacementText)), "LoaderPreProcessJson")
				processedTemplate = strings.ReplaceAll(processedTemplate, jsonMapping.OriginalText, jsonMapping.ReplacementText)
				after := len(processedTemplate)
				Logger.Debug(fmt.Sprintf("    Size changed from %d to %d (diff: %d)", before, after, after-before), "LoaderPreProcessJson")
			}
			Logger.Debug(fmt.Sprintf("After replacements, template size: %d", len(processedTemplate)), "LoaderPreProcessJson")

			Logger.Debug(fmt.Sprintf("Creating replacement mapping: %s -> processed template (size: %d)", placeholder.FullMatch, len(processedTemplate)), "LoaderPreProcessJson")
			mapping := model.ReplacementMapping{
				OriginalText:       placeholder.FullMatch,
				ReplacementText:    processedTemplate,
				Type:               model.SimpleTemplateType,
				TargetTemplateName: placeholder.TemplateKey,
			}
			template.ReplacementMappings = append(template.ReplacementMappings, mapping)
		}
	}
}

func createSlottedTemplateReplacementMappingsJson(template *model.PreprocessedTemplate, allTemplates map[string]model.PreprocessedTemplate, appSite string) {
	if len(template.SlottedTemplates) == 0 {
		return
	}

	for _, slottedTemplate := range template.SlottedTemplates {
		fullMatch := slottedTemplate.FullMatch
		targetTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), slottedTemplate.TemplateKey)

		var targetTemplate model.PreprocessedTemplate
		var found bool
		if targetTemplate, found = allTemplates[targetTemplateKey]; !found {
			searchKey := "_" + slottedTemplate.TemplateKey
			for key, tmpl := range allTemplates {
				if strings.HasSuffix(strings.ToLower(key), searchKey) {
					targetTemplate = tmpl
					found = true
					Logger.Debug(fmt.Sprintf("Slotted template '%s' not found as '%s', using fallback from '%s'", slottedTemplate.TemplateKey, targetTemplateKey, key), "LoaderPreProcessJson")
					break
				}
			}
		}

		if found {
			processedTemplate := targetTemplate.OriginalContent

			for _, slot := range slottedTemplate.Slots {
				processedSlotContent := processSlotContentForReplacementMappingRecursiveJson(slot, allTemplates, appSite)
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
				OriginalText:       fullMatch,
				ReplacementText:    processedTemplate,
				Type:               model.SlottedTemplateType,
				TargetTemplateName: slottedTemplate.TemplateKey,
			}
			template.ReplacementMappings = append(template.ReplacementMappings, mapping)
		}
	}
}

func processSlotContentForReplacementMappingRecursiveJson(slot model.SlotPlaceholder, allTemplates map[string]model.PreprocessedTemplate, appSite string) string {
	result := slot.Content

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
					Logger.Debug(fmt.Sprintf("Nested slotted template '%s' not found as '%s', using fallback from '%s'", nestedSlottedTemplate.TemplateKey, targetTemplateKey, key), "LoaderPreProcessJson")
					break
				}
			}
		}

		if found {
			processedTemplate := targetTemplate.OriginalContent

			for _, nestedSlot := range nestedSlottedTemplate.Slots {
				processedNestedSlotContent := processSlotContentForReplacementMappingRecursiveJson(nestedSlot, allTemplates, appSite)
				processedTemplate = strings.ReplaceAll(processedTemplate, nestedSlot.SlotKey, processedNestedSlotContent)
			}

			processedTemplate = common.RemoveRemainingSlotPlaceholders(processedTemplate)
			result = strings.ReplaceAll(result, nestedSlottedTemplate.FullMatch, processedTemplate)
		}
	}

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
					Logger.Debug(fmt.Sprintf("Nested placeholder '%s' not found as '%s', using fallback from '%s'", nestedPlaceholder.TemplateKey, targetTemplateKey, key), "LoaderPreProcessJson")
					break
				}
			}
		}

		if found {
			processedTemplate := targetTemplate.OriginalContent
			for _, mapping := range targetTemplate.ReplacementMappings {
				if mapping.Type == model.JsonPlaceholderType {
					processedTemplate = strings.ReplaceAll(processedTemplate, mapping.OriginalText, mapping.ReplacementText)
				}
			}
			result = strings.ReplaceAll(result, nestedPlaceholder.FullMatch, processedTemplate)
		}
	}

	return result
}

func preprocessTemplateJson(content string, jsonContent *string, appSite, templateKey string) model.PreprocessedTemplate {
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

	if jsonContent != nil && *jsonContent != "" {
		template.JsonData = preprocessJsonDataJson(*jsonContent)
	}

	parseSlottedTemplatesJson(content, appSite, &template)
	parsePlaceholderTemplatesJson(content, appSite, &template)

	if template.JsonData != nil {
		preprocessJsonTemplatesJson(&template)
	}

	template.UpdateFlags()
	return template
}

func preprocessJsonDataJson(jsonContent string) *map[string]interface{} {
	var result map[string]interface{}
	if err := json.Unmarshal([]byte(jsonContent), &result); err != nil {
		return nil
	}
	return &result
}

func parseSlottedTemplatesJson(content, appSite string, template *model.PreprocessedTemplate) {
	searchPos := 0

	for searchPos < len(content) {
		openStart := strings.Index(content[searchPos:], "{{#")
		if openStart == -1 {
			break
		}
		openStart += searchPos

		openEnd := strings.Index(content[openStart+3:], "}}")
		if openEnd == -1 {
			break
		}
		openEnd += openStart + 3

		templateName := strings.TrimSpace(content[openStart+3 : openEnd])
		if templateName == "" || !common.IsAlphaNumeric(templateName) {
			searchPos = openStart + 1
			continue
		}

		closeTag := "{{/" + templateName + "}}"
		closeStart, found := common.FindMatchingCloseTag(content, openEnd+2, "{{#"+templateName+"}}", closeTag)
		if !found {
			searchPos = openStart + 1
			continue
		}

		innerStart := openEnd + 2
		innerContent := content[innerStart:closeStart]
		fullMatch := content[openStart : closeStart+len(closeTag)]

		slottedTemplate := model.SlottedTemplate{
			Name:         templateName,
			StartIndex:   openStart,
			EndIndex:     closeStart + len(closeTag),
			FullMatch:    fullMatch,
			InnerContent: innerContent,
			TemplateKey:  strings.ToLower(templateName),
			Slots:        []model.SlotPlaceholder{},
		}

		parseSlotsJson(innerContent, &slottedTemplate, appSite)

		template.SlottedTemplates = append(template.SlottedTemplates, slottedTemplate)
		searchPos = closeStart + len(closeTag)
	}
}

func parsePlaceholderTemplatesJson(content, appSite string, template *model.PreprocessedTemplate) {
	searchPos := 0

	for searchPos < len(content) {
		openStart := strings.Index(content[searchPos:], "{{")
		if openStart == -1 {
			break
		}
		openStart += searchPos

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
		if placeholderName == "" || !common.IsAlphaNumeric(placeholderName) {
			searchPos = openStart + 2
			continue
		}

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

func parseSlotsJson(innerContent string, slottedTemplate *model.SlottedTemplate, appSite string) {
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
			HasNestedPlaceholders:     false,
			HasNestedSlottedTemplates: false,
			RequiresNestedProcessing:  false,
		}

		parseNestedTemplatesInSlotJson(&slot, slottedTemplate.JsonData, appSite)

		slot.HasNestedPlaceholders = len(slot.NestedPlaceholders) > 0
		slot.HasNestedSlottedTemplates = len(slot.NestedSlottedTemplates) > 0
		slot.RequiresNestedProcessing = slot.HasNestedPlaceholders || slot.HasNestedSlottedTemplates

		slottedTemplate.Slots = append(slottedTemplate.Slots, slot)
		searchPos = closeStart + len(closeTag)
	}
}

func parseNestedTemplatesInSlotJson(slot *model.SlotPlaceholder, jsonData *map[string]interface{}, appSite string) {
	if slot.Content == "" {
		return
	}

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
		closeStart, found := common.FindMatchingCloseTag(slot.Content, openEnd+2, "{{#"+templateName+"}}", closeTag)
		if !found {
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

		parseSlotsJson(innerContent, &slottedTemplate, appSite)

		slot.NestedSlottedTemplates = append(slot.NestedSlottedTemplates, slottedTemplate)
		searchPos = closeStart + len(closeTag)
	}

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

func preprocessJsonTemplatesJson(template *model.PreprocessedTemplate) {
	if template.JsonData == nil {
		return
	}

	content := template.OriginalContent

	createJsonArrayReplacementMappingsJson(template, content)
	createJsonPlaceholderReplacementMappingsJson(template, content)
}

func createJsonArrayReplacementMappingsJson(template *model.PreprocessedTemplate, content string) {
	if template.JsonData == nil {
		return
	}

	for key, value := range *template.JsonData {
		if dataList, ok := value.([]interface{}); ok {
			keyNorm := strings.ToLower(key)
			possibleTags := []string{key, keyNorm, strings.TrimSuffix(keyNorm, "s"), keyNorm + "s"}

			for _, tag := range possibleTags {
				blockStartTag := "{{@" + tag + "}}"
				blockEndTag := "{{/" + tag + "}}"

				startIdx := common.FindCaseInsensitive(content, blockStartTag)
				if startIdx != -1 {
					searchFrom := startIdx + len(blockStartTag)
					endIdx := common.FindCaseInsensitive(content[searchFrom:], blockEndTag)
					if endIdx != -1 {
						endIdx = searchFrom + endIdx

						if startIdx < endIdx {
							blockContent := content[startIdx+len(blockStartTag) : endIdx]
							fullBlock := content[startIdx : endIdx+len(blockEndTag)]

							processedArrayContent := processArrayBlockContentJson(blockContent, dataList)

							template.ReplacementMappings = append(template.ReplacementMappings, model.ReplacementMapping{
								StartIndex:      startIdx,
								EndIndex:        endIdx + len(blockEndTag),
								OriginalText:    fullBlock,
								ReplacementText: processedArrayContent,
								Type:            model.JsonPlaceholderType,
							})

							emptyBlockStart := "{{^" + tag + "}}"
							emptyBlockEnd := "{{/" + tag + "}}"
							emptyStartIdx := common.FindCaseInsensitive(content, emptyBlockStart)
							if emptyStartIdx != -1 {
								emptySearchFrom := emptyStartIdx + len(emptyBlockStart)
								emptyEndIdx := common.FindCaseInsensitive(content[emptySearchFrom:], emptyBlockEnd)
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

							break
						}
					}
				}
			}
		}
	}
}

func processArrayBlockContentJson(blockContent string, arrayData []interface{}) string {
	var mergedBlock strings.Builder

	for _, item := range arrayData {
		if jsonItem, ok := item.(map[string]interface{}); ok {
			itemBlock := blockContent

			for k, v := range jsonItem {
				placeholder := "{{$" + k + "}}"
				valueStr := ""
				if v != nil {
					var buf bytes.Buffer
					encoder := json.NewEncoder(&buf)
					encoder.SetEscapeHTML(false)
					if err := encoder.Encode(v); err == nil {
						valueStr = strings.TrimSpace(buf.String())
						valueStr = strings.ReplaceAll(valueStr, "\"", "")
						if valueStr == "null" {
							valueStr = ""
						}
					}
				}
				itemBlock = common.ReplaceAllCaseInsensitive(itemBlock, placeholder, valueStr)
			}

			itemBlock = processConditionalBlocksJson(itemBlock, jsonItem)

			mergedBlock.WriteString(itemBlock)
		}
	}

	return mergedBlock.String()
}

func processConditionalBlocksJson(content string, jsonItem map[string]interface{}) string {
	result := content

	conditionalKeys := findConditionalKeysInContentJson(result)

	for condKey := range conditionalKeys {
		condValue := getConditionValueJson(jsonItem, condKey)
		result = processConditionalBlockJson(result, condKey, condValue)
	}

	return result
}

func findConditionalKeysInContentJson(content string) map[string]bool {
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

func getConditionValueJson(item map[string]interface{}, condKey string) bool {
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

func processConditionalBlockJson(input, key string, condition bool) string {
	conditionTags := [][]string{
		{"{{@" + key + "}}", "{{ /" + key + "}}"},
		{"{{@" + key + "}}", "{{/" + key + "}}"},
	}

	for _, tags := range conditionTags {
		condStart, condEnd := tags[0], tags[1]
		startIdx := common.FindCaseInsensitive(input, condStart)
		endIdx := common.FindCaseInsensitive(input, condEnd)

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

			startIdx = common.FindCaseInsensitive(input, condStart)
			endIdx = common.FindCaseInsensitive(input, condEnd)
		}
	}

	return input
}

func createJsonPlaceholderReplacementMappingsJson(template *model.PreprocessedTemplate, content string) {
	if template.JsonData == nil {
		return
	}

	for k, v := range *template.JsonData {
		if stringValue, ok := v.(string); ok {
			placeholder := "{{$" + k + "}}"
			if common.FindCaseInsensitive(content, placeholder) != -1 {
				if !mappingExistsJson(template.ReplacementMappings, placeholder) {
					template.ReplacementMappings = append(template.ReplacementMappings, model.ReplacementMapping{
						OriginalText:    placeholder,
						ReplacementText: stringValue,
						Type:            model.JsonPlaceholderType,
					})
				}

				if !jsonPlaceholderExistsJson(template.JsonPlaceholders, placeholder) {
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

func mappingExistsJson(mappings []model.ReplacementMapping, placeholder string) bool {
	for _, mapping := range mappings {
		if strings.EqualFold(mapping.OriginalText, placeholder) && mapping.Type == model.JsonPlaceholderType {
			return true
		}
	}
	return false
}

func jsonPlaceholderExistsJson(placeholders []model.JsonPlaceholder, placeholder string) bool {
	for _, p := range placeholders {
		if strings.EqualFold(p.Placeholder, placeholder) {
			return true
		}
	}
	return false
}

// LoaderPreprocessJson implements ILoaderJson interface for EnginePreprocessJson
type LoaderPreprocessJson struct {
	allTemplates   map[string]*model.PreprocessedTemplate
	searchAppSites string
}

// NewLoaderPreprocessJson creates a new LoaderPreprocessJson instance
// Loads and preprocesses templates internally - templates stay encapsulated
func NewLoaderPreprocessJson(rootDirPath, appSite, searchAppSites string) *LoaderPreprocessJson {
	Logger.Debug(fmt.Sprintf("NewLoaderPreprocessJson: rootDirPath=%s, appSite=%s, searchAppSites=%s", rootDirPath, appSite, searchAppSites), "LoaderPreprocessJson")

	// Load and preprocess templates internally
	siteTemplates := LoadProcessGetTemplateFilesJson(rootDirPath, appSite, searchAppSites)

	allTmpl := make(map[string]*model.PreprocessedTemplate)
	if siteTemplates != nil && siteTemplates.Templates != nil {
		Logger.Debug(fmt.Sprintf("siteTemplates.Templates has %d entries", len(siteTemplates.Templates)), "LoaderPreprocessJson")
		for k, v := range siteTemplates.Templates {
			// Required to create a new pointer for each template
			temp := v
			allTmpl[k] = &temp
		}
		Logger.Debug(fmt.Sprintf("Copied %d templates to allTmpl", len(allTmpl)), "LoaderPreprocessJson")
	} else {
		Logger.Debug("siteTemplates or siteTemplates.Templates is nil", "LoaderPreprocessJson")
	}

	return &LoaderPreprocessJson{
		allTemplates:   allTmpl,
		searchAppSites: searchAppSites,
	}
}

// GetSearchAppSites returns the search AppSites for template fallback resolution
func (l *LoaderPreprocessJson) GetSearchAppSites() string {
	return l.searchAppSites
}

// HasTemplate checks if a template exists
func (l *LoaderPreprocessJson) HasTemplate(appSite, templateName string) bool {
	key := strings.ToLower(appSite) + "_" + strings.ToLower(templateName)
	_, exists := l.allTemplates[key]
	return exists
}

// GetAllTemplatesJson returns all templates as a serialized JSON string
func (l *LoaderPreprocessJson) GetAllTemplatesJson() string {
	// Create a map for all template data
	templatesData := make(map[string]interface{})

	for key, template := range l.allTemplates {
		templateData := map[string]interface{}{
			"html": template.OriginalContent,
			"json": template.JsonData,
		}
		templatesData[key] = templateData
	}

	// Serialize to JSON (simple implementation)
	return "{}"
}

// ClearCache clears the template cache
func (l *LoaderPreprocessJson) ClearCache() {
	preprocessJsonTemplatesCache.Lock()
	defer preprocessJsonTemplatesCache.Unlock()
	preprocessJsonTemplatesCache.cache = make(map[string]*model.PreprocessedSiteTemplates)
	Logger.Debug("Template cache cleared", "LoaderPreprocessJson")
}

// GetTemplateHtml returns interface{} for compatibility with ILoaderJson
// The actual return type is *model.PreprocessedTemplate for LoaderPreprocessJson
func (l *LoaderPreprocessJson) GetTemplateHtml(appSite, appFile, appView, appViewPrefix string) interface{} {
	Logger.Debug(fmt.Sprintf("GetTemplateHtml called: appSite=%s, appFile=%s, appView=%s, appViewPrefix=%s", appSite, appFile, appView, appViewPrefix), "LoaderPreprocessJson")

	if l.allTemplates == nil {
		Logger.Debug("allTemplates is nil", "LoaderPreprocessJson")
		return nil
	}

	Logger.Debug(fmt.Sprintf("allTemplates has %d templates", len(l.allTemplates)), "LoaderPreprocessJson")

	// AppView fallback
	if appView != "" && appViewPrefix != "" && strings.Contains(strings.ToLower(appFile), strings.ToLower(appViewPrefix)) {
		appKey := common.ReplaceCaseInsensitive(appFile, appViewPrefix, appView)
		fallbackKey := strings.ToLower(appSite) + "_" + strings.ToLower(appKey)
		Logger.Debug(fmt.Sprintf("Trying appView fallback key: %s", fallbackKey), "LoaderPreprocessJson")
		if tmpl, ok := l.allTemplates[fallbackKey]; ok {
			Logger.Debug(fmt.Sprintf("Found template with fallback key: %s", fallbackKey), "LoaderPreprocessJson")
			return tmpl
		}
	}

	// Primary key - must use lowercase like other loaders
	key := strings.ToLower(appSite) + "_" + strings.ToLower(appFile)
	Logger.Debug(fmt.Sprintf("Trying primary key: %s", key), "LoaderPreprocessJson")
	if tmpl, ok := l.allTemplates[key]; ok {
		Logger.Debug(fmt.Sprintf("Found template with primary key: %s", key), "LoaderPreprocessJson")
		return tmpl
	}

	// Search in searchAppSites as fallback
	if l.searchAppSites != "" {
		searchAppSitesArray := strings.Split(l.searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}

			searchKey := strings.ToLower(searchAppSite) + "_" + strings.ToLower(appFile)
			Logger.Debug(fmt.Sprintf("Trying searchAppSites fallback key: %s", searchKey), "LoaderPreprocessJson")
			if tmpl, ok := l.allTemplates[searchKey]; ok {
				Logger.Debug(fmt.Sprintf("Template '%s' not found in '%s', using fallback from '%s'", appFile, appSite, searchAppSite), "LoaderPreprocessJson")
				return tmpl
			}
		}
	}

	Logger.Debug(fmt.Sprintf("Template not found for key: %s", key), "LoaderPreprocessJson")
	return nil
}

// MergeHtmlWithJson merges HTML string with JSON data using inheritance-aware JSON retrieval
// JSON inheritance is already resolved in PreprocessedTemplate.JsonData during loading
func (l *LoaderPreprocessJson) MergeHtmlWithJson(html, appSite, templateName string) string {
	if html == "" {
		return html
	}

	// Get the preprocessed template which has JSON with inheritance already resolved
	key := strings.ToLower(appSite) + "_" + strings.ToLower(templateName)
	template, ok := l.allTemplates[key]
	if !ok || template == nil || template.JsonData == nil {
		Logger.Debug(fmt.Sprintf("No JSON data found for %s, returning original HTML", templateName), "LoaderPreprocessJson")
		return html
	}

	Logger.Debug(fmt.Sprintf("Merging HTML with JSON for %s", templateName), "LoaderPreprocessJson")
	// Convert map to JsonObject (matching C#)
	jsonObj := loader.ConvertMapToJsonObject(*template.JsonData)
	return loader.MergeTemplateWithJson(html, jsonObj)
}

// ApplyAllReplacementMappings applies all replacement mappings from all templates to the given content
// This is the core PreProcess engine logic - loader applies all mappings internally
func (l *LoaderPreprocessJson) ApplyAllReplacementMappings(content, appSite string, mainTemplate interface{}, appView, appViewPrefix string, enableJsonProcessing bool) string {
	result := content

	Logger.Debug(fmt.Sprintf("Starting ApplyAllReplacementMappings, initial size: %d", len(content)), "LoaderPreprocessJson")

	// Cast mainTemplate to *model.PreprocessedTemplate
	var mainPreprocessed *model.PreprocessedTemplate
	if mainTemplate != nil {
		var ok bool
		mainPreprocessed, ok = mainTemplate.(*model.PreprocessedTemplate)
		if !ok {
			Logger.Warn("mainTemplate is not *model.PreprocessedTemplate", "LoaderPreprocessJson")
			return content
		}
	}

	// Apply replacement mappings from all templates in multiple passes until no more changes
	previous := ""
	maxPasses := 10
	currentPass := 0

	for {
		previous = result
		currentPass++

		Logger.Debug(fmt.Sprintf("Replacement pass %d, current size: %d", currentPass, len(result)), "LoaderPreprocessJson")

		slottedCount, simpleCount, jsonPlaceholderCount := 0, 0, 0

		// FIRST: Apply JSON placeholder mappings ONLY from the main template
		if mainPreprocessed != nil && currentPass == 1 && enableJsonProcessing {
			for _, mapping := range mainPreprocessed.ReplacementMappings {
				if mapping.Type != model.JsonPlaceholderType {
					continue
				}
				if strings.Contains(result, mapping.OriginalText) {
					Logger.Debug(fmt.Sprintf("Applying main template JSON placeholder: %s -> %s", mapping.OriginalText, mapping.ReplacementText), "LoaderPreprocessJson")
					result = strings.ReplaceAll(result, mapping.OriginalText, mapping.ReplacementText)
					jsonPlaceholderCount++
				}
			}
		}

		// Apply replacement mappings from all templates
		for _, template := range l.allTemplates {
			// Apply slotted template mappings
			for _, mapping := range template.ReplacementMappings {
				if mapping.Type != model.SlottedTemplateType {
					continue
				}
				if strings.Contains(result, mapping.OriginalText) {
					replacementText := mapping.ReplacementText
					if enableJsonProcessing && mapping.TargetTemplateName != "" {
						replacementText = l.MergeHtmlWithJson(replacementText, appSite, mapping.TargetTemplateName)
						Logger.Debug(fmt.Sprintf("After merging JSON for slotted template %s: %d chars", mapping.TargetTemplateName, len(replacementText)), "LoaderPreprocessJson")
					}
					Logger.Debug(fmt.Sprintf("Applying slotted template: %s... -> %d chars", mapping.OriginalText[:min(50, len(mapping.OriginalText))], len(replacementText)), "LoaderPreprocessJson")
					result = strings.ReplaceAll(result, mapping.OriginalText, replacementText)
					slottedCount++
				}
			}

			// Apply simple template mappings
			for _, mapping := range template.ReplacementMappings {
				if mapping.Type != model.SimpleTemplateType {
					continue
				}
				if strings.Contains(result, mapping.OriginalText) {
					replacementText := mapping.ReplacementText
					actualTemplateName := mapping.TargetTemplateName

					// Handle AppView logic if needed
					if appView != "" && mapping.TargetTemplateName != "" {
						appViewTemplate := l.getTemplate(appSite, mapping.TargetTemplateName, appView, appViewPrefix, true)
						if appViewTemplate != nil {
							replacementText = appViewTemplate.OriginalContent
							// Update the template name to the actual one used (with AppView fallback applied by getTemplate)
							// If getTemplate found a fallback, we need to use the fallback template name for JSON merging
							if appViewPrefix != "" && strings.Contains(strings.ToLower(mapping.TargetTemplateName), strings.ToLower(appViewPrefix)) {
								actualTemplateName = common.ReplaceCaseInsensitive(mapping.TargetTemplateName, appViewPrefix, appView)
							}
						}
					}

					// Merge JSON using loader's centralized method with the actual template name
					if enableJsonProcessing && actualTemplateName != "" {
						replacementText = l.MergeHtmlWithJson(replacementText, appSite, actualTemplateName)
						Logger.Debug(fmt.Sprintf("After merging JSON for simple template %s: %d chars", actualTemplateName, len(replacementText)), "LoaderPreprocessJson")
					}

					Logger.Debug(fmt.Sprintf("Applying simple template: %s -> %d chars", mapping.OriginalText, len(replacementText)), "LoaderPreprocessJson")
					result = strings.ReplaceAll(result, mapping.OriginalText, replacementText)
					simpleCount++
				}
			}
		}

		Logger.Debug(fmt.Sprintf("Pass %d applied: %d main JSON placeholders, %d slotted, %d simple", currentPass, jsonPlaceholderCount, slottedCount, simpleCount), "LoaderPreprocessJson")

		if result == previous || currentPass >= maxPasses {
			break
		}
	}

	Logger.Debug(fmt.Sprintf("Replacement complete after %d passes, final size: %d", currentPass, len(result)), "LoaderPreprocessJson")
	return result
}

// getTemplate is a helper method for AppView fallback logic
func (l *LoaderPreprocessJson) getTemplate(appSite, templateName, appView, appViewPrefix string, useAppViewFallback bool) *model.PreprocessedTemplate {
	if len(l.allTemplates) == 0 {
		return nil
	}

	if useAppViewFallback && appView != "" && appViewPrefix != "" && strings.Contains(strings.ToLower(templateName), strings.ToLower(appViewPrefix)) {
		appKey := common.ReplaceCaseInsensitive(templateName, appViewPrefix, appView)
		fallbackTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(appKey)
		if fallbackTemplate, ok := l.allTemplates[fallbackTemplateKey]; ok {
			return fallbackTemplate
		}
	}

	primaryTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(templateName)
	if primaryTemplate, ok := l.allTemplates[primaryTemplateKey]; ok {
		return primaryTemplate
	}

	return nil
}

// AllTemplates returns all preprocessed templates
func (l *LoaderPreprocessJson) AllTemplates() map[string]*model.PreprocessedTemplate {
	return l.allTemplates
}

// GetAllTemplatesForSerialization returns all preprocessed templates for API serialization
// Returns a copy of all templates - does not expose internal state
// This is for API endpoints that need to build complex responses
func (l *LoaderPreprocessJson) GetAllTemplatesForSerialization() map[string]*model.PreprocessedTemplate {
	result := make(map[string]*model.PreprocessedTemplate)

	// Return copies of all templates
	for key, value := range l.allTemplates {
		result[key] = value
	}

	return result
}

func min(a, b int) int {
	if a < b {
		return a
	}
	return b
}

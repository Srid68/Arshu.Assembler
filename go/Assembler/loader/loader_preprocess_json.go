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

var preprocessedTemplatesCacheJson = struct {
	sync.RWMutex
	cache map[string]*model.PreprocessedSiteTemplates
}{cache: make(map[string]*model.PreprocessedSiteTemplates)}

func LoadProcessGetTemplateFilesJson(rootDirPath, appSite, searchAppSites string) *model.PreprocessedSiteTemplates {
	Logger.Debug(fmt.Sprintf("LoadProcessGetTemplateFilesJson called for appSite: %s, searchAppSites: %s", appSite, searchAppSites), "LoaderPreProcessJson")

	cacheKey := filepath.Dir(rootDirPath) + "|" + appSite + "|" + searchAppSites
	preprocessedTemplatesCacheJson.RLock()
	cached, ok := preprocessedTemplatesCacheJson.cache[cacheKey]
	preprocessedTemplatesCacheJson.RUnlock()
	if ok {
		Logger.Debug(fmt.Sprintf("Returning cached templates for %s (%d templates)", appSite, len(cached.Templates)), "LoaderPreProcessJson")
		return cached
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

	preprocessedTemplatesCacheJson.Lock()
	preprocessedTemplatesCacheJson.cache[cacheKey] = result
	preprocessedTemplatesCacheJson.Unlock()
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
    resolveJsonInheritanceForAllTemplatesJson(siteTemplates, parentMap, appSite)

    Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 1: JSON arrays", appSite), "LoaderPreProcessJson")

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

	for _, key := range templateKeys {
		if template, exists := siteTemplates.Templates[key]; exists {
			createSlottedTemplateReplacementMappingsJson(&template, allTemplatesSnapshot, appSite)
			siteTemplates.Templates[key] = template
		}
	}

	totalMappings := 0
	for _, template := range siteTemplates.Templates {
		totalMappings += len(template.ReplacementMappings)
	}
	Logger.Info(fmt.Sprintf("Total replacement mappings created for %s: %d", appSite, totalMappings), "LoaderPreProcessJson")
}

// buildParentMapForPreprocessedJson builds a parent-child relationship map by analyzing template placeholders
func buildParentMapForPreprocessedJson(siteTemplates *model.PreprocessedSiteTemplates, appSite string) map[string]string {
    parentMap := make(map[string]string)
    for templateKey, t := range siteTemplates.Templates {
        html := t.OriginalContent
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
            if placeholderName == "" || !common.IsAlphaNumeric(placeholderName) {
                searchPos = openStart + 1
                continue
            }

            childKey := strings.ToLower(appSite) + "_" + strings.ToLower(placeholderName)
            if _, exists := parentMap[childKey]; !exists {
                parentMap[childKey] = templateKey
                Logger.Debug(fmt.Sprintf("Parent relationship: %s -> parent: %s", childKey, templateKey), "LoaderPreProcessJson")
            }

            searchPos = closeStart + 2
        }
    }
    Logger.Debug(fmt.Sprintf("Built parent map with %d relationships", len(parentMap)), "LoaderPreProcessJson")
    return parentMap
}

// resolveJsonInheritanceForAllTemplatesJson updates JsonData for all templates by resolving keys ending with '#'
func resolveJsonInheritanceForAllTemplatesJson(siteTemplates *model.PreprocessedSiteTemplates, parentMap map[string]string, appSite string) {
    for templateKey, template := range siteTemplates.Templates {
        if template.JsonData == nil {
            continue
        }

        resolved := make(map[string]interface{})
        for k, v := range *template.JsonData {
            if strings.HasSuffix(k, "#") {
                actualKey := k[:len(k)-1]
                if defStr, ok := v.(string); ok {
                    inherited := searchParentTreeForKeyPreJson(actualKey, templateKey, siteTemplates, parentMap)
                    if inherited != "" {
                        resolved[actualKey] = inherited
                        Logger.Debug(fmt.Sprintf("Resolved inherited key %s -> %s = %s for template %s", k, actualKey, inherited, templateKey), "LoaderPreProcessJson")
                        continue
                    }
                    Logger.Debug(fmt.Sprintf("No inherited value found for %s, using default: %s", actualKey, defStr), "LoaderPreProcessJson")
                    resolved[actualKey] = defStr
                    continue
                }
            }
            resolved[k] = v
        }

        // Replace JsonData with resolved map
        template.JsonData = &resolved

        // Remove existing JSON-related mappings and placeholders, then recreate from resolved data
        template.ReplacementMappings = filterNonJsonMappings(template.ReplacementMappings)
        template.JsonPlaceholders = []model.JsonPlaceholder{}
        createJsonArrayReplacementMappingsJson(&template, template.OriginalContent)
        createJsonPlaceholderReplacementMappingsJson(&template, template.OriginalContent)

        // Save back
        siteTemplates.Templates[templateKey] = template
        Logger.Debug(fmt.Sprintf("Updated JsonData and recreated JSON mappings for template %s after inheritance", templateKey), "LoaderPreProcessJson")
    }
}

// searchParentTreeForKeyPreJson searches up the parent tree for a string value for key
func searchParentTreeForKeyPreJson(key, currentTemplateKey string, siteTemplates *model.PreprocessedSiteTemplates, parentMap map[string]string) string {
    parentKey, ok := parentMap[currentTemplateKey]
    if !ok {
        Logger.Debug(fmt.Sprintf("No parent found for %s", currentTemplateKey), "LoaderPreProcessJson")
        return ""
    }
    if parentTemplate, exists := siteTemplates.Templates[parentKey]; exists && parentTemplate.JsonData != nil {
        for pk, pv := range *parentTemplate.JsonData {
            if strings.EqualFold(pk, key) {
                if str, ok := pv.(string); ok {
                    Logger.Debug(fmt.Sprintf("Found key %s in parent %s: %s", key, parentKey, str), "LoaderPreProcessJson")
                    return str
                }
            }
        }
    }
    // Not found at this level; continue up
    return searchParentTreeForKeyPreJson(key, parentKey, siteTemplates, parentMap)
}

// filterNonJsonMappings keeps only mappings that are not of JsonPlaceholderType
func filterNonJsonMappings(mappings []model.ReplacementMapping) []model.ReplacementMapping {
    if len(mappings) == 0 {
        return mappings
    }
    filtered := make([]model.ReplacementMapping, 0, len(mappings))
    for _, m := range mappings {
        if m.Type != model.JsonPlaceholderType {
            filtered = append(filtered, m)
        }
    }
    return filtered
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
				OriginalText:    placeholder.FullMatch,
				ReplacementText: processedTemplate,
				Type:            model.SimpleTemplateType,
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
				OriginalText:    fullMatch,
				ReplacementText: processedTemplate,
				Type:            model.SlottedTemplateType,
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
			placeholders := []string{
				"{{$" + k + "}}",
				"{{" + k + "}}",
			}

			for _, placeholder := range placeholders {
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

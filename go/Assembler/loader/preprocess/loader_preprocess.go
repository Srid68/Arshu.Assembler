package loader_preprocess

import (
	Logger "arshu/common"
	"assembler/common"
	"assembler/loader"
	"assembler/model"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
)

var preprocessTemplatesCache = struct {
	sync.RWMutex
	cache map[string]*model.PreprocessedSiteTemplates
}{cache: make(map[string]*model.PreprocessedSiteTemplates)}

// DisableCache flag to disable caching for testing/debugging purposes. Default is false (caching enabled).
var DisableCache = false

func LoadProcessGetTemplateFiles(rootDirPath, appSite, searchAppSites string) *model.PreprocessedSiteTemplates {
	Logger.Debug(fmt.Sprintf("LoadProcessGetTemplateFiles called for appSite: %s, searchAppSites: %s, DisableCache: %v", appSite, searchAppSites, DisableCache), "LoaderPreProcess")

	cacheKey := filepath.Dir(rootDirPath) + "|" + appSite + "|" + searchAppSites

	if !DisableCache {
		preprocessTemplatesCache.RLock()
		cached, ok := preprocessTemplatesCache.cache[cacheKey]
		preprocessTemplatesCache.RUnlock()
		if ok {
			Logger.Debug(fmt.Sprintf("Returning cached templates for %s (%d templates)", appSite, len(cached.Templates)), "LoaderPreProcess")
			return cached
		}
	}

	result := loadTemplatesFromSingleAppSite(rootDirPath, appSite)

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

	createAllReplacementMappingsForSite(result, appSite)
	Logger.Debug(fmt.Sprintf("Created all replacement mappings for %s", appSite), "LoaderPreProcess")

	if !DisableCache {
		preprocessTemplatesCache.Lock()
		preprocessTemplatesCache.cache[cacheKey] = result
		preprocessTemplatesCache.Unlock()
		Logger.Debug(fmt.Sprintf("Cached templates for %s", appSite), "LoaderPreProcess")
	}
	return result
}

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

	filepath.Walk(appSitesPath, func(path string, info os.FileInfo, err error) error {
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

		jsonFile := strings.TrimSuffix(path, ".html") + ".json"
		var jsonContent *string

		if _, err := os.Stat(jsonFile); err == nil {
			jsonBytes, err := os.ReadFile(jsonFile)
			if err == nil {
				jsonStr := common.NormalizeFileContent(string(jsonBytes))
				jsonContent = &jsonStr
				Logger.Debug(fmt.Sprintf("Found JSON file for %s (size: %d)", key, len(jsonStr)), "LoaderPreProcess")
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
								Logger.Debug(fmt.Sprintf("Found JSON file (case-insensitive) for %s (size: %d)", key, len(jsonStr)), "LoaderPreProcess")
							}
							break
						}
					}
				}
			}
		}

		result.RawTemplates[key] = content
		result.TemplateKeys[key] = struct{}{}

		preprocessed := preprocessTemplate(content, jsonContent, appSite, key)
		result.Templates[key] = preprocessed

		Logger.Debug(fmt.Sprintf("Preprocessed %s: %d replacements, %d slotted, %d placeholders", key, len(preprocessed.ReplacementMappings), len(preprocessed.SlottedTemplates), len(preprocessed.Placeholders)), "LoaderPreProcess")
		return nil
	})

	Logger.Debug(fmt.Sprintf("Loaded %d templates for %s", len(result.Templates), appSite), "LoaderPreProcess")
	return result
}

func ClearPreProcessCache() {
	preprocessTemplatesCache.Lock()
	preprocessTemplatesCache.cache = make(map[string]*model.PreprocessedSiteTemplates)
	preprocessTemplatesCache.Unlock()
}

func createAllReplacementMappingsForSite(siteTemplates *model.PreprocessedSiteTemplates, appSite string) {
	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 0: JSON inheritance", appSite), "LoaderPreProcess")
	parentMap := buildParentMapForPreProcess(siteTemplates, appSite)
	resolveJsonInheritanceForAllTemplates(siteTemplates, parentMap)

	recreateJsonPlaceholderMappingsAfterInheritance(siteTemplates)

	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 1: JSON arrays", appSite), "LoaderPreProcess")
	for key := range siteTemplates.Templates {
		template := siteTemplates.Templates[key]
		createJsonArrayReplacementMappings(&template, template.OriginalContent)
		siteTemplates.Templates[key] = template
	}

	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 2: Simple placeholders", appSite), "LoaderPreProcess")
	for key := range siteTemplates.Templates {
		template := siteTemplates.Templates[key]
		createPlaceholderReplacementMappings(&template, siteTemplates.Templates, appSite)
		siteTemplates.Templates[key] = template
	}

	Logger.Debug(fmt.Sprintf("Creating replacement mappings for %s - Phase 3: Slotted templates", appSite), "LoaderPreProcess")
	for key := range siteTemplates.Templates {
		template := siteTemplates.Templates[key]
		createSlottedTemplateReplacementMappings(&template, siteTemplates.Templates, appSite)
		siteTemplates.Templates[key] = template
	}

	totalMappings := 0
	for _, template := range siteTemplates.Templates {
		totalMappings += len(template.ReplacementMappings)
	}
	Logger.Info(fmt.Sprintf("Total replacement mappings created for %s: %d", appSite, totalMappings), "LoaderPreProcess")
}

func createPlaceholderReplacementMappings(template *model.PreprocessedTemplate, allTemplates map[string]model.PreprocessedTemplate, appSite string) {
	if !template.HasPlaceholders {
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
					Logger.Debug(fmt.Sprintf("Template '%s' not found as '%s', using fallback from '%s'", placeholder.TemplateKey, targetTemplateKey, key), "LoaderPreProcess")
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

			Logger.Debug(fmt.Sprintf("Creating replacement mapping: %s -> processed template (size: %d)", placeholder.FullMatch, len(processedTemplate)), "LoaderPreProcess")
			template.ReplacementMappings = append(template.ReplacementMappings, model.ReplacementMapping{
				OriginalText:    placeholder.FullMatch,
				ReplacementText: processedTemplate,
				Type:            model.SimpleTemplateType,
			})
		}
	}
}

func createSlottedTemplateReplacementMappings(template *model.PreprocessedTemplate, allTemplates map[string]model.PreprocessedTemplate, appSite string) {
	if !template.HasSlottedTemplates {
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
					Logger.Debug(fmt.Sprintf("Slotted template '%s' not found as '%s', using fallback from '%s'", slottedTemplate.TemplateKey, targetTemplateKey, key), "LoaderPreProcess")
					break
				}
			}
		}

		if found {
			processedTemplate := targetTemplate.OriginalContent
			for _, slot := range slottedTemplate.Slots {
				processedSlotContent := processSlotContentForReplacementMapping(slot, allTemplates, appSite)
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

			template.ReplacementMappings = append(template.ReplacementMappings, model.ReplacementMapping{
				OriginalText:    fullMatch,
				ReplacementText: processedTemplate,
				Type:            model.SlottedTemplateType,
			})
		}
	}
}

func processSlotContentForReplacementMapping(slot model.SlotPlaceholder, allTemplates map[string]model.PreprocessedTemplate, appSite string) string {
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
					Logger.Debug(fmt.Sprintf("Nested slotted template '%s' not found as '%s', using fallback from '%s'", nestedSlottedTemplate.TemplateKey, targetTemplateKey, key), "LoaderPreProcess")
					break
				}
			}
		}

		if found {
			processedTemplate := targetTemplate.OriginalContent
			for _, nestedSlot := range nestedSlottedTemplate.Slots {
				processedNestedSlotContent := processSlotContentForReplacementMapping(nestedSlot, allTemplates, appSite)
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
					Logger.Debug(fmt.Sprintf("Nested placeholder '%s' not found as '%s', using fallback from '%s'", nestedPlaceholder.TemplateKey, targetTemplateKey, key), "LoaderPreProcess")
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

func preprocessTemplate(content string, jsonContent *string, appSite, templateKey string) model.PreprocessedTemplate {
	template := model.PreprocessedTemplate{
		OriginalContent:     content,
		ReplacementMappings: []model.ReplacementMapping{},
		JsonPlaceholders:    []model.JsonPlaceholder{},
	}

	if content == "" {
		return template
	}

	if jsonContent != nil && *jsonContent != "" {
		template.JsonData = preprocessJsonData(*jsonContent)
	}

	parseSlottedTemplates(content, appSite, &template)
	parsePlaceholderTemplates(content, appSite, &template)

	if template.JsonData != nil {
		preprocessJsonTemplates(&template)
	}

	template.UpdateFlags()
	return template
}

func preprocessJsonData(jsonContent string) *map[string]interface{} {
	var result map[string]interface{}
	if err := json.Unmarshal([]byte(jsonContent), &result); err != nil {
		return nil
	}
	return &result
}

func parseSlottedTemplates(content, appSite string, template *model.PreprocessedTemplate) {
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
		closeStart, _ := common.FindMatchingCloseTag(content, openEnd+2, "{{#"+templateName+"}}", closeTag)
		if closeStart == -1 {
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
		parseSlots(innerContent, &slottedTemplate, appSite)
		template.SlottedTemplates = append(template.SlottedTemplates, slottedTemplate)
		searchPos = closeStart + len(closeTag)
	}
}

func parsePlaceholderTemplates(content, appSite string, template *model.PreprocessedTemplate) {
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

func parseSlots(innerContent string, slottedTemplate *model.SlottedTemplate, appSite string) {
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
		closeStart, _ := common.FindMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag)
		if closeStart == -1 {
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
			Number:                 slotNum,
			StartIndex:             slotStart,
			EndIndex:               closeStart + len(closeTag),
			Content:                slotContent,
			SlotKey:                slotKey,
			OpenTag:                openTag,
			CloseTag:               closeTag,
			NestedPlaceholders:     []model.TemplatePlaceholder{},
			NestedSlottedTemplates: []model.SlottedTemplate{},
		}
		parseNestedTemplatesInSlot(&slot, slottedTemplate.JsonData, appSite)
		slottedTemplate.Slots = append(slottedTemplate.Slots, slot)
		searchPos = closeStart + len(closeTag)
	}
}

func parseNestedTemplatesInSlot(slot *model.SlotPlaceholder, jsonData *map[string]interface{}, appSite string) {
	if slot.Content == "" {
		return
	}
	content := slot.Content
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
		templateName := strings.TrimSpace(content[openStart+2 : closeStart])
		if templateName != "" && common.IsAlphaNumeric(templateName) {
			templateKey := strings.ToLower(templateName)
			slot.NestedPlaceholders = append(slot.NestedPlaceholders, model.TemplatePlaceholder{
				Name:        templateName,
				StartIndex:  openStart,
				EndIndex:    closeStart + 2,
				FullMatch:   content[openStart : closeStart+2],
				TemplateKey: templateKey,
				JsonData:    jsonData,
			})
		}
		searchPos = closeStart + 2
	}
	searchPos = 0
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
		openTag := "{{#" + templateName + "}}"
		closeStart, _ := common.FindMatchingCloseTag(content, openEnd+2, openTag, closeTag)
		if closeStart == -1 {
			searchPos = openStart + 1
			continue
		}
		innerContent := content[openEnd+2 : closeStart]
		templateKey := strings.ToLower(templateName)
		nestedSlottedTemplate := model.SlottedTemplate{
			Name:         templateName,
			StartIndex:   openStart,
			EndIndex:     closeStart + len(closeTag),
			FullMatch:    content[openStart : closeStart+len(closeTag)],
			InnerContent: innerContent,
			TemplateKey:  templateKey,
			JsonData:     jsonData,
		}
		parseSlots(innerContent, &nestedSlottedTemplate, appSite)
		slot.NestedSlottedTemplates = append(slot.NestedSlottedTemplates, nestedSlottedTemplate)
		searchPos = closeStart + len(closeTag)
	}
}

func preprocessJsonTemplates(template *model.PreprocessedTemplate) {
	if template.JsonData == nil {
		return
	}
	content := template.OriginalContent
	createJsonArrayReplacementMappings(template, content)
	createJsonPlaceholderReplacementMappings(template, content)
}

// indexCaseInsensitive returns the index of the first instance of substr in s, case-insensitively.
// It returns -1 if substr is not present in s.
func indexCaseInsensitive(s, substr string) int {
	sLower := strings.ToLower(s)
	substrLower := strings.ToLower(substr)
	return strings.Index(sLower, substrLower)
}

func createJsonArrayReplacementMappings(template *model.PreprocessedTemplate, content string) {
	if template.JsonData == nil {
		return
	}
	for jsonKey, value := range *template.JsonData {
		if dataList, ok := value.([]interface{}); ok {
			keyNorm := strings.ToLower(jsonKey)
			possibleTags := []string{jsonKey, keyNorm, strings.TrimSuffix(keyNorm, "s"), keyNorm + "s"}
			for _, tag := range possibleTags {
				blockStartTag := "{{@" + tag + "}}"
				blockEndTag := "{{/" + tag + "}}"
				startIdx := indexCaseInsensitive(content, blockStartTag)
				if startIdx != -1 {
					searchFrom := startIdx + len(blockStartTag)
					endIdx := indexCaseInsensitive(content[searchFrom:], blockEndTag)
					if endIdx != -1 {
						endIdx += searchFrom
						blockContent := content[startIdx+len(blockStartTag) : endIdx]
						fullBlock := content[startIdx : endIdx+len(blockEndTag)]
						processedArrayContent := processArrayBlockContentSafely(blockContent, dataList)
						template.ReplacementMappings = append(template.ReplacementMappings, model.ReplacementMapping{
							StartIndex:      startIdx,
							EndIndex:        endIdx + len(blockEndTag),
							OriginalText:    fullBlock,
							ReplacementText: processedArrayContent,
							Type:            model.JsonPlaceholderType,
						})
						emptyBlockStart := "{{^" + tag + "}}"
						emptyBlockEnd := "{{/" + tag + "}}"
						emptyStartIdx := indexCaseInsensitive(content, emptyBlockStart)
						if emptyStartIdx != -1 {
							emptySearchFrom := emptyStartIdx + len(emptyBlockStart)
							emptyEndIdx := indexCaseInsensitive(content[emptySearchFrom:], emptyBlockEnd)
							if emptyEndIdx != -1 {
								emptyEndIdx += emptySearchFrom
								if emptyEndIdx > emptyStartIdx+len(emptyBlockStart) {
									emptyBlockContent := content[emptyStartIdx+len(emptyBlockStart) : emptyEndIdx]
									fullEmptyBlock := content[emptyStartIdx : emptyEndIdx+len(emptyBlockEnd)]
									emptyReplacement := ""
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

func processArrayBlockContentSafely(blockContent string, arrayData []interface{}) string {
	var mergedBlock strings.Builder
	for _, item := range arrayData {
		if jsonItem, ok := item.(map[string]interface{}); ok {
			itemBlock := blockContent
			for k, v := range jsonItem {
				placeholder := "{{$" + k + "}}"
				var valueStr string
				switch val := v.(type) {
				case bool:
					valueStr = fmt.Sprintf("%t", val)
				case nil:
					valueStr = ""
				default:
					valueStr = fmt.Sprintf("%v", val)
				}
				itemBlock = replaceAllCaseInsensitive(itemBlock, placeholder, valueStr)
			}
			itemBlock = processConditionalBlocksSafely(itemBlock, jsonItem)
			mergedBlock.WriteString(itemBlock)
		}
	}
	return mergedBlock.String()
}

func replaceAllCaseInsensitive(input, search, replacement string) string {
	idx := 0
	for {
		found := strings.Index(strings.ToLower(input[idx:]), strings.ToLower(search))
		if found == -1 {
			break
		}
		found += idx
		input = input[:found] + replacement + input[found+len(search):]
		idx = found + len(replacement)
	}
	return input
}

func processConditionalBlocksSafely(content string, jsonItem map[string]interface{}) string {
	result := content
	conditionalKeys := findConditionalKeysInContent(result)
	for condKey := range conditionalKeys {
		condValue := getConditionValue(jsonItem, condKey)
		result = processConditionalBlockSafely(result, condKey, condValue)
	}
	return result
}

func findConditionalKeysInContent(content string) map[string]bool {
	conditionalKeys := make(map[string]bool)
	condIdx := 0
	for {
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

func getConditionValue(item map[string]interface{}, condKey string) bool {
	if condObj, exists := item[condKey]; exists && condObj != nil {
		if boolValue, ok := condObj.(bool); ok {
			return boolValue
		}
		if strValue, ok := condObj.(string); ok {
			if b, err := strconv.ParseBool(strValue); err == nil {
				return b
			}
		}
		if numValue, ok := condObj.(float64); ok {
			return numValue != 0
		}
	}
	for k, v := range item {
		if strings.EqualFold(k, condKey) && v != nil {
			if boolValue, ok := v.(bool); ok {
				return boolValue
			}
			if strValue, ok := v.(string); ok {
				if b, err := strconv.ParseBool(strValue); err == nil {
					return b
				}
			}
			if numValue, ok := v.(float64); ok {
				return numValue != 0
			}
		}
	}
	return false
}

func processConditionalBlockSafely(input, key string, condition bool) string {
	conditionTags := [][]string{
		{"{{@" + key + "}}", "{{ /" + key + "}}"},
		{"{{@" + key + "}}", "{{/" + key + "}}"},
	}
	for _, tags := range conditionTags {
		condStart, condEnd := tags[0], tags[1]
		startIdx := strings.Index(input, condStart)
		endIdx := strings.Index(input, condEnd)
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
			startIdx = strings.Index(input, condStart)
			endIdx = strings.Index(input, condEnd)
		}
	}
	return input
}

func createJsonPlaceholderReplacementMappings(template *model.PreprocessedTemplate, content string) {
	if template.JsonData == nil {
		return
	}
	for k, v := range *template.JsonData {
		if stringValue, ok := v.(string); ok {
			placeholder := "{{$" + k + "}}"
			if strings.Contains(content, placeholder) {
				template.ReplacementMappings = append(template.ReplacementMappings, model.ReplacementMapping{
					OriginalText:    placeholder,
					ReplacementText: stringValue,
					Type:            model.JsonPlaceholderType,
				})
				placeholderExists := false
				for _, p := range template.JsonPlaceholders {
					if p.Placeholder == placeholder {
						placeholderExists = true
						break
					}
				}
				if !placeholderExists {
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

func buildParentMapForPreProcess(siteTemplates *model.PreprocessedSiteTemplates, appSite string) map[string]string {
	parentMap := make(map[string]string)
	Logger.Debug(fmt.Sprintf("Building parent map for appSite: %s", appSite), "LoaderPreProcess")

	// Process templates in deterministic order to ensure consistent parent relationships
	// Sort keys: SearchAppSites first, then main AppSite (so main AppSite wins in case of conflicts)
	mainAppSitePrefix := strings.ToLower(appSite) + "_"
	var searchTemplateKeys []string
	var mainTemplateKeys []string

	for templateKey := range siteTemplates.Templates {
		if strings.HasPrefix(templateKey, mainAppSitePrefix) {
			mainTemplateKeys = append(mainTemplateKeys, templateKey)
		} else {
			searchTemplateKeys = append(searchTemplateKeys, templateKey)
		}
	}

	// Process SearchAppSites templates first, then main AppSite (last wins)
	allKeys := append(searchTemplateKeys, mainTemplateKeys...)

	for _, templateKey := range allKeys {
		template := siteTemplates.Templates[templateKey]
		for _, placeholder := range template.Placeholders {
			placeholderName := placeholder.Name
			childTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(placeholderName))
			// Use "last wins" strategy - later templates (main AppSite) override earlier ones (SearchAppSites)
			if existingParent, exists := parentMap[childTemplateKey]; exists && existingParent != templateKey {
				Logger.Debug(fmt.Sprintf("Overwriting parent relationship: %s -> parent: %s (was: %s)", childTemplateKey, templateKey, existingParent), "LoaderPreProcess")
			} else if !exists {
				Logger.Debug(fmt.Sprintf("Parent relationship: %s -> parent: %s", childTemplateKey, templateKey), "LoaderPreProcess")
			}
			parentMap[childTemplateKey] = templateKey
		}
		for _, slottedTemplate := range template.SlottedTemplates {
			templateName := slottedTemplate.Name
			childTemplateKey := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
			// Use "last wins" strategy - later templates (main AppSite) override earlier ones (SearchAppSites)
			if existingParent, exists := parentMap[childTemplateKey]; exists && existingParent != templateKey {
				Logger.Debug(fmt.Sprintf("Overwriting parent relationship (slotted): %s -> parent: %s (was: %s)", childTemplateKey, templateKey, existingParent), "LoaderPreProcess")
			} else if !exists {
				Logger.Debug(fmt.Sprintf("Parent relationship (slotted): %s -> parent: %s", childTemplateKey, templateKey), "LoaderPreProcess")
			}
			parentMap[childTemplateKey] = templateKey
		}
	}
	Logger.Debug(fmt.Sprintf("Built parent map with %d relationships", len(parentMap)), "LoaderPreProcess")
	return parentMap
}

func resolveJsonInheritanceForAllTemplates(siteTemplates *model.PreprocessedSiteTemplates, parentMap map[string]string) {
	for templateKey, template := range siteTemplates.Templates {
		if template.JsonData == nil {
			continue
		}
		resolvedJson := make(map[string]interface{})
		hasInheritance := false
		for key, value := range *template.JsonData {
			if strings.HasSuffix(key, "#") {
				if strValue, ok := value.(string); ok {
					hasInheritance = true
					actualKey := key[:len(key)-1]
					resolvedValue := searchParentTreeForKeyPreProcess(actualKey, templateKey, siteTemplates.Templates, parentMap)
					if resolvedValue != "" {
						resolvedJson[actualKey] = resolvedValue
						Logger.Debug(fmt.Sprintf("Resolved inherited key %s -> %s = %s for template %s", key, actualKey, resolvedValue, templateKey), "LoaderPreProcess")
					} else {
						resolvedJson[actualKey] = strValue
						Logger.Debug(fmt.Sprintf("No inherited value found for %s, using default: %s", actualKey, strValue), "LoaderPreProcess")
					}
				}
			} else {
				resolvedJson[key] = value
			}
		}
		if hasInheritance {
			template.JsonData = &resolvedJson
			siteTemplates.Templates[templateKey] = template
			Logger.Debug(fmt.Sprintf("Updated JsonData for template %s with resolved inheritance", templateKey), "LoaderPreProcess")
		}
	}
}

func recreateJsonPlaceholderMappingsAfterInheritance(siteTemplates *model.PreprocessedSiteTemplates) {
	for templateKey, template := range siteTemplates.Templates {
		if template.JsonData == nil {
			continue
		}
		newMappings := []model.ReplacementMapping{}
		for _, mapping := range template.ReplacementMappings {
			if mapping.Type != model.JsonPlaceholderType {
				newMappings = append(newMappings, mapping)
			}
		}
		template.ReplacementMappings = newMappings
		createJsonArrayReplacementMappings(&template, template.OriginalContent)
		createJsonPlaceholderReplacementMappings(&template, template.OriginalContent)
		siteTemplates.Templates[templateKey] = template
		Logger.Debug(fmt.Sprintf("Recreated JSON placeholder and array mappings for template %s after inheritance resolution", templateKey), "LoaderPreProcess")
	}
}

func searchParentTreeForKeyPreProcess(key string, currentTemplateKey string, allTemplates map[string]model.PreprocessedTemplate, parentMap map[string]string) string {
	parentKey, exists := parentMap[currentTemplateKey]
	if !exists {
		Logger.Debug(fmt.Sprintf("No parent found for %s", currentTemplateKey), "LoaderPreProcess")
		return ""
	}
	Logger.Debug(fmt.Sprintf("Checking parent %s for key %s", parentKey, key), "LoaderPreProcess")
	parentTemplate, exists := allTemplates[parentKey]
	if !exists {
		Logger.Debug(fmt.Sprintf("Parent template %s not found in templates", parentKey), "LoaderPreProcess")
		return ""
	}
	if parentTemplate.JsonData == nil {
		Logger.Debug(fmt.Sprintf("Parent template %s has no JSON data, searching further up", parentKey), "LoaderPreProcess")
		return searchParentTreeForKeyPreProcess(key, parentKey, allTemplates, parentMap)
	}
	for k, v := range *parentTemplate.JsonData {
		if strings.EqualFold(k, key) {
			if strValue, ok := v.(string); ok {
				Logger.Debug(fmt.Sprintf("Found key %s in parent %s: %s", key, parentKey, strValue), "LoaderPreProcess")
				return strValue
			}
		}
	}
	Logger.Debug(fmt.Sprintf("Key %s not found in parent %s, searching further up", key, parentKey), "LoaderPreProcess")
	return searchParentTreeForKeyPreProcess(key, parentKey, allTemplates, parentMap)
}
// LoaderPreProcess implements ILoaderPreProcess interface for EnginePreProcess
type LoaderPreProcess struct {
	preprocessedTemplates *model.PreprocessedSiteTemplates
	searchAppSites        string
	appSite               string
}

// NewLoaderPreProcess creates a new LoaderPreProcess instance
func NewLoaderPreProcess(rootDirPath, appSite, searchAppSites string) *LoaderPreProcess {
	return &LoaderPreProcess{
		preprocessedTemplates: LoadProcessGetTemplateFiles(rootDirPath, appSite, searchAppSites),
		searchAppSites:        searchAppSites,
		appSite:               appSite,
	}
}

// GetSearchAppSites returns the search AppSites for template fallback resolution
func (l *LoaderPreProcess) GetSearchAppSites() string {
	return l.searchAppSites
}

// HasTemplate checks if a template exists
func (l *LoaderPreProcess) HasTemplate(appSite, templateName string) bool {
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
	_, exists := l.preprocessedTemplates.Templates[key]
	return exists
}

// ClearCache clears the template cache
func (l *LoaderPreProcess) ClearCache() {
	ClearPreProcessCache()
}

// GetTemplateHtml gets a preprocessed template by appSite and name with optional AppView fallback
func (l *LoaderPreProcess) GetTemplateHtml(appSite, templateName, appView, appViewPrefix string) *model.PreprocessedTemplate {
	// Try AppView fallback first if provided
	if appView != "" && appViewPrefix != "" && strings.Contains(strings.ToLower(templateName), strings.ToLower(appViewPrefix)) {
		appKey := common.ReplaceCaseInsensitive(templateName, appViewPrefix, appView)
		key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(appKey))
		if t, ok := l.preprocessedTemplates.Templates[key]; ok {
			return &t
		}
	}

	// Try primary template key
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
	if t, ok := l.preprocessedTemplates.Templates[key]; ok {
		return &t
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
			if t, ok := l.preprocessedTemplates.Templates[searchKey]; ok {
				return &t
			}
		}
	}

	return nil
}

// MergeHtmlWithJson merges HTML string with JSON data for the specified template
func (l *LoaderPreProcess) MergeHtmlWithJson(html, appSite, templateName string) string {
	if html == "" {
		return html
	}

	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(templateName))
	template, ok := l.preprocessedTemplates.Templates[key]
	if !ok || template.JsonData == nil {
		Logger.Debug(fmt.Sprintf("No JSON data found for %s, returning original HTML", templateName), "LoaderPreProcess")
		return html
	}

	// Convert map to JsonObject (matching C#)
	jsonObj := loader.ConvertMapToJsonObject(*template.JsonData)

	Logger.Debug(fmt.Sprintf("Merging HTML with JSON for %s", templateName), "LoaderPreProcess")
	return loader.MergeTemplateWithJson(html, jsonObj)
}

// ApplyAllReplacementMappings applies all replacement mappings from all templates to the given content
func (l *LoaderPreProcess) ApplyAllReplacementMappings(content, appSite string, mainTemplate *model.PreprocessedTemplate, appView, appViewPrefix string, enableJsonProcessing bool) string {
	result := content
	previous := ""
	maxPasses := 10
	currentPass := 0

	for currentPass < maxPasses {
		previous = result
		currentPass++

		jsonPlaceholderCount := 0
		slottedCount := 0
		simpleCount := 0

		// FIRST: Apply JSON placeholder mappings ONLY from the main template (pass 1 only)
		if currentPass == 1 && enableJsonProcessing && mainTemplate != nil {
			for _, mapping := range mainTemplate.ReplacementMappings {
				if mapping.Type == model.JsonPlaceholderType && strings.Contains(result, mapping.OriginalText) {
					result = strings.ReplaceAll(result, mapping.OriginalText, mapping.ReplacementText)
					jsonPlaceholderCount++
				}
			}
		}

		// SECOND: Apply slotted template replacements from all templates
		for _, template := range l.preprocessedTemplates.Templates {
			for _, mapping := range template.ReplacementMappings {
				if mapping.Type == model.SlottedTemplateType && strings.Contains(result, mapping.OriginalText) {
					result = strings.ReplaceAll(result, mapping.OriginalText, mapping.ReplacementText)
					slottedCount++
				}
			}

			// THIRD: Apply simple template replacements with AppView logic
			for _, mapping := range template.ReplacementMappings {
				if mapping.Type == model.SimpleTemplateType && strings.Contains(result, mapping.OriginalText) {
					replacementText := l.applyAppViewLogicToReplacement(mapping.OriginalText, mapping.ReplacementText, appSite, appView, appViewPrefix)
					result = strings.ReplaceAll(result, mapping.OriginalText, replacementText)
					simpleCount++
				}
			}
		}

		Logger.Debug(fmt.Sprintf("Pass %d: JSON placeholders=%d, slotted=%d, simple=%d", currentPass, jsonPlaceholderCount, slottedCount, simpleCount), "LoaderPreProcess")

		if result == previous || currentPass >= maxPasses {
			break
		}
	}

	return result
}

// applyAppViewLogicToReplacement applies AppView fallback logic to template replacement text
func (l *LoaderPreProcess) applyAppViewLogicToReplacement(originalText, replacementText, appSite, appView, appViewPrefix string) string {
	if appView == "" {
		return replacementText
	}

	placeholderName := extractPlaceholderName(originalText)
	if placeholderName == "" {
		return replacementText
	}

	// Use GetTemplateHtml to find the AppView-specific template variant
	appViewTemplate := l.GetTemplateHtml(appSite, placeholderName, appView, appViewPrefix)
	if appViewTemplate == nil {
		return replacementText
	}

	// Apply JSON placeholder replacements to the template
	processedContent := appViewTemplate.OriginalContent
	for _, jsonMapping := range appViewTemplate.ReplacementMappings {
		if jsonMapping.Type != model.JsonPlaceholderType {
			continue
		}
		processedContent = strings.Replace(processedContent, jsonMapping.OriginalText, jsonMapping.ReplacementText, -1)
	}
	return processedContent
}

// extractPlaceholderName extracts placeholder name from {{PlaceholderName}} format
func extractPlaceholderName(originalText string) string {
	if !strings.HasPrefix(originalText, "{{") || !strings.HasSuffix(originalText, "}}") {
		return ""
	}
	return strings.TrimSpace(originalText[2 : len(originalText)-2])
}

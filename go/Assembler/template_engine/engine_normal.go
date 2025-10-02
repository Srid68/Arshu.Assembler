package template_engine

import (
	"assembler/app"
	"assembler/app/json"
	"assembler/template_common"
	"strings"
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

// ReplaceCaseInsensitive replaces the first occurrence of 'from' in 'text' (case-insensitive) with 'to'
func ReplaceCaseInsensitive(text, from, to string) string {
	textLower := strings.ToLower(text)
	fromLower := strings.ToLower(from)
	if idx := strings.Index(textLower, fromLower); idx != -1 {
		end := idx + len(from)
		return text[:idx] + to + text[end:]
	}
	return text
}

// MergeTemplates merges templates by replacing placeholders with corresponding HTML
// This is a hybrid method that processes both slotted templates and simple placeholders
// JSON files with matching names are automatically merged with HTML templates before processing
func (e *EngineNormal) MergeTemplates(appSite, appFile, appView string, templates map[string]struct {
	HTML string
	JSON *string
}, enableJsonProcessing bool) string {
	if len(templates) == 0 {
		return ""
	}
	mainHtml, mainJson := e.GetTemplate(appSite, appFile, templates, appView, e.AppViewPrefix, true)
	if mainHtml == "" {
		return ""
	}
	contentHtml := mainHtml
	if enableJsonProcessing && mainJson != nil {
		contentHtml = MergeTemplateWithJson(contentHtml, *mainJson)
	}
	mergedTemplates := make(map[string]string)
	allJsonValues := make(map[string]string)

	// Add main template JSON values to the global collection if it exists
	if enableJsonProcessing && mainJson != nil {
		jsonObj := app.ParseJsonString(*mainJson)
		for k, v := range jsonObj.Iter() {
			if v.Kind == json.JsonString {
				allJsonValues[k] = v.StrVal
			}
		}
	}

	for k, v := range templates {
		htmlContent := v.HTML
		jsonContent := v.JSON
		if enableJsonProcessing && jsonContent != nil {
			htmlContent = MergeTemplateWithJson(htmlContent, *jsonContent)
			// Parse JSON and collect key-value pairs for allJsonValues
			jsonObj := app.ParseJsonString(*jsonContent)
			for jk, jv := range jsonObj.Iter() {
				if jv.Kind == json.JsonString {
					allJsonValues[jk] = jv.StrVal
				}
			}
		}
		mergedTemplates[k] = htmlContent
	}
	previous := ""
	for {
		previous = contentHtml
		contentHtml = e.MergeTemplateSlots(contentHtml, appSite, appView, mergedTemplates)
		contentHtml = e.ReplaceTemplatePlaceholdersWithJson(contentHtml, appSite, mergedTemplates, allJsonValues, appView)
		if contentHtml == previous {
			break
		}
	}
	return contentHtml
}

// MergeTemplateWithJson merges JSON data into the HTML template
// Advanced merge logic for block and conditional patterns
func MergeTemplateWithJson(template, jsonText string) string {
	// Parse JSON using our JsonConverter
	jsonObj := app.ParseJsonString(jsonText)

	// Advanced merge logic for block and conditional patterns
	result := template

	// Instead of finding all array tags first, directly match JSON array keys to template blocks
	for key, value := range jsonObj.Iter() {
		if value.Kind == json.JsonArrayKind && value.ArrVal != nil {
			dataList := value.ArrVal
			// Try to find a matching template block for this JSON array
			keyNorm := strings.ToLower(key)

			// Look for possible template tags that match this JSON key
			possibleTags := []string{
				key,
				keyNorm,
				strings.TrimSuffix(keyNorm, "s"), // Remove trailing 's'
				keyNorm + "s",                    // Add trailing 's'
			}

			for _, tag := range possibleTags {
				blockStartTag := "{{@" + tag + "}}"
				blockEndTag := "{{/" + tag + "}}"

				if startIdx := findCaseInsensitive(result, blockStartTag); startIdx != -1 {
					searchFrom := startIdx + len(blockStartTag)
					if endIdx := findCaseInsensitive(result[searchFrom:], blockEndTag); endIdx != -1 {
						endIdx = searchFrom + endIdx

						if startIdx < endIdx {
							// Found a valid block - process it
							contentStartIdx := startIdx + len(blockStartTag)
							if contentStartIdx <= endIdx {
								blockContent := result[contentStartIdx:endIdx]
								var mergedBlock strings.Builder

								// Find all conditional blocks in the template block (e.g., {{@Key}}...{{/Key}})
								conditionalKeys := make(map[string]bool)
								condIdx := 0
								for condIdx < len(blockContent) {
									if condStart := findCaseInsensitive(blockContent[condIdx:], "{{@"); condStart != -1 {
										condStart = condIdx + condStart
										if condEnd := strings.Index(blockContent[condStart:], "}}"); condEnd != -1 {
											condEnd = condStart + condEnd
											condKey := strings.TrimSpace(blockContent[condStart+3 : condEnd])
											conditionalKeys[condKey] = true
											condIdx = condEnd + 2
										} else {
											break
										}
									} else {
										break
									}
								}

								for i := 0; i < dataList.Len(); i++ {
									if item, exists := dataList.Get(i); exists && item.Kind == json.JsonObjectKind && item.ObjVal != nil {
										itemObj := item.ObjVal
										itemBlock := blockContent

										// Replace all placeholders dynamically
										for k, v := range itemObj.Iter() {
											placeholder := "{{$" + k + "}}"
											var valueStr string
											switch v.Kind {
											case json.JsonString:
												valueStr = v.StrVal
											case json.JsonNumber:
												valueStr = v.String()
											case json.JsonInteger:
												valueStr = v.String()
											case json.JsonBool:
												valueStr = v.String()
											default:
												valueStr = ""
											}
											itemBlock = replaceAllCaseInsensitive(itemBlock, placeholder, valueStr)
										}

										// Handle all conditional blocks dynamically
										for condKey := range conditionalKeys {
											condValue := false
											// Try case-insensitive lookup for the conditional key
											for objKey, condObj := range itemObj.Iter() {
												if strings.EqualFold(objKey, condKey) {
													switch condObj.Kind {
													case json.JsonBool:
														condValue = condObj.BoolVal
													case json.JsonString:
														// Parse string as bool, or check if non-empty
														if condObj.StrVal == "true" {
															condValue = true
														} else if condObj.StrVal == "false" {
															condValue = false
														} else {
															condValue = condObj.StrVal != ""
														}
													case json.JsonInteger:
														condValue = condObj.IntVal != 0
													case json.JsonNumber:
														condValue = condObj.NumVal != 0.0
													default:
														condValue = false
													}
													break
												}
											}
											itemBlock = handleConditional(itemBlock, condKey, condValue)
										}
										mergedBlock.WriteString(itemBlock)
									}
								}

								// Replace block in result
								result = result[:startIdx] + mergedBlock.String() + result[endIdx+len(blockEndTag):]
								break // Process only the first matching template for this JSON key
							}
						}
					}
				}
			}
		}
	}

	// Handle {{^ArrayName}} block if array is empty (dynamic detection)
	for key, value := range jsonObj.Iter() {
		emptyBlockStart := "{{^" + key + "}}"
		emptyBlockEnd := "{{/" + key + "}}"

		if emptyStartIdx := findCaseInsensitive(result, emptyBlockStart); emptyStartIdx != -1 {
			if emptyEndIdx := findCaseInsensitive(result, emptyBlockEnd); emptyEndIdx != -1 {
				if value.Kind == json.JsonArrayKind && value.ArrVal != nil {
					isEmpty := value.ArrVal.Len() == 0
					emptyContent := result[emptyStartIdx+len(emptyBlockStart) : emptyEndIdx]
					if isEmpty {
						result = result[:emptyStartIdx] + emptyContent + result[emptyEndIdx+len(emptyBlockEnd):]
					} else {
						result = result[:emptyStartIdx] + result[emptyEndIdx+len(emptyBlockEnd):]
					}
				}
			}
		}
	}

	// Replace remaining simple placeholders
	for key, value := range jsonObj.Iter() {
		if value.Kind == json.JsonString {
			placeholder := "{{$" + key + "}}"
			result = replaceAllCaseInsensitive(result, placeholder, value.StrVal)
		}
	}

	return result
}

// GetTemplate retrieves a template (html and json) from the templates dictionary with AppView fallback
func (e *EngineNormal) GetTemplate(appSite, templateName string, templates map[string]struct {
	HTML string
	JSON *string
}, appView, appViewPrefix string, useAppViewFallback bool) (string, *string) {
	if len(templates) == 0 {
		return "", nil
	}
	viewPrefix := appViewPrefix

	// FIRST: Check for AppView-specific template resolution when AppView context is provided
	if useAppViewFallback && appView != "" && viewPrefix != "" {
		// Case-insensitive check if template_name contains view_prefix
		templateNameLower := strings.ToLower(templateName)
		viewPrefixLower := strings.ToLower(viewPrefix)

		if strings.Contains(templateNameLower, viewPrefixLower) {
			// Direct replacement: Replace the AppViewPrefix with the AppView value
			// For example: Html3AContent with AppViewPrefix=Html3A and AppView=html3B becomes html3BContent
			appKey := ReplaceCaseInsensitive(templateName, viewPrefix, appView)
			fallbackTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(appKey)
			for k := range templates {
				if strings.EqualFold(k, fallbackTemplateKey) {
					v := templates[k]
					return v.HTML, v.JSON // Found AppView-specific template, use it
				}
			}
		}
	}

	// SECOND: If no AppView-specific template found, try primary template
	primaryTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(templateName)
	for k := range templates {
		if strings.EqualFold(k, primaryTemplateKey) {
			v := templates[k]
			return v.HTML, v.JSON
		}
	}

	return "", nil
}

// ReplaceTemplatePlaceholdersWithJson replaces placeholders using both templates and JSON values
func (e *EngineNormal) ReplaceTemplatePlaceholdersWithJson(html, appSite string, htmlFiles, jsonValues map[string]string, appView string) string {
	result := html
	searchPos := 0
	for searchPos < len(result) {
		openStart := strings.Index(result[searchPos:], "{{")
		if openStart == -1 {
			break
		}
		openStart += searchPos
		if openStart+2 < len(result) && strings.ContainsAny(string(result[openStart+2]), "#@/") { // Don't skip '$' placeholders!
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

		// PRIORITY 1: Check for JSON placeholders first (starts with '$')
		if strings.HasPrefix(placeholderName, "$") {
			key := placeholderName[1:] // Remove the leading '$'
			if value, exists := jsonValues[key]; exists {
				processedReplacement = value
			}
		} else if template_common.IsAlphaNumeric(placeholderName) {
			// PRIORITY 2: Check for template placeholders (alphanumeric)
			templateKey := strings.ToLower(appSite) + "_" + strings.ToLower(placeholderName)

			// Check for AppView fallback FIRST if appView is provided
			if appView != "" && e.AppViewPrefix != "" {
				// Case-insensitive check if placeholderName contains AppViewPrefix
				placeholderLower := strings.ToLower(placeholderName)
				prefixLower := strings.ToLower(e.AppViewPrefix)
				if strings.Contains(placeholderLower, prefixLower) {
					// Replace AppViewPrefix with AppView in placeholderName
					appKey := ReplaceCaseInsensitive(placeholderName, e.AppViewPrefix, appView)
					fallbackTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(appKey)
					if fallbackContent, exists := htmlFiles[fallbackTemplateKey]; exists {
						processedReplacement = e.ReplaceTemplatePlaceholdersWithJson(fallbackContent, appSite, htmlFiles, jsonValues, appView)
					}
				}
			}

			// If no AppView fallback found, use primary template key
			if processedReplacement == "" {
				if templateContent, exists := htmlFiles[templateKey]; exists {
					processedReplacement = e.ReplaceTemplatePlaceholdersWithJson(templateContent, appSite, htmlFiles, jsonValues, appView)
				}
			}

			// PRIORITY 3: If no template found, try JSON values (for backward compatibility)
			if processedReplacement == "" {
				if value, exists := jsonValues[placeholderName]; exists {
					processedReplacement = value
				}
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

// MergeTemplateSlots recursively merges slotted templates with content
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

// ProcessTemplateSlots processes slotted templates using IndexOf
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
		if templateName == "" || !template_common.IsAlphaNumeric(templateName) {
			searchPos = openStart + 1
			continue
		}
		closeTag := "{{/" + templateName + "}}"
		closeStart, found := template_common.FindMatchingCloseTag(result, openEnd+2, "{{#"+templateName+"}}", closeTag)
		if !found {
			searchPos = openStart + 1
			continue
		}
		innerStart := openEnd + 2
		innerContent := result[innerStart:closeStart]

		// Convert string map to anonymous struct map for GetTemplate method (like Rust does)
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

		// Use GetTemplate method like Rust does
		templateHtml, _ := e.GetTemplate(appSite, templateName, templatesForGetTemplate, appView, e.AppViewPrefix, true)
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

// ExtractSlotContents extracts slot contents using IndexOf approach
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
		closeStart, found := template_common.FindMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag)
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

// ReplaceTemplatePlaceholders processes simple placeholders only
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
		if placeholderName == "" || !template_common.IsAlphaNumeric(placeholderName) {
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

// Helper: Replace all case-insensitive occurrences
func replaceAllCaseInsensitive(input, search, replacement string) string {
	result := input
	idx := 0
	for {
		if found := findCaseInsensitive(result[idx:], search); found != -1 {
			found = idx + found
			result = result[:found] + replacement + result[found+len(search):]
			idx = found + len(replacement)
		} else {
			break
		}
	}
	return result
}

// Helper: Handle conditional blocks like {{@Selected}}...{{/Selected}}
func handleConditional(input, key string, condition bool) string {
	result := input

	// Support spaces inside block tags, e.g. {{@Selected}} ... {{ /Selected}}
	condStart := "{{@" + key + "}}"
	condEndSpace := "{{ /" + key + "}}"

	// Handle first pattern: {{ /Key}} (with space)
	for {
		if startIdx := findCaseInsensitive(result, condStart); startIdx != -1 {
			if endIdx := findCaseInsensitive(result, condEndSpace); endIdx != -1 {
				content := result[startIdx+len(condStart) : endIdx]
				if condition {
					result = result[:startIdx] + content + result[endIdx+len(condEndSpace):]
				} else {
					result = result[:startIdx] + result[endIdx+len(condEndSpace):]
				}
			} else {
				break
			}
		} else {
			break
		}
	}

	// Also handle without space: {{/Key}}
	condEndNoSpace := "{{/" + key + "}}"
	for {
		if startIdx := findCaseInsensitive(result, condStart); startIdx != -1 {
			if endIdx := findCaseInsensitive(result, condEndNoSpace); endIdx != -1 {
				content := result[startIdx+len(condStart) : endIdx]
				if condition {
					result = result[:startIdx] + content + result[endIdx+len(condEndNoSpace):]
				} else {
					result = result[:startIdx] + result[endIdx+len(condEndNoSpace):]
				}
			} else {
				break
			}
		} else {
			break
		}
	}

	return result
}

// Helper: Find case-insensitive occurrence of search string
func findCaseInsensitive(haystack, needle string) int {
	lower := strings.ToLower(haystack)
	lowerNeedle := strings.ToLower(needle)
	return strings.Index(lower, lowerNeedle)
}

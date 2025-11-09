package common

import (
	"fmt"
	"strconv"
	"strings"
)

// MergeTemplateWithJson merges an HTML template with JSON data.
func MergeTemplateWithJson(template string, jsonData map[string]interface{}) string {
	// Advanced merge logic for block and conditional patterns
	result := template

	// Process JSON arrays
	for jsonKey, value := range jsonData {
		if dataList, ok := value.([]interface{}); ok {
			keyNorm := strings.ToLower(jsonKey)
			possibleTags := []string{jsonKey, strings.ToLower(jsonKey), strings.TrimSuffix(keyNorm, "s"), keyNorm + "s"}

			for _, tag := range possibleTags {
				blockStartTag := "{{@" + tag + "}}"
				blockEndTag := "{{/" + tag + "}}"

				startIdx := strings.Index(strings.ToLower(result), strings.ToLower(blockStartTag))
				if startIdx != -1 {
					searchFrom := startIdx + len(blockStartTag)
					endIdx := strings.Index(strings.ToLower(result[searchFrom:]), strings.ToLower(blockEndTag))
					if endIdx != -1 {
						endIdx += searchFrom
						blockContent := result[startIdx+len(blockStartTag) : endIdx]
						var mergedBlock strings.Builder

						conditionalKeys := findConditionalKeys(blockContent)

						for _, item := range dataList {
							itemBlock := blockContent
							if itemMap, ok := item.(map[string]interface{}); ok {
								for k, v := range itemMap {
									placeholder := "{{$" + k + "}}"
									valueStr := fmt.Sprintf("%v", v)
									itemBlock = ReplaceAllCaseInsensitive(itemBlock, placeholder, valueStr)
								}
								for condKey := range conditionalKeys {
									condValue := false
									if condObj, ok := itemMap[condKey]; ok {
										condValue = isTruthy(condObj)
									}
									itemBlock = HandleConditional(itemBlock, condKey, condValue)
								}
							}
							mergedBlock.WriteString(itemBlock)
						}

						result = result[:startIdx] + mergedBlock.String() + result[endIdx+len(blockEndTag):]
						break // Process only the first matching template for this JSON key
					}
				}
			}
		}
	}

	// Handle {{^ArrayName}} block if array is empty
	for key, value := range jsonData {
		emptyBlockStart := "{{^" + key + "}}"
		emptyBlockEnd := "{{/" + key + "}}"
		startIdx := strings.Index(strings.ToLower(result), strings.ToLower(emptyBlockStart))
		if startIdx != -1 {
			endIdx := strings.Index(strings.ToLower(result[startIdx:]), strings.ToLower(emptyBlockEnd))
			if endIdx != -1 {
				endIdx += startIdx
				if dataList, ok := value.([]interface{}); ok && len(dataList) == 0 {
					emptyContent := result[startIdx+len(emptyBlockStart) : endIdx]
					result = result[:startIdx] + emptyContent + result[endIdx+len(emptyBlockEnd):]
				} else {
					result = result[:startIdx] + result[endIdx+len(emptyBlockEnd):]
				}
			}
		}
	}

	// Replace remaining simple placeholders
	for k, v := range jsonData {
		placeholder := "{{$" + k + "}}"
		valueStr := fmt.Sprintf("%v", v)
		result = ReplaceAllCaseInsensitive(result, placeholder, valueStr)
	}

	return result
}

func findConditionalKeys(blockContent string) map[string]bool {
	conditionalKeys := make(map[string]bool)
	idx := 0
	for {
		condStart := strings.Index(strings.ToLower(blockContent[idx:]), "{{@")
		if condStart == -1 {
			break
		}
		condStart += idx
		condEnd := strings.Index(blockContent[condStart:], "}}")
		if condEnd == -1 {
			break
		}
		condEnd += condStart
		condKey := strings.TrimSpace(blockContent[condStart+3 : condEnd])
		conditionalKeys[condKey] = true
		idx = condEnd + 2
	}
	return conditionalKeys
}

func isTruthy(condObj interface{}) bool {
	switch v := condObj.(type) {
	case bool:
		return v
	case string:
		b, err := strconv.ParseBool(v)
		return err == nil && b
	case int, int8, int16, int32, int64:
		return v != 0
	case uint, uint8, uint16, uint32, uint64:
		return v != 0
	case float32, float64:
		return v != 0
	}
	return false
}

func HandleConditional(input, key string, condition bool) string {
	// Handle with space
	condStart := "{{@" + key + "}}"
	condEnd := "{{ /" + key + "}}"
	startIdx := strings.Index(strings.ToLower(input), strings.ToLower(condStart))
	endIdx := strings.Index(strings.ToLower(input), strings.ToLower(condEnd))
	for startIdx != -1 && endIdx != -1 {
		content := input[startIdx+len(condStart) : endIdx]
		if condition {
			input = input[:startIdx] + content + input[endIdx+len(condEnd):]
		} else {
			input = input[:startIdx] + input[endIdx+len(condEnd):]
		}
		startIdx = strings.Index(strings.ToLower(input), strings.ToLower(condStart))
		endIdx = strings.Index(strings.ToLower(input), strings.ToLower(condEnd))
	}

	// Handle without space
	condEnd = "{{/" + key + "}}"
	startIdx = strings.Index(strings.ToLower(input), strings.ToLower(condStart))
	endIdx = strings.Index(strings.ToLower(input), strings.ToLower(condEnd))
	for startIdx != -1 && endIdx != -1 {
		content := input[startIdx+len(condStart) : endIdx]
		if condition {
			input = input[:startIdx] + content + input[endIdx+len(condEnd):]
		} else {
			input = input[:startIdx] + input[endIdx+len(condEnd):]
		}
		startIdx = strings.Index(strings.ToLower(input), strings.ToLower(condStart))
		endIdx = strings.Index(strings.ToLower(input), strings.ToLower(condEnd))
	}
	return input
}

func ReplaceAllCaseInsensitive(input, search, replacement string) string {
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

func FindCaseInsensitive(haystack, needle string) int {
	return strings.Index(strings.ToLower(haystack), strings.ToLower(needle))
}
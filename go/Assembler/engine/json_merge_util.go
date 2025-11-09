package engine

import (
	"assembler/common"
)

// MergeTemplateWithJson merges an HTML template with JSON data.
func MergeTemplateWithJson(template string, jsonData map[string]interface{}) string {
	return common.MergeTemplateWithJson(template, jsonData)
}
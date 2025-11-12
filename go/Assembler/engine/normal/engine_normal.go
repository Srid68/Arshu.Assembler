package engine_normal

import (
	"assembler/common"
	"fmt"
	"strings"

	Logger "arshu/common"
	interfaces "assembler/interface"
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

func (e *EngineNormal) MergeTemplates(appSite, appFile, appView string, loader interfaces.ILoaderNormal, enableJsonProcessing bool) string {
	Logger.Debug(fmt.Sprintf("MergeTemplates called: appSite=%s, appFile=%s, appView=%s, enableJson=%t", appSite, appFile, appView, enableJsonProcessing), "EngineNormal")

	if loader == nil {
		Logger.Warn("No loader provided", "EngineNormal")
		return ""
	}

	// Get main template using loader (includes AppView fallback and SearchAppSites logic)
	contentHtml := loader.GetTemplateHtml(appSite, appFile, appView, e.AppViewPrefix)
	if contentHtml == "" {
		Logger.Warn(fmt.Sprintf("Main template not found for appSite=%s, appFile=%s", appSite, appFile), "EngineNormal")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Main template found, html size: %d", len(contentHtml)), "EngineNormal")

	// Merge main template with JSON using loader's centralized method
	if enableJsonProcessing {
		Logger.Debug("Merging main template with JSON", "EngineNormal")
		contentHtml = loader.MergeHtmlWithJson(contentHtml, appSite, appFile)
		Logger.Debug(fmt.Sprintf("After main JSON merge: %d chars", len(contentHtml)), "EngineNormal")
	}

	// Simple loop like C# implementation
	// Templates are now loaded on-demand via loader
	previous := ""
	actualPasses := 0
	maxPasses := 10
	for pass := 0; pass < maxPasses; pass++ {
		previous = contentHtml
		actualPasses = pass + 1

		Logger.Debug(fmt.Sprintf("Pass %d, current size: %d", actualPasses, len(contentHtml)), "EngineNormal")

		contentHtml = e.MergeTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing)
		Logger.Debug(fmt.Sprintf("After slot merge: %d chars", len(contentHtml)), "EngineNormal")

		contentHtml = e.ReplaceTemplatePlaceholders(contentHtml, appSite, appView, loader, enableJsonProcessing)
		Logger.Debug(fmt.Sprintf("After placeholder replacement: %d chars", len(contentHtml)), "EngineNormal")

		if contentHtml == previous {
			Logger.Debug(fmt.Sprintf("No changes in pass %d, stopping", actualPasses), "EngineNormal")
			break
		}
	}

	Logger.Debug(fmt.Sprintf("MergeTemplates complete after %d passes: output size=%d", actualPasses, len(contentHtml)), "EngineNormal")
	return contentHtml
}

// GetTemplateWithJson gets a template with on-demand loading and JSON merging from ILoaderNormal
func (e *EngineNormal) GetTemplateWithJson(appSite, templateName string, loader interfaces.ILoaderNormal, appView string, enableJsonProcessing bool) string {
	// Get HTML template (includes AppView fallback and SearchAppSites logic)
	html := loader.GetTemplateHtml(appSite, templateName, appView, e.AppViewPrefix)
	if html == "" {
		return ""
	}

	Logger.Debug(fmt.Sprintf("GetTemplateWithJson: template=%s, html size=%d", templateName, len(html)), "EngineNormal")

	// Merge with JSON if enabled using loader's centralized method
	if enableJsonProcessing {
		originalSize := len(html)
		html = loader.MergeHtmlWithJson(html, appSite, templateName)
		Logger.Debug(fmt.Sprintf("After JSON merge for %s: size %d -> %d", templateName, originalSize, len(html)), "EngineNormal")
	}

	return html
}

// ReplaceTemplatePlaceholders processes simple placeholders only (without slotted template processing)
func (e *EngineNormal) ReplaceTemplatePlaceholders(html, appSite, appView string, loader interfaces.ILoaderNormal, enableJsonProcessing bool) string {
	result := html
	searchPos := 0

	for searchPos < len(result) {
		// Look for opening placeholder {{
		openStart := strings.Index(result[searchPos:], "{{")
		if openStart == -1 {
			break
		}
		openStart += searchPos

		// Make sure it's not a slotted template or special placeholder
		if openStart+2 < len(result) && strings.ContainsAny(string(result[openStart+2]), "#@$/") {
			searchPos = openStart + 2
			continue
		}

		// Find closing }}
		closeStart := strings.Index(result[openStart+2:], "}}")
		if closeStart == -1 {
			break
		}
		closeStart += openStart + 2

		// Extract placeholder name
		placeholderName := strings.TrimSpace(result[openStart+2 : closeStart])
		if placeholderName == "" || !common.IsAlphaNumeric(placeholderName) {
			searchPos = openStart + 2
			continue
		}

		// Load template with JSON on-demand
		templateContent := e.GetTemplateWithJson(appSite, placeholderName, loader, appView, enableJsonProcessing)

		if templateContent != "" {
			// Recursively process the loaded template
			processedReplacement := e.ReplaceTemplatePlaceholders(templateContent, appSite, appView, loader, enableJsonProcessing)
			placeholder := result[openStart : closeStart+2]
			result = strings.Replace(result, placeholder, processedReplacement, 1)
			searchPos = openStart + len(processedReplacement)
		} else {
			searchPos = closeStart + 2
		}
	}

	return result
}

// MergeTemplateSlots recursively merges slotted templates (e.g., center.html, columns.html) with content
func (e *EngineNormal) MergeTemplateSlots(contentHtml, appSite, appView string, loader interfaces.ILoaderNormal, enableJsonProcessing bool) string {
	if contentHtml == "" {
		return contentHtml
	}

	previous := ""
	for {
		previous = contentHtml
		contentHtml = e.ProcessTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing)
		if contentHtml == previous {
			break
		}
	}
	return contentHtml
}

// ProcessTemplateSlots is a helper method to process slotted templates
func (e *EngineNormal) ProcessTemplateSlots(contentHtml, appSite, appView string, loader interfaces.ILoaderNormal, enableJsonProcessing bool) string {
	result := contentHtml
	searchPos := 0

	for searchPos < len(result) {
		// Look for opening tag {{#
		openStart := strings.Index(result[searchPos:], "{{#")
		if openStart == -1 {
			break
		}
		openStart += searchPos

		// Find the end of the template name
		openEnd := strings.Index(result[openStart+3:], "}}")
		if openEnd == -1 {
			break
		}
		openEnd += openStart + 3

		// Extract template name
		templateName := strings.TrimSpace(result[openStart+3 : openEnd])
		if templateName == "" || !common.IsAlphaNumeric(templateName) {
			searchPos = openStart + 1
			continue
		}

		// Look for corresponding closing tag
		closeTag := "{{/" + templateName + "}}"
		closeStart, found := common.FindMatchingCloseTag(result, openEnd+2, "{{#"+templateName+"}}", closeTag)
		if !found {
			searchPos = openStart + 1
			continue
		}

		// Extract inner content
		innerStart := openEnd + 2
		innerContent := result[innerStart:closeStart]

		// Load template with JSON on-demand
		templateHtml := e.GetTemplateWithJson(appSite, templateName, loader, appView, enableJsonProcessing)

		if templateHtml != "" {
			// Extract slot contents
			slotContents := e.ExtractSlotContents(innerContent, appSite, appView, loader, enableJsonProcessing)

			// Replace slots in template
			processedTemplate := templateHtml
			for k, v := range slotContents {
				processedTemplate = strings.ReplaceAll(processedTemplate, k, v)
			}

			// Remove any remaining slot placeholders
			processedTemplate = common.RemoveRemainingSlotPlaceholders(processedTemplate)

			// Replace the entire slotted section
			fullMatch := result[openStart : closeStart+len(closeTag)]
			result = strings.Replace(result, fullMatch, processedTemplate, 1)
			searchPos = openStart + len(processedTemplate)
		} else {
			searchPos = openStart + 1
		}
	}

	return result
}

// ExtractSlotContents extracts slot contents from inner content
func (e *EngineNormal) ExtractSlotContents(innerContent, appSite, appView string, loader interfaces.ILoaderNormal, enableJsonProcessing bool) map[string]string {
	slotContents := make(map[string]string)
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

		closeStart, found := common.FindMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag)
		if !found {
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

		// Process both slotted templates AND simple placeholders in slot content
		// This enables proper nested template processing to match the preprocessing implementation
		recursiveResult := e.MergeTemplateSlots(slotContent, appSite, appView, loader, enableJsonProcessing)
		recursiveResult = e.ReplaceTemplatePlaceholders(recursiveResult, appSite, appView, loader, enableJsonProcessing)
		slotContents[slotKey] = recursiveResult

		searchPos = closeStart + len(closeTag)
	}

	return slotContents
}


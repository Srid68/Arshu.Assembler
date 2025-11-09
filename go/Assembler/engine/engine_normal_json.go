package engine

import (
	Logger "arshu/common"
	"assembler/common"
	"fmt"
	"strings"
)

type ILoaderJson interface {
	GetTemplateHtml(appSite, appFile, appView, appViewPrefix string) string
	GetTemplateJson(appSite, appFile string) map[string]interface{}
}

type EngineNormalJson struct {
	AppViewPrefix string
}

func (e *EngineNormalJson) MergeTemplates(appSite, appFile, appView string, loader ILoaderJson, enableJsonProcessing bool) string {
	Logger.Debug(fmt.Sprintf("MergeTemplates called: appSite=%s, appFile=%s, appView=%s, enableJson=%t", appSite, appFile, appView, enableJsonProcessing), "EngineNormalJson")

	if loader == nil {
		Logger.Debug("No loader provided", "EngineNormalJson")
		return ""
	}

	contentHtml := loader.GetTemplateHtml(appSite, appFile, appView, e.AppViewPrefix)
	if contentHtml == "" {
		Logger.Debug(fmt.Sprintf("Main template not found for appSite=%s, appFile=%s", appSite, appFile), "EngineNormalJson")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Main template found, html size: %d", len(contentHtml)), "EngineNormalJson")

	if enableJsonProcessing {
		jsonData := loader.GetTemplateJson(appSite, appFile)
		if jsonData != nil {
			Logger.Debug("Merging main template with JSON", "EngineNormalJson")
			contentHtml = common.MergeTemplateWithJson(contentHtml, jsonData)
			Logger.Debug(fmt.Sprintf("After main JSON merge: %d chars", len(contentHtml)), "EngineNormalJson")
		}
	}

	previous := ""
	maxPasses := 10
	actualPasses := 0
	for pass := 0; pass < maxPasses; pass++ {
		previous = contentHtml
		actualPasses = pass + 1

		Logger.Debug(fmt.Sprintf("Pass %d, current size: %d", actualPasses, len(contentHtml)), "EngineNormalJson")

		contentHtml = e.mergeTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing)
		Logger.Debug(fmt.Sprintf("After slot merge: %d chars", len(contentHtml)), "EngineNormalJson")

		contentHtml = e.replaceTemplatePlaceholders(contentHtml, appSite, appView, loader, enableJsonProcessing)
		Logger.Debug(fmt.Sprintf("After placeholder replacement: %d chars", len(contentHtml)), "EngineNormalJson")

		if contentHtml == previous {
			Logger.Debug(fmt.Sprintf("No changes in pass %d, stopping", actualPasses), "EngineNormalJson")
			break
		}
	}

	Logger.Debug(fmt.Sprintf("MergeTemplates complete after %d passes: output size=%d", actualPasses, len(contentHtml)), "EngineNormalJson")
	return contentHtml
}

func (e *EngineNormalJson) getTemplateWithJson(appSite, templateName, appView string, loader ILoaderJson, enableJsonProcessing bool) string {
	html := loader.GetTemplateHtml(appSite, templateName, appView, e.AppViewPrefix)
	if html == "" {
		return ""
	}

	Logger.Debug(fmt.Sprintf("GetTemplateWithJson: template=%s, html size=%d", templateName, len(html)), "EngineNormalJson")

	if enableJsonProcessing {
		jsonData := loader.GetTemplateJson(appSite, templateName)
		if jsonData != nil {
			Logger.Debug(fmt.Sprintf("Merging JSON for template %s", templateName), "EngineNormalJson")
			originalSize := len(html)
			html = common.MergeTemplateWithJson(html, jsonData)
			Logger.Debug(fmt.Sprintf("After JSON merge for %s: size %d -> %d", templateName, originalSize, len(html)), "EngineNormalJson")
		} else {
			Logger.Debug(fmt.Sprintf("No JSON data found for template %s", templateName), "EngineNormalJson")
		}
	}

	return html
}

func (e *EngineNormalJson) mergeTemplateSlots(contentHtml, appSite, appView string, loader ILoaderJson, enableJsonProcessing bool) string {
	if contentHtml == "" {
		return contentHtml
	}

	previous := ""
	for {
		previous = contentHtml
		contentHtml = e.processTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing)
		if contentHtml == previous {
			break
		}
	}
	return contentHtml
}

func (e *EngineNormalJson) processTemplateSlots(contentHtml, appSite, appView string, loader ILoaderJson, enableJsonProcessing bool) string {
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

		templateHtml := e.getTemplateWithJson(appSite, templateName, appView, loader, enableJsonProcessing)

		if templateHtml != "" {
			slotContents := e.extractSlotContents(innerContent, appSite, appView, loader, enableJsonProcessing)

			processedTemplate := templateHtml
			for key, value := range slotContents {
				processedTemplate = strings.ReplaceAll(processedTemplate, key, value)
			}

			processedTemplate = common.RemoveRemainingSlotPlaceholders(processedTemplate)

			fullMatch := result[openStart : closeStart+len(closeTag)]
			result = strings.Replace(result, fullMatch, processedTemplate, 1)
			searchPos = openStart + len(processedTemplate)
		} else {
			searchPos = openStart + 1
		}
	}

	return result
}

func (e *EngineNormalJson) extractSlotContents(innerContent, appSite, appView string, loader ILoaderJson, enableJsonProcessing bool) map[string]string {
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

		closeTag := "{{/HTMLPLACEHOLDER}}"
		if slotNum != "" {
			closeTag = "{{/HTMLPLACEHOLDER" + slotNum + "}}"
		}
		openTag := "{{@HTMLPLACEHOLDER}}"
		if slotNum != "" {
			openTag = "{{@HTMLPLACEHOLDER" + slotNum + "}}"
		}

		closeStart, found := common.FindMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag)
		if !found {
			searchPos = slotStart + 1
			continue
		}

		slotContent := innerContent[slotOpenEnd:closeStart]

		slotKey := "{{$HTMLPLACEHOLDER}}"
		if slotNum != "" {
			slotKey = "{{$HTMLPLACEHOLDER" + slotNum + "}}"
		}

		recursiveResult := e.mergeTemplateSlots(slotContent, appSite, appView, loader, enableJsonProcessing)
		recursiveResult = e.replaceTemplatePlaceholders(recursiveResult, appSite, appView, loader, enableJsonProcessing)
		slotContents[slotKey] = recursiveResult

		searchPos = closeStart + len(closeTag)
	}

	return slotContents
}

func (e *EngineNormalJson) replaceTemplatePlaceholders(html, appSite, appView string, loader ILoaderJson, enableJsonProcessing bool) string {
	result := html
	searchPos := 0

	for searchPos < len(result) {
		openStart := strings.Index(result[searchPos:], "{{")
		if openStart == -1 {
			break
		}
		openStart += searchPos

		if openStart+2 < len(result) && (result[openStart+2] == '#' || result[openStart+2] == '@' || result[openStart+2] == '$' || result[openStart+2] == '/') {
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

		templateContent := e.getTemplateWithJson(appSite, placeholderName, appView, loader, enableJsonProcessing)

		if templateContent != "" {
			processedReplacement := e.replaceTemplatePlaceholders(templateContent, appSite, appView, loader, enableJsonProcessing)
			placeholder := result[openStart : closeStart+2]
			result = strings.Replace(result, placeholder, processedReplacement, 1)
			searchPos = openStart + len(processedReplacement)
		} else {
			searchPos = closeStart + 2
		}
	}

	return result
}
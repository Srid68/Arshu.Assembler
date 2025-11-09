package engine

import (
	Logger "arshu/common"
	"assembler/common"
	"assembler/model"
	"fmt"
	"strings"
)

type ILoaderPreprocessJson interface {
	GetTemplateHtml(appSite, appFile, appView, appViewPrefix string) *model.PreprocessedTemplate
	GetTemplateJson(appSite, appFile string) map[string]interface{}
	AllTemplates() map[string]*model.PreprocessedTemplate
}

type EnginePreProcessJson struct {
	AppViewPrefix string
}

func (e *EnginePreProcessJson) MergeTemplates(appSite, appFile, appView string, loader ILoaderPreprocessJson, enableJsonProcessing bool) string {
	Logger.Debug(fmt.Sprintf("MergeTemplates called: appSite=%s, appFile=%s, appView=%s, enableJson=%t", appSite, appFile, appView, enableJsonProcessing), "EnginePreProcessJson")

	if loader == nil {
		Logger.Debug("No loader provided", "EnginePreProcessJson")
		return ""
	}

	preprocessedTemplates := loader.AllTemplates()
	if len(preprocessedTemplates) == 0 {
		Logger.Debug("No preprocessed templates available", "EnginePreProcessJson")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Using %d preprocessed templates", len(preprocessedTemplates)), "EnginePreProcessJson")

	mainPreprocessed := loader.GetTemplateHtml(appSite, appFile, appView, e.AppViewPrefix)
	if mainPreprocessed == nil {
		Logger.Debug(fmt.Sprintf("Main template not found for appSite=%s, appFile=%s", appSite, appFile), "EnginePreProcessJson")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Main template found, original size: %d", len(mainPreprocessed.OriginalContent)), "EnginePreProcessJson")

	contentHtml := mainPreprocessed.OriginalContent

	if enableJsonProcessing && mainPreprocessed.JsonData != nil {
		Logger.Debug("Merging main template JSON", "EnginePreProcessJson")
		contentHtml = MergeTemplateWithJson(contentHtml, *mainPreprocessed.JsonData)
	}

	contentHtml = e.applyTemplateReplacements(contentHtml, preprocessedTemplates, enableJsonProcessing, appView, mainPreprocessed, loader, appSite)

	Logger.Debug(fmt.Sprintf("MergeTemplates complete: output size=%d", len(contentHtml)), "EnginePreProcessJson")
	return contentHtml
}

func (e *EnginePreProcessJson) getTemplate(appSite, templateName string, preprocessedTemplates map[string]*model.PreprocessedTemplate, appView, appViewPrefix string, useAppViewFallback bool) *model.PreprocessedTemplate {
	if len(preprocessedTemplates) == 0 {
		return nil
	}

	viewPrefix := appViewPrefix
	if viewPrefix == "" {
		viewPrefix = e.AppViewPrefix
	}

	if useAppViewFallback && appView != "" && viewPrefix != "" && strings.Contains(templateName, viewPrefix) {
		appKey := common.ReplaceCaseInsensitive(templateName, viewPrefix, appView)
		fallbackTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(appKey)
		if fallbackTemplate, ok := preprocessedTemplates[fallbackTemplateKey]; ok {
			return fallbackTemplate
		}
	}

	primaryTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(templateName)
	if primaryTemplate, ok := preprocessedTemplates[primaryTemplateKey]; ok {
		return primaryTemplate
	}

	return nil
}

func (e *EnginePreProcessJson) applyTemplateReplacements(content string, preprocessedTemplates map[string]*model.PreprocessedTemplate, enableJsonProcessing bool, appView string, mainTemplate *model.PreprocessedTemplate, loader ILoaderPreprocessJson, appSite string) string {
	result := content
	Logger.Debug(fmt.Sprintf("Starting ApplyTemplateReplacements, initial size: %d", len(content)), "EnginePreProcessJson")

	previous := ""
	maxPasses := 10
	currentPass := 0

	for {
		previous = result
		currentPass++

		Logger.Debug(fmt.Sprintf("Replacement pass %d, current size: %d", currentPass, len(result)), "EnginePreProcessJson")

		slottedCount, simpleCount, jsonPlaceholderCount := 0, 0, 0

		if mainTemplate != nil && currentPass == 1 && enableJsonProcessing {
			for _, mapping := range mainTemplate.ReplacementMappings {
				if mapping.Type != model.JsonPlaceholderType {
					continue
				}
				if strings.Contains(result, mapping.OriginalText) {
					Logger.Debug(fmt.Sprintf("Applying main template JSON placeholder: %s -> %s", mapping.OriginalText, mapping.ReplacementText), "EnginePreProcessJson")
					result = strings.ReplaceAll(result, mapping.OriginalText, mapping.ReplacementText)
					jsonPlaceholderCount++
				}
			}
		}

		for _, template := range preprocessedTemplates {
			for _, mapping := range template.ReplacementMappings {
				if mapping.Type != model.SlottedTemplateType {
					continue
				}
				if strings.Contains(result, mapping.OriginalText) {
					replacementText := mapping.ReplacementText
					if enableJsonProcessing && mapping.TargetTemplateName != "" {
						targetJson := loader.GetTemplateJson(appSite, mapping.TargetTemplateName)
						if targetJson != nil {
							Logger.Debug(fmt.Sprintf("Merging JSON for slotted template %s", mapping.TargetTemplateName), "EnginePreProcessJson")
							replacementText = MergeTemplateWithJson(replacementText, targetJson)
						}
					}
					Logger.Debug(fmt.Sprintf("Applying slotted template: %s... -> %d chars", mapping.OriginalText[:min(50, len(mapping.OriginalText))], len(replacementText)), "EnginePreProcessJson")
					result = strings.ReplaceAll(result, mapping.OriginalText, replacementText)
					slottedCount++
				}
			}

			for _, mapping := range template.ReplacementMappings {
				if mapping.Type != model.SimpleTemplateType {
					continue
				}
				if strings.Contains(result, mapping.OriginalText) {
					replacementText := mapping.ReplacementText
					if appView != "" && mapping.TargetTemplateName != "" {
						appViewTemplate := e.getTemplate(appSite, mapping.TargetTemplateName, preprocessedTemplates, appView, e.AppViewPrefix, true)
						if appViewTemplate != nil {
							replacementText = appViewTemplate.OriginalContent
						}
					}

					if enableJsonProcessing && mapping.TargetTemplateName != "" {
						targetJson := loader.GetTemplateJson(appSite, mapping.TargetTemplateName)
						if targetJson != nil {
							Logger.Debug(fmt.Sprintf("Merging JSON for simple template %s", mapping.TargetTemplateName), "EnginePreProcessJson")
							replacementText = MergeTemplateWithJson(replacementText, targetJson)
						}
					}
					Logger.Debug(fmt.Sprintf("Applying simple template: %s -> %d chars", mapping.OriginalText, len(replacementText)), "EnginePreProcessJson")
					result = strings.ReplaceAll(result, mapping.OriginalText, replacementText)
					simpleCount++
				}
			}
		}

		Logger.Debug(fmt.Sprintf("Pass %d applied: %d main JSON placeholders, %d slotted, %d simple", currentPass, jsonPlaceholderCount, slottedCount, simpleCount), "EnginePreProcessJson")

		if result == previous || currentPass >= maxPasses {
			break
		}
	}

	Logger.Debug(fmt.Sprintf("Replacement complete after %d passes, final size: %d", currentPass, len(result)), "EnginePreProcessJson")
	return result
}

package engine

import (
	"assembler/common"
	"assembler/model"
	Logger "arshu/common"
	"fmt"
	"strings"
)

type EnginePreProcess struct {
	AppViewPrefix string
}

func NewEnginePreProcess(prefix string) *EnginePreProcess {
	return &EnginePreProcess{AppViewPrefix: prefix}
}

func (e *EnginePreProcess) SetAppViewPrefix(prefix string) {
	e.AppViewPrefix = prefix
}

func (e *EnginePreProcess) GetAppViewPrefix() string {
	return e.AppViewPrefix
}

// MergeTemplates merges templates using preprocessed data structures
func (e *EnginePreProcess) MergeTemplates(appSite, appFile, appView string, preprocessedTemplates map[string]model.PreprocessedTemplate, enableJsonProcessing bool) string {
	Logger.Debug(fmt.Sprintf("MergeTemplates called: appSite=%s, appFile=%s, appView=%s, enableJson=%t", appSite, appFile, appView, enableJsonProcessing), "EnginePreProcess")

	if len(preprocessedTemplates) == 0 {
		Logger.Warn("No preprocessed templates available", "EnginePreProcess")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Using %d preprocessed templates", len(preprocessedTemplates)), "EnginePreProcess")

	mainPreprocessed := e.GetTemplate(appSite, appFile, preprocessedTemplates, appView, e.AppViewPrefix, true)
	if mainPreprocessed == nil {
		Logger.Warn(fmt.Sprintf("Main template not found for appSite=%s, appFile=%s", appSite, appFile), "EnginePreProcess")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Main template found, original size: %d", len(mainPreprocessed.OriginalContent)), "EnginePreProcess")

	contentHtml := mainPreprocessed.OriginalContent

	// Apply ALL replacement mappings from ALL templates (TemplateLoader did all the processing)
	contentHtml = e.ApplyTemplateReplacements(contentHtml, preprocessedTemplates, enableJsonProcessing, appView, mainPreprocessed)

	Logger.Debug(fmt.Sprintf("MergeTemplates complete: output size=%d", len(contentHtml)), "EnginePreProcess")

	return contentHtml
}

// GetTemplate retrieves a preprocessed template from the dictionary with AppView fallback
func (e *EnginePreProcess) GetTemplate(appSite, templateName string, preprocessedTemplates map[string]model.PreprocessedTemplate, appView, appViewPrefix string, useAppViewFallback bool) *model.PreprocessedTemplate {
	if len(preprocessedTemplates) == 0 {
		return nil
	}
	viewPrefix := appViewPrefix
	if viewPrefix == "" {
		viewPrefix = e.AppViewPrefix
	}

	// FIRST: Check for AppView-specific template resolution when AppView context is provided
	if useAppViewFallback && appView != "" && viewPrefix != "" {
		// Case-insensitive check if template_name contains view_prefix
		templateNameLower := strings.ToLower(templateName)
		viewPrefixLower := strings.ToLower(viewPrefix)

		if strings.Contains(templateNameLower, viewPrefixLower) {
			// Direct replacement: Replace the AppViewPrefix with the AppView value
			// For example: Html3AContent with AppViewPrefix=Html3A and AppView=html3B becomes html3BContent
			appKey := common.ReplaceCaseInsensitive(templateName, viewPrefix, appView)
			fallbackTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(appKey)
			if tmpl, ok := preprocessedTemplates[fallbackTemplateKey]; ok {
				return &tmpl // Found AppView-specific template, use it
			}
		}
	}

	// SECOND: If no AppView-specific template found, try primary template
	primaryTemplateKey := strings.ToLower(appSite) + "_" + strings.ToLower(templateName)
	if tmpl, ok := preprocessedTemplates[primaryTemplateKey]; ok {
		return &tmpl
	}

	return nil
}

// ApplyTemplateReplacements applies all replacement mappings from all templates
func (e *EnginePreProcess) ApplyTemplateReplacements(content string, preprocessedTemplates map[string]model.PreprocessedTemplate, enableJsonProcessing bool, appView string, mainTemplate *model.PreprocessedTemplate) string {
	result := content

	Logger.Debug(fmt.Sprintf("Starting ApplyTemplateReplacements, initial size: %d", len(content)), "EnginePreProcess")

	maxPasses := 10
	currentPass := 0

	for i := 0; i < maxPasses; i++ {
		previous := result
		currentPass++

		Logger.Debug(fmt.Sprintf("Replacement pass %d, current size: %d", currentPass, len(result)), "EnginePreProcess")

		slottedCount := 0
		simpleCount := 0
		jsonPlaceholderCount := 0

		// FIRST: Apply JSON placeholder mappings ONLY from the main template (to avoid overwriting component content)
		if mainTemplate != nil && currentPass == 1 && enableJsonProcessing {
			for _, mapping := range mainTemplate.ReplacementMappings {
				if mapping.Type == model.JsonPlaceholderType && strings.Contains(result, mapping.OriginalText) {
					Logger.Debug(fmt.Sprintf("Applying main template JSON placeholder: %s -> %s", mapping.OriginalText, mapping.ReplacementText), "EnginePreProcess")
					result = strings.ReplaceAll(result, mapping.OriginalText, mapping.ReplacementText)
					jsonPlaceholderCount++
				}
			}
		}

		// Apply replacement mappings from all templates
		for _, tmpl := range preprocessedTemplates {
			// Apply slotted template mappings
			for _, mapping := range tmpl.ReplacementMappings {
				if mapping.Type == model.SlottedTemplateType && strings.Contains(result, mapping.OriginalText) {
					result = strings.ReplaceAll(result, mapping.OriginalText, mapping.ReplacementText)
					slottedCount++
				}
			}
			// Apply simple template mappings (components) - replacement text already has JSON values baked in by LoaderPreProcess
			for _, mapping := range tmpl.ReplacementMappings {
				if mapping.Type == model.SimpleTemplateType && strings.Contains(result, mapping.OriginalText) {
					replacementText := e.ApplyAppViewLogicToReplacement(mapping.OriginalText, mapping.ReplacementText, preprocessedTemplates, appView)
					result = strings.ReplaceAll(result, mapping.OriginalText, replacementText)
					simpleCount++
				}
			}
		}

		Logger.Debug(fmt.Sprintf("Pass %d applied: %d main JSON placeholders, %d slotted, %d simple", currentPass, jsonPlaceholderCount, slottedCount, simpleCount), "EnginePreProcess")

		if result == previous {
			break
		}
	}

	// All JSON replacements are handled in LoaderPreProcess during replacement mapping creation
	// The engine only does simple string replacements using pre-prepared mappings
	Logger.Debug(fmt.Sprintf("Replacement complete after %d passes, final size: %d", currentPass, len(result)), "EnginePreProcess")

	return result
}

// ApplyAppViewLogicToReplacement applies AppView fallback logic to template replacement text
func (e *EnginePreProcess) ApplyAppViewLogicToReplacement(originalText, replacementText string, preprocessedTemplates map[string]model.PreprocessedTemplate, appView string) string {
	// If no appView context, use the default replacement text (which already has JSON values baked in)
	if appView == "" {
		return replacementText
	}

	placeholderName := extractPlaceholderName(originalText)
	if placeholderName == "" {
		return replacementText
	}

	var appSite string
	for key := range preprocessedTemplates {
		parts := strings.Split(key, "_")
		if len(parts) > 0 {
			appSite = parts[0]
			break
		}
	}

	// Use GetTemplate to find the AppView-specific template variant
	appViewTemplate := e.GetTemplate(appSite, placeholderName, preprocessedTemplates, appView, e.AppViewPrefix, true)

	// If no AppView-specific template found, use the default replacement text
	if appViewTemplate == nil {
		return replacementText
	}

	// Return the AppView-specific template's original content (which already has JSON baked in by LoaderPreProcess)
	return appViewTemplate.OriginalContent
}

// extractPlaceholderName extracts placeholder name from {{PlaceholderName}} format
func extractPlaceholderName(originalText string) string {
	if !strings.HasPrefix(originalText, "{{") || !strings.HasSuffix(originalText, "}}") {
		return ""
	}
	return strings.TrimSpace(originalText[2 : len(originalText)-2])
}

package template_engine

import (
	"assembler/template_common"
	"assembler/template_model"
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
func (e *EnginePreProcess) MergeTemplates(appSite, appFile, appView string, preprocessedTemplates map[string]template_model.PreprocessedTemplate, enableJsonProcessing bool) string {
	if len(preprocessedTemplates) == 0 {
		return ""
	}
	mainPreprocessed := e.GetTemplate(appSite, appFile, preprocessedTemplates, appView, e.AppViewPrefix, true)
	if mainPreprocessed == nil {
		return ""
	}
	contentHtml := mainPreprocessed.OriginalContent

	// Apply ALL replacement mappings from ALL templates (TemplateLoader did all the processing)
	contentHtml = e.ApplyTemplateReplacements(contentHtml, preprocessedTemplates, enableJsonProcessing, appView)

	return contentHtml
}

// GetTemplate retrieves a preprocessed template from the dictionary with AppView fallback
func (e *EnginePreProcess) GetTemplate(appSite, templateName string, preprocessedTemplates map[string]template_model.PreprocessedTemplate, appView, appViewPrefix string, useAppViewFallback bool) *template_model.PreprocessedTemplate {
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
			appKey := template_common.ReplaceCaseInsensitive(templateName, viewPrefix, appView)
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
func (e *EnginePreProcess) ApplyTemplateReplacements(content string, preprocessedTemplates map[string]template_model.PreprocessedTemplate, enableJsonProcessing bool, appView string) string {
	result := content
	maxPasses := 10

	for i := 0; i < maxPasses; i++ {
		previous := result

		for _, tmpl := range preprocessedTemplates {
			// Slotted templates first
			for _, mapping := range tmpl.ReplacementMappings {
				if mapping.Type == template_model.SlottedTemplateType && strings.Contains(result, mapping.OriginalText) {
					result = strings.ReplaceAll(result, mapping.OriginalText, mapping.ReplacementText)
				}
			}
			// Simple templates with AppView logic
			for _, mapping := range tmpl.ReplacementMappings {
				if mapping.Type == template_model.SimpleTemplateType && strings.Contains(result, mapping.OriginalText) {
					replacementText := e.ApplyAppViewLogicToReplacement(mapping.OriginalText, mapping.ReplacementText, preprocessedTemplates, appView)
					result = strings.ReplaceAll(result, mapping.OriginalText, replacementText)
				}
			}
			// JSON placeholders
			if enableJsonProcessing {
				for _, mapping := range tmpl.ReplacementMappings {
					if mapping.Type == template_model.JsonPlaceholderType && strings.Contains(result, mapping.OriginalText) {
						result = strings.ReplaceAll(result, mapping.OriginalText, mapping.ReplacementText)
					}
				}
				for _, placeholder := range tmpl.JsonPlaceholders {
					result = replaceAllCaseInsensitivePreProcess(result, placeholder.Placeholder, placeholder.Value)
				}
			}
		}

		if result == previous {
			break
		}
	}

	return result
}

// replaceCaseInsensitive replaces the first occurrence of 'from' in 'text' (case-insensitive) with 'to'
func replaceCaseInsensitive(text, from, to string) string {
	textLower := strings.ToLower(text)
	fromLower := strings.ToLower(from)
	if idx := strings.Index(textLower, fromLower); idx != -1 {
		end := idx + len(from)
		return text[:idx] + to + text[end:]
	}
	return text
}

// replaceAllCaseInsensitivePreProcess replaces all occurrences of a substring with another, ignoring case
func replaceAllCaseInsensitivePreProcess(input, search, replacement string) string {
	if search == "" {
		return input
	}
	lowerSearch := strings.ToLower(search)
	var builder strings.Builder
	lastIndex := 0
	for {
		index := strings.Index(strings.ToLower(input[lastIndex:]), lowerSearch)
		if index == -1 {
			builder.WriteString(input[lastIndex:])
			break
		}
		index += lastIndex
		builder.WriteString(input[lastIndex:index])
		builder.WriteString(replacement)
		lastIndex = index + len(search)
	}
	return builder.String()
}

// ApplyAppViewLogicToReplacement applies AppView fallback logic to template replacement text
func (e *EnginePreProcess) ApplyAppViewLogicToReplacement(originalText, replacementText string, preprocessedTemplates map[string]template_model.PreprocessedTemplate, appView string) string {
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

	template := e.GetTemplate(appSite, placeholderName, preprocessedTemplates, appView, e.AppViewPrefix, true)
	if template != nil {
		return template.OriginalContent
	}

	return replacementText
}

// extractPlaceholderName extracts placeholder name from {{PlaceholderName}} format
func extractPlaceholderName(originalText string) string {
	if !strings.HasPrefix(originalText, "{{") || !strings.HasSuffix(originalText, "}}") {
		return ""
	}
	return strings.TrimSpace(originalText[2 : len(originalText)-2])
}

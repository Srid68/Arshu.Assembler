package engine_preprocessjson

import (
	Logger "arshu/common"
	interfaces "assembler/interface"
	"assembler/model"
	"fmt"
)

type EnginePreProcessJson struct {
	AppViewPrefix string
}

func NewEnginePreProcessJson(prefix string) *EnginePreProcessJson {
	return &EnginePreProcessJson{AppViewPrefix: prefix}
}

func (e *EnginePreProcessJson) MergeTemplates(appSite, appFile, appView string, loader interfaces.ILoaderJson, enableJsonProcessing bool) string {
	Logger.Debug(fmt.Sprintf("MergeTemplates called: appSite=%s, appFile=%s, appView=%s, enableJson=%t", appSite, appFile, appView, enableJsonProcessing), "EnginePreProcessJson")

	if loader == nil {
		Logger.Warn("No loader provided", "EnginePreProcessJson")
		return ""
	}

	// Use ILoaderJson to retrieve the main template
	result := loader.GetTemplateHtml(appSite, appFile, appView, e.AppViewPrefix)
	if result == nil {
		Logger.Warn(fmt.Sprintf("Main template not found for appSite=%s, appFile=%s", appSite, appFile), "EnginePreProcessJson")
		return ""
	}

	mainPreprocessed, ok := result.(*model.PreprocessedTemplate)
	if !ok || mainPreprocessed == nil {
		Logger.Warn(fmt.Sprintf("Main template not found or invalid type for appSite=%s, appFile=%s", appSite, appFile), "EnginePreProcessJson")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Main template found, original size: %d", len(mainPreprocessed.OriginalContent)), "EnginePreProcessJson")

	// Start with original content
	contentHtml := mainPreprocessed.OriginalContent

	// Merge JSON into main template first using loader's centralized method
	if enableJsonProcessing {
		contentHtml = loader.MergeHtmlWithJson(contentHtml, appSite, appFile)
		Logger.Debug(fmt.Sprintf("After main template JSON merge: %d chars", len(contentHtml)), "EnginePreProcessJson")
	}

	// Apply ALL replacement mappings from ALL templates using loader's method
	contentHtml = loader.ApplyAllReplacementMappings(contentHtml, appSite, mainPreprocessed, appView, e.AppViewPrefix, enableJsonProcessing)

	Logger.Debug(fmt.Sprintf("MergeTemplates complete: output size=%d", len(contentHtml)), "EnginePreProcessJson")
	return contentHtml
}
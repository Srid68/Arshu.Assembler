package engine_preprocess

import (
	Logger "arshu/common"
	interfaces "assembler/interface"
	"fmt"
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
// This method only does merging using preprocessed data structures - no loading or parsing
func (e *EnginePreProcess) MergeTemplates(appSite, appFile, appView string, loader interfaces.ILoaderPreProcess, enableJsonProcessing bool) string {
	Logger.Debug(fmt.Sprintf("MergeTemplates called: appSite=%s, appFile=%s, appView=%s, enableJson=%t", appSite, appFile, appView, enableJsonProcessing), "EnginePreProcess")

	if loader == nil {
		Logger.Warn("No loader provided", "EnginePreProcess")
		return ""
	}

	// Get main template using ILoaderPreProcess (includes AppView fallback and SearchAppSites logic)
	mainPreprocessed := loader.GetTemplateHtml(appSite, appFile, appView, e.AppViewPrefix)
	if mainPreprocessed == nil {
		Logger.Warn(fmt.Sprintf("Main template not found for appSite=%s, appFile=%s", appSite, appFile), "EnginePreProcess")
		return ""
	}

	Logger.Debug(fmt.Sprintf("Main template found, original size: %d", len(mainPreprocessed.OriginalContent)), "EnginePreProcess")

	// Start with original content
	contentHtml := mainPreprocessed.OriginalContent

	// Merge JSON into main template first using loader's centralized method (for consistency with other engines)
	if enableJsonProcessing {
		contentHtml = loader.MergeHtmlWithJson(contentHtml, appSite, appFile)
		Logger.Debug(fmt.Sprintf("After main template JSON merge: %d chars", len(contentHtml)), "EnginePreProcess")
	}

	// Apply ALL replacement mappings from ALL templates using loader's method
	contentHtml = loader.ApplyAllReplacementMappings(contentHtml, appSite, mainPreprocessed, appView, e.AppViewPrefix, enableJsonProcessing)

	Logger.Debug(fmt.Sprintf("MergeTemplates complete: output size=%d", len(contentHtml)), "EnginePreProcess")

	return contentHtml
}

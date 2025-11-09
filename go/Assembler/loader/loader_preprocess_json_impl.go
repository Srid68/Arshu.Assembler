package loader

import (
	Logger "arshu/common"
	"assembler/model"
	"fmt"
	"strings"
)

type LoaderPreprocessJsonImpl struct {
	*model.PreprocessedSiteTemplates
	searchAppSites string
}

// NewLoaderPreprocessJsonImpl creates a new LoaderPreprocessJsonImpl with searchAppSites support
func NewLoaderPreprocessJsonImpl(siteTemplates *model.PreprocessedSiteTemplates, searchAppSites string) *LoaderPreprocessJsonImpl {
	return &LoaderPreprocessJsonImpl{
		PreprocessedSiteTemplates: siteTemplates,
		searchAppSites:            searchAppSites,
	}
}

func (l *LoaderPreprocessJsonImpl) AllTemplates() map[string]*model.PreprocessedTemplate {
	templates := make(map[string]*model.PreprocessedTemplate)
	for key, t := range l.Templates {
		// Create a new variable to avoid taking the address of a loop variable
		temp := t
		templates[key] = &temp
	}
	return templates
}

func (l *LoaderPreprocessJsonImpl) GetTemplateHtml(appSite, appFile, appView, appViewPrefix string) *model.PreprocessedTemplate {
	if appView != "" && appViewPrefix != "" {
		appKey := strings.Replace(appFile, appViewPrefix, appView, 1)
		key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(appKey))
		if t, ok := l.Templates[key]; ok {
			return &t
		}
	}

	// Try primary template key
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(appFile))
	if t, ok := l.Templates[key]; ok {
		return &t
	}

	// Search in SearchAppSites as fallback
	if l.searchAppSites != "" {
		searchAppSitesArray := strings.Split(l.searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}

			searchKey := fmt.Sprintf("%s_%s", strings.ToLower(searchAppSite), strings.ToLower(appFile))
			if t, ok := l.Templates[searchKey]; ok {
				Logger.Debug(fmt.Sprintf("Template '%s' not found in '%s', using fallback from '%s'", appFile, appSite, searchAppSite), "LoaderPreprocessJsonImpl")
				return &t
			}
		}
	}

	return nil
}

func (l *LoaderPreprocessJsonImpl) GetTemplateJson(appSite, appFile string) map[string]interface{} {
	// Try primary template key
	key := fmt.Sprintf("%s_%s", strings.ToLower(appSite), strings.ToLower(appFile))
	if t, ok := l.Templates[key]; ok && t.JsonData != nil {
		return *t.JsonData
	}

	// Search in SearchAppSites as fallback
	if l.searchAppSites != "" {
		searchAppSitesArray := strings.Split(l.searchAppSites, ",")
		for _, searchAppSite := range searchAppSitesArray {
			searchAppSite = strings.TrimSpace(searchAppSite)
			if searchAppSite == "" {
				continue
			}

			searchKey := fmt.Sprintf("%s_%s", strings.ToLower(searchAppSite), strings.ToLower(appFile))
			if t, ok := l.Templates[searchKey]; ok && t.JsonData != nil {
				Logger.Debug(fmt.Sprintf("JSON for '%s' not found in '%s', using fallback from '%s'", appFile, appSite, searchAppSite), "LoaderPreprocessJsonImpl")
				return *t.JsonData
			}
		}
	}

	return nil
}

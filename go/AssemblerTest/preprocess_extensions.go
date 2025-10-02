package main

import (
	"assembler/template_model"
	"encoding/json"
)

// ToJson serializes PreprocessedSiteTemplates to JSON
func ToJson(siteTemplates *template_model.PreprocessedSiteTemplates, indented bool) (string, error) {
	var b []byte
	var err error
	if indented {
		b, err = json.MarshalIndent(siteTemplates, "", "  ")
	} else {
		b, err = json.Marshal(siteTemplates)
	}
	return string(b), err
}

// CreateSummary returns a summary object for PreprocessedSiteTemplates
func CreateSummary(siteTemplates *template_model.PreprocessedSiteTemplates) *PreprocessedSummary {
	templatesRequiringProcessing := 0
	templatesWithJsonData := 0
	templatesWithPlaceholders := 0
	totalTemplates := len(siteTemplates.Templates)
	for _, t := range siteTemplates.Templates {
		if t.RequiresProcessing {
			templatesRequiringProcessing++
		}
		if t.HasJsonData {
			templatesWithJsonData++
		}
		if t.HasPlaceholders {
			templatesWithPlaceholders++
		}
	}
	return &PreprocessedSummary{
		SiteName:                     siteTemplates.SiteName,
		TemplatesRequiringProcessing: templatesRequiringProcessing,
		TemplatesWithJsonData:        templatesWithJsonData,
		TemplatesWithPlaceholders:    templatesWithPlaceholders,
		TotalTemplates:               totalTemplates,
	}
}

// ToSummaryJson serializes the summary to JSON
func ToSummaryJson(siteTemplates *template_model.PreprocessedSiteTemplates, indented bool) (string, error) {
	summary := CreateSummary(siteTemplates)
	var b []byte
	var err error
	if indented {
		b, err = json.MarshalIndent(summary, "", "  ")
	} else {
		b, err = json.Marshal(summary)
	}
	return string(b), err
}

// PreprocessedSummary matches the C# structure
// You may move this to template_model if you want to share
// but for test project, keep here for now

// ...existing code...
type PreprocessedSummary struct {
	SiteName                     string `json:"siteName"`
	TemplatesRequiringProcessing int    `json:"templatesRequiringProcessing"`
	TemplatesWithJsonData        int    `json:"templatesWithJsonData"`
	TemplatesWithPlaceholders    int    `json:"templatesWithPlaceholders"`
	TotalTemplates               int    `json:"totalTemplates"`
}

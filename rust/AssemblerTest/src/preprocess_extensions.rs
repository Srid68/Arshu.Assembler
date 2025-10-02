// PreprocessExtensions: Methods for summary and JSON conversion
use assembler::template_model::model_preprocess::{PreprocessedSiteTemplates, PreprocessedSummary};

pub struct PreprocessJsonConverter;

impl PreprocessJsonConverter {
    /// Serializes a PreprocessedSiteTemplates to JSON format matching C# structure
    pub fn serialize_preprocessed_templates(templates: &PreprocessedSiteTemplates, indented: bool) -> String {
        if indented {
            Self::to_pretty_json(templates)
        } else {
            Self::to_json(templates)
        }
    }

    /// Serializes a PreprocessedSummary to JSON format matching C# structure
    pub fn serialize_preprocessed_summary(summary: &PreprocessedSummary, indented: bool) -> String {
        if indented {
            Self::to_pretty_json(summary)
        } else {
            Self::to_json(summary)
        }
    }

    fn to_json<T: serde::Serialize>(value: &T) -> String {
        serde_json::to_string(value).unwrap_or_default()
    }

    fn to_pretty_json<T: serde::Serialize>(value: &T) -> String {
        serde_json::to_string_pretty(value).unwrap_or_default()
    }
}

pub trait PreprocessExtensions {
    fn create_summary(&self) -> PreprocessedSummary;
    fn to_json(&self, indented: bool) -> String;
    fn to_summary_json(&self, indented: bool) -> String;
}

impl PreprocessExtensions for PreprocessedSiteTemplates {
    fn create_summary(&self) -> PreprocessedSummary {
        let templates_requiring_processing = self.templates.values()
            .filter(|t| t.requires_processing())
            .count();
        let templates_with_json_data = self.templates.values()
            .filter(|t| t.has_json_data())
            .count();
        let templates_with_placeholders = self.templates.values()
            .filter(|t| t.has_placeholders())
            .count();
        PreprocessedSummary {
            site_name: self.site_name.clone(),
            templates_requiring_processing,
            templates_with_json_data,
            templates_with_placeholders,
            total_templates: self.templates.len(),
        }
    }

    fn to_json(&self, indented: bool) -> String {
        PreprocessJsonConverter::serialize_preprocessed_templates(self, indented)
    }

    fn to_summary_json(&self, indented: bool) -> String {
        let summary = self.create_summary();
        PreprocessJsonConverter::serialize_preprocessed_summary(&summary, indented)
    }
}
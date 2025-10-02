// TemplateModel: Data structure for parsed template
// Should represent the template's structure and metadata

use serde::{Deserialize, Serialize};
use crate::app::JsonObject;

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct PreprocessedSiteTemplates {
    #[serde(rename = "siteName")]
    pub site_name: String,
    pub templates: std::collections::HashMap<String, PreprocessedTemplate>,
    #[serde(rename = "rawTemplates")]
    pub raw_templates: std::collections::HashMap<String, String>,
    #[serde(rename = "templateKeys")]
    pub template_keys: std::collections::HashSet<String>,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct PreprocessedTemplate {
    #[serde(rename = "originalContent")]
    pub original_content: String,
    pub placeholders: Vec<TemplatePlaceholder>,
    #[serde(rename = "slottedTemplates")]
    pub slotted_templates: Vec<SlottedTemplate>,
    #[serde(rename = "jsonData")]
    pub json_data: Option<JsonObject>,
    #[serde(rename = "jsonPlaceholders")]
    pub json_placeholders: Vec<JsonPlaceholder>,
    #[serde(rename = "replacementMappings")]
    pub replacement_mappings: Vec<ReplacementMapping>,
    
    // Helper properties included in JSON serialization
    #[serde(rename = "hasPlaceholders")]
    pub has_placeholders_flag: bool,
    #[serde(rename = "hasSlottedTemplates")]
    pub has_slotted_templates_flag: bool,
    #[serde(rename = "hasJsonData")]
    pub has_json_data_flag: bool,
    #[serde(rename = "hasJsonPlaceholders")]
    pub has_json_placeholders_flag: bool,
    #[serde(rename = "hasReplacementMappings")]
    pub has_replacement_mappings_flag: bool,
    #[serde(rename = "requiresProcessing")]
    pub requires_processing_flag: bool,
}

impl PreprocessedTemplate {
    // Helper properties to check template state - matching C# structure
    pub fn has_placeholders(&self) -> bool {
        !self.placeholders.is_empty()
    }
    
    pub fn has_slotted_templates(&self) -> bool {
        !self.slotted_templates.is_empty()
    }
    
    pub fn has_json_data(&self) -> bool {
        self.json_data.is_some() && !self.json_data.as_ref().unwrap().is_empty()
    }
    
    pub fn has_json_placeholders(&self) -> bool {
        !self.json_placeholders.is_empty()
    }
    
    pub fn has_replacement_mappings(&self) -> bool {
        !self.replacement_mappings.is_empty()
    }
    
    pub fn requires_processing(&self) -> bool {
        self.has_placeholders() || self.has_slotted_templates() || self.has_json_data() || self.has_json_placeholders() || self.has_replacement_mappings()
    }
    
    // Helper method to update convenience flags
    pub fn update_flags(&mut self) {
        self.has_placeholders_flag = self.has_placeholders();
        self.has_slotted_templates_flag = self.has_slotted_templates();
        self.has_json_data_flag = self.has_json_data();
        self.has_json_placeholders_flag = self.has_json_placeholders();
        self.has_replacement_mappings_flag = self.has_replacement_mappings();
        self.requires_processing_flag = self.requires_processing();
    }
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct JsonPlaceholder {
    pub key: String,
    pub placeholder: String,
    pub value: String,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct ReplacementMapping {
    #[serde(rename = "startIndex")]
    pub start_index: usize,
    #[serde(rename = "endIndex")]
    pub end_index: usize,
    #[serde(rename = "originalText")]
    pub original_text: String,
    #[serde(rename = "replacementText")]
    pub replacement_text: String,
    #[serde(rename = "type")]
    pub r#type: ReplacementType,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub enum ReplacementType {
    JsonPlaceholder,
    SimpleTemplate,
    SlottedTemplate,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct TemplatePlaceholder {
    pub name: String,
    #[serde(rename = "startIndex")]
    pub start_index: usize,
    #[serde(rename = "endIndex")]
    pub end_index: usize,
    #[serde(rename = "fullMatch")]
    pub full_match: String,
    #[serde(rename = "templateKey")]
    pub template_key: String,
    #[serde(rename = "jsonData")]
    pub json_data: Option<JsonObject>,
    #[serde(rename = "nestedPlaceholders")]
    pub nested_placeholders: Vec<TemplatePlaceholder>,
    #[serde(rename = "nestedSlots")]
    pub nested_slots: Vec<SlotPlaceholder>,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct SlottedTemplate {
    pub name: String,
    #[serde(rename = "startIndex")]
    pub start_index: usize,
    #[serde(rename = "endIndex")]
    pub end_index: usize,
    #[serde(rename = "fullMatch")]
    pub full_match: String,
    #[serde(rename = "innerContent")]
    pub inner_content: String,
    pub slots: Vec<SlotPlaceholder>,
    #[serde(rename = "templateKey")]
    pub template_key: String,
    #[serde(rename = "jsonData")]
    pub json_data: Option<JsonObject>,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct SlotPlaceholder {
    #[serde(rename = "nestedSlots")]
    pub nested_slots: Vec<SlotPlaceholder>,
    pub number: String,
    #[serde(rename = "startIndex")]
    pub start_index: usize,
    #[serde(rename = "endIndex")]
    pub end_index: usize,
    pub content: String,
    #[serde(rename = "slotKey")]
    pub slot_key: String,
    #[serde(rename = "openTag")]
    pub open_tag: String,
    #[serde(rename = "closeTag")]
    pub close_tag: String,
    #[serde(rename = "nestedPlaceholders")]
    pub nested_placeholders: Vec<TemplatePlaceholder>,
    #[serde(rename = "nestedSlottedTemplates")]
    pub nested_slotted_templates: Vec<SlottedTemplate>,
}

impl SlotPlaceholder {
    // Helper properties matching C# structure
    pub fn has_nested_placeholders(&self) -> bool {
        !self.nested_placeholders.is_empty()
    }
    
    pub fn has_nested_slotted_templates(&self) -> bool {
        !self.nested_slotted_templates.is_empty()
    }
    
    pub fn requires_nested_processing(&self) -> bool {
        self.has_nested_placeholders() || self.has_nested_slotted_templates()
    }
}

impl PreprocessedSiteTemplates {
}

#[derive(Debug, Serialize, Clone)]
pub struct PreprocessedSummary {
    #[serde(rename = "siteName")]
    pub site_name: String,
    #[serde(rename = "templatesRequiringProcessing")]
    pub templates_requiring_processing: usize,
    #[serde(rename = "templatesWithJsonData")]
    pub templates_with_json_data: usize,
    #[serde(rename = "templatesWithPlaceholders")]
    pub templates_with_placeholders: usize,
    #[serde(rename = "totalTemplates")]
    pub total_templates: usize,
}

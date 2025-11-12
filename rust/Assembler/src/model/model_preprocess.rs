// TemplateModel: Data structure for parsed template
// Should represent the template's structure and metadata

use crate::app::JsonObject;

#[derive(Debug, Clone)]
pub struct PreprocessedSiteTemplates {
    pub site_name: String,
    pub templates: std::collections::HashMap<String, PreprocessedTemplate>,
    pub raw_templates: std::collections::HashMap<String, String>,
    pub template_keys: std::collections::HashSet<String>,
}

#[derive(Debug, Clone)]
pub struct PreprocessedTemplate {
    pub original_content: String,
    pub placeholders: Vec<TemplatePlaceholder>,
    pub slotted_templates: Vec<SlottedTemplate>,
    pub json_data: Option<JsonObject>,
    pub json_placeholders: Vec<JsonPlaceholder>,
    pub replacement_mappings: Vec<ReplacementMapping>,

    // Helper properties included in JSON serialization
    pub has_placeholders_flag: bool,
    pub has_slotted_templates_flag: bool,
    pub has_json_data_flag: bool,
    pub has_json_placeholders_flag: bool,
    pub has_replacement_mappings_flag: bool,
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
        self.has_placeholders()
            || self.has_slotted_templates()
            || self.has_json_data()
            || self.has_json_placeholders()
            || self.has_replacement_mappings()
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

#[derive(Debug, Clone)]
pub struct JsonPlaceholder {
    pub key: String,
    pub placeholder: String,
    pub value: String,
}

#[derive(Debug, Clone)]
pub struct ReplacementMapping {
    pub start_index: usize,
    pub end_index: usize,
    pub original_text: String,
    pub replacement_text: String,
    pub r#type: ReplacementType,
    /// Name of the target template this mapping references (for engine to retrieve JSON)
    /// Format: lowercase template name (e.g., "header", "footer")
    pub target_template_name: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum ReplacementType {
    JsonPlaceholder,
    SimpleTemplate,
    SlottedTemplate,
}

#[derive(Debug, Clone)]
pub struct TemplatePlaceholder {
    pub name: String,
    pub start_index: usize,
    pub end_index: usize,
    pub full_match: String,
    pub template_key: String,
    pub json_data: Option<JsonObject>,
    pub nested_placeholders: Vec<TemplatePlaceholder>,
    pub nested_slots: Vec<SlotPlaceholder>,
}

#[derive(Debug, Clone)]
pub struct SlottedTemplate {
    pub name: String,
    pub start_index: usize,
    pub end_index: usize,
    pub full_match: String,
    pub inner_content: String,
    pub slots: Vec<SlotPlaceholder>,
    pub template_key: String,
    pub json_data: Option<JsonObject>,
}

#[derive(Debug, Clone)]
pub struct SlotPlaceholder {
    pub nested_slots: Vec<SlotPlaceholder>,
    pub number: String,
    pub start_index: usize,
    pub end_index: usize,
    pub content: String,
    pub slot_key: String,
    pub open_tag: String,
    pub close_tag: String,
    pub nested_placeholders: Vec<TemplatePlaceholder>,
    pub nested_slotted_templates: Vec<SlottedTemplate>,
    pub has_nested_placeholders: bool,
    pub has_nested_slotted_templates: bool,
    pub requires_nested_processing: bool,
}

impl PreprocessedSiteTemplates {}

#[derive(Debug, Clone)]
pub struct PreprocessedSummary {
    pub site_name: String,
    pub templates_requiring_processing: usize,
    pub templates_with_json_data: usize,
    pub templates_with_placeholders: usize,
    pub total_templates: usize,
}

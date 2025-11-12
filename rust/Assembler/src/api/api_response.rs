use crate::model::model_preprocess::{
    JsonPlaceholder, PreprocessedSiteTemplates, PreprocessedSummary, PreprocessedTemplate,
    ReplacementMapping, SlotPlaceholder, SlottedTemplate, TemplatePlaceholder,
};

#[derive(Clone, Debug)]
pub struct TemplateData {
    pub html: String,
    pub json: Option<String>,
}

#[derive(Clone, Debug)]
pub struct PreProcessTemplateMetadata {
    pub original_content: String,
    pub placeholders: Vec<TemplatePlaceholder>,
    pub slotted_templates: Vec<SlottedTemplate>,
    pub json_data: Option<String>,
    pub json_placeholders: Vec<JsonPlaceholder>,
    pub replacement_mappings: Vec<ReplacementMapping>,
    pub has_placeholders: bool,
    pub has_slotted_templates: bool,
    pub has_json_data: bool,
    pub has_json_placeholders: bool,
    pub has_replacement_mappings: bool,
    pub requires_processing: bool,
}

#[derive(Clone, Debug)]
pub struct ApiResponse {
    pub app_site: String,
    pub app_file: Option<String>,
    pub app_view: Option<String>,
    pub server_time_ms: f64,
    pub html: String,
    pub engine_time_ms: f64,
}

impl ApiResponse {
    pub fn serialize_to_json(&self, indented: bool) -> String {
        if indented {
            self.serialize_to_json_pretty()
        } else {
            self.serialize_to_json_compact()
        }
    }

    fn serialize_to_json_compact(&self) -> String {
        let mut sb = String::new();
        sb.push('{');
        sb.push_str(&format!(
            "\"AppSite\":\"{}\"",
            Self::escape_json_string(&self.app_site)
        ));
        if let Some(ref app_file) = self.app_file {
            sb.push_str(&format!(
                ",\"AppFile\":\"{}\"",
                Self::escape_json_string(app_file)
            ));
        }
        if let Some(ref app_view) = self.app_view {
            sb.push_str(&format!(
                ",\"AppView\":\"{}\"",
                Self::escape_json_string(app_view)
            ));
        }
        sb.push_str(&format!(",\"ServerTimeMs\":{}", self.server_time_ms));
        sb.push_str(&format!(
            ",\"Html\":\"{}\"",
            Self::escape_html_string(&self.html)
        ));
        sb.push_str(&format!(",\"EngineTimeMs\":{}", self.engine_time_ms));
        sb.push('}');
        sb
    }

    fn serialize_to_json_pretty(&self) -> String {
        let mut sb = String::new();
        sb.push_str("{\n  ");
        sb.push_str(&format!(
            "\"AppSite\": \"{}\"",
            Self::escape_json_string(&self.app_site)
        ));
        if let Some(ref app_file) = self.app_file {
            sb.push_str(",\n  ");
            sb.push_str(&format!(
                "\"AppFile\": \"{}\"",
                Self::escape_json_string(app_file)
            ));
        }
        if let Some(ref app_view) = self.app_view {
            sb.push_str(",\n  ");
            sb.push_str(&format!(
                "\"AppView\": \"{}\"",
                Self::escape_json_string(app_view)
            ));
        }
        sb.push_str(",\n  ");
        sb.push_str(&format!("\"ServerTimeMs\": {}", self.server_time_ms));
        sb.push_str(",\n  ");
        sb.push_str(&format!(
            "\"Html\": \"{}\"",
            Self::escape_html_string(&self.html)
        ));
        sb.push_str(",\n  ");
        sb.push_str(&format!("\"EngineTimeMs\": {}", self.engine_time_ms));
        sb.push_str("\n}");
        sb
    }


    fn escape_html_string(input: &str) -> String {
        input
            .replace("\\", "\\\\")
            .replace('"', "\\\"")
            //.replace('"', "\\u0022")
            .replace('\n', "\\n")
            .replace('\r', "\\r")
            .replace('\t', "\\t")
    }

    fn escape_json_string(input: &str) -> String {
        input
            .replace("\\", "\\\\")
            .replace('"', "\\\"")
            //.replace('"', "\\u0022")
            .replace('\n', "\\n")
            .replace('\r', "\\r")
            .replace('\t', "\\t")
            .replace('<', "\\u003C")
            .replace('>', "\\u003E")
            .replace('&', "\\u0026")
            .replace('\'', "\\u0027")
            .replace('+', "\\u002B")
    }


    fn serialize_vec<T, F>(vec: &Vec<T>, value_serializer: F) -> String
    where
        F: Fn(&T) -> String,
    {
        let mut sb = String::new();
        sb.push('[');
        let mut first = true;
        for item in vec {
            if !first {
                sb.push(',');
            }
            sb.push_str(&value_serializer(item));
            first = false;
        }
        sb.push(']');
        sb
    }


    fn serialize_placeholder(ph: &TemplatePlaceholder) -> String {
        let mut sb = String::new();
        sb.push('{');
        sb.push_str(&format!(
            "\"Name\":\"{}\",",
            Self::escape_json_string(&ph.name)
        ));
        sb.push_str(&format!("\"StartIndex\":{},", ph.start_index));
        sb.push_str(&format!("\"EndIndex\":{},", ph.end_index));
        sb.push_str(&format!(
            "\"FullMatch\":\"{}\",",
            Self::escape_json_string(&ph.full_match)
        ));
        sb.push_str(&format!(
            "\"TemplateKey\":\"{}\",",
            Self::escape_json_string(&ph.template_key)
        ));
        sb.push_str("\"JsonData\":");
        if let Some(ref json_data) = ph.json_data {
            sb.push_str(&Self::escape_json_string(&format!("{:?}", json_data)));
        } else {
            sb.push_str("null");
        }
        sb.push_str(",\"NestedPlaceholders\":");
        sb.push_str(&Self::serialize_vec(
            &ph.nested_placeholders,
            Self::serialize_placeholder,
        ));
        sb.push_str(",\"NestedSlots\":");
        sb.push_str(&Self::serialize_vec(
            &ph.nested_slots,
            Self::serialize_slot_placeholder,
        ));
        sb.push('}');
        sb
    }

    fn serialize_slot_placeholder(
        slot: &crate::model::model_preprocess::SlotPlaceholder,
    ) -> String {
        let mut sb = String::new();
        sb.push('{');
        sb.push_str(&format!(
            "\"Number\":\"{}\",",
            Self::escape_json_string(&slot.number)
        ));
        sb.push_str(&format!("\"StartIndex\":{},", slot.start_index));
        sb.push_str(&format!("\"EndIndex\":{},", slot.end_index));
        sb.push_str(&format!(
            "\"Content\":\"{}\",",
            Self::escape_json_string(&slot.content)
        ));
        sb.push_str(&format!(
            "\"SlotKey\":\"{}\",",
            Self::escape_json_string(&slot.slot_key)
        ));
        sb.push_str(&format!(
            "\"NestedSlots\":{}",
            Self::serialize_vec(&slot.nested_slots, Self::serialize_slot_placeholder)
        ));
        sb.push('}');
        sb
    }

    fn serialize_slotted_template(st: &SlottedTemplate) -> String {
        let mut sb = String::new();
        sb.push('{');
        sb.push_str(&format!(
            "\"Name\":\"{}\",",
            Self::escape_json_string(&st.name)
        ));
        sb.push_str(&format!("\"StartIndex\":{},", st.start_index));
        sb.push_str(&format!("\"EndIndex\":{},", st.end_index));
        sb.push_str(&format!(
            "\"FullMatch\":\"{}\",",
            Self::escape_json_string(&st.full_match)
        ));
        sb.push_str(&format!(
            "\"TemplateKey\":\"{}\",",
            Self::escape_json_string(&st.template_key)
        ));
        sb.push_str(&format!(
            "\"Slots\":{}",
            Self::serialize_vec(&st.slots, Self::serialize_slot_placeholder)
        ));
        sb.push('}');
        sb
    }

    fn serialize_json_placeholder(jp: &JsonPlaceholder) -> String {
        let mut sb = String::new();
        sb.push('{');
        sb.push_str(&format!(
            "\"Key\":\"{}\",",
            Self::escape_json_string(&jp.key)
        ));
        sb.push_str(&format!(
            "\"Placeholder\":\"{}\",",
            Self::escape_json_string(&jp.placeholder)
        ));
        sb.push_str(&format!(
            "\"Value\":\"{}\"",
            Self::escape_json_string(&jp.value)
        ));
        sb.push('}');
        sb
    }

    fn serialize_replacement_mapping(rm: &ReplacementMapping) -> String {
        let mut sb = String::new();
        sb.push('{');
        sb.push_str(&format!("\"StartIndex\":{},", rm.start_index));
        sb.push_str(&format!("\"EndIndex\":{},", rm.end_index));
        sb.push_str(&format!(
            "\"OriginalText\":\"{}\",",
            Self::escape_html_string(&rm.original_text)
        ));
        sb.push_str(&format!(
            "\"ReplacementText\":\"{}\",",
            Self::escape_html_string(&rm.replacement_text)
        ));
        sb.push_str(&format!("\"Type\":{}", rm.r#type as u32));
        sb.push('}');
        sb
    }

    /// Serializes PreprocessedSiteTemplates to JSON (matching C# output format)
    pub fn serialize_preprocessed_site_templates(
        templates: &PreprocessedSiteTemplates,
        indented: bool,
    ) -> String {
        if indented {
            Self::serialize_preprocessed_site_templates_pretty(templates)
        } else {
            Self::serialize_preprocessed_site_templates_compact(templates)
        }
    }

    fn serialize_preprocessed_site_templates_compact(
        templates: &PreprocessedSiteTemplates,
    ) -> String {
        let mut sb = String::new();
        sb.push('{');
        sb.push_str(&format!(
            "\"siteName\":\"{}\"",
            Self::escape_json_string(&templates.site_name)
        ));

        // Serialize templates
        sb.push_str(",\"templates\":{");
        let mut first = true;
        for (key, template) in &templates.templates {
            if !first {
                sb.push(',');
            }
            sb.push_str(&format!("\"{}\":", Self::escape_json_string(key)));
            sb.push_str(&Self::serialize_preprocessed_template(template));
            first = false;
        }
        sb.push('}');

        // Serialize rawTemplates
        sb.push_str(",\"rawTemplates\":{");
        first = true;
        for (key, content) in &templates.raw_templates {
            if !first {
                sb.push(',');
            }
            sb.push_str(&format!(
                "\"{}\":\"{}\"",
                Self::escape_json_string(key),
                Self::escape_html_string(content)
            ));
            first = false;
        }
        sb.push('}');

        // Serialize templateKeys
        sb.push_str(",\"templateKeys\":[");
        first = true;
        for key in &templates.template_keys {
            if !first {
                sb.push(',');
            }
            sb.push_str(&format!("\"{}\"", Self::escape_json_string(key)));
            first = false;
        }
        sb.push_str("]}");

        sb
    }

    fn serialize_preprocessed_site_templates_pretty(
        templates: &PreprocessedSiteTemplates,
    ) -> String {
        let mut sb = String::new();
        sb.push_str("{\n  ");
        sb.push_str(&format!(
            "\"siteName\":\"{}\"",
            Self::escape_json_string(&templates.site_name)
        ));

        // Serialize templates
        sb.push_str(",\n  \"templates\":{\n  ");
        let mut first = true;
        for (key, template) in &templates.templates {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&format!("\"{}\":{{\n  ", Self::escape_json_string(key)));
            sb.push_str(&Self::serialize_preprocessed_template_pretty_inner(
                template,
            ));
            sb.push_str("\n}");
            first = false;
        }
        sb.push_str("\n}");

        // Serialize rawTemplates
        sb.push_str(",\n  \"rawTemplates\":{\n  ");
        first = true;
        for (key, content) in &templates.raw_templates {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&format!(
                "\"{}\":\"{}\"",
                Self::escape_json_string(key),
                Self::escape_html_string(content)
            ));
            first = false;
        }
        sb.push_str("\n}");

        // Serialize templateKeys
        sb.push_str(",\n  \"templateKeys\":[\n  ");
        first = true;
        for key in &templates.template_keys {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&format!("\"{}\"", Self::escape_json_string(key)));
            first = false;
        }
        sb.push_str("\n]\n}");

        sb
    }

    fn serialize_preprocessed_template(template: &PreprocessedTemplate) -> String {
        let mut sb = String::new();
        sb.push('{');
        sb.push_str(&format!(
            "\"originalContent\":\"{}\"",
            Self::escape_html_string(&template.original_content)
        ));

        sb.push_str(",\"placeholders\":");
        sb.push_str(&Self::serialize_vec(
            &template.placeholders,
            Self::serialize_placeholder,
        ));

        sb.push_str(",\"slottedTemplates\":");
        sb.push_str(&Self::serialize_vec(
            &template.slotted_templates,
            Self::serialize_slotted_template,
        ));

        sb.push_str(",\"jsonData\":");
        if template.json_data.is_some() {
            sb.push_str("\"Arshu.App.Json.JsonObject\"");
        } else {
            sb.push_str("null");
        }

        sb.push_str(",\"jsonPlaceholders\":");
        sb.push_str(&Self::serialize_vec(
            &template.json_placeholders,
            Self::serialize_json_placeholder,
        ));

        sb.push_str(",\"replacementMappings\":");
        sb.push_str(&Self::serialize_vec(
            &template.replacement_mappings,
            Self::serialize_replacement_mapping,
        ));

        sb.push_str(&format!(
            ",\"hasPlaceholders\":{}",
            template.has_placeholders()
        ));
        sb.push_str(&format!(
            ",\"hasSlottedTemplates\":{}",
            template.has_slotted_templates()
        ));
        sb.push_str(&format!(",\"hasJsonData\":{}", template.has_json_data()));
        sb.push_str(&format!(
            ",\"hasJsonPlaceholders\":{}",
            template.has_json_placeholders()
        ));
        sb.push_str(&format!(
            ",\"hasReplacementMappings\":{}",
            template.has_replacement_mappings()
        ));
        sb.push_str(&format!(
            ",\"requiresProcessing\":{}",
            template.requires_processing()
        ));

        sb.push('}');
        sb
    }

    fn serialize_preprocessed_template_pretty_inner(template: &PreprocessedTemplate) -> String {
        let mut sb = String::new();
        sb.push_str(&format!(
            "\"originalContent\":\"{}\"",
            Self::escape_html_string(&template.original_content)
        ));

        sb.push_str(",\n  \"placeholders\":[\n  ");
        let mut first = true;
        for ph in &template.placeholders {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_placeholder_pretty(ph));
            first = false;
        }
        sb.push_str("\n]");

        sb.push_str(",\n  \"slottedTemplates\":[\n  ");
        first = true;
        for st in &template.slotted_templates {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_slotted_template_pretty(st));
            first = false;
        }
        sb.push_str("\n]");

        sb.push_str(",\n  \"jsonData\":");
        if template.json_data.is_some() {
            sb.push_str("\"Arshu.App.Json.JsonObject\"");
        } else {
            sb.push_str("null");
        }

        sb.push_str(",\n  \"jsonPlaceholders\":[\n  ");
        first = true;
        for jp in &template.json_placeholders {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_json_placeholder(jp));
            first = false;
        }
        sb.push_str("\n]");

        sb.push_str(",\n  \"replacementMappings\":[\n  ");
        first = true;
        for rm in &template.replacement_mappings {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_replacement_mapping_pretty(rm));
            first = false;
        }
        sb.push_str("\n]");

        sb.push_str(&format!(
            ",\n  \"hasPlaceholders\":{}",
            template.has_placeholders()
        ));
        sb.push_str(&format!(
            ",\n  \"hasSlottedTemplates\":{}",
            template.has_slotted_templates()
        ));
        sb.push_str(&format!(
            ",\n  \"hasJsonData\":{}",
            template.has_json_data()
        ));
        sb.push_str(&format!(
            ",\n  \"hasJsonPlaceholders\":{}",
            template.has_json_placeholders()
        ));
        sb.push_str(&format!(
            ",\n  \"hasReplacementMappings\":{}",
            template.has_replacement_mappings()
        ));
        sb.push_str(&format!(
            ",\n  \"requiresProcessing\":{}",
            template.requires_processing()
        ));

        sb
    }

    fn serialize_placeholder_pretty(ph: &TemplatePlaceholder) -> String {
        let mut sb = String::new();
        sb.push_str("{\n  ");
        sb.push_str(&format!(
            "\"Name\":\"{}\"",
            Self::escape_json_string(&ph.name)
        ));
        sb.push_str(&format!(",\n  \"StartIndex\":{}", ph.start_index));
        sb.push_str(&format!(",\n  \"EndIndex\":{}", ph.end_index));
        sb.push_str(&format!(
            ",\n  \"FullMatch\":\"{}\"",
            Self::escape_json_string(&ph.full_match)
        ));
        sb.push_str(&format!(
            ",\n  \"TemplateKey\":\"{}\"",
            Self::escape_json_string(&ph.template_key)
        ));
        sb.push_str(",\n  \"JsonData\":");
        if ph.json_data.is_some() {
            sb.push_str("\"Arshu.App.Json.JsonObject\"");
        } else {
            sb.push_str("null");
        }
        sb.push_str(",\n  \"NestedPlaceholders\":[\n  ");
        let mut first = true;
        for nested in &ph.nested_placeholders {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_placeholder(nested));
            first = false;
        }
        sb.push_str("\n]");
        sb.push_str(",\n  \"NestedSlots\":[\n  ");
        first = true;
        for slot in &ph.nested_slots {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_slot_placeholder(slot));
            first = false;
        }
        sb.push_str("\n]\n}");
        sb
    }

    fn serialize_slotted_template_pretty(st: &SlottedTemplate) -> String {
        let mut sb = String::new();
        sb.push_str("{\n  ");
        sb.push_str(&format!(
            "\"Name\":\"{}\"",
            Self::escape_json_string(&st.name)
        ));
        sb.push_str(&format!(",\n  \"StartIndex\":{}", st.start_index));
        sb.push_str(&format!(",\n  \"EndIndex\":{}", st.end_index));
        sb.push_str(&format!(
            ",\n  \"FullMatch\":\"{}\"",
            Self::escape_json_string(&st.full_match)
        ));
        sb.push_str(&format!(
            ",\n  \"TemplateKey\":\"{}\"",
            Self::escape_json_string(&st.template_key)
        ));
        sb.push_str(",\n  \"Slots\":[\n  ");
        let mut first = true;
        for slot in &st.slots {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_slot_placeholder_pretty(slot));
            first = false;
        }
        sb.push_str("\n]\n}");
        sb
    }

    fn serialize_slot_placeholder_pretty(slot: &SlotPlaceholder) -> String {
        let mut sb = String::new();
        sb.push_str("{\n  ");
        sb.push_str(&format!(
            "\"Number\":\"{}\"",
            Self::escape_json_string(&slot.number)
        ));
        sb.push_str(&format!(",\n  \"StartIndex\":{}", slot.start_index));
        sb.push_str(&format!(",\n  \"EndIndex\":{}", slot.end_index));
        sb.push_str(&format!(
            ",\n  \"Content\":\"{}\"",
            Self::escape_json_string(&slot.content)
        ));
        sb.push_str(&format!(
            ",\n  \"SlotKey\":\"{}\"",
            Self::escape_json_string(&slot.slot_key)
        ));
        sb.push_str(&format!(
            ",\n  \"OpenTag\":\"{}\"",
            Self::escape_json_string(&slot.open_tag)
        ));
        sb.push_str(&format!(
            ",\n  \"CloseTag\":\"{}\"",
            Self::escape_json_string(&slot.close_tag)
        ));
        sb.push_str(",\n  \"NestedSlots\":[\n  ");
        let mut first = true;
        for nested in &slot.nested_slots {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_slot_placeholder(nested));
            first = false;
        }
        sb.push_str("\n]");
        sb.push_str(",\n  \"NestedPlaceholders\":[\n  ");
        first = true;
        for ph in &slot.nested_placeholders {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_placeholder(ph));
            first = false;
        }
        sb.push_str("\n]");
        sb.push_str(",\n  \"NestedSlottedTemplates\":[\n  ");
        first = true;
        for st in &slot.nested_slotted_templates {
            if !first {
                sb.push_str(",\n  ");
            }
            sb.push_str(&Self::serialize_slotted_template(st));
            first = false;
        }
        sb.push_str("\n]");
        sb.push_str(&format!(
            ",\n  \"HasNestedPlaceholders\":{}",
            slot.has_nested_placeholders
        ));
        sb.push_str(&format!(
            ",\n  \"HasNestedSlottedTemplates\":{}",
            slot.has_nested_slotted_templates
        ));
        sb.push_str(&format!(
            ",\n  \"RequiresNestedProcessing\":{}",
            slot.requires_nested_processing
        ));
        sb.push_str("\n}");
        sb
    }

    fn serialize_replacement_mapping_pretty(rm: &ReplacementMapping) -> String {
        let mut sb = String::new();
        sb.push_str("{\n  ");
        sb.push_str(&format!("\"StartIndex\":{}", rm.start_index));
        sb.push_str(&format!(",\n  \"EndIndex\":{}", rm.end_index));
        sb.push_str(&format!(
            ",\n  \"OriginalText\":\"{}\"",
            Self::escape_html_string(&rm.original_text)
        ));
        sb.push_str(&format!(
            ",\n  \"ReplacementText\":\"{}\"",
            Self::escape_html_string(&rm.replacement_text)
        ));
        sb.push_str(&format!(",\n  \"Type\":{}", rm.r#type as u32));
        sb.push_str("\n}");
        sb
    }

    /// Creates a summary from PreprocessedSiteTemplates (matching C# CreatePreprocessedSummary)
    pub fn create_preprocessed_summary(
        site_templates: &PreprocessedSiteTemplates,
    ) -> PreprocessedSummary {
        PreprocessedSummary {
            site_name: site_templates.site_name.clone(),
            templates_requiring_processing: site_templates
                .templates
                .values()
                .filter(|t| t.requires_processing())
                .count(),
            templates_with_json_data: site_templates
                .templates
                .values()
                .filter(|t| t.has_json_data())
                .count(),
            templates_with_placeholders: site_templates
                .templates
                .values()
                .filter(|t| t.has_placeholders())
                .count(),
            total_templates: site_templates.templates.len(),
        }
    }

    /// Serializes PreprocessedSummary to JSON (matching C# output format)
    pub fn serialize_preprocessed_summary(summary: &PreprocessedSummary, indented: bool) -> String {
        if indented {
            format!(
                "{{\n  \"siteName\":\"{}\",\n  \"templatesRequiringProcessing\":{},\n  \"templatesWithJsonData\":{},\n  \"templatesWithPlaceholders\":{},\n  \"totalTemplates\":{}\n}}",
                Self::escape_json_string(&summary.site_name),
                summary.templates_requiring_processing,
                summary.templates_with_json_data,
                summary.templates_with_placeholders,
                summary.total_templates
            )
        } else {
            format!(
                "{{\"siteName\":\"{}\",\"templatesRequiringProcessing\":{},\"templatesWithJsonData\":{},\"templatesWithPlaceholders\":{},\"totalTemplates\":{}}}",
                Self::escape_json_string(&summary.site_name),
                summary.templates_requiring_processing,
                summary.templates_with_json_data,
                summary.templates_with_placeholders,
                summary.total_templates
            )
        }
    }
}

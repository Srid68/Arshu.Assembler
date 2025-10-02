use crate::template_model::model_preprocess::{
    PreprocessedSiteTemplates, PreprocessedTemplate, TemplatePlaceholder, 
    SlottedTemplate, JsonPlaceholder, ReplacementMapping, ReplacementType, SlotPlaceholder
};
use crate::template_common::template_utils::TemplateUtils;
use std::collections::{HashMap, HashSet};
use std::fs;
use std::path::Path;
use walkdir;
use lazy_static::lazy_static;
use std::sync::Mutex;

/// <summary>
/// Handles loading and caching of HTML templates from the file system for PreProcess engine
/// </summary>
pub struct LoaderPreProcess;

lazy_static! {
    static ref PREPROCESSED_TEMPLATES_CACHE: Mutex<HashMap<String, PreprocessedSiteTemplates>> = 
        Mutex::new(HashMap::new());
}

impl LoaderPreProcess {
    pub fn load_process_get_template_files(root_dir_path: &str, app_site: &str) -> PreprocessedSiteTemplates {
        let cache_key = format!("{}|{}", 
            Path::new(root_dir_path).parent().unwrap_or(Path::new("")).display(), 
            app_site);
        
        {
            let cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
            if let Some(cached) = cache.get(&cache_key) {
                return cached.clone();
            }
        }

        let mut result = PreprocessedSiteTemplates {
            site_name: app_site.to_string(),
            templates: HashMap::new(),
            raw_templates: HashMap::new(),
            template_keys: HashSet::new(),
        };

        let app_sites_path = format!("{}/AppSites/{}", root_dir_path, app_site);
        
        if !Path::new(&app_sites_path).exists() {
            let mut cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
            cache.insert(cache_key, result.clone());
            return result;
        }

        for entry in walkdir::WalkDir::new(&app_sites_path)
            .into_iter()
            .filter_map(|e| e.ok()) 
        {
            let path = entry.path();
            if path.extension().map(|ext| ext == "html").unwrap_or(false) {
                let file_name = path.file_stem().unwrap().to_string_lossy().to_string();
                let key = format!("{}_{}", app_site.to_lowercase(), file_name.to_lowercase());
                let content = fs::read_to_string(path).unwrap_or_default();

                let json_file = path.with_extension("json");
                let json_content = if json_file.exists() {
                    Some(fs::read_to_string(json_file).unwrap_or_default())
                } else {
                    None
                };

                result.raw_templates.insert(key.clone(), content.clone());
                result.template_keys.insert(key.clone());

                let preprocessed = Self::preprocess_template(&content, json_content.as_deref(), &key);
                result.templates.insert(key, preprocessed);
            }
        }

        Self::create_all_replacement_mappings_for_site(&mut result, app_site);

        // Update convenience flags for all templates after processing
        for template in result.templates.values_mut() {
            template.update_flags();
        }

        let mut cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
        cache.insert(cache_key, result.clone());
        result
    }

    pub fn preprocess_json_data(json_content: &str) -> Option<crate::app::JsonObject> {
        use crate::app::JsonConverter;
        Some(JsonConverter::parse_json_string(json_content))
    }

    pub fn clear_cache() {
        let mut preprocessed_cache = PREPROCESSED_TEMPLATES_CACHE.lock().unwrap();
        preprocessed_cache.clear();
    }

    fn preprocess_template(content: &str, json_content: Option<&str>, _template_key: &str) -> PreprocessedTemplate {
        let mut template = PreprocessedTemplate {
            original_content: content.to_string(),
            placeholders: Vec::new(),
            slotted_templates: Vec::new(),
            json_data: None,
            json_placeholders: Vec::new(),
            replacement_mappings: Vec::new(),
            has_placeholders_flag: false,
            has_slotted_templates_flag: false,
            has_json_data_flag: false,
            has_json_placeholders_flag: false,
            has_replacement_mappings_flag: false,
            requires_processing_flag: false,
        };
        
        if content.is_empty() {
            return template;
        }

        if let Some(json) = json_content {
            template.json_data = Self::preprocess_json_data(json);
        }
        
        Self::parse_slotted_templates(content, &mut template);
        Self::parse_placeholder_templates(content, &mut template);
        
        if template.has_json_data() {
            Self::preprocess_json_templates(&mut template);
        }
        
        template.update_flags();
        template
    }

    fn create_all_replacement_mappings_for_site(site_templates: &mut PreprocessedSiteTemplates, app_site: &str) {
        let template_keys: Vec<String> = site_templates.templates.keys().cloned().collect();
        
        for key in &template_keys {
            if let Some(template) = site_templates.templates.get_mut(key) {
                let content = template.original_content.clone();
                Self::create_json_array_replacement_mappings(template, &content);
            }
        }

        let all_templates_snapshot = site_templates.templates.clone();
        for key in &template_keys {
            if let Some(template) = site_templates.templates.get_mut(key) {
                Self::create_placeholder_replacement_mappings(template, &all_templates_snapshot, app_site);
            }
        }

        let all_templates_snapshot = site_templates.templates.clone();
        for key in &template_keys {
            if let Some(template) = site_templates.templates.get_mut(key) {
                Self::create_slotted_template_replacement_mappings(template, &all_templates_snapshot, app_site);
            }
        }
    }

    fn create_placeholder_replacement_mappings(template: &mut PreprocessedTemplate, all_templates: &HashMap<String, PreprocessedTemplate>, app_site: &str) {
        if !template.has_placeholders() {
            return;
        }

        for placeholder in &template.placeholders {
            let target_template_key = format!("{}_{}", app_site.to_lowercase(), placeholder.template_key);
            if let Some(target_template) = all_templates.get(&target_template_key) {
                template.replacement_mappings.push(ReplacementMapping {
                    start_index: placeholder.start_index,
                    end_index: placeholder.end_index,
                    original_text: placeholder.full_match.clone(),
                    replacement_text: target_template.original_content.clone(),
                    r#type: ReplacementType::SimpleTemplate,
                });
            }
        }
    }

    fn create_slotted_template_replacement_mappings(template: &mut PreprocessedTemplate, all_templates: &HashMap<String, PreprocessedTemplate>, app_site: &str) {
        if !template.has_slotted_templates() {
            return;
        }

        for slotted_template in &template.slotted_templates {
            let full_match = &slotted_template.full_match;
            let target_template_key = format!("{}_{}", app_site.to_lowercase(), slotted_template.template_key);

            if let Some(target_template) = all_templates.get(&target_template_key) {
                let mut processed_template = target_template.original_content.clone();

                for slot in &slotted_template.slots {
                    let processed_slot_content = Self::process_slot_content_for_replacement_mapping(slot, all_templates, app_site);
                    processed_template = processed_template.replace(&slot.slot_key, &processed_slot_content);
                }

                if slotted_template.slots.is_empty() {
                    let actual_inner_content = &slotted_template.inner_content;
                    if !actual_inner_content.trim().is_empty() {
                        let default_slot_key = "{{$HTMLPLACEHOLDER}}";
                        if processed_template.contains(default_slot_key) {
                            processed_template = processed_template.replace(default_slot_key, actual_inner_content.trim());
                        }
                    }
                }

                processed_template = TemplateUtils::remove_remaining_slot_placeholders(&processed_template);

                template.replacement_mappings.push(ReplacementMapping {
                    start_index: slotted_template.start_index,
                    end_index: slotted_template.end_index,
                    original_text: full_match.clone(),
                    replacement_text: processed_template,
                    r#type: ReplacementType::SlottedTemplate,
                });
            }
        }
    }

    fn process_slot_content_for_replacement_mapping(slot: &SlotPlaceholder, all_templates: &HashMap<String, PreprocessedTemplate>, app_site: &str) -> String {
        let mut result = slot.content.clone();

        for nested_slotted_template in &slot.nested_slotted_templates {
            let target_template_key = format!("{}_{}", app_site.to_lowercase(), nested_slotted_template.template_key);
            if let Some(target_template) = all_templates.get(&target_template_key) {
                let mut processed_template = target_template.original_content.clone();

                for nested_slot in &nested_slotted_template.slots {
                    let processed_nested_slot_content = Self::process_slot_content_for_replacement_mapping(nested_slot, all_templates, app_site);
                    processed_template = processed_template.replace(&nested_slot.slot_key, &processed_nested_slot_content);
                }

                processed_template = TemplateUtils::remove_remaining_slot_placeholders(&processed_template);
                result = result.replace(&nested_slotted_template.full_match, &processed_template);
            }
        }

        for nested_placeholder in &slot.nested_placeholders {
            let target_template_key = format!("{}_{}", app_site.to_lowercase(), nested_placeholder.template_key);
            if let Some(target_template) = all_templates.get(&target_template_key) {
                let processed_template = target_template.original_content.clone();
                result = result.replace(&nested_placeholder.full_match, &processed_template);
            }
        }

        result
    }

    fn parse_slotted_templates(content: &str, template: &mut PreprocessedTemplate) {
        let mut search_pos = 0;
        while search_pos < content.len() {
            let open_start = match content[search_pos..].find("{{#") {
                Some(pos) => search_pos + pos,
                None => break,
            };
            let open_end = match content[open_start + 3..].find("}}") {
                Some(pos) => open_start + 3 + pos,
                None => break,
            };

            let template_name = content[open_start + 3..open_end].trim();
            if template_name.is_empty() || !TemplateUtils::is_alphanumeric(template_name) {
                search_pos = open_start + 1;
                continue;
            }

            let close_tag = format!("{{{{/{}}}}}", template_name);
            let open_tag = format!("{{{{#{}}}}}", template_name);
            
            let close_start = match TemplateUtils::find_matching_close_tag(content, open_end + 2, &open_tag, &close_tag) {
                Some(pos) => pos,
                None => {
                    search_pos = open_start + 1;
                    continue;
                }
            };

            let full_match = content[open_start..close_start + close_tag.len()].to_string();
            let inner_content = content[open_end + 2..close_start].to_string();

            let mut slots = Vec::new();
            Self::parse_slots(&inner_content, &mut slots);
            
            let slotted_template = SlottedTemplate {
                name: template_name.to_string(),
                start_index: open_start,
                end_index: close_start + close_tag.len(),
                full_match,
                inner_content,
                slots,
                template_key: template_name.to_lowercase(),
                json_data: None,
            };

            if !template.slotted_templates.iter().any(|st| st.name == template_name) {
                template.slotted_templates.push(slotted_template);
            }

            search_pos = close_start + close_tag.len();
        }
    }

    fn parse_slots(inner_content: &str, slots: &mut Vec<SlotPlaceholder>) {
        let mut search_pos = 0;
        while search_pos < inner_content.len() {
            let slot_start = match inner_content[search_pos..].find("{{@HTMLPLACEHOLDER") {
                Some(pos) => search_pos + pos,
                None => break,
            };

            let after_placeholder = slot_start + 18;
            let mut slot_num = String::new();
            let mut pos = after_placeholder;

            while pos < inner_content.len() {
                let ch = inner_content.chars().nth(pos).unwrap();
                if ch.is_ascii_digit() {
                    slot_num.push(ch);
                    pos += 1;
                } else {
                    break;
                }
            }

            if pos + 1 >= inner_content.len() || &inner_content[pos..pos + 2] != "}}" {
                search_pos = slot_start + 1;
                continue;
            }

            let slot_open_end = pos + 2;

            let close_tag = if slot_num.is_empty() {
                "{{/HTMLPLACEHOLDER}}".to_string()
            } else {
                format!("{{{{/HTMLPLACEHOLDER{}}}}}", slot_num)
            };
            
            let open_tag = if slot_num.is_empty() {
                "{{@HTMLPLACEHOLDER}}".to_string()
            } else {
                format!("{{{{@HTMLPLACEHOLDER{}}}}}", slot_num)
            };

            let close_start = match TemplateUtils::find_matching_close_tag(inner_content, slot_open_end, &open_tag, &close_tag) {
                Some(pos) => pos,
                None => {
                    search_pos = slot_start + 1;
                    continue;
                }
            };

            let slot_content = inner_content[slot_open_end..close_start].to_string();

            let slot_key = if slot_num.is_empty() {
                "{{$HTMLPLACEHOLDER}}".to_string()
            } else {
                format!("{{{{$HTMLPLACEHOLDER{}}}}}", slot_num)
            };

            let mut temp_template = PreprocessedTemplate {
                original_content: slot_content.clone(),
                placeholders: Vec::new(),
                slotted_templates: Vec::new(),
                json_data: None,
                json_placeholders: Vec::new(),
                replacement_mappings: Vec::new(),
                has_placeholders_flag: false,
                has_slotted_templates_flag: false,
                has_json_data_flag: false,
                has_json_placeholders_flag: false,
                has_replacement_mappings_flag: false,
                requires_processing_flag: false,
            };
            
            Self::parse_slotted_templates(&slot_content, &mut temp_template);
            let nested_slotted_templates = temp_template.slotted_templates.clone();
            
            Self::parse_placeholder_templates(&slot_content, &mut temp_template);
            let nested_placeholders = temp_template.placeholders.clone();

            let slot = SlotPlaceholder {
                number: slot_num,
                start_index: slot_start,
                end_index: close_start + close_tag.len(),
                content: slot_content,
                slot_key,
                open_tag,
                close_tag: close_tag.clone(),
                nested_slots: Vec::new(),
                nested_placeholders,
                nested_slotted_templates,
            };

            slots.push(slot);
            search_pos = close_start + close_tag.len();
        }
    }

    fn parse_placeholder_templates(content: &str, template: &mut PreprocessedTemplate) {
        let mut search_pos = 0;
        while search_pos < content.len() {
            let open_start = match content[search_pos..].find("{{") {
                Some(pos) => search_pos + pos,
                None => break,
            };
            
            if open_start + 2 < content.len() {
                let next_char = content.chars().nth(open_start + 2).unwrap_or('\0');
                if next_char == '#' || next_char == '@' || next_char == '$' || next_char == '/' {
                    search_pos = open_start + 2;
                    continue;
                }
            }
            
            let close_start = match content[open_start + 2..].find("}}") {
                Some(pos) => open_start + 2 + pos,
                None => break,
            };
            
            let placeholder_name = content[open_start + 2..close_start].trim();
            if placeholder_name.is_empty() || !TemplateUtils::is_alphanumeric(placeholder_name) {
                search_pos = open_start + 2;
                continue;
            }
            
            let placeholder = TemplatePlaceholder {
                name: placeholder_name.to_string(),
                start_index: open_start,
                end_index: close_start + 2,
                full_match: content[open_start..close_start + 2].to_string(),
                template_key: placeholder_name.to_lowercase(),
                json_data: None,
                nested_placeholders: Vec::new(),
                nested_slots: Vec::new(),
            };

            if !template.placeholders.iter().any(|p| p.name == placeholder_name) {
                template.placeholders.push(placeholder);
            }
            
            search_pos = close_start + 2;
        }
    }
    
    fn preprocess_json_templates(template: &mut PreprocessedTemplate) {
        if template.json_data.is_none() {
            return;
        }

        let content = template.original_content.clone();
        Self::create_json_array_replacement_mappings(template, &content);
        Self::create_json_placeholder_replacement_mappings(template, &content);
    }

    fn create_json_array_replacement_mappings(template: &mut PreprocessedTemplate, content: &str) {
        if template.json_data.is_none() { return; }
        let json_data = template.json_data.as_ref().unwrap();

        if let Some(object) = json_data.as_object() {
            for (json_key, json_value) in object {
                if let Some(data_list) = json_value.as_array() {
                    let key_norm = json_key.to_lowercase();
                    let possible_tags = vec![
                        json_key.clone(),
                        key_norm.clone(),
                        key_norm.trim_end_matches('s').to_string(),
                        format!("{}s", key_norm),
                    ];

                    for tag in possible_tags {
                        let block_start_tag = format!("{{{{@{}}}}}", tag);
                        let block_end_tag = format!("{{{{/{}}}}}", tag);

                        if let Some(start_idx) = content.to_lowercase().find(&block_start_tag.to_lowercase()) {
                            let search_from = start_idx + block_start_tag.len();
                            if let Some(end_idx) = content[search_from..].to_lowercase().find(&block_end_tag.to_lowercase()) {
                                let end_idx = search_from + end_idx;

                                if end_idx > start_idx {
                                    let block_content = &content[start_idx + block_start_tag.len()..end_idx];
                                    let full_block = &content[start_idx..end_idx + block_end_tag.len()];

                                    let processed_array_content = Self::process_array_block_content_safely(block_content, data_list);

                                    template.replacement_mappings.push(ReplacementMapping {
                                        start_index: start_idx,
                                        end_index: end_idx + block_end_tag.len(),
                                        original_text: full_block.to_string(),
                                        replacement_text: processed_array_content,
                                        r#type: ReplacementType::JsonPlaceholder,
                                    });

                                    let empty_block_start = format!("{{{{^{}}}}}", tag);
                                    let empty_block_end = format!("{{{{/{}}}}}", tag);
                                    if let Some(empty_start_idx) = content.to_lowercase().find(&empty_block_start.to_lowercase()) {
                                        let empty_search_from = empty_start_idx + empty_block_start.len();
                                        if let Some(empty_end_idx) = content[empty_search_from..].to_lowercase().find(&empty_block_end.to_lowercase()) {
                                            let empty_end_idx = empty_search_from + empty_end_idx;
                                            if empty_end_idx > empty_start_idx + empty_block_start.len() {
                                                let empty_content_start = empty_start_idx + empty_block_start.len();
                                                let empty_block_content = &content[empty_content_start..empty_end_idx];
                                                let full_empty_block = &content[empty_start_idx..empty_end_idx + empty_block_end.len()];
                                                let empty_replacement = if data_list.is_empty() { empty_block_content } else { "" };
                                                
                                                template.replacement_mappings.push(ReplacementMapping {
                                                    start_index: empty_start_idx,
                                                    end_index: empty_end_idx + empty_block_end.len(),
                                                    original_text: full_empty_block.to_string(),
                                                    replacement_text: empty_replacement.to_string(),
                                                    r#type: ReplacementType::JsonPlaceholder,
                                                });
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    fn process_array_block_content_safely(block_content: &str, array_data: &crate::app::JsonArray) -> String {
        use crate::app::JsonValue;
        let mut merged_block = String::new();
        
        for item in array_data.iter() {
            if let JsonValue::Object(json_item) = item {
                let mut item_block = block_content.to_string();

                for (key, value) in json_item.iter() {
                    let placeholder = format!("{{{{${}}}}}", key);
                    let value_str = match value {
                        JsonValue::String(s) => s.clone(),
                        JsonValue::Number(n) => n.to_string(),
                        JsonValue::Integer(i) => i.to_string(),
                        JsonValue::Bool(b) => b.to_string(),
                        JsonValue::Null => "null".to_string(),
                        _ => value.to_string(),
                    };
                    item_block = Self::replace_all_case_insensitive(&item_block, &placeholder, &value_str);
                }

                item_block = Self::process_conditional_blocks_safely(&item_block, json_item);
                merged_block.push_str(&item_block);
            }
        }
        merged_block
    }

    fn replace_all_case_insensitive(input: &str, search: &str, replacement: &str) -> String {
        let mut result = String::new();
        let mut last_end = 0;
        let lower_search = search.to_lowercase();
        let lower_input = input.to_lowercase();

        while let Some(start) = lower_input[last_end..].find(&lower_search) {
            let start = last_end + start;
            result.push_str(&input[last_end..start]);
            result.push_str(replacement);
            last_end = start + search.len();
        }
        result.push_str(&input[last_end..]);
        result
    }

    fn process_conditional_blocks_safely(content: &str, json_item: &crate::app::JsonObject) -> String {
        let mut result = content.to_string();
        let conditional_keys = Self::find_conditional_keys_in_content(&result);

        for cond_key in conditional_keys {
            let cond_value = Self::get_condition_value(json_item, &cond_key);
            result = Self::process_conditional_block_safely(&result, &cond_key, cond_value);
        }
        result
    }

    fn find_conditional_keys_in_content(content: &str) -> HashSet<String> {
        let mut conditional_keys = HashSet::new();
        let mut cond_idx = 0;

        while let Some(cond_start) = content[cond_idx..].to_lowercase().find("{{@") {
            let cond_start = cond_idx + cond_start;
            if let Some(cond_end) = content[cond_start..].find("}}") {
                let cond_end = cond_start + cond_end;
                let cond_key = content[cond_start + 3..cond_end].trim().to_string();
                conditional_keys.insert(cond_key);
                cond_idx = cond_end + 2;
            } else {
                break;
            }
        }
        conditional_keys
    }

    fn get_condition_value(item: &crate::app::JsonObject, cond_key: &str) -> bool {
        use crate::app::JsonValue;
        if let Some(value) = item.get(cond_key) {
            return match value {
                JsonValue::Bool(b) => *b,
                JsonValue::String(s) => s.parse::<bool>().unwrap_or(false),
                JsonValue::Integer(i) => *i != 0,
                JsonValue::Number(n) => *n != 0.0,
                _ => false,
            };
        }
        for (key, value) in item.iter() {
            if key.eq_ignore_ascii_case(cond_key) {
                return match value {
                    JsonValue::Bool(b) => *b,
                    JsonValue::String(s) => s.parse::<bool>().unwrap_or(false),
                    JsonValue::Integer(i) => *i != 0,
                    JsonValue::Number(n) => *n != 0.0,
                    _ => false,
                };
            }
        }
        false
    }

    fn process_conditional_block_safely(input: &str, key: &str, condition: bool) -> String {
        let mut result = input.to_string();
        let condition_tags = vec![
            (format!("{{{{@{}}}}}", key), format!("{{{{ /{}}}}}", key)),
            (format!("{{{{@{}}}}}", key), format!("{{{{/{}}}}}", key)),
        ];

        for (cond_start_tag, cond_end_tag) in condition_tags {
            while let Some(start_idx) = result.to_lowercase().find(&cond_start_tag.to_lowercase()) {
                let content_start = start_idx + cond_start_tag.len();
                if let Some(end_idx) = result[content_start..].to_lowercase().find(&cond_end_tag.to_lowercase()) {
                    let end_idx = content_start + end_idx;
                    let content = &result[content_start..end_idx];
                    let replacement = if condition { content.to_string() } else { String::new() };
                    result.replace_range(start_idx..end_idx + cond_end_tag.len(), &replacement);
                } else {
                    break;
                }
            }
        }
        result
    }

    fn create_json_placeholder_replacement_mappings(template: &mut PreprocessedTemplate, content: &str) {
        if template.json_data.is_none() { return; }
        let json_data = template.json_data.as_ref().unwrap();

        if let Some(object) = json_data.as_object() {
            for (key, value) in object {
                if let Some(string_value) = value.as_str() {
                    let placeholders = vec![
                        format!("{{{{${}}}}}", key),
                        format!("{{{{{}}}}}", key),
                    ];

                    for placeholder in placeholders {
                        if content.to_lowercase().contains(&placeholder.to_lowercase()) {
                            template.replacement_mappings.push(ReplacementMapping {
                                start_index: 0, 
                                end_index: 0,   
                                original_text: placeholder.clone(),
                                replacement_text: string_value.to_string(),
                                r#type: ReplacementType::JsonPlaceholder,
                            });
                            
                            if !template.json_placeholders.iter().any(|p| p.placeholder == placeholder) {
                                template.json_placeholders.push(JsonPlaceholder {
                                    key: key.clone(),
                                    placeholder: placeholder,
                                    value: string_value.to_string(),
                                });
                            }
                        }
                    }
                }
            }
        }
    }
}
use crate::app::json::{JsonObject, JsonValue};
use std::collections::{HashMap, HashSet};

/// Shared JSON merging utilities for all loaders
/// Moved from EngineNormal to centralize JSON processing logic
pub struct JsonMergeUtil;

impl JsonMergeUtil {
    /// Merges HTML template with JSON data using placeholder replacement
    /// Handles:
    /// - Array blocks: {{@array}}...{{/array}}
    /// - Conditional blocks: {{@condition}}...{{/condition}}
    /// - Empty array blocks: {{^array}}...{{/array}}
    /// - Simple placeholders: {{$key}}
    ///
    /// # Arguments
    /// * `template` - The HTML template content
    /// * `json_object` - The parsed JsonObject
    ///
    /// # Returns
    /// Merged HTML with JSON data populated
    pub fn merge_template_with_json(template: &str, json_object: &JsonObject) -> String {
        let mut dict: HashMap<String, JsonValue> = HashMap::new();

        // Convert JsonObject to dictionary
        for (key, value) in json_object.iter() {
            dict.insert(key.to_lowercase(), value.clone());
        }

        // Advanced merge logic for block and conditional patterns
        let mut result = template.to_string();

        // Process JSON arrays - match JSON array keys to template blocks
        let keys: Vec<String> = dict.keys().cloned().collect();
        for json_key in keys {
            if let Some(JsonValue::Array(data_list)) = dict.get(&json_key) {
                // Try to find a matching template block for this JSON array
                let key_norm = json_key.to_lowercase();

                // Look for possible template tags that match this JSON key
                let possible_tags = vec![
                    json_key.clone(),
                    key_norm.clone(),
                    key_norm.trim_end_matches('s').to_string(),
                    format!("{}s", &key_norm),
                ];

                for tag in possible_tags {
                    let block_start_tag = format!("{{{{@{}}}}}", tag);
                    let block_end_tag = format!("{{{{/{}}}}}", tag);

                    if let Some(start_idx) =
                        Self::index_of_case_insensitive(&result, &block_start_tag)
                    {
                        let search_from = start_idx + block_start_tag.len();
                        if let Some(end_idx) = Self::index_of_case_insensitive_from(
                            &result,
                            &block_end_tag,
                            search_from,
                        ) {
                            if end_idx > start_idx {
                                // Found a valid block - process it
                                let content_start_idx = start_idx + block_start_tag.len();
                                if content_start_idx <= end_idx {
                                    let block_content =
                                        result[content_start_idx..end_idx].to_string();
                                    let mut merged_block = String::new();

                                    // Find all conditional blocks in the template block
                                    let conditional_keys =
                                        Self::find_conditional_keys(&block_content);

                                    for item in data_list.iter() {
                                        if let JsonValue::Object(item_obj) = item {
                                            let mut item_block = block_content.clone();

                                            // Replace all placeholders dynamically
                                            for (kvp_key, kvp_value) in item_obj.iter() {
                                                let placeholder = format!("{{{{${}}}}}", kvp_key);
                                                let value_str = match kvp_value {
                                                    JsonValue::Bool(b) => {
                                                        if *b { "true" } else { "false" }
                                                            .to_string()
                                                    }
                                                    JsonValue::Null => String::new(),
                                                    JsonValue::String(s) => s.clone(),
                                                    JsonValue::Integer(i) => i.to_string(),
                                                    JsonValue::Number(n) => n.to_string(),
                                                    _ => kvp_value.to_string(),
                                                };
                                                item_block = Self::replace_all_case_insensitive(
                                                    &item_block,
                                                    &placeholder,
                                                    &value_str,
                                                );
                                            }

                                            // Handle all conditional blocks dynamically
                                            for cond_key in &conditional_keys {
                                                let cond_value =
                                                    Self::get_condition_value(item_obj, cond_key);
                                                item_block = Self::handle_conditional(
                                                    &item_block,
                                                    cond_key,
                                                    cond_value,
                                                );
                                            }
                                            merged_block.push_str(&item_block);
                                        }
                                    }

                                    // Replace block in result
                                    let before = &result[..start_idx];
                                    let after = &result[end_idx + block_end_tag.len()..];
                                    result = format!("{}{}{}", before, merged_block, after);
                                    break; // Process only the first matching template for this JSON key
                                }
                            }
                        }
                    }
                }
            }
        }

        // Handle {{^ArrayName}} block if array is empty (dynamic detection)
        let keys_for_empty: Vec<String> = dict.keys().cloned().collect();
        for key in keys_for_empty {
            let empty_block_start = format!("{{{{^{}}}}}", key);
            let empty_block_end = format!("{{{{/{}}}}}", key);

            if let Some(empty_start_idx) =
                Self::index_of_case_insensitive(&result, &empty_block_start)
            {
                if let Some(empty_end_idx) =
                    Self::index_of_case_insensitive(&result, &empty_block_end)
                {
                    if let Some(JsonValue::Array(l)) = dict.get(&key) {
                        let is_empty = l.is_empty();
                        let content_start = empty_start_idx + empty_block_start.len();
                        if content_start < empty_end_idx {
                            let empty_content = result[content_start..empty_end_idx].to_string();
                            let before = &result[..empty_start_idx];
                            let after = &result[empty_end_idx + empty_block_end.len()..];

                            result = if is_empty {
                                format!("{}{}{}", before, empty_content, after)
                            } else {
                                format!("{}{}", before, after)
                            };
                        }
                    }
                }
            }
        }

        // Replace remaining simple placeholders
        for (kvp_key, kvp_value) in &dict {
            let value_str = match kvp_value {
                JsonValue::String(s) => Some(s.clone()),
                JsonValue::Bool(b) => Some(if *b { "true" } else { "false" }.to_string()),
                JsonValue::Integer(i) => Some(i.to_string()),
                JsonValue::Number(d) => Some(d.to_string()),
                JsonValue::Null => Some(String::new()),
                _ => None,
            };

            if let Some(value_str) = value_str {
                let placeholder = format!("{{{{${}}}}}", kvp_key);
                result = Self::replace_all_case_insensitive(&result, &placeholder, &value_str);
            }
        }

        result
    }

    /// Helper: Replace all case-insensitive occurrences
    fn replace_all_case_insensitive(input: &str, search: &str, replacement: &str) -> String {
        let mut result = input.to_string();
        let mut idx = 0;

        while let Some(found) = Self::index_of_case_insensitive_from(&result, search, idx) {
            let before = &result[..found];
            let after = &result[found + search.len()..];
            result = format!("{}{}{}", before, replacement, after);
            idx = found + replacement.len();
        }

        result
    }

    /// Helper: Handle conditional blocks like {{@Selected}}...{{/Selected}}
    fn handle_conditional(input: &str, key: &str, condition: bool) -> String {
        let mut result = input.to_string();

        // Support spaces inside block tags, e.g. {{@Selected}} ... {{ /Selected}}
        let condition_tags = vec![
            (format!("{{{{@{}}}}}", key), format!("{{{{ /{}}}}}", key)),
            (format!("{{{{@{}}}}}", key), format!("{{{{/{}}}}}", key)),
        ];

        for (cond_start, cond_end) in condition_tags {
            while let Some(start_idx) = Self::index_of_case_insensitive(&result, &cond_start) {
                if let Some(end_idx) = Self::index_of_case_insensitive(&result, &cond_end) {
                    let content_start = start_idx + cond_start.len();
                    if end_idx > content_start {
                        let content = result[content_start..end_idx].to_string();
                        let before = &result[..start_idx];
                        let after = &result[end_idx + cond_end.len()..];

                        result = if condition {
                            format!("{}{}{}", before, content, after)
                        } else {
                            format!("{}{}", before, after)
                        };
                    } else {
                        break;
                    }
                } else {
                    break;
                }
            }
        }

        result
    }

    /// Helper: Find all conditional keys in content
    fn find_conditional_keys(content: &str) -> HashSet<String> {
        let mut conditional_keys = HashSet::new();
        let mut cond_idx = 0;
        let _content_bytes = content.as_bytes();

        while cond_idx < content.len() {
            if let Some(cond_start) = Self::index_of_case_insensitive_from(content, "{{@", cond_idx)
            {
                if let Some(cond_end) =
                    Self::index_of_case_insensitive_from(content, "}}", cond_start)
                {
                    let start = cond_start + 3;
                    if start < cond_end {
                        let cond_key = content[start..cond_end].trim().to_string();
                        conditional_keys.insert(cond_key);
                        cond_idx = cond_end + 2;
                    } else {
                        break;
                    }
                } else {
                    break;
                }
            } else {
                break;
            }
        }

        conditional_keys
    }

    /// Helper: Get condition value from item data
    fn get_condition_value(item: &JsonObject, cond_key: &str) -> bool {
        // Try exact match first (case-insensitive)
        for (key, value) in item.iter() {
            if key.eq_ignore_ascii_case(cond_key) {
                return match value {
                    JsonValue::Bool(b) => *b,
                    JsonValue::String(s) => s.parse::<bool>().unwrap_or(false),
                    JsonValue::Integer(i) => *i != 0,
                    JsonValue::Number(d) => *d != 0.0,
                    _ => false,
                };
            }
        }

        false
    }

    /// Helper: Case-insensitive indexOf
    fn index_of_case_insensitive(haystack: &str, needle: &str) -> Option<usize> {
        Self::index_of_case_insensitive_from(haystack, needle, 0)
    }

    /// Helper: Case-insensitive indexOf from position
    fn index_of_case_insensitive_from(haystack: &str, needle: &str, from: usize) -> Option<usize> {
        let haystack_lower = haystack.to_lowercase();
        let needle_lower = needle.to_lowercase();

        haystack_lower[from..]
            .find(&needle_lower)
            .map(|pos| from + pos)
    }
}

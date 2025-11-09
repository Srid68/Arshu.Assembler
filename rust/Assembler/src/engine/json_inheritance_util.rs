use crate::app::json_convertor::JsonConverter;
use arshu::common::Logger;
use std::collections::HashMap;

pub struct JsonInheritanceUtil;

impl JsonInheritanceUtil {
    /// Builds a parent map from template structure by analyzing placeholders
    /// This tracks which template is the parent of another based on {{TemplateName}} references
    pub fn build_parent_map(
        app_site: &str,
        all_templates: &HashMap<String, (String, Option<String>)>,
    ) -> HashMap<String, String> {
        let mut parent_map = HashMap::new();

        Logger::debug(&format!("Building parent map for appSite: {}", app_site), Some("JsonInheritance"));

        for (template_key, (html, _)) in all_templates {
            let mut search_pos = 0;
            while search_pos < html.len() {
                let open_start = match html[search_pos..].find("{{") {
                    Some(pos) => search_pos + pos,
                    None => break,
                };

                // Skip special placeholders (#, @, $, /)
                if open_start + 2 < html.len() {
                    let next_char = html.chars().nth(open_start + 2).unwrap_or('\0');
                    if next_char == '#' || next_char == '@' || next_char == '$' || next_char == '/' {
                        search_pos = open_start + 2;
                        continue;
                    }
                }

                let close_start = match html[open_start + 2..].find("}}") {
                    Some(pos) => open_start + 2 + pos,
                    None => break,
                };

                let placeholder_name = html[open_start + 2..close_start].trim();

                // Check if this is a valid alphanumeric template name
                if !placeholder_name.is_empty() && Self::is_alphanumeric(placeholder_name) {
                    // This template (template_key) is the parent of the placeholder template
                    let child_template_key = format!("{}_{}", app_site.to_lowercase(), placeholder_name.to_lowercase());

                    if !parent_map.contains_key(&child_template_key) {
                        parent_map.insert(child_template_key.clone(), template_key.clone());
                        Logger::debug(&format!("Parent relationship: {} -> parent: {}", child_template_key, template_key), Some("JsonInheritance"));
                    }
                }

                search_pos = close_start + 2;
            }
        }

        Logger::debug(&format!("Built parent map with {} relationships", parent_map.len()), Some("JsonInheritance"));
        parent_map
    }

    /// Resolves a JSON key with inheritance support
    /// If the key ends with #, searches up the parent tree for the key without #
    pub fn resolve_json_key_with_inheritance(
        json_key: &str,
        current_value: &str,
        current_template_key: &str,
        all_templates: &HashMap<String, (String, Option<String>)>,
        parent_map: &HashMap<String, String>,
    ) -> String {
        // If key doesn't end with #, no inheritance - return current value
        if !json_key.ends_with('#') {
            return current_value.to_string();
        }

        // Extract the actual key name without the # suffix
        let actual_key = &json_key[0..json_key.len() - 1];

        Logger::debug(&format!("Resolving inherited key: {} -> {} for template {}", json_key, actual_key, current_template_key), Some("JsonInheritance"));

        // Search up the parent tree for the key
        let inherited_value = Self::search_parent_tree_for_key(actual_key, current_template_key, all_templates, parent_map);

        if let Some(value) = inherited_value {
            Logger::debug(&format!("Found inherited value for {}: {}", actual_key, value), Some("JsonInheritance"));
            return value;
        }

        // If not found in parents, use the current value as default
        Logger::debug(&format!("No inherited value found for {}, using default: {}", actual_key, current_value), Some("JsonInheritance"));
        current_value.to_string()
    }

    /// Searches up the parent tree to find a JSON key value
    fn search_parent_tree_for_key(
        key: &str,
        current_template_key: &str,
        all_templates: &HashMap<String, (String, Option<String>)>,
        parent_map: &HashMap<String, String>,
    ) -> Option<String> {
        // Get parent template key
        let parent_key = match parent_map.get(current_template_key) {
            Some(pk) => pk,
            None => {
                Logger::debug(&format!("No parent found for {}", current_template_key), Some("JsonInheritance"));
                return None;
            }
        };

        Logger::debug(&format!("Checking parent {} for key {}", parent_key, key), Some("JsonInheritance"));

        // Get parent's JSON data
        let parent_template = match all_templates.get(parent_key) {
            Some(pt) => pt,
            None => {
                Logger::debug(&format!("Parent template {} not found in all_templates", parent_key), Some("JsonInheritance"));
                return None;
            }
        };

        let parent_json = match &parent_template.1 {
            Some(json_str) => json_str,
            None => {
                Logger::debug(&format!("Parent template {} has no JSON data, searching further up", parent_key), Some("JsonInheritance"));
                // Parent has no JSON, search further up the tree
                return Self::search_parent_tree_for_key(key, parent_key, all_templates, parent_map);
            }
        };

        // Parse parent's JSON
        let parent_json_obj = JsonConverter::parse_json_string(parent_json);

        // Look for the key (case-insensitive)
        for (k, v) in parent_json_obj.iter() {
            if k.eq_ignore_ascii_case(key) {
                if let Some(s) = v.as_str() {
                    Logger::debug(&format!("Found key {} in parent {}: {}", key, parent_key, s), Some("JsonInheritance"));
                    return Some(s.to_string());
                }
            }
        }

        Logger::debug(&format!("Key {} not found in parent {}, searching further up", key, parent_key), Some("JsonInheritance"));
        // Not found in this parent, search further up the tree
        Self::search_parent_tree_for_key(key, parent_key, all_templates, parent_map)
    }

    fn is_alphanumeric(s: &str) -> bool {
        s.chars().all(|c| c.is_alphanumeric())
    }
}
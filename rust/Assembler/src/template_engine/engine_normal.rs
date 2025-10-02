use std::collections::HashMap;
use crate::app::json::JsonValue;
use crate::app::json_convertor::JsonConverter;
use crate::template_common::template_utils::TemplateUtils;

/// <summary>
/// IndexOf-based template engine implementation for improved performance
/// </summary>
pub struct EngineNormal {
    app_view_prefix: String,
}

impl EngineNormal {
    pub fn new(prefix: String) -> Self {
        Self { app_view_prefix: prefix }
    }

    pub fn app_view_prefix(&self) -> &str {
        &self.app_view_prefix
    }
    
    pub fn set_app_view_prefix(&mut self, prefix: String) {
        self.app_view_prefix = prefix;
    }
    
    /// Helper method for case-insensitive string replacement
    fn replace_case_insensitive(text: &str, from: &str, to: &str) -> String {
        let text_lower = text.to_lowercase();
        let from_lower = from.to_lowercase();
        
        if let Some(start) = text_lower.find(&from_lower) {
            let end = start + from.len();
            let mut result = String::new();
            result.push_str(&text[..start]);
            result.push_str(to);
            result.push_str(&text[end..]);
            result
        } else {
            text.to_string()
        }
    }
    
    /// <summary>
    /// Merges templates by replacing placeholders with corresponding HTML
    /// This is a hybrid method that processes both slotted templates and simple placeholders
    /// JSON files with matching names are automatically merged with HTML templates before processing
    /// </summary>
    /// <param name="app_site">The application site name for template key generation</param>
    /// <param name="app_view">The application view name (optional)</param>
    /// <param name="app_file">The application file name</param>
    /// <param name="templates">Dictionary of available templates, where value is tuple of (HTML content, JSON content or null)</param>
    /// <param name="enable_json_processing">Whether to enable JSON data processing</param>
    /// <returns>HTML with placeholders replaced</returns>
    pub fn merge_templates(
        &self,
        app_site: &str,
        app_file: &str,
        app_view: Option<&str>,
        templates: &mut HashMap<String, (String, Option<String>)>,
        enable_json_processing: bool,
    ) -> String {
        if templates.is_empty() {
            return String::new();
        }

        // Use the unified get_template method to retrieve the main template (html and json)
        let (main_template_html, main_template_json) = self.get_template(
            app_site,
            app_file,
            templates,
            app_view,
            true,
        );
        
        let mut content_html = match main_template_html {
            Some(html) => html,
            None => return String::new(),
        };

        // Apply JSON merging to the main template if it has JSON and JSON processing is enabled
        if enable_json_processing {
            if let Some(json) = main_template_json.clone() {
                content_html = Self::merge_template_with_json(&content_html, &json);
            }
        }

        // Step 2: Process each template with its associated JSON (matching C# approach exactly)
        let mut processed_templates: HashMap<String, String> = HashMap::new();
        let mut all_json_values: HashMap<String, String> = HashMap::new();
        
        // Add main template JSON values to the global collection if it exists
        if enable_json_processing {
            if let Some(json) = main_template_json.clone() {
                let json_obj = JsonConverter::parse_json_string(&json);
                for (k, v) in json_obj.iter() {
                    if let Some(s) = v.as_str() {
                        all_json_values.insert(k.clone(), s.to_string());
                    }
                }
            }
        }
        
        for (key, (html_content, json_content)) in templates.iter() {
            let mut processed_html = html_content.clone();
            
            // If JSON exists and JSON processing is enabled, merge it with the HTML template first
            if enable_json_processing {
                if let Some(json) = json_content {
                    processed_html = Self::merge_template_with_json(&processed_html, json);
                    
                    // Parse JSON and collect key-value pairs using our JsonConverter (matching C# exactly)
                    let json_obj = JsonConverter::parse_json_string(json);
                    for (k, v) in json_obj.iter() {
                        if let Some(s) = v.as_str() {
                            all_json_values.insert(k.clone(), s.to_string());
                        }
                    }
                }
            }
            processed_templates.insert(key.clone(), processed_html);
        }

        let mut previous;
        loop {
            previous = content_html.clone();
            content_html = self.merge_template_slots(&content_html, app_site, app_view, &processed_templates);
            content_html = self.replace_template_placeholders_with_json(&content_html, app_site, &processed_templates, &all_json_values, app_view);
            if content_html == previous {
                break;
            }
        }
        
        content_html
    }
}

impl EngineNormal {
    // Merge Templates

    /// <summary>
    /// Retrieves a template (html and json) from the templates dictionary based on various scenarios including AppView fallback logic
    /// </summary>
    fn get_template(
        &self,
        app_site: &str,
        template_name: &str,
        templates: &HashMap<String, (String, Option<String>)>,
        app_view: Option<&str>,
        use_app_view_fallback: bool,
    ) -> (Option<String>, Option<String>) {
        if templates.is_empty() {
            return (None, None);
        }

        let view_prefix = &self.app_view_prefix;
        let primary_template_key = format!("{}_{}", app_site.to_lowercase(), template_name.to_lowercase());
        let found_key = templates.keys().find(|k| k.eq_ignore_ascii_case(&primary_template_key));
        
        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if use_app_view_fallback && !view_prefix.is_empty() {
            if let Some(app_view) = app_view {
                // Case-insensitive check if template_name contains view_prefix
                let template_name_lower = template_name.to_lowercase();
                let view_prefix_lower = view_prefix.to_lowercase();
                
                if template_name_lower.contains(&view_prefix_lower) {
                    // Direct replacement: Replace the AppViewPrefix with the AppView value
                    // For example: Html3AContent with AppViewPrefix=Html3A and AppView=html3B becomes html3BContent
                    let app_key = Self::replace_case_insensitive(template_name, view_prefix, app_view);
                    let fallback_template_key = format!("{}_{}", app_site.to_lowercase(), app_key.to_lowercase());
                    if let Some(fallback_found_key) = templates.keys().find(|k| k.eq_ignore_ascii_case(&fallback_template_key)) {
                        if let Some((html, json)) = templates.get(fallback_found_key) {
                            return (Some(html.clone()), json.clone());
                        }
                    }
                }
            }
        }
        
        // SECOND: If no AppView-specific template found, try primary template
        if let Some(found_key) = found_key {
            if let Some((html, json)) = templates.get(found_key) {
                return (Some(html.clone()), json.clone());
            }
        }
        
        (None, None)
    }

    // FIXED: Hybrid approach that correctly handles both template and JSON placeholders
    fn replace_template_placeholders_with_json(
        &self,
        html: &str,
        app_site: &str,
        processed_templates: &HashMap<String, String>,
        json_values: &HashMap<String, String>,
        app_view: Option<&str>,
    ) -> String {
        let mut result = html.to_string();
        let mut search_pos = 0;

        // This temporary map is needed to reuse the complex `get_template` logic
        // which expects a specific tuple structure.
        let temp_templates_for_lookup: HashMap<String, (String, Option<String>)> =
            processed_templates.iter().map(|(k, v)| (k.clone(), (v.clone(), None))).collect();

        while search_pos < result.len() {
            if let Some(open_start) = result[search_pos..].find("{{") {
                let open_start = search_pos + open_start;
                
                // Make sure it's not a slot placeholder, which are handled separately
                if open_start + 2 < result.len() {
                    let c = result.chars().nth(open_start + 2).unwrap_or(' ');
                    if c == '#' || c == '@' || c == '/' { // Don't skip '$' placeholders here!
                        search_pos = open_start + 2;
                        continue;
                    }
                }

                if let Some(close_start) = result[open_start + 2..].find("}}") {
                    let close_start = open_start + 2 + close_start;
                    
                    let placeholder_name = result[open_start + 2..close_start].trim();
                    if placeholder_name.is_empty() {
                        search_pos = open_start + 2;
                        continue;
                    }

                    let mut processed_replacement: Option<String> = None;

                    // PRIORITY 1: Check for JSON placeholders first (starts with '$')
                    if placeholder_name.starts_with('$') {
                        let key = &placeholder_name[1..]; // Remove the leading '$'
                        if let Some(json_value) = json_values.get(key) {
                            processed_replacement = Some(json_value.clone());
                        }
                    }
                    // PRIORITY 2: Check for template placeholders (alphanumeric)
                    else if TemplateUtils::is_alphanumeric(placeholder_name) {
                        let (template_content, _) = self.get_template(
                            app_site,
                            placeholder_name,
                            &temp_templates_for_lookup,
                            app_view,
                            true,
                        );

                        if let Some(tc) = template_content {
                            // Recursively process nested placeholders within the replacement template
                            processed_replacement = Some(self.replace_template_placeholders_with_json(
                                &tc, app_site, processed_templates, json_values, app_view
                            ));
                        }
                        // PRIORITY 3: If no template found, try JSON values (for backward compatibility)
                        else if let Some(json_value) = json_values.get(placeholder_name) {
                            processed_replacement = Some(json_value.clone());
                        }
                    }

                    if let Some(replacement) = processed_replacement {
                        let placeholder = &result[open_start..close_start + 2];
                        result = result.replacen(placeholder, &replacement, 1);
                        search_pos = open_start + replacement.len();
                    } else {
                        search_pos = close_start + 2;
                    }
                } else {
                    break;
                }
            } else {
                break;
            }
        }
        result
    }

    // Slot Processing

    /// <summary>
    /// IndexOf-based version: Recursively merges a slotted template (e.g., center.html, columns.html) with content.html
    /// Slot patterns in content.html: {{#TemplateName}} ... {{@HTMLPLACEHOLDER[N]}} ... {{/HTMLPLACEHOLDER[N]}} ... {{/TemplateName}}
    /// </summary>
    /// <param name="content_html">The content HTML containing slot patterns</param>
    /// <param name="app_site">The application site name for template key generation</param>
    /// <param name="templates">Dictionary of available templates</param>
    /// <returns>Merged HTML with slots filled</returns>
    fn merge_template_slots(
        &self,
        content_html: &str,
        app_site: &str,
        app_view: Option<&str>,
        templates: &HashMap<String, String>,
    ) -> String {
        if content_html.is_empty() || templates.is_empty() {
            return content_html.to_string();
        }

        let mut previous;
        let mut result = content_html.to_string();
        loop {
            previous = result.clone();
            result = self.process_template_slots(&result, app_site, app_view, templates);
            if result == previous {
                break;
            }
        }
        result
    }

    /// <summary>
    /// Helper method to process slotted templates using IndexOf
    /// </summary>
    fn process_template_slots(
        &self,
        content_html: &str,
        app_site: &str,
        app_view: Option<&str>,
        templates: &HashMap<String, String>,
    ) -> String {
        let mut result = content_html.to_string();
        let mut search_pos = 0;

        while search_pos < result.len() {
            // Look for opening tag {{#
            if let Some(open_start) = result[search_pos..].find("{{#") {
                let open_start = search_pos + open_start;
                
                // Find the end of the template name
                if let Some(open_end) = result[open_start + 3..].find("}}") {
                    let open_end = open_start + 3 + open_end;
                    
                    // Extract template name
                    let template_name = result[open_start + 3..open_end].trim();
                    if template_name.is_empty() || !TemplateUtils::is_alphanumeric(template_name) {
                        search_pos = open_start + 1;
                        continue;
                    }

                    // Look for corresponding closing tag
                    let close_tag = format!("{{{{/{}}}}}", template_name);
                    if let Some(close_start) = TemplateUtils::find_matching_close_tag(
                        &result, open_end + 2, &format!("{{{{#{}}}}}", template_name), &close_tag) {
                        
                        // Extract inner content
                        let inner_start = open_end + 2;
                        let inner_content = &result[inner_start..close_start];

                        // Process the template replacement using the get_template method
                        let (template_html, _) = self.get_template(
                            app_site,
                            template_name,
                            &templates.iter().map(|(k, v)| (k.clone(), (v.clone(), None))).collect(),
                            app_view,
                            true,
                        );

                        if let Some(template_html) = template_html {
                            // Extract slot contents
                            let slot_contents = self.extract_slot_contents(inner_content, app_site, app_view, templates);

                            // Replace slots in template
                            let mut processed_template = template_html;
                            for (k, v) in slot_contents.iter() {
                                processed_template = processed_template.replace(k, v);
                            }

                            // Remove any remaining slot placeholders
                            processed_template = crate::template_common::template_utils::TemplateUtils::remove_remaining_slot_placeholders(&processed_template);

                            // Replace the entire slotted section
                            let full_match = &result[open_start..close_start + close_tag.len()];
                            result = result.replacen(full_match, &processed_template, 1);
                            search_pos = open_start + processed_template.len();
                        } else {
                            search_pos = open_start + 1;
                        }
                    } else {
                        search_pos = open_start + 1;
                    }
                } else {
                    break;
                }
            } else {
                break;
            }
        }

        result
    }

    /// <summary>
    /// Extract slot contents using IndexOf approach
    /// </summary>
    fn extract_slot_contents(
        &self,
        inner_content: &str,
        app_site: &str,
        app_view: Option<&str>,
        templates: &HashMap<String, String>,
    ) -> HashMap<String, String> {
        let mut slot_contents = HashMap::new();
        let mut search_pos = 0;

        while search_pos < inner_content.len() {
            // Look for slot start {{@HTMLPLACEHOLDER
            if let Some(slot_start) = inner_content[search_pos..].find("{{@HTMLPLACEHOLDER") {
                let slot_start = search_pos + slot_start;
                
                // Find the number (if any) and closing }}
                let after_placeholder = slot_start + 18; // Length of "{{@HTMLPLACEHOLDER"
                let mut slot_num = String::new();
                let mut pos = after_placeholder;

                // Extract slot number
                while pos < inner_content.len() && inner_content.chars().nth(pos).unwrap_or(' ').is_digit(10) {
                    slot_num.push(inner_content.chars().nth(pos).unwrap());
                    pos += 1;
                }

                // Check for closing }}
                if pos + 1 >= inner_content.len() || &inner_content[pos..pos + 2] != "}}" {
                    search_pos = slot_start + 1;
                    continue;
                }

                let slot_open_end = pos + 2;

                // Find matching closing tag
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

                if let Some(close_start) = TemplateUtils::find_matching_close_tag(
                    inner_content, slot_open_end, &open_tag, &close_tag) {
                    
                    // Extract slot content
                    let slot_content = &inner_content[slot_open_end..close_start];

                    // Generate slot key
                    let slot_key = if slot_num.is_empty() {
                        "{{$HTMLPLACEHOLDER}}".to_string()
                    } else {
                        format!("{{{{$HTMLPLACEHOLDER{}}}}}", slot_num)
                    };

                    // FIXED: Process both slotted templates AND simple placeholders in slot content
                    // This enables proper nested template processing to match the preprocessing implementation
                    let mut recursive_result = self.merge_template_slots(slot_content, app_site, app_view, templates);
                    recursive_result = self.replace_template_placeholders(&recursive_result, app_site, app_view, templates);
                    slot_contents.insert(slot_key, recursive_result);

                    search_pos = close_start + close_tag.len();
                } else {
                    search_pos = slot_start + 1;
                }
            } else {
                break;
            }
        }

        slot_contents
    }

    // PlaceHolder Processing

    /// <summary>
    /// Helper method to process simple placeholders only (without slotted template processing)
    /// </summary>
    fn replace_template_placeholders(
        &self,
        html: &str,
        app_site: &str,
        app_view: Option<&str>,
        html_files: &HashMap<String, String>,
    ) -> String {
        let mut result = html.to_string();
        let mut search_pos = 0;

        // Try to get JSON values from the main template if available
        let json_values = if let Some(json_raw) = html_files.get("__json_values__") {
            if !json_raw.is_empty() {
                // Parse as key=value pairs separated by newlines (custom format for this fix)
                json_raw.split('\n')
                    .filter_map(|line| {
                        let parts: Vec<&str> = line.splitn(2, '=').collect();
                        if parts.len() == 2 {
                            Some((parts[0].trim().to_string(), parts[1].trim().to_string()))
                        } else {
                            None
                        }
                    })
                    .collect::<HashMap<String, String>>()
            } else {
                HashMap::new()
            }
        } else {
            HashMap::new()
        };

        while search_pos < result.len() {
            // Look for opening placeholder {{
            if let Some(open_start) = result[search_pos..].find("{{") {
                let open_start = search_pos + open_start;
                
                // Make sure it's not a slotted template or special placeholder
                if open_start + 2 < result.len() {
                    let c = result.chars().nth(open_start + 2).unwrap_or(' ');
                    if c == '#' || c == '@' || c == '$' || c == '/' {
                        search_pos = open_start + 2;
                        continue;
                    }
                }

                // Find closing }}
                if let Some(close_start) = result[open_start + 2..].find("}}") {
                    let close_start = open_start + 2 + close_start;
                    
                    // Extract placeholder name
                    let placeholder_name = result[open_start + 2..close_start].trim();
                    if placeholder_name.is_empty() || !TemplateUtils::is_alphanumeric(placeholder_name) {
                        search_pos = open_start + 2;
                        continue;
                    }

                    // Look up replacement in templates using the get_template method
                    let (template_content, _) = self.get_template(
                        app_site,
                        placeholder_name,
                        &html_files.iter().map(|(k, v)| (k.clone(), (v.clone(), None))).collect(),
                        app_view,
                        true,
                    );

                    let processed_replacement = if let Some(tc) = template_content {
                        Some(self.replace_template_placeholders(&tc, app_site, app_view, html_files))
                    }
                    // If not found, try JSON value
                    else if let Some(json_value) = json_values.get(placeholder_name) {
                        Some(json_value.clone())
                    } else {
                        None
                    };

                    if let Some(replacement) = processed_replacement {
                        let placeholder = &result[open_start..close_start + 2];
                        result = result.replacen(placeholder, &replacement, 1);
                        search_pos = open_start + replacement.len();
                    } else {
                        search_pos = close_start + 2;
                    }
                } else {
                    break;
                }
            } else {
                break;
            }
        }

        result
    }

    // Json Processing

    /// <summary>
    /// Merges HTML template with JSON data using placeholder replacement
    /// </summary>
    /// <param name="template">The HTML template content</param>
    /// <param name="json_text">The JSON data as string</param>
    /// <returns>Merged HTML with JSON data populated</returns>
    fn merge_template_with_json(template: &str, json_text: &str) -> String {
        // Parse JSON using our JsonConverter
        let json_obj = JsonConverter::parse_json_string(json_text);

        // Advanced merge logic for block and conditional patterns
        let mut result = template.to_string();

        // Process array blocks one at a time to avoid index corruption
        'outer: loop {
            let mut processed_any = false;
            
            // First pass: find all {{@TagName}} patterns in current template
            let mut array_tag_names = std::collections::HashSet::new();
            let result_lower = result.to_lowercase();
            let mut start_search = 0;
            
            while let Some(tag_start) = result_lower[start_search..].find("{{@") {
                let tag_start = start_search + tag_start + 3;
                if let Some(tag_end) = result_lower[tag_start..].find("}}") {
                    let tag_end = tag_start + tag_end;
                    let tag_name = &result[tag_start..tag_end].trim().to_lowercase();
                    if !tag_name.is_empty() {
                        array_tag_names.insert(tag_name.clone());
                    }
                    start_search = tag_end + 2;
                } else {
                    break;
                }
            }

            // Second pass: find first matching JSON array to process
            for (json_key, value) in json_obj.iter() {
                if let JsonValue::Array(data_list) = value {
                    let key_norm = json_key.to_lowercase();
                    
                    // Find matching template tag for this JSON key using Node.js logic
                    let mut matching_tag: Option<String> = None;
                    
                    // Try exact key match first
                    if array_tag_names.contains(&key_norm) {
                        matching_tag = Some(key_norm.clone());
                    }
                    // Try singular form (remove trailing 's')
                    else if key_norm.ends_with('s') {
                        let singular = key_norm.trim_end_matches('s');
                        if array_tag_names.contains(singular) {
                            matching_tag = Some(singular.to_string());
                        }
                    }
                    // Try plural form (add 's')
                    else {
                        let plural = format!("{}s", key_norm);
                        if array_tag_names.contains(&plural) {
                            matching_tag = Some(plural);
                        }
                    }

                    if let Some(tag) = matching_tag {
                        let block_start_tag = format!("{{{{@{}}}}}", tag);
                        let block_end_tag = format!("{{{{/{}}}}}", tag);

                        if let Some(start_idx) = Self::find_case_insensitive(&result, &block_start_tag) {
                            let search_from = start_idx + block_start_tag.len();
                            if let Some(relative_end_idx) = Self::find_case_insensitive(&result[search_from..], &block_end_tag) {
                                let end_idx = search_from + relative_end_idx;
                                
                                let content_start_idx = start_idx + block_start_tag.len();
                                let block_content = &result[content_start_idx..end_idx];
                                let mut merged_block = String::new();

                                // Collect all conditional keys in this block
                                let mut conditional_keys = std::collections::HashSet::new();
                                let mut cond_idx = 0;
                                while let Some(cond_start) = block_content[cond_idx..].to_lowercase().find("{{@") {
                                    let cond_start = cond_idx + cond_start;
                                    if let Some(cond_end) = block_content[cond_start..].find("}}") {
                                        let cond_end = cond_start + cond_end;
                                        let cond_key = block_content[cond_start + 3..cond_end].trim();
                                        conditional_keys.insert(cond_key.to_string());
                                        cond_idx = cond_end + 2;
                                    } else {
                                        break;
                                    }
                                }

                                for item in data_list.iter() {
                                    if let JsonValue::Object(item_obj) = item {
                                        let mut item_block = block_content.to_string();

                                        // Replace placeholders
                                        for (k, v) in item_obj.iter() {
                                            let placeholder = format!("{{{{${}}}}}", k);
                                            let value_str = match v {
                                                JsonValue::String(s) => s.clone(),
                                                JsonValue::Number(n) => n.to_string(),
                                                JsonValue::Integer(i) => i.to_string(),
                                                JsonValue::Bool(b) => b.to_string(),
                                                _ => String::new(),
                                            };
                                            item_block = Self::replace_all_case_insensitive(&item_block, &placeholder, &value_str);
                                        }

                                        // Handle conditionals
                                        for cond_key in &conditional_keys {
                                            let mut cond_value = false;
                                            for (obj_key, cond_obj) in item_obj.iter() {
                                                if obj_key.to_lowercase() == cond_key.to_lowercase() {
                                                    cond_value = match cond_obj {
                                                        JsonValue::Bool(b) => *b,
                                                        JsonValue::String(s) => s.parse::<bool>().unwrap_or(!s.is_empty()),
                                                        JsonValue::Integer(i) => *i != 0,
                                                        JsonValue::Number(n) => *n != 0.0,
                                                        _ => false,
                                                    };
                                                    break;
                                                }
                                            }
                                            item_block = Self::handle_conditional(&item_block, cond_key, cond_value);
                                        }
                                        merged_block.push_str(&item_block);
                                    }
                                }

                                result = format!("{}{}{}", 
                                    &result[..start_idx], 
                                    merged_block, 
                                    &result[end_idx + block_end_tag.len()..]
                                );
                                processed_any = true;
                                break; // Process one array at a time
                            }
                        }
                    }
                }
            }
            
            if !processed_any {
                break 'outer; // No more arrays to process
            }
        }

        // Handle {{^ArrayName}} block if array is empty (dynamic detection)
        for (key, value) in json_obj.iter() {
            let empty_block_start = format!("{{{{^{}}}}}", key);
            let empty_block_end = format!("{{{{/{}}}}}", key);
            
            if let Some(empty_start_idx) = Self::find_case_insensitive(&result, &empty_block_start) {
                if let Some(empty_end_idx) = Self::find_case_insensitive(&result, &empty_block_end) {
                    if let JsonValue::Array(l) = value {
                        let is_empty = l.is_empty();
                        let empty_content = &result[empty_start_idx + empty_block_start.len()..empty_end_idx];
                        result = if is_empty {
                            format!("{}{}{}", 
                                &result[..empty_start_idx], 
                                empty_content, 
                                &result[empty_end_idx + empty_block_end.len()..]
                            )
                        } else {
                            format!("{}{}", 
                                &result[..empty_start_idx], 
                                &result[empty_end_idx + empty_block_end.len()..]
                            )
                        };
                    }
                }
            }
        }

        // Replace remaining simple placeholders
        for (key, value) in json_obj.iter() {
            let placeholder = format!("{{{{${}}}}}", key);
            let value_str = match value {
                JsonValue::String(s) => s.clone(),
                JsonValue::Number(n) => n.to_string(),
                JsonValue::Integer(i) => i.to_string(),
                JsonValue::Bool(b) => b.to_string(),
                _ => continue, // Skip arrays and objects as they're handled above
            };
            
            // Use simple case-sensitive replacement for exact matches first
            if result.contains(&placeholder) {
                result = result.replace(&placeholder, &value_str);
            } else {
                // Fallback to case-insensitive if needed
                result = Self::replace_all_case_insensitive(&result, &placeholder, &value_str);
            }
        }

        result
    }

    /// <summary>
    /// Helper: Replace all case-insensitive occurrences
    /// </summary>
    fn replace_all_case_insensitive(input: &str, search: &str, replacement: &str) -> String {
        let mut result = input.to_string();
        let mut idx = 0;
        loop {
            if let Some(found) = Self::find_case_insensitive(&result[idx..], search) {
                let found = idx + found;
                result.replace_range(found..found + search.len(), replacement);
                idx = found + replacement.len();
            } else {
                break;
            }
        }
        result
    }

    /// <summary>
    /// Helper: Handle conditional blocks like {{@Selected}}...{{/Selected}}
    /// </summary>
    fn handle_conditional(input: &str, key: &str, condition: bool) -> String {
        let mut result = input.to_string();
        
        // Support spaces inside block tags, e.g. {{@Selected}} ... {{ /Selected}}
        let cond_start = format!("{{{{@{}}}}}", key);
        let cond_end_space = format!("{{{{ /{}}}}}", key);
        
        // Handle first pattern: {{ /Key}} (with space)
        loop {
            if let Some(start_idx) = Self::find_case_insensitive(&result, &cond_start) {
                if let Some(end_idx) = Self::find_case_insensitive(&result, &cond_end_space) {
                    let content = &result[start_idx + cond_start.len()..end_idx];
                    result = if condition {
                        format!("{}{}{}", 
                            &result[..start_idx], 
                            content, 
                            &result[end_idx + cond_end_space.len()..]
                        )
                    } else {
                        format!("{}{}", 
                            &result[..start_idx], 
                            &result[end_idx + cond_end_space.len()..]
                        )
                    };
                } else {
                    break;
                }
            } else {
                break;
            }
        }
        
        // Also handle without space: {{/Key}}
        let cond_end_no_space = format!("{{{{/{}}}}}", key);
        loop {
            if let Some(start_idx) = Self::find_case_insensitive(&result, &cond_start) {
                if let Some(end_idx) = Self::find_case_insensitive(&result, &cond_end_no_space) {
                    let content = &result[start_idx + cond_start.len()..end_idx];
                    result = if condition {
                        format!("{}{}{}", 
                            &result[..start_idx], 
                            content, 
                            &result[end_idx + cond_end_no_space.len()..]
                        )
                    } else {
                        format!("{}{}", 
                            &result[..start_idx], 
                            &result[end_idx + cond_end_no_space.len()..]
                        )
                    };
                } else {
                    break;
                }
            } else {
                break;
            }
        }
        
        result
    }

    /// <summary>
    /// Helper: Find case-insensitive occurrence of search string
    /// </summary>
    fn find_case_insensitive(haystack: &str, needle: &str) -> Option<usize> {
        haystack.to_lowercase().find(&needle.to_lowercase())
    }
}

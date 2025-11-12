use crate::common::common_util::CommonUtil;
use crate::interface::ILoaderJson;
use arshu::common::Logger;
use std::collections::HashMap;

/// NEW Normal engine that uses ILoaderJson interface and JsonObject
/// Based on EngineNormal but with improved architecture and type safety
pub struct EngineNormalJson {
    app_view_prefix: String,
}

impl EngineNormalJson {
    pub fn new(prefix: String) -> Self {
        Self {
            app_view_prefix: prefix,
        }
    }

    pub fn app_view_prefix(&self) -> &str {
        &self.app_view_prefix
    }

    pub fn set_app_view_prefix(&mut self, prefix: String) {
        self.app_view_prefix = prefix;
    }

    /// Merges templates by replacing placeholders with corresponding HTML
    /// This is a hybrid method that processes both slotted templates and simple placeholders
    /// JSON files with matching names are automatically merged with HTML templates before processing
    ///
    /// # Arguments
    /// * `app_site` - The application site name for template key generation
    /// * `app_file` - The application file name
    /// * `app_view` - The application view name (optional)
    /// * `loader` - ILoaderJson instance providing templates and JSON merging
    /// * `enable_json_processing` - Whether to enable JSON data processing
    ///
    /// # Returns
    /// HTML with placeholders replaced
    pub fn merge_templates(
        &self,
        app_site: &str,
        app_file: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderJson<String>,
        enable_json_processing: bool,
    ) -> String {
        let app_view_str = app_view.unwrap_or("null");
        Logger::debug(
            &format!(
                "MergeTemplates called: appSite={}, appFile={}, appView={}, enableJson={}",
                app_site, app_file, app_view_str, enable_json_processing
            ),
            Some("EngineNormalJson"),
        );

        // Get main template using ILoaderJson
        // FIRST: Try direct lookup without AppView overrides to match C# behavior
        let mut content_html = loader.get_template_html(app_site, app_file, None, None);
        if content_html.is_none() {
            content_html =
                loader.get_template_html(app_site, app_file, app_view, Some(&self.app_view_prefix));
        }
        let mut content_html = match content_html {
            Some(html) => html,
            None => {
                Logger::warn(
                    &format!(
                        "Main template not found for appSite={}, appFile={}",
                        app_site, app_file
                    ),
                    Some("EngineNormalJson"),
                );
                return String::new();
            }
        };

        Logger::debug(
            &format!("Main template found, html size: {}", content_html.len()),
            Some("EngineNormalJson"),
        );

        // Merge main template with JSON data - engine handles merging
        if enable_json_processing {
            content_html = loader.merge_html_with_json(&content_html, app_site, app_file);
            Logger::debug(
                &format!("After main JSON merge: {} chars", content_html.len()),
                Some("EngineNormalJson"),
            );
        }

        // Simple loop like Go implementation - avoid StringBuilder overhead
        let max_passes = 10;
        let mut actual_passes = 0;

        for pass in 0..max_passes {
            let previous = content_html.clone();
            actual_passes = pass + 1;

            Logger::debug(
                &format!(
                    "Pass {}, current size: {}",
                    actual_passes,
                    content_html.len()
                ),
                Some("EngineNormalJson"),
            );

            content_html = self.merge_template_slots(
                &content_html,
                app_site,
                app_view,
                loader,
                enable_json_processing,
            );
            Logger::debug(
                &format!("After slot merge: {} chars", content_html.len()),
                Some("EngineNormalJson"),
            );

            content_html = self.replace_template_placeholders(
                &content_html,
                app_site,
                app_view,
                loader,
                enable_json_processing,
            );
            Logger::debug(
                &format!(
                    "After placeholder replacement: {} chars",
                    content_html.len()
                ),
                Some("EngineNormalJson"),
            );

            if content_html == previous {
                Logger::debug(
                    &format!("No changes in pass {}, stopping", actual_passes),
                    Some("EngineNormalJson"),
                );
                break;
            }
        }

        Logger::debug(
            &format!(
                "MergeTemplates complete after {} passes: output size={}",
                actual_passes,
                content_html.len()
            ),
            Some("EngineNormalJson"),
        );

        content_html
    }

    /// Gets a template with on-demand loading and JSON merging from ILoaderJson
    fn get_template_with_json(
        &self,
        app_site: &str,
        template_name: &str,
        loader: &dyn ILoaderJson<String>,
        app_view: Option<&str>,
        enable_json_processing: bool,
    ) -> Option<String> {
        // Get HTML template
        let mut html = loader.get_template_html(
            app_site,
            template_name,
            app_view,
            Some(&self.app_view_prefix),
        )?;

        Logger::debug(
            &format!(
                "GetTemplateWithJson: template={}, html size={}",
                template_name,
                html.len()
            ),
            Some("EngineNormalJson"),
        );

        // Merge with JSON if enabled - engine handles merging
        if enable_json_processing {
            let original_size = html.len();
            html = loader.merge_html_with_json(&html, app_site, template_name);
            Logger::debug(
                &format!(
                    "After JSON merge for {}: size {} -> {}",
                    template_name,
                    original_size,
                    html.len()
                ),
                Some("EngineNormalJson"),
            );
        }

        Some(html)
    }

    // Slot Processing

    /// IndexOf-based version: Recursively merges a slotted template with content
    /// Slot patterns in content: {{#TemplateName}} ... {{@HTMLPLACEHOLDER[N]}} ... {{/HTMLPLACEHOLDER[N]}} ... {{/TemplateName}}
    fn merge_template_slots(
        &self,
        content_html: &str,
        app_site: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderJson<String>,
        enable_json_processing: bool,
    ) -> String {
        if content_html.is_empty() {
            return content_html.to_string();
        }

        let mut result = content_html.to_string();
        let mut previous;

        loop {
            previous = result.clone();
            result = self.process_template_slots(
                &result,
                app_site,
                app_view,
                loader,
                enable_json_processing,
            );
            if result == previous {
                break;
            }
        }

        result
    }

    /// Helper method to process slotted templates using IndexOf
    fn process_template_slots(
        &self,
        content_html: &str,
        app_site: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderJson<String>,
        enable_json_processing: bool,
    ) -> String {
        let mut result = content_html.to_string();
        let mut search_pos = 0;

        while search_pos < result.len() {
            // Look for opening tag {{#
            let open_start = match result[search_pos..].find("{{#") {
                Some(pos) => search_pos + pos,
                None => break,
            };

            // Find the end of the template name
            let open_end = match result[open_start + 3..].find("}}") {
                Some(pos) => open_start + 3 + pos,
                None => break,
            };

            // Extract template name
            let template_name = result[open_start + 3..open_end].trim();
            if template_name.is_empty() || !CommonUtil::is_alphanumeric(template_name) {
                search_pos = open_start + 1;
                continue;
            }

            // Look for corresponding closing tag
            let close_tag = format!("{{{{/{}}}}}", template_name);
            let open_tag = format!("{{{{#{}}}}}", template_name);
            let close_start = match CommonUtil::find_matching_close_tag(
                &result,
                open_end + 2,
                &open_tag,
                &close_tag,
            ) {
                Some(pos) => pos,
                None => {
                    search_pos = open_start + 1;
                    continue;
                }
            };

            // Extract inner content
            let inner_start = open_end + 2;
            let inner_content = &result[inner_start..close_start];

            // Load template with JSON on-demand
            if let Some(template_html) = self.get_template_with_json(
                app_site,
                template_name,
                loader,
                app_view,
                enable_json_processing,
            ) {
                // Extract slot contents
                let slot_contents = self.extract_slot_contents(
                    inner_content,
                    app_site,
                    app_view,
                    loader,
                    enable_json_processing,
                );

                // Replace slots in template
                let mut processed_template = template_html;
                for (slot_key, slot_value) in slot_contents {
                    processed_template = processed_template.replace(&slot_key, &slot_value);
                }

                // Remove any remaining slot placeholders
                processed_template =
                    CommonUtil::remove_remaining_slot_placeholders(&processed_template);

                // Replace the entire slotted section
                let full_match_start = open_start;
                let full_match_end = close_start + close_tag.len();
                result = format!(
                    "{}{}{}",
                    &result[..full_match_start],
                    processed_template,
                    &result[full_match_end..]
                );
                search_pos = full_match_start + processed_template.len();
            } else {
                search_pos = open_start + 1;
            }
        }

        result
    }

    /// Extract slot contents using IndexOf approach
    fn extract_slot_contents(
        &self,
        inner_content: &str,
        app_site: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderJson<String>,
        enable_json_processing: bool,
    ) -> HashMap<String, String> {
        let mut slot_contents = HashMap::new();
        let mut search_pos = 0;

        while search_pos < inner_content.len() {
            // Look for slot start {{@HTMLPLACEHOLDER
            let slot_start = match inner_content[search_pos..].find("{{@HTMLPLACEHOLDER") {
                Some(pos) => search_pos + pos,
                None => break,
            };

            // Find the number (if any) and closing }}
            let after_placeholder = slot_start + 18; // Length of "{{@HTMLPLACEHOLDER"
            let mut slot_num = String::new();
            let mut pos = after_placeholder;

            // Extract slot number
            while pos < inner_content.len() {
                let byte = inner_content.as_bytes()[pos];
                if byte.is_ascii_digit() {
                    slot_num.push(byte as char);
                    pos += 1;
                } else {
                    break;
                }
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

            let close_start = match CommonUtil::find_matching_close_tag(
                inner_content,
                slot_open_end,
                &open_tag,
                &close_tag,
            ) {
                Some(pos) => pos,
                None => {
                    search_pos = slot_start + 1;
                    continue;
                }
            };

            // Extract slot content
            let slot_content = &inner_content[slot_open_end..close_start];

            // Generate slot key
            let slot_key = if slot_num.is_empty() {
                "{{$HTMLPLACEHOLDER}}".to_string()
            } else {
                format!("{{{{$HTMLPLACEHOLDER{}}}}}", slot_num)
            };

            // Process both slotted templates AND simple placeholders in slot content
            let mut recursive_result = self.merge_template_slots(
                slot_content,
                app_site,
                app_view,
                loader,
                enable_json_processing,
            );
            recursive_result = self.replace_template_placeholders(
                &recursive_result,
                app_site,
                app_view,
                loader,
                enable_json_processing,
            );
            slot_contents.insert(slot_key, recursive_result);

            search_pos = close_start + close_tag.len();
        }

        slot_contents
    }

    // PlaceHolder Processing

    /// Helper method to process simple placeholders only (without slotted template processing)
    fn replace_template_placeholders(
        &self,
        html: &str,
        app_site: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderJson<String>,
        enable_json_processing: bool,
    ) -> String {
        let mut result = html.to_string();
        let mut search_pos = 0;

        while search_pos < result.len() {
            // Look for opening placeholder {{
            let open_start = match result[search_pos..].find("{{") {
                Some(pos) => search_pos + pos,
                None => break,
            };

            // Make sure it's not a slotted template or special placeholder
            if open_start + 2 < result.len() {
                let next_byte = result.as_bytes()[open_start + 2];
                if next_byte == b'#' || next_byte == b'@' || next_byte == b'$' || next_byte == b'/'
                {
                    search_pos = open_start + 2;
                    continue;
                }
            }

            // Find closing }}
            let close_start = match result[open_start + 2..].find("}}") {
                Some(pos) => open_start + 2 + pos,
                None => break,
            };

            // Extract placeholder name
            let placeholder_name = result[open_start + 2..close_start].trim();
            if placeholder_name.is_empty() || !CommonUtil::is_alphanumeric(placeholder_name) {
                search_pos = open_start + 2;
                continue;
            }

            // Load template with JSON on-demand
            if let Some(template_content) = self.get_template_with_json(
                app_site,
                placeholder_name,
                loader,
                app_view,
                enable_json_processing,
            ) {
                // Recursively process the loaded template
                let processed_replacement = self.replace_template_placeholders(
                    &template_content,
                    app_site,
                    app_view,
                    loader,
                    enable_json_processing,
                );
                let placeholder = &result[open_start..close_start + 2];
                result = result.replace(placeholder, &processed_replacement);
                search_pos = open_start + processed_replacement.len();
            } else {
                search_pos = close_start + 2;
            }
        }

        result
    }
}

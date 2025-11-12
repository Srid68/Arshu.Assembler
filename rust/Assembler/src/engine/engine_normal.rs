use crate::common::common_util::CommonUtil;
use crate::interface::ILoaderNormal;
use arshu::common::Logger;
use std::collections::HashMap;

/// IndexOf-based template engine implementation for improved performance
/// Matches C# EngineNormal structure - uses ILoaderNormal for template access
pub struct EngineNormal {
    app_view_prefix: String,
}

impl EngineNormal {
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
    pub fn merge_templates(
        &self,
        app_site: &str,
        app_file: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderNormal,
        enable_json_processing: bool,
    ) -> String {
        let app_view_str = app_view.unwrap_or("null");
        Logger::debug(
            &format!(
                "MergeTemplates called: appSite={}, appFile={}, appView={}, enableJson={}",
                app_site, app_file, app_view_str, enable_json_processing
            ),
            Some("EngineNormal"),
        );

        // Get main template using loader (includes AppView fallback and SearchAppSites logic)
        let mut content_html = match loader.get_template_html(
            app_site,
            app_file,
            app_view,
            Some(&self.app_view_prefix),
        ) {
            Some(html) => html,
            None => {
                Logger::warn(
                    &format!(
                        "Main template not found for appSite={}, appFile={}",
                        app_site, app_file
                    ),
                    Some("EngineNormal"),
                );
                return String::new();
            }
        };

        Logger::debug(
            &format!("Main template found, html size: {}", content_html.len()),
            Some("EngineNormal"),
        );

        // Merge main template with JSON using loader's centralized method
        if enable_json_processing {
            Logger::debug("Merging main template with JSON", Some("EngineNormal"));
            content_html = loader.merge_html_with_json(&content_html, app_site, app_file);
            Logger::debug(
                &format!("After main JSON merge: {} chars", content_html.len()),
                Some("EngineNormal"),
            );
        }

        // Simple loop - templates are now loaded on-demand via loader
        let max_passes = 10;
        let mut actual_passes = 0;
        for pass in 0..max_passes {
            let previous = content_html.clone();
            actual_passes = pass + 1;

            Logger::debug(
                &format!("Pass {}, current size: {}", actual_passes, content_html.len()),
                Some("EngineNormal"),
            );

            content_html = self.merge_template_slots(&content_html, app_site, app_view, loader, enable_json_processing);
            Logger::debug(
                &format!("After slot merge: {} chars", content_html.len()),
                Some("EngineNormal"),
            );

            content_html = self.replace_template_placeholders(&content_html, app_site, app_view, loader, enable_json_processing);
            Logger::debug(
                &format!("After placeholder replacement: {} chars", content_html.len()),
                Some("EngineNormal"),
            );

            if content_html == previous {
                Logger::debug(
                    &format!("No changes in pass {}, stopping", actual_passes),
                    Some("EngineNormal"),
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
            Some("EngineNormal"),
        );
        content_html
    }

    /// Gets a template with on-demand loading and JSON merging from ILoaderNormal
    fn get_template_with_json(
        &self,
        app_site: &str,
        template_name: &str,
        loader: &dyn ILoaderNormal,
        app_view: Option<&str>,
        enable_json_processing: bool,
    ) -> Option<String> {
        // Get HTML template (includes AppView fallback and SearchAppSites logic)
        let mut html = loader.get_template_html(app_site, template_name, app_view, Some(&self.app_view_prefix))?;

        Logger::debug(
            &format!(
                "GetTemplateWithJson: template={}, html size={}",
                template_name,
                html.len()
            ),
            Some("EngineNormal"),
        );

        // Merge with JSON if enabled using loader's centralized method
        if enable_json_processing {
            let original_size = html.len();
            html = loader.merge_html_with_json(&html, app_site, template_name);
            Logger::debug(
                &format!(
                    "After JSON merge for {}: size {} -> {}",
                    template_name, original_size, html.len()
                ),
                Some("EngineNormal"),
            );
        }

        Some(html)
    }

    /// Recursively merges slotted templates
    /// Slot patterns: {{#TemplateName}} ... {{@HTMLPLACEHOLDER[N]}} ... {{/HTMLPLACEHOLDER[N]}} ... {{/TemplateName}}
    fn merge_template_slots(
        &self,
        content_html: &str,
        app_site: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderNormal,
        enable_json_processing: bool,
    ) -> String {
        if content_html.is_empty() {
            return content_html.to_string();
        }

        let mut result = String::with_capacity(content_html.len() * 2);
        let mut search_pos = 0;

        while search_pos < content_html.len() {
            // Find opening slot marker {{#TemplateName}}
            let open_start = match content_html[search_pos..].find("{{#") {
                Some(pos) => search_pos + pos,
                None => {
                    result.push_str(&content_html[search_pos..]);
                    break;
                }
            };

            // Add content before slot
            result.push_str(&content_html[search_pos..open_start]);

            let open_end = match content_html[open_start + 3..].find("}}") {
                Some(pos) => open_start + 3 + pos,
                None => {
                    result.push_str(&content_html[open_start..]);
                    break;
                }
            };

            let template_name = content_html[open_start + 3..open_end].trim();
            let closing_tag = format!("{{{{/{}}}}}", template_name);

            let close_start = match CommonUtil::find_matching_close_tag(
                content_html,
                open_end + 2,
                &format!("{{{{#{}}}}}", template_name),
                &closing_tag,
            ) {
                Some(pos) => pos,
                None => {
                    result.push_str(&content_html[open_start..]);
                    search_pos = open_start + 1;
                    continue;
                }
            };

            // Extract slot content between opening and closing tags (inner content)
            let inner_content = &content_html[open_end + 2..close_start];

            // Get the slot template
            let slot_template = match self.get_template_with_json(
                app_site,
                template_name,
                loader,
                app_view,
                enable_json_processing,
            ) {
                Some(tmpl) => tmpl,
                None => {
                    Logger::warn(
                        &format!("Slot template '{}' not found", template_name),
                        Some("EngineNormal"),
                    );
                    result.push_str(&content_html[open_start..close_start + closing_tag.len()]);
                    search_pos = close_start + closing_tag.len();
                    continue;
                }
            };

            // Extract slot contents from inner_content
            let slot_contents = self.extract_slot_contents(inner_content, app_site, app_view, loader, enable_json_processing);

            // Replace slot placeholders in the slotted template
            let mut merged_slot = slot_template.clone();
            for (slot_key, slot_content) in slot_contents {
                merged_slot = merged_slot.replace(&slot_key, &slot_content);
            }

            // Remove any remaining slot placeholders that weren't replaced
            merged_slot = CommonUtil::remove_remaining_slot_placeholders(&merged_slot);

            result.push_str(&merged_slot);
            search_pos = close_start + closing_tag.len();
        }

        result
    }

    /// Extracts slot contents from inner content between {{#SlotName}} and {{/SlotName}}
    /// Returns a HashMap of slot keys ({{$HTMLPLACEHOLDER}}, {{$HTMLPLACEHOLDER1}}, etc.) to their content
    fn extract_slot_contents(
        &self,
        inner_content: &str,
        app_site: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderNormal,
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

            // Extract slot number using byte positions
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
            let recursive_result = self.merge_template_slots(slot_content, app_site, app_view, loader, enable_json_processing);
            let recursive_result = self.replace_template_placeholders(&recursive_result, app_site, app_view, loader, enable_json_processing);
            slot_contents.insert(slot_key, recursive_result);

            search_pos = close_start + close_tag.len();
        }

        slot_contents
    }

    /// Replaces simple template placeholders {{TemplateName}} with template content
    fn replace_template_placeholders(
        &self,
        content_html: &str,
        app_site: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderNormal,
        enable_json_processing: bool,
    ) -> String {
        if content_html.is_empty() {
            return content_html.to_string();
        }

        let mut result = String::with_capacity(content_html.len() * 2);
        let mut search_pos = 0;

        while search_pos < content_html.len() {
            let open_start = match content_html[search_pos..].find("{{") {
                Some(pos) => search_pos + pos,
                None => {
                    result.push_str(&content_html[search_pos..]);
                    break;
                }
            };

            result.push_str(&content_html[search_pos..open_start]);

            if open_start + 2 >= content_html.len() {
                result.push_str(&content_html[open_start..]);
                break;
            }

            // Skip special markers
            let next_char = content_html.as_bytes()[open_start + 2];
            if next_char == b'#' || next_char == b'@' || next_char == b'$' || next_char == b'/' {
                result.push_str("{{");
                search_pos = open_start + 2;
                continue;
            }

            let close_start = match content_html[open_start + 2..].find("}}") {
                Some(pos) => open_start + 2 + pos,
                None => {
                    result.push_str(&content_html[open_start..]);
                    break;
                }
            };

            let placeholder_name = content_html[open_start + 2..close_start].trim();

            if placeholder_name.is_empty() || !CommonUtil::is_alphanumeric(placeholder_name) {
                result.push_str(&content_html[open_start..close_start + 2]);
                search_pos = close_start + 2;
                continue;
            }

            // Get and merge template
            match self.get_template_with_json(app_site, placeholder_name, loader, app_view, enable_json_processing) {
                Some(template_content) => {
                    result.push_str(&template_content);
                }
                None => {
                    result.push_str(&content_html[open_start..close_start + 2]);
                }
            }

            search_pos = close_start + 2;
        }

        result
    }
}

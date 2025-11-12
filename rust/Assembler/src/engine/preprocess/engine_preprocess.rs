use crate::interface::ILoaderPreProcess;
use arshu::common::Logger;

/// PreProcess template engine implementation that only does merging using preprocessed data structures
/// All parsing is done by Loader, this engine only handles merging
/// Matches C# EnginePreProcess structure - uses ILoaderPreProcess for template access
pub struct EnginePreProcess {
    app_view_prefix: String,
}

impl EnginePreProcess {
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

    /// Merges templates using preprocessed data structures
    /// This method only does merging using preprocessed data structures - no loading or parsing
    pub fn merge_templates(
        &self,
        app_site: &str,
        app_file: &str,
        app_view: Option<&str>,
        loader: &dyn ILoaderPreProcess,
        enable_json_processing: bool,
    ) -> String {
        let app_view_str = app_view.unwrap_or("null");
        Logger::debug(
            &format!(
                "MergeTemplates called: appSite={}, appFile={}, appView={}, enableJson={}",
                app_site, app_file, app_view_str, enable_json_processing
            ),
            Some("EnginePreProcess"),
        );

        // Get main template using ILoaderPreProcess (includes AppView fallback and SearchAppSites logic)
        let main_preprocessed = match loader.get_template_html(
            app_site,
            app_file,
            app_view,
            Some(&self.app_view_prefix),
        ) {
            Some(tmpl) => tmpl,
            None => {
                Logger::warn(
                    &format!(
                        "Main template not found for appSite={}, appFile={}",
                        app_site, app_file
                    ),
                    Some("EnginePreProcess"),
                );
                return String::new();
            }
        };

        Logger::debug(
            &format!(
                "Main template found, original size: {}",
                main_preprocessed.original_content.len()
            ),
            Some("EnginePreProcess"),
        );

        // Start with original content
        let mut content_html = main_preprocessed.original_content.clone();

        // Merge JSON into main template first using loader's centralized method (for consistency with other engines)
        if enable_json_processing {
            content_html = loader.merge_html_with_json(&content_html, app_site, app_file);
            Logger::debug(
                &format!("After main template JSON merge: {} chars", content_html.len()),
                Some("EnginePreProcess"),
            );
        }

        // Apply ALL replacement mappings from ALL templates using loader's method
        // The loader handles multi-pass logic internally
        let result = loader.apply_all_replacement_mappings(
            &content_html,
            app_site,
            Some(&main_preprocessed),
            app_view,
            Some(&self.app_view_prefix),
            enable_json_processing,
        );

        Logger::debug(
            &format!("MergeTemplates complete: output size={}", result.len()),
            Some("EnginePreProcess"),
        );
        result
    }
}

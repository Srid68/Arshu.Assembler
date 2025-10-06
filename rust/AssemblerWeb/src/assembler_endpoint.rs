use actix_web::{web, HttpRequest, HttpResponse, Responder};
use actix_web::http::header::ContentType;
use serde::{Deserialize, Serialize};
use std::time::Instant;
use assembler::template_common::template_utils::TemplateUtils;
use assembler::template_loader::loader_normal::LoaderNormal;
use assembler::template_loader::loader_preprocess::LoaderPreProcess;
use assembler::template_engine::engine_normal::EngineNormal;
use assembler::template_engine::engine_preprocess::EnginePreProcess;
use assembler::template_api::api_response::TemplateData;
use assembler::template_common::logger::Logger;
use crate::security_validator;

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct MergeRequest {
    pub app_site: Option<String>,
    pub app_view: Option<String>,
    pub app_view_prefix: Option<String>,
    pub app_file: Option<String>,
    pub engine_type: Option<String>,
}

/// GET / - Root endpoint using Index AppSite
#[utoipa::path(
    get,
    path = "/",
    responses(
        (status = 200, description = "Root template HTML using Index AppSite", body = String)
    )
)]
pub async fn index(req: HttpRequest) -> impl Responder {
    // Use Index AppSite with engine toggle parameter
    let (root_dir_path, _project_directory) = TemplateUtils::get_assembler_web_dir_path();
    let root_dir_path_str = root_dir_path.to_str().unwrap_or("");

    // Get engine type from query parameter (default to Normal)
    let engine_type = req.query_string()
        .split('&')
        .find(|param| param.starts_with("engine="))
        .and_then(|param| param.split('=').nth(1))
        .unwrap_or("Normal");

    // Validate EngineType against allowlist
    if !security_validator::is_valid_engine_type(engine_type) {
        return HttpResponse::BadRequest().body("Invalid engine type. Use 'Normal' or 'PreProcess'");
    }

    // TEMPORARY: Clear cache for development
    LoaderNormal::clear_cache();
    LoaderPreProcess::clear_cache();

    // Load templates for Index AppSite
    let mut normal_templates_raw = LoaderNormal::load_get_template_files(root_dir_path_str, "Index");
    let preprocess_templates_raw = LoaderPreProcess::load_process_get_template_files(root_dir_path_str, "Index");

    // Merge using selected engine (no AppView context for Index)
    let merged_html = if engine_type.eq_ignore_ascii_case("PreProcess") {
        let engine = EnginePreProcess::new(String::new());
        engine.merge_templates("Index", "Index", None, &preprocess_templates_raw.templates, true)
    } else {
        let engine = EngineNormal::new(String::new());
        engine.merge_templates("Index", "Index", None, &mut normal_templates_raw, true)
    };

    HttpResponse::Ok().content_type(ContentType::html()).body(merged_html)
}

/// POST /merge - Merge templates endpoint
#[utoipa::path(
    post,
    path = "/merge",
    request_body = MergeRequest,
    responses(
        (status = 200, description = "Merged template output", body = String)
    )
)]
#[actix_web::post("/merge")]
pub async fn merge_templates(req: web::Json<MergeRequest>) -> impl Responder {
    let log_msg = format!(
        "/merge endpoint called with: app_site={:?}, app_file={:?}, engine_type={:?}, app_view={:?}, app_view_prefix={:?}",
        req.app_site, req.app_file, req.engine_type, req.app_view, req.app_view_prefix
    );
    println!("{}", log_msg);
    Logger::info(&log_msg, Some("MergeEndpoint"));

    // Validate required fields
    let app_site = match &req.app_site {
        Some(s) if !s.is_empty() => s,
        _ => return HttpResponse::BadRequest().body("Missing required field: app_site"),
    };
    let app_file = match &req.app_file {
        Some(f) if !f.is_empty() => f,
        _ => return HttpResponse::BadRequest().body("Missing required field: app_file"),
    };
    let engine_type = match &req.engine_type {
        Some(e) if !e.is_empty() => e,
        _ => return HttpResponse::BadRequest().body("Missing required field: engine_type"),
    };

    // Get wwwroot directory
    let (assembler_web_dir_path, _project_directory) = TemplateUtils::get_assembler_web_dir_path();
    let root_dir_path = assembler_web_dir_path.to_str().unwrap_or("");

    // Validate EngineType against allowlist
    if !security_validator::is_valid_engine_type(engine_type) {
        return HttpResponse::BadRequest().body("Invalid EngineType value");
    }

    // Validate AppSite against allowlist loaded from appsites.csv
    let valid_app_sites = match security_validator::get_valid_app_sites(root_dir_path) {
        Ok(sites) => sites,
        Err(e) => return HttpResponse::InternalServerError().body(format!("Failed to load AppSites: {}", e)),
    };

    if !security_validator::is_valid_app_site(app_site, &valid_app_sites) {
        return HttpResponse::BadRequest().body("Invalid AppSite value");
    }

    // Validate path components for path traversal attacks
    if !security_validator::is_valid_path_component(Some(app_site)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppSite");
    }

    if !security_validator::is_valid_path_component(Some(app_file)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppFile");
    }

    if let Some(ref app_view) = req.app_view {
        if !app_view.is_empty() && !security_validator::is_valid_path_component(Some(app_view)) {
            return HttpResponse::BadRequest().body("Invalid characters in AppView");
        }
    }

    if let Some(ref app_view_prefix) = req.app_view_prefix {
        if !app_view_prefix.is_empty() && !security_validator::is_valid_path_component(Some(app_view_prefix)) {
            return HttpResponse::BadRequest().body("Invalid characters in AppViewPrefix");
        }
    }

    // TEMPORARY: Clear cache for development - remove this for production
    LoaderNormal::clear_cache();
    LoaderPreProcess::clear_cache();

    let server_start = Instant::now();

    let templates_map = LoaderNormal::load_get_template_files(root_dir_path, app_site);
    let pre_templates = LoaderPreProcess::load_process_get_template_files(root_dir_path, app_site);

    // Convert (String, Option<String>) to TemplateData for ApiResponse
    let mut templates = std::collections::HashMap::new();
    for (k, v) in &templates_map {
        let (html, json) = v;
        templates.insert(k.clone(), TemplateData {
            html: html.clone(),
            json: json.clone(),
        });
    }

    // Convert PreprocessedTemplate to PreProcessTemplateMetadata for ApiResponse
    let mut preprocess_map = std::collections::HashMap::new();
    for (k, v) in &pre_templates.templates {
        preprocess_map.insert(
            k.clone(),
            assembler::template_api::api_response::PreProcessTemplateMetadata {
                original_content: v.original_content.clone(),
                placeholders: v.placeholders.clone(),
                slotted_templates: v.slotted_templates.clone(),
                json_data: v.json_data.as_ref().map(|j| format!("{:?}", j)),
                json_placeholders: v.json_placeholders.clone(),
                replacement_mappings: v.replacement_mappings.clone(),
                has_placeholders: v.has_placeholders_flag,
                has_slotted_templates: v.has_slotted_templates_flag,
                has_json_data: v.has_json_data_flag,
                has_json_placeholders: v.has_json_placeholders_flag,
                has_replacement_mappings: v.has_replacement_mappings_flag,
                requires_processing: v.requires_processing_flag,
            }
        );
    }

    let engine_start = Instant::now();

    let (templates, pre_process_templates, merged_html) = if engine_type.eq_ignore_ascii_case("PreProcess") {
        let engine = EnginePreProcess::new(req.app_view_prefix.clone().unwrap_or_default());
        let html = engine.merge_templates(
            app_site,
            app_file,
            req.app_view.as_deref(),
            &pre_templates.templates,
            true
        );
        (std::collections::HashMap::new(), preprocess_map, html)
    } else {
        let engine = EngineNormal::new(req.app_view_prefix.clone().unwrap_or_default());
        let html = engine.merge_templates(
            app_site,
            app_file,
            req.app_view.as_deref(),
            &mut templates_map.clone(),
            true
        );
        (templates, std::collections::HashMap::new(), html)
    };

    let engine_time_ms = engine_start.elapsed().as_secs_f64() * 1000.0;
    let server_time_ms = server_start.elapsed().as_secs_f64() * 1000.0;

    let response = assembler::template_api::api_response::ApiResponse {
        templates,
        pre_process_templates,
        app_site: app_site.clone(),
        app_file: Some(app_file.clone()),
        app_view: req.app_view.clone(),
        server_time_ms,
        html: merged_html,
        engine_time_ms,
    };

    HttpResponse::Ok()
        .content_type("application/json")
        .body(response.serialize_to_json())
}

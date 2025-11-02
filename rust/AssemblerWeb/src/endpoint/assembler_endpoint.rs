use actix_web::{web, HttpRequest, HttpResponse, Responder};
use actix_web::http::header::ContentType;
use serde::{Deserialize, Serialize};
use std::time::Instant;
use std::collections::HashSet;
use std::sync::OnceLock;
use assembler::loader::loader_normal::LoaderNormal;
use assembler::loader::loader_preprocess::LoaderPreProcess;
use assembler::engine::engine_normal::EngineNormal;
use assembler::engine::engine_preprocess::EnginePreProcess;
use assembler::api::api_response::TemplateData;
use assembler::common::logger::Logger;
use assembler::config::config_util;

pub const DEFAULT_APP_SITE: &str = "Test";

// Maximum parameter length to prevent DoS attacks
const PARAM_MAX_LENGTH: usize = 256;

/// Valid engine types allowlist
static VALID_ENGINE_TYPES: OnceLock<HashSet<String>> = OnceLock::new();

/// Gets the valid engine types
fn get_valid_engine_types() -> &'static HashSet<String> {
    VALID_ENGINE_TYPES.get_or_init(|| {
        let mut set = HashSet::new();
        set.insert("Normal".to_string());
        set.insert("PreProcess".to_string());
        set
    })
}

/// Gets the valid AppSites from TemplateConfig. Throws if not loaded.
fn get_valid_app_sites() -> Result<HashSet<String>, String> {
    assembler::config::config_util::ConfigUtil::get_app_sites()
}

/// Validates if a path component is safe (no traversal, invalid chars, or excessive length)
fn is_valid_path_component(value: Option<&str>) -> bool {
    match value {
        None => false,
        Some(v) if v.trim().is_empty() => false,
        Some(v) => {
            // Check parameter length to prevent DoS
            if v.len() > PARAM_MAX_LENGTH {
                return false;
            }

            // Check for path traversal attempts
            if v.contains("..") || v.contains('/') || v.contains('\\') {
                return false;
            }

            // Check for other suspicious characters
            let invalid_chars = ['<', '>', ':', '"', '|', '?', '*', '\0'];
            if v.chars().any(|c| invalid_chars.contains(&c) || c.is_control()) {
                return false;
            }

            true
        }
    }
}

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct ScenarioDto {
    pub app_site: String,
    pub app_file: String,
    pub app_view: String,
    pub display_name: String,
    pub description: String,
}

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct MergeRequest {
    pub app_site: Option<String>,
    pub app_view: Option<String>,
    pub engine_type: Option<String>,
}

/// GET / - Root endpoint using Default AppSite
#[utoipa::path(
    get,
    path = "/",
    responses(
        (status = 200, description = "Root template HTML using Default AppSite", body = String)
    )
)]
pub async fn index(req: HttpRequest) -> impl Responder {
    // Get appsite from query parameter or use default
    let project_directory = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let root_dir_path = project_directory.join("wwwroot");
    let root_dir_path_str = root_dir_path.to_str().unwrap_or("");

    let mut app_site = DEFAULT_APP_SITE.to_string();
    let mut app_file = "index".to_string();

    // Get appsite from query parameter
    let requested_app_site = req.query_string()
        .split('&')
        .find(|param| param.starts_with("appsite="))
        .and_then(|param| param.split('=').nth(1));

    // If appsite query param is provided, validate it exists in scenarios
    if let Some(requested) = requested_app_site {
        // Validate AppSite against allowlist
        let valid_app_sites = match get_valid_app_sites() {
            Ok(sites) => sites,
            Err(e) => return HttpResponse::InternalServerError().body(format!("Failed to get valid AppSites: {}", e)),
        };

        if !valid_app_sites.iter().any(|valid| valid.eq_ignore_ascii_case(requested)) {
            return HttpResponse::BadRequest().body("Invalid AppSite value");
        }

        // Validate path components for path traversal attacks
        if !is_valid_path_component(Some(requested)) {
            return HttpResponse::BadRequest().body("Invalid characters in AppSite");
        }

        // Get AppFile from scenarios
        let scenarios = match config_util::ConfigUtil::get_scenarios() {
            Ok(scenarios) => scenarios,
            Err(e) => return HttpResponse::InternalServerError().body(format!("Failed to get scenarios: {}", e)),
        };

        let matching_scenario = scenarios.iter().find(|s|
            s.app_site.eq_ignore_ascii_case(requested) && s.app_view.is_empty()
        );

        match matching_scenario {
            Some(scenario) => {
                app_site = scenario.app_site.clone();
                app_file = scenario.app_file.clone();
            },
            None => return HttpResponse::BadRequest().body(format!("No matching scenario found for AppSite='{}' without AppView", requested)),
        }
    }

    // Get engine type from query parameter (default to Normal)
    let engine_type = req.query_string()
        .split('&')
        .find(|param| param.starts_with("engine="))
        .and_then(|param| param.split('=').nth(1))
        .unwrap_or("Normal");

    // Validate EngineType against allowlist
    if !get_valid_engine_types().iter().any(|valid| valid.eq_ignore_ascii_case(engine_type)) {
        return HttpResponse::BadRequest().body("Invalid engine type. Use 'Normal' or 'PreProcess'");
    }

    // Load templates for requested AppSite
    let mut normal_templates_raw = LoaderNormal::load_get_template_files(root_dir_path_str, &app_site);
    let preprocess_templates_raw = LoaderPreProcess::load_process_get_template_files(root_dir_path_str, &app_site);

    // Merge using selected engine (no AppView context)
    let merged_html = if engine_type.eq_ignore_ascii_case("PreProcess") {
        let engine = EnginePreProcess::new(String::new());
        engine.merge_templates(&app_site, &app_file, None, &preprocess_templates_raw.templates, true)
    } else {
        let engine = EngineNormal::new(String::new());
        engine.merge_templates(&app_site, &app_file, None, &mut normal_templates_raw, true)
    };

    HttpResponse::Ok().content_type(ContentType::html()).body(merged_html)
}

/// GET /api/scenarios - Get all scenarios
#[utoipa::path(
    get,
    path = "/api/scenarios",
    responses(
        (status = 200, description = "List of scenarios", body = Vec<ScenarioDto>),
        (status = 500, description = "Internal server error")
    )
)]
pub async fn get_scenarios() -> impl Responder {
    match assembler::config::config_util::ConfigUtil::get_scenarios() {
        Ok(scenarios) => {
            let scenario_dtos: Vec<ScenarioDto> = scenarios
                .iter()
                .map(|s| ScenarioDto {
                    app_site: s.app_site.clone(),
                    app_file: s.app_file.clone(),
                    app_view: s.app_view.clone(),
                    display_name: s.display_name.clone(),
                    description: s.description.clone(),
                })
                .collect();

            HttpResponse::Ok()
                .content_type("application/json")
                .json(scenario_dtos)
        }
        Err(e) => {
            HttpResponse::InternalServerError()
                .body(format!("Error loading scenarios: {}", e))
        }
    }
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
pub async fn merge_templates(
    req: web::Json<MergeRequest>,
    http_req: HttpRequest
) -> impl Responder {
    // Enable logging for merge operations
    let original_log_level = Logger::get_log_level();
    let project_directory = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));

    let template_analysis_dir = project_directory.join("template_analysis");
    let logs_dir = template_analysis_dir.join("logs");
    let _ = std::fs::create_dir_all(&logs_dir);

    let mut context_log_files = std::collections::HashMap::new();
    context_log_files.insert("LoaderNormal".to_string(), logs_dir.join("rust_loadernormal.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("LoaderPreProcess".to_string(), logs_dir.join("rust_loaderpreprocess.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("EngineNormal".to_string(), logs_dir.join("rust_enginenormal.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("EnginePreProcess".to_string(), logs_dir.join("rust_enginepreprocess.log").to_str().unwrap_or("").to_string());

    use assembler::common::logger::LogLevel;
    Logger::configure(LogLevel::DEBUG, false, assembler::common::logger::LogRotation::NONE);
    Logger::add_context_log_files(context_log_files);

    let log_msg = format!(
        "/merge endpoint called with: app_site={:?}, engine_type={:?}, app_view={:?}",
        req.app_site, req.engine_type, req.app_view
    );
    println!("{}", log_msg);
    Logger::info(&log_msg, Some("MergeEndpoint"));

    // Validate required fields
    let app_site = match &req.app_site {
        Some(s) if !s.is_empty() => s,
        _ => return HttpResponse::BadRequest().body("Missing required field: appSite"),
    };
    let engine_type = match &req.engine_type {
        Some(e) if !e.is_empty() => e,
        _ => return HttpResponse::BadRequest().body("Missing required field: engineType"),
    };

    // Get wwwroot directory (project_directory already set from parameter)
    let assembler_web_dir_path = project_directory.join("wwwroot");
    let root_dir_path = assembler_web_dir_path.to_str().unwrap_or("");

    // Validate EngineType against allowlist
    if !get_valid_engine_types().iter().any(|valid| valid.eq_ignore_ascii_case(engine_type)) {
        return HttpResponse::BadRequest().body("Invalid EngineType value");
    }

    // Validate AppSite against allowlist loaded from appsites.csv
    let valid_app_sites = match get_valid_app_sites() {
        Ok(sites) => sites,
        Err(e) => return HttpResponse::InternalServerError().body(format!("Failed to load AppSites: {}", e)),
    };

    if !valid_app_sites.iter().any(|valid| valid.eq_ignore_ascii_case(app_site)) {
        return HttpResponse::BadRequest().body("Invalid AppSite value");
    }

    // Validate path components for path traversal attacks
    if !is_valid_path_component(Some(app_site)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppSite");
    }

    // Get AppFile from scenarios
    let scenarios = match assembler::config::config_util::ConfigUtil::get_scenarios() {
        Ok(s) => s,
        Err(e) => return HttpResponse::InternalServerError().body(format!("Failed to load scenarios: {}", e)),
    };

    let app_view = req.app_view.as_deref().unwrap_or("");
    let matching_scenario = scenarios.iter().find(|s| {
        s.app_site.eq_ignore_ascii_case(app_site) && s.app_view.eq_ignore_ascii_case(app_view)
    });

    let app_file = match matching_scenario {
        Some(scenario) => &scenario.app_file,
        None => {
            return HttpResponse::BadRequest().body(format!(
                "No matching scenario found for AppSite='{}' and AppView='{}'",
                app_site, app_view
            ));
        }
    };

    let app_view_prefix = if let Some(ref view) = req.app_view {
        if !view.is_empty() {
            app_file.to_string()
        } else {
            String::new()
        }
    } else {
        String::new()
    };

    if !is_valid_path_component(Some(app_file)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppFile");
    }

    if let Some(ref app_view) = req.app_view {
        if !app_view.is_empty() && !is_valid_path_component(Some(app_view)) {
            return HttpResponse::BadRequest().body("Invalid characters in AppView");
        }
    }
    
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
            assembler::api::api_response::PreProcessTemplateMetadata {
                original_content: v.original_content.clone(),
                placeholders: v.placeholders.clone(),
                slotted_templates: v.slotted_templates.clone(),
                json_data: v.json_data.as_ref().map(|j| assembler::app::json_convertor::JsonConverter::serialize_object(j)),
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
        let engine = EnginePreProcess::new(app_view_prefix.clone());
        let html = engine.merge_templates(
            app_site,
            app_file,
            req.app_view.as_deref(),
            &pre_templates.templates,
            true
        );
        (std::collections::HashMap::new(), preprocess_map, html)
    } else {
        let engine = EngineNormal::new(app_view_prefix.clone());
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

    // Save HTML output only if save query parameter is present
    let save_param = http_req.query_string()
        .split('&')
        .find(|param| param.starts_with("save="))
        .and_then(|param| param.split('=').nth(1))
        .unwrap_or("");

    if save_param.eq_ignore_ascii_case("true") {
        let output_dir = project_directory.join("template_analysis").join("output");
        let _ = std::fs::create_dir_all(&output_dir);

        let app_view_suffix = if let Some(ref view) = req.app_view {
            if !view.is_empty() {
                format!("_{}", view)
            } else {
                String::new()
            }
        } else {
            String::new()
        };
        let engine_suffix = engine_type.to_lowercase();
        let output_file = output_dir.join(format!("{}{}_{}.html", app_site, app_view_suffix, engine_suffix));
        let _ = std::fs::write(&output_file, &merged_html);
    }

    let response = assembler::api::api_response::ApiResponse {
        templates,
        pre_process_templates,
        app_site: app_site.clone(),
        app_file: Some(app_file.clone()),
        app_view: req.app_view.clone(),
        server_time_ms,
        html: merged_html,
        engine_time_ms,
    };

    // Restore original log level
    Logger::set_log_level(original_log_level);

    HttpResponse::Ok()
        .content_type("application/json")
        .body(response.serialize_to_json(false))
}

/// POST /api/templates - Load templates for client-side merging
#[utoipa::path(
    post,
    path = "/api/templates",
    responses(
        (status = 200, description = "Templates and PreProcessed templates", body = String),
        (status = 400, description = "Bad request"),
        (status = 500, description = "Internal server error")
    )
)]
pub async fn get_templates(
    body: web::Bytes,
    req: HttpRequest
) -> impl Responder {
    let project_directory = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let root_dir_path = project_directory.join("wwwroot");
    let root_dir_path_str = root_dir_path.to_str().unwrap_or("");

    // Parse JSON manually
    let app_site = match serde_json::from_slice::<serde_json::Value>(&body) {
        Ok(json) => {
            json.get("appsite")
                .or_else(|| json.get("appSite"))
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string()
        }
        Err(_) => return HttpResponse::BadRequest().body("Invalid JSON format"),
    };

    if app_site.is_empty() {
        return HttpResponse::BadRequest().body("Missing appsite parameter");
    }

    // Validate AppSite against allowlist loaded from appsites.csv
    let valid_app_sites = match get_valid_app_sites() {
        Ok(sites) => sites,
        Err(e) => return HttpResponse::InternalServerError().body(format!("Failed to load AppSites: {}", e)),
    };

    if !valid_app_sites.iter().any(|valid| valid.eq_ignore_ascii_case(&app_site)) {
        return HttpResponse::BadRequest().body("Invalid AppSite value");
    }

    // Validate path components for path traversal attacks
    if !is_valid_path_component(Some(&app_site)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppSite");
    }

    let server_start = Instant::now();

    // Load Normal templates
    let normal_templates = LoaderNormal::load_get_template_files(root_dir_path_str, &app_site);

    // Load PreProcess templates
    let preprocess_templates = LoaderPreProcess::load_process_get_template_files(root_dir_path_str, &app_site);

    // Convert Normal templates to TemplateData objects for proper JSON serialization
    let mut normal_result = std::collections::HashMap::new();
    for (k, v) in &normal_templates {
        let (html, json) = v;
        normal_result.insert(k.clone(), TemplateData {
            html: html.clone(),
            json: json.clone(),
        });
    }

    // Convert PreProcess templates to metadata-only objects
    let mut preprocess_result = std::collections::HashMap::new();
    for (k, v) in &preprocess_templates.templates {
        preprocess_result.insert(
            k.clone(),
            assembler::api::api_response::PreProcessTemplateMetadata {
                original_content: v.original_content.clone(),
                placeholders: v.placeholders.clone(),
                slotted_templates: v.slotted_templates.clone(),
                json_data: v.json_data.as_ref().map(|_| "Arshu.App.Json.JsonObject".to_string()),
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

    let server_time_ms = server_start.elapsed().as_secs_f64() * 1000.0;

    // Use named response class
    let response = assembler::api::api_response::ApiResponse {
        templates: normal_result,
        pre_process_templates: preprocess_result,
        app_site: app_site.clone(),
        app_file: None,
        app_view: None,
        server_time_ms,
        html: String::new(),
        engine_time_ms: 0.0,
    };

    let json_result = response.serialize_to_json(false);

    // Check if save query parameter is present
    let save_param = req.query_string()
        .split('&')
        .find(|param| param.starts_with("save="))
        .and_then(|param| param.split('=').nth(1))
        .unwrap_or("");

    if save_param.eq_ignore_ascii_case("true") {
        let templates_dir = project_directory.join("template_analysis").join("templates");
        let _ = std::fs::create_dir_all(&templates_dir);

        let save_file = templates_dir.join(format!("rust_{}_templates.json", app_site));
        let _ = std::fs::write(&save_file, &json_result);
    }

    HttpResponse::Ok()
        .content_type("application/json")
        .body(json_result)
}

// OpenAPI documentation
use utoipa::OpenApi;

#[derive(OpenApi)]
#[openapi(
    paths(
        index,
        get_scenarios,
        get_templates,
        merge_templates
    ),
    components(schemas(MergeRequest, ScenarioDto)),
    tags((name = "Assembler", description = "Assembler API endpoints"))
)]
pub struct ApiDoc;

pub async fn openapi_handler() -> impl Responder {
    let openapi = ApiDoc::openapi();
    HttpResponse::Ok()
        .content_type("application/json")
        .json(openapi)
}

/// Maps all assembler endpoints to the service config
/// Usage: map_assembler_endpoints(&mut cfg)
pub fn map_assembler_endpoints(cfg: &mut web::ServiceConfig) {
    cfg.service(web::resource("/").route(web::get().to(index)))
        .route("/api/scenarios", web::get().to(get_scenarios))
        .route("/api/templates", web::post().to(get_templates))
        .service(merge_templates);
}


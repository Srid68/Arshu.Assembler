use actix_web::http::header::ContentType;
use actix_web::{web, HttpRequest, HttpResponse, Responder};
use arshu::common::Logger;
use assembler::config::config_util;
use assembler::engine::EngineNormal;
use assembler::engine::EngineNormalJson;
use assembler::engine::EnginePreProcess;
use assembler::engine::EnginePreProcessJson;
use assembler::interface::i_loader_json::ILoaderJson;
use assembler::loader::normal::loader_normal::LoaderNormal;
use assembler::loader::normaljson::loader_normal_json::LoaderNormalJson;
use assembler::loader::preprocess::loader_preprocess::LoaderPreProcess;
use assembler::loader::preprocessjson::loader_preprocess_json::LoaderPreProcessJson;
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::sync::OnceLock;
use std::time::Instant;

use crate::WEB_ROOT_FOLDER_NAME;

pub const DEFAULT_APP_SITE: &str = "Main";
pub const DEFAULT_ENGINE_TYPE: &str = "Normal";
pub const SEARCH_APP_SITE: &str = "Main, Language";

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
        set.insert("NormalJson".to_string());
        set.insert("PreProcessJson".to_string());
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
            if v.chars()
                .any(|c| invalid_chars.contains(&c) || c.is_control())
            {
                return false;
            }

            true
        }
    }
}

/// Builds the JSON response for /api/templates endpoint using pre-serialized template JSON
fn build_templates_api_response(
    normal_templates_json: &str,
    preprocess_templates_json: &str,
    app_site: &str,
    server_time_ms: f64,
) -> String {
    format!(
        r#"{{"Templates":{},"PreProcessTemplates":{},"AppSite":"{}","AppFile":null,"AppView":null,"ServerTimeMs":{}}}"#,
        normal_templates_json,
        preprocess_templates_json,
        escape_json_string(app_site),
        server_time_ms
    )
}

/// Escapes a string for safe inclusion in JSON
fn escape_json_string(input: &str) -> String {
    input
        .replace('\\', "\\\\")
        .replace('"', "\\\"")
        .replace('\r', "\\r")
        .replace('\n', "\\n")
        .replace('\t', "\\t")
        .replace('<', "\\u003C")
        .replace('>', "\\u003E")
        .replace('&', "\\u0026")
        .replace('\'', "\\u0027")
        .replace('+', "\\u002B")
}

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct ScenarioDto {
    pub app_site: String,
    pub app_file: String,
    pub app_view: String,
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
    let project_directory =
        std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let root_dir_path = project_directory.join(WEB_ROOT_FOLDER_NAME);
    let root_dir_path_str = root_dir_path.to_str().unwrap_or("");

    let mut app_site = DEFAULT_APP_SITE.to_string();
    let mut app_file = "index".to_string();

    // Get appsite from query parameter
    let requested_app_site = req
        .query_string()
        .split('&')
        .find(|param| param.starts_with("appsite="))
        .and_then(|param| param.split('=').nth(1));

    // If appsite query param is provided, validate it exists in scenarios
    if let Some(requested) = requested_app_site {
        // Validate AppSite against allowlist
        let valid_app_sites = match get_valid_app_sites() {
            Ok(sites) => sites,
            Err(e) => {
                return HttpResponse::InternalServerError()
                    .body(format!("Failed to get valid AppSites: {}", e))
            }
        };

        if !valid_app_sites
            .iter()
            .any(|valid| valid.eq_ignore_ascii_case(requested))
        {
            return HttpResponse::BadRequest().body("Invalid AppSite value");
        }

        // Validate path components for path traversal attacks
        if !is_valid_path_component(Some(requested)) {
            return HttpResponse::BadRequest().body("Invalid characters in AppSite");
        }

        // Get AppFile from scenarios
        let scenarios = match config_util::ConfigUtil::get_scenarios() {
            Ok(scenarios) => scenarios,
            Err(e) => {
                return HttpResponse::InternalServerError()
                    .body(format!("Failed to get scenarios: {}", e))
            }
        };

        let matching_scenario = scenarios
            .iter()
            .find(|s| s.app_site.eq_ignore_ascii_case(requested) && s.app_view.is_empty());

        match matching_scenario {
            Some(scenario) => {
                app_site = scenario.app_site.clone();
                app_file = scenario.app_file.clone();
            }
            None => {
                return HttpResponse::BadRequest().body(format!(
                    "No matching scenario found for AppSite='{}' without AppView",
                    requested
                ))
            }
        }
    }

    // Get engine type from query parameter (default to DEFAULT_ENGINE_TYPE)
    let engine_type = req
        .query_string()
        .split('&')
        .find(|param| param.starts_with("engine="))
        .and_then(|param| param.split('=').nth(1))
        .unwrap_or(DEFAULT_ENGINE_TYPE);

    // Validate EngineType against allowlist
    if !get_valid_engine_types()
        .iter()
        .any(|valid| valid.eq_ignore_ascii_case(engine_type))
    {
        return HttpResponse::BadRequest()
            .body("Invalid engine type. Use 'Normal', 'PreProcess', 'NormalJson', or 'PreProcessJson'");
    }

    // Merge using selected engine (no AppView context)
    let merged_html = if engine_type.eq_ignore_ascii_case("PreProcessJson") {
        let loader = LoaderPreProcessJson::new(root_dir_path_str, &app_site, SEARCH_APP_SITE);
        let engine = EnginePreProcessJson::new(String::new());
        engine.merge_templates(
            &app_site,
            &app_file,
            None,
            &loader,
            true,
        )
    } else if engine_type.eq_ignore_ascii_case("PreProcess") {
        let loader = LoaderPreProcess::new(root_dir_path_str, &app_site, SEARCH_APP_SITE);
        let engine = EnginePreProcess::new(String::new());
        engine.merge_templates(
            &app_site,
            &app_file,
            None,
            &loader,
            true,
        )
    } else if engine_type.eq_ignore_ascii_case("NormalJson") {
        let loader = LoaderNormalJson::new(root_dir_path_str, &app_site, SEARCH_APP_SITE);
        let engine = EngineNormalJson::new(String::new());
        engine.merge_templates(
            &app_site,
            &app_file,
            None,
            &loader,
            true,
        )
    } else {
        let loader = LoaderNormal::new(root_dir_path_str, &app_site, SEARCH_APP_SITE);
        let engine = EngineNormal::new(String::new());
        engine.merge_templates(
            &app_site,
            &app_file,
            None,
            &loader,
            true,
        )
    };

    HttpResponse::Ok()
        .content_type(ContentType::html())
        .body(merged_html)
}

/// GET /{appSite}/{appView?} - Navigation endpoint
#[utoipa::path(
    get,
    path = "/{appSite}/{appView}",
    responses(
        (status = 200, description = "Merged HTML template", body = String),
        (status = 400, description = "Bad request"),
        (status = 500, description = "Internal server error")
    )
)]
pub async fn navigation_endpoint(
    req: HttpRequest,
    path: web::Path<(String, Option<String>)>,
) -> impl Responder {
    let (app_site, app_view_opt) = path.into_inner();
    let app_view = app_view_opt.unwrap_or_default();

    // Validate AppSite against allowlist
    let valid_app_sites = match get_valid_app_sites() {
        Ok(sites) => sites,
        Err(e) => {
            return HttpResponse::InternalServerError()
                .body(format!("Error loading valid AppSites: {}", e))
        }
    };

    if !valid_app_sites.contains(&app_site.to_lowercase()) {
        return HttpResponse::BadRequest().body("Invalid AppSite value");
    }

    // Validate path components for path traversal attacks
    if !is_valid_path_component(Some(&app_site)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppSite");
    }

    if !app_view.is_empty() && !is_valid_path_component(Some(&app_view)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppView");
    }

    // Get AppFile from scenarios
    let scenarios = match config_util::ConfigUtil::get_scenarios() {
        Ok(s) => s,
        Err(e) => {
            return HttpResponse::InternalServerError()
                .body(format!("Failed to get scenarios: {}", e))
        }
    };

    let matching_scenario = scenarios.iter().find(|s| {
        s.app_site.eq_ignore_ascii_case(&app_site) && s.app_view.eq_ignore_ascii_case(&app_view)
    });

    let app_file = match matching_scenario {
        Some(s) => &s.app_file,
        None => {
            return HttpResponse::BadRequest().body(format!(
                "No matching scenario found for AppSite='{}' and AppView='{}'",
                app_site, app_view
            ))
        }
    };

    // Get engine type from query parameter (default to DEFAULT_ENGINE_TYPE)
    let query_string = req.query_string();
    let params: Vec<&str> = query_string.split('&').collect();
    let mut engine_type = DEFAULT_ENGINE_TYPE.to_string();
    for param in params {
        if let Some(value) = param.strip_prefix("engine=") {
            engine_type = value.to_string();
            break;
        }
    }

    // Validate EngineType against allowlist
    if !get_valid_engine_types()
        .iter()
        .any(|valid| valid.eq_ignore_ascii_case(&engine_type))
    {
        return HttpResponse::BadRequest()
            .body("Invalid engine type. Use 'Normal', 'PreProcess', 'NormalJson', or 'PreProcessJson'");
    }

    // Get wwwroot path
    let project_directory =
        std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let wwwroot_path = project_directory.join(WEB_ROOT_FOLDER_NAME);
    let wwwroot_path_str = wwwroot_path.to_str().unwrap_or("");

    // Merge using selected engine
    let merged_html = if engine_type.eq_ignore_ascii_case("PreProcessJson") {
        let loader = LoaderPreProcessJson::new(wwwroot_path_str, &app_site, SEARCH_APP_SITE);
        let engine = EnginePreProcessJson::new(String::new());
        let app_view_opt = if app_view.is_empty() {
            None
        } else {
            Some(app_view.as_str())
        };
        engine.merge_templates(
            &app_site,
            app_file,
            app_view_opt,
            &loader,
            false,
        )
    } else if engine_type.eq_ignore_ascii_case("PreProcess") {
        let loader = LoaderPreProcess::new(wwwroot_path_str, &app_site, SEARCH_APP_SITE);
        let engine = EnginePreProcess::new(String::new());
        let app_view_opt = if app_view.is_empty() {
            None
        } else {
            Some(app_view.as_str())
        };
        engine.merge_templates(
            &app_site,
            app_file,
            app_view_opt,
            &loader,
            false,
        )
    } else if engine_type.eq_ignore_ascii_case("NormalJson") {
        let loader = LoaderNormalJson::new(wwwroot_path_str, &app_site, SEARCH_APP_SITE);
        let engine = EngineNormalJson::new(String::new());
        let app_view_opt = if app_view.is_empty() {
            None
        } else {
            Some(app_view.as_str())
        };
        engine.merge_templates(
            &app_site,
            app_file,
            app_view_opt,
            &loader,
            false,
        )
    } else {
        let loader = LoaderNormal::new(wwwroot_path_str, &app_site, SEARCH_APP_SITE);
        let engine = EngineNormal::new(String::new());
        let app_view_opt = if app_view.is_empty() {
            None
        } else {
            Some(app_view.as_str())
        };
        engine.merge_templates(
            &app_site,
            app_file,
            app_view_opt,
            &loader,
            false,
        )
    };

    HttpResponse::Ok()
        .content_type("text/html")
        .body(merged_html)
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
    http_req: HttpRequest,
) -> impl Responder {
    // Enable logging for merge operations
    let project_directory =
        std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));

    let template_analysis_dir = project_directory.join("Analysis");
    let logs_dir = template_analysis_dir.join("logs");
    let _ = std::fs::create_dir_all(&logs_dir);

    let mut context_log_files = std::collections::HashMap::new();
    context_log_files.insert(
        "LoaderNormal".to_string(),
        logs_dir
            .join("rust_loadernormal.log")
            .to_str()
            .unwrap_or("")
            .to_string(),
    );
    context_log_files.insert(
        "LoaderPreProcess".to_string(),
        logs_dir
            .join("rust_loaderpreprocess.log")
            .to_str()
            .unwrap_or("")
            .to_string(),
    );
    context_log_files.insert(
        "LoaderNormalJson".to_string(),
        logs_dir
            .join("rust_loadernormaljson.log")
            .to_str()
            .unwrap_or("")
            .to_string(),
    );
    context_log_files.insert(
        "LoaderPreProcessJson".to_string(),
        logs_dir
            .join("rust_loaderpreprocessjson.log")
            .to_str()
            .unwrap_or("")
            .to_string(),
    );
    context_log_files.insert(
        "EngineNormal".to_string(),
        logs_dir
            .join("rust_enginenormal.log")
            .to_str()
            .unwrap_or("")
            .to_string(),
    );
    context_log_files.insert(
        "EnginePreProcess".to_string(),
        logs_dir
            .join("rust_enginepreprocess.log")
            .to_str()
            .unwrap_or("")
            .to_string(),
    );
    context_log_files.insert(
        "EngineNormalJson".to_string(),
        logs_dir
            .join("rust_enginenormaljson.log")
            .to_str()
            .unwrap_or("")
            .to_string(),
    );
    context_log_files.insert(
        "EnginePreProcessJson".to_string(),
        logs_dir
            .join("rust_enginepreprocessjson.log")
            .to_str()
            .unwrap_or("")
            .to_string(),
    );

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
    let assembler_web_dir_path = project_directory.join(WEB_ROOT_FOLDER_NAME);
    let root_dir_path = assembler_web_dir_path.to_str().unwrap_or("");

    // Validate EngineType against allowlist
    if !get_valid_engine_types()
        .iter()
        .any(|valid| valid.eq_ignore_ascii_case(engine_type))
    {
        return HttpResponse::BadRequest().body("Invalid EngineType value");
    }

    // Validate AppSite against allowlist loaded from appsites.csv
    let valid_app_sites = match get_valid_app_sites() {
        Ok(sites) => sites,
        Err(e) => {
            return HttpResponse::InternalServerError()
                .body(format!("Failed to load AppSites: {}", e))
        }
    };

    if !valid_app_sites
        .iter()
        .any(|valid| valid.eq_ignore_ascii_case(app_site))
    {
        return HttpResponse::BadRequest().body("Invalid AppSite value");
    }

    // Validate path components for path traversal attacks
    if !is_valid_path_component(Some(app_site)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppSite");
    }

    // Get AppFile from scenarios
    let scenarios = match assembler::config::config_util::ConfigUtil::get_scenarios() {
        Ok(s) => s,
        Err(e) => {
            return HttpResponse::InternalServerError()
                .body(format!("Failed to load scenarios: {}", e))
        }
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

    let engine_start = Instant::now();

    let merged_html = if engine_type.eq_ignore_ascii_case("PreProcessJson") {
        let loader = LoaderPreProcessJson::new(root_dir_path, app_site, SEARCH_APP_SITE);
        let engine = EnginePreProcessJson::new(app_view_prefix.clone());
        engine.merge_templates(
            app_site,
            app_file,
            req.app_view.as_deref(),
            &loader,
            true,
        )
    } else if engine_type.eq_ignore_ascii_case("PreProcess") {
        let loader = LoaderPreProcess::new(root_dir_path, app_site, SEARCH_APP_SITE);
        let engine = EnginePreProcess::new(app_view_prefix.clone());
        engine.merge_templates(
            app_site,
            app_file,
            req.app_view.as_deref(),
            &loader,
            true,
        )
    } else if engine_type.eq_ignore_ascii_case("NormalJson") {
        let loader = LoaderNormalJson::new(root_dir_path, app_site, SEARCH_APP_SITE);
        let engine = EngineNormalJson::new(app_view_prefix.clone());
        engine.merge_templates(
            app_site,
            app_file,
            req.app_view.as_deref(),
            &loader,
            true,
        )
    } else {
        let loader = LoaderNormal::new(root_dir_path, app_site, SEARCH_APP_SITE);
        let engine = EngineNormal::new(app_view_prefix.clone());
        engine.merge_templates(
            app_site,
            app_file,
            req.app_view.as_deref(),
            &loader,
            true,
        )
    };

    let engine_time_ms = engine_start.elapsed().as_secs_f64() * 1000.0;
    let server_time_ms = server_start.elapsed().as_secs_f64() * 1000.0;

    // Save HTML output only if save query parameter is present
    let save_param = http_req
        .query_string()
        .split('&')
        .find(|param| param.starts_with("save="))
        .and_then(|param| param.split('=').nth(1))
        .unwrap_or("");

    if save_param.eq_ignore_ascii_case("true") {
        let output_dir = project_directory.join("Analysis").join("output");
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
        let output_file = output_dir.join(format!(
            "{}{}_{}.html",
            app_site, app_view_suffix, engine_suffix
        ));
        let _ = std::fs::write(&output_file, &merged_html);
    }

    let response = assembler::api::api_response::ApiResponse {
        app_site: app_site.clone(),
        app_file: Some(app_file.clone()),
        app_view: req.app_view.clone(),
        server_time_ms,
        html: merged_html,
        engine_time_ms,
    };

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
pub async fn get_templates(body: web::Bytes, _req: HttpRequest) -> impl Responder {
    // Enable logging for template operations
    let project_directory =
        std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));

    let template_analysis_dir = project_directory.join("Analysis");
    let logs_dir = template_analysis_dir.join("logs");
    let _ = std::fs::create_dir_all(&logs_dir);

    let mut context_log_files = std::collections::HashMap::new();
    context_log_files.insert(
        "LoaderNormalJson".to_string(),
        logs_dir.join("rust_loadernormaljson.log").to_string_lossy().to_string(),
    );
    context_log_files.insert(
        "LoaderPreProcessJson".to_string(),
        logs_dir.join("rust_loaderpreprocessjson.log").to_string_lossy().to_string(),
    );

    Logger::add_context_log_files(context_log_files);

    let root_dir_path = project_directory.join(WEB_ROOT_FOLDER_NAME);
    let root_dir_path_str = root_dir_path.to_str().unwrap_or("");

    // Parse JSON manually
    let app_site = match serde_json::from_slice::<serde_json::Value>(&body) {
        Ok(json) => json
            .get("appsite")
            .or_else(|| json.get("appSite"))
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string(),
        Err(_) => {
            return HttpResponse::BadRequest().body("Invalid JSON format");
        }
    };

    if app_site.is_empty() {
        return HttpResponse::BadRequest().body("Missing appsite parameter");
    }

    // Validate AppSite against allowlist loaded from appsites.csv
    let valid_app_sites = match get_valid_app_sites() {
        Ok(sites) => sites,
        Err(e) => {
            return HttpResponse::InternalServerError()
                .body(format!("Failed to load AppSites: {}", e));
        }
    };

    if !valid_app_sites
        .iter()
        .any(|valid| valid.eq_ignore_ascii_case(&app_site))
    {
        return HttpResponse::BadRequest().body("Invalid AppSite value");
    }

    // Validate path components for path traversal attacks
    if !is_valid_path_component(Some(&app_site)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppSite");
    }

    let server_start = Instant::now();

    // Load Normal templates using JSON loader
    let normal_loader = LoaderNormalJson::new(root_dir_path_str, &app_site, SEARCH_APP_SITE);

    // Load PreProcess templates using JSON loader
    let preprocess_loader = LoaderPreProcessJson::new(root_dir_path_str, &app_site, SEARCH_APP_SITE);

    // Get pre-serialized JSON from loaders
    let normal_templates_json = normal_loader.get_all_templates_json();
    let preprocess_templates_json = preprocess_loader.get_all_templates_json();

    let server_time_ms = server_start.elapsed().as_secs_f64() * 1000.0;

    // Build JSON response manually using pre-serialized template JSON
    let json_result = build_templates_api_response(
        &normal_templates_json,
        &preprocess_templates_json,
        &app_site,
        server_time_ms,
    );

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
        navigation_endpoint,
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
        .route("/{appSite}", web::get().to(navigation_endpoint))
        .route("/{appSite}/{appView}", web::get().to(navigation_endpoint))
        .route("/api/templates", web::post().to(get_templates))
        .service(merge_templates);
}

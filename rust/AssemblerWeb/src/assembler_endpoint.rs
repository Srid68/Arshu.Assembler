use actix_web::{web, HttpRequest, HttpResponse, Responder};
use actix_web::http::header::ContentType;
use serde::{Deserialize, Serialize};
use serde_json::json;
use std::time::Instant;
use assembler::common::common_util::CommonUtil;
use assembler::loader::loader_normal::LoaderNormal;
use assembler::loader::loader_preprocess::LoaderPreProcess;
use assembler::engine::engine_normal::EngineNormal;
use assembler::engine::engine_preprocess::EnginePreProcess;
use assembler::api::api_response::TemplateData;
use assembler::common::logger::Logger;
use assembler::test::testing_utils::TestingUtils;
use assembler::performance::performance_utils::PerformanceUtils;
use crate::security_validator;

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct MergeRequest {
    pub app_site: Option<String>,
    pub app_view: Option<String>,
    pub engine_type: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct ReportRequest {
    pub file_name: Option<String>,
    pub use_lang_prefix: Option<bool>,
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
    let (root_dir_path, _project_directory) = CommonUtil::get_assembler_web_dir_path();
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
pub async fn merge_templates(req: web::Json<MergeRequest>) -> impl Responder {
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

    // Get wwwroot directory
    let (assembler_web_dir_path, _project_directory) = CommonUtil::get_assembler_web_dir_path();
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

    if !security_validator::is_valid_path_component(Some(app_file)) {
        return HttpResponse::BadRequest().body("Invalid characters in AppFile");
    }

    if let Some(ref app_view) = req.app_view {
        if !app_view.is_empty() && !security_validator::is_valid_path_component(Some(app_view)) {
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

    // Save HTML output to template_analysis/output folder (parent of wwwroot)
    let (_, project_directory) = CommonUtil::get_assembler_web_dir_path();
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

    HttpResponse::Ok()
        .content_type("application/json")
        .body(response.serialize_to_json())
}

/// POST /test/standard - Run standard tests
#[utoipa::path(
    post,
    path = "/test/standard",
    responses(
        (status = 200, description = "Test results", body = String)
    )
)]
pub async fn test_standard(project_dir: web::Data<std::path::PathBuf>) -> impl Responder {
    let start = Instant::now();
    let project_dir_str = project_dir.to_str().unwrap_or("");
    let root_dir_str = std::path::Path::new(project_dir_str).join("wwwroot").to_str().unwrap_or("").to_string();

    // Enable logging temporarily for tests
    let original_log_level = Logger::get_log_level();

    // Configure logger with context-specific log files for StandardTests
    let template_analysis_dir = std::path::Path::new(project_dir_str).join("template_analysis");
    let logs_dir = template_analysis_dir.join("logs");
    let _ = std::fs::create_dir_all(&logs_dir);

    let mut context_log_files = std::collections::HashMap::new();
    context_log_files.insert("LoaderNormal".to_string(), logs_dir.join("rust_loadernormal.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("EngineNormal".to_string(), logs_dir.join("rust_enginenormal.log").to_str().unwrap_or("").to_string());

    use assembler::common::logger::LogLevel;
    Logger::configure(LogLevel::DEBUG, None, false, assembler::common::logger::LogRotation::NONE);
    Logger::configure_context_log_files(context_log_files);

    // Get scenarios from ConfigUtil
    let scenarios = match assembler::config::config_util::ConfigUtil::get_scenarios() {
        Ok(s) => s,
        Err(e) => {
            Logger::set_log_level(original_log_level);
            return HttpResponse::InternalServerError().body(format!("Failed to load scenarios: {}", e));
        }
    };

    let results = TestingUtils::run_standard_tests(&root_dir_str, project_dir_str, &scenarios, false, false, true);
    if !results.is_empty() {
        TestingUtils::print_test_summary_table(&root_dir_str, project_dir_str, &results, "STANDARD TEST");
    }
    let elapsed = start.elapsed().as_secs_f64();
    let test_count = results.len();

    // Check for failures
    let failed_count = results.iter().filter(|r|
        r.normal_preprocess == "FAIL" ||
        r.cross_view_unmatch == "FAIL" ||
        !r.error.is_empty()
    ).count();

    let mut message = format!("Successful run of Standard Tests in {:.2} secs ({} tests)", elapsed, test_count);
    if failed_count > 0 {
        message.push_str(&format!("\n⚠️ Warning: {} test(s) failed", failed_count));
    }

    let response = json!({
        "success": true,
        "message": message,
        "elapsed": elapsed,
        "testCount": test_count
    });

    // Restore original log level
    Logger::set_log_level(original_log_level);

    HttpResponse::Ok()
        .content_type("application/json")
        .body(response.to_string())
}

/// POST /test/advanced - Run advanced tests
#[utoipa::path(
    post,
    path = "/test/advanced",
    responses(
        (status = 200, description = "Test results", body = String)
    )
)]
pub async fn test_advanced(project_dir: web::Data<std::path::PathBuf>) -> impl Responder {
    let start = Instant::now();
    let project_dir_str = project_dir.to_str().unwrap_or("");
    let root_dir_str = std::path::Path::new(project_dir_str).join("wwwroot").to_str().unwrap_or("").to_string();

    // Enable logging temporarily for tests
    let original_log_level = Logger::get_log_level();

    // Configure logger with context-specific log files for AdvancedTests
    let template_analysis_dir = std::path::Path::new(project_dir_str).join("template_analysis");
    let logs_dir = template_analysis_dir.join("logs");
    let _ = std::fs::create_dir_all(&logs_dir);

    let mut context_log_files = std::collections::HashMap::new();
    context_log_files.insert("LoaderNormal".to_string(), logs_dir.join("rust_loadernormal.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("LoaderPreProcess".to_string(), logs_dir.join("rust_loaderpreprocess.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("EngineNormal".to_string(), logs_dir.join("rust_enginenormal.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("EnginePreProcess".to_string(), logs_dir.join("rust_enginepreprocess.log").to_str().unwrap_or("").to_string());

    use assembler::common::logger::LogLevel;
    Logger::configure(LogLevel::DEBUG, None, false, assembler::common::logger::LogRotation::NONE);
    Logger::configure_context_log_files(context_log_files);

    // Get scenarios from ConfigUtil
    let scenarios = match assembler::config::config_util::ConfigUtil::get_scenarios() {
        Ok(s) => s,
        Err(e) => {
            Logger::set_log_level(original_log_level);
            return HttpResponse::InternalServerError().body(format!("Failed to load scenarios: {}", e));
        }
    };

    // Dump preprocessed template structures before running advanced tests
    TestingUtils::dump_preprocessed_template_structures(&root_dir_str, project_dir_str, &scenarios, true);

    let results = TestingUtils::run_advanced_tests(&root_dir_str, project_dir_str, &scenarios, false, false, true);
    if !results.is_empty() {
        TestingUtils::print_test_summary_table(&root_dir_str, project_dir_str, &results, "ADVANCED TEST");
    }
    let elapsed = start.elapsed().as_secs_f64();
    let test_count = results.len();

    // Check for failures
    let failed_count = results.iter().filter(|r|
        r.normal_preprocess == "FAIL" ||
        r.cross_view_unmatch == "FAIL" ||
        !r.error.is_empty()
    ).count();

    let mut message = format!("Successful run of Advanced Tests in {:.2} secs ({} tests)", elapsed, test_count);
    if failed_count > 0 {
        message.push_str(&format!("\n⚠️ Warning: {} test(s) failed", failed_count));
    }

    let response = json!({
        "success": true,
        "message": message,
        "elapsed": elapsed,
        "testCount": test_count
    });

    // Restore original log level
    Logger::set_log_level(original_log_level);

    HttpResponse::Ok()
        .content_type("application/json")
        .body(response.to_string())
}

/// POST /test/performance - Run performance tests
#[utoipa::path(
    post,
    path = "/test/performance",
    responses(
        (status = 200, description = "Performance test results", body = String)
    )
)]
pub async fn test_performance(project_dir: web::Data<std::path::PathBuf>) -> impl Responder {
    let start = Instant::now();
    let project_dir_str = project_dir.to_str().unwrap_or("");
    let root_dir_str = std::path::Path::new(project_dir_str).join("wwwroot").to_str().unwrap_or("").to_string();

    // Get scenarios from ConfigUtil
    let scenarios = match assembler::config::config_util::ConfigUtil::get_scenarios() {
        Ok(s) => s,
        Err(e) => {
            return HttpResponse::InternalServerError().body(format!("Failed to load scenarios: {}", e));
        }
    };

    // Disable logging during performance tests
    use assembler::common::logger::LogLevel;
    let original_log_level = Logger::get_log_level();
    Logger::set_log_level(LogLevel::NONE);

    let results = PerformanceUtils::run_performance_comparison(&root_dir_str, project_dir_str, &scenarios, true, true);

    // Restore logging
    Logger::set_log_level(original_log_level);

    if !results.is_empty() {
        PerformanceUtils::print_perf_summary_table(&root_dir_str, project_dir_str, &results);
    }

    let elapsed = start.elapsed().as_secs_f64();
    let test_count = results.len();

    // Check for performance test mismatches
    let mismatch_count = results.iter().filter(|r| r.results_match != "YES").count();

    let mut message = format!("Successful run of Performance Tests in {:.2} secs ({} tests)", elapsed, test_count);
    if mismatch_count > 0 {
        message.push_str(&format!("\n⚠️ Warning: {} test(s) have output mismatch", mismatch_count));
    }

    let response = json!({
        "success": true,
        "message": message,
        "elapsed": elapsed,
        "testCount": test_count
    });

    HttpResponse::Ok()
        .content_type("application/json")
        .body(response.to_string())
}

/// POST /test/consolidate-performance - Consolidate performance data from all servers
#[utoipa::path(
    post,
    path = "/test/consolidate-performance",
    responses(
        (status = 200, description = "Consolidated performance results", body = String)
    )
)]
pub async fn test_consolidate_performance(project_dir: web::Data<std::path::PathBuf>) -> impl Responder {
    let start = Instant::now();
    let project_dir_str = project_dir.to_str().unwrap_or("");
    let root_dir_str = std::path::Path::new(project_dir_str).join("wwwroot").to_str().unwrap_or("").to_string();

    // Configure logging for consolidate endpoint
    let template_analysis_dir = std::path::Path::new(project_dir_str).join("template_analysis");
    let logs_dir = template_analysis_dir.join("logs");
    let _ = std::fs::create_dir_all(&logs_dir);
    let consolidate_log_file = logs_dir.join("rust_consolidate_perf.log");

    // Log start
    let log_msg = format!("\n[{}] Starting consolidate-performance endpoint\n", chrono::Utc::now().to_rfc3339());
    let _ = std::fs::OpenOptions::new().create(true).append(true).open(&consolidate_log_file)
        .and_then(|mut f| std::io::Write::write_all(&mut f, log_msg.as_bytes()));

    // Read server configuration from servers.csv
    let servers_config_path = std::path::Path::new(&root_dir_str).join("App_Data").join("servers.csv");
    let mut servers = Vec::new();

    if servers_config_path.exists() {
        if let Ok(csv_content) = std::fs::read_to_string(&servers_config_path) {
            for line in csv_content.lines() {
                let line = line.trim();
                if line.is_empty() {
                    continue;
                }
                let parts: Vec<&str> = line.split(',').collect();
                if parts.len() >= 3 {
                    let language = parts[0].trim();
                    let method = parts[1].trim().to_uppercase();
                    let url = parts[2].trim();
                    let file_name = if parts.len() >= 4 { parts[3].trim() } else { "" };
                    if !language.is_empty() && !method.is_empty() && !url.is_empty() {
                        servers.push((language.to_string(), method, url.to_string(), file_name.to_string()));
                    }
                }
            }
        }
    }

    if servers.is_empty() {
        let error_msg = "No server configuration found. Please configure servers in App_Data/servers.csv";
        let log_msg = format!("[{}] ❌ {}\n", chrono::Utc::now().to_rfc3339(), error_msg);
        let _ = std::fs::OpenOptions::new().create(true).append(true).open(&consolidate_log_file)
            .and_then(|mut f| std::io::Write::write_all(&mut f, log_msg.as_bytes()));

        let response = json!({
            "success": false,
            "message": error_msg,
            "elapsed": start.elapsed().as_secs_f64(),
            "testCount": 0
        });

        return HttpResponse::Ok()
            .content_type("application/json")
            .body(response.to_string());
    }

    let client = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(120))
        .build()
        .unwrap();

    use std::collections::BTreeMap;
    use std::collections::HashMap;
    let mut app_perf: BTreeMap<String, BTreeMap<String, (Option<f64>, Option<f64>, Option<i32>, String)>> = BTreeMap::new();
    let mut servers_processed = Vec::new();
    let mut servers_failed = Vec::new();

    // Group servers by language
    let mut servers_by_lang: HashMap<String, Vec<(String, String, String)>> = HashMap::new();
    for (lang, method, url, file_name) in &servers {
        servers_by_lang.entry(lang.clone())
            .or_insert_with(Vec::new)
            .push((method.clone(), url.clone(), file_name.clone()));
    }

    for (lang, lang_servers) in &servers_by_lang {
        let mut lang_success = false;
        let mut lang_errors = Vec::new();

        for (method, url, file_name) in lang_servers {
            // Log fetch attempt
            let log_msg = if method == "POST" {
                format!("[{}] Fetching {} via POST {} (fileName: {})\n", chrono::Utc::now().to_rfc3339(), lang, url, file_name)
            } else {
                let full_url = format!("{}{}", url, file_name);
                format!("[{}] Fetching {} via GET {}\n", chrono::Utc::now().to_rfc3339(), lang, full_url)
            };
            let _ = std::fs::OpenOptions::new().create(true).append(true).open(&consolidate_log_file)
                .and_then(|mut f| std::io::Write::write_all(&mut f, log_msg.as_bytes()));

            let result = if method == "POST" {
                let report_request = json!({
                    "fileName": file_name,
                    "useLangPrefix": false
                });
                let json_body = serde_json::to_string(&report_request).unwrap();
                client.post(url)
                    .header("Content-Type", "application/json")
                    .body(json_body)
                    .send()
                    .await
            } else {
                let full_url = format!("{}{}", url, file_name);
                client.get(&full_url).send().await
            };

            match result {
                Ok(resp) if resp.status().is_success() => {
                if let Ok(content) = resp.text().await {
                    if let Ok(arr) = serde_json::from_str::<serde_json::Value>(&content) {
                        if let Some(items) = arr.as_array() {
                            let item_count = items.len();
                            for item in items {
                                let app_site = item.get("AppSite").or_else(|| item.get("app_site")).or_else(|| item.get("appSite"))
                                    .and_then(|v| v.as_str()).unwrap_or("").to_string();
                                let app_view = item.get("AppView").or_else(|| item.get("app_view")).or_else(|| item.get("appView"))
                                    .and_then(|v| v.as_str()).unwrap_or("").to_string();

                                let normal_time = item.get("NormalTimeMs").or_else(|| item.get("normal_time_ms")).or_else(|| item.get("normalTimeMs"))
                                    .and_then(|v| v.as_f64())
                                    .or_else(|| item.get("NormalTimeNanos").or_else(|| item.get("normal_time_nanos"))
                                        .and_then(|v| v.as_f64()).map(|n| n / 1_000_000.0));

                                let preprocess_time = item.get("PreProcessTimeMs").or_else(|| item.get("preprocess_time_ms")).or_else(|| item.get("preProcessTimeMs"))
                                    .and_then(|v| v.as_f64())
                                    .or_else(|| item.get("PreProcessTimeNanos").or_else(|| item.get("preprocess_time_nanos"))
                                        .and_then(|v| v.as_f64()).map(|n| n / 1_000_000.0));

                                let output_size = item.get("OutputSize").or_else(|| item.get("output_size"))
                                    .and_then(|v| v.as_i64()).map(|v| v as i32);

                                if !app_site.is_empty() {
                                    let key = if app_view.is_empty() {
                                        app_site.clone()
                                    } else {
                                        format!("{} → {}", app_site, app_view)
                                    };

                                    // Use case-insensitive comparison for key matching
                                    let existing_key = app_perf.keys()
                                        .find(|k| k.eq_ignore_ascii_case(&key))
                                        .cloned();

                                    let final_key = existing_key.unwrap_or(key);
                                    app_perf.entry(final_key).or_insert_with(BTreeMap::new)
                                        .insert(lang.clone(), (normal_time, preprocess_time, output_size, app_view.clone()));
                                }
                            }
                            // Log success
                            let log_msg = format!("[{}] ✅ {}: Successfully processed {} items\n", chrono::Utc::now().to_rfc3339(), lang, item_count);
                            let _ = std::fs::OpenOptions::new().create(true).append(true).open(&consolidate_log_file)
                                .and_then(|mut f| std::io::Write::write_all(&mut f, log_msg.as_bytes()));
                        }
                    }
                }
                lang_success = true;
                break; // Success, no need to try other methods
            }
            Ok(resp) => {
                let domain = url.split("://").nth(1).and_then(|s| s.split('/').next()).unwrap_or(url);
                let error_msg = format!("{} {} (HTTP {})", method, domain, resp.status().as_u16());
                lang_errors.push(error_msg.clone());
                // Log warning
                let log_msg = format!("[{}] ⚠️ {}: {}\n", chrono::Utc::now().to_rfc3339(), lang, error_msg);
                let _ = std::fs::OpenOptions::new().create(true).append(true).open(&consolidate_log_file)
                    .and_then(|mut f| std::io::Write::write_all(&mut f, log_msg.as_bytes()));
            }
            Err(e) => {
                let domain = url.split("://").nth(1).and_then(|s| s.split('/').next()).unwrap_or(url);
                let error_msg = format!("{} {} (ERROR: {})", method, domain, e);
                lang_errors.push(error_msg.clone());
                // Log warning
                let log_msg = format!("[{}] ⚠️ {}: {}\n", chrono::Utc::now().to_rfc3339(), lang, error_msg);
                let _ = std::fs::OpenOptions::new().create(true).append(true).open(&consolidate_log_file)
                    .and_then(|mut f| std::io::Write::write_all(&mut f, log_msg.as_bytes()));
            }
        }
        }

        // After trying all methods for this language, determine overall result
        if lang_success {
            servers_processed.push(lang.clone());
        } else {
            let failure_msg = format!("{}: All methods failed - {}", lang, lang_errors.join("; "));
            servers_failed.push(failure_msg.clone());
            let log_msg = format!("[{}] ❌ {}: All methods failed\n", chrono::Utc::now().to_rfc3339(), lang);
            let _ = std::fs::OpenOptions::new().create(true).append(true).open(&consolidate_log_file)
                .and_then(|mut f| std::io::Write::write_all(&mut f, log_msg.as_bytes()));
        }
    }

    // Build HTML report
    let mut html = String::new();
    html.push_str("<!DOCTYPE html>\n");
    html.push_str("<html>\n");
    html.push_str("<head>\n");
    html.push_str("    <meta charset=\"UTF-8\">\n");
    html.push_str("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
    html.push_str("    <title>Consolidated Performance Summary</title>\n");
    html.push_str("    <style>\n");
    html.push_str("        body { font-family: Arial, sans-serif; margin: 20px; }\n");
    html.push_str("        h1 { color: #333; }\n");
    html.push_str("        h2 { color: #333; margin-top: 40px; }\n");
    html.push_str("        .meta { color: #666; font-style: italic; margin-bottom: 10px; }\n");
    html.push_str("        .table-container { overflow-x: auto; }\n");
    html.push_str("        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 700px; }\n");
    html.push_str("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n");
    html.push_str("        th { background-color: #4CAF50; color: white; }\n");
    html.push_str("        tr:nth-child(even) { background-color: #f2f2f2; }\n");
    html.push_str("        td:nth-child(2), td:nth-child(3), td:nth-child(4), td:nth-child(5), td:nth-child(6), td:nth-child(7) { text-align: right; }\n");
    html.push_str("        .best-perf { background-color: #90EE90; font-weight: bold; }\n");
    html.push_str("        @media (max-width: 768px) {\n");
    html.push_str("            body { margin: 10px; }\n");
    html.push_str("            th, td { padding: 8px; font-size: 14px; }\n");
    html.push_str("            h1 { font-size: 24px; }\n");
    html.push_str("            h2 { font-size: 20px; }\n");
    html.push_str("            .meta { font-size: 12px; }\n");
    html.push_str("        }\n");
    html.push_str("    </style>\n");
    html.push_str("</head>\n");
    html.push_str("<body>\n");
    html.push_str("    <h1>Consolidated Performance Summary</h1>\n");
    html.push_str(&format!("    <div class=\"meta\">Generated: {} UTC | All times in milliseconds (ms)</div>\n", chrono::Utc::now().format("%Y-%m-%d %H:%M:%S")));

    // Get list of languages dynamically from configuration
    let mut languages: Vec<String> = servers_by_lang.keys().map(|k| k.clone()).collect();
    languages.sort();

    // Normal Engine Table
    html.push_str("    <h2>Normal Engine</h2>\n");
    html.push_str("    <div class=\"table-container\">\n");
    html.push_str("    <table>\n");
    html.push_str("        <tr><th>AppSite/AppView</th>");
    for lang in &languages {
        html.push_str(&format!("<th>{}</th>", lang));
    }
    html.push_str("<th>OutputSize</th></tr>\n");
    for (app, langs) in &app_perf {
        // Find minimum time for highlighting
        let valid_times: Vec<f64> = languages.iter()
            .filter_map(|lang| langs.get(lang.as_str()).and_then(|t| t.0))
            .collect();
        let min_time = if !valid_times.is_empty() {
            valid_times.iter().cloned().fold(f64::INFINITY, f64::min)
        } else {
            f64::NAN
        };

        html.push_str(&format!("        <tr><td>{}</td>", app));
        for lang in &languages {
            let time_opt = langs.get(lang.as_str()).and_then(|t| t.0);
            let time_value = time_opt.map(|v| format!("{:.2}", v)).unwrap_or_else(|| "-".to_string());
            let is_best = time_opt.map(|v| !min_time.is_nan() && (v - min_time).abs() < 0.001).unwrap_or(false);
            let css_class = if is_best { " class=\"best-perf\"" } else { "" };
            html.push_str(&format!("<td{}>{}</td>", css_class, time_value));
        }
        let output_size = langs.values().find_map(|t| t.2).map(|v| v.to_string()).unwrap_or_else(|| "-".to_string());
        html.push_str(&format!("<td>{}</td></tr>\n", output_size));
    }
    html.push_str("    </table>\n");
    html.push_str("    </div>\n");

    // PreProcess Engine Table
    html.push_str("    <h2>PreProcess Engine</h2>\n");
    html.push_str("    <div class=\"table-container\">\n");
    html.push_str("    <table>\n");
    html.push_str("        <tr><th>AppSite/AppView</th>");
    for lang in &languages {
        html.push_str(&format!("<th>{}</th>", lang));
    }
    html.push_str("<th>OutputSize</th></tr>\n");
    for (app, langs) in &app_perf {
        // Find minimum time for highlighting
        let valid_times: Vec<f64> = languages.iter()
            .filter_map(|lang| langs.get(lang.as_str()).and_then(|t| t.1))
            .collect();
        let min_time = if !valid_times.is_empty() {
            valid_times.iter().cloned().fold(f64::INFINITY, f64::min)
        } else {
            f64::NAN
        };

        html.push_str(&format!("        <tr><td>{}</td>", app));
        for lang in &languages {
            let time_opt = langs.get(lang.as_str()).and_then(|t| t.1);
            let time_value = time_opt.map(|v| format!("{:.2}", v)).unwrap_or_else(|| "-".to_string());
            let is_best = time_opt.map(|v| !min_time.is_nan() && (v - min_time).abs() < 0.001).unwrap_or(false);
            let css_class = if is_best { " class=\"best-perf\"" } else { "" };
            html.push_str(&format!("<td{}>{}</td>", css_class, time_value));
        }
        let output_size = langs.values().find_map(|t| t.2).map(|v| v.to_string()).unwrap_or_else(|| "-".to_string());
        html.push_str(&format!("<td>{}</td></tr>\n", output_size));
    }
    html.push_str("    </table>\n");
    html.push_str("    </div>\n");
    html.push_str("</body>\n</html>");

    // Write HTML to Reports directory
    let reports_dir = std::path::Path::new(project_dir_str).join("template_analysis").join("Reports");
    let _ = std::fs::create_dir_all(&reports_dir);
    let html_path = reports_dir.join("all_perf_tests.html");
    let _ = std::fs::write(&html_path, html);

    let elapsed = start.elapsed().as_secs_f64();

    // Log completion
    let total_languages = servers_by_lang.len();
    let log_msg = format!("[{}] Consolidation complete in {:.2}s - {} AppSites from {}/{} languages\n",
        chrono::Utc::now().to_rfc3339(), elapsed, app_perf.len(), servers_processed.len(), total_languages);
    let _ = std::fs::OpenOptions::new().create(true).append(true).open(&consolidate_log_file)
        .and_then(|mut f| std::io::Write::write_all(&mut f, log_msg.as_bytes()));

    let mut message = format!("Consolidated {} AppSites from {}/{} languages in {:.2} secs",
        app_perf.len(), servers_processed.len(), total_languages, elapsed);

    if !servers_processed.is_empty() {
        message.push_str(&format!(" | ✅ Success: {}", servers_processed.join(", ")));
    }

    if !servers_failed.is_empty() {
        message.push_str(&format!("\n❌ Failed: {}", servers_failed.join("; ")));
    }

    let response = json!({
        "success": servers_processed.len() > 0,
        "message": message,
        "elapsed": elapsed,
        "testCount": servers_processed.len()
    });

    HttpResponse::Ok()
        .content_type("application/json")
        .body(response.to_string())
}


/// POST /api/report - Retrieve a report file
#[utoipa::path(
    post,
    path = "/api/report",
    request_body = ReportRequest,
    responses(
        (status = 200, description = "Report file contents", body = String),
        (status = 400, description = "Bad request"),
        (status = 404, description = "Report not found")
    )
)]
pub async fn get_report(req: web::Json<ReportRequest>, project_dir: web::Data<std::path::PathBuf>) -> impl Responder {
    // Validate required fields
    let file_name = match &req.file_name {
        Some(f) if !f.is_empty() => f,
        _ => return HttpResponse::BadRequest().body("Missing required field: fileName"),
    };

    // Validate path component for path traversal attacks
    if !security_validator::is_valid_path_component(Some(file_name)) {
        return HttpResponse::BadRequest().body("Invalid characters in fileName");
    }

    // Apply language prefix if requested
    let use_lang_prefix = req.use_lang_prefix.unwrap_or(false);
    let final_file_name = if use_lang_prefix {
        format!("rust_{}", file_name)
    } else {
        file_name.clone()
    };

    // Construct file path in template_analysis/Reports
    let reports_dir = project_dir.as_ref().join("template_analysis").join("Reports");
    let file_path = reports_dir.join(&final_file_name);

    // Read and return file
    match std::fs::read_to_string(&file_path) {
        Ok(content) => {
            // Determine content type based on file extension
            let content_type = if final_file_name.ends_with(".html") {
                "text/html"
            } else if final_file_name.ends_with(".json") {
                "application/json"
            } else if final_file_name.ends_with(".md") {
                "text/markdown"
            } else {
                "text/plain"
            };

            HttpResponse::Ok()
                .content_type(content_type)
                .body(content)
        }
        Err(_) => {
            HttpResponse::NotFound().body(format!("Report not found: {}", final_file_name))
        }
    }
}

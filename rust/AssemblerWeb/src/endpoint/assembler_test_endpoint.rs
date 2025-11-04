use serde_json::json;
use actix_web::{web, HttpRequest, HttpResponse, Responder};
use serde::{Deserialize, Serialize};
use std::time::Instant;
use arshu::common::{Logger, LogLevel};
use assembler::test::testing_utils::TestingUtils;
use assembler::performance::performance_utils::PerformanceUtils;
use crate::endpoint::security_validator;

// Configurable rule groups for consolidated report grouping
const RULE_GROUPS: &[&str] = &[
    "HtmlRule1",
    "HtmlRule2",
    "HtmlRule3",
    "JsonRule1",
    "JsonRule2",
    "Rule1"
];

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct ReportRequest {
    pub file_name: Option<String>,
    pub use_lang_prefix: Option<bool>,
    pub lang_prefix: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct TestResponse {
    pub message: String,
}

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "PascalCase")]
pub struct TestSummaryRowDto {
    pub app_site: String,
    pub app_file: String,
    pub app_view: String,
    pub normal_pre_process: String,
    pub cross_view_un_match: String,
    pub error: String,
}

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "PascalCase")]
pub struct PerfSummaryRowDto {
    pub app_site: String,
    pub app_file: String,
    pub app_view: String,
    pub iterations: i32,
    pub normal_time_ms: f64,
    pub pre_process_time_ms: f64,
    pub output_size: i32,
    pub results_match: String,
    pub perf_difference: String,
    pub scenario_total_time_ms: i32,
    pub elapsed_time_ms: i32,
}

/// POST /test/standard - Run standard tests
#[utoipa::path(
    post,
    path = "/test/standard",
    responses(
        (status = 200, description = "Test results", body = String)
    )
)]
pub async fn test_standard() -> impl Responder {
    let start = Instant::now();
    let project_dir = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let project_dir_str = project_dir.to_str().unwrap_or("");
    let root_dir_str = std::path::Path::new(project_dir_str).join("wwwroot").to_str().unwrap_or("").to_string();

    // Enable logging temporarily for tests
    let original_log_level = Logger::get_log_level();

    // Configure logger with context-specific log files for StandardTests
    let template_analysis_dir = std::path::Path::new(project_dir_str).join("Analysis");
    let logs_dir = template_analysis_dir.join("logs");
    let _ = std::fs::create_dir_all(&logs_dir);

    let mut context_log_files = std::collections::HashMap::new();
    context_log_files.insert("LoaderNormal".to_string(), logs_dir.join("rust_loadernormal.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("EngineNormal".to_string(), logs_dir.join("rust_enginenormal.log").to_str().unwrap_or("").to_string());

    // LogLevel already imported at top
    Logger::configure(LogLevel::DEBUG, false, arshu::common::LogRotation::NONE);
    Logger::add_context_log_files(context_log_files);

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
pub async fn test_advanced() -> impl Responder {
    let start = Instant::now();
    let project_dir = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let project_dir_str = project_dir.to_str().unwrap_or("");
    let root_dir_str = std::path::Path::new(project_dir_str).join("wwwroot").to_str().unwrap_or("").to_string();

    // Enable logging temporarily for tests
    let original_log_level = Logger::get_log_level();

    // Configure logger with context-specific log files for AdvancedTests
    let template_analysis_dir = std::path::Path::new(project_dir_str).join("Analysis");
    let logs_dir = template_analysis_dir.join("logs");
    let _ = std::fs::create_dir_all(&logs_dir);

    let mut context_log_files = std::collections::HashMap::new();
    context_log_files.insert("LoaderNormal".to_string(), logs_dir.join("rust_loadernormal.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("LoaderPreProcess".to_string(), logs_dir.join("rust_loaderpreprocess.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("EngineNormal".to_string(), logs_dir.join("rust_enginenormal.log").to_str().unwrap_or("").to_string());
    context_log_files.insert("EnginePreProcess".to_string(), logs_dir.join("rust_enginepreprocess.log").to_str().unwrap_or("").to_string());

    // LogLevel already imported at top
    Logger::configure(LogLevel::DEBUG, false, arshu::common::LogRotation::NONE);
    Logger::add_context_log_files(context_log_files);

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
pub async fn test_performance() -> impl Responder {
    let start = Instant::now();
    let project_dir = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
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
    // LogLevel already imported at top
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
pub async fn test_consolidate_performance() -> impl Responder {
    let start = Instant::now();
    let project_dir = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let project_dir_str = project_dir.to_str().unwrap_or("");
    let root_dir_str = std::path::Path::new(project_dir_str).join("wwwroot").to_str().unwrap_or("").to_string();

    // Configure logging for consolidate endpoint
    let template_analysis_dir = std::path::Path::new(project_dir_str).join("Analysis");
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
    html.push_str("        .worst-perf { background-color: #FFB6C6; font-weight: bold; }\n");
    html.push_str("        .avg-perf { background-color: #FFD700; font-weight: bold; }\n");
    html.push_str("        .legend { display: flex; gap: 20px; margin: 20px 0; flex-wrap: wrap; }\n");
    html.push_str("        .legend-item { display: flex; align-items: center; gap: 8px; }\n");
    html.push_str("        .legend-box { width: 24px; height: 24px; border: 1px solid #999; }\n");
    html.push_str("        .view-toggle { margin: 20px 0; }\n");
    html.push_str("        .view-btn { padding: 10px 20px; margin-right: 10px; cursor: pointer; border: 2px solid #4CAF50; background: white; color: #4CAF50; font-size: 14px; border-radius: 5px; }\n");
    html.push_str("        .view-btn.active { background: #4CAF50; color: white; }\n");
    html.push_str("        .view-content { display: none; }\n");
    html.push_str("        .view-content.active { display: block; }\n");
    html.push_str("        .chart-container { margin: 20px 0; }\n");
    html.push_str("        .chart-row { margin-bottom: 25px; }\n");
    html.push_str("        .chart-label { font-weight: bold; margin-bottom: 8px; font-size: 14px; color: #333; }\n");
    html.push_str("        .chart-bars-container { display: flex; flex-direction: column; gap: 8px; }\n");
    html.push_str("        .chart-bar-wrapper { display: flex; align-items: center; gap: 10px; }\n");
    html.push_str("        .chart-bar-label { min-width: 80px; font-weight: 600; color: #555; font-size: 13px; }\n");
    html.push_str("        .chart-bar { height: 30px; border-radius: 5px; display: flex; align-items: center; justify-content: flex-end; padding-right: 10px; color: white; font-weight: bold; font-size: 12px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); transition: transform 0.2s; min-width: 40px; }\n");
    html.push_str("        .chart-bar:hover { transform: translateX(5px); box-shadow: 0 4px 8px rgba(0,0,0,0.15); }\n");
    html.push_str("        .chart-bar-value { margin-left: 10px; font-weight: 600; color: #333; font-size: 13px; min-width: 60px; }\n");
    html.push_str("        .grouped-chart-section { margin-bottom: 40px; padding: 20px; background: #f9f9f9; border-radius: 8px; }\n");
    html.push_str("        .grouped-chart-title { font-size: 1.3em; font-weight: bold; color: #667eea; margin-bottom: 15px; border-bottom: 2px solid #667eea; padding-bottom: 8px; }\n");
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
    html.push_str(&format!("    <div class=\"meta\">Generated: {} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>\n", chrono::Utc::now().format("%Y-%m-%d %H:%M:%S")));
    html.push_str("    <div class=\"legend\">\n");
    html.push_str("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #4CAF50; opacity: 0.8;\"></div><span>Normal Engine (N)</span></div>\n");
    html.push_str("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #2196F3; opacity: 0.8;\"></div><span>PreProcess Engine (P)</span></div>\n");
    html.push_str("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #90EE90;\"></div><span>Best (Lowest Time - Table View)</span></div>\n");
    html.push_str("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #FFD700;\"></div><span>Nearest to Average (Table View)</span></div>\n");
    html.push_str("        <div class=\"legend-item\"><div class=\"legend-box\" style=\"background-color: #FFB6C6;\"></div><span>Worst (Highest Time - Table View)</span></div>\n");
    html.push_str("    </div>\n");
    html.push_str("    <div class=\"view-toggle\">\n");
    html.push_str("        <button class=\"view-btn active\" data-view=\"grouped\">Grouped View</button>\n");
    html.push_str("        <button class=\"view-btn\" data-view=\"chart\">Bar Chart View</button>\n");
    html.push_str("        <button class=\"view-btn\" data-view=\"table\">Table View</button>\n");
    html.push_str("    </div>\n");

    // Get list of languages dynamically from configuration
    let mut languages: Vec<String> = servers_by_lang.keys().map(|k| k.clone()).collect();
    languages.sort();

    // Grouped Bar Chart View (active by default)
    html.push_str("    <div id=\"combined-grouped\" class=\"view-content active\">\n");
    html.push_str("        <div class=\"chart-container\">\n");

    for rule_pattern in RULE_GROUPS {
        // Find all apps matching this rule pattern (excluding Test AppSite)
        let matching_apps: Vec<&String> = app_perf.keys()
            .filter(|app| app.starts_with(rule_pattern) && !app.contains("Test"))
            .collect();

        if matching_apps.is_empty() {
            continue;
        }

        html.push_str("            <div class=\"grouped-chart-section\">\n");
        html.push_str(&format!("                <div class=\"grouped-chart-title\">{}</div>\n", rule_pattern));
        html.push_str("                <div class=\"chart-bars-container\">\n");

        // Calculate max time across ALL languages in this rule group for consistent scaling
        let mut all_max_values = Vec::new();
        for lang in &languages {
            let normal_times: Vec<f64> = matching_apps.iter()
                .filter_map(|app| app_perf.get(*app).and_then(|langs| langs.get(lang.as_str()).and_then(|t| t.0)))
                .filter(|&v| v > 0.0)
                .collect();
            let preprocess_times: Vec<f64> = matching_apps.iter()
                .filter_map(|app| app_perf.get(*app).and_then(|langs| langs.get(lang.as_str()).and_then(|t| t.1)))
                .filter(|&v| v > 0.0)
                .collect();

            if !normal_times.is_empty() {
                all_max_values.push(normal_times.iter().cloned().fold(f64::NEG_INFINITY, f64::max));
            }
            if !preprocess_times.is_empty() {
                all_max_values.push(preprocess_times.iter().cloned().fold(f64::NEG_INFINITY, f64::max));
            }
        }
        let max_time_for_scale = if !all_max_values.is_empty() {
            all_max_values.iter().cloned().fold(f64::NEG_INFINITY, f64::max)
        } else {
            1.0
        };

        // For each language, calculate min/avg/max across all apps in this rule group
        for lang in &languages {
            // Collect Normal Engine times
            let normal_times: Vec<f64> = matching_apps.iter()
                .filter_map(|app| app_perf.get(*app).and_then(|langs| langs.get(lang.as_str()).and_then(|t| t.0)))
                .filter(|&v| v > 0.0)
                .collect();

            // Collect PreProcess Engine times
            let preprocess_times: Vec<f64> = matching_apps.iter()
                .filter_map(|app| app_perf.get(*app).and_then(|langs| langs.get(lang.as_str()).and_then(|t| t.1)))
                .filter(|&v| v > 0.0)
                .collect();

            if normal_times.is_empty() && preprocess_times.is_empty() {
                continue;
            }

            // Calculate aggregates
            let normal_min = if !normal_times.is_empty() { Some(normal_times.iter().cloned().fold(f64::INFINITY, f64::min)) } else { None };
            let normal_avg = if !normal_times.is_empty() { Some(normal_times.iter().sum::<f64>() / normal_times.len() as f64) } else { None };
            let normal_max = if !normal_times.is_empty() { Some(normal_times.iter().cloned().fold(f64::NEG_INFINITY, f64::max)) } else { None };

            let preprocess_min = if !preprocess_times.is_empty() { Some(preprocess_times.iter().cloned().fold(f64::INFINITY, f64::min)) } else { None };
            let preprocess_avg = if !preprocess_times.is_empty() { Some(preprocess_times.iter().sum::<f64>() / preprocess_times.len() as f64) } else { None };
            let preprocess_max = if !preprocess_times.is_empty() { Some(preprocess_times.iter().cloned().fold(f64::NEG_INFINITY, f64::max)) } else { None };

            html.push_str("                    <div class=\"chart-bar-wrapper\">\n");
            html.push_str(&format!("                        <div class=\"chart-bar-label\">{}</div>\n", lang));
            html.push_str("                        <div style=\"position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;\">\n");

            // Normal Engine Bar (showing min, avg, max as segments)
            if let (Some(min), Some(avg), Some(max)) = (normal_min, normal_avg, normal_max) {
                let min_width = (min / max_time_for_scale) * 100.0;
                let avg_width = (avg / max_time_for_scale) * 100.0;
                let max_width = (max / max_time_for_scale) * 100.0;

                // Draw max bar (light green background)
                html.push_str(&format!("                            <div style=\"position: absolute; left: 0; top: 0; width: {:.2}%; height: 15px; background-color: #90EE90; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{} Normal Max: {:.2}ms\"></div>\n", max_width, lang, max));
                // Draw avg bar (gold - middle layer)
                html.push_str(&format!("                            <div style=\"position: absolute; left: 0; top: 0; width: {:.2}%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{} Normal Avg: {:.2}ms\"></div>\n", avg_width, lang, avg));
                // Draw min bar (dark green - top layer)
                html.push_str(&format!("                            <div style=\"position: absolute; left: 0; top: 0; width: {:.2}%; height: 15px; background-color: #4CAF50; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{} Normal Min: {:.2}ms\"></div>\n", min_width, lang, min));
                // Label
                let label_style = if max_width > 85.0 {
                    format!("position: absolute; right: calc(100% - {:.2}% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;", max_width)
                } else {
                    format!("position: absolute; left: calc({:.2}% + 5px); top: 0; font-size: 11px; color: #4CAF50; font-weight: 600; white-space: nowrap;", max_width)
                };
                html.push_str(&format!("                            <span style=\"{}\">N: {:.2}/{:.2}/{:.2}</span>\n", label_style, min, avg, max));
            }

            // PreProcess Engine Bar (showing min, avg, max as segments)
            if let (Some(min), Some(avg), Some(max)) = (preprocess_min, preprocess_avg, preprocess_max) {
                let min_width = (min / max_time_for_scale) * 100.0;
                let avg_width = (avg / max_time_for_scale) * 100.0;
                let max_width = (max / max_time_for_scale) * 100.0;

                // Draw max bar (light pink background)
                html.push_str(&format!("                            <div style=\"position: absolute; left: 0; top: 15px; width: {:.2}%; height: 15px; background-color: #FFB6C6; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{} PreProcess Max: {:.2}ms\"></div>\n", max_width, lang, max));
                // Draw avg bar (gold - middle layer)
                html.push_str(&format!("                            <div style=\"position: absolute; left: 0; top: 15px; width: {:.2}%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{} PreProcess Avg: {:.2}ms\"></div>\n", avg_width, lang, avg));
                // Draw min bar (dark blue - top layer)
                html.push_str(&format!("                            <div style=\"position: absolute; left: 0; top: 15px; width: {:.2}%; height: 15px; background-color: #2196F3; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{} PreProcess Min: {:.2}ms\"></div>\n", min_width, lang, min));
                // Label
                let label_style = if max_width > 85.0 {
                    format!("position: absolute; right: calc(100% - {:.2}% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;", max_width)
                } else {
                    format!("position: absolute; left: calc({:.2}% + 5px); top: 15px; font-size: 11px; color: #2196F3; font-weight: 600; white-space: nowrap;", max_width)
                };
                html.push_str(&format!("                            <span style=\"{}\">P: {:.2}/{:.2}/{:.2}</span>\n", label_style, min, avg, max));
            }

            html.push_str("                        </div>\n");
            html.push_str("                    </div>\n");
        }

        html.push_str("                </div>\n");
        html.push_str("            </div>\n");
    }

    html.push_str("        </div>\n");
    html.push_str("    </div>\n");

    // Bar Chart View - Individual AppSites with performance bars for each language
    html.push_str("    <div id=\"combined-chart\" class=\"view-content\">\n");
    html.push_str("        <div class=\"chart-container\">\n");

    // Generate combined chart data showing both engines (filter by rule groups)
    let filtered_apps: Vec<&String> = app_perf.keys()
        .filter(|app| RULE_GROUPS.iter().any(|rule| app.starts_with(rule)))
        .collect();

    for app in filtered_apps.iter() {
        html.push_str("            <div class=\"chart-row\">\n");
        html.push_str(&format!("                <div class=\"chart-label\">{}</div>\n", app));
        html.push_str("                <div class=\"chart-bars-container\">\n");

        let langs = app_perf.get(*app).unwrap();

        // Calculate max time across BOTH engines for consistent scaling
        let mut all_times = Vec::new();
        for lang in &languages {
            if let Some(times) = langs.get(lang.as_str()) {
                if let Some(normal_time) = times.0 {
                    if normal_time > 0.0 {
                        all_times.push(normal_time);
                    }
                }
                if let Some(preprocess_time) = times.1 {
                    if preprocess_time > 0.0 {
                        all_times.push(preprocess_time);
                    }
                }
            }
        }
        let max_time_for_scale = if !all_times.is_empty() {
            all_times.iter().cloned().fold(f64::NEG_INFINITY, f64::max)
        } else {
            1.0
        };

        // Calculate highlighting for Normal Engine
        let normal_valid_times: Vec<f64> = languages.iter()
            .filter_map(|lang| langs.get(lang.as_str()).and_then(|t| t.0))
            .filter(|&v| v > 0.0)
            .collect();
        let normal_min_time = if !normal_valid_times.is_empty() {
            Some(normal_valid_times.iter().cloned().fold(f64::INFINITY, f64::min))
        } else {
            None
        };
        let normal_max_time = if !normal_valid_times.is_empty() {
            Some(normal_valid_times.iter().cloned().fold(f64::NEG_INFINITY, f64::max))
        } else {
            None
        };
        let normal_avg_time = if !normal_valid_times.is_empty() {
            Some(normal_valid_times.iter().sum::<f64>() / normal_valid_times.len() as f64)
        } else {
            None
        };

        // Calculate highlighting for PreProcess Engine
        let preprocess_valid_times: Vec<f64> = languages.iter()
            .filter_map(|lang| langs.get(lang.as_str()).and_then(|t| t.1))
            .filter(|&v| v > 0.0)
            .collect();
        let preprocess_min_time = if !preprocess_valid_times.is_empty() {
            Some(preprocess_valid_times.iter().cloned().fold(f64::INFINITY, f64::min))
        } else {
            None
        };
        let preprocess_max_time = if !preprocess_valid_times.is_empty() {
            Some(preprocess_valid_times.iter().cloned().fold(f64::NEG_INFINITY, f64::max))
        } else {
            None
        };
        let preprocess_avg_time = if !preprocess_valid_times.is_empty() {
            Some(preprocess_valid_times.iter().sum::<f64>() / preprocess_valid_times.len() as f64)
        } else {
            None
        };

        for lang in &languages {
            if let Some(times) = langs.get(lang.as_str()) {
                let normal_time = times.0;
                let preprocess_time = times.1;

                if (normal_time.is_some() && normal_time.unwrap() > 0.0) || (preprocess_time.is_some() && preprocess_time.unwrap() > 0.0) {
                    html.push_str("                    <div class=\"chart-bar-wrapper\">\n");
                    html.push_str(&format!("                        <div class=\"chart-bar-label\">{}</div>\n", lang));
                    html.push_str("                        <div style=\"position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;\">\n");

                    // Normal Engine Bar (bottom layer) with label at the end
                    if let Some(normal_time_value) = normal_time {
                        if normal_time_value > 0.0 {
                            let width_percent = (normal_time_value / max_time_for_scale) * 100.0;

                            // Determine highlight color
                            let mut normal_bg_color = "#4CAF50"; // default green
                            if let Some(min_time) = normal_min_time {
                                if (normal_time_value - min_time).abs() < 0.01 {
                                    normal_bg_color = "#90EE90"; // best (light green)
                                }
                            }
                            if let Some(max_time) = normal_max_time {
                                if (normal_time_value - max_time).abs() < 0.01 {
                                    normal_bg_color = "#FFB6C6"; // worst (light red)
                                }
                            }
                            if let Some(avg_time) = normal_avg_time {
                                if normal_valid_times.len() > 2 {
                                    let nearest_to_avg = normal_valid_times.iter().cloned()
                                        .min_by(|a, b| (a - avg_time).abs().partial_cmp(&(b - avg_time).abs()).unwrap()).unwrap();
                                    if (normal_time_value - nearest_to_avg).abs() < 0.01 {
                                        normal_bg_color = "#FFD700"; // avg (gold)
                                    }
                                }
                            }

                            // Position label: inside bar if very wide (>85%), otherwise outside at end
                            let normal_label_style = if width_percent > 85.0 {
                                format!("position: absolute; right: calc(100% - {:.2}% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;", width_percent)
                            } else {
                                format!("position: absolute; left: calc({:.2}% + 5px); top: 0; font-size: 11px; color: {}; font-weight: 600; white-space: nowrap;", width_percent, normal_bg_color)
                            };
                            html.push_str(&format!("                            <div style=\"position: absolute; left: 0; top: 0; width: {:.2}%; height: 15px; background-color: {}; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{} Normal: {:.2}ms\"></div>\n", width_percent, normal_bg_color, lang, normal_time_value));
                            html.push_str(&format!("                            <span style=\"{}\">N: {:.2}ms</span>\n", normal_label_style, normal_time_value));
                        }
                    }

                    // PreProcess Engine Bar (top layer, slightly offset) with label at the end
                    if let Some(preprocess_time_value) = preprocess_time {
                        if preprocess_time_value > 0.0 {
                            let width_percent = (preprocess_time_value / max_time_for_scale) * 100.0;

                            // Determine highlight color
                            let mut preprocess_bg_color = "#2196F3"; // default blue
                            if let Some(min_time) = preprocess_min_time {
                                if (preprocess_time_value - min_time).abs() < 0.01 {
                                    preprocess_bg_color = "#90EE90"; // best (light green)
                                }
                            }
                            if let Some(max_time) = preprocess_max_time {
                                if (preprocess_time_value - max_time).abs() < 0.01 {
                                    preprocess_bg_color = "#FFB6C6"; // worst (light red)
                                }
                            }
                            if let Some(avg_time) = preprocess_avg_time {
                                if preprocess_valid_times.len() > 2 {
                                    let nearest_to_avg = preprocess_valid_times.iter().cloned()
                                        .min_by(|a, b| (a - avg_time).abs().partial_cmp(&(b - avg_time).abs()).unwrap()).unwrap();
                                    if (preprocess_time_value - nearest_to_avg).abs() < 0.01 {
                                        preprocess_bg_color = "#FFD700"; // avg (gold)
                                    }
                                }
                            }

                            // Position label: inside bar if very wide (>85%), otherwise outside at end
                            let preprocess_label_style = if width_percent > 85.0 {
                                format!("position: absolute; right: calc(100% - {:.2}% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;", width_percent)
                            } else {
                                format!("position: absolute; left: calc({:.2}% + 5px); top: 15px; font-size: 11px; color: {}; font-weight: 600; white-space: nowrap;", width_percent, preprocess_bg_color)
                            };
                            html.push_str(&format!("                            <div style=\"position: absolute; left: 0; top: 15px; width: {:.2}%; height: 15px; background-color: {}; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);\" title=\"{} PreProcess: {:.2}ms\"></div>\n", width_percent, preprocess_bg_color, lang, preprocess_time_value));
                            html.push_str(&format!("                            <span style=\"{}\">P: {:.2}ms</span>\n", preprocess_label_style, preprocess_time_value));
                        }
                    }

                    html.push_str("                        </div>\n");
                    html.push_str("                    </div>\n");
                }
            }
        }

        html.push_str("                </div>\n");
        html.push_str("            </div>\n");
    }

    html.push_str("        </div>\n");
    html.push_str("    </div>\n");

    // Table View (Normal and PreProcess Engine Tables)
    html.push_str("    <div id=\"normal-table\" class=\"view-content\">\n");
    html.push_str("    <h2>Normal Engine</h2>\n");
    html.push_str("    <div class=\"table-container\">\n");
    html.push_str("    <table>\n");
    html.push_str("        <tr><th>AppSite/AppView</th>");
    for lang in &languages {
        html.push_str(&format!("<th>{}</th>", lang));
    }
    html.push_str("<th>OutputSize</th></tr>\n");

    // Filter apps by RULE_GROUPS
    let filtered_apps: Vec<&String> = app_perf.keys()
        .filter(|app| RULE_GROUPS.iter().any(|rule| app.starts_with(rule)))
        .collect();

    for app in filtered_apps.iter() {
        let langs = app_perf.get(*app).unwrap();
        // Find min, max, and avg time for highlighting (excluding zero values)
        let valid_times: Vec<f64> = languages.iter()
            .filter_map(|lang| langs.get(lang.as_str()).and_then(|t| t.0))
            .filter(|&v| v > 0.0)
            .collect();
        let min_time = if !valid_times.is_empty() { valid_times.iter().cloned().fold(f64::INFINITY, f64::min) } else { f64::NAN };
        let max_time = if !valid_times.is_empty() { valid_times.iter().cloned().fold(f64::NEG_INFINITY, f64::max) } else { f64::NAN };
        let avg_time = if !valid_times.is_empty() { valid_times.iter().sum::<f64>() / valid_times.len() as f64 } else { f64::NAN };

        html.push_str(&format!("        <tr><td>{}</td>", app));
        for lang in &languages {
            let time_opt = langs.get(lang.as_str()).and_then(|t| t.0);
            let time_value = time_opt.map(|v| format!("{:.2}", v)).unwrap_or_else(|| "-".to_string());

            let css_class = if let Some(current_time) = time_opt {
                if current_time > 0.0 {
                    if !min_time.is_nan() && (current_time - min_time).abs() < 0.001 {
                        " class=\"best-perf\""
                    } else if !max_time.is_nan() && (current_time - max_time).abs() < 0.001 {
                        " class=\"worst-perf\""
                    } else if !avg_time.is_nan() && valid_times.len() > 2 {
                        let nearest_to_avg = valid_times.iter().cloned().min_by(|a, b| (a - avg_time).abs().partial_cmp(&(b - avg_time).abs()).unwrap()).unwrap();
                        if (current_time - nearest_to_avg).abs() < 0.001 {
                            " class=\"avg-perf\""
                        } else {
                            ""
                        }
                    } else {
                        ""
                    }
                } else {
                    ""
                }
            } else {
                ""
            };

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

    for app in filtered_apps.iter() {
        let langs = app_perf.get(*app).unwrap();
        // Find min, max, and avg time for highlighting (excluding zero values)
        let valid_times: Vec<f64> = languages.iter()
            .filter_map(|lang| langs.get(lang.as_str()).and_then(|t| t.1))
            .filter(|&v| v > 0.0)
            .collect();
        let min_time = if !valid_times.is_empty() { valid_times.iter().cloned().fold(f64::INFINITY, f64::min) } else { f64::NAN };
        let max_time = if !valid_times.is_empty() { valid_times.iter().cloned().fold(f64::NEG_INFINITY, f64::max) } else { f64::NAN };
        let avg_time = if !valid_times.is_empty() { valid_times.iter().sum::<f64>() / valid_times.len() as f64 } else { f64::NAN };

        html.push_str(&format!("        <tr><td>{}</td>", app));
        for lang in &languages {
            let time_opt = langs.get(lang.as_str()).and_then(|t| t.1);
            let time_value = time_opt.map(|v| format!("{:.2}", v)).unwrap_or_else(|| "-".to_string());

            let css_class = if let Some(current_time) = time_opt {
                if current_time > 0.0 {
                    if !min_time.is_nan() && (current_time - min_time).abs() < 0.001 {
                        " class=\"best-perf\""
                    } else if !max_time.is_nan() && (current_time - max_time).abs() < 0.001 {
                        " class=\"worst-perf\""
                    } else if !avg_time.is_nan() && valid_times.len() > 2 {
                        let nearest_to_avg = valid_times.iter().cloned().min_by(|a, b| (a - avg_time).abs().partial_cmp(&(b - avg_time).abs()).unwrap()).unwrap();
                        if (current_time - nearest_to_avg).abs() < 0.001 {
                            " class=\"avg-perf\""
                        } else {
                            ""
                        }
                    } else {
                        ""
                    }
                } else {
                    ""
                }
            } else {
                ""
            };

            html.push_str(&format!("<td{}>{}</td>", css_class, time_value));
        }
        let output_size = langs.values().find_map(|t| t.2).map(|v| v.to_string()).unwrap_or_else(|| "-".to_string());
        html.push_str(&format!("<td>{}</td></tr>\n", output_size));
    }
    html.push_str("    </table>\n");
    html.push_str("    </div>\n");
    html.push_str("    </div>\n");

    // Add JavaScript for view switching
    html.push_str("    <script>\n");
    html.push_str("        document.querySelectorAll('.view-btn').forEach(btn => {\n");
    html.push_str("            btn.addEventListener('click', () => {\n");
    html.push_str("                document.querySelectorAll('.view-btn').forEach(b => b.classList.remove('active'));\n");
    html.push_str("                document.querySelectorAll('.view-content').forEach(v => v.classList.remove('active'));\n");
    html.push_str("                btn.classList.add('active');\n");
    html.push_str("                const view = btn.getAttribute('data-view');\n");
    html.push_str("                if (view === 'grouped') document.getElementById('combined-grouped').classList.add('active');\n");
    html.push_str("                else if (view === 'chart') document.getElementById('combined-chart').classList.add('active');\n");
    html.push_str("                else if (view === 'table') document.getElementById('normal-table').classList.add('active');\n");
    html.push_str("            });\n");
    html.push_str("        });\n");
    html.push_str("    </script>\n");
    html.push_str("</body>\n</html>");

    // Write HTML to Reports directory
    let reports_dir = std::path::Path::new(project_dir_str).join("Analysis").join("Reports");
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
pub async fn get_report(req: web::Json<ReportRequest>) -> impl Responder {
    // Validate required fields
    let file_name = match &req.file_name {
        Some(f) if !f.is_empty() => f,
        _ => return HttpResponse::BadRequest().body("Missing required field: fileName"),
    };

    // Validate path component for path traversal attacks
    if !security_validator::is_valid_path_component(Some(file_name)) {
        return HttpResponse::BadRequest().body("Invalid characters in fileName");
    }

    // Validate langPrefix for path traversal if provided
    if let Some(ref lang_prefix) = req.lang_prefix {
        if !security_validator::is_valid_path_component(Some(lang_prefix)) {
            return HttpResponse::BadRequest().body("Invalid characters in langPrefix");
        }
    }

    // Apply language prefix if requested
    let use_lang_prefix = req.use_lang_prefix.unwrap_or(false);
    let final_file_name = if use_lang_prefix {
        if let Some(ref lang_prefix) = req.lang_prefix {
            format!("{}_{}", lang_prefix, file_name)
        } else {
            file_name.clone()
        }
    } else {
        file_name.clone()
    };

    // Construct file path in Analysis/Reports
    let project_dir = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let reports_dir = project_dir.join("Analysis").join("Reports");
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

/// POST /api/save-log - Save log files from client
#[utoipa::path(
    post,
    path = "/api/save-log",
    responses(
        (status = 200, description = "Log saved successfully", body = TestResponse),
        (status = 400, description = "Bad request"),
        (status = 500, description = "Internal server error")
    )
)]
pub async fn save_log(
    body: web::Bytes
) -> impl Responder {
    let project_dir = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let project_dir_str = project_dir.to_str().unwrap_or("");

    // Parse JSON
    let (context_name, log_content) = match serde_json::from_slice::<serde_json::Value>(&body) {
        Ok(json) => {
            let context = json.get("context")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let content = json.get("content")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .trim()
                .to_string();
            (context, content)
        }
        Err(_) => return HttpResponse::BadRequest().body("Invalid JSON format"),
    };

    if context_name.is_empty() {
        return HttpResponse::BadRequest().body("Missing context parameter");
    }

    // If content is empty after trimming, return success without saving
    if log_content.is_empty() {
        let response = TestResponse {
            message: "No content to save".to_string(),
        };
        return HttpResponse::Ok().json(response);
    }

    // Validate context name parameter (256 char limit, no path traversal)
    if !security_validator::is_valid_path_component(Some(&context_name)) {
        return HttpResponse::BadRequest().body("Invalid context parameter");
    }

    // Validate log content (size and format)
    let (is_valid, error_message) = security_validator::is_valid_log_content(Some(&log_content));
    if !is_valid {
        return HttpResponse::BadRequest().body(error_message.unwrap_or_else(|| "Invalid log content".to_string()));
    }

    let logs_dir = std::path::Path::new(project_dir_str).join("Analysis").join("logs");
    let _ = std::fs::create_dir_all(&logs_dir);

    let log_file = logs_dir.join(format!("javascript_{}.log", context_name.to_lowercase()));
    let _ = std::fs::write(&log_file, log_content);

    let response = TestResponse {
        message: "Log saved successfully".to_string(),
    };
    HttpResponse::Ok().json(response)
}

/// POST /api/save-output - Save HTML output from client
#[utoipa::path(
    post,
    path = "/api/save-output",
    responses(
        (status = 200, description = "Output saved successfully", body = TestResponse),
        (status = 400, description = "Bad request"),
        (status = 500, description = "Internal server error")
    )
)]
pub async fn save_output(
    body: web::Bytes
) -> impl Responder {
    let project_dir = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let project_dir_str = project_dir.to_str().unwrap_or("");

    println!("[/api/save-output] Endpoint called");
    println!("[/api/save-output] Request body length: {}", body.len());

    // Parse JSON
    let (app_site, app_view, engine_type, html_content) = match serde_json::from_slice::<serde_json::Value>(&body) {
        Ok(json) => {
            let site = json.get("appSite")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let view = json.get("appView")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let engine = json.get("engineType")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let html = json.get("html")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            println!("[/api/save-output] Parsed: appSite={}, appView={}, engineType={}, htmlLength={}",
                site, view, engine, html.len());
            (site, view, engine, html)
        }
        Err(e) => {
            println!("[/api/save-output] JSON parse error: {}", e);
            return HttpResponse::BadRequest().body("Invalid JSON format");
        }
    };

    if app_site.is_empty() || engine_type.is_empty() || html_content.is_empty() {
        println!("[/api/save-output] Missing parameters: appSite={}, engineType={}, htmlLength={}",
            app_site, engine_type, html_content.len());
        return HttpResponse::BadRequest().body("Missing required parameters");
    }

    // Validate AppSite against allowlist
    let valid_app_sites = match security_validator::get_valid_app_sites() {
        Ok(sites) => sites,
        Err(e) => {
            println!("[/api/save-output] Failed to load AppSites: {}", e);
            return HttpResponse::InternalServerError().body(format!("Failed to load AppSites: {}", e));
        }
    };

    if !valid_app_sites.iter().any(|valid| valid.eq_ignore_ascii_case(&app_site)) {
        println!("[/api/save-output] Invalid AppSite: {}", app_site);
        return HttpResponse::BadRequest().body("Invalid AppSite value");
    }

    // Validate engine type against allowlist
    if !security_validator::get_valid_engine_types().iter().any(|valid| valid.eq_ignore_ascii_case(&engine_type)) {
        println!("[/api/save-output] Invalid engineType: {}", engine_type);
        return HttpResponse::BadRequest().body("Invalid engine type");
    }

    // Validate parameters (256 char limit, no path traversal)
    if !security_validator::is_valid_path_component(Some(&app_site)) {
        println!("[/api/save-output] Invalid AppSite path component: {}", app_site);
        return HttpResponse::BadRequest().body("Invalid AppSite parameter");
    }
    if !app_view.is_empty() && !security_validator::is_valid_path_component(Some(&app_view)) {
        println!("[/api/save-output] Invalid AppView path component: {}", app_view);
        return HttpResponse::BadRequest().body("Invalid AppView parameter");
    }
    if !security_validator::is_valid_path_component(Some(&engine_type)) {
        println!("[/api/save-output] Invalid engineType path component: {}", engine_type);
        return HttpResponse::BadRequest().body("Invalid engineType parameter");
    }

    // Validate output size against template size + buffer
    let template_total_size = security_validator::get_template_total_size(&app_site, &app_view);
    let output_size = html_content.len();
    let max_allowed_size = template_total_size + security_validator::OUTPUT_SIZE_BUFFER;
    println!("[/api/save-output] Size validation: output={}, template={}, buffer={}, max={}",
        output_size, template_total_size, security_validator::OUTPUT_SIZE_BUFFER, max_allowed_size);

    if !security_validator::is_valid_output_size_with_buffer(Some(&html_content), template_total_size) {
        let error_msg = format!(
            "Save output failed: output size ({} bytes) exceeds max size allowed ({} bytes = template {} + buffer {})",
            output_size, max_allowed_size, template_total_size, security_validator::OUTPUT_SIZE_BUFFER
        );
        println!("[/api/save-output] {}", error_msg);
        return HttpResponse::BadRequest().body(error_msg);
    }

    let output_dir = std::path::Path::new(project_dir_str).join("Analysis").join("output");
    let _ = std::fs::create_dir_all(&output_dir);

    let app_view_suffix = if !app_view.is_empty() {
        format!("_{}", app_view)
    } else {
        String::new()
    };
    let engine_suffix = engine_type.to_lowercase();
    let output_file = output_dir.join(format!("javascript_{}{}_{}.html", app_site, app_view_suffix, engine_suffix));
    let _ = std::fs::write(&output_file, html_content);
    println!("[/api/save-output] Success! Output saved to: {:?}", output_file);

    let response = TestResponse {
        message: "Output saved successfully".to_string(),
    };
    HttpResponse::Ok().json(response)
}

/// POST /api/test-results - Save test results from client
#[utoipa::path(
    post,
    path = "/api/test-results",
    request_body = Vec<TestSummaryRowDto>,
    responses(
        (status = 200, description = "Test results saved successfully", body = TestResponse),
        (status = 400, description = "Bad request"),
        (status = 500, description = "Internal server error")
    )
)]
pub async fn save_test_results(
    summary_rows: web::Json<Vec<TestSummaryRowDto>>,
    req: HttpRequest
) -> impl Responder {
    let project_dir = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let project_dir_str = project_dir.to_str().unwrap_or("");

    println!("POST /api/test-results called with {} rows", summary_rows.len());

    // Validate each row
    let valid_app_sites = match security_validator::get_valid_app_sites() {
        Ok(sites) => sites,
        Err(e) => return HttpResponse::InternalServerError().body(format!("Failed to load AppSites: {}", e)),
    };

    for row in summary_rows.iter() {
        // Validate AppSite is in allowlist
        if !row.app_site.is_empty() && !valid_app_sites.iter().any(|valid| valid.eq_ignore_ascii_case(&row.app_site)) {
            return HttpResponse::BadRequest().body(format!("Invalid AppSite: {}", row.app_site));
        }

        // Validate parameter lengths (256 char limit)
        if !security_validator::is_valid_path_component(Some(&row.app_site)) {
            return HttpResponse::BadRequest().body("Invalid AppSite parameter");
        }
        if !security_validator::is_valid_path_component(Some(&row.app_file)) {
            return HttpResponse::BadRequest().body("Invalid AppFile parameter");
        }
        if !row.app_view.is_empty() && !security_validator::is_valid_path_component(Some(&row.app_view)) {
            return HttpResponse::BadRequest().body("Invalid AppView parameter");
        }
    }

    let reports_path = std::path::Path::new(project_dir_str).join("Analysis").join("Reports");
    let _ = std::fs::create_dir_all(&reports_path);

    // Get test type from query parameter
    let test_type = req.query_string()
        .split('&')
        .find(|param| param.starts_with("testType="))
        .and_then(|param| param.split('=').nth(1))
        .unwrap_or("standardtest");

    let test_type_file = test_type.to_lowercase().replace(" ", "").replace("-", "");

    // Generate HTML table matching C# standard format
    let formatted_test_type = test_type.replace("test", " TEST").to_uppercase();
    let mut html = String::new();
    html.push_str("<!DOCTYPE html>\n<html>\n<head>\n");
    html.push_str("    <meta charset=\"UTF-8\">\n");
    html.push_str("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
    html.push_str(&format!("    <title>JavaScript {}</title>\n", formatted_test_type));
    html.push_str("    <style>\n");
    html.push_str("        body { font-family: Arial, sans-serif; margin: 20px; }\n");
    html.push_str("        h1 { color: #333; }\n");
    html.push_str("        .table-container { overflow-x: auto; }\n");
    html.push_str("        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }\n");
    html.push_str("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n");
    html.push_str("        th { background-color: #4CAF50; color: white; }\n");
    html.push_str("        tr:nth-child(even) { background-color: #f2f2f2; }\n");
    html.push_str("        .pass { color: green; font-weight: bold; }\n");
    html.push_str("        .fail { color: red; font-weight: bold; }\n");
    html.push_str("        @media (max-width: 768px) {\n");
    html.push_str("            body { margin: 10px; }\n");
    html.push_str("            th, td { padding: 8px; font-size: 14px; }\n");
    html.push_str("            h1 { font-size: 24px; }\n");
    html.push_str("        }\n");
    html.push_str("    </style>\n</head>\n<body>\n");
    html.push_str(&format!("    <h1>JavaScript {}</h1>\n", formatted_test_type));
    html.push_str(&format!("    <div class=\"meta\" style=\"color: #666; font-style: italic; margin-bottom: 10px;\">Generated: {} UTC</div>\n",
        chrono::Utc::now().format("%Y-%m-%d %H:%M:%S")));
    html.push_str("    <div class=\"table-container\">\n    <table>\n        <tr>\n");
    html.push_str("            <th>AppSite</th>\n            <th>AppFile</th>\n            <th>AppView</th>\n");
    html.push_str("            <th>OutputMatch</th>\n            <th>ViewUnMatch</th>\n            <th>Error</th>\n        </tr>\n");

    for row in summary_rows.iter() {
        let output_match_class = if row.normal_pre_process == "PASS" {
            "pass"
        } else if row.normal_pre_process == "FAIL" {
            "fail"
        } else {
            ""
        };
        let view_unmatch_class = if row.cross_view_un_match == "PASS" {
            "pass"
        } else if row.cross_view_un_match == "FAIL" {
            "fail"
        } else {
            ""
        };

        html.push_str("        <tr>\n");
        html.push_str(&format!("            <td>{}</td>\n", row.app_site));
        html.push_str(&format!("            <td>{}</td>\n", row.app_file));
        html.push_str(&format!("            <td>{}</td>\n", row.app_view));
        html.push_str(&format!("            <td class=\"{}\">{}</td>\n", output_match_class, row.normal_pre_process));
        html.push_str(&format!("            <td class=\"{}\">{}</td>\n", view_unmatch_class, row.cross_view_un_match));
        html.push_str(&format!("            <td>{}</td>\n", row.error));
        html.push_str("        </tr>\n");
    }

    html.push_str("    </table>\n    </div>\n</body>\n</html>");

    let html_file = reports_path.join(format!("javascript_{}_Summary.html", test_type_file));
    let _ = std::fs::write(&html_file, html);
    println!("Test summary HTML saved to: {:?}", html_file);

    // Save JSON summary file
    let json_file = reports_path.join(format!("javascript_{}_Summary.json", test_type_file));
    let json_data = match serde_json::to_string_pretty(&*summary_rows) {
        Ok(j) => j,
        Err(e) => return HttpResponse::InternalServerError().body(format!("JSON serialization failed: {}", e)),
    };
    let _ = std::fs::write(&json_file, json_data);
    println!("Test summary JSON saved to: {:?}", json_file);

    let response = TestResponse {
        message: "Test results saved successfully".to_string(),
    };
    HttpResponse::Ok().json(response)
}

/// POST /api/performance-results - Save performance results from client
#[utoipa::path(
    post,
    path = "/api/performance-results",
    request_body = Vec<PerfSummaryRowDto>,
    responses(
        (status = 200, description = "Performance results saved successfully", body = TestResponse),
        (status = 400, description = "Bad request"),
        (status = 500, description = "Internal server error")
    )
)]
pub async fn save_performance_results(
    summary_rows: web::Json<Vec<PerfSummaryRowDto>>
) -> impl Responder {
    let project_dir = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));
    let project_dir_str = project_dir.to_str().unwrap_or("");

    println!("POST /api/performance-results called with {} rows", summary_rows.len());

    // Validate each row
    let valid_app_sites = match security_validator::get_valid_app_sites() {
        Ok(sites) => sites,
        Err(e) => return HttpResponse::InternalServerError().body(format!("Failed to load AppSites: {}", e)),
    };

    for row in summary_rows.iter() {
        // Validate AppSite is in allowlist
        if !row.app_site.is_empty() && !valid_app_sites.iter().any(|valid| valid.eq_ignore_ascii_case(&row.app_site)) {
            return HttpResponse::BadRequest().body(format!("Invalid AppSite: {}", row.app_site));
        }

        // Validate parameter lengths (256 char limit)
        if !security_validator::is_valid_path_component(Some(&row.app_site)) {
            return HttpResponse::BadRequest().body("Invalid AppSite parameter");
        }
        if !security_validator::is_valid_path_component(Some(&row.app_file)) {
            return HttpResponse::BadRequest().body("Invalid AppFile parameter");
        }
        if !row.app_view.is_empty() && !security_validator::is_valid_path_component(Some(&row.app_view)) {
            return HttpResponse::BadRequest().body("Invalid AppView parameter");
        }
    }

    let reports_path = std::path::Path::new(project_dir_str).join("Analysis").join("Reports");
    let _ = std::fs::create_dir_all(&reports_path);

    // Generate HTML table
    let mut html = String::new();
    html.push_str("<html><head><title>Client-Side Performance Summary Table</title>\n");
    html.push_str("<style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}.meta{color:#666;font-style:italic;margin-bottom:10px;}</style></head><body>\n");
    html.push_str("<h2>Client-Side JavaScript PERFORMANCE SUMMARY TABLE</h2>\n");
    html.push_str(&format!("<div class=\"meta\">Generated: {} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>\n",
        chrono::Utc::now().format("%Y-%m-%d %H:%M:%S")));
    html.push_str("<table>\n<tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th><th>ScnTime(ms)</th><th>Elapsed(ms)</th></tr>\n");

    for row in summary_rows.iter() {
        html.push_str("<tr>\n");
        html.push_str(&format!("<td>{}</td>\n", row.app_site));
        html.push_str(&format!("<td>{}</td>\n", row.app_view));
        html.push_str(&format!("<td>{:.2}</td>\n", row.normal_time_ms));
        html.push_str(&format!("<td>{:.2}</td>\n", row.pre_process_time_ms));
        html.push_str(&format!("<td>{}</td>\n", row.results_match));
        html.push_str(&format!("<td>{}</td>\n", row.perf_difference));
        html.push_str(&format!("<td>{}</td>\n", row.scenario_total_time_ms));
        html.push_str(&format!("<td>{}</td>\n", row.elapsed_time_ms));
        html.push_str("</tr>\n");
    }

    html.push_str("</table></body></html>");

    let html_file = reports_path.join("javascript_perfsummary.html");
    let _ = std::fs::write(&html_file, html);
    println!("Performance summary HTML saved to: {:?}", html_file);

    // Save JSON summary file
    let json_file = reports_path.join("javascript_perfsummary.json");
    let json_data = match serde_json::to_string_pretty(&*summary_rows) {
        Ok(j) => j,
        Err(e) => return HttpResponse::InternalServerError().body(format!("JSON serialization failed: {}", e)),
    };
    let _ = std::fs::write(&json_file, json_data);
    println!("Performance summary JSON saved to: {:?}", json_file);

    let response = TestResponse {
        message: "Performance results saved successfully".to_string(),
    };
    HttpResponse::Ok().json(response)
}

/// Maps all assembler test endpoints to the service config
/// Usage: map_assembler_test_endpoints(&mut cfg)
pub fn map_assembler_test_endpoints(cfg: &mut web::ServiceConfig) {
    cfg.route("/api/test-results", web::post().to(save_test_results))
        .route("/api/performance-results", web::post().to(save_performance_results))
        .route("/api/save-log", web::post().to(save_log))
        .route("/api/save-output", web::post().to(save_output))
        .route("/test/standard", web::post().to(test_standard))
        .route("/test/advanced", web::post().to(test_advanced))
        .route("/test/performance", web::post().to(test_performance))
        .route("/test/consolidate-performance", web::post().to(test_consolidate_performance))
        .route("/api/report", web::post().to(get_report));
}

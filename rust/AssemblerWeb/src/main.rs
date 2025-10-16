#[cfg(debug_assertions)]
use std::thread;
use utoipa::OpenApi;
use std::time::Duration;
use actix_web::{web, App, HttpServer, Responder, HttpResponse};
use actix_files as fs;
use std::sync::{Arc, Mutex};
use std::time::Instant;
use actix_web::dev::{Service, ServiceRequest, ServiceResponse, Transform};
use futures_util::future::{self, LocalBoxFuture};
use actix_web::Error;

mod security_validator;
mod assembler_endpoint;

use assembler_endpoint::{MergeRequest, ScenarioDto, ReportRequest, TestResponse, TestSummaryRowDto, PerfSummaryRowDto, index, get_scenarios, get_templates, merge_templates, save_test_results, save_performance_results, save_log, save_output, test_standard, test_advanced, test_performance, test_consolidate_performance, get_report};


async fn openapi_handler() -> impl Responder {
    println!("[DEBUG] /openapi.json endpoint called");
    let openapi = ApiDoc::openapi();
    println!("[DEBUG] OpenAPI generated: {} bytes", serde_json::to_string(&openapi).map(|s| s.len()).unwrap_or(0));
    HttpResponse::Ok()
        .content_type("application/json")
        .json(openapi)
}

#[derive(OpenApi)]
#[openapi(
    paths(
        assembler_endpoint::index,
        assembler_endpoint::get_scenarios,
        assembler_endpoint::get_templates,
        assembler_endpoint::merge_templates,
        assembler_endpoint::save_test_results,
        assembler_endpoint::save_performance_results,
        assembler_endpoint::save_log,
        assembler_endpoint::save_output,
        assembler_endpoint::test_standard,
        assembler_endpoint::test_advanced,
        assembler_endpoint::test_performance,
        assembler_endpoint::test_consolidate_performance,
        assembler_endpoint::get_report
    ),
    components(schemas(MergeRequest, ScenarioDto, ReportRequest, TestResponse, TestSummaryRowDto, PerfSummaryRowDto)),
    tags((name = "Assembler", description = "Assembler API endpoints"))
)]
struct ApiDoc;

#[actix_web::main]
async fn main() -> std::io::Result<()> {
    // Parse command line args
    let args: Vec<String> = std::env::args().collect();
    let skip_idle_tracking = args.iter().any(|arg| arg == "--skipIdleTracking");

    // Parse port from --port argument or PORT environment variable, default to 8090
    let port: u16 = args.iter()
        .position(|arg| arg == "--port")
        .and_then(|i| args.get(i + 1))
        .and_then(|p| p.parse().ok())
        .or_else(|| std::env::var("PORT").ok().and_then(|p| p.parse().ok()))
        .unwrap_or(8090);

    // Get project directory (current working directory for web projects)
    let project_directory = std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from("."));

    let template_analysis_dir = project_directory.join("template_analysis");
    let logs_dir = template_analysis_dir.join("logs");
    if let Err(e) = std::fs::create_dir_all(&logs_dir) {
        eprintln!("Failed to create logs directory: {}", e);
    }

    // Configure separate log files for global contexts only
    // Note: Endpoint-specific contexts and engine-specific logs are configured per-endpoint
    let mut context_log_files = std::collections::HashMap::new();
    context_log_files.insert("Main".to_string(), logs_dir.join("rust_main.log").to_string_lossy().to_string());
    context_log_files.insert("IdleTracking".to_string(), logs_dir.join("rust_idletracking.log").to_string_lossy().to_string());

    assembler::common::logger::Logger::configure(
        assembler::common::logger::LogLevel::DEBUG,
        None,
        false,
        assembler::common::logger::LogRotation::NONE
    );
    assembler::common::logger::Logger::configure_context_log_files(context_log_files);
    assembler::common::logger::Logger::info("AssemblerWeb starting up", Some("Main"));

    // Load ConfigUtil (AppSites and Scenarios) at startup
    let wwwroot_path = project_directory.join("wwwroot");
    let wwwroot_path_str = wwwroot_path.to_str().unwrap_or("");
    if let Err(e) = assembler::config::config_util::ConfigUtil::load(wwwroot_path_str) {
        eprintln!("[WARNING] Failed to load ConfigUtil: {}", e);
        assembler::common::logger::Logger::warn(&format!("Failed to load ConfigUtil: {}", e), Some("Main"));
    }

    println!("Starting server on http://localhost:{}", port);
    println!("Scalar UI will be available at http://localhost:{}/scalar", port);

    // Launch browser after a short delay (only in debug mode)
    #[cfg(debug_assertions)]
    {
        let browser_port = port;
        thread::spawn(move || {
            thread::sleep(Duration::from_millis(500));
            if let Err(e) = webbrowser::open(&format!("http://localhost:{}/", browser_port)) {
                println!("Failed to open browser: {}", e);
            }
        });
    }
    
    let is_debug = std::env::var("DEBUG").as_deref() == Ok("true")
    || std::env::var("VSCODE_DEBUG").unwrap_or_default() == "true"
    || std::env::var("IDLE_TRACKER_DISABLED").unwrap_or_default() == "true"
    || std::env::var("APP_ENV").unwrap_or_default() == "development"
    || skip_idle_tracking;

    if !is_debug {
        println!("[IdleTracking] Idle tracking ENABLED");
    } else {
        println!("[IdleTracking] Idle tracking DISABLED");
    }
    let idle_seconds = std::env::var("IDLE_SECONDS")
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .unwrap_or(10);
    let idle_tracking = IdleTracking::new(idle_seconds, !is_debug);

    let project_dir_data = web::Data::new(project_directory.clone());
    let wwwroot_path = project_directory.join("wwwroot");
    let server = HttpServer::new(move || {
        let scalar_path = wwwroot_path.join("scalar");
        let wwwroot_serve_path = wwwroot_path.clone();

        App::new()
            .app_data(project_dir_data.clone())
            .wrap(idle_tracking.clone())
            .service(web::resource("/").route(web::get().to(index)))
            .route("/api/scenarios", web::get().to(get_scenarios))
            .route("/api/templates", web::post().to(get_templates))
            .service(merge_templates)
            .route("/api/test-results", web::post().to(save_test_results))
            .route("/api/performance-results", web::post().to(save_performance_results))
            .route("/api/save-log", web::post().to(save_log))
            .route("/api/save-output", web::post().to(save_output))
            .route("/test/standard", web::post().to(test_standard))
            .route("/test/advanced", web::post().to(test_advanced))
            .route("/test/performance", web::post().to(test_performance))
            .route("/test/consolidate-performance", web::post().to(test_consolidate_performance))
            .route("/api/report", web::post().to(get_report))
            .route("/openapi.json", web::get().to(openapi_handler))
            .service(fs::Files::new("/scalar", scalar_path).index_file("index.html"))
            .service(fs::Files::new("/", wwwroot_serve_path).index_file("index.html"))
    })
    .bind(("0.0.0.0", port))?;
    println!("Server listening on http://localhost:{}", port);
    server.run().await
}

// Idle Tracking Middleware
#[derive(Clone)]
pub struct IdleTracking {
    last_request: Arc<Mutex<Instant>>,
    shutdown_initiated: Arc<Mutex<bool>>,
    active_holds: Arc<Mutex<std::collections::HashMap<String, Instant>>>,
    idle_seconds: u64,
    hold_timeout_seconds: u64,
    enabled: bool,
}

impl IdleTracking {
    pub fn new(idle_seconds: u64, enabled: bool) -> Self {
        IdleTracking {
            last_request: Arc::new(Mutex::new(Instant::now())),
            shutdown_initiated: Arc::new(Mutex::new(false)),
            active_holds: Arc::new(Mutex::new(std::collections::HashMap::new())),
            idle_seconds,
            hold_timeout_seconds: 300, // Safety timeout for stuck holds
            enabled,
        }
    }
}

impl<S, B> Transform<S, ServiceRequest> for IdleTracking
where
    S: Service<ServiceRequest, Response = ServiceResponse<B>, Error = Error> + 'static,
    B: 'static,
{
    type Response = ServiceResponse<B>;
    type Error = Error;
    type Transform = IdleTrackingMiddleware<S>;
    type InitError = (); 
    type Future = future::Ready<Result<Self::Transform, Self::InitError>>;

    fn new_transform(&self, service: S) -> Self::Future {
        let last_request = self.last_request.clone();
        let shutdown_initiated = self.shutdown_initiated.clone();
        let active_holds = self.active_holds.clone();
        let idle_seconds = self.idle_seconds;
        let hold_timeout_seconds = self.hold_timeout_seconds;
        let enabled = self.enabled;

        // Start idle checker thread only if enabled
        if enabled {
            println!("[STARTUP] Configured idleSeconds = {}", idle_seconds);
            println!("[STARTUP] Starting idle monitor with 10-second check interval");

            std::thread::spawn({
                let last_request = last_request.clone();
                let shutdown_initiated = shutdown_initiated.clone();
                let active_holds = active_holds.clone();
                move || {
                    loop {
                        std::thread::sleep(Duration::from_secs(10));
                        let last = *last_request.lock().unwrap();
                        let idle = last.elapsed().as_secs();

                        // Clean up expired holds and count active holds
                        let mut holds = active_holds.lock().unwrap();
                        let now = Instant::now();
                        let mut expired_holds = Vec::new();

                        for (hold_id, hold_time) in holds.iter() {
                            let hold_age = now.duration_since(*hold_time).as_secs();
                            if hold_age >= hold_timeout_seconds {
                                expired_holds.push(hold_id.clone());
                            }
                        }

                        for hold_id in &expired_holds {
                            holds.remove(hold_id);
                            println!("[MONITOR] Removed expired hold: {} (age: {}s)", hold_id, hold_timeout_seconds);
                        }

                        let active_holds_count = holds.len();
                        drop(holds); // Release lock before potentially exiting

                        println!("[MONITOR] IdleTime: {}s, Threshold: {}s, ActiveHolds: {}", idle, idle_seconds, active_holds_count);

                        let mut shutdown = shutdown_initiated.lock().unwrap();
                        // Only trigger shutdown if idle time exceeded AND no active holds
                        if !*shutdown && idle > idle_seconds && active_holds_count == 0 {
                            *shutdown = true;
                            println!("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown");
                            assembler::common::logger::Logger::info(&format!("Idle timeout reached ({}s) with no active requests, shutting down server...", idle_seconds), Some("IdleTracking"));
                            std::process::exit(0);
                        }
                    }
                }
            });
        }
        future::ok(IdleTrackingMiddleware {
            service,
            last_request,
            active_holds,
        })
    }
}

pub struct IdleTrackingMiddleware<S> {
    service: S,
    last_request: Arc<Mutex<Instant>>,
    active_holds: Arc<Mutex<std::collections::HashMap<String, Instant>>>,
}

impl<S, B> Service<ServiceRequest> for IdleTrackingMiddleware<S>
where
    S: Service<ServiceRequest, Response = ServiceResponse<B>, Error = Error> + 'static,
    B: 'static,
{
    type Response = ServiceResponse<B>;
    type Error = Error;
    type Future = LocalBoxFuture<'static, Result<Self::Response, Self::Error>>;

    fn poll_ready(
        &self,
        ctx: &mut std::task::Context<'_>,
    ) -> std::task::Poll<Result<(), Self::Error>> {
        self.service.poll_ready(ctx)
    }

    fn call(&self, req: ServiceRequest) -> Self::Future {
        let last_request = self.last_request.clone();
        let active_holds = self.active_holds.clone();

        // Generate unique hold ID for this request
        let hold_id = format!("hold_{}", uuid::Uuid::new_v4().to_string().replace("-", ""));

        // Set hold before processing to prevent shutdown during long-running requests
        {
            let mut holds = active_holds.lock().unwrap();
            holds.insert(hold_id.clone(), Instant::now());
            println!("[REQUEST] Request started, hold set: {}", hold_id);
        }

        // Update last request time
        {
            let mut last = last_request.lock().unwrap();
            *last = Instant::now();
        }

        let fut = self.service.call(req);
        let hold_id_for_cleanup = hold_id.clone();

        Box::pin(async move {
            let res = fut.await;

            // Update timestamp after processing
            {
                let mut last = last_request.lock().unwrap();
                *last = Instant::now();
            }

            // Always remove hold after processing (even if error occurs)
            {
                let mut holds = active_holds.lock().unwrap();
                holds.remove(&hold_id_for_cleanup);
                println!("[REQUEST] Request completed, hold removed: {}", hold_id_for_cleanup);
            }

            res
        })
    }
}

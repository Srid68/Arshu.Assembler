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

use assembler_endpoint::{MergeRequest, ScenarioDto, ReportRequest, index, get_scenarios, merge_templates, test_standard, test_advanced, test_performance, test_consolidate_performance, get_report};


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
        assembler_endpoint::merge_templates,
        assembler_endpoint::test_standard,
        assembler_endpoint::test_advanced,
        assembler_endpoint::test_performance,
        assembler_endpoint::test_consolidate_performance,
        assembler_endpoint::get_report
    ),
    components(schemas(MergeRequest, ScenarioDto, ReportRequest)),
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
            .service(merge_templates)
            .route("/test/standard", web::post().to(test_standard))
            .route("/test/advanced", web::post().to(test_advanced))
            .route("/test/performance", web::post().to(test_performance))
            .route("/test/consolidate-performance", web::post().to(test_consolidate_performance))
            .route("/api/scenarios", web::get().to(get_scenarios))
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
    idle_seconds: u64,
    enabled: bool,
}

impl IdleTracking {
    pub fn new(idle_seconds: u64, enabled: bool) -> Self {
        IdleTracking {
            last_request: Arc::new(Mutex::new(Instant::now())),
            shutdown_initiated: Arc::new(Mutex::new(false)),
            idle_seconds,
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
        let idle_seconds = self.idle_seconds;
        let enabled = self.enabled;
        // Start idle checker thread only if enabled
        if enabled {
            std::thread::spawn({
                let last_request = last_request.clone();
                let shutdown_initiated = shutdown_initiated.clone();
                move || {
                    loop {
                        std::thread::sleep(Duration::from_secs(10));
                        let last = *last_request.lock().unwrap();
                        let idle = last.elapsed().as_secs();
                        let mut shutdown = shutdown_initiated.lock().unwrap();
                        if !*shutdown && idle > idle_seconds {
                            *shutdown = true;
                            println!("Idle timeout reached ({}s), shutting down server...", idle_seconds);
                            std::process::exit(0);
                        }
                    }
                }
            });
        }
        future::ok(IdleTrackingMiddleware {
            service,
            last_request,
        })
    }
}

pub struct IdleTrackingMiddleware<S> {
    service: S,
    last_request: Arc<Mutex<Instant>>,
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
        // Update last request time
        {
            let mut last = last_request.lock().unwrap();
            *last = Instant::now();
        }
        let fut = self.service.call(req);
        Box::pin(async move {
            let res = fut.await;
            res
        })
    }
}

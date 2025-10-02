#[cfg(debug_assertions)]
use std::thread;
use serde::{Deserialize, Serialize};
use utoipa::OpenApi;
use std::time::Duration;
use assembler::template_common::template_utils::TemplateUtils;
use actix_web::{web, App, HttpServer, Responder, HttpResponse};
use actix_files as fs;
use std::sync::{Arc, Mutex};
use std::time::Instant;
use actix_web::dev::{Service, ServiceRequest, ServiceResponse, Transform};
use futures_util::future::{self, LocalBoxFuture};
use actix_web::Error;

#[derive(Debug, Deserialize, Serialize, utoipa::ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct MergeRequest {
    pub app_site: Option<String>,
    pub app_view: Option<String>,
    pub app_view_prefix: Option<String>,
    pub app_file: Option<String>,
    pub engine_type: Option<String>,
}

#[utoipa::path(
    post,
    path = "/merge",
    request_body = MergeRequest,
    responses(
        (status = 200, description = "Merged template output", body = String)
    )
)]

#[actix_web::post("/merge")]
async fn merge_templates(req: web::Json<MergeRequest>) -> impl Responder {
    println!(
        "/merge endpoint called with: app_site={:?}, app_file={:?}, engine_type={:?}, app_view={:?}, app_view_prefix={:?}",
        req.app_site, req.app_file, req.engine_type, req.app_view, req.app_view_prefix
    );
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
    let root_dir_path = assembler_web_dir_path; // rust/AssemblerWeb/wwwroot

        use std::time::Instant;
        let server_start = Instant::now();
        let engine_start = Instant::now();
        let merged_html = if engine_type.eq_ignore_ascii_case("PreProcess") {
            let templates = assembler::template_loader::loader_preprocess::LoaderPreProcess::load_process_get_template_files(root_dir_path.to_str().unwrap_or(""), app_site);
            let engine = assembler::template_engine::engine_preprocess::EnginePreProcess::new(req.app_view_prefix.clone().unwrap_or_default());
            engine.merge_templates(
                app_site,
                app_file,
                req.app_view.as_deref(),
                &templates.templates,
                true
            )
        } else {
            let mut templates = assembler::template_loader::loader_normal::LoaderNormal::load_get_template_files(root_dir_path.to_str().unwrap_or(""), app_site);
            let engine = assembler::template_engine::engine_normal::EngineNormal::new(req.app_view_prefix.clone().unwrap_or_default());
            engine.merge_templates(
                app_site,
                app_file,
                req.app_view.as_deref(),
                &mut templates,
                true
            )
        };
        let engine_time_ms = engine_start.elapsed().as_secs_f64() * 1000.0;
        let server_time_ms = server_start.elapsed().as_secs_f64() * 1000.0;
        let response_obj = serde_json::json!({
            "html": merged_html,
            "timing": {
                "serverTimeMs": server_time_ms,
                "engineTimeMs": engine_time_ms
            }
        });
        HttpResponse::Ok().json(response_obj)
}


#[utoipa::path(
    get,
    path = "/",
    responses(
        (status = 200, description = "Root template HTML", body = String)
    )
)]

async fn index() -> impl Responder {
    use std::fs;
    use actix_web::http::header::ContentType;

    // Use TemplateUtils to get the correct wwwroot/AppSites path
    let (assembler_web_dir_path, _project_directory) = TemplateUtils::get_assembler_web_dir_path();
    let appsites_path = assembler_web_dir_path.join("AppSites");

    let mut options_list = Vec::new();

    if appsites_path.exists() {
        if let Ok(test_dirs_iter) = fs::read_dir(&appsites_path) {
            let mut test_dirs: Vec<String> = test_dirs_iter
                .filter_map(|entry| entry.ok().and_then(|e| e.file_name().into_string().ok()))
                .filter(|name| !name.eq_ignore_ascii_case("roottemplate.html"))
                .collect();
            test_dirs.sort_by(|a, b| a.to_lowercase().cmp(&b.to_lowercase()));

            for test_dir in test_dirs {
                let test_dir_path = appsites_path.join(&test_dir);
                let mut html_files: Vec<String> = fs::read_dir(&test_dir_path)
                    .map(|files| files.filter_map(|f| f.ok())
                        .filter(|f| f.path().extension().map_or(false, |ext| ext == "html"))
                        .map(|f| f.path().file_stem().unwrap().to_string_lossy().to_string())
                        .collect::<Vec<_>>())
                    .unwrap_or_default();
                html_files.sort_by(|a, b| a.to_lowercase().cmp(&b.to_lowercase()));

                let views_path = test_dir_path.join("Views");
                let has_views = views_path.exists();

                for html_file in &html_files {
                    let app_view_prefix = html_file.chars().take(6).collect::<String>();
                    let option_value = format!("{},{},,{}", test_dir, html_file, app_view_prefix);
                    let option_text = format!("{} - {}", test_dir, html_file);
                    options_list.push(format!("<option value=\"{}\">{}</option>", option_value, option_text));
                }

                if has_views {
                    let mut view_files: Vec<String> = fs::read_dir(&views_path)
                        .map(|files| files.filter_map(|f| f.ok())
                            .filter(|f| f.path().extension().map_or(false, |ext| ext == "html"))
                            .map(|f| f.path().file_stem().unwrap().to_string_lossy().to_string())
                            .collect::<Vec<_>>())
                        .unwrap_or_default();
                    view_files.sort_by(|a, b| a.to_lowercase().cmp(&b.to_lowercase()));

                    let app_view_values: Vec<String> = view_files.iter()
                        .filter_map(|vf| {
                            let idx = vf.to_lowercase().find("content");
                            if let Some(idx) = idx {
                                let view_part = &vf[..idx];
                                if !view_part.is_empty() {
                                    let mut chars = view_part.chars();
                                    return Some(chars.next().unwrap().to_uppercase().collect::<String>() + chars.as_str());
                                }
                            }
                            None
                        })
                        .collect();

                    for root_file in &html_files {
                        let root_app_view_prefix = root_file.chars().take(6).collect::<String>();
                        let matching_view_prefix = view_files.iter()
                            .filter_map(|vf| {
                                let idx = vf.to_lowercase().find("content");
                                if let Some(idx) = idx {
                                    let prefix = &vf[..idx];
                                    if !prefix.is_empty() && root_file.to_lowercase().starts_with(&prefix.to_lowercase()) {
                                        return Some(prefix.to_string());
                                    }
                                }
                                None
                            })
                            .next();

                        if matching_view_prefix.is_some() {
                            for app_view in &app_view_values {
                                let option_value_app_view = format!("{},{},{},{}", test_dir, root_file, app_view, root_app_view_prefix);
                                let option_text_app_view = format!("{} - {} (AppView: {})", test_dir, root_file, app_view);
                                options_list.push(format!("<option value=\"{}\">{}</option>", option_value_app_view, option_text_app_view));
                            }
                        }
                    }
                }
            }
        }
    }

    let options = options_list.join("\n        ");
    let template_path = appsites_path.join("roottemplate.html");
    let html = fs::read_to_string(&template_path).unwrap_or_else(|_| format!("<html><body>Template not found at {:?}</body></html>", template_path));
    let html = html.replace("<!--OPTIONS-->", &options);
    HttpResponse::Ok().content_type(ContentType::html()).body(html)
}

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
    paths(index, merge_templates),
    components(schemas(MergeRequest)),
    tags((name = "Assembler", description = "Assembler API endpoints"))
)]
struct ApiDoc;

#[actix_web::main]
async fn main() -> std::io::Result<()> {
    // OS environment detection
    if std::path::Path::new("/proc/sys/kernel/osrelease").exists() {
        let os_release = std::fs::read_to_string("/proc/sys/kernel/osrelease").unwrap_or_default();
        if os_release.contains("microsoft") {
            println!("[WSL] Running in WSL environment");
        } else if std::path::Path::new("/etc/os-release").exists() {
            let os_info = std::fs::read_to_string("/etc/os-release").unwrap_or_default();
            let distro = os_info.lines().find(|l| l.starts_with("ID=")).map(|l| l[3..].trim_matches('"')).unwrap_or("Unknown Linux");
            println!("[Linux] Running in {} environment", distro);
        } else {
            println!("[Linux] Running in Linux environment");
        }
    } else {
        println!("[Windows] Running in Windows environment");
    }
    println!("Starting Rust AssemblerWeb server...");
    println!("Starting server on http://localhost:8080");
    println!("Scalar UI will be available at http://localhost:8080/scalar");
    
    // Launch browser after a short delay (only in debug mode)
    #[cfg(debug_assertions)]
    thread::spawn(|| {
        thread::sleep(Duration::from_millis(500));
        if let Err(e) = webbrowser::open("http:/localhost:8080/scalar") {
            println!("Failed to open browser: {}", e);
        }
    });
    
    let idle_seconds = std::env::var("IDLE_SECONDS")
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .unwrap_or(10);
    let idle_tracking = IdleTracking::new(idle_seconds);

    let server = HttpServer::new(move || {
        let (assembler_web_dir_path, _) = TemplateUtils::get_assembler_web_dir_path();
        let scalar_path = assembler_web_dir_path.join("scalar");

        #[cfg(not(debug_assertions))]
        {
            App::new()
                .service(web::resource("/").route(web::get().to(index)))
                .service(merge_templates)
                .route("/openapi.json", web::get().to(openapi_handler))
                .service(fs::Files::new("/scalar", scalar_path).index_file("index.html"))
                .wrap(idle_tracking.clone())
        }
        #[cfg(debug_assertions)]
        {
            App::new()
                .service(web::resource("/").route(web::get().to(index)))
                .service(merge_templates)
                .route("/openapi.json", web::get().to(openapi_handler))
                .service(fs::Files::new("/scalar", scalar_path).index_file("index.html"))
        }
    })
    .bind(("0.0.0.0", 8080))?;
    println!("Server listening on http://localhost:8080");

    server.run().await
}

// Idle Tracking Middleware
#[derive(Clone)]
pub struct IdleTracking {
    last_request: Arc<Mutex<Instant>>,
    shutdown_initiated: Arc<Mutex<bool>>,
    idle_seconds: u64,
}

impl IdleTracking {
    pub fn new(idle_seconds: u64) -> Self {
        IdleTracking {
            last_request: Arc::new(Mutex::new(Instant::now())),
            shutdown_initiated: Arc::new(Mutex::new(false)),
            idle_seconds,
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
        // Start idle checker thread
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

use std::thread;
use std::time::Duration;
use actix_web::{web, App, HttpServer};
use actix_files as fs;

mod endpoint;
mod services;

use endpoint::{map_assembler_endpoints, map_assembler_test_endpoints, openapi_handler};
use services::IdleTracking;

#[actix_web::main]
async fn main() -> std::io::Result<()> {
    // Parse command line args
    let args: Vec<String> = std::env::args().collect();
    let skip_idle_tracking = args.iter().any(|arg| arg == "--skipIdleTracking");

    // Parse port from --port argument or PORT environment variable, default to 8030
    let port: u16 = args.iter()
        .position(|arg| arg == "--port")
        .and_then(|i| args.get(i + 1))
        .and_then(|p| p.parse().ok())
        .or_else(|| std::env::var("PORT").ok().and_then(|p| p.parse().ok()))
        .unwrap_or(8030);

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

    // Configure logger (no main log file - only context files)
    assembler::common::logger::Logger::configure(
        assembler::common::logger::LogLevel::INFO,
        false,
        assembler::common::logger::LogRotation::HOURLY
    );
    
    // Set logs directory for clearing
    assembler::common::logger::Logger::set_logs_directory(logs_dir.to_string_lossy().to_string());

    // Clear logs based on build mode
    #[cfg(debug_assertions)]
    assembler::common::logger::Logger::clear_logs();
    
    #[cfg(not(debug_assertions))]
    assembler::common::logger::Logger::clear_old_logs(7);

    // Configure context log files AFTER clearing (which would delete them)
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

    // Determine if in debug mode first (needed for browser launch decision)
    let mut is_debug = std::env::var("DEBUG").as_deref() == Ok("true")
    || std::env::var("VSCODE_DEBUG").unwrap_or_default() == "true"
    || std::env::var("APP_ENV").unwrap_or_default() == "development";
    
    // Also check if compiled in debug mode
    if cfg!(debug_assertions) {
        is_debug = true;
    }

    // Separate check for idle tracking
    // Command line args and explicit env vars take precedence
    let idle_tracking_enabled = if skip_idle_tracking {
        false  // --skipIdleTracking flag explicitly disables
    } else {
        match std::env::var("IDLE_TRACKER_DISABLED").as_deref() {
            Ok("false") => true,  // Explicitly enable idle tracking
            Ok("true") => false,  // Explicitly disable idle tracking
            _ => !is_debug  // Default: disable in debug mode
        }
    };

    // Launch browser after a short delay (only in debug mode)
    if is_debug {
        let browser_port = port;
        thread::spawn(move || {
            thread::sleep(Duration::from_millis(500));
            if let Err(e) = webbrowser::open(&format!("http://localhost:{}/", browser_port)) {
                println!("Failed to open browser: {}", e);
            }
        });
    }

    if idle_tracking_enabled {
        println!("[IdleTracking] Idle tracking ENABLED");
    } else {
        println!("[IdleTracking] Idle tracking DISABLED");
    }
    let idle_seconds = std::env::var("IDLE_SECONDS")
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .unwrap_or(10);
    let idle_tracking = IdleTracking::new(idle_seconds, idle_tracking_enabled);
    let idle_tracking_for_shutdown = idle_tracking.clone();

    let wwwroot_path = project_directory.join("wwwroot");
    let server = HttpServer::new(move || {
        let scalar_path = wwwroot_path.join("scalar");
        let wwwroot_serve_path = wwwroot_path.clone();

        App::new()
            .wrap(idle_tracking.clone())
            .configure(map_assembler_endpoints)
            .configure(map_assembler_test_endpoints)
            .route("/openapi.json", web::get().to(openapi_handler))
            .service(fs::Files::new("/scalar", scalar_path).index_file("index.html"))
            .service(fs::Files::new("/", wwwroot_serve_path).index_file("index.html"))
    })
    .bind(("0.0.0.0", port))?;
    println!("Server listening on http://localhost:{}", port);
    
    let server = server.run();
    let _server_handle = server.handle();

    // Demonstrate hold functionality for external processes
    // Example: Acquire a hold to prevent idle shutdown during long-running operations
    let example_hold_id = "example_operation";
    idle_tracking_for_shutdown.acquire_hold(example_hold_id);
    assembler::common::logger::Logger::info(&format!("Example hold acquired: {}", example_hold_id), Some("Main"));
    // Release the hold when the operation completes
    idle_tracking_for_shutdown.release_hold(example_hold_id);
    assembler::common::logger::Logger::info(&format!("Example hold released: {}", example_hold_id), Some("Main"));

    // Note: Shutdown handler removed - IdleTracking middleware handles shutdown via process::exit
    // Manual Ctrl+C will be caught by the OS and terminate the process

    server.await
}

use std::sync::{Arc, Mutex, Once};
use std::time::{Duration, Instant};
use actix_web::dev::{Service, ServiceRequest, ServiceResponse, Transform};
use actix_web::Error;
use actix_web::rt::System;
use futures_util::future::{self, LocalBoxFuture};

static INIT: Once = Once::new();

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

    pub fn shutdown(&self) {
        let holds = self.active_holds.lock().unwrap();
        println!("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: {}", holds.len());
        assembler::common::logger::Logger::info(&format!("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: {}", holds.len()), Some("IdleTracking"));
        
        // Log any remaining holds
        for (hold_id, _) in holds.iter() {
            println!("[SHUTDOWN] Unreleased hold: {}", hold_id);
            assembler::common::logger::Logger::info(&format!("[SHUTDOWN] Unreleased hold: {}", hold_id), Some("IdleTracking"));
        }
        drop(holds);
        
        println!("[SHUTDOWN] IdleTrackingMiddleware stopped");
        assembler::common::logger::Logger::info("[SHUTDOWN] IdleTrackingMiddleware stopped", Some("IdleTracking"));
    }

    pub fn start_monitor(&self) {
        let last_request = self.last_request.clone();
        let shutdown_initiated = self.shutdown_initiated.clone();
        let active_holds = self.active_holds.clone();
        let idle_seconds = self.idle_seconds;
        let hold_timeout_seconds = self.hold_timeout_seconds;
        let self_clone = self.clone();

        println!("[STARTUP] Configured idleSeconds = {}", idle_seconds);
        assembler::common::logger::Logger::info(&format!("[STARTUP] Configured idleSeconds = {}", idle_seconds), Some("IdleTracking"));
        println!("[STARTUP] Starting idle monitor with 10-second check interval");
        assembler::common::logger::Logger::info("[STARTUP] Starting idle monitor with 10-second check interval", Some("IdleTracking"));

        std::thread::spawn(move || {
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
                    let msg = format!("[MONITOR] Removed expired hold: {} (age: {}s)", hold_id, hold_timeout_seconds);
                    println!("{}", msg);
                    assembler::common::logger::Logger::info(&msg, Some("IdleTracking"));
                }

                let active_holds_count = holds.len();
                drop(holds); // Release lock before potentially exiting

                let monitor_msg = format!("[MONITOR] IdleTime: {}s, Threshold: {}s, ActiveHolds: {}", idle, idle_seconds, active_holds_count);
                println!("{}", monitor_msg);
                assembler::common::logger::Logger::info(&monitor_msg, Some("IdleTracking"));

                let mut shutdown = shutdown_initiated.lock().unwrap();
                // Only trigger shutdown if idle time exceeded AND no active holds
                if !*shutdown && idle > idle_seconds && active_holds_count == 0 {
                    *shutdown = true;
                    drop(shutdown);
                    
                    println!("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown");
                    assembler::common::logger::Logger::info("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown", Some("IdleTracking"));
                    
                    // Call shutdown method to log everything
                    self_clone.shutdown();
                    
                    assembler::common::logger::Logger::info("AssemblerWeb shutting down due to idle timeout...", Some("Main"));
                    println!("AssemblerWeb shutting down due to idle timeout...");
                    
                    // Call shutdown to log and cleanup
                    self_clone.shutdown();
                    
                    // Give time for logs to flush
                    std::thread::sleep(Duration::from_millis(200));
                    
                    // Stop the actix system gracefully
                    System::current().stop();
                    
                    // Give time for graceful shutdown
                    std::thread::sleep(Duration::from_millis(300));
                    
                    // Exit the process - this is necessary for the process to actually terminate
                    // VS Code debugger should handle this gracefully since we've stopped the system first
                    std::process::exit(0);
                }
            }
        });
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
        let active_holds = self.active_holds.clone();
        let enabled = self.enabled;

        // Start idle checker thread only once (not per worker thread)
        if enabled {
            let self_for_init = self.clone();
            INIT.call_once(move || {
                self_for_init.start_monitor();
            });
        }
        future::ok(IdleTrackingMiddleware {
            service,
            last_request,
            active_holds,
            enabled,
        })
    }
}

pub struct IdleTrackingMiddleware<S> {
    service: S,
    last_request: Arc<Mutex<Instant>>,
    active_holds: Arc<Mutex<std::collections::HashMap<String, Instant>>>,
    enabled: bool,
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
        let enabled = self.enabled;

        // Only track and log if enabled
        if !enabled {
            let fut = self.service.call(req);
            return Box::pin(async move {
                fut.await
            });
        }

        // Generate unique hold ID for this request
        let hold_id = format!("hold_{}", uuid::Uuid::new_v4().to_string().replace("-", ""));

        // Set hold before processing to prevent shutdown during long-running requests
        {
            let mut holds = active_holds.lock().unwrap();
            holds.insert(hold_id.clone(), Instant::now());
        }
        
        // Log after releasing the lock
        println!("[REQUEST] Request started, hold set: {}", hold_id);
        assembler::common::logger::Logger::info(&format!("[REQUEST] Request started, hold set: {}", hold_id), Some("IdleTracking"));

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
            }

            // Log output after releasing locks
            println!("[REQUEST] Request completed");
            assembler::common::logger::Logger::info("[REQUEST] Request completed", Some("IdleTracking"));
            println!("[REQUEST] Hold removed: {}", hold_id_for_cleanup);
            assembler::common::logger::Logger::info(&format!("[REQUEST] Hold removed: {}", hold_id_for_cleanup), Some("IdleTracking"));

            res
        })
    }
}

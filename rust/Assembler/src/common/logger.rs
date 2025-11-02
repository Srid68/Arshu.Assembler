use std::fs::{File, OpenOptions};
use std::io::Write;
use std::sync::Mutex;
use std::collections::HashMap;
use chrono::Local;
use std::path::Path;

/// Simple custom logger for consistent logging across all language implementations.
/// Provides file-based logging with different log levels.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub enum LogLevel {
    DEBUG = 0,
    INFO = 1,
    WARN = 2,
    ERROR = 3,
    NONE = 4,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LogRotation {
    NONE = 0,
    HOURLY = 1,
    DAILY = 2,
}

struct LoggerConfig {
    current_log_level: LogLevel,
    log_file_path: Option<String>,
    console_output: bool,
    log_rotation: LogRotation,
    logs_directory: Option<String>, // Directory for scanning/clearing logs
    context_log_files: HashMap<String, String>,
}

lazy_static::lazy_static! {
    static ref LOGGER_CONFIG: Mutex<LoggerConfig> = Mutex::new(LoggerConfig {
        current_log_level: LogLevel::NONE,
        log_file_path: None,
        console_output: true,
        log_rotation: LogRotation::HOURLY,
        logs_directory: None,
        context_log_files: HashMap::new(),
    });
}

pub struct Logger;

impl Logger {
    /// Configure the logger (no log file path - use set_logs_directory instead)
    pub fn configure(level: LogLevel, console_output: bool, rotation: LogRotation) {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.current_log_level = level;
        config.console_output = console_output;
        config.log_rotation = rotation;
    }

    /// Set the logs directory - the ONLY way to specify where logs are stored
    pub fn set_logs_directory(logs_directory: String) {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.logs_directory = Some(logs_directory);
    }

    /// Configure context-specific log files (replaces all existing contexts)
    pub fn configure_context_log_files(context_log_files: HashMap<String, String>) {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.context_log_files = context_log_files.clone();

        // Initialize each context log file
        for (context, path) in &context_log_files {
            // Create directory if it doesn't exist
            if let Some(parent) = Path::new(path).parent() {
                let _ = std::fs::create_dir_all(parent);
            }

            // Only write header if file doesn't exist (don't overwrite)
            if !Path::new(path).exists() {
                if let Ok(mut file) = File::create(path) {
                    let _ = writeln!(file, "=== Log started at {} [{}] ===",
                        Local::now().format("%Y-%m-%d %H:%M:%S"), context);
                } else {
                    eprintln!("Failed to initialize log file for context {}: {}", context, path);
                }
            }
        }
    }

    /// Add context-specific log files (merges with existing contexts)
    pub fn add_context_log_files(context_log_files: HashMap<String, String>) {
        let mut config = LOGGER_CONFIG.lock().unwrap();

        // Add or update each context
        for (context, path) in context_log_files {
            // Skip if already configured with same path
            if let Some(existing_path) = config.context_log_files.get(&context) {
                if existing_path == &path {
                    continue;
                }
            }

            // Create directory if it doesn't exist
            if let Some(parent) = Path::new(&path).parent() {
                let _ = std::fs::create_dir_all(parent);
            }

            // Only write header if file doesn't exist (don't overwrite)
            if !Path::new(&path).exists() {
                if let Ok(mut file) = File::create(&path) {
                    let _ = writeln!(file, "=== Log started at {} [{}] ===",
                        Local::now().format("%Y-%m-%d %H:%M:%S"), context);
                } else {
                    eprintln!("Failed to add log file for context {}: {}", context, path);
                }
            }

            config.context_log_files.insert(context, path);
        }
    }

    /// Remove specific context log files
    pub fn remove_context_log_files(contexts: &[&str]) {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        for context in contexts {
            config.context_log_files.remove(*context);
        }
    }

    /// Clear all log files (main and context-specific)
    pub fn clear_logs() {
        let config = LOGGER_CONFIG.lock().unwrap();
        
        // Delete main log file
        if let Some(path) = &config.log_file_path {
            let _ = std::fs::remove_file(path);
        }
        
        // Delete all context log files
        for (_, path) in &config.context_log_files {
            let _ = std::fs::remove_file(path);
        }

        // Clear all .log files in the logs directory
        if let Some(logs_dir) = &config.logs_directory {
            if let Ok(entries) = std::fs::read_dir(logs_dir) {
                for entry in entries.flatten() {
                    if let Ok(file_type) = entry.file_type() {
                        if file_type.is_file() {
                            if let Some(file_name) = entry.file_name().to_str() {
                                if file_name.ends_with(".log") {
                                    let _ = std::fs::remove_file(entry.path());
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// Clear old log files older than the specified number of days
    pub fn clear_old_logs(days: i64) {
        use std::time::{SystemTime, Duration};
        
        let config = LOGGER_CONFIG.lock().unwrap();
        let cutoff = SystemTime::now() - Duration::from_secs((days * 24 * 60 * 60) as u64);
        
        // Clear old files in logs directory
        if let Some(logs_dir) = &config.logs_directory {
            if let Ok(entries) = std::fs::read_dir(logs_dir) {
                for entry in entries.flatten() {
                    if let Ok(metadata) = entry.metadata() {
                        if metadata.is_file() {
                            if let Some(file_name) = entry.file_name().to_str() {
                                if file_name.ends_with(".log") {
                                    if let Ok(modified) = metadata.modified() {
                                        if modified < cutoff {
                                            let _ = std::fs::remove_file(entry.path());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// Set the current log level
    pub fn set_log_level(level: LogLevel) {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.current_log_level = level;
    }

    /// Get the current log level
    pub fn get_log_level() -> LogLevel {
        let config = LOGGER_CONFIG.lock().unwrap();
        config.current_log_level
    }

    /// Log a DEBUG level message
    pub fn debug(message: &str, context: Option<&str>) {
        Self::log(LogLevel::DEBUG, message, context);
    }

    /// Log an INFO level message
    pub fn info(message: &str, context: Option<&str>) {
        Self::log(LogLevel::INFO, message, context);
    }

    /// Log a WARN level message
    pub fn warn(message: &str, context: Option<&str>) {
        Self::log(LogLevel::WARN, message, context);
    }

    /// Log an ERROR level message
    pub fn error(message: &str, context: Option<&str>) {
        Self::log(LogLevel::ERROR, message, context);
    }

    /// Core logging method
    fn log(level: LogLevel, message: &str, context: Option<&str>) {
        let config = LOGGER_CONFIG.lock().unwrap();

        if level < config.current_log_level {
            return;
        }

        let timestamp = Local::now().format("%Y-%m-%d %H:%M:%S%.3f");
        let level_str = format!("{:?}", level);
        let level_str_padded = format!("{:<5}", level_str);
        let context_str = context.map(|c| format!("[{}] ", c)).unwrap_or_default();
        let log_line = format!("{} {} {}{}", timestamp, level_str_padded, context_str, message);

        // Console output
        if config.console_output {
            match level {
                LogLevel::DEBUG => println!("\x1b[90m{}\x1b[0m", log_line),  // Gray
                LogLevel::INFO => println!("{}", log_line),                   // White
                LogLevel::WARN => println!("\x1b[33m{}\x1b[0m", log_line),   // Yellow
                LogLevel::ERROR => println!("\x1b[31m{}\x1b[0m", log_line),  // Red
                LogLevel::NONE => {},
            }
        }

        // File output with rotation check
        // First, check if there's a context-specific log file
        if let Some(ctx) = context {
            if let Some(context_path) = config.context_log_files.get(ctx) {
                if let Ok(mut file) = OpenOptions::new()
                    .create(true)
                    .append(true)
                    .open(context_path)
                {
                    let _ = writeln!(file, "{}", log_line);
                }
            }
        } else if let Some(path) = &config.log_file_path {
            if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(path) {
                let _ = writeln!(file, "{}", log_line);
            }
        }
    }

    /// Enable logging to file
    pub fn enable_file_logging(file_path: &str) {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.log_file_path = Some(file_path.to_string());

        if let Ok(mut file) = File::create(file_path) {
            let _ = writeln!(file, "=== Log started at {} ===", Local::now().format("%Y-%m-%d %H:%M:%S"));
        } else {
            eprintln!("Failed to initialize log file: {}", file_path);
        }
    }

    /// Disable logging to file
    pub fn disable_file_logging() {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.log_file_path = None;
    }

    /// Enable console output
    pub fn enable_console_output() {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.console_output = true;
    }

    /// Disable console output
    pub fn disable_console_output() {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.console_output = false;
    }

    /// Flush any pending logs
    /// Note: Rust file writes are typically immediate with writeln! macro
    /// This is a no-op for compatibility with other language implementations
    pub fn flush() {
        // Rust's writeln! macro flushes immediately to the file handle
        // So this is a no-op, but provided for API compatibility
    }
}

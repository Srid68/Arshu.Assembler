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
    base_log_file_path: Option<String>,
    current_rotated_path: Option<String>,
    context_log_files: HashMap<String, String>,
}

lazy_static::lazy_static! {
    static ref LOGGER_CONFIG: Mutex<LoggerConfig> = Mutex::new(LoggerConfig {
        current_log_level: LogLevel::NONE,
        log_file_path: None,
        console_output: true,
        log_rotation: LogRotation::NONE,
        base_log_file_path: None,
        current_rotated_path: None,
        context_log_files: HashMap::new(),
    });
}

pub struct Logger;

impl Logger {
    /// Configure the logger
    pub fn configure(level: LogLevel, log_file_path: Option<String>, console_output: bool, rotation: LogRotation) {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.current_log_level = level;
        config.base_log_file_path = log_file_path.clone();
        config.console_output = console_output;
        config.log_rotation = rotation;

        // Generate the initial rotated path
        config.current_rotated_path = Self::get_rotated_file_path_internal(&config);
        config.log_file_path = config.current_rotated_path.clone();

        // Create or clear log file if specified
        if let Some(path) = &config.log_file_path {
            if let Ok(mut file) = File::create(path) {
                let _ = writeln!(file, "=== Log started at {} ===", Local::now().format("%Y-%m-%d %H:%M:%S"));
            } else {
                eprintln!("Failed to initialize log file: {}", path);
            }
        }
    }

    /// Generate the rotated file path based on rotation setting
    fn get_rotated_file_path_internal(config: &LoggerConfig) -> Option<String> {
        let base_path = config.base_log_file_path.as_ref()?;

        if config.log_rotation == LogRotation::NONE {
            return Some(base_path.clone());
        }

        let path = Path::new(base_path);
        let directory = path.parent().map(|p| p.to_string_lossy().to_string()).unwrap_or_default();
        let file_stem = path.file_stem().map(|s| s.to_string_lossy().to_string()).unwrap_or_default();
        let extension = path.extension().map(|e| format!(".{}", e.to_string_lossy())).unwrap_or_default();

        let now = Local::now();
        let suffix = match config.log_rotation {
            LogRotation::HOURLY => now.format("%Y-%m-%d_%H").to_string(),
            LogRotation::DAILY => now.format("%Y-%m-%d").to_string(),
            LogRotation::NONE => String::new(),
        };

        let rotated_file_name = format!("{}_{}{}", file_stem, suffix, extension);

        if directory.is_empty() {
            Some(rotated_file_name)
        } else {
            Some(format!("{}/{}", directory, rotated_file_name))
        }
    }

    /// Configure context-specific log files
    pub fn configure_context_log_files(context_log_files: HashMap<String, String>) {
        let mut config = LOGGER_CONFIG.lock().unwrap();
        config.context_log_files = context_log_files.clone();

        // Initialize each context log file
        for (context, path) in &context_log_files {
            // Create directory if it doesn't exist
            if let Some(parent) = Path::new(path).parent() {
                let _ = std::fs::create_dir_all(parent);
            }

            if let Ok(mut file) = File::create(path) {
                let _ = writeln!(file, "=== Log started at {} [{}] ===",
                    Local::now().format("%Y-%m-%d %H:%M:%S"), context);
            } else {
                eprintln!("Failed to initialize log file for context {}: {}", context, path);
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
        let mut config = LOGGER_CONFIG.lock().unwrap();

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
        } else if config.base_log_file_path.is_some() {
            // Check if we need to rotate to a new file
            let new_rotated_path = Self::get_rotated_file_path_internal(&config);
            if new_rotated_path != config.current_rotated_path {
                config.current_rotated_path = new_rotated_path.clone();
                config.log_file_path = new_rotated_path.clone();

                // Write header to new rotated file
                if let Some(path) = &config.log_file_path {
                    if !std::path::Path::new(path).exists() {
                        if let Ok(mut file) = File::create(path) {
                            let _ = writeln!(file, "=== Log started at {} ===", Local::now().format("%Y-%m-%d %H:%M:%S"));
                        }
                    }
                }
            }

            if let Some(path) = &config.log_file_path {
                if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(path) {
                    let _ = writeln!(file, "{}", log_line);
                }
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
}

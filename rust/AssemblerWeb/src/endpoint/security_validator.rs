use std::collections::HashSet;
use std::sync::OnceLock;
use regex::Regex;

// Maximum parameter length to prevent DoS attacks
const PARAM_MAX_LENGTH: usize = 256;

// Maximum content sizes to prevent DDOS attacks
pub const MAX_LOG_FILE_SIZE: usize = 500 * 1024; // 500 KB per log file

/// Valid engine types allowlist
static VALID_ENGINE_TYPES: OnceLock<HashSet<String>> = OnceLock::new();

/// Gets the valid engine types (equivalent to C# SecurityValidator.ValidEngineTypes)
pub fn get_valid_engine_types() -> &'static HashSet<String> {
    VALID_ENGINE_TYPES.get_or_init(|| {
        let mut set = HashSet::new();
        set.insert("Normal".to_string());
        set.insert("PreProcess".to_string());
        set
    })
}

// Log entry validation pattern (allows timestamps, log levels, messages, stack traces)
// Matches patterns like: [timestamp] LEVEL: message or similar structured log formats
static LOG_ENTRY_PATTERN: OnceLock<Regex> = OnceLock::new();

fn get_log_entry_pattern() -> &'static Regex {
    LOG_ENTRY_PATTERN.get_or_init(|| {
        Regex::new(r"^[\[\]0-9:\-\s\.TZ]+\s*(DEBUG|INFO|WARN|ERROR|TRACE|FATAL)?:?\s*.+$")
            .expect("Failed to compile log entry regex")
    })
}

/// Gets the valid AppSites from TemplateConfig. Throws if not loaded.
pub fn get_valid_app_sites() -> Result<HashSet<String>, String> {
    assembler::config::config_util::ConfigUtil::get_app_sites()
}

/// Validates if a path component is safe (no traversal, invalid chars, or excessive length)
pub fn is_valid_path_component(value: Option<&str>) -> bool {
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
            if v.chars().any(|c| invalid_chars.contains(&c) || c.is_control()) {
                return false;
            }

            true
        }
    }
}

/// Validates content size against maximum limit
pub fn is_valid_content_size(content: Option<&str>, max_size: usize) -> bool {
    match content {
        None => true,
        Some(c) if c.is_empty() => true,
        Some(c) => c.len() <= max_size,
    }
}

/// Validates log content format and size
pub fn is_valid_log_content(log_content: Option<&str>) -> (bool, Option<String>) {
    let content = match log_content {
        None => return (false, Some("Log content is empty".to_string())),
        Some(c) if c.is_empty() => return (false, Some("Log content is empty".to_string())),
        Some(c) => c,
    };

    // Check file size limit (500 KB per log file)
    if !is_valid_content_size(Some(content), MAX_LOG_FILE_SIZE) {
        return (false, Some("Log file exceeds maximum size limit (500 KB)".to_string()));
    }

    // Split into lines for validation
    let lines: Vec<&str> = content.lines()
        .filter(|line| !line.trim().is_empty())
        .collect();

    // Check if at least some lines match log pattern (allow some flexibility)
    // Require at least 50% of non-empty lines to match log pattern
    let mut valid_lines = 0;
    let total_lines = lines.len();

    let log_pattern = get_log_entry_pattern();

    for line in &lines {
        // Check if line matches log pattern or is a continuation line (stack trace, etc.)
        if log_pattern.is_match(line) || line.starts_with("    at ") || line.starts_with("\tat ") {
            valid_lines += 1;
        }
    }

    // At least 50% of lines should match expected log format
    if total_lines > 0 && (valid_lines as f64 / total_lines as f64) < 0.5 {
        return (false, Some("Log content does not match expected format".to_string()));
    }

    (true, None)
}


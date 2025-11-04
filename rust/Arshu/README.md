# Arshu Common Library (Rust)

Core shared library providing logging and common utilities for all Arshu Rust projects.

## Purpose

Provides a single, centralized implementation of Logger that is shared across all Arshu Rust projects (Assembler, OCIServer, etc.) to avoid duplication and ensure consistent logging behavior.

## Components

### Logger (`arshu_common::common`)

Provides file-based and console logging with:
- Multiple log levels (DEBUG, INFO, WARN, ERROR, NONE)
- Context-specific log files
- Log rotation (NONE, HOURLY, DAILY)
- Configurable console output
- Thread-safe operations

## Installation

### From Local Path

In your `Cargo.toml`:

```toml
[dependencies]
arshu-common = { path = "C:/Polyglot/Arshu.Assembler/rust/Arshu" }
```

## Usage

### Basic Usage

```rust
use arshu_common::common::{Logger, LogLevel, LogRotation};

fn main() {
    // Configure logger
    Logger::configure(LogLevel::DEBUG, false, LogRotation::HOURLY);
    Logger::set_logs_directory("/path/to/logs");

    // Set context-specific log files
    let mut context_files = std::collections::HashMap::new();
    context_files.insert("MyApp".to_string(), "/path/to/logs/myapp.log".to_string());
    Logger::configure_context_log_files(context_files);

    // Log messages
    Logger::info("Application started", Some("MyApp"));
    Logger::debug("Debug information", Some("MyApp"));
    Logger::warn("Warning message", Some("MyApp"));
    Logger::error("Error occurred", Some("MyApp"));
}
```

## API Reference

### Logger Methods

- `Logger::configure(level, console_output, rotation)` - Configure the logger
- `Logger::set_logs_directory(directory)` - Set the logs directory
- `Logger::set_log_level(level)` - Set the current log level
- `Logger::get_log_level()` - Get the current log level
- `Logger::debug(message, context)` - Log a DEBUG message
- `Logger::info(message, context)` - Log an INFO message
- `Logger::warn(message, context)` - Log a WARN message
- `Logger::error(message, context)` - Log an ERROR message
- `Logger::configure_context_log_files(context_log_files)` - Configure context-specific log files
- `Logger::add_context_log_files(context_log_files)` - Add context-specific log files
- `Logger::remove_context_log_files(contexts)` - Remove specific context log files
- `Logger::clear_logs()` - Clear all log files
- `Logger::clear_old_logs(days)` - Clear log files older than specified days
- `Logger::flush()` - Flush pending logs

### Log Levels

- `LogLevel::DEBUG` - Detailed debugging information
- `LogLevel::INFO` - General informational messages
- `LogLevel::WARN` - Warning messages
- `LogLevel::ERROR` - Error messages
- `LogLevel::NONE` - No logging

### Log Rotation

- `LogRotation::NONE` - No rotation
- `LogRotation::HOURLY` - Rotate logs every hour
- `LogRotation::DAILY` - Rotate logs every day

## Projects Using arshu-common

- **arshu-assembler** - Template assembly engine
- **Arshu.OCIServer** - OCI-compliant container registry
- Future Rust projects

## License

MIT

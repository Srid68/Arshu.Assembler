# Arshu Common Library (Go)

Core shared library providing logging and common utilities for all Arshu Go projects.

## Purpose

Provides a single, centralized implementation of Logger that is shared across all Arshu Go projects (Assembler, OCIServer, etc.) to avoid duplication and ensure consistent logging behavior.

## Components

### Logger (`github.com/srid68/arshu-common/common`)

Provides file-based and console logging with:
- Multiple log levels (DEBUG, INFO, WARN, ERROR, NONE)
- Context-specific log files
- Log rotation (NONE, HOURLY, DAILY)
- Configurable console output
- Thread-safe operations

## Installation

### From Local Module

In your `go.mod`:

```go
module yourproject

go 1.21

require github.com/srid68/arshu-common v1.0.0

replace github.com/srid68/arshu-common => C:/Polyglot/Arshu.Assembler/go/Arshu
```

Then:
```bash
go mod tidy
```

## Usage

### Basic Usage

```go
package main

import (
    "github.com/srid68/arshu-common/common"
)

func main() {
    // Configure logger
    common.Configure(common.DEBUG, false, common.HOURLY)
    common.SetLogsDirectory("/path/to/logs")

    // Set context-specific log files
    common.ConfigureContextLogFiles(map[string]string{
        "MyApp": "/path/to/logs/myapp.log",
    })

    // Log messages
    common.Info("Application started", "MyApp")
    common.Debug("Debug information", "MyApp")
    common.Warn("Warning message", "MyApp")
    common.Error("Error occurred", "MyApp")
}
```

## API Reference

### Logger Functions

- `Configure(level LogLevel, consoleOutput bool, rotation LogRotation)` - Configure the logger
- `SetLogsDirectory(directory string)` - Set the logs directory
- `SetLogLevel(level LogLevel)` - Set the current log level
- `GetLogLevel() LogLevel` - Get the current log level
- `Debug(message, context string)` - Log a DEBUG message
- `Info(message, context string)` - Log an INFO message
- `Warn(message, context string)` - Log a WARN message
- `Error(message, context string)` - Log an ERROR message
- `ConfigureContextLogFiles(contextLogFiles map[string]string)` - Configure context-specific log files
- `AddContextLogFiles(contextLogFiles map[string]string)` - Add context-specific log files
- `RemoveContextLogFiles(contexts ...string)` - Remove specific context log files
- `ClearLogs()` - Clear all log files
- `ClearOldLogs(days int)` - Clear log files older than specified days
- `Flush()` - Flush pending logs

### Log Levels

- `DEBUG` - Detailed debugging information
- `INFO` - General informational messages
- `WARN` - Warning messages
- `ERROR` - Error messages
- `NONE` - No logging

### Log Rotation

- `NONE` - No rotation
- `HOURLY` - Rotate logs every hour
- `DAILY` - Rotate logs every day

## Projects Using arshu-common

- **arshu-assembler** - Template assembly engine
- **Arshu.OCIServer** - OCI-compliant container registry
- Future Go projects

## License

MIT

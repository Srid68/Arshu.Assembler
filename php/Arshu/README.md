# Arshu Common Library (PHP)

Core shared library providing logging and common utilities for all Arshu PHP projects.

## Purpose

Provides a single, centralized implementation of Logger that is shared across all Arshu PHP projects (Assembler, OCIServer, etc.) to avoid duplication and ensure consistent logging behavior.

## Components

### Logger (`Arshu\Common\Logger`)

Provides file-based and console logging with:
- Multiple log levels (DEBUG, INFO, WARN, ERROR, NONE)
- Context-specific log files
- Log rotation (NONE, HOURLY, DAILY)
- Configurable console output
- Thread-safe operations

## Installation

### From Local Package

Add to your `composer.json`:

```json
{
    "repositories": [
        {
            "type": "artifact",
            "url": "file://C:/Polyglot/LocalPackages"
        }
    ],
    "require": {
        "arshu/common": "^1.0"
    }
}
```

Then run:
```bash
composer install
```

## Usage

### Basic Usage

```php
<?php
use Arshu\Common\Logger;

// Configure logger
Logger::configure(Logger::DEBUG, false, Logger::HOURLY);
Logger::setLogsDirectory('/path/to/logs');

// Set context-specific log files
Logger::configureContextLogFiles([
    'MyApp' => '/path/to/logs/myapp.log'
]);

// Log messages
Logger::info('Application started', 'MyApp');
Logger::debug('Debug information', 'MyApp');
Logger::warn('Warning message', 'MyApp');
Logger::error('Error occurred', 'MyApp');
```

## API Reference

### Logger Methods

- `Logger::configure($level, $consoleOutput, $rotation)` - Configure the logger
- `Logger::setLogsDirectory($directory)` - Set the logs directory
- `Logger::setLogLevel($level)` - Set the current log level
- `Logger::getLogLevel()` - Get the current log level
- `Logger::debug($message, $context)` - Log a DEBUG message
- `Logger::info($message, $context)` - Log an INFO message
- `Logger::warn($message, $context)` - Log a WARN message
- `Logger::error($message, $context)` - Log an ERROR message
- `Logger::configureContextLogFiles($contextLogFiles)` - Configure context-specific log files
- `Logger::addContextLogFiles($contextLogFiles)` - Add context-specific log files
- `Logger::removeContextLogFiles(...$contexts)` - Remove specific context log files
- `Logger::clearLogs()` - Clear all log files
- `Logger::clearOldLogs($days)` - Clear log files older than specified days
- `Logger::flush()` - Flush pending logs

### Log Levels

- `Logger::DEBUG` - Detailed debugging information
- `Logger::INFO` - General informational messages
- `Logger::WARN` - Warning messages
- `Logger::ERROR` - Error messages
- `Logger::NONE` - No logging

### Log Rotation

- `Logger::NONE` - No rotation
- `Logger::HOURLY` - Rotate logs every hour
- `Logger::DAILY` - Rotate logs every day

## Projects Using arshu/common

- **arshu/assembler** - Template assembly engine
- **Arshu.OCIServer** - OCI-compliant container registry
- Future PHP projects

## License

MIT

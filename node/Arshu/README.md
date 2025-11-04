# @arshu/common

Core shared library providing logging and common utilities for all Arshu Node.js projects.

## Purpose

Provides a single, centralized implementation of Logger that is shared across all Arshu Node.js projects (Assembler, OCIServer, etc.) to avoid duplication and ensure consistent logging behavior.

## Components

### Logger

Provides file-based and console logging with:
- Multiple log levels (DEBUG, INFO, WARN, ERROR, NONE)
- Context-specific log files
- Log rotation (NONE, HOURLY, DAILY)
- Configurable console output
- Color-coded console output

## Installation

### From Local Package

```bash
npm install file:C:/Polyglot/LocalPackages/arshu-common-1.0.0.tgz
```

Or add to package.json:
```json
{
  "dependencies": {
    "@arshu/common": "file:../../LocalPackages/arshu-common-1.0.0.tgz"
  }
}
```

## Usage

### Basic Usage

```javascript
import { Logger, LogLevel, LogRotation } from '@arshu/common';

// Configure logger
Logger.configure(LogLevel.DEBUG, false, LogRotation.HOURLY);
Logger.setLogsDirectory('/path/to/logs');

// Set context-specific log files
Logger.configureContextLogFiles({
    'MyApp': '/path/to/logs/myapp.log'
});

// Log messages
Logger.info('Application started', 'MyApp');
Logger.debug('Debug information', 'MyApp');
Logger.warn('Warning message', 'MyApp');
Logger.error('Error occurred', 'MyApp');
```

### Using Convenience Functions

```javascript
import { configure, info, debug, warn, error, LogLevel } from '@arshu/common';

configure(LogLevel.INFO, true);
info('Application started');
debug('Debug message');
warn('Warning message');
error('Error occurred');
```

## API Reference

### Logger Methods

- `configure(level, consoleOutput, rotation)` - Configure the logger
- `setLogsDirectory(directory)` - Set the logs directory
- `setLogLevel(level)` - Set the current log level
- `getLogLevel()` - Get the current log level
- `debug(message, context)` - Log a DEBUG message
- `info(message, context)` - Log an INFO message
- `warn(message, context)` - Log a WARN message
- `error(message, context)` - Log an ERROR message
- `configureContextLogFiles(contextLogFiles)` - Configure context-specific log files
- `addContextLogFiles(contextLogFiles)` - Add context-specific log files
- `removeContextLogFiles(...contexts)` - Remove specific context log files
- `clearLogs()` - Clear all log files
- `clearOldLogs(days)` - Clear log files older than specified days
- `flush()` - Flush pending logs (no-op in Node.js, for API compatibility)

### Log Levels

- `LogLevel.DEBUG` - Detailed debugging information
- `LogLevel.INFO` - General informational messages
- `LogLevel.WARN` - Warning messages
- `LogLevel.ERROR` - Error messages
- `LogLevel.NONE` - No logging

### Log Rotation

- `LogRotation.NONE` - No rotation
- `LogRotation.HOURLY` - Rotate logs every hour
- `LogRotation.DAILY` - Rotate logs every day

## Projects Using @arshu/common

- **@arshu/assembler** - Template assembly engine
- **Arshu.OCIServer** - OCI-compliant container registry
- Future Node.js projects

## License

MIT

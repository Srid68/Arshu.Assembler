import fs from 'fs';
import path from 'path';

/**
 * LogLevel enum
 */
export const LogLevel = {
    DEBUG: 0,
    INFO: 1,
    WARN: 2,
    ERROR: 3,
    NONE: 4
};

/**
 * LogRotation enum
 */
export const LogRotation = {
    NONE: 0,
    HOURLY: 1,
    DAILY: 2
};

const logLevelNames = {
    [LogLevel.DEBUG]: 'DEBUG',
    [LogLevel.INFO]: 'INFO ',
    [LogLevel.WARN]: 'WARN ',
    [LogLevel.ERROR]: 'ERROR',
    [LogLevel.NONE]: 'NONE '
};

const logLevelColors = {
    [LogLevel.DEBUG]: '\x1b[90m',  // Gray
    [LogLevel.INFO]: '\x1b[97m',   // White
    [LogLevel.WARN]: '\x1b[93m',   // Yellow
    [LogLevel.ERROR]: '\x1b[91m'   // Red
};

const colorReset = '\x1b[0m';

/**
 * Simple custom logger for consistent logging across all language implementations.
 * Provides file-based logging with different log levels.
 */
class LoggerClass {
    constructor() {
        this.currentLogLevel = LogLevel.INFO;
        this.logFilePath = null;
        this.consoleOutput = true;
        this.logRotation = LogRotation.HOURLY;
        this.logsDirectory = null;
        this.currentRotatedPath = null;
        this.contextLogFiles = {};
    }

    /**
     * Configure the logger (no log file path - use setLogsDirectory instead)
     */
    configure(level, consoleOutput = true, rotation = LogRotation.NONE) {
        this.currentLogLevel = level;
        this.consoleOutput = consoleOutput;
        this.logRotation = rotation;
    }

    /**
     * Set the logs directory - the ONLY way to specify where logs are stored
     */
    setLogsDirectory(logsDirectory) {
        this.logsDirectory = logsDirectory;
    }

    /**
     * Set the current log level
     */
    setLogLevel(level) {
        this.currentLogLevel = level;
    }

    /**
     * Get the current log level
     */
    getLogLevel() {
        return this.currentLogLevel;
    }

    /**
     * Log a DEBUG level message
     */
    debug(message, context = null) {
        this._log(LogLevel.DEBUG, message, context);
    }

    /**
     * Log an INFO level message
     */
    info(message, context = null) {
        this._log(LogLevel.INFO, message, context);
    }

    /**
     * Log a WARN level message
     */
    warn(message, context = null) {
        this._log(LogLevel.WARN, message, context);
    }

    /**
     * Log an ERROR level message
     */
    error(message, context = null) {
        this._log(LogLevel.ERROR, message, context);
    }

    /**
     * Core logging method
     */
    _log(level, message, context = null) {
        if (level < this.currentLogLevel) {
            return;
        }

        const now = new Date();
        const timestamp = now.toISOString().replace('T', ' ').substring(0, 23);
        const levelStr = logLevelNames[level];
        const contextStr = context ? `[${context}] ` : '';
        const logLine = `${timestamp} ${levelStr} ${contextStr}${message}`;

        // Console output
        if (this.consoleOutput) {
            const color = logLevelColors[level] || '';
            console.log(`${color}${logLine}${colorReset}`);
        }

        // File output - check if there's a context-specific log file
        if (context && this.contextLogFiles[context]) {
            try {
                fs.appendFileSync(this.contextLogFiles[context], logLine + '\n');
                return; // Don't write to default log file if context file is used
            } catch {
                // Silently fail to avoid recursive logging issues
            }
        }

        if (this.logFilePath) {
            try {
                fs.appendFileSync(this.logFilePath, logLine + '\n');
            } catch {
                // Silently fail to avoid recursive logging issues
            }
        }
    }

    /**
     * Enable logging to file
     */
    enableFileLogging(filePath) {
        this.logFilePath = filePath;
        try {
            const timestamp = new Date().toISOString().replace('T', ' ').substring(0, 19);
            fs.writeFileSync(filePath, `=== Log started at ${timestamp} ===\n`);
        } catch (err) {
            console.error(`Failed to initialize log file: ${err.message}`);
        }
    }

    /**
     * Disable logging to file
     */
    disableFileLogging() {
        this.logFilePath = null;
    }

    /**
     * Enable console output
     */
    enableConsoleOutput() {
        this.consoleOutput = true;
    }

    /**
     * Disable console output
     */
    disableConsoleOutput() {
        this.consoleOutput = false;
    }

    /**
     * Clear all log files in the logs directory
     */
    clearLogs() {
        if (!this.logsDirectory) {
            return;
        }

        if (fs.existsSync(this.logsDirectory)) {
            try {
                const files = fs.readdirSync(this.logsDirectory);
                for (const file of files) {
                    if (file.endsWith('.log')) {
                        const filePath = path.join(this.logsDirectory, file);
                        if (fs.statSync(filePath).isFile()) {
                            fs.unlinkSync(filePath);
                        }
                    }
                }
            } catch (err) {
                // Ignore errors
            }
        }
    }

    /**
     * Clear old log files older than the specified number of days
     */
    clearOldLogs(days) {
        if (!this.logsDirectory) {
            return;
        }

        const cutoffTime = Date.now() - (days * 24 * 60 * 60 * 1000);
        
        try {
            const files = fs.readdirSync(this.logsDirectory);
            
            for (const fileName of files) {
                if (!fileName.endsWith('.log')) {
                    continue;
                }

                const fullPath = path.join(this.logsDirectory, fileName);
                const stats = fs.statSync(fullPath);
                if (stats.isFile() && stats.mtimeMs < cutoffTime) {
                    fs.unlinkSync(fullPath);
                }
            }
        } catch (err) {
            // Ignore errors
        }
    }
    /**
     * Clear old log files older than the specified number of days
     */
    clearOldLogs(days) {
        const cutoffTime = Date.now() - (days * 24 * 60 * 60 * 1000);
        const filesToCheck = [];
        
        // Add main log file pattern
        if (this.baseLogFilePath) {
            filesToCheck.push(this.baseLogFilePath);
        }
        
        // Add context log file patterns
        for (const path of Object.values(this.contextLogFiles)) {
            filesToCheck.push(path);
        }
    }

    /**
     * Configure context-specific log files (replaces all existing contexts)
     */
    configureContextLogFiles(contextLogFiles) {
        this.contextLogFiles = { ...contextLogFiles };

        // Initialize each context log file
        for (const [context, filePath] of Object.entries(contextLogFiles)) {
            try {
                const dir = path.dirname(filePath);
                if (dir && !fs.existsSync(dir)) {
                    fs.mkdirSync(dir, { recursive: true });
                }

                // Only write header if file doesn't exist (don't overwrite)
                if (!fs.existsSync(filePath)) {
                    const timestamp = new Date().toISOString().replace('T', ' ').substring(0, 19);
                    fs.writeFileSync(filePath, `=== Log started at ${timestamp} [${context}] ===\n`);
                }
            } catch (err) {
                console.error(`Failed to initialize log file for context ${context}: ${err.message}`);
            }
        }
    }

    /**
     * Add context-specific log files (merges with existing contexts)
     */
    addContextLogFiles(contextLogFiles) {
        // Add or update each context
        for (const [context, filePath] of Object.entries(contextLogFiles)) {
            // Skip if already configured with same path
            if (this.contextLogFiles[context] === filePath) {
                continue;
            }

            try {
                const dir = path.dirname(filePath);
                if (dir && !fs.existsSync(dir)) {
                    fs.mkdirSync(dir, { recursive: true });
                }

                // Only write header if file doesn't exist (don't overwrite)
                if (!fs.existsSync(filePath)) {
                    const timestamp = new Date().toISOString().replace('T', ' ').substring(0, 19);
                    fs.writeFileSync(filePath, `=== Log started at ${timestamp} [${context}] ===\n`);
                }

                this.contextLogFiles[context] = filePath;
            } catch (err) {
                console.error(`Failed to add log file for context ${context}: ${err.message}`);
            }
        }
    }

    /**
     * Remove specific context log files
     */
    removeContextLogFiles(...contexts) {
        for (const context of contexts) {
            delete this.contextLogFiles[context];
        }
    }
}

// Export singleton instance
export const Logger = new LoggerClass();

// Export convenience functions
export const configure = (...args) => Logger.configure(...args);
export const setLogLevel = (...args) => Logger.setLogLevel(...args);
export const getLogLevel = () => Logger.getLogLevel();
export const debug = (...args) => Logger.debug(...args);
export const info = (...args) => Logger.info(...args);
export const warn = (...args) => Logger.warn(...args);
export const error = (...args) => Logger.error(...args);
export const enableFileLogging = (...args) => Logger.enableFileLogging(...args);
export const disableFileLogging = () => Logger.disableFileLogging();
export const enableConsoleOutput = () => Logger.enableConsoleOutput();
export const disableConsoleOutput = () => Logger.disableConsoleOutput();
export const configureContextLogFiles = (...args) => Logger.configureContextLogFiles(...args);
export const addContextLogFiles = (...args) => Logger.addContextLogFiles(...args);
export const removeContextLogFiles = (...args) => Logger.removeContextLogFiles(...args);

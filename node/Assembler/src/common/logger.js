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
        this.logRotation = LogRotation.NONE;
        this.baseLogFilePath = null;
        this.currentRotatedPath = null;
        this.contextLogFiles = {};
    }

    /**
     * Configure the logger
     */
    configure(level, logFilePath = null, consoleOutput = true, rotation = LogRotation.NONE) {
        this.currentLogLevel = level;
        this.baseLogFilePath = logFilePath;
        this.consoleOutput = consoleOutput;
        this.logRotation = rotation;

        // Generate the initial rotated path
        this.currentRotatedPath = this._getRotatedFilePath();
        this.logFilePath = this.currentRotatedPath;

        // Create or clear log file if specified
        if (this.logFilePath) {
            try {
                const timestamp = new Date().toISOString().replace('T', ' ').substring(0, 19);
                fs.writeFileSync(this.logFilePath, `=== Log started at ${timestamp} ===\n`);
            } catch (err) {
                console.error(`Failed to initialize log file: ${err.message}`);
            }
        }
    }

    /**
     * Generate the rotated file path based on rotation setting
     */
    _getRotatedFilePath() {
        if (!this.baseLogFilePath) {
            return null;
        }

        if (this.logRotation === LogRotation.NONE) {
            return this.baseLogFilePath;
        }

        const dir = path.dirname(this.baseLogFilePath);
        const ext = path.extname(this.baseLogFilePath);
        const nameWithoutExt = path.basename(this.baseLogFilePath, ext);

        const now = new Date();
        let suffix;

        if (this.logRotation === LogRotation.HOURLY) {
            const year = now.getFullYear();
            const month = String(now.getMonth() + 1).padStart(2, '0');
            const day = String(now.getDate()).padStart(2, '0');
            const hour = String(now.getHours()).padStart(2, '0');
            suffix = `${year}-${month}-${day}_${hour}`;
        } else { // DAILY
            const year = now.getFullYear();
            const month = String(now.getMonth() + 1).padStart(2, '0');
            const day = String(now.getDate()).padStart(2, '0');
            suffix = `${year}-${month}-${day}`;
        }

        const rotatedFileName = `${nameWithoutExt}_${suffix}${ext}`;
        return path.join(dir, rotatedFileName);
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

        // File output with rotation check
        // First, check if there's a context-specific log file
        if (context && this.contextLogFiles[context]) {
            try {
                fs.appendFileSync(this.contextLogFiles[context], logLine + '\n');
                return; // Don't write to default log file if context file is used
            } catch {
                // Silently fail to avoid recursive logging issues
            }
        }

        if (this.baseLogFilePath) {
            try {
                // Check if we need to rotate to a new file
                const newRotatedPath = this._getRotatedFilePath();
                if (newRotatedPath !== this.currentRotatedPath) {
                    this.currentRotatedPath = newRotatedPath;
                    this.logFilePath = newRotatedPath;

                    // Write header to new rotated file
                    if (this.logFilePath && !fs.existsSync(this.logFilePath)) {
                        const timestamp = new Date().toISOString().replace('T', ' ').substring(0, 19);
                        fs.writeFileSync(this.logFilePath, `=== Log started at ${timestamp} ===\n`);
                    }
                }

                if (this.logFilePath) {
                    fs.appendFileSync(this.logFilePath, logLine + '\n');
                }
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
     * Configure context-specific log files
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

                const timestamp = new Date().toISOString().replace('T', ' ').substring(0, 19);
                fs.writeFileSync(filePath, `=== Log started at ${timestamp} [${context}] ===\n`);
            } catch (err) {
                console.error(`Failed to initialize log file for context ${context}: ${err.message}`);
            }
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

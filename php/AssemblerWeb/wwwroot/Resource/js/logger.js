// Client-Side Logger - Matches backend Logger structure
// Provides in-memory logging with context support for engine testing

class Logger {
    static LogLevel = {
        DEBUG: 0,
        INFO: 1,
        WARN: 2,
        ERROR: 3,
        NONE: 4
    };

    static _currentLogLevel = Logger.LogLevel.NONE;
    static _consoleOutput = false;
    static _contextLogs = new Map();

    /**
     * Configure the logger
     * @param {number} level - Log level
     * @param {boolean} consoleOutput - Enable console output
     */
    static configure(level, consoleOutput = false) {
        Logger._currentLogLevel = level;
        Logger._consoleOutput = consoleOutput;
    }

    /**
     * Set the current log level
     * @param {number} level - Log level
     */
    static setLogLevel(level) {
        Logger._currentLogLevel = level;
    }

    /**
     * Get the current log level
     * @returns {number} Current log level
     */
    static getLogLevel() {
        return Logger._currentLogLevel;
    }

    /**
     * Initialize context-specific log buffers
     * @param {Array<string>} contexts - Array of context names
     */
    static initializeContexts(contexts) {
        Logger._contextLogs.clear();
        for (const context of contexts) {
            Logger._contextLogs.set(context, []);
        }
    }

    /**
     * Get logs for a specific context
     * @param {string} context - Context name
     * @returns {string} Log content
     */
    static getContextLogs(context) {
        const logs = Logger._contextLogs.get(context);
        return logs ? logs.join('\n') : '';
    }

    /**
     * Clear logs for a specific context
     * @param {string} context - Context name
     */
    static clearContextLogs(context) {
        if (Logger._contextLogs.has(context)) {
            Logger._contextLogs.set(context, []);
        }
    }

    /**
     * Clear all context logs
     */
    static clearAllLogs() {
        for (const [context, logs] of Logger._contextLogs.entries()) {
            Logger._contextLogs.set(context, []);
        }
    }

    /**
     * Log a DEBUG level message
     * @param {string} message - Log message
     * @param {string} context - Optional context
     */
    static debug(message, context = null) {
        Logger._log(Logger.LogLevel.DEBUG, message, context);
    }

    /**
     * Log an INFO level message
     * @param {string} message - Log message
     * @param {string} context - Optional context
     */
    static info(message, context = null) {
        Logger._log(Logger.LogLevel.INFO, message, context);
    }

    /**
     * Log a WARN level message
     * @param {string} message - Log message
     * @param {string} context - Optional context
     */
    static warn(message, context = null) {
        Logger._log(Logger.LogLevel.WARN, message, context);
    }

    /**
     * Log an ERROR level message
     * @param {string} message - Log message
     * @param {string} context - Optional context
     */
    static error(message, context = null) {
        Logger._log(Logger.LogLevel.ERROR, message, context);
    }

    /**
     * Core logging method
     * @param {number} level - Log level
     * @param {string} message - Log message
     * @param {string} context - Optional context
     */
    static _log(level, message, context = null) {
        if (level < Logger._currentLogLevel) {
            return;
        }

        const timestamp = new Date().toISOString().replace('T', ' ').substring(0, 23);
        const levelStr = Object.keys(Logger.LogLevel).find(key => Logger.LogLevel[key] === level).padEnd(5);
        const contextStr = context ? `[${context}] ` : '';
        const logLine = `${timestamp} ${levelStr} ${contextStr}${message}`;

        // Console output
        if (Logger._consoleOutput) {
            switch (level) {
                case Logger.LogLevel.DEBUG:
                    console.log(`%c${logLine}`, 'color: gray');
                    break;
                case Logger.LogLevel.INFO:
                    console.log(logLine);
                    break;
                case Logger.LogLevel.WARN:
                    console.warn(logLine);
                    break;
                case Logger.LogLevel.ERROR:
                    console.error(logLine);
                    break;
            }
        }

        // Context-specific log buffer
        if (context && Logger._contextLogs.has(context)) {
            Logger._contextLogs.get(context).push(logLine);
        }
    }

    /**
     * Enable console output
     */
    static enableConsoleOutput() {
        Logger._consoleOutput = true;
    }

    /**
     * Disable console output
     */
    static disableConsoleOutput() {
        Logger._consoleOutput = false;
    }
}

// Export for use in other scripts
if (typeof window !== 'undefined') {
    window.Logger = Logger;
}

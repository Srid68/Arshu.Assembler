<?php

namespace Assembler\Common;

/**
 * Simple custom logger for consistent logging across all language implementations.
 * Provides file-based logging with different log levels.
 */
class Logger
{
    const DEBUG = 0;
    const INFO = 1;
    const WARN = 2;
    const ERROR = 3;
    const NONE = 4;

    const ROTATION_NONE = 0;
    const ROTATION_HOURLY = 1;
    const ROTATION_DAILY = 2;

    private static $logLevelNames = [
        self::DEBUG => 'DEBUG',
        self::INFO => 'INFO ',
        self::WARN => 'WARN ',
        self::ERROR => 'ERROR',
        self::NONE => 'NONE '
    ];

    private static $logLevelColors = [
        self::DEBUG => "\033[90m",  // Gray
        self::INFO => "\033[97m",   // White
        self::WARN => "\033[93m",   // Yellow
        self::ERROR => "\033[91m"   // Red
    ];

    private static $colorReset = "\033[0m";
    private static $currentLogLevel = self::INFO;
    private static $logFilePath = null;
    private static $consoleOutput = true;
    private static $logRotation = self::ROTATION_HOURLY;
    private static $logsDirectory = null;
    private static $contextLogFiles = [];

    /**
     * Configure the logger (no log file path - use setLogsDirectory instead)
     */
    public static function configure(int $level, bool $consoleOutput = true, int $rotation = self::ROTATION_NONE): void
    {
        self::$currentLogLevel = $level;
        self::$consoleOutput = $consoleOutput;
        self::$logRotation = $rotation;
    }

    /**
     * Set the logs directory - the ONLY way to specify where logs are stored
     */
    public static function setLogsDirectory(string $logsDirectory): void
    {
        self::$logsDirectory = $logsDirectory;
    }

    /**
     * Set the current log level
     */
    public static function setLogLevel(int $level): void
    {
        self::$currentLogLevel = $level;
    }

    /**
     * Get the current log level
     */
    public static function getLogLevel(): int
    {
        return self::$currentLogLevel;
    }

    /**
     * Log a DEBUG level message
     */
    public static function debug(string $message, ?string $context = null): void
    {
        self::log(self::DEBUG, $message, $context);
    }

    /**
     * Log an INFO level message
     */
    public static function info(string $message, ?string $context = null): void
    {
        self::log(self::INFO, $message, $context);
    }

    /**
     * Log a WARN level message
     */
    public static function warn(string $message, ?string $context = null): void
    {
        self::log(self::WARN, $message, $context);
    }

    /**
     * Log an ERROR level message
     */
    public static function error(string $message, ?string $context = null): void
    {
        self::log(self::ERROR, $message, $context);
    }

    /**
     * Core logging method
     */
    private static function log(int $level, string $message, ?string $context = null): void
    {
        if ($level < self::$currentLogLevel) {
            return;
        }

        $timestamp = date('Y-m-d H:i:s.') . substr((string)microtime(), 2, 3);
        $levelStr = self::$logLevelNames[$level];
        $contextStr = $context !== null ? "[{$context}] " : '';
        $logLine = "{$timestamp} {$levelStr} {$contextStr}{$message}";

        // Console output
        if (self::$consoleOutput) {
            $color = self::$logLevelColors[$level] ?? '';
            echo $color . $logLine . self::$colorReset . "\n";
        }

        // File output with rotation check
        // First, check if there's a context-specific log file
        if ($context !== null && isset(self::$contextLogFiles[$context])) {
            $logFile = self::$contextLogFiles[$context];
            $directory = dirname($logFile);

            // Check if directory exists and is writable
            if (is_dir($directory) && is_writable($directory)) {
                try {
                    @file_put_contents($logFile, $logLine . "\n", FILE_APPEND);
                    return; // Don't write to default log file if context file is used
                } catch (\Exception $e) {
                    // Silently fail to avoid recursive logging issues
                }
            }
        }

        if (self::$logFilePath !== null) {
            try {
                $directory = dirname(self::$logFilePath);
                if (is_dir($directory) && is_writable($directory)) {
                    @file_put_contents(self::$logFilePath, $logLine . "\n", FILE_APPEND);
                }
            } catch (\Exception $e) {
                // Silently fail to avoid recursive logging issues
            }
        }
    }

    /**
     * Enable logging to file
     */
    public static function enableFileLogging(string $filePath): void
    {
        self::$logFilePath = $filePath;
        try {
            $timestamp = date('Y-m-d H:i:s');
            file_put_contents($filePath, "=== Log started at {$timestamp} ===\n");
        } catch (\Exception $e) {
            error_log("Failed to initialize log file: " . $e->getMessage());
        }
    }

    /**
     * Disable logging to file
     */
    public static function disableFileLogging(): void
    {
        self::$logFilePath = null;
    }

    /**
     * Enable console output
     */
    public static function enableConsoleOutput(): void
    {
        self::$consoleOutput = true;
    }

    /**
     * Disable console output
     */
    public static function disableConsoleOutput(): void
    {
        self::$consoleOutput = false;
    }

    /**
     * Clear all log files (main and context-specific)
     */
    /**
     * Clear all log files in the logs directory
     */
    public static function clearLogs(): void
    {
        if (self::$logsDirectory === null) {
            return;
        }

        if (is_dir(self::$logsDirectory)) {
            $files = @scandir(self::$logsDirectory);
            if ($files !== false) {
                foreach ($files as $file) {
                    if (pathinfo($file, PATHINFO_EXTENSION) === 'log') {
                        @unlink(self::$logsDirectory . DIRECTORY_SEPARATOR . $file);
                    }
                }
            }
        }

        // Remove the marker file used to detect initialisation so the next request
        // will recreate the log headers.
        $marker = self::$logsDirectory . DIRECTORY_SEPARATOR . '.logs_initialized';
        if (file_exists($marker)) {
            @unlink($marker);
        }
    }

    /**
     * Clear old log files older than the specified number of days
     */
    public static function clearOldLogs(int $days): void
    {
        if (self::$logsDirectory === null) {
            return;
        }

        $cutoffTime = time() - ($days * 24 * 60 * 60);
        
        if (is_dir(self::$logsDirectory)) {
            $files = @scandir(self::$logsDirectory);
            if ($files !== false) {
                foreach ($files as $fileName) {
                    if (pathinfo($fileName, PATHINFO_EXTENSION) !== 'log') {
                        continue;
                    }

                    $fullPath = self::$logsDirectory . DIRECTORY_SEPARATOR . $fileName;
                    if (is_file($fullPath)) {
                        $mtime = @filemtime($fullPath);
                        if ($mtime !== false && $mtime < $cutoffTime) {
                            @unlink($fullPath);
                        }
                    }
                }
            }
        }
    }

    /**
     * Configure context-specific log files (replaces all existing contexts)
     */
    public static function configureContextLogFiles(array $contextLogFiles): void
    {
        self::$contextLogFiles = $contextLogFiles;

        // Initialize each context log file
        foreach ($contextLogFiles as $context => $path) {
            try {
                $directory = dirname($path);
                if (!empty($directory) && !is_dir($directory)) {
                    @mkdir($directory, 0755, true);
                }

                // Check if directory is writable before attempting to write
                if (is_dir($directory) && is_writable($directory)) {
                    // Only write header if file doesn't exist (don't overwrite)
                    if (!file_exists($path)) {
                        $timestamp = date('Y-m-d H:i:s');
                        @file_put_contents($path, "=== Log started at {$timestamp} [{$context}] ===\n");
                    }
                }
            } catch (\Exception $e) {
                // Silently fail to avoid logging errors in production
            }
        }
    }

    /**
     * Add context-specific log files (merges with existing contexts)
     */
    public static function addContextLogFiles(array $contextLogFiles): void
    {
        // Add or update each context
        foreach ($contextLogFiles as $context => $path) {
            // Skip if already configured with same path
            if (isset(self::$contextLogFiles[$context]) && self::$contextLogFiles[$context] === $path) {
                continue;
            }

            try {
                $directory = dirname($path);
                if (!empty($directory) && !is_dir($directory)) {
                    @mkdir($directory, 0755, true);
                }

                // Check if directory is writable before attempting to write
                if (is_dir($directory) && is_writable($directory)) {
                    // Only write header if file doesn't exist (don't overwrite)
                    if (!file_exists($path)) {
                        $timestamp = date('Y-m-d H:i:s');
                        @file_put_contents($path, "=== Log started at {$timestamp} [{$context}] ===\n");
                    }
                }

                self::$contextLogFiles[$context] = $path;
            } catch (\Exception $e) {
                // Silently fail to avoid logging errors in production
            }
        }
    }

    /**
     * Remove specific context log files
     */
    public static function removeContextLogFiles(string ...$contexts): void
    {
        foreach ($contexts as $context) {
            unset(self::$contextLogFiles[$context]);
        }
    }
}

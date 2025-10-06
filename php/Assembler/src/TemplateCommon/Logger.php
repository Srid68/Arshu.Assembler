<?php

namespace Assembler\TemplateCommon;

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
    private static $logRotation = self::ROTATION_NONE;
    private static $baseLogFilePath = null;
    private static $currentRotatedPath = null;
    private static $contextLogFiles = [];

    /**
     * Configure the logger
     */
    public static function configure(int $level, ?string $logFilePath = null, bool $consoleOutput = true, int $rotation = self::ROTATION_NONE): void
    {
        self::$currentLogLevel = $level;
        self::$baseLogFilePath = $logFilePath;
        self::$consoleOutput = $consoleOutput;
        self::$logRotation = $rotation;

        // Generate the initial rotated path
        self::$currentRotatedPath = self::getRotatedFilePath();
        self::$logFilePath = self::$currentRotatedPath;

        // Create or clear log file if specified
        if (self::$logFilePath !== null) {
            try {
                $timestamp = date('Y-m-d H:i:s');
                file_put_contents(self::$logFilePath, "=== Log started at {$timestamp} ===\n");
            } catch (\Exception $e) {
                error_log("Failed to initialize log file: " . $e->getMessage());
            }
        }
    }

    /**
     * Generate the rotated file path based on rotation setting
     */
    private static function getRotatedFilePath(): ?string
    {
        if (self::$baseLogFilePath === null) {
            return null;
        }

        if (self::$logRotation === self::ROTATION_NONE) {
            return self::$baseLogFilePath;
        }

        $directory = dirname(self::$baseLogFilePath);
        $extension = pathinfo(self::$baseLogFilePath, PATHINFO_EXTENSION);
        $fileNameWithoutExt = pathinfo(self::$baseLogFilePath, PATHINFO_FILENAME);

        $now = new \DateTime();
        $suffix = '';

        if (self::$logRotation === self::ROTATION_HOURLY) {
            $suffix = $now->format('Y-m-d_H');
        } else { // ROTATION_DAILY
            $suffix = $now->format('Y-m-d');
        }

        $rotatedFileName = $fileNameWithoutExt . '_' . $suffix . ($extension ? '.' . $extension : '');
        return $directory . DIRECTORY_SEPARATOR . $rotatedFileName;
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
            try {
                file_put_contents(self::$contextLogFiles[$context], $logLine . "\n", FILE_APPEND);
                return; // Don't write to default log file if context file is used
            } catch (\Exception $e) {
                // Silently fail to avoid recursive logging issues
            }
        }

        if (self::$baseLogFilePath !== null) {
            try {
                // Check if we need to rotate to a new file
                $newRotatedPath = self::getRotatedFilePath();
                if ($newRotatedPath !== self::$currentRotatedPath) {
                    self::$currentRotatedPath = $newRotatedPath;
                    self::$logFilePath = $newRotatedPath;

                    // Write header to new rotated file
                    if (self::$logFilePath !== null && !file_exists(self::$logFilePath)) {
                        $timestamp = date('Y-m-d H:i:s');
                        file_put_contents(self::$logFilePath, "=== Log started at {$timestamp} ===\n");
                    }
                }

                if (self::$logFilePath !== null) {
                    file_put_contents(self::$logFilePath, $logLine . "\n", FILE_APPEND);
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
     * Configure context-specific log files
     */
    public static function configureContextLogFiles(array $contextLogFiles): void
    {
        self::$contextLogFiles = $contextLogFiles;

        // Initialize each context log file
        foreach ($contextLogFiles as $context => $path) {
            try {
                $directory = dirname($path);
                if (!empty($directory) && !is_dir($directory)) {
                    mkdir($directory, 0755, true);
                }

                $timestamp = date('Y-m-d H:i:s');
                file_put_contents($path, "=== Log started at {$timestamp} [{$context}] ===\n");
            } catch (\Exception $e) {
                error_log("Failed to initialize log file for context {$context}: " . $e->getMessage());
            }
        }
    }
}

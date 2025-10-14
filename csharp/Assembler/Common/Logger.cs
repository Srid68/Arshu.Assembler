using System;
using System.Collections.Generic;
using System.IO;

namespace Assembler.Common
{
    /// <summary>
    /// Simple custom logger for consistent logging across all language implementations.
    /// Provides file-based logging with different log levels.
    /// </summary>
    public static class Logger
    {
        public enum LogLevel
        {
            DEBUG = 0,
            INFO = 1,
            WARN = 2,
            ERROR = 3,
            NONE = 4
        }

        public enum LogRotation
        {
            NONE = 0,
            HOURLY = 1,
            DAILY = 2
        }

        private static LogLevel _currentLogLevel = LogLevel.NONE;
        private static string? _logFilePath = null;
        private static bool _consoleOutput = true;
        private static readonly object _lock = new object();
        private static LogRotation _logRotation = LogRotation.NONE;
        private static string? _baseLogFilePath = null;
        private static string? _currentRotatedPath = null;

        // Support for context-specific log files
        private static Dictionary<string, string> _contextLogFiles = new Dictionary<string, string>();
        private static Dictionary<string, string> _contextRotatedPaths = new Dictionary<string, string>();

        /// <summary>
        /// Configure the logger
        /// </summary>
        public static void Configure(LogLevel level, string? logFilePath = null, bool consoleOutput = true, LogRotation rotation = LogRotation.NONE)
        {
            _currentLogLevel = level;
            _baseLogFilePath = logFilePath;
            _consoleOutput = consoleOutput;
            _logRotation = rotation;

            // Generate the initial rotated path
            _currentRotatedPath = GetRotatedFilePath();
            _logFilePath = _currentRotatedPath;

            // Create or clear log file if specified
            if (_logFilePath != null)
            {
                try
                {
                    File.WriteAllText(_logFilePath, $"=== Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to initialize log file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Configure context-specific log files
        /// </summary>
        public static void ConfigureContextLogFiles(Dictionary<string, string> contextLogFiles)
        {
            lock (_lock)
            {
                _contextLogFiles = new Dictionary<string, string>(contextLogFiles);
                _contextRotatedPaths.Clear();

                // Initialize each context log file
                foreach (var kvp in _contextLogFiles)
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(kvp.Value);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        File.WriteAllText(kvp.Value, $"=== Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{kvp.Key}] ===\n");
                        _contextRotatedPaths[kvp.Key] = kvp.Value;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to initialize log file for context {kvp.Key}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Set the current log level
        /// </summary>
        public static void SetLogLevel(LogLevel level)
        {
            _currentLogLevel = level;
        }

        /// <summary>
        /// Get the current log level
        /// </summary>
        public static LogLevel GetLogLevel()
        {
            return _currentLogLevel;
        }

        /// <summary>
        /// Log a DEBUG level message
        /// </summary>
        public static void Debug(string message, string? context = null)
        {
            Log(LogLevel.DEBUG, message, context);
        }

        /// <summary>
        /// Log an INFO level message
        /// </summary>
        public static void Info(string message, string? context = null)
        {
            Log(LogLevel.INFO, message, context);
        }

        /// <summary>
        /// Log a WARN level message
        /// </summary>
        public static void Warn(string message, string? context = null)
        {
            Log(LogLevel.WARN, message, context);
        }

        /// <summary>
        /// Log an ERROR level message
        /// </summary>
        public static void Error(string message, string? context = null)
        {
            Log(LogLevel.ERROR, message, context);
        }

        /// <summary>
        /// Generate the rotated file path based on rotation setting
        /// </summary>
        private static string? GetRotatedFilePath()
        {
            if (_baseLogFilePath == null)
            {
                return null;
            }

            if (_logRotation == LogRotation.NONE)
            {
                return _baseLogFilePath;
            }

            var directory = Path.GetDirectoryName(_baseLogFilePath) ?? "";
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_baseLogFilePath);
            var extension = Path.GetExtension(_baseLogFilePath);

            var now = DateTime.Now;
            string suffix;

            if (_logRotation == LogRotation.HOURLY)
            {
                suffix = now.ToString("yyyy-MM-dd_HH");
            }
            else // DAILY
            {
                suffix = now.ToString("yyyy-MM-dd");
            }

            var rotatedFileName = $"{fileNameWithoutExtension}_{suffix}{extension}";
            return Path.Combine(directory, rotatedFileName);
        }

        /// <summary>
        /// Core logging method
        /// </summary>
        private static void Log(LogLevel level, string message, string? context = null)
        {
            if (level < _currentLogLevel)
            {
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level.ToString().PadRight(5);
            string contextStr = context != null ? $"[{context}] " : "";
            string logLine = $"{timestamp} {levelStr} {contextStr}{message}";

            lock (_lock)
            {
                // Console output
                if (_consoleOutput)
                {
                    switch (level)
                    {
                        case LogLevel.DEBUG:
                            Console.ForegroundColor = ConsoleColor.Gray;
                            break;
                        case LogLevel.INFO:
                            Console.ForegroundColor = ConsoleColor.White;
                            break;
                        case LogLevel.WARN:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            break;
                        case LogLevel.ERROR:
                            Console.ForegroundColor = ConsoleColor.Red;
                            break;
                    }
                    Console.WriteLine(logLine);
                    Console.ResetColor();
                }

                // File output with rotation check
                // First, check if there's a context-specific log file
                if (context != null && _contextLogFiles.ContainsKey(context))
                {
                    try
                    {
                        var contextLogFile = _contextLogFiles[context];
                        File.AppendAllText(contextLogFile, logLine + "\n");
                    }
                    catch
                    {
                        // Silently fail to avoid recursive logging issues
                    }
                }
                else if (_baseLogFilePath != null)
                {
                    try
                    {
                        // Check if we need to rotate to a new file
                        var newRotatedPath = GetRotatedFilePath();
                        if (newRotatedPath != _currentRotatedPath)
                        {
                            _currentRotatedPath = newRotatedPath;
                            _logFilePath = _currentRotatedPath;

                            // Write header to new rotated file
                            if (_logFilePath != null && !File.Exists(_logFilePath))
                            {
                                File.WriteAllText(_logFilePath, $"=== Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
                            }
                        }

                        if (_logFilePath != null)
                        {
                            File.AppendAllText(_logFilePath, logLine + "\n");
                        }
                    }
                    catch
                    {
                        // Silently fail to avoid recursive logging issues
                    }
                }
            }
        }

        /// <summary>
        /// Enable logging to file
        /// </summary>
        public static void EnableFileLogging(string filePath)
        {
            _logFilePath = filePath;
            if (_logFilePath != null)
            {
                try
                {
                    File.WriteAllText(_logFilePath, $"=== Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to initialize log file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Disable logging to file
        /// </summary>
        public static void DisableFileLogging()
        {
            _logFilePath = null;
        }

        /// <summary>
        /// Enable console output
        /// </summary>
        public static void EnableConsoleOutput()
        {
            _consoleOutput = true;
        }

        /// <summary>
        /// Disable console output
        /// </summary>
        public static void DisableConsoleOutput()
        {
            _consoleOutput = false;
        }
    }
}

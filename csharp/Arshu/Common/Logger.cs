using System;
using System.Collections.Generic;
using System.IO;

namespace Arshu.Common
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
        private static LogRotation _logRotation = LogRotation.HOURLY;
        private static string? _logsDirectory = null; // Directory for scanning/clearing logs

        // Support for context-specific log files with persistent StreamWriters
        private static Dictionary<string, string> _contextLogFiles = new Dictionary<string, string>();
        private static Dictionary<string, string> _contextRotatedPaths = new Dictionary<string, string>();
        private static Dictionary<string, StreamWriter> _contextWriters = new Dictionary<string, StreamWriter>();

        /// <summary>
        /// Configure the logger (no log file path - use SetLogsDirectory instead)
        /// </summary>
        public static void Configure(LogLevel level, bool consoleOutput = true, LogRotation rotation = LogRotation.HOURLY)
        {
            _currentLogLevel = level;
            _consoleOutput = consoleOutput;
            _logRotation = rotation;
        }

        /// <summary>
        /// Set the logs directory - the ONLY way to specify where logs are stored
        /// </summary>
        public static void SetLogsDirectory(string logsDirectory)
        {
            lock (_lock)
            {
                _logsDirectory = logsDirectory;
            }
        }

        /// <summary>
        /// Configure context-specific log files (replaces all existing contexts)
        /// </summary>
        public static void ConfigureContextLogFiles(Dictionary<string, string> contextLogFiles)
        {
            lock (_lock)
            {
                // Close existing writers
                foreach (var writer in _contextWriters.Values)
                {
                    writer?.Flush();
                    writer?.Dispose();
                }
                _contextWriters.Clear();
                _contextLogFiles.Clear();
                _contextRotatedPaths.Clear();

                // Initialize each context log file with persistent StreamWriter
                foreach (var kvp in contextLogFiles)
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(kvp.Value);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        
                        // Only write header if file doesn't exist (don't overwrite)
                        bool fileExists = File.Exists(kvp.Value);
                        
                        // Create StreamWriter with AutoFlush enabled
                        var writer = new StreamWriter(kvp.Value, append: true)
                        {
                            AutoFlush = true
                        };
                        _contextWriters[kvp.Key] = writer;
                        _contextLogFiles[kvp.Key] = kvp.Value;
                        
                        if (!fileExists)
                        {
                            writer.WriteLine($"=== Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{kvp.Key}] ===");
                        }
                        
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
        /// Add context-specific log files (merges with existing contexts)
        /// </summary>
        public static void AddContextLogFiles(Dictionary<string, string> contextLogFiles)
        {
            lock (_lock)
            {
                foreach (var kvp in contextLogFiles)
                {
                    try
                    {
                        // Skip if already configured with same path
                        if (_contextLogFiles.ContainsKey(kvp.Key) && _contextLogFiles[kvp.Key] == kvp.Value)
                        {
                            continue;
                        }

                        // Close old writer if exists
                        if (_contextWriters.ContainsKey(kvp.Key))
                        {
                            _contextWriters[kvp.Key]?.Flush();
                            _contextWriters[kvp.Key]?.Dispose();
                            _contextWriters.Remove(kvp.Key);
                        }

                        var directory = Path.GetDirectoryName(kvp.Value);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        
                        bool fileExists = File.Exists(kvp.Value);
                        
                        var writer = new StreamWriter(kvp.Value, append: true)
                        {
                            AutoFlush = true
                        };
                        _contextWriters[kvp.Key] = writer;
                        _contextLogFiles[kvp.Key] = kvp.Value;
                        
                        if (!fileExists)
                        {
                            writer.WriteLine($"=== Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{kvp.Key}] ===");
                        }
                        
                        _contextRotatedPaths[kvp.Key] = kvp.Value;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to add log file for context {kvp.Key}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Remove specific context log files
        /// </summary>
        public static void RemoveContextLogFiles(params string[] contexts)
        {
            lock (_lock)
            {
                foreach (var context in contexts)
                {
                    if (_contextWriters.ContainsKey(context))
                    {
                        _contextWriters[context]?.Flush();
                        _contextWriters[context]?.Dispose();
                        _contextWriters.Remove(context);
                    }
                    _contextLogFiles.Remove(context);
                    _contextRotatedPaths.Remove(context);
                }
            }
        }

        /// <summary>
        /// Clear all log files (for development mode)
        /// </summary>
        public static void ClearLogs()
        {
            lock (_lock)
            {
                // Close and dispose all StreamWriters first
                foreach (var writer in _contextWriters.Values)
                {
                    try
                    {
                        writer?.Flush();
                        writer?.Dispose();
                    }
                    catch { }
                }
                _contextWriters.Clear();

                // Clear main log file
                if (_logFilePath != null && File.Exists(_logFilePath))
                {
                    try { File.Delete(_logFilePath); } catch { }
                }

                // Clear context-specific log files
                foreach (var kvp in _contextLogFiles)
                {
                    if (File.Exists(kvp.Value))
                    {
                        try { File.Delete(kvp.Value); } catch { }
                    }
                }

                // Also clear all .log files in the logs directory (for DEBUG mode)
                if (_logsDirectory != null && Directory.Exists(_logsDirectory))
                {
                    try
                    {
                        var logFiles = Directory.GetFiles(_logsDirectory, "*.log");
                        foreach (var logFile in logFiles)
                        {
                            try { File.Delete(logFile); } catch { }
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Clear old log files older than specified days
        /// </summary>
        public static void ClearOldLogs(int days)
        {
            if (days <= 0) return;

            var cutoffDate = DateTime.Now.AddDays(-days);

            lock (_lock)
            {
                // Clear old main log files
                if (_logsDirectory != null && Directory.Exists(_logsDirectory))
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(_logsDirectory, "*.log"))
                        {
                            var fileInfo = new FileInfo(file);
                            if (fileInfo.LastWriteTime < cutoffDate)
                            {
                                try { File.Delete(file); } catch { }
                            }
                        }
                    }
                    catch { }
                }

                // Clear old context-specific log files
                foreach (var kvp in _contextLogFiles)
                {
                    var directory = Path.GetDirectoryName(kvp.Value);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(kvp.Value);
                        var extension = Path.GetExtension(kvp.Value);
                        var pattern = $"{fileName}_*{extension}";
                        
                        try
                        {
                            foreach (var file in Directory.GetFiles(directory, pattern))
                            {
                                var fileInfo = new FileInfo(file);
                                if (fileInfo.LastWriteTime < cutoffDate)
                                {
                                    try { File.Delete(file); } catch { }
                                }
                            }
                        }
                        catch { }
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
        /// Generate the rotated file path based on the rotation setting (not used since we don't have main log file)
        /// </summary>
        private static string GetRotatedFilePath()
        {
            // No main log file - only context files
            return "";
        }

        /// <summary>
        /// Get the rotated file path for a context log file
        /// </summary>
        private static string GetRotatedFilePathForContext(string contextLogPath)
        {
            if (string.IsNullOrEmpty(contextLogPath))
            {
                return "";
            }

            if (_logRotation == LogRotation.NONE)
            {
                return contextLogPath;
            }

            var directory = Path.GetDirectoryName(contextLogPath) ?? "";
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(contextLogPath);
            var extension = Path.GetExtension(contextLogPath);

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
                if (context != null && _contextWriters.ContainsKey(context))
                {
                    try
                    {
                        var writer = _contextWriters[context];
                        writer.WriteLine(logLine);
                        writer.Flush(); // Explicit flush in addition to AutoFlush
                    }
                    catch (Exception ex)
                    {
                        // Log to console so we can see what's wrong
                        Console.Error.WriteLine($"[Logger ERROR] Failed to write to context log '{context}': {ex.Message}");
                    }
                }
                // No main log file - only context files are used
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

        /// <summary>
        /// Flush any pending logs
        /// </summary>
        public static void Flush()
        {
            lock (_lock)
            {
                foreach (var writer in _contextWriters.Values)
                {
                    try
                    {
                        writer?.Flush();
                    }
                    catch
                    {
                        // Silently fail
                    }
                }
            }
        }
    }
}

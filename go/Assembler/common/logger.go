package common

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

// LogLevel represents the severity of a log message
type LogLevel int

const (
	DEBUG LogLevel = iota
	INFO
	WARN
	ERROR
	NONE
)

// LogRotation represents log rotation intervals
type LogRotation int

const (
	ROTATION_NONE LogRotation = iota
	ROTATION_HOURLY
	ROTATION_DAILY
)

var logLevelNames = map[LogLevel]string{
	DEBUG: "DEBUG",
	INFO:  "INFO ",
	WARN:  "WARN ",
	ERROR: "ERROR",
	NONE:  "NONE ",
}

var logLevelColors = map[LogLevel]string{
	DEBUG: "\033[90m", // Gray
	INFO:  "\033[97m", // White
	WARN:  "\033[93m", // Yellow
	ERROR: "\033[91m", // Red
}

const colorReset = "\033[0m"

// Logger is a simple custom logger for consistent logging across all language implementations
type LoggerType struct {
	currentLogLevel    LogLevel
	logFilePath        string
	consoleOutput      bool
	mutex              sync.Mutex
	logRotation        LogRotation
	logsDirectory      string
	currentRotatedPath string
	contextLogFiles    map[string]string
}

var globalLogger = &LoggerType{
	currentLogLevel: INFO,
	consoleOutput:   true,
	logRotation:     ROTATION_HOURLY,
	contextLogFiles: make(map[string]string),
}

// Configure sets up the logger (no log file path - use SetLogsDirectory instead)
func Configure(level LogLevel, consoleOutput bool, rotation LogRotation) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()

	globalLogger.currentLogLevel = level
	globalLogger.consoleOutput = consoleOutput
	globalLogger.logRotation = rotation
}

// SetLogsDirectory sets the logs directory - the ONLY way to specify where logs are stored
func SetLogsDirectory(logsDirectory string) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()
	globalLogger.logsDirectory = logsDirectory
}

// SetLogLevel sets the current log level
func SetLogLevel(level LogLevel) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()
	globalLogger.currentLogLevel = level
}

// GetLogLevel returns the current log level
func GetLogLevel() LogLevel {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()
	return globalLogger.currentLogLevel
}

// Debug logs a DEBUG level message
func Debug(message string, context ...string) {
	contextStr := ""
	if len(context) > 0 {
		contextStr = context[0]
	}
	log(DEBUG, message, contextStr)
}

// Info logs an INFO level message
func Info(message string, context ...string) {
	contextStr := ""
	if len(context) > 0 {
		contextStr = context[0]
	}
	log(INFO, message, contextStr)
}

// Warn logs a WARN level message
func Warn(message string, context ...string) {
	contextStr := ""
	if len(context) > 0 {
		contextStr = context[0]
	}
	log(WARN, message, contextStr)
}

// Error logs an ERROR level message
func Error(message string, context ...string) {
	contextStr := ""
	if len(context) > 0 {
		contextStr = context[0]
	}
	log(ERROR, message, contextStr)
}

// log is the core logging method
func log(level LogLevel, message string, context string) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()

	if level < globalLogger.currentLogLevel {
		return
	}

	timestamp := time.Now().Format("2006-01-02 15:04:05.000")
	levelStr := logLevelNames[level]
	contextStr := ""
	if context != "" {
		contextStr = fmt.Sprintf("[%s] ", context)
	}
	logLine := fmt.Sprintf("%s %s %s%s", timestamp, levelStr, contextStr, message)

	// Console output
	if globalLogger.consoleOutput {
		color := logLevelColors[level]
		fmt.Printf("%s%s%s\n", color, logLine, colorReset)
	}

	// File output - check if there's a context-specific log file
	if context != "" {
		if contextPath, ok := globalLogger.contextLogFiles[context]; ok {
			file, err := os.OpenFile(contextPath, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
			if err == nil {
				file.WriteString(logLine + "\n")
				file.Close()
			}
			return // Don't write to default log file if context file is used
		}
	}

	if globalLogger.logFilePath != "" {
		file, err := os.OpenFile(globalLogger.logFilePath, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
		if err == nil {
			file.WriteString(logLine + "\n")
			file.Close()
		}
	}
}

// EnableFileLogging enables logging to the specified file
func EnableFileLogging(filePath string) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()

	globalLogger.logFilePath = filePath
	timestamp := time.Now().Format("2006-01-02 15:04:05")
	header := fmt.Sprintf("=== Log started at %s ===\n", timestamp)
	err := os.WriteFile(filePath, []byte(header), 0644)
	if err != nil {
		fmt.Fprintf(os.Stderr, "Failed to initialize log file: %v\n", err)
	}
}

// DisableFileLogging disables logging to file
func DisableFileLogging() {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()
	globalLogger.logFilePath = ""
}

// ClearLogs deletes all log files in the logs directory
func ClearLogs() {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()

	if globalLogger.logsDirectory == "" {
		return
	}

	entries, err := os.ReadDir(globalLogger.logsDirectory)
	if err == nil {
		for _, entry := range entries {
			if !entry.IsDir() && filepath.Ext(entry.Name()) == ".log" {
				os.Remove(filepath.Join(globalLogger.logsDirectory, entry.Name()))
			}
		}
	}
}

// ClearOldLogs deletes log files older than the specified number of days
func ClearOldLogs(days int) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()

	if globalLogger.logsDirectory == "" {
		return
	}

	cutoff := time.Now().AddDate(0, 0, -days)
	entries, err := os.ReadDir(globalLogger.logsDirectory)
	if err != nil {
		return
	}

	for _, entry := range entries {
		if entry.IsDir() {
			continue
		}

		fileName := entry.Name()
		if !strings.HasSuffix(fileName, ".log") {
			continue
		}

		fullPath := filepath.Join(globalLogger.logsDirectory, fileName)
		info, err := os.Stat(fullPath)
		if err == nil && info.ModTime().Before(cutoff) {
			os.Remove(fullPath)
		}
	}
}

// EnableConsoleOutput enables console output
func EnableConsoleOutput() {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()
	globalLogger.consoleOutput = true
}

// DisableConsoleOutput disables console output
func DisableConsoleOutput() {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()
	globalLogger.consoleOutput = false
}

// ConfigureContextLogFiles configures context-specific log files (replaces all existing contexts)
func ConfigureContextLogFiles(contextLogFiles map[string]string) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()

	globalLogger.contextLogFiles = make(map[string]string)
	for k, v := range contextLogFiles {
		globalLogger.contextLogFiles[k] = v
	}

	// Initialize each context log file
	for context, path := range contextLogFiles {
		// Create directory if it doesn't exist
		dir := filepath.Dir(path)
		if dir != "" {
			os.MkdirAll(dir, 0755)
		}

		// Only write header if file doesn't exist (don't overwrite)
		if _, err := os.Stat(path); os.IsNotExist(err) {
			timestamp := time.Now().Format("2006-01-02 15:04:05")
			header := fmt.Sprintf("=== Log started at %s [%s] ===\n", timestamp, context)
			err := os.WriteFile(path, []byte(header), 0644)
			if err != nil {
				fmt.Fprintf(os.Stderr, "Failed to initialize log file for context %s: %v\n", context, err)
			}
		}
	}
}

// AddContextLogFiles adds context-specific log files (merges with existing contexts)
func AddContextLogFiles(contextLogFiles map[string]string) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()

	// Add or update each context
	for context, path := range contextLogFiles {
		// Skip if already configured with same path
		if existingPath, exists := globalLogger.contextLogFiles[context]; exists && existingPath == path {
			continue
		}

		// Create directory if it doesn't exist
		dir := filepath.Dir(path)
		if dir != "" {
			os.MkdirAll(dir, 0755)
		}

		// Only write header if file doesn't exist (don't overwrite)
		if _, err := os.Stat(path); os.IsNotExist(err) {
			timestamp := time.Now().Format("2006-01-02 15:04:05")
			header := fmt.Sprintf("=== Log started at %s [%s] ===\n", timestamp, context)
			err := os.WriteFile(path, []byte(header), 0644)
			if err != nil {
				fmt.Fprintf(os.Stderr, "Failed to add log file for context %s: %v\n", context, err)
			}
		}

		globalLogger.contextLogFiles[context] = path
	}
}

// RemoveContextLogFiles removes specific context log files
func RemoveContextLogFiles(contexts ...string) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()

	for _, context := range contexts {
		delete(globalLogger.contextLogFiles, context)
	}
}

package template_common

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
	DEBUG: "\033[90m",  // Gray
	INFO:  "\033[97m",  // White
	WARN:  "\033[93m",  // Yellow
	ERROR: "\033[91m",  // Red
}

const colorReset = "\033[0m"

// Logger is a simple custom logger for consistent logging across all language implementations
type LoggerType struct {
	currentLogLevel   LogLevel
	logFilePath       string
	consoleOutput     bool
	mutex             sync.Mutex
	logRotation       LogRotation
	baseLogFilePath   string
	currentRotatedPath string
	contextLogFiles   map[string]string
}

var globalLogger = &LoggerType{
	currentLogLevel: INFO,
	consoleOutput:   true,
	contextLogFiles: make(map[string]string),
}

// Configure sets up the logger with the specified level, file path, and console output
func Configure(level LogLevel, logFilePath string, consoleOutput bool, rotation LogRotation) {
	globalLogger.mutex.Lock()
	defer globalLogger.mutex.Unlock()

	globalLogger.currentLogLevel = level
	globalLogger.baseLogFilePath = logFilePath
	globalLogger.consoleOutput = consoleOutput
	globalLogger.logRotation = rotation

	// Generate the initial rotated path
	globalLogger.currentRotatedPath = getRotatedFilePath()
	globalLogger.logFilePath = globalLogger.currentRotatedPath

	// Create or clear log file if specified
	if globalLogger.logFilePath != "" {
		timestamp := time.Now().Format("2006-01-02 15:04:05")
		header := fmt.Sprintf("=== Log started at %s ===\n", timestamp)
		err := os.WriteFile(globalLogger.logFilePath, []byte(header), 0644)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Failed to initialize log file: %v\n", err)
		}
	}
}

// getRotatedFilePath generates the rotated file path based on rotation setting
func getRotatedFilePath() string {
	if globalLogger.baseLogFilePath == "" {
		return ""
	}

	if globalLogger.logRotation == ROTATION_NONE {
		return globalLogger.baseLogFilePath
	}

	dir := filepath.Dir(globalLogger.baseLogFilePath)
	ext := filepath.Ext(globalLogger.baseLogFilePath)
	nameWithoutExt := strings.TrimSuffix(filepath.Base(globalLogger.baseLogFilePath), ext)

	now := time.Now()
	var suffix string

	if globalLogger.logRotation == ROTATION_HOURLY {
		suffix = now.Format("2006-01-02_15")
	} else { // ROTATION_DAILY
		suffix = now.Format("2006-01-02")
	}

	rotatedFileName := fmt.Sprintf("%s_%s%s", nameWithoutExt, suffix, ext)
	return filepath.Join(dir, rotatedFileName)
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

	// File output with rotation check
	// First, check if there's a context-specific log file
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

	if globalLogger.baseLogFilePath != "" {
		// Check if we need to rotate to a new file
		newRotatedPath := getRotatedFilePath()
		if newRotatedPath != globalLogger.currentRotatedPath {
			globalLogger.currentRotatedPath = newRotatedPath
			globalLogger.logFilePath = newRotatedPath

			// Write header to new rotated file
			if _, err := os.Stat(globalLogger.logFilePath); os.IsNotExist(err) {
				timestamp := time.Now().Format("2006-01-02 15:04:05")
				header := fmt.Sprintf("=== Log started at %s ===\n", timestamp)
				os.WriteFile(globalLogger.logFilePath, []byte(header), 0644)
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

// ConfigureContextLogFiles configures context-specific log files
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

		timestamp := time.Now().Format("2006-01-02 15:04:05")
		header := fmt.Sprintf("=== Log started at %s [%s] ===\n", timestamp, context)
		err := os.WriteFile(path, []byte(header), 0644)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Failed to initialize log file for context %s: %v\n", context, err)
		}
	}
}

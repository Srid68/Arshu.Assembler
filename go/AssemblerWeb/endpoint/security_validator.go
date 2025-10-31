package endpoint

import (
	"assembler/config"
	"strings"
)

// Maximum parameter length to prevent DoS attacks
const ParamMaxLength = 256

// Maximum log file size (500 KB)
const MaxLogFileSize = 500 * 1024

// Output size buffer (50 KB)
const OutputSizeBuffer = 50 * 1024

// IsValidContentSize validates content size against a maximum limit (bytes)
func IsValidContentSize(content *string, maxSize int) bool {
	if content == nil || *content == "" {
		return true
	}
	return len([]byte(*content)) <= maxSize
}

// IsValidOutputSizeWithBuffer validates output size against template total size plus buffer
func IsValidOutputSizeWithBuffer(htmlContent *string, templateTotalSize int) bool {
	if htmlContent == nil || *htmlContent == "" {
		return true
	}
	outputSize := len([]byte(*htmlContent))
	if templateTotalSize > 0 {
		maxAllowedSize := templateTotalSize + OutputSizeBuffer
		return outputSize <= maxAllowedSize
	}
	// If template size is unknown, reject
	return false
}

// IsValidLogContent validates log content format and size (basic, no regex)
func IsValidLogContent(logContent *string) (bool, string) {
	if logContent == nil || *logContent == "" {
		return false, "Log content is empty"
	}
	if !IsValidContentSize(logContent, MaxLogFileSize) {
		return false, "Log file exceeds maximum size limit (500 KB)"
	}
	lines := strings.Split(*logContent, "\n")
	validLines := 0
	totalLines := 0
	for _, line := range lines {
		line = strings.TrimSpace(line)
		if line == "" {
			continue
		}
		totalLines++
		// Accept lines that look like log entries or stack traces (basic)
		if strings.HasPrefix(line, "[") || strings.HasPrefix(line, "at ") || strings.HasPrefix(line, "\tat ") {
			validLines++
		} else if len(line) > 10 {
			validLines++ // Accept long lines as possible log messages
		}
	}
	if totalLines > 0 && float64(validLines)/float64(totalLines) < 0.5 {
		return false, "Log content does not match expected format"
	}
	return true, ""
}

// GetTemplateTotalSize gets the template total size for an AppSite/AppView from scenarios
func GetTemplateTotalSize(appSite, appView string) int {
	scenarios, err := config.GetScenarios()
	if err != nil {
		return 0
	}
	for _, s := range scenarios {
		if strings.EqualFold(s.AppSite, appSite) && strings.EqualFold(s.AppView, appView) {
			return s.TotalSize
		}
	}
	return 0
}

// ValidEngineTypes is the allowlist of valid engine types
var ValidEngineTypes = map[string]bool{
	"Normal":     true,
	"PreProcess": true,
}

// GetValidAppSites gets the valid AppSites from ConfigUtil
func GetValidAppSites() (map[string]bool, error) {
	return config.GetAppSites()
}

// IsValidPathComponent validates if a path component is safe
func IsValidPathComponent(value *string) bool {
	if value == nil {
		return false
	}

	v := strings.TrimSpace(*value)
	if v == "" {
		return false
	}

	// Check parameter length to prevent DoS
	if len(v) > ParamMaxLength {
		return false
	}

	// Check for path traversal attempts
	if strings.Contains(v, "..") || strings.Contains(v, "/") || strings.Contains(v, "\\") {
		return false
	}

	// Check for suspicious characters
	invalidChars := []rune{'<', '>', ':', '"', '|', '?', '*', '\x00'}
	for _, char := range v {
		for _, invalid := range invalidChars {
			if char == invalid {
				return false
			}
		}
		// Check for control characters
		if char < 32 {
			return false
		}
	}

	return true
}

// IsValidEngineType validates engine type against allowlist (case-insensitive)
func IsValidEngineType(engineType string) bool {
	for validType := range ValidEngineTypes {
		if strings.EqualFold(validType, engineType) {
			return true
		}
	}
	return false
}

// IsValidAppSite validates app_site against allowlist (case-insensitive)
func IsValidAppSite(appSite string, validAppSites map[string]bool) bool {
	for validSite := range validAppSites {
		if strings.EqualFold(validSite, appSite) {
			return true
		}
	}
	return false
}

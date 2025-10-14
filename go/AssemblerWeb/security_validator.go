package main

import (
	"assembler/config"
	"strings"
)

// Maximum parameter length to prevent DoS attacks
const ParamMaxLength = 256

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

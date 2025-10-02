package template_common

import (
	"os"
	"path/filepath"
	"strings"
	"unicode"
)

// GetAssemblerWebDirPath returns the path to the AssemblerWeb wwwroot directory and the project directory
func GetAssemblerWebDirPath() (assemblerWebDirPath string, projectDirectory string) {
	// Docker: /app/wwwroot
	dockerWebroot := "/app/wwwroot"
	if stat, err := os.Stat(dockerWebroot); err == nil && stat.IsDir() {
		assemblerWebDirPath = dockerWebroot
		projectDirectory = "/app" // Not used in Docker, but returned for compatibility
		return assemblerWebDirPath, projectDirectory
	}

	currentDirectory, _ := os.Getwd()
	projectDirectory = currentDirectory
	currentDirInfo := filepath.Base(currentDirectory)
	idxBin := strings.Index(currentDirectory, "bin")
	if idxBin > -1 {
		projectDirectory = currentDirectory[:idxBin]
	} else if strings.HasSuffix(currentDirInfo, "AssemblerTest") {
		// Already in AssemblerTest
	} else if strings.HasSuffix(currentDirInfo, "go") {
		projectDirectory = filepath.Join(currentDirectory, "AssemblerTest")
	} else if strings.HasPrefix(currentDirInfo, "AssemblerWeb") {
		// Already in AssemblerWeb
	} else if strings.HasPrefix(currentDirInfo, "Arshu.Assembler") {
		projectDirectory = filepath.Join(currentDirectory, "go", "AssemblerTest")
	}
	assemblerWebDirPath = ""
	if projectDirectory != "" {
		parent := filepath.Dir(projectDirectory)
		webDirPath := filepath.Join(parent, "AssemblerWeb", "wwwroot")
		if stat, err := os.Stat(webDirPath); err == nil && stat.IsDir() {
			assemblerWebDirPath = webDirPath
		}
	}
	return assemblerWebDirPath, projectDirectory
}

// IsAlphaNumeric checks if a string contains only alphanumeric characters
func IsAlphaNumeric(str string) bool {
	if str == "" {
		return false
	}
	for _, c := range str {
		if !unicode.IsLetter(c) && !unicode.IsDigit(c) {
			return false
		}
	}
	return true
}

// FindMatchingCloseTag finds the matching closing tag with proper nesting support
func FindMatchingCloseTag(content string, startPos int, openTag, closeTag string) (int, bool) {
	searchPos := startPos
	openCount := 1
	contentLen := len(content)
	for searchPos < contentLen && openCount > 0 {
		nextOpen := strings.Index(content[searchPos:], openTag)
		nextClose := strings.Index(content[searchPos:], closeTag)
		if nextClose == -1 {
			return -1, false
		}
		nextClose += searchPos
		if nextOpen != -1 {
			nextOpen += searchPos
			if nextOpen < nextClose {
				openCount++
				searchPos = nextOpen + len(openTag)
				continue
			}
		}
		openCount--
		searchPos = nextClose + len(closeTag)
	}
	if openCount == 0 {
		return searchPos - len(closeTag), true
	}
	return -1, false
}

// RemoveRemainingSlotPlaceholders removes remaining slot placeholders from HTML content
func RemoveRemainingSlotPlaceholders(html string) string {
	result := html
	searchPos := 0

	for searchPos < len(result) {
		placeholderStart := strings.Index(result[searchPos:], "{{$HTMLPLACEHOLDER")
		if placeholderStart == -1 {
			break
		}

		placeholderStart += searchPos
		afterPlaceholder := placeholderStart + 18
		pos := afterPlaceholder

		// Skip digits
		for pos < len(result) && result[pos] >= '0' && result[pos] <= '9' {
			pos++
		}

		if pos+1 < len(result) && result[pos:pos+2] == "}}" {
			placeholderEnd := pos + 2
			placeholder := result[placeholderStart:placeholderEnd]
			result = strings.Replace(result, placeholder, "", 1)
			// Don't advance search_pos since we removed content
		} else {
			searchPos = placeholderStart + 1
		}
	}

	return result
}

// ReplaceCaseInsensitive replaces the first occurrence of 'from' in 'text' (case-insensitive) with 'to'
func ReplaceCaseInsensitive(text, from, to string) string {
	textLower := strings.ToLower(text)
	fromLower := strings.ToLower(from)
	if idx := strings.Index(textLower, fromLower); idx != -1 {
		end := idx + len(from)
		return text[:idx] + to + text[end:]
	}
	return text
}

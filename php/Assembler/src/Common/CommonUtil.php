<?php

namespace Assembler\Common;

/**
 * Shared utility methods for template processing
 */
class CommonUtil
{
    /**
     * Get all HTML files in a directory (filenames without extension)
     * @param string $dirPath Directory to search
     * @return array List of HTML file names (without extension)
     */
    public static function getHtmlFiles(string $dirPath): array
    {
        $htmlFiles = [];
        if (!is_dir($dirPath)) {
            return $htmlFiles;
        }
        $files = scandir($dirPath);
        foreach ($files as $file) {
            if ($file === '.' || $file === '..') continue;
            $filePath = $dirPath . DIRECTORY_SEPARATOR . $file;
            if (is_file($filePath) && strtolower(pathinfo($file, PATHINFO_EXTENSION)) === 'html') {
                $htmlFiles[] = pathinfo($file, PATHINFO_FILENAME);
            }
        }
        sort($htmlFiles);
        return $htmlFiles;
    }

    /**
     * Get the path to the AssemblerWeb wwwroot directory and the project directory
     * @return array Array containing 'assemblerWebDirPath' and 'projectDirectory'
     */
    public static function getAssemblerWebDirPath(): array
    {
        // Docker/Fly.io: /app/AssemblerWeb/wwwroot
        $dockerWebroot = '/app/AssemblerWeb/wwwroot';
        if (is_dir($dockerWebroot)) {
            $assemblerWebDirPath = realpath($dockerWebroot);
            $projectDirectory = '/app';
            return [
                'assemblerWebDirPath' => $assemblerWebDirPath,
                'projectDirectory' => $projectDirectory
            ];
        }

        // Get current directory and determine project directory dynamically
        $currentDirectory = getcwd();
        $projectDirectory = $currentDirectory;

        // Check if we're in vendor directory (typical for Composer autoload)
        $vendorPos = strpos($currentDirectory, 'vendor');
        if ($vendorPos !== false) {
            // Extract path up to but not including vendor
            $projectDirectory = substr($currentDirectory, 0, $vendorPos);
        }
        // Check if current directory ends with AssemblerTest or AssemblerWeb
        else if (basename($currentDirectory) === 'AssemblerTest' || basename($currentDirectory) === 'AssemblerWeb') {
            $projectDirectory = $currentDirectory;
        }
        // Check if current directory is php
        else if (basename($currentDirectory) === 'php') {
            $projectDirectory = $currentDirectory . DIRECTORY_SEPARATOR . 'AssemblerTest';
        }
        // Check if current directory starts with Arshu.Assembler
        else if (strpos(basename($currentDirectory), 'Arshu.Assembler') === 0) {
            $projectDirectory = $currentDirectory . DIRECTORY_SEPARATOR . 'php' . DIRECTORY_SEPARATOR . 'AssemblerTest';
        }

        // Determine assemblerWebDirPath from project directory
        $assemblerWebDirPath = '';
        if (!empty($projectDirectory)) {
            $parentDir = dirname($projectDirectory);
            $webDirPath = $parentDir . DIRECTORY_SEPARATOR . 'AssemblerWeb' . DIRECTORY_SEPARATOR . 'wwwroot';
            if (is_dir($webDirPath)) {
                $assemblerWebDirPath = realpath($webDirPath);
            }
        }

        return [
            'assemblerWebDirPath' => $assemblerWebDirPath,
            'projectDirectory' => $projectDirectory
        ];
    }

    /**
     * Check if string contains only alphanumeric characters
     * @param string $str The string to check
     * @return bool True if string contains only alphanumeric characters
     */
    public static function isAlphaNumeric(string $str): bool
    {
        return !empty($str) && ctype_alnum($str);
    }

    /**
     * Find matching closing tag with proper nesting support
     * @param string $content The content to search in
     * @param int $startPos Starting position to search from
     * @param string $openTag The opening tag to match
     * @param string $closeTag The closing tag to find
     * @return int Position of matching close tag, or -1 if not found
     */
    public static function findMatchingCloseTag(string $content, int $startPos, string $openTag, string $closeTag): int
    {
        $searchPos = $startPos;
        $openCount = 1;

        while ($searchPos < strlen($content) && $openCount > 0) {
            $nextOpen = strpos($content, $openTag, $searchPos);
            $nextClose = strpos($content, $closeTag, $searchPos);

            if ($nextClose === false) return -1;

            if ($nextOpen !== false && $nextOpen < $nextClose) {
                $openCount++;
                $searchPos = $nextOpen + strlen($openTag);
            } else {
                $openCount--;
                if ($openCount === 0) {
                    return $nextClose;
                }
                $searchPos = $nextClose + strlen($closeTag);
            }
        }

        return -1;
    }

    /**
     * Remove remaining slot placeholders from HTML content
     * @param string $html The HTML content to process
     * @return string HTML with slot placeholders removed
     */
    public static function removeRemainingSlotPlaceholders(string $html): string
    {
        $result = $html;
        $searchPos = 0;

        while ($searchPos < strlen($result)) {
            $placeholderStart = strpos($result, '{{$HTMLPLACEHOLDER', $searchPos);
            if ($placeholderStart === false) break;

            $afterPlaceholder = $placeholderStart + 18;
            $pos = $afterPlaceholder;

            // Skip digits
            while ($pos < strlen($result) && is_numeric($result[$pos])) {
                $pos++;
            }

            // Check for closing }}
            if ($pos + 1 < strlen($result) && substr($result, $pos, 2) === '}}') {
                $placeholderEnd = $pos + 2;
                $placeholder = substr($result, $placeholderStart, $placeholderEnd - $placeholderStart);
                $result = str_replace($placeholder, '', $result);
                // Don't advance searchPos since we removed content
            } else {
                $searchPos = $placeholderStart + 1;
            }
        }

        return $result;
    }

    /**
     * Replaces the first occurrence of 'from' in 'text' (case-insensitive) with 'to'
     * @param string $text Text to search in
     * @param string $from Text to search for
     * @param string $to Replacement text
     * @return string Modified text
     */
    public static function replaceCaseInsensitive(string $text, string $from, string $to): string
    {
        if (empty($text) || empty($from)) {
            return $text;
        }

        $index = stripos($text, $from);
        if ($index !== false) {
            return substr($text, 0, $index) . $to . substr($text, $index + strlen($from));
        }
        return $text;
    }

    /**
     * Normalizes file content by removing UTF-8 BOM and normalizing line endings to LF (\n)
     * @param string $content The content to process
     * @return string Content with BOM removed and line endings normalized
     */
    public static function normalizeFileContent(string $content): string
    {
        if (empty($content)) {
            return $content;
        }

        // Remove UTF-8 BOM is '\xEF\xBB\xBF' (3 bytes)
        if (strlen($content) >= 3 && substr($content, 0, 3) === "\xEF\xBB\xBF") {
            $content = substr($content, 3);
        } else if (strlen($content) >= 3 && substr($content, 0, 3) === "\u{FEFF}") {
            // Also handle if BOM is present as UTF-8 character U+FEFF
            $content = substr($content, 3);
        }

        // Normalize line endings to LF (\n)
        $content = str_replace("\r\n", "\n", $content);
        $content = str_replace("\r", "\n", $content);

        return $content;
    }

    /**
     * Count UTF-16 code units (same as C# string.Length)
     * This is for test reporting only to match C#'s character counting
     * @param string $str The string to count
     * @return int Number of UTF-16 code units
     */
    public static function utf16Len(string $str): int
    {
        $count = 0;
        $len = mb_strlen($str, 'UTF-8');
        for ($i = 0; $i < $len; $i++) {
            $char = mb_substr($str, $i, 1, 'UTF-8');
            $codePoint = mb_ord($char, 'UTF-8');
            if ($codePoint <= 0xFFFF) {
                $count++; // BMP character = 1 UTF-16 code unit
            } else {
                $count += 2; // Supplementary character = 2 UTF-16 code units (surrogate pair)
            }
        }
        return $count;
    }
}
?>
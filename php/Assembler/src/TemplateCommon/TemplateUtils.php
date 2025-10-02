<?php

namespace Assembler\TemplateCommon;

/**
 * Shared utility methods for template processing
 */
class TemplateUtils
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

        // Local: resolve from workspace root
        $workspaceRoot = realpath(__DIR__ . '/../../../..');
        $assemblerWebDirPath = $workspaceRoot . DIRECTORY_SEPARATOR . 'php' . DIRECTORY_SEPARATOR . 'AssemblerWeb' . DIRECTORY_SEPARATOR . 'wwwroot';
        if (!is_dir($assemblerWebDirPath)) {
            // Fallback: workspaceRoot/AssemblerWeb/wwwroot
            $assemblerWebDirPath = $workspaceRoot . DIRECTORY_SEPARATOR . 'AssemblerWeb' . DIRECTORY_SEPARATOR . 'wwwroot';
        }
        $assemblerWebDirPath = realpath($assemblerWebDirPath);
        $projectDirectory = $workspaceRoot . DIRECTORY_SEPARATOR . 'php' . DIRECTORY_SEPARATOR . 'AssemblerTest';
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
}
?>
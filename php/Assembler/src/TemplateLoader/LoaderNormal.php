<?php

namespace Assembler\TemplateLoader;

use Assembler\TemplateCommon\Logger;

/**
 * Template result structure
 */
class TemplateResult
{
    public string $html;
    public ?string $json;

    public function __construct(string $html, ?string $json = null)
    {
        $this->html = $html;
        $this->json = $json;
    }
}

/**
 * Handles loading and caching of HTML templates from the file system
 */
class LoaderNormal
{
    private static array $htmlTemplatesCache = [];

    /**
     * Loads HTML files and corresponding JSON files from the specified application site directory, caching the output per appSite
     * @param string $rootDirPath Root directory path
     * @param string $appSite Application site name
     * @return array<string, TemplateResult> Array of templates
     */
    public static function loadGetTemplateFiles(string $rootDirPath, string $appSite): array
    {
        Logger::debug("LoadGetTemplateFiles called for appSite: $appSite", 'LoaderNormal');

        $cacheKey = dirname($rootDirPath) . '|' . $appSite;

        if (isset(self::$htmlTemplatesCache[$cacheKey])) {
            $cached = self::$htmlTemplatesCache[$cacheKey];
            Logger::debug("Returning cached templates for $appSite (" . count($cached) . " templates)", 'LoaderNormal');
            return $cached;
        }

        $result = [];
        $appSitesPath = $rootDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $appSite;

        if (!is_dir($appSitesPath)) {
            Logger::warn("AppSites directory not found: $appSitesPath", 'LoaderNormal');
            self::$htmlTemplatesCache[$cacheKey] = $result;
            return $result;
        }

        Logger::debug("Loading templates from: $appSitesPath", 'LoaderNormal');

        // Recursively find all HTML files
        $iterator = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator($appSitesPath, \RecursiveDirectoryIterator::SKIP_DOTS),
            \RecursiveIteratorIterator::LEAVES_ONLY
        );

        foreach ($iterator as $file) {
            if ($file->isFile() && $file->getExtension() === 'html') {
                $fileName = $file->getBasename('.html');
                $key = strtolower($appSite) . '_' . strtolower($fileName);

                $htmlContent = file_get_contents($file->getPathname());
                Logger::debug("Loading template: $key (html size: " . strlen($htmlContent ?: '') . ")", 'LoaderNormal');

                // Find JSON file case-insensitively
                $jsonFile = substr($file->getPathname(), 0, -5) . '.json'; // Replace .html with .json
                $jsonContent = null;

                if (file_exists($jsonFile)) {
                    $jsonContent = file_get_contents($jsonFile);
                    Logger::debug("Found JSON file for $key (size: " . strlen($jsonContent ?: '') . ")", 'LoaderNormal');
                } else {
                    // Try case-insensitive search in the same directory
                    $dir = dirname($file->getPathname());
                    $baseName = strtolower($file->getBasename('.html'));
                    $files = scandir($dir);

                    if ($files !== false) {
                        foreach ($files as $entry) {
                            if ($entry !== '.' && $entry !== '..' && is_file($dir . DIRECTORY_SEPARATOR . $entry)) {
                                if (strtolower(pathinfo($entry, PATHINFO_EXTENSION)) === 'json') {
                                    $entryBase = strtolower(pathinfo($entry, PATHINFO_FILENAME));
                                    if ($entryBase === $baseName) {
                                        $matchedJsonPath = $dir . DIRECTORY_SEPARATOR . $entry;
                                        $jsonContent = file_get_contents($matchedJsonPath);
                                        Logger::debug("Found JSON file (case-insensitive) for $key (size: " . strlen($jsonContent ?: '') . ")", 'LoaderNormal');
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                $result[$key] = new TemplateResult($htmlContent ?: '', $jsonContent ?: null);
            }
        }

        Logger::info("Loaded " . count($result) . " templates for $appSite", 'LoaderNormal');

        self::$htmlTemplatesCache[$cacheKey] = $result;
        return $result;
    }

    /**
     * Clear all cached templates (useful for testing or when templates change)
     */
    public static function clearCache(): void
    {
        self::$htmlTemplatesCache = [];
    }
}
?>
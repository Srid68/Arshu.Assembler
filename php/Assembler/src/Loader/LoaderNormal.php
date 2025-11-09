<?php

namespace Assembler\Loader;

use Arshu\Common\Logger;
use Assembler\Common\CommonUtil;

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
    public static function loadGetTemplateFiles(string $rootDirPath, string $appSite, string $searchAppSites = ""): array
    {
        Logger::debug("LoadGetTemplateFiles called for appSite: $appSite, searchAppSites: $searchAppSites", 'LoaderNormal');

        $cacheKey = dirname($rootDirPath) . '|' . $appSite . '|' . $searchAppSites;

        if (isset(self::$htmlTemplatesCache[$cacheKey])) {
            $cached = self::$htmlTemplatesCache[$cacheKey];
            Logger::debug("Returning cached templates for $appSite (" . count($cached) . " templates)", 'LoaderNormal');
            return $cached;
        }

        // Load templates from primary appSite
        $result = self::loadTemplatesFromSingleAppSite($rootDirPath, $appSite);

        // Load templates from searchAppSites for fallback
        if (!empty($searchAppSites)) {
            $searchAppSitesArray = explode(',', $searchAppSites);
            foreach ($searchAppSitesArray as $searchAppSiteRaw) {
                $searchAppSite = trim($searchAppSiteRaw);
                if (empty($searchAppSite)) continue;
                $searchTemplates = self::loadTemplatesFromSingleAppSite($rootDirPath, $searchAppSite);
                foreach ($searchTemplates as $k => $v) {
                    if (!isset($result[$k])) {
                        $result[$k] = $v;
                        Logger::debug("Added fallback template '$k' from '$searchAppSite'", 'LoaderNormal');
                    }
                }
            }
        }

        self::$htmlTemplatesCache[$cacheKey] = $result;
        return $result;
    }

    // Helper to load templates from a single AppSite (no caching/fallback)
    private static function loadTemplatesFromSingleAppSite(string $rootDirPath, string $appSite): array
    {
        $result = [];
        $appSitesPath = $rootDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $appSite;
        if (!is_dir($appSitesPath)) {
            Logger::warn("AppSites directory not found: $appSitesPath", 'LoaderNormal');
            return $result;
        }
        Logger::debug("Loading templates from: $appSitesPath", 'LoaderNormal');
        $iterator = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator($appSitesPath, \RecursiveDirectoryIterator::SKIP_DOTS),
            \RecursiveIteratorIterator::LEAVES_ONLY
        );
        foreach ($iterator as $file) {
            if ($file->isFile() && $file->getExtension() === 'html') {
                $fileName = $file->getBasename('.html');
                $key = strtolower($appSite) . '_' . strtolower($fileName);
                $htmlContent = CommonUtil::normalizeFileContent(file_get_contents($file->getPathname()));
                Logger::debug("Loading template: $key (html size: " . strlen($htmlContent ?: '') . ")", 'LoaderNormal');
                // Find JSON file case-insensitively
                $jsonFile = substr($file->getPathname(), 0, -5) . '.json';
                $jsonContent = null;
                if (file_exists($jsonFile)) {
                    $jsonContent = CommonUtil::normalizeFileContent(file_get_contents($jsonFile));
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
                                        $jsonContent = CommonUtil::normalizeFileContent(file_get_contents($matchedJsonPath));
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
        Logger::debug("Loaded " . count($result) . " templates for $appSite", 'LoaderNormal');
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
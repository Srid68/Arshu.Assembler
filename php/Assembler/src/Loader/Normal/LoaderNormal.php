<?php

namespace Assembler\Loader\Normal;

use Assembler\Loader\TemplateResult;

use Arshu\Common\Logger;
use Assembler\Common\CommonUtil;

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
     * @param string $searchAppSites Comma-delimited string of AppSite names to search for fallback templates (can be empty string)
     * @return array<string, array{html: string, json: ?string}> Array of templates
     */
    public static function loadGetTemplateFiles(string $rootDirPath, string $appSite, string $searchAppSites = ""): array
    {
        Logger::debug("LoadGetTemplateFiles called for appSite: {$appSite}, searchAppSites: {$searchAppSites}", 'LoaderNormal');

        $cacheKey = dirname($rootDirPath) . '|' . $appSite . '|' . $searchAppSites;

        if (isset(self::$htmlTemplatesCache[$cacheKey])) {
            $cached = self::$htmlTemplatesCache[$cacheKey];
            Logger::debug("Returning cached templates for {$appSite} (" . count($cached) . " templates)", 'LoaderNormal');
            return $cached;
        }

        // Load templates from primary appSite
        $result = self::loadTemplatesFromSingleAppSite($rootDirPath, $appSite);

        // Load templates from searchAppSites for fallback
        if (!empty($searchAppSites)) {
            $searchAppSitesArray = explode(',', $searchAppSites);
            foreach ($searchAppSitesArray as $searchAppSiteRaw) {
                $searchAppSite = trim($searchAppSiteRaw);
                if (empty($searchAppSite)) {
                    continue;
                }
                $searchTemplates = self::loadTemplatesFromSingleAppSite($rootDirPath, $searchAppSite);
                foreach ($searchTemplates as $k => $v) {
                    // Only add if not already present (primary appSite takes precedence)
                    if (!isset($result[$k])) {
                        $result[$k] = $v;
                        Logger::debug("Added fallback template '{$k}' from '{$searchAppSite}'", 'LoaderNormal');
                    }
                }
            }
        }

        self::$htmlTemplatesCache[$cacheKey] = $result;
        return $result;
    }

    /// <summary>
    /// Loads templates from a single AppSite without caching or fallback logic
    /// </summary>
    private static function loadTemplatesFromSingleAppSite(string $rootDirPath, string $appSite): array
    {
        $result = [];
        $appSitesPath = $rootDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $appSite;

        if (!is_dir($appSitesPath)) {
            Logger::warn("AppSites directory not found: {$appSitesPath}", 'LoaderNormal');
            return $result;
        }

        Logger::debug("Loading templates from: {$appSitesPath}", 'LoaderNormal');

        $iterator = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator($appSitesPath, \RecursiveDirectoryIterator::SKIP_DOTS),
            \RecursiveIteratorIterator::LEAVES_ONLY
        );

        foreach ($iterator as $file) {
            if ($file->isFile() && $file->getExtension() === 'html') {
                $fileName = $file->getBasename('.html');
                $key = strtolower("{$appSite}_{$fileName}");
                $htmlContent = CommonUtil::normalizeFileContent(file_get_contents($file->getPathname()));

                Logger::debug("Loading template: {$key} (html size: " . strlen($htmlContent) . ")", 'LoaderNormal');

                // Find JSON file case-insensitively
                $jsonFile = substr($file->getPathname(), 0, -5) . '.json';
                $jsonContent = null;

                // Try exact match first
                if (file_exists($jsonFile)) {
                    $jsonContent = CommonUtil::normalizeFileContent(file_get_contents($jsonFile));
                    Logger::debug("Found JSON file for {$key} (size: " . strlen($jsonContent) . ")", 'LoaderNormal');
                } else {
                    // Try case-insensitive search in the same directory
                    $directory = dirname($file->getPathname());
                    $baseFileName = pathinfo($file->getBasename(), PATHINFO_FILENAME);
                    if (!empty($directory)) {
                        $jsonFiles = glob($directory . DIRECTORY_SEPARATOR . '*.json');
                        $matchingJson = null;
                        foreach ($jsonFiles as $jsonFilePath) {
                            if (strcasecmp(pathinfo($jsonFilePath, PATHINFO_FILENAME), $baseFileName) === 0) {
                                $matchingJson = $jsonFilePath;
                                break;
                            }
                        }

                        if ($matchingJson !== null) {
                            $jsonContent = CommonUtil::normalizeFileContent(file_get_contents($matchingJson));
                            Logger::debug("Found JSON file (case-insensitive) for {$key} (size: " . strlen($jsonContent) . ")", 'LoaderNormal');
                        }
                    }
                }
                $result[$key] = new TemplateResult($htmlContent, $jsonContent);
            }
        }

        Logger::debug("Loaded " . count($result) . " templates for {$appSite}", 'LoaderNormal');
        return $result;
    }

    /// <summary>
    /// Clear all cached templates (useful for testing or when templates change)
    /// </summary>
    public static function clearCache(): void
    {
        self::$htmlTemplatesCache = [];
    }
}

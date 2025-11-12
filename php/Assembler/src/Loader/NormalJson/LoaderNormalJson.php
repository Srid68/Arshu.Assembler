<?php

namespace Assembler\Loader\NormalJson;

use Assembler\Interface\ILoaderJson;

use Assembler\App\Json\JsonObject;
use Arshu\Common\Logger;
use Assembler\Common\CommonUtil;
use Assembler\App\JsonConverter;
use Exception;
use Assembler\Engine\JsonInheritanceUtil; // Import the new utility

/// <summary>
/// Loader that implements ILoaderJson for Normal engine
/// Loads templates with JsonObject for type safety
/// </summary>
class LoaderNormalJson implements ILoaderJson
{
    private static array $_htmlTemplatesCache = [];
    private array $_templates;
    private array $_parentMap;
    private string $_appSite;

    /// <summary>
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    /// </summary>
    public string $searchAppSites;

    /// <summary>
    /// Creates a new loader instance by loading templates from the specified root directory
    /// </summary>
    /// <param name="rootDirPath">Root directory path containing AppSites folder</param>
    /// <param name="appSites">Primary AppSite name to load</param>
    /// <param name="searchAppSites">Comma-delimited string of AppSite names to search for fallback templates (can be empty string)</param>
    public function __construct(string $rootDirPath, string $appSites, string $searchAppSites)
    {
        $this->searchAppSites = $searchAppSites;
        $this->_appSite = $appSites;

        // Load templates from primary appSite
        $this->_templates = self::loadGetTemplateFiles($rootDirPath, $appSites);

        // Load templates from searchAppSites for fallback
        if (!empty($this->searchAppSites)) {
            $searchAppSitesArray = explode(',', $this->searchAppSites);
            foreach ($searchAppSitesArray as $searchAppSiteRaw) {
                $searchAppSite = trim($searchAppSiteRaw);
                if (empty($searchAppSite)) {
                    continue;
                }

                $searchTemplates = self::loadGetTemplateFiles($rootDirPath, $searchAppSite);
                foreach ($searchTemplates as $key => $value) {
                    // Only add if not already present (primary appSite takes precedence)
                    if (!isset($this->_templates[$key])) {
                        $this->_templates[$key] = $value;
                    }
                }
            }
        }

        // Build parent-child relationship map for JSON inheritance
        $this->_parentMap = JsonInheritanceUtil::buildParentMap($this->_appSite, $this->_templates);
        Logger::debug("Built parent map with " . count($this->_parentMap) . " relationships for JSON inheritance", "LoaderNormalJson");
    }

    /// <summary>
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    /// </summary>
    public function getSearchAppSites(): string
    {
        return $this->searchAppSites;
    }

    /// <summary>
    /// Gets a template's HTML content by appSite and name with optional AppView fallback
    /// Returns raw HTML only (no JSON merged)
    /// Searches in SearchAppSites if not found in primary appSite
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (e.g., "Header", "Index")</param>
    /// <param name="appView">Optional AppView for fallback logic</param>
    /// <param name="appViewPrefix">Optional AppView prefix for fallback logic</param>
    /// <returns>Template HTML content or null if not found</returns>
    public function getTemplateHtml(string $appSite, string $templateName, ?string $appView = null, ?string $appViewPrefix = null): mixed
    {
        $template = $this->getTemplateInternal($appSite, $templateName, $appView, $appViewPrefix);
        return $template['html'] ?? null;
    }

    /// <summary>
    /// Gets parsed JSON data for a template
    /// Returns null if no JSON file exists for the template
    /// Searches in SearchAppSites if not found in primary appSite
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name</param>
    /// <returns>Parsed JsonObject or null if no JSON file exists</returns>
    public function getTemplateJson(string $appSite, string $templateName): ?JsonObject
    {
        $template = $this->getTemplateInternal($appSite, $templateName, null, null);
        return $template['json'] ?? null;
    }

    /// <summary>
    /// Merges HTML string with JSON data using inheritance-aware JSON retrieval
    /// This centralizes JSON merging logic in the loader for clean architecture
    /// The loader internally retrieves JSON (with inheritance resolution) and merges it with HTML
    /// Note: This method always works with string HTML, independent of TTemplate type
    /// </summary>
    /// <param name="html">The HTML string content to merge</param>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (for retrieving JSON data)</param>
    /// <returns>HTML string with JSON data merged, or original HTML if no JSON exists</returns>
    public function mergeHtmlWithJson(string $html, string $appSite, string $templateName): string
    {
        if (empty($html)) {
            return $html;
        }

        // Get JSON with inheritance resolution
        $jsonData = $this->getTemplateJsonWithInheritance($appSite, $templateName);

        if ($jsonData === null) {
            Logger::debug("No JSON data found for {$templateName}, returning original HTML", "LoaderNormalJson");
            return $html;
        }

        Logger::debug("Merging HTML with JSON for {$templateName}", "LoaderNormalJson");
        return \Assembler\Engine\JsonMergeUtil::mergeTemplateWithJson($html, $jsonData);
    }

    /// <summary>
    /// Checks if a template exists
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name</param>
    /// <returns>True if template exists, false otherwise</returns>
    public function hasTemplate(string $appSite, string $templateName): bool
    {
        $key = strtolower("{$appSite}_{$templateName}");
        return isset($this->_templates[$key]);
    }

    /// <summary>
    /// Gets all templates as a serialized JSON string
    /// For LoaderNormalJson, this is not typically used, but required by interface
    /// </summary>
    public function getAllTemplatesJson(): string
    {
        // Not implemented for normal loader
        return '{}';
    }

    /// <summary>
    /// Clears the template cache (for testing/hot reload)
    /// </summary>
    public static function clearCache(): void
    {
        self::$_htmlTemplatesCache = [];
    }

    /// <summary>
    /// Applies all replacement mappings from all templates to the given content
    /// For LoaderNormalJson, just returns content as-is since this is only relevant for PreProcess loaders
    /// </summary>
    public function applyAllReplacementMappings(string $content, string $appSite, mixed $mainTemplate, ?string $appView, ?string $appViewPrefix, bool $enableJsonProcessing): string
    {
        // Not applicable for normal loader - just return content unchanged
        return $content;
    }

    /// <summary>
    /// Internal helper method with AppView fallback logic and SearchAppSites support
    /// This implements the template resolution strategy used by the engines
    /// </summary>
    private function getTemplateInternal(string $appSite, string $templateName, ?string $appView, ?string $appViewPrefix): ?array
    {
        // Try AppView fallback first if provided
        if (!empty($appView) && !empty($appViewPrefix) &&
            stripos($templateName, $appViewPrefix) !== false)
        {
            $appKey = CommonUtil::replaceCaseInsensitive($templateName, $appViewPrefix, $appView);
            $fallbackKey = strtolower("{$appSite}_{$appKey}");

            if (isset($this->_templates[$fallbackKey])) {
                return $this->_templates[$fallbackKey];
            }
        }

        // Try primary template key
        $primaryKey = strtolower("{$appSite}_{$templateName}");
        if (isset($this->_templates[$primaryKey])) {
            return $this->_templates[$primaryKey];
        }

        // Search in SearchAppSites as fallback
        if (!empty($this->searchAppSites)) {
            $searchAppSitesArray = explode(',', $this->searchAppSites);
            foreach ($searchAppSitesArray as $searchAppSiteRaw) {
                $searchAppSite = trim($searchAppSiteRaw);
                if (empty($searchAppSite)) {
                    continue;
                }

                $searchKey = strtolower("{$searchAppSite}_{$templateName}");
                if (isset($this->_templates[$searchKey])) {
                    Logger::debug("Template '{$templateName}' not found in '{$appSite}', using fallback from '{$searchAppSite}'", "LoaderNormalJson");
                    return $this->_templates[$searchKey];
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Loads HTML files and corresponding JSON files from a single application site
    /// JSON is parsed to JsonObject immediately for type safety
    /// </summary>
    private static function loadGetTemplateFiles(string $rootDirPath, string $appSite): array
    {
        Logger::debug("LoadGetTemplateFiles called for appSite: {$appSite}", "LoaderNormalJson");

        $cacheKey = dirname($rootDirPath) . '|' . $appSite;
        if (isset(self::$_htmlTemplatesCache[$cacheKey])) {
            $cached = self::$_htmlTemplatesCache[$cacheKey];
            Logger::debug("Returning cached templates for {$appSite} (" . count($cached) . " templates)", "LoaderNormalJson");
            return $cached;
        }

        $result = [];
        $appSitesPath = $rootDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $appSite;

        if (!is_dir($appSitesPath)) {
            Logger::warn("AppSites directory not found: {$appSitesPath}", "LoaderNormalJson");
            self::$_htmlTemplatesCache[$cacheKey] = $result;
            return $result;
        }

        Logger::debug("Loading templates from: {$appSitesPath}", "LoaderNormalJson");

        $iterator = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator($appSitesPath, \RecursiveDirectoryIterator::SKIP_DOTS),
            \RecursiveIteratorIterator::LEAVES_ONLY
        );

        foreach ($iterator as $file) {
            if ($file->isFile() && $file->getExtension() === 'html') {
                $fileName = $file->getBasename('.html');
                $key = strtolower("{$appSite}_{$fileName}");
                $htmlContent = CommonUtil::normalizeFileContent(file_get_contents($file->getPathname()));

                Logger::debug("Loading template: {$key} (html size: " . strlen($htmlContent) . ")", "LoaderNormalJson");

                // Find and parse JSON file to JsonObject
                $jsonFile = substr($file->getPathname(), 0, -5) . '.json';
                $jsonObject = null;

                // Try exact match first
                if (file_exists($jsonFile)) {
                    $jsonContent = CommonUtil::normalizeFileContent(file_get_contents($jsonFile));
                    if (!empty($jsonContent)) {
                        $jsonObject = JsonConverter::parseJsonString($jsonContent);
                        Logger::debug("Found and parsed JSON file for {$key} (size: " . strlen($jsonContent) . ")", "LoaderNormalJson");
                    }
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
                            if (!empty($jsonContent)) {
                                $jsonObject = JsonConverter::parseJsonString($jsonContent);
                                Logger::debug("Found and parsed JSON file (case-insensitive) for {$key} (size: " . strlen($jsonContent) . ")", "LoaderNormalJson");
                            }
                        }
                    }
                }

                $result[$key] = ['html' => $htmlContent, 'json' => $jsonObject];
            }
        }

        Logger::debug("Loaded " . count($result) . " templates for {$appSite}", "LoaderNormalJson");

        self::$_htmlTemplatesCache[$cacheKey] = $result;
        return $result;
    }

    /// <summary>
    /// Gets parsed JSON with inheritance resolution
    /// Resolves keys ending with # by searching up the parent tree
    /// </summary>
    public function getTemplateJsonWithInheritance(string $appSite, string $templateName): ?JsonObject
    {
        $templateKey = strtolower("{$appSite}_{$templateName}");
        $template = $this->getTemplateInternal($appSite, $templateName, null, null);
        if (!isset($template['json']) || $template['json'] === null) {
            return null;
        }

        $jsonObj = $template['json'];
        $resolvedJson = new JsonObject();

        // Process each JSON key and resolve inheritance
        foreach ($jsonObj as $key => $value) {
            // Check if this is an inheritable key (ends with #)
            if (str_ends_with($key, '#') && is_string($value)) {
                // Resolve inherited value
                $actualKey = substr($key, 0, -1);
                $resolvedValue = JsonInheritanceUtil::resolveJsonKeyWithInheritance(
                    $key, // Pass original key with # for logging
                    $value,
                    $templateKey,
                    $this->_templates,
                    $this->_parentMap
                );
                if ($resolvedValue !== null) {
                    $resolvedJson->setValue($actualKey, $resolvedValue);
                    Logger::debug("Resolved inherited key {$key} -> {$actualKey} = {$resolvedValue}", "LoaderNormalJson");
                    continue;
                }
            }

            // Normal key - keep as is
            $resolvedJson->setValue($key, $value);
        }

        return $resolvedJson;
    }
}

<?php

namespace Assembler\TemplateLoader;

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
        $cacheKey = dirname($rootDirPath) . '|' . $appSite;
        
        if (isset(self::$htmlTemplatesCache[$cacheKey])) {
            return self::$htmlTemplatesCache[$cacheKey];
        }

        $result = [];
        $appSitesPath = $rootDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $appSite;
        
        if (!is_dir($appSitesPath)) {
            self::$htmlTemplatesCache[$cacheKey] = $result;
            return $result;
        }

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
                $jsonFile = substr($file->getPathname(), 0, -5) . '.json'; // Replace .html with .json
                $jsonContent = null;
                
                if (file_exists($jsonFile)) {
                    $jsonContent = file_get_contents($jsonFile);
                }
                
                $result[$key] = new TemplateResult($htmlContent ?: '', $jsonContent ?: null);
            }
        }

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
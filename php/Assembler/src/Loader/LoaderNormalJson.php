<?php

namespace Assembler\Loader;

use Arshu\Common\Logger;
use Assembler\Common\CommonUtil;

class LoaderNormalJson
{
    private static array $htmlTemplatesCache = [];
    private array $templates;
    private string $searchAppSites;
    private string $rootDirPath;
    private string $appSites;

    public function __construct(string $rootDirPath, string $appSites, string $searchAppSites)
    {
        $this->searchAppSites = $searchAppSites;
        $this->templates = [];
        $this->rootDirPath = $rootDirPath;
        $this->appSites = $appSites;
        $this->load();
    }

    private function load(): void
    {
        $primaryTemplates = $this->loadGetTemplateFiles($this->rootDirPath, $this->appSites);
        $this->templates = $primaryTemplates;

        if (!empty($this->searchAppSites)) {
            $searchAppSitesArray = explode(',', $this->searchAppSites);
            foreach ($searchAppSitesArray as $searchAppSite) {
                $trimmedSite = trim($searchAppSite);
                if ($trimmedSite) {
                    $searchTemplates = $this->loadGetTemplateFiles($this->rootDirPath, $trimmedSite);
                    foreach ($searchTemplates as $key => $value) {
                        if (!isset($this->templates[$key])) {
                            $this->templates[$key] = $value;
                        }
                    }
                }
            }
        }
    }

    public function getTemplateHtml(string $appSite, string $templateName, ?string $appView = null, ?string $appViewPrefix = null): ?string
    {
        $template = $this->getTemplateInternal($appSite, $templateName, $appView, $appViewPrefix);
        return $template['html'] ?? null;
    }

    public function getTemplateJson(string $appSite, string $templateName): ?array
    {
        $template = $this->getTemplateInternal($appSite, $templateName, null, null);
        return $template['json'] ?? null;
    }

    public function hasTemplate(string $appSite, string $templateName): bool
    {
        $key = strtolower($appSite) . '_' . strtolower($templateName);
        return isset($this->templates[$key]);
    }

    public static function clearCache(): void
    {
        self::$htmlTemplatesCache = [];
    }

    private function getTemplateInternal(string $appSite, string $templateName, ?string $appView, ?string $appViewPrefix): ?array
    {
        if ($appView && $appViewPrefix && stripos($templateName, $appViewPrefix) !== false) {
            $appKey = CommonUtil::replaceCaseInsensitive($templateName, $appViewPrefix, $appView);
            $fallbackKey = strtolower($appSite) . '_' . strtolower($appKey);
            if (isset($this->templates[$fallbackKey])) {
                return $this->templates[$fallbackKey];
            }
        }

        $primaryKey = strtolower($appSite) . '_' . strtolower($templateName);
        if (isset($this->templates[$primaryKey])) {
            return $this->templates[$primaryKey];
        }

        if (!empty($this->searchAppSites)) {
            $searchAppSitesArray = explode(',', $this->searchAppSites);
            foreach ($searchAppSitesArray as $searchAppSite) {
                $trimmedSite = trim($searchAppSite);
                if ($trimmedSite) {
                    $searchKey = strtolower($trimmedSite) . '_' . strtolower($templateName);
                    if (isset($this->templates[$searchKey])) {
                        Logger::debug("Template '$templateName' not found in '$appSite', using fallback from '$trimmedSite'", 'LoaderNormalJson');
                        return $this->templates[$searchKey];
                    }
                }
            }
        }

        return null;
    }

    private function loadGetTemplateFiles(string $rootDirPath, string $appSite): array
    {
        Logger::debug("LoadGetTemplateFiles called for appSite: $appSite", 'LoaderNormalJson');
        $cacheKey = dirname($rootDirPath) . '|' . $appSite;
        if (isset(self::$htmlTemplatesCache[$cacheKey])) {
            $cached = self::$htmlTemplatesCache[$cacheKey];
            Logger::debug("Returning cached templates for $appSite (" . count($cached) . " templates)", 'LoaderNormalJson');
            return $cached;
        }

        $result = [];
        $appSitesPath = $rootDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $appSite;

        if (!is_dir($appSitesPath)) {
            Logger::warn("AppSites directory not found: $appSitesPath", 'LoaderNormalJson');
            self::$htmlTemplatesCache[$cacheKey] = $result;
            return $result;
        }

        Logger::debug("Loading templates from: $appSitesPath", 'LoaderNormalJson');
        $files = new \RecursiveIteratorIterator(new \RecursiveDirectoryIterator($appSitesPath));

        foreach ($files as $file) {
            if ($file->isFile() && $file->getExtension() == 'html') {
                $fileName = $file->getBasename('.html');
                $key = strtolower($appSite) . '_' . strtolower($fileName);
                $htmlContent = CommonUtil::normalizeFileContent(file_get_contents($file->getPathname()));
                Logger::debug("Loading template: $key (html size: " . strlen($htmlContent) . ")", 'LoaderNormalJson');

                $jsonFile = $file->getPath() . DIRECTORY_SEPARATOR . $fileName . '.json';
                $jsonObject = null;
                if (file_exists($jsonFile)) {
                    $jsonContent = CommonUtil::normalizeFileContent(file_get_contents($jsonFile));
                    if ($jsonContent) {
                        $jsonObject = json_decode($jsonContent, true);
                        Logger::debug("Found and parsed JSON file for $key (size: " . strlen($jsonContent) . ")", 'LoaderNormalJson');
                    }
                }
                $result[$key] = ['html' => $htmlContent, 'json' => $jsonObject];
            }
        }

        Logger::debug("Loaded " . count($result) . " templates for $appSite", 'LoaderNormalJson');
        self::$htmlTemplatesCache[$cacheKey] = $result;
        return $result;
    }
}
<?php

namespace Assembler\Config;

class Scenario
{
    public string $appSite;
    public string $appFile;
    public string $appView;

    public function __construct(string $appSite, string $appFile, string $appView)
    {
        $this->appSite = $appSite;
        $this->appFile = $appFile;
        $this->appView = $appView;
    }

    public function __toString(): string
    {
        return "{$this->appSite}:{$this->appFile}:{$this->appView}";
    }
}

class ConfigUtil
{
    public static string $defaultAppFile = 'Index';

    private static ?array $cachedAppSites = null;
    private static ?array $cachedScenarios = null;
    private static ?string $wwwrootPath = null;

    /**
     * Extracts unique AppSites from scenarios
     */
    private static function extractAppSitesFromScenarios(array $scenarios): array
    {
        $appSitesSet = [];
        foreach ($scenarios as $scenario) {
            if (!empty($scenario->appSite)) {
                $appSitesSet[strtolower($scenario->appSite)] = true;
            }
        }

        echo sprintf("[ConfigUtil] Extracted %d AppSites from folder scan\n", count($appSitesSet));

        return array_keys($appSitesSet);
    }

    /**
     * Discovers scenarios by scanning AppSites folder structure
     */
    private static function loadScenariosInternal(string $wwwrootPath): array
    {
        $appSitesPath = $wwwrootPath . DIRECTORY_SEPARATOR . 'AppSites';

        if (!is_dir($appSitesPath)) {
            throw new \Exception("AppSites directory not found: $appSitesPath");
        }

        $scenarios = [];

        // Get all directories in AppSites folder
        $entries = scandir($appSitesPath);
        $appSiteDirs = [];

        foreach ($entries as $entry) {
            if ($entry === '.' || $entry === '..') {
                continue;
            }
            $fullPath = $appSitesPath . DIRECTORY_SEPARATOR . $entry;
            if (is_dir($fullPath)) {
                $appSiteDirs[] = $entry;
            }
        }

        sort($appSiteDirs);

        foreach ($appSiteDirs as $appSite) {
            // Get all HTML files in the appSite directory (top level only)
            $appSiteDir = $appSitesPath . DIRECTORY_SEPARATOR . $appSite;
            $files = scandir($appSiteDir);

            $htmlFiles = [];
            foreach ($files as $file) {
                $filePath = $appSiteDir . DIRECTORY_SEPARATOR . $file;
                if (is_file($filePath) && str_ends_with($file, '.html')) {
                    $htmlFiles[] = $file;
                }
            }

            // If no HTML files found, use DEFAULT_APP_FILE
            if (empty($htmlFiles)) {
                $htmlFiles = [self::$defaultAppFile];
            }

            foreach ($htmlFiles as $htmlFile) {
                $appFile = $htmlFile === self::$defaultAppFile ? self::$defaultAppFile : pathinfo($htmlFile, PATHINFO_FILENAME);

                // Check for Views folder
                $viewsPath = $appSiteDir . DIRECTORY_SEPARATOR . 'Views';
                $viewDirs = [];

                if (is_dir($viewsPath)) {
                    // Get all subdirectories in Views folder
                    $viewEntries = scandir($viewsPath);
                    foreach ($viewEntries as $viewEntry) {
                        if ($viewEntry === '.' || $viewEntry === '..') {
                            continue;
                        }
                        $viewPath = $viewsPath . DIRECTORY_SEPARATOR . $viewEntry;
                        if (is_dir($viewPath)) {
                            $viewDirs[] = $viewEntry;
                        }
                    }
                }

                // Only add empty AppView scenario if no specific Views exist
                if (count($viewDirs) === 0) {
                    $scenarios[] = new Scenario($appSite, $appFile, '');
                } else {
                    // Add specific view scenarios
                    foreach ($viewDirs as $viewDir) {
                        $scenarios[] = new Scenario($appSite, $appFile, $viewDir);
                    }
                }
            }
        }

        if (count($scenarios) === 0) {
            throw new \Exception('No scenarios found in AppSites folder');
        }

        echo sprintf("[ConfigUtil] Loaded %d scenarios from AppSites folder\n", count($scenarios));

        return $scenarios;
    }

    /**
     * Loads AppSites from wwwroot path and caches them. Call this during startup.
     */
    public static function load(string $wwwrootPath): void
    {
        self::$wwwrootPath = $wwwrootPath;
        self::$cachedScenarios = self::loadScenariosInternal($wwwrootPath);
        self::$cachedAppSites = self::extractAppSitesFromScenarios(self::$cachedScenarios);
    }

    /**
     * Reloads AppSites and Scenarios from the stored wwwroot path. Throws if not loaded.
     */
    public static function reload(): void
    {
        if (self::$wwwrootPath === null) {
            throw new \Exception('ConfigUtil not loaded. Call load(wwwrootPath) first.');
        }

        self::$cachedScenarios = self::loadScenariosInternal(self::$wwwrootPath);
        self::$cachedAppSites = self::extractAppSitesFromScenarios(self::$cachedScenarios);
    }

    /**
     * Gets the cached AppSites. Throws if not loaded.
     */
    public static function getAppSites(): array
    {
        if (self::$cachedAppSites === null) {
            throw new \Exception('AppSitesConfig not loaded. Call load(wwwrootPath) first.');
        }

        return self::$cachedAppSites;
    }

    /**
     * Gets the cached Scenarios. Throws if not loaded.
     */
    public static function getScenarios(): array
    {
        if (self::$cachedScenarios === null) {
            throw new \Exception('AppSitesConfig not loaded. Call load(wwwrootPath) first.');
        }

        return self::$cachedScenarios;
    }

    /**
     * Filters scenarios by appSite
     */
    public static function filterByAppSite(array $scenarios, ?string $appSiteFilter): array
    {
        if (empty($appSiteFilter)) {
            return $scenarios;
        }

        return array_values(array_filter(
            $scenarios,
            fn($s) => strcasecmp($s->appSite, $appSiteFilter) === 0
        ));
    }
}

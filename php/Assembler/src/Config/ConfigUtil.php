<?php

namespace Assembler\Config;

class Scenario
{
    public string $appSite;
    public string $appFile;
    public string $appView;
    public int $totalSize;
    public string $displayName;
    public string $description;

    public function __construct(
        string $appSite,
        string $appFile,
        string $appView,
        int $totalSize = 0,
        string $displayName = '',
        string $description = ''
    ) {
        $this->appSite = $appSite;
        $this->appFile = $appFile;
        $this->appView = $appView;
        $this->totalSize = $totalSize;
        $this->displayName = $displayName;
        $this->description = $description;
    }

    public function __toString(): string
    {
        return "{$this->appSite}:{$this->appFile}:{$this->appView}:{$this->totalSize}";
    }

    public function toCsvLine(): string
    {
        return sprintf(
            '%s,%s,%s,%d,"%s","%s"',
            $this->appSite,
            $this->appFile,
            $this->appView,
            $this->totalSize,
            $this->displayName,
            $this->description
        );
    }
}

class ConfigUtil
{
    private static ?array $cachedAppSites = null;
    private static ?array $cachedScenarios = null;
    private static ?string $wwwrootPath = null;


    private static function extractAppSitesFromScenarios(array $scenarios): array
    {
        $appSites = [];
        foreach ($scenarios as $scenario) {
            if (!empty($scenario->appSite)) {
                $appSites[$scenario->appSite] = true;
            }
        }

        \Assembler\Common\Logger::info(sprintf("Extracted %d AppSites from scenarios.csv", count($appSites)), "ConfigUtil");

        return array_keys($appSites);
    }

    private static function calculateTotalTemplateSize(string $appSitesPath, string $appSite): int
    {
        $totalSize = 0;
        $appSiteDir = $appSitesPath . DIRECTORY_SEPARATOR . $appSite;

        try {
            $files = self::getAllFiles($appSiteDir);
            foreach ($files as $file) {
                if (str_ends_with($file, '.html') || str_ends_with($file, '.json')) {
                    $totalSize += filesize($file);
                }
            }
        } catch (\Exception $e) {
            return 0;
        }

        return $totalSize;
    }

    private static function getAllFiles(string $dir, array &$results = []): array
    {
        if (!is_dir($dir)) {
            return $results;
        }

        $files = scandir($dir);

        foreach ($files as $file) {
            if ($file === '.' || $file === '..') continue;

            $path = $dir . DIRECTORY_SEPARATOR . $file;

            if (is_dir($path)) {
                self::getAllFiles($path, $results);
            } else {
                $results[] = $path;
            }
        }

        return $results;
    }

    private static function generateDisplayName(string $appSite, string $appView): string
    {
        $rulePart = str_replace(['Html', 'Json'], '', $appSite);
        $displayName = '';

        if (str_starts_with($appSite, 'Html')) {
            $displayName = $rulePart . ' (HTML)';
        } elseif (str_starts_with($appSite, 'Json')) {
            $displayName = $rulePart . ' (JSON)';
        } else {
            $displayName = $appSite;
        }

        if (!empty($appView)) {
            $displayName .= " - AppView: $appView";
        }

        return $displayName;
    }

    private static function generateDescription(string $appSite, string $appView): string
    {
        $description = '';

        if (str_contains($appSite, 'Rule1')) {
            $description = 'Simple placeholder replacement';
        } elseif (str_contains($appSite, 'Rule2')) {
            $description = 'Slotted markup patterns';
        } elseif (str_contains($appSite, 'Rule3')) {
            $description = 'Context-based placeholders';
        }

        if (str_contains($appSite, 'Html') && str_contains($appSite, 'Json')) {
            $description .= ' with HTML and JSON';
        } elseif (str_contains($appSite, 'Json')) {
            $description .= ' with JSON data';
        }

        if (!empty($appView)) {
            $description .= " ($appView view)";
        }

        return $description;
    }

    private static function generateScenariosCsv(string $wwwrootPath): void
    {
        $appSitesPath = $wwwrootPath . DIRECTORY_SEPARATOR . 'AppSites';
        $appDataPath = $wwwrootPath . DIRECTORY_SEPARATOR . 'App_Data';
        $csvFilePath = $appDataPath . DIRECTORY_SEPARATOR . 'scenarios.csv';

        if (!is_dir($appSitesPath)) {
            throw new \Exception("AppSites directory not found: $appSitesPath");
        }

        // Ensure App_Data directory exists
        if (!is_dir($appDataPath)) {
            mkdir($appDataPath, 0755, true);
        }

        $scenarios = [];

        // Get all directories in AppSites folder
        $entries = scandir($appSitesPath);
        $appSiteDirs = [];

        foreach ($entries as $entry) {
            if ($entry === '.' || $entry === '..') continue;
            $fullPath = $appSitesPath . DIRECTORY_SEPARATOR . $entry;
            if (is_dir($fullPath)) {
                $appSiteDirs[] = $entry;
            }
        }

        sort($appSiteDirs);

        foreach ($appSiteDirs as $appSite) {
            $appSiteDir = $appSitesPath . DIRECTORY_SEPARATOR . $appSite;
            $files = scandir($appSiteDir);

            foreach ($files as $file) {
                if (!str_ends_with($file, '.html')) continue;
                if ($file === '.' || $file === '..') continue;

                $appFile = pathinfo($file, PATHINFO_FILENAME);

                // Calculate total size
                $totalSize = self::calculateTotalTemplateSize($appSitesPath, $appSite);

                // Generate display name and description
                $displayName = self::generateDisplayName($appSite, '');
                $description = self::generateDescription($appSite, '');

                // Add default scenario (no AppView)
                $scenarios[] = new Scenario($appSite, $appFile, '', $totalSize, $displayName, $description);

                // Check for Views folder
                $viewsPath = $appSiteDir . DIRECTORY_SEPARATOR . 'Views';
                if (is_dir($viewsPath)) {
                    $viewFiles = scandir($viewsPath);

                    foreach ($viewFiles as $viewFile) {
                        if (!str_ends_with($viewFile, '.html')) continue;
                        if ($viewFile === '.' || $viewFile === '..') continue;

                        $viewName = pathinfo($viewFile, PATHINFO_FILENAME);
                        $appView = '';

                        // Extract AppView from view filename
                        if (stripos($viewName, 'content') !== false) {
                            $contentIndex = stripos($viewName, 'content');
                            if ($contentIndex > 0) {
                                $viewPart = substr($viewName, 0, $contentIndex);
                                if (strlen($viewPart) > 0) {
                                    $appView = ucfirst($viewPart);
                                }
                            }
                        }

                        if (!empty($appView)) {
                            $viewDisplayName = self::generateDisplayName($appSite, $appView);
                            $viewDescription = self::generateDescription($appSite, $appView);
                            $scenarios[] = new Scenario($appSite, $appFile, $appView, $totalSize, $viewDisplayName, $viewDescription);
                        }
                    }
                }
            }
        }

        // Write as multi-line CSV with header
        $csvLines = ["AppSite,AppFile,AppView,TotalSize,DisplayName,Description"];
        foreach ($scenarios as $scenario) {
            $csvLines[] = $scenario->toCsvLine();
        }

        file_put_contents($csvFilePath, implode("\n", $csvLines));
    }

    private static function parseCsvLine(string $line): array
    {
        $result = [];
        $current = '';
        $inQuotes = false;

        for ($i = 0; $i < strlen($line); $i++) {
            $c = $line[$i];

            if ($c === '"') {
                $inQuotes = !$inQuotes;
            } elseif ($c === ',' && !$inQuotes) {
                $result[] = $current;
                $current = '';
            } else {
                $current .= $c;
            }
        }

        $result[] = $current;
        return $result;
    }

    private static function loadScenariosInternal(string $wwwrootPath): array
    {
        $appDataPath = $wwwrootPath . DIRECTORY_SEPARATOR . 'App_Data';
        $csvFilePath = $appDataPath . DIRECTORY_SEPARATOR . 'scenarios.csv';

        // Generate if not exists
        if (!file_exists($csvFilePath)) {
            self::generateScenariosCsv($wwwrootPath);
        }

        // Read CSV lines
        $csvContent = file_get_contents($csvFilePath);
        $csvLines = array_map('trim', explode("\n", $csvContent));
        $csvLines = array_filter($csvLines, fn($l) => !empty($l));

        if (empty($csvLines)) {
            throw new \Exception('scenarios.csv is empty');
        }

        $scenarios = [];

        // Check if first line is a header
        $hasHeader = str_contains($csvLines[0], 'AppSite') && str_contains($csvLines[0], 'AppFile');
        $startLine = $hasHeader ? 1 : 0;

        for ($i = $startLine; $i < count($csvLines); $i++) {
            $line = trim($csvLines[$i]);
            if (empty($line)) continue;

            $parts = self::parseCsvLine($line);

            if (count($parts) >= 2) {
                $appSite = trim($parts[0]);
                $appFile = trim($parts[1]);
                $appView = count($parts) > 2 ? trim($parts[2]) : '';
                $totalSize = count($parts) > 3 ? intval(trim($parts[3])) : 0;
                $displayName = count($parts) > 4 ? trim($parts[4], '" ') : '';
                $description = count($parts) > 5 ? trim($parts[5], '" ') : '';

                $scenarios[] = new Scenario($appSite, $appFile, $appView, $totalSize, $displayName, $description);
            }
        }

        if (empty($scenarios)) {
            throw new \Exception('No scenarios found in scenarios.csv');
        }

        return $scenarios;
    }

    public static function load(string $wwwrootPath): void
    {
        self::$wwwrootPath = $wwwrootPath;
        self::$cachedScenarios = self::loadScenariosInternal($wwwrootPath);
        self::$cachedAppSites = self::extractAppSitesFromScenarios(self::$cachedScenarios);
    }

    public static function reload(): void
    {
        if (self::$wwwrootPath === null) {
            throw new \Exception('ConfigUtil not loaded. Call load(wwwrootPath) first.');
        }

        self::$cachedScenarios = self::loadScenariosInternal(self::$wwwrootPath);
        self::$cachedAppSites = self::extractAppSitesFromScenarios(self::$cachedScenarios);
    }

    public static function getAppSites(): array
    {
        if (self::$cachedAppSites === null) {
            throw new \Exception('AppSitesConfig not loaded. Call load(wwwrootPath) first.');
        }

        return self::$cachedAppSites;
    }

    public static function getScenarios(): array
    {
        if (self::$cachedScenarios === null) {
            throw new \Exception('AppSitesConfig not loaded. Call load(wwwrootPath) first.');
        }

        return self::$cachedScenarios;
    }

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

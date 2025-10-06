<?php

class AppSitesConfig
{
    private static function generateAppSitesCsv(string $wwwrootPath): void
    {
        $appSitesPath = $wwwrootPath . DIRECTORY_SEPARATOR . 'AppSites';
        $appDataPath = $wwwrootPath . DIRECTORY_SEPARATOR . 'App_Data';
        $csvFilePath = $appDataPath . DIRECTORY_SEPARATOR . 'appsites.csv';

        if (!is_dir($appSitesPath)) {
            throw new Exception("AppSites directory not found: " . $appSitesPath);
        }

        if (!is_dir($appDataPath)) {
            mkdir($appDataPath, 0755, true);
        }

        $entries = scandir($appSitesPath);
        $appSites = [];
        foreach ($entries as $entry) {
            if ($entry !== '.' && $entry !== '..' && is_dir($appSitesPath . DIRECTORY_SEPARATOR . $entry)) {
                $appSites[] = $entry;
            }
        }

        if (!in_array('Index', $appSites, true)) {
            $appSites[] = 'Index';
        }

        sort($appSites);

        $csv = implode(',', $appSites);
        file_put_contents($csvFilePath, $csv);
    }

    public static function loadAppSites(string $wwwrootPath): array
    {
        $appDataPath = $wwwrootPath . DIRECTORY_SEPARATOR . 'App_Data';
        $csvFilePath = $appDataPath . DIRECTORY_SEPARATOR . 'appsites.csv';

        if (!file_exists($csvFilePath)) {
            self::generateAppSitesCsv($wwwrootPath);
        }

        $csv = trim(file_get_contents($csvFilePath));

        if (empty($csv)) {
            throw new Exception('appsites.csv is empty');
        }

        $appSites = array_filter(array_map('trim', explode(',', $csv)), fn($s) => strlen($s) > 0);

        if (count($appSites) === 0) {
            throw new Exception('No AppSites found in appsites.csv');
        }

        return array_map('strtolower', $appSites);
    }

    public static function reloadAppSites(string $wwwrootPath): array
    {
        self::generateAppSitesCsv($wwwrootPath);
        return self::loadAppSites($wwwrootPath);
    }
}

<?php

use Assembler\Config\ConfigUtil;

class SecurityValidator
{
    private const PARAM_MAX_LENGTH = 256;
    private static array $validEnginTypes = ['normal', 'preprocess'];
    private static ?array $cachedValidAppSites = null;

    public static function getValidAppSites(string $wwwrootPath): array
    {
        if (self::$cachedValidAppSites !== null) {
            return self::$cachedValidAppSites;
        }

        self::$cachedValidAppSites = ConfigUtil::getAppSites();
        return self::$cachedValidAppSites;
    }

    public static function clearAppSitesCache(): void
    {
        self::$cachedValidAppSites = null;
    }

    public static function isValidPathComponent(?string $value): bool
    {
        if ($value === null || !is_string($value)) {
            return false;
        }

        $v = trim($value);
        if ($v === '') {
            return false;
        }

        if (strlen($v) > self::PARAM_MAX_LENGTH) {
            return false;
        }

        if (str_contains($v, '..') || str_contains($v, '/') || str_contains($v, '\\')) {
            return false;
        }

        $invalidChars = ['<', '>', ':', '"', '|', '?', '*', "\0"];
        for ($i = 0; $i < strlen($v); $i++) {
            $char = $v[$i];
            if (in_array($char, $invalidChars, true)) {
                return false;
            }
            if (ord($char) < 32) {
                return false;
            }
        }

        return true;
    }

    public static function isValidEngineType(?string $engineType): bool
    {
        if ($engineType === null || !is_string($engineType)) {
            return false;
        }
        return in_array(strtolower($engineType), self::$validEnginTypes, true);
    }

    public static function isValidAppSite(?string $appSite, array $validAppSites): bool
    {
        if ($appSite === null || !is_string($appSite)) {
            return false;
        }
        return in_array(strtolower($appSite), $validAppSites, true);
    }
}

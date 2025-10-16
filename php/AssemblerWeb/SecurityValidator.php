<?php

use Assembler\Config\ConfigUtil;

class SecurityValidator
{
    private const PARAM_MAX_LENGTH = 256;
    // Maximum content sizes to prevent DDOS attacks (match C#)
    private const MAX_LOG_FILE_SIZE = 500 * 1024; // 500 KB per log file
    // Buffer allowance for output size validation (50 KB)
    public const OUTPUT_SIZE_BUFFER = 50 * 1024; // 50 KB buffer
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

    public static function isValidContentSize(?string $content, int $maxSize): bool
    {
        if (empty($content)) return true;
        return strlen($content) <= $maxSize;
    }

    /**
     * Validates log content format and size similar to C# implementation
     * @param string|null $logContent
     * @param string|null &$errorMessage
     * @return bool
     */
    public static function isValidLogContent(?string $logContent, ?string &$errorMessage): bool
    {
        $errorMessage = null;

        if ($logContent === null || $logContent === '') {
            $errorMessage = 'Log content is empty';
            return false;
        }

        // Check file size limit (500 KB)
        if (!self::isValidContentSize($logContent, self::MAX_LOG_FILE_SIZE)) {
            $errorMessage = 'Log file exceeds maximum size limit (500 KB)';
            return false;
        }

        // Log entry pattern similar to C# regex
        $pattern = '/^[\[\]0-9:\-\s\.TZ]+\s*(DEBUG|INFO|WARN|ERROR|TRACE|FATAL)?:?\s*.+$/im';

        // Split into lines
        $lines = preg_split('/\r?\n/', $logContent);
        $validLines = 0;
        $totalLines = 0;

        foreach ($lines as $line) {
            $line = trim($line);
            if ($line === '') continue;
            $totalLines++;
            if (preg_match($pattern, $line) || str_starts_with($line, '    at ') || str_starts_with($line, "\tat ")) {
                $validLines++;
            }
        }

        if ($totalLines > 0 && (($validLines / $totalLines) < 0.5)) {
            $errorMessage = 'Log content does not match expected format';
            return false;
        }

        return true;
    }

    /**
     * Validates output size against template total size with fixed buffer
     */
    public static function isValidOutputSizeWithBuffer(?string $htmlContent, int $templateTotalSize): bool
    {
        if ($htmlContent === null || $htmlContent === '') return true;
        $outputSize = strlen($htmlContent);
        if ($templateTotalSize > 0) {
            $maxAllowed = $templateTotalSize + self::OUTPUT_SIZE_BUFFER;
            return $outputSize <= $maxAllowed;
        }
        // If template size unknown, reject
        return false;
    }

    /**
     * Gets the template total size for an AppSite from scenarios
     */
    public static function getTemplateTotalSize(string $appSite, ?string $appView): int
    {
        try {
            $scenarios = \Assembler\Config\ConfigUtil::getScenarios();
            foreach ($scenarios as $s) {
                if (strcasecmp($s->appSite, $appSite) === 0 && strcasecmp($s->appView ?? '', $appView ?? '') === 0) {
                    return $s->totalSize ?? 0;
                }
            }
            return 0;
        } catch (\Exception $e) {
            return 0;
        }
    }

    public static function isValidAppSite(?string $appSite, array $validAppSites): bool
    {
        if ($appSite === null || !is_string($appSite)) {
            return false;
        }
        return in_array(strtolower($appSite), $validAppSites, true);
    }
}

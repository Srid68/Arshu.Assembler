<?php

namespace Assembler\Engine;

use Assembler\App\Json\JsonObject;
use Assembler\App\JsonConverter;
use Arshu\Common\Logger;

/// <summary>
/// Utility class for resolving JSON key inheritance from parent templates
/// Inheritance follows a tree structure: parent -> child, not siblings
/// </summary>
class JsonInheritanceUtil
{
    /// <summary>
    /// Resolves a JSON key with inheritance support
    /// If the key ends with #, searches up the parent tree for the key without #
    /// </summary>
    /// <param name="jsonKey">The JSON key (may end with #)</param>
    /// <param name="currentValue">Current value of the key</param>
    /// <param name="currentTemplateKey">Current template key (e.g., "jsonruleflow1a_languagelinks")</param>
    /// <param name="allTemplates">Dictionary of all templates with their JSON data</param>
    /// <param name="parentMap">Map of child template key to parent template key</param>
    /// <returns>Resolved value or the default value after # or empty string</returns>
    public static function resolveJsonKeyWithInheritance(
        string $jsonKey,
        ?string $currentValue,
        string $currentTemplateKey,
        array $allTemplates,
        array $parentMap): ?string
    {
        // If key doesn't end with #, no inheritance - return current value
        if (!str_ends_with($jsonKey, "#")) {
            return $currentValue;
        }

        // Extract the actual key name without the # suffix
        $actualKey = substr($jsonKey, 0, -1);

        Logger::debug("Resolving inherited key: {$jsonKey} -> {$actualKey} for template {$currentTemplateKey}", "JsonInheritance");

        // Search up the parent tree for the key
        $inheritedValue = self::searchParentTreeForKey($actualKey, $currentTemplateKey, $allTemplates, $parentMap);

        if ($inheritedValue !== null) {
            Logger::debug("Found inherited value for {$actualKey}: {$inheritedValue}", "JsonInheritance");
            return $inheritedValue;
        }

        // If not found in parents, use the current value as default
        Logger::debug("No inherited value found for {$actualKey}, using default: {$currentValue}", "JsonInheritance");
        return $currentValue;
    }

    /// <summary>
    /// Searches up the parent tree to find a JSON key value
    /// </summary>
    private static function searchParentTreeForKey(
        string $key,
        string $currentTemplateKey,
        array $allTemplates,
        array $parentMap): ?string
    {
        // Get parent template key
        if (!isset($parentMap[$currentTemplateKey])) {
            Logger::debug("No parent found for {$currentTemplateKey}", "JsonInheritance");
            return null;
        }
        $parentKey = $parentMap[$currentTemplateKey];

        Logger::debug("Checking parent {$parentKey} for key {$key}", "JsonInheritance");

        // Get parent's JSON data
        if (!isset($allTemplates[$parentKey])) {
            Logger::debug("Parent template {$parentKey} not found in allTemplates", "JsonInheritance");
            return null;
        }
        $parentTemplate = $allTemplates[$parentKey]; // This will be an array: ['html' => ..., 'json' => ...]

        if (empty($parentTemplate['json'])) {
            Logger::debug("Parent template {$parentKey} has no JSON data, searching further up", "JsonInheritance");
            // Parent has no JSON, search further up the tree
            return self::searchParentTreeForKey($key, $parentKey, $allTemplates, $parentMap);
        }

        // Parse parent's JSON
        $parentJsonObj = JsonConverter::parseJsonString($parentTemplate['json']);

        // Look for the key (case-insensitive)
        foreach ($parentJsonObj as $jsonKey => $jsonValue) {
            if (strcasecmp($jsonKey, $key) === 0) {
                if (is_string($jsonValue)) {
                    Logger::debug("Found key {$key} in parent {$parentKey}: {$jsonValue}", "JsonInheritance");
                    return $jsonValue;
                }
            }
        }

        Logger::debug("Key {$key} not found in parent {$parentKey}, searching further up", "JsonInheritance");
        // Not found in this parent, search further up the tree
        return self::searchParentTreeForKey($key, $parentKey, $allTemplates, $parentMap);
    }

    /// <summary>
    /// Builds a parent map from template structure by analyzing placeholders
    /// This tracks which template is the parent of another based on {{TemplateName}} references
    /// </summary>
    public static function buildParentMap(
        string $appSite,
        array $allTemplates): array
    {
        $parentMap = []; // Using a simple array for case-insensitive keys in PHP is tricky, will rely on consistent casing or strtolower for keys if needed.
                         // For now, assuming keys in $allTemplates and $appSite are consistently cased or handled by strtolower.

        Logger::debug("Building parent map for appSite: {$appSite}", "JsonInheritance");

        foreach ($allTemplates as $templateKey => $templateData) {
            $html = $templateData['html'];

            // Find all {{TemplateName}} placeholders in this template
            $searchPos = 0;
            while ($searchPos < strlen($html)) {
                $openStart = strpos($html, "{{", $searchPos);
                if ($openStart === false) break;

                // Skip special placeholders (#, @, $, /)
                if ($openStart + 2 < strlen($html) &&
                    ($html[$openStart + 2] == '#' || $html[$openStart + 2] == '@' ||
                     $html[$openStart + 2] == '$' || $html[$openStart + 2] == '/'))
                {
                    $searchPos = $openStart + 2;
                    continue;
                }

                $closeStart = strpos($html, "}}", $openStart + 2);
                if ($closeStart === false) break;

                $placeholderName = trim(substr($html, $openStart + 2, $closeStart - ($openStart + 2)));

                // Check if this is a valid alphanumeric template name
                if (!empty($placeholderName) && self::isAlphaNumeric($placeholderName)) {
                    // This template (templateKey) is the parent of the placeholder template
                    $childTemplateKey = strtolower("{$appSite}_{$placeholderName}");

                    if (!isset($parentMap[$childTemplateKey])) {
                        $parentMap[$childTemplateKey] = $templateKey;
                        Logger::debug("Parent relationship: {$childTemplateKey} -> parent: {$templateKey}", "JsonInheritance");
                    }
                }

                $searchPos = $closeStart + 2;
            }
        }

        Logger::debug("Built parent map with " . count($parentMap) . " relationships", "JsonInheritance");
        return $parentMap;
    }

    private static function isAlphaNumeric(string $str): bool
    {
        return ctype_alnum($str);
    }
}

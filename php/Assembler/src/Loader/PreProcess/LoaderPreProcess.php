<?php

namespace Assembler\Loader\PreProcess;

use Assembler\Loader\TemplateResult;

use Assembler\App\Json\JsonArray;
use Assembler\App\Json\JsonObject;
use Assembler\App\JsonConverter;
use Arshu\Common\Logger;
use Assembler\Common\CommonUtil;
use Assembler\Model\PreprocessedSiteTemplates;
use Assembler\Model\PreprocessedTemplate;
use Assembler\Model\TemplatePlaceholder;
use Assembler\Model\SlottedTemplate;
use Assembler\Model\SlotPlaceholder;
use Assembler\Model\JsonPlaceholder;
use Assembler\Model\ReplacementMapping;
use Assembler\Model\ReplacementType;
use Exception;

/**
 * Handles loading and preprocessing of HTML templates from the file system
 */
class LoaderPreProcess
{
    private static array $preprocessedTemplatesCache = [];

    /**
     * Loads and preprocesses HTML files from the specified application site directory into structured templates, caching the output per appSite and rootDirName
     * @param string $rootDirPath Root directory path
     * @param string $appSite Primary AppSite name to load
     * @param string $searchAppSites Comma-delimited string of AppSite names to search for fallback templates (can be empty string)
     * @return PreprocessedSiteTemplates PreprocessedSiteTemplates containing structured template data
     */
    public static function loadProcessGetTemplateFiles(string $rootDirPath, string $appSite, string $searchAppSites = ''): PreprocessedSiteTemplates
    {
        Logger::debug("LoadProcessGetTemplateFiles called for appSite: $appSite, searchAppSites: $searchAppSites", 'LoaderPreProcess');

        $cacheKey = dirname($rootDirPath) . '|' . $appSite . '|' . $searchAppSites;

        if (isset(self::$preprocessedTemplatesCache[$cacheKey])) {
            $cached = self::$preprocessedTemplatesCache[$cacheKey];
            Logger::debug("Returning cached templates for $appSite (" . count($cached->templates) . " templates)", 'LoaderPreProcess');
            return $cached;
        }

        // Load templates from primary appSite
        $result = self::loadTemplatesFromSingleAppSite($rootDirPath, $appSite);

        // Load templates from searchAppSites for fallback
        if (!empty($searchAppSites)) {
            $searchAppSitesArray = explode(',', $searchAppSites);
            foreach ($searchAppSitesArray as $searchAppSite) {
                $searchAppSite = trim($searchAppSite);
                if (empty($searchAppSite)) {
                    continue;
                }

                $searchResult = self::loadTemplatesFromSingleAppSite($rootDirPath, $searchAppSite);

                // Merge templates (primary appSite takes precedence)
                foreach ($searchResult->templates as $key => $value) {
                    if (!isset($result->templates[$key])) {
                        $result->templates[$key] = $value;
                        $result->rawTemplates[$key] = $searchResult->rawTemplates[$key];
                        $result->templateKeys[] = $key;
                        Logger::debug("Added fallback template '$key' from '$searchAppSite'", 'LoaderPreProcess');
                    }
                }
            }
        }

        // CRITICAL: Create ALL replacement mappings after all templates are loaded
        // This ensures PreProcess engine does ONLY merging, no processing logic
        self::createAllReplacementMappingsForSite($result, $appSite);

        Logger::debug("Created all replacement mappings for $appSite", 'LoaderPreProcess');

        self::$preprocessedTemplatesCache[$cacheKey] = $result;
        return $result;
    }

    /**
     * Loads templates from a single AppSite without caching or fallback logic
     * @param string $rootDirPath Root directory path
     * @param string $appSite Application site name
     * @return PreprocessedSiteTemplates PreprocessedSiteTemplates containing structured template data
     */
    private static function loadTemplatesFromSingleAppSite(string $rootDirPath, string $appSite): PreprocessedSiteTemplates
    {
        $result = new PreprocessedSiteTemplates();
        $result->siteName = $appSite;

        $appSitesPath = $rootDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $appSite;

        if (!is_dir($appSitesPath)) {
            Logger::warn("AppSites directory not found: $appSitesPath", 'LoaderPreProcess');
            return $result;
        }

        Logger::debug("Loading templates from: $appSitesPath", 'LoaderPreProcess');

        // Recursively find all HTML files
        $iterator = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator($appSitesPath, \RecursiveDirectoryIterator::SKIP_DOTS),
            \RecursiveIteratorIterator::LEAVES_ONLY
        );

        foreach ($iterator as $file) {
            if ($file->isFile() && $file->getExtension() === 'html') {
                $fileName = $file->getBasename('.html');
                $key = strtolower($appSite) . '_' . strtolower($fileName);

                $content = CommonUtil::normalizeFileContent(file_get_contents($file->getPathname())) ?: '';
                Logger::debug("Loading template: $key (size: " . strlen($content) . ")", 'LoaderPreProcess');

                // Find JSON file case-insensitively
                $jsonFile = substr($file->getPathname(), 0, -5) . '.json';
                $jsonObject = null;

                // Try exact match first
                if (file_exists($jsonFile)) {
                    $jsonContent = CommonUtil::normalizeFileContent(file_get_contents($jsonFile));
                    if (!empty($jsonContent)) {
                        $jsonObject = JsonConverter::parseJsonString($jsonContent);
                        Logger::debug("Found JSON file for {$key} (size: " . strlen($jsonContent) . ")", 'LoaderPreProcess');
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
                                Logger::debug("Found JSON file (case-insensitive) for {$key} (size: " . strlen($jsonContent) . ")", 'LoaderPreProcess');
                            }
                        }
                    }
                }

                // Store raw template for backward compatibility
                $result->rawTemplates[$key] = $content;
                $result->templateKeys[] = $key;

                // Preprocess the template with JSON data
                $preprocessed = self::preprocessTemplate($content, $jsonObject, $appSite, $key);
                $result->templates[$key] = $preprocessed;

                Logger::debug("Preprocessed $key: " . count($preprocessed->replacementMappings) . " replacements, " .
                    count($preprocessed->slottedTemplates) . " slotted, " . count($preprocessed->placeholders) . " placeholders", 'LoaderPreProcess');
            }
        }

        Logger::debug("Loaded " . count($result->templates) . " templates for $appSite", 'LoaderPreProcess');
        return $result;
    }

    /**
     * Preprocesses JSON data into a JsonObject structure for efficient template merging
     * @param string $jsonContent The JSON content to preprocess
     * @return \Assembler\App\Json\JsonObject JsonObject containing preprocessed JSON data
     */
    public static function preprocessJsonData(string $jsonContent): JsonObject
    {
        return JsonConverter::parseJsonString($jsonContent);
    }

    /**
     * Clear all cached templates (useful for testing or when templates change)
     */
    public static function clearCache(): void
    {
        self::$preprocessedTemplatesCache = [];
    }

    /**
     * Creates a preprocessed template by parsing its structure and any associated JSON data.
     * This method handles parsing and JSON preprocessing, leaving only merging to the template engine.
     * @param string $content The template content to parse
     * @param string|null $jsonContent The JSON content to parse (optional)
     * @param string $appSite The application site name
     * @param string $templateKey The template key for reference
     * @return PreprocessedTemplate PreprocessedTemplate containing parsed structure and preprocessed JSON
     */
    private static function preprocessTemplate(string $content, ?JsonObject $jsonObject, string $appSite, string $templateKey): PreprocessedTemplate
    {
        $template = new PreprocessedTemplate();
        $template->originalContent = $content;

        if (empty($content)) {
            return $template;
        }

        // Parse JSON data into a structure
        if ($jsonObject !== null) {
            $template->setJsonData($jsonObject);
        }

        // Parse template structure
        self::parseSlottedTemplates($content, $appSite, $template);
        self::parsePlaceholderTemplates($content, $appSite, $template);

        // Preprocess JSON templates - analyze and prepare JSON placeholders and blocks
        if ($template->hasJsonData()) {
            self::preprocessJsonTemplates($template);
        }

        return $template;
    }

    /**
     * Creates ALL replacement mappings for all templates after they are loaded
     * This ensures the PreProcess engine only does merging, no processing logic
     * Critical architectural method - moves ALL processing from engine to loader
     * @param PreprocessedSiteTemplates $siteTemplates All templates for the site
     * @param string $appSite The application site name
     */
    public static function createAllReplacementMappingsForSite(PreprocessedSiteTemplates $siteTemplates, string $appSite): void
    {
        // Phase 0: Build parent map and resolve JSON inheritance BEFORE creating any mappings
        Logger::debug("Creating replacement mappings for $appSite - Phase 0: JSON inheritance", 'LoaderPreProcess');
        $parentMap = self::buildParentMapForPreProcess($siteTemplates, $appSite);
        self::resolveJsonInheritanceForAllTemplates($siteTemplates, $parentMap);

        // After resolving inheritance, recreate JSON placeholder mappings with the resolved values
        self::recreateJsonPlaceholderMappingsAfterInheritance($siteTemplates);

        Logger::debug("Creating replacement mappings for $appSite - Phase 1: JSON arrays", 'LoaderPreProcess');
        // Phase 1: Create JSON replacement mappings for all templates first (no dependencies)
        foreach ($siteTemplates->templates as $template) {
            // Create replacement mappings for JSON array blocks (including negative blocks)
            self::createJsonArrayReplacementMappings($template, $template->originalContent);
        }

        Logger::debug("Creating replacement mappings for $appSite - Phase 2: Simple placeholders", 'LoaderPreProcess');
        // Phase 2: Create simple template replacement mappings (may depend on JSON but not on slotted templates)
        foreach ($siteTemplates->templates as $template) {
            // Create replacement mappings for simple placeholders
            self::createPlaceholderReplacementMappings($template, $siteTemplates->templates, $appSite);
        }

        Logger::debug("Creating replacement mappings for $appSite - Phase 3: Slotted templates", 'LoaderPreProcess');
        // Phase 3: Create slotted template replacement mappings (may depend on other templates)
        foreach ($siteTemplates->templates as $template) {
            // Create replacement mappings for slotted templates
            self::createSlottedTemplateReplacementMappings($template, $siteTemplates->templates, $appSite);
        }

        // Log summary of all replacement mappings
        $totalMappings = 0;
        foreach ($siteTemplates->templates as $template) {
            $totalMappings += count($template->replacementMappings);
        }
        Logger::info("Total replacement mappings created for $appSite: $totalMappings", 'LoaderPreProcess');
    }

    /**
     * IndexOf-based version: Parses slotted templates in the content and adds them to the preprocessed template
     * @param string $content Template content
     * @param string $appSite Application site
     * @param PreprocessedTemplate $template Template to populate
     */
    private static function parseSlottedTemplates(string $content, string $appSite, PreprocessedTemplate $template): void
    {
        $searchPos = 0;

        while ($searchPos < strlen($content)) {
            // Look for opening tag {{#
            $openStart = strpos($content, '{{#', $searchPos);
            if ($openStart === false) break;

            // Find the end of the template name
            $openEnd = strpos($content, '}}', $openStart + 3);
            if ($openEnd === false) break;

            // Extract template name
            $templateName = trim(substr($content, $openStart + 3, $openEnd - $openStart - 3));
            if (empty($templateName) || !CommonUtil::isAlphaNumeric($templateName)) {
                $searchPos = $openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            $closeTag = '{{/' . $templateName . '}}';
            $closeStart = CommonUtil::findMatchingCloseTag(
                $content, 
                $openEnd + 2, 
                '{{#' . $templateName . '}}', 
                $closeTag
            );
            if ($closeStart === -1) {
                $searchPos = $openStart + 1;
                continue;
            }

            // Extract inner content
            $innerStart = $openEnd + 2;
            $innerContent = substr($content, $innerStart, $closeStart - $innerStart);
            $fullMatch = substr($content, $openStart, $closeStart + strlen($closeTag) - $openStart);

            // Create slotted template structure
            $slottedTemplate = new SlottedTemplate();
            $slottedTemplate->name = $templateName;
            $slottedTemplate->startIndex = $openStart;
            $slottedTemplate->endIndex = $closeStart + strlen($closeTag);
            $slottedTemplate->fullMatch = $fullMatch;
            $slottedTemplate->innerContent = $innerContent;
            $slottedTemplate->templateKey = strtolower($templateName); // Simple template name since appSite is passed as parameter

            // Parse slots within the slotted template
            self::parseSlots($innerContent, $slottedTemplate, $appSite);

            $template->slottedTemplates[] = $slottedTemplate;
            $searchPos = $closeStart + strlen($closeTag);
        }
    }

    /**
     * Parses placeholder templates in the content and adds them to the preprocessed template
     * @param string $content Template content
     * @param string $appSite Application site
     * @param PreprocessedTemplate $template Template to populate
     */
    private static function parsePlaceholderTemplates(string $content, string $appSite, PreprocessedTemplate $template): void
    {
        $searchPos = 0;

        while ($searchPos < strlen($content)) {
            // Look for opening placeholder {{
            $openStart = strpos($content, '{{', $searchPos);
            if ($openStart === false) break;

            // Make sure it's not a slotted template, conditional, or special placeholder
            if ($openStart + 2 < strlen($content) && 
                in_array($content[$openStart + 2], ['#', '@', '$', '/'])) {
                $searchPos = $openStart + 2;
                continue;
            }

            // Find closing }}
            $closeStart = strpos($content, '}}', $openStart + 2);
            if ($closeStart === false) break;

            // Extract placeholder name
            $placeholderName = trim(substr($content, $openStart + 2, $closeStart - $openStart - 2));
            if (empty($placeholderName) || !CommonUtil::isAlphaNumeric($placeholderName)) {
                $searchPos = $openStart + 2;
                continue;
            }

            $fullMatch = substr($content, $openStart, $closeStart + 2 - $openStart);

            // Create placeholder structure
            $placeholder = new TemplatePlaceholder();
            $placeholder->name = $placeholderName;
            $placeholder->startIndex = $openStart;
            $placeholder->endIndex = $closeStart + 2;
            $placeholder->fullMatch = $fullMatch;
            $placeholder->templateKey = strtolower($placeholderName);

            $template->placeholders[] = $placeholder;
            $searchPos = $closeStart + 2;
        }
    }

    /**
     * Parses slots within a slotted template
     * @param string $innerContent Inner content of slotted template
     * @param SlottedTemplate $slottedTemplate Slotted template to populate
     * @param string $appSite Application site
     */
    private static function parseSlots(string $innerContent, SlottedTemplate $slottedTemplate, string $appSite): void
    {
        $searchPos = 0;

        while ($searchPos < strlen($innerContent)) {
            $placeholderStart = strpos($innerContent, '{{@HTMLPLACEHOLDER', $searchPos);
            if ($placeholderStart === false) break;

            $afterPlaceholder = $placeholderStart + 18;
            $pos = $afterPlaceholder;

            // Extract any trailing number (if present)
            $placeholderNumber = '';
            while ($pos < strlen($innerContent) && is_numeric($innerContent[$pos])) {
                $placeholderNumber .= $innerContent[$pos];
                $pos++;
            }

            if ($pos + 1 < strlen($innerContent) && substr($innerContent, $pos, 2) === '}}') {
                $placeholderEnd = $pos + 2;
                $placeholder = substr($innerContent, $placeholderStart, $placeholderEnd - $placeholderStart);
                
                // Find the matching closing tag by counting nested opening/closing pairs
                $closingTag = '{{/HTMLPLACEHOLDER' . $placeholderNumber . '}}';
                $openingTag = '{{@HTMLPLACEHOLDER' . $placeholderNumber . '}}';
                
                $closingStart = CommonUtil::findMatchingCloseTag($innerContent, $placeholderEnd, $openingTag, $closingTag);
                
                if ($closingStart !== -1) {
                    $slotContent = substr($innerContent, $placeholderEnd, $closingStart - $placeholderEnd);
                    
                    // Create slot structure
                    $slot = new SlotPlaceholder();
                    $slot->number = $placeholderNumber;
                    $slot->startIndex = $placeholderStart;
                    $slot->endIndex = $closingStart + strlen($closingTag);
                    $slot->content = $slotContent;
                    $slot->slotKey = '{{$HTMLPLACEHOLDER' . $placeholderNumber . '}}';
                    $slot->openTag = $placeholder;
                    $slot->closeTag = $closingTag;

                                // Parse nested templates within the slot content
                                self::parseNestedTemplatesInSlot($slot, $slottedTemplate->jsonData, $appSite);
                    $slottedTemplate->slots[] = $slot;
                    $searchPos = $closingStart + strlen($closingTag);
                } else {
                    $searchPos = $placeholderEnd;
                }
            } else {
                $searchPos = $placeholderStart + 1;
            }
        }
    }

    /**
     * Parses nested templates within slot content
     * @param SlotPlaceholder $slot Slot to parse nested templates in
     * @param string $appSite Application site
     */
    private static function parseNestedTemplatesInSlot(SlotPlaceholder $slot, ?JsonObject $jsonData, string $appSite): void
    {
        if (empty($slot->content)) {
            return;
        }

        // Parse simple placeholders like {{ComponentName}}
        $searchPos = 0;
        while (($openPos = strpos($slot->content, '{{', $searchPos)) !== false) {
            $closePos = strpos($slot->content, '}}', $openPos + 2);
            if ($closePos === false) {
                break;
            }

            $innerContent = substr($slot->content, $openPos + 2, $closePos - $openPos - 2);
            
            // Skip if it starts with #, /, @ or $ (these are not simple template placeholders)
            if (empty($innerContent) || $innerContent[0] === '#' || $innerContent[0] === '/' || 
                $innerContent[0] === '@' || $innerContent[0] === '$') {
                $searchPos = $closePos + 2;
                continue;
            }

            $templateName = trim($innerContent);
            $templateKey = strtolower($templateName);

            $placeholder = new TemplatePlaceholder();
            $placeholder->name = $templateName;
            $placeholder->startIndex = $openPos;
            $placeholder->endIndex = $closePos + 2;
            $placeholder->fullMatch = substr($slot->content, $openPos, $closePos + 2 - $openPos);
                            $placeholder->templateKey = $templateKey;
                            $placeholder->jsonData = $jsonData;
            $slot->nestedPlaceholders[] = $placeholder;
            $searchPos = $closePos + 2;
        }

        // Parse slotted templates like {{#TemplateName}} ... {{/TemplateName}}
        $searchPos = 0;
        while (($openPos = strpos($slot->content, '{{#', $searchPos)) !== false) {
            $openClosePos = strpos($slot->content, '}}', $openPos + 3);
            if ($openClosePos === false) {
                break;
            }

            $templateName = trim(substr($slot->content, $openPos + 3, $openClosePos - $openPos - 3));
            $endTag = '{{/' . $templateName . '}}';
            $endPos = strpos($slot->content, $endTag, $openClosePos + 2);
            
            if ($endPos === false) {
                $searchPos = $openClosePos + 2;
                continue;
            }

            $innerContent = substr($slot->content, $openClosePos + 2, $endPos - $openClosePos - 2);
            $templateKey = strtolower($templateName);

            $nestedSlottedTemplate = new SlottedTemplate();
            $nestedSlottedTemplate->name = $templateName;
            $nestedSlottedTemplate->startIndex = $openPos;
            $nestedSlottedTemplate->endIndex = $endPos + strlen($endTag);
            $nestedSlottedTemplate->fullMatch = substr($slot->content, $openPos, $nestedSlottedTemplate->endIndex - $openPos);
            $nestedSlottedTemplate->innerContent = $innerContent;
            $nestedSlottedTemplate->templateKey = $templateKey;
            $nestedSlottedTemplate->jsonData = $jsonData;

            // Parse slots within this nested slotted template
            self::parseSlots($innerContent, $nestedSlottedTemplate, $appSite);

            $slot->nestedSlottedTemplates[] = $nestedSlottedTemplate;
            $searchPos = $nestedSlottedTemplate->endIndex;
        }
    }

    /**
     * Preprocesses JSON templates - analyze and prepare JSON placeholders and blocks
     * @param PreprocessedTemplate $template Template to process
     */
    private static function preprocessJsonTemplates(PreprocessedTemplate $template): void
    {
        if ($template->jsonData === null) {
            return;
        }

        $content = $template->originalContent;

        // Step 1: Create replacement mappings for JSON array blocks
        self::createJsonArrayReplacementMappings($template, $content);

        // Step 2: Create replacement mappings for JSON placeholders
        self::createJsonPlaceholderReplacementMappings($template, $content);

        // Note: No processing here - only creating replacement mappings for the PreProcess engine
    }



    private static function createJsonPlaceholderReplacementMappings(PreprocessedTemplate $template, string $content): void
    {
        if ($template->jsonData === null) return;

        foreach ($template->jsonData as $key => $value) {
            if (is_string($value)) {
                // Only handle {{$key}} pattern
                $placeholder = '{{$' . $key . '}}';

                if (stripos($content, $placeholder) !== false) {
                    // Create replacement mapping for direct replacement
                    $mapping = new ReplacementMapping(
                        $placeholder,
                        $value,
                        ReplacementType::JSON_PLACEHOLDER
                    );
                    $template->replacementMappings[] = $mapping;

                    // Also create JsonPlaceholder (avoid duplicates)
                    $placeholderExists = false;
                    foreach ($template->jsonPlaceholders as $existing) {
                        if ($existing->placeholder === $placeholder) {
                            $placeholderExists = true;
                            break;
                        }
                    }
                    if (!$placeholderExists) {
                        $jsonPlaceholder = new JsonPlaceholder($key, $placeholder, $value);
                        $template->jsonPlaceholders[] = $jsonPlaceholder;
                    }
                }
            }
        }
    }

    /**
     * Creates replacement mappings for simple placeholders ({{templatename}})
     * This moves ALL placeholder processing logic from PreProcess engine to TemplateLoader
     * @param PreprocessedTemplate $template Template to process
     * @param array<string, PreprocessedTemplate> $allTemplates All templates
     * @param string $appSite Application site
     */
    private static function createPlaceholderReplacementMappings(PreprocessedTemplate $template, array $allTemplates, string $appSite): void
    {
        Logger::debug("[LoaderPreProcess] createPlaceholderReplacementMappings for template: hasPlaceholders=" . ($template->hasPlaceholders() ? 'true' : 'false') . ", placeholders count=" . count($template->placeholders), 'LoaderPreProcess');

        if (!$template->hasPlaceholders()) return;

        foreach ($template->placeholders as $placeholder) {
            Logger::debug("[LoaderPreProcess] Processing placeholder: {$placeholder->fullMatch}, templateKey: {$placeholder->templateKey}", 'LoaderPreProcess');
            // FIRST: Try current appSite
            $targetTemplateKey = strtolower($appSite) . '_' . $placeholder->templateKey;
            $targetTemplate = null;

            if (isset($allTemplates[$targetTemplateKey])) {
                $targetTemplate = $allTemplates[$targetTemplateKey];
                // Found in current appSite
            } else {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                $searchKey = '_' . $placeholder->templateKey;
                foreach ($allTemplates as $key => $template_value) {
                    if (str_ends_with(strtolower($key), strtolower($searchKey))) {
                        $targetTemplate = $template_value;
                        Logger::debug("Template '{$placeholder->templateKey}' not found as '{$targetTemplateKey}', using fallback from '{$key}'", 'LoaderPreProcess');
                        break;
                    }
                }
            }

            if ($targetTemplate) {
                // Start with the target template's original content
                $processedTemplate = $targetTemplate->originalContent;

                // CRITICAL FIX: Apply the target template's JSON placeholder replacements
                // This ensures nested components use their own JSON context (e.g., header.json for Header component)
                $jsonMappingCount = 0;
                foreach ($targetTemplate->replacementMappings as $m) {
                    if ($m->type === ReplacementType::JSON_PLACEHOLDER && strpos($processedTemplate, $m->originalText) !== false) {
                        Logger::debug("  Replacing placeholder (original size: " . strlen($m->originalText) . ", replacement size: " . strlen($m->replacementText) . ") in {$targetTemplateKey}", 'LoaderPreProcess');
                        $processedTemplate = str_replace($m->originalText, $m->replacementText, $processedTemplate);
                        $jsonMappingCount++;
                    }
                }
                if ($jsonMappingCount > 0) {
                    Logger::debug("Applied {$jsonMappingCount} JSON mappings to {$targetTemplateKey}", 'LoaderPreProcess');
                }

                // Create the replacement mapping
                Logger::debug("[LoaderPreProcess] Creating replacement mapping for {$placeholder->fullMatch}, processed size: " . strlen($processedTemplate) . ", contains {{@: " . (strpos($processedTemplate, '{{@') !== false ? 'YES - BUG!' : 'NO - OK'), 'LoaderPreProcess');

                $mapping = new ReplacementMapping(
                    $placeholder->fullMatch,
                    $processedTemplate,
                    ReplacementType::SIMPLE_TEMPLATE
                );

                $template->replacementMappings[] = $mapping;
            }
        }
    }

    /**
     * Creates replacement mappings for slotted templates ({{#templatename}}...{{/templatename}})
     * This moves ALL slotted template processing logic from PreProcess engine to TemplateLoader
     * @param PreprocessedTemplate $template Template to process
     * @param array<string, PreprocessedTemplate> $allTemplates All templates
     * @param string $appSite Application site
     */
    private static function createSlottedTemplateReplacementMappings(PreprocessedTemplate $template, array $allTemplates, string $appSite): void
    {
        if (!$template->hasSlottedTemplates()) return;

        foreach ($template->slottedTemplates as $slottedTemplate) {
            // FIRST: Try current appSite
            $targetTemplateKey = strtolower($appSite) . '_' . $slottedTemplate->templateKey;
            $targetTemplate = null;

            if (isset($allTemplates[$targetTemplateKey])) {
                $targetTemplate = $allTemplates[$targetTemplateKey];
                // Found in current appSite
            } else {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                $searchKey = '_' . $slottedTemplate->templateKey;
                foreach ($allTemplates as $key => $template_value) {
                    if (str_ends_with(strtolower($key), strtolower($searchKey))) {
                        $targetTemplate = $template_value;
                        Logger::debug("Slotted template '{$slottedTemplate->templateKey}' not found as '{$targetTemplateKey}', using fallback from '{$key}'", 'LoaderPreProcess');
                        break;
                    }
                }
            }

            if ($targetTemplate) {
                $processedTemplate = $targetTemplate->originalContent;

                // Process slots in the target template
                foreach ($slottedTemplate->slots as $slot) {
                    $processedSlotContent = self::processSlotContentForReplacementMapping($slot, $allTemplates, $appSite);
                    $processedTemplate = str_replace($slot->slotKey, $processedSlotContent, $processedTemplate);
                }

                // Handle default slot content when no explicit slots exist
                if (count($slottedTemplate->slots) === 0) {
                    $actualInnerContent = $slottedTemplate->innerContent;
                    if (!empty($actualInnerContent) && trim($actualInnerContent) !== '') {
                        $defaultSlotKey = '{{$HTMLPLACEHOLDER}}';
                        if (strpos($processedTemplate, $defaultSlotKey) !== false) {
                            $processedTemplate = str_replace($defaultSlotKey, trim($actualInnerContent), $processedTemplate);
                        }
                    }
                }

                // Remove remaining slot placeholders
                $processedTemplate = CommonUtil::removeRemainingSlotPlaceholders($processedTemplate);

                // Create the replacement mapping
                $mapping = new ReplacementMapping(
                    $slottedTemplate->fullMatch,
                    $processedTemplate,
                    ReplacementType::SLOTTED_TEMPLATE
                );
                
                $template->replacementMappings[] = $mapping;
            }
        }
    }

    /**
     * Processes slot content for creating replacement mappings
     * This recursively processes nested templates and placeholders
     * @param SlotPlaceholder $slot Slot to process
     * @param array<string, PreprocessedTemplate> $allTemplates All templates
     * @param string $appSite Application site
     * @return string Processed slot content
     */
    private static function processSlotContentForReplacementMapping(SlotPlaceholder $slot, array $allTemplates, string $appSite): string
    {
        $result = $slot->content;

        // Process nested slotted templates
        foreach ($slot->nestedSlottedTemplates as $nestedSlottedTemplate) {
            // FIRST: Try current appSite
            $targetTemplateKey = strtolower($appSite) . '_' . $nestedSlottedTemplate->templateKey;
            $targetTemplate = null;

            if (isset($allTemplates[$targetTemplateKey])) {
                $targetTemplate = $allTemplates[$targetTemplateKey];
                // Found in current appSite
            } else {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                $searchKey = '_' . $nestedSlottedTemplate->templateKey;
                foreach ($allTemplates as $key => $template_value) {
                    if (str_ends_with(strtolower($key), strtolower($searchKey))) {
                        $targetTemplate = $template_value;
                        Logger::debug("Nested slotted template '{$nestedSlottedTemplate->templateKey}' not found as '{$targetTemplateKey}', using fallback from '{$key}'", 'LoaderPreProcess');
                        break;
                    }
                }
            }

            if ($targetTemplate) {
                // Use the target template's original content without applying replacement mappings
                // This prevents circular dependencies during replacement mapping creation
                $processedTemplate = $targetTemplate->originalContent;

                // Process nested slots
                foreach ($nestedSlottedTemplate->slots as $nestedSlot) {
                    $processedNestedSlotContent = self::processSlotContentForReplacementMapping($nestedSlot, $allTemplates, $appSite);
                    $processedTemplate = str_replace($nestedSlot->slotKey, $processedNestedSlotContent, $processedTemplate);
                }

                // Remove remaining slot placeholders
                $processedTemplate = CommonUtil::removeRemainingSlotPlaceholders($processedTemplate);

                // Replace in result
                $result = str_replace($nestedSlottedTemplate->fullMatch, $processedTemplate, $result);
            }
        }

        // Process nested simple placeholders
        foreach ($slot->nestedPlaceholders as $nestedPlaceholder) {
            // FIRST: Try current appSite
            $targetTemplateKey = strtolower($appSite) . '_' . $nestedPlaceholder->templateKey;
            $targetTemplate = null;

            if (isset($allTemplates[$targetTemplateKey])) {
                $targetTemplate = $allTemplates[$targetTemplateKey];
                // Found in current appSite
            } else {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                $searchKey = '_' . $nestedPlaceholder->templateKey;
                foreach ($allTemplates as $key => $template_value) {
                    if (str_ends_with(strtolower($key), strtolower($searchKey))) {
                        $targetTemplate = $template_value;
                        Logger::debug("Nested placeholder '{$nestedPlaceholder->templateKey}' not found as '{$targetTemplateKey}', using fallback from '{$key}'", 'LoaderPreProcess');
                        break;
                    }
                }
            }

            if ($targetTemplate) {
                // Start with the target template's original content
                $processedTemplate = $targetTemplate->originalContent;

                // CRITICAL FIX: Apply the target template's JSON placeholder replacements
                // This ensures nested components use their own JSON context
                foreach ($targetTemplate->replacementMappings as $jsonMapping) {
                    if ($jsonMapping->type === ReplacementType::JSON_PLACEHOLDER) {
                        $processedTemplate = str_replace($jsonMapping->originalText, $jsonMapping->replacementText, $processedTemplate);
                    }
                }

                // Replace in result
                $result = str_replace($nestedPlaceholder->fullMatch, $processedTemplate, $result);
            }
        }

        return $result;
    }

    /**
     * Creates replacement mappings for JSON array blocks
     * @param PreprocessedTemplate $template Template to process
     * @param string $content Template content
     */
    private static function createJsonArrayReplacementMappings(PreprocessedTemplate $template, string $content): void
    {
        // Process JSON array templates if we have JSON data
        if (!$template->jsonData) return;
        
        // Convert JsonObject to array for easier processing
        $jsonArray = [];
        if ($template->jsonData instanceof \Assembler\App\Json\JsonObject) {
            foreach ($template->jsonData as $key => $value) {
                $jsonArray[$key] = $value;
            }
        } else {
            $jsonArray = (array)$template->jsonData;
        }
        
        // Create case-insensitive key mapping for better template matching
        $caseInsensitiveKeys = [];
        foreach ($jsonArray as $key => $value) {
            $caseInsensitiveKeys[strtolower($key)] = $key;
        }
        
        // Find all array templates in content first to match with JSON keys case-insensitively
        $foundTemplateKeys = [];
        if (preg_match_all('/\{\{[@^](\w+)\}\}/', $content, $matches)) {
            foreach ($matches[1] as $templateKey) {
                $templateKeyLower = strtolower($templateKey);
                if (isset($caseInsensitiveKeys[$templateKeyLower])) {
                    $actualJsonKey = $caseInsensitiveKeys[$templateKeyLower];
                    $foundTemplateKeys[$actualJsonKey] = $templateKey; // Map JSON key to template key
                }
            }
        }
        
        foreach ($jsonArray as $arrayKey => $arrayValue) {
            // Skip if this JSON key doesn't have a corresponding template
            if (!isset($foundTemplateKeys[$arrayKey])) {
                continue;
            }
            
            $templateKey = $foundTemplateKeys[$arrayKey]; // Use the template key found in content
            
            // Convert arrayValue to PHP array if it's a JsonObject or object
            $processedArrayValue = null;
            
            // Debug the actual class
            
            if ($arrayValue instanceof \Assembler\App\Json\JsonArray) {
                // For JsonArray, iterate through its items to get the array
                $processedArrayValue = [];
                foreach ($arrayValue as $item) {
                    if ($item instanceof \Assembler\App\Json\JsonObject) {
                        // Convert JsonObject item to array
                        $itemArray = [];
                        foreach ($item as $key => $value) {
                            $itemArray[$key] = $value;
                        }
                        $processedArrayValue[] = $itemArray;
                    } else {
                        $processedArrayValue[] = $item;
                    }
                }
            } elseif ($arrayValue instanceof \Assembler\App\Json\JsonObject) {
                // For JsonObject, iterate through its properties to get the array
                $processedArrayValue = [];
                foreach ($arrayValue as $item) {
                    if ($item instanceof \Assembler\App\Json\JsonObject) {
                        // Convert JsonObject item to array
                        $itemArray = [];
                        foreach ($item as $key => $value) {
                            $itemArray[$key] = $value;
                        }
                        $processedArrayValue[] = $itemArray;
                    } else {
                        $processedArrayValue[] = $item;
                    }
                }
            } elseif (is_object($arrayValue)) {
                $processedArrayValue = json_decode(json_encode($arrayValue), true);
            } elseif (is_array($arrayValue)) {
                $processedArrayValue = $arrayValue;
            }
            
            // Only process if we have a valid array (empty arrays are valid too for negative blocks)
            if (is_array($processedArrayValue)) {
                Logger::debug("Processing array template blocks for '$arrayKey' using template key '$templateKey'", 'LoaderPreProcess');
                // Process positive array blocks {{@templateKey}}...{{/templateKey}}
                $openTag = '{{@' . $templateKey . '}}';
                $closeTag = '{{/' . $templateKey . '}}';
                self::processJsonArrayBlock($template, $content, $openTag, $closeTag, $processedArrayValue, false);
                
                // Process negative array blocks {{^templateKey}}...{{/templateKey}}
                $negOpenTag = '{{^' . $templateKey . '}}';
                $negCloseTag = '{{/' . $templateKey . '}}';
                self::processJsonArrayBlock($template, $content, $negOpenTag, $negCloseTag, $processedArrayValue, true);
            } else {
            }
        }
    }
    
    private static function processJsonArrayBlock(PreprocessedTemplate $template, string $content, string $openTag, string $closeTag, array $arrayValue, bool $isNegative): void
    {
        $searchPos = 0;
        $mappingCount = 0;

        while ($searchPos < strlen($content)) {
            $openStart = strpos($content, $openTag, $searchPos);
            if ($openStart === false) break;

            $openEnd = $openStart + strlen($openTag);
            $closeStart = strpos($content, $closeTag, $openEnd);
            if ($closeStart === false) break;

            $innerContent = substr($content, $openEnd, $closeStart - $openEnd);
            $fullMatch = $openTag . $innerContent . $closeTag;

            $replacementText = '';

            if ($isNegative) {
                // Negative block: show content only if array is empty
                $replacementText = empty($arrayValue) ? $innerContent : '';
            } else {
                // Positive block: process array content safely
                $replacementText = self::processArrayBlockContentSafely($innerContent, $arrayValue);
            }

            Logger::debug("[LoaderPreProcess]   Creating JSON array mapping: '{$openTag}' -> " . strlen($replacementText) . " chars", 'LoaderPreProcess');
            Logger::debug("[LoaderPreProcess]   Replacement contains {{@: " . (strpos($replacementText, '{{@') !== false ? 'YES - BUG!' : 'NO - OK'), 'LoaderPreProcess');

            $mapping = new ReplacementMapping(
                $fullMatch,
                $replacementText,
                ReplacementType::JSON_PLACEHOLDER,
                $openStart,
                $closeStart + strlen($closeTag)
            );

            $template->replacementMappings[] = $mapping;
            $mappingCount++;

            $searchPos = $closeStart + strlen($closeTag);
        }

        if ($mappingCount > 0) {
            Logger::debug("[LoaderPreProcess] Applied {$mappingCount} JSON array mappings for '{$openTag}'", 'LoaderPreProcess');
        }
    }

    /**
     * Safely processes array block content by iterating through JSON array data and replacing placeholders
     * This method handles all processing logic safely without causing substring errors
     */
    private static function processArrayBlockContentSafely(string $blockContent, array $arrayData): string
    {
        try {
            $mergedBlock = "";

            // Process each item in the array data
            foreach ($arrayData as $item) {
                $itemBlock = $blockContent;

                // Convert item to array if it's an object
                if (is_object($item)) {
                    $item = json_decode(json_encode($item), true);
                }

                if (is_array($item)) {
                    // Replace all placeholders for this item
                    foreach ($item as $key => $value) {
                        $placeholder = '{{$' . $key . '}}';
                        $valueStr = '';
                        if ($value !== null) {
                            if (is_string($value) || is_numeric($value)) {
                                $valueStr = (string)$value;
                            } elseif (is_bool($value)) {
                                $valueStr = $value ? 'true' : 'false';
                            } elseif (is_object($value)) {
                                $valueStr = json_encode($value, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
                            } else {
                                $valueStr = (string)$value;
                            }
                        }
                        Logger::debug("[LoaderPreProcess]   Replacing placeholder (original size: " . strlen($placeholder) . ", replacement size: " . strlen($valueStr) . ")", 'LoaderPreProcess');
                        $itemBlock = self::replaceAllCaseInsensitive($itemBlock, $placeholder, $valueStr);
                    }

                    // Handle conditional blocks for this item safely
                    $itemBlock = self::processConditionalBlocksSafely($itemBlock, $item);
                }

                $mergedBlock .= $itemBlock;
            }

            return $mergedBlock;
        } catch (\Exception $e) {
            // If processing fails, return original content
            return $blockContent;
        }
    }

    /**
     * Helper method to replace all case-insensitive occurrences
     */
    private static function replaceAllCaseInsensitive(string $input, string $search, string $replacement): string
    {
        $idx = 0;
        while (true) {
            $found = stripos($input, $search, $idx);
            if ($found === false) break;
            $input = substr($input, 0, $found) . $replacement . substr($input, $found + strlen($search));
            $idx = $found + strlen($replacement);
        }
        return $input;
    }

    /**
     * Safely processes conditional blocks without causing substring errors
     */
    private static function processConditionalBlocksSafely(string $content, array $jsonItem): string
    {
        try {
            $result = $content;

            // Find all conditional keys in the content
            $conditionalKeys = self::findConditionalKeysInContent($result);

            if (count($conditionalKeys) > 0) {
                Logger::debug("[LoaderPreProcess] Found " . count($conditionalKeys) . " conditional keys: " . implode(", ", $conditionalKeys), 'LoaderPreProcess');
            }

            foreach ($conditionalKeys as $condKey) {
                $condValue = self::getConditionValue($jsonItem, $condKey);
                Logger::debug("[LoaderPreProcess] Processing conditional {{{$condKey}}} with value: " . ($condValue ? 'true' : 'false'), 'LoaderPreProcess');
                $result = self::processConditionalBlockSafely($result, $condKey, $condValue);
            }

            return $result;
        } catch (\Exception $e) {
            // If processing fails, return original content
            Logger::debug("[LoaderPreProcess] Exception in processConditionalBlocksSafely: " . $e->getMessage(), 'LoaderPreProcess');
            return $content;
        }
    }

    /**
     * Helper method to find conditional keys in content
     */
    private static function findConditionalKeysInContent(string $content): array
    {
        $conditionalKeys = [];
        $condIdx = 0;

        while (true) {
            $condStart = stripos($content, "{{@", $condIdx);
            if ($condStart === false) break;
            $condEnd = stripos($content, "}}", $condStart);
            if ($condEnd === false) break;
            $condKey = trim(substr($content, $condStart + 3, $condEnd - ($condStart + 3)));
            $conditionalKeys[$condKey] = true; // Use associative array for uniqueness
            $condIdx = $condEnd + 2;
        }

        return array_keys($conditionalKeys);
    }

    /**
     * Helper method to get condition value from item data
     */
    private static function getConditionValue(array $item, string $condKey): bool
    {
        // First try exact match
        if (isset($item[$condKey])) {
            $condObj = $item[$condKey];
            if ($condObj !== null) {
                if (is_bool($condObj)) {
                    return $condObj;
                } elseif (is_string($condObj) && in_array(strtolower($condObj), ['true', 'false'])) {
                    return strtolower($condObj) === 'true';
                } elseif (is_numeric($condObj)) {
                    return $condObj != 0;
                }
            }
        }

        // If exact match fails, try case-insensitive match
        foreach ($item as $key => $value) {
            if (strcasecmp($key, $condKey) === 0) {
                if ($value !== null) {
                    if (is_bool($value)) {
                        return $value;
                    } elseif (is_string($value) && in_array(strtolower($value), ['true', 'false'])) {
                        return strtolower($value) === 'true';
                    } elseif (is_numeric($value)) {
                        return $value != 0;
                    }
                }
            }
        }

        return false;
    }

    /**
     * Safely processes a single conditional block without causing substring errors
     */
    private static function processConditionalBlockSafely(string $input, string $key, bool $condition): string
    {
        try {
            $originalLength = strlen($input);

            // Support both space variants: {{ /Key}} and {{/Key}}
            $conditionTags = [
                ["{{@" . $key . "}}", "{{ /" . $key . "}}"],
                ["{{@" . $key . "}}", "{{/" . $key . "}}"]
            ];

            foreach ($conditionTags as $tags) {
                list($condStart, $condEnd) = $tags;
                $startIdx = stripos($input, $condStart);
                $endIdx = stripos($input, $condEnd);

                if ($startIdx !== false && $endIdx !== false) {
                    Logger::debug("[LoaderPreProcess] Found conditional block {{@{$key}}} at {$startIdx}, closing at {$endIdx}", 'LoaderPreProcess');
                }

                while ($startIdx !== false && $endIdx !== false) {
                    // Safety check to prevent negative length
                    $contentStart = $startIdx + strlen($condStart);
                    if ($endIdx > $contentStart) {
                        $content = substr($input, $contentStart, $endIdx - $contentStart);
                        $input = $condition
                            ? substr($input, 0, $startIdx) . $content . substr($input, $endIdx + strlen($condEnd))
                            : substr($input, 0, $startIdx) . substr($input, $endIdx + strlen($condEnd));
                        Logger::debug("[LoaderPreProcess] Processed {{@{$key}}} block: condition={$condition}, removed tags", 'LoaderPreProcess');
                    } else {
                        // Malformed conditional block - skip it
                        Logger::debug("[LoaderPreProcess] Malformed conditional block for {{@{$key}}}", 'LoaderPreProcess');
                        break;
                    }

                    $startIdx = stripos($input, $condStart);
                    $endIdx = stripos($input, $condEnd);
                }
            }

            $newLength = strlen($input);
            if ($newLength != $originalLength) {
                Logger::debug("[LoaderPreProcess] processConditionalBlockSafely changed length from {$originalLength} to {$newLength}", 'LoaderPreProcess');
            }

            return $input;
        } catch (\Exception $e) {
            // If processing fails, return original input
            Logger::debug("[LoaderPreProcess] Exception in processConditionalBlockSafely: " . $e->getMessage(), 'LoaderPreProcess');
            return $input;
        }
    }

    // NOTE: The following methods are INTENTIONAL COPIES used across all loaders for architectural separation.
    // Each loader/engine pair is independent to allow individual evolution without shared dependencies.
    // DO NOT extract these to shared utilities - that would create tight coupling.

    /**
     * Builds a parent-child relationship map by analyzing template placeholders
     * Tracks which template is the parent of another based on {{TemplateName}} references
     */
    private static function buildParentMapForPreProcess(PreprocessedSiteTemplates $siteTemplates, string $appSite): array
    {
        $parentMap = [];

        Logger::debug("Building parent map for appSite: $appSite", "LoaderPreProcess");

        foreach ($siteTemplates->templates as $templateKey => $template) {
            // Find all {{TemplateName}} placeholders in this template
            foreach ($template->placeholders as $placeholder) {
                $placeholderName = $placeholder->name;

                // This template (templateKey) is the parent of the placeholder template
                $childTemplateKey = strtolower($appSite) . '_' . strtolower($placeholderName);

                if (!isset($parentMap[$childTemplateKey])) {
                    $parentMap[$childTemplateKey] = $templateKey;
                    Logger::debug("Parent relationship: $childTemplateKey -> parent: $templateKey", "LoaderPreProcess");
                }
            }

            // Also check slotted templates
            foreach ($template->slottedTemplates as $slottedTemplate) {
                $templateName = $slottedTemplate->name;
                $childTemplateKey = strtolower($appSite) . '_' . strtolower($templateName);

                if (!isset($parentMap[$childTemplateKey])) {
                    $parentMap[$childTemplateKey] = $templateKey;
                    Logger::debug("Parent relationship (slotted): $childTemplateKey -> parent: $templateKey", "LoaderPreProcess");
                }
            }
        }

        Logger::debug("Built parent map with " . count($parentMap) . " relationships", "LoaderPreProcess");
        return $parentMap;
    }

    /**
     * Resolves JSON inheritance for all templates by modifying their JsonData in place
     */
    private static function resolveJsonInheritanceForAllTemplates(PreprocessedSiteTemplates $siteTemplates, array $parentMap): void
    {
        foreach ($siteTemplates->templates as $templateKey => $template) {
            if ($template->jsonData === null) {
                continue;
            }

            $resolvedJson = [];
            $hasInheritance = false;

            foreach ($template->jsonData as $key => $value) {
                // Check if this is an inheritable key (ends with #)
                if (str_ends_with($key, '#') && is_string($value)) {
                    $hasInheritance = true;
                    $actualKey = substr($key, 0, -1);
                    $resolvedValue = self::searchParentTreeForKeyPreProcess($actualKey, $templateKey, $siteTemplates->templates, $parentMap);

                    if ($resolvedValue !== null) {
                        $resolvedJson[$actualKey] = $resolvedValue;
                        Logger::debug("Resolved inherited key $key -> $actualKey = $resolvedValue for template $templateKey", "LoaderPreProcess");
                    } else {
                        // Use default value if not found in parents
                        $resolvedJson[$actualKey] = $value;
                        Logger::debug("No inherited value found for $actualKey, using default: $value", "LoaderPreProcess");
                    }
                } else {
                    // Normal key - keep as is
                    $resolvedJson[$key] = $value;
                }
            }

            // Replace JsonData with resolved version if any inheritance was found
            if ($hasInheritance) {
                // Create a new JsonObject with the resolved values
                $newJsonData = new \Assembler\App\Json\JsonObject();
                foreach ($resolvedJson as $key => $val) {
                    $newJsonData->setValue($key, $val);
                }
                $template->jsonData = $newJsonData;
                Logger::debug("Updated JsonData for template $templateKey with resolved inheritance", "LoaderPreProcess");
            }
        }
    }

    /**
     * Recreates JSON placeholder replacement mappings after inheritance resolution
     * This is needed because mappings were created during preprocessing before inheritance was resolved
     */
    private static function recreateJsonPlaceholderMappingsAfterInheritance(PreprocessedSiteTemplates $siteTemplates): void
    {
        foreach ($siteTemplates->templates as $templateKey => $template) {
            if ($template->jsonData === null) {
                continue;
            }

            // Remove old JSON placeholder mappings (both simple placeholders AND array blocks use JsonPlaceholder type)
            $newMappings = [];
            foreach ($template->replacementMappings as $mapping) {
                if ($mapping->type !== ReplacementType::JSON_PLACEHOLDER) {
                    $newMappings[] = $mapping;
                }
            }

            $template->replacementMappings = $newMappings;
            $template->jsonPlaceholders = []; // Clear old json placeholders

            // Recreate JSON array block mappings FIRST (they may contain simple placeholders)
            self::createJsonArrayReplacementMappings($template, $template->originalContent);

            // Then recreate simple JSON placeholder mappings from the resolved JsonData
            self::preprocessJsonTemplates($template); // This will recreate simple JSON placeholders and add them to replacementMappings

            Logger::debug("Recreated JSON placeholder and array mappings for template $templateKey after inheritance resolution", "LoaderPreProcess");
        }
    }

    /**
     * Searches up the parent tree to find a JSON key value
     */
    private static function searchParentTreeForKeyPreProcess(string $key, string $currentTemplateKey, array $allTemplates, array $parentMap): ?string
    {
        // Get parent template key
        if (!isset($parentMap[$currentTemplateKey])) {
            Logger::debug("No parent found for $currentTemplateKey", "LoaderPreProcess");
            return null;
        }

        $parentKey = $parentMap[$currentTemplateKey];
        Logger::debug("Checking parent $parentKey for key $key", "LoaderPreProcess");

        // Get parent's template
        if (!isset($allTemplates[$parentKey])) {
            Logger::debug("Parent template $parentKey not found in templates", "LoaderPreProcess");
            return null;
        }

        $parentTemplate = $allTemplates[$parentKey];

        if ($parentTemplate->jsonData === null) {
            Logger::debug("Parent template $parentKey has no JSON data, searching further up", "LoaderPreProcess");
            // Parent has no JSON, search further up the tree
            return self::searchParentTreeForKeyPreProcess($key, $parentKey, $allTemplates, $parentMap);
        }

        // Look for the key (case-insensitive)
        foreach ($parentTemplate->jsonData as $jsonKvpKey => $jsonKvpValue) {
            if (strcasecmp($jsonKvpKey, $key) === 0) {
                if (is_string($jsonKvpValue)) {
                    Logger::debug("Found key $key in parent $parentKey: $jsonKvpValue", "LoaderPreProcess");
                    return $jsonKvpValue;
                }
            }
        }

        Logger::debug("Key $key not found in parent $parentKey, searching further up", "LoaderPreProcess");
        // Not found in this parent, search further up the tree
        return self::searchParentTreeForKeyPreProcess($key, $parentKey, $allTemplates, $parentMap);
    }
}
?>

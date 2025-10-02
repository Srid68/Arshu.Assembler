<?php

namespace Assembler\TemplateLoader;

use Assembler\App\JsonConverter;
use Assembler\TemplateCommon\TemplateUtils;
use Assembler\TemplateModel\PreprocessedSiteTemplates;
use Assembler\TemplateModel\PreprocessedTemplate;
use Assembler\TemplateModel\TemplatePlaceholder;
use Assembler\TemplateModel\SlottedTemplate;
use Assembler\TemplateModel\SlotPlaceholder;
use Assembler\TemplateModel\JsonPlaceholder;
use Assembler\TemplateModel\ReplacementMapping;
use Assembler\TemplateModel\ReplacementType;

/**
 * Handles loading and preprocessing of HTML templates from the file system
 */
class LoaderPreProcess
{
    private static array $preprocessedTemplatesCache = [];

    /**
     * Loads and preprocesses HTML files from the specified application site directory into structured templates, caching the output per appSite and rootDirName
     * @param string $rootDirPath Root directory path
     * @param string $appSite Application site name
     * @return PreprocessedSiteTemplates PreprocessedSiteTemplates containing structured template data
     */
    public static function loadProcessGetTemplateFiles(string $rootDirPath, string $appSite): PreprocessedSiteTemplates
    {
        $cacheKey = dirname($rootDirPath) . '|' . $appSite;
        
        if (isset(self::$preprocessedTemplatesCache[$cacheKey])) {
            return self::$preprocessedTemplatesCache[$cacheKey];
        }

        $result = new PreprocessedSiteTemplates();
        $result->siteName = $appSite;

        $appSitesPath = $rootDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $appSite;
        
        if (!is_dir($appSitesPath)) {
            self::$preprocessedTemplatesCache[$cacheKey] = $result;
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
                
                $content = file_get_contents($file->getPathname()) ?: '';
                
                // Look for corresponding JSON file
                $jsonFile = substr($file->getPathname(), 0, -5) . '.json'; // Replace .html with .json
                $jsonContent = null;
                
                if (file_exists($jsonFile)) {
                    $jsonContent = file_get_contents($jsonFile) ?: null;
                }

                // Store raw template for backward compatibility
                $result->rawTemplates[$key] = $content;
                $result->templateKeys[] = $key;

                // Preprocess the template with JSON data
                $preprocessed = self::preprocessTemplate($content, $jsonContent, $appSite, $key);
                $result->templates[$key] = $preprocessed;
            }
        }

        // CRITICAL: Create ALL replacement mappings after all templates are loaded
        // This ensures PreProcess engine does ONLY merging, no processing logic
        self::createAllReplacementMappingsForSite($result, $appSite);

        self::$preprocessedTemplatesCache[$cacheKey] = $result;
        return $result;
    }

    /**
     * Preprocesses JSON data into a JsonObject structure for efficient template merging
     * @param string $jsonContent The JSON content to preprocess
     * @return \Assembler\App\Json\JsonObject JsonObject containing preprocessed JSON data
     */
    public static function preprocessJsonData(string $jsonContent): \Assembler\App\Json\JsonObject
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
    private static function preprocessTemplate(string $content, ?string $jsonContent, string $appSite, string $templateKey): PreprocessedTemplate
    {
        $template = new PreprocessedTemplate();
        $template->originalContent = $content;

        if (empty($content)) {
            return $template;
        }

        // Parse JSON data into a structure
        if (!empty($jsonContent)) {
            $template->jsonData = self::preprocessJsonData($jsonContent);
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
    private static function createAllReplacementMappingsForSite(PreprocessedSiteTemplates $siteTemplates, string $appSite): void
    {
        // Phase 1: Create JSON replacement mappings for all templates first (no dependencies)
        foreach ($siteTemplates->templates as $template) {
            // Create replacement mappings for JSON array blocks (including negative blocks)
            self::createJsonArrayReplacementMappings($template, $template->originalContent);
        }

        // Phase 2: Create simple template replacement mappings (may depend on JSON but not on slotted templates)
        foreach ($siteTemplates->templates as $template) {
            // Create replacement mappings for simple placeholders
            self::createPlaceholderReplacementMappings($template, $siteTemplates->templates, $appSite);
        }

        // Phase 3: Create slotted template replacement mappings (may depend on other templates)
        foreach ($siteTemplates->templates as $template) {
            // Create replacement mappings for slotted templates
            self::createSlottedTemplateReplacementMappings($template, $siteTemplates->templates, $appSite);
        }
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
            if (empty($templateName) || !TemplateUtils::isAlphaNumeric($templateName)) {
                $searchPos = $openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            $closeTag = '{{/' . $templateName . '}}';
            $closeStart = TemplateUtils::findMatchingCloseTag(
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
            if (empty($placeholderName) || !TemplateUtils::isAlphaNumeric($placeholderName)) {
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
                
                $closingStart = \Assembler\TemplateCommon\TemplateUtils::findMatchingCloseTag($innerContent, $placeholderEnd, $openingTag, $closingTag);
                
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
                    self::parseNestedTemplatesInSlot($slot, $appSite);

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
    private static function parseNestedTemplatesInSlot(SlotPlaceholder $slot, string $appSite): void
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
        if (!$template->jsonData) return;

        $content = $template->originalContent;

        // Step 1: Create replacement mappings for JSON array blocks
        self::createJsonArrayReplacementMappings($template, $content);

        // Step 2: Create replacement mappings for JSON placeholders  
        // Simple JSON placeholder replacement for {{$key}} and {{key}} patterns
        foreach ($template->jsonData as $key => $value) {
            if (is_string($value)) {
                // Check for {{$key}} pattern first
                $placeholder = '{{$' . $key . '}}';
                if (strpos($content, $placeholder) !== false) {
                    $jsonPlaceholder = new JsonPlaceholder($key, $placeholder, $value);
                    $template->jsonPlaceholders[] = $jsonPlaceholder;
                }
                
                // Also check for {{key}} pattern (without $)
                $placeholder = '{{' . $key . '}}';
                if (strpos($content, $placeholder) !== false) {
                    $jsonPlaceholder = new JsonPlaceholder($key, $placeholder, $value);
                    $template->jsonPlaceholders[] = $jsonPlaceholder;
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
        if (!$template->hasPlaceholders()) return;

        foreach ($template->placeholders as $placeholder) {
            $targetTemplateKey = strtolower($appSite) . '_' . $placeholder->templateKey;
            $targetTemplate = $allTemplates[$targetTemplateKey] ?? null;
            
            if ($targetTemplate) {
                // Use the target template's original content without applying other replacement mappings
                // This prevents circular dependencies and infinite recursion
                $processedTemplate = $targetTemplate->originalContent;

                // Create the replacement mapping
                $mapping = new ReplacementMapping();
                $mapping->originalText = $placeholder->fullMatch;
                $mapping->replacementText = $processedTemplate;
                $mapping->type = ReplacementType::SIMPLE_TEMPLATE;
                
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
            $targetTemplateKey = strtolower($appSite) . '_' . $slottedTemplate->templateKey;
            $targetTemplate = $allTemplates[$targetTemplateKey] ?? null;
            
            if ($targetTemplate) {
                $processedTemplate = $targetTemplate->originalContent;

                // Process slots in the target template
                foreach ($slottedTemplate->slots as $slot) {
                    $processedSlotContent = self::processSlotContentForReplacementMapping($slot, $allTemplates, $appSite);
                    $processedTemplate = str_replace($slot->slotKey, $processedSlotContent, $processedTemplate);
                }

                // Remove remaining slot placeholders
                $processedTemplate = TemplateUtils::removeRemainingSlotPlaceholders($processedTemplate);

                // Create the replacement mapping
                $mapping = new ReplacementMapping();
                $mapping->originalText = $slottedTemplate->fullMatch;
                $mapping->replacementText = $processedTemplate;
                $mapping->type = ReplacementType::SLOTTED_TEMPLATE;
                
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
            $targetTemplateKey = strtolower($appSite) . '_' . $nestedSlottedTemplate->templateKey;
            if (isset($allTemplates[$targetTemplateKey])) {
                $targetTemplate = $allTemplates[$targetTemplateKey];
                
                // Use the target template's original content without applying replacement mappings
                // This prevents circular dependencies during replacement mapping creation
                $processedTemplate = $targetTemplate->originalContent;

                // Process nested slots
                foreach ($nestedSlottedTemplate->slots as $nestedSlot) {
                    $processedNestedSlotContent = self::processSlotContentForReplacementMapping($nestedSlot, $allTemplates, $appSite);
                    $processedTemplate = str_replace($nestedSlot->slotKey, $processedNestedSlotContent, $processedTemplate);
                }

                // Remove remaining slot placeholders
                $processedTemplate = TemplateUtils::removeRemainingSlotPlaceholders($processedTemplate);

                // Replace in result
                $result = str_replace($nestedSlottedTemplate->fullMatch, $processedTemplate, $result);
            }
        }

        // Process nested simple placeholders
        foreach ($slot->nestedPlaceholders as $nestedPlaceholder) {
            $targetTemplateKey = strtolower($appSite) . '_' . $nestedPlaceholder->templateKey;
            if (isset($allTemplates[$targetTemplateKey])) {
                $targetTemplate = $allTemplates[$targetTemplateKey];
                
                // Use the target template's original content
                $processedTemplate = $targetTemplate->originalContent;

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
        // Process JSON placeholders first
        foreach ($template->jsonPlaceholders as $jsonPlaceholder) {
            $mapping = new ReplacementMapping();
            $mapping->originalText = $jsonPlaceholder->placeholder;
            $mapping->replacementText = $jsonPlaceholder->value;
            $mapping->type = ReplacementType::JSON_PLACEHOLDER;
            
            $template->replacementMappings[] = $mapping;
        }
        
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
                error_log("DEBUG: Processing array template blocks for '$arrayKey' using template key '$templateKey'");
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
            
            $mapping = new ReplacementMapping();
            $mapping->originalText = $fullMatch;
            $mapping->replacementText = $replacementText;
            $mapping->type = ReplacementType::JSON_PLACEHOLDER;
            $mapping->startIndex = $openStart;
            $mapping->endIndex = $closeStart + strlen($closeTag);
            
            $template->replacementMappings[] = $mapping;
            
            $searchPos = $closeStart + strlen($closeTag);
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
                                $valueStr = json_encode($value);
                            } else {
                                $valueStr = (string)$value;
                            }
                        }
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

            foreach ($conditionalKeys as $condKey) {
                $condValue = self::getConditionValue($jsonItem, $condKey);
                $result = self::processConditionalBlockSafely($result, $condKey, $condValue);
            }

            return $result;
        } catch (\Exception $e) {
            // If processing fails, return original content
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
            // Support both space variants: {{ /Key}} and {{/Key}}
            $conditionTags = [
                ["{{@" . $key . "}}", "{{ /" . $key . "}}"],
                ["{{@" . $key . "}}", "{{/" . $key . "}}"]
            ];

            foreach ($conditionTags as $tags) {
                list($condStart, $condEnd) = $tags;
                $startIdx = stripos($input, $condStart);
                $endIdx = stripos($input, $condEnd);

                while ($startIdx !== false && $endIdx !== false) {
                    // Safety check to prevent negative length
                    $contentStart = $startIdx + strlen($condStart);
                    if ($endIdx > $contentStart) {
                        $content = substr($input, $contentStart, $endIdx - $contentStart);
                        $input = $condition
                            ? substr($input, 0, $startIdx) . $content . substr($input, $endIdx + strlen($condEnd))
                            : substr($input, 0, $startIdx) . substr($input, $endIdx + strlen($condEnd));
                    } else {
                        // Malformed conditional block - skip it
                        break;
                    }

                    $startIdx = stripos($input, $condStart);
                    $endIdx = stripos($input, $condEnd);
                }
            }

            return $input;
        } catch (\Exception $e) {
            // If processing fails, return original input
            return $input;
        }
    }

    /**
     * Creates replacement mappings for JSON placeholders ({{$key}} patterns)
     * This creates direct string replacements without any processing logic
     */
    private static function createJsonPlaceholderReplacementMappings(PreprocessedTemplate $template, string $content): void
    {
        if ($template->jsonData === null) return;

        foreach ($template->jsonData as $key => $value) {
            if (is_string($value)) {
                // Handle both {{$key}} and {{key}} patterns
                $placeholders = [
                    '{{$' . $key . '}}',
                    '{{' . $key . '}}'
                ];

                foreach ($placeholders as $placeholder) {
                    $searchPos = 0;
                    while ($searchPos < strlen($content)) {
                        $pos = stripos($content, $placeholder, $searchPos);
                        if ($pos === false) break;

                        $mapping = new ReplacementMapping();
                        $mapping->originalText = $placeholder;
                        $mapping->replacementText = $value;
                        $mapping->type = ReplacementType::JSON_PLACEHOLDER;
                        $mapping->startIndex = $pos;
                        $mapping->endIndex = $pos + strlen($placeholder);

                        $template->replacementMappings[] = $mapping;
                        $searchPos = $pos + strlen($placeholder);
                    }
                }
            }
        }
    }
}
?>
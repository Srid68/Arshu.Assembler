<?php

namespace Assembler\TemplateEngine;

use Assembler\App\JsonConverter;
use Assembler\App\Json\JsonObject;
use Assembler\App\Json\JsonArray;
use Assembler\TemplateCommon\TemplateUtils;
use Assembler\TemplateLoader\TemplateResult;

/**
 * IndexOf-based template engine implementation for improved performance
 */
class EngineNormal
{
    private string $appViewPrefix = '';

    public function __construct(string $appViewPrefix = '')
    {
        $this->appViewPrefix = $appViewPrefix;
    }

    public function setAppViewPrefix(string $prefix): void
    {
        $this->appViewPrefix = $prefix;
    }

    public function getAppViewPrefix(): string
    {
        return $this->appViewPrefix;
    }

    /**
     * Merges templates by replacing placeholders with corresponding HTML
     * This is a hybrid method that processes both slotted templates and simple placeholders
     * JSON files with matching names are automatically merged with HTML templates before processing
     * @param string $appSite The application site name for template key generation
     * @param string $appFile The application file name
     * @param string|null $appView The application view name (optional)
     * @param array<string, TemplateResult> $templates Array of available templates
     * @param bool $enableJsonProcessing Whether to enable JSON data processing
     * @return string HTML with placeholders replaced
     */
    public function mergeTemplates(string $appSite, string $appFile, ?string $appView, array $templates, bool $enableJsonProcessing = true): string
    {
        if (empty($templates)) {
            return '';
        }

        // Direct lookup for main template
        $mainTemplateKey = strtolower($appSite) . '_' . strtolower($appFile);
        $mainTemplate = $templates[$mainTemplateKey] ?? null;

        if (!$mainTemplate) {
            // AppView fallback logic
            if ($appView && $this->appViewPrefix && stripos($appFile, $this->appViewPrefix) !== false) {
                $appKey = str_ireplace($this->appViewPrefix, $appView, $appFile);
                $fallbackTemplateKey = strtolower($appSite) . '_' . strtolower($appKey);
                $mainTemplate = $templates[$fallbackTemplateKey] ?? null;
                if (!$mainTemplate) {
                    return '';
                }
            } else {
                return '';
            }
        }

        $contentHtml = $mainTemplate->html;
        if ($enableJsonProcessing && $mainTemplate->json) {
            $contentHtml = $this->mergeTemplateWithJson($contentHtml, $mainTemplate->json);
        }

        // Pre-merge all templates and JSON values
        $mergedTemplates = [];
        $allJsonValues = [];

        foreach ($templates as $key => $template) {
            $htmlContent = $template->html;
            $jsonContent = $template->json;

            if ($enableJsonProcessing && $jsonContent) {
                $htmlContent = $this->mergeTemplateWithJson($htmlContent, $jsonContent);
                try {
                    $jsonObj = JsonConverter::parseJsonString($jsonContent);
                    foreach ($jsonObj as $jsonKey => $jsonValue) {
                        if (is_string($jsonValue)) {
                            $allJsonValues[strtolower($jsonKey)] = $jsonValue;
                        }
                    }
                } catch (\Exception $e) {
                    // Ignore JSON parsing errors
                }
            }
            $mergedTemplates[$key] = $htmlContent;
        }

        // Iterative processing with change detection
        $maxPasses = 10;
        for ($pass = 0; $pass < $maxPasses; $pass++) {
            $before = $contentHtml;
            $afterSlots = $this->mergeTemplateSlots($before, $appSite, $appView, $mergedTemplates);
            $afterJson = $this->replaceTemplatePlaceholdersWithJson($afterSlots, $appSite, $mergedTemplates, $allJsonValues, $appView);
            
            $changed = $afterJson !== $before;
            if (!$changed) break;
            $contentHtml = $afterJson;
        }

        return $contentHtml;
    }

    /**
     * Retrieves a template from the templates array based on various scenarios including AppView fallback logic
     * @param string $appSite Application site
     * @param string $templateName Template name
     * @param array<string, TemplateResult> $templates Available templates
     * @param string|null $appView Application view
     * @param string|null $appViewPrefix Application view prefix
     * @param bool $useAppViewFallback Whether to use AppView fallback
     * @return array Array with 'html' and 'json' keys
     */
    public function getTemplate(string $appSite, string $templateName, array $templates, ?string $appView = null, ?string $appViewPrefix = null, bool $useAppViewFallback = true): array
    {
        if (empty($templates)) {
            return ['html' => null, 'json' => null];
        }

        $viewPrefix = $appViewPrefix ?? $this->appViewPrefix;
        $primaryTemplateKey = strtolower($appSite) . '_' . strtolower($templateName);
        
        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if ($useAppViewFallback && $appView && $viewPrefix && stripos($templateName, $viewPrefix) !== false) {
            $appKey = str_ireplace($viewPrefix, $appView, $templateName);
            $fallbackTemplateKey = strtolower($appSite) . '_' . strtolower($appKey);
            $fallbackTemplate = $templates[$fallbackTemplateKey] ?? null;
            if ($fallbackTemplate) {
                return ['html' => $fallbackTemplate->html, 'json' => $fallbackTemplate->json];
            }
        }

        // SECOND: If no AppView-specific template found, try primary template
        $primaryTemplate = $templates[$primaryTemplateKey] ?? null;
        if ($primaryTemplate) {
            return ['html' => $primaryTemplate->html, 'json' => $primaryTemplate->json];
        }

        return ['html' => null, 'json' => null];
    }

    /**
     * Replace placeholders using both templates and JSON values
     * @param string $html HTML content
     * @param string $appSite Application site
     * @param array<string, string> $htmlFiles HTML template files
     * @param array<string, string> $jsonValues JSON values
     * @param string|null $appView Application view
     * @return string Processed HTML
     */
    private function replaceTemplatePlaceholdersWithJson(string $html, string $appSite, array $htmlFiles, array $jsonValues, ?string $appView = null): string
    {
        $result = $html;
        $searchPos = 0;

        while ($searchPos < strlen($result)) {
            // Look for opening placeholder {{
            $openStart = strpos($result, '{{', $searchPos);
            if ($openStart === false) break;

            // Make sure it's not a slotted template or special placeholder
            if ($openStart + 2 < strlen($result) && 
                in_array($result[$openStart + 2], ['#', '@', '$', '/'])) {
                $searchPos = $openStart + 2;
                continue;
            }

            // Find closing }}
            $closeStart = strpos($result, '}}', $openStart + 2);
            if ($closeStart === false) break;

            // Extract placeholder name
            $placeholderName = trim(substr($result, $openStart + 2, $closeStart - $openStart - 2));
            if (!$placeholderName || !TemplateUtils::isAlphaNumeric($placeholderName)) {
                $searchPos = $openStart + 2;
                continue;
            }

            // Convert htmlFiles array to templates format for getTemplate
            $templatesForGetTemplate = [];
            foreach ($htmlFiles as $key => $value) {
                $templatesForGetTemplate[$key] = new TemplateResult($value, null);
            }

            // Look up replacement in templates
            $templateData = $this->getTemplate(
                $appSite, 
                $placeholderName, 
                $templatesForGetTemplate, 
                $appView, 
                $this->appViewPrefix, 
                true
            );

            $processedReplacement = null;

            if ($templateData['html']) {
                $processedReplacement = $this->replaceTemplatePlaceholdersWithJson(
                    $templateData['html'], 
                    $appSite, 
                    $htmlFiles, 
                    $jsonValues ?: [], 
                    $appView
                );
            } elseif (!empty($jsonValues) && isset($jsonValues[strtolower($placeholderName)])) {
                // If template not found, try JSON value
                $processedReplacement = $jsonValues[strtolower($placeholderName)];
            }

            if ($processedReplacement !== null) {
                $placeholder = substr($result, $openStart, $closeStart + 2 - $openStart);
                $result = str_replace($placeholder, $processedReplacement, $result);
                $searchPos = $openStart + strlen($processedReplacement);
            } else {
                $searchPos = $closeStart + 2;
            }
        }

        return $result;
    }

    /**
     * Merge templates by processing slotted templates
     * @param string $contentHtml HTML content
     * @param string $appSite Application site
     * @param string|null $appView Application view
     * @param array<string, string> $templates Available templates
     * @return string HTML with slots filled
     */
    private function mergeTemplateSlots(string $contentHtml, string $appSite, ?string $appView, array $templates): string
    {
        if (empty($contentHtml) || empty($templates)) {
            return $contentHtml;
        }

        do {
            $previous = $contentHtml;
            $contentHtml = $this->processTemplateSlots($contentHtml, $appSite, $appView, $templates);
        } while ($contentHtml !== $previous);
        
        return $contentHtml;
    }

    /**
     * Helper method to process slotted templates using strpos
     * @param string $contentHtml HTML content
     * @param string $appSite Application site
     * @param string|null $appView Application view
     * @param array<string, string> $templates Available templates
     * @return string Processed HTML
     */
    private function processTemplateSlots(string $contentHtml, string $appSite, ?string $appView, array $templates): string
    {
        $result = $contentHtml;
        $searchPos = 0;

        while ($searchPos < strlen($result)) {
            // Look for opening tag {{#
            $openStart = strpos($result, '{{#', $searchPos);
            if ($openStart === false) break;

            // Find the end of the template name
            $openEnd = strpos($result, '}}', $openStart + 3);
            if ($openEnd === false) break;

            // Extract template name
            $templateName = trim(substr($result, $openStart + 3, $openEnd - $openStart - 3));
            if (empty($templateName) || !TemplateUtils::isAlphaNumeric($templateName)) {
                $searchPos = $openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            $closeTag = '{{/' . $templateName . '}}';
            $closeStart = TemplateUtils::findMatchingCloseTag($result, $openEnd + 2, '{{#' . $templateName . '}}', $closeTag);
            if ($closeStart === false) {
                $searchPos = $openStart + 1;
                continue;
            }

            // Extract inner content
            $innerStart = $openEnd + 2;
            $innerContent = substr($result, $innerStart, $closeStart - $innerStart);

            // Process the template replacement using the GetTemplate method
            $templateData = $this->getTemplate($appSite, $templateName, 
                array_map(function($html) { return (object)['html' => $html, 'json' => null]; }, $templates), 
                $appView, $this->appViewPrefix, true);

            if ($templateData['html']) {
                // Extract slot contents
                $slotContents = $this->extractSlotContents($innerContent, $appSite, $appView, $templates);

                // Replace slots in template
                $processedTemplate = $templateData['html'];
                foreach ($slotContents as $slotKey => $slotValue) {
                    $processedTemplate = str_replace($slotKey, $slotValue, $processedTemplate);
                }

                // Remove any remaining slot placeholders
                $processedTemplate = TemplateUtils::removeRemainingSlotPlaceholders($processedTemplate);

                // Replace the entire slotted section
                $fullMatch = substr($result, $openStart, $closeStart + strlen($closeTag) - $openStart);
                $result = str_replace($fullMatch, $processedTemplate, $result);
                $searchPos = $openStart + strlen($processedTemplate);
            } else {
                $searchPos = $openStart + 1;
            }
        }

        return $result;
    }

    /**
     * Extract slot contents using strpos approach
     * @param string $innerContent Inner content to process
     * @param string $appSite Application site
     * @param string|null $appView Application view
     * @param array<string, string> $templates Available templates
     * @return array<string, string> Slot contents
     */
    private function extractSlotContents(string $innerContent, string $appSite, ?string $appView, array $templates): array
    {
        $slotContents = [];
        $searchPos = 0;

        while ($searchPos < strlen($innerContent)) {
            // Look for slot start {{@HTMLPLACEHOLDER
            $slotStart = strpos($innerContent, '{{@HTMLPLACEHOLDER', $searchPos);
            if ($slotStart === false) break;

            // Find the number (if any) and closing }}
            $afterPlaceholder = $slotStart + 18; // Length of "{{@HTMLPLACEHOLDER"
            $slotNum = '';
            $pos = $afterPlaceholder;

            // Extract slot number
            while ($pos < strlen($innerContent) && ctype_digit($innerContent[$pos])) {
                $slotNum .= $innerContent[$pos];
                $pos++;
            }

            // Check for closing }}
            if ($pos + 1 >= strlen($innerContent) || substr($innerContent, $pos, 2) !== '}}') {
                $searchPos = $slotStart + 1;
                continue;
            }

            $slotOpenEnd = $pos + 2;

            // Find matching closing tag
            $closeTag = empty($slotNum) ? '{{/HTMLPLACEHOLDER}}' : '{{/HTMLPLACEHOLDER' . $slotNum . '}}';
            $openTag = empty($slotNum) ? '{{@HTMLPLACEHOLDER}}' : '{{@HTMLPLACEHOLDER' . $slotNum . '}}';

            $closeStart = TemplateUtils::findMatchingCloseTag($innerContent, $slotOpenEnd, $openTag, $closeTag);
            if ($closeStart === false) {
                $searchPos = $slotStart + 1;
                continue;
            }

            // Extract slot content
            $slotContent = substr($innerContent, $slotOpenEnd, $closeStart - $slotOpenEnd);

            // Generate slot key
            $slotKey = empty($slotNum) ? '{{$HTMLPLACEHOLDER}}' : '{{$HTMLPLACEHOLDER' . $slotNum . '}}';

            // Process both slotted templates AND simple placeholders in slot content
            $recursiveResult = $this->mergeTemplateSlots($slotContent, $appSite, $appView, $templates);
            $recursiveResult = $this->replaceTemplatePlaceholders($recursiveResult, $appSite, $appView, $templates);
            $slotContents[$slotKey] = $recursiveResult;

            $searchPos = $closeStart + strlen($closeTag);
        }

        return $slotContents;
    }

    /**
     * Helper method to process simple placeholders only (without slotted template processing)
     * @param string $html HTML content
     * @param string $appSite Application site
     * @param string|null $appView Application view
     * @param array<string, string> $htmlFiles Available templates
     * @return string Processed HTML
     */
    private function replaceTemplatePlaceholders(string $html, string $appSite, ?string $appView, array $htmlFiles): string
    {
        $result = $html;
        $searchPos = 0;

        // Try to get JSON values from the main template if available
        $jsonValues = null;
        if (isset($htmlFiles['__json_values__']) && !empty($htmlFiles['__json_values__'])) {
            // Parse as key=value pairs separated by newlines (custom format for this fix)
            $lines = explode("\n", $htmlFiles['__json_values__']);
            $jsonValues = [];
            foreach ($lines as $line) {
                $parts = explode('=', $line, 2);
                if (count($parts) === 2) {
                    $jsonValues[trim($parts[0])] = trim($parts[1]);
                }
            }
        }

        while ($searchPos < strlen($result)) {
            // Look for opening placeholder {{
            $openStart = strpos($result, '{{', $searchPos);
            if ($openStart === false) break;

            // Make sure it's not a slotted template or special placeholder
            if ($openStart + 2 < strlen($result) && 
                ($result[$openStart + 2] === '#' || $result[$openStart + 2] === '@' || 
                 $result[$openStart + 2] === '$' || $result[$openStart + 2] === '/')) {
                $searchPos = $openStart + 2;
                continue;
            }

            // Find closing }}
            $closeStart = strpos($result, '}}', $openStart + 2);
            if ($closeStart === false) break;

            // Extract placeholder name
            $placeholderName = trim(substr($result, $openStart + 2, $closeStart - $openStart - 2));
            if (empty($placeholderName) || !TemplateUtils::isAlphaNumeric($placeholderName)) {
                $searchPos = $openStart + 2;
                continue;
            }

            // Look up replacement in templates - use getTemplate method for consistent AppView logic
            $templateData = $this->getTemplate($appSite, $placeholderName, 
                array_map(function($html) { return (object)['html' => $html, 'json' => null]; }, $htmlFiles), 
                $appView, $this->appViewPrefix, true);

            $processedReplacement = null;

            if ($templateData['html']) {
                $processedReplacement = $this->replaceTemplatePlaceholdersWithJson($templateData['html'], $appSite, $htmlFiles, $jsonValues ?: [], $appView);
            } elseif ($jsonValues && isset($jsonValues[$placeholderName])) {
                // If template not found, try JSON value
                $processedReplacement = $jsonValues[$placeholderName];
            }

            if ($processedReplacement !== null) {
                $placeholder = substr($result, $openStart, $closeStart + 2 - $openStart);
                $result = str_replace($placeholder, $processedReplacement, $result);
                $searchPos = $openStart + strlen($processedReplacement);
            } else {
                $searchPos = $closeStart + 2;
            }
        }

        return $result;
    }

    /**
     * Merge JSON data into the HTML template
     * @param string $template HTML template
     * @param string $jsonText JSON data as string
     * @return string Merged HTML
     */
    private function mergeTemplateWithJson(string $template, string $jsonText): string
    {
        // Parse JSON using JsonConverter
        $jsonObject = JsonConverter::parseJsonString($jsonText);
        
        $dict = [];

        // Convert JsonObject to dictionary
        foreach ($jsonObject as $key => $value) {
            if ($value instanceof JsonArray) {
                // Convert JsonArray to array of associative arrays
                $arr = [];
                foreach ($value as $item) {
                    if ($item instanceof JsonObject) {
                        $obj = [];
                        foreach ($item as $subKey => $subValue) {
                            $obj[strtolower($subKey)] = $subValue;
                        }
                        $arr[] = $obj;
                    } else {
                        // Handle array of simple values
                        $simpleObj = ['value' => $item];
                        $arr[] = $simpleObj;
                    }
                }
                $dict[strtolower($key)] = $arr;
            } else {
                $dict[strtolower($key)] = $value;
            }
        }

        // Advanced merge logic for block and conditional patterns
        $result = $template;

        // Instead of finding all array tags first, directly match JSON array keys to template blocks
        foreach ($dict as $jsonKey => $dataList) {
            if (is_array($dataList)) {
                // Try to find a matching template block for this JSON array
                $keyNorm = strtolower($jsonKey);

                // Look for possible template tags that match this JSON key
                $possibleTags = [$jsonKey, $keyNorm, rtrim($keyNorm, 's'), $keyNorm . 's'];

                foreach ($possibleTags as $tag) {
                    $blockStartTag = '{{@' . $tag . '}}';
                    $blockEndTag = '{{/' . $tag . '}}';

                    $startIdx = stripos($result, $blockStartTag);
                    if ($startIdx !== false) {
                        $searchFrom = $startIdx + strlen($blockStartTag);
                        $endIdx = stripos($result, $blockEndTag, $searchFrom);

                        if ($endIdx !== false && $endIdx > $startIdx) {
                            // Found a valid block - process it
                            $contentStartIdx = $startIdx + strlen($blockStartTag);
                            if ($contentStartIdx <= $endIdx) {
                                $blockContent = substr($result, $contentStartIdx, $endIdx - $contentStartIdx);
                                $mergedBlock = '';

                                // Find all conditional blocks in the template block (e.g., {{@Key}}...{{/Key}})
                                $conditionalKeys = [];
                                $condIdx = 0;
                                while (true) {
                                    $condStart = stripos($blockContent, '{{@', $condIdx);
                                    if ($condStart === false) break;
                                    $condEnd = strpos($blockContent, '}}', $condStart);
                                    if ($condEnd === false) break;
                                    $condKey = trim(substr($blockContent, $condStart + 3, $condEnd - ($condStart + 3)));
                                    $conditionalKeys[] = $condKey;
                                    $condIdx = $condEnd + 2;
                                }

                                foreach ($dataList as $item) {
                                    $itemBlock = $blockContent;

                                    // Ensure item is an array
                                    if (!is_array($item)) {
                                        $item = ['value' => $item];
                                    }

                                    // Replace all placeholders dynamically
                                    foreach ($item as $kvpKey => $kvpValue) {
                                        $placeholder = '{{$' . $kvpKey . '}}';
                                        $valueStr = $kvpValue !== null ? (string)$kvpValue : '';
                                        $itemBlock = $this->replaceAllCaseInsensitive($itemBlock, $placeholder, $valueStr);
                                    }

                                    // Handle all conditional blocks dynamically
                                    foreach ($conditionalKeys as $condKey) {
                                        $condValue = false;
                                        $condKeyLower = strtolower($condKey);
                                        if (isset($item[$condKeyLower]) && $item[$condKeyLower] !== null) {
                                            $condObj = $item[$condKeyLower];
                                            if (is_bool($condObj)) {
                                                $condValue = $condObj;
                                            } elseif (is_string($condObj) && in_array(strtolower($condObj), ['true', 'false'])) {
                                                $condValue = strtolower($condObj) === 'true';
                                            } elseif (is_numeric($condObj)) {
                                                $condValue = $condObj != 0;
                                            }
                                        }
                                        $itemBlock = $this->handleConditional($itemBlock, $condKey, $condValue);
                                    }
                                    $mergedBlock .= $itemBlock;
                                }

                                // Replace block in result
                                $result = substr($result, 0, $startIdx) . $mergedBlock . substr($result, $endIdx + strlen($blockEndTag));
                                break; // Process only the first matching template for this JSON key
                            }
                        }
                    }
                }
            }
        }

        // Handle {{^ArrayName}} block if array is empty (dynamic detection)
        foreach ($dict as $key => $value) {
            $emptyBlockStart = '{{^' . $key . '}}';
            $emptyBlockEnd = '{{/' . $key . '}}';
            $emptyStartIdx = stripos($result, $emptyBlockStart);
            $emptyEndIdx = stripos($result, $emptyBlockEnd);
            if ($emptyStartIdx !== false && $emptyEndIdx !== false && is_array($value)) {
                $isEmpty = count($value) == 0;
                $emptyContent = substr($result, $emptyStartIdx + strlen($emptyBlockStart), $emptyEndIdx - ($emptyStartIdx + strlen($emptyBlockStart)));
                $result = $isEmpty
                    ? substr($result, 0, $emptyStartIdx) . $emptyContent . substr($result, $emptyEndIdx + strlen($emptyBlockEnd))
                    : substr($result, 0, $emptyStartIdx) . substr($result, $emptyEndIdx + strlen($emptyBlockEnd));
            }
        }

        // Replace remaining simple placeholders
        foreach ($dict as $key => $value) {
            if (is_string($value)) {
                $placeholder = '{{$' . $key . '}}';
                $result = $this->replaceAllCaseInsensitive($result, $placeholder, $value);
            }
        }

        return $result;
    }

    /**
     * Case-insensitive string replacement
     * @param string $input Input string
     * @param string $search Search string
     * @param string $replacement Replacement string
     * @return string Result string
     */
    private function replaceAllCaseInsensitive(string $input, string $search, string $replacement): string
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
     * Handle conditional blocks
     * @param string $input Input string
     * @param string $key Conditional key
     * @param bool $condition Condition value
     * @return string Processed string
     */
    private function handleConditional(string $input, string $key, bool $condition): string
    {
        // Support spaces inside block tags, e.g. {{@Selected}} ... {{ /Selected}}
        $condStart = '{{@' . $key . '}}';
        $condEnd = '{{ /' . $key . '}}';
        $startIdx = stripos($input, $condStart);
        $endIdx = stripos($input, $condEnd);
        while ($startIdx !== false && $endIdx !== false) {
            $content = substr($input, $startIdx + strlen($condStart), $endIdx - ($startIdx + strlen($condStart)));
            $input = $condition
                ? substr($input, 0, $startIdx) . $content . substr($input, $endIdx + strlen($condEnd))
                : substr($input, 0, $startIdx) . substr($input, $endIdx + strlen($condEnd));
            $startIdx = stripos($input, $condStart);
            $endIdx = stripos($input, $condEnd);
        }
        // Also handle without space: {{/Selected}}
        $condEnd = '{{/' . $key . '}}';
        $startIdx = stripos($input, $condStart);
        $endIdx = stripos($input, $condEnd);
        while ($startIdx !== false && $endIdx !== false) {
            $content = substr($input, $startIdx + strlen($condStart), $endIdx - ($startIdx + strlen($condStart)));
            $input = $condition
                ? substr($input, 0, $startIdx) . $content . substr($input, $endIdx + strlen($condEnd))
                : substr($input, 0, $startIdx) . substr($input, $endIdx + strlen($condEnd));
            $startIdx = stripos($input, $condStart);
            $endIdx = stripos($input, $condEnd);
        }
        return $input;
    }
}
?>
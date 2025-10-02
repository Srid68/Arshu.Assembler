<?php

namespace Assembler\TemplateEngine;

use Assembler\TemplateModel\PreprocessedTemplate;
use Assembler\TemplateModel\ReplacementType;

/**
 * PreProcess template engine implementation that only does merging using preprocessed data structures
 * All parsing is done by TemplateLoader, this engine only handles merging
 */
class EnginePreProcess 
{
    private string $appViewPrefix;

    public function __construct(string $appViewPrefix = '') 
    {
        $this->appViewPrefix = $appViewPrefix;
    }

    public function getAppViewPrefix(): string 
    {
        return $this->appViewPrefix;
    }

    public function setAppViewPrefix(string $appViewPrefix): void 
    {
        $this->appViewPrefix = $appViewPrefix;
    }

    /**
     * Merges templates using preprocessed data structures
     * This method only does merging using preprocessed data structures - no loading or parsing
     * @param string $appSite The application site name for template key generation
     * @param string $appFile The application file name
     * @param string|null $appView The application view name (optional)
     * @param array<string, PreprocessedTemplate> $preprocessedTemplates Dictionary of preprocessed templates for this specific appSite
     * @param bool $enableJsonProcessing Whether to enable JSON data processing
     * @return string HTML with placeholders replaced using preprocessed structures
     */
    public function mergeTemplates(string $appSite, string $appFile, ?string $appView, array $preprocessedTemplates, bool $enableJsonProcessing = true): string 
    {
        if (empty($preprocessedTemplates)) {
            return '';
        }

        // Use the getTemplate method to retrieve the main template
        $mainPreprocessed = $this->getTemplate($appSite, $appFile, $preprocessedTemplates, $appView, $this->appViewPrefix, true);
        if ($mainPreprocessed === null) {
            return '';
        }

        // Start with original content
        $contentHtml = $mainPreprocessed->originalContent;

        // Apply ALL replacement mappings from ALL templates (TemplateLoader did all the processing)
        $contentHtml = $this->applyTemplateReplacements($contentHtml, $preprocessedTemplates, $enableJsonProcessing, $appView);

        return $contentHtml;
    }

    /**
     * Retrieves a template from the preprocessed templates dictionary based on various scenarios including AppView fallback logic
     * @param string $appSite The application site name
     * @param string $templateName The template name (can be appFile or placeholderName)
     * @param array<string, PreprocessedTemplate> $preprocessedTemplates Dictionary of preprocessed templates
     * @param string|null $appView The application view name (optional)
     * @param string|null $appViewPrefix The application view prefix (optional, uses instance property if not provided)
     * @param bool $useAppViewFallback Whether to apply AppView fallback logic
     * @return PreprocessedTemplate|null The template if found, null otherwise
     */
    private function getTemplate(string $appSite, string $templateName, array $preprocessedTemplates, ?string $appView = null, ?string $appViewPrefix = null, bool $useAppViewFallback = true): ?PreprocessedTemplate 
    {
        if (empty($preprocessedTemplates)) {
            return null;
        }

        $viewPrefix = $appViewPrefix ?? $this->appViewPrefix;
        
        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if ($useAppViewFallback && !empty($appView) && !empty($viewPrefix) && stripos($templateName, $viewPrefix) !== false) {
            // Direct replacement: Replace the AppViewPrefix with the AppView value
            // For example: Html3AContent with AppViewPrefix=Html3A and AppView=html3B becomes html3BContent
            $appKey = $this->replaceAllCaseInsensitive($templateName, $viewPrefix, $appView);
            $fallbackTemplateKey = strtolower($appSite) . '_' . strtolower($appKey);
            if (isset($preprocessedTemplates[$fallbackTemplateKey])) {
                return $preprocessedTemplates[$fallbackTemplateKey]; // Found AppView-specific template, use it
            }
        }

        // SECOND: If no AppView-specific template found, try primary template
        $primaryTemplateKey = strtolower($appSite) . '_' . strtolower($templateName);
        if (isset($preprocessedTemplates[$primaryTemplateKey])) {
            return $preprocessedTemplates[$primaryTemplateKey];
        }

        return null;
    }

    /**
     * Applies all replacement mappings from all templates - NO processing logic, only direct replacements
     * @param string $content The content to apply replacements to
     * @param array<string, PreprocessedTemplate> $preprocessedTemplates Dictionary of preprocessed templates
     * @param bool $enableJsonProcessing Whether to enable JSON processing
     * @param string|null $appView The application view name
     * @return string Content with all replacements applied
     */
    private function applyTemplateReplacements(string $content, array $preprocessedTemplates, bool $enableJsonProcessing, ?string $appView): string 
    {
        $result = $content;

        // Apply replacement mappings from all templates in multiple passes until no more changes
        $maxPasses = 10; // Prevent infinite loops
        $currentPass = 0;
        
        do {
            $previous = $result;
            $currentPass++;
            
            // Apply replacement mappings from all templates
            foreach ($preprocessedTemplates as $template) {
                // CRITICAL: Apply slotted template mappings FIRST (before JSON processing changes the content)
                foreach ($template->replacementMappings as $mapping) {
                    if ($mapping->type === ReplacementType::SLOTTED_TEMPLATE) {
                        if (strpos($result, $mapping->originalText) !== false) {
                            $result = str_replace($mapping->originalText, $mapping->replacementText, $result);
                        }
                    }
                }
                
                // Then apply other replacement mappings (simple templates) with AppView logic
                foreach ($template->replacementMappings as $mapping) {
                    if ($mapping->type === ReplacementType::SIMPLE_TEMPLATE) {
                        if (strpos($result, $mapping->originalText) !== false) {
                            // Apply AppView logic before replacement
                            $replacementText = $this->applyAppViewLogicToReplacement($mapping->originalText, $mapping->replacementText, $preprocessedTemplates, $appView);
                            $result = str_replace($mapping->originalText, $replacementText, $result);
                        }
                    }
                }
                
                // Apply JSON replacement mappings only if JSON processing is enabled
                if ($enableJsonProcessing) {
                    foreach ($template->replacementMappings as $mapping) {
                        if ($mapping->type === ReplacementType::JSON_PLACEHOLDER) {
                            if (strpos($result, $mapping->originalText) !== false) {
                                $result = str_replace($mapping->originalText, $mapping->replacementText, $result);
                            }
                        }
                    }
                }
                
                // Apply JSON placeholders if JSON processing is enabled (LAST)
                if ($enableJsonProcessing) {
                    foreach ($template->jsonPlaceholders as $placeholder) {
                        $result = $this->replaceAllCaseInsensitive($result, $placeholder->placeholder, $placeholder->value);
                    }
                }
            }
            
        } while ($result !== $previous && $currentPass < $maxPasses);

        return $result;
    }

    /**
     * Helper method to replace all case-insensitive occurrences
     * @param string $input Input string
     * @param string $search Search string
     * @param string $replacement Replacement string
     * @return string String with all occurrences replaced
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
     * Applies AppView fallback logic to template replacement text using the centralized getTemplate method
     * @param string $originalText Original placeholder text
     * @param string $replacementText Current replacement text
     * @param array<string, PreprocessedTemplate> $preprocessedTemplates Dictionary of preprocessed templates
     * @param string|null $appView The application view name
     * @return string Updated replacement text with AppView logic applied
     */
    private function applyAppViewLogicToReplacement(string $originalText, string $replacementText, array $preprocessedTemplates, ?string $appView): string 
    {
        // Check if the original text is a placeholder that should use AppView fallback logic
        // Extract placeholder name from {{PlaceholderName}} format
        $placeholderName = $this->extractPlaceholderName($originalText);
        if (empty($placeholderName)) {
            return $replacementText;
        }

        // Use the centralized getTemplate method for consistent AppView logic
        // First get the appSite from the template key pattern
        $sampleKey = array_keys($preprocessedTemplates)[0] ?? '';
        if (empty($sampleKey)) {
            return $replacementText;
        }
            
        $parts = explode('_', $sampleKey);
        if (count($parts) < 2) {
            return $replacementText;
        }
            
        $appSite = $parts[0]; // Extract appSite from the key pattern
        
        $template = $this->getTemplate($appSite, $placeholderName, $preprocessedTemplates, $appView, $this->appViewPrefix, true);
        
        return $template?->originalContent ?? $replacementText;
    }

    /**
     * Extracts placeholder name from {{PlaceholderName}} format
     * @param string $originalText Original placeholder text
     * @return string Extracted placeholder name or empty string
     */
    private function extractPlaceholderName(string $originalText): string 
    {
        if (empty($originalText) || !str_starts_with($originalText, '{{') || !str_ends_with($originalText, '}}')) {
            return '';
        }
        
        return trim(substr($originalText, 2, -2));
    }
}

?>
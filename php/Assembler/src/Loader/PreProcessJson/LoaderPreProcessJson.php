<?php

namespace Assembler\Loader\PreProcessJson;

use Assembler\Interface\ILoaderJson;

use Arshu\Common\Logger;
use Assembler\Common\CommonUtil;
use Assembler\Model\PreprocessedTemplate;
use Assembler\Model\SlottedTemplate;
use Assembler\Model\SlotPlaceholder;
use Assembler\Model\TemplatePlaceholder;
use Assembler\Model\ReplacementMapping;
use Assembler\Model\ReplacementType;
use Assembler\Model\JsonPlaceholder;
use Assembler\App\JsonConverter;

class LoaderPreProcessJson implements ILoaderJson
{
    private static array $preprocessedTemplatesCache = [];
    private array $templates;
    private string $searchAppSites;
    private string $rootDirPath;
    private string $appSite;

    public function __construct(string $rootDirPath, string $appSite, string $searchAppSites)
    {
        $this->searchAppSites = $searchAppSites;
        $this->templates = [];
        $this->rootDirPath = $rootDirPath;
        $this->appSite = $appSite;
        $this->load();
    }

    private function load(): void
    {
        $siteTemplates = $this->loadProcessGetTemplateFiles($this->rootDirPath, $this->appSite);
        $this->templates = $siteTemplates['templates'];

        if (!empty($this->searchAppSites)) {
            $searchAppSitesArray = explode(',', $this->searchAppSites);
            foreach ($searchAppSitesArray as $searchAppSite) {
                $trimmedSite = trim($searchAppSite);
                if ($trimmedSite) {
                    $searchSiteTemplates = $this->loadProcessGetTemplateFiles($this->rootDirPath, $trimmedSite);
                    foreach ($searchSiteTemplates['templates'] as $key => $value) {
                        if (!isset($this->templates[$key])) {
                            $this->templates[$key] = $value;
                        }
                    }
                }
            }
        }
    }

    public function getTemplateHtml(string $appSite, string $templateName, ?string $appView = null, ?string $appViewPrefix = null): ?PreprocessedTemplate
    {
        return $this->getTemplateInternal($appSite, $templateName, $appView, $appViewPrefix);
    }

    public function getTemplateJson(string $appSite, string $templateName): ?\Assembler\App\Json\JsonObject
    {
        $template = $this->getTemplateInternal($appSite, $templateName, null, null);
        return $template ? $template->getJsonData() : null;
    }

    public function hasTemplate(string $appSite, string $templateName): bool
    {
        $key = strtolower($appSite) . '_' . strtolower($templateName);
        return isset($this->templates[$key]);
    }

    public static function clearCache(): void
    {
        self::$preprocessedTemplatesCache = [];
    }

    public function getAllTemplates(): array
    {
        return $this->templates;
    }

    private function getTemplateInternal(string $appSite, string $templateName, ?string $appView, ?string $appViewPrefix): ?PreprocessedTemplate
    {
        if ($appView && $appViewPrefix && stripos($templateName, $appViewPrefix) !== false) {
            $appKey = CommonUtil::replaceCaseInsensitive($templateName, $appViewPrefix, $appView);
            $fallbackKey = strtolower($appSite) . '_' . strtolower($appKey);
            if (isset($this->templates[$fallbackKey])) {
                return $this->templates[$fallbackKey];
            }
        }

        $primaryKey = strtolower($appSite) . '_' . strtolower($templateName);
        if (isset($this->templates[$primaryKey])) {
            return $this->templates[$primaryKey];
        }

        if (!empty($this->searchAppSites)) {
            $searchAppSitesArray = explode(',', $this->searchAppSites);
            foreach ($searchAppSitesArray as $searchAppSite) {
                $trimmedSite = trim($searchAppSite);
                if ($trimmedSite) {
                    $searchKey = strtolower($trimmedSite) . '_' . strtolower($templateName);
                    if (isset($this->templates[$searchKey])) {
                        Logger::debug("Template '$templateName' not found in '$appSite', using fallback from '$trimmedSite'", 'LoaderPreProcessJson');
                        return $this->templates[$searchKey];
                    }
                }
            }
        }
        return null;
    }

    public function mergeHtmlWithJson(string $html, string $appSite, string $templateName): string
    {
        if (empty($html)) {
            return $html;
        }

        // Get the preprocessed template which has JSON with inheritance already resolved
        $template = $this->getTemplateInternal($appSite, $templateName, null, null);

        if (!$template || !$template->getJsonData()) {
            Logger::debug("No JSON data found for $templateName, returning original HTML", 'LoaderPreProcessJson');
            return $html;
        }

        Logger::debug("Merging HTML with JSON for $templateName", 'LoaderPreProcessJson');
        return \Assembler\Engine\JsonMergeUtil::mergeTemplateWithJson($html, $template->getJsonData());
    }

    public function applyAllReplacementMappings(string $content, string $appSite, mixed $mainTemplate, ?string $appView, ?string $appViewPrefix, bool $enableJsonProcessing): string
    {
        $result = $content;

        Logger::debug("Starting ApplyAllReplacementMappings, initial size: " . strlen($content), 'LoaderPreProcessJson');

        // Cast mainTemplate to PreprocessedTemplate
        $mainPreprocessed = null;
        if ($mainTemplate instanceof PreprocessedTemplate) {
            $mainPreprocessed = $mainTemplate;
        }

        // Apply replacement mappings from all templates in multiple passes until no more changes
        $maxPasses = 10; // Prevent infinite loops
        $currentPass = 0;

        do {
            $previous = $result;
            $currentPass++;

            Logger::debug("Replacement pass $currentPass, current size: " . strlen($result), 'LoaderPreProcessJson');

            $slottedCount = 0;
            $simpleCount = 0;
            $jsonPlaceholderCount = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template
            if ($mainPreprocessed && $currentPass == 1 && $enableJsonProcessing) {
                foreach ($mainPreprocessed->getReplacementMappings() as $mapping) {
                    if ($mapping->getType() !== ReplacementType::JSON_PLACEHOLDER) {
                        continue;
                    }

                    if (strpos($result, $mapping->getOriginalText()) !== false) {
                        Logger::debug("Applying main template JSON placeholder: " . $mapping->getOriginalText() . " -> " . $mapping->getReplacementText(), 'LoaderPreProcessJson');
                        $result = str_replace($mapping->getOriginalText(), $mapping->getReplacementText(), $result);
                        $jsonPlaceholderCount++;
                    }
                }
            }

            // Apply replacement mappings from all templates
            foreach ($this->templates as $template) {
                // Apply slotted template mappings - engine retrieves and merges JSON
                foreach ($template->getReplacementMappings() as $mapping) {
                    if ($mapping->getType() !== ReplacementType::SLOTTED_TEMPLATE) {
                        continue;
                    }

                    if (strpos($result, $mapping->getOriginalText()) !== false) {
                        // Get replacement text and merge JSON using loader's centralized method
                        $replacementText = $mapping->getReplacementText();
                        if ($enableJsonProcessing && !empty($mapping->getTargetTemplateName())) {
                            $replacementText = $this->mergeHtmlWithJson($replacementText, $appSite, $mapping->getTargetTemplateName());
                            Logger::debug("After merging JSON for slotted template " . $mapping->getTargetTemplateName() . ": " . strlen($replacementText) . " chars", 'LoaderPreProcessJson');
                        }

                        $originalTextSubstr = substr($mapping->getOriginalText(), 0, min(50, strlen($mapping->getOriginalText())));
                        Logger::debug("Applying slotted template: $originalTextSubstr... -> " . strlen($replacementText) . " chars", 'LoaderPreProcessJson');
                        $result = str_replace($mapping->getOriginalText(), $replacementText, $result);
                        $slottedCount++;
                    }
                }

                // Apply simple template mappings (components) - engine retrieves and merges JSON
                foreach ($template->getReplacementMappings() as $mapping) {
                    if ($mapping->getType() !== ReplacementType::SIMPLE_TEMPLATE) {
                        continue;
                    }

                    if (strpos($result, $mapping->getOriginalText()) !== false) {
                        // Get replacement text and merge JSON using loader's centralized method
                        $replacementText = $mapping->getReplacementText();

                        // Handle AppView logic if needed
                        if (!empty($appView) && !empty($mapping->getTargetTemplateName())) {
                            $appViewTemplate = $this->getTemplate($appSite, $mapping->getTargetTemplateName(), $appView, $appViewPrefix, true);
                            if ($appViewTemplate) {
                                $replacementText = $appViewTemplate->getOriginalContent();
                            }
                        }

                        // Merge JSON using loader's centralized method
                        if ($enableJsonProcessing && !empty($mapping->getTargetTemplateName())) {
                            $replacementText = $this->mergeHtmlWithJson($replacementText, $appSite, $mapping->getTargetTemplateName());
                            Logger::debug("After merging JSON for simple template " . $mapping->getTargetTemplateName() . ": " . strlen($replacementText) . " chars", 'LoaderPreProcessJson');
                        }

                        Logger::debug("Applying simple template: " . $mapping->getOriginalText() . " -> " . strlen($replacementText) . " chars", 'LoaderPreProcessJson');
                        $result = str_replace($mapping->getOriginalText(), $replacementText, $result);
                        $simpleCount++;
                    }
                }
            }

            Logger::debug("Pass $currentPass applied: $jsonPlaceholderCount main JSON placeholders, $slottedCount slotted, $simpleCount simple", 'LoaderPreProcessJson');

        } while ($result !== $previous && $currentPass < $maxPasses);

        Logger::debug("Replacement complete after $currentPass passes, final size: " . strlen($result), 'LoaderPreProcessJson');

        return $result;
    }

    private function getTemplate(string $appSite, string $templateName, ?string $appView = null, ?string $appViewPrefix = null, bool $useAppViewFallback = true): ?PreprocessedTemplate
    {
        if (empty($this->templates)) {
            return null;
        }

        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if ($useAppViewFallback && !empty($appView) && !empty($appViewPrefix) && stripos($templateName, $appViewPrefix) !== false) {
            // Direct replacement: Replace the AppViewPrefix with the AppView value
            $appKey = CommonUtil::replaceCaseInsensitive($templateName, $appViewPrefix, $appView);
            $fallbackTemplateKey = strtolower($appSite) . '_' . strtolower($appKey);
            if (isset($this->templates[$fallbackTemplateKey])) {
                return $this->templates[$fallbackTemplateKey]; // Found AppView-specific template, use it
            }
        }

        // SECOND: If no AppView-specific template found, try primary template
        $primaryTemplateKey = strtolower($appSite) . '_' . strtolower($templateName);
        if (isset($this->templates[$primaryTemplateKey])) {
            return $this->templates[$primaryTemplateKey];
        }

        return null;
    }

    public function getSearchAppSites(): string
    {
        return $this->searchAppSites;
    }

    public function getAllTemplatesJson(): string
    {
        // Create a map for all template data
        $templatesData = [];

        foreach ($this->templates as $key => $template) {
            $templatesData[$key] = [
                'html' => $template->getOriginalContent(),
                'json' => $template->getJsonData()
            ];
        }

        // Serialize to JSON
        return json_encode($templatesData);
    }

    private function loadProcessGetTemplateFiles(string $rootDirPath, string $appSite): array
    {
        Logger::debug("LoadProcessGetTemplateFiles called for appSite: $appSite", 'LoaderPreProcessJson');
        $cacheKey = dirname($rootDirPath) . '|' . $appSite;

        if (isset(self::$preprocessedTemplatesCache[$cacheKey])) {
            $cached = self::$preprocessedTemplatesCache[$cacheKey];
            Logger::debug("Returning cached templates for $appSite (" . count($cached['templates']) . " templates)", 'LoaderPreProcessJson');
            return $cached;
        }

        $result = [
            'siteName' => $appSite,
            'templates' => [],
            'rawTemplates' => [],
            'templateKeys' => [],
        ];

        $appSitesPath = $rootDirPath . DIRECTORY_SEPARATOR . 'AppSites' . DIRECTORY_SEPARATOR . $appSite;

        if (!is_dir($appSitesPath)) {
            Logger::warn("AppSites directory not found: $appSitesPath", 'LoaderPreProcessJson');
            self::$preprocessedTemplatesCache[$cacheKey] = $result;
            return $result;
        }

        Logger::debug("Loading templates from: $appSitesPath", 'LoaderPreProcessJson');
        $files = new \RecursiveIteratorIterator(new \RecursiveDirectoryIterator($appSitesPath));

        foreach ($files as $file) {
            if ($file->isFile() && $file->getExtension() == 'html') {
                $fileName = $file->getBasename('.html');
                $key = strtolower($appSite) . '_' . strtolower($fileName);
                $content = CommonUtil::normalizeFileContent(file_get_contents($file->getPathname()));
                    Logger::debug("TEMPLATE LOAD: key=$key, file={$file->getPathname()}, size=" . strlen($content) . ", content-preview=" . substr($content,0,80), 'LoaderPreProcessJson');
                Logger::debug("Loading template: $key (size: " . strlen($content) . ")", 'LoaderPreProcessJson');

                $jsonFile = $file->getPath() . DIRECTORY_SEPARATOR . $fileName . '.json';
                $jsonData = null;
                if (file_exists($jsonFile)) {
                    $jsonContent = CommonUtil::normalizeFileContent(file_get_contents($jsonFile));
                    if ($jsonContent) {
                        $jsonData = json_decode($jsonContent, true);
                        Logger::debug("Found JSON file for $key, parsed to JsonObject", 'LoaderPreProcessJson');
                    }
                }

                $result['rawTemplates'][$key] = $content;
                $result['templateKeys'][] = $key;


                    $preprocessed = $this->preprocessTemplate($content, $jsonData, $appSite, $key);
                    Logger::debug("PREPROCESS: key=$key, original-size=" . strlen($preprocessed->getOriginalContent()) . ", replacements=" . count($preprocessed->getReplacementMappings()) . ", slotted=" . count($preprocessed->getSlottedTemplates()) . ", placeholders=" . count($preprocessed->getPlaceholders()), 'LoaderPreProcessJson');
                    $result['templates'][$key] = $preprocessed;

                    Logger::debug("Preprocessed $key: " . count($preprocessed->getReplacementMappings()) . " replacements, " . count($preprocessed->getSlottedTemplates()) . " slotted, " . count($preprocessed->getPlaceholders()) . " placeholders", 'LoaderPreProcessJson');
            }
        }

        Logger::debug("Loaded " . count($result['templates']) . " templates for $appSite", 'LoaderPreProcessJson');
        $this->createAllReplacementMappingsForSite($result, $appSite);
        Logger::debug("Created all replacement mappings for $appSite", 'LoaderPreProcessJson');

        self::$preprocessedTemplatesCache[$cacheKey] = $result;
        return $result;
    }

    private function preprocessTemplate(string $content, ?array $jsonData, string $appSite, string $templateKey): PreprocessedTemplate
    {
        $template = new PreprocessedTemplate($content, $jsonData);
        if (empty($content)) return $template;

        $this->parseSlottedTemplates($content, $appSite, $template);
        $this->parsePlaceholderTemplates($content, $appSite, $template);

        if ($template->hasJsonData()) {
            $this->preprocessJsonTemplates($template);
        }

        return $template;
    }

    private function createAllReplacementMappingsForSite(array &$siteTemplates, string $appSite): void
    {
        Logger::debug("createAllReplacementMappingsForSite called for appSite: $appSite", 'LoaderPreProcessJson');
        // Phase 0: Build parent map and resolve JSON inheritance BEFORE creating any mappings
        Logger::debug("Creating replacement mappings for $appSite - Phase 0: JSON inheritance", 'LoaderPreProcessJson');
        $parentMap = $this->buildParentMapForPreProcessJson($siteTemplates, $appSite);
        $this->resolveJsonInheritanceForAllTemplatesJson($siteTemplates, $parentMap);
        $this->recreateJsonPlaceholderMappingsAfterInheritanceJson($siteTemplates);
        Logger::debug("Phase 0 (JSON inheritance) complete for $appSite.", 'LoaderPreProcessJson');

        Logger::debug("Creating replacement mappings for $appSite - Phase 1: JSON arrays", 'LoaderPreProcessJson');
        foreach ($siteTemplates['templates'] as $templateKey => $template) {
            Logger::debug("  Processing template $templateKey for JSON array mappings.", 'LoaderPreProcessJson');
            $this->createJsonArrayReplacementMappings($template, $template->getOriginalContent());
        }
        Logger::debug("Phase 1 (JSON arrays) complete for $appSite. Total mappings: " . $this->getTotalReplacementMappings($siteTemplates), 'LoaderPreProcessJson');

        Logger::debug("Creating replacement mappings for $appSite - Phase 2: Simple placeholders", 'LoaderPreProcessJson');
        foreach ($siteTemplates['templates'] as $templateKey => $template) {
            Logger::debug("  Processing template $templateKey for simple placeholder mappings.", 'LoaderPreProcessJson');
            $this->createPlaceholderReplacementMappings($template, $siteTemplates['templates'], $appSite);
        }
        Logger::debug("Phase 2 (Simple placeholders) complete for $appSite. Total mappings: " . $this->getTotalReplacementMappings($siteTemplates), 'LoaderPreProcessJson');

        Logger::debug("Creating replacement mappings for $appSite - Phase 3: Slotted templates", 'LoaderPreProcessJson');
        foreach ($siteTemplates['templates'] as $templateKey => $template) {
            Logger::debug("  Processing template $templateKey for slotted template mappings.", 'LoaderPreProcessJson');
            $this->createSlottedTemplateReplacementMappings($template, $siteTemplates['templates'], $appSite);
        }
        Logger::debug("Phase 3 (Slotted templates) complete for $appSite. Total mappings: " . $this->getTotalReplacementMappings($siteTemplates), 'LoaderPreProcessJson');

        $totalMappings = 0;
        foreach ($siteTemplates['templates'] as $template) {
            $totalMappings += count($template->getReplacementMappings());
        }
        Logger::info("Total replacement mappings created for $appSite: $totalMappings", 'LoaderPreProcessJson');
    }

    private function getTotalReplacementMappings(array $siteTemplates): int
    {
        $total = 0;
        foreach ($siteTemplates['templates'] as $template) {
            $total += count($template->getReplacementMappings());
        }
        return $total;
    }

    private function createPlaceholderReplacementMappings(PreprocessedTemplate $template, array $allTemplates, string $appSite): void
    {
        if (!$template->hasPlaceholders()) return;

        foreach ($template->getPlaceholders() as $placeholder) {
            $targetTemplateKey = strtolower($appSite) . '_' . $placeholder->getTemplateKey();
            if (isset($allTemplates[$targetTemplateKey])) {
                $targetTemplate = $allTemplates[$targetTemplateKey];
                $processedTemplate = $targetTemplate->getOriginalContent();

                Logger::debug("Creating replacement mapping: " . $placeholder->getFullMatch() . " -> " . $placeholder->getTemplateKey(), 'LoaderPreProcessJson');
                $template->addReplacementMapping(new ReplacementMapping(
                    $placeholder->getFullMatch(),
                    $processedTemplate,
                    ReplacementType::SIMPLE_TEMPLATE,
                    0,
                    0,
                    $placeholder->getTemplateKey()
                ));
            }
        }
    }

    private function createSlottedTemplateReplacementMappings(PreprocessedTemplate $template, array $allTemplates, string $appSite): void
    {
        if (!$template->hasSlottedTemplates()) return;

        foreach ($template->getSlottedTemplates() as $slottedTemplate) {
            $fullMatch = $slottedTemplate->getFullMatch();
            $targetTemplateKey = strtolower($appSite) . '_' . $slottedTemplate->getTemplateKey();

            if (isset($allTemplates[$targetTemplateKey])) {
                $targetTemplate = $allTemplates[$targetTemplateKey];
                $processedTemplate = $targetTemplate->getOriginalContent();

                foreach ($slottedTemplate->getSlots() as $slot) {
                    $processedSlotContent = $this->processSlotContentForReplacementMapping($slot, $allTemplates, $appSite);
                    $processedTemplate = str_replace($slot->getSlotKey(), $processedSlotContent, $processedTemplate);
                }

                if (count($slottedTemplate->getSlots()) === 0) {
                    $actualInnerContent = $slottedTemplate->getInnerContent();
                    if (trim($actualInnerContent)) {
                        $defaultSlotKey = '{{$HTMLPLACEHOLDER}}';
                        if (strpos($processedTemplate, $defaultSlotKey) !== false) {
                            $processedTemplate = str_replace($defaultSlotKey, trim($actualInnerContent), $processedTemplate);
                        }
                    }
                }

                $processedTemplate = CommonUtil::removeRemainingSlotPlaceholders($processedTemplate);

                Logger::debug("Creating slotted replacement mapping: " . $slottedTemplate->getName() . " -> " . $slottedTemplate->getTemplateKey(), 'LoaderPreProcessJson');
                $template->addReplacementMapping(new ReplacementMapping(
                    $fullMatch,
                    $processedTemplate,
                    ReplacementType::SLOTTED_TEMPLATE,
                    0,
                    0,
                    $slottedTemplate->getTemplateKey()
                ));
            }
        }
    }

    private function processSlotContentForReplacementMapping(SlotPlaceholder $slot, array $allTemplates, string $appSite): string
    {
        $result = $slot->getContent();

        foreach ($slot->getNestedSlottedTemplates() as $nestedSlottedTemplate) {
            $targetTemplateKey = strtolower($appSite) . '_' . $nestedSlottedTemplate->getTemplateKey();
            if (isset($allTemplates[$targetTemplateKey])) {
                $targetTemplate = $allTemplates[$targetTemplateKey];
                $processedTemplate = $targetTemplate->getOriginalContent();

                foreach ($nestedSlottedTemplate->getSlots() as $nestedSlot) {
                    $processedNestedSlotContent = $this->processSlotContentForReplacementMapping($nestedSlot, $allTemplates, $appSite);
                    $processedTemplate = str_replace($nestedSlot->getSlotKey(), $processedNestedSlotContent, $processedTemplate);
                }

                $processedTemplate = CommonUtil::removeRemainingSlotPlaceholders($processedTemplate);
                $result = str_replace($nestedSlottedTemplate->getFullMatch(), $processedTemplate, $result);
            }
        }

        return $result;
    }

    private function parseSlottedTemplates(string $content, string $appSite, PreprocessedTemplate $template): void
    {
        $searchPos = 0;
        while ($searchPos < strlen($content)) {
            $openStart = strpos($content, '{{#', $searchPos);
            if ($openStart === false) break;

            $openEnd = strpos($content, '}}', $openStart + 3);
            if ($openEnd === false) break;

            $templateName = trim(substr($content, $openStart + 3, $openEnd - ($openStart + 3)));
            if (empty($templateName) || !CommonUtil::isAlphaNumeric($templateName)) {
                $searchPos = $openStart + 1;
                continue;
            }

            $closeTag = '{{/' . $templateName . '}}';
            $closeStart = CommonUtil::findMatchingCloseTag($content, $openEnd + 2, '{{#' . $templateName . '}}', $closeTag);
            if ($closeStart === false) {
                $searchPos = $openStart + 1;
                continue;
            }

            $innerStart = $openEnd + 2;
            $innerContent = substr($content, $innerStart, $closeStart - $innerStart);
            $fullMatch = substr($content, $openStart, $closeStart + strlen($closeTag) - $openStart);

            $slottedTemplate = new SlottedTemplate(
                $templateName,
                $openStart,
                $closeStart + strlen($closeTag),
                $fullMatch,
                $innerContent,
                strtolower($templateName)
            );

            $this->parseSlots($innerContent, $slottedTemplate, $appSite);
            $template->addSlottedTemplate($slottedTemplate);
            $searchPos = $closeStart + strlen($closeTag);
        }
    }

    private function parseSlots(string $innerContent, SlottedTemplate $slottedTemplate, string $appSite): void
    {
        $searchPos = 0;
        while ($searchPos < strlen($innerContent)) {
            $slotStart = strpos($innerContent, '{{@HTMLPLACEHOLDER', $searchPos);
            if ($slotStart === false) break;

            $afterPlaceholder = $slotStart + 18;
            $slotNum = '';
            $pos = $afterPlaceholder;

            while ($pos < strlen($innerContent) && ctype_digit($innerContent[$pos])) {
                $slotNum .= $innerContent[$pos];
                $pos++;
            }

            if ($pos + 1 >= strlen($innerContent) || substr($innerContent, $pos, 2) !== '}}') {
                $searchPos = $slotStart + 1;
                continue;
            }

            $slotOpenEnd = $pos + 2;
            $openTag = '{{@HTMLPLACEHOLDER' . $slotNum . '}}';
            $closeTag = '{{/HTMLPLACEHOLDER' . $slotNum . '}}';
            $closeStart = CommonUtil::findMatchingCloseTag($innerContent, $slotOpenEnd, $openTag, $closeTag);

            if ($closeStart === false) {
                $searchPos = $slotStart + 1;
                continue;
            }

            $slotContent = substr($innerContent, $slotOpenEnd, $closeStart - $slotOpenEnd);
            $slotKey = '{{$HTMLPLACEHOLDER' . $slotNum . '}}';

            $slot = new SlotPlaceholder(
                $slotNum,
                $slotStart,
                $closeStart + strlen($closeTag),
                $slotContent,
                $slotKey,
                $openTag,
                $closeTag
            );

            $this->parseNestedTemplatesInSlot($slot, $slottedTemplate->getJsonData(), $appSite);
            $slottedTemplate->addSlot($slot);
            $searchPos = $closeStart + strlen($closeTag);
        }
    }

    private function parseNestedTemplatesInSlot(SlotPlaceholder $slot, ?array $jsonData, string $appSite): void
    {
        $content = $slot->getContent();
        if (empty($content)) return;
        
        $searchPos = 0;
        while ($searchPos < strlen($content)) {
            $openStart = strpos($content, '{{', $searchPos);
            if ($openStart === false) break;

            if ($openStart + 2 < strlen($content) && in_array($content[$openStart + 2], ['#', '@', '$', '/'])) {
                $searchPos = $openStart + 2;
                continue;
            }

            $closeStart = strpos($content, '}}', $openStart + 2);
            if ($closeStart === false) break;

            $templateName = trim(substr($content, $openStart + 2, $closeStart - ($openStart + 2)));
            if ($templateName && CommonUtil::isAlphaNumeric($templateName)) {
                $templateKey = strtolower($templateName);
                $slot->addNestedPlaceholder(new TemplatePlaceholder(
                    $templateName,
                    $openStart,
                    $closeStart + 2,
                    substr($content, $openStart, $closeStart + 2 - $openStart),
                    $templateKey,
                    $jsonData
                ));
            }
            $searchPos = $closeStart + 2;
        }

        $searchPos = 0;
        while ($searchPos < strlen($content)) {
            $openStart = strpos($content, '{{#', $searchPos);
            if ($openStart === false) break;

            $openEnd = strpos($content, '}}', $openStart + 3);
            if ($openEnd === false) break;

            $templateName = trim(substr($content, $openStart + 3, $openEnd - ($openStart + 3)));
            if (empty($templateName) || !CommonUtil::isAlphaNumeric($templateName)) {
                $searchPos = $openStart + 1;
                continue;
            }

            $closeTag = '{{/' . $templateName . '}}';
            $openTag = '{{#' . $templateName . '}}';
            $closeStart = CommonUtil::findMatchingCloseTag($content, $openEnd + 2, $openTag, $closeTag);

            if ($closeStart === false) {
                $searchPos = $openStart + 1;
                continue;
            }

            $innerContent = substr($content, $openEnd + 2, $closeStart - ($openEnd + 2));
            $templateKey = strtolower($templateName);
            $nestedSlottedTemplate = new SlottedTemplate(
                $templateName,
                $openStart,
                $closeStart + strlen($closeTag),
                substr($content, $openStart, $closeStart + strlen($closeTag) - $openStart),
                $innerContent,
                $templateKey,
                $jsonData
            );

            $this->parseSlots($innerContent, $nestedSlottedTemplate, $appSite);
            $slot->addNestedSlottedTemplate($nestedSlottedTemplate);
            $searchPos = $closeStart + strlen($closeTag);
        }
    }

    private function parsePlaceholderTemplates(string $content, string $appSite, PreprocessedTemplate $template): void
    {
        $searchPos = 0;
        while ($searchPos < strlen($content)) {
            $openStart = strpos($content, '{{', $searchPos);
            if ($openStart === false) break;

            if ($openStart + 2 < strlen($content) && in_array($content[$openStart + 2], ['#', '@', '$', '/'])) {
                $searchPos = $openStart + 2;
                continue;
            }

            $closeStart = strpos($content, '}}', $openStart + 2);
            if ($closeStart === false) break;

            $placeholderName = trim(substr($content, $openStart + 2, $closeStart - ($openStart + 2)));
            if ($placeholderName && CommonUtil::isAlphaNumeric($placeholderName)) {
                $placeholder = new TemplatePlaceholder(
                    $placeholderName,
                    $openStart,
                    $closeStart + 2,
                    substr($content, $openStart, $closeStart + 2 - $openStart),
                    strtolower($placeholderName)
                );
                $template->addPlaceholder($placeholder);
            }
            $searchPos = $closeStart + 2;
        }
    }

    private function preprocessJsonTemplates(PreprocessedTemplate $template): void
    {
        if (!$template->getJsonData()) return;
        Logger::debug("preprocessJsonTemplates: hasJsonData=" . ($template->getJsonData() ? "true" : "false") . ", count=" . $template->getJsonData()->count(), 'LoaderPreProcessJson');
        $content = $template->getOriginalContent();
        $this->createJsonArrayReplacementMappings($template, $content);
        $this->createJsonPlaceholderReplacementMappings($template, $content);
    }

    private function createJsonArrayReplacementMappings(PreprocessedTemplate $template, string $content): void
    {
        if (!$template->getJsonData()) return;

        foreach ($template->getJsonData() as $key => $value) {
            if (is_array($value) && isset($value[0])) { // is list
                $dataList = $value;
                $keyNorm = strtolower($key);
                $possibleTags = [$key, $keyNorm, rtrim($keyNorm, 's'), $keyNorm . 's'];

                foreach ($possibleTags as $tag) {
                    $blockStartTag = '{{@' . $tag . '}}';
                    $blockEndTag = '{{/' . $tag . '}}';
                    $startIdx = stripos($content, $blockStartTag);

                    if ($startIdx !== false) {
                        $endIdx = stripos($content, $blockEndTag, $startIdx + strlen($blockStartTag));
                        if ($endIdx !== false) {
                            $blockContent = substr($content, $startIdx + strlen($blockStartTag), $endIdx - ($startIdx + strlen($blockStartTag)));
                            $fullBlock = substr($content, $startIdx, $endIdx + strlen($blockEndTag) - $startIdx);
                            $processedArrayContent = $this->processArrayBlockContentSafely($blockContent, $dataList);

                            $template->addReplacementMapping(new ReplacementMapping(
                                $fullBlock,
                                $processedArrayContent,
                                ReplacementType::JSON_PLACEHOLDER,
                                $startIdx,
                                $endIdx + strlen($blockEndTag)
                            ));

                            $emptyBlockStart = '{{^' . $tag . '}}';
                            $emptyStartIdx = stripos($content, $emptyBlockStart);
                            if ($emptyStartIdx !== false) {
                                $emptyEndIdx = stripos($content, $blockEndTag, $emptyStartIdx + strlen($emptyBlockStart));
                                if ($emptyEndIdx !== false) {
                                    $emptyBlockContent = substr($content, $emptyStartIdx + strlen($emptyBlockStart), $emptyEndIdx - ($emptyStartIdx + strlen($emptyBlockStart)));
                                    $fullEmptyBlock = substr($content, $emptyStartIdx, $emptyEndIdx + strlen($blockEndTag) - $emptyStartIdx);
                                    $emptyReplacement = count($dataList) === 0 ? $emptyBlockContent : '';
                                    $template->addReplacementMapping(new ReplacementMapping(
                                        $fullEmptyBlock,
                                        $emptyReplacement,
                                        ReplacementType::JSON_PLACEHOLDER,
                                        $emptyStartIdx,
                                        $emptyEndIdx + strlen($blockEndTag)
                                    ));
                                }
                            }
                            break;
                        }
                    }
                }
            }
        }
    }

    private function createJsonPlaceholderReplacementMappings(PreprocessedTemplate $template, string $content): void
    {
        if (!$template->getJsonData()) return;

        Logger::debug("createJsonPlaceholderReplacementMappings: starting, jsonData count=" . $template->getJsonData()->count(), 'LoaderPreProcessJson');
        foreach ($template->getJsonData() as $key => $value) {
            Logger::debug("  Checking key='$key', value type=" . gettype($value) . ", is_string=" . (is_string($value) ? "true" : "false"), 'LoaderPreProcessJson');
            if (is_string($value)) {
                $placeholder = '{{$' . $key . '}}';
                Logger::debug("    String value found, placeholder='$placeholder', searching in content (len=" . strlen($content) . ")", 'LoaderPreProcessJson');
                if (stripos($content, $placeholder) !== false) {
                    Logger::debug("    Found '$placeholder' in content, adding replacement mapping", 'LoaderPreProcessJson');
                    $template->addReplacementMapping(new ReplacementMapping(
                        $placeholder,
                        $value,
                        ReplacementType::JSON_PLACEHOLDER,
                    ));

                    if (!in_array($placeholder, array_map(fn($p) => $p->getPlaceholder(), $template->getJsonPlaceholders()))) {
                        $template->addJsonPlaceholder(new JsonPlaceholder($key, $placeholder, $value));
                    }
                } else {
                    Logger::debug("    Placeholder '$placeholder' NOT found in content", 'LoaderPreProcessJson');
                }
            }
        }
        Logger::debug("createJsonPlaceholderReplacementMappings: finished", 'LoaderPreProcessJson');
    }

    private function processArrayBlockContentSafely(string $blockContent, array $arrayData): string
    {
        try {
            $mergedBlock = '';
            foreach ($arrayData as $item) {
                if (is_array($item)) {
                    $itemBlock = $blockContent;
                    foreach ($item as $key => $value) {
                        $placeholder = '{{$' . $key . '}}';
                        $itemBlock = str_ireplace($placeholder, (string)$value, $itemBlock);
                    }
                    $itemBlock = $this->processConditionalBlocksSafely($itemBlock, $item);
                    $mergedBlock .= $itemBlock;
                }
            }
            return $mergedBlock;
        } catch (\Exception $e) {
            return $blockContent;
        }
    }

    private function processConditionalBlocksSafely(string $content, array $jsonItem): string
    {
        try {
            $result = $content;
            $conditionalKeys = $this->findConditionalKeysInContent($result);
            foreach ($conditionalKeys as $condKey) {
                $condValue = $this->getConditionValue($jsonItem, $condKey);
                $result = $this->processConditionalBlockSafely($result, $condKey, $condValue);
            }
            return $result;
        } catch (\Exception $e) {
            return $content;
        }
    }

    private function findConditionalKeysInContent(string $content): array
    {
        $conditionalKeys = [];
        preg_match_all('/{{@(\w+)}}/i', $content, $matches);
        if (!empty($matches[1])) {
            $conditionalKeys = $matches[1];
        }
        return array_unique($conditionalKeys);
    }

    private function getConditionValue(array $item, string $condKey): bool
    {
        $lowerCondKey = strtolower($condKey);
        foreach ($item as $key => $val) {
            if (strtolower($key) === $lowerCondKey) {
                if (is_bool($val)) return $val;
                if (is_string($val)) return strtolower($val) === 'true';
                if (is_numeric($val)) return $val != 0;
                return false;
            }
        }
        return false;
    }

    private function processConditionalBlockSafely(string $input, string $key, bool $condition): string
    {
        try {
            $tags = [
                ['start' => '{{@' . $key . '}}', 'end' => '{{ /' . $key . '}}'],
                ['start' => '{{@' . $key . '}}', 'end' => '{{/' . $key . '}}']
            ];

            foreach ($tags as $tag) {
                $index = stripos($input, $tag['start']);
                while ($index !== false) {
                    $endIndex = stripos($input, $tag['end'], $index + strlen($tag['start']));
                    if ($endIndex === false) break;

                    $content = substr($input, $index + strlen($tag['start']), $endIndex - ($index + strlen($tag['start'])));
                    if ($condition) {
                        $input = substr($input, 0, $index) . $content . substr($input, $endIndex + strlen($tag['end']));
                        $index += strlen($content);
                    } else {
                        $input = substr($input, 0, $index) . substr($input, $endIndex + strlen($tag['end']));
                    }
                    $index = stripos($input, $tag['start'], $index);
                }
            }
            return $input;
        } catch (\Exception $e) {
            return $input;
        }
    }

    // NOTE: The following methods are INTENTIONAL COPIES used across all loaders for architectural separation.
    // Each loader/engine pair is independent to allow individual evolution without shared dependencies.
    // DO NOT extract these to shared utilities - that would create tight coupling.

    private function buildParentMapForPreProcessJson(array $siteTemplates, string $appSite): array
    {
        $parentMap = [];
        Logger::debug("Building parent map for appSite: $appSite", "LoaderPreProcessJson");

        foreach ($siteTemplates['templates'] as $templateKey => $template) {
            foreach ($template->getPlaceholders() as $placeholder) {
                $childTemplateKey = strtolower($appSite) . '_' . strtolower($placeholder->name);
                if (!isset($parentMap[$childTemplateKey])) {
                    $parentMap[$childTemplateKey] = $templateKey;
                    Logger::debug("Parent relationship: $childTemplateKey -> parent: $templateKey", "LoaderPreProcessJson");
                }
            }

            foreach ($template->getSlottedTemplates() as $slottedTemplate) {
                $childTemplateKey = strtolower($appSite) . '_' . strtolower($slottedTemplate->name);
                if (!isset($parentMap[$childTemplateKey])) {
                    $parentMap[$childTemplateKey] = $templateKey;
                    Logger::debug("Parent relationship (slotted): $childTemplateKey -> parent: $templateKey", "LoaderPreProcessJson");
                }
            }
        }

        Logger::debug("Built parent map with " . count($parentMap) . " relationships", "LoaderPreProcessJson");
        return $parentMap;
    }

    private function resolveJsonInheritanceForAllTemplatesJson(array &$siteTemplates, array $parentMap): void
    {
        foreach ($siteTemplates['templates'] as $templateKey => $template) {
            $jsonData = $template->getJsonData();
            if ($jsonData === null) {
                continue;
            }

            $resolvedJson = [];
            $hasInheritance = false;

            foreach ($jsonData as $key => $value) {
                if (str_ends_with($key, '#') && is_string($value)) {
                    $hasInheritance = true;
                    $actualKey = substr($key, 0, -1);
                    $resolvedValue = $this->searchParentTreeForKeyPreProcessJson($actualKey, $templateKey, $siteTemplates['templates'], $parentMap);

                    if ($resolvedValue !== null) {
                        $resolvedJson[$actualKey] = $resolvedValue;
                        Logger::debug("Resolved inherited key $key -> $actualKey = $resolvedValue for template $templateKey", "LoaderPreProcessJson");
                    } else {
                        $resolvedJson[$actualKey] = $value; // Use default if not resolved
                        Logger::debug("No inherited value found for $actualKey, using default: $value", "LoaderPreProcessJson");
                    }
                } else {
                    $resolvedJson[$key] = $value;
                }
            }

            if ($hasInheritance) {
                // Create a new JsonObject with the resolved values
                $newJsonData = new \Assembler\App\Json\JsonObject();
                foreach ($resolvedJson as $key => $val) {
                    $newJsonData->setValue($key, $val);
                }
                $template->setJsonData($newJsonData);
                Logger::debug("Updated JsonData for template $templateKey with resolved inheritance", "LoaderPreProcessJson");
            }
        }
    }

    private function recreateJsonPlaceholderMappingsAfterInheritanceJson(array &$siteTemplates): void
    {
        foreach ($siteTemplates['templates'] as $templateKey => $template) {
            if ($template->getJsonData() === null) {
                continue;
            }

            $newMappings = [];
            foreach ($template->getReplacementMappings() as $mapping) {
                if ($mapping->getType() !== ReplacementType::JSON_PLACEHOLDER) {
                    $newMappings[] = $mapping;
                }
            }

            $template->setReplacementMappings($newMappings);
            $template->clearJsonPlaceholders();

            $this->createJsonArrayReplacementMappings($template, $template->getOriginalContent());
            $this->createJsonPlaceholderReplacementMappings($template, $template->getOriginalContent());

            Logger::debug("Recreated JSON placeholder and array mappings for template $templateKey after inheritance resolution", "LoaderPreProcessJson");
        }
    }

    private function searchParentTreeForKeyPreProcessJson(string $key, string $currentTemplateKey, array $allTemplates, array $parentMap): ?string
    {
        if (!isset($parentMap[$currentTemplateKey])) {
            Logger::debug("No parent found for $currentTemplateKey", "LoaderPreProcessJson");
            return null;
        }

        $parentKey = $parentMap[$currentTemplateKey];
        Logger::debug("Checking parent $parentKey for key $key", "LoaderPreProcessJson");

        if (!isset($allTemplates[$parentKey])) {
            Logger::debug("Parent template $parentKey not found in templates", "LoaderPreProcessJson");
            return null;
        }

        $parentTemplate = $allTemplates[$parentKey];

        if ($parentTemplate->getJsonData() === null) {
            Logger::debug("Parent template $parentKey has no JSON data, searching further up", "LoaderPreProcessJson");
            return $this->searchParentTreeForKeyPreProcessJson($key, $parentKey, $allTemplates, $parentMap);
        }

        foreach ($parentTemplate->getJsonData() as $jsonKvpKey => $jsonKvpValue) {
            if (strcasecmp($jsonKvpKey, $key) === 0) {
                if (is_string($jsonKvpValue)) {
                    Logger::debug("Found key $key in parent $parentKey: $jsonKvpValue", "LoaderPreProcessJson");
                    return $jsonKvpValue;
                }
            }
        }

        Logger::debug("Key $key not found in parent $parentKey, searching further up", "LoaderPreProcessJson");
        return $this->searchParentTreeForKeyPreProcessJson($key, $parentKey, $allTemplates, $parentMap);
    }
}

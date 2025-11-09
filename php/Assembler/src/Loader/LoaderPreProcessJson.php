<?php

namespace Assembler\Loader;

use Arshu\Common\Logger;
use Assembler\Common\CommonUtil;
use Assembler\Model\PreprocessedTemplate;
use Assembler\Model\SlottedTemplate;
use Assembler\Model\SlotPlaceholder;
use Assembler\Model\TemplatePlaceholder;
use Assembler\Model\ReplacementMapping;
use Assembler\Model\ReplacementType;
use Assembler\Model\JsonPlaceholder;

class LoaderPreProcessJson
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

    public function getTemplateJson(string $appSite, string $templateName): ?array
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
        Logger::debug("Creating replacement mappings for $appSite - Phase 1: JSON arrays", 'LoaderPreProcessJson');
        foreach ($siteTemplates['templates'] as $template) {
            $this->createJsonArrayReplacementMappings($template, $template->getOriginalContent());
        }

        Logger::debug("Creating replacement mappings for $appSite - Phase 2: Simple placeholders", 'LoaderPreProcessJson');
        foreach ($siteTemplates['templates'] as $template) {
            $this->createPlaceholderReplacementMappings($template, $siteTemplates['templates'], $appSite);
        }

        Logger::debug("Creating replacement mappings for $appSite - Phase 3: Slotted templates", 'LoaderPreProcessJson');
        foreach ($siteTemplates['templates'] as $template) {
            $this->createSlottedTemplateReplacementMappings($template, $siteTemplates['templates'], $appSite);
        }

        $totalMappings = 0;
        foreach ($siteTemplates['templates'] as $template) {
            $totalMappings += count($template->getReplacementMappings());
        }
        Logger::info("Total replacement mappings created for $appSite: $totalMappings", 'LoaderPreProcessJson');
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
                    ReplacementType::SimpleTemplate,
                    null,
                    null,
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
                    ReplacementType::SlottedTemplate,
                    null,
                    null,
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
                                ReplacementType::JsonPlaceholder,
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
                                        ReplacementType::JsonPlaceholder,
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

        foreach ($template->getJsonData() as $key => $value) {
            if (is_string($value)) {
                $placeholder = '{{$' . $key . '}}';
                if (stripos($content, $placeholder) !== false) {
                    $template->addReplacementMapping(new ReplacementMapping(
                        $placeholder,
                        $value,
                        ReplacementType::JsonPlaceholder
                    ));

                    if (!in_array($placeholder, array_map(fn($p) => $p->getPlaceholder(), $template->getJsonPlaceholders()))) {
                        $template->addJsonPlaceholder(new JsonPlaceholder($key, $placeholder, $value));
                    }
                }
            }
        }
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
}
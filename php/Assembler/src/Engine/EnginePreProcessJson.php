<?php

namespace Assembler\Engine;

use Arshu\Common\Logger;
use Assembler\Common\CommonUtil;
use Assembler\Loader\LoaderPreProcessJson;
use Assembler\Model\ReplacementType;

class EnginePreProcessJson
{
    private string $appViewPrefix = '';

    public function setAppViewPrefix(string $prefix): void
    {
        $this->appViewPrefix = $prefix;
    }

    public function mergeTemplates(string $appSite, string $appFile, ?string $appView, LoaderPreProcessJson $loader, bool $enableJsonProcessing = true): string
    {
        Logger::debug("MergeTemplates called: appSite=$appSite, appFile=$appFile, appView=" . ($appView ?? 'null') . ", enableJson=$enableJsonProcessing", 'EnginePreProcessJson');

        if (!$loader) {
            Logger::warn('No loader provided', 'EnginePreProcessJson');
            return '';
        }

        $preprocessedTemplates = $loader->getAllTemplates();
        if (empty($preprocessedTemplates)) {
            Logger::warn('No preprocessed templates available', 'EnginePreProcessJson');
            return '';
        }

        Logger::debug('Using ' . count($preprocessedTemplates) . ' preprocessed templates', 'EnginePreProcessJson');

        $mainPreprocessed = $loader->getTemplateHtml($appSite, $appFile, $appView, $this->appViewPrefix);
        if (!$mainPreprocessed) {
            Logger::warn("Main template not found for appSite=$appSite, appFile=$appFile", 'EnginePreProcessJson');
            return '';
        }

        Logger::debug('Main template found, original size: ' . strlen($mainPreprocessed->getOriginalContent()), 'EnginePreProcessJson');

        $contentHtml = $mainPreprocessed->getOriginalContent();

        if ($enableJsonProcessing && $mainPreprocessed->getJsonData()) {
            Logger::debug('Merging main template JSON', 'EnginePreProcessJson');
            $contentHtml = JsonMergeUtil::mergeTemplateWithJson($contentHtml, $mainPreprocessed->getJsonData());
        }

        $contentHtml = $this->applyTemplateReplacements($contentHtml, $preprocessedTemplates, $enableJsonProcessing, $appView, $mainPreprocessed, $loader, $appSite);

        Logger::debug('MergeTemplates complete: output size=' . strlen($contentHtml), 'EnginePreProcessJson');
        return $contentHtml;
    }

    private function getTemplate(string $appSite, string $templateName, array $preprocessedTemplates, ?string $appView = null, ?string $appViewPrefix = null, bool $useAppViewFallback = true)
    {
        if (empty($preprocessedTemplates)) {
            return null;
        }

        $viewPrefix = $appViewPrefix ?? $this->appViewPrefix;

        if ($useAppViewFallback && $appView && $viewPrefix && stripos($templateName, $viewPrefix) !== false) {
            $appKey = CommonUtil::replaceCaseInsensitive($templateName, $viewPrefix, $appView);
            $fallbackTemplateKey = strtolower($appSite) . '_' . strtolower($appKey);
            if (isset($preprocessedTemplates[$fallbackTemplateKey])) {
                return $preprocessedTemplates[$fallbackTemplateKey];
            }
        }

        $primaryTemplateKey = strtolower($appSite) . '_' . strtolower($templateName);
        if (isset($preprocessedTemplates[$primaryTemplateKey])) {
            return $preprocessedTemplates[$primaryTemplateKey];
        }

        return null;
    }

    private function applyTemplateReplacements(string $content, array $preprocessedTemplates, bool $enableJsonProcessing, ?string $appView, $mainTemplate, LoaderPreProcessJson $loader, string $appSite): string
    {
        $result = $content;
        Logger::debug('Starting ApplyTemplateReplacements, initial size: ' . strlen($content), 'EnginePreProcessJson');

        $previous = '';
        $maxPasses = 10;
        $currentPass = 0;

        do {
            $previous = $result;
            $currentPass++;
            Logger::debug("Replacement pass $currentPass, current size: " . strlen($result), 'EnginePreProcessJson');

            $slottedCount = 0;
            $simpleCount = 0;
            $jsonPlaceholderCount = 0;

            if ($mainTemplate && $currentPass === 1 && $enableJsonProcessing) {
                foreach ($mainTemplate->getReplacementMappings() as $i => $mapping) {
                    Logger::debug("[Main JSON Placeholder] Mapping #$i: " . json_encode($mapping), 'EnginePreProcessJson');
                    if (method_exists($mapping, 'getType') && $mapping->getType() !== ReplacementType::JSON_PLACEHOLDER) continue;
                    if (method_exists($mapping, 'getOriginalText') && strpos($result, $mapping->getOriginalText()) !== false) {
                        Logger::debug('Applying main template JSON placeholder: ' . $mapping->getOriginalText() . ' -> ' . $mapping->getReplacementText(), 'EnginePreProcessJson');
                        $result = str_replace($mapping->getOriginalText(), $mapping->getReplacementText(), $result);
                        $jsonPlaceholderCount++;
                    }
                }
            }

            foreach ($preprocessedTemplates as $tidx => $template) {
                Logger::debug("[Template #$tidx] Processing template: " . (method_exists($template, 'getOriginalContent') ? $template->getOriginalContent() : 'N/A'), 'EnginePreProcessJson');
                foreach ($template->getReplacementMappings() as $mid => $mapping) {
                    Logger::debug("[Slotted/Simple] Mapping #$mid: " . json_encode($mapping), 'EnginePreProcessJson');
                    if (method_exists($mapping, 'getType') && $mapping->getType() === ReplacementType::SLOTTED_TEMPLATE) {
                        if (method_exists($mapping, 'getOriginalText') && strpos($result, $mapping->getOriginalText()) !== false) {
                            $replacementText = $mapping->getReplacementText();
                            if ($enableJsonProcessing && method_exists($mapping, 'getTargetTemplateName') && $mapping->getTargetTemplateName()) {
                                $targetJson = $loader->getTemplateJson($appSite, $mapping->getTargetTemplateName());
                                if ($targetJson) {
                                    Logger::debug('Merging JSON for slotted template ' . $mapping->getTargetTemplateName(), 'EnginePreProcessJson');
                                    $replacementText = JsonMergeUtil::mergeTemplateWithJson($replacementText, $targetJson);
                                }
                            }
                            Logger::debug('Applying slotted template: ' . substr($mapping->getOriginalText(), 0, 50) . '... -> ' . strlen($replacementText) . ' chars', 'EnginePreProcessJson');
                            $result = str_replace($mapping->getOriginalText(), $replacementText, $result);
                            $slottedCount++;
                        }
                    } elseif (method_exists($mapping, 'getType') && $mapping->getType() === ReplacementType::SIMPLE_TEMPLATE) {
                        if (method_exists($mapping, 'getOriginalText') && strpos($result, $mapping->getOriginalText()) !== false) {
                            $replacementText = $mapping->getReplacementText();
                            if ($appView && method_exists($mapping, 'getTargetTemplateName') && $mapping->getTargetTemplateName()) {
                                $appViewTemplate = $this->getTemplate($appSite, $mapping->getTargetTemplateName(), $preprocessedTemplates, $appView, $this->appViewPrefix, true);
                                if ($appViewTemplate) {
                                    $replacementText = $appViewTemplate->getOriginalContent();
                                }
                            }

                            if ($enableJsonProcessing && method_exists($mapping, 'getTargetTemplateName') && $mapping->getTargetTemplateName()) {
                                $targetJson = $loader->getTemplateJson($appSite, $mapping->getTargetTemplateName());
                                if ($targetJson) {
                                    Logger::debug('Merging JSON for simple template ' . $mapping->getTargetTemplateName(), 'EnginePreProcessJson');
                                    $replacementText = JsonMergeUtil::mergeTemplateWithJson($replacementText, $targetJson);
                                }
                            }
                            Logger::debug('Applying simple template: ' . $mapping->getOriginalText() . ' -> ' . strlen($replacementText) . ' chars', 'EnginePreProcessJson');
                            $result = str_replace($mapping->getOriginalText(), $replacementText, $result);
                            $simpleCount++;
                        }
                    }
                }
            }
            Logger::debug("Pass $currentPass applied: $jsonPlaceholderCount main JSON placeholders, $slottedCount slotted, $simpleCount simple", 'EnginePreProcessJson');
        } while ($result !== $previous && $currentPass < $maxPasses);

        Logger::debug("Replacement complete after $currentPass passes, final size: " . strlen($result), 'EnginePreProcessJson');
        return $result;
    }
}
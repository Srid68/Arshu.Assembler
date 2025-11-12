<?php

namespace Assembler\Engine\PreProcessJson;

use Assembler\Interface\ILoaderJson;

use Arshu\Common\Logger;
use Assembler\Loader\LoaderPreProcessJson;

class EnginePreProcessJson
{
    private string $appViewPrefix = '';

    public function setAppViewPrefix(string $prefix): void
    {
        $this->appViewPrefix = $prefix;
    }

    public function mergeTemplates(string $appSite, string $appFile, ?string $appView, ILoaderJson $loader, bool $enableJsonProcessing = true): string
    {
        Logger::debug("MergeTemplates called: appSite=$appSite, appFile=$appFile, appView=" . ($appView ?? 'null') . ", enableJson=$enableJsonProcessing", 'EnginePreProcessJson');

        if (!$loader) {
            Logger::warn('No loader provided', 'EnginePreProcessJson');
            return '';
        }

        // Use ILoaderJson to retrieve the main template
        $mainPreprocessed = $loader->getTemplateHtml($appSite, $appFile, $appView, $this->appViewPrefix);
        if (!$mainPreprocessed) {
            Logger::warn("Main template not found for appSite=$appSite, appFile=$appFile", 'EnginePreProcessJson');
            return '';
        }

        Logger::debug('Main template found, original size: ' . strlen($mainPreprocessed->getOriginalContent()), 'EnginePreProcessJson');

        // Start with original content
        $contentHtml = $mainPreprocessed->getOriginalContent();

        // Merge JSON into main template first using loader's centralized method
        if ($enableJsonProcessing) {
            $contentHtml = $loader->mergeHtmlWithJson($contentHtml, $appSite, $appFile);
            Logger::debug('After main template JSON merge: ' . strlen($contentHtml) . ' chars', 'EnginePreProcessJson');
        }

        // Apply ALL replacement mappings from ALL templates using loader's method
        $contentHtml = $loader->applyAllReplacementMappings($contentHtml, $appSite, $mainPreprocessed, $appView, $this->appViewPrefix, $enableJsonProcessing);

        Logger::debug('MergeTemplates complete: output size=' . strlen($contentHtml), 'EnginePreProcessJson');
        return $contentHtml;
    }
}
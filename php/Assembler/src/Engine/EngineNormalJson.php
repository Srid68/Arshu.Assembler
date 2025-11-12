<?php

namespace Assembler\Engine;

use Arshu\Common\Logger;
use Assembler\Common\CommonUtil;
use Assembler\Loader\LoaderNormalJson;

class EngineNormalJson
{
    private string $appViewPrefix = '';

    public function setAppViewPrefix(string $prefix): void
    {
        $this->appViewPrefix = $prefix;
    }

    /**
     * Merges templates by replacing placeholders with corresponding HTML
     * Strictly follows C# structure and logging.
     */
    public function mergeTemplates(string $appSite, string $appFile, ?string $appView, LoaderNormalJson $loader, bool $enableJsonProcessing = true): string
    {
        Logger::debug("MergeTemplates called: appSite=$appSite, appFile=$appFile, appView=" . ($appView ?? "null") . ", enableJson=$enableJsonProcessing", "EngineNormalJson");

        if (!$loader) {
            Logger::warn("No loader provided", "EngineNormalJson");
            return "";
        }

        $contentHtml = $loader->getTemplateHtml($appSite, $appFile, $appView, $this->appViewPrefix);
        if (empty($contentHtml)) {
            Logger::warn("Main template not found for appSite=$appSite, appFile=$appFile", "EngineNormalJson");
            return "";
        }

        Logger::debug("Main template found, html size: " . strlen($contentHtml), "EngineNormalJson");

        if ($enableJsonProcessing) {
            $contentHtml = $this->mergeHtmlWithJson($contentHtml, $appSite, $appFile, $loader);
            Logger::debug("After main JSON merge: " . strlen($contentHtml) . " chars", "EngineNormalJson");
        }

        $previous = '';
        $maxPasses = 10;
        $actualPasses = 0;
        for ($pass = 0; $pass < $maxPasses; $pass++) {
            $previous = $contentHtml;
            $actualPasses = $pass + 1;

            Logger::debug("Pass $actualPasses, current size: " . strlen($contentHtml), "EngineNormalJson");

            $contentHtml = $this->mergeTemplateSlots($contentHtml, $appSite, $appView, $loader, $enableJsonProcessing);
            Logger::debug("After slot merge: " . strlen($contentHtml) . " chars", "EngineNormalJson");

            $contentHtml = $this->replaceTemplatePlaceholders($contentHtml, $appSite, $appView, $loader, $enableJsonProcessing);
            Logger::debug("After placeholder replacement: " . strlen($contentHtml) . " chars", "EngineNormalJson");

            if ($contentHtml === $previous) {
                Logger::debug("No changes in pass $actualPasses, stopping", "EngineNormalJson");
                break;
            }
        }

        Logger::debug("MergeTemplates complete after $actualPasses passes: output size=" . strlen($contentHtml), "EngineNormalJson");
        return $contentHtml;
    }

    /**
     * Merges HTML with JSON using loader's centralized method (C# style)
     * The loader handles JSON inheritance internally
     */
    private function mergeHtmlWithJson(string $html, string $appSite, string $templateName, LoaderNormalJson $loader): string
    {
        Logger::debug("Calling loader->mergeHtmlWithJson for template $templateName", "EngineNormalJson");
        $originalSize = strlen($html);
        $html = $loader->mergeHtmlWithJson($html, $appSite, $templateName);
        Logger::debug("After JSON merge for $templateName: size $originalSize -> " . strlen($html), "EngineNormalJson");
        return $html;
    }

    private function getTemplateWithJson(string $appSite, string $templateName, LoaderNormalJson $loader, ?string $appView, bool $enableJsonProcessing): ?string
    {
        $html = $loader->getTemplateHtml($appSite, $templateName, $appView, $this->appViewPrefix);
        if (!$html) {
            return null;
        }

        Logger::debug("GetTemplateWithJson: template=$templateName, html size=" . strlen($html), 'EngineNormalJson');

        if ($enableJsonProcessing) {
            Logger::debug("Calling loader->mergeHtmlWithJson for template $templateName", 'EngineNormalJson');
            $originalSize = strlen($html);
            $html = $loader->mergeHtmlWithJson($html, $appSite, $templateName);
            Logger::debug("After JSON merge for $templateName: size $originalSize -> " . strlen($html), 'EngineNormalJson');
        }

        return $html;
    }

    private function mergeTemplateSlots(string $contentHtml, string $appSite, ?string $appView, LoaderNormalJson $loader, bool $enableJsonProcessing): string
    {
        if (empty($contentHtml)) {
            return $contentHtml;
        }

        $previous = '';
        do {
            $previous = $contentHtml;
            $contentHtml = $this->processTemplateSlots($contentHtml, $appSite, $appView, $loader, $enableJsonProcessing);
        } while ($contentHtml !== $previous);
        return $contentHtml;
    }

    private function processTemplateSlots(string $contentHtml, string $appSite, ?string $appView, LoaderNormalJson $loader, bool $enableJsonProcessing): string
    {
        $result = $contentHtml;
        $searchPos = 0;

        while ($searchPos < strlen($result)) {
            $openStart = strpos($result, '{{#', $searchPos);
            if ($openStart === false) break;

            $openEnd = strpos($result, '}}', $openStart + 3);
            if ($openEnd === false) break;

            $templateName = trim(substr($result, $openStart + 3, $openEnd - ($openStart + 3)));
            if (empty($templateName) || !CommonUtil::isAlphaNumeric($templateName)) {
                $searchPos = $openStart + 1;
                continue;
            }

            $closeTag = '{{/' . $templateName . '}}';
            $closeStart = CommonUtil::findMatchingCloseTag($result, $openEnd + 2, '{{#' . $templateName . '}}', $closeTag);
            if ($closeStart === false) {
                $searchPos = $openStart + 1;
                continue;
            }

            $innerStart = $openEnd + 2;
            $innerContent = substr($result, $innerStart, $closeStart - $innerStart);

            $templateHtml = $this->getTemplateWithJson($appSite, $templateName, $loader, $appView, $enableJsonProcessing);

            if ($templateHtml) {
                $slotContents = $this->extractSlotContents($innerContent, $appSite, $appView, $loader, $enableJsonProcessing);
                $processedTemplate = $templateHtml;
                foreach ($slotContents as $key => $value) {
                    $processedTemplate = str_replace($key, $value, $processedTemplate);
                }

                $processedTemplate = CommonUtil::removeRemainingSlotPlaceholders($processedTemplate);

                $fullMatch = substr($result, $openStart, $closeStart + strlen($closeTag) - $openStart);
                $result = str_replace($fullMatch, $processedTemplate, $result);
                $searchPos = $openStart + strlen($processedTemplate);
            } else {
                $searchPos = $openStart + 1;
            }
        }

        return $result;
    }

    private function extractSlotContents(string $innerContent, string $appSite, ?string $appView, LoaderNormalJson $loader, bool $enableJsonProcessing): array
    {
        $slotContents = [];
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

            $recursiveResult = $this->mergeTemplateSlots($slotContent, $appSite, $appView, $loader, $enableJsonProcessing);
            $recursiveResult = $this->replaceTemplatePlaceholders($recursiveResult, $appSite, $appView, $loader, $enableJsonProcessing);
            $slotContents[$slotKey] = $recursiveResult;

            $searchPos = $closeStart + strlen($closeTag);
        }

        return $slotContents;
    }

    private function replaceTemplatePlaceholders(string $html, string $appSite, ?string $appView, LoaderNormalJson $loader, bool $enableJsonProcessing): string
    {
        $result = $html;
        $searchPos = 0;

        while ($searchPos < strlen($result)) {
            $openStart = strpos($result, '{{', $searchPos);
            if ($openStart === false) break;

            if ($openStart + 2 < strlen($result) && in_array($result[$openStart + 2], ['#', '@', '$', '/'])) {
                $searchPos = $openStart + 2;
                continue;
            }

            $closeStart = strpos($result, '}}', $openStart + 2);
            if ($closeStart === false) break;

            $placeholderName = trim(substr($result, $openStart + 2, $closeStart - ($openStart + 2)));
            if (empty($placeholderName) || !CommonUtil::isAlphaNumeric($placeholderName)) {
                $searchPos = $openStart + 2;
                continue;
            }

            $templateContent = $this->getTemplateWithJson($appSite, $placeholderName, $loader, $appView, $enableJsonProcessing);

            if ($templateContent) {
                $processedReplacement = $this->replaceTemplatePlaceholders($templateContent, $appSite, $appView, $loader, $enableJsonProcessing);
                $placeholder = substr($result, $openStart, $closeStart + 2 - $openStart);
                $result = str_replace($placeholder, $processedReplacement, $result);
                $searchPos = $openStart + strlen($processedReplacement);
            } else {
                $searchPos = $closeStart + 2;
            }
        }

        return $result;
    }
}
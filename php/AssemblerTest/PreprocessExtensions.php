<?php

use Assembler\TemplateModel\PreprocessedSiteTemplates;
use Assembler\TemplateModel\PreprocessedTemplate;
use Assembler\TemplateModel\PreprocessedSummary;
use Assembler\TemplateModel\TemplatePlaceholder;
use Assembler\TemplateModel\SlottedTemplate;
use Assembler\TemplateModel\SlotPlaceholder;
use Assembler\TemplateModel\JsonPlaceholder;
use Assembler\TemplateModel\ReplacementMapping;
use Assembler\App\Json\JsonObject;
use Assembler\App\Json\JsonArray;

/**
 * Extension methods for PreprocessedSiteTemplates (static utility class in PHP)
 */
class PreprocessExtensions
{
    /**
     * Convert PreprocessedSiteTemplates to JSON string
     * @param PreprocessedSiteTemplates $siteTemplates
     * @param bool $indented
     * @return string
     */
    public static function toJson(PreprocessedSiteTemplates $siteTemplates, bool $indented = true): string
    {
        return self::serializePreprocessedTemplates($siteTemplates, $indented);
    }

    /**
     * Create summary from PreprocessedSiteTemplates
     * @param PreprocessedSiteTemplates $siteTemplates
     * @return array Summary array
     */
    public static function createSummary(PreprocessedSiteTemplates $siteTemplates): array
    {
        $templatesRequiringProcessing = 0;
        $templatesWithJsonData = 0;
        $templatesWithPlaceholders = 0;

        foreach ($siteTemplates->templates as $template) {
            if ($template->requiresProcessing()) {
                $templatesRequiringProcessing++;
            }
            if ($template->hasJsonData()) {
                $templatesWithJsonData++;
            }
            if ($template->hasPlaceholders()) {
                $templatesWithPlaceholders++;
            }
        }

        return [
            'siteName' => $siteTemplates->siteName,
            'templatesRequiringProcessing' => $templatesRequiringProcessing,
            'templatesWithJsonData' => $templatesWithJsonData,
            'templatesWithPlaceholders' => $templatesWithPlaceholders,
            'totalTemplates' => count($siteTemplates->templates)
        ];
    }

    /**
     * Convert PreprocessedSiteTemplates to summary JSON string
     * @param PreprocessedSiteTemplates $siteTemplates
     * @param bool $indented
     * @return string
     */
    public static function toSummaryJson(PreprocessedSiteTemplates $siteTemplates, bool $indented = true): string
    {
        return self::serializePreprocessedSummary(self::createSummary($siteTemplates), $indented);
    }

    /**
     * Serialize PreprocessedSiteTemplates to JSON
     * @param PreprocessedSiteTemplates $templates
     * @param bool $indented
     * @return string
     */
    public static function serializePreprocessedTemplates(PreprocessedSiteTemplates $templates, bool $indented = true): string
    {
        $jsonObject = self::convertPreprocessedTemplatesToJsonObject($templates);
        $json = json_encode($jsonObject->toArray(), $indented ? JSON_PRETTY_PRINT : 0);
        return str_replace("\r\n", "\n", $json);
    }

    /**
     * Serialize PreprocessedSummary to JSON
     * @param array $summary
     * @param bool $indented
     * @return string
     */
    public static function serializePreprocessedSummary(array $summary, bool $indented = true): string
    {
        $json = json_encode($summary, $indented ? JSON_PRETTY_PRINT : 0);
        return str_replace("\r\n", "\n", $json);
    }

    /**
     * Convert PreprocessedSiteTemplates to JsonObject
     * @param PreprocessedSiteTemplates $templates
     * @return JsonObject
     */
    private static function convertPreprocessedTemplatesToJsonObject(PreprocessedSiteTemplates $templates): JsonObject
    {
        $jsonObject = new JsonObject();
        $jsonObject->setValue('siteName', $templates->siteName);

        $templatesObject = new JsonObject();
        foreach ($templates->templates as $key => $template) {
            $templatesObject->setValue($key, self::convertPreprocessedTemplateToJsonObject($template));
        }
        $jsonObject->setValue('templates', $templatesObject);

        $rawTemplatesObject = new JsonObject();
        foreach ($templates->rawTemplates as $key => $value) {
            $rawTemplatesObject->setValue($key, $value);
        }
        $jsonObject->setValue('rawTemplates', $rawTemplatesObject);

        $templateKeysArray = new JsonArray();
        foreach ($templates->templateKeys as $key) {
            $templateKeysArray->add($key);
        }
        $jsonObject->setValue('templateKeys', $templateKeysArray);

        return $jsonObject;
    }

    /**
     * Convert PreprocessedTemplate to JsonObject
     * @param PreprocessedTemplate $template
     * @return JsonObject
     */
    private static function convertPreprocessedTemplateToJsonObject(PreprocessedTemplate $template): JsonObject
    {
        $jsonObject = new JsonObject();
        $jsonObject->setValue('originalContent', $template->originalContent);

        $placeholdersArray = new JsonArray();
        foreach ($template->placeholders as $placeholder) {
            $placeholdersArray->add(self::convertTemplatePlaceholderToJsonObject($placeholder));
        }
        $jsonObject->setValue('placeholders', $placeholdersArray);

        $slottedTemplatesArray = new JsonArray();
        foreach ($template->slottedTemplates as $slotted) {
            $slottedTemplatesArray->add(self::convertSlottedTemplateToJsonObject($slotted));
        }
        $jsonObject->setValue('slottedTemplates', $slottedTemplatesArray);

        if ($template->jsonData !== null) {
            $jsonObject->setValue('jsonData', $template->jsonData);
        }

        $jsonPlaceholdersArray = new JsonArray();
        foreach ($template->jsonPlaceholders as $jsonPlaceholder) {
            $jsonPlaceholdersArray->add(self::convertJsonPlaceholderToJsonObject($jsonPlaceholder));
        }
        $jsonObject->setValue('jsonPlaceholders', $jsonPlaceholdersArray);

        $replacementMappingsArray = new JsonArray();
        foreach ($template->replacementMappings as $mapping) {
            $replacementMappingsArray->add(self::convertReplacementMappingToJsonObject($mapping));
        }
        $jsonObject->setValue('replacementMappings', $replacementMappingsArray);

        $jsonObject->setValue('hasPlaceholders', $template->hasPlaceholders());
        $jsonObject->setValue('hasSlottedTemplates', $template->hasSlottedTemplates());
        $jsonObject->setValue('hasJsonData', $template->hasJsonData());
        $jsonObject->setValue('hasJsonPlaceholders', $template->hasJsonPlaceholders());
        $jsonObject->setValue('hasReplacementMappings', $template->hasReplacementMappings());
        $jsonObject->setValue('requiresProcessing', $template->requiresProcessing());

        return $jsonObject;
    }

    /**
     * Convert TemplatePlaceholder to JsonObject
     * @param TemplatePlaceholder $placeholder
     * @return JsonObject
     */
    private static function convertTemplatePlaceholderToJsonObject(TemplatePlaceholder $placeholder): JsonObject
    {
        $jsonObject = new JsonObject();
        $jsonObject->setValue('name', $placeholder->name);
        $jsonObject->setValue('startIndex', $placeholder->startIndex);
        $jsonObject->setValue('endIndex', $placeholder->endIndex);
        $jsonObject->setValue('fullMatch', $placeholder->fullMatch);
        $jsonObject->setValue('templateKey', $placeholder->templateKey);
        
        if ($placeholder->jsonData !== null) {
            $jsonObject->setValue('jsonData', $placeholder->jsonData);
        }

        $nestedPlaceholdersArray = new JsonArray();
        foreach ($placeholder->nestedPlaceholders as $nested) {
            $nestedPlaceholdersArray->add(self::convertTemplatePlaceholderToJsonObject($nested));
        }
        $jsonObject->setValue('nestedPlaceholders', $nestedPlaceholdersArray);

        $nestedSlotsArray = new JsonArray();
        foreach ($placeholder->nestedSlots as $slot) {
            $nestedSlotsArray->add(self::convertSlotPlaceholderToJsonObject($slot));
        }
        $jsonObject->setValue('nestedSlots', $nestedSlotsArray);

        return $jsonObject;
    }

    /**
     * Convert SlottedTemplate to JsonObject
     * @param SlottedTemplate $slotted
     * @return JsonObject
     */
    private static function convertSlottedTemplateToJsonObject(SlottedTemplate $slotted): JsonObject
    {
        $jsonObject = new JsonObject();
        $jsonObject->setValue('name', $slotted->name);
        $jsonObject->setValue('startIndex', $slotted->startIndex);
        $jsonObject->setValue('endIndex', $slotted->endIndex);
        $jsonObject->setValue('fullMatch', $slotted->fullMatch);
        $jsonObject->setValue('innerContent', $slotted->innerContent);
        $jsonObject->setValue('templateKey', $slotted->templateKey);
        
        if ($slotted->jsonData !== null) {
            $jsonObject->setValue('jsonData', $slotted->jsonData);
        }

        $slotsArray = new JsonArray();
        foreach ($slotted->slots as $slot) {
            $slotsArray->add(self::convertSlotPlaceholderToJsonObject($slot));
        }
        $jsonObject->setValue('slots', $slotsArray);

        return $jsonObject;
    }

    /**
     * Convert SlotPlaceholder to JsonObject
     * @param SlotPlaceholder $slot
     * @return JsonObject
     */
    private static function convertSlotPlaceholderToJsonObject(SlotPlaceholder $slot): JsonObject
    {
        $jsonObject = new JsonObject();
        $jsonObject->setValue('number', $slot->number);
        $jsonObject->setValue('startIndex', $slot->startIndex);
        $jsonObject->setValue('endIndex', $slot->endIndex);
        $jsonObject->setValue('content', $slot->content);
        $jsonObject->setValue('slotKey', $slot->slotKey);
        $jsonObject->setValue('openTag', $slot->openTag);
        $jsonObject->setValue('closeTag', $slot->closeTag);

        $nestedSlotsArray = new JsonArray();
        foreach ($slot->nestedSlots as $nested) {
            $nestedSlotsArray->add(self::convertSlotPlaceholderToJsonObject($nested));
        }
        $jsonObject->setValue('nestedSlots', $nestedSlotsArray);

        $nestedPlaceholdersArray = new JsonArray();
        foreach ($slot->nestedPlaceholders as $placeholder) {
            $nestedPlaceholdersArray->add(self::convertTemplatePlaceholderToJsonObject($placeholder));
        }
        $jsonObject->setValue('nestedPlaceholders', $nestedPlaceholdersArray);

        $nestedSlottedTemplatesArray = new JsonArray();
        foreach ($slot->nestedSlottedTemplates as $slotted) {
            $nestedSlottedTemplatesArray->add(self::convertSlottedTemplateToJsonObject($slotted));
        }
        $jsonObject->setValue('nestedSlottedTemplates', $nestedSlottedTemplatesArray);

        return $jsonObject;
    }

    /**
     * Convert JsonPlaceholder to JsonObject
     * @param JsonPlaceholder $jsonPlaceholder
     * @return JsonObject
     */
    private static function convertJsonPlaceholderToJsonObject(JsonPlaceholder $jsonPlaceholder): JsonObject
    {
        $jsonObject = new JsonObject();
        $jsonObject->setValue('key', $jsonPlaceholder->key);
        $jsonObject->setValue('placeholder', $jsonPlaceholder->placeholder);
        $jsonObject->setValue('value', $jsonPlaceholder->value);
        return $jsonObject;
    }

    /**
     * Convert ReplacementMapping to JsonObject
     * @param ReplacementMapping $mapping
     * @return JsonObject
     */
    private static function convertReplacementMappingToJsonObject(ReplacementMapping $mapping): JsonObject
    {
        $jsonObject = new JsonObject();
        $jsonObject->setValue('startIndex', $mapping->startIndex);
        $jsonObject->setValue('endIndex', $mapping->endIndex);
        $jsonObject->setValue('originalText', $mapping->originalText);
        $jsonObject->setValue('replacementText', $mapping->replacementText);
        $jsonObject->setValue('type', $mapping->type);
        return $jsonObject;
    }
}
?>

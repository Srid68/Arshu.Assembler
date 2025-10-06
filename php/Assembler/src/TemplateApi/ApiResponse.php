<?php
namespace Assembler\TemplateApi;

/**
 * Template data containing HTML and optional JSON
 */
class TemplateData {
    public string $html = '';
    public ?string $json = null;
}

/**
 * Preprocessed template metadata
 */
class PreProcessTemplateMetadata {
    public string $originalContent = '';
    public array $placeholders = [];
    public array $slottedTemplates = [];
    public mixed $jsonData = null;
    public array $jsonPlaceholders = [];
    public array $replacementMappings = [];
    public bool $hasPlaceholders = false;
    public bool $hasSlottedTemplates = false;
    public bool $hasJsonData = false;
    public bool $hasJsonPlaceholders = false;
    public bool $hasReplacementMappings = false;
    public bool $requiresProcessing = false;
}

/**
 * API response structure with manual JSON serialization
 */
class ApiResponse {
    public array $templates = [];
    public array $preProcessTemplates = [];
    public string $appSite = '';
    public ?string $appFile = null;
    public ?string $appView = null;
    public float $serverTimeMs = 0.0;
    public string $html = '';
    public float $engineTimeMs = 0.0;

    /**
     * Serialize to JSON string manually (no json_encode)
     * @return string JSON string
     */
    public function serializeToJson(): string {
        $sb = [];
        $sb[] = '{';

        // Serialize Templates dictionary
        $sb[] = '"Templates":';
        $sb[] = $this->_serializeDictionary($this->templates, fn($v) => $this->_serializeTemplateData($v));

        $sb[] = ',';

        // Serialize PreProcessTemplates dictionary
        $sb[] = '"PreProcessTemplates":';
        $sb[] = $this->_serializeDictionary($this->preProcessTemplates, fn($v) => $this->_serializePreProcessMetadata($v));

        $sb[] = ',';

        // Serialize AppSite
        $sb[] = '"AppSite":"';
        $sb[] = self::_escapeJsonString($this->appSite);
        $sb[] = '"';

        // Serialize AppFile if not null
        if ($this->appFile !== null) {
            $sb[] = ',"AppFile":"';
            $sb[] = self::_escapeJsonString($this->appFile);
            $sb[] = '"';
        }

        // Serialize AppView if not null
        if ($this->appView !== null) {
            $sb[] = ',"AppView":"';
            $sb[] = self::_escapeJsonString($this->appView);
            $sb[] = '"';
        }

        // Serialize ServerTimeMs
        $sb[] = ',"ServerTimeMs":';
        $sb[] = (string)$this->serverTimeMs;

        // Merged Html
        $sb[] = ',"Html":"';
        $sb[] = self::_escapeHtmlString($this->html);
        $sb[] = '"';

        // Engine Time
        $sb[] = ',"EngineTimeMs":';
        $sb[] = (string)$this->engineTimeMs;

        $sb[] = '}';
        return implode('', $sb);
    }

    /**
     * Escape JSON string values
     * @param string $input
     * @return string
     */
    private static function _escapeJsonString(string $input): string {
        if (empty($input)) return '';

        return str_replace(
            ['\\', '"', "\r", "\n", "\t", '<', '>', '&', "'", '+'],
            ['\\\\', '\\"', '\\r', '\\n', '\\t', '\\u003C', '\\u003E', '\\u0026', '\\u0027', '\\u002B'],
            $input
        );
    }

    /**
     * Escape HTML string values
     * @param string $input
     * @return string
     */
    private static function _escapeHtmlString(string $input): string {
        if (empty($input)) return '';

        return str_replace(
            ['\\', '"', "\r", "\n", "\t"],
            ['\\\\', '\\"', '\\r', '\\n', '\\t'],
            $input
        );
    }

    /**
     * Serialize an array as dictionary
     * @param array $dict
     * @param callable $valueSerializer
     * @return string
     */
    private function _serializeDictionary(array $dict, callable $valueSerializer): string {
        $sb = [];
        $sb[] = '{';
        $first = true;

        foreach ($dict as $key => $value) {
            if (!$first) $sb[] = ',';
            $sb[] = '"';
            $sb[] = self::_escapeJsonString((string)$key);
            $sb[] = '":';
            $sb[] = $valueSerializer($value);
            $first = false;
        }

        $sb[] = '}';
        return implode('', $sb);
    }

    /**
     * Serialize TemplateData
     * @param TemplateData $data
     * @return string
     */
    private function _serializeTemplateData(TemplateData $data): string {
        $sb = [];
        $sb[] = '{"Html":"';
        $sb[] = self::_escapeJsonString($data->html);
        $sb[] = '","Json":';

        if ($data->json !== null) {
            $sb[] = '"';
            $sb[] = self::_escapeJsonString($data->json);
            $sb[] = '"';
        } else {
            $sb[] = 'null';
        }

        $sb[] = '}';
        return implode('', $sb);
    }

    /**
     * Serialize PreProcessTemplateMetadata
     * @param PreProcessTemplateMetadata $metadata
     * @return string
     */
    private function _serializePreProcessMetadata(PreProcessTemplateMetadata $metadata): string {
        $sb = [];
        $sb[] = '{';

        // OriginalContent
        $sb[] = '"OriginalContent":"';
        $sb[] = self::_escapeHtmlString($metadata->originalContent);
        $sb[] = '",';

        // Placeholders
        $sb[] = '"Placeholders":';
        $sb[] = $this->_serializePlaceholdersList($metadata->placeholders);
        $sb[] = ',';

        // SlottedTemplates
        $sb[] = '"SlottedTemplates":';
        $sb[] = $this->_serializeSlottedTemplatesList($metadata->slottedTemplates);
        $sb[] = ',';

        // JsonData
        $sb[] = '"JsonData":';
        if ($metadata->jsonData !== null) {
            if (is_string($metadata->jsonData)) {
                $jsonDataStr = $metadata->jsonData;
                if (str_starts_with($jsonDataStr, '{') || str_starts_with($jsonDataStr, '[')) {
                    // Appears to be JSON already, include as-is
                    $sb[] = $jsonDataStr;
                } else {
                    // Treat as string value
                    $sb[] = '"';
                    $sb[] = self::_escapeJsonString($jsonDataStr);
                    $sb[] = '"';
                }
            } else {
                // For objects, just output the class name like C# does
                $sb[] = '"';
                $sb[] = self::_escapeJsonString(get_class($metadata->jsonData));
                $sb[] = '"';
            }
        } else {
            $sb[] = 'null';
        }
        $sb[] = ',';

        // JsonPlaceholders
        $sb[] = '"JsonPlaceholders":';
        $sb[] = $this->_serializeJsonPlaceholdersList($metadata->jsonPlaceholders);
        $sb[] = ',';

        // ReplacementMappings
        $sb[] = '"ReplacementMappings":';
        $sb[] = $this->_serializeReplacementMappingsList($metadata->replacementMappings);
        $sb[] = ',';

        // Boolean properties
        $sb[] = '"HasPlaceholders":';
        $sb[] = $metadata->hasPlaceholders ? 'true' : 'false';
        $sb[] = ',"HasSlottedTemplates":';
        $sb[] = $metadata->hasSlottedTemplates ? 'true' : 'false';
        $sb[] = ',"HasJsonData":';
        $sb[] = $metadata->hasJsonData ? 'true' : 'false';
        $sb[] = ',"HasJsonPlaceholders":';
        $sb[] = $metadata->hasJsonPlaceholders ? 'true' : 'false';
        $sb[] = ',"HasReplacementMappings":';
        $sb[] = $metadata->hasReplacementMappings ? 'true' : 'false';
        $sb[] = ',"RequiresProcessing":';
        $sb[] = $metadata->requiresProcessing ? 'true' : 'false';

        $sb[] = '}';
        return implode('', $sb);
    }

    /**
     * Serialize list of placeholders
     * @param array $placeholders
     * @return string
     */
    private function _serializePlaceholdersList(array $placeholders): string {
        $sb = [];
        $sb[] = '[';

        for ($i = 0; $i < count($placeholders); $i++) {
            if ($i > 0) $sb[] = ',';
            $sb[] = $this->_serializePlaceholder($placeholders[$i]);
        }

        $sb[] = ']';
        return implode('', $sb);
    }

    /**
     * Serialize placeholder
     * @param object $placeholder
     * @return string
     */
    private function _serializePlaceholder($placeholder): string {
        $sb = [];
        $sb[] = '{';
        $sb[] = '"Name":"';
        $sb[] = self::_escapeJsonString($placeholder->name);
        $sb[] = '","StartIndex":';
        $sb[] = (string)$placeholder->startIndex;
        $sb[] = ',"EndIndex":';
        $sb[] = (string)$placeholder->endIndex;
        $sb[] = ',"FullMatch":"';
        $sb[] = self::_escapeJsonString($placeholder->fullMatch);
        $sb[] = '","TemplateKey":"';
        $sb[] = self::_escapeJsonString($placeholder->templateKey);
        $sb[] = '","JsonData":';

        if (isset($placeholder->jsonData) && $placeholder->jsonData !== null) {
            $sb[] = '"';
            $sb[] = self::_escapeJsonString((string)$placeholder->jsonData);
            $sb[] = '"';
        } else {
            $sb[] = 'null';
        }

        $sb[] = ',"NestedPlaceholders":';
        $sb[] = $this->_serializePlaceholdersList($placeholder->nestedPlaceholders);
        $sb[] = ',"NestedSlots":';
        $sb[] = $this->_serializeSlotPlaceholdersList($placeholder->nestedSlots);
        $sb[] = '}';

        return implode('', $sb);
    }

    /**
     * Serialize list of slot placeholders
     * @param array $slots
     * @return string
     */
    private function _serializeSlotPlaceholdersList(array $slots): string {
        $sb = [];
        $sb[] = '[';

        for ($i = 0; $i < count($slots); $i++) {
            if ($i > 0) $sb[] = ',';
            $sb[] = $this->_serializeSlotPlaceholder($slots[$i]);
        }

        $sb[] = ']';
        return implode('', $sb);
    }

    /**
     * Serialize slot placeholder
     * @param object $slot
     * @return string
     */
    private function _serializeSlotPlaceholder($slot): string {
        $sb = [];
        $sb[] = '{';
        $sb[] = '"Number":"';
        $sb[] = self::_escapeJsonString($slot->number);
        $sb[] = '","StartIndex":';
        $sb[] = (string)$slot->startIndex;
        $sb[] = ',"EndIndex":';
        $sb[] = (string)$slot->endIndex;
        $sb[] = ',"Content":"';
        $sb[] = self::_escapeJsonString($slot->content);
        $sb[] = '","SlotKey":"';
        $sb[] = self::_escapeJsonString($slot->slotKey);
        $sb[] = '","OpenTag":"';
        $sb[] = self::_escapeJsonString($slot->openTag);
        $sb[] = '","CloseTag":"';
        $sb[] = self::_escapeJsonString($slot->closeTag);
        $sb[] = '","NestedSlots":';
        $sb[] = $this->_serializeSlotPlaceholdersList($slot->nestedSlots);
        $sb[] = ',"NestedPlaceholders":';
        $sb[] = $this->_serializePlaceholdersList($slot->nestedPlaceholders);
        $sb[] = ',"NestedSlottedTemplates":';
        $sb[] = $this->_serializeSlottedTemplatesList($slot->nestedSlottedTemplates);
        $sb[] = ',"HasNestedPlaceholders":';
        $sb[] = $slot->hasNestedPlaceholders() ? 'true' : 'false';
        $sb[] = ',"HasNestedSlottedTemplates":';
        $sb[] = $slot->hasNestedSlottedTemplates() ? 'true' : 'false';
        $sb[] = ',"RequiresNestedProcessing":';
        $sb[] = $slot->requiresNestedProcessing() ? 'true' : 'false';
        $sb[] = '}';

        return implode('', $sb);
    }

    /**
     * Serialize list of slotted templates
     * @param array $templates
     * @return string
     */
    private function _serializeSlottedTemplatesList(array $templates): string {
        $sb = [];
        $sb[] = '[';

        for ($i = 0; $i < count($templates); $i++) {
            if ($i > 0) $sb[] = ',';
            $sb[] = $this->_serializeSlottedTemplate($templates[$i]);
        }

        $sb[] = ']';
        return implode('', $sb);
    }

    /**
     * Serialize slotted template
     * @param object $template
     * @return string
     */
    private function _serializeSlottedTemplate($template): string {
        $sb = [];
        $sb[] = '{';
        $sb[] = '"Name":"';
        $sb[] = self::_escapeJsonString($template->name);
        $sb[] = '","StartIndex":';
        $sb[] = (string)$template->startIndex;
        $sb[] = ',"EndIndex":';
        $sb[] = (string)$template->endIndex;
        $sb[] = ',"FullMatch":"';
        $sb[] = self::_escapeJsonString($template->fullMatch);
        $sb[] = '","TemplateKey":"';
        $sb[] = self::_escapeJsonString($template->templateKey);
        $sb[] = '","Slots":';
        $sb[] = $this->_serializeSlotPlaceholdersList($template->slots);
        $sb[] = '}';

        return implode('', $sb);
    }

    /**
     * Serialize list of JSON placeholders
     * @param array $placeholders
     * @return string
     */
    private function _serializeJsonPlaceholdersList(array $placeholders): string {
        $sb = [];
        $sb[] = '[';

        for ($i = 0; $i < count($placeholders); $i++) {
            if ($i > 0) $sb[] = ',';
            $sb[] = $this->_serializeJsonPlaceholder($placeholders[$i]);
        }

        $sb[] = ']';
        return implode('', $sb);
    }

    /**
     * Serialize JSON placeholder
     * @param object $placeholder
     * @return string
     */
    private function _serializeJsonPlaceholder($placeholder): string {
        $sb = [];
        $sb[] = '{';
        $sb[] = '"Key":"';
        $sb[] = self::_escapeJsonString($placeholder->key);
        $sb[] = '","Placeholder":"';
        $sb[] = self::_escapeJsonString($placeholder->placeholder);
        $sb[] = '","Value":"';
        $sb[] = self::_escapeJsonString($placeholder->value);
        $sb[] = '"}';

        return implode('', $sb);
    }

    /**
     * Serialize list of replacement mappings
     * @param array $mappings
     * @return string
     */
    private function _serializeReplacementMappingsList(array $mappings): string {
        $sb = [];
        $sb[] = '[';

        for ($i = 0; $i < count($mappings); $i++) {
            if ($i > 0) $sb[] = ',';
            $sb[] = $this->_serializeReplacementMapping($mappings[$i]);
        }

        $sb[] = ']';
        return implode('', $sb);
    }

    /**
     * Serialize replacement mapping
     * @param object $mapping
     * @return string
     */
    private function _serializeReplacementMapping($mapping): string {
        $sb = [];
        $sb[] = '{';
        $sb[] = '"StartIndex":';
        $sb[] = (string)$mapping->startIndex;
        $sb[] = ',"EndIndex":';
        $sb[] = (string)$mapping->endIndex;
        $sb[] = ',"OriginalText":"';
        $sb[] = self::_escapeHtmlString($mapping->originalText);
        $sb[] = '","ReplacementText":"';
        $sb[] = self::_escapeHtmlString($mapping->replacementText);
        $sb[] = '","Type":"';
        $sb[] = self::_escapeJsonString((string)$mapping->type);
        $sb[] = '"}';

        return implode('', $sb);
    }

    /**
     * Serialize PreprocessedSiteTemplates to JSON manually (for dump files)
     * @param \Assembler\TemplateModel\PreprocessedSiteTemplates $templates
     * @param bool $indented
     * @return string
     */
    public static function serializePreprocessedSiteTemplates($templates, bool $indented = true): string {
        if ($indented) {
            return self::_serializePreprocessedSiteTemplatesPretty($templates);
        }
        return self::_serializePreprocessedSiteTemplatesCompact($templates);
    }

    /**
     * Serialize PreprocessedSummary to JSON manually
     * @param array $summary
     * @param bool $indented
     * @return string
     */
    public static function serializePreprocessedSummary(array $summary, bool $indented = true): string {
        if ($indented) {
            return "{\n" .
                "  \"siteName\":\"" . self::_escapeJsonString($summary['siteName']) . "\",\n" .
                "  \"templatesRequiringProcessing\":" . $summary['templatesRequiringProcessing'] . ",\n" .
                "  \"templatesWithJsonData\":" . $summary['templatesWithJsonData'] . ",\n" .
                "  \"templatesWithPlaceholders\":" . $summary['templatesWithPlaceholders'] . ",\n" .
                "  \"totalTemplates\":" . $summary['totalTemplates'] . "\n" .
                "}";
        }
        return "{\"siteName\":\"" . self::_escapeJsonString($summary['siteName']) .
            "\",\"templatesRequiringProcessing\":" . $summary['templatesRequiringProcessing'] .
            ",\"templatesWithJsonData\":" . $summary['templatesWithJsonData'] .
            ",\"templatesWithPlaceholders\":" . $summary['templatesWithPlaceholders'] .
            ",\"totalTemplates\":" . $summary['totalTemplates'] . "}";
    }

    private static function _serializePreprocessedSiteTemplatesPretty($templates): string {
        $sb = [];
        $sb[] = "{\n";
        $sb[] = "  \"siteName\":\"" . self::_escapeJsonString($templates->siteName) . "\",\n";

        // Templates
        $sb[] = "  \"templates\":{\n";
        $first = true;
        foreach ($templates->templates as $key => $template) {
            if (!$first) $sb[] = ",\n";
            $sb[] = "  \"" . self::_escapeJsonString($key) . "\":";
            $sb[] = self::_serializePreprocessedTemplatePretty($template);
            $first = false;
        }
        $sb[] = "\n  },\n";

        // RawTemplates
        $sb[] = "  \"rawTemplates\":{\n";
        $first = true;
        foreach ($templates->rawTemplates as $key => $value) {
            if (!$first) $sb[] = ",\n";
            $sb[] = "  \"" . self::_escapeJsonString($key) . "\":\"" . self::_escapeJsonString($value) . "\"";
            $first = false;
        }
        $sb[] = "\n  },\n";

        // TemplateKeys
        $sb[] = "  \"templateKeys\":[\n";
        $first = true;
        foreach ($templates->templateKeys as $key) {
            if (!$first) $sb[] = ",\n";
            $sb[] = "  \"" . self::_escapeJsonString($key) . "\"";
            $first = false;
        }
        $sb[] = "\n  ]\n";
        $sb[] = "}";

        return implode('', $sb);
    }

    private static function _serializePreprocessedSiteTemplatesCompact($templates): string {
        $sb = [];
        $sb[] = "{\"siteName\":\"" . self::_escapeJsonString($templates->siteName) . "\",";

        // Templates
        $sb[] = "\"templates\":{";
        $first = true;
        foreach ($templates->templates as $key => $template) {
            if (!$first) $sb[] = ",";
            $sb[] = "\"" . self::_escapeJsonString($key) . "\":";
            $sb[] = self::_serializePreprocessedTemplateCompact($template);
            $first = false;
        }
        $sb[] = "},";

        // RawTemplates
        $sb[] = "\"rawTemplates\":{";
        $first = true;
        foreach ($templates->rawTemplates as $key => $value) {
            if (!$first) $sb[] = ",";
            $sb[] = "\"" . self::_escapeJsonString($key) . "\":\"" . self::_escapeJsonString($value) . "\"";
            $first = false;
        }
        $sb[] = "},";

        // TemplateKeys
        $sb[] = "\"templateKeys\":[";
        $first = true;
        foreach ($templates->templateKeys as $key) {
            if (!$first) $sb[] = ",";
            $sb[] = "\"" . self::_escapeJsonString($key) . "\"";
            $first = false;
        }
        $sb[] = "]}";

        return implode('', $sb);
    }

    private static function _serializePreprocessedTemplatePretty($template): string {
        $sb = [];
        $sb[] = "{\n";
        $sb[] = "  \"originalContent\":\"" . self::_escapeJsonString($template->originalContent) . "\",\n";

        // Placeholders
        $sb[] = "  \"placeholders\":[\n";
        for ($i = 0; $i < count($template->placeholders); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializePlaceholderPretty($template->placeholders[$i]);
        }
        $sb[] = "\n],\n";

        // SlottedTemplates
        $sb[] = "  \"slottedTemplates\":[\n";
        for ($i = 0; $i < count($template->slottedTemplates); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializeSlottedTemplatePretty($template->slottedTemplates[$i]);
        }
        $sb[] = "\n],\n";

        // JsonData
        $sb[] = "  \"jsonData\":";
        if ($template->jsonData !== null) {
            $sb[] = "\"Arshu\\\\App\\\\Json\\\\JsonObject\"";
        } else {
            $sb[] = "null";
        }
        $sb[] = ",\n";

        // JsonPlaceholders
        $sb[] = "  \"jsonPlaceholders\":[\n";
        for ($i = 0; $i < count($template->jsonPlaceholders); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializeJsonPlaceholderPretty($template->jsonPlaceholders[$i]);
        }
        $sb[] = "\n],\n";

        // ReplacementMappings
        $sb[] = "  \"replacementMappings\":[\n";
        for ($i = 0; $i < count($template->replacementMappings); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializeReplacementMappingPretty($template->replacementMappings[$i]);
        }
        $sb[] = "\n],\n";

        // Boolean flags
        $sb[] = "  \"hasPlaceholders\":" . ($template->hasPlaceholders() ? 'true' : 'false') . ",\n";
        $sb[] = "  \"hasSlottedTemplates\":" . ($template->hasSlottedTemplates() ? 'true' : 'false') . ",\n";
        $sb[] = "  \"hasJsonData\":" . ($template->hasJsonData() ? 'true' : 'false') . ",\n";
        $sb[] = "  \"hasJsonPlaceholders\":" . ($template->hasJsonPlaceholders() ? 'true' : 'false') . ",\n";
        $sb[] = "  \"hasReplacementMappings\":" . ($template->hasReplacementMappings() ? 'true' : 'false') . ",\n";
        $sb[] = "  \"requiresProcessing\":" . ($template->requiresProcessing() ? 'true' : 'false') . "\n";
        $sb[] = "}";

        return implode('', $sb);
    }

    private static function _serializePreprocessedTemplateCompact($template): string {
        $sb = [];
        $sb[] = "{\"originalContent\":\"" . self::_escapeJsonString($template->originalContent) . "\",";

        // Placeholders
        $sb[] = "\"placeholders\":[";
        for ($i = 0; $i < count($template->placeholders); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializePlaceholderCompact($template->placeholders[$i]);
        }
        $sb[] = "],";

        // SlottedTemplates
        $sb[] = "\"slottedTemplates\":[";
        for ($i = 0; $i < count($template->slottedTemplates); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializeSlottedTemplateCompact($template->slottedTemplates[$i]);
        }
        $sb[] = "],";

        // JsonData
        $sb[] = "\"jsonData\":";
        if ($template->jsonData !== null) {
            $sb[] = "\"Arshu\\\\App\\\\Json\\\\JsonObject\"";
        } else {
            $sb[] = "null";
        }
        $sb[] = ",";

        // JsonPlaceholders
        $sb[] = "\"jsonPlaceholders\":[";
        for ($i = 0; $i < count($template->jsonPlaceholders); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializeJsonPlaceholderCompact($template->jsonPlaceholders[$i]);
        }
        $sb[] = "],";

        // ReplacementMappings
        $sb[] = "\"replacementMappings\":[";
        for ($i = 0; $i < count($template->replacementMappings); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializeReplacementMappingCompact($template->replacementMappings[$i]);
        }
        $sb[] = "],";

        // Boolean flags
        $sb[] = "\"hasPlaceholders\":" . ($template->hasPlaceholders() ? 'true' : 'false') . ",";
        $sb[] = "\"hasSlottedTemplates\":" . ($template->hasSlottedTemplates() ? 'true' : 'false') . ",";
        $sb[] = "\"hasJsonData\":" . ($template->hasJsonData() ? 'true' : 'false') . ",";
        $sb[] = "\"hasJsonPlaceholders\":" . ($template->hasJsonPlaceholders() ? 'true' : 'false') . ",";
        $sb[] = "\"hasReplacementMappings\":" . ($template->hasReplacementMappings() ? 'true' : 'false') . ",";
        $sb[] = "\"requiresProcessing\":" . ($template->requiresProcessing() ? 'true' : 'false') . "}";

        return implode('', $sb);
    }

    private static function _serializePlaceholderPretty($placeholder): string {
        $sb = [];
        $sb[] = "\n  {\n";
        $sb[] = "  \"name\":\"" . self::_escapeJsonString($placeholder->name) . "\",\n";
        $sb[] = "  \"startIndex\":" . $placeholder->startIndex . ",\n";
        $sb[] = "  \"endIndex\":" . $placeholder->endIndex . ",\n";
        $sb[] = "  \"fullMatch\":\"" . self::_escapeJsonString($placeholder->fullMatch) . "\",\n";
        $sb[] = "  \"templateKey\":\"" . self::_escapeJsonString($placeholder->templateKey) . "\",\n";
        $sb[] = "  \"jsonData\":";
        $sb[] = ($placeholder->jsonData !== null) ? "\"" . self::_escapeJsonString($placeholder->jsonData) . "\"" : "null";
        $sb[] = ",\n";
        $sb[] = "  \"nestedPlaceholders\":[\n";
        for ($i = 0; $i < count($placeholder->nestedPlaceholders); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializePlaceholderPretty($placeholder->nestedPlaceholders[$i]);
        }
        $sb[] = "\n],\n";
        $sb[] = "  \"nestedSlots\":[\n";
        for ($i = 0; $i < count($placeholder->nestedSlots); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializeSlotPlaceholderPretty($placeholder->nestedSlots[$i]);
        }
        $sb[] = "\n]\n";
        $sb[] = "}";
        return implode('', $sb);
    }

    private static function _serializePlaceholderCompact($placeholder): string {
        $sb = [];
        $sb[] = "{\"name\":\"" . self::_escapeJsonString($placeholder->name) . "\",";
        $sb[] = "\"startIndex\":" . $placeholder->startIndex . ",";
        $sb[] = "\"endIndex\":" . $placeholder->endIndex . ",";
        $sb[] = "\"fullMatch\":\"" . self::_escapeJsonString($placeholder->fullMatch) . "\",";
        $sb[] = "\"templateKey\":\"" . self::_escapeJsonString($placeholder->templateKey) . "\",";
        $sb[] = "\"jsonData\":";
        $sb[] = ($placeholder->jsonData !== null) ? "\"" . self::_escapeJsonString($placeholder->jsonData) . "\"" : "null";
        $sb[] = ",";
        $sb[] = "\"nestedPlaceholders\":[";
        for ($i = 0; $i < count($placeholder->nestedPlaceholders); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializePlaceholderCompact($placeholder->nestedPlaceholders[$i]);
        }
        $sb[] = "],";
        $sb[] = "\"nestedSlots\":[";
        for ($i = 0; $i < count($placeholder->nestedSlots); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializeSlotPlaceholderCompact($placeholder->nestedSlots[$i]);
        }
        $sb[] = "]}";
        return implode('', $sb);
    }

    private static function _serializeSlottedTemplatePretty($slotted): string {
        $sb = [];
        $sb[] = "\n  {\n";
        $sb[] = "  \"name\":\"" . self::_escapeJsonString($slotted->name) . "\",\n";
        $sb[] = "  \"startIndex\":" . $slotted->startIndex . ",\n";
        $sb[] = "  \"endIndex\":" . $slotted->endIndex . ",\n";
        $sb[] = "  \"fullMatch\":\"" . self::_escapeJsonString($slotted->fullMatch) . "\",\n";
        $sb[] = "  \"templateKey\":\"" . self::_escapeJsonString($slotted->templateKey) . "\",\n";
        $sb[] = "  \"slots\":[\n";
        for ($i = 0; $i < count($slotted->slots); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializeSlotPlaceholderPretty($slotted->slots[$i]);
        }
        $sb[] = "\n]\n";
        $sb[] = "}";
        return implode('', $sb);
    }

    private static function _serializeSlottedTemplateCompact($slotted): string {
        $sb = [];
        $sb[] = "{\"name\":\"" . self::_escapeJsonString($slotted->name) . "\",";
        $sb[] = "\"startIndex\":" . $slotted->startIndex . ",";
        $sb[] = "\"endIndex\":" . $slotted->endIndex . ",";
        $sb[] = "\"fullMatch\":\"" . self::_escapeJsonString($slotted->fullMatch) . "\",";
        $sb[] = "\"templateKey\":\"" . self::_escapeJsonString($slotted->templateKey) . "\",";
        $sb[] = "\"slots\":[";
        for ($i = 0; $i < count($slotted->slots); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializeSlotPlaceholderCompact($slotted->slots[$i]);
        }
        $sb[] = "]}";
        return implode('', $sb);
    }

    private static function _serializeSlotPlaceholderPretty($slot): string {
        $sb = [];
        $sb[] = "\n  {\n";
        $sb[] = "  \"number\":\"" . self::_escapeJsonString($slot->number) . "\",\n";
        $sb[] = "  \"startIndex\":" . $slot->startIndex . ",\n";
        $sb[] = "  \"endIndex\":" . $slot->endIndex . ",\n";
        $sb[] = "  \"content\":\"" . self::_escapeJsonString($slot->content) . "\",\n";
        $sb[] = "  \"slotKey\":\"" . self::_escapeJsonString($slot->slotKey) . "\",\n";
        $sb[] = "  \"openTag\":\"" . self::_escapeJsonString($slot->openTag) . "\",\n";
        $sb[] = "  \"closeTag\":\"" . self::_escapeJsonString($slot->closeTag) . "\",\n";
        $sb[] = "  \"nestedSlots\":[\n";
        for ($i = 0; $i < count($slot->nestedSlots); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializeSlotPlaceholderPretty($slot->nestedSlots[$i]);
        }
        $sb[] = "\n],\n";
        $sb[] = "  \"nestedPlaceholders\":[\n";
        for ($i = 0; $i < count($slot->nestedPlaceholders); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializePlaceholderPretty($slot->nestedPlaceholders[$i]);
        }
        $sb[] = "\n],\n";
        $sb[] = "  \"nestedSlottedTemplates\":[\n";
        for ($i = 0; $i < count($slot->nestedSlottedTemplates); $i++) {
            if ($i > 0) $sb[] = ",\n";
            $sb[] = self::_serializeSlottedTemplatePretty($slot->nestedSlottedTemplates[$i]);
        }
        $sb[] = "\n],\n";
        $sb[] = "  \"hasNestedPlaceholders\":" . ($slot->hasNestedPlaceholders() ? 'true' : 'false') . ",\n";
        $sb[] = "  \"hasNestedSlottedTemplates\":" . ($slot->hasNestedSlottedTemplates() ? 'true' : 'false') . ",\n";
        $sb[] = "  \"requiresNestedProcessing\":" . ($slot->requiresNestedProcessing() ? 'true' : 'false') . "\n";
        $sb[] = "}";
        return implode('', $sb);
    }

    private static function _serializeSlotPlaceholderCompact($slot): string {
        $sb = [];
        $sb[] = "{\"number\":\"" . self::_escapeJsonString($slot->number) . "\",";
        $sb[] = "\"startIndex\":" . $slot->startIndex . ",";
        $sb[] = "\"endIndex\":" . $slot->endIndex . ",";
        $sb[] = "\"content\":\"" . self::_escapeJsonString($slot->content) . "\",";
        $sb[] = "\"slotKey\":\"" . self::_escapeJsonString($slot->slotKey) . "\",";
        $sb[] = "\"openTag\":\"" . self::_escapeJsonString($slot->openTag) . "\",";
        $sb[] = "\"closeTag\":\"" . self::_escapeJsonString($slot->closeTag) . "\",";
        $sb[] = "\"nestedSlots\":[";
        for ($i = 0; $i < count($slot->nestedSlots); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializeSlotPlaceholderCompact($slot->nestedSlots[$i]);
        }
        $sb[] = "],";
        $sb[] = "\"nestedPlaceholders\":[";
        for ($i = 0; $i < count($slot->nestedPlaceholders); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializePlaceholderCompact($slot->nestedPlaceholders[$i]);
        }
        $sb[] = "],";
        $sb[] = "\"nestedSlottedTemplates\":[";
        for ($i = 0; $i < count($slot->nestedSlottedTemplates); $i++) {
            if ($i > 0) $sb[] = ",";
            $sb[] = self::_serializeSlottedTemplateCompact($slot->nestedSlottedTemplates[$i]);
        }
        $sb[] = "],";
        $sb[] = "\"hasNestedPlaceholders\":" . ($slot->hasNestedPlaceholders() ? 'true' : 'false') . ",";
        $sb[] = "\"hasNestedSlottedTemplates\":" . ($slot->hasNestedSlottedTemplates() ? 'true' : 'false') . ",";
        $sb[] = "\"requiresNestedProcessing\":" . ($slot->requiresNestedProcessing() ? 'true' : 'false') . "}";
        return implode('', $sb);
    }

    private static function _serializeJsonPlaceholderPretty($jsonPlaceholder): string {
        $sb = [];
        $sb[] = "\n  {\n";
        $sb[] = "  \"key\":\"" . self::_escapeJsonString($jsonPlaceholder->key) . "\",\n";
        $sb[] = "  \"placeholder\":\"" . self::_escapeJsonString($jsonPlaceholder->placeholder) . "\",\n";
        $sb[] = "  \"value\":\"" . self::_escapeJsonString($jsonPlaceholder->value) . "\"\n";
        $sb[] = "}";
        return implode('', $sb);
    }

    private static function _serializeJsonPlaceholderCompact($jsonPlaceholder): string {
        return "{\"key\":\"" . self::_escapeJsonString($jsonPlaceholder->key) . "\"," .
            "\"placeholder\":\"" . self::_escapeJsonString($jsonPlaceholder->placeholder) . "\"," .
            "\"value\":\"" . self::_escapeJsonString($jsonPlaceholder->value) . "\"}";
    }

    private static function _serializeReplacementMappingPretty($mapping): string {
        $sb = [];
        $sb[] = "\n  {\n";
        $sb[] = "  \"startIndex\":" . $mapping->startIndex . ",\n";
        $sb[] = "  \"endIndex\":" . $mapping->endIndex . ",\n";
        $sb[] = "  \"originalText\":\"" . self::_escapeJsonString($mapping->originalText) . "\",\n";
        $sb[] = "  \"replacementText\":\"" . self::_escapeJsonString($mapping->replacementText) . "\",\n";
        $sb[] = "  \"type\":" . $mapping->type . "\n";
        $sb[] = "}";
        return implode('', $sb);
    }

    private static function _serializeReplacementMappingCompact($mapping): string {
        return "{\"startIndex\":" . $mapping->startIndex . "," .
            "\"endIndex\":" . $mapping->endIndex . "," .
            "\"originalText\":\"" . self::_escapeJsonString($mapping->originalText) . "\"," .
            "\"replacementText\":\"" . self::_escapeJsonString($mapping->replacementText) . "\"," .
            "\"type\":" . $mapping->type . "}";
    }
}

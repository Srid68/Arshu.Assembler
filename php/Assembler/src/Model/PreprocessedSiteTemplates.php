<?php
namespace Assembler\TemplateModel;
class PreprocessedSiteTemplates {
    public string $siteName = '';
    public array $templates = [];
    public array $rawTemplates = [];
    public array $templateKeys = [];
    public function toJson(bool $indented = true): string {
        $obj = [
            'siteName' => $this->siteName,
            'templates' => [],
            'rawTemplates' => $this->rawTemplates,
            'templateKeys' => array_values($this->templateKeys)
        ];
        foreach ($this->templates as $key => $template) {
            $obj['templates'][$key] = $template->toArray();
        }
        return json_encode($obj, $indented ? JSON_PRETTY_PRINT : 0);
    }
    public function createSummary(): array {
        $templatesRequiringProcessing = 0;
        $templatesWithJsonData = 0;
        $templatesWithPlaceholders = 0;
        foreach ($this->templates as $template) {
            if ($template->requiresProcessing()) $templatesRequiringProcessing++;
            if ($template->hasJsonData()) $templatesWithJsonData++;
            if ($template->hasPlaceholders()) $templatesWithPlaceholders++;
        }
        return [
            'siteName' => $this->siteName,
            'templatesRequiringProcessing' => $templatesRequiringProcessing,
            'templatesWithJsonData' => $templatesWithJsonData,
            'templatesWithPlaceholders' => $templatesWithPlaceholders,
            'totalTemplates' => count($this->templates)
        ];
    }
    public function toSummaryJson(bool $indented = true): string {
        return json_encode($this->createSummary(), $indented ? JSON_PRETTY_PRINT : 0);
    }
}

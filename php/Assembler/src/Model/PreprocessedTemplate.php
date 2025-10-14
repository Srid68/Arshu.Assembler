<?php
namespace Assembler\TemplateModel;
use Assembler\App\Json\JsonObject;
class PreprocessedTemplate {
    public string $originalContent = '';
    public array $placeholders = [];
    public array $slottedTemplates = [];
    public ?JsonObject $jsonData = null;
    public array $jsonPlaceholders = [];
    public array $replacementMappings = [];
    public function hasPlaceholders(): bool { return !empty($this->placeholders); }
    public function hasSlottedTemplates(): bool { return !empty($this->slottedTemplates); }
    public function hasJsonData(): bool { return $this->jsonData !== null && $this->jsonData->count() > 0; }
    public function hasJsonPlaceholders(): bool { return !empty($this->jsonPlaceholders); }
    public function hasReplacementMappings(): bool { return !empty($this->replacementMappings); }
    public function requiresProcessing(): bool {
        return $this->hasPlaceholders() || $this->hasSlottedTemplates() || $this->hasJsonData() || $this->hasJsonPlaceholders() || $this->hasReplacementMappings();
    }
    public function toArray(): array {
        return [
            'originalContent' => $this->originalContent,
            'placeholders' => array_map(fn($p) => $p->toArray(), $this->placeholders),
            'slottedTemplates' => array_map(fn($st) => $st->toArray(), $this->slottedTemplates),
            'jsonData' => $this->jsonData?->toArray(),
            'jsonPlaceholders' => array_map(fn($jp) => $jp->toArray(), $this->jsonPlaceholders),
            'replacementMappings' => array_map(fn($rm) => $rm->toArray(), $this->replacementMappings),
            'hasPlaceholders' => $this->hasPlaceholders(),
            'hasSlottedTemplates' => $this->hasSlottedTemplates(),
            'hasJsonData' => $this->hasJsonData(),
            'hasJsonPlaceholders' => $this->hasJsonPlaceholders(),
            'hasReplacementMappings' => $this->hasReplacementMappings(),
            'requiresProcessing' => $this->requiresProcessing()
        ];
    }
}

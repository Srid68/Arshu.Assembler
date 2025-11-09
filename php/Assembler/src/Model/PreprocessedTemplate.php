<?php
namespace Assembler\Model;
use Assembler\App\Json\JsonObject;
class PreprocessedTemplate {
    // Getter for jsonData
    public function getJsonData(): ?JsonObject {
        return $this->jsonData;
    }
    // Getter for originalContent
    public function getOriginalContent(): string {
        return $this->originalContent;
    }
    // Add a placeholder to the placeholders array
    public function addPlaceholder($placeholder): void {
        $this->placeholders[] = $placeholder;
    }

    // Add a slotted template to the slottedTemplates array
    public function addSlottedTemplate($slottedTemplate): void {
        $this->slottedTemplates[] = $slottedTemplate;
    }

    // Add a replacement mapping to the replacementMappings array
    public function addReplacementMapping($replacementMapping): void {
        $this->replacementMappings[] = $replacementMapping;
    }

    // Add a JSON placeholder to the jsonPlaceholders array
    public function addJsonPlaceholder($jsonPlaceholder): void {
        $this->jsonPlaceholders[] = $jsonPlaceholder;
    }
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

    // Added missing getter methods for compatibility
    public function getReplacementMappings(): array { return $this->replacementMappings; }
    public function getSlottedTemplates(): array { return $this->slottedTemplates; }
    public function getPlaceholders(): array { return $this->placeholders; }

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

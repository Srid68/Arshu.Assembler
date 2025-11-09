<?php
namespace Assembler\Model;
use Assembler\App\Json\JsonObject;
class TemplatePlaceholder {
    // Getter for templateKey
    public function getTemplateKey(): string {
        return $this->templateKey;
    }
    public string $name = '';
    public int $startIndex = 0;
    public int $endIndex = 0;
    public string $fullMatch = '';
    public string $templateKey = '';
    public ?JsonObject $jsonData = null;
    public array $nestedPlaceholders = [];
    public array $nestedSlots = [];
    public function toArray(): array {
        return [
            'name' => $this->name,
            'startIndex' => $this->startIndex,
            'endIndex' => $this->endIndex,
            'fullMatch' => $this->fullMatch,
            'templateKey' => $this->templateKey,
            'jsonData' => $this->jsonData?->toArray(),
            'nestedPlaceholders' => array_map(fn($np) => $np->toArray(), $this->nestedPlaceholders),
            'nestedSlots' => array_map(fn($ns) => $ns->toArray(), $this->nestedSlots)
        ];
    }
}

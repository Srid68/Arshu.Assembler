<?php
namespace Assembler\TemplateModel;
use Assembler\App\Json\JsonObject;
class TemplatePlaceholder {
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

<?php
namespace Assembler\Model;
use Assembler\App\Json\JsonObject;
class TemplatePlaceholder {
    public function __construct(
        string $name = '',
        int $startIndex = 0,
        int $endIndex = 0,
        string $fullMatch = '',
        string $templateKey = '',
        ?JsonObject $jsonData = null
    ) {
        $this->name = $name;
        $this->startIndex = $startIndex;
        $this->endIndex = $endIndex;
        $this->fullMatch = $fullMatch;
        $this->templateKey = $templateKey;
        $this->jsonData = $jsonData;
    }

    // Getter for templateKey
    public function getTemplateKey(): string {
        return $this->templateKey;
    }
    public function getFullMatch(): string {
        return $this->fullMatch;
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

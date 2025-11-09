<?php
namespace Assembler\Model;
use Assembler\App\Json\JsonObject;
class SlottedTemplate {
    // Getter for templateKey
    public function getTemplateKey(): string {
        return $this->templateKey;
    }
    // Getter for fullMatch
    public function getFullMatch(): string {
        return $this->fullMatch;
    }
    // Add a slot to the slots array
    public function addSlot($slot): void {
        $this->slots[] = $slot;
    }
    // Getter for jsonData
    public function getJsonData(): ?JsonObject {
        return $this->jsonData;
    }
    public string $name = '';
    public int $startIndex = 0;
    public int $endIndex = 0;
    public string $fullMatch = '';
    public string $innerContent = '';
    public array $slots = [];
    public string $templateKey = '';
    public ?JsonObject $jsonData = null;
    public function toArray(): array {
        return [
            'name' => $this->name,
            'startIndex' => $this->startIndex,
            'endIndex' => $this->endIndex,
            'fullMatch' => $this->fullMatch,
            'innerContent' => $this->innerContent,
            'slots' => array_map(fn($s) => $s->toArray(), $this->slots),
            'templateKey' => $this->templateKey,
            'jsonData' => $this->jsonData?->toArray()
        ];
    }
}

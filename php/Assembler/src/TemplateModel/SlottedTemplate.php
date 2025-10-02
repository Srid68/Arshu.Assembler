<?php
namespace Assembler\TemplateModel;
use Assembler\App\Json\JsonObject;
class SlottedTemplate {
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

<?php
namespace Assembler\TemplateModel;
class SlotPlaceholder {
    public array $nestedSlots = [];
    public string $number = '';
    public int $startIndex = 0;
    public int $endIndex = 0;
    public string $content = '';
    public string $slotKey = '';
    public string $openTag = '';
    public string $closeTag = '';
    public array $nestedPlaceholders = [];
    public array $nestedSlottedTemplates = [];
    public function hasNestedPlaceholders(): bool { return !empty($this->nestedPlaceholders); }
    public function hasNestedSlottedTemplates(): bool { return !empty($this->nestedSlottedTemplates); }
    public function requiresNestedProcessing(): bool { return $this->hasNestedPlaceholders() || $this->hasNestedSlottedTemplates(); }
    public function toArray(): array {
        return [
            'nestedSlots' => array_map(fn($ns) => $ns->toArray(), $this->nestedSlots),
            'number' => $this->number,
            'startIndex' => $this->startIndex,
            'endIndex' => $this->endIndex,
            'content' => $this->content,
            'slotKey' => $this->slotKey,
            'openTag' => $this->openTag,
            'closeTag' => $this->closeTag,
            'nestedPlaceholders' => array_map(fn($np) => $np->toArray(), $this->nestedPlaceholders),
            'nestedSlottedTemplates' => array_map(fn($nst) => $nst->toArray(), $this->nestedSlottedTemplates),
            'hasNestedPlaceholders' => $this->hasNestedPlaceholders(),
            'hasNestedSlottedTemplates' => $this->hasNestedSlottedTemplates(),
            'requiresNestedProcessing' => $this->requiresNestedProcessing()
        ];
    }
}

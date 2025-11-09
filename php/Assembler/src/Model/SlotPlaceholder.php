<?php
namespace Assembler\Model;
class SlotPlaceholder {
    public function __construct(
        string $number = '',
        int $startIndex = 0,
        int $endIndex = 0,
        string $content = '',
        string $slotKey = '',
        string $openTag = '',
        string $closeTag = ''
    ) {
        $this->number = $number;
        $this->startIndex = $startIndex;
        $this->endIndex = $endIndex;
        $this->content = $content;
        $this->slotKey = $slotKey;
        $this->openTag = $openTag;
        $this->closeTag = $closeTag;
    }
    
    // Getter for content
    public function getContent(): string {
        return $this->content;
    }
    
    public function getSlotKey(): string {
        return $this->slotKey;
    }
    
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
    
    // Getter methods
    public function getNestedPlaceholders(): array { return $this->nestedPlaceholders; }
    public function getNestedSlottedTemplates(): array { return $this->nestedSlottedTemplates; }
    public function getNestedSlots(): array { return $this->nestedSlots; }
    
    // Adder methods
    public function addNestedPlaceholder(TemplatePlaceholder $placeholder): void {
        $this->nestedPlaceholders[] = $placeholder;
    }
    
    public function addNestedSlottedTemplate(SlottedTemplate $template): void {
        $this->nestedSlottedTemplates[] = $template;
    }
    
    public function addNestedSlot(SlotPlaceholder $slot): void {
        $this->nestedSlots[] = $slot;
    }
    
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

<?php
namespace Assembler\Model;
class ReplacementMapping {
    public function __construct(
        string $originalText,
        string $replacementText,
        string $type,
        int $startIndex = 0,
        int $endIndex = 0,
        ?string $targetTemplateName = null
    ) {
        $this->originalText = $originalText;
        $this->replacementText = $replacementText;
        $this->type = $type;
        $this->startIndex = $startIndex;
        $this->endIndex = $endIndex;
        $this->targetTemplateName = $targetTemplateName;
    }
    public int $startIndex = 0;
    public int $endIndex = 0;
    public string $originalText = '';
    public string $replacementText = '';
    public string $type = ReplacementType::SIMPLE_TEMPLATE;
    public ?string $targetTemplateName = null;
    
    public function getStartIndex(): int {
        return $this->startIndex;
    }
    
    public function getEndIndex(): int {
        return $this->endIndex;
    }
    
    public function getOriginalText(): string {
        return $this->originalText;
    }
    
    public function getReplacementText(): string {
        return $this->replacementText;
    }
    
    public function getType(): string {
        return $this->type;
    }

    public function getTargetTemplateName(): ?string {
        return $this->targetTemplateName;
    }
    
    public function toArray(): array
    {
        return [
            'startIndex' => $this->startIndex,
            'endIndex' => $this->endIndex,
            'originalText' => $this->originalText,
            'replacementText' => $this->replacementText,
            'type' => $this->type,
            'targetTemplateName' => $this->targetTemplateName
        ];
    }

    // For debug logging
    public function __toString(): string
    {
        return json_encode($this->toArray());
    }

    // For json_encode
    public function jsonSerialize(): array
    {
        return $this->toArray();
    }
}

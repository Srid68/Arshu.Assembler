<?php
namespace Assembler\Model;
class ReplacementMapping {
    public int $startIndex = 0;
    public int $endIndex = 0;
    public string $originalText = '';
    public string $replacementText = '';
    public string $type = ReplacementType::SIMPLE_TEMPLATE;
    public function toArray(): array
    {
        return [
            'startIndex' => $this->startIndex,
            'endIndex' => $this->endIndex,
            'originalText' => $this->originalText,
            'replacementText' => $this->replacementText,
            'type' => $this->type
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

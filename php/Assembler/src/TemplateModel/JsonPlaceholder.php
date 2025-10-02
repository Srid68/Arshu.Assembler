<?php
namespace Assembler\TemplateModel;
class JsonPlaceholder {
    public string $key = '';
    public string $placeholder = '';
    public string $value = '';
    public function __construct(string $key = '', string $placeholder = '', string $value = '') {
        $this->key = $key;
        $this->placeholder = $placeholder;
        $this->value = $value;
    }
    public function toArray(): array {
        return [
            'key' => $this->key,
            'placeholder' => $this->placeholder,
            'value' => $this->value
        ];
    }
}

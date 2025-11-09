<?php

namespace Assembler\Loader;

/**
 * Simple DTO that carries raw HTML and optional JSON payloads for LoaderNormal.
 * Mirrors the C#/Node TemplateResult shape so engines can share logic.
 */
class TemplateResult
{
    public string $html;
    public ?string $json;

    public function __construct(string $html = '', ?string $json = null)
    {
        $this->html = $html;
        $this->json = $json;
    }
}

<?php

namespace Assembler\Loader;

interface ILoader
{
    public function getTemplateHtml(string $appSite, string $templateName, ?string $appView, string $appViewPrefix): mixed;
    public function getTemplateJson(string $appSite, string $templateName): mixed;
}

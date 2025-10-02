<?php
// PHP Assembler Entry Point
// This file serves as the main entry point for the Assembler library

require_once __DIR__ . '/App/JsonConverter.php';
require_once __DIR__ . '/App/Json/JsonObject.php';
require_once __DIR__ . '/App/Json/JsonArray.php';
require_once __DIR__ . '/TemplateCommon/TemplateUtils.php';
require_once __DIR__ . '/TemplateModel/ModelPreProcess.php';
require_once __DIR__ . '/TemplateEngine/EngineNormal.php';
require_once __DIR__ . '/TemplateEngine/EnginePreProcess.php';
require_once __DIR__ . '/TemplateLoader/LoaderNormal.php';
require_once __DIR__ . '/TemplateLoader/LoaderPreProcess.php';
?>
<?php
// Simple validation script to test autoloading
require_once 'vendor/autoload.php';
// Direct include for TemplateModel classes (autoloader issue)
require_once 'src/TemplateModel/ModelPreProcess.php';

// Test that all classes can be loaded
try {
    $classes = [
        'Assembler\\TemplateCommon\\TemplateUtils',
        'Assembler\\App\\Json\\JsonObject', 
        'Assembler\\App\\Json\\JsonArray',
        'Assembler\\App\\JsonConverter',
        'Assembler\\TemplateLoader\\LoaderNormal',
        'Assembler\\TemplateLoader\\LoaderPreProcess',
        'Assembler\\TemplateEngine\\EngineNormal',
        'Assembler\\TemplateEngine\\EnginePreProcess',
        'Assembler\\TemplateModel\\PreprocessedSiteTemplates',
        'Assembler\\TemplateModel\\PreprocessedTemplate',
        'Assembler\\TemplateModel\\ReplacementType'
    ];
    
    foreach ($classes as $class) {
        try {
            if (class_exists($class)) {
                echo "✅ $class loaded successfully\n";
            } else {
                echo "❌ $class failed to load\n";
                // Try to load the file directly
                $reflection = new ReflectionClass($class);
                echo "   File: " . $reflection->getFileName() . "\n";
            }
        } catch (Exception $e) {
            echo "❌ $class error: " . $e->getMessage() . "\n";
        }
    }
    
    echo "\n🎉 PHP Assembler library validation complete!\n";
    
} catch (Exception $e) {
    echo "❌ Error: " . $e->getMessage() . "\n";
    exit(1);
}
?>
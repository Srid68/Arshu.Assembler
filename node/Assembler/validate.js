// Simple validation script to test Node.js Assembler library
import * as Assembler from './src/index.js';

console.log('🧪 Testing Node.js Assembler Library...\n');

const tests = [
    { name: 'TemplateUtils', class: Assembler.TemplateUtils },
    { name: 'JsonConverter', class: Assembler.JsonConverter },
    { name: 'JsonObject', class: Assembler.JsonObject },
    { name: 'JsonArray', class: Assembler.JsonArray },
    { name: 'LoaderNormal', class: Assembler.LoaderNormal },
    { name: 'LoaderPreProcess', class: Assembler.LoaderPreProcess },
    { name: 'EngineNormal', class: Assembler.EngineNormal },
    { name: 'EnginePreProcess', class: Assembler.EnginePreProcess },
    { name: 'PreprocessedSiteTemplates', class: Assembler.PreprocessedSiteTemplates },
    { name: 'PreprocessedTemplate', class: Assembler.PreprocessedTemplate },
    { name: 'ReplacementType', class: Assembler.ReplacementType }
];

let allPassed = true;

for (const test of tests) {
    try {
        if (test.class) {
            console.log(`✅ ${test.name} loaded successfully`);
        } else {
            console.log(`❌ ${test.name} failed to load`);
            allPassed = false;
        }
    } catch (error) {
        console.log(`❌ ${test.name} error: ${error.message}`);
        allPassed = false;
    }
}

console.log(`\n🎉 Node.js Assembler library validation ${allPassed ? 'complete!' : 'failed!'}\n`);

if (!allPassed) {
    process.exit(1);
}
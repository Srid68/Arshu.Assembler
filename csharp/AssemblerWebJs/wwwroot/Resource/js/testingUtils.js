// Client-Side Testing Utils - Matches Node.js structure
// Provides standard and advanced testing functionality

class TestingUtils {
    /**
     * Runs standard tests - checks for unresolved template fields
     * @param {Array} scenarios - Array of test scenarios
     * @param {boolean} printHtmlOutput - Print HTML output
     * @param {boolean} skipDetails - Skip verbose output
     * @param {boolean} enableJsonProcessing - Enable JSON processing
     * @param {Function} progressCallback - Progress callback function
     * @returns {Array} Summary rows
     */
    static async runStandardTests(scenarios, printHtmlOutput = false, skipDetails = false, enableJsonProcessing = true, progressCallback = null) {
        console.log('\n========== ENTER RunStandardTests ==========');

        // Initialize logger for testing
        if (typeof Logger !== 'undefined') {
            Logger.configure(Logger.LogLevel.DEBUG, true); // Enable DEBUG logging and console output for debugging
            Logger.initializeContexts(['EngineNormal', 'EnginePreProcess']);
            console.log('Logger initialized with DEBUG level');
        }

        const summaryRows = [];

        for (let i = 0; i < scenarios.length; i++) {
            const scenario = scenarios[i];

            if (progressCallback) {
                progressCallback({
                    current: i + 1,
                    total: scenarios.length,
                    scenario: scenario.displayText
                });
            }

            try {
                if (!skipDetails) {
                    console.log(`${scenario.appSite}: 🔍 STANDARD TEST : appsite: ${scenario.appSite} appfile: ${scenario.appFile}`);
                    console.log(`${scenario.appSite}: AppSite: ${scenario.appSite}, AppViewPrefix: ${scenario.appFile}`);
                    console.log(`${scenario.appSite}: ${'='.repeat(50)}`);
                }

                // Load templates for this scenario
                const response = await fetch('/api/templates', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({ appsite: scenario.appSite })
                });
                if (!response.ok) {
                    throw new Error(`Failed to load templates: ${response.statusText}`);
                }

                const data = await response.json();

                // Convert templates to Map format expected by EngineNormal (matching ToolbarClientJs pattern)
                const templatesMap = new Map();
                for (const [key, template] of Object.entries(data.Templates)) {
                    templatesMap.set(key, {
                        html: template.Html || template.html || '',
                        json: template.Json || template.json || null
                    });
                }

                // Calculate appViewPrefix from appFile when appView is not empty
                const appViewPrefix = scenario.appView ? scenario.appFile : "";

                // Test Normal engine
                const normalEngine = new EngineNormal();
                normalEngine.appViewPrefix = appViewPrefix;
                const resultNormal = normalEngine.mergeTemplates(scenario.appSite, scenario.appFile, scenario.appView, templatesMap, enableJsonProcessing);

                if (!skipDetails) {
                    console.log(`${scenario.appSite}: 🧪 STANDARD TEST : scenario: AppView='${scenario.appView}', AppViewPrefix='${scenario.appViewPrefix}'`);
                    console.log(`Output length = ${resultNormal?.length || 0} Output sample: ${resultNormal?.substring(0, Math.min(200, resultNormal?.length || 0))}`);
                }

                if (printHtmlOutput) {
                    console.log(`\nFULL HTML OUTPUT for AppView '${scenario.appView}':\n${resultNormal}\n`);
                }

                // Save output file
                await TestingUtils.saveOutput(scenario.appSite, scenario.appView, 'normal', resultNormal);

                // Check for unresolved placeholders and empty output
                let hasUnresolved = false;
                let isEmpty = resultNormal.trim().length === 0;
                let startIndex = 0;

                while ((startIndex = resultNormal.indexOf("{{", startIndex)) !== -1) {
                    const endIndex = resultNormal.indexOf("}}", startIndex);
                    if (endIndex !== -1) {
                        const content = resultNormal.substring(startIndex + 2, endIndex);
                        // Only flag as unresolved if it doesn't start with $ (which are normal template placeholders)
                        if (!content.startsWith("$")) {
                            hasUnresolved = true;
                            break;
                        }
                        startIndex = endIndex + 2;
                    } else {
                        break;
                    }
                }

                const failed = hasUnresolved || isEmpty;

                summaryRows.push({
                    AppSite: scenario.appSite,
                    AppFile: scenario.appFile,
                    AppView: scenario.appView,
                    NormalPreProcess: failed ? "FAIL" : "PASS",
                    CrossViewUnMatch: "",
                    Error: failed ? (isEmpty ? "Empty" : "Unresolv") : ""
                });

                if (failed) {
                    console.log(`${scenario.appSite}: ❌ TEST FAILED: Found unresolved template fields or empty output.`);
                }

            } catch (error) {
                console.error(`❌ Test failed for ${scenario.appSite}:${scenario.appFile}: ${error.message}`);
                summaryRows.push({
                    AppSite: scenario.appSite,
                    AppFile: scenario.appFile,
                    AppView: scenario.appView,
                    NormalPreProcess: "FAIL",
                    CrossViewUnMatch: "",
                    Error: error.message.substring(0, 50)
                });
            }
        }

        // Save logs after all tests
        if (typeof Logger !== 'undefined') {
            const engineNormalLog = Logger.getContextLogs('EngineNormal');
            console.log(`EngineNormal log length: ${engineNormalLog ? engineNormalLog.length : 0}`);
            if (engineNormalLog && engineNormalLog.length > 0) {
                console.log('Saving EngineNormal log...');
                await TestingUtils.saveLog('EngineNormal', engineNormalLog);
                console.log('EngineNormal log saved');
            } else {
                console.log('No EngineNormal log to save');
            }
        }

        return summaryRows;
    }

    /**
     * Runs advanced tests - compares Normal vs PreProcess engines
     * @param {Array} scenarios - Array of test scenarios
     * @param {boolean} printHtmlOutput - Print HTML output
     * @param {boolean} skipDetails - Skip verbose output
     * @param {boolean} enableJsonProcessing - Enable JSON processing
     * @param {Function} progressCallback - Progress callback function
     * @returns {Array} Summary rows
     */
    static async runAdvancedTests(scenarios, printHtmlOutput = false, skipDetails = false, enableJsonProcessing = true, progressCallback = null) {
        if (!skipDetails) {
            console.log('🔬 Advanced Mode: Running advanced tests for filtered scenarios\n');
        }

        // Initialize logger for testing
        if (typeof Logger !== 'undefined') {
            Logger.configure(Logger.LogLevel.DEBUG, true); // Enable DEBUG logging and console output for debugging
            Logger.initializeContexts(['EngineNormal', 'EnginePreProcess']);
            console.log('Logger initialized with DEBUG level');
        }

        // Group scenarios by AppSite and AppFile
        const scenarioGroups = new Map();
        for (const scenario of scenarios) {
            const key = `${scenario.appSite}|${scenario.appFile}`;
            if (!scenarioGroups.has(key)) {
                scenarioGroups.set(key, []);
            }
            scenarioGroups.get(key).push(scenario);
        }

        const summaryRows = [];
        let scenarioIndex = 0;

        // Process each group
        for (const [groupKey, groupScenarios] of scenarioGroups) {
            const scenarioResults = [];

            // Test each scenario in the group
            for (const scenario of groupScenarios) {
                scenarioIndex++;

                if (progressCallback) {
                    progressCallback({
                        current: scenarioIndex,
                        total: scenarios.length,
                        scenario: scenario.displayText
                    });
                }

                try {
                    console.log(`🔍 ADVANCED TEST : appsite: ${scenario.appSite} appfile: ${scenario.appFile}`);

                    // Load templates for this scenario
                    const response = await fetch('/api/templates', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({ appsite: scenario.appSite })
                    });
                    if (!response.ok) {
                        throw new Error(`Failed to load templates: ${response.statusText}`);
                    }

                    const data = await response.json();

                    // Convert templates to Map format expected by EngineNormal (matching ToolbarClientJs pattern)
                    const normalTemplatesMap = new Map();
                    for (const [key, template] of Object.entries(data.Templates)) {
                        normalTemplatesMap.set(key, {
                            html: template.Html || template.html || '',
                            json: template.Json || template.json || null
                        });
                    }

                    // PreProcess engine can use plain object directly
                    const preprocessTemplates = data.PreProcessTemplates;

                    // Calculate appViewPrefix from appFile when appView is not empty
                    const appViewPrefix = scenario.appView ? scenario.appFile : "";

                    // Test Normal Engine
                    const normalEngine = new EngineNormal();
                    normalEngine.appViewPrefix = appViewPrefix;
                    const resultNormal = normalEngine.mergeTemplates(scenario.appSite, scenario.appFile, scenario.appView, normalTemplatesMap, enableJsonProcessing);

                    // Test PreProcess Engine
                    const preprocessEngine = new EnginePreProcess();
                    preprocessEngine.appViewPrefix = appViewPrefix;
                    const resultPreprocess = preprocessEngine.mergeTemplates(scenario.appSite, scenario.appFile, scenario.appView, preprocessTemplates, enableJsonProcessing);

                    const outputsMatch = resultNormal === resultPreprocess;

                    if (!skipDetails) {
                        console.log(`\n${scenario.appSite}: 📊 RESULTS COMPARISON:`);
                        console.log(`${scenario.appSite}: ---------------------------------------------`);
                        console.log(`${scenario.appSite}: 🔹 Both Methods:`);
                        console.log(`${scenario.appSite}:   Normal: ${resultNormal.length} chars`);
                        console.log(`${scenario.appSite}:   PreProcess: ${resultPreprocess.length} chars`);
                        console.log(`${scenario.appSite}:   ${outputsMatch ? '✅' : '❌'} Normal vs PreProcess: ${outputsMatch ? 'MATCH' : 'NO MATCH'}`);

                        if (outputsMatch) {
                            console.log(`\n${scenario.appSite}: 🎉 ALL METHODS PRODUCE IDENTICAL RESULTS! ✅`);
                        } else {
                            console.log(`\n${scenario.appSite}: ⚠️  METHODS PRODUCE DIFFERENT RESULTS! ❌`);
                        }
                    }

                    if (printHtmlOutput) {
                        if (scenario.appView) {
                            console.log(`\nFULL HTML OUTPUT (Normal) for AppView '${scenario.appView}':\n${resultNormal}`);
                            console.log(`\nFULL HTML OUTPUT (PreProcess) for AppView '${scenario.appView}':\n${resultPreprocess}`);
                        } else {
                            console.log(`\n📋 FINAL OUTPUT SAMPLE (full HTML):\n${scenario.appSite}: ${resultNormal}`);
                        }
                    }

                    // Save output files
                    await TestingUtils.saveOutput(scenario.appSite, scenario.appView, 'normal', resultNormal);
                    await TestingUtils.saveOutput(scenario.appSite, scenario.appView, 'preprocess', resultPreprocess);

                    const matchResult = outputsMatch ? "PASS" : "FAIL";

                    // Store result for cross-view comparison
                    scenarioResults.push({
                        scenario: scenario,
                        normalOutput: resultNormal,
                        preprocessOutput: resultPreprocess,
                        matchStatus: matchResult
                    });

                    // Show output analysis when there's a difference
                    if (!outputsMatch) {
                        console.log(`\n🔎 Output Analysis:`);
                        TestingUtils.analyzeOutputDifferences(resultNormal, resultPreprocess);
                    }

                    // Check for unmerged template fields
                    if (!skipDetails) {
                        console.log(`\n${scenario.appSite}: 🔎 Checking for unmerged template fields in outputs...`);
                    }

                    const outputInfos = [
                        { name: 'Normal', result: resultNormal },
                        { name: 'PreProcess', result: resultPreprocess }
                    ];

                    let foundUnmerged = false;
                    for (const outputInfo of outputInfos) {
                        const unmergedFields = TestingUtils.findUnmergedTemplateFields(outputInfo.result);
                        if (unmergedFields.length > 0) {
                            const filteredFields = enableJsonProcessing ?
                                unmergedFields :
                                unmergedFields.filter(f => !f.startsWith('${{Json') && !f.startsWith('${{$Json'));

                            if (filteredFields.length > 0) {
                                if (!skipDetails) {
                                    console.log(`${scenario.appSite}:   ❌ ${outputInfo.name} output contains ${filteredFields.length} unmerged non-JSON template fields!`);
                                    for (const field of filteredFields) {
                                        console.log(`${scenario.appSite}:      Unmerged field: ${field}`);
                                    }
                                }
                                foundUnmerged = true;
                            }
                        }
                    }

                } catch (error) {
                    console.error(`❌ Test failed for ${scenario.appSite}:${scenario.appFile}: ${error.message}`);
                    scenarioResults.push({
                        scenario: scenario,
                        normalOutput: null,
                        preprocessOutput: null,
                        matchStatus: "FAIL",
                        error: error.message.substring(0, 50)
                    });
                }
            }

            // Perform cross-view comparison for this group
            let crossViewResult = "";
            if (scenarioResults.length > 1) {
                let allDiffer = true;
                for (let i = 1; i < scenarioResults.length; i++) {
                    for (let j = i + 1; j < scenarioResults.length; j++) {
                        if (scenarioResults[i].normalOutput === scenarioResults[j].normalOutput ||
                            scenarioResults[i].preprocessOutput === scenarioResults[j].preprocessOutput) {
                            allDiffer = false;
                            break;
                        }
                    }
                    if (!allDiffer) break;
                }
                crossViewResult = allDiffer ? "PASS" : "FAIL";
            }

            // Add summary rows with cross-view results
            for (let i = 0; i < scenarioResults.length; i++) {
                const result = scenarioResults[i];
                const crossView = (i > 0 && scenarioResults.length > 1) ? crossViewResult : "";

                summaryRows.push({
                    AppSite: result.scenario.appSite,
                    AppFile: result.scenario.appFile,
                    AppView: result.scenario.appView,
                    NormalPreProcess: result.matchStatus,
                    CrossViewUnMatch: crossView,
                    Error: result.error || ""
                });
            }
        }

        // Save logs after all tests
        if (typeof Logger !== 'undefined') {
            const engineNormalLog = Logger.getContextLogs('EngineNormal');
            const enginePreProcessLog = Logger.getContextLogs('EnginePreProcess');

            if (engineNormalLog && engineNormalLog.length > 0) {
                await TestingUtils.saveLog('EngineNormal', engineNormalLog);
            }
            if (enginePreProcessLog && enginePreProcessLog.length > 0) {
                await TestingUtils.saveLog('EnginePreProcess', enginePreProcessLog);
            }
        }

        return summaryRows;
    }

    /**
     * Find unmerged template fields in output
     * @param {string} output - Template output
     * @returns {Array} Array of unmerged fields
     */
    static findUnmergedTemplateFields(output) {
        const regex = /\{\{[^}]+\}\}/g;
        const matches = output.match(regex);
        return matches || [];
    }

    /**
     * Analyze differences between two outputs
     * @param {string} output1 - First output
     * @param {string} output2 - Second output
     */
    static analyzeOutputDifferences(output1, output2) {
        const lines1 = output1.split('\n');
        const lines2 = output2.split('\n');

        console.log(`   Lines: ${lines1.length} vs ${lines2.length}`);

        const commonLength = Math.min(lines1.length, lines2.length);
        for (let i = 0; i < commonLength; i++) {
            if (lines1[i] !== lines2[i]) {
                console.log(`\n   Difference at line ${i + 1}:`);
                console.log(`   Normal:    ${lines1[i].length} chars`);
                console.log(`   PreProcess:${lines2[i].length} chars`);

                const minLength = Math.min(lines1[i].length, lines2[i].length);
                for (let j = 0; j < minLength; j++) {
                    if (lines1[i][j] !== lines2[i][j]) {
                        console.log(`   First difference at character ${j + 1}: '${lines1[i][j]}' vs '${lines2[i][j]}'`);
                        break;
                    }
                }
            }
        }
    }

    /**
     * Save test results to server
     * @param {Array} summaryRows - Test summary rows
     * @param {string} testType - Test type (standardtest/advancedtest)
     * @returns {Object} Server response
     */
    static async saveTestResults(summaryRows, testType) {
        try {
            const response = await fetch(`/api/test-results?testType=${testType}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(summaryRows)
            });

            if (!response.ok) {
                throw new Error(`Failed to save test results: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            console.error('Error saving test results:', error);
            throw error;
        }
    }

    /**
     * Save log content to server
     * @param {string} context - Log context (e.g., "LoaderNormal", "EngineNormal")
     * @param {string} content - Log content
     * @returns {Object} Server response
     */
    static async saveLog(context, content) {
        try {
            const response = await fetch('/api/save-log', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ context, content })
            });

            if (!response.ok) {
                throw new Error(`Failed to save log: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            console.error('Error saving log:', error);
            throw error;
        }
    }

    /**
     * Save HTML output to server
     * @param {string} appSite - App site name
     * @param {string} appView - App view name
     * @param {string} engineType - Engine type (Normal/PreProcess)
     * @param {string} html - HTML content
     * @returns {Object} Server response
     */
    static async saveOutput(appSite, appView, engineType, html) {
        try {
            const response = await fetch('/api/save-output', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ appSite, appView, engineType, html })
            });

            if (!response.ok) {
                throw new Error(`Failed to save output: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            console.error('Error saving output:', error);
            throw error;
        }
    }

    /**
     * Initialize console log capturing
     * @returns {Object} Logger object with methods to capture and retrieve logs
     */
    static createLogger() {
        const logs = {
            LoaderNormal: [],
            EngineNormal: [],
            LoaderPreProcess: [],
            EnginePreProcess: []
        };

        const originalConsoleLog = console.log;
        const originalConsoleError = console.error;

        return {
            startCapture: (context) => {
                if (!logs[context]) logs[context] = [];

                console.log = function(...args) {
                    const message = args.map(arg => typeof arg === 'object' ? JSON.stringify(arg) : String(arg)).join(' ');
                    logs[context].push(`[${new Date().toISOString()}] ${message}`);
                    originalConsoleLog.apply(console, args);
                };

                console.error = function(...args) {
                    const message = args.map(arg => typeof arg === 'object' ? JSON.stringify(arg) : String(arg)).join(' ');
                    logs[context].push(`[${new Date().toISOString()}] ERROR: ${message}`);
                    originalConsoleError.apply(console, args);
                };
            },

            stopCapture: () => {
                console.log = originalConsoleLog;
                console.error = originalConsoleError;
            },

            getLogs: (context) => {
                return logs[context] ? logs[context].join('\n') : '';
            },

            clearLogs: (context) => {
                if (logs[context]) logs[context] = [];
            },

            saveLogs: async (context) => {
                const content = logs[context] ? logs[context].join('\n') : '';
                if (content) {
                    return await TestingUtils.saveLog(context, content);
                }
                return null;
            }
        };
    }
}

// Export for use in other scripts
if (typeof window !== 'undefined') {
    window.TestingUtils = TestingUtils;
}

import * as Assembler from '../index.js';
import { promises as fs } from 'fs';
import path from 'path';

const { LoaderNormal, LoaderPreProcess, EngineNormal, EnginePreProcess } = Assembler;

export async function runStandardTests(assemblerWebDirPath, projectDirectory, scenarios, printHtmlOutput = false, skipDetails = false, enableJsonProcessing = true) {
    const summaryRows = [];

    if (!assemblerWebDirPath) {
        console.log("❌ No assemblerWebDirPath passed");
        return summaryRows;
    }

    if (!projectDirectory) {
        console.log("❌ No projectDirectory passed");
        return summaryRows;
    }

    if (!scenarios || scenarios.length === 0) {
        console.log("❌ No scenarios passed");
        return summaryRows;
    }

    // Group scenarios by AppSite and AppFile
    const groupedScenarios = {};
    for (const scenario of scenarios) {
        const key = `${scenario.appSite}|${scenario.appFile}`;
        if (!groupedScenarios[key]) {
            groupedScenarios[key] = [];
        }
        groupedScenarios[key].push(scenario);
    }

    const outputDir = path.join(projectDirectory, 'Analysis', 'output');
    await fs.mkdir(outputDir, { recursive: true });

    for (const [key, group] of Object.entries(groupedScenarios)) {
        const testSite = group[0].appSite;
        const appFileName = group[0].appFile;

        if (!skipDetails) {
            console.log(`${testSite}: 🔍 STANDARD TEST : appsite: ${testSite} appfile: ${appFileName}`);
            console.log(`${testSite}: ${'='.repeat(50)}`);
        }

        try {
            const scenarioOutputs = [];
            const templates = LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, testSite);

            for (const scenario of group) {
                const appView = scenario.appView;
                const normalEngine = new EngineNormal();
                normalEngine.appViewPrefix = appFileName;
                const resultNormal = normalEngine.mergeTemplates(testSite, appFileName, appView, templates, enableJsonProcessing);
                scenarioOutputs.push(resultNormal || "");

                // Save HTML output to Analysis folder
                const appViewSuffix = appView ? `_${appView}` : "";
                const outputFile = path.join(outputDir, `${testSite}${appViewSuffix}_normal.html`);
                await fs.writeFile(outputFile, resultNormal || "");

                if (!skipDetails) {
                    console.log(`${testSite}: 🧪 STANDARD TEST : scenario: AppView='${appView}'`);
                    console.log(`Output length = ${resultNormal?.length || 0}`);
                }
                if (printHtmlOutput) {
                    console.log(`\nFULL HTML OUTPUT for AppView '${appView}':\n${resultNormal}\n`);
                }
            }

            // Validate for unresolved placeholders
            const scenarioUnresolved = [];
            for (const output of scenarioOutputs) {
                let hasUnresolved = false;
                const isEmpty = !output || output.trim() === "";

                // Scan for any {{...}} patterns which indicate unresolved placeholders
                let startIndex = 0;
                while (true) {
                    startIndex = output.indexOf("{{", startIndex);
                    if (startIndex === -1) break;

                    const endIndex = output.indexOf("}}", startIndex);
                    if (endIndex === -1) break;

                    // Any {{...}} pattern in final output is unresolved
                    hasUnresolved = true;
                    if (!skipDetails) {
                        const content = output.substring(startIndex, endIndex + 2);
                        console.log(`${testSite}: ❌ Found unresolved placeholder: ${content}`);
                    }
                    break;
                }
                scenarioUnresolved.push(hasUnresolved || isEmpty);
            }

            // Compare outputs for cross-view
            let matchResult = "";
            if (group.length > 2) { // default + at least two AppViews
                let allDiffer = true;
                const firstAppViewOutput = scenarioOutputs[1];
                for (let i = 2; i < scenarioOutputs.length; i++) {
                    if (scenarioOutputs[i] === firstAppViewOutput) {
                        allDiffer = false;
                        break;
                    }
                }
                matchResult = allDiffer ? "PASS" : "FAIL";
                if (!skipDetails) {
                    if (allDiffer) {
                        console.log(`✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in ${testSite} as expected.`);
                    } else {
                        console.log(`❌ FAILURE: Some outputs for AppViews MATCH in ${testSite}. Expected them to differ.`);
                    }
                }
            }

            // Add summary rows for each scenario (matching C# logic)
            for (let i = 0; i < group.length; i++) {
                const scenario = group[i];
                const crossView = (i > 0 && group.length > 2) ? matchResult : "";
                const hasUnresolved = scenarioUnresolved[i];
                const normalPreProcess = (i === 0) ? (hasUnresolved ? "FAIL" : "PASS") : "";

                summaryRows.push({
                    AppSite: testSite,
                    AppFile: appFileName,
                    AppView: scenario.appView,
                    NormalPreProcess: normalPreProcess,
                    CrossViewUnMatch: crossView,
                    Error: ""
                });
            }
        } catch (error) {
            console.log(`❌ Error in ${testSite}/${appFileName}: ${error.message}`);
            summaryRows.push({
                AppSite: testSite,
                AppFile: appFileName,
                AppView: "",
                NormalPreProcess: "",
                CrossViewUnMatch: "",
                Error: error.message
            });
        }
    }
    return summaryRows;
}

export async function runAdvancedTests(assemblerWebDirPath, projectDirectory, scenarios, printHtmlOutput = false, skipDetails = false, enableJsonProcessing = true) {
    const summaryRows = [];

    if (!assemblerWebDirPath) {
        console.log("❌ No assemblerWebDirPath passed");
        return summaryRows;
    }

    if (!projectDirectory) {
        console.log("❌ No projectDirectory passed");
        return summaryRows;
    }

    if (!scenarios || scenarios.length === 0) {
        console.log("❌ No scenarios passed");
        return summaryRows;
    }

    // Group scenarios by AppSite and AppFile
    const groupedScenarios = {};
    for (const scenario of scenarios) {
        const key = `${scenario.appSite}|${scenario.appFile}`;
        if (!groupedScenarios[key]) {
            groupedScenarios[key] = [];
        }
        groupedScenarios[key].push(scenario);
    }

    const outputDir = path.join(projectDirectory, 'Analysis', 'output');
    await fs.mkdir(outputDir, { recursive: true });

    for (const [key, group] of Object.entries(groupedScenarios)) {
        const testSite = group[0].appSite;
        const appFileName = group[0].appFile;

        if (!skipDetails) {
            console.log(`🔍 ADVANCED TEST : appsite: ${testSite} appfile: ${appFileName}`);
        }

        try {
            LoaderNormal.clearCache();
            LoaderPreProcess.clearCache();

            const templates = LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, testSite);
            const preprocessedTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, testSite).templates;

            const scenarioResults = [];

            for (const scenario of group) {
                const appView = scenario.appView;
                const normalEngine = new EngineNormal();
                normalEngine.appViewPrefix = appFileName;
                const preProcessEngine = new EnginePreProcess();
                preProcessEngine.appViewPrefix = appFileName;

                const resultNormal = normalEngine.mergeTemplates(testSite, appFileName, appView, templates, enableJsonProcessing);
                const preprocessedTemplatesObj = Object.fromEntries(preprocessedTemplates);
                const resultPreProcess = preProcessEngine.mergeTemplates(testSite, appFileName, appView, preprocessedTemplatesObj, enableJsonProcessing);

                const outputsMatch = resultNormal === resultPreProcess;
                const matchStatus = outputsMatch ? "PASS" : "FAIL";

                scenarioResults.push({
                    appView: appView,
                    normalOutput: resultNormal || "",
                    preProcessOutput: resultPreProcess || "",
                    matchStatus: matchStatus
                });

                // Save HTML outputs to Analysis folder
                const appViewSuffix = appView ? `_${appView}` : "";
                const normalOutputFile = path.join(outputDir, `${testSite}${appViewSuffix}_normal.html`);
                const preprocessOutputFile = path.join(outputDir, `${testSite}${appViewSuffix}_preprocess.html`);
                await fs.writeFile(normalOutputFile, resultNormal || "");
                await fs.writeFile(preprocessOutputFile, resultPreProcess || "");

                if (!skipDetails || !outputsMatch) {
                    console.log(`${testSite}: scenario: AppView='${appView}' - ${matchStatus}`);
                }

                if (printHtmlOutput) {
                    console.log(`\nNORMAL OUTPUT for AppView '${appView}':\n${resultNormal}\n`);
                    console.log(`\nPREPROCESS OUTPUT for AppView '${appView}':\n${resultPreProcess}\n`);
                }
            }

            // Print detailed output analysis after processing all scenarios
            if (scenarioResults.length > 0) {
                const firstResult = scenarioResults[0];
                const normalLen = firstResult.normalOutput.length;
                const preprocessLen = firstResult.preProcessOutput.length;
                console.log(`\n${testSite}: 📊 DETAILED OUTPUT ANALYSIS:`);
                console.log(`   Normal length: ${normalLen} chars`);
                console.log(`   PreProcess length: ${preprocessLen} chars`);
                console.log(`   Difference: ${Math.abs(normalLen - preprocessLen)} chars`);
            }

            // Cross-view comparison
            let crossViewResult = "";
            if (scenarioResults.length > 1) {
                let allDiffer = true;
                for (let i = 1; i < scenarioResults.length; i++) {
                    for (let j = i + 1; j < scenarioResults.length; j++) {
                        if (scenarioResults[i].normalOutput === scenarioResults[j].normalOutput ||
                            scenarioResults[i].preProcessOutput === scenarioResults[j].preProcessOutput) {
                            allDiffer = false;
                            break;
                        }
                    }
                    if (!allDiffer) break;
                }
                crossViewResult = allDiffer ? "PASS" : "FAIL";
            }

            // Add summary rows
            for (let i = 0; i < scenarioResults.length; i++) {
                const result = scenarioResults[i];
                const crossView = (i > 0 && scenarioResults.length > 1) ? crossViewResult : "";

                summaryRows.push({
                    AppSite: testSite,
                    AppFile: appFileName,
                    AppView: result.appView,
                    NormalPreProcess: result.matchStatus,
                    CrossViewUnMatch: crossView,
                    Error: ""
                });
            }
        } catch (error) {
            console.log(`❌ Error testing ${testSite} ${appFileName}: ${error.message}`);
            summaryRows.push({
                AppSite: testSite,
                AppFile: appFileName,
                AppView: "",
                NormalPreProcess: "ERROR",
                CrossViewUnMatch: "",
                Error: error.message
            });
        }
    }
    return summaryRows;
}

export function printTestSummaryTable(assemblerWebDirPath, projectDirectory, summaryRows, testType) {
    if (!summaryRows || summaryRows.length === 0) return;
    if (!testType) testType = "TEST";

    console.log(`\n==================== Node.js ${testType.toUpperCase()} SUMMARY ====================\n`);

    const headers = ["AppSite", "AppFile", "AppView", "NormalPreProcess", "CrossViewUnMatch", "Error"];
    const colCount = headers.length;
    const widths = new Array(colCount);

    // Calculate column widths
    for (let i = 0; i < colCount; i++) {
        let maxLen = headers[i].length;
        for (const row of summaryRows) {
            const value = getValue(row, i);
            if (value.length > maxLen) maxLen = value.length;
        }
        widths[i] = maxLen < 10 ? 10 : maxLen;
    }

    // Print header
    process.stdout.write("| ");
    for (let i = 0; i < colCount; i++) {
        process.stdout.write(headers[i].padEnd(widths[i]));
        if (i < colCount - 1) process.stdout.write(" | ");
    }
    console.log(" |");

    // Print divider
    process.stdout.write("|");
    for (let i = 0; i < colCount; i++) {
        process.stdout.write(" " + "-".repeat(widths[i]) + " ");
        if (i < colCount - 1) process.stdout.write("|");
    }
    console.log("|");

    // Print rows
    for (const row of summaryRows) {
        process.stdout.write("| ");
        process.stdout.write((row.AppSite || "").padEnd(widths[0]));
        process.stdout.write(" | ");
        process.stdout.write((row.AppFile || "").padEnd(widths[1]));
        process.stdout.write(" | ");
        process.stdout.write((row.AppView || "").padEnd(widths[2]));
        process.stdout.write(" | ");
        process.stdout.write((row.NormalPreProcess || "").padEnd(widths[3]));
        process.stdout.write(" | ");
        process.stdout.write((row.CrossViewUnMatch || "").padEnd(widths[4]));
        process.stdout.write(" | ");
        process.stdout.write((row.Error || "").padEnd(widths[5]));
        console.log(" |");
    }

    // Print bottom divider
    process.stdout.write("|");
    for (let i = 0; i < colCount; i++) {
        process.stdout.write(" " + "-".repeat(widths[i]) + " ");
        if (i < colCount - 1) process.stdout.write("|");
    }
    console.log("|");

    // Save HTML and JSON files
    try {
        const reportsDir = path.join(projectDirectory, 'Analysis', 'Reports');
        fs.mkdir(reportsDir, { recursive: true }).then(() => {
            const testTypeFile = testType.replace(/\s/g, "").replace(/-/g, "").toLowerCase();
            const outFile = path.join(reportsDir, `nodejs_${testTypeFile}_Summary.html`);
            const jsonFile = path.join(reportsDir, `nodejs_${testTypeFile}_Summary.json`);

            const htmlContent = generateSummaryHtml(summaryRows, testType);
            fs.writeFile(outFile, htmlContent).then(() => {
                console.log(`Test summary HTML saved to: ${outFile}`);
            });

            fs.writeFile(jsonFile, JSON.stringify(summaryRows, null, 2)).then(() => {
                console.log(`Test summary JSON saved to: ${jsonFile}`);
            });
        });
    } catch (error) {
        console.error(`Error saving test summary files: ${error.message}`);
    }

    console.log("\n======================================================\n");
}

function generateSummaryHtml(summaryRows, testType) {
    let html = `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Node.js ${testType} Summary</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        h1 { color: #333; }
        .table-container { overflow-x: auto; }
        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }
        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }
        th { background-color: #4CAF50; color: white; }
        tr:nth-child(even) { background-color: #f2f2f2; }
        .pass { color: green; font-weight: bold; }
        .fail { color: red; font-weight: bold; }
        @media (max-width: 768px) {
            body { margin: 10px; }
            th, td { padding: 8px; font-size: 14px; }
            h1 { font-size: 24px; }
        }
    </style>
</head>
<body>
    <h1>Node.js ${testType} Summary</h1>
    <div class="meta" style="color: #666; font-style: italic; margin-bottom: 10px;">Generated: ${new Date().toISOString().replace('T', ' ').substring(0, 19)} UTC</div>
    <div class="table-container">
    <table>
        <tr>
            <th>AppSite</th>
            <th>AppFile</th>
            <th>AppView</th>
            <th>OutputMatch</th>
            <th>ViewUnMatch</th>
            <th>Error</th>
        </tr>`;

    for (const row of summaryRows) {
        const outputMatchClass = row.NormalPreProcess === "PASS" ? "pass" : (row.NormalPreProcess === "FAIL" ? "fail" : "");
        const viewUnMatchClass = row.CrossViewUnMatch === "PASS" ? "pass" : (row.CrossViewUnMatch === "FAIL" ? "fail" : "");

        html += `
        <tr>
            <td>${row.AppSite}</td>
            <td>${row.AppFile}</td>
            <td>${row.AppView}</td>
            <td class="${outputMatchClass}">${row.NormalPreProcess}</td>
            <td class="${viewUnMatchClass}">${row.CrossViewUnMatch}</td>
            <td>${row.Error}</td>
        </tr>`;
    }

    html += `
    </table>
    </div>
</body>
</html>`;

    return html;
}

function getValue(row, index) {
    switch (index) {
        case 0: return row.AppSite || "";
        case 1: return row.AppFile || "";
        case 2: return row.AppView || "";
        case 3: return row.NormalPreProcess || "";
        case 4: return row.CrossViewUnMatch || "";
        case 5: return row.Error || "";
        default: return "";
    }
}

export async function dumpPreprocessedTemplateStructures(assemblerWebDirPath, projectDirectory, scenarios, skipDetails = false) {
    if (!assemblerWebDirPath) {
        if (!skipDetails)
            console.log("❌ No assemblerWebDirPath passed for DumpPreprocessedTemplateStructures");
        return;
    }

    if (!projectDirectory) {
        if (!skipDetails)
            console.log("❌ No projectDirectory passed for DumpPreprocessedTemplateStructures");
        return;
    }

    if (!scenarios || scenarios.length === 0) {
        if (!skipDetails)
            console.log("❌ No scenarios passed for DumpPreprocessedTemplateStructures");
        return;
    }

    // Get unique AppSites from scenarios
    const appSites = [...new Set(scenarios.map(s => s.appSite))];

    for (const site of appSites) {
        try {
            LoaderNormal.clearCache();
            LoaderPreProcess.clearCache();

            const templates = LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, site);
            const preprocessedSiteTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, site);

            const fullJson = Assembler.ApiResponse.serializePreprocessedSiteTemplates(preprocessedSiteTemplates, true);

            // Save to file for easier analysis
            const outputDir = path.join(projectDirectory, 'Analysis');
            await fs.mkdir(outputDir, { recursive: true });

            const summaryFile = path.join(outputDir, `${site}_summary.json`);
            const fullFile = path.join(outputDir, `${site}_full.json`);

            // Delete existing files
            try {
                await fs.unlink(summaryFile);
            } catch {}
            try {
                await fs.unlink(fullFile);
            } catch {}

            const summary = Assembler.ApiResponse.createPreprocessedSummary(preprocessedSiteTemplates);
            await fs.writeFile(summaryFile, Assembler.ApiResponse.serializePreprocessedSummary(summary, true));
            await fs.writeFile(fullFile, fullJson);

            if (!skipDetails) {
                console.log(`✅ Dumped structure for ${site}`);
                console.log(`   Summary: ${summaryFile}`);
                console.log(`   Full: ${fullFile}`);
            }
        } catch (error) {
            if (!skipDetails) {
                console.log(`❌ Error dumping structure for ${site}: ${error.message}`);
            }
        }
    }
}

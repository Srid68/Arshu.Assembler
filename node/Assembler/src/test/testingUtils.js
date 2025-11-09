import * as Assembler from '../index.js';
import { promises as fs } from 'fs';
import path from 'path';

const { LoaderNormal, LoaderPreProcess, EngineNormal, EnginePreProcess, LoaderNormalJson, LoaderPreProcessJson, EngineNormalJson, EnginePreProcessJson } = Assembler;

export async function runStandardTests(assemblerWebDirPath, projectDirectory, scenarios, searchAppSites, printHtmlOutput = false, skipDetails = false, enableJsonProcessing = true) {
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
            const templates = LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites);
            const preprocessedSiteTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites);
            
            const loaderNormalJson = new LoaderNormalJson(assemblerWebDirPath, testSite, searchAppSites);
            await loaderNormalJson.load();
            
            const loaderPreProcessJson = new LoaderPreProcessJson(assemblerWebDirPath, testSite, searchAppSites);
            await loaderPreProcessJson.load();

            for (const scenario of group) {
                const appView = scenario.appView;

                const normalEngine = new EngineNormal();
                normalEngine.appViewPrefix = appFileName;
                const resultNormal = normalEngine.mergeTemplates(testSite, appFileName, appView, templates, searchAppSites, enableJsonProcessing);

                const preProcessEngine = new EnginePreProcess();
                preProcessEngine.appViewPrefix = appFileName;
                const resultPreProcess = preProcessEngine.mergeTemplates(testSite, appFileName, appView, preprocessedSiteTemplates.templates, searchAppSites, enableJsonProcessing);

                const normalJsonEngine = new EngineNormalJson();
                normalJsonEngine.appViewPrefix = appFileName;
                const resultNormalJson = normalJsonEngine.mergeTemplates(testSite, appFileName, appView, loaderNormalJson, enableJsonProcessing);

                const preProcessJsonEngine = new EnginePreProcessJson();
                preProcessJsonEngine.appViewPrefix = appFileName;
                const resultPreProcessJson = preProcessJsonEngine.mergeTemplates(testSite, appFileName, appView, loaderPreProcessJson, enableJsonProcessing);

                const appViewSuffix = appView ? `_${appView}` : "";
                await fs.writeFile(path.join(outputDir, `${testSite}${appViewSuffix}_normal.html`), resultNormal || "");
                await fs.writeFile(path.join(outputDir, `${testSite}${appViewSuffix}_preprocess.html`), resultPreProcess || "");
                await fs.writeFile(path.join(outputDir, `${testSite}${appViewSuffix}_normalJson.html`), resultNormalJson || "");
                await fs.writeFile(path.join(outputDir, `${testSite}${appViewSuffix}_preProcessJson.html`), resultPreProcessJson || "");

                const outputsMatch = resultNormal === resultPreProcess && resultNormal === resultNormalJson && resultNormal === resultPreProcessJson;
                const matchStatus = outputsMatch ? "PASS" : "FAIL";

                if (!skipDetails) {
                    console.log(`${testSite}: 🧪 STANDARD TEST : scenario: AppView='${appView}' - ${matchStatus}`);
                    console.log(`  Normal: ${resultNormal?.length || 0}, PreProcess: ${resultPreProcess?.length || 0}, NormalJson: ${resultNormalJson?.length || 0}, PreProcessJson: ${resultPreProcessJson?.length || 0}`);
                }

                if (printHtmlOutput) {
                    console.log(`\nFULL HTML OUTPUT for AppView '${appView}':\n${resultNormal}\n`);
                }

                summaryRows.push({
                    AppSite: testSite,
                    AppFile: appFileName,
                    AppView: scenario.appView,
                    NormalSize: resultNormal?.length || 0,
                    PreProcessSize: resultPreProcess?.length || 0,
                    NormalJsonSize: resultNormalJson?.length || 0,
                    PreProcessJsonSize: resultPreProcessJson?.length || 0,
                    AllMatch: matchStatus,
                    Error: ""
                });
            }
        } catch (error) {
            console.log(`❌ Error in ${testSite}/${appFileName}: ${error.message}`);
            summaryRows.push({
                AppSite: testSite,
                AppFile: appFileName,
                AppView: "",
                AllMatch: "ERROR",
                Error: error.message
            });
        }
    }
    return summaryRows;
}

export async function runAdvancedTests(assemblerWebDirPath, projectDirectory, scenarios, searchAppSites, printHtmlOutput = false, skipDetails = false, enableJsonProcessing = true) {
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
            const loaderNormalJson = new LoaderNormalJson(assemblerWebDirPath, testSite, searchAppSites);
            await loaderNormalJson.load();
            const loaderPreProcessJson = new LoaderPreProcessJson(assemblerWebDirPath, testSite, searchAppSites);
            await loaderPreProcessJson.load();

            const templates = LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites);
            const preprocessedTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, testSite, searchAppSites).templates;

            const scenarioResults = [];

            for (const scenario of group) {
                const appView = scenario.appView;
                const normalEngine = new EngineNormal();
                normalEngine.appViewPrefix = appFileName;
                const preProcessEngine = new EnginePreProcess();
                preProcessEngine.appViewPrefix = appFileName;
                const normalJsonEngine = new EngineNormalJson();
                normalJsonEngine.appViewPrefix = appFileName;
                const preProcessJsonEngine = new EnginePreProcessJson();
                preProcessJsonEngine.appViewPrefix = appFileName;

                const resultNormal = normalEngine.mergeTemplates(testSite, appFileName, appView, templates, searchAppSites, enableJsonProcessing);
                const preprocessedTemplatesObj = Object.fromEntries(preprocessedTemplates);
                const resultPreProcess = preProcessEngine.mergeTemplates(testSite, appFileName, appView, preprocessedTemplatesObj, searchAppSites, enableJsonProcessing);
                const resultNormalJson = normalJsonEngine.mergeTemplates(testSite, appFileName, appView, loaderNormalJson, enableJsonProcessing);
                const resultPreProcessJson = preProcessJsonEngine.mergeTemplates(testSite, appFileName, appView, loaderPreProcessJson, enableJsonProcessing);

                const outputsMatch = resultNormal === resultPreProcess && resultNormal === resultNormalJson && resultNormal === resultPreProcessJson;
                const matchStatus = outputsMatch ? "PASS" : "FAIL";

                scenarioResults.push({
                    appView: appView,
                    normalOutput: resultNormal || "",
                    preProcessOutput: resultPreProcess || "",
                    normalJsonOutput: resultNormalJson || "",
                    preProcessJsonOutput: resultPreProcessJson || "",
                    matchStatus: matchStatus
                });

                const appViewSuffix = appView ? `_${appView}` : "";
                await fs.writeFile(path.join(outputDir, `${testSite}${appViewSuffix}_normal.html`), resultNormal || "");
                await fs.writeFile(path.join(outputDir, `${testSite}${appViewSuffix}_preprocess.html`), resultPreProcess || "");
                await fs.writeFile(path.join(outputDir, `${testSite}${appViewSuffix}_normalJson.html`), resultNormalJson || "");
                await fs.writeFile(path.join(outputDir, `${testSite}${appViewSuffix}_preProcessJson.html`), resultPreProcessJson || "");

                if (!skipDetails || !outputsMatch) {
                    console.log(`${testSite}: scenario: AppView='${appView}' - ${matchStatus}`);
                }

                if (printHtmlOutput) {
                    console.log(`\nNORMAL OUTPUT for AppView '${appView}':\n${resultNormal}\n`);
                    console.log(`\nPREPROCESS OUTPUT for AppView '${appView}':\n${resultPreProcess}\n`);
                    console.log(`\nNORMAL JSON OUTPUT for AppView '${appView}':\n${resultNormalJson}\n`);
                    console.log(`\nPREPROCESS JSON OUTPUT for AppView '${appView}':\n${resultPreProcessJson}\n`);
                }
            }

            if (!skipDetails && scenarioResults.length > 0) {
                const firstResult = scenarioResults[0];
                console.log(`\n${testSite}: 📊 DETAILED OUTPUT ANALYSIS:`);
                console.log(`   Normal length: ${firstResult.normalOutput.length} chars`);
                console.log(`   PreProcess length: ${firstResult.preProcessOutput.length} chars`);
                console.log(`   NormalJson length: ${firstResult.normalJsonOutput.length} chars`);
                console.log(`   PreProcessJson length: ${firstResult.preProcessJsonOutput.length} chars`);
            }

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

            for (let i = 0; i < scenarioResults.length; i++) {
                const result = scenarioResults[i];
                const crossView = (i > 0 && scenarioResults.length > 1) ? crossViewResult : "";

                summaryRows.push({
                    AppSite: testSite,
                    AppFile: appFileName,
                    AppView: result.appView,
                    NormalPreProcess: result.matchStatus,
                    CrossViewUnMatch: crossView,
                    Error: "",
                    NormalSize: result.normalOutput.length,
                    PreProcessSize: result.preProcessOutput.length,
                    NormalJsonSize: result.normalJsonOutput.length,
                    PreProcessJsonSize: result.preProcessJsonOutput.length,
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

    const isAdvancedTest = summaryRows[0].NormalSize !== undefined;

    if (isAdvancedTest) {
        console.log(`| ${"AppSite".padEnd(15)} | ${"AppFile".padEnd(10)} | ${"AppView".padEnd(10)} | ${"Normal".padEnd(8)} | ${"PreProc".padEnd(8)} | ${"NormJson".padEnd(8)} | ${"PreProcJson".padEnd(11)} | ${"Match All".padEnd(9)} | ${"ViewUnMatch".padEnd(11)} | ${"Error".padEnd(10)} |`);
        console.log(`| ${"-".repeat(15)} | ${"-".repeat(10)} | ${"-".repeat(10)} | ${"-".repeat(8)} | ${"-".repeat(8)} | ${"-".repeat(8)} | ${"-".repeat(11)} | ${"-".repeat(9)} | ${"-".repeat(11)} | ${"-".repeat(10)} |`);

        for (const row of summaryRows) {
            const allMatch = row.NormalSize === row.PreProcessSize && row.NormalSize === row.NormalJsonSize && row.NormalSize === row.PreProcessJsonSize;
            const matchIndicator = allMatch ? "✓" : "✗";
            console.log(`| ${(row.AppSite || "").padEnd(15)} | ${(row.AppFile || "").padEnd(10)} | ${(row.AppView || "").padEnd(10)} | ${String(row.NormalSize || 0).padEnd(8)} | ${String(row.PreProcessSize || 0).padEnd(8)} | ${String(row.NormalJsonSize || 0).padEnd(8)} | ${String(row.PreProcessJsonSize || 0).padEnd(11)} | ${matchIndicator.padEnd(9)} | ${(row.CrossViewUnMatch || "").padEnd(11)} | ${(row.Error || "").padEnd(10)} |`);
        }

        console.log(`| ${"-".repeat(15)} | ${"-".repeat(10)} | ${"-".repeat(10)} | ${"-".repeat(8)} | ${"-".repeat(8)} | ${"-".repeat(8)} | ${"-".repeat(11)} | ${"-".repeat(9)} | ${"-".repeat(11)} | ${"-".repeat(10)} |`);
    } else {
        console.log(`| ${"AppSite".padEnd(15)} | ${"AppFile".padEnd(10)} | ${"AppView".padEnd(10)} | ${"Normal".padEnd(8)} | ${"PreProc".padEnd(8)} | ${"NormJson".padEnd(8)} | ${"PreProcJson".padEnd(11)} | ${"All Match".padEnd(9)} | ${"Error".padEnd(10)} |`);
        console.log(`| ${"-".repeat(15)} | ${"-".repeat(10)} | ${"-".repeat(10)} | ${"-".repeat(8)} | ${"-".repeat(8)} | ${"-".repeat(8)} | ${"-".repeat(11)} | ${"-".repeat(9)} | ${"-".repeat(10)} |`);

        for (const row of summaryRows) {
            console.log(`| ${(row.AppSite || "").padEnd(15)} | ${(row.AppFile || "").padEnd(10)} | ${(row.AppView || "").padEnd(10)} | ${String(row.NormalSize || 0).padEnd(8)} | ${String(row.PreProcessSize || 0).padEnd(8)} | ${String(row.NormalJsonSize || 0).padEnd(8)} | ${String(row.PreProcessJsonSize || 0).padEnd(11)} | ${(row.AllMatch || "").padEnd(9)} | ${(row.Error || "").padEnd(10)} |`);
        }

        console.log(`| ${"-".repeat(15)} | ${"-".repeat(10)} | ${"-".repeat(10)} | ${"-".repeat(8)} | ${"-".repeat(8)} | ${"-".repeat(8)} | ${"-".repeat(11)} | ${"-".repeat(9)} | ${"-".repeat(10)} |`);
    }

    try {
        const reportsDir = path.join(projectDirectory, 'Analysis', 'Reports');
        fs.mkdir(reportsDir, { recursive: true }).then(() => {
            const testTypeFile = testType.replace(/\s/g, "").replace(/-/g, "").toLowerCase();
            const outFile = path.join(reportsDir, `nodejs_${testTypeFile}_Summary.html`);
            const jsonFile = path.join(reportsDir, `nodejs_${testTypeFile}_Summary.json`);

            const htmlContent = generateSummaryHtml(summaryRows, testType, isAdvancedTest);
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

function generateSummaryHtml(summaryRows, testType, isAdvancedTest = false) {
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
    <table>`;

    if (isAdvancedTest) {
        html += `
        <tr>
            <th>AppSite</th>
            <th>AppFile</th>
            <th>AppView</th>
            <th>Normal</th>
            <th>PreProc</th>
            <th>NormJson</th>
            <th>PreProcJson</th>
            <th>Match All</th>
            <th>ViewUnMatch</th>
            <th>Error</th>
        </tr>`;

        for (const row of summaryRows) {
            const allMatch = row.NormalSize === row.PreProcessSize && row.NormalSize === row.NormalJsonSize && row.NormalSize === row.PreProcessJsonSize;
            const matchIndicator = allMatch ? "✓" : "✗";
            const matchClass = allMatch ? "pass" : "fail";
            const viewUnMatchClass = row.CrossViewUnMatch === "PASS" ? "pass" : (row.CrossViewUnMatch === "FAIL" ? "fail" : "");

            html += `
        <tr>
            <td>${row.AppSite}</td>
            <td>${row.AppFile}</td>
            <td>${row.AppView}</td>
            <td>${row.NormalSize}</td>
            <td>${row.PreProcessSize}</td>
            <td>${row.NormalJsonSize}</td>
            <td>${row.PreProcessJsonSize}</td>
            <td class="${matchClass}">${matchIndicator}</td>
            <td class="${viewUnMatchClass}">${row.CrossViewUnMatch}</td>
            <td>${row.Error}</td>
        </tr>`;
        }
    } else {
        html += `
        <tr>
            <th>AppSite</th>
            <th>AppFile</th>
            <th>AppView</th>
            <th>Normal</th>
            <th>PreProc</th>
            <th>NormJson</th>
            <th>PreProcJson</th>
            <th>All Match</th>
            <th>Error</th>
        </tr>`;

        for (const row of summaryRows) {
            const allMatch = row.AllMatch === "PASS" ? "pass" : "fail";
            html += `
        <tr>
            <td>${row.AppSite}</td>
            <td>${row.AppFile}</td>
            <td>${row.AppView}</td>
            <td>${row.NormalSize}</td>
            <td>${row.PreProcessSize}</td>
            <td>${row.NormalJsonSize}</td>
            <td>${row.PreProcessJsonSize}</td>
            <td class="${allMatch}">${row.AllMatch}</td>
            <td>${row.Error}</td>
        </tr>`;
        }
    }

    html += `
    </table>
    </div>
</body>
</html>`;

    return html;
}

export async function dumpPreprocessedTemplateStructures(assemblerWebDirPath, projectDirectory, scenarios, searchAppSites, skipDetails = false) {
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

    const appSites = [...new Set(scenarios.map(s => s.appSite))];

    for (const site of appSites) {
        try {
            LoaderNormal.clearCache();
            LoaderPreProcess.clearCache();

            const preprocessedSiteTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, site, searchAppSites);
            const fullJson = Assembler.ApiResponse.serializePreprocessedSiteTemplates(preprocessedSiteTemplates, true);

			const outputDir = path.join(projectDirectory, 'Analysis', 'dump');
			await fs.mkdir(outputDir, { recursive: true });

			const summaryFile = path.join(outputDir, `${site}_summary.json`);
			const fullFile = path.join(outputDir, `${site}_full.json`);

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
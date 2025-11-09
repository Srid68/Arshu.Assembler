// Node.js PerformanceUtils - Shared performance comparison and summary table logic
// Matches C#/Rust/Go structure

import { promises as fs } from 'fs';
import fsSync from 'fs';
import path from 'path';
import { LoaderNormal, LoaderPreProcess, EngineNormal, EnginePreProcess } from '../index.js';

export class PerformanceUtils {
    /**
     * Runs performance comparison and returns summary rows
     * @param {string} assemblerWebDirPath
     * @param {Array} scenarios
     * @param {string} searchAppSites
     * @param {boolean} skipDetails
     * @param {boolean} enableJsonProcessing
     * @returns {Array}
     */
    static runPerformanceComparison(assemblerWebDirPath, scenarios, searchAppSites, skipDetails = false, enableJsonProcessing = true) {
        const startTime = Date.now();

        if (!assemblerWebDirPath) {
            console.log("❌ No assemblerWebDirPath passed");
            return [];
        }

        if (!scenarios || scenarios.length === 0) {
            console.log("❌ No scenarios passed");
            return [];
        }

        const iterations = 1000;
        const perfSummaryRows = [];

        for (const scenario of scenarios) {
            const scenarioStartTime = Date.now();
            const testAppSite = scenario.appSite;
            const appFileName = scenario.appFile;
            const appView = scenario.appView;
            const appViewPrefix = appView ? appView.substring(0, Math.min(appView.length, 6)) : "";

            try {
                LoaderNormal.clearCache();
                LoaderPreProcess.clearCache();
                const templates = LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, testAppSite, searchAppSites);
                const siteTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, testAppSite, searchAppSites);
                if (!templates || templates.size === 0)
                    continue;
                const mainTemplateKey = (testAppSite + "_" + appFileName).toLowerCase();
                if (!templates.has(mainTemplateKey))
                    continue;

                if (!skipDetails) {
                    console.log('-'.repeat(60));
                    console.log(`[Node.js] Testing: AppSite=${testAppSite}, AppFile=${appFileName}, AppView=${appView}`);
                    console.log(`[Node.js] Iterations: ${iterations.toLocaleString()}`);
                }
                const normalEngine = new EngineNormal();
                normalEngine.appViewPrefix = appViewPrefix;

                // JIT Warmup - run a few iterations first to warm up the V8 engine
                for (let warmup = 0; warmup < 100; warmup++) {
                    normalEngine.mergeTemplates(testAppSite, appFileName, appView, templates, searchAppSites, enableJsonProcessing);
                }

                const normalStart = process.hrtime.bigint();
                let resultNormal = "";
                for (let i = 0; i < iterations; i++) {
                    resultNormal = normalEngine.mergeTemplates(testAppSite, appFileName, appView, templates, searchAppSites, enableJsonProcessing);
                }
                const normalEnd = process.hrtime.bigint();
                const normalTimeNs = Number(normalEnd - normalStart);
                const normalTimeMs = normalTimeNs / 1_000_000;
                if (!skipDetails) {
                    console.log(`[Node.js] Normal Engine:     ${normalTimeMs.toFixed(0)}ms | Avg: ${(normalTimeMs / iterations).toFixed(3)}ms/op | Size: ${resultNormal.length} chars`);
                }
                LoaderNormal.clearCache();
                LoaderPreProcess.clearCache();
                const preProcessEngine = new EnginePreProcess();
                preProcessEngine.appViewPrefix = appViewPrefix;

                // JIT Warmup for PreProcess engine
                const preprocessedTemplatesObj = Object.fromEntries(siteTemplates.templates);
                for (let warmup = 0; warmup < 100; warmup++) {
                    preProcessEngine.mergeTemplates(testAppSite, appFileName, appView, preprocessedTemplatesObj, searchAppSites, enableJsonProcessing);
                }

                const preProcessStart = process.hrtime.bigint();
                let resultPreProcess = "";
                for (let i = 0; i < iterations; i++) {
                    resultPreProcess = preProcessEngine.mergeTemplates(testAppSite, appFileName, appView, preprocessedTemplatesObj, searchAppSites, enableJsonProcessing);
                }
                const preProcessEnd = process.hrtime.bigint();
                const preProcessTimeNs = Number(preProcessEnd - preProcessStart);
                const preProcessTimeMs = preProcessTimeNs / 1_000_000;
                if (!skipDetails) {
                    console.log(`[Node.js] PreProcess Engine: ${preProcessTimeMs.toFixed(0)}ms | Avg: ${(preProcessTimeMs / iterations).toFixed(3)}ms/op | Size: ${resultPreProcess.length} chars`);
                    const difference = preProcessTimeMs - normalTimeMs;
                    const differencePercent = normalTimeMs > 0 ? (difference / normalTimeMs) * 100 : 0;
                    console.log(`[Node.js] Performance: ${difference >= 0 ? "+" : ""}${difference.toFixed(0)}ms (${differencePercent >= 0 ? "+" : ""}${differencePercent.toFixed(1)}%) | Match: ${resultNormal === resultPreProcess ? "YES" : "NO"}`);
                }

                const scenarioTotalTime = Date.now() - scenarioStartTime;
                const elapsedTime = Date.now() - startTime;

                if (!skipDetails) {
                    console.log(`[Node.js] Scenario Total Time: ${scenarioTotalTime}ms | Elapsed: ${elapsedTime}ms`);
                }

                perfSummaryRows.push({
                    AppSite: testAppSite,
                    AppFile: appFileName,
                    AppView: appView,
                    Iterations: iterations,
                    NormalTimeNanos: normalTimeNs,
                    PreProcessTimeNanos: preProcessTimeNs,
                    NormalTimeMs: normalTimeMs,
                    PreProcessTimeMs: preProcessTimeMs,
                    OutputSize: resultNormal.length,
                    ResultsMatch: (resultNormal === resultPreProcess ? "YES" : "NO"),
                    PerfDifference: normalTimeMs > 0 ? `${((preProcessTimeMs - normalTimeMs) / normalTimeMs * 100).toFixed(1)}%` : "0%",
                    ScenarioTotalTimeMs: scenarioTotalTime,
                    ElapsedTimeMs: elapsedTime
                });
            } catch (error) {
                // Silent error handling
            }
        }

        if (!skipDetails) {
            const elapsed = Date.now() - startTime;
            console.log(`\n========== Performance Testing Completed in ${elapsed}ms ==========\n`);
        }
        return perfSummaryRows;
    }

    /**
     * Prints the performance summary table in markdown format
     * @param {string} assemblerWebDirPath
     * @param {string} projectDirectory
     * @param {Array} summaryRows
     */
    static printPerfSummaryTable(assemblerWebDirPath, projectDirectory, summaryRows) {
        if (!summaryRows || summaryRows.length === 0) {
            return;
        }

        console.log('\n==================== NODE.JS PERFORMANCE SUMMARY ====================\n');

        const headers = ['AppSite', 'AppView', 'Normal(ms)', 'PreProc(ms)', 'Match', 'PerfDiff', 'ScnTime(ms)', 'Elapsed(ms)'];
        const colCount = headers.length;
        const widths = new Array(colCount).fill(0);

        // Calculate column widths
        for (let i = 0; i < colCount; i++) {
            widths[i] = headers[i].length;
        }

        for (const row of summaryRows) {
            const normalMs = (row.NormalTimeNanos || 0) / 1_000_000;
            const preProcessMs = (row.PreProcessTimeNanos || 0) / 1_000_000;
            widths[0] = Math.max(widths[0], (row.AppSite || '').length);
            widths[1] = Math.max(widths[1], (row.AppView || '').length);
            widths[2] = Math.max(widths[2], normalMs.toFixed(2).length);
            widths[3] = Math.max(widths[3], preProcessMs.toFixed(2).length);
            widths[4] = Math.max(widths[4], (row.ResultsMatch || '').length);
            widths[5] = Math.max(widths[5], (row.PerfDifference || '').length);
            widths[6] = Math.max(widths[6], (row.ScenarioTotalTimeMs || 0).toString().length);
            widths[7] = Math.max(widths[7], (row.ElapsedTimeMs || 0).toString().length);
        }

        // Print header
        process.stdout.write('| ');
        for (let i = 0; i < colCount; i++) {
            process.stdout.write(headers[i].padEnd(widths[i]));
            if (i < colCount - 1) process.stdout.write(' | ');
        }
        console.log(' |');

        // Print divider
        process.stdout.write('|');
        for (let i = 0; i < colCount; i++) {
            process.stdout.write(' ' + '-'.repeat(widths[i]) + ' ');
            if (i < colCount - 1) process.stdout.write('|');
        }
        console.log('|');

        // Print rows
        for (const row of summaryRows) {
            const normalMs = (row.NormalTimeNanos || 0) / 1_000_000;
            const preProcessMs = (row.PreProcessTimeNanos || 0) / 1_000_000;
            process.stdout.write('| ');
            process.stdout.write((row.AppSite || '').padEnd(widths[0]));
            process.stdout.write(' | ');
            process.stdout.write((row.AppView || '').padEnd(widths[1]));
            process.stdout.write(' | ');
            process.stdout.write(normalMs.toFixed(2).padEnd(widths[2]));
            process.stdout.write(' | ');
            process.stdout.write(preProcessMs.toFixed(2).padEnd(widths[3]));
            process.stdout.write(' | ');
            process.stdout.write((row.ResultsMatch || '').padEnd(widths[4]));
            process.stdout.write(' | ');
            process.stdout.write((row.PerfDifference || '').padEnd(widths[5]));
            process.stdout.write(' | ');
            process.stdout.write((row.ScenarioTotalTimeMs || 0).toString().padEnd(widths[6]));
            process.stdout.write(' | ');
            process.stdout.write((row.ElapsedTimeMs || 0).toString().padEnd(widths[7]));
            console.log(' |');
        }

        // Print bottom divider
        process.stdout.write('|');
        for (let i = 0; i < colCount; i++) {
            process.stdout.write(' ' + '-'.repeat(widths[i]) + ' ');
            if (i < colCount - 1) process.stdout.write('|');
        }
        console.log('|');

        // Save HTML file
        try {
            const reportsDir = path.join(projectDirectory, 'Analysis', 'Reports');
            if (!fsSync.existsSync(reportsDir)) {
                fsSync.mkdirSync(reportsDir, { recursive: true });
            }

            const html = [];
            html.push('<!DOCTYPE html>');
            html.push('<html>');
            html.push('<head>');
            html.push('    <meta charset="UTF-8">');
            html.push('    <meta name="viewport" content="width=device-width, initial-scale=1.0">');
            html.push('    <title>Node.js Performance Summary Table</title>');
            html.push('    <style>');
            html.push('        body { font-family: Arial, sans-serif; margin: 20px; }');
            html.push('        h2 { color: #333; }');
            html.push('        .table-container { overflow-x: auto; }');
            html.push('        table { border-collapse: collapse; width: 100%; min-width: 600px; }');
            html.push('        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }');
            html.push('        th { background-color: #4CAF50; color: white; }');
            html.push('        tr:nth-child(even) { background-color: #f2f2f2; }');
            html.push('        @media (max-width: 768px) {');
            html.push('            body { margin: 10px; }');
            html.push('            th, td { padding: 8px; font-size: 14px; }');
            html.push('            h2 { font-size: 20px; }');
            html.push('        }');
            html.push('    </style>');
            html.push('</head>');
            html.push('<body>');
            html.push('    <h2>Node.js Performance Summary Table</h2>');
            html.push(`    <div class="meta" style="color: #666; font-style: italic; margin-bottom: 10px;">Generated: ${new Date().toISOString().replace('T', ' ').substring(0, 19)} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>`);
            html.push('    <div class="table-container">');
            html.push('    <table>');
            html.push('        <tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th><th>ScnTime(ms)</th><th>Elapsed(ms)</th></tr>');

            for (const row of summaryRows) {
                const normalMs = (row.NormalTimeNanos || 0) / 1_000_000;
                const preProcessMs = (row.PreProcessTimeNanos || 0) / 1_000_000;
                html.push('        <tr>');
                html.push(`<td>${row.AppSite || ''}</td>`);
                html.push(`<td>${row.AppView || ''}</td>`);
                html.push(`<td>${normalMs.toFixed(2)}</td>`);
                html.push(`<td>${preProcessMs.toFixed(2)}</td>`);
                html.push(`<td>${row.ResultsMatch || ''}</td>`);
                html.push(`<td>${row.PerfDifference || ''}</td>`);
                html.push(`<td>${row.ScenarioTotalTimeMs || 0}</td>`);
                html.push(`<td>${row.ElapsedTimeMs || 0}</td>`);
                html.push('</tr>');
            }

            html.push('    </table>');
            html.push('    </div>');
            html.push('</body>');
            html.push('</html>');

            const outFile = path.join(reportsDir, 'nodejs_perfsummary.html');
            fsSync.writeFileSync(outFile, html.join('\n'), 'utf8');
            console.log(`Performance summary HTML saved to: ${outFile}`);
        } catch (error) {
            console.error(`Error saving performance summary HTML: ${error.message}`);
        }

        // Save JSON file
        try {
            const reportsDir = path.join(projectDirectory, 'Analysis', 'Reports');
            if (!fsSync.existsSync(reportsDir)) {
                fsSync.mkdirSync(reportsDir, { recursive: true });
            }

            const jsonFile = path.join(reportsDir, 'nodejs_perfsummary.json');
            const jsonData = JSON.stringify(summaryRows, null, 2);
            fsSync.writeFileSync(jsonFile, jsonData, 'utf8');
            console.log(`Performance summary JSON saved to: ${jsonFile}`);
        } catch (error) {
            console.error(`Error saving performance summary JSON: ${error.message}`);
        }
    }
}

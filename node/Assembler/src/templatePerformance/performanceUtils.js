// Node.js PerformanceUtils - Shared performance comparison and summary table logic
// Matches C#/Rust/Go structure

import { promises as fs } from 'fs';
import fsSync from 'fs';
import path from 'path';
import { LoaderNormal, LoaderPreProcess, EngineNormal, EnginePreProcess } from '../index.js';

export class PerformanceUtils {
    static perfSummaryRows = [];
    
    /**
     * Runs performance comparison and returns summary rows
     * @param {string} assemblerWebDirPath
     * @param {string} appSiteFilter
     * @param {boolean} skipDetails
     * @returns {Array}
     */
    static runPerformanceComparison(assemblerWebDirPath, enableJsonProcessing, appSiteFilter = null, skipDetails = false) {
        const iterations = 1000;
        const appSitesPath = path.join(assemblerWebDirPath, 'AppSites');
        
        if (!fsSync.existsSync(appSitesPath)) {
            if (!skipDetails) {
                console.log(`❌ AppSites directory not found: ${appSitesPath}`);
            }
            return [];
        }

        const allAppSiteDirs = fsSync.readdirSync(appSitesPath, { withFileTypes: true })
            .filter(dirent => dirent.isDirectory())
            .map(dirent => dirent.name);
        
        this.perfSummaryRows = [];
        
        for (const testAppSite of allAppSiteDirs) {
            if (appSiteFilter && testAppSite.toLowerCase() !== appSiteFilter.toLowerCase()) {
                continue;
            }
            
            const appSiteDir = path.join(appSitesPath, testAppSite);
            const htmlFiles = fsSync.readdirSync(appSiteDir)
                .filter(file => file.endsWith('.html') && !file.includes('/') && !file.includes('\\'));
            
            for (const htmlFile of htmlFiles) {
                const appFileName = path.parse(htmlFile).name;
                
                try {
                    // Clear caches before loading templates
                    LoaderNormal.clearCache();
                    LoaderPreProcess.clearCache();
                    
                    const templates = LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, testAppSite);
                    const siteTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, testAppSite);
                    
                    if (!templates || templates.size === 0) {
                        if (!skipDetails) {
                            console.log(`❌ No templates found for ${testAppSite}`);
                        }
                        continue;
                    }
                    
                    const mainTemplateKey = (testAppSite + '_' + appFileName).toLowerCase();
                    if (!templates.has(mainTemplateKey)) {
                        if (!skipDetails) {
                            console.log(`❌ No main template found for ${mainTemplateKey}`);
                        }
                        continue;
                    }
                    
                    if (!skipDetails) {
                        console.log(`Template Key: ${mainTemplateKey}`);
                        console.log(`Templates available: ${templates.size}`);
                    }
                    
                    // Build AppView scenarios
                    const appViewScenarios = [['', '']]; // No AppView
                    const appSitePath = path.join(appSitesPath, testAppSite);
                    const viewsPath = path.join(appSitePath, 'Views');
                    
                    if (fsSync.existsSync(viewsPath)) {
                        const viewFiles = fsSync.readdirSync(viewsPath).filter(file => file.endsWith('.html'));
                        for (const viewFile of viewFiles) {
                            const viewName = path.parse(viewFile).name;
                            let appView = '';
                            let appViewPrefix = '';
                            
                            if (viewName.toLowerCase().includes('content')) {
                                const contentIndex = viewName.toLowerCase().indexOf('content');
                                if (contentIndex > 0) {
                                    const viewPart = viewName.substring(0, contentIndex);
                                    if (viewPart.length > 0) {
                                        appView = viewPart.charAt(0).toUpperCase() + viewPart.slice(1);
                                        appViewPrefix = appView.substring(0, Math.min(appView.length, 6));
                                    }
                                }
                            }
                            
                            if (appView) {
                                appViewScenarios.push([appView, appViewPrefix]);
                            }
                        }
                    }
                    
                    for (const [appView, appViewPrefix] of appViewScenarios) {
                        if (!skipDetails) {
                            console.log('-'.repeat(60));
                            console.log(`>>> NODE SCENARIO : '${testAppSite}', '${appFileName}', '${appView}', '${appViewPrefix}'`);
                            console.log(`Iterations per test: ${iterations.toLocaleString()}`);
                        }
                        
                        // Normal Engine
                        const normalEngine = new EngineNormal();
                        normalEngine.appViewPrefix = appViewPrefix;
                        
                        // Warmup - run a few iterations first to ensure V8 JIT optimization
                        for (let warmup = 0; warmup < 100; warmup++) {
                            normalEngine.mergeTemplates(testAppSite, appFileName, appView, templates, enableJsonProcessing);
                        }
                        
                        const normalStart = process.hrtime.bigint();
                        let resultNormal = '';
                        for (let i = 0; i < iterations; i++) {
                            resultNormal = normalEngine.mergeTemplates(testAppSite, appFileName, appView, templates, enableJsonProcessing);
                        }
                        const normalEnd = process.hrtime.bigint();
                        const normalTimeMs = Number(normalEnd - normalStart) / 1_000_000;
                        
                        if (!skipDetails) {
                            console.log(`[Normal Engine] ${iterations.toLocaleString()} iterations: ${normalTimeMs.toFixed(0)}ms`);
                            console.log(`[Normal Engine] Avg: ${(normalTimeMs / iterations).toFixed(3)}ms per op, Output size: ${resultNormal.length} chars`);
                        }
                        
                        // PreProcess Engine
                        const preProcessEngine = new EnginePreProcess();
                        preProcessEngine.appViewPrefix = appViewPrefix;
                        
                        // Warmup for PreProcess engine
                        const preprocessedTemplatesObj = Object.fromEntries(siteTemplates.templates);
                        for (let warmup = 0; warmup < 100; warmup++) {
                            preProcessEngine.mergeTemplates(testAppSite, appFileName, appView, preprocessedTemplatesObj, enableJsonProcessing);
                        }
                        
                        const preProcessStart = process.hrtime.bigint();
                        let resultPreProcess = '';
                        for (let i = 0; i < iterations; i++) {
                            resultPreProcess = preProcessEngine.mergeTemplates(testAppSite, appFileName, appView, preprocessedTemplatesObj, enableJsonProcessing);
                        }
                        const preProcessEnd = process.hrtime.bigint();
                        const preProcessTimeMs = Number(preProcessEnd - preProcessStart) / 1_000_000;
                        
                        if (!skipDetails) {
                            console.log(`[PreProcess Engine] ${iterations.toLocaleString()} iterations: ${preProcessTimeMs.toFixed(0)}ms`);
                            console.log(`[PreProcess Engine] Avg: ${(preProcessTimeMs / iterations).toFixed(3)}ms per op, Output size: ${resultPreProcess.length} chars`);
                            
                            // Comparison
                            console.log('>>> NODE PERFORMANCE COMPARISON:');
                            console.log('-'.repeat(50));
                            const difference = preProcessTimeMs - normalTimeMs;
                            const differencePercent = normalTimeMs > 0 ? (difference / normalTimeMs) * 100 : 0;
                            
                            console.log(`Time difference: ${difference.toFixed(0)}ms (${differencePercent.toFixed(1)}%)`);
                            console.log(`Results match: ${(resultNormal === resultPreProcess ? '✅ YES' : '❌ NO')}`);
                        }
                        
                        this.perfSummaryRows.push({
                            appSite: testAppSite,
                            appFile: appFileName,
                            appView: appView,
                            iterations: iterations,
                            normalTimeMs: normalTimeMs,
                            preProcessTimeMs: preProcessTimeMs,
                            outputSize: resultNormal.length,
                            resultsMatch: (resultNormal === resultPreProcess ? 'YES' : 'NO'),
                            perfDifference: normalTimeMs > 0 ? `${((preProcessTimeMs - normalTimeMs) / normalTimeMs * 100).toFixed(1)}%` : '0%'
                        });
                    }
                } catch (error) {
                    if (!skipDetails) {
                        console.log(`❌ Error in performance testing for ${testAppSite}: ${error.message}`);
                    }
                }
            }
        }
        
        return this.perfSummaryRows;
    }

    /**
     * Prints the performance summary table in markdown format
     * @param {string} assemblerWebDirPath
     * @param {Array} summaryRows
     */
    static printPerfSummaryTable(assemblerWebDirPath, summaryRows) {
        if (!summaryRows || summaryRows.length === 0) {
            console.log('No performance data to display.');
            return;
        }
        
        console.log('\n==================== NODE PERFORMANCE SUMMARY ====================\n');
        
        const headers = ['AppSite', 'AppView', 'Normal(ms)', 'PreProc(ms)', 'Match', 'PerfDiff'];
        const colWidths = [10, 7, 10, 11, 5, 8];
        
        // Print header
        console.log('| ' + headers.map((header, i) => header.padEnd(colWidths[i])).join(' | ') + ' |');
        console.log('| ' + colWidths.map(w => '-'.repeat(w)).join(' | ') + ' |');
        
        // Print data rows
        for (const row of summaryRows) {
            const dataRow = [
                (row.appSite || '').padEnd(colWidths[0]).substring(0, colWidths[0]),
                (row.appView || '').padEnd(colWidths[1]).substring(0, colWidths[1]),
                (row.normalTimeMs || 0).toFixed(2).padEnd(colWidths[2]).substring(0, colWidths[2]),
                (row.preProcessTimeMs || 0).toFixed(2).padEnd(colWidths[3]).substring(0, colWidths[3]),
                (row.resultsMatch || '').padEnd(colWidths[4]).substring(0, colWidths[4]),
                (row.perfDifference || '').padEnd(colWidths[5]).substring(0, colWidths[5])
            ];
            console.log('| ' + dataRow.join(' | ') + ' |');
        }
        
        console.log('| ' + colWidths.map(w => '-'.repeat(w)).join(' | ') + ' |');
        
        // Save files to the correct locations
        this.savePerformanceResults(assemblerWebDirPath, summaryRows);
    }

    static savePerformanceResults(assemblerWebDirPath, perfRows) {
        try {
            // Save JSON file
            const jsonFile = path.join(assemblerWebDirPath, "nodejs_perfsummary.json");
            const jsonData = JSON.stringify(perfRows, null, 2);
            fsSync.writeFileSync(jsonFile, jsonData, 'utf8');
            console.log(`Performance summary JSON saved to: ${jsonFile}`);

            // Save HTML file
            const htmlFile = path.join(assemblerWebDirPath, "nodejs_perfsummary.html");
            let html = `<!DOCTYPE html>
<html>
<head>
    <title>Node.js Performance Summary</title>
    <style>
        table { border-collapse: collapse; width: 100%; }
        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
        th { background-color: #f2f2f2; }
    </style>
</head>
<body>
    <h1>Node.js Performance Summary</h1>
    <table>
        <tr>
            <th>AppSite</th>
            <th>AppView</th>
            <th>Normal(ms)</th>
            <th>PreProcess(ms)</th>
            <th>Match</th>
            <th>Performance Difference</th>
        </tr>`;
            
            for (const row of perfRows) {
                html += `
        <tr>
            <td>${row.appSite || ''}</td>
            <td>${row.appView || ''}</td>
            <td>${(row.normalTimeMs || 0).toFixed(2)}</td>
            <td>${(row.preProcessTimeMs || 0).toFixed(2)}</td>
            <td>${row.resultsMatch || ''}</td>
            <td>${row.perfDifference || ''}</td>
        </tr>`;
            }
            
            html += `
    </table>
</body>
</html>`;

            fsSync.writeFileSync(htmlFile, html, 'utf8');
            console.log(`Performance summary HTML saved to: ${htmlFile}`);
        } catch (error) {
            console.error(`Error saving performance summary files: ${error.message}`);
        }
    }
}

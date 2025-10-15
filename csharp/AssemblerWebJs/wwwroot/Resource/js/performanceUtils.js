// Client-Side Performance Utils - Matches Node.js structure
// Provides performance testing and comparison functionality

class PerformanceUtils {
    /**
     * Runs performance comparison and returns summary rows
     * @param {Array} scenarios - Array of test scenarios
     * @param {boolean} enableJsonProcessing - Enable JSON processing
     * @param {boolean} skipDetails - Skip verbose output
     * @param {Function} progressCallback - Progress callback function
     * @returns {Array} Performance summary rows
     */
    static async runPerformanceComparison(scenarios, enableJsonProcessing = true, skipDetails = false, progressCallback = null) {
        const startTime = Date.now();
        console.log('\n========== RunPerformanceComparison ==========');
        const iterations = 1000;
        const warmupIterations = 100;
        const perfSummaryRows = [];

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
                // Load templates for this scenario
                const response = await fetch('/api/templates', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({ appsite: scenario.appSite })
                });
                if (!response.ok) {
                    continue;
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

                if (!skipDetails) {
                    console.log('-'.repeat(60));
                    console.log(`[ClientJS] Testing: AppSite=${scenario.appSite}, AppFile=${scenario.appFile}, AppView=${scenario.appView}`);
                    console.log(`[ClientJS] Iterations: ${iterations.toLocaleString()}`);
                }

                // Normal Engine
                const normalEngine = new EngineNormal();
                normalEngine.appViewPrefix = scenario.appViewPrefix;

                // Warmup - run a few iterations first to ensure JIT optimization
                for (let warmup = 0; warmup < warmupIterations; warmup++) {
                    normalEngine.mergeTemplates(scenario.appSite, scenario.appFile, scenario.appView, normalTemplatesMap, enableJsonProcessing);
                }

                const normalStart = performance.now();
                let resultNormal = '';
                for (let iter = 0; iter < iterations; iter++) {
                    resultNormal = normalEngine.mergeTemplates(scenario.appSite, scenario.appFile, scenario.appView, normalTemplatesMap, enableJsonProcessing);
                }
                const normalEnd = performance.now();
                const normalTimeMs = normalEnd - normalStart;

                if (!skipDetails) {
                    const avg = normalTimeMs / iterations;
                    console.log(`[ClientJS] Normal Engine:     ${normalTimeMs.toFixed(0)}ms | Avg: ${avg.toFixed(3)}ms/op | Size: ${resultNormal.length} chars`);
                }

                // PreProcess Engine
                const preprocessEngine = new EnginePreProcess();
                preprocessEngine.appViewPrefix = scenario.appViewPrefix;

                // Warmup for PreProcess engine
                for (let warmup = 0; warmup < warmupIterations; warmup++) {
                    preprocessEngine.mergeTemplates(scenario.appSite, scenario.appFile, scenario.appView, preprocessTemplates, enableJsonProcessing);
                }

                const preProcessStart = performance.now();
                let resultPreProcess = '';
                for (let iter = 0; iter < iterations; iter++) {
                    resultPreProcess = preprocessEngine.mergeTemplates(scenario.appSite, scenario.appFile, scenario.appView, preprocessTemplates, enableJsonProcessing);
                }
                const preProcessEnd = performance.now();
                const preProcessTimeMs = preProcessEnd - preProcessStart;

                if (!skipDetails) {
                    const avg = preProcessTimeMs / iterations;
                    console.log(`[ClientJS] PreProcess Engine: ${preProcessTimeMs.toFixed(0)}ms | Avg: ${avg.toFixed(3)}ms/op | Size: ${resultPreProcess.length} chars`);

                    const difference = preProcessTimeMs - normalTimeMs;
                    const differencePercent = normalTimeMs > 0 ? (difference / normalTimeMs) * 100 : 0;
                    const signMs = difference >= 0 ? '+' : '';
                    const signPct = differencePercent >= 0 ? '+' : '';
                    const matchStr = resultNormal === resultPreProcess ? 'YES' : 'NO';
                    console.log(`[ClientJS] Performance: ${signMs}${difference.toFixed(0)}ms (${signPct}${differencePercent.toFixed(1)}%) | Match: ${matchStr}`);
                }

                perfSummaryRows.push({
                    AppSite: scenario.appSite,
                    AppFile: scenario.appFile,
                    AppView: scenario.appView,
                    Iterations: iterations,
                    NormalTimeMs: normalTimeMs,
                    PreProcessTimeMs: preProcessTimeMs,
                    OutputSize: resultNormal.length,
                    ResultsMatch: (resultNormal === resultPreProcess ? 'YES' : 'NO'),
                    PerfDifference: normalTimeMs > 0 ? `${((preProcessTimeMs - normalTimeMs) / normalTimeMs * 100).toFixed(1)}%` : '0%'
                });

            } catch (error) {
                console.error(`Error testing ${scenario.appSite}: ${error.message}`);
            }
        }

        const elapsed = Date.now() - startTime;
        console.log(`\n========== Performance Testing Completed in ${elapsed}ms ==========\n`);
        return perfSummaryRows;
    }

    /**
     * Save performance results to server
     * @param {Array} summaryRows - Performance summary rows
     * @returns {Object} Server response
     */
    static async savePerformanceResults(summaryRows) {
        try {
            const response = await fetch('/api/performance-results', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(summaryRows)
            });

            if (!response.ok) {
                throw new Error(`Failed to save performance results: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            console.error('Error saving performance results:', error);
            throw error;
        }
    }

    /**
     * Format performance summary table as HTML string
     * @param {Array} summaryRows - Performance summary rows
     * @returns {string} HTML table
     */
    static formatPerfSummaryTable(summaryRows) {
        if (!summaryRows || summaryRows.length === 0) {
            return '<p>No performance data available.</p>';
        }

        const timestamp = new Date().toISOString().replace('T', ' ').substring(0, 19);
        let html = `<div style="color: #666; font-style: italic; margin-bottom: 10px;">Generated: ${timestamp} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>`;
        html += '<table style="width: 100%; border-collapse: collapse; margin: 16px 0;">';
        html += '<thead>';
        html += '<tr style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white;">';
        html += '<th style="border: 1px solid #ddd; padding: 12px;">AppSite</th>';
        html += '<th style="border: 1px solid #ddd; padding: 12px;">AppView</th>';
        html += '<th style="border: 1px solid #ddd; padding: 12px;">Normal(ms)</th>';
        html += '<th style="border: 1px solid #ddd; padding: 12px;">PreProc(ms)</th>';
        html += '<th style="border: 1px solid #ddd; padding: 12px;">Match</th>';
        html += '<th style="border: 1px solid #ddd; padding: 12px;">PerfDiff</th>';
        html += '</tr>';
        html += '</thead>';
        html += '<tbody>';

        for (let i = 0; i < summaryRows.length; i++) {
            const row = summaryRows[i];
            const bgColor = i % 2 === 0 ? '#f9f9f9' : 'white';
            html += `<tr style="background: ${bgColor};">`;
            html += `<td style="border: 1px solid #ddd; padding: 12px;">${row.AppSite || ''}</td>`;
            html += `<td style="border: 1px solid #ddd; padding: 12px;">${row.AppView || ''}</td>`;
            html += `<td style="border: 1px solid #ddd; padding: 12px;">${(row.NormalTimeMs || 0).toFixed(2)}</td>`;
            html += `<td style="border: 1px solid #ddd; padding: 12px;">${(row.PreProcessTimeMs || 0).toFixed(2)}</td>`;
            html += `<td style="border: 1px solid #ddd; padding: 12px;">${row.ResultsMatch || ''}</td>`;
            html += `<td style="border: 1px solid #ddd; padding: 12px;">${row.PerfDifference || ''}</td>`;
            html += '</tr>';
        }

        html += '</tbody>';
        html += '</table>';

        return html;
    }
}

// Export for use in other scripts
if (typeof window !== 'undefined') {
    window.PerformanceUtils = PerformanceUtils;
}

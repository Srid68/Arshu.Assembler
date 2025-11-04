import path from 'path';
import { fileURLToPath } from 'url';
import fsSync from 'fs';
import { Logger } from '@arshu/common';
import { ConfigUtil } from '@arshu/assembler';
import { getValidAppSites, isValidEngineType, isValidAppSite, isValidPathComponent, isValidLogContent, isValidOutputSizeWithBuffer, getTemplateTotalSize, OUTPUT_SIZE_BUFFER } from './securityValidator.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Import test and performance utilities from the assembler package
import { runStandardTests, runAdvancedTests, dumpPreprocessedTemplateStructures, printTestSummaryTable, PerformanceUtils, CommonUtil } from '@arshu/assembler';

/**
 * Get project directory
 * @returns {string} Project directory path
 */
function getProjectDirectory() {
    return process.cwd();
}

/**
 * Get wwwroot path
 * @returns {string} Wwwroot directory path
 */
function getWwwrootPath() {
    return path.join(getProjectDirectory(), 'wwwroot');
}

// Configurable rule groups for consolidated report grouping
const RULE_GROUPS = [
    'HtmlRule1',
    'HtmlRule2',
    'HtmlRule3',
    'JsonRule1',
    'JsonRule2',
    'Rule1'
];

/**
 * GET /test/standard - Run standard tests
 */
export async function testStandardEndpoint(req, res) {
    const start = Date.now()
    const projectDirectory = getProjectDirectory()
    const assemblerWebDirPath = getWwwrootPath()

    // Enable logging temporarily for tests
    const originalLogLevel = Logger.getLogLevel()

    // Configure logger with context-specific log files for StandardTests
    const templateAnalysisDir = path.join(projectDirectory, 'template_analysis')
    const logsDir = path.join(templateAnalysisDir, 'logs')
    await fsSync.promises.mkdir(logsDir, { recursive: true }).catch(() => { })

    const contextLogFiles = {
        'LoaderNormal': path.join(logsDir, 'nodejs_loadernormal.log'),
        'EngineNormal': path.join(logsDir, 'nodejs_enginenormal.log')
    }

    Logger.configure(0, false) // DEBUG level
    Logger.addContextLogFiles(contextLogFiles)

    try {
        const scenarios = ConfigUtil.getScenarios()
        const results = await runStandardTests(assemblerWebDirPath, projectDirectory, scenarios, false, true, true)
        if (results && results.length > 0) {
            await printTestSummaryTable(assemblerWebDirPath, projectDirectory, results, 'STANDARD TEST')
        }

        // Restore original log level
        Logger.setLogLevel(originalLogLevel)

        const elapsed = (Date.now() - start) / 1000
        const testCount = results.length

        // Check for failures
        const failedCount = results.filter(r =>
            r.normalPreProcess === 'FAIL' ||
            r.crossViewUnMatch === 'FAIL' ||
            (r.error && r.error !== '')
        ).length

        let message = `Successful run of Standard Tests in ${elapsed.toFixed(2)} secs (${testCount} tests)`
        if (failedCount > 0) {
            message += `\n⚠️ Warning: ${failedCount} test(s) failed`
        }

        res.json({
            success: true,
            message: message,
            elapsed: elapsed,
            testCount: testCount
        })
    } catch (error) {
        // Restore original log level
        Logger.setLogLevel(originalLogLevel)
        console.error('Error in standard tests:', error)
        res.status(500).json({ error: 'Internal server error' })
    }
}

/**
 * GET /test/advanced - Run advanced tests
 */
export async function testAdvancedEndpoint(req, res) {
    const start = Date.now()
    const projectDirectory = getProjectDirectory()
    const assemblerWebDirPath = getWwwrootPath()

    // Enable logging temporarily for tests
    const originalLogLevel = Logger.getLogLevel()

    // Configure logger with context-specific log files for AdvancedTests
    const templateAnalysisDir = path.join(projectDirectory, 'template_analysis')
    const logsDir = path.join(templateAnalysisDir, 'logs')
    await fsSync.promises.mkdir(logsDir, { recursive: true }).catch(() => { })

    const contextLogFiles = {
        'LoaderNormal': path.join(logsDir, 'nodejs_loadernormal.log'),
        'LoaderPreProcess': path.join(logsDir, 'nodejs_loaderpreprocess.log'),
        'EngineNormal': path.join(logsDir, 'nodejs_enginenormal.log'),
        'EnginePreProcess': path.join(logsDir, 'nodejs_enginepreprocess.log')
    }

    Logger.configure(0, false) // DEBUG level
    Logger.addContextLogFiles(contextLogFiles)

    try {
        const scenarios = ConfigUtil.getScenarios()
        await dumpPreprocessedTemplateStructures(assemblerWebDirPath, projectDirectory, scenarios, true)
        const results = await runAdvancedTests(assemblerWebDirPath, projectDirectory, scenarios, false, true, true)
        if (results && results.length > 0) {
            await printTestSummaryTable(assemblerWebDirPath, projectDirectory, results, 'ADVANCED TEST')
        }

        // Restore original log level
        Logger.setLogLevel(originalLogLevel)

        const elapsed = (Date.now() - start) / 1000
        const testCount = results.length

        // Check for failures
        const failedCount = results.filter(r =>
            r.normalPreProcess === 'FAIL' ||
            r.crossViewUnMatch === 'FAIL' ||
            (r.error && r.error !== '')
        ).length

        let message = `Successful run of Advanced Tests in ${elapsed.toFixed(2)} secs (${testCount} tests)`
        if (failedCount > 0) {
            message += `\n⚠️ Warning: ${failedCount} test(s) failed`
        }

        res.json({
            success: true,
            message: message,
            elapsed: elapsed,
            testCount: testCount
        })
    } catch (error) {
        // Restore original log level
        Logger.setLogLevel(originalLogLevel)
        console.error('Error in advanced tests:', error)
        res.status(500).json({ error: 'Internal server error' })
    }
}

/**
 * GET /test/performance - Run performance tests
 */
export async function testPerformanceEndpoint(req, res) {
    const start = Date.now()
    const projectDirectory = getProjectDirectory()
    const assemblerWebDirPath = getWwwrootPath()

    try {
        // Disable logging during performance tests
        Logger.setLogLevel(5) // NONE level

        const scenarios = ConfigUtil.getScenarios()
        const results = PerformanceUtils.runPerformanceComparison(assemblerWebDirPath, scenarios, true, true)
        if (results && results.length > 0) {
            PerformanceUtils.printPerfSummaryTable(assemblerWebDirPath, projectDirectory, results)
        }

        // Restore original log level
        Logger.setLogLevel(0) // DEBUG level

        const elapsed = (Date.now() - start) / 1000
        const testCount = results.length

        // Check for performance test mismatches
        const mismatchCount = results.filter(r => r.ResultsMatch !== 'YES').length

        let message = `Successful run of Performance Tests in ${elapsed.toFixed(2)} secs (${testCount} tests)`
        if (mismatchCount > 0) {
            message += `\n⚠️ Warning: ${mismatchCount} test(s) have output mismatch error in Node`
        }

        res.json({
            success: true,
            message: message,
            elapsed: elapsed,
            testCount: testCount
        })
    } catch (error) {
        console.error('Error in performance tests:', error)
        res.status(500).json({ error: 'Internal server error' })
    }
}

/**
 * GET /test/consolidate-performance - Consolidate performance data from all servers
 */
export async function testConsolidatePerformanceEndpoint(req, res) {
    const start = Date.now()
    const projectDirectory = getProjectDirectory()
    const assemblerWebDirPath = getWwwrootPath()

    // Configure logging for consolidate endpoint
    const templateAnalysisDir = path.join(projectDirectory, 'template_analysis')
    const logsDir = path.join(templateAnalysisDir, 'logs')
    await fsSync.promises.mkdir(logsDir, { recursive: true }).catch(() => { })
    const consolidateLogFile = path.join(logsDir, 'nodejs_consolidate_perf.log')

    // Log start
    const logMsg = `\n[${new Date().toISOString()}] Starting consolidate-performance endpoint\n`
    await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => { })

    try {
        // Read server configuration from servers.csv
        const serversConfigPath = path.join(assemblerWebDirPath, 'App_Data', 'servers.csv')
        let servers = []

        if (fsSync.existsSync(serversConfigPath)) {
            try {
                const csvContent = fsSync.readFileSync(serversConfigPath, 'utf8')
                const lines = csvContent.split('\n')
                for (const line of lines) {
                    const trimmedLine = line.trim()
                    if (trimmedLine === '') continue
                    const parts = trimmedLine.split(',')
                    if (parts.length >= 3) {
                        const language = parts[0].trim()
                        const method = parts[1].trim().toUpperCase()
                        const url = parts[2].trim()
                        const fileName = parts.length >= 4 ? parts[3].trim() : ''
                        if (language && method && url) {
                            servers.push({ language, method, url, fileName })
                        }
                    }
                }
            } catch (err) {
                console.error('Failed to read servers.csv:', err)
            }
        }

        if (servers.length === 0) {
            const errorMsg = 'No server configuration found. Please configure servers in App_Data/servers.csv'
            const logMsg = `[${new Date().toISOString()}] ❌ ${errorMsg}\n`
            await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => { })

            return res.json({
                success: false,
                message: errorMsg,
                elapsed: (Date.now() - start) / 1000,
                testCount: 0
            })
        }

        const serversProcessed = []
        const serversFailed = []
        const performanceData = new Map() // Map<appSite, Map<language, perfData>>

        // Group servers by language
        const serversByLang = new Map()
        for (const server of servers) {
            if (!serversByLang.has(server.language)) {
                serversByLang.set(server.language, [])
            }
            serversByLang.get(server.language).push(server)
        }

        // Fetch data from each language (trying all methods)
        for (const [lang, langServers] of serversByLang) {
            let langSuccess = false
            const langErrors = []

            for (const server of langServers) {
                // Log fetch attempt
                const logMsg = server.method === 'POST'
                    ? `[${new Date().toISOString()}] Fetching ${lang} via POST ${server.url} (fileName: ${server.fileName})\n`
                    : `[${new Date().toISOString()}] Fetching ${lang} via GET ${server.url}${server.fileName}\n`
                await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => { })

                try {
                    let response
                    if (server.method === 'POST') {
                        const reportRequest = {
                            fileName: server.fileName,
                            useLangPrefix: false
                        }
                        response = await fetch(server.url, {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify(reportRequest),
                            signal: AbortSignal.timeout(30000)
                        })
                    } else {
                        const fullUrl = server.url + server.fileName
                        response = await fetch(fullUrl, {
                            signal: AbortSignal.timeout(30000)
                        })
                    }

                    if (response.ok) {
                        const jsonText = await response.text()
                        const data = JSON.parse(jsonText)

                        // Process each performance entry
                        if (Array.isArray(data)) {
                            const itemCount = data.length
                            for (const entry of data) {
                                const appSite = getStringField(entry, 'appSite', 'AppSite', 'app_site')
                                const appView = getStringField(entry, 'appView', 'AppView', 'app_view')

                                // Handle both milliseconds and nanoseconds
                                let normalTimeMs = getFloatField(entry, 'normalTimeMs', 'NormalTimeMs', 'normal_time_ms')
                                const normalTimeNanos = getFloatField(entry, 'NormalTimeNanos', 'normal_time_nanos')
                                if (normalTimeNanos !== null) {
                                    normalTimeMs = normalTimeNanos / 1_000_000
                                }

                                let preProcessTimeMs = getFloatField(entry, 'preProcessTimeMs', 'PreProcessTimeMs', 'preprocess_time_ms')
                                const preProcessTimeNanos = getFloatField(entry, 'PreProcessTimeNanos', 'preprocess_time_nanos')
                                if (preProcessTimeNanos !== null) {
                                    preProcessTimeMs = preProcessTimeNanos / 1_000_000
                                }

                                const outputSize = getIntField(entry, 'outputSize', 'OutputSize', 'output_size')

                                // Create composite key: AppSite + AppView to handle scenarios with different views
                                // Normalize empty string to ensure consistency
                                const normalizedAppView = appView && appView.trim() !== '' ? appView : ''
                                const compositeKey = normalizedAppView ? `${appSite} → ${normalizedAppView}` : appSite

                                // Use case-insensitive comparison for key matching
                                let existingKey = null
                                for (const key of performanceData.keys()) {
                                    if (key.toLowerCase() === compositeKey.toLowerCase()) {
                                        existingKey = key
                                        break
                                    }
                                }
                                const finalKey = existingKey || compositeKey

                                // Debug logging
                                const debugMsg = `[${new Date().toISOString()}] ${lang}: AppSite='${appSite}', AppView='${appView}', CompositeKey='${finalKey}'\n`
                                await fsSync.promises.appendFile(consolidateLogFile, debugMsg).catch(() => { })

                                if (!performanceData.has(finalKey)) {
                                    performanceData.set(finalKey, new Map())
                                }

                                const langMap = performanceData.get(finalKey)
                                langMap.set(lang, {
                                    normalTimeMs,
                                    preProcessTimeMs,
                                    outputSize,
                                    appSite,
                                    appView
                                })
                            }
                            // Log success
                            const logMsg = `[${new Date().toISOString()}] ✅ ${lang}: Successfully processed ${itemCount} items\n`
                            await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => { })
                        }

                        langSuccess = true
                        break // Success, no need to try other methods
                    } else {
                        const domain = server.url.replace(/^https?:\/\//, '').split('/')[0]
                        const errorMsg = `${server.method} ${domain} (HTTP ${response.status})`
                        langErrors.push(errorMsg)
                        // Log warning
                        const logMsg = `[${new Date().toISOString()}] ⚠️ ${lang}: ${errorMsg}\n`
                        await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => { })
                    }
                } catch (err) {
                    const domain = server.url.replace(/^https?:\/\//, '').split('/')[0]
                    const errorMsg = `${server.method} ${domain} (ERROR: ${err.message})`
                    langErrors.push(errorMsg)
                    // Log warning
                    const logMsg = `[${new Date().toISOString()}] ⚠️ ${lang}: ${errorMsg}\n`
                    await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => { })
                }
            }

            // After trying all methods for this language, determine overall result
            if (langSuccess) {
                serversProcessed.push(lang)
            } else {
                const failureMsg = `${lang}: All methods failed - ${langErrors.join('; ')}`
                serversFailed.push(failureMsg)
                const logMsg = `[${new Date().toISOString()}] ❌ ${lang}: All methods failed\n`
                await fsSync.promises.appendFile(consolidateLogFile, logMsg).catch(() => { })
            }
        }

        // Generate HTML report
        const html = []
        html.push('<!DOCTYPE html>')
        html.push('<html>')
        html.push('<head>')
        html.push('    <meta charset="UTF-8">')
        html.push('    <meta name="viewport" content="width=device-width, initial-scale=1.0">')
        html.push('    <title>Consolidated Performance Summary</title>')
        html.push('    <style>')
        html.push('        body { font-family: Arial, sans-serif; margin: 20px; }')
        html.push('        h1 { color: #333; }')
        html.push('        h2 { color: #333; margin-top: 40px; }')
        html.push('        .meta { color: #666; font-style: italic; margin-bottom: 10px; }')
        html.push('        .table-container { overflow-x: auto; }')
        html.push('        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 700px; }')
        html.push('        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }')
        html.push('        th { background-color: #4CAF50; color: white; }')
        html.push('        tr:nth-child(even) { background-color: #f2f2f2; }')
        html.push('        td:nth-child(2), td:nth-child(3), td:nth-child(4), td:nth-child(5), td:nth-child(6), td:nth-child(7) { text-align: right; }')
        html.push('        .best-perf { background-color: #90EE90; font-weight: bold; }')
        html.push('        .worst-perf { background-color: #FFB6C6; font-weight: bold; }')
        html.push('        .avg-perf { background-color: #FFD700; font-weight: bold; }')
        html.push('        .legend { display: flex; gap: 20px; margin: 20px 0; flex-wrap: wrap; }')
        html.push('        .legend-item { display: flex; align-items: center; gap: 8px; }')
        html.push('        .legend-box { width: 24px; height: 24px; border: 1px solid #999; }')
        html.push('        .view-toggle { margin: 20px 0; }')
        html.push('        .view-btn { padding: 10px 20px; margin-right: 10px; cursor: pointer; border: 2px solid #4CAF50; background: white; color: #4CAF50; font-size: 14px; border-radius: 5px; }')
        html.push('        .view-btn.active { background: #4CAF50; color: white; }')
        html.push('        .view-content { display: none; }')
        html.push('        .view-content.active { display: block; }')
        html.push('        .chart-container { margin: 20px 0; }')
        html.push('        .chart-row { margin-bottom: 25px; }')
        html.push('        .chart-label { font-weight: bold; margin-bottom: 8px; font-size: 14px; color: #333; }')
        html.push('        .chart-bars-container { display: flex; flex-direction: column; gap: 8px; }')
        html.push('        .chart-bar-wrapper { display: flex; align-items: center; gap: 10px; }')
        html.push('        .chart-bar-label { min-width: 80px; font-weight: 600; color: #555; font-size: 13px; }')
        html.push('        .chart-bar { height: 30px; border-radius: 5px; display: flex; align-items: center; justify-content: flex-end; padding-right: 10px; color: white; font-weight: bold; font-size: 12px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); transition: transform 0.2s; min-width: 40px; }')
        html.push('        .chart-bar:hover { transform: translateX(5px); box-shadow: 0 4px 8px rgba(0,0,0,0.15); }')
        html.push('        .chart-bar-value { margin-left: 10px; font-weight: 600; color: #333; font-size: 13px; min-width: 60px; }')
        html.push('        .grouped-chart-section { margin-bottom: 40px; padding: 20px; background: #f9f9f9; border-radius: 8px; }')
        html.push('        .grouped-chart-title { font-size: 1.3em; font-weight: bold; color: #667eea; margin-bottom: 15px; border-bottom: 2px solid #667eea; padding-bottom: 8px; }')
        html.push('        .grouped-bar-group { display: flex; align-items: center; margin-bottom: 20px; }')
        html.push('        .grouped-bar-label { min-width: 100px; font-weight: 600; color: #333; font-size: 13px; }')
        html.push('        .grouped-bars { flex: 1; display: flex; flex-direction: column; gap: 4px; }')
        html.push('        .grouped-bar-item { display: flex; align-items: center; gap: 8px; }')
        html.push('        .grouped-bar { height: 24px; border-radius: 4px; display: flex; align-items: center; justify-content: flex-end; padding-right: 8px; color: white; font-weight: bold; font-size: 11px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); min-width: 30px; }')
        html.push('        .grouped-lang-label { min-width: 60px; font-size: 12px; color: #666; }')
        html.push('    </style>')
        html.push('</head>')
        html.push('<body>')
        html.push('    <h1>Consolidated Performance Summary</h1>')
        html.push(`    <div class="meta">Generated: ${new Date().toISOString().replace('T', ' ').substring(0, 19)} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>`)
        html.push('    <div class="legend">')
        html.push('        <div class="legend-item"><div class="legend-box" style="background-color: #4CAF50; opacity: 0.8;"></div><span>Normal Engine (N)</span></div>')
        html.push('        <div class="legend-item"><div class="legend-box" style="background-color: #2196F3; opacity: 0.8;"></div><span>PreProcess Engine (P)</span></div>')
        html.push('        <div class="legend-item"><div class="legend-box" style="background-color: #90EE90;"></div><span>Best (Lowest Time - Table View)</span></div>')
        html.push('        <div class="legend-item"><div class="legend-box" style="background-color: #FFD700;"></div><span>Nearest to Average (Table View)</span></div>')
        html.push('        <div class="legend-item"><div class="legend-box" style="background-color: #FFB6C6;"></div><span>Worst (Highest Time - Table View)</span></div>')
        html.push('    </div>')
        html.push('    <div class="view-toggle">')
        html.push('        <button class="view-btn active" data-view="grouped">Grouped View</button>')
        html.push('        <button class="view-btn" data-view="chart">Bar Chart View</button>')
        html.push('        <button class="view-btn" data-view="table">Table View</button>')
        html.push('    </div>')

        // Get list of languages dynamically from configuration
        const languages = Array.from(serversByLang.keys()).sort()

        // Combined Bar Chart View
        html.push('    <div id="combined-chart" class="view-content">')
        html.push('        <div class="chart-container">')

        // Generate combined chart data showing both engines (filter by rule groups)
        const filteredApps = Array.from(performanceData.keys())
            .filter(app => RULE_GROUPS.some(rule => app.startsWith(rule)))
            .sort()

        for (const app of filteredApps) {
            html.push('            <div class="chart-row">')
            html.push(`                <div class="chart-label">${app}</div>`)
            html.push('                <div class="chart-bars-container">')

            // Calculate max time across BOTH engines for consistent scaling
            const allTimes = []
            for (const lang of languages) {
                const langData = performanceData.get(app)
                if (langData && langData.has(lang)) {
                    const data = langData.get(lang)
                    if (data.normalTimeMs !== null && data.normalTimeMs !== undefined && data.normalTimeMs > 0) {
                        allTimes.push(data.normalTimeMs)
                    }
                    if (data.preProcessTimeMs !== null && data.preProcessTimeMs !== undefined && data.preProcessTimeMs > 0) {
                        allTimes.push(data.preProcessTimeMs)
                    }
                }
            }
            const maxTimeForScale = allTimes.length > 0 ? Math.max(...allTimes) : 1.0

            // Calculate highlighting for Normal Engine
            const normalValidTimes = []
            for (const lang of languages) {
                const langData = performanceData.get(app)
                if (langData && langData.has(lang)) {
                    const data = langData.get(lang)
                    if (data.normalTimeMs !== null && data.normalTimeMs !== undefined && data.normalTimeMs > 0) {
                        normalValidTimes.push(data.normalTimeMs)
                    }
                }
            }
            const normalMinTime = normalValidTimes.length > 0 ? Math.min(...normalValidTimes) : null
            const normalMaxTime = normalValidTimes.length > 0 ? Math.max(...normalValidTimes) : null
            const normalAvgTime = normalValidTimes.length > 0 ? normalValidTimes.reduce((a, b) => a + b, 0) / normalValidTimes.length : null

            // Calculate highlighting for PreProcess Engine
            const preprocessValidTimes = []
            for (const lang of languages) {
                const langData = performanceData.get(app)
                if (langData && langData.has(lang)) {
                    const data = langData.get(lang)
                    if (data.preProcessTimeMs !== null && data.preProcessTimeMs !== undefined && data.preProcessTimeMs > 0) {
                        preprocessValidTimes.push(data.preProcessTimeMs)
                    }
                }
            }
            const preprocessMinTime = preprocessValidTimes.length > 0 ? Math.min(...preprocessValidTimes) : null
            const preprocessMaxTime = preprocessValidTimes.length > 0 ? Math.max(...preprocessValidTimes) : null
            const preprocessAvgTime = preprocessValidTimes.length > 0 ? preprocessValidTimes.reduce((a, b) => a + b, 0) / preprocessValidTimes.length : null

            for (const lang of languages) {
                const langData = performanceData.get(app)
                if (langData && langData.has(lang)) {
                    const data = langData.get(lang)
                    const normalTime = data.normalTimeMs
                    const preprocessTime = data.preProcessTimeMs

                    if ((normalTime !== null && normalTime !== undefined && normalTime > 0) ||
                        (preprocessTime !== null && preprocessTime !== undefined && preprocessTime > 0)) {
                        html.push('                    <div class="chart-bar-wrapper">')
                        html.push(`                        <div class="chart-bar-label">${lang}</div>`)

                        // Container for overlapping bars (both start from 0) - with overflow visible for labels
                        html.push('                        <div style="position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;">')

                        // Normal Engine Bar (bottom layer) with label at the end
                        if (normalTime !== null && normalTime !== undefined && normalTime > 0) {
                            const widthPercent = (normalTime / maxTimeForScale) * 100

                            // Determine highlight color
                            let normalBgColor = '#4CAF50' // default green
                            if (normalMinTime !== null && Math.abs(normalTime - normalMinTime) < 0.01) {
                                normalBgColor = '#90EE90' // best (light green)
                            } else if (normalMaxTime !== null && Math.abs(normalTime - normalMaxTime) < 0.01) {
                                normalBgColor = '#FFB6C6' // worst (light red)
                            } else if (normalAvgTime !== null && normalValidTimes.length > 2) {
                                const nearestToAvg = normalValidTimes.reduce((prev, curr) =>
                                    Math.abs(curr - normalAvgTime) < Math.abs(prev - normalAvgTime) ? curr : prev
                                )
                                if (Math.abs(normalTime - nearestToAvg) < 0.01) {
                                    normalBgColor = '#FFD700' // avg (gold)
                                }
                            }

                            // Position label: inside bar if very wide (>85%), otherwise outside at end
                            const normalLabelStyle = widthPercent > 85
                                ? `position: absolute; right: calc(100% - ${widthPercent}% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;`
                                : `position: absolute; left: calc(${widthPercent}% + 5px); top: 0; font-size: 11px; color: ${normalBgColor}; font-weight: 600; white-space: nowrap;`
                            html.push(`                            <div style="position: absolute; left: 0; top: 0; width: ${widthPercent}%; height: 15px; background-color: ${normalBgColor}; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="${lang} Normal: ${normalTime.toFixed(2)}ms"></div>`)
                            html.push(`                            <span style="${normalLabelStyle}">N: ${normalTime.toFixed(2)}ms</span>`)
                        }

                        // PreProcess Engine Bar (top layer, slightly offset) with label at the end
                        if (preprocessTime !== null && preprocessTime !== undefined && preprocessTime > 0) {
                            const widthPercent = (preprocessTime / maxTimeForScale) * 100

                            // Determine highlight color
                            let preprocessBgColor = '#2196F3' // default blue
                            if (preprocessMinTime !== null && Math.abs(preprocessTime - preprocessMinTime) < 0.01) {
                                preprocessBgColor = '#90EE90' // best (light green)
                            } else if (preprocessMaxTime !== null && Math.abs(preprocessTime - preprocessMaxTime) < 0.01) {
                                preprocessBgColor = '#FFB6C6' // worst (light red)
                            } else if (preprocessAvgTime !== null && preprocessValidTimes.length > 2) {
                                const nearestToAvg = preprocessValidTimes.reduce((prev, curr) =>
                                    Math.abs(curr - preprocessAvgTime) < Math.abs(prev - preprocessAvgTime) ? curr : prev
                                )
                                if (Math.abs(preprocessTime - nearestToAvg) < 0.01) {
                                    preprocessBgColor = '#FFD700' // avg (gold)
                                }
                            }

                            // Position label: inside bar if very wide (>85%), otherwise outside at end
                            const preprocessLabelStyle = widthPercent > 85
                                ? `position: absolute; right: calc(100% - ${widthPercent}% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;`
                                : `position: absolute; left: calc(${widthPercent}% + 5px); top: 15px; font-size: 11px; color: ${preprocessBgColor}; font-weight: 600; white-space: nowrap;`
                            html.push(`                            <div style="position: absolute; left: 0; top: 15px; width: ${widthPercent}%; height: 15px; background-color: ${preprocessBgColor}; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="${lang} PreProcess: ${preprocessTime.toFixed(2)}ms"></div>`)
                            html.push(`                            <span style="${preprocessLabelStyle}">P: ${preprocessTime.toFixed(2)}ms</span>`)
                        }

                        html.push('                        </div>')
                        html.push('                    </div>')
                    }
                }
            }

            html.push('                </div>')
            html.push('            </div>')
        }

        html.push('        </div>')
        html.push('    </div>')

        // Grouped Bar Chart View
        html.push('    <div id="combined-grouped" class="view-content active">')
        html.push('        <div class="chart-container">')

        for (const rulePattern of RULE_GROUPS) {
            // Find all apps matching this rule pattern (excluding Test AppSite for now)
            const matchingApps = Array.from(performanceData.keys())
                .filter(app => app.startsWith(rulePattern) && !app.includes('Test'))
                .sort()

            if (matchingApps.length === 0) continue

            html.push('            <div class="grouped-chart-section">')
            html.push(`                <div class="grouped-chart-title">${rulePattern}</div>`)
            html.push('                <div class="chart-bars-container">')

            // Calculate max time across ALL languages in this rule group for consistent scaling
            const allMaxValues = []
            for (const lang of languages) {
                const normalTimes = []
                for (const app of matchingApps) {
                    const langData = performanceData.get(app)
                    if (langData && langData.has(lang)) {
                        const data = langData.get(lang)
                        if (data.normalTimeMs !== null && data.normalTimeMs !== undefined && data.normalTimeMs > 0) {
                            normalTimes.push(data.normalTimeMs)
                        }
                    }
                }
                const preprocessTimes = []
                for (const app of matchingApps) {
                    const langData = performanceData.get(app)
                    if (langData && langData.has(lang)) {
                        const data = langData.get(lang)
                        if (data.preProcessTimeMs !== null && data.preProcessTimeMs !== undefined && data.preProcessTimeMs > 0) {
                            preprocessTimes.push(data.preProcessTimeMs)
                        }
                    }
                }

                if (normalTimes.length > 0) allMaxValues.push(Math.max(...normalTimes))
                if (preprocessTimes.length > 0) allMaxValues.push(Math.max(...preprocessTimes))
            }
            const maxTimeForScale = allMaxValues.length > 0 ? Math.max(...allMaxValues) : 1.0

            // For each language, calculate min/avg/max across all apps in this rule group
            for (const lang of languages) {
                // Collect Normal Engine times for this language across all apps in the group
                const normalTimes = []
                for (const app of matchingApps) {
                    const langData = performanceData.get(app)
                    if (langData && langData.has(lang)) {
                        const data = langData.get(lang)
                        if (data.normalTimeMs !== null && data.normalTimeMs !== undefined && data.normalTimeMs > 0) {
                            normalTimes.push(data.normalTimeMs)
                        }
                    }
                }

                // Collect PreProcess Engine times for this language across all apps in the group
                const preprocessTimes = []
                for (const app of matchingApps) {
                    const langData = performanceData.get(app)
                    if (langData && langData.has(lang)) {
                        const data = langData.get(lang)
                        if (data.preProcessTimeMs !== null && data.preProcessTimeMs !== undefined && data.preProcessTimeMs > 0) {
                            preprocessTimes.push(data.preProcessTimeMs)
                        }
                    }
                }

                if (normalTimes.length === 0 && preprocessTimes.length === 0) continue

                // Calculate aggregates
                const normalMin = normalTimes.length > 0 ? Math.min(...normalTimes) : null
                const normalAvg = normalTimes.length > 0 ? normalTimes.reduce((a, b) => a + b, 0) / normalTimes.length : null
                const normalMax = normalTimes.length > 0 ? Math.max(...normalTimes) : null

                const preprocessMin = preprocessTimes.length > 0 ? Math.min(...preprocessTimes) : null
                const preprocessAvg = preprocessTimes.length > 0 ? preprocessTimes.reduce((a, b) => a + b, 0) / preprocessTimes.length : null
                const preprocessMax = preprocessTimes.length > 0 ? Math.max(...preprocessTimes) : null

                html.push('                    <div class="chart-bar-wrapper">')
                html.push(`                        <div class="chart-bar-label">${lang}</div>`)

                // Container for overlapping bars
                html.push('                        <div style="position: relative; flex: 1; height: 30px; min-width: 0; overflow: visible;">')

                // Normal Engine Bar (showing min, avg, max as segments)
                if (normalMin !== null && normalAvg !== null && normalMax !== null) {
                    const minWidth = (normalMin / maxTimeForScale) * 100
                    const avgWidth = (normalAvg / maxTimeForScale) * 100
                    const maxWidth = (normalMax / maxTimeForScale) * 100

                    // Draw max bar (light green background)
                    html.push(`                            <div style="position: absolute; left: 0; top: 0; width: ${maxWidth}%; height: 15px; background-color: #90EE90; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="${lang} Normal Max: ${normalMax.toFixed(2)}ms"></div>`)

                    // Draw avg bar (gold - middle layer)
                    html.push(`                            <div style="position: absolute; left: 0; top: 0; width: ${avgWidth}%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="${lang} Normal Avg: ${normalAvg.toFixed(2)}ms"></div>`)

                    // Draw min bar (dark green - top layer)
                    html.push(`                            <div style="position: absolute; left: 0; top: 0; width: ${minWidth}%; height: 15px; background-color: #4CAF50; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="${lang} Normal Min: ${normalMin.toFixed(2)}ms"></div>`)

                    // Label at end of max bar
                    const labelStyle = maxWidth > 85
                        ? `position: absolute; right: calc(100% - ${maxWidth}% + 5px); top: 0; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;`
                        : `position: absolute; left: calc(${maxWidth}% + 5px); top: 0; font-size: 11px; color: #4CAF50; font-weight: 600; white-space: nowrap;`
                    html.push(`                            <span style="${labelStyle}">N: ${normalMin.toFixed(2)}/${normalAvg.toFixed(2)}/${normalMax.toFixed(2)}</span>`)
                }

                // PreProcess Engine Bar (showing min, avg, max as segments)
                if (preprocessMin !== null && preprocessAvg !== null && preprocessMax !== null) {
                    const minWidth = (preprocessMin / maxTimeForScale) * 100
                    const avgWidth = (preprocessAvg / maxTimeForScale) * 100
                    const maxWidth = (preprocessMax / maxTimeForScale) * 100

                    // Draw max bar (light pink background)
                    html.push(`                            <div style="position: absolute; left: 0; top: 15px; width: ${maxWidth}%; height: 15px; background-color: #FFB6C6; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="${lang} PreProcess Max: ${preprocessMax.toFixed(2)}ms"></div>`)

                    // Draw avg bar (gold - middle layer)
                    html.push(`                            <div style="position: absolute; left: 0; top: 15px; width: ${avgWidth}%; height: 15px; background-color: #FFD700; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="${lang} PreProcess Avg: ${preprocessAvg.toFixed(2)}ms"></div>`)

                    // Draw min bar (dark blue - top layer)
                    html.push(`                            <div style="position: absolute; left: 0; top: 15px; width: ${minWidth}%; height: 15px; background-color: #2196F3; border-radius: 3px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);" title="${lang} PreProcess Min: ${preprocessMin.toFixed(2)}ms"></div>`)

                    // Label at end of max bar
                    const labelStyle = maxWidth > 85
                        ? `position: absolute; right: calc(100% - ${maxWidth}% + 5px); top: 15px; font-size: 11px; color: #333; font-weight: 600; white-space: nowrap;`
                        : `position: absolute; left: calc(${maxWidth}% + 5px); top: 15px; font-size: 11px; color: #2196F3; font-weight: 600; white-space: nowrap;`
                    html.push(`                            <span style="${labelStyle}">P: ${preprocessMin.toFixed(2)}/${preprocessAvg.toFixed(2)}/${preprocessMax.toFixed(2)}</span>`)
                }

                html.push('                        </div>')
                html.push('                    </div>')
            }

            html.push('                </div>')
            html.push('            </div>')
        }

        html.push('        </div>')
        html.push('    </div>')

        // Normal Engine Table View
        html.push('    <div id="normal-table" class="view-content">')
        html.push('    <h2>Normal Engine</h2>')
        html.push('    <div class="table-container">')
        html.push('    <table>')
        html.push('        <tr>')
        html.push('            <th>AppSite/AppView</th>')
        for (const lang of languages) {
            html.push(`            <th>${lang}</th>`)
        }
        html.push('            <th>OutputSize</th>')
        html.push('        </tr>')
        for (const app of filteredApps) {
            const langData = performanceData.get(app)

            // Find min, max, and avg time for highlighting (excluding zero values)
            const validTimes = []
            for (const lang of languages) {
                if (langData && langData.has(lang)) {
                    const data = langData.get(lang)
                    if (data.normalTimeMs !== null && data.normalTimeMs !== undefined && data.normalTimeMs > 0) {
                        validTimes.push(data.normalTimeMs)
                    }
                }
            }
            const minTime = validTimes.length > 0 ? Math.min(...validTimes) : null
            const maxTime = validTimes.length > 0 ? Math.max(...validTimes) : null
            const avgTime = validTimes.length > 0 ? validTimes.reduce((a, b) => a + b, 0) / validTimes.length : null

            html.push('        <tr>')
            html.push(`            <td>${app}</td>`)
            for (const lang of languages) {
                const data = langData && langData.has(lang) ? langData.get(lang) : null
                const timeValue = data && data.normalTimeMs !== null && data.normalTimeMs !== undefined
                    ? data.normalTimeMs.toFixed(2)
                    : '-'

                let cssClass = ''
                if (data && data.normalTimeMs !== null && data.normalTimeMs !== undefined && data.normalTimeMs > 0) {
                    const currentTime = data.normalTimeMs
                    if (minTime !== null && Math.abs(currentTime - minTime) < 0.001) {
                        cssClass = ' class="best-perf"'
                    } else if (maxTime !== null && Math.abs(currentTime - maxTime) < 0.001) {
                        cssClass = ' class="worst-perf"'
                    } else if (avgTime !== null && validTimes.length > 2) {
                        // Find the value nearest to average
                        const nearestToAvg = validTimes.reduce((prev, curr) =>
                            Math.abs(curr - avgTime) < Math.abs(prev - avgTime) ? curr : prev
                        )
                        if (Math.abs(currentTime - nearestToAvg) < 0.001) {
                            cssClass = ' class="avg-perf"'
                        }
                    }
                }

                html.push(`            <td${cssClass}>${timeValue}</td>`)
            }
            const outputSize = getFirstOutputSize(langData)
            html.push(`            <td>${formatInt(outputSize)}</td>`)
            html.push('        </tr>')
        }
        html.push('    </table>')
        html.push('    </div>')
        html.push('    </div>')

        // PreProcess Engine Table View
        html.push('    <div id="preprocess-table" class="view-content">')
        html.push('    <h2>PreProcess Engine</h2>')
        html.push('    <div class="table-container">')
        html.push('    <table>')
        html.push('        <tr>')
        html.push('            <th>AppSite/AppView</th>')
        for (const lang of languages) {
            html.push(`            <th>${lang}</th>`)
        }
        html.push('            <th>OutputSize</th>')
        html.push('        </tr>')
        for (const app of filteredApps) {
            const langData = performanceData.get(app)

            // Find min, max, and avg time for highlighting (excluding zero values)
            const validTimes = []
            for (const lang of languages) {
                if (langData && langData.has(lang)) {
                    const data = langData.get(lang)
                    if (data.preProcessTimeMs !== null && data.preProcessTimeMs !== undefined && data.preProcessTimeMs > 0) {
                        validTimes.push(data.preProcessTimeMs)
                    }
                }
            }
            const minTime = validTimes.length > 0 ? Math.min(...validTimes) : null
            const maxTime = validTimes.length > 0 ? Math.max(...validTimes) : null
            const avgTime = validTimes.length > 0 ? validTimes.reduce((a, b) => a + b, 0) / validTimes.length : null

            html.push('        <tr>')
            html.push(`            <td>${app}</td>`)

            for (const lang of languages) {
                const data = langData && langData.has(lang) ? langData.get(lang) : null
                const timeValue = data && data.preProcessTimeMs !== null && data.preProcessTimeMs !== undefined
                    ? data.preProcessTimeMs.toFixed(2)
                    : '-'

                let cssClass = ''
                if (data && data.preProcessTimeMs !== null && data.preProcessTimeMs !== undefined && data.preProcessTimeMs > 0) {
                    const currentTime = data.preProcessTimeMs
                    if (minTime !== null && Math.abs(currentTime - minTime) < 0.001) {
                        cssClass = ' class="best-perf"'
                    } else if (maxTime !== null && Math.abs(currentTime - maxTime) < 0.001) {
                        cssClass = ' class="worst-perf"'
                    } else if (avgTime !== null && validTimes.length > 2) {
                        const nearestToAvg = validTimes.reduce((prev, curr) =>
                            Math.abs(curr - avgTime) < Math.abs(prev - avgTime) ? curr : prev
                        )
                        if (Math.abs(currentTime - nearestToAvg) < 0.001) {
                            cssClass = ' class="avg-perf"'
                        }
                    }
                }

                html.push(`            <td${cssClass}>${timeValue}</td>`)
            }
            const outputSize = getFirstOutputSize(langData)
            html.push(`            <td>${formatInt(outputSize)}</td>`)
            html.push('        </tr>')
        }
        html.push('    </table>')
        html.push('    </div>')
        html.push('    </div>')

        // Add JavaScript for view switching
        html.push('    <script>')
        html.push('        document.addEventListener("DOMContentLoaded", function() {')
        html.push('            const viewButtons = document.querySelectorAll(".view-btn");')
        html.push('            const viewContents = document.querySelectorAll(".view-content");')
        html.push('            ')
        html.push('            function switchView(viewName) {')
        html.push('                viewButtons.forEach(btn => {')
        html.push('                    if (btn.getAttribute("data-view") === viewName) {')
        html.push('                        btn.classList.add("active");')
        html.push('                    } else {')
        html.push('                        btn.classList.remove("active");')
        html.push('                    }')
        html.push('                });')
        html.push('                ')
        html.push('                viewContents.forEach(content => {')
        html.push('                    if (viewName === "grouped" && content.id === "combined-grouped") {')
        html.push('                        content.classList.add("active");')
        html.push('                    } else if (viewName === "chart" && content.id === "combined-chart") {')
        html.push('                        content.classList.add("active");')
        html.push('                    } else if (viewName === "table" && (content.id === "normal-table" || content.id === "preprocess-table")) {')
        html.push('                        content.classList.add("active");')
        html.push('                    } else {')
        html.push('                        content.classList.remove("active");')
        html.push('                    }')
        html.push('                });')
        html.push('            }')
        html.push('            ')
        html.push('            viewButtons.forEach(btn => {')
        html.push('                btn.addEventListener("click", function() {')
        html.push('                    switchView(this.getAttribute("data-view"));')
        html.push('                });')
        html.push('            });')
        html.push('        });')
        html.push('    </script>')
        html.push('</body>')
        html.push('</html>')

        // Write HTML file to Reports directory
        const reportsDir = path.join(projectDirectory, 'template_analysis', 'Reports')
        await fsSync.promises.mkdir(reportsDir, { recursive: true }).catch(() => { })
        const htmlPath = path.join(reportsDir, 'all_perf_tests.html')
        fsSync.writeFileSync(htmlPath, html.join('\n'), 'utf8')

        const elapsed = (Date.now() - start) / 1000

        // Log completion
        const totalLanguages = serversByLang.size
        const completionLogMsg = `[${new Date().toISOString()}] Consolidation complete in ${elapsed.toFixed(2)}s - ${performanceData.size} AppSites from ${serversProcessed.length}/${totalLanguages} languages\n`
        await fsSync.promises.appendFile(consolidateLogFile, completionLogMsg).catch(() => { })

        let message = `Consolidated ${performanceData.size} AppSites from ${serversProcessed.length}/${totalLanguages} languages in ${elapsed.toFixed(2)} secs`
        if (serversProcessed.length > 0) {
            message += ` | ✅ Success: ${serversProcessed.join(', ')}`
        }
        if (serversFailed.length > 0) {
            message += `\n❌ Failed: ${serversFailed.join('; ')}`
        }

        res.json({
            success: serversProcessed.length > 0,
            message: message,
            elapsed: elapsed,
            testCount: serversProcessed.length
        })
    } catch (error) {
        console.error('Error in consolidate performance:', error)
        res.status(500).json({ error: 'Internal server error' })
    }
}

/**
 * POST /api/report - Get report file
 */
export async function getReportEndpoint(req, res) {
    const projectDirectory = getProjectDirectory();
    try {
        const { fileName, useLangPrefix, langPrefix } = req.body

        if (!fileName) {
            return res.status(400).json({ error: 'Missing required field: fileName' })
        }

        // Validate fileName for path traversal
        if (!isValidPathComponent(fileName)) {
            return res.status(400).json({ error: 'Invalid characters in fileName' })
        }

        // Validate langPrefix for path traversal if provided
        if (langPrefix && !isValidPathComponent(langPrefix)) {
            return res.status(400).json({ error: 'Invalid characters in langPrefix' })
        }

        // Construct file path
        const prefix = useLangPrefix && langPrefix ? langPrefix + '_' : ''
        const fullFileName = prefix + fileName
        const reportsDir = path.join(projectDirectory, 'template_analysis', 'Reports')
        const filePath = path.join(reportsDir, fullFileName)

        // Check if file exists
        if (!fsSync.existsSync(filePath)) {
            return res.status(404).json({ error: `Report file not found: ${fullFileName}` })
        }

        // Read and return the file content
        const content = fsSync.readFileSync(filePath, 'utf8')

        // Determine content type based on file extension
        let contentType = 'text/plain'
        const extension = path.extname(fullFileName).toLowerCase()
        if (extension === '.html') {
            contentType = 'text/html'
        } else if (extension === '.json') {
            contentType = 'application/json'
        } else if (extension === '.md') {
            contentType = 'text/markdown'
        }

        res.setHeader('Content-Type', contentType)
        res.send(content)
    } catch (error) {
        console.error('Error in getReport endpoint:', error)
        res.status(500).json({ error: 'Internal server error' })
    }
}

/**
 * POST /api/save-log - Save a log file (browser-callable)
 * Expects JSON: { context, content }
 */
export async function saveLogEndpoint(req, res) {
    const projectDirectory = getProjectDirectory();
    try {
        let { context, content } = req.body || {}

        // Trim whitespace from content
        content = (content || '').trim()

        if (!context) {
            return res.status(400).json({ success: false, message: 'missing context' })
        }

        // If content is empty after trimming, return success without saving
        if (!content) {
            return res.json({ success: true, message: 'No content to save', error: null })
        }

        if (!isValidPathComponent(context)) {
            return res.status(400).json({ success: false, message: 'invalid context path component' })
        }

        const validation = isValidLogContent(content)
        if (!validation.valid) {
            return res.status(400).json({ success: false, message: validation.errorMessage || 'invalid log content' })
        }

        const logsDir = path.join(projectDirectory, 'template_analysis', 'logs')
        await fsSync.promises.mkdir(logsDir, { recursive: true }).catch(() => {})

        const logFileName = `javascript_${context}.log`
        const logPath = path.join(logsDir, logFileName)
        await fsSync.promises.writeFile(logPath, content, 'utf8')

        // Return TestResponse-shaped JSON
        return res.json({ success: true, message: 'ok', error: null })
    } catch (error) {
        console.error('Error in /api/save-log:', error)
        res.status(500).json({ success: false, message: error.message })
    }
}

/**
 * POST /api/save-output - Save an output file (browser-callable)
 * Expects JSON: { appSite, appView, engineType, html }
 */
export async function saveOutputEndpoint(req, res) {
    const projectDirectory = getProjectDirectory();
    try {
        console.log('[/api/save-output] Endpoint called')

        const { appSite, appView, engineType, html } = req.body || {}

        console.log(`[/api/save-output] Parsed: appSite=${appSite || ''}, appView=${appView || ''}, engineType=${engineType || ''}, htmlLength=${html ? html.length : 0}`)

        if (!appSite || !engineType || !html) {
            console.log(`[/api/save-output] Missing parameters: appSite=${appSite || ''}, engineType=${engineType || ''}, htmlLength=${html ? html.length : 0}`)
            return res.status(400).json({ success: false, message: 'Missing required parameters' })
        }

        // Validate AppSite against allowlist
        const validAppSites = getValidAppSites()
        if (!isValidAppSite(appSite, validAppSites)) {
            console.log(`[/api/save-output] Invalid AppSite: ${appSite}`)
            return res.status(400).json({ success: false, message: 'Invalid AppSite value' })
        }

        // Validate engine type against allowlist
        if (!isValidEngineType(engineType)) {
            console.log(`[/api/save-output] Invalid engineType: ${engineType}`)
            return res.status(400).json({ success: false, message: 'Invalid engine type' })
        }

        // Validate path components
        if (!isValidPathComponent(appSite)) {
            console.log(`[/api/save-output] Invalid AppSite path component: ${appSite}`)
            return res.status(400).json({ success: false, message: 'Invalid AppSite parameter' })
        }
        if (appView && !isValidPathComponent(appView)) {
            console.log(`[/api/save-output] Invalid AppView path component: ${appView}`)
            return res.status(400).json({ success: false, message: 'Invalid AppView parameter' })
        }
        if (!isValidPathComponent(engineType)) {
            console.log(`[/api/save-output] Invalid engineType path component: ${engineType}`)
            return res.status(400).json({ success: false, message: 'Invalid engineType parameter' })
        }

        // Validate output size against template total size + buffer
        const templateTotalSize = getTemplateTotalSize(appSite, appView || '')
        const outputSize = Buffer.byteLength(html, 'utf8')
        const maxAllowedSize = templateTotalSize + OUTPUT_SIZE_BUFFER
        console.log(`[/api/save-output] Size validation: output=${outputSize}, template=${templateTotalSize}, buffer=${OUTPUT_SIZE_BUFFER}, max=${maxAllowedSize}`)

        if (!isValidOutputSizeWithBuffer(html, templateTotalSize)) {
            const errorMsg = `Save output failed: output size (${outputSize} bytes) exceeds max size allowed (${maxAllowedSize} bytes = template ${templateTotalSize} + buffer ${OUTPUT_SIZE_BUFFER})`
            console.log(`[/api/save-output] ${errorMsg}`)
            return res.status(400).json({ success: false, message: errorMsg })
        }

        const outputDir = path.join(projectDirectory, 'template_analysis', 'output')
        await fsSync.promises.mkdir(outputDir, { recursive: true }).catch(() => {})

        const engineSuffix = engineType.toLowerCase()
        const viewPart = appView ? `_${appView}` : ''
        const outputFileName = `${appSite}${viewPart}_${engineSuffix}.html`
        const outputPath = path.join(outputDir, `javascript_${outputFileName}`)

        await fsSync.promises.writeFile(outputPath, html, 'utf8')
        console.log(`[/api/save-output] Success! Output saved to: ${outputPath}`)

        // Return TestResponse-shaped JSON
        return res.json({ success: true, message: 'Output saved successfully', error: null })
    } catch (error) {
        console.error('[/api/save-output] Error:', error)
        res.status(500).json({ success: false, message: error.message })
    }
}

/**
 * POST /api/test-results - Save test results and generate HTML/JSON reports
 */
export async function saveTestResultsEndpoint(req, res) {
    const projectDirectory = getProjectDirectory();
  try {
    const summaryRows = req.body;
    if (!Array.isArray(summaryRows)) {
      return res.status(400).send('Invalid test results format');
    }

    console.log(`POST /api/test-results called with ${summaryRows.length} rows`);

    // Validation
    const validAppSites = getValidAppSites();
    for (const row of summaryRows) {
      const appSite = row.AppSite || row.appSite || row.app_site;
      const appFile = row.AppFile || row.appFile || row.app_file;
      const appView = row.AppView || row.appView || row.app_view;

      // Validate AppSite is in allowlist (case-insensitive)
      if (appSite && !isValidAppSite(appSite, validAppSites)) {
        console.log(`[/api/test-results] Invalid AppSite: ${appSite}`);
        return res.status(400).send(`Invalid AppSite: ${appSite}`);
      }
      // Validate parameter lengths (256 char limit)
      if (appSite && !isValidPathComponent(appSite)) {
        console.log(`[/api/test-results] Invalid AppSite parameter: ${appSite}`);
        return res.status(400).send('Invalid AppSite parameter');
      }
      if (appFile && !isValidPathComponent(appFile)) {
        console.log(`[/api/test-results] Invalid AppFile parameter: ${appFile}`);
        return res.status(400).send('Invalid AppFile parameter');
      }
      if (appView && !isValidPathComponent(appView)) {
        console.log(`[/api/test-results] Invalid AppView parameter: ${appView}`);
        return res.status(400).send('Invalid AppView parameter');
      }
    }
    const reportsPath = path.join(projectDirectory, 'template_analysis', 'Reports');
    await fsSync.promises.mkdir(reportsPath, { recursive: true }).catch(() => {});

    // Get test type from query parameter
    const testType = req.query.testType || 'standardtest';
    const testTypeFile = testType.toLowerCase().replace(/\s/g, '').replace(/-/g, '');

    // Generate HTML table matching Rust format
    const formattedTestType = testType.replace(/test/gi, ' TEST').toUpperCase();
    const htmlParts = [];
    htmlParts.push('<!DOCTYPE html>\n<html>\n<head>');
    htmlParts.push('    <meta charset="UTF-8">');
    htmlParts.push('    <meta name="viewport" content="width=device-width, initial-scale=1.0">');
    htmlParts.push(`    <title>JavaScript ${formattedTestType}</title>`);
    htmlParts.push('    <style>');
    htmlParts.push('        body { font-family: Arial, sans-serif; margin: 20px; }');
    htmlParts.push('        h1 { color: #333; }');
    htmlParts.push('        .table-container { overflow-x: auto; }');
    htmlParts.push('        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }');
    htmlParts.push('        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }');
    htmlParts.push('        th { background-color: #4CAF50; color: white; }');
    htmlParts.push('        tr:nth-child(even) { background-color: #f2f2f2; }');
    htmlParts.push('        .pass { color: green; font-weight: bold; }');
    htmlParts.push('        .fail { color: red; font-weight: bold; }');
    htmlParts.push('        @media (max-width: 768px) {');
    htmlParts.push('            body { margin: 10px; }');
    htmlParts.push('            th, td { padding: 8px; font-size: 14px; }');
    htmlParts.push('            h1 { font-size: 24px; }');
    htmlParts.push('        }');
    htmlParts.push('    </style>\n</head>\n<body>');
    htmlParts.push(`    <h1>JavaScript ${formattedTestType}</h1>`);
    htmlParts.push(`    <div class="meta" style="color: #666; font-style: italic; margin-bottom: 10px;">Generated: ${new Date().toISOString().replace('T', ' ').substring(0, 19)} UTC</div>`);
    htmlParts.push('    <div class="table-container">\n    <table>\n        <tr>');
    htmlParts.push('            <th>AppSite</th>\n            <th>AppFile</th>\n            <th>AppView</th>');
    htmlParts.push('            <th>OutputMatch</th>\n            <th>ViewUnMatch</th>\n            <th>Error</th>');
    htmlParts.push('        </tr>');

    for (const row of summaryRows) {
      const appSite = getStringField(row, 'AppSite', 'app_site', 'appSite');
      const appFile = getStringField(row, 'AppFile', 'app_file', 'appFile');
      const appView = getStringField(row, 'AppView', 'app_view', 'appView');
      const normalPreProcess = getStringField(row, 'NormalPreProcess', 'normal_pre_process', 'normalPreProcess');
      const crossViewUnMatch = getStringField(row, 'CrossViewUnMatch', 'cross_view_un_match', 'crossViewUnMatch');
      const errorMsg = getStringField(row, 'Error', 'error');

      const outputMatchClass = normalPreProcess === 'PASS' ? 'pass' : (normalPreProcess === 'FAIL' ? 'fail' : '');
      const viewUnmatchClass = crossViewUnMatch === 'PASS' ? 'pass' : (crossViewUnMatch === 'FAIL' ? 'fail' : '');

      htmlParts.push('        <tr>');
      htmlParts.push(`            <td>${appSite}</td>`);
      htmlParts.push(`            <td>${appFile}</td>`);
      htmlParts.push(`            <td>${appView}</td>`);
      htmlParts.push(`            <td class="${outputMatchClass}">${normalPreProcess}</td>`);
      htmlParts.push(`            <td class="${viewUnmatchClass}">${crossViewUnMatch}</td>`);
      htmlParts.push(`            <td>${errorMsg}</td>`);
      htmlParts.push('        </tr>');
    }

    htmlParts.push('    </table>\n    </div>\n</body>\n</html>');

    // Save HTML
    const htmlFile = path.join(reportsPath, `javascript_${testTypeFile}_Summary.html`);
    await fsSync.promises.writeFile(htmlFile, htmlParts.join('\n'), 'utf8');

    // Save JSON
    const jsonFile = path.join(reportsPath, `javascript_${testTypeFile}_Summary.json`);
    await fsSync.promises.writeFile(jsonFile, JSON.stringify(summaryRows, null, 2), 'utf8');
    res.json({ message: 'Test results saved successfully' });
  } catch (error) {
    console.error('Error in /api/test-results:', error);
    res.status(500).send(`Error: ${error.message}`);
  }
}

/**
 * POST /api/performance-results - Save performance results and generate HTML/JSON reports
 */
export async function savePerformanceResultsEndpoint(req, res) {
    const projectDirectory = getProjectDirectory();
  try {
    const summaryRows = req.body;
    if (!Array.isArray(summaryRows)) {
      return res.status(400).send('Invalid performance results format');
    }

    console.log(`POST /api/performance-results called with ${summaryRows.length} rows`);

    // Validate each row
    const validAppSites = getValidAppSites();
    for (const row of summaryRows) {
      const appSite = row.AppSite || row.appSite || row.app_site;
      const appFile = row.AppFile || row.appFile || row.app_file;
      const appView = row.AppView || row.appView || row.app_view;

      // Validate AppSite is in allowlist (case-insensitive)
      if (appSite && !isValidAppSite(appSite, validAppSites)) {
        return res.status(400).send(`Invalid AppSite: ${appSite}`);
      }
      // Validate parameter lengths (256 char limit)
      if (appSite && !isValidPathComponent(appSite)) {
        return res.status(400).send('Invalid AppSite parameter');
      }
      if (appFile && !isValidPathComponent(appFile)) {
        return res.status(400).send('Invalid AppFile parameter');
      }
      if (appView && !isValidPathComponent(appView)) {
        return res.status(400).send('Invalid AppView parameter');
      }
    }

    const reportsPath = path.join(projectDirectory, 'template_analysis', 'Reports');
    await fsSync.promises.mkdir(reportsPath, { recursive: true }).catch(() => {});

    // Generate HTML table
    const htmlParts = [];
    htmlParts.push('<html><head><title>Client-Side Performance Summary Table</title>');
    htmlParts.push('<style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}.meta{color:#666;font-style:italic;margin-bottom:10px;}</style></head><body>');
    htmlParts.push('<h2>Client-Side JavaScript PERFORMANCE SUMMARY TABLE</h2>');
    htmlParts.push(`<div class="meta">Generated: ${new Date().toISOString().replace('T', ' ').substring(0, 19)} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>`);
    htmlParts.push('<table>');
    htmlParts.push('<tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th><th>ScnTime(ms)</th><th>Elapsed(ms)</th></tr>');

    for (const row of summaryRows) {
      const appSite = getStringField(row, 'AppSite', 'app_site', 'appSite');
      const appView = getStringField(row, 'AppView', 'app_view', 'appView');
      const normalTimeMs = getFloatField(row, 'NormalTimeMs', 'normal_time_ms', 'normalTimeMs') || 0;
      const preProcessTimeMs = getFloatField(row, 'PreProcessTimeMs', 'preprocess_time_ms', 'preProcessTimeMs') || 0;
      const resultsMatch = getStringField(row, 'ResultsMatch', 'results_match', 'resultsMatch');
      const perfDifference = getStringField(row, 'PerfDifference', 'perf_difference', 'perfDifference');
      const scenarioTotalTimeMs = getIntField(row, 'ScenarioTotalTimeMs', 'scenario_total_time_ms', 'scenarioTotalTimeMs') || 0;
      const elapsedTimeMs = getIntField(row, 'ElapsedTimeMs', 'elapsed_time_ms', 'elapsedTimeMs') || 0;

      htmlParts.push('<tr>');
      htmlParts.push(`<td>${appSite}</td>`);
      htmlParts.push(`<td>${appView}</td>`);
      htmlParts.push(`<td>${normalTimeMs.toFixed(2)}</td>`);
      htmlParts.push(`<td>${preProcessTimeMs.toFixed(2)}</td>`);
      htmlParts.push(`<td>${resultsMatch}</td>`);
      htmlParts.push(`<td>${perfDifference}</td>`);
      htmlParts.push(`<td>${scenarioTotalTimeMs}</td>`);
      htmlParts.push(`<td>${elapsedTimeMs}</td>`);
      htmlParts.push('</tr>');
    }

    htmlParts.push('</table></body></html>');

    // Save HTML
    const htmlFile = path.join(reportsPath, 'javascript_perfsummary.html');
    await fsSync.promises.writeFile(htmlFile, htmlParts.join('\n'), 'utf8');
    console.log(`Performance summary HTML saved to: ${htmlFile}`);

    // Save JSON
    const jsonFile = path.join(reportsPath, 'javascript_perfsummary.json');
    await fsSync.promises.writeFile(jsonFile, JSON.stringify(summaryRows, null, 2), 'utf8');
    console.log(`Performance summary JSON saved to: ${jsonFile}`);

    res.json({ message: 'Performance results saved successfully' });
  } catch (error) {
    console.error('Error in /api/performance-results:', error);
    res.status(500).send(`Error: ${error.message}`);
  }
}


// Helper functions for field extraction
function getStringField(obj, ...keys) {
    for (const key of keys) {
        if (obj[key] !== undefined && obj[key] !== null) {
            return String(obj[key])
        }
    }
    return ''
}

function getFloatField(obj, ...keys) {
    for (const key of keys) {
        if (obj[key] !== undefined && obj[key] !== null) {
            const val = parseFloat(obj[key])
            if (!isNaN(val)) {
                return val
            }
        }
    }
    return null
}

function getIntField(obj, ...keys) {
    for (const key of keys) {
        if (obj[key] !== undefined && obj[key] !== null) {
            const val = parseInt(obj[key])
            if (!isNaN(val)) {
                return val
            }
        }
    }
    return null
}

function formatFloat(val) {
    return val !== null && val !== undefined ? val.toFixed(2) : '-'
}

function formatInt(val) {
    return val !== null && val !== undefined ? val.toString() : '-'
}

function getFirstOutputSize(langData) {
    for (const data of langData.values()) {
        if (data.outputSize !== null && data.outputSize !== undefined) {
            return data.outputSize
        }
    }
    return null
}

/**
 * Maps all assembler test endpoints to the Express app
 * @param {Express} app - Express application instance
 */
export function mapAssemblerTestEndpoints(app) {
    app.post('/api/test-results', (req, res) => saveTestResultsEndpoint(req, res));
    app.post('/api/performance-results', (req, res) => savePerformanceResultsEndpoint(req, res));
    app.post('/api/save-log', (req, res) => saveLogEndpoint(req, res));
    app.post('/api/save-output', (req, res) => saveOutputEndpoint(req, res));
    app.post('/test/standard', (req, res) => testStandardEndpoint(req, res));
    app.post('/test/advanced', (req, res) => testAdvancedEndpoint(req, res));
    app.post('/test/performance', (req, res) => testPerformanceEndpoint(req, res));
    app.post('/test/consolidate-performance', (req, res) => testConsolidatePerformanceEndpoint(req, res));
    app.post('/api/report', (req, res) => getReportEndpoint(req, res));
}

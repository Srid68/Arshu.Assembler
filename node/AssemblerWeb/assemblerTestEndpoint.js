import path from 'path';
import { fileURLToPath } from 'url';
import fsSync from 'fs';
import { getValidAppSites, isValidEngineType, isValidAppSite, isValidPathComponent, isValidLogContent, isValidOutputSizeWithBuffer, getTemplateTotalSize, OUTPUT_SIZE_BUFFER } from './securityValidator.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Conditional import for Logger based on environment
const assemblerBasePath = fsSync.existsSync('/app/wwwroot') ? './Assembler/src' : '../Assembler/src';
const loggerModule = await import(`${assemblerBasePath}/common/logger.js`);
const { Logger } = loggerModule;

// Import test and performance utilities
const testingUtilsModule = await import(`${assemblerBasePath}/test/testingUtils.js`);
const { runStandardTests, runAdvancedTests, dumpPreprocessedTemplateStructures, printTestSummaryTable } = testingUtilsModule;

const performanceUtilsModule = await import(`${assemblerBasePath}/performance/performanceUtils.js`);
const { PerformanceUtils } = performanceUtilsModule;

const commonUtilModule = await import(`${assemblerBasePath}/common/commonUtil.js`);
const { CommonUtil } = commonUtilModule;

/**
 * GET /test/standard - Run standard tests
 */
export async function testStandardEndpoint(req, res, projectDirectory, ConfigUtil) {
    const start = Date.now()
    const assemblerWebDirPath = path.join(projectDirectory, 'wwwroot')

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

    Logger.configure(0, null, false) // DEBUG level
    Logger.configureContextLogFiles(contextLogFiles)

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
export async function testAdvancedEndpoint(req, res, projectDirectory, ConfigUtil) {
    const start = Date.now()
    const assemblerWebDirPath = path.join(projectDirectory, 'wwwroot')

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

    Logger.configure(0, null, false) // DEBUG level
    Logger.configureContextLogFiles(contextLogFiles)

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
export async function testPerformanceEndpoint(req, res, projectDirectory, ConfigUtil) {
    const start = Date.now()
    const assemblerWebDirPath = path.join(projectDirectory, 'wwwroot')

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
export async function testConsolidatePerformanceEndpoint(req, res, projectDirectory) {
    const start = Date.now()
    const assemblerWebDirPath = path.join(projectDirectory, 'wwwroot')

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
        html.push('    <title>All Performance Tests</title>')
        html.push('    <style>')
        html.push('        body { font-family: Arial, sans-serif; margin: 20px; }')
        html.push('        h1, h2 { color: #333; }')
        html.push('        .table-container { overflow-x: auto; }')
        html.push('        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }')
        html.push('        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }')
        html.push('        th { background-color: #4CAF50; color: white; }')
        html.push('        tr:nth-child(even) { background-color: #f2f2f2; }')
        html.push('        .best-perf { background-color: #90EE90; font-weight: bold; }')
        html.push('        @media (max-width: 768px) {')
        html.push('            body { margin: 10px; }')
        html.push('            th, td { padding: 8px; font-size: 14px; }')
        html.push('            h1, h2 { font-size: 24px; }')
        html.push('        }')
        html.push('    </style>')
        html.push('</head>')
        html.push('<body>')
        html.push('<h1>Consolidated Performance Tests</h1>')
        html.push(`<div class="meta" style="color:#666;font-style:italic;margin-bottom:10px;">Generated: ${new Date().toISOString().replace('T', ' ').substring(0, 19)} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>`)

        // Sort appSites
        const sortedAppSites = Array.from(performanceData.keys()).sort()

        // Get list of languages dynamically from configuration
        const languages = Array.from(serversByLang.keys()).sort()

        // Normal Engine Table
        html.push('<h2>Normal Engine Performance (ms)</h2>')
        html.push('<div class="table-container">')
        html.push('<table><tr><th>AppSite</th><th>AppView</th>')
        for (const lang of languages) {
            html.push(`<th>${lang}</th>`)
        }
        html.push('<th>Output Size</th></tr>')

        for (const compositeKey of sortedAppSites) {
            const langData = performanceData.get(compositeKey)
            const firstData = langData.values().next().value
            const appSite = firstData?.appSite || ''
            const appView = firstData?.appView || ''

            // Find minimum time for highlighting (excluding zero values)
            const validTimes = []
            for (const lang of languages) {
                const data = langData.get(lang)
                if (data?.normalTimeMs !== null && data?.normalTimeMs !== undefined && data.normalTimeMs > 0) {
                    validTimes.push(data.normalTimeMs)
                }
            }
            const minTime = validTimes.length > 0 ? Math.min(...validTimes) : null

            html.push(`<tr><td>${appSite}</td><td>${appView}</td>`)

            for (const lang of languages) {
                const data = langData.get(lang)
                const timeValue = formatFloat(data?.normalTimeMs)
                const isBest = minTime !== null && data?.normalTimeMs !== null && data?.normalTimeMs !== undefined && data.normalTimeMs > 0 && Math.abs(data.normalTimeMs - minTime) < 0.001
                const cssClass = isBest ? ' class="best-perf"' : ''
                html.push(`<td${cssClass}>${timeValue}</td>`)
            }

            // Output size from first available language
            const firstOutputSize = getFirstOutputSize(langData)
            html.push(`<td>${formatInt(firstOutputSize)}</td>`)
            html.push('</tr>')
        }
        html.push('</table>')
        html.push('</div>')

        // PreProcess Engine Table
        html.push('<h2>PreProcess Engine Performance (ms)</h2>')
        html.push('<div class="table-container">')
        html.push('<table><tr><th>AppSite</th><th>AppView</th>')
        for (const lang of languages) {
            html.push(`<th>${lang}</th>`)
        }
        html.push('<th>Output Size</th></tr>')

        for (const compositeKey of sortedAppSites) {
            const langData = performanceData.get(compositeKey)
            const firstData = langData.values().next().value
            const appSite = firstData?.appSite || ''
            const appView = firstData?.appView || ''

            // Find minimum time for highlighting (excluding zero values)
            const validTimes = []
            for (const lang of languages) {
                const data = langData.get(lang)
                if (data?.preProcessTimeMs !== null && data?.preProcessTimeMs !== undefined && data.preProcessTimeMs > 0) {
                    validTimes.push(data.preProcessTimeMs)
                }
            }
            const minTime = validTimes.length > 0 ? Math.min(...validTimes) : null

            html.push(`<tr><td>${appSite}</td><td>${appView}</td>`)

            for (const lang of languages) {
                const data = langData.get(lang)
                const timeValue = formatFloat(data?.preProcessTimeMs)
                const isBest = minTime !== null && data?.preProcessTimeMs !== null && data?.preProcessTimeMs !== undefined && data.preProcessTimeMs > 0 && Math.abs(data.preProcessTimeMs - minTime) < 0.001
                const cssClass = isBest ? ' class="best-perf"' : ''
                html.push(`<td${cssClass}>${timeValue}</td>`)
            }

            const firstOutputSize = getFirstOutputSize(langData)
            html.push(`<td>${formatInt(firstOutputSize)}</td>`)
            html.push('</tr>')
        }
        html.push('</table>')
        html.push('</div>')
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
export async function getReportEndpoint(req, res, projectDirectory) {
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
export async function saveLogEndpoint(req, res, projectDirectory) {
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
export async function saveOutputEndpoint(req, res, projectDirectory) {
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
export async function saveTestResultsEndpoint(req, res, projectDirectory) {
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
export async function savePerformanceResultsEndpoint(req, res, projectDirectory) {
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
